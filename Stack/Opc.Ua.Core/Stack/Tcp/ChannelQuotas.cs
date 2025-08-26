/* Copyright (c) 1996-2022 The OPC Foundation. All rights reserved.
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

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Stores various configuration parameters used by the channel.
    /// </summary>
    public class ChannelQuotas
    {
        /// <summary>
        /// Creates an object with default values.
        /// </summary>
        public ChannelQuotas()
        {
            MessageContext = ServiceMessageContext.GlobalContext;
            MaxMessageSize = TcpMessageLimits.DefaultMaxMessageSize;
            MaxBufferSize = TcpMessageLimits.DefaultMaxBufferSize;
            ChannelLifetime = TcpMessageLimits.DefaultChannelLifetime;
            SecurityTokenLifetime = TcpMessageLimits.DefaultSecurityTokenLifeTime;
        }

        /// <summary>
        /// The context to use when encoding/decoding messages.
        /// </summary>
        public IServiceMessageContext MessageContext { get; set; }

        /// <summary>
        /// Validator to use when handling certificates.
        /// </summary>
        public ICertificateValidator CertificateValidator { get; set; }

        /// <summary>
        /// The maximum size for a message sent or received.
        /// </summary>
        public int MaxMessageSize { get; set; }

        /// <summary>
        /// The maximum size for the send or receive buffers.
        /// </summary>
        public int MaxBufferSize { get; set; }

        /// <summary>
        /// The default lifetime for the channel in milliseconds.
        /// </summary>
        public int ChannelLifetime { get; set; }

        /// <summary>
        /// The default lifetime for a security token in milliseconds.
        /// </summary>
        public int SecurityTokenLifetime { get; set; }
    }
}
