// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;

namespace Opc.Ua.Client.FileSystem
{
    /// <summary>
    /// File access modes for the OPC UA <c>FileType.Open</c> method,
    /// mirrored as flags so callers don't have to remember the raw
    /// Part 5 bit values.
    /// </summary>
    /// <remarks>
    /// The numeric values match the OPC UA <c>OpenFileMode</c>
    /// enumeration defined in Part 5 §C.4.4.
    /// </remarks>
    [Flags]
    public enum UaFileMode : byte
    {
        /// <summary>
        /// Sentinel value indicating that no mode has been specified.
        /// Passing this to
        /// <see cref="UaFileInfo.OpenAsync(UaFileMode, System.Threading.CancellationToken)"/>
        /// is rejected with <see cref="ArgumentException"/> at
        /// runtime; the value exists so callers can use it as a default
        /// and check explicitly.
        /// </summary>
        None = 0,

        /// <summary>
        /// Open for reading.
        /// </summary>
        Read = 1,

        /// <summary>
        /// Open for writing.
        /// </summary>
        Write = 2,

        /// <summary>
        /// Combine with <see cref="Write"/> to truncate the file on
        /// open.
        /// </summary>
        EraseExisting = 4,

        /// <summary>
        /// Combine with <see cref="Write"/> to position the cursor at
        /// the end of the file on open.
        /// </summary>
        Append = 8,

        /// <summary>
        /// Convenience combination of <see cref="Read"/> and
        /// <see cref="Write"/>.
        /// </summary>
        ReadWrite = Read | Write
    }
}
