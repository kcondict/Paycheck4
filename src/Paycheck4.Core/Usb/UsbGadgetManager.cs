using System;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Paycheck4.Core.Usb
{
	/// <summary>
	/// Manages USB gadget communication using USB serial (ACM)
	/// Assumes USB gadget mode is already configured on the system
	/// </summary>
	public class UsbGadgetManager : IUsbGadgetManager, IDisposable
	{
		#region Constants
		private const string SerialDevicePath = "/dev/ttyGS0";
		private const int BufferSize = 8192;
		#endregion

		#region Fields
		private readonly ILogger<UsbGadgetManager> _logger;
		private readonly CancellationTokenSource _cancellationSource;
		private readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1);
		private SerialPort? _serialPort;
		private Task? _readTask;
		private bool _isDisposed;
		#endregion

		#region Events
		/// <summary>
		/// Event raised when data is received from the host
		/// </summary>
		public event EventHandler<DataReceivedEventArgs>? DataReceived;
		#endregion

		#region Constructor
		public UsbGadgetManager(ILogger<UsbGadgetManager> logger)
		{
			_logger = logger;
			_cancellationSource = new CancellationTokenSource();
		}
		#endregion

		#region Public Methods
		/// <summary>
		/// Initializes the USB gadget interface by opening the serial device
		/// </summary>
		public async Task InitializeAsync()
		{
			try
			{
				_logger.LogInformation("Initializing USB gadget interface");

				// Check if device exists
				if (!File.Exists(SerialDevicePath))
				{
					throw new InvalidOperationException(
						$"USB serial device not found at {SerialDevicePath}. " +
						"Ensure the USB serial gadget is configured.");
				}

				// Open the device using SerialPort
				_serialPort = new SerialPort(SerialDevicePath)
				{
					BaudRate = 9600,
					Parity = Parity.None,
					DataBits = 8,
					StopBits = StopBits.One,
					Handshake = Handshake.None,
					ReadTimeout = 100,
					WriteTimeout = 1000
				};

				_serialPort.Open();

				_logger.LogInformation("USB gadget interface initialized successfully");

				// Start read loop
				_readTask = Task.Run(() => ReadLoopAsync(_cancellationSource.Token));

				await Task.CompletedTask;
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Failed to initialize USB gadget interface");
				throw;
			}
		}

		/// <summary>
		/// Sends data to the USB host
		/// </summary>
		public async Task SendAsync(byte[] data, int offset, int count)
		{
			if (_serialPort == null || !_serialPort.IsOpen)
			{
				throw new InvalidOperationException("USB gadget interface not initialized");
			}

			// Only allow one write at a time
			if (!await _writeLock.WaitAsync(0))
			{
				_logger.LogWarning("Previous write still in progress, skipping this send");
				return;
			}

			try
			{
				_logger.LogInformation("Starting write of {Count} bytes to USB host", count);
				// SerialPort.Write is synchronous, wrap in Task.Run
				await Task.Run(() => _serialPort.Write(data, offset, count), _cancellationSource.Token);
				_logger.LogInformation("Sent {Count} bytes to USB host successfully", count);
			}
			catch (OperationCanceledException)
			{
				_logger.LogWarning("Send operation canceled");
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Failed to send data to USB host");
				throw;
			}
			finally
			{
				_writeLock.Release();
			}
		}

		/// <summary>
		/// Closes the USB gadget interface
		/// </summary>
		public async Task CloseAsync()
		{
			_logger.LogInformation("Closing USB gadget interface");
			
			// Cancel the token first
			_cancellationSource.Cancel();
			
			// Close the stream to unblock any pending reads
			try
			{
				if (_serialPort != null && _serialPort.IsOpen)
				{
					_serialPort.Close();
				}
			}
			catch (Exception ex)
			{
				_logger.LogWarning(ex, "Exception while closing serial port");
			}
			
			// Wait for read task to complete (with timeout)
			if (_readTask != null)
			{
				try
				{
					await Task.WhenAny(_readTask, Task.Delay(2000));
					if (!_readTask.IsCompleted)
					{
						_logger.LogWarning("Read task did not complete within timeout");
					}
				}
				catch (Exception ex)
				{
					_logger.LogWarning(ex, "Exception while waiting for read task");
				}
			}

			// Dispose the stream
			try
			{
				_serialPort?.Dispose();
			}
			catch (Exception ex)
			{
				_logger.LogWarning(ex, "Exception while disposing serial port");
			}
			
			_serialPort = null;

			_logger.LogInformation("USB gadget interface closed");
		}
		#endregion

		#region Private Methods
		private async Task ReadLoopAsync(CancellationToken cancellationToken)
		{
			var buffer = new byte[BufferSize];

			_logger.LogInformation("USB read loop started");

			try
			{
				while (!cancellationToken.IsCancellationRequested && _serialPort != null && _serialPort.IsOpen)
				{
					try
					{
						// Check if data is available before trying to read
						if (_serialPort.BytesToRead > 0)
						{
							var bytesRead = await Task.Run(() => _serialPort.Read(buffer, 0, buffer.Length), cancellationToken);
							
							if (bytesRead > 0)
							{
								var hexString = BitConverter.ToString(buffer, 0, bytesRead).Replace("-", " ");
								_logger.LogInformation("Received {BytesRead} bytes from USB host: {HexData}", bytesRead, hexString);
								OnDataReceived(buffer, 0, bytesRead);
							}
						}
						else
						{
							// Small delay to avoid busy waiting
							await Task.Delay(10, cancellationToken);
						}
					}
					catch (OperationCanceledException)
					{
						break;
					}
					catch (Exception ex)
					{
						_logger.LogError(ex, "Error reading from USB device");
						await Task.Delay(1000, cancellationToken);
					}
				}
			}
			finally
			{
				_logger.LogInformation("USB read loop stopped");
			}
		}

		private void OnDataReceived(byte[] data, int offset, int count)
		{
			DataReceived?.Invoke(this, new DataReceivedEventArgs(data, offset, count));
		}
		#endregion

		#region IDisposable
		public void Dispose()
		{
			if (_isDisposed) return;

			CloseAsync().GetAwaiter().GetResult();
			_cancellationSource.Dispose();
			_isDisposed = true;
		}
		#endregion
	}

	/// <summary>
	/// Event arguments for received USB data
	/// </summary>
	public class DataReceivedEventArgs : EventArgs
	{
		public byte[] Data { get; }
		public int Offset { get; }
		public int Count { get; }

		public DataReceivedEventArgs(byte[] data, int offset, int count)
		{
			Data = data;
			Offset = offset;
			Count = count;
		}
	}
}