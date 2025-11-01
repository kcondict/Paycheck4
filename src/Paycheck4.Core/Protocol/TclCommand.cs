#region Project Header
/*  ____   __   __ __  ____ _  _ ____ ____ _  _  ___ 
 * (  _ \ /  \ /  /  )/ ___) )( (  __/ ___)/ )( \/ __)
 *  ) __/(  O (  O ) \\___ ) __ () _)\___ \) __ (/(__
 * (__)   \__/ \__/(__)(___\_)(_(__) (____/\_)(_/\___) 
 *
 * Paycheck4 - Nanoptix Printer Protocol Emulator
 * 
 * Copyright (c) 2025, Your Organization. All rights reserved.
 */
#endregion

using System;

namespace Paycheck4.Core.Protocol
{
    /// <summary>
    /// Represents a parsed TCL protocol command
    /// </summary>
    public class TclCommand
    {
        /// <summary>
        /// Gets the raw command string
        /// </summary>
        public string Raw { get; private set; }

        /// <summary>
        /// Gets the command code
        /// </summary>
        public string Code { get; private set; }

        /// <summary>
        /// Gets the command arguments
        /// </summary>
        public string[] Args { get; private set; }

        /// <summary>
        /// Creates a new TclCommand instance
        /// </summary>
        public TclCommand(string raw, string code, string[] args)
        {
            Raw = raw;
            Code = code;
            Args = args;
        }

        /// <summary>
        /// Returns a string representation of the command
        /// </summary>
        public override string ToString() => 
            $"Code={Code}, Args=[{string.Join(",", Args ?? Array.Empty<string>())}]";
    }
}