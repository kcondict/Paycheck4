using System;
using System.Threading.Tasks;

namespace Paycheck4.Core.Usb
{
	/// <summary>
	/// Interface for USB gadget mode management
	/// Assumes USB gadget mode is already configured on the system
	/// </summary>
	public interface IUsbGadgetManager
	{
		/// <summary>
		/// Event raised when data is received from the host
		/// </summary>
		event EventHandler<DataReceivedEventArgs>? DataReceived;

		/// <summary>
		/// Initializes the USB gadget interface
		/// </summary>
		Task InitializeAsync();

		/// <summary>
		/// Sends data to the USB host
		/// </summary>
		Task SendAsync(byte[] data, int offset, int count);

		/// <summary>
		/// Closes the USB gadget interface
		/// </summary>
		Task CloseAsync();
	}
}