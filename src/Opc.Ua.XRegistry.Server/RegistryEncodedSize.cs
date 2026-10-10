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

namespace Opc.Ua.XRegistry.Server
{
    /// <summary>
    /// Measures the actual UA Binary size of a value without buffering it.
    /// </summary>
    public static class RegistryEncodedSize
    {
        /// <summary>
        /// Returns the encoded size, or fails with Bad_EncodingLimitsExceeded beyond <paramref name="maximum"/>.
        /// </summary>
        public static long Measure(IEncodeable value, IServiceMessageContext context, long maximum)
        {
            if (value is null)
            {
                throw new ArgumentNullException(nameof(value));
            }
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (maximum <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximum));
            }
            using var stream = new CountingStream(maximum);
            using var encoder = new BinaryEncoder(stream, context, true);
            value.Encode(encoder);
            return stream.Length;
        }

        private sealed class CountingStream(long maximum) : Stream
        {
            public override bool CanRead => false;

            public override bool CanSeek => true;

            public override bool CanWrite => true;

            public override long Length => m_length;

            public override long Position
            {
                get => m_position;
                set
                {
                    if (value < 0 || value > maximum)
                    {
                        throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                    }
                    m_position = value;
                }
            }

            public override void Flush()
            {
            }

            public override int Read(byte[] buffer, int offset, int count) =>
                throw new NotSupportedException("The stream only measures encoded bytes.");

            public override long Seek(long offset, SeekOrigin origin)
            {
                Position = checked(offset + (origin switch
                {
                    SeekOrigin.Begin => 0,
                    SeekOrigin.Current => m_position,
                    SeekOrigin.End => m_length,
                    _ => throw new ArgumentOutOfRangeException(nameof(origin))
                }));
                return m_position;
            }

            public override void SetLength(long value)
            {
                if (value < 0 || value > maximum)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }
                m_length = value;
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                if (buffer is null || offset < 0 || count < 0 || offset > buffer.Length - count)
                {
                    throw new ArgumentException("Invalid encoded byte range.");
                }
                Advance(count);
            }

#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
            public override void Write(ReadOnlySpan<byte> buffer) => Advance(buffer.Length);
#endif

            public override void WriteByte(byte value) => Advance(1);

            private void Advance(int count)
            {
                if (m_position > maximum - count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }
                m_position += count;
                m_length = Math.Max(m_length, m_position);
            }

            private long m_position;
            private long m_length;
        }
    }
}
