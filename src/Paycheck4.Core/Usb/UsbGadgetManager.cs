using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Paycheck4.Core.Usb
{
	/// <summary>
	/// Manages USB gadget communication using USB serial (ACM)
	/// Assumes USB gadget mode is already configured on the system
	/// Note: Uses native Linux syscalls instead of FileStream for Mono compatibility
	/// </summary>
	public class UsbGadgetManager : IUsbGadgetManager, IDisposable
	{
	#region P/Invoke for Linux syscalls
	[StructLayout(LayoutKind.Sequential)]
	private struct pollfd
	{
		public int fd;
		public short events;
		public short revents;
	}

	private const short POLLIN = 0x0001;
	private const int O_RDWR = 0x0002;
	private const int O_NONBLOCK = 0x0800;

	[DllImport("libc", SetLastError = true)]
	private static extern int open([MarshalAs(UnmanagedType.LPStr)] string pathname, int flags);

	[DllImport("libc", SetLastError = true)]
	private static extern int close(int fd);

	[DllImport("libc", SetLastError = true)]
	private static extern int read(int fd, byte[] buf, int count);

	[DllImport("libc", SetLastError = true)]
	private static extern int write(int fd, byte[] buf, int count);

	[DllImport("libc", SetLastError = true)]
	private static extern int poll(pollfd[] fds, uint nfds, int timeout);
	#endregion

	#region Constants
	private const string SerialDevicePath = "/dev/ttyGS0";
	private const int BufferSize = 8192;
	#endregion

	#region Fields
	private readonly ILogger<UsbGadgetManager> _logger;
	private readonly CancellationTokenSource _cancellationSource;
	private readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1);
	private int _readFd = -1;   // Separate file descriptor for reading
	private int _writeFd = -1;  // Separate file descriptor for writing
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

			// Open the device using native open() syscall
			// Use separate file descriptors for read and write to prevent loopback
			_readFd = open(SerialDevicePath, O_RDWR | O_NONBLOCK);
			
			if (_readFd < 0)
			{
				var errno = Marshal.GetLastWin32Error();
				throw new InvalidOperationException($"Failed to open {SerialDevicePath} for reading, errno: {errno}");
			}

			_writeFd = open(SerialDevicePath, O_RDWR | O_NONBLOCK);
			
			if (_writeFd < 0)
			{
				var errno = Marshal.GetLastWin32Error();
				close(_readFd);
				_readFd = -1;
				throw new InvalidOperationException($"Failed to open {SerialDevicePath} for writing, errno: {errno}");
			}

			_logger.LogInformation("Opened {Device} with read_fd={ReadFd}, write_fd={WriteFd}", 
				SerialDevicePath, _readFd, _writeFd);
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
			if (_writeFd < 0)
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
				
				// Use native write() syscall on the write file descriptor
				var bytesWritten = write(_writeFd, data.Skip(offset).Take(count).ToArray(), count);
				
				if (bytesWritten < 0)
				{
					var errno = Marshal.GetLastWin32Error();
					throw new IOException($"Write failed, errno: {errno}");
				}
				
				_logger.LogInformation("Sent {Count} bytes to USB host successfully", bytesWritten);
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
			// Prevent multiple calls
			if (_readFd < 0 && _writeFd < 0)
			{
				return;
			}

			_logger.LogInformation("Closing USB gadget interface");
			
			// Cancel the token first
			_cancellationSource.Cancel();
			
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

			// Close both file descriptors
			try
			{
				if (_readFd >= 0)
				{
					close(_readFd);
					_readFd = -1;
				}
				if (_writeFd >= 0)
				{
					close(_writeFd);
					_writeFd = -1;
				}
			}
			catch (Exception ex)
			{
				_logger.LogWarning(ex, "Exception while closing device");
			}

			_logger.LogInformation("USB gadget interface closed");
		}
		#endregion

	#region Private Methods
	private async Task ReadLoopAsync(CancellationToken cancellationToken)
	{
		var buffer = new byte[BufferSize];

		_logger.LogInformation("USB read loop started");
		_logger.LogInformation("File descriptor for /dev/ttyGS0 - read: {ReadFd}, write: {WriteFd}", _readFd, _writeFd);

		try
		{
			while (!cancellationToken.IsCancellationRequested && _readFd >= 0)
			{
				try
				{
					// Use poll() to check if data is available on the READ descriptor only (100ms timeout)
					var fds = new[] { new pollfd { fd = _readFd, events = POLLIN, revents = 0 } };
					var pollResult = poll(fds, 1, 100);

					if (pollResult < 0)
					{
						var errno = Marshal.GetLastWin32Error();
						_logger.LogError("poll() failed, errno: {Errno}", errno);
						await Task.Delay(1000, cancellationToken);
						continue;
					}

					if (pollResult == 0)
					{
						// Timeout - no data available
						continue;
					}

					// Data is available - read it from the READ descriptor
					var bytesRead = read(_readFd, buffer, buffer.Length);
					
					if (bytesRead > 0)
					{
						var hexString = BitConverter.ToString(buffer, 0, bytesRead).Replace("-", " ");
						_logger.LogInformation("Received {BytesRead} bytes from USB host: {HexData}", bytesRead, hexString);
						OnDataReceived(buffer, 0, bytesRead);
					}
					else if (bytesRead == 0)
					{
						_logger.LogWarning("Read returned 0 bytes - device may be disconnected");
						await Task.Delay(1000, cancellationToken);
					}
					else
					{
						// Error
						var errno = Marshal.GetLastWin32Error();
						_logger.LogError("read() failed, errno: {Errno}", errno);
						await Task.Delay(1000, cancellationToken);
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
	}		private void OnDataReceived(byte[] data, int offset, int count)
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