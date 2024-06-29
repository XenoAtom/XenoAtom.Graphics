// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System;

namespace XenoAtom.Graphics
{
    /// <summary>
    /// A structure describing several common properties for initializing a <see cref="GraphicsManager"/>.
    /// </summary>
    public struct GraphicsManagerOptions
    {
        /// <summary>
        /// Indicates whether the <see cref="GraphicsManager"/> will support debug features, provided they are supported by the host system.
        /// </summary>
        public bool Debug;

        /// <summary>
        /// Logger used when <see cref="Debug"/> is true. Default is Console.Out.
        /// </summary>
        public DebugLogDelegate DebugLog;

        /// <summary>
        /// Flags used to log messages from the graphics backend. Default is <see cref="DebugLogLevel.Error"/> and <see cref="DebugLogLevel.Warning"/>.
        /// </summary>
        public DebugLogLevel DebugLogLevel;

        /// <summary>
        /// Indicates the kind of debug messages that should be logged. Default is <see cref="DebugLogKind.General"/> and <see cref="DebugLogKind.Validation"/>.
        /// </summary>
        public DebugLogKind DebugLogKind;

        /// <summary>
        /// Initializes a new instance of the <see cref="GraphicsManagerOptions"/> structure. Debug is set to false.
        /// </summary>
        public GraphicsManagerOptions()
        {
            Debug = false;
            DebugLog = LogToConsole;
            DebugLogLevel = DebugLogLevel.Warning | DebugLogLevel.Error;
            DebugLogKind = DebugLogKind.General | DebugLogKind.Validation;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="GraphicsManagerOptions"/> structure. Debug is set to true with default logging to Console.
        /// </summary>
        public GraphicsManagerOptions(bool debug)
        {
            Debug = debug;
            DebugLog = LogToConsole;
            DebugLogLevel = DebugLogLevel.Warning | DebugLogLevel.Error;
            DebugLogKind = DebugLogKind.General | DebugLogKind.Validation;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="GraphicsManagerOptions"/> structure.
        /// </summary>
        /// <param name="debug">A boolean indicating whether the <see cref="GraphicsManager"/> will support debug features.</param>
        /// <param name="debugLog">Logger used when <see cref="Debug"/> is true.</param>
        /// <param name="debugLogLevel">Flags used to log messages from the graphics backend</param>
        /// <param name="debugLogKind">Indicates the kind of debug messages that should be logged.</param>
        public GraphicsManagerOptions(bool debug, DebugLogDelegate debugLog, DebugLogLevel debugLogLevel, DebugLogKind debugLogKind)
        {
            Debug = debug;
            DebugLog = debugLog;
            DebugLogLevel = debugLogLevel;
            DebugLogKind = debugLogKind;
        }

        private static readonly DebugLogDelegate LogToConsole = static (level, kind, message) => Console.WriteLine($"[{level.ToText()}] {kind} - {message}");
    }
}