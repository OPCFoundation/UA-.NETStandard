/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
