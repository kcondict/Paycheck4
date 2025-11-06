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
- **Framework**: .NET 8.0
- **Language**: C# 12
- **USB Mode**: Gadget Mode (g_printer kernel module)
- **Protocol**: TCL (Thermal Control Language)
- **Device IDs**: 
  - Vendor ID: 0x0f0f
  - Product ID: 0x1001

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

The project includes a build script (`build.sh`) that handles building, publishing, and deployment. 

### Quick Start
```bash
# Make script executable
chmod +x build.sh

# Build and deploy debug version
./build.sh -p -d --host 192.168.68.69 --user kcondict

# Build and deploy release version
./build.sh -p -c Release -d --host 192.168.68.69 --user kcondict
```

### Build Script Options
```bash
Options:
  -h, --help                 Show help message
  -c, --configuration        Set build configuration (Debug|Release) [default: Debug]
  -r, --runtime              Set runtime identifier [default: linux-arm64]
  -s, --self-contained       Build as self-contained application
  -p, --publish              Publish the application instead of building
  -o, --output               Set the output directory for publish
  -d, --deploy               Deploy to remote host after build/publish
  --host                     Remote host for deployment (e.g., 192.168.68.69)
  --user                     Remote username for deployment
  --clean                    Clean before building
  --restore                  Restore dependencies before building
  -v, --verbosity            Set verbosity level (quiet|minimal|normal|detailed|diagnostic)
```

### Examples
```bash
# Simple debug build
./build.sh

# Release build
./build.sh -c Release

# Publish self-contained release
./build.sh -p -c Release -s

# Build and deploy to specific host
./build.sh -p -d --host 192.168.68.69 --user kcondict

# Full release deployment with all options
./build.sh -p -c Release -s -d --host 192.168.68.69 --user kcondict --clean
```

The script will:
1. Build/publish the application
2. Set up the target directory with correct permissions
3. Copy all required files
4. Set appropriate execute permissions

### Build vs. Publish

The project supports two main compilation approaches: `build` and `publish`. Here's when to use each:

#### dotnet build
- Use during development and testing
- Creates binaries in the project's `/bin` directory
- Produces assemblies and debugging symbols
- Does not include dependencies or runtime
- Output remains in project structure
- Example output structure:
```
bin/
  Debug/
    net8.0/
      Paycheck4.Core.dll
      Paycheck4.Console.dll
      Paycheck4.Console.deps.json
```

#### dotnet publish
- Use for creating deployment packages
- Creates complete, deployable package
- Includes all dependencies and runtime (with --self-contained)
- Optimized for deployment
- Can target specific platforms
- Example output structure:
```
publish/
  Paycheck4.Console
  Paycheck4.Core.dll
  Paycheck4.Console.dll
  System.*.dll        # Runtime libraries
  *.so               # Native libraries
  appsettings.json
  # All other dependencies
```

### Development Build

First, add projects to solution if not already added:
```bash
# Add projects to solution
dotnet sln add src/Paycheck4.Core/Paycheck4.Core.csproj
dotnet sln add src/Paycheck4.Console/Paycheck4.Console.csproj
dotnet sln add src/Paycheck4.Tests/Paycheck4.Tests.csproj
```

Then build:
```bash
# Build entire solution
dotnet build Paycheck4.sln

# Or build individual projects
dotnet build src/Paycheck4.Core/Paycheck4.Core.csproj
dotnet build src/Paycheck4.Console/Paycheck4.Console.csproj
dotnet build src/Paycheck4.Tests/Paycheck4.Tests.csproj
```

### Release Build

```bash
# Build solution in Release configuration
dotnet build Paycheck4.sln -c Release

# Or build specific project in Release configuration
dotnet build src/Paycheck4.Console/Paycheck4.Console.csproj -c Release
```

### Publishing for Deployment

```bash
# Create a self-contained deployment for Raspberry Pi
dotnet publish -c Release -r linux-arm64 --self-contained
```

The published output will be in `src/Paycheck4.Console/bin/Release/net8.0/linux-arm64/publish/`

### When to Use Each Command

Use `dotnet build`:
- During active development
- When running tests locally
- For quick iteration and debugging
- When you have .NET SDK installed

Use `dotnet publish`:
- Creating deployment packages
- Building for different platforms
- When deploying to production
- When target machine doesn't have .NET installed

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
Connects to the COM port and echoes back any received data.

```bash
cd test/SerialTestApp
dotnet run
```

Features:
- Auto-echoes all received messages
- Displays hex dump of received data
- Allows manual message sending
- Type 'exit' to quit

### Raspberry Pi Test Application (PiSerialTest)
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

1. Start the USB serial gadget on the Pi:
```bash
sudo /usr/local/bin/setup_usb_serial_device.sh
```

2. Connect the Pi to PC via USB-C

3. On PC, start the echo test app:
```bash
cd test/SerialTestApp && dotnet run
```

4. On Pi, start the sender test app:
```bash
sudo /opt/PiSerialTest/PiSerialTest
```

You should see messages being sent from Pi, echoed by PC, and received back on Pi.

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