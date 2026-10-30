/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.XRegistry.Bridge.Sync
{
    /// <summary>
    /// Physical local filesystem qualification. FileShare.None is local ownership,
    /// not a distributed lease. Directory barriers must actually succeed.
    /// </summary>
    /// <remarks>
    /// Construction performs no I/O. This provider accepts only <see cref="LocalFileSystem"/> and physical
    /// <see cref="FileStream"/> instances, flushes file data to disk, and reuses <see cref="DirectoryDurability"/>
    /// for directory barriers. It does not qualify network or distributed storage, or provision private permissions.
    /// </remarks>
    public sealed class LocalXRegistrySyncFileDurability : IXRegistrySyncFileDurability
    {
        /// <inheritdoc/>
        /// <remarks>
        /// Creates the directory if needed and holds its <c>sync.writer</c> file with exclusive sharing.
        /// Disposing the ownership handle closes the file without removing it.
        /// </remarks>
        public async ValueTask<IAsyncDisposable> AcquireWriterAsync(
            IFileSystem fileSystem,
            string directory,
            CancellationToken cancellationToken = default)
        {
            fileSystem.ThrowIfNull(nameof(fileSystem));
            directory.ThrowIfNull(nameof(directory));
            cancellationToken.ThrowIfCancellationRequested();
            if (fileSystem.GetType() != typeof(LocalFileSystem))
            {
                throw new ArgumentException(
                    "This durability provider qualifies only LocalFileSystem.", nameof(fileSystem));
            }
            var parents = new Stack<string>();
            string? current = directory;
            while (current is not null && !Directory.Exists(current))
            {
                string? parent = Path.GetDirectoryName(current);
                if (parent is not null)
                {
                    parents.Push(parent);
                }
                current = parent;
            }
            Directory.CreateDirectory(directory);
            foreach (string parent in parents)
            {
                DirectoryDurability.Flush(parent);
            }
            FileStream? stream = new(
                Path.Combine(directory, "sync.writer"),
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                1,
                FileOptions.Asynchronous);
            try
            {
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                var ownership = new Ownership(stream);
                stream = null;
                return ownership;
            }
            finally
            {
                stream?.Dispose();
            }
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Requires a physical <see cref="FileStream"/> and flushes it to disk after its asynchronous flush.
        /// The final disk barrier is synchronous and is not canceled once started.
        /// </remarks>
        public async ValueTask FlushFileAsync(Stream stream, CancellationToken cancellationToken = default)
        {
            stream.ThrowIfNull(nameof(stream));
            if (stream is not FileStream file)
            {
                throw new IOException("A physical file is required for a durable synchronization commit.");
            }
            await file.FlushAsync(cancellationToken).ConfigureAwait(false);
            file.Flush(flushToDisk: true);
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Uses the shared <see cref="DirectoryDurability"/> barrier, checking cancellation before entering it.
        /// Barrier failures are propagated rather than treated as durable success.
        /// </remarks>
        public ValueTask FlushDirectoryAsync(string directory, CancellationToken cancellationToken = default)
        {
            directory.ThrowIfNull(nameof(directory));
            cancellationToken.ThrowIfCancellationRequested();
            DirectoryDurability.Flush(directory);
            return default;
        }

        private sealed class Ownership(FileStream stream) : IAsyncDisposable
        {
            public ValueTask DisposeAsync()
            {
                stream.Dispose();
                return default;
            }
        }
    }
}
