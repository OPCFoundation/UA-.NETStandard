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

using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server.FileSystem
{
    /// <summary>
    /// Async storage abstraction used by
    /// <see cref="FileSystemNodeManager"/> to expose the OPC UA
    /// Part 5 §C / Part 20 §4 <c>FileType</c> /
    /// <c>FileDirectoryType</c> companion model over an arbitrary
    /// backing store.
    /// </summary>
    /// <remarks>
    /// <para>
    /// All paths are <strong>forward-slash separated, provider-relative</strong>
    /// strings, with the empty string and <c>"/"</c> both denoting the
    /// root. Implementations are responsible for translating these
    /// portable paths to whatever the backing store understands
    /// (native filesystem paths, blob keys, in-memory tree keys …)
    /// and for rejecting path-traversal attempts (e.g. <c>".."</c>
    /// segments that escape the mount root).
    /// </para>
    /// <para>
    /// Implementations should be safe for concurrent calls; the node
    /// manager does not serialise access on its side. Streams returned
    /// from <see cref="OpenReadAsync"/> and <see cref="OpenWriteAsync"/>
    /// are <em>owned by the caller</em> (the node manager closes them
    /// when the corresponding OPC UA file handle is closed or the
    /// server shuts down).
    /// </para>
    /// </remarks>
    public interface IFileSystemProvider
    {
        /// <summary>
        /// Display name surfaced as the <c>BrowseName</c> of the root
        /// mount node in the server's address space. Must be a valid
        /// OPC UA name (non-null, non-empty, unique among providers
        /// mounted in the same server).
        /// </summary>
        string MountName { get; }

        /// <summary>
        /// <c>true</c> if the provider permits create / write / delete
        /// operations. Read-only providers report <c>false</c> here so
        /// the node manager can decline write requests with
        /// <see cref="StatusCodes.BadUserAccessDenied"/> without
        /// round-tripping to the backend.
        /// </summary>
        bool IsWritable { get; }

        /// <summary>
        /// Returns metadata about the file or directory at
        /// <paramref name="path"/>, or <c>null</c> when the path
        /// resolves to nothing.
        /// </summary>
        ValueTask<FileSystemEntry?> GetEntryAsync(string path, CancellationToken ct);

        /// <summary>
        /// Enumerates the immediate children (files + directories) of
        /// the directory at <paramref name="path"/>. Throws
        /// <see cref="DirectoryNotFoundException"/> when
        /// <paramref name="path"/> refers to a missing or non-directory
        /// node.
        /// </summary>
        IAsyncEnumerable<FileSystemEntry> EnumerateAsync(string path, CancellationToken ct);

        /// <summary>
        /// Opens the file at <paramref name="path"/> for reading.
        /// </summary>
        /// <exception cref="FileNotFoundException">If the file does not exist.</exception>
        ValueTask<Stream> OpenReadAsync(string path, CancellationToken ct);

        /// <summary>
        /// Opens the file at <paramref name="path"/> for writing.
        /// </summary>
        /// <param name="path">Provider-relative file path.</param>
        /// <param name="mode">How the file should be opened / created.</param>
        /// <param name="ct">Cancellation token.</param>
        ValueTask<Stream> OpenWriteAsync(string path, FileWriteMode mode, CancellationToken ct);

        /// <summary>
        /// Creates a directory (and any missing parents) at
        /// <paramref name="path"/>. No-op when the directory already
        /// exists. Throws <see cref="IOException"/> when a file
        /// already occupies the slot.
        /// </summary>
        ValueTask CreateDirectoryAsync(string path, CancellationToken ct);

        /// <summary>
        /// Creates an empty file at <paramref name="path"/>. Throws
        /// <see cref="IOException"/> when the path already exists
        /// (either as a file or a directory).
        /// </summary>
        ValueTask CreateFileAsync(string path, CancellationToken ct);

        /// <summary>
        /// Deletes the file or directory at <paramref name="path"/>.
        /// Directories are deleted recursively. Throws
        /// <see cref="FileNotFoundException"/> /
        /// <see cref="DirectoryNotFoundException"/> when the path
        /// does not exist.
        /// </summary>
        ValueTask DeleteAsync(string path, CancellationToken ct);

        /// <summary>
        /// Moves (renames) <paramref name="source"/> to
        /// <paramref name="target"/>. Both arguments are
        /// provider-relative paths. Throws
        /// <see cref="IOException"/> on collision.
        /// </summary>
        ValueTask MoveAsync(string source, string target, CancellationToken ct);

        /// <summary>
        /// Recursively copies <paramref name="source"/> to
        /// <paramref name="target"/>. Both arguments are
        /// provider-relative paths. Throws
        /// <see cref="IOException"/> on collision.
        /// </summary>
        ValueTask CopyAsync(string source, string target, CancellationToken ct);
    }
}
