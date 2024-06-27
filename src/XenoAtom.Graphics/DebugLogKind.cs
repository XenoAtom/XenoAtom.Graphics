// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

namespace XenoAtom.Graphics
{
    public enum DebugLogKind
    {
        /// <summary>
        /// Log messages that are useful for debugging.
        /// </summary>
        Debug,

        /// <summary>
        /// Log messages that are useful for diagnosing problems.
        /// </summary>
        Info,

        /// <summary>
        /// Log messages that are useful for diagnosing problems.
        /// </summary>
        Warning,

        /// <summary>
        /// Log messages that are useful for diagnosing problems.
        /// </summary>
        Error,

        /// <summary>
        /// Log messages that are useful for diagnosing problems.
        /// </summary>
        Performance,
    }

    /// <summary>
    /// A delegate used to log messages from the graphics backend.
    /// </summary>
    /// <param name="kind">The kind of debug.</param>
    /// <param name="message">The message.</param>
    public delegate void DebugLogDelegate(DebugLogKind kind, string message);
}