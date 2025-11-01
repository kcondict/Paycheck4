using System;
using System.Threading.Tasks;
using Paycheck4.Core.Protocol;
using Paycheck4.Core.Usb;

namespace Paycheck4.Core
{
    /// <summary>
    /// Main implementation of the printer emulator
    /// </summary>
    public class PrinterEmulator : IPrinterEmulator
    {
        #region Fields
        private readonly UsbGadgetManager _usbManager;
        private readonly UsbGadgetConfigurator _usbConfigurator;
        private readonly TclProtocol _protocol;
        private bool _isDisposed;
        private PrinterStatus _status;
        #endregion

        #region Properties
        public PrinterStatus Status
        {
            get => _status;
            private set
            {
                if (_status != value)
                {
                    var oldStatus = _status;
                    _status = value;
                    OnStatusChanged(new PrinterStatusEventArgs(oldStatus, _status));
                }
            }
        }
        #endregion

        #region Events
        public event EventHandler<PrinterStatusEventArgs>? StatusChanged;
        #endregion

        #region Constructor
        public PrinterEmulator()
        {
            _usbManager = new UsbGadgetManager();
            _usbConfigurator = new UsbGadgetConfigurator();
            _protocol = new TclProtocol();

            // Wire up event handlers
            _usbManager.DataReceived += OnUsbDataReceived;
            _usbManager.DeviceStatusChanged += OnUsbDeviceStatusChanged;
            _protocol.StatusChanged += OnProtocolStatusChanged;
        }
        #endregion

        #region IPrinterEmulator Implementation
        public void Initialize()
        {
            try
            {
                Status = PrinterStatus.Initializing;

                // Configure USB gadget mode
                Task.Run(async () =>
                {
                    await _usbConfigurator.ConfigureAsync(0xf0f, 0x1001, "Nanoptix", "PayCheck 4");
                    await _usbConfigurator.EnableAsync();
                }).Wait();

                // Initialize USB manager
                Task.Run(async () =>
                {
                    await _usbManager.InitializeAsync();
                }).Wait();

                // Initialize protocol handler
                _protocol.Initialize();

                Status = PrinterStatus.Ready;
            }
            catch (Exception)
            {
                Status = PrinterStatus.Error;
                throw;
            }
        }

        public void Start()
        {
            if (Status != PrinterStatus.Ready && Status != PrinterStatus.Stopped)
            {
                throw new InvalidOperationException("Printer emulator must be in Ready or Stopped state to start");
            }

            _usbManager.Start();
            Status = PrinterStatus.Running;
        }

        public void Stop()
        {
            _usbManager.Stop();
            Status = PrinterStatus.Stopped;
        }

        public void Reset()
        {
            Stop();
            Task.Run(async () =>
            {
                await _usbConfigurator.DisableAsync();
                Initialize();
            }).Wait();
        }
        #endregion

        #region Event Handlers
        private void OnUsbDataReceived(object? sender, DataReceivedEventArgs e)
        {
            _protocol.ProcessData(e.Data, e.Offset, e.Count);
        }

        private void OnUsbDeviceStatusChanged(object? sender, DeviceStatusEventArgs e)
        {
            Status = e.IsConnected ? PrinterStatus.Running : PrinterStatus.Error;
        }

        private void OnProtocolStatusChanged(object? sender, PrinterStatusEventArgs e)
        {
            Status = e.NewStatus;
        }

        private void OnStatusChanged(PrinterStatusEventArgs e)
        {
            StatusChanged?.Invoke(this, e);
        }
        #endregion

        #region IDisposable Implementation
        public void Dispose()
        {
            if (_isDisposed) return;

            Stop();
            _usbManager.Dispose();
            _isDisposed = true;
        }
        #endregion
    }
}