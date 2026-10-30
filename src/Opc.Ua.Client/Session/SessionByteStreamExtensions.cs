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

namespace Opc.Ua.Client
{
    /// <summary>
    /// Reads large ByteString variables without materializing the complete value.
    /// </summary>
    public static class SessionByteStreamExtensions
    {
        /// <summary>
        /// Copies ranged reads into a caller-owned destination, rejecting oversized responses before writing.
        /// Short non-empty reads are followed until the server reports an empty value or no remaining range.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        /// <exception cref="ServiceResultException"></exception>
        /// <exception cref="IOException"></exception>
        public static ValueTask<long> ReadBytesToAsync(
            this ISessionClient session,
            NodeId nodeId,
            Stream destination,
            long maxBytes,
            int chunkSize = 4096,
            CancellationToken ct = default)
        {
            return ClientOperationContext.ExecuteAsync(session,
                token => ReadBytesToCoreAsync(session, nodeId, destination, maxBytes, chunkSize, token), ct);
        }

        /// <summary>
        /// Completes validation and destination writes before accepting the complete transfer.
        /// </summary>
        private static async ValueTask<long> ReadBytesToCoreAsync(
            ISessionClient session, NodeId nodeId, Stream destination, long maxBytes, int chunkSize, CancellationToken ct)
        {
            if (session is null)
            {
                throw new ArgumentNullException(nameof(session));
            }
            if (destination is null)
            {
                throw new ArgumentNullException(nameof(destination));
            }
            if (!destination.CanWrite)
            {
                throw new ArgumentException("The destination must be writable.", nameof(destination));
            }
            if (maxBytes is <= 0 or > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(maxBytes));
            }
            if (chunkSize is <= 0 or > 1_048_576)
            {
                throw new ArgumentOutOfRangeException(nameof(chunkSize));
            }
            if (session.MessageContext.MaxByteStringLength > 0)
            {
                chunkSize = Math.Min(chunkSize, session.MessageContext.MaxByteStringLength);
            }
            int offset = 0;
            while (true)
            {
                int requested = (int)Math.Min(chunkSize, (long)int.MaxValue - offset + 1);
                ArrayOf<ReadValueId> requests =
                [
                    new ReadValueId
                    {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        IndexRange = new NumericRange(offset, offset + requested - 1).ToString()
                    }
                ];
                ReadResponse response = await session.ReadAsync(
                    null, 0, TimestampsToReturn.Neither, requests, ct).ConfigureAwait(false);
                ClientBase.ValidateResponse(response.Results, requests);
                ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, requests);
                DataValue value = response.Results[0];
                if (value.StatusCode == StatusCodes.BadIndexRangeNoData)
                {
                    return offset;
                }
                if (StatusCode.IsBad(value.StatusCode))
                {
                    throw ClientOperationContext.OriginalFailure(session, new ServiceResultException(value.StatusCode));
                }
                if (!value.WrappedValue.TryGetValue(out ByteString chunk))
                {
                    throw new ServiceResultException(StatusCodes.BadTypeMismatch, "The value is not a ByteString.");
                }
                if (chunk.IsEmpty)
                {
                    return offset;
                }
                if (chunk.Length > requested || chunk.Length > maxBytes - offset)
                {
                    throw new IOException("The ByteString exceeds the requested range or transfer limit.");
                }
#if NET6_0_OR_GREATER
                await destination.WriteAsync(chunk.Memory, ct).ConfigureAwait(false);
#else
                byte[] bytes = chunk.Span.ToArray();
                await destination.WriteAsync(bytes, 0, bytes.Length, ct).ConfigureAwait(false);
#endif
                offset = checked(offset + chunk.Length);
            }
        }
    }
}
