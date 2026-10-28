/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense,
 * and/or sell copies of the Software, and to permit persons to
 * whom the Software is furnished to do so, subject to the following
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
using System.Buffers.Binary;

namespace Opc.Ua.DataChannels.Wire
{
    /// <summary>
    /// Holds a decoded UASC flow body with borrowed storage.
    /// </summary>
    internal readonly struct UascFlowMessage
    {
        /// <summary>
        /// Creates a decoded message.
        /// </summary>
        internal UascFlowMessage(
            uint channelId,
            FlowRecord record,
            FlowControl? control)
        {
            ChannelId = channelId;
            Record = record;
            Control = control;
        }

        /// <summary>
        /// Gets the control-only admission identifier, or zero for DATA before routing.
        /// </summary>
        internal uint ChannelId { get; }

        /// <summary>
        /// Gets the parsed header and unmodified CONTROL or DATA body.
        /// </summary>
        internal FlowRecord Record { get; }

        /// <summary>
        /// Gets the typed CONTROL body, or null for DATA.
        /// </summary>
        internal FlowControl? Control { get; }

        /// <summary>
        /// Gets the unmodified UADP NetworkMessage after its MSG TypeId.
        /// </summary>
        internal ByteString ApplicationBody => Record.Body;
    }

    /// <summary>
    /// Encodes canonical MSG TypeIds followed by unchanged UADP or compact binary control.
    /// </summary>
    internal static class UascFlowMessageCodec
    {
        /// <summary>
        /// The canonical namespace-zero Two-Byte NodeId size.
        /// </summary>
        internal const int TypeIdSize = 2;

        /// <summary>
        /// The TypeId, Revision and OperationId size before the control opcode.
        /// </summary>
        internal const int ControlPrefixSize = TypeIdSize + sizeof(uint) * 2;

        /// <summary>
        /// Classifies only the provisional canonical Two-Byte TypeIds, never alternate NodeId encodings.
        /// </summary>
        internal static FlowRecordKind Classify(ReadOnlySpan<byte> plaintextBody)
        {
            if (plaintextBody.Length < TypeIdSize)
            {
                throw new FlowCodecException(FlowCodecError.Truncated);
            }
            if (plaintextBody[0] != 0)
            {
                throw new FlowCodecException(FlowCodecError.WrongType);
            }
            return plaintextBody[1] switch
            {
                254 => FlowRecordKind.Data,
                255 => FlowRecordKind.Control,
                _ => throw new FlowCodecException(FlowCodecError.WrongType)
            };
        }

        /// <summary>
        /// Decodes one final WebTransport management MSG containing exactly one common CONTROL record.
        /// </summary>
        internal static UascFlowMessage DecodeManagement(
            DataChannelUascMessageKind kind,
            byte chunkType,
            uint requestId,
            ByteString plaintextBody,
            FlowCodecLimits limits)
        {
            if (kind != DataChannelUascMessageKind.Msg)
            {
                throw new FlowCodecException(FlowCodecError.WrongType);
            }
            if (chunkType != (byte)'F')
            {
                throw new FlowCodecException(FlowCodecError.NonFinalUasc);
            }
            FlowRecord record = FlowRecordCodec.DecodeExact(plaintextBody, limits);
            if (record.Kind != FlowRecordKind.Control)
            {
                throw new FlowCodecException(FlowCodecError.WrongType);
            }
            if (requestId != record.MessageId)
            {
                throw new FlowCodecException(FlowCodecError.RequestIdMismatch);
            }
            FlowControl control = FlowControlCodec.Decode(in record, limits);
            FlowControlCodec.ValidateCarrier(control, FlowCarrier.WebTransport);
            return new UascFlowMessage(control.Flow, record, control);
        }

        /// <summary>
        /// Encodes a WebTransport management CONTROL record without a Service or length wrapper.
        /// </summary>
        internal static ByteString EncodeManagement(
            uint channelId,
            in FlowRecord record,
            FlowCodecLimits limits,
            out uint requestId)
        {
            if (record.Kind != FlowRecordKind.Control)
            {
                throw new FlowCodecException(FlowCodecError.WrongType);
            }
            FlowControl control = FlowControlCodec.Decode(in record, limits);
            FlowControlCodec.ValidateCarrier(control, FlowCarrier.WebTransport);
            ValidateControlChannel(channelId, control);
            requestId = record.MessageId;
            return FlowRecordCodec.Encode(in record, limits);
        }

