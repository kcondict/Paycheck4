using System.Threading.Tasks;

namespace Paycheck4.Core.Usb
{
    /// <summary>
    /// Interface for USB gadget mode management
    /// </summary>
    public interface IUsbGadgetManager
    {
        /// <summary>
        /// Configures USB gadget mode for printer emulation
        /// </summary>
        Task ConfigureAsync(int vendorId, int productId, string manufacturer, string product);

        /// <summary>
        /// Enables the USB gadget by binding it to a UDC
        /// </summary>
        Task EnableAsync();

        /// <summary>
        /// Disables the USB gadget by unbinding it from the UDC
        /// </summary>
        Task DisableAsync();
    }
}