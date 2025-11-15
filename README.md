# Paycheck4 Printer Emulator

A USB printer emulator for the Nanoptix PayCheck 4 thermal printer, designed to run on a Raspberry Pi 5 with Debian 12. This emulator implements the TCL (Thermal Control Language) protocol and presents itself as a USB printer device to Electronic Gaming Machines (EGMs).

## Project Goals

- Emulate a Nanoptix PayCheck 4 printer in USB device mode
- Implement the complete TCL protocol for printer control
- Provide reliable communication with EGM hosts
- Support forwarding print jobs to network printers
- Maintain gaming industry compliance standards

## Technical Details

- **Platform**: Raspberry Pi 5 running Debian 12 (Bookworm)
- **Framework**: .NET Framework 4.8 (via Mono runtime)
- **Language**: C# 12
- **USB Mode**: Gadget Mode (g_serial kernel module for CDC ACM)
- **Protocol**: TCL (Thermal Control Language)
- **Device IDs**: 
  - Vendor ID: 0x0f0f
  - Product ID: 0x1001
- **Communication**: USB CDC ACM (/dev/ttyGS0 on Pi, COM6 on Windows PC)

## Project Structure

```
paycheck4/
├── build.sh                        # Build and deployment script
├── g_printer.service               # Systemd service file
├── setup_usb_serial_device.sh      # USB serial gadget setup script
├── Paycheck4.sln                   # Solution file
├── PRD.md                          # Product Requirements Document
├── README.md                       # This file
│
├── src/
│   ├── Paycheck4.Core/             # Core library
│   │   ├── Paycheck4.Core.csproj
│   │   ├── IPrinterEmulator.cs     # Main emulator interface
│   │   ├── PrinterEmulator.cs      # Core emulator implementation
│   │   ├── PrinterStatus.cs        # Status enumeration
│   │   ├── Protocol/               # TCL protocol implementation
│   │   │   ├── TclCommand.cs       # TCL command definitions
│   │   │   └── TclProtocol.cs      # Protocol parser/handler
│   │   └── Usb/                    # USB serial communication
│   │       ├── IUsbGadgetManager.cs       # USB interface
│   │       ├── UsbGadgetManager.cs        # USB serial manager (/dev/ttyGS0)
│   │       └── LoggerExtensions.cs        # Logging utilities
│   │
│   ├── Paycheck4.Console/          # Console application (systemd service)
│   │   ├── Paycheck4.Console.csproj
│   │   ├── Program.cs              # Entry point and DI setup
│   │   ├── PrinterEmulatorService.cs      # Hosted service
│   │   ├── appsettings.json        # Production configuration
│   │   ├── appsettings.Development.json   # Dev configuration
│   │   └── Configuration/
│   │       └── PrinterConfig.cs    # Configuration models
│   │
│   └── Paycheck4.Tests/            # Unit tests
│       ├── Paycheck4.Tests.csproj
│       └── PrinterEmulatorTests.cs # Emulator unit tests
│
├── test/                           # Test applications
│   ├── PiSerialTest/               # Raspberry Pi test app
│   │   ├── PiSerialTest.csproj
│   │   └── Program.cs              # Sends test messages every second
│   │
│   └── SerialTestApp/              # PC test app
│       ├── SerialTestApp.csproj
│       └── Program.cs              # Echoes received messages back
│
└── notes/                          # Development notes
    ├── notes
    └── notes-1
```

## Prerequisites

1. Raspberry Pi 5 with Debian 12 (Bookworm)
2. .NET 8.0 SDK installed
3. USB gadget mode enabled in kernel
4. Appropriate permissions for USB device access

## Building and Deploying

The project targets .NET Framework 4.8 for Mono compatibility on Raspberry Pi 5.

### Building for Raspberry Pi (Release)

Build both Core library and Console application targeting .NET Framework 4.8:

```bash
# Build Paycheck4.Core for net48
dotnet build src/Paycheck4.Core/Paycheck4.Core.csproj -c Release -f net48

# Build Paycheck4.Console for net48
dotnet build src/Paycheck4.Console/Paycheck4.Console.csproj -c Release -f net48

# Deploy to Raspberry Pi
scp -r src/Paycheck4.Console/bin/Release/net48/* kcondict@192.168.68.69:~/paycheck4/
```

Or use the combined command:
```bash
dotnet build src/Paycheck4.Core/Paycheck4.Core.csproj -c Release -f net48 && \
dotnet build src/Paycheck4.Console/Paycheck4.Console.csproj -c Release -f net48 && \
scp -r src/Paycheck4.Console/bin/Release/net48/* kcondict@192.168.68.69:~/paycheck4/
```

