/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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

#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
using System;

namespace Opc.Ua.Core.Tests.Types.Encoders
{
    /// <summary>
    /// Adds IEncoder Extension Method for tests targeting a netstandard 2.0 assembly (no span support) but being run on net 8 or higher
    /// </summary>
    internal static class EncoderSpanExtensions
    {
        /// <summary>
        /// Bridges encoder.WriteByteString(string, ReadOnlySpan<byte>) to the byte[] overloads.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="encoder"/> is <c>null</c>.</exception>
        public static void WriteByteString(this IEncoder encoder, string fieldName, ReadOnlySpan<byte> value)
        {
            if (encoder == null)
            {
                throw new ArgumentNullException(nameof(encoder));
            }

            // Preserve test expectations:
            // - value == ReadOnlySpan<byte>.Empty (null/default span) => encode null
            // - value.IsEmpty (but not Equal to .Empty) => encode empty array
            // - otherwise => encode the span content
            if (value == ReadOnlySpan<byte>.Empty)
            {
                encoder.WriteByteString(fieldName, null);
                return;
            }

            if (value.IsEmpty)
            {
                encoder.WriteByteString(fieldName, []);
                return;
            }

            encoder.WriteByteString(fieldName, value.ToArray());
        }
    }
}
#endif

