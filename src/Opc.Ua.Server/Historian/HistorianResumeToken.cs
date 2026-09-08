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
using System.Buffers.Binary;

namespace Opc.Ua.Server.Historian
{
    /// <summary>
    /// Opaque resume marker produced by an <see cref="IHistorianProvider"/>
    /// to support paged history reads.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A <see cref="HistorianResumeToken"/> is the provider's "where to
    /// resume next" hint. It carries arbitrary provider-specific state
    /// (keyset position, opaque cursor id, byte offset, etc.) and is
    /// serialised by the framework into the OPC UA HistoryRead continuation
    /// point. The framework guarantees the token will be passed back
    /// verbatim to the same provider on the next page read or released
    /// when the client abandons the continuation.
    /// </para>
    /// <para>
    /// Providers <strong>must not</strong> hold long-lived resources
    /// (database connections, cursors, transactions) in a resume token;
    /// the framework persists tokens across requests and the originating
    /// task may have completed before the next page is requested.
    /// </para>
    /// </remarks>
    public readonly record struct HistorianResumeToken(ByteString State)
    {
        /// <summary>
        /// Returns <c>true</c> when the token carries no state (no more pages).
        /// </summary>
        public bool IsEmpty => State.IsEmpty;

        /// <summary>
        /// Creates a portable token from an exclusive historical cursor.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public static HistorianResumeToken FromCursor(HistorianResumeCursor cursor)
        {
            int keyLength = cursor.Key.Length;
            if (keyLength > kMaxCursorKeyLength)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(cursor),
                    "The historian cursor key exceeds the supported limit.");
            }
            byte[] buffer = new byte[sizeof(int) +
                sizeof(long) +
                sizeof(long) +
                sizeof(int) +
                keyLength];
            Span<byte> span = buffer;
            BinaryPrimitives.WriteInt32LittleEndian(span, 1);
            BinaryPrimitives.WriteInt64LittleEndian(
                span[sizeof(int)..],
                cursor.Timestamp.ToDateTime().ToBinary());
            BinaryPrimitives.WriteInt64LittleEndian(
                span[(sizeof(int) + sizeof(long))..],
                cursor.Sequence);
            BinaryPrimitives.WriteInt32LittleEndian(
                span[(sizeof(int) + (2 * sizeof(long)))..],
                keyLength);
            cursor.Key.Span.CopyTo(
                span[(sizeof(int) + (2 * sizeof(long)) + sizeof(int))..]);
            return new HistorianResumeToken(ByteString.From(buffer));
        }

        /// <summary>
        /// Decodes a portable historical cursor.
        /// </summary>
        public bool TryGetCursor(out HistorianResumeCursor cursor)
        {
            ReadOnlySpan<byte> span = State.Span;
            const int fixedLength = sizeof(int) + (2 * sizeof(long)) + sizeof(int);
            if (span.Length < fixedLength ||
                span.Length > fixedLength + kMaxCursorKeyLength ||
                BinaryPrimitives.ReadInt32LittleEndian(span) != 1)
            {
                cursor = default;
                return false;
            }

            long timestamp = BinaryPrimitives.ReadInt64LittleEndian(span[sizeof(int)..]);
            long sequence = BinaryPrimitives.ReadInt64LittleEndian(
                span[(sizeof(int) + sizeof(long))..]);
            int keyLength = BinaryPrimitives.ReadInt32LittleEndian(
                span[(sizeof(int) + (2 * sizeof(long)))..]);
            if (sequence < 0 ||
                keyLength < 0 ||
                keyLength > kMaxCursorKeyLength ||
                keyLength != span.Length - fixedLength)
            {
                cursor = default;
                return false;
            }

            try
            {
                cursor = new HistorianResumeCursor(
                    new DateTimeUtc(DateTime.FromBinary(timestamp)),
                    ByteString.From(span[fixedLength..]),
                    sequence);
                return true;
            }
            catch (ArgumentException)
            {
                cursor = default;
                return false;
            }
        }

        private const int kMaxCursorKeyLength = 64 * 1024;
    }

    /// <summary>
    /// Exclusive resume position shared by historian provider implementations.
    /// </summary>
    public readonly record struct HistorianResumeCursor(
        DateTimeUtc Timestamp,
        ByteString Key,
        long Sequence);
}
