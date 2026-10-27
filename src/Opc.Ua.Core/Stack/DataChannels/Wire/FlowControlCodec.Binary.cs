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

namespace Opc.Ua.DataChannels.Wire
{
    /// <summary>
    /// Encodes the closed binary CONTROL shapes without a Service or JSON envelope.
    /// </summary>
    internal static partial class FlowControlCodec
    {
        /// <summary>
        /// Decodes one bounded CONTROL body, using the selected UASC carrier's HELLO table when supplied.
        /// </summary>
        /// <exception cref="FlowCodecException"></exception>
        internal static FlowControl Decode(
            in FlowRecord record,
            FlowCodecLimits limits,
            FlowCarrier? uascCarrier = null)
        {
            FlowRecordCodec.GetEncodedLength(in record, limits);
            if (record.Kind != FlowRecordKind.Control)
            {
                throw new FlowCodecException(FlowCodecError.WrongType);
            }
            var reader = new FlowBinaryReader(record.Body.Span);
            byte opcode = reader.Byte();
            if (opcode > (byte)FlowOperation.AssociateAck)
            {
                throw new FlowCodecException(FlowCodecError.UnknownOperation);
            }
            var operation = (FlowOperation)opcode;
            FlowControl control;
            if (operation == FlowOperation.Hello)
            {
                control = ReadHello(ref reader, uascCarrier);
            }
            else if (operation == FlowOperation.Bind)
            {
                control = new FlowBinding(reader.Byte(), reader.Challenge(), reader.Challenge());
            }
            else if (operation == FlowOperation.Open)
            {
                control = ReadOpen(ref reader);
            }
            else if (operation is FlowOperation.Associate or FlowOperation.AssociateAck)
            {
                control = new FlowAssociation(
                    operation, reader.UInt32(), reader.Byte(), reader.Guid());
            }
            else
            {
                uint flow = reader.UInt32();
                control = operation switch
                {
                    FlowOperation.Accept => new FlowAccept(
                        flow, reader.UInt64(), ReadParameters(ref reader)),
                    FlowOperation.Prepare => new FlowPrepare(flow, ReadLimits(ref reader)),
                    FlowOperation.Commit => new FlowCommit(flow, reader.UInt32()),
                    FlowOperation.Credit => new FlowCredit(flow, reader.UInt32(), reader.UInt32()),
                    FlowOperation.Ping or FlowOperation.Pong =>
                        new FlowProbe(operation, flow, reader.UInt64()),
                    FlowOperation.Reset or FlowOperation.Status =>
                        new FlowOutcome(operation, flow,
                            (FlowWireStatus)reader.Discriminator((byte)FlowWireStatus.WouldBlock)),
                    _ => new FlowSignal(operation, flow)
                };
            }
            reader.RequireEnd();
            Validate(control, record.Revision, record.MessageId);
            return control;
        }

        /// <summary>
        /// Creates a canonical CONTROL record after a bounded allocation-free counting pass.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="FlowCodecException"></exception>
        internal static FlowRecord Encode(
            FlowControl control,
            uint revision,
            uint messageId,
            FlowCodecLimits limits)
        {
            int length = GetEncodedBodyLength(control, revision, messageId, limits);
            byte[] buffer = new byte[length];
            WriteBody(control, buffer);
            return new FlowRecord(FlowRecordKind.Control, revision, messageId, new ByteString(buffer));
        }

        /// <summary>
        /// Writes a validated CONTROL into admitted storage with the selected UASC HELLO table, when supplied.
        /// </summary>
        internal static void WriteBody(
            FlowControl control,
            Span<byte> destination,
            FlowCarrier? uascCarrier = null)
        {
            var writer = new FlowBinaryWriter(destination, destination.Length);
            WriteControl(ref writer, control, uascCarrier);
        }

        /// <summary>
        /// Validates and measures the complete body before an owner admits any encoding allocations.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        internal static int GetEncodedBodyLength(
            FlowControl control,
            uint revision,
            uint messageId,
            FlowCodecLimits limits)
        {
            if (limits is null)
            {
                throw new ArgumentNullException(nameof(limits));
            }
            Validate(control, revision, messageId);
            int maximum = Math.Min(limits.MaxControlBytes, limits.MaxRecordBytes - FlowRecordCodec.HeaderSize);
            var counter = new FlowBinaryWriter([], maximum);
            WriteControl(ref counter, control);
            return counter.Length;
        }

        /// <summary>
        /// Reads context claims in their fixed binary order.
        /// </summary>
        private static FlowHello ReadHello(ref FlowBinaryReader reader, FlowCarrier? uascCarrier)
        {
            uint version = reader.UInt32();
            bool nativeBaseline = uascCarrier is FlowCarrier.Quic or FlowCarrier.WebTransport;
            byte value = reader.Discriminator(nativeBaseline ? (byte)2 : (byte)FlowCarrier.WebTransport);
            FlowCarrier carrier = nativeBaseline
                ? value switch
                {
                    0 => FlowCarrier.DedicatedUasc,
                    1 => FlowCarrier.Quic,
                    2 => FlowCarrier.WebTransport,
                    _ => throw new FlowCodecException(FlowCodecError.UnknownDiscriminator)
                }
                : (FlowCarrier)value;
            return new FlowHello(
                version,
                carrier,
                reader.String(FlowWireText.MaxUriBytes),
                reader.Guid(),
                reader.UInt64(),
                reader.Challenge(),
                (FlowWireProtection)reader.Discriminator((byte)FlowWireProtection.Integrity),
                reader.UInt32(),
                reader.UInt32(),
                reader.UInt32());
        }

