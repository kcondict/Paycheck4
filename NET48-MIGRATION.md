# .NET Framework 4.8 Migration Notes

## Overview
The project has been converted from .NET 8.0 to .NET Framework 4.8 to support running as a Windows executable with Mono on Debian Bookworm (Raspberry Pi).

## Key Changes

### Project Files
- **Target Framework**: Changed from `net8.0` to `net48`
- **Package Versions**: Downgraded to .NET Framework 4.8 compatible versions
  - Microsoft.Extensions.* packages: 8.0.0 → 2.2.0
  - Serilog packages: Downgraded to compatible versions
  - System.IO.Ports: 8.0.0 → 7.0.0 (latest compatible version)

### Architecture Changes
- **Removed Generic Host Pattern**: .NET Framework 4.8 doesn't support `Microsoft.Extensions.Hosting` the same way
- **Direct Service Initialization**: Program.cs now directly creates and manages the PrinterEmulator instead of using IHostedService
- **PrinterEmulatorService.cs**: No longer used (can be deleted)

### Code Changes
- **Using Directives**: Removed `ImplicitUsings` - all using statements now explicit
- **AppContext.BaseDirectory → AppDomain.CurrentDomain.BaseDirectory**: .NET Framework equivalent
- **Console Cancellation**: Manual implementation using `Console.CancelKeyPress` event

## Building

### Windows
```batch
build-net48.bat
```

Or manually:
```bash
dotnet build src/Paycheck4.Console/Paycheck4.Console.csproj -c Release
```

Output: `src/Paycheck4.Console/bin/Release/net48/`

## Deployment

### Copy to Raspberry Pi
```batch
deploy-net48.bat
```

Or manually:
```bash
scp -r src/Paycheck4.Console/bin/Release/net48/* kcondict@192.168.68.69:~/paycheck4/
```

## Running on Raspberry Pi

### Prerequisites
1. Install Mono:
```bash
sudo apt-get update
sudo apt-get install mono-complete
```

2. Verify Mono version:
```bash
mono --version
```
Should be Mono 6.8 or later for best .NET Framework 4.8 compatibility.

### Run the Application
```bash
cd ~/paycheck4
mono Paycheck4.Console.exe
```

### Run as Service (systemd)
Create `/etc/systemd/system/paycheck4.service`:
```ini
[Unit]
Description=Paycheck4 Printer Emulator
After=network.target

[Service]
Type=simple
User=kcondict
WorkingDirectory=/home/kcondict/paycheck4
ExecStart=/usr/bin/mono /home/kcondict/paycheck4/Paycheck4.Console.exe
Restart=on-failure
RestartSec=5

[Install]
WantedBy=multi-user.target
```

Enable and start:
```bash
sudo systemctl daemon-reload
sudo systemctl enable paycheck4
sudo systemctl start paycheck4
sudo systemctl status paycheck4
```

## Compatibility Notes

### Mono on Debian Bookworm
- Debian 12 (Bookworm) includes Mono 6.8+
- .NET Framework 4.8 features supported
- System.IO.Ports works with Mono's implementation

### Known Limitations
- No single-file publish (not supported in .NET Framework)
- All dependencies (.dll files) must be deployed together
- Configuration files (appsettings.json) must be in same directory as executable

## Troubleshooting

### Missing Dependencies
If you see "Assembly not found" errors:
```bash
# Check what assemblies Mono can find
mono --debug Paycheck4.Console.exe
```

### Serial Port Access
Ensure user has permissions:
```bash
sudo usermod -a -G dialout kcondict
sudo usermod -a -G tty kcondict
```

### Logging
Logs are written to `logs/paycheck4.log` in the application directory.

## Development

### IDE Support
- Visual Studio 2019/2022 (full support)
- Visual Studio Code (with C# extension)
- Rider

### Testing
Tests still target .NET Framework 4.8:
```bash
dotnet test src/Paycheck4.Tests/Paycheck4.Tests.csproj
```
