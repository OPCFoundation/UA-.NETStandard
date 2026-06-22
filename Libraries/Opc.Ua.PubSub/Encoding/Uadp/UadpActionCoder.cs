/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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

namespace Opc.Ua.PubSub.Encoding.Uadp
{
    /// <summary>
    /// Stateless encode + decode for UADP Action NetworkMessages.
    /// </summary>
    /// <remarks>
    /// Implements Part 14 v1.05 §7.2.4.4.2 ActionHeader plus
    /// §7.2.4.5.9/§7.2.4.5.10 action request/response payloads. The
    /// ExtendedFlags2 NetworkMessage type remains DataSetMessage and
    /// <see cref="ExtendedFlags2EncodingMask.ActionHeaderEnabled"/> is
    /// set; ActionFlags bit 0 distinguishes request from response.
    /// TODO: re-check against the final 1.05.07 UADP action tables.
    /// </remarks>
    public static class UadpActionCoder
    {
        private const byte kActionRequest = 0x01;
        private const byte kResponseAddressEnabled = 0x02;
        private const byte kCorrelationDataEnabled = 0x04;
        private const byte kTimeoutHintEnabled = 0x10;

        /// <summary>
        /// Encodes an action NetworkMessage.
        /// </summary>
        /// <param name="message">Source message.</param>
        /// <param name="context">Network message context.</param>
        public static byte[] Encode(
            PubSubNetworkMessage message,
            PubSubNetworkMessageContext context)
        {
            return Encode(message, context, securityEnabled: false, out _);
        }

        internal static byte[] Encode(
            PubSubNetworkMessage message,
            PubSubNetworkMessageContext context,
            bool securityEnabled,
            out int payloadOffset)
        {
            if (message is null)
            {
                throw new ArgumentNullException(nameof(message));
            }
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            return message switch
            {
                UadpActionRequestMessage request =>
                    EncodeRequest(request, context, securityEnabled, out payloadOffset),
                UadpActionResponseMessage response =>
                    EncodeResponse(response, context, securityEnabled, out payloadOffset),
                _ => throw new InvalidOperationException(
                    "Action encoding requires a UadpActionRequestMessage "
                    + "or UadpActionResponseMessage instance.")
            };
        }

        internal static PubSubNetworkMessage? TryDecode(
            ref UadpBinaryReader reader,
            UadpDecodedHeader header,
            ushort dataSetWriterId,
            PubSubNetworkMessageContext context)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (!reader.TryReadByte(out byte actionFlags))
            {
                return null;
            }

            bool isRequest = (actionFlags & kActionRequest) != 0;
            if (!TryReadActionHeader(
                ref reader, actionFlags, out string responseAddress,
                out ByteString correlationData, out double timeoutHint))
            {
                return null;
            }
            if (!reader.TryReadUInt16Le(out ushort actionTargetId) ||
                !reader.TryReadUInt16Le(out ushort requestId) ||
                !reader.TryReadByte(out byte stateByte))
            {
                return null;
            }

            uint statusCode = 0;
            if (!isRequest && !reader.TryReadUInt32Le(out statusCode))
            {
                return null;
            }
            StatusCode status = new StatusCode(statusCode);

            ArrayOf<DataSetField>? decodedPayload = UadpFieldDecoder.DecodeFields(
                ref reader,
                PubSubFieldEncoding.Variant,
                PubSubDataSetMessageType.KeyFrame,
                metaData: null,
                context.MessageContext);
            if (decodedPayload is null)
            {
                return null;
            }
            var payload = (ArrayOf<DataSetField>)decodedPayload;

            return isRequest
                ? new UadpActionRequestMessage
                {
                    PublisherId = header.PublisherId,
                    WriterGroupId = header.WriterGroupId,
                    DataSetClassId = header.DataSetClassId,
                    DataSetWriterId = dataSetWriterId,
                    ActionTargetId = actionTargetId,
                    RequestId = requestId,
                    ActionState = (ActionState)stateByte,
                    ResponseAddress = responseAddress,
                    CorrelationData = correlationData,
                    TimeoutHint = timeoutHint,
                    Payload = payload
                }
                : new UadpActionResponseMessage
                {
                    PublisherId = header.PublisherId,
                    WriterGroupId = header.WriterGroupId,
                    DataSetClassId = header.DataSetClassId,
                    DataSetWriterId = dataSetWriterId,
                    ActionTargetId = actionTargetId,
                    RequestId = requestId,
                    ActionState = (ActionState)stateByte,
                    Status = status,
                    CorrelationData = correlationData,
                    TimeoutHint = timeoutHint,
                    Payload = payload
                };
        }

        private static byte[] EncodeRequest(
            UadpActionRequestMessage message,
            PubSubNetworkMessageContext context,
            bool securityEnabled,
            out int payloadOffset)
        {
            byte[] buffer = new byte[8192];
            var writer = new UadpBinaryWriter(buffer, 0, buffer.Length);
            WriteCommon(ref writer, message, securityEnabled);
            payloadOffset = writer.Position;

            byte actionFlags = kActionRequest | kTimeoutHintEnabled;
            if (!string.IsNullOrEmpty(message.ResponseAddress))
            {
                actionFlags |= kResponseAddressEnabled;
            }
            if (!message.CorrelationData.IsNull)
            {
                actionFlags |= kCorrelationDataEnabled;
            }

            writer.WriteByte(actionFlags);
            WriteActionHeader(ref writer, actionFlags, message.ResponseAddress,
                message.CorrelationData, message.TimeoutHint);
            WriteActionPayloadHeader(ref writer, message.ActionTargetId,
                message.RequestId, message.ActionState);
            UadpFieldEncoder.EncodeFields(
                ref writer, message.Payload, message.FieldEncoding,
                PubSubDataSetMessageType.KeyFrame, message.MetaData,
                context.MessageContext, message.FieldContentMask);
            return TrimToWritten(buffer, writer.Position);
        }

