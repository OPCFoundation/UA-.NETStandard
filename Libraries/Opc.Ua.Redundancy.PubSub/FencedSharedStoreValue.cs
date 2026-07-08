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
using System.Buffers.Binary;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Redundancy;

namespace Opc.Ua.PubSub.Redundancy
{
    internal static class FencedSharedStoreValue
    {
        public static bool TryExtractPayload(ByteString stored, out ByteString payload, out long fencingToken)
        {
            payload = default;
            fencingToken = 0;

            if (stored.IsNull)
            {
                return false;
            }

            ReadOnlySpan<byte> span = stored.Span;
            if (!TryDecodeEnvelope(span, out fencingToken, out int payloadOffset, out int payloadLength))
            {
                payload = stored;
                fencingToken = 0;
                return true;
            }

            payload = payloadLength == 0
                ? ByteString.Empty
                : new ByteString(span.Slice(payloadOffset, payloadLength).ToArray());
            return true;
        }

        public static async ValueTask StoreAsync(
            ISharedKeyValueStore store,
            string key,
            ByteString payload,
            long? fencingToken,
            CancellationToken cancellationToken)
        {
            if (!fencingToken.HasValue)
            {
                await store.SetAsync(key, payload, cancellationToken).ConfigureAwait(false);
                return;
            }

            for (int attempt = 0; attempt < s_maxCompareAndSwapAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                (bool found, ByteString currentBytes) = await store
                    .TryGetAsync(key, cancellationToken)
                    .ConfigureAwait(false);
                long currentToken = 0;
                if (found && !currentBytes.IsNull)
                {
                    _ = TryExtractPayload(currentBytes, out _, out currentToken);
                }

                if (fencingToken.Value < currentToken)
                {
                    throw new InvalidOperationException(
                        $"Rejecting stale fenced write for '{key}'. Current token {currentToken} is newer than " +
                        $"caller token {fencingToken.Value}.");
                }

                ByteString expected = found ? currentBytes : default;
                ByteString next = Wrap(payload, fencingToken.Value);
                if (await store.CompareAndSwapAsync(key, expected, next, cancellationToken).ConfigureAwait(false))
                {
                    return;
                }
            }

            throw new InvalidOperationException(
                $"Unable to commit fenced write for '{key}' after {s_maxCompareAndSwapAttempts} compare-and-swap " +
                "attempts.");
        }

        private static ByteString Wrap(ByteString payload, long fencingToken)
        {
            byte[]? payloadBytes = payload.IsNull ? null : payload.ToArray();
            int payloadLength = payloadBytes?.Length ?? 0;
            byte[] buffer = new byte[s_headerLength + payloadLength];
            Span<byte> span = buffer;
            BinaryPrimitives.WriteUInt32LittleEndian(span[..4], s_magic);
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(4, 4), s_version);
            BinaryPrimitives.WriteInt64LittleEndian(span.Slice(8, 8), fencingToken);
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(16, 4), payloadLength);
            if (payloadLength > 0 && payloadBytes is not null)
            {
                payloadBytes.CopyTo(buffer, s_headerLength);
            }

            return new ByteString(buffer);
        }

        private static bool TryDecodeEnvelope(
            ReadOnlySpan<byte> span,
            out long fencingToken,
            out int payloadOffset,
            out int payloadLength)
        {
            fencingToken = 0;
            payloadOffset = 0;
            payloadLength = 0;

            if (span.Length < s_headerLength)
            {
                return false;
            }

            if (BinaryPrimitives.ReadUInt32LittleEndian(span[..4]) != s_magic
                || BinaryPrimitives.ReadInt32LittleEndian(span.Slice(4, 4)) != s_version)
            {
                return false;
            }

            fencingToken = BinaryPrimitives.ReadInt64LittleEndian(span.Slice(8, 8));
            payloadLength = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(16, 4));
            if (payloadLength < 0 || span.Length != s_headerLength + payloadLength)
            {
                fencingToken = 0;
                payloadLength = 0;
                return false;
            }

            payloadOffset = s_headerLength;
            return true;
        }

        private const uint s_magic = 0x46535048;
        private const int s_version = 1;
        private const int s_headerLength = 20;
        private const int s_maxCompareAndSwapAttempts = 5;
    }
}
