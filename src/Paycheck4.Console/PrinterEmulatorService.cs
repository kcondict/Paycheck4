using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Paycheck4.Core;

namespace Paycheck4.Console
{
    public class PrinterEmulatorService : IHostedService
    {
        private readonly IPrinterEmulator _emulator;
        private readonly ILogger<PrinterEmulatorService> _logger;

        public PrinterEmulatorService(
            IPrinterEmulator emulator,
            ILogger<PrinterEmulatorService> logger)
        {
            _emulator = emulator;
            _logger = logger;

            _emulator.StatusChanged += OnEmulatorStatusChanged;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("Initializing printer emulator...");
                _emulator.Initialize();

                _logger.LogInformation("Starting printer emulator...");
                _emulator.Start();

                _logger.LogInformation("Printer emulator is running");
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start printer emulator");
                throw;
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("Stopping printer emulator...");
                _emulator.Stop();
                _emulator.Dispose();
                _logger.LogInformation("Printer emulator stopped");
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping printer emulator");
                throw;
            }
        }

        private void OnEmulatorStatusChanged(object? sender, PrinterStatusEventArgs e)
        {
            _logger.LogInformation("Printer emulator status changed to: {Status}", e.NewStatus);
        }
    }
}