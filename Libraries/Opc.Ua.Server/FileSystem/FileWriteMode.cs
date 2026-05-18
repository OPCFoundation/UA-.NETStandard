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

namespace Opc.Ua.Server.FileSystem
{
    /// <summary>
    /// How <see cref="IFileSystemProvider.OpenWriteAsync"/> should open
    /// the target file. Matches the OPC UA Part 5 §C
    /// <c>FileType.Open</c> mode bits but without the read flag.
    /// </summary>
    public enum FileWriteMode
    {
        /// <summary>
        /// Opens the file for writing; creates it if it doesn't
        /// exist; preserves existing content (the caller then issues
        /// <c>SetPosition</c> / <c>Write</c> to put data wherever it
        /// wants).
        /// </summary>
        OpenOrCreate = 0,

        /// <summary>
        /// Creates the file (truncating any existing content) and
        /// opens it for writing. Equivalent to the
        /// <c>FileType.Open</c> "Erase" bit.
        /// </summary>
        Truncate = 1,

        /// <summary>
        /// Opens the file for writing with the position pre-set to
        /// the end of the file. Equivalent to the
        /// <c>FileType.Open</c> "Append" bit.
        /// </summary>
        Append = 2
    }
}
