/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

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
        /// is rejected with <see cref="System.ArgumentException"/> at
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