        /// <summary>
        /// Decodes one completely reassembled, security-verified MSG body.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="FlowCodecException"></exception>
        internal static UascFlowMessage Decode(
            FlowCarrier carrier,
            DataChannelUascMessageKind kind,
            byte chunkType,
            uint requestId,
            ByteString plaintextBody,
            FlowCodecLimits limits)
        {
            RequireUasc(carrier);
            if (chunkType != (byte)'F')
            {
                throw new FlowCodecException(FlowCodecError.NonFinalUasc);
            }
            if (requestId != 0)
            {
                throw new FlowCodecException(FlowCodecError.RequestIdMismatch);
            }
            if (limits is null)
            {
                throw new ArgumentNullException(nameof(limits));
            }
            if (kind != DataChannelUascMessageKind.Msg)
            {
                throw new FlowCodecException(FlowCodecError.WrongType);
            }
            FlowRecordKind recordKind = Classify(plaintextBody.Span);
            if (recordKind == FlowRecordKind.Data)
            {
                int maximum = Math.Min(limits.MaxDataBytes,
                    limits.MaxRecordBytes - FlowRecordCodec.HeaderSize - TypeIdSize);
                ByteString applicationBody = plaintextBody[TypeIdSize..];
                if (applicationBody.Length > maximum)
                {
                    throw new FlowCodecException(FlowCodecError.TooLarge);
                }
                var data = new FlowRecord(FlowRecordKind.Data, 1, 1, applicationBody);
                return new UascFlowMessage(0, data, null);
            }

            if (plaintextBody.Length < ControlPrefixSize)
            {
                throw new FlowCodecException(FlowCodecError.Truncated);
            }
            uint revision = BinaryPrimitives.ReadUInt32LittleEndian(plaintextBody.Span[TypeIdSize..]);
            uint operationId = BinaryPrimitives.ReadUInt32LittleEndian(
                plaintextBody.Span[(TypeIdSize + sizeof(uint))..]);
            if (operationId == 0)
            {
                throw new FlowCodecException(FlowCodecError.InvalidMessageId);
            }
            ByteString controlBody = plaintextBody[ControlPrefixSize..];
            if (controlBody.Length > limits.MaxControlBytes)
            {
                throw new FlowCodecException(FlowCodecError.TooLarge);
            }
            var record = new FlowRecord(FlowRecordKind.Control, revision, operationId, controlBody);
            FlowControl control = FlowControlCodec.Decode(in record, limits, carrier);
            FlowControlCodec.ValidateCarrier(control, carrier, uascBaseline: true);
            if (plaintextBody.Length > limits.MaxRecordBytes - FlowRecordCodec.HeaderSize)
            {
                throw new FlowCodecException(FlowCodecError.TooLarge);
            }
            return new UascFlowMessage(control.Flow, record, control);
        }

        /// <summary>
        /// Encodes one MSG TypeId and complete UADP or control body, with RequestId zero.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="FlowCodecException"></exception>
        internal static ByteString Encode(
            FlowCarrier carrier,
            uint channelId,
            in FlowRecord record,
            FlowCodecLimits limits,
            out uint requestId)
        {
            RequireUasc(carrier);
            if (limits is null)
            {
                throw new ArgumentNullException(nameof(limits));
            }
            requestId = 0;
            if (record.Kind == FlowRecordKind.Data)
            {
                ValidateDataChannel(channelId, carrier);
                if (record.Body.IsNull ||
                    record.Body.Length > limits.MaxDataBytes ||
                    record.Body.Length > limits.MaxRecordBytes - FlowRecordCodec.HeaderSize - TypeIdSize)
                {
                    throw new FlowCodecException(FlowCodecError.TooLarge);
                }
                byte[] data = new byte[checked(TypeIdSize + record.Body.Length)];
                data[1] = 254;
                record.Body.Span.CopyTo(data.AsSpan(TypeIdSize));
                return new ByteString(data);
            }

            if (record.Kind != FlowRecordKind.Control)
            {
                throw new FlowCodecException(FlowCodecError.WrongType);
            }
            FlowControl control = FlowControlCodec.Decode(in record, limits);
            int bodyLength = record.Body.Length;
            FlowControlCodec.ValidateCarrier(control, carrier, uascBaseline: true);
            ValidateControlChannel(channelId, control);
            int length = checked(ControlPrefixSize + bodyLength);
            if (length > limits.MaxRecordBytes - FlowRecordCodec.HeaderSize)
            {
                throw new FlowCodecException(FlowCodecError.TooLarge);
            }

            byte[] body = new byte[length];
            body[1] = 255;
            BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(TypeIdSize), record.Revision);
            BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(TypeIdSize + sizeof(uint)), record.MessageId);
            if (control is FlowHello)
            {
                FlowControlCodec.WriteBody(control, body.AsSpan(ControlPrefixSize), carrier);
            }
            else
            {
                record.Body.Span.CopyTo(body.AsSpan(ControlPrefixSize));
            }
            return new ByteString(body);
        }

        /// <summary>
        /// Rejects a DATA record addressed to the management channel.
        /// </summary>
        private static void ValidateDataChannel(uint channelId, FlowCarrier carrier)
        {
            if (channelId == 0)
            {
                throw new FlowCodecException(FlowCodecError.InvalidFlow);
            }
        }

        /// <summary>
        /// Requires the protected ChannelId prefix to agree with the CONTROL flow.
        /// </summary>
        private static void ValidateControlChannel(uint channelId, FlowControl control)
        {
            if (channelId != control.Flow)
            {
                throw new FlowCodecException(FlowCodecError.InvalidFlow);
            }
        }

        /// <summary>
        /// Requires an explicitly selected UASC flow profile.
        /// </summary>
        private static void RequireUasc(FlowCarrier carrier)
        {
            if (carrier is not (FlowCarrier.DedicatedUasc or FlowCarrier.SharedUasc or
                FlowCarrier.Quic or FlowCarrier.WebTransport))
            {
                throw new FlowCodecException(FlowCodecError.CarrierMismatch);
            }
        }
    }
}
