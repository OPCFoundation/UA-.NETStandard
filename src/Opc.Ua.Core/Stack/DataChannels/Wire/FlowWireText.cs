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

namespace Opc.Ua.DataChannels.Wire
{
    /// <summary>
    /// Provides strict Unicode validation without replacement fallback or runtime-specific encodings.
    /// </summary>
    internal static class FlowWireText
    {
        /// <summary>The maximum encoded length of an absolute URI.</summary>
        internal const int MaxUriBytes = 4096;

        /// <summary>The maximum encoded length of a content type.</summary>
        internal const int MaxContentTypeBytes = 255;

        /// <summary>
        /// Rejects invalid scalar encodings, including overlong UTF-8 and encoded surrogates.
        /// </summary>
        /// <exception cref="FlowCodecException"></exception>
        internal static void ValidateUtf8(ReadOnlySpan<byte> bytes)
        {
            for (int i = 0; i < bytes.Length; i++)
            {
                byte lead = bytes[i];
                if (lead < 0x80)
                {
                    continue;
                }
                int count = lead is >= 0xC2 and <= 0xDF ? 2 :
                    lead is >= 0xE0 and <= 0xEF ? 3 :
                    lead is >= 0xF0 and <= 0xF4 ? 4 : 0;
                if (count == 0 || bytes.Length - i < count)
                {
                    throw new FlowCodecException(FlowCodecError.InvalidUtf8);
                }
                for (int j = 1; j < count; j++)
                {
                    if ((bytes[i + j] & 0xC0) != 0x80)
                    {
                        throw new FlowCodecException(FlowCodecError.InvalidUtf8);
                    }
                }
                byte second = bytes[i + 1];
                if ((lead == 0xE0 && second < 0xA0) ||
                    (lead == 0xED && second >= 0xA0) ||
                    (lead == 0xF0 && second < 0x90) ||
                    (lead == 0xF4 && second >= 0x90))
                {
                    throw new FlowCodecException(FlowCodecError.InvalidUtf8);
                }
                i += count - 1;
            }
        }

        /// <summary>
        /// Counts valid Unicode scalars as UTF-8 bytes without allocating an encoded copy.
        /// </summary>
        /// <exception cref="FlowCodecException"></exception>
        internal static int Utf8Length(string text)
        {
            int length = 0;
            for (int i = 0; i < text.Length; i++)
            {
                int scalar = ReadScalar(text, ref i);
                int count = scalar < 0x80 ? 1 : scalar < 0x800 ? 2 : scalar < 0x10000 ? 3 : 4;
                if (length > int.MaxValue - count)
                {
                    throw new FlowCodecException(FlowCodecError.TooLarge);
                }
                length += count;
            }
            return length;
        }

        /// <summary>
        /// Reads one Unicode scalar, advancing over a low surrogate when necessary.
        /// </summary>
        /// <exception cref="FlowCodecException"></exception>
        internal static int ReadScalar(string text, ref int index)
        {
            int scalar = text[index];
            if (scalar is >= 0xD800 and <= 0xDBFF)
            {
                if (index + 1 == text.Length || text[index + 1] is < '\uDC00' or > '\uDFFF')
                {
                    throw new FlowCodecException(FlowCodecError.InvalidUnicode);
                }
                return 0x10000 + ((scalar - 0xD800) << 10) + text[++index] - 0xDC00;
            }
            else if (scalar is >= 0xDC00 and <= 0xDFFF)
            {
                throw new FlowCodecException(FlowCodecError.InvalidUnicode);
            }
            return scalar;
        }

        /// <summary>
        /// Checks an absolute identifier while preserving its original string.
        /// </summary>
        /// <exception cref="FlowCodecException"></exception>
        internal static void ValidateUri(string text)
        {
            if (Utf8Length(text) > MaxUriBytes)
            {
                throw new FlowCodecException(FlowCodecError.TooLarge);
            }
            if (!Uri.IsWellFormedUriString(text, UriKind.Absolute))
            {
                throw new FlowCodecException(FlowCodecError.InvalidUri);
            }
        }
    }
}
