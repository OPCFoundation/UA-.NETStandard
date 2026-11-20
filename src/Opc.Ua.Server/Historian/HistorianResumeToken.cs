// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
    /// retained server-side by the framework behind the OPC UA HistoryRead continuation
    /// identifier. The framework guarantees the token will be passed back
    /// verbatim to the same provider on the next page read or released
    /// when the client abandons the continuation.
    /// </para>
    /// <para>
    /// Providers <strong>must not</strong> hold long-lived resources
    /// (database connections, cursors, transactions) in a resume token;
    /// the framework retains tokens across requests and the originating
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
