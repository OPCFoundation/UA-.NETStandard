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
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Gds.Client
{
    /// <summary>
    /// Helper for reading and writing trust-list files exposed via the OPC UA
    /// FileType / TrustListType server-side proxies. The helper streams the
    /// payload through a single rented <see cref="ArrayPool{T}"/> buffer to
    /// keep the working set small.
    /// </summary>
    internal static class TrustListFileTransferHelper
    {
        /// <summary>
        /// Reads the full contents of an already opened file handle into a
        /// decoded <see cref="TrustListDataType"/>. The size is bounded by
        /// <paramref name="maxTrustListSize"/>; exceeding this limit throws a
        /// <see cref="ServiceResultException"/> with
        /// <see cref="StatusCodes.BadEncodingLimitsExceeded"/>. The caller
        /// remains responsible for closing the file handle.
        /// </summary>
        public static async Task<TrustListDataType> ReadAsync(
            FileTypeClient file,
            uint fileHandle,
            IServiceMessageContext messageContext,
            long maxTrustListSize,
            int chunkSize,
            CancellationToken ct)
        {
            if (file == null)
            {
                throw new ArgumentNullException(nameof(file));
            }

            if (messageContext == null)
            {
                throw new ArgumentNullException(nameof(messageContext));
            }

            if (chunkSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(chunkSize));
            }

            byte[] rentedBuffer = ArrayPool<byte>.Shared.Rent(chunkSize);
            try
            {
                using var ostrm = new MemoryStream();
                long totalBytesRead = 0;

                while (true)
                {
                    ByteString chunk = await file.ReadAsync(fileHandle, chunkSize, ct)
                        .ConfigureAwait(false);
                    byte[] bytes = chunk.ToArray() ?? Array.Empty<byte>();

                    totalBytesRead += bytes.Length;
                    if (totalBytesRead > maxTrustListSize)
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadEncodingLimitsExceeded,
                            "Trust list size exceeds maximum allowed size of {0} bytes",
                            maxTrustListSize);
                    }

                    ostrm.Write(bytes, 0, bytes.Length);

                    if (bytes.Length != chunkSize)
                    {
                        break;
                    }
                }

                ostrm.Position = 0;
                var trustList = new TrustListDataType();
                using var decoder = new BinaryDecoder(ostrm, messageContext);
                trustList.Decode(decoder);
                return trustList;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rentedBuffer);
            }
        }

        /// <summary>
        /// Writes a <see cref="TrustListDataType"/> to the supplied trust-list
        /// type proxy and commits the change with
        /// <see cref="TrustListTypeClient.CloseAndUpdateAsync"/>. The size is
        /// bounded by <paramref name="maxTrustListSize"/>.
        /// </summary>
        public static async Task<bool> WriteAsync(
            TrustListTypeClient trustListClient,
            TrustListDataType trustList,
            IServiceMessageContext messageContext,
            long maxTrustListSize,
            int chunkSize,
            CancellationToken ct)
        {
            if (trustListClient == null)
            {
                throw new ArgumentNullException(nameof(trustListClient));
            }

            if (trustList == null)
            {
                throw new ArgumentNullException(nameof(trustList));
            }

            if (messageContext == null)
            {
                throw new ArgumentNullException(nameof(messageContext));
            }

            if (chunkSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(chunkSize));
            }

            using var strm = new MemoryStream();
            using (var encoder = new BinaryEncoder(strm, messageContext, true))
            {
                encoder.WriteEncodeable(null, trustList);
            }
            strm.Position = 0;

            if (strm.Length > maxTrustListSize)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadEncodingLimitsExceeded,
                    "Trust list size {0} exceeds maximum allowed size of {1} bytes",
                    strm.Length,
                    maxTrustListSize);
            }

            uint fileHandle = await trustListClient.OpenAsync(
                (byte)((int)OpenFileMode.Write | (int)OpenFileMode.EraseExisting),
                ct).ConfigureAwait(false);

            byte[] rentedBuffer = ArrayPool<byte>.Shared.Rent(chunkSize);
            try
            {
                while (true)
                {
                    int bytesRead = strm.Read(rentedBuffer, 0, chunkSize);
                    if (bytesRead == 0)
                    {
                        break;
                    }

                    var slice = new byte[bytesRead];
                    Buffer.BlockCopy(rentedBuffer, 0, slice, 0, bytesRead);
                    await trustListClient.WriteAsync(
                        fileHandle,
                        slice.ToByteString(),
                        ct).ConfigureAwait(false);

                    if (bytesRead < chunkSize)
                    {
                        break;
                    }
                }

                return await trustListClient.CloseAndUpdateAsync(fileHandle, ct)
                    .ConfigureAwait(false);
            }
            catch
            {
                try
                {
                    await trustListClient.CloseAsync(fileHandle, ct).ConfigureAwait(false);
                }
                catch
                {
                    // ignore close failures during error handling
                }
                throw;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rentedBuffer);
            }
        }
    }
}
