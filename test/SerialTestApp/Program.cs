using System.IO.Ports;
using System.Text;

namespace SerialTestApp;

class Program
{
	static async Task Main(string[] args)
	{
		const string portName = "COM6";
		char currentTemplateId = '0'; // Start at '0', cycle through '0'-'9'
		
		Console.WriteLine("=== USB Serial Test Application ===");
		Console.WriteLine($"Attempting to connect to {portName}...");
		Console.WriteLine();

		try
		{
		using var port = new SerialPort(portName)
		{
			BaudRate = 9600,  // Ignored for USB serial, but required
			Parity = Parity.None,
			DataBits = 8,
			StopBits = StopBits.One,
			Handshake = Handshake.None,
			DtrEnable = false,  // Disable DTR (prevents echo loopback)
			RtsEnable = false,  // Disable RTS (prevents echo loopback)
			ReadTimeout = 1000,
			WriteTimeout = 1000
		};			port.Open();
			
			// Clear any stale data in buffers
			port.DiscardInBuffer();
			port.DiscardOutBuffer();
			
			Console.WriteLine($"✓ Connected to {portName}");
			Console.WriteLine();
			Console.WriteLine("Commands:");
			Console.WriteLine("  p - Send test print command");
			Console.WriteLine("  L - Send large print command (15 fields x 18 chars)");
			Console.WriteLine("  Type text and press Enter to send");
			Console.WriteLine("  Type 'exit' to quit");
			Console.WriteLine("  Type 'hex:<bytes>' to send hex (e.g., hex:48656C6C6F)");
			Console.WriteLine();

			var cts = new CancellationTokenSource();
			
			// Read loop
			var readTask = Task.Run(async () =>
			{
				var buffer = new byte[1024];
				while (!cts.Token.IsCancellationRequested)
				{
					try
					{
						if (port.BytesToRead > 0)
						{
							var count = port.Read(buffer, 0, buffer.Length);
							var hex = BitConverter.ToString(buffer, 0, count).Replace("-", " ");
							var text = Encoding.ASCII.GetString(buffer, 0, count);
							Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [RX] {count} bytes: {hex}");
							Console.WriteLine($"     Text: {text.TrimEnd()}");
							
							// DISABLED: Echo the message back (causing loop issue)
							// port.Write(buffer, 0, count);
							// Console.WriteLine($"[ECHO] Sent back {count} bytes");
							Console.WriteLine($"[DEBUG] NOT echoing - testing if Pi receives without echo");
							Console.WriteLine();
						}
						await Task.Delay(10, cts.Token);
					}
					catch (OperationCanceledException)
					{
						break;
					}
					catch (Exception ex)
					{
						Console.WriteLine($"[ERROR] {ex.Message}");
					}
				}
			}, cts.Token);

			// Command loop
			while (true)
			{
				Console.Write("> ");
				var input = Console.ReadLine();
				
				if (string.IsNullOrEmpty(input))
					continue;

				if (input.Equals("exit", StringComparison.OrdinalIgnoreCase))
					break;

				try
				{
					if (input.Equals("p", StringComparison.OrdinalIgnoreCase))
					{
						// Send test print command with incrementing template ID: ^P|<template_id>|1|Field1|Field2|Field3|Field4|^
						var printCommand = $"^P|{currentTemplateId}|1|John Doe|$100.00|Check #12345|11/07/2025|^";
						
						// Use WriteLine() - this is required for USB CDC ACM to actually transmit the data
						// The Pi will filter out the CR/LF line terminator
						port.WriteLine(printCommand);
						
						var bytes = Encoding.ASCII.GetBytes(printCommand + port.NewLine);
						Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [TX] Print command (Template {currentTemplateId}): {printCommand}");
						Console.WriteLine($"     {bytes.Length} bytes (with CRLF): {BitConverter.ToString(bytes).Replace("-", " ")}");
						
						// Increment template ID, cycling from '0' to '9'
						currentTemplateId = currentTemplateId == '9' ? '0' : (char)(currentTemplateId + 1);
					}
					else if (input.Equals("L", StringComparison.OrdinalIgnoreCase))
					{
						// Send large print command with 15 fields of 18 characters each
						// Format: ^P|<template_id>|1|Field1|Field2|...|Field15|^
						var field1 = "Field01-1234567890"; // 18 chars
						var field2 = "Field02-ABCDEFGHIJ"; // 18 chars
						var field3 = "Field03-KLMNOPQRST"; // 18 chars
						var field4 = "Field04-UVWXYZ0123"; // 18 chars
						var field5 = "Field05-4567890ABC"; // 18 chars
						var field6 = "Field06-DEFGHIJKLM"; // 18 chars
						var field7 = "Field07-NOPQRSTUVW"; // 18 chars
						var field8 = "Field08-XYZ0123456"; // 18 chars
						var field9 = "Field09-7890ABCDEF"; // 18 chars
						var field10 = "Field10-GHIJKLMNOP"; // 18 chars
						var field11 = "Field11-QRSTUVWXYZ"; // 18 chars
						var field12 = "Field12-0123456789"; // 18 chars
						var field13 = "Field13-ABCDEFGHIJ"; // 18 chars
						var field14 = "Field14-KLMNOPQRST"; // 18 chars
						var field15 = "Field15-UVWXYZ0123"; // 18 chars
						
						var largePrintCommand = $"^P|{currentTemplateId}|1|{field1}|{field2}|{field3}|{field4}|{field5}|{field6}|{field7}|{field8}|{field9}|{field10}|{field11}|{field12}|{field13}|{field14}|{field15}|^";
						
						Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [TX] Large print command (Template {currentTemplateId}):");
						Console.WriteLine($"     Total message: {largePrintCommand.Length} bytes");
						Console.WriteLine($"     Sending in 5 segments with 2ms pauses");
						
						// Split the message into 5 roughly equal segments
						int totalLength = largePrintCommand.Length;
						int segmentSize = (totalLength + 4) / 5; // Round up
						
						for (int i = 0; i < 5; i++)
						{
							int startPos = i * segmentSize;
							int length = Math.Min(segmentSize, totalLength - startPos);
							
							if (length <= 0)
								break;
							
							string segment = largePrintCommand.Substring(startPos, length);
							
							// Use WriteLine() to force USB transmission
							// The Pi will filter out the CR/LF bytes
							port.WriteLine(segment);
							
							var segmentBytes = Encoding.ASCII.GetBytes(segment + port.NewLine);
							Console.WriteLine($"     Segment {i + 1}/5: {segmentBytes.Length} bytes (with CRLF)");
							
							// Pause 2ms between segments (except after the last one)
							if (i < 4)
								System.Threading.Thread.Sleep(2);
						}
						
						Console.WriteLine($"     All segments sent");
						
						// Increment template ID, cycling from '0' to '9'
						currentTemplateId = currentTemplateId == '9' ? '0' : (char)(currentTemplateId + 1);
					}
					else if (input.StartsWith("hex:", StringComparison.OrdinalIgnoreCase))
					{
						// Send hex bytes
						var hexString = input.Substring(4).Replace(" ", "").Replace("-", "");
						var bytes = new byte[hexString.Length / 2];
						for (int i = 0; i < bytes.Length; i++)
						{
							bytes[i] = Convert.ToByte(hexString.Substring(i * 2, 2), 16);
						}
						port.Write(bytes, 0, bytes.Length);
						Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [TX] {bytes.Length} bytes: {BitConverter.ToString(bytes).Replace("-", " ")}");
					}
					else
					{
						// Send text
						var data = input + "\r\n";
						port.Write(data);
						Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [TX] {data.Length} bytes: {data.TrimEnd()}");
					}
				}
				catch (Exception ex)
				{
					Console.WriteLine($"[ERROR] Sending: {ex.Message}");
				}
			}

			cts.Cancel();
			await readTask;
			port.Close();
			Console.WriteLine("\nDisconnected.");
		}
		catch (Exception ex)
		{
			Console.WriteLine($"[ERROR] {ex.Message}");
			Console.WriteLine();
			Console.WriteLine("Available COM ports:");
			foreach (var p in SerialPort.GetPortNames())
			{
				Console.WriteLine($"  - {p}");
			}
		}
	}
}
