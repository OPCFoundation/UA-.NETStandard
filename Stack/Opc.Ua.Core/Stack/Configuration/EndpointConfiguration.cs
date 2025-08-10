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

using Opc.Ua.Bindings;

namespace Opc.Ua
{
    /// <summary>
    /// Describes how to connect to an endpoint.
    /// </summary>
    public partial class EndpointConfiguration
    {
        /// <summary>
        /// Creates an instance of a configuration with reasonable default values.
        /// </summary>
        public static EndpointConfiguration Create()
        {
            return new EndpointConfiguration
            {
                // message defaults
                OperationTimeout = TcpMessageLimits.DefaultOperationTimeout,
                UseBinaryEncoding = true,
                MaxMessageSize = TcpMessageLimits.DefaultMaxMessageSize,
                MaxBufferSize = TcpMessageLimits.DefaultMaxBufferSize,
                ChannelLifetime = TcpMessageLimits.DefaultChannelLifetime,
                SecurityTokenLifetime = TcpMessageLimits.DefaultSecurityTokenLifeTime,

                // encoding defaults
                MaxArrayLength = DefaultEncodingLimits.MaxArrayLength,
                MaxByteStringLength = DefaultEncodingLimits.MaxByteStringLength,
                MaxStringLength = DefaultEncodingLimits.MaxStringLength,
                MaxEncodingNestingLevels = DefaultEncodingLimits.MaxEncodingNestingLevels,
                MaxDecoderRecoveries = DefaultEncodingLimits.MaxDecoderRecoveries,
            };
        }

        /// <summary>
        /// Creates an instance of a configuration with reasonable default values.
        /// </summary>
        public static EndpointConfiguration Create(ApplicationConfiguration applicationConfiguration)
        {
            if (applicationConfiguration == null || applicationConfiguration.TransportQuotas == null)
            {
                return Create();
            }

            return new EndpointConfiguration
            {
                OperationTimeout = applicationConfiguration.TransportQuotas.OperationTimeout,
                UseBinaryEncoding = true,
                MaxArrayLength = applicationConfiguration.TransportQuotas.MaxArrayLength,
                MaxByteStringLength = applicationConfiguration.TransportQuotas.MaxByteStringLength,
                MaxMessageSize = applicationConfiguration.TransportQuotas.MaxMessageSize,
                MaxStringLength = applicationConfiguration.TransportQuotas.MaxStringLength,
                MaxBufferSize = applicationConfiguration.TransportQuotas.MaxBufferSize,
                MaxEncodingNestingLevels = applicationConfiguration.TransportQuotas.MaxEncodingNestingLevels,
                MaxDecoderRecoveries = applicationConfiguration.TransportQuotas.MaxDecoderRecoveries,
                ChannelLifetime = applicationConfiguration.TransportQuotas.ChannelLifetime,
                SecurityTokenLifetime = applicationConfiguration.TransportQuotas.SecurityTokenLifetime,
            };
        }

        /// <summary>
        /// The maximum nesting level accepted while encoding or decoding objects.
        /// </summary>
        public int MaxEncodingNestingLevels
        {
            get =>
                m_maxEncodingNestingLevels <= 0
                    ? DefaultEncodingLimits.MaxEncodingNestingLevels
                    : m_maxEncodingNestingLevels;
            set => m_maxEncodingNestingLevels = value;
        }

        /// <summary>
        /// The number of times the decoder can recover from an error
        /// caused by an encoded ExtensionObject before throwing a decoder error.
        /// </summary>
        public int MaxDecoderRecoveries { get; set; }

        private int m_maxEncodingNestingLevels;
    }
}
