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

namespace Opc.Ua
{
    /// <summary>
    /// Defaults for encoders while encoding and decoding messages.
    /// Passed to encoders in <see cref="IServiceMessageContext"/>.
    /// </summary>
    public static class DefaultEncodingLimits
    {
        /// <summary>
        /// The default maximum length for any string, byte string or xml element.
        /// </summary>
        public static readonly int MaxStringLength = ushort.MaxValue;

        /// <summary>
        /// The default maximum length for any array.
        /// </summary>
        public static readonly int MaxArrayLength = ushort.MaxValue;

        /// <summary>
        /// The default maximum length for any ByteString.
        /// </summary>
        public static readonly int MaxByteStringLength = ushort.MaxValue * 16;

        /// <summary>
        /// The default maximum length for any Message.
        /// </summary>
        /// <remarks>
        /// Default is 2MB. Set to multiple of MinBufferSize to avoid rounding errors in other UA implementations.
        /// </remarks>
        public static readonly int MaxMessageSize = 8192 * 256;

        /// <summary>
        /// The default maximum nesting level accepted while encoding or decoding objects.
        /// </summary>
        public static readonly int MaxEncodingNestingLevels = 200;

        /// <summary>
        /// The default number of times the decoder can recover from an error
        /// caused by an encoded ExtensionObject before throwing a decoder error.
        /// </summary>
        public static readonly int MaxDecoderRecoveries;
    }
}
