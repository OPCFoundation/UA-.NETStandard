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

namespace System.Text
{
    /// <summary>
    /// Polyfills for span based Encoding methods that are not available in
    /// .NET Standard 2.0 or .NET Framework. The span overloads are provided
    /// in the box on .NET Standard 2.1 and .NET 5 or later. The fallbacks
    /// allocate an intermediate array and are only compiled on the legacy
    /// target frameworks.
    /// </summary>
    public static class Polyfills
    {
#if NETSTANDARD2_0 || NETFRAMEWORK
        /// <summary>
        /// Decodes the bytes into a string. Empty input is handled explicitly to
        /// avoid pinning an empty span to a null pointer.
        /// </summary>
        public static string GetString(this Encoding encoding, ReadOnlySpan<byte> bytes)
        {
            return bytes.IsEmpty ? string.Empty : encoding.GetString(bytes.ToArray());
        }

        /// <summary>
        /// Encodes the characters into the destination span and returns the byte count.
        /// </summary>
        public static int GetBytes(this Encoding encoding, ReadOnlySpan<char> chars, Span<byte> bytes)
        {
            if (chars.IsEmpty)
            {
                return 0;
            }

            byte[] encoded = encoding.GetBytes(chars.ToArray());
            encoded.AsSpan().CopyTo(bytes);
            return encoded.Length;
        }
#endif
    }
}
