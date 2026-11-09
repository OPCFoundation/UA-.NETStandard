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
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
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
using Opc.Ua.Client.FileSystem;
using Opc.Ua.Machinery.Result;

namespace Opc.Ua.GMS.Client
{
    public sealed partial class GmsResultClient
    {
        /// <summary>
        /// Downloads a bounded result file and releases both the file and metadata handles acquired by this call.
        /// </summary>
        public async ValueTask<ByteString> DownloadAsync(
            string resultId,
            CancellationToken cancellationToken = default)
        {
            using var destination = new MemoryStream();
            await DownloadAsync(resultId, destination, cancellationToken).ConfigureAwait(false);
            return new ByteString(destination.ToArray());
        }

        /// <summary>
        /// Downloads to a caller-owned destination, never closing it or silently truncating an oversized response.
        /// The destination may contain a partial prefix when transfer fails; no successful result is returned then.
        /// </summary>
        public async ValueTask DownloadAsync(
            string resultId,
            Stream destination,
            CancellationToken cancellationToken = default)
        {
            RequireIdentifier(resultId);
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }
            if (!destination.CanWrite)
            {
                throw new ArgumentException("The destination must be writable.", nameof(destination));
            }
            cancellationToken.ThrowIfCancellationRequested();
            GMSResultManagementTypeClient management = await ResultsAsync(cancellationToken).ConfigureAwait(false);
            (uint handle, ResultDataType data, int error) =
                await management.GetResultByIdAsync(resultId, 0, cancellationToken).ConfigureAwait(false);
            Exception? failure = null;
            try
            {
                ThrowIfDomainError(error, "GetResultById");
                ValidateResult(data);
                cancellationToken.ThrowIfCancellationRequested();
                ResultTransferTypeClient transfer = await management.GetResultTransferAsync(
                    m_telemetry, cancellationToken).ConfigureAwait(false)
                    ?? throw ServiceResultException.Create(
                        StatusCodes.BadNotSupported, "The GMS does not expose optional result-file transfer.");
                var files = new TemporaryFileTransferClient(Session, transfer.ObjectId,
                    new FileSystemClientOptions
                    {
                        ChunkSize = Math.Min(8192, m_limits.MaximumFileBytes),
                        MaxBufferedReadSize = m_limits.MaximumFileBytes
                    });
                UaFileStream source = await files.GenerateFileForReadAsync(
                    Variant.FromStructure(new ResultTransferOptionsDataType { ResultId = resultId }),
                    cancellationToken).ConfigureAwait(false);
                await using (source.ConfigureAwait(false))
                {
                    await CopyBoundedAsync(source, destination, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception exception)
            {
                failure = exception;
                throw;
            }
            finally
            {
                await ReleaseAsync(management, handle, failure).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Reads at most the remaining allowance plus one byte to detect overflow before forwarding it.
        /// </summary>
        private async ValueTask CopyBoundedAsync(
            UaFileStream source,
            Stream destination,
            CancellationToken cancellationToken)
        {
            int chunkSize = Math.Min(8192, m_limits.MaximumFileBytes);
            byte[] buffer = ArrayPool<byte>.Shared.Rent(chunkSize);
            try
            {
                long count = 0;
                while (true)
                {
                    int request = (int)Math.Min(chunkSize, m_limits.MaximumFileBytes - count + 1);
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP2_1_OR_GREATER
                    int read = await source.ReadAsync(buffer.AsMemory(0, request), cancellationToken).ConfigureAwait(false);
#else
                    int read = await source.ReadAsync(buffer, 0, request, cancellationToken).ConfigureAwait(false);
#endif
                    if (read == 0)
                    {
                        return;
                    }
                    count += read;
                    if (count > m_limits.MaximumFileBytes)
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadEncodingLimitsExceeded, "The GMS result download exceeds its byte bound.");
                    }
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP2_1_OR_GREATER
                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
#else
                    await destination.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
#endif
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
            }
        }
    }
}