The build output includes:
- `Paycheck4.Console.exe` - Main application
- `Paycheck4.Core.dll` - Core library
- All dependencies (Microsoft.Extensions.*, Serilog.*, etc.)
- Configuration files (appsettings.json, appsettings.Development.json)

### Building PC Test Application (SerialTestApp)

The PC test application is used to send test commands to the printer emulator over USB serial.

```bash
# Build the test application
dotnet build test/SerialTestApp/SerialTestApp.csproj -c Release

# Run the test application (connects to COM6 by default)
cd test/SerialTestApp
dotnet run --no-build -c Release
```

Available commands in SerialTestApp:
- `p` - Send standard print command (4 fields)
- `L` - Send large print command (15 fields x 18 chars, sent in 5 segments with 2ms pauses)
- `hex:<bytes>` - Send raw hex bytes (e.g., `hex:48656C6C6F`)
- `exit` - Quit the application
- Any other text - Send as-is to the printer

The large print command ('L') is particularly useful for testing message reassembly:
- Sends ~293 bytes total
- Split into 5 segments
- 2ms pause between segments
- Tests the WFNS (Waiting For Next Segment) state machine

### Development Build

For debugging and development:

```bash
# Build in Debug configuration
dotnet build src/Paycheck4.Core/Paycheck4.Core.csproj -c Debug -f net48
dotnet build src/Paycheck4.Console/Paycheck4.Console.csproj -c Debug -f net48

# Deploy to Pi
scp -r src/Paycheck4.Console/bin/Debug/net48/* kcondict@192.168.68.69:~/paycheck4/
```

### Running on Raspberry Pi

On the Raspberry Pi, use Mono to run the .NET Framework application:

```bash
# Navigate to deployment directory
cd ~/paycheck4

# Run with Mono
mono Paycheck4.Console.exe
```

Note: The application requires access to `/dev/ttyGS0` and may need elevated privileges:
```bash
sudo mono Paycheck4.Console.exe
```

## Running Tests

```bash
# Run all tests
dotnet test

# Run tests with coverage
dotnet test --collect:"XPlat Code Coverage"

# Run tests for a specific project
dotnet test src/Paycheck4.Tests/Paycheck4.Tests.csproj
```

## USB Serial Test Applications

Two test applications are included to verify USB serial communication:

### PC Test Application (SerialTestApp)
Connects to the COM port and allows sending test commands to the printer emulator.

**Building:**
```bash
# Build the application
dotnet build test/SerialTestApp/SerialTestApp.csproj -c Release

# Run the application
cd test/SerialTestApp
dotnet run --no-build -c Release
```

**Features:**
- Connects to COM6 by default (configurable in code)
- Sends test print commands:
  - `p` - Standard print command with 4 fields
  - `L` - Large print command with 15 fields (18 chars each), sent in 5 segments with 2ms pauses
- Send raw hex bytes: `hex:48656C6C6F`
- Send custom text messages
- Displays hex dump of all received data
- Type 'exit' to quit

**Test Commands:**
- Standard Print (`p`): Sends ~55 byte message with template ID, 1 copy, and 4 data fields
- Large Print (`L`): Sends ~293 byte message split into 5 segments to test message reassembly
  - Each segment sent via `WriteLine()` to force USB transmission
  - 2ms pause between segments tests the WFNS (Waiting For Next Segment) state machine
  - Pi filters out CR/LF bytes added by `WriteLine()`

### Raspberry Pi Test Application (PiSerialTest)
*Note: This may be deprecated in favor of the main Paycheck4.Console application*

Sends timestamped messages every second and displays echoed responses.

```bash
# Build and deploy
cd test/PiSerialTest
dotnet publish -c Release -r linux-arm64 --self-contained
scp -r bin/Release/net8.0/linux-arm64/publish kcondict@192.168.68.70:/opt/PiSerialTest/

# Run on Pi
ssh kcondict@192.168.68.70 'sudo /opt/PiSerialTest/PiSerialTest'
```

Features:
- Sends messages every second with timestamp
- Listens for echo responses
- Displays sent and received messages
- Press Ctrl+C to quit

### Testing USB Serial Communication

1. Start the Paycheck4.Console application on the Pi:
```bash
ssh kcondict@192.168.68.69
cd ~/paycheck4
sudo mono Paycheck4.Console.exe
```

2. Connect the Pi to PC via USB-C

3. On PC, start the test app:
```bash
cd test/SerialTestApp
dotnet run --no-build -c Release
```

4. Send test commands:
   - Press `p` for standard print command
   - Press `L` for large multi-segment print command
   - Type custom messages and press Enter

