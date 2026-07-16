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
    /// Common base for <see cref="UaFileInfo"/> and
    /// <see cref="UaDirectoryInfo"/>; mirrors
    /// <see cref="System.IO.FileSystemInfo"/>.
    /// </summary>
    public abstract class UaFileSystemInfo
    {
        internal UaFileSystemInfo(
            FileSystemClient owner,
            UaDirectoryInfo? parent,
            NodeId nodeId,
            QualifiedName browseName,
            IReadOnlyList<QualifiedName> segments)
        {
            Owner = owner ?? throw new ArgumentNullException(nameof(owner));
            Parent = parent;
            if (nodeId.IsNull)
            {
                throw new ArgumentNullException(nameof(nodeId));
            }
            NodeId = nodeId;
            BrowseName = browseName;
            Segments = segments;
        }

        /// <summary>
        /// The <see cref="FileSystemClient"/> that produced this info
        /// object. All operations that need to call back to the server
        /// route through this owner.
        /// </summary>
        public FileSystemClient Owner { get; }

        /// <summary>
        /// The parent directory; <c>null</c> only for the root.
        /// </summary>
        public UaDirectoryInfo? Parent { get; }

        /// <summary>
        /// The OPC UA <see cref="NodeId"/> of the underlying object.
        /// </summary>
        public NodeId NodeId { get; }

        /// <summary>
        /// The OPC UA <see cref="QualifiedName"/> of the underlying
        /// object.
        /// </summary>
        public QualifiedName BrowseName { get; }

        /// <summary>
        /// The unqualified leaf name of this file or directory.
        /// </summary>
        /// <remarks>
        /// This property mirrors
        /// <see cref="System.IO.FileSystemInfo.Name"/>: it discards the
        /// namespace prefix. Use <see cref="FullPath"/> when round-trip
        /// fidelity matters.
        /// </remarks>
        public string Name => BrowseName.Name ?? string.Empty;

        /// <summary>
        /// The canonical, namespace-aware path of this object relative
        /// to the <see cref="FileSystemClient"/> root, in
        /// <see cref="UaPath"/> form (always begins with <c>'/'</c>).
        /// </summary>
        public string FullPath => UaPath.Format(Segments);

        /// <summary>
        /// The path segments leading to this object.
        /// </summary>
        public IReadOnlyList<QualifiedName> Segments { get; }

        /// <summary>
        /// <c>true</c> for <see cref="UaDirectoryInfo"/>; <c>false</c>
        /// for <see cref="UaFileInfo"/>.
        /// </summary>
        public abstract bool IsDirectory { get; }

        /// <summary>
        /// Re-reads metadata from the server. Subclasses populate type-
        /// specific properties.
        /// </summary>
        public abstract ValueTask RefreshAsync(CancellationToken ct = default);

        /// <summary>
        /// Deletes this file or empty directory. For non-empty
        /// directories, use <see cref="UaDirectoryInfo.DeleteAsync"/>
        /// with <c>recursive: true</c>.
        /// </summary>
        public virtual ValueTask DeleteAsync(CancellationToken ct = default)
        {
            return Owner.DeleteCoreAsync(this, recursive: false, ct);
        }

        /// <summary>
        /// Moves this object to <paramref name="destPath"/>. The
        /// destination's parent directory must exist.
        /// </summary>
        public ValueTask<UaFileSystemInfo> MoveToAsync(
            string destPath,
            CancellationToken ct = default)
        {
            return Owner.MoveOrCopyAsync(this, destPath, copy: false, ct);
        }

        /// <summary>
        /// Moves this object into <paramref name="destinationDirectory"/>,
        /// optionally renaming it via <paramref name="newName"/>.
        /// </summary>
        public ValueTask<UaFileSystemInfo> MoveToAsync(
            UaDirectoryInfo destinationDirectory,
            string? newName = null,
            CancellationToken ct = default)
        {
            return Owner.MoveOrCopyAsync(
                this,
                destinationDirectory,
                newName ?? Name,
                copy: false,
                ct);
        }

        /// <summary>
        /// Copies this object to <paramref name="destPath"/>.
        /// </summary>
        public ValueTask<UaFileSystemInfo> CopyToAsync(
            string destPath,
            CancellationToken ct = default)
        {
            return Owner.MoveOrCopyAsync(this, destPath, copy: true, ct);
        }

        /// <summary>
        /// Copies this object into
        /// <paramref name="destinationDirectory"/>, optionally renaming
        /// it via <paramref name="newName"/>.
        /// </summary>
        public ValueTask<UaFileSystemInfo> CopyToAsync(
            UaDirectoryInfo destinationDirectory,
            string? newName = null,
            CancellationToken ct = default)
        {
            return Owner.MoveOrCopyAsync(
                this,
                destinationDirectory,
                newName ?? Name,
                copy: true,
                ct);
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return FullPath;
        }
    }
}
