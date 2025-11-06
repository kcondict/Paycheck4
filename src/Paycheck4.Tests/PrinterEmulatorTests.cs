using Xunit;
using Moq;
using System;
using Microsoft.Extensions.Logging;
using Paycheck4.Core.Protocol;
using Paycheck4.Core.Usb;

namespace Paycheck4.Core.Tests
{
    public class PrinterEmulatorTests
    {
        private readonly Mock<ILogger<PrinterEmulator>> _mockLogger;
        private readonly Mock<ILogger<UsbGadgetManager>> _mockUsbLogger;

        public PrinterEmulatorTests()
        {
            _mockLogger = new Mock<ILogger<PrinterEmulator>>();
            _mockUsbLogger = new Mock<ILogger<UsbGadgetManager>>();
        }

        // Note: Most tests are disabled because they require actual hardware (/dev/ttyGS0)
        // These tests would need integration testing on the Raspberry Pi

        [Fact]
        public void Constructor_WithValidLoggers_DoesNotThrow()
        {
            // Act & Assert
            var exception = Record.Exception(() => 
                new PrinterEmulator(_mockLogger.Object, _mockUsbLogger.Object));
            
            Assert.Null(exception);
        }

        [Fact]
        public void Constructor_WithNullEmulatorLogger_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => 
                new PrinterEmulator(null!, _mockUsbLogger.Object));
        }

        [Fact]
        public void Constructor_WithNullUsbLogger_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => 
                new PrinterEmulator(_mockLogger.Object, null!));
        }

        // Integration tests would go here - require hardware
        // [Fact]
        // public void Initialize_WithValidDevice_SetsStatusToReady()
        // [Fact]
        // public void Start_WhenReady_SetsStatusToRunning()
        // etc.
    }
}