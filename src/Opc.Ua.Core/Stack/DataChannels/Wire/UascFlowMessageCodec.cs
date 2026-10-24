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
        /// Gets the decoded control record, or the unmodified DATA body.
        /// </summary>
        internal FlowRecord Record { get; }

        /// <summary>
        /// Gets the typed CONTROL body, or null for DATA.
        /// </summary>
        internal FlowControl? Control { get; }

        /// <summary>
        /// Gets the unmodified UADP NetworkMessage for FLO.
        /// </summary>
        internal ByteString ApplicationBody => Record.Body;
    }

    /// <summary>
    /// Encodes UASC FLO as an unchanged UADP message and FLC as a typed control body.
    /// </summary>
    internal static class UascFlowMessageCodec
    {
        /// <summary>
        /// Decodes one verified final FLO or FLC body.
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

            if (kind == DataChannelUascMessageKind.Flo)
            {
                int maximum = Math.Min(limits.MaxDataBytes, limits.MaxRecordBytes - FlowRecordCodec.HeaderSize);
                if (plaintextBody.Length > maximum)
                {
                    throw new FlowCodecException(FlowCodecError.TooLarge);
                }
                var data = new FlowRecord(FlowRecordKind.Data, 1, 1, plaintextBody);
                return new UascFlowMessage(0, data, null);
            }

            if (kind != DataChannelUascMessageKind.Flc)
            {
                throw new FlowCodecException(FlowCodecError.WrongType);
            }
            if (plaintextBody.Length < sizeof(uint) * 2)
            {
                throw new FlowCodecException(FlowCodecError.Truncated);
            }
            uint revision = BinaryPrimitives.ReadUInt32LittleEndian(plaintextBody.Span);
            uint operationId = BinaryPrimitives.ReadUInt32LittleEndian(plaintextBody.Span[sizeof(uint)..]);
            if (operationId == 0)
            {
                throw new FlowCodecException(FlowCodecError.InvalidMessageId);
            }
            ByteString controlBody = plaintextBody[(sizeof(uint) * 2)..];
            if (controlBody.Length > limits.MaxControlBytes)
            {
                throw new FlowCodecException(FlowCodecError.TooLarge);
            }
            var record = new FlowRecord(FlowRecordKind.Control, revision, operationId, controlBody);
            FlowControl control = FlowControlCodec.Decode(in record, limits);
            FlowControlCodec.ValidateCarrier(control, carrier, uascBaseline: true);
            if (plaintextBody.Length > limits.MaxRecordBytes - FlowRecordCodec.HeaderSize)
            {
                throw new FlowCodecException(FlowCodecError.TooLarge);
            }
            return new UascFlowMessage(control.Flow, record, control);
        }

        /// <summary>
        /// Encodes one UASC FLO or FLC application body.
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
                    record.Body.Length > limits.MaxRecordBytes - FlowRecordCodec.HeaderSize)
                {
                    throw new FlowCodecException(FlowCodecError.TooLarge);
                }
                return record.Body;
            }

            FlowControl control = FlowControlCodec.Decode(in record, limits);
            int bodyLength = record.Body.Length;
            FlowControlCodec.ValidateCarrier(control, carrier, uascBaseline: true);
            ValidateControlChannel(channelId, control);
            int length = checked(sizeof(uint) * 2 + bodyLength);
            if (length > limits.MaxRecordBytes - FlowRecordCodec.HeaderSize)
            {
                throw new FlowCodecException(FlowCodecError.TooLarge);
            }

            byte[] body = new byte[length];
            BinaryPrimitives.WriteUInt32LittleEndian(body, record.Revision);
            BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(sizeof(uint)), record.MessageId);
            record.Body.Span.CopyTo(body.AsSpan(sizeof(uint) * 2));
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
