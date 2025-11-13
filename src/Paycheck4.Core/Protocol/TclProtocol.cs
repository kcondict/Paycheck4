using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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
        private bool _isRunning = false;
        private int _statusReportingInterval = 5000;
        
        // Message buffering for handling partial messages
        private readonly System.Text.StringBuilder _messageBuffer = new System.Text.StringBuilder();
        private DateTime _lastReceiveTime = DateTime.MinValue;
        private const int MessageTimeoutMs = 10;
        
        // Print state machine
        private PrintState _currentPrintState = PrintState.IdleTOF;
        private System.Threading.Timer? _printStateTimer;
        private readonly int _printStartDelayInterval;
        private readonly int _validationDelayInterval;
        private readonly int _busyStateChangeInterval;
        private readonly int _tofStateChangeInterval;
        private char _currentTemplateId = ' '; // Current template being processed (0x20 until first print command)
        
        // PaperInChute flag simulation
        private System.Threading.Timer? _paperInChuteTimer;
        private readonly int _paperInChuteSetInterval;
        private readonly int _paperInChuteClearInterval;
        
        // Extended status data
        private byte _unitAddress = 0x00;
        private string _softwareVersion = "PAY-6.22B";
        private byte _statusFlags1 = (byte)StatusFlag1.Unmask;
        private byte _statusFlags2 = (byte)StatusFlag2.Unmask;
        private byte _statusFlags3 = (byte)StatusFlag3.Unmask;
        private byte _statusFlags4 = (byte)StatusFlag4.Unmask;
        private byte _statusFlags5 = (byte)(StatusFlag5.Unmask | StatusFlag5.ValidationDone | StatusFlag5.AtTopOfForm | StatusFlag5.ResetPowerUp);
        
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
        public TclProtocol(
            ILogger<TclProtocol>? logger = null, 
            int statusReportingInterval = 2000,
            int printStartDelayInterval = 3000,
            int validationDelayInterval = 18000,
            int busyStateChangeInterval = 20000,
            int tofStateChangeInterval = 4000,
            int paperInChuteSetInterval = 2000,
            int paperInChuteClearInterval = 3000)
        {
            _logger = logger;
            _statusReportingInterval = statusReportingInterval;
            _printStartDelayInterval = printStartDelayInterval;
            _validationDelayInterval = validationDelayInterval;
            _busyStateChangeInterval = busyStateChangeInterval;
            _tofStateChangeInterval = tofStateChangeInterval;
            _paperInChuteSetInterval = paperInChuteSetInterval;
            _paperInChuteClearInterval = paperInChuteClearInterval;
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
        
        /// <summary>
        /// Print state machine states
        /// </summary>
        private enum PrintState
        {
            IdleTOF,
            BusyNotTOFValClear,
            BusyValDone,
            IdleNotTOF
        }
        #endregion

        #region Message Processing
        /// <summary>
        /// Process incoming data from the host
        /// </summary>
        public void ProcessIncomingData(byte[] data, int offset, int count)
        {
            // Append raw bytes to buffer by decoding in-place
            for (int i = 0; i < count; i++)
            {
                _messageBuffer.Append((char)data[offset + i]);
            }
            _lastReceiveTime = DateTime.UtcNow;
            
            // Try to extract complete messages (start with '^' and end with '^')
            ProcessBufferedMessages();
        }
        
        /// <summary>
        /// Process complete messages from the buffer
        /// </summary>
        private void ProcessBufferedMessages()
        {
            int lastCompleteMessageStart = -1;
            int lastCompleteMessageEnd = -1;
            int discardedMessageCount = 0;
            
            // First pass: find ALL complete messages and only keep the last one
            int searchStart = 0;
            while (searchStart < _messageBuffer.Length)
            {
                var bufferLength = _messageBuffer.Length;
                
                // Find start of message (must be '^' for valid TCL commands)
                int startIndex = -1;
                for (int i = searchStart; i < bufferLength; i++)
                {
                    if (_messageBuffer[i] == '^')
                    {
                        startIndex = i;
                        break;
                    }
                }
                
                if (startIndex == -1)
                    break;
                
                // Find end of message (second '^')
                int endIndex = -1;
                for (int i = startIndex + 1; i < bufferLength; i++)
                {
                    if (_messageBuffer[i] == '^')
                    {
                        endIndex = i;
                        break;
                    }
                }
                
                if (endIndex == -1)
                {
                    // Incomplete message at end - will handle below
                    break;
                }
                
                // Found a complete message
                if (lastCompleteMessageStart != -1)
                {
                    // We already had a complete message, this means we're discarding the previous one
                    discardedMessageCount++;
                }
                
                lastCompleteMessageStart = startIndex;
                lastCompleteMessageEnd = endIndex;
                searchStart = endIndex + 1;
            }
            
            // Handle the results
            if (lastCompleteMessageStart != -1 && lastCompleteMessageEnd != -1)
            {
                // We have at least one complete message - process only the last one
                if (discardedMessageCount > 0)
                {
                    _logger?.LogWarning("Discarding {Count} old command message(s), processing only the most recent", discardedMessageCount);
                }
                
                // Log complete message (only allocation here is for logging)
                if (_logger != null && _logger.IsEnabled(LogLevel.Information))
                {
                    var messageLength = lastCompleteMessageEnd - lastCompleteMessageStart + 1;
                    var message = _messageBuffer.ToString(lastCompleteMessageStart, messageLength);
                    _logger.LogInformation("Received complete message from host: {Message}", message);
                }
                
                // Process the last complete message
                ProcessCompleteMessage(lastCompleteMessageStart, lastCompleteMessageEnd);
                
                // Remove everything up to and including the last processed message
                // This preserves any incomplete message that may be after it
                _messageBuffer.Remove(0, lastCompleteMessageEnd + 1);
                
                // Reset the receive time for the remaining buffer (incomplete message)
                if (_messageBuffer.Length > 0)
                {
                    _lastReceiveTime = DateTime.UtcNow;
                }
            }
            else
            {
                // No complete messages found
                var bufferLength = _messageBuffer.Length;
                
                if (bufferLength > 0)
                {
                    // Find if there's a start marker (^)
                    int startIndex = -1;
                    for (int i = 0; i < bufferLength; i++)
                    {
                        if (_messageBuffer[i] == '^')
                        {
                            startIndex = i;
                            break;
                        }
                    }
                    
                    // Check if timeout has expired
                    var timeSinceLastReceive = (DateTime.UtcNow - _lastReceiveTime).TotalMilliseconds;
                    var isTimeout = timeSinceLastReceive > MessageTimeoutMs;
                    
                    if (startIndex > 0)
                    {
                        // Remove junk before the start marker (log immediately as error)
                        if (_logger != null && _logger.IsEnabled(LogLevel.Warning))
                        {
                            var junkData = _messageBuffer.ToString(0, startIndex);
                            _logger.LogWarning("Discarding {Length} bytes of incorrectly formatted data before valid command marker: {Data}", startIndex, junkData);
                        }
                        _messageBuffer.Remove(0, startIndex);
                    }
                    else if (startIndex == -1)
                    {
                        // No ^ found at all - this is invalid data (possibly echo response)
                        // Log immediately at warning level and discard
                        if (_logger != null && _logger.IsEnabled(LogLevel.Warning))
                        {
                            var invalidData = _messageBuffer.ToString();
                            _logger.LogWarning("Received data without valid command marker ('^') - discarding {Length} bytes of incorrectly formatted data (waited {Time}ms): {Data}", 
                                bufferLength, timeSinceLastReceive, invalidData);
                        }
                        _messageBuffer.Clear();
                    }
                    else if (isTimeout)
                    {
                        // Has ^ at position 0 but incomplete message and timeout expired
                        _logger?.LogError("Incomplete message received (timeout after {Time}ms) - discarding {Length} bytes", timeSinceLastReceive, bufferLength);
                        _messageBuffer.Clear();
                    }
                }
            }
        }
        
        /// <summary>
        /// Process a complete TCL message using buffer indices (no string allocation)
        /// </summary>
        private void ProcessCompleteMessage(int startIndex, int endIndex)
        {
            var messageLength = endIndex - startIndex + 1;
            
            // All valid TCL commands start with '^' and end with '^'
            if (_messageBuffer[startIndex] != '^' || _messageBuffer[endIndex] != '^')
            {
                // This is likely an echo of our status response (starts with '*') or other invalid data
                if (_logger != null && _logger.IsEnabled(LogLevel.Warning))
                {
                    var message = _messageBuffer.ToString(startIndex, messageLength);
                    _logger.LogWarning("Received incorrectly formatted message (expected '^...^' format): {Message}", message);
                }
                return;
            }
            
            // Check message type by examining buffer directly
            
            // Pattern: ^P|... for print command
            if (messageLength >= 5 && 
                _messageBuffer[startIndex + 1] == 'P' &&
                _messageBuffer[startIndex + 2] == '|' &&
                _messageBuffer[endIndex - 1] == '|')
            {
                var message = _messageBuffer.ToString(startIndex, messageLength);
                var printCommand = ParsePrintTemplateCommand(message);
                if (printCommand != null)
                {
                    _logger?.LogInformation("Print command detected: Template={Template}, Copies={Copies}, Fields={FieldCount}",
                        printCommand.TemplateId, printCommand.Copies, printCommand.PrintFields.Count);
                    StartPrintJob(printCommand.TemplateId);
                }
                return;
            }
            
            // Pattern: ^S|^ for status request
            if (messageLength == 4 &&
                _messageBuffer[startIndex + 1] == 'S' &&
                _messageBuffer[startIndex + 2] == '|')
            {
                _logger?.LogInformation("Status request command received");
                HandleStatusRequest();
                return;
            }
            
            // Pattern: ^Se|^ for extended status request
            if (messageLength == 5 &&
                _messageBuffer[startIndex + 1] == 'S' &&
                _messageBuffer[startIndex + 2] == 'e' &&
                _messageBuffer[startIndex + 3] == '|')
            {
                _logger?.LogInformation("Extended status request command received");
                HandleExtendedStatusRequest();
                return;
            }
            
            // Pattern: ^C|^ for clear software error flags
            if (messageLength == 4 &&
                _messageBuffer[startIndex + 1] == 'C' &&
                _messageBuffer[startIndex + 2] == '|')
            {
                _logger?.LogInformation("Clear error flags command received");
                HandleClearErrorFlags();
                return;
            }
            
            // Unknown/unrecognized command - log error
            if (_logger != null && _logger.IsEnabled(LogLevel.Error))
            {
                var message = _messageBuffer.ToString(startIndex, messageLength);
                _logger.LogError("Unrecognized command received: {Message}", message);
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

        #region Print State Machine
        /// <summary>
        /// Starts the print job state machine
        /// </summary>
        private void StartPrintJob(char templateId)
        {
            if (_currentPrintState != PrintState.IdleTOF)
            {
                _logger?.LogError("Print command received while printer is busy (State: {State}). Ignoring command.", _currentPrintState);
                return;
            }
            
            // Check if we already have a timer running (print job already started but not yet transitioned)
            if (_printStateTimer != null)
            {
                _logger?.LogError("Print command received while previous print job is still initializing. Ignoring command.");
                return;
            }
            
            // Save the template ID for use in status responses
            _currentTemplateId = templateId;
            
            _logger?.LogInformation("Starting print job for template {Template} - transitioning from IdleTOF, starting PrintStartDelayTimer ({Interval}ms)", templateId, _printStartDelayInterval);
            
            // Start the print start delay timer
            _printStateTimer = new System.Threading.Timer(
                _ => TransitionToBusyNotTOFValClear(),
                null,
                _printStartDelayInterval,
                Timeout.Infinite);
        }
        
        /// <summary>
        /// Transition to BusyNotTOFValClear state
        /// </summary>
        private void TransitionToBusyNotTOFValClear()
        {
            _currentPrintState = PrintState.BusyNotTOFValClear;
            
            // Set Busy flag, Clear ValidationDone and AtTopOfForm flags
            _statusFlags1 |= (byte)StatusFlag1.Busy;
            _statusFlags5 &= (byte)~StatusFlag5.ValidationDone;
            _statusFlags5 &= (byte)~StatusFlag5.AtTopOfForm;
            
            _logger?.LogInformation("Transitioned to BusyNotTOFValClear - Busy=SET, ValidationDone=CLEAR, AtTopOfForm=CLEAR. Starting ValidationDelayTimer ({Interval}ms)", _validationDelayInterval);
            
            // Start validation delay timer
            _printStateTimer?.Dispose();
            _printStateTimer = new System.Threading.Timer(
                _ => TransitionToBusyValDone(),
                null,
                _validationDelayInterval,
                Timeout.Infinite);
        }
        
        /// <summary>
        /// Transition to BusyValDone state
        /// </summary>
        private void TransitionToBusyValDone()
        {
            _currentPrintState = PrintState.BusyValDone;
            
            // Set ValidationDone flag
            _statusFlags5 |= (byte)StatusFlag5.ValidationDone;
            
            _logger?.LogInformation("Transitioned to BusyValDone - ValidationDone=SET. Starting BusyStateChangeTimer ({Interval}ms)", _busyStateChangeInterval);
            
            // Start (or restart) the PaperInChute flag simulation
            _paperInChuteTimer?.Dispose();
            _paperInChuteTimer = new System.Threading.Timer(
                _ => SetPaperInChute(),
                null,
                _paperInChuteSetInterval,
                Timeout.Infinite);
            _logger?.LogInformation("Starting PaperInChuteSetTimer ({Interval}ms)", _paperInChuteSetInterval);
            
            // Start busy state change timer
            _printStateTimer?.Dispose();
            _printStateTimer = new System.Threading.Timer(
                _ => TransitionToIdleNotTOF(),
                null,
                _busyStateChangeInterval,
                Timeout.Infinite);
        }
        
        /// <summary>
        /// Transition to IdleNotTOF state
        /// </summary>
        private void TransitionToIdleNotTOF()
        {
            _currentPrintState = PrintState.IdleNotTOF;
            
            // Clear Busy flag
            _statusFlags1 &= (byte)~StatusFlag1.Busy;
            
            _logger?.LogInformation("Transitioned to IdleNotTOF - Busy=CLEAR. Starting AtTOFTimer ({Interval}ms)", _tofStateChangeInterval);
            
            // Start TOF timer
            _printStateTimer?.Dispose();
            _printStateTimer = new System.Threading.Timer(
                _ => TransitionToIdleTOF(),
                null,
                _tofStateChangeInterval,
                Timeout.Infinite);
        }
        
        /// <summary>
        /// Transition to IdleTOF state
        /// </summary>
        private void TransitionToIdleTOF()
        {
            _currentPrintState = PrintState.IdleTOF;
            
            // Set AtTopOfForm flag
            _statusFlags5 |= (byte)StatusFlag5.AtTopOfForm;
            
            _logger?.LogInformation("Transitioned to IdleTOF - AtTopOfForm=SET. Print job complete, ready for next command.");
            
            // Dispose timer (no next transition)
            _printStateTimer?.Dispose();
            _printStateTimer = null;
        }
        #endregion

        #region PaperInChute Flag Simulation
        /// <summary>
        /// Sets the PaperInChute flag and starts the clear timer
        /// </summary>
        private void SetPaperInChute()
        {
            // Set the PaperInChute bit
            _statusFlags3 |= (byte)StatusFlag3.PaperInChute;
            
            _logger?.LogInformation("PaperInChute flag SET. Starting PaperInChuteClearTimer ({Interval}ms)", _paperInChuteClearInterval);
            
            // Start the clear timer
            _paperInChuteTimer?.Dispose();
            _paperInChuteTimer = new System.Threading.Timer(
                _ => ClearPaperInChute(),
                null,
                _paperInChuteClearInterval,
                Timeout.Infinite);
        }
        
        /// <summary>
        /// Clears the PaperInChute flag
        /// </summary>
        private void ClearPaperInChute()
        {
            // Clear the PaperInChute bit
            _statusFlags3 &= (byte)~StatusFlag3.PaperInChute;
            
            _logger?.LogInformation("PaperInChute flag CLEARED");
            
            // Dispose timer
            _paperInChuteTimer?.Dispose();
            _paperInChuteTimer = null;
        }
        #endregion

        #region Command Handlers
        /// <summary>
        /// Handles a status request from the host
        /// </summary>
        private void HandleStatusRequest()
        {
            _logger?.LogInformation("Handling explicit status request from host");
            var response = BuildExtendedStatusResponse();
            ResponseReady?.Invoke(this, new TclResponseEventArgs(response));
        }
        
        /// <summary>
        /// Handles an extended status request from the host
        /// </summary>
        private void HandleExtendedStatusRequest()
        {
            _logger?.LogInformation("Handling extended status request from host");
            // Same as regular status request - send extended status
            var response = BuildExtendedStatusResponse();
            ResponseReady?.Invoke(this, new TclResponseEventArgs(response));
        }
        
        /// <summary>
        /// Handles clear error flags command from the host
        /// </summary>
        private void HandleClearErrorFlags()
        {
            _logger?.LogInformation("Clearing software error flags");
            
            // TODO: Implement clearing of error flags
            // For now, just log that command was received
            // Future: Clear specific error bits in status flags
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
            // Don't send status yet - wait for Start()
            _logger?.LogInformation("TCL protocol initialized - status broadcasting will begin when started");
        }
        
        /// <summary>
        /// Starts the TCL protocol handler and begins status broadcasting
        /// </summary>
        public void Start()
        {
            if (_isRunning)
                return;
                
            _isRunning = true;
            _logger?.LogInformation("Starting TCL protocol - beginning status broadcast");
            
            // Send initial status immediately
            SendExtendedStatusResponse();
            
            // Send status every StatusReportingInterval milliseconds continuously
            _ = Task.Run(async () =>
            {
                while (_isRunning)
                {
                    await Task.Delay(_statusReportingInterval);
                    if (_isRunning)
                    {
                        _logger?.LogInformation("Sending periodic extended status");
                        SendExtendedStatusResponse();
                    }
                }
            });
        }
        
        /// <summary>
        /// Stops the TCL protocol handler
        /// </summary>
        public void Stop()
        {
            _isRunning = false;
            _logger?.LogInformation("TCL protocol stopped - status broadcasting halted");
        }

        /// <summary>
        /// Process incoming data from USB device
        /// </summary>
        public void ProcessData(byte[] data, int offset, int count)
        {
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
            
            // Template number and end delimiter (ASCII)
            response.AddRange(Encoding.ASCII.GetBytes($"|P{_currentTemplateId}|*"));
            
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