using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
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
        private readonly IUsbGadgetManager _usbManager;
        private readonly TclProtocol _protocol;
        private readonly ILogger<PrinterEmulator> _logger;
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
        public PrinterEmulator(
            ILogger<PrinterEmulator> logger,
            ILogger<UsbGadgetManager> usbLogger,
            ILogger<TclProtocol> protocolLogger,
            int statusReportingInterval = 2000,
            int printStartDelayInterval = 3000,
            int validationDelayInterval = 18000,
            int busyStateChangeInterval = 20000,
            int tofStateChangeInterval = 4000)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _usbManager = new UsbGadgetManager(usbLogger ?? throw new ArgumentNullException(nameof(usbLogger)));
            _protocol = new TclProtocol(
                protocolLogger ?? throw new ArgumentNullException(nameof(protocolLogger)), 
                statusReportingInterval,
                printStartDelayInterval,
                validationDelayInterval,
                busyStateChangeInterval,
                tofStateChangeInterval);

            // Wire up event handlers
            _usbManager.DataReceived += OnUsbDataReceived;
            _protocol.StatusChanged += OnProtocolStatusChanged;
            _protocol.ResponseReady += OnProtocolResponseReady;
        }
        #endregion

        #region IPrinterEmulator Implementation
        public void Initialize()
        {
            try
            {
                _logger.LogInformation("Initializing printer emulator");
                Status = PrinterStatus.Initializing;

                // Initialize USB manager
                // Note: USB gadget must already be configured via setup_usb_serial_device.sh
                Task.Run(_usbManager.InitializeAsync).Wait();

                // Initialize protocol handler
                _protocol.Initialize();

                Status = PrinterStatus.Ready;
                _logger.LogInformation("Printer emulator initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize printer emulator");
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

            _logger.LogInformation("Starting printer emulator");
            Status = PrinterStatus.Running;
            
            // Start the protocol handler (begins status broadcasting)
            _protocol.Start();
        }

        public void Stop()
        {
            _logger.LogInformation("Stopping printer emulator");
            
            // Stop the protocol handler first
            _protocol.Stop();
            
            Status = PrinterStatus.Stopped;
        }

        public void Reset()
        {
            _logger.LogInformation("Resetting printer emulator");
            Stop();
            Initialize();
        }
        #endregion

        #region Event Handlers
        private void OnUsbDataReceived(object? sender, DataReceivedEventArgs e)
        {
            _protocol.ProcessData(e.Data, e.Offset, e.Count);
        }

        private void OnProtocolStatusChanged(object? sender, PrinterStatusEventArgs e)
        {
            Status = e.NewStatus;
        }
        
        private async void OnProtocolResponseReady(object? sender, TclResponseEventArgs e)
        {
            try
            {
                _logger.LogInformation("Sending protocol response: {ByteCount} bytes", e.Response.Length);
                var hexString = BitConverter.ToString(e.Response).Replace("-", " ");
                _logger.LogInformation("Response data: {HexData}", hexString);
                await _usbManager.SendAsync(e.Response, 0, e.Response.Length);
                _logger.LogInformation("Protocol response sent successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send protocol response");
            }
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

            _logger.LogInformation("Disposing printer emulator");
            Stop();
            
            // Close USB connection
            Task.Run(_usbManager.CloseAsync).Wait();
            
            // Dispose USB manager if it implements IDisposable
            if (_usbManager is IDisposable disposableManager)
            {
                disposableManager.Dispose();
            }
            
            _isDisposed = true;
        }
        #endregion
    }
}