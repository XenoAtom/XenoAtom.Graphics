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
    public enum DebugLogFlags
    {
        /// <summary>
        /// No debug log.
        /// </summary>
        None = 0,

        /// <summary>
        /// Log messages that are useful for debugging.
        /// </summary>
        Debug = 1 << 0,

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

        /// <summary>
        /// Log messages that are useful for diagnosing problems.
        /// </summary>
        Performance = 1 << 4,
    }

    /// <summary>
    /// Extensions for <see cref="DebugLogFlags"/>.
    /// </summary>
    public static class DebugLogFlagsExtensions
    {
        /// <summary>
        /// Converts a <see cref="DebugLogFlags"/> to a string.
        /// </summary>
        /// <param name="flags">The flags</param>
        /// <returns>A string representation.</returns>
        public static string ToText(this DebugLogFlags flags)
        {
            switch (flags)
            {
                case DebugLogFlags.None:
                    return "None";
                case DebugLogFlags.Debug:
                    return "Debug";
                case DebugLogFlags.Info:
                    return "Info";
                case DebugLogFlags.Warning:
                    return "Warning";
                case DebugLogFlags.Error:
                    return "Error";
                case DebugLogFlags.Performance:
                    return "Performance";
                default:
                {
                    var builder = new StringBuilder();
                    if ((flags & DebugLogFlags.Debug) != 0)
                    {
                        builder.Append("Debug");
                    }

                    if ((flags & DebugLogFlags.Info) != 0)
                    {
                        if (builder.Length > 0) builder.Append(" | ");
                        builder.Append("Info");
                    }

                    if ((flags & DebugLogFlags.Warning) != 0)
                    {
                        if (builder.Length > 0) builder.Append(" | ");
                        builder.Append("Warning");
                    }

                    if ((flags & DebugLogFlags.Error) != 0)
                    {
                        if (builder.Length > 0) builder.Append(" | ");
                        builder.Append("Error");
                    }

                    if ((flags & DebugLogFlags.Performance) != 0)
                    {
                        if (builder.Length > 0) builder.Append(" | ");
                        builder.Append("Performance");
                    }

                    return builder.ToString();
                }
            }
        }
    }

    /// <summary>
    /// A delegate used to log messages from the graphics backend.
    /// </summary>
    /// <param name="flags">The kind of debug.</param>
    /// <param name="message">The message.</param>
    public delegate void DebugLogDelegate(DebugLogFlags flags, string message);
}