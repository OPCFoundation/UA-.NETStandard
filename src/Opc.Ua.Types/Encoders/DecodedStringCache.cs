// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

#if NET6_0_OR_GREATER
using System;
using System.Buffers.Binary;
using System.Text;

namespace Opc.Ua
{
    /// <summary>
    /// Reuses the instances of short strings that the binary decoder decoded recently
    /// on the current thread.
    /// </summary>
    /// <remarks>
    /// Values of one message repeat the same short strings: every event of a Publish
    /// response carries the same source name and often the same message text and locale,
    /// and a Read or Write request repeats the identifiers of string node ids. Decoding
    /// allocated a new string for each of them. The cache is direct mapped by a hash of
    /// the UTF-8 bytes, holds a fixed number of entries per thread and compares the
    /// full value before it returns an entry, so the decoded value never changes; only
    /// the instance is shared, which is safe because strings are immutable. A hit on an
    /// ASCII string compares the bytes with the cached characters without decoding
    /// them, so it costs less than the allocation it replaces; other strings are
    /// decoded and compared as characters.
    /// The length limit admits typical string node ids such as
    /// <c>Scalar_Static_Mass_Int32_Int32_00</c> (33 bytes), and the entry count keeps a
    /// request of a hundred distinct ids mostly free of slot collisions; the table
    /// costs 8 KB per decoding thread.
    /// </remarks>
    internal static class DecodedStringCache
    {
        /// <summary>
        /// The longest UTF-8 encoded string, in bytes, that the cache holds.
        /// </summary>
        public const int MaxUtf8Length = 64;

        /// <summary>
        /// Returns the string for the UTF-8 bytes, reusing the instance decoded last
        /// for the same characters on this thread.
        /// </summary>
        /// <param name="utf8">At most <see cref="MaxUtf8Length"/> UTF-8 bytes.</param>
        public static string GetOrAdd(ReadOnlySpan<byte> utf8)
        {
            string?[] entries = t_entries ??= new string?[kEntries];
            int index = Hash(utf8) & (kEntries - 1);
            string? cached = entries[index];
#if NET8_0_OR_GREATER
            // ASCII, the common case: a hit compares the bytes with the cached characters
            // and skips decoding. Equal ASCII bytes decode to exactly these characters.
            if (cached != null && cached.Length == utf8.Length && Ascii.Equals(utf8, cached))
            {
                return cached;
            }
#endif

            // UTF-8 never decodes to more characters than it has bytes; invalid
            // sequences become one replacement character each, as in GetString.
            Span<char> buffer = stackalloc char[MaxUtf8Length];
            ReadOnlySpan<char> value = buffer[..Encoding.UTF8.GetChars(utf8, buffer)];
            if (cached != null && value.SequenceEqual(cached))
            {
                return cached;
            }

            string created = new(value);
            entries[index] = created;
            return created;
        }

        /// <summary>
        /// Hashes the UTF-8 bytes eight at a time. The hash only selects the entry and
        /// a hit always compares the full value, so inputs that share a hash cost a
        /// miss, never a wrong result.
        /// </summary>
        private static int Hash(ReadOnlySpan<byte> utf8)
        {
            ulong hash = (ulong)utf8.Length;
            int offset = 0;
            for (; offset + sizeof(ulong) <= utf8.Length; offset += sizeof(ulong))
            {
                hash = Mix(hash ^ BinaryPrimitives.ReadUInt64LittleEndian(utf8[offset..]));
            }
            if (offset < utf8.Length)
            {
                ulong tail = 0;
                for (int ii = offset; ii < utf8.Length; ii++)
                {
                    tail = (tail << 8) | utf8[ii];
                }
                hash = Mix(hash ^ tail);
            }
            return (int)Mix(hash);
        }

        /// <summary>
        /// Spreads the bits of the value: the multiplication carries each bit into the
        /// higher bits and the shift folds the upper half back into the lower one.
        /// </summary>
        private static ulong Mix(ulong value)
        {
            value *= 0x9E3779B97F4A7C15UL;
            return value ^ (value >> 32);
        }

        /// <summary>
        /// The number of entries per thread, a power of two.
        /// </summary>
        private const int kEntries = 1024;

        [ThreadStatic]
        private static string?[]? t_entries;
    }
}
#endif