You should see:
- PC: Messages being sent with hex dumps
- Pi: Log messages showing message receipt, reassembly, and processing
  - Buffer state transitions (WFFS → WFNS)
  - Complete message processing
  - Print job state machine transitions

## Deployment

1. Build the release package:
```bash
dotnet publish -c Release -r linux-arm64 --self-contained
```

2. Copy the published files to the Raspberry Pi:
```bash
scp -r src/Paycheck4.Console/bin/Release/net8.0/linux-arm64/publish/* pi@raspberry:/opt/paycheck4/
```

3. Set permissions on the Raspberry Pi:
```bash
chmod +x /opt/paycheck4/Paycheck4.Console
```

4. Configure USB serial gadget (ONE-TIME SETUP):

The application uses USB serial (ACM - Abstract Control Model) for communication via `/dev/ttyGS0`. Run the setup script once:

```bash
# Copy the setup script to the Pi
scp setup_usb_serial_device.sh pi@raspberry:/tmp/

# Run the setup script
ssh pi@raspberry "sudo bash /tmp/setup_usb_serial_device.sh"

# Move it to a permanent location for future use
ssh pi@raspberry "sudo cp /tmp/setup_usb_serial_device.sh /usr/local/bin/ && sudo chmod +x /usr/local/bin/setup_usb_serial_device.sh"

# Configure it to run at boot by adding to /etc/rc.local or creating a systemd service
```

To make it run at boot, add to `/etc/rc.local` before the `exit 0` line:
```bash
/usr/local/bin/setup_usb_serial_device.sh
```

Or create a systemd service (recommended):
```bash
sudo nano /etc/systemd/system/usb-gadget.service

# Add:
[Unit]
Description=USB Serial Gadget Setup
DefaultDependencies=no
After=local-fs.target

[Service]
Type=oneshot
ExecStart=/usr/local/bin/setup_usb_serial_device.sh
RemainAfterExit=yes

[Install]
WantedBy=sysinit.target

# Enable it
sudo systemctl enable usb-gadget.service
sudo systemctl start usb-gadget.service
```

Verify the device is available:
```bash
ls -l /dev/ttyGS0
```

The application will use the `/dev/ttyGS0` device for USB serial communication. The PC will see this as a COM port (e.g., COM6 on Windows).

5. Create a systemd service (recommended for production):
```bash
# Create the service file
sudo nano /etc/systemd/system/paycheck4.service

# Add the following content:
[Unit]
Description=PayCheck 4 Printer Emulator
After=network.target

[Service]
Type=simple
User=root
WorkingDirectory=/opt/paycheck4
ExecStart=/opt/paycheck4/Paycheck4.Console
Restart=always
RestartSec=10

[Install]
WantedBy=multi-user.target

# Enable and start the service
sudo systemctl enable paycheck4
sudo systemctl start paycheck4

# View service status and logs
sudo systemctl status paycheck4
sudo journalctl -u paycheck4 -f
```

Alternatively, for development/testing, run directly with sudo:
```bash
sudo /opt/paycheck4/Paycheck4.Console
```

Note: Root privileges are required to configure USB gadget mode settings.

## Configuration

The application uses the following configuration sources:
- appsettings.json
- Environment variables
- Command line arguments

Key configuration options:
- USB device parameters
- Logging settings
- Network printer configuration

## Logging

Logs are written to:
- Console output
- Daily rolling file logs in `logs/paycheck4.log`

## Development Guidelines

- Follow the coding style guide in `.github/copilot-instructions.md`
- Add XML documentation for public APIs
- Include unit tests for new features
- Use meaningful commit messages
- Update documentation as needed

## Testing Strategy

1. **Unit Tests**: Testing individual components in isolation
   - Protocol parsing
   - USB communication
   - Status management
   - Event handling

2. **Integration Tests**: Testing component interactions
   - USB device enumeration
   - Protocol communication
   - Status transitions

3. **System Tests**: Testing the complete system
   - End-to-end print jobs
   - Error handling
   - Resource management

## Troubleshooting

Common issues and solutions:

1. USB Device Permission Denied
```bash
sudo chmod 666 /dev/bus/usb/<bus>/<device>
```

2. Gadget Mode Not Available
```bash
# Check kernel modules
lsmod | grep dwc2
sudo modprobe dwc2
```

3. Application Won't Start
- Check logs in `logs/paycheck4.log`
- Verify USB device permissions
- Ensure no other USB gadget is active

## Contributing

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Add or update tests
5. Submit a pull request

## License

Copyright © 2025. All rights reserved.

## Contact

For issues or questions, please create a GitHub issue.