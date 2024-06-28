// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System;
using System.Text;

namespace XenoAtom.Graphics
{
    /// <summary>
    /// Flags used to log messages from the graphics backend.
    /// </summary>
    [Flags]
    public enum DebugLogLevel
    {
        /// <summary>
        /// No debug log.
        /// </summary>
        None = 0,

        /// <summary>
        /// Log messages with verbose level.
        /// </summary>
        Verbose = 1 << 0,

        /// <summary>
        /// Log messages that are useful for diagnosing problems.
        /// </summary>
        Info = 1 << 1,

        /// <summary>
        /// Log messages that are useful for diagnosing problems.
        /// </summary>
        Warning = 1 << 2,

        /// <summary>
        /// Log messages that are useful for diagnosing problems.
        /// </summary>
        Error = 1 << 3,
    }

    [Flags]
    public enum DebugLogKind
    {
        /// <summary>
        /// No selection.
        /// </summary>
        None = 0,

        /// <summary>
        /// Specifies that some general event has occurred. This is typically a non-specification, non-performance event.
        /// </summary>
        General = 1 << 0,

        /// <summary>
        /// Specifies that something has occurred during validation against the Vulkan specification that may indicate invalid behavior.
        /// </summary>
        Validation = 1 << 1,

        /// <summary>
        /// Specifies a potentially non-optimal use of Vulkan
        /// </summary>
        Performance = 1 << 2,

        /// <summary>
        /// All debug log kinds.
        /// </summary>
        All = General | Validation | Performance
    }
    
    /// <summary>
    /// Extensions for <see cref="DebugLogLevel"/>.
    /// </summary>
    public static class DebugLogLevelExtensions
    {
        /// <summary>
        /// Converts a <see cref="DebugLogLevel"/> to a string.
        /// </summary>
        /// <param name="debugLogLevel">The flags</param>
        /// <returns>A string representation.</returns>
        public static string ToText(this DebugLogLevel debugLogLevel)
        {
            switch (debugLogLevel)
            {
                case DebugLogLevel.None:
                    return "None";
                case DebugLogLevel.Verbose:
                    return "Verbose";
                case DebugLogLevel.Info:
                    return "Info";
                case DebugLogLevel.Warning:
                    return "Warning";
                case DebugLogLevel.Error:
                    return "Error";
                default:
                {
                    var builder = new StringBuilder();
                    if ((debugLogLevel & DebugLogLevel.Verbose) != 0)
                    {
                        builder.Append("Verbose");
                    }

                    if ((debugLogLevel & DebugLogLevel.Info) != 0)
                    {
                        if (builder.Length > 0) builder.Append(" | ");
                        builder.Append("Info");
                    }

                    if ((debugLogLevel & DebugLogLevel.Warning) != 0)
                    {
                        if (builder.Length > 0) builder.Append(" | ");
                        builder.Append("Warning");
                    }

                    if ((debugLogLevel & DebugLogLevel.Error) != 0)
                    {
                        if (builder.Length > 0) builder.Append(" | ");
                        builder.Append("Error");
                    }

                    return builder.ToString();
                }
            }
        }
    }

    /// <summary>
    /// A delegate used to log messages from the graphics backend.
    /// </summary>
    /// <param name="debugLogLevel">The kind of debug.</param>
    /// <param name="message">The message.</param>
    public delegate void DebugLogDelegate(DebugLogLevel debugLogLevel, DebugLogKind debugLogKind, string message);
}