using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Paycheck4.Core.Usb
{
    /// <summary>
    /// Manages Linux USB gadget configuration via configfs for printer emulation
    /// </summary>
    public class UsbGadgetConfigurator
    {
        #region Constants
        private const string ConfigfsBasePath = "/sys/kernel/config/usb_gadget";
        private const string GadgetName = "paycheck4";
        private const int DelayAfterKernelOperation = 2000; // ms
        private const int DelayAfterFileOperation = 500; // ms
        private const string DefaultLangId = "0x409"; // English (United States)
        #endregion

        #region Fields
        private readonly string _gadgetPath;
        private readonly string _stringsPath;
        private readonly string _configPath;
        private readonly string _functionPath;
        #endregion

        #region Constructor
        public UsbGadgetConfigurator()
        {
            _gadgetPath = Path.Combine(ConfigfsBasePath, GadgetName);
            _stringsPath = Path.Combine(_gadgetPath, $"strings/{DefaultLangId}");
            _configPath = Path.Combine(_gadgetPath, "configs/c.1");
            _functionPath = Path.Combine(_gadgetPath, "functions/printer.usb0");
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Configures the USB gadget for printer emulation
        /// </summary>
        public async Task ConfigureAsync(int vendorId, int productId, string manufacturer, string product)
        {
            await EnsureConfigfsAccessAsync();
            await CleanupExistingConfigurationAsync();
            await CreateGadgetStructureAsync();
            await ConfigureDeviceParametersAsync(vendorId, productId, manufacturer, product);
            await ConfigurePrinterFunctionAsync();
            await CreateSymbolicLinkAsync();
            Console.WriteLine("USB gadget configuration completed successfully");
        }

        /// <summary>
        /// Enables the USB gadget by binding it to the UDC
        /// </summary>
        public async Task EnableAsync()
        {
            Console.WriteLine("Starting USB gadget enable process...");
            
            // Initialize and check system state
            await LogSystemState();
            await LoadDwc2Module();
            await CleanupAllGadgets();
            
            // Find and prepare UDC
            var udc = await FindAndVerifyUdc();
            await BindToUdc(udc);
        }

        /// <summary>
        /// Disables the USB gadget by unbinding it from the UDC
        /// </summary>
        public async Task DisableAsync()
        {
            try
            {
                var udcPath = Path.Combine(_gadgetPath, "UDC");
                Console.WriteLine("Disabling USB gadget...");
                await WriteValueAsync(udcPath, "");
                await Task.Delay(DelayAfterKernelOperation);
                Console.WriteLine("USB gadget disabled successfully");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to disable USB gadget: {ex.Message}", ex);
            }
        }
        #endregion

        #region Private Helper Methods - Core Operations
        private async Task EnsureConfigfsAccessAsync()
        {
            var configfsCheck = await ExecuteCommandAsync($"ls -la {ConfigfsBasePath}");
            if (configfsCheck.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    "USB gadget configuration directory not found. Ensure configfs is mounted and USB gadget mode is enabled in the kernel.");
            }
        }

        private async Task CleanupExistingConfigurationAsync()
        {
            try
            {
                // Check if gadget exists
                var checkGadget = await ExecuteCommandAsync($"ls -la {_gadgetPath}");
                if (checkGadget.ExitCode != 0) return;

                Console.WriteLine("Starting USB gadget cleanup...");
                await UnbindFromUdc();
                await RemoveSymlinks();
                await RemoveGadgetStructure();
                await VerifyCleanup();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to clean up USB gadget configuration. Detailed error: {ex.Message}\n" +
                    "Try these manual cleanup steps:\n" +
                    "1. echo '' > /sys/kernel/config/usb_gadget/paycheck4/UDC\n" +
                    "2. rm -rf /sys/kernel/config/usb_gadget/paycheck4", ex);
            }
        }

        private async Task CreateGadgetStructureAsync()
        {
            Console.WriteLine("Creating USB gadget structure...");

            // Step 1: Create base gadget directory
            await ExecuteCommandAsync($"sudo mkdir -p {_gadgetPath}");
            await Task.Delay(100);

            // Step 2: Create initial configuration files with null values
            var initialFiles = new[]
            {
                "bDeviceClass",
                "bDeviceSubClass",
                "bDeviceProtocol",
                "bMaxPacketSize0",
                "bcdDevice",
                "bcdUSB",
                "idVendor",
                "idProduct"
            };

            foreach (var file in initialFiles)
            {
                await ExecuteCommandAsync($"sudo sh -c 'echo 0 > {_gadgetPath}/{file}'");
                await Task.Delay(100);
            }

            // Step 3: Create strings directory and files
            await ExecuteCommandAsync($"sudo mkdir -p {_stringsPath}");
            await Task.Delay(100);

            var stringFiles = new[] { "serialnumber", "manufacturer", "product" };
            foreach (var file in stringFiles)
            {
                await ExecuteCommandAsync($"sudo sh -c 'echo \"\" > {_stringsPath}/{file}'");
                await Task.Delay(100);
            }

            // Step 4: Create config directory and its files
            await ExecuteCommandAsync($"sudo mkdir -p {_configPath}/strings/{DefaultLangId}");
            await Task.Delay(100);

            // Create config files with initial values
            await ExecuteCommandAsync($"sudo sh -c 'echo 250 > {_configPath}/MaxPower'");
            await ExecuteCommandAsync($"sudo sh -c 'echo 0x80 > {_configPath}/bmAttributes'");
            await ExecuteCommandAsync($"sudo sh -c 'echo \"Config 1\" > {_configPath}/strings/{DefaultLangId}/configuration'");
            await Task.Delay(100);

            // Step 5: Create function directory
            await ExecuteCommandAsync($"sudo mkdir -p {_functionPath}");
            await Task.Delay(100);

            // Create function files
            await ExecuteCommandAsync($"sudo sh -c 'echo 8192 > {_functionPath}/q_len'");
            await ExecuteCommandAsync($"sudo sh -c 'echo \"PayCheck4 USB Printer\" > {_functionPath}/pnp_string'");
            await Task.Delay(100);

            // Step 6: Create symbolic link
            await ExecuteCommandAsync($"sudo ln -s {_functionPath} {_configPath}/");
            await Task.Delay(100);

            Console.WriteLine("USB gadget structure created successfully");
        }
        #endregion

        #region Private Helper Methods - Configuration
        private async Task ConfigureDeviceParametersAsync(int vendorId, int productId, string manufacturer, string product)
        {
            // Configure each file using direct shell commands
            var commands = new Dictionary<string, string>
            {
                // Device class configuration
                { $"{_gadgetPath}/bDeviceClass", "0x07" },      // Printer class
                { $"{_gadgetPath}/bDeviceSubClass", "0x01" },   // Printer subclass
                { $"{_gadgetPath}/bDeviceProtocol", "0x01" },   // Unidirectional
                { $"{_gadgetPath}/bMaxPacketSize0", "0x40" },
                
                // USB version and device info
                { $"{_gadgetPath}/bcdDevice", "0x0100" },
                { $"{_gadgetPath}/bcdUSB", "0x0200" },
                
                // Vendor and product IDs
                { $"{_gadgetPath}/idVendor", $"0x{vendorId:X4}" },
                { $"{_gadgetPath}/idProduct", $"0x{productId:X4}" },
                
                // Device strings
                { $"{_stringsPath}/manufacturer", manufacturer },
                { $"{_stringsPath}/product", product },
                { $"{_stringsPath}/serialnumber", "000001" },
                
                // USB speed
                { $"{_gadgetPath}/max_speed", "high-speed" },
                
                // Configuration
                { $"{_configPath}/bmAttributes", "0x80" },  // Bus powered
                { $"{_configPath}/MaxPower", "250" },
                { $"{_configPath}/strings/{DefaultLangId}/configuration", "Printer Config" }
            };

            foreach (var (path, value) in commands)
            {
                var escapedValue = value.Replace("\"", "\\\"");
                var result = await ExecuteCommandAsync($"sudo sh -c 'echo \"{escapedValue}\" > {path}'");
                if (result.ExitCode != 0)
                {
                    throw new InvalidOperationException($"Failed to write {value} to {path}: {result.Error}");
                }
                await Task.Delay(100); // Small delay between writes
            }

            // Configure OS descriptors
            await ExecuteCommandAsync($"sudo mkdir -p {_gadgetPath}/os_desc");
            await Task.Delay(100);

            var osCommands = new Dictionary<string, string>
            {
                { $"{_gadgetPath}/os_desc/use", "1" },
                { $"{_gadgetPath}/os_desc/qw_sign", "MSFT100" },
                { $"{_gadgetPath}/os_desc/b_vendor_code", "0xCD" }
            };

            foreach (var (path, value) in osCommands)
            {
                var result = await ExecuteCommandAsync($"sudo sh -c 'echo \"{value}\" > {path}'");
                if (result.ExitCode != 0)
                {
                    Console.WriteLine($"Warning: Failed to set OS descriptor {path}: {result.Error}");
                }
                await Task.Delay(100);
            }
        }

        private async Task ConfigurePowerManagementAsync()
        {
            var config = new Dictionary<string, string>
            {
                { $"{_configPath}/MaxPower", "250" },
                { $"{_configPath}/bmAttributes", "0x07" }, // Self-powered
                { $"{_configPath}/strings/{DefaultLangId}/configuration", "Printer Config" }
            };

            foreach (var (path, value) in config)
            {
                await WriteValueAsync(path, value);
            }
        }

        private async Task ConfigureOsDescriptorsAsync()
        {
            var config = new Dictionary<string, string>
            {
                { $"{_gadgetPath}/os_desc/use", "1" },
                { $"{_gadgetPath}/os_desc/qw_sign", "MSFT100" },
                { $"{_gadgetPath}/os_desc/b_vendor_code", "0xCD" }
            };

            foreach (var (path, value) in config)
            {
                await WriteValueAsync(path, value);
            }
        }

        private async Task ConfigurePrinterFunctionAsync()
        {
            try
            {
                // First ensure function directory exists with correct permissions
                await ExecuteCommandAsync($"sudo mkdir -p {_functionPath}");
                await ExecuteCommandAsync($"sudo chown root:root {_functionPath}");
                await ExecuteCommandAsync($"sudo chmod 755 {_functionPath}");
                await Task.Delay(DelayAfterFileOperation);

                // Configure each parameter separately due to different file handling requirements
                // First configure pnp_string as it's a regular file
                var pnpStringPath = Path.Combine(_functionPath, "pnp_string");
                var pnpResult = await ExecuteCommandAsync($"sudo sh -c 'echo -n \"PayCheck4 USB Printer\" > {pnpStringPath}'");
                if (pnpResult.ExitCode != 0)
                {
                    throw new InvalidOperationException($"Failed to set pnp_string: {pnpResult.Error}");
                }

                // Allow some time for the kernel to process
                await Task.Delay(DelayAfterFileOperation);

                // Now handle q_len which is a special kernel-managed file
                var qLenPath = Path.Combine(_functionPath, "q_len");
                
                // First check if the file exists and is the right type
                var fileCheck = await ExecuteCommandAsync($"ls -l {qLenPath}");
                if (fileCheck.ExitCode != 0)
                {
                    throw new InvalidOperationException($"q_len file not found: {fileCheck.Error}");
                }

                // Try to read current value first
                var currentValue = await ExecuteCommandAsync($"sudo cat {qLenPath}");
                Console.WriteLine($"Current q_len value: {currentValue.Output.Trim()}");

                // Only try to write if current value is different
                if (currentValue.Output.Trim() != "8192")
                {
                    // Try multiple write methods in case one fails
                    var methods = new[]
                    {
                        $"sudo sh -c 'echo 8192 > {qLenPath}'",
                        $"printf '8192' | sudo tee {qLenPath}",
                        $"echo 8192 | sudo dd of={qLenPath} bs=1"
                    };

                    bool success = false;
                    Exception? lastError = null;
                    foreach (var method in methods)
                    {
                        try
                        {
                            var result = await ExecuteCommandAsync(method);
                            if (result.ExitCode == 0)
                            {
                                // Verify the write
                                await Task.Delay(DelayAfterFileOperation);
                                var verify = await ExecuteCommandAsync($"sudo cat {qLenPath}");
                                if (verify.ExitCode == 0 && verify.Output.Trim() == "8192")
                                {
                                    Console.WriteLine($"Successfully set q_len using: {method}");
                                    success = true;
                                    break;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            lastError = ex;
                            // Continue to next method
                        }
                        await Task.Delay(DelayAfterFileOperation);
                    }

                    if (!success)
                    {
                        throw new InvalidOperationException(
                            "Failed to set q_len after trying multiple methods. " +
                            "This might indicate a kernel module issue.", 
                            lastError ?? new InvalidOperationException("All write methods failed"));
                    }
                }

                // Final verification of function directory
                var dirCheck = await ExecuteCommandAsync($"ls -la {_functionPath}");
                if (dirCheck.ExitCode == 0)
                {
                    Console.WriteLine($"Printer function configuration complete. Directory listing:\n{dirCheck.Output}");
                }
            }
            catch (Exception ex)
            {
                // Get kernel logs for debugging
                var dmesg = await ExecuteCommandAsync("dmesg | tail -n 5");
                throw new InvalidOperationException(
                    $"Printer function configuration failed: {ex.Message}\n" +
                    $"Recent kernel messages:\n{dmesg.Output}", ex);
            }
        }

        private async Task CreateSymbolicLinkAsync()
        {
            string target = Path.Combine(_configPath, "printer.usb0");
            
            // Remove existing symlink if it exists
            await ExecuteCommandAsync($"sudo rm -f {target}");
            
            // Create new symlink
            var result = await ExecuteCommandAsync($"sudo ln -s {_functionPath} {target}");
            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException($"Failed to create symbolic link: {result.Error}");
            }
        }
        #endregion

        #region Private Helper Methods - UDC Management
        private async Task UnbindFromUdc()
        {
            var udcPath = Path.Combine(_gadgetPath, "UDC");
            var currentUdc = await ExecuteCommandAsync($"cat {udcPath}");
            if (!string.IsNullOrWhiteSpace(currentUdc.Output))
            {
                Console.WriteLine($"Unbinding from UDC: {currentUdc.Output.Trim()}");
                await WriteValueAsync(udcPath, "");
                await Task.Delay(DelayAfterKernelOperation);
            }
        }

        private async Task RemoveSymlinks()
        {
            Console.WriteLine("Removing configuration symlinks...");
            await ExecuteCommandAsync($"sudo find {_gadgetPath}/configs -type l -delete");
            await Task.Delay(DelayAfterFileOperation);
        }

        private async Task RemoveGadgetStructure()
        {
            // Important: Order matters! We need to remove things in the correct order
            // to avoid "device or resource busy" errors

            Console.WriteLine("Starting gadget structure cleanup...");

            // First clear UDC binding to ensure the gadget is not in use
            await ExecuteCommandAsync($"sudo sh -c 'echo \"\" > {_gadgetPath}/UDC'");
            await Task.Delay(DelayAfterKernelOperation);

            try
            {
                // 1. Remove symlinks in configs first
                await ExecuteCommandAsync($"sudo find {_gadgetPath}/configs -type l -delete");
                await Task.Delay(DelayAfterFileOperation);

                // 2. Remove all values from writable files to ensure clean state
                var controlFiles = new[]
                {
                    "UDC",
                    "bDeviceClass",
                    "bDeviceSubClass",
                    "bDeviceProtocol",
                    "bMaxPacketSize0",
                    "bcdDevice",
                    "bcdUSB",
                    "idVendor",
                    "idProduct"
                };

                foreach (var file in controlFiles)
                {
                    var filePath = Path.Combine(_gadgetPath, file);
                    await ExecuteCommandAsync($"sudo sh -c 'echo 0 > {filePath}' 2>/dev/null || true");
                }

                // 3. Remove configuration strings (deepest first)
                await ExecuteCommandAsync($"sudo sh -c 'rm -f {_gadgetPath}/configs/*/strings/*/* 2>/dev/null || true'");
                await ExecuteCommandAsync($"sudo sh -c 'rmdir {_gadgetPath}/configs/*/strings/* 2>/dev/null || true'");
                await ExecuteCommandAsync($"sudo sh -c 'rmdir {_gadgetPath}/configs/*/strings 2>/dev/null || true'");

                // 4. Remove configuration attributes
                await ExecuteCommandAsync($"sudo sh -c 'rm -f {_gadgetPath}/configs/*/* 2>/dev/null || true'");
                await ExecuteCommandAsync($"sudo sh -c 'rmdir {_gadgetPath}/configs/* 2>/dev/null || true'");
                await ExecuteCommandAsync($"sudo sh -c 'rmdir {_gadgetPath}/configs 2>/dev/null || true'");

                // 5. Remove functions
                await ExecuteCommandAsync($"sudo sh -c 'rm -f {_gadgetPath}/functions/*/* 2>/dev/null || true'");
                await ExecuteCommandAsync($"sudo sh -c 'rmdir {_gadgetPath}/functions/* 2>/dev/null || true'");
                await ExecuteCommandAsync($"sudo sh -c 'rmdir {_gadgetPath}/functions 2>/dev/null || true'");

                // 6. Remove strings
                await ExecuteCommandAsync($"sudo sh -c 'rm -f {_gadgetPath}/strings/*/* 2>/dev/null || true'");
                await ExecuteCommandAsync($"sudo sh -c 'rmdir {_gadgetPath}/strings/* 2>/dev/null || true'");
                await ExecuteCommandAsync($"sudo sh -c 'rmdir {_gadgetPath}/strings 2>/dev/null || true'");

                // 7. Remove OS descriptors
                await ExecuteCommandAsync($"sudo sh -c 'rm -f {_gadgetPath}/os_desc/* 2>/dev/null || true'");
                await ExecuteCommandAsync($"sudo sh -c 'rmdir {_gadgetPath}/os_desc 2>/dev/null || true'");

                // 8. Remove webusb
                await ExecuteCommandAsync($"sudo sh -c 'rm -f {_gadgetPath}/webusb/* 2>/dev/null || true'");
                await ExecuteCommandAsync($"sudo sh -c 'rmdir {_gadgetPath}/webusb 2>/dev/null || true'");

                // 9. Remove the gadget directory itself
                await ExecuteCommandAsync($"sudo rmdir {_gadgetPath} 2>/dev/null || true");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Error during cleanup: {ex.Message}");
                // Continue anyway - we'll verify the state later
            }
        }

        private async Task VerifyCleanup()
        {
            var verifyCleanup = await ExecuteCommandAsync($"ls -la {_gadgetPath}");
            if (verifyCleanup.ExitCode == 0)
            {
                Console.WriteLine($"Warning: Gadget directory still exists after cleanup:\n{verifyCleanup.Output}");
            }
            else
            {
                Console.WriteLine("Gadget cleanup completed successfully");
            }
        }

        private async Task LoadDwc2Module()
        {
            var lsmod = await ExecuteCommandAsync("lsmod | grep dwc2");
            Console.WriteLine($"DWC2 module status:\n{lsmod.Output}");
            
            await ExecuteCommandAsync("sudo modprobe dwc2");
            await Task.Delay(DelayAfterKernelOperation);
        }

        private async Task<string> FindAndVerifyUdc()
        {
            var udcPath = "/sys/class/udc";
            if (!Directory.Exists(udcPath))
            {
                throw new InvalidOperationException("USB Device Controller directory not found after loading dwc2 module.");
            }

            var udcs = Directory.GetDirectories(udcPath);
            if (udcs.Length == 0)
            {
                throw new InvalidOperationException("No USB Device Controller found after loading dwc2 module.");
            }

            var udc = Path.GetFileName(udcs[0]);
            Console.WriteLine($"Found UDC: {udc}");
            
            var udcStatus = await ExecuteCommandAsync($"cat /sys/class/udc/{udc}/state");
            Console.WriteLine($"UDC {udc} state: {udcStatus.Output.Trim()}");
            
            return udc;
        }

        private async Task BindToUdc(string udc)
        {
            var gadgetUdcPath = Path.Combine(_gadgetPath, "UDC");
            Console.WriteLine("Attempting to bind USB gadget...");

            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    await WriteValueAsync(gadgetUdcPath, udc);
                    await Task.Delay(DelayAfterKernelOperation);

                    var verify = await ExecuteCommandAsync($"cat {gadgetUdcPath}");
                    if (verify.Output.Trim() == udc)
                    {
                        Console.WriteLine("Successfully bound to UDC");
                        await LogUsbStatus();
                        return;
                    }

                    Console.WriteLine($"Bind attempt {attempt} failed, retrying...");
                    await WriteValueAsync(gadgetUdcPath, "");
                    await Task.Delay(DelayAfterKernelOperation);

                    if (attempt == 2)
                    {
                        Console.WriteLine("Reloading dwc2 module...");
                        await LoadDwc2Module();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error during bind attempt {attempt}: {ex.Message}");
                    if (attempt == 3) throw;
                }
            }

            throw new InvalidOperationException(
                "Failed to bind USB gadget after multiple attempts.\n" +
                "Try manually:\n" +
                "1. sudo rmmod dwc2\n" +
                "2. sudo modprobe dwc2\n" +
                "3. sudo rm -rf /sys/kernel/config/usb_gadget/paycheck4\n" +
                "4. Reboot the system if issues persist");
        }
        #endregion

        #region Private Helper Methods - System State
        private async Task CleanupAllGadgets()
        {
            Console.WriteLine("Cleaning up existing USB gadget configurations...");
            var existingGadgets = await ExecuteCommandAsync("ls -1 /sys/kernel/config/usb_gadget/");
            foreach (var gadget in existingGadgets.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var gadgetPath = $"/sys/kernel/config/usb_gadget/{gadget.Trim()}";
                Console.WriteLine($"Removing gadget: {gadgetPath}");
                
                try
                {
                    await WriteValueAsync($"{gadgetPath}/UDC", "");
                    await Task.Delay(DelayAfterKernelOperation);
                    await ExecuteCommandAsync($"sudo rm -rf {gadgetPath}");
                    await Task.Delay(DelayAfterFileOperation);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Error cleaning up {gadgetPath}: {ex.Message}");
                }
            }
        }

        private async Task LogSystemState()
        {
            var lsusb = await ExecuteCommandAsync("lsusb");
            Console.WriteLine($"Current USB devices:\n{lsusb.Output}");

            var udcListing = await ExecuteCommandAsync("ls -l /sys/class/udc/");
            Console.WriteLine($"Current UDC status:\n{udcListing.Output}");

            var usbStatus = await ExecuteCommandAsync("dmesg | grep -i usb | tail -n 5");
            Console.WriteLine($"Recent USB kernel messages:\n{usbStatus.Output}");
        }

        private async Task LogUsbStatus()
        {
            var finalLsusb = await ExecuteCommandAsync("lsusb");
            Console.WriteLine($"Final USB device list:\n{finalLsusb.Output}");
            
            var dmesg = await ExecuteCommandAsync("dmesg | tail -n 10");
            Console.WriteLine($"Recent kernel messages:\n{dmesg.Output}");
        }
        #endregion

        #region Private Helper Methods - File Operations
        private async Task WriteValueAsync(string path, string value)
        {
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    // First check if the file exists and is writable
                    var checkFile = await ExecuteCommandAsync($"sudo test -w {path}");
                    if (checkFile.ExitCode != 0)
                    {
                        // If file doesn't exist or isn't writable, wait a bit and retry
                        await Task.Delay(DelayAfterFileOperation);
                        continue;
                    }

                    var escapedValue = value.Replace("\"", "\\\"");
                    // Use tee instead of direct echo to handle write errors better
                    var result = await ExecuteCommandAsync($"echo \"{escapedValue}\" | sudo tee {path} > /dev/null");
                    if (result.ExitCode == 0)
                    {
                        // Verify the write
                        var verify = await ExecuteCommandAsync($"cat {path}");
                        if (!string.IsNullOrWhiteSpace(verify.Output))
                        {
                            return; // Success
                        }
                    }

                    // If we get here, the write failed or verification failed
                    await Task.Delay(DelayAfterFileOperation * attempt);
                }
                catch (Exception ex)
                {
                    if (attempt == 3)
                    {
                        throw new InvalidOperationException($"Failed to write value to {path} after {attempt} attempts: {ex.Message}");
                    }
                    await Task.Delay(DelayAfterFileOperation * attempt);
                }
            }

            throw new InvalidOperationException($"Failed to write value to {path} after multiple attempts");
        }

        private async Task<(int ExitCode, string Output, string Error)> ExecuteCommandAsync(string command)
        {
            using var process = new System.Diagnostics.Process();
            process.StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "bash",
                Arguments = $"-c \"{command.Replace("\"", "\\\"")}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            var output = new System.Text.StringBuilder();
            var error = new System.Text.StringBuilder();

            process.OutputDataReceived += (sender, e) => { if (e.Data != null) output.AppendLine(e.Data); };
            process.ErrorDataReceived += (sender, e) => { if (e.Data != null) error.AppendLine(e.Data); };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync();

            return (process.ExitCode, output.ToString(), error.ToString());
        }
        #endregion
    }
}