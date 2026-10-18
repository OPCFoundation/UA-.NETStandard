/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Buffers.Binary;

namespace Opc.Ua.DataChannels.Wire
{
    /// <summary>
    /// Holds a decoded carrier payload with borrowed record storage, not proof of carrier authentication.
    /// </summary>
    internal readonly struct FlowWireEnvelope
    {
        /// <summary>
        /// Creates a carrier-payload view.
        /// </summary>
        internal FlowWireEnvelope(uint channelId, FlowRecord record, FlowControl? control)
        {
            ChannelId = channelId;
            Record = record;
            Control = control;
        }

        /// <summary>
        /// Gets the flow or management identifier.
        /// </summary>
        internal uint ChannelId { get; }

        /// <summary>
        /// Gets the common record.
        /// </summary>
        internal FlowRecord Record { get; }

        /// <summary>
        /// Gets the typed CONTROL body, or null for DATA.
        /// </summary>
        internal FlowControl? Control { get; }
    }

    /// <summary>
    /// Handles flow payload envelopes only. UASC security and native transport framing remain with their adapters.
    /// </summary>
    internal static class FlowCarrierCodec
    {
        /// <summary>
        /// Decodes a verified, unpadded FLO body for either explicitly selected UASC flow profile.
        /// </summary>
        /// <exception cref="FlowCodecException"></exception>
        internal static FlowWireEnvelope DecodeUascPayload(
            FlowCarrier carrier,
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
            uint channelId = ReadChannel(plaintextBody);
            plaintextBody = plaintextBody[4..];
            FlowRecord record = FlowRecordCodec.DecodeExact(plaintextBody, limits);
            FlowControl? control = record.Kind == FlowRecordKind.Control ?
                FlowControlCodec.Decode(in record, limits) : null;
            ValidateUasc(carrier, channelId, in record, control);
            return new FlowWireEnvelope(channelId, record, control);
        }

        /// <summary>
        /// Encodes a UASC flow body only and supplies its required RequestId, without applying any security.
        /// </summary>
        /// <exception cref="FlowCodecException"></exception>
        internal static ByteString EncodeUascPayload(
            FlowCarrier carrier,
            uint channelId,
            in FlowRecord record,
            FlowCodecLimits limits,
            out uint requestId)
        {
            RequireUasc(carrier);
            int recordLength = FlowRecordCodec.GetEncodedLength(in record, limits);
            FlowControl? control = record.Kind == FlowRecordKind.Control ?
                FlowControlCodec.Decode(in record, limits) : null;
            ValidateUasc(carrier, channelId, in record, control);
            requestId = 0;
            if (recordLength > int.MaxValue - 4)
            {
                throw new FlowCodecException(FlowCodecError.TooLarge);
            }
            byte[] bytes = new byte[checked(recordLength + 4)];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, channelId);
            FlowRecordCodec.Write(in record, bytes.AsSpan(4), limits);
            return new ByteString(bytes);
        }

        /// <summary>
        /// Decodes exactly one native DATA datagram after native transport context framing has been removed.
        /// </summary>
        /// <exception cref="FlowCodecException"></exception>
        internal static FlowWireEnvelope DecodeDatagram(
            FlowCarrier carrier,
            ByteString payload,
            int datagramCapacity,
            FlowCodecLimits limits)
        {
            RequireNative(carrier, datagramCapacity);
            if (payload.Length > datagramCapacity)
            {
                throw new FlowCodecException(FlowCodecError.TooLarge);
            }
            uint channelId = ReadChannel(payload);
            FlowRecord record = FlowRecordCodec.DecodeExact(payload[4..], limits);
            ValidateDatagram(channelId, in record);
            return new FlowWireEnvelope(channelId, record, null);
        }

        /// <summary>
        /// Encodes a bounded native DATA datagram with its little-endian ChannelId.
        /// </summary>
        /// <exception cref="FlowCodecException"></exception>
        internal static ByteString EncodeDatagram(
            FlowCarrier carrier,
            uint channelId,
            in FlowRecord record,
            int datagramCapacity,
            FlowCodecLimits limits)
        {
            RequireNative(carrier, datagramCapacity);
            int recordLength = FlowRecordCodec.GetEncodedLength(in record, limits);
            ValidateDatagram(channelId, in record);
            if ((long)recordLength + 4 > datagramCapacity)
            {
                throw new FlowCodecException(FlowCodecError.TooLarge);
            }
            byte[] bytes = new byte[checked(recordLength + 4)];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, channelId);
            FlowRecordCodec.Write(in record, bytes.AsSpan(4), limits);
            return new ByteString(bytes);
        }

        /// <summary>
        /// Checks dedicated addressing and CONTROL body/prefix consistency.
        /// </summary>
        /// <exception cref="FlowCodecException"></exception>
        private static void ValidateUasc(
            FlowCarrier carrier,
            uint channelId,
            in FlowRecord record,
            FlowControl? control)
        {
            if (control != null)
            {
                FlowControlCodec.ValidateCarrier(control, carrier);
                if (control.Flow != channelId)
                {
                    throw new FlowCodecException(FlowCodecError.InvalidFlow);
                }
            }
            else if (record.Kind == FlowRecordKind.Data &&
                (channelId == 0 || (carrier == FlowCarrier.DedicatedUasc && channelId != 1)))
            {
                throw new FlowCodecException(FlowCodecError.InvalidFlow);
            }
        }

        /// <summary>
        /// Checks the datagram's permitted kind and address.
        /// </summary>
        /// <exception cref="FlowCodecException"></exception>
        private static void ValidateDatagram(uint channelId, in FlowRecord record)
        {
            if (record.Kind != FlowRecordKind.Data)
            {
                throw new FlowCodecException(FlowCodecError.DatagramControl);
            }
            if (channelId == 0)
            {
                throw new FlowCodecException(FlowCodecError.InvalidFlow);
            }
        }

        /// <summary>
        /// Reads an external ChannelId without touching the common-record length.
        /// </summary>
        /// <exception cref="FlowCodecException"></exception>
        private static uint ReadChannel(ByteString payload)
        {
            if (payload.Length < 4)
            {
                throw new FlowCodecException(FlowCodecError.Truncated);
            }
            return BinaryPrimitives.ReadUInt32LittleEndian(payload.Span);
        }

        /// <summary>
        /// Requires an explicitly selected UASC flow profile.
        /// </summary>
        /// <exception cref="FlowCodecException"></exception>
        private static void RequireUasc(FlowCarrier carrier)
        {
            if (carrier is not (FlowCarrier.DedicatedUasc or FlowCarrier.SharedUasc))
            {
                throw new FlowCodecException(FlowCodecError.CarrierMismatch);
            }
        }

        /// <summary>
        /// Requires a native profile and a nonnegative caller-supplied capacity.
        /// </summary>
        /// <exception cref="FlowCodecException"></exception>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        private static void RequireNative(FlowCarrier carrier, int capacity)
        {
            if (carrier is not (FlowCarrier.Quic or FlowCarrier.WebTransport))
            {
                throw new FlowCodecException(FlowCodecError.CarrierMismatch);
            }
            if (capacity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }
        }
    }
}
