using System.IO.Ports;
using System.Text;

namespace PiSerialTest;

class Program
{
	static async Task Main(string[] args)
	{
		const string portName = "/dev/ttyGS0";
		
		Console.WriteLine("=== Pi USB Serial Test ===");
		Console.WriteLine($"Opening {portName}...");

		try
		{
			using var port = new SerialPort(portName)
			{
				BaudRate = 9600,
				Parity = Parity.None,
				DataBits = 8,
				StopBits = StopBits.One,
				Handshake = Handshake.None,
				ReadTimeout = 100,
				WriteTimeout = 1000
			};

			port.Open();
			Console.WriteLine($"✓ Connected to {portName}");
			Console.WriteLine("Sending messages every second...");
			Console.WriteLine("Press Ctrl+C to quit");
			Console.WriteLine();

			var cts = new CancellationTokenSource();
			Console.CancelKeyPress += (s, e) => 
			{
				e.Cancel = true;
				cts.Cancel();
			};

			var messageCount = 0;

			// Read task - listens for echoed messages
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
							var text = Encoding.ASCII.GetString(buffer, 0, count).TrimEnd();
							Console.WriteLine($"[ECHO RECEIVED] {text}");
						}
						await Task.Delay(10, cts.Token);
					}
					catch (OperationCanceledException)
					{
						break;
					}
					catch (Exception ex)
					{
						Console.WriteLine($"[ERROR] Reading: {ex.Message}");
					}
				}
			}, cts.Token);

			// Send task - sends messages every second
			while (!cts.Token.IsCancellationRequested)
			{
				try
				{
					messageCount++;
					var message = $"Message #{messageCount} from Pi @ {DateTime.Now:HH:mm:ss}\r\n";
					port.Write(message);
					Console.WriteLine($"[SENT] {message.TrimEnd()}");
					
					await Task.Delay(1000, cts.Token);
				}
				catch (OperationCanceledException)
				{
					break;
				}
				catch (Exception ex)
				{
					Console.WriteLine($"[ERROR] Sending: {ex.Message}");
					await Task.Delay(1000, cts.Token);
				}
			}

			await readTask;
			port.Close();
			Console.WriteLine("\nDisconnected.");
		}
		catch (Exception ex)
		{
			Console.WriteLine($"[ERROR] {ex.Message}");
			Console.WriteLine("\nMake sure:");
			Console.WriteLine("1. USB gadget is configured (run setup_usb_serial_device.sh)");
			Console.WriteLine("2. Device /dev/ttyGS0 exists");
			Console.WriteLine("3. You have permission to access the device");
		}
	}
}
