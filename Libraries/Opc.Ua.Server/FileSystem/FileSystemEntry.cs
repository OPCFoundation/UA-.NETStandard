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

namespace Opc.Ua.Server.FileSystem
{
    /// <summary>
    /// Metadata describing a single entry in an
    /// <see cref="IFileSystemProvider"/>: either a file or a
    /// directory.
    /// </summary>
    /// <param name="Path">
    /// Provider-relative path (forward-slash separated, never starts with a
    /// slash, never ends with a slash except for the root which is an
    /// empty string).
    /// </param>
    /// <param name="Name">
    /// Display / browse name (the last segment of <see cref="Path"/>).
    /// For the root entry this is the provider's
    /// <see cref="IFileSystemProvider.MountName"/>.
    /// </param>
    /// <param name="IsDirectory">
    /// <c>true</c> when the entry represents a directory, <c>false</c>
    /// for a file.
    /// </param>
    /// <param name="Length">
    /// File size in bytes. <c>0</c> for directories.
    /// </param>
    /// <param name="IsWritable">
    /// <c>true</c> when the entry can be written to / modified /
    /// deleted by the current user.
    /// </param>
    /// <param name="LastModifiedUtc">
    /// Last-write timestamp in UTC.
    /// </param>
    /// <param name="MimeType">
    /// IANA media type (best-effort guess from the file extension is
    /// acceptable). Empty string for directories or unknown types.
    /// </param>
    public readonly record struct FileSystemEntry(
        string Path,
        string Name,
        bool IsDirectory,
        long Length,
        bool IsWritable,
        DateTime LastModifiedUtc,
        string MimeType);
}
