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

/* Licensed to the .NET Foundation under one or more agreements.
 * The .NET Foundation licenses this file to you under the MIT license.
 * See the LICENSE file in the project root for more information. */

#nullable enable

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Buffers.Text;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Opc.Ua
{
    /// <summary>
    /// Optimized byte sequence reader
    /// </summary>
    internal ref struct SequenceReader
    {
        /// <summary>
        /// Gets the total number of bytes processed by the reader.
        /// </summary>
        public long Consumed { get; private set; }

        /// <summary>
        /// Gets a value indicating whether there is no more data.
        /// </summary>
        public readonly bool End => !m_moreData;

        /// <summary>
        /// Gets count of byte in the reader's <see cref="Sequence"/>.
        /// </summary>
        public readonly long Length { get; }

        /// <summary>
        /// Gets the current position in the <see cref="Sequence"/>.
        /// </summary>
        public readonly SequencePosition Position
            => Sequence.GetPosition(m_currentSpanIndex, m_curPos);

        /// <summary>
        /// Gets remaining bytes in the reader's <see cref="Sequence"/>.
        /// </summary>
        public readonly long Remaining => Length - Consumed;

        /// <summary>
        /// Gets the underlying sequence of the reader.
        /// </summary>
        public readonly ReadOnlySequence<byte> Sequence { get; }

        /// <summary>
        /// Whether the architecture is little endian. Enables testing big endian on
        /// little endian architecture.
        /// </summary>
        public bool IsLittleEndian { get; internal set; }
            = BitConverter.IsLittleEndian;

        /// <summary>
        /// Create a new reader of sequence
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SequenceReader(scoped in ReadOnlySequence<byte> sequence)
        {
            m_currentSpanIndex = 0;
            Consumed = 0;
            Sequence = sequence;
            m_isLast = Sequence.IsSingleSegment;
            m_curPos = Sequence.Start;
            Length = Sequence.Length;

            ReadOnlySpan<byte> first = Sequence.First.Span;
            m_nextPos = Sequence.GetPosition(first.Length);
            m_currentSpan = first;
            m_moreData = first.Length > 0;

            if (!m_moreData && !Sequence.IsSingleSegment)
            {
                m_moreData = true;
                GetNextSpan();
            }
        }

        /// <summary>
        /// Move the reader ahead the specified number of items.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Advance(long count)
        {
            const long tooBigOrNegative = unchecked((long)0xFFFFFFFF80000000);
            if ((count & tooBigOrNegative) == 0 &&
                m_currentSpan.Length - m_currentSpanIndex > (int)count)
            {
                m_currentSpanIndex += (int)count;
                Consumed += count;
            }
            else if (!m_isLast)
            {
                // Can't satisfy from the current span
                AdvanceToNextSpan(count);
            }
            else if (m_currentSpan.Length - m_currentSpanIndex == (int)count)
            {
                m_currentSpanIndex += (int)count;
                Consumed += count;
                m_moreData = false;
            }
            else
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }
        }

#if NET8_0_OR_GREATER
        /// <summary>
        /// Skip consecutive instances of any of the given <paramref name="values"/>.
        /// </summary>
        /// <returns>How many positions the reader has been advanced.</returns>
        public long SkipAnyOf(SearchValues<byte> values)
        {
            long start = Consumed;
            do
            {
                int i = m_currentSpanIndex;
                while (i < m_currentSpan.Length && values.Contains(m_currentSpan[i]))
                {
                    i++;
                }
                int advanced = i - m_currentSpanIndex;
                if (advanced == 0)
                {
                    // Didn't advance at all in this span, exit.
                    break;
                }
                AdvanceCurrentSpan(advanced);
                // If we're at position 0 after advancing and not at the End,
                // we're in a new span and should continue the loop.
            }
            while (m_currentSpanIndex == 0 && m_moreData);
            return Consumed - start;
        }
#endif

        /// <summary>
        /// Moves the reader to the end of the sequence.
        /// </summary>
        public void AdvanceToEnd()
        {
            if (m_moreData)
            {
                Consumed = Length;
                m_currentSpan = default;
                m_currentSpanIndex = 0;
                m_isLast = Sequence.IsSingleSegment;
                m_curPos = Sequence.End;
                m_nextPos = default;
                m_moreData = false;
            }
        }

        /// <summary>
        /// Check to see if the given values are next.
        /// </summary>
        /// <param name="next">The span to compare the next items to.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool SkipToken(scoped ReadOnlySpan<byte> next)
        {
            ReadOnlySpan<byte> unread = GetUnreadSpan();
            if (next.Length <= unread.Length)
            {
                if (unread[..next.Length].SequenceEqual(next))
                {
                    AdvanceCurrentSpan(next.Length);
                    return true;
                }
                return false;
            }
            // Only check the slow path if there wasn't enough to satisfy next
            if (IsNextSlow(next))
            {
                Advance(next.Length);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Skip value if it is next. Returns false if not advanced
        /// </summary>
        /// <param name="next"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool SkipByte(byte next)
        {
            if (m_moreData && m_currentSpan[m_currentSpanIndex] == next)
            {
                AdvanceCurrentSpan(1);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Check next value is provided value.
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool IsNextByte(byte value)
        {
            return m_moreData && m_currentSpan[m_currentSpanIndex] == value;
        }

        /// <summary>
        /// Check to see if the given token follows.
        /// </summary>
        /// <param name="token">The span to compare the next items to.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool IsNextToken(scoped ReadOnlySpan<byte> token)
        {
            ReadOnlySpan<byte> unread = GetUnreadSpan();
            if (token.Length <= unread.Length)
            {
                return unread[..token.Length].SequenceEqual(token);
            }
            // Only check the slow path if there wasn't enough to satisfy next
            return IsNextSlow(token);
        }

        /// <summary>
        /// Move the reader back the specified number of items.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Rewind(long count)
        {
            ThrowIfNegativeOrZeroOrGreaterThan(count, Consumed);
            Consumed -= count;
            if (m_currentSpanIndex >= count)
            {
                m_currentSpanIndex -= (int)count;
                m_moreData = true;
            }
            else
            {
                // Current segment doesn't have enough data, scan backward through segments
                long prev = Consumed;
                ResetReader();
                Advance(prev);
            }
        }

        /// <summary>
        /// Advance past the given <paramref name="delimiter"/>, if found.
        /// </summary>
        /// <param name="delimiter">The delimiter to search for.</param>
        /// <returns>True if the given <paramref name="delimiter"/> was found.</returns>
        public bool TrySkipPast(byte delimiter)
        {
            if (TrySkipUntil(delimiter))
            {
                Advance(1);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Advance until the given <paramref name="delimiter"/>, if found.
        /// </summary>
        /// <param name="delimiter">The delimiter to search for.</param>
        /// <returns>True if the given <paramref name="delimiter"/> was found.</returns>
        public bool TrySkipUntil(byte delimiter)
        {
            ReadOnlySpan<byte> remaining = GetUnreadSpan();
            int index = remaining.IndexOf(delimiter);
            if (index != -1)
            {
                Advance(index);
                return true;
            }
            // var copy = this;
            while (m_moreData)
            {
                index = remaining.IndexOf(delimiter);
                if (index != -1)
                {
                    // Found the delimiter.
                    if (index > 0)
                    {
                        AdvanceCurrentSpan(index);
                    }
                    return true;
                }
                AdvanceCurrentSpan(remaining.Length);
                remaining = m_currentSpan;
            }
            // Didn't find anything
            return false;
        }

        /// <summary>
        /// Copies data from the current position to the provided span.
        /// </summary>
        /// <param name="destination">Destination to copy to.</param>
        /// <returns>True if there is enough data to copy to span.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool TryCopyTo(Span<byte> destination)
        {
            ReadOnlySpan<byte> firstSpan = GetUnreadSpan();
            if (firstSpan.Length >= destination.Length)
            {
                firstSpan[..destination.Length].CopyTo(destination);
                return true;
            }
            return TryCopyMultisegment(destination);
        }

        /// <summary>
        /// Read the next value and advance the reader.
        /// </summary>
        /// <param name="value">The next value or default if at the end.</param>
        /// <returns>False if at the end of the reader.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryRead(out byte value)
        {
            if (m_moreData)
            {
                value = m_currentSpan[m_currentSpanIndex];
                m_currentSpanIndex++;
                Consumed++;
                if (m_currentSpanIndex >= m_currentSpan.Length)
                {
                    if (!m_isLast)
                    {
                        GetNextSpan();
                    }
                    else
                    {
                        m_moreData = false;
                    }
                }
                return true;
            }
            value = default;
            return false;
        }

        /// <summary>
        /// Try to read data with given <paramref name="count"/>.
        /// </summary>
        /// <param name="count">Read count.</param>
        /// <param name="sequence">The read data.</param>
        /// <returns></returns>
        public bool TryReadExact(long count, out ReadOnlySequence<byte> sequence)
        {
            ThrowIfNegative(count, nameof(count));
            if (Remaining < count)
            {
                sequence = default;
                return false;
            }
            sequence = Sequence.Slice(Position, count);
            if (count != 0)
            {
                Advance(count);
            }
            return true;
        }

        /// <summary>
        /// Reads an <see cref="short"/> as little endian.
        /// </summary>
        /// <param name="value"></param>
        /// <returns>False if there wasn't enough data.</returns>
        public bool TryReadLittleEndian(out short value)
        {
            if (TryReadUnaligned(out value))
            {
                if (!IsLittleEndian)
                {
                    value = BinaryPrimitives.ReverseEndianness(value);
                }
                return true;
            }
            return false;
        }

        /// <summary>
        /// Reads an <see cref="int"/> as little endian.
        /// </summary>
        /// <param name="value"></param>
        /// <returns>False if there wasn't enough data.</returns>
        public bool TryReadLittleEndian(out int value)
        {
            if (TryReadUnaligned(out value))
            {
                if (!IsLittleEndian)
                {
                    value = BinaryPrimitives.ReverseEndianness(value);
                }
                return true;
            }
            return false;
        }

        /// <summary>
        /// Reads an <see cref="long"/> as little endian.
        /// </summary>
        /// <param name="value"></param>
        /// <returns>False if there wasn't enough data.</returns>
        public bool TryReadLittleEndian(out long value)
        {
            if (TryReadUnaligned(out value))
            {
                if (!IsLittleEndian)
                {
                    value = BinaryPrimitives.ReverseEndianness(value);
                }
                return true;
            }
            return false;
        }

        /// <summary>
        /// Reads an <see cref="float"/> as little endian.
        /// </summary>
        /// <param name="value"></param>
        /// <returns>False if there wasn't enough data.</returns>
        public bool TryReadLittleEndian(out float value)
        {
            if (IsLittleEndian)
            {
                return TryReadUnaligned(out value);
            }
            if (TryReadUnaligned(out int i))
            {
#if NET8_0_OR_GREATER
                value = BitConverter.Int32BitsToSingle(
#else
                value = Convert.ToSingle(
#endif
                    BinaryPrimitives.ReverseEndianness(i));
                return true;
            }
            value = default;
            return false;
        }

        /// <summary>
        /// Reads an <see cref="double"/> as little endian.
        /// </summary>
        /// <param name="value"></param>
        /// <returns>False if there wasn't enough data.</returns>
        public bool TryReadLittleEndian(out double value)
        {
            if (IsLittleEndian)
            {
                return TryReadUnaligned(out value);
            }
            if (TryReadUnaligned(out long i))
            {
                value = BitConverter.Int64BitsToDouble(
                    BinaryPrimitives.ReverseEndianness(i));
                return true;
            }
            value = default;
            return false;
        }

        /// <summary>
        /// Try read variable length integer from the sequence
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public bool TryReadVariableLength(out long value)
        {
            if (!TryRead(out byte b))
            {
                value = default;
                return false;
            }
            ulong n = b & 0x7FUL;
            int shift = 7;
            while ((b & 0x80) != 0)
            {
                if (!TryRead(out b))
                {
                    value = default;
                    return false;
                }
                n |= (b & 0x7FUL) << shift;
                shift += 7;
            }
            value = (long)n;
            value = (-(value & 0x01L)) ^ ((value >> 1) & 0x7fffffffffffffffL);
            return true;
        }

        /// <summary>
        /// Try to read everything up to the given <paramref name="delimiter"/>.
        /// </summary>
        /// <param name="sequence">The read data, if any.</param>
        /// <param name="delimiter">The delimiter to look for.</param>
        /// <returns>True if the <paramref name="delimiter"/> was found.</returns>
        public bool TryReadPast(out ReadOnlySequence<byte> sequence, byte delimiter)
        {
            SequenceReader copy = this;
            ReadOnlySpan<byte> remaining = GetUnreadSpan();
            while (m_moreData)
            {
                int index = remaining.IndexOf(delimiter);
                if (index != -1)
                {
                    // Found the delimiter. Move to it, slice, then move past it.
                    if (index > 0)
                    {
                        AdvanceCurrentSpan(index);
                    }
                    sequence = Sequence.Slice(copy.Position, Position);
                    Advance(1);
                    return true;
                }
                AdvanceCurrentSpan(remaining.Length);
                remaining = m_currentSpan;
            }
            // Didn't find anything
            sequence = default;
            return false;
        }

#if NET8_0_OR_GREATER
        /// <summary>
        /// Try to read everything up to the given <paramref name="delimiters"/>.
        /// </summary>
        /// <param name="sequence">The read data, if any.</param>
        /// <param name="delimiters">The delimiters to look for.</param>
        /// <param name="found">If true the found delimiter.</param>
        /// <returns>True if any of the <paramref name="delimiters"/> were found.</returns>
        public bool TryReadPastAny(out ReadOnlySequence<byte> sequence, SearchValues<byte> delimiters,
            out byte found)
        {
            SequenceReader copy = this;
            ReadOnlySpan<byte> remaining = GetUnreadSpan();
            while (m_moreData)
            {
                int index = remaining.IndexOfAny(delimiters);
                if (index != -1)
                {
                    // Found one of the delimiters. Move to it, slice, then move past it.
                    if (index > 0)
                    {
                        AdvanceCurrentSpan(index);
                    }

                    sequence = Sequence.Slice(copy.Position, Position);
                    found = m_currentSpan[m_currentSpanIndex];
                    Advance(1);
                    return true;
                }

                Advance(remaining.Length);
                remaining = m_currentSpan;
            }

            // Didn't find anything
            found = default;
            sequence = default;
            return false;
        }
#endif

        /// <summary>
        /// Try to read everything up to the given <paramref name="delimiter"/>.
        /// </summary>
        /// <param name="sequence">The read data, if any.</param>
        /// <param name="delimiter">The delimiter to look for.</param>
        /// <returns>True if the <paramref name="delimiter"/> was found.</returns>
        public bool TryReadUntil(out ReadOnlySequence<byte> sequence, byte delimiter)
        {
            SequenceReader copy = this;
            ReadOnlySpan<byte> remaining = GetUnreadSpan();
            while (m_moreData)
            {
                int index = remaining.IndexOf(delimiter);
                if (index != -1)
                {
                    // Found the delimiter. Move to it, slice, then move past it.
                    if (index > 0)
                    {
                        AdvanceCurrentSpan(index);
                    }
                    sequence = Sequence.Slice(copy.Position, Position);
                    return true;
                }
                AdvanceCurrentSpan(remaining.Length);
                remaining = m_currentSpan;
            }
            // Didn't find anything
            sequence = default;
            return false;
        }

        /// <summary>
        /// Try to read data until the entire given <paramref name="token"/> matches.
        /// </summary>
        /// <param name="sequence">The read data without the token, if any.</param>
        /// <param name="token">The multi (byte) delimiter.</param>
        /// <returns>True if the <paramref name="token"/> was found.</returns>
        public bool TryReadPast(out ReadOnlySequence<byte> sequence, scoped ReadOnlySpan<byte> token)
        {
            if (token.Length > 1)
            {
                SequenceReader copy = this;
                while (m_moreData)
                {
                    if (!TryReadUntil(out sequence, token[0]))
                    {
                        this = copy;
                        return false;
                    }
                    if (SkipToken(token))
                    {
                        sequence = copy.Sequence.Slice(copy.Consumed, Consumed - copy.Consumed - token.Length);
                        return true;
                    }
                    Advance(1);
                }
                sequence = default;
                return false;
            }
            else if (token.Length == 1)
            {
                return TryReadPast(out sequence, token[0]);
            }
            sequence = default;
            return true;
        }

#if NET8_0_OR_GREATER
        /// <summary>
        /// Try parse everything up to the delimiter
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="value"></param>
        /// <param name="delimiter"></param>
        /// <param name="provider"></param>
        /// <returns></returns>
        public bool TryParseUtf8<T>(out T value, byte delimiter, IFormatProvider? provider = null)
            where T : struct, IUtf8SpanParsable<T>
        {
            if (TryReadPast(out ReadOnlySequence<byte> sequence, delimiter))
            {
                if (sequence.IsSingleSegment)
                {
                    return T.TryParse(sequence.FirstSpan, provider, out value);
                }
                int length = (int)sequence.Length;
                byte[] buffer = ArrayPool<byte>.Shared.Rent(length);
                sequence.CopyTo(buffer);
                bool success = T.TryParse(buffer.AsSpan()[..length], provider, out value);
                ArrayPool<byte>.Shared.Return(buffer);
                return success;
            }
            value = default;
            return false;
        }
#endif

        /// <summary>
        /// Try parse Guid from sequence
        /// </summary>
        /// <param name="value"></param>
        /// <param name="standardFormat"></param>
        /// <returns></returns>
        public bool TryParseUtf8(out Guid value, char standardFormat = '\0')
        {
            bool success;
            int consumed;
            ReadOnlySpan<byte> span = GetUnreadSpan();
            const int maxChars = 40;
            if (span.Length >= maxChars || Remaining == span.Length)
            {
                success = Utf8Parser.TryParse(span, out value, out consumed, standardFormat);
            }
            else
            {
                int len = Math.Min((int)Remaining, maxChars);
                byte[] buffer = ArrayPool<byte>.Shared.Rent(len);
                Span<byte> dest = buffer.AsSpan()[..len];
                success = TryCopyTo(dest);
                Debug.Assert(success, "Should always have enough buffer");
                success = Utf8Parser.TryParse(dest, out value, out consumed, standardFormat);
                ArrayPool<byte>.Shared.Return(buffer);
            }
            if (success)
            {
                Advance(consumed);
            }
            return success;
        }

        /// <summary>
        /// Try parse everything in the sequence and move reader to the end
        /// </summary>
        /// <param name="value"></param>
        /// <param name="standardFormat"></param>
        /// <returns></returns>
        public bool TryParseUtf8(out short value, char standardFormat = '\0')
        {
            bool success;
            int consumed;
            ReadOnlySpan<byte> span = GetUnreadSpan();
            const int maxChars = 8;
            int maxLen = Remaining > maxChars ? maxChars : (int)Remaining;
            if (span.Length >= maxChars || Remaining == span.Length)
            {
                success = Utf8Parser.TryParse(span, out value, out consumed, standardFormat);
            }
            else
            {
                ReadOnlySequence<byte> remaining = Sequence.Slice(Position, maxLen);
                byte[] buffer = ArrayPool<byte>.Shared.Rent(maxLen);
                remaining.CopyTo(buffer);
                success = Utf8Parser.TryParse(buffer.AsSpan()[..maxLen], out value, out consumed, standardFormat);
                ArrayPool<byte>.Shared.Return(buffer);
            }
            if (success && consumed > 0)
            {
                Advance(consumed);
            }
            return success;
        }

        /// <summary>
        /// Try parse everything in the sequence and move reader to the end
        /// </summary>
        /// <param name="value"></param>
        /// <param name="standardFormat"></param>
        /// <returns></returns>
        public bool TryParseUtf8(out ushort value, char standardFormat = '\0')
        {
            bool success;
            int consumed;
            ReadOnlySpan<byte> span = GetUnreadSpan();
            const int maxChars = 7;
            int maxLen = Remaining > maxChars ? maxChars : (int)Remaining;
            if (span.Length >= maxChars || Remaining == span.Length)
            {
                success = Utf8Parser.TryParse(span, out value, out consumed, standardFormat);
            }
            else
            {
                ReadOnlySequence<byte> remaining = Sequence.Slice(Position, maxLen);
                byte[] buffer = ArrayPool<byte>.Shared.Rent(maxLen);
                remaining.CopyTo(buffer);
                success = Utf8Parser.TryParse(buffer.AsSpan()[..maxLen], out value, out consumed, standardFormat);
                ArrayPool<byte>.Shared.Return(buffer);
            }
            if (success && consumed > 0)
            {
                Advance(consumed);
            }
            return success;
        }

        /// <summary>
        /// Try parse everything in the sequence and move reader to the end
        /// </summary>
        /// <param name="value"></param>
        /// <param name="standardFormat"></param>
        /// <returns></returns>
        public bool TryParseUtf8(out int value, char standardFormat = '\0')
        {
            bool success;
            int consumed;
            ReadOnlySpan<byte> span = GetUnreadSpan();
            const int maxChars = 15;
            int maxLen = Remaining > maxChars ? maxChars : (int)Remaining;
            if (span.Length >= maxChars || Remaining == span.Length)
            {
                success = Utf8Parser.TryParse(span, out value, out consumed, standardFormat);
            }
            else
            {
                ReadOnlySequence<byte> remaining = Sequence.Slice(Position, maxLen);
                byte[] buffer = ArrayPool<byte>.Shared.Rent(maxLen);
                remaining.CopyTo(buffer);
                success = Utf8Parser.TryParse(buffer.AsSpan()[..maxLen], out value, out consumed, standardFormat);
                ArrayPool<byte>.Shared.Return(buffer);
            }
            if (success && consumed > 0)
            {
                Advance(consumed);
            }
            return success;
        }

        /// <summary>
        /// Try parse everything in the sequence and move reader to the end
        /// </summary>
        /// <param name="value"></param>
        /// <param name="standardFormat"></param>
        /// <returns></returns>
        public bool TryParseUtf8(out uint value, char standardFormat = '\0')
        {
            bool success;
            int consumed;
            ReadOnlySpan<byte> span = GetUnreadSpan();
            const int maxChars = 14;
            int maxLen = Remaining > maxChars ? maxChars : (int)Remaining;
            if (span.Length >= maxChars || Remaining == span.Length)
            {
                success = Utf8Parser.TryParse(span, out value, out consumed, standardFormat);
            }
            else
            {
                ReadOnlySequence<byte> remaining = Sequence.Slice(Position, maxLen);
                byte[] buffer = ArrayPool<byte>.Shared.Rent(maxLen);
                remaining.CopyTo(buffer);
                success = Utf8Parser.TryParse(buffer.AsSpan()[..maxLen], out value, out consumed, standardFormat);
                ArrayPool<byte>.Shared.Return(buffer);
            }
            if (success && consumed > 0)
            {
                Advance(consumed);
            }
            return success;
        }

        /// <summary>
        /// Try read unmanaged value
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="value"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryReadUnaligned<T>(out T value) where T : unmanaged
        {
            ReadOnlySpan<byte> span = GetUnreadSpan();
            if (span.Length < Unsafe.SizeOf<T>())
            {
                return TryReadUnalignedMultisegment(out value);
            }
            value = Unsafe.ReadUnaligned<T>(ref MemoryMarshal.GetReference(span));
            Advance(Unsafe.SizeOf<T>());
            return true;
        }

        /// <summary>
        /// Reset
        /// </summary>
        internal void ResetReader()
        {
            m_currentSpanIndex = 0;
            Consumed = 0;
            m_curPos = Sequence.Start;
            m_nextPos = m_curPos;

            if (Sequence.TryGet(ref m_nextPos, out ReadOnlyMemory<byte> memory, advance: true))
            {
                m_moreData = true;

                if (memory.Length == 0)
                {
                    m_currentSpan = default;

                    // No data in the first span, move to one with data
                    GetNextSpan();
                }
                else
                {
                    m_currentSpan = memory.Span;
                }
            }
            else
            {
                // No data in any spans and at end of _sequence
                m_moreData = false;
                m_currentSpan = default;
            }
        }

        /// <summary>
        /// Unchecked helper to avoid unnecessary checks where you know count is valid.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AdvanceCurrentSpan(long count)
        {
            Debug.Assert(count >= 0, "count >= 0");

            Consumed += count;
            m_currentSpanIndex += (int)count;
            if (m_currentSpanIndex >= m_currentSpan.Length)
            {
                GetNextSpan();
            }
        }

        /// <summary>
        /// Move to next span if possible
        /// </summary>
        /// <param name="count"></param>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        private void AdvanceToNextSpan(long count)
        {
            ThrowIfNegative(count, nameof(count));
            Consumed += count;
            while (m_moreData)
            {
                int remaining = m_currentSpan.Length - m_currentSpanIndex;

                if (remaining > count)
                {
                    m_currentSpanIndex += (int)count;
                    count = 0;
                    break;
                }

                // As there may not be any further segments we need to
                // push the current index to the end of the span.
                m_currentSpanIndex += remaining;
                count -= remaining;
                Debug.Assert(count >= 0, "count >= 0");

                GetNextSpan();

                if (count == 0)
                {
                    break;
                }
            }

            if (count != 0)
            {
                // Not enough data left- adjust for where we actually ended and throw
                Consumed -= count;
                throw new ArgumentOutOfRangeException(nameof(count),
                    "Cannot advance past end of sequence.");
            }
        }

        /// <summary>
        /// Get the next segment with available data, if any.
        /// </summary>
        private void GetNextSpan()
        {
            if (!m_isLast)
            {
                SequencePosition previousNextPosition = m_nextPos;
                while (Sequence.TryGet(ref m_nextPos, out ReadOnlyMemory<byte> memory, advance: true))
                {
                    m_curPos = previousNextPosition;
                    if (memory.Length > 0)
                    {
                        m_currentSpan = memory.Span;
                        m_currentSpanIndex = 0;
                        return;
                    }
                    m_currentSpan = default;
                    m_currentSpanIndex = 0;
                    previousNextPosition = m_nextPos;
                }
            }
            m_moreData = false;
        }

        /// <summary>
        /// Gets the unread portion of the cirremt süam.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private readonly ReadOnlySpan<byte> GetUnreadSpan()
        {
            return m_currentSpan[m_currentSpanIndex..];
        }

        /// <summary>
        /// Find the token across segments
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
        private readonly bool IsNextSlow(scoped ReadOnlySpan<byte> token)
        {
            ReadOnlySpan<byte> currentSpan = GetUnreadSpan();

            // We should only come in here if we need more data than we have in our current span
            Debug.Assert(currentSpan.Length < token.Length);

            SequencePosition nextPosition = m_nextPos;
            while (token.StartsWith(currentSpan))
            {
                if (token.Length == currentSpan.Length)
                {
                    // Fully matched
                    return true;
                }
                // Need to check the next segment
                while (true)
                {
                    if (!Sequence.TryGet(ref nextPosition, out ReadOnlyMemory<byte> nextSegment, advance: true))
                    {
                        // Nothing left
                        return false;
                    }
                    if (nextSegment.Length > 0)
                    {
                        token = token[currentSpan.Length..];
                        currentSpan = nextSegment.Span;
                        if (currentSpan.Length > token.Length)
                        {
                            currentSpan = currentSpan[..token.Length];
                        }
                        break;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Copy across segments
        /// </summary>
        /// <param name="destination"></param>
        /// <returns></returns>
        private readonly bool TryCopyMultisegment(Span<byte> destination)
        {
            long remaining = Length - Consumed;
            if (remaining < destination.Length)
            {
                return false;
            }

            ReadOnlySpan<byte> firstSpan = GetUnreadSpan();
            Debug.Assert(firstSpan.Length < destination.Length, "firstSpan.Length < destination.Length");
            firstSpan.CopyTo(destination);
            int copied = firstSpan.Length;

            SequencePosition next = m_nextPos;
            while (Sequence.TryGet(ref next, out ReadOnlyMemory<byte> nextSegment, true))
            {
                if (nextSegment.Length > 0)
                {
                    ReadOnlySpan<byte> nextSpan = nextSegment.Span;
                    int toCopy = Math.Min(nextSpan.Length, destination.Length - copied);
                    nextSpan[..toCopy].CopyTo(destination[copied..]);
                    copied += toCopy;
                    if (copied >= destination.Length)
                    {
                        break;
                    }
                }
            }
            return true;
        }

        /// <summary>
        /// Try read but with multi segment
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="value"></param>
        /// <returns></returns>
        private bool TryReadUnalignedMultisegment<T>(out T value) where T : unmanaged
        {
            Debug.Assert(GetUnreadSpan().Length < Unsafe.SizeOf<T>());
            Span<byte> tempSpan = stackalloc byte[Unsafe.SizeOf<T>()];
            if (TryCopyTo(tempSpan))
            {
                value = Unsafe.ReadUnaligned<T>(ref MemoryMarshal.GetReference(tempSpan));
                Advance(Unsafe.SizeOf<T>());
                return true;
            }
            // Not enough data left- adjust for where we actually ended
            AdvanceToEnd();
            value = default;
            return false;
        }

        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        internal readonly string ContentToString => Encoding.UTF8.GetString(
            Sequence
                .Slice(Position, Math.Min(Remaining, 100))
#if !NET8_0_OR_GREATER
                .ToArray()
#endif
            );

        private static void ThrowIfNegative(long count, string paramName)
        {
            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(
                    paramName, "Value was negative");
            }
        }

        private static void ThrowIfNegativeOrZeroOrGreaterThan(
            long count, long max)
        {
            if (count <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(count), "Value was negative or 0");
            }
            if (count > max)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(count), $"Value was greater than {max}");
            }
        }

        private SequencePosition m_nextPos;
        private SequencePosition m_curPos;
        private ReadOnlySpan<byte> m_currentSpan;
        private int m_currentSpanIndex;
        private bool m_isLast;
        private bool m_moreData;
    }
}
