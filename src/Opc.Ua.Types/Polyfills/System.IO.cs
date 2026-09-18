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

namespace System.IO
{
    /// <summary>
    /// Polyfills for System.Io methods that are not available in .NET Standard 2.0 or .NET Framework.
    /// </summary>
    public static class Polyfills
    {
#if NETSTANDARD2_0 || NETFRAMEWORK
        /// <summary>
        /// Contains a character in a string using a specified comparison type.
        /// </summary>
        public static void Write(this TextWriter target, ReadOnlySpan<char> value)
        {
            target.Write(value.ToString());
        }

        /// <summary>
        /// Contains a character in a string using a specified comparison type.
        /// </summary>
        public static void Write(this BinaryWriter target, ReadOnlySpan<byte> value)
        {
            target.Write(value.ToArray());
        }

        /// <summary>
        /// Reads a sequence of bytes from the stream into the span.
        /// </summary>
        public static int Read(this Stream stream, Span<byte> buffer)
        {
            byte[] rented = Buffers.ArrayPool<byte>.Shared.Rent(buffer.Length);
            try
            {
                int read = stream.Read(rented, 0, buffer.Length);
                if (read > 0)
                {
                    rented.AsSpan(0, read).CopyTo(buffer);
                }
                return read;
            }
            finally
            {
                Buffers.ArrayPool<byte>.Shared.Return(rented);
            }
        }

        /// <summary>
        /// Writes a span of bytes to the stream.
        /// </summary>
        public static void Write(this Stream stream, ReadOnlySpan<byte> buffer)
        {
            byte[] rented = Buffers.ArrayPool<byte>.Shared.Rent(buffer.Length);
            try
            {
                buffer.CopyTo(rented);
                stream.Write(rented, 0, buffer.Length);
            }
            finally
            {
                Buffers.ArrayPool<byte>.Shared.Return(rented);
            }
        }
#endif

#if !NET7_0_OR_GREATER
        /// <summary>
        /// Reads exactly the number of bytes required to fill the span, throwing
        /// <see cref="EndOfStreamException"/> if the stream ends before the span is filled.
        /// </summary>
        public static void ReadExactly(this Stream stream, Span<byte> buffer)
        {
            int total = 0;
            while (total < buffer.Length)
            {
                int read = stream.Read(buffer.Slice(total));
                if (read <= 0)
                {
                    throw new EndOfStreamException();
                }
                total += read;
            }
        }
#endif
    }
}
