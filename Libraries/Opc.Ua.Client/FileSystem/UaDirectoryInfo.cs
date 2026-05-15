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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client.FileSystem
{
    /// <summary>
    /// Strongly-typed handle to a single OPC UA <c>FileDirectoryType</c>
    /// instance. Mirrors <see cref="System.IO.DirectoryInfo"/>.
    /// </summary>
    public sealed class UaDirectoryInfo : UaFileSystemInfo
    {
        internal UaDirectoryInfo(
            FileSystemClient owner,
            UaDirectoryInfo? parent,
            NodeId nodeId,
            QualifiedName browseName,
            IReadOnlyList<QualifiedName> segments)
            : base(owner, parent, nodeId, browseName, segments)
        {
            Proxy = new FileDirectoryTypeClient(
                owner.Session,
                nodeId,
                owner.Session.MessageContext.Telemetry);
        }

        /// <inheritdoc/>
        public override bool IsDirectory => true;

        /// <summary>
        /// The underlying <see cref="FileDirectoryTypeClient"/> proxy.
        /// </summary>
        public FileDirectoryTypeClient Proxy { get; }

        /// <inheritdoc/>
        public override ValueTask RefreshAsync(CancellationToken ct = default)
        {
            // FileDirectoryType has no scalar properties to refresh; the
            // child-set is inherently lazy via EnumerateAsync.
            return default;
        }

        /// <summary>
        /// Enumerates the immediate children of this directory.
        /// Returns both files (<see cref="UaFileInfo"/>) and
        /// sub-directories (<see cref="UaDirectoryInfo"/>) in browse
        /// order.
        /// </summary>
        public IAsyncEnumerable<UaFileSystemInfo> EnumerateAsync(
            CancellationToken ct = default)
        {
            return Owner.EnumerateChildrenAsync(this, includeFiles: true, includeDirectories: true, ct);
        }

        /// <summary>
        /// Enumerates only the file children of this directory.
        /// </summary>
        public async IAsyncEnumerable<UaFileInfo> EnumerateFilesAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken ct = default)
        {
            await foreach (UaFileSystemInfo child in Owner
                .EnumerateChildrenAsync(this, includeFiles: true, includeDirectories: false, ct)
                .ConfigureAwait(false))
            {
                if (child is UaFileInfo file)
                {
                    yield return file;
                }
            }
        }

        /// <summary>
        /// Enumerates only the directory children of this directory.
        /// </summary>
        public async IAsyncEnumerable<UaDirectoryInfo> EnumerateDirectoriesAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken ct = default)
        {
            await foreach (UaFileSystemInfo child in Owner
                .EnumerateChildrenAsync(this, includeFiles: false, includeDirectories: true, ct)
                .ConfigureAwait(false))
            {
                if (child is UaDirectoryInfo dir)
                {
                    yield return dir;
                }
            }
        }

        /// <summary>
        /// Creates a new sub-directory in this directory.
        /// </summary>
        /// <param name="name">The new directory's leaf name. Must NOT
        /// include a namespace prefix or a path separator.</param>
        /// <param name="ct">Cancellation token.</param>
        public ValueTask<UaDirectoryInfo> CreateSubdirectoryAsync(
            string name,
            CancellationToken ct = default)
        {
            return Owner.CreateDirectoryInAsync(this, name, ct);
        }

        /// <summary>
        /// Creates a new empty file in this directory. The server is
        /// asked NOT to immediately open the file
        /// (<c>requestFileOpen: false</c>); call
        /// <see cref="UaFileInfo.OpenAsync(UaFileMode, CancellationToken)"/>
        /// when you are ready to write.
        /// </summary>
        /// <param name="name">The new file's leaf name. Must NOT
        /// include a namespace prefix or a path separator.</param>
        /// <param name="ct">Cancellation token.</param>
        public ValueTask<UaFileInfo> CreateFileAsync(
            string name,
            CancellationToken ct = default)
        {
            return Owner.CreateFileInAsync(this, name, ct);
        }

        /// <summary>
        /// Deletes this directory. When
        /// <paramref name="recursive"/> is <c>false</c> the directory
        /// must be empty (an <see cref="System.IO.IOException"/> is
        /// thrown otherwise). When <c>true</c> the server's recursive
        /// <c>Delete</c> primitive is invoked exactly once (no
        /// client-side traversal).
        /// </summary>
        public ValueTask DeleteAsync(
            bool recursive,
            CancellationToken ct = default)
        {
            return Owner.DeleteCoreAsync(this, recursive, ct);
        }
    }
}
