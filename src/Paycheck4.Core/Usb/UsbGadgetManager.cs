using System;
using System.Threading;
using System.Threading.Tasks;

namespace Paycheck4.Core.Usb
{
    /// <summary>
    /// Manages USB gadget mode configuration and communication
    /// </summary>
    public class UsbGadgetManager : IDisposable
    {
        #region Constants
        private const int VendorId = 0xf0f;
        private const int ProductId = 0x1001;
        private const string ManufacturerString = "Nanoptix";
        private const string ProductString = "PayCheck 4";
        private const int EndpointSize = 64;
        #endregion

        #region Fields
        private readonly CancellationTokenSource _cancellationSource = new CancellationTokenSource();
        private Task? _readTask;
        private bool _isDisposed;
        #endregion

        #region Events
        /// <summary>
        /// Event raised when data is received from the host
        /// </summary>
        public event EventHandler<DataReceivedEventArgs>? DataReceived;

        /// <summary>
        /// Event raised when device connection status changes
        /// </summary>
        public event EventHandler<DeviceStatusEventArgs>? DeviceStatusChanged;
        #endregion

        #region Public Methods
        /// <summary>
        /// Initializes the USB gadget mode
        /// </summary>
        public async Task InitializeAsync()
        {
            // Configure gadget mode:
            // 1. Write device descriptors
            // 2. Configure endpoints
            // 3. Enable gadget
            await Task.CompletedTask; // TODO: Implement
        }

        /// <summary>
        /// Starts USB communication
        /// </summary>
        public void Start()
        {
            _readTask = Task.Run(ReadLoop, _cancellationSource.Token);
        }

        /// <summary>
        /// Stops USB communication
        /// </summary>
        public void Stop()
        {
            _cancellationSource.Cancel();
            _readTask?.Wait();
            _readTask = null;
        }

        /// <summary>
        /// Sends data to the host
        /// </summary>
        public async Task SendDataAsync(byte[] data, int offset, int count)
        {
            // Write data to USB endpoint
            await Task.CompletedTask; // TODO: Implement
        }
        #endregion

        #region Private Methods
        private async Task ReadLoop()
        {
            var buffer = new byte[EndpointSize];

            while (!_cancellationSource.Token.IsCancellationRequested)
            {
                try
                {
                    // Read from USB endpoint
                    // Raise DataReceived event
                    await Task.Delay(1); // TODO: Replace with actual read
                }
                catch (Exception ex)
                {
                    // Log error and notify status change
                    OnDeviceStatusChanged(false, ex.Message);
                }
            }
        }

        private void OnDataReceived(byte[] data, int offset, int count)
        {
            DataReceived?.Invoke(this, new DataReceivedEventArgs(data, offset, count));
        }

        private void OnDeviceStatusChanged(bool connected, string error = null)
        {
            DeviceStatusChanged?.Invoke(this, new DeviceStatusEventArgs(connected, error));
        }
        #endregion

        #region IDisposable
        public void Dispose()
        {
            if (_isDisposed) return;
            
            Stop();
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

    /// <summary>
    /// Event arguments for device status changes
    /// </summary>
    public class DeviceStatusEventArgs : EventArgs
    {
        public bool IsConnected { get; }
        public string? Error { get; }

        public DeviceStatusEventArgs(bool isConnected, string? error = null)
        {
            IsConnected = isConnected;
            Error = error;
        }
    }
}