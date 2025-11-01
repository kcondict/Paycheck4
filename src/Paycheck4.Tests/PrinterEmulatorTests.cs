using Xunit;
using Moq;
using System;
using Paycheck4.Core.Protocol;
using Paycheck4.Core.Usb;

namespace Paycheck4.Core.Tests
{
    public class PrinterEmulatorTests
    {
        private readonly PrinterEmulator _emulator;
        private readonly Mock<UsbGadgetManager> _mockUsbManager;
        private readonly Mock<UsbGadgetConfigurator> _mockUsbConfigurator;
        private readonly Mock<TclProtocol> _mockProtocol;

        public PrinterEmulatorTests()
        {
            _mockUsbManager = new Mock<UsbGadgetManager>();
            _mockUsbConfigurator = new Mock<UsbGadgetConfigurator>();
            _mockProtocol = new Mock<TclProtocol>();
            _emulator = new PrinterEmulator();
        }

        [Fact]
        public void Initialize_SetsStatusToReady()
        {
            // Act
            _emulator.Initialize();

            // Assert
            Assert.Equal(PrinterStatus.Ready, _emulator.Status);
        }

        [Fact]
        public void Start_WhenReady_SetsStatusToRunning()
        {
            // Arrange
            _emulator.Initialize();

            // Act
            _emulator.Start();

            // Assert
            Assert.Equal(PrinterStatus.Running, _emulator.Status);
        }

        [Fact]
        public void Start_WhenNotInitialized_ThrowsException()
        {
            // Act & Assert
            Assert.Throws<InvalidOperationException>(() => _emulator.Start());
        }

        [Fact]
        public void Stop_SetsStatusToStopped()
        {
            // Arrange
            _emulator.Initialize();
            _emulator.Start();

            // Act
            _emulator.Stop();

            // Assert
            Assert.Equal(PrinterStatus.Stopped, _emulator.Status);
        }

        [Fact]
        public void Reset_ReturnsToReadyState()
        {
            // Arrange
            _emulator.Initialize();
            _emulator.Start();

            // Act
            _emulator.Reset();

            // Assert
            Assert.Equal(PrinterStatus.Ready, _emulator.Status);
        }

        [Fact]
        public void StatusChanged_EventIsRaised()
        {
            // Arrange
            var eventRaised = false;
            _emulator.StatusChanged += (s, e) => eventRaised = true;

            // Act
            _emulator.Initialize();

            // Assert
            Assert.True(eventRaised);
        }

        [Fact]
        public void Dispose_StopsEmulator()
        {
            // Arrange
            _emulator.Initialize();
            _emulator.Start();

            // Act
            _emulator.Dispose();

            // Assert
            Assert.Equal(PrinterStatus.Stopped, _emulator.Status);
        }
    }
}