        private static byte[] EncodeResponse(
            UadpActionResponseMessage message,
            PubSubNetworkMessageContext context,
            bool securityEnabled,
            out int payloadOffset)
        {
            byte[] buffer = new byte[8192];
            var writer = new UadpBinaryWriter(buffer, 0, buffer.Length);
            WriteCommon(ref writer, message, securityEnabled);
            payloadOffset = writer.Position;

            byte actionFlags = 0;
            if (!message.CorrelationData.IsNull)
            {
                actionFlags |= kCorrelationDataEnabled;
            }

            writer.WriteByte(actionFlags);
            WriteActionHeader(ref writer, actionFlags, message.ResponseAddress,
                message.CorrelationData, message.TimeoutHint);
            WriteActionPayloadHeader(ref writer, message.ActionTargetId,
                message.RequestId, message.ActionState);
            writer.WriteUInt32Le((uint)message.Status.Code);
            UadpFieldEncoder.EncodeFields(
                ref writer, message.Payload, message.FieldEncoding,
                PubSubDataSetMessageType.KeyFrame, message.MetaData,
                context.MessageContext, message.FieldContentMask);
            return TrimToWritten(buffer, writer.Position);
        }

        private static void WriteCommon(
            ref UadpBinaryWriter writer,
            UadpActionRequestMessage message,
            bool securityEnabled)
        {
            UadpDiscoveryWire.WriteCommonHeader(
                ref writer,
                message.UadpVersion,
                message.PublisherId,
                message.DataSetClassId,
                ExtendedFlags2EncodingMask.ActionHeaderEnabled,
                securityEnabled || message.SecurityEnabled,
                payloadHeaderEnabled: true,
                message.WriterGroupId);
            writer.WriteByte(1);
            writer.WriteUInt16Le(message.DataSetWriterId);
        }

        private static void WriteCommon(
            ref UadpBinaryWriter writer,
            UadpActionResponseMessage message,
            bool securityEnabled)
        {
            UadpDiscoveryWire.WriteCommonHeader(
                ref writer,
                message.UadpVersion,
                message.PublisherId,
                message.DataSetClassId,
                ExtendedFlags2EncodingMask.ActionHeaderEnabled,
                securityEnabled || message.SecurityEnabled,
                payloadHeaderEnabled: true,
                message.WriterGroupId);
            writer.WriteByte(1);
            writer.WriteUInt16Le(message.DataSetWriterId);
        }

        private static void WriteActionHeader(
            ref UadpBinaryWriter writer,
            byte actionFlags,
            string responseAddress,
            ByteString correlationData,
            double timeoutHint)
        {
            if ((actionFlags & kResponseAddressEnabled) != 0)
            {
                writer.WriteString(responseAddress);
            }
            if ((actionFlags & kCorrelationDataEnabled) != 0)
            {
                WriteByteString(ref writer, correlationData);
            }
            if ((actionFlags & kTimeoutHintEnabled) != 0)
            {
                writer.WriteInt64Le(BitConverter.DoubleToInt64Bits(timeoutHint));
            }
        }

        private static void WriteActionPayloadHeader(
            ref UadpBinaryWriter writer,
            ushort actionTargetId,
            ushort requestId,
            ActionState actionState)
        {
            writer.WriteUInt16Le(actionTargetId);
            writer.WriteUInt16Le(requestId);
            writer.WriteByte((byte)actionState);
        }

        private static bool TryReadActionHeader(
            ref UadpBinaryReader reader,
            byte actionFlags,
            out string responseAddress,
            out ByteString correlationData,
            out double timeoutHint)
        {
            responseAddress = string.Empty;
            correlationData = default;
            timeoutHint = 0;

            if ((actionFlags & kResponseAddressEnabled) != 0)
            {
                if (!reader.TryReadString(out string? address))
                {
                    return false;
                }
                responseAddress = address ?? string.Empty;
            }
            if ((actionFlags & kCorrelationDataEnabled) != 0 &&
                !TryReadByteString(ref reader, out correlationData))
            {
                return false;
            }
            if ((actionFlags & kTimeoutHintEnabled) != 0)
            {
                if (!reader.TryReadInt64Le(out long bits))
                {
                    return false;
                }
                timeoutHint = BitConverter.Int64BitsToDouble(bits);
            }
            return true;
        }

        private static void WriteByteString(ref UadpBinaryWriter writer, ByteString value)
        {
            if (value.IsNull)
            {
                writer.WriteUInt32Le(uint.MaxValue);
                return;
            }
            writer.WriteUInt32Le(checked((uint)value.Length));
            writer.WriteBytes(value.Span);
        }

        private static bool TryReadByteString(
            ref UadpBinaryReader reader,
            out ByteString value)
        {
            value = default;
            if (!reader.TryReadUInt32Le(out uint length))
            {
                return false;
            }
            if (length == uint.MaxValue)
            {
                return true;
            }
            if (length > reader.Remaining)
            {
                return false;
            }
            int byteCount = checked((int)length);
            var bytes = new byte[byteCount];
            Buffer.BlockCopy(
                reader.Buffer, reader.Origin + reader.Position, bytes, 0, byteCount);
            reader.Advance(byteCount);
            value = ByteString.From(bytes);
            return true;
        }

        private static byte[] TrimToWritten(byte[] buffer, int written)
        {
            var result = new byte[written];
            Buffer.BlockCopy(buffer, 0, result, 0, written);
            return result;
        }
    }
}
