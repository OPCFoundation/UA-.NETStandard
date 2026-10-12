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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Bounded, sequential FileType transfers shared by companion clients.
    /// </summary>
    public static class FileTypeStreamExtensions
    {
        /// <summary>
        /// Opens a file for replacement, streams its content and closes it. The caller owns the source.
        /// </summary>
        public static async ValueTask<long> UploadFromAsync(
            this FileTypeClient file,
            Stream source,
            long maxBytes,
            int chunkSize = 4096,
            CancellationToken ct = default)
        {
            (long total, _) = await file.UploadFromWithResultAsync(source, maxBytes, chunkSize, ct)
                .ConfigureAwait(false);
            return total;
        }

        /// <summary>
        /// Uploads a stream and preserves the final Close method's status and argument diagnostics.
        /// </summary>
        /// <exception cref="IOException"></exception>
        public static async ValueTask<(long BytesTransferred, CallMethodResult Result)> UploadFromWithResultAsync(
            this FileTypeClient file,
            Stream source,
            long maxBytes,
            int chunkSize = 4096,
            CancellationToken ct = default)
        {
            Validate(file, source, maxBytes, chunkSize, readingSource: true);
            if (source.CanSeek && source.Length - source.Position > maxBytes)
            {
                throw new IOException("The source exceeds the configured transfer limit.");
            }
            uint handle = await file.OpenAsync(6, ct).ConfigureAwait(false);
            return await file.WriteStreamAndCloseWithResultAsync(handle, source, maxBytes, chunkSize, ct)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Streams to an already-open handle and closes it. Cleanup failures never replace a transfer failure.
        /// Closing a partially written remote file does not promise rollback.
        /// </summary>
        public static async ValueTask<long> WriteStreamAndCloseAsync(
            this FileTypeClient file,
            uint handle,
            Stream source,
            long maxBytes,
            int chunkSize = 4096,
            CancellationToken ct = default)
        {
            (long total, _) = await file.WriteStreamAndCloseWithResultAsync(handle, source, maxBytes, chunkSize, ct)
                .ConfigureAwait(false);
            return total;
        }

        /// <summary>
        /// Writes an open file and preserves its completed Close result without a post-commit cancellation check.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public static async ValueTask<(long BytesTransferred, CallMethodResult Result)>
            WriteStreamAndCloseWithResultAsync(
            this FileTypeClient file,
            uint handle,
            Stream source,
            long maxBytes,
            int chunkSize = 4096,
            CancellationToken ct = default)
        {
            Validate(file, source, maxBytes, chunkSize, readingSource: true);
            try
            {
                long total = await file.WriteStreamAsync(handle, source, maxBytes, chunkSize, ct).ConfigureAwait(false);
                CallMethodResult result = await file.CallWithResultAsync(
                    MethodIds.FileType_Close, [Variant.From(handle)], ct).ConfigureAwait(false);
                if (StatusCode.IsBad(result.StatusCode))
                {
                    throw new ServiceResultException(result.StatusCode);
                }
                return (total, result);
            }
            catch (Exception)
            {
                await file.CloseAfterFailureAsync(handle).ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>
        /// Writes bounded chunks to a caller-owned open handle without committing or closing it.
        /// </summary>
        /// <exception cref="IOException"></exception>
        public static async ValueTask<long> WriteStreamAsync(
            this FileTypeClient file,
            uint handle,
            Stream source,
            long maxBytes,
            int chunkSize = 4096,
            CancellationToken ct = default)
        {
            Validate(file, source, maxBytes, chunkSize, readingSource: true);
            byte[] buffer = new byte[chunkSize];
            long total = 0;
            while (true)
            {
#if NET6_0_OR_GREATER
                int read = await source.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false);
#else
                int read = await source.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false);
#endif
                if (read == 0)
                {
                    return total;
                }
                if (read > maxBytes - total)
                {
                    throw new IOException("The upload exceeds the configured transfer limit.");
                }
                await file.WriteAsync(handle, ByteString.From(buffer.AsSpan(0, read)), ct).ConfigureAwait(false);
                total += read;
            }
        }

        /// <summary>
        /// Streams a remote file until an empty read, including when intermediate reads are short.
        /// The caller owns the destination; an oversized chunk is rejected before writing it.
        /// </summary>
        /// <exception cref="IOException"></exception>
        public static async ValueTask<long> DownloadToStreamAsync(
            this FileTypeClient file,
            Stream destination,
            long maxBytes,
            int chunkSize = 4096,
            CancellationToken ct = default)
        {
            Validate(file, destination, maxBytes, chunkSize, readingSource: false);
            uint handle = await file.OpenAsync(1, ct).ConfigureAwait(false);
            try
            {
                long total = 0;
                while (true)
                {
                    ByteString chunk = await file.ReadAsync(handle, chunkSize, ct).ConfigureAwait(false);
                    if (chunk.IsEmpty)
                    {
                        await file.CloseAsync(handle, CancellationToken.None).ConfigureAwait(false);
                        return total;
                    }
                    if (chunk.Length > chunkSize || chunk.Length > maxBytes - total)
                    {
                        throw new IOException("The download exceeds the requested chunk or transfer limit.");
                    }
#if NET6_0_OR_GREATER
                    await destination.WriteAsync(chunk.Memory, ct).ConfigureAwait(false);
#else
                    byte[] buffer = chunk.Span.ToArray();
                    await destination.WriteAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false);
#endif
                    total += chunk.Length;
                }
            }
            catch (Exception)
            {
                await file.CloseAfterFailureAsync(handle).ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>
        /// Releases a handle after a failed transfer, logging cleanup errors without masking the primary failure.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static async ValueTask CloseAfterFailureAsync(this FileTypeClient file, uint handle)
        {
            if (file is null)
            {
                throw new ArgumentNullException(nameof(file));
            }
            try
            {
                await file.CloseAsync(handle, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                file.Telemetry.CreateLogger(nameof(FileTypeStreamExtensions)).FileHandleCleanupFailed(
                    exception, file.ObjectId);
            }
        }

        /// <summary>
        /// Validates transfer arguments before any remote call or buffer allocation.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        /// <exception cref="ArgumentException"></exception>
        private static void Validate(
            FileTypeClient file, Stream stream, long maxBytes, int chunkSize, bool readingSource)
        {
            if (file is null)
            {
                throw new ArgumentNullException(nameof(file));
            }
            if (stream is null)
            {
                throw new ArgumentNullException(nameof(stream));
            }
            if (maxBytes <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxBytes));
            }
            if (chunkSize is <= 0 or > 1_048_576)
            {
                throw new ArgumentOutOfRangeException(nameof(chunkSize));
            }
            if (readingSource ? !stream.CanRead : !stream.CanWrite)
            {
                throw new ArgumentException(
                    "The stream does not support the requested transfer direction.", nameof(stream));
            }
        }
    }

    /// <summary>
    /// Source-generated diagnostics for bounded FileType transfers.
    /// </summary>
    internal static partial class FileTypeStreamExtensionsLog
    {
        /// <summary>
        /// Records a cleanup failure without discarding the original transfer exception.
        /// </summary>
        [LoggerMessage(EventId = ClientEventIds.FileTypeStreamExtensions, Level = LogLevel.Warning,
            Message = "Failed to close a remote file handle after a transfer failure for {FileId}.")]
        internal static partial void FileHandleCleanupFailed(this ILogger logger, Exception exception, NodeId fileId);
    }
}
