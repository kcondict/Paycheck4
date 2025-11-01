using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Paycheck4.Core.Usb
{
    /// <summary>
    /// Manages Linux USB gadget configuration via configfs
    /// </summary>
    public class UsbGadgetConfigurator
    {
        #region Constants
        private const string ConfigfsBasePath = "/sys/kernel/config/usb_gadget";
        private const string GadgetName = "paycheck4";
        #endregion

        #region Fields
        private readonly string _gadgetPath;
        private readonly string _stringsPath;
        private readonly string _configPath;
        #endregion

        #region Constructor
        public UsbGadgetConfigurator()
        {
            _gadgetPath = Path.Combine(ConfigfsBasePath, GadgetName);
            _stringsPath = Path.Combine(_gadgetPath, "strings/0x409");
            _configPath = Path.Combine(_gadgetPath, "configs/c.1");
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Configures USB gadget mode for printer emulation
        /// </summary>
        private async Task CleanupExistingConfigurationAsync()
        {
            if (!Directory.Exists(_gadgetPath)) return;

            try
            {
                // Step 1: Unbind from UDC first
                var udcPath = Path.Combine(_gadgetPath, "UDC");
                if (File.Exists(udcPath))
                {
                    var currentUdc = await File.ReadAllTextAsync(udcPath);
                    if (!string.IsNullOrWhiteSpace(currentUdc))
                    {
                        // Unbind and wait for kernel to process
                        await WriteValueAsync(udcPath, "");
                        await Task.Delay(1000);
                    }
                }

                // Step 2: Remove configuration symlinks
                var configFiles = Directory.GetFiles(_configPath, "*", SearchOption.AllDirectories);
                foreach (var file in configFiles)
                {
                    if (File.GetAttributes(file).HasFlag(FileAttributes.ReparsePoint))
                    {
                        File.Delete(file);
                    }
                }
                await Task.Delay(100); // Wait for symlink deletions

                // Step 3: Remove config structure in correct order
                if (Directory.Exists(_configPath))
                {
                    // First remove the strings directory under each config
                    foreach (var configDir in Directory.GetDirectories(_configPath))
                    {
                        var configStrings = Path.Combine(configDir, "strings");
                        if (Directory.Exists(configStrings))
                        {
                            foreach (var langDir in Directory.GetDirectories(configStrings))
                            {
                                try 
                                {
                                    Directory.Delete(langDir, true);
                                }
                                catch (IOException)
                                {
                                    // If we can't delete it now, try the parent later
                                    continue;
                                }
                            }
                            try
                            {
                                Directory.Delete(configStrings, true);
                            }
                            catch (IOException)
                            {
                                // If strings dir is busy, we'll try again with the parent
                                continue;
                            }
                        }
                    }

                    // Now try to remove the config directories themselves
                    foreach (var dir in Directory.GetDirectories(_configPath))
                    {
                        try
                        {
                            Directory.Delete(dir, true);
                        }
                        catch (IOException)
                        {
                            // If still busy, we'll try one last time during final cleanup
                            continue;
                        }
                    }
                }

                // Step 4: Remove function directories with retry
                var functionsPath = Path.Combine(_gadgetPath, "functions");
                if (Directory.Exists(functionsPath))
                {
                    foreach (var functionDir in Directory.GetDirectories(functionsPath))
                    {
                        for (int attempt = 0; attempt < 5; attempt++)
                        {
                            try
                            {
                                Directory.Delete(functionDir, true);
                                break;
                            }
                            catch (IOException) when (attempt < 4)
                            {
                                // Exponential backoff: 500ms, 1s, 2s, 4s
                                await Task.Delay(500 * (1 << attempt));
                                continue;
                            }
                        }
                    }
                }

                // Step 5: Clean up remaining gadget structure in specific order
                var dirsToRemove = new[]
                {
                    // Order matters - remove deeper structures first
                    Path.Combine(_gadgetPath, "configs/c.1/strings"),
                    Path.Combine(_gadgetPath, "configs/c.1"),
                    Path.Combine(_gadgetPath, "configs"),
                    Path.Combine(_gadgetPath, "functions"),
                    Path.Combine(_gadgetPath, "strings/0x409"),
                    Path.Combine(_gadgetPath, "strings"),
                    _gadgetPath
                };

                // Try multiple passes with delays if needed
                for (int pass = 0; pass < 3; pass++)
                {
                    bool allDeleted = true;
                    foreach (var dir in dirsToRemove)
                    {
                        if (Directory.Exists(dir))
                        {
                            try
                            {
                                Directory.Delete(dir, true);
                            }
                            catch (IOException)
                            {
                                allDeleted = false;
                                continue;
                            }
                        }
                    }
                    
                    if (allDeleted) break;
                    await Task.Delay(500 * (pass + 1));
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "Failed to clean up USB gadget configuration. Detailed error: " + ex.Message +
                    "\nTry these manual cleanup steps:\n" +
                    "1. echo '' > /sys/kernel/config/usb_gadget/paycheck4/UDC\n" +
                    "2. rm -rf /sys/kernel/config/usb_gadget/paycheck4", ex);
            }
        }

        public async Task ConfigureAsync(int vendorId, int productId, string manufacturer, string product)
        {
            // Check if we have access to configfs
            if (!Directory.Exists(ConfigfsBasePath))
            {
                throw new InvalidOperationException(
                    "USB gadget configuration directory not found. Ensure configfs is mounted and USB gadget mode is enabled in the kernel.");
            }

            try
            {
                // Clean up any existing configuration
                await CleanupExistingConfigurationAsync();

                // Create gadget directory
                Directory.CreateDirectory(_gadgetPath);
            }
            catch (UnauthorizedAccessException)
            {
                throw new UnauthorizedAccessException(
                    "Access denied. The application must be run with root privileges (sudo) to configure USB gadget mode.");
            }

            // Set device identifiers
            await WriteValueAsync(Path.Combine(_gadgetPath, "idVendor"), $"0x{vendorId:X4}");
            await WriteValueAsync(Path.Combine(_gadgetPath, "idProduct"), $"0x{productId:X4}");
            await WriteValueAsync(Path.Combine(_gadgetPath, "bcdDevice"), "0x0100");
            await WriteValueAsync(Path.Combine(_gadgetPath, "bcdUSB"), "0x0200");

            // Create strings directory and set values
            Directory.CreateDirectory(_stringsPath);
            await WriteValueAsync(Path.Combine(_stringsPath, "serialnumber"), "000001");
            await WriteValueAsync(Path.Combine(_stringsPath, "manufacturer"), manufacturer);
            await WriteValueAsync(Path.Combine(_stringsPath, "product"), product);

            // Create configuration
            Directory.CreateDirectory(_configPath);
            await WriteValueAsync(Path.Combine(_configPath, "MaxPower"), "250");

            // Create function directory for printer
            string functionPath = Path.Combine(_gadgetPath, "functions/printer.usb0");
            Directory.CreateDirectory(functionPath);

            // Create symbolic link from config to function
            string target = Path.Combine(_configPath, "printer.usb0");
            if (!File.Exists(target))
            {
                File.CreateSymbolicLink(target, functionPath);
            }
        }

        /// <summary>
        /// Enables the USB gadget by binding it to a UDC
        /// </summary>
        public async Task EnableAsync()
        {
            // Find available UDC by listing /sys/class/udc directory
            var udcPath = "/sys/class/udc";
            if (!Directory.Exists(udcPath))
            {
                throw new InvalidOperationException("USB Device Controller directory not found. Ensure the dwc2 module is loaded.");
            }

            var udcs = Directory.GetDirectories(udcPath);
            if (udcs.Length == 0)
            {
                throw new InvalidOperationException("No USB Device Controller found. Try: sudo modprobe dwc2");
            }

            // Use the first UDC found (usually there's only one on RPi)
            var udc = Path.GetFileName(udcs[0]);
            await WriteValueAsync(Path.Combine(_gadgetPath, "UDC"), udc);
        }

        /// <summary>
        /// Disables the USB gadget by unbinding it from the UDC
        /// </summary>
        public async Task DisableAsync()
        {
            await WriteValueAsync(Path.Combine(_gadgetPath, "UDC"), "");
        }
        #endregion

        #region Private Methods
        private async Task WriteValueAsync(string path, string value)
        {
            await File.WriteAllTextAsync(path, value);
        }
        #endregion
    }
}