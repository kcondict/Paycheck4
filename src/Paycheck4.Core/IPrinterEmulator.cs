using System;

namespace Paycheck4.Core
{
    /// <summary>
    /// Core interface defining printer emulator capabilities
    /// </summary>
    public interface IPrinterEmulator : IDisposable
    {
        /// <summary>
        /// Current status of the printer emulator
        /// </summary>
        PrinterStatus Status { get; }

        /// <summary>
        /// Event raised when printer status changes
        /// </summary>
        event EventHandler<PrinterStatusEventArgs> StatusChanged;

        /// <summary>
        /// Initializes the printer emulator
        /// </summary>
        void Initialize();

        /// <summary>
        /// Starts the printer emulator services
        /// </summary>
        void Start();

        /// <summary>
        /// Stops the printer emulator services
        /// </summary>
        void Stop();

        /// <summary>
        /// Forces a reset of the printer emulator
        /// </summary>
        void Reset();
    }
}