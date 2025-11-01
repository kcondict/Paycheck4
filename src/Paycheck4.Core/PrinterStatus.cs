namespace Paycheck4.Core
{
    /// <summary>
    /// Represents the current status of the printer emulator
    /// </summary>
    public enum PrinterStatus
    {
        /// <summary>
        /// Initial state, not ready
        /// </summary>
        NotInitialized,

        /// <summary>
        /// Currently initializing
        /// </summary>
        Initializing,

        /// <summary>
        /// Ready to receive commands
        /// </summary>
        Ready,

        /// <summary>
        /// Currently processing a print job
        /// </summary>
        Printing,

        /// <summary>
        /// Printer is running
        /// </summary>
        Running,

        /// <summary>
        /// Printer is stopped
        /// </summary>
        Stopped,

        /// <summary>
        /// Error condition detected
        /// </summary>
        Error,

        /// <summary>
        /// Paper out condition
        /// </summary>
        PaperOut,

        /// <summary>
        /// Paper low warning
        /// </summary>
        PaperLow,

        /// <summary>
        /// Communication error with host
        /// </summary>
        CommunicationError,

        /// <summary>
        /// Communication error with network printer
        /// </summary>
        NetworkPrinterError
    }

    /// <summary>
    /// Event arguments for printer status changes
    /// </summary>
    public class PrinterStatusEventArgs : System.EventArgs
    {
        /// <summary>
        /// Previous printer status
        /// </summary>
        public PrinterStatus OldStatus { get; }

        /// <summary>
        /// New printer status
        /// </summary>
        public PrinterStatus NewStatus { get; }

        /// <summary>
        /// Optional error message if status change was due to an error
        /// </summary>
        public string ErrorMessage { get; }

        public PrinterStatusEventArgs(PrinterStatus oldStatus, PrinterStatus newStatus, string errorMessage = null)
        {
            OldStatus = oldStatus;
            NewStatus = newStatus;
            ErrorMessage = errorMessage;
        }
    }
}