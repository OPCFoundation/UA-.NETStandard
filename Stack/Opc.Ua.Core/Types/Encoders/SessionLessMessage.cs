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

using System;
using System.IO;
using System.Text;

namespace Opc.Ua
{
    /// <summary>
    /// Extensions for encoders to encode or decode session-less messages.
    /// </summary>
    public static class SessionLessMessage
    {
        /// <summary>
        /// Decodes a session-less message from a json.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="buffer"/>
        /// is <c>null</c>.</exception>
        public static IEncodeable DecodeAsJson(
            byte[] buffer,
            IServiceMessageContext context)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            using var decoder = new JsonDecoder(Encoding.UTF8.GetString(buffer), context);
            // decode the actual message.
            var message = new SessionLessServiceMessage();
            message.Decode(decoder);
            return message.Message;
        }

        /// <summary>
        /// Encodes a session-less message to a buffer.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="message"/>
        /// is <c>null</c>.</exception>
        /// <exception cref="ServiceResultException"></exception>
        public static void EncodeAsJson(
            IEncodeable message,
            Stream stream,
            IServiceMessageContext context,
            bool leaveOpen)
        {
            if (message == null)
            {
                throw new ArgumentNullException(nameof(message));
            }

            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            // create encoder.
            var encoder = new JsonEncoder(context, true, false, stream, leaveOpen);
            try
            {
                long start = stream.Position;

                // write the message.
                var envelope = new SessionLessServiceMessage
                {
                    NamespaceUris = context.NamespaceUris,
                    ServerUris = context.ServerUris,
                    Message = message
                };

                envelope.Encode(encoder);

                // check that the max message size was not exceeded.
                if (context.MaxMessageSize > 0 &&
                    context.MaxMessageSize < (int)(stream.Position - start))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadEncodingLimitsExceeded,
                        "MaxMessageSize {0} < {1}",
                        context.MaxMessageSize,
                        (int)(stream.Position - start));
                }

                encoder.Close();
            }
            finally
            {
                if (leaveOpen)
                {
                    stream.Position = 0;
                }
                encoder.Dispose();
            }
        }

        /// <summary>
        /// Decodes a session-less message from a buffer.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="buffer"/>
        /// is <c>null</c>.</exception>
        /// <exception cref="ServiceResultException"></exception>
        public static IEncodeable DecodeAsBinary(
            byte[] buffer,
            IServiceMessageContext context)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            using var decoder = new BinaryDecoder(buffer, context);
            // read the node id.
            NodeId typeId = decoder.ReadNodeId(null);

            // convert to absolute node id.
            var absoluteId = NodeId.ToExpandedNodeId(typeId, context.NamespaceUris);

            // lookup message session-less envelope type.
            Type actualType = decoder.Context.Factory.GetSystemType(absoluteId);

            if (actualType == null || actualType != typeof(SessionlessInvokeRequestType))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadDecodingError,
                    "Cannot decode session-less service message with type id: {0}.",
                    absoluteId);
            }

            // decode the actual message.
            var message = new SessionLessServiceMessage();

            message.Decode(decoder);

            decoder.Close();

            return message.Message;
        }

        /// <summary>
        /// Encodes a session-less message to a buffer.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="context"/>
        /// is <c>null</c>.</exception>
        /// <exception cref="ServiceResultException"></exception>
        public static void EncodeAsBinary(
            IEncodeable message,
            Stream stream,
            IServiceMessageContext context,
            bool leaveOpen)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            // create encoder.
            using var encoder = new BinaryEncoder(stream, context, leaveOpen);
            long start = encoder.Position;

            // write the type id.
            encoder.WriteNodeId(null, DataTypeIds.SessionlessInvokeRequestType);

            // write the message.
            var envelope = new SessionLessServiceMessage
            {
                NamespaceUris = context.NamespaceUris,
                ServerUris = context.ServerUris,
                Message = message ?? throw new ArgumentNullException(nameof(message))
            };

            envelope.Encode(encoder);

            // check that the max message size was not exceeded.
            if (context.MaxMessageSize > 0 &&
                context.MaxMessageSize < (int)(encoder.Position - start))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadEncodingLimitsExceeded,
                    "MaxMessageSize {0} < {1}",
                    context.MaxMessageSize,
                    (int)(encoder.Position - start));
            }
        }
    }
}