        /// <summary>
        /// Reads the grant only when the preceding authorization discriminator selects it.
        /// </summary>
        private static FlowOpen ReadOpen(ref FlowBinaryReader reader)
        {
            uint flow = reader.UInt32();
            ulong stream = reader.UInt64();
            FlowWireParameters parameters = ReadParameters(ref reader);
            var authorization = (FlowWireAuthorization)reader.Discriminator((byte)FlowWireAuthorization.Session);
            Guid? grant = authorization == FlowWireAuthorization.Session ? reader.Guid() : null;
            return new FlowOpen(flow, stream, parameters, authorization, grant);
        }

        /// <summary>
        /// Reads the inlined parameter structure in model field order, using compact wire enums.
        /// </summary>
        private static FlowWireParameters ReadParameters(ref FlowBinaryReader reader)
        {
            string resource = reader.String(FlowWireText.MaxUriBytes);
            string protocol = reader.String(FlowWireText.MaxUriBytes);
            string contentType = reader.String(FlowWireText.MaxContentTypeBytes);
            var direction = (FlowWireDirection)reader.Discriminator((byte)FlowWireDirection.Duplex);
            var delivery = (FlowWireDelivery)reader.Discriminator((byte)FlowWireDelivery.Datagram);
            var protection = (FlowWireProtection)reader.Discriminator((byte)FlowWireProtection.Integrity);
            FlowWireLimits limits = ReadLimits(ref reader);
            return new FlowWireParameters(
                resource, protocol, contentType, direction, delivery, protection, limits, reader.Boolean());
        }

        /// <summary>
        /// Reads the four revisable fields in their binary order.
        /// </summary>
        private static FlowWireLimits ReadLimits(ref FlowBinaryReader reader)
        {
            return new FlowWireLimits(reader.UInt32(), reader.UInt32(), reader.Rate(), reader.Byte());
        }

        /// <summary>
        /// Writes validated CONTROL fields, selecting transport HELLO bytes only for a native UASC baseline.
        /// </summary>
        /// <exception cref="FlowCodecException"></exception>
        private static void WriteControl(
            ref FlowBinaryWriter writer,
            FlowControl control,
            FlowCarrier? uascCarrier = null)
        {
            writer.Byte((byte)control.Operation);
            if (control is FlowHello hello)
            {
                writer.UInt32(hello.Version);
                byte carrier = uascCarrier is FlowCarrier.Quic or FlowCarrier.WebTransport
                    ? hello.Carrier switch
                    {
                        FlowCarrier.Quic => (byte)1,
                        FlowCarrier.WebTransport => (byte)2,
                        _ => throw new FlowCodecException(FlowCodecError.CarrierMismatch)
                    }
                    : (byte)hello.Carrier;
                writer.Byte(carrier);
                writer.String(hello.Application);
                writer.Guid(hello.Context);
                writer.UInt64(hello.Generation);
                writer.Challenge(hello.Challenge);
                writer.Byte((byte)hello.Protection);
                writer.UInt32(hello.MaxMessageSize);
                writer.UInt32(hello.MaxReceiveBytes);
                writer.UInt32(hello.MaxFlows);
                return;
            }
            if (control is FlowBinding binding)
            {
                writer.Byte(binding.Role);
                writer.Challenge(binding.TranscriptDigest);
                writer.Challenge(binding.Proof);
                return;
            }
            writer.UInt32(control.Flow);
            switch (control)
            {
                case FlowOpen open:
                    writer.UInt64(open.Stream);
                    WriteParameters(ref writer, open.Parameters);
                    writer.Byte((byte)open.Authorization);
                    if (open.Grant is Guid grant)
                    {
                        writer.Guid(grant);
                    }
                    break;
                case FlowAccept accept:
                    writer.UInt64(accept.Stream);
                    WriteParameters(ref writer, accept.Parameters);
                    break;
                case FlowPrepare prepare:
                    WriteLimits(ref writer, prepare.Limits);
                    break;
                case FlowCommit commit:
                    writer.UInt32(commit.LastOldMessageId);
                    break;
                case FlowCredit credit:
                    writer.UInt32(credit.ChannelCredit);
                    writer.UInt32(credit.ConnectionCredit);
                    break;
                case FlowAssociation association:
                    writer.Byte(association.Direction);
                    writer.Guid(association.Token);
                    break;
                case FlowProbe probe:
                    writer.UInt64(probe.Timestamp);
                    break;
                case FlowOutcome outcome:
                    writer.Byte((byte)outcome.Status);
                    break;
                case FlowSignal:
                    break;
                default:
                    throw new FlowCodecException(FlowCodecError.UnknownOperation);
            }
        }

        /// <summary>
        /// Writes the complete inlined parameter structure.
        /// </summary>
        private static void WriteParameters(ref FlowBinaryWriter writer, FlowWireParameters parameters)
        {
            writer.String(parameters.Resource);
            writer.String(parameters.Protocol);
            writer.String(parameters.ContentType);
            writer.Byte((byte)parameters.Direction);
            writer.Byte((byte)parameters.Delivery);
            writer.Byte((byte)parameters.Protection);
            WriteLimits(ref writer, parameters.Limits);
            writer.Boolean(parameters.AllowQueueExpiry);
        }

        /// <summary>
        /// Writes the four revisable fields without a nested length or type envelope.
        /// </summary>
        private static void WriteLimits(ref FlowBinaryWriter writer, FlowWireLimits limits)
        {
            writer.UInt32(limits.MaxMessageSize);
            writer.UInt32(limits.MaxReceiveBytes);
            writer.Rate(limits.MaxRate);
            writer.Byte(limits.Priority);
        }
    }
}
