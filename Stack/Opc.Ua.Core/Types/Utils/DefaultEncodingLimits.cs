/* Copyright (c) 1996-2024 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation Corporate Members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;
using Opc.Ua.Bindings;

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
        public static readonly int MaxStringLength = UInt16.MaxValue;

        /// <summary>
        /// The default maximum length for any array.
        /// </summary>
        public static readonly int MaxArrayLength = UInt16.MaxValue;

        /// <summary>
        /// The default maximum length for any ByteString.
        /// </summary>
        public static readonly int MaxByteStringLength = UInt16.MaxValue * 16;

        /// <summary>
        /// The default maximum length for any Message.
        /// </summary>
        /// <remarks>
        /// Default is 2MB. Set to multiple of MinBufferSize to avoid rounding errors in other UA implementations.
        /// </remarks>
        public static readonly int MaxMessageSize = TcpMessageLimits.MinBufferSize * 256;

        /// <summary>
        /// The default maximum nesting level accepted while encoding or decoding objects.
        /// </summary>
        public static readonly int MaxEncodingNestingLevels = 200;

        /// <summary>
        /// The default number of times the decoder can recover from an error 
        /// caused by an encoded ExtensionObject before throwing a decoder error.
        /// </summary>
        public static readonly int MaxDecoderRecoveries = 0;
    }
}
