using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Paycheck4.Core.Protocol
{
    /// <summary>
    /// Implementation of the TCL (Thermal Controller Language) protocol
    /// </summary>
    public class TclProtocol
    {
        #region Events
        /// <summary>
        /// Event raised when printer status changes
        /// </summary>
        /// <remarks>
        /// This event will be used when processing command responses that affect printer status
        /// </remarks>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CS0067:The event is never used", 
            Justification = "Event will be used when command response handling is implemented")]
        public event EventHandler<PrinterStatusEventArgs>? StatusChanged;
        #endregion

        #region Constants
        private const byte ENQ = 0x05;
        private const byte ACK = 0x06;
        private const byte NAK = 0x15;

        private static readonly byte[] FormFeed = Encoding.ASCII.GetBytes("^f|I|^");
        private static readonly byte[] ClearFlags = Encoding.ASCII.GetBytes("^C|^");
        private static readonly byte[] ClearFlagsAndJam = Encoding.ASCII.GetBytes("^C|j|^");
        private static readonly byte[] ResetCommand = Encoding.ASCII.GetBytes("^r|^");
        #endregion

        #region Fields and Events
        private readonly object _lock = new object();
        private readonly Queue<byte[]> _responseQueue = new Queue<byte[]>();
        private readonly AutoResetEvent _responseEvent = new AutoResetEvent(false);
        private readonly ILogger<TclProtocol>? _logger;
        private bool _extendedStatusSent = false;
        
        // Extended status data
        private byte _unitAddress = 0x00;
        private string _softwareVersion = "PAY-6.22B";
        private byte _statusFlags1 = (byte)StatusFlag1.Unmask;
        private byte _statusFlags2 = (byte)StatusFlag2.Unmask;
        private byte _statusFlags3 = (byte)StatusFlag3.Unmask;
        private byte _statusFlags4 = (byte)StatusFlag4.Unmask;
        private byte _statusFlags5 = (byte)(StatusFlag5.Unmask | StatusFlag5.ValidationDone | StatusFlag5.AtTopOfForm | StatusFlag5.ResetPowerUp);
        private string _tempNumber = "P9";
        
        /// <summary>
        /// Event raised when a TCL command is received and parsed
        /// </summary>
        /// <remarks>
        /// This event will be used when command parsing is implemented
        /// </remarks>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CS0067:The event is never used", 
            Justification = "Event will be used when command parsing is implemented")]
        public event EventHandler<TclCommandEventArgs>? CommandReceived;

        /// <summary>
        /// Event raised when printer status should be reported
        /// </summary>
        public event EventHandler<TclStatusRequestEventArgs>? StatusRequested;
        
        /// <summary>
        /// Event raised when a response needs to be sent to the host
        /// </summary>
        public event EventHandler<TclResponseEventArgs>? ResponseReady;
        #endregion
        
        #region Constructor
        public TclProtocol(ILogger<TclProtocol>? logger = null)
        {
            _logger = logger;
        }
        #endregion

        #region Status Flags
        [Flags]
        public enum StatusFlag1 : byte
        {
            Mask = 0x3f,
            Unmask = 0x40,
            Busy = 0x20,
            SystemError = 0x10,
            PlatenUp = 0x08,
            PaperOut = 0x04,
            HeadError = 0x02,
            VoltageError = 0x01
        }

        [Flags]
        public enum StatusFlag2 : byte
        {
            Mask = 0x3f,
            Unmask = 0x40,
            TemperatureError = 0x20,
            LibraryRefError = 0x10,
            PrintRegionDataError = 0x08,
            LibraryLoadError = 0x04,
            BufferOverflow = 0x02,
            JobMemoryOverflow = 0x01
        }

        [Flags]
        public enum StatusFlag3 : byte
        {
            Mask = 0x3f,
            Unmask = 0x40,
            CommandError = 0x20,
            PrintLibrariesCorrupt = 0x10,
            PaperInChute = 0x08,
            FlashProgramError = 0x04,
            PrinterOffline = 0x02,
            MissingSupplyIndex = 0x01
        }

        [Flags]
        public enum StatusFlag4 : byte
        {
            Mask = 0x0f,
            Unmask = 0x40,
            JournalPrintMode = 0x80,
            Reserved = 0x40,
            PaperJam = 0x20,
            PaperLow = 0x01
        }

        [Flags]
        public enum StatusFlag5 : byte
        {
            Mask = 0x3f,
            Unmask = 0x40,
            ValidationDone = 0x20,
            AtTopOfForm = 0x10,
            XedOff = 0x08,
            PrinterOpen = 0x04,
            BarcodeDataIsAccessed = 0x02,
            ResetPowerUp = 0x01
        }
        #endregion

        #region Message Processing
        /// <summary>
        /// Process incoming data from the host
        /// </summary>
        public void ProcessIncomingData(byte[] data, int offset, int count)
        {
            // Mark that we've received data (host is connected)
            _extendedStatusSent = true;
            
            // Convert to string for command parsing
            var message = Encoding.ASCII.GetString(data, offset, count);
            _logger?.LogInformation("Received data from host: {Message}", message);
            
            // Check for print template command
            if (message.StartsWith("^P|") && message.EndsWith("|^"))
            {
                var printCommand = ParsePrintTemplateCommand(message);
                if (printCommand != null)
                {
                    _logger?.LogInformation("Print command detected: Template={Template}, Copies={Copies}, Fields={FieldCount}",
                        printCommand.TemplateId, printCommand.Copies, printCommand.PrintFields.Count);
                    
                    // TODO: Raise event or handle print command
                }
            }
        }
        
        /// <summary>
        /// Parses a print template command
        /// Format: ^P|<template_id>|<copies>|<pr1_data>|<pr2_data>|...|prN_data|^
        /// </summary>
        private PrintTemplateCommand? ParsePrintTemplateCommand(string message)
        {
            try
            {
                // Remove start/end delimiters
                if (!message.StartsWith("^P|") || !message.EndsWith("|^"))
                    return null;
                
                var content = message.Substring(3, message.Length - 5); // Remove "^P|" and "|^"
                var parts = content.Split('|');
                
                if (parts.Length < 2)
                {
                    _logger?.LogWarning("Invalid print command: insufficient fields");
                    return null;
                }
                
                // Parse template ID (single character)
                if (parts[0].Length != 1)
                {
                    _logger?.LogWarning("Invalid template ID: {TemplateId}", parts[0]);
                    return null;
                }
                char templateId = parts[0][0];
                
                // Parse copies (1-4 digits, 1-9999)
                if (!int.TryParse(parts[1], out int copies) || copies < 1 || copies > 9999)
                {
                    _logger?.LogWarning("Invalid copies value: {Copies}", parts[1]);
                    return null;
                }
                
                // Collect print data fields (remaining parts)
                var printFields = new List<string>();
                for (int i = 2; i < parts.Length; i++)
                {
                    printFields.Add(parts[i]);
                }
                
                return new PrintTemplateCommand
                {
                    TemplateId = templateId,
                    Copies = copies,
                    PrintFields = printFields
                };
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error parsing print template command");
                return null;
            }
        }

        /// <summary>
        /// Gets the next response to send to the host
        /// </summary>
        public byte[]? GetNextResponse(int timeout = 1000)
        {
            if (_responseEvent.WaitOne(timeout))
            {
                lock (_lock)
                {
                    if (_responseQueue.Count > 0)
                    {
                        return _responseQueue.Dequeue();
                    }
                }
            }
            return Array.Empty<byte>();
        }

        /// <summary>
        /// Queues a response to be sent to the host
        /// </summary>
        private void QueueResponse(byte[] response)
        {
            if (response == null) return;
            
            lock (_lock)
            {
                _responseQueue.Enqueue(response);
                _responseEvent.Set();
            }
        }
        #endregion

        #region Command Handlers
        /// <summary>
        /// Handles a status request from the host
        /// </summary>
        private void HandleStatusRequest()
        {
            var args = new TclStatusRequestEventArgs();
            StatusRequested?.Invoke(this, args);
            
            // Format and queue status response
            var response = FormatStatusResponse(args.Status);
            QueueResponse(response);
        }

        /// <summary>
        /// Formats a status response according to TCL protocol
        /// </summary>
        private byte[] FormatStatusResponse(PrinterStatus status)
        {
            // Convert PrinterStatus to TCL status flags
            // Format according to protocol specs
            // Return formatted response
            return Array.Empty<byte>(); // TODO: Implement with actual status response
        }
        #endregion

        #region Initialization
        /// <summary>
        /// Initializes the TCL protocol handler
        /// </summary>
        public void Initialize()
        {
            // Send extended status immediately
            SendExtendedStatusResponse();
            
            // Send status every 5 seconds continuously
            _ = Task.Run(async () =>
            {
                while (true)
                {
                    await Task.Delay(5000);
                    _logger?.LogInformation("Sending periodic extended status");
                    SendExtendedStatusResponse();
                }
            });
        }

        /// <summary>
        /// Process incoming data from USB device
        /// </summary>
        public void ProcessData(byte[] data, int offset, int count)
        {
            // Mark that we've received data (host is connected)
            _extendedStatusSent = true;
            
            // Process the incoming data
            ProcessIncomingData(data, offset, count);
        }
        
        /// <summary>
        /// Sends an extended status response to the host
        /// </summary>
        private void SendExtendedStatusResponse()
        {
            _logger?.LogInformation("Building and sending extended status response");
            var response = BuildExtendedStatusResponse();
            
            // Log the response bytes
            var hexString = BitConverter.ToString(response).Replace("-", " ");
            _logger?.LogInformation("Extended status response: {ByteCount} bytes - {HexData}", response.Length, hexString);
            
            ResponseReady?.Invoke(this, new TclResponseEventArgs(response));
        }
        
        /// <summary>
        /// Builds an extended status response according to TCL protocol
        /// Format: *S|0|PAY-6.22B|flag1|flag2|flag3|flag4|flag5|P9|*
        /// Flags are single binary bytes, other fields are ASCII strings
        /// </summary>
        private byte[] BuildExtendedStatusResponse()
        {
            // Build the response manually with mix of ASCII and binary
            var response = new List<byte>();
            
            // Start delimiter and unit address (ASCII)
            response.AddRange(Encoding.ASCII.GetBytes($"*S|{_unitAddress}|{_softwareVersion}|"));
            
            // Status flags (binary bytes)
            response.Add(_statusFlags1);
            response.Add((byte)'|');
            response.Add(_statusFlags2);
            response.Add((byte)'|');
            response.Add(_statusFlags3);
            response.Add((byte)'|');
            response.Add(_statusFlags4);
            response.Add((byte)'|');
            response.Add(_statusFlags5);
            
            // Temp number and end delimiter (ASCII)
            response.AddRange(Encoding.ASCII.GetBytes($"|{_tempNumber}|*"));
            
            return response.ToArray();
        }
        #endregion

        #region Command Generation
        /// <summary>
        /// Generates a TCL format print command
        /// </summary>
        public byte[] GeneratePrintCommand(string data)
        {
            // Format print command according to TCL protocol
            return Array.Empty<byte>(); // TODO: Implement with actual print command
        }

        /// <summary>
        /// Generates a paper feed command
        /// </summary>
        public byte[] GenerateFeedCommand(int lines)
        {
            // Format feed command according to TCL protocol
            return Array.Empty<byte>(); // TODO: Implement with actual feed command
        }

        /// <summary>
        /// Generates a paper cut command
        /// </summary>
        public byte[] GenerateCutCommand()
        {
            // Format cut command according to TCL protocol
            return Array.Empty<byte>(); // TODO: Implement with actual cut command
        }
        #endregion
    }

    /// <summary>
    /// Event arguments for TCL commands
    /// </summary>
    public class TclCommandEventArgs : EventArgs
    {
        public TclCommand Command { get; }
        public byte[] Data { get; }

        public TclCommandEventArgs(TclCommand command, byte[] data)
        {
            Command = command;
            Data = data;
        }
    }

    /// <summary>
    /// Event arguments for status requests
    /// </summary>
    public class TclStatusRequestEventArgs : EventArgs
    {
        public PrinterStatus Status { get; set; }
    }
    
    /// <summary>
    /// Event arguments for response data ready to send
    /// </summary>
    public class TclResponseEventArgs : EventArgs
    {
        public byte[] Response { get; }
        
        public TclResponseEventArgs(byte[] response)
        {
            Response = response;
        }
    }

    /// <summary>
    /// TCL command types
    /// </summary>
    public enum TclCommandType
    {
        Print,
        Feed,
        Cut,
        Status,
        Reset,
        ClearError,
        SelfTest
    }
    
    /// <summary>
    /// Represents a parsed print template command
    /// </summary>
    public class PrintTemplateCommand
    {
        /// <summary>
        /// Template ID (single character)
        /// </summary>
        public char TemplateId { get; set; }
        
        /// <summary>
        /// Number of copies to print (1-9999)
        /// </summary>
        public int Copies { get; set; }
        
        /// <summary>
        /// Variable data fields for the template
        /// </summary>
        public List<string> PrintFields { get; set; } = new List<string>();
    }
}