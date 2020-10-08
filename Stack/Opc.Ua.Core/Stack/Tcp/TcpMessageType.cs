/* Copyright (c) 1996-2020 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Defines constants for the UA TCP message identifiers.
    /// </summary>
    public static class TcpMessageType
    {
        /// <summary>
        /// A final chunk for a message.
        /// </summary>
        public const uint Final = 0x46000000;

        /// <summary>
        /// An intermediate chunk for a message.
        /// </summary>
        public const uint Intermediate = 0x43000000;

        /// <summary>
        /// A final chunk for a message which indicates that the message has been aborted by the sender.
        /// </summary>
        public const uint Abort = 0x41000000;

        /// <summary>
        /// A mask used to select the message type portion of the message id.
        /// </summary>
        public const uint MessageTypeMask = 0x00FFFFFF;

        /// <summary>
        /// A mask used to select the chunk type portion of the message id.
        /// </summary>
        public const uint ChunkTypeMask = 0xFF000000;

        /// <summary>
        /// A chunk for a generic message.
        /// </summary>
        public const uint MessageIntermediate = Message | Intermediate;
        
        /// <summary>
        /// A chunk for a generic message.
        /// </summary>
        public const uint MessageFinal = Message | Final;

        /// <summary>
        /// A chunk for a generic message.
        /// </summary>
        public const uint Message = 0x0047534D;

        /// <summary>
        /// A chunk for an OpenSecureChannel message.
        /// </summary>
        public const uint Open = 0x004E504F;

        /// <summary>
        /// A chunk for a CloseSecureChannel message.
        /// </summary>
        public const uint Close = 0x004F4C43;

        /// <summary>
        /// A hello message.
        /// </summary>
        public const uint Hello = 0x464C4548;

        /// <summary>
        /// A reverse hello message.
        /// </summary>
        public const uint ReverseHello = 0x46454852;

        /// <summary>
        /// An acknowledge message.
        /// </summary>
        public const uint Acknowledge = 0x464B4341;

        /// <summary>
        /// An error message.
        /// </summary>
        public const uint Error = 0x46525245;

        /// <summary>
        /// Returns true if the message type is equal to the expected type.
        /// </summary>
        public static bool IsType(uint actualType, uint expectedType)
        {
            return ((actualType & MessageTypeMask) == expectedType);
        }

        /// <summary>
        /// Returns true if the message type indicates it is a final chunk.
        /// </summary>
        public static bool IsFinal(uint messageType)
        {
            return ((messageType & ChunkTypeMask) == Final);
        }

        /// <summary>
        /// Returns true if the message type indicates it is a abort chunk.
        /// </summary>
        public static bool IsAbort(uint messageType)
        {
            return ((messageType & ChunkTypeMask) == Abort);
        }

        /// <summary>
        /// Returns true if the message type is recognized.
        /// </summary>
        public static bool IsValid(uint messageType)
        {
            switch (messageType)
            {
                case Hello:
                case ReverseHello:
                case Acknowledge:
                case Error:
                    {
                        return true;
                    }
            }

            if (((messageType & ChunkTypeMask) != Final) && ((messageType & ChunkTypeMask) != Intermediate))
            {
                return false;
            }

            switch (messageType & MessageTypeMask)
            {
                case Message:
                case Open:
                case Close:
                    {
                        break;
                    }

                default:
                    {
                        return false;
                    }
            }

            return true;
        }
    }

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
        /// The minimum send or receive buffer size.
        /// </summary>
        public const int MinBufferSize = 8192;

        /// <summary>
        /// Minimum message body size
        /// </summary>
        public const int MinBodySize = 1;

        /// <summary>
        /// The minimum send or receive buffer size.
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
        public const uint MinSequenceNumber = UInt32.MaxValue - 1024;

        /// <summary>
        /// The first sequence number after a rollover must be less than this value.
        /// </summary>
        public const uint MaxRolloverSequenceNumber = 1024;

        /// <summary>
        /// The default buffer size to use for communication.
        /// </summary>
        public const int DefaultMaxBufferSize = 65535;

        /// <summary>
        /// The default maximum message size.
        /// </summary>
        public const int DefaultMaxMessageSize = 16 * 65535;

        /// <summary>
        /// How long a connection will remain in the server after it goes into a faulted state.
        /// </summary>
        public const int DefaultChannelLifetime = 60000;

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
        /// The fraction of the lifetime to wait before forcing the activation of the renewed token.
        /// </summary>
        public const double TokenActivationPeriod = 0.95;

        /// <summary>
        /// The certificates that have the key size larger than KeySizeExtraPadding need an extra padding byte in the transport message
        /// </summary>
        public const int KeySizeExtraPadding = 2048;
    }
}
