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

using System.IO;

namespace Opc.Ua
{
    /// <summary>
    /// A deny-all file system that never resolves any path. It is used when a
    /// component must load resources exclusively from an in-memory table and
    /// must never touch the local file system - for example when validating a
    /// type dictionary received from an untrusted network peer, where an
    /// attacker-supplied import location (such as a UNC path) would otherwise
    /// trigger an outbound SMB connection or arbitrary local file access.
    /// </summary>
    public sealed class NullFileSystem : IFileSystem
    {
        /// <summary>
        /// Get singleton instance
        /// </summary>
        public static readonly NullFileSystem Instance = new();

        /// <inheritdoc/>
        public bool Exists(string path, bool isDirectory)
        {
            return false;
        }

        /// <inheritdoc/>
        public void Delete(string path, bool isDirectory)
        {
            throw new FileNotFoundException(
                "The null file system does not provide access to any path.", path);
        }

        /// <inheritdoc/>
        public Stream OpenRead(string path)
        {
            throw new FileNotFoundException(
                "The null file system does not provide access to any path.", path);
        }

        /// <inheritdoc/>
        public Stream OpenWrite(string path)
        {
            throw new FileNotFoundException(
                "The null file system does not provide access to any path.", path);
        }

        /// <inheritdoc/>
        public System.DateTime GetLastWriteTime(string path)
        {
            throw new FileNotFoundException(
                "The null file system does not provide access to any path.", path);
        }

        /// <inheritdoc/>
        public long GetLength(string path)
        {
            throw new FileNotFoundException(
                "The null file system does not provide access to any path.", path);
        }
    }
}
