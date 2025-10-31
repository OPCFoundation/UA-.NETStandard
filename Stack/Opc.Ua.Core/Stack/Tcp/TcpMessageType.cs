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
            return (actualType & MessageTypeMask) == expectedType;
        }

        /// <summary>
        /// Returns true if the message type indicates it is a final chunk.
        /// </summary>
        public static bool IsFinal(uint messageType)
        {
            return (messageType & ChunkTypeMask) == Final;
        }

        /// <summary>
        /// Returns true if the message type indicates it is a abort chunk.
        /// </summary>
        public static bool IsAbort(uint messageType)
        {
            return (messageType & ChunkTypeMask) == Abort;
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
                    return true;
                default:
                    uint chunkTypeMask = messageType & ChunkTypeMask;
                    if (chunkTypeMask is not Final and not Intermediate and not Abort)
                    {
                        return false;
                    }
                    switch (messageType & MessageTypeMask)
                    {
                        case Message:
                        case Open:
                        case Close:
                            return true;
                        default:
                            return false;
                    }

            }
        }
    }

}
