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
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using UaLens.Plugins.Companions.Providers;

namespace UaLens.Plugins.Companions
{
    /// <summary>
    /// Publishes one bounded local file without overwriting an existing destination.
    /// </summary>
    internal static class CompanionLocalExport
    {
        /// <summary>
        /// Writes to an owned pending file and checks freshness immediately before publication.
        /// </summary>
        public static async Task WriteAsync(
            string destination, ByteString content, Action requireCurrent, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(requireCurrent);
            string path = OpenUsdCompanionExporter.ValidateDestination(destination);
            if (content.IsNull || content.Length is 0 or > MaximumBytes)
            {
                throw new ArgumentException("A nonempty export of at most 1 MiB is required.", nameof(content));
            }
            await PublishAsync(path,
                stream => stream.WriteAsync(content.Memory, cancellationToken).AsTask(),
                requireCurrent, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Copies a bounded source stream through the same new-file publication and final freshness checks.
        /// The caller retains ownership of the source.
        /// </summary>
        public static Task CopyAsync(
            string destination, Stream source, long maximumBytes, Action requireCurrent,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(requireCurrent);
            if (maximumBytes is < 1 or > 16 * 1024 * 1024)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumBytes));
            }
            string path = OpenUsdCompanionExporter.ValidateDestination(destination);
            return PublishAsync(path, async stream =>
            {
                byte[] buffer = ArrayPool<byte>.Shared.Rent(8192);
                try
                {
                    long copied = 0;
                    int count;
                    while ((count = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
                    {
                        if (count > maximumBytes - copied)
                        {
                            throw new IOException("The source exceeds the explicit export byte limit.");
                        }
                        await stream.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                        copied += count;
                    }
                    if (copied == 0)
                    {
                        throw new IOException("An empty source cannot be published.");
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
                }
            }, requireCurrent, cancellationToken);
        }

        private static async Task PublishAsync(
            string path, Func<Stream, Task> writeAsync, Action requireCurrent, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            requireCurrent();
            string temporary = Path.Combine(Path.GetDirectoryName(path)!,
                ".ualens-export-" + Guid.NewGuid().ToString("N") + ".pending");
            using var pending = new PendingFile(temporary);
            try
            {
                var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096,
                    FileOptions.Asynchronous);
                pending.Owned = true;
                await using (file.ConfigureAwait(false))
                {
                    await writeAsync(file).ConfigureAwait(false);
                    await file.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
                cancellationToken.ThrowIfCancellationRequested();
                requireCurrent();
                _ = OpenUsdCompanionExporter.ValidateDestination(path);
                cancellationToken.ThrowIfCancellationRequested();
                File.Move(temporary, path);
                pending.Owned = false;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or
                OperationCanceledException or InvalidOperationException or ArgumentException)
            {
                pending.Failure = error;
                throw;
            }
        }

        /// <summary>The maximum serialized output size.</summary>
        internal const int MaximumBytes = 1024 * 1024;

        /// <summary>
        /// Removes only the unpublished file owned by this operation, retaining both failures.
        /// </summary>
        private sealed class PendingFile(string path) : IDisposable
        {
            public bool Owned { get; set; }

            public Exception? Failure { get; set; }

            public void Dispose()
            {
                if (!Owned)
                {
                    return;
                }
                try
                {
                    File.Delete(path);
                    Owned = false;
                }
                catch (Exception cleanup) when (Failure is not null &&
                    cleanup is IOException or UnauthorizedAccessException)
                {
                    throw new AggregateException("Export and owned-file cleanup failed.", Failure, cleanup);
                }
            }
        }
    }
}
