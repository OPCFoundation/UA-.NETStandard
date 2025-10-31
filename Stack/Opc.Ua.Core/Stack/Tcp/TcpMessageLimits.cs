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
    /// Defines constants for the UA TCP message identifiers.
    /// </summary>
    public static class TcpMessageLimits
    {
        /// <summary>
        /// The size of the message type and size prefix in each message.
        /// </summary>
        public const int MessageTypeAndSize = 8;

        /// <summary>
        /// The minimum send or receive buffer size for an ECC security profile.
        /// </summary>
        public const int ECCMinBufferSize = 1024;

        /// <summary>
        /// The minimum send or receive buffer size.
        /// </summary>
        public const int MinBufferSize = 8192;

        /// <summary>
        /// Minimum message body size
        /// </summary>
        public const int MinBodySize = 1;

        /// <summary>
        /// The maximum send or receive buffer size.
        /// </summary>
        public const int MaxBufferSize = 8192 * 18;

        /// <summary>
        /// The maximum length for the reason in an error message.
        /// </summary>
        public const int MaxErrorReasonLength = 4096;

        /// <summary>
        /// The maximum length for the endpoint url in the hello message.
        /// </summary>
        public const int MaxEndpointUrlLength = 4096;

        /// <summary>
        /// The maximum length for an x509 certificate.
        /// </summary>
        public const int MaxCertificateSize = 7500;

        /// <summary>
        /// The maximum length for an a security policy uri.
        /// </summary>
        public const int MaxSecurityPolicyUriSize = 256;

        /// <summary>
        /// The length of the base message header.
        /// </summary>
        public const int BaseHeaderSize = 12;

        /// <summary>
        /// The length of the message header use with symmetric cryptography.
        /// </summary>
        public const int SymmetricHeaderSize = 16;

        /// <summary>
        /// The length of the sequence message header.
        /// </summary>
        public const int SequenceHeaderSize = 8;

        /// <summary>
        /// The length a X509 certificate thumbprint.
        /// </summary>
        public const int CertificateThumbprintSize = 20;

        /// <summary>
        /// The number of bytes required to specify the length of an encoding string or bytestring.
        /// </summary>
        public const int StringLengthSize = 4;

        /// <summary>
        /// Sequence numbers may only rollover if they are larger than this value.
        /// </summary>
        public const uint MinSequenceNumber = uint.MaxValue - 1024;

        /// <summary>
        /// The first sequence number after a rollover must be less than this value.
        /// </summary>
        public const uint MaxRolloverSequenceNumber = 1024;

        /// <summary>
        /// The default buffer size to use for communication.
        /// </summary>
        public const int DefaultMaxBufferSize = ushort.MaxValue;

        /// <summary>
        /// The default maximum chunk count for Request and Response messages.
        /// </summary>
        public const int DefaultMaxChunkCount = DefaultMaxMessageSize / MinBufferSize;

        /// <summary>
        /// The default maximum message size.
        /// </summary>
        /// <remarks>
        /// The default is 2MB. Ensure to set this to a value aligned to <see cref="MinBufferSize"/>.
        /// This default is for the Tcp transport. <see cref="DefaultEncodingLimits.MaxMessageSize"/> for the generic default.
        /// </remarks>
        public const int DefaultMaxMessageSize = MinBufferSize * 256;

        /// <summary>
        /// The default maximum message size for the discovery channel.
        /// </summary>
        public const int DefaultDiscoveryMaxMessageSize = DefaultMaxBufferSize;

        /// <summary>
        /// How long processing of a service call can take before it goes into a faulted state.
        /// </summary>
        public const int DefaultOperationTimeout = 120000;

        /// <summary>
        /// How long a secure channel will remain in the server after it goes into a faulted state.
        /// </summary>
        public const int DefaultChannelLifetime = 30000;

        /// <summary>
        /// How long a security token lasts before it needs to be renewed.
        /// </summary>
        public const int DefaultSecurityTokenLifeTime = 3600000;

        /// <summary>
        /// The minimum lifetime for a security token lasts before it needs to be renewed.
        /// </summary>
        public const int MinSecurityTokenLifeTime = 60000;

        /// <summary>
        /// The minimum time interval between reconnect attempts.
        /// </summary>
        public const int MinTimeBetweenReconnects = 0;

        /// <summary>
        /// The maximum time interval between reconnect attempts.
        /// </summary>
        public const int MaxTimeBetweenReconnects = 120000;

        /// <summary>
        /// The fraction of the lifetime to wait before renewing a token.
        /// </summary>
        public const double TokenRenewalPeriod = 0.75;

        /// <summary>
        /// The fraction of the lifetime to jitter renewing a token.
        /// </summary>
        public const double TokenRenewalJitterPeriod = 0.05;

        /// <summary>
        /// The fraction of the lifetime to wait before forcing the activation of the renewed token.
        /// </summary>
        public const double TokenActivationPeriod = 0.95;

        /// <summary>
        /// The certificates that have the key size larger than KeySizeExtraPadding need an extra padding byte in the transport message
        /// </summary>
        public const int KeySizeExtraPadding = 2048;

        /// <summary>
        /// Aligns the max message size to the nearest min buffer size.
        /// </summary>
        /// <remarks>
        /// Align user configured maximum message size to avoid rounding errors in other UA implementations.
        /// </remarks>
        public static int AlignRoundMaxMessageSize(int value)
        {
            const int alignmentMask = MinBufferSize - 1;
            return (value + alignmentMask) & ~alignmentMask;
        }
    }
}
