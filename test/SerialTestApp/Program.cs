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
				DtrEnable = true,  // Enable DTR
				RtsEnable = true,  // Enable RTS
				ReadTimeout = 1000,
				WriteTimeout = 1000
			};

			port.Open();
			
			// Clear any stale data in buffers
			port.DiscardInBuffer();
			port.DiscardOutBuffer();
			
			Console.WriteLine($"✓ Connected to {portName}");
			Console.WriteLine();
			Console.WriteLine("Commands:");
			Console.WriteLine("  p - Send test print command");
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
							Console.WriteLine($"[RX] {count} bytes: {hex}");
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
						var bytes = Encoding.ASCII.GetBytes(printCommand);
						port.Write(bytes, 0, bytes.Length);
						Console.WriteLine($"[TX] Print command (Template {currentTemplateId}): {printCommand}");
						Console.WriteLine($"     {bytes.Length} bytes: {BitConverter.ToString(bytes).Replace("-", " ")}");
						
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
						Console.WriteLine($"[TX] {bytes.Length} bytes: {BitConverter.ToString(bytes).Replace("-", " ")}");
					}
					else
					{
						// Send text
						var data = input + "\r\n";
						port.Write(data);
						Console.WriteLine($"[TX] {data.Length} bytes: {data.TrimEnd()}");
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
