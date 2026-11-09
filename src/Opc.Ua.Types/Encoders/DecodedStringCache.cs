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

#if NET6_0_OR_GREATER
using System;
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
    /// allocated a new string for each of them. The cache is direct mapped, holds a
    /// fixed number of entries per thread and compares the decoded characters before it
    /// returns an entry, so the decoded value never changes; only the instance is
    /// shared, which is safe because strings are immutable. A miss costs a hash of the
    /// decoded characters in addition to the allocation it replaces.
    /// </remarks>
    internal static class DecodedStringCache
    {
        /// <summary>
        /// The longest UTF-8 encoded string, in bytes, that the cache holds.
        /// </summary>
        public const int MaxUtf8Length = 32;

        /// <summary>
        /// Returns the string for the UTF-8 bytes, reusing the instance decoded last
        /// for the same characters on this thread.
        /// </summary>
        /// <param name="utf8">At most <see cref="MaxUtf8Length"/> UTF-8 bytes.</param>
        public static string GetOrAdd(ReadOnlySpan<byte> utf8)
        {
            // UTF-8 never decodes to more characters than it has bytes; invalid
            // sequences become one replacement character each, as in GetString.
            Span<char> buffer = stackalloc char[MaxUtf8Length];
            ReadOnlySpan<char> value = buffer[..Encoding.UTF8.GetChars(utf8, buffer)];

            string?[] entries = t_entries ??= new string?[kEntries];
            int index = string.GetHashCode(value, StringComparison.Ordinal) & (kEntries - 1);
            string? cached = entries[index];
            if (cached != null && value.SequenceEqual(cached))
            {
                return cached;
            }

            string created = new(value);
            entries[index] = created;
            return created;
        }

        /// <summary>
        /// The number of entries per thread, a power of two.
        /// </summary>
        private const int kEntries = 128;

        [ThreadStatic]
        private static string?[]? t_entries;
    }
}
#endif
