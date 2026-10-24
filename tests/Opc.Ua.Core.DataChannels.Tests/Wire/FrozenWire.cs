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
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Opc.Ua.DataChannels.Wire;

namespace Opc.Ua.Core.DataChannels.Tests.Wire
{
    /// <summary>
    /// Provides independently authored public specimens with literal headers and payloads.
    /// No production encoder, private fixture file or environment setting supplies the expected bytes.
    /// </summary>
    internal sealed partial class FrozenWire
    {
        /// <summary>
        /// Materializes fixed literal bytes and independent typed expectations for one specimen.
        /// </summary>
        internal FrozenWire(string name)
        {
            Definition definition = Describe(name);
            Carrier = definition.Carrier;
            Datagram = definition.Datagram;
            ChannelId = definition.ChannelId;
            Revision = definition.Revision;
            MessageId = definition.MessageId;
            ExpectedControl = definition.Control;
            ExpectedBody = definition.Json is null ?
                ByteString.FromHexString(definition.BodyHex) :
                new ByteString(new UTF8Encoding(false, true).GetBytes(definition.Json));
            Bytes = new ByteString(ByteString.FromHexString(definition.PrefixHex).ToArray()
                .Concat(ExpectedBody.ToArray()).ToArray());
        }

        /// <summary>
        /// Gets fixed carrier-payload bytes, excluding all transport and security headers and footers.
        /// </summary>
        internal ByteString Bytes { get; }

        /// <summary>
        /// Gets the carrier whose already verified plaintext payload is being tested.
        /// </summary>
        internal FlowCarrier Carrier { get; }

        /// <summary>
        /// Gets whether the specimen is one native datagram rather than a reliable stream record.
        /// </summary>
        internal bool Datagram { get; }

        /// <summary>
        /// Gets the expected explicit or implicit channel address.
        /// </summary>
        internal uint ChannelId { get; }

        /// <summary>
        /// Gets the independently specified revision.
        /// </summary>
        internal uint Revision { get; }

        /// <summary>
        /// Gets the independently specified message identifier.
        /// </summary>
        internal uint MessageId { get; }

        /// <summary>
        /// Gets the independently constructed typed control, or null for DATA.
        /// </summary>
        internal FlowControl? ExpectedControl { get; }

        /// <summary>
        /// Gets the literal payload without a record header or ChannelId wrapper.
        /// </summary>
        internal ByteString ExpectedBody { get; }

        /// <summary>
        /// Gets the plaintext input to the codec, never an authenticated network packet.
        /// </summary>
        internal ByteString CarrierPayload => Bytes;

        /// <summary>
        /// Gets literal common-record bytes without calling the production decoder.
        /// </summary>
        internal ByteString CommonRecord =>
            Bytes[(Carrier is FlowCarrier.DedicatedUasc or FlowCarrier.SharedUasc || Datagram ? 4 : 0)..];

        /// <summary>
        /// Decodes the specimen at the actual post-security carrier seam.
        /// </summary>
        internal FlowWireEnvelope Decode(FlowCodecLimits? limits = null)
        {
            limits ??= new FlowCodecLimits();
            if (Carrier is FlowCarrier.DedicatedUasc or FlowCarrier.SharedUasc)
            {
                throw new InvalidOperationException("UASC messages use UascFlowMessageCodec directly.");
            }
            if (Datagram)
            {
                return FlowCarrierCodec.DecodeDatagram(Carrier, Bytes, Bytes.Length, limits);
            }
            FlowRecord record = FlowRecordCodec.DecodeExact(Bytes, limits);
            FlowControl? control = record.Kind == FlowRecordKind.Control ?
                FlowControlCodec.Decode(in record, limits) : null;
            if (control != null)
            {
                FlowControlCodec.ValidateCarrier(control, Carrier);
            }
            return new FlowWireEnvelope(control?.Flow ?? ChannelId, record, control);
        }

        /// <summary>
        /// Enumerates one independently authored canonical body for each of the eighteen operations.
        /// </summary>
        internal static IEnumerable<string> ControlNames()
        {
            yield return "quic_hello";
            yield return "control_open";
            yield return "control_accept";
            yield return "control_ready";
            yield return "control_prepare";
            yield return "control_accept-update";
            yield return "control_commit";
            yield return "control_ack";
            yield return "control_abort-update";
            yield return "control_credit";
            yield return "control_end";
            yield return "control_reset";
            yield return "control_ping";
            yield return "control_pong";
            yield return "control_status";
            yield return "control_bind";
            yield return "control_associate";
            yield return "control_associate-ack";
        }

        /// <summary>
        /// Enumerates every opaque payload kind on each applicable envelope.
        /// </summary>
        internal static IEnumerable<string> DataNames()
        {
            foreach (string carrier in new[] { "quic", "webtransport" })
            {
                foreach (string body in new[] { "opaque", "uadp", "json" })
                {
                    yield return carrier + "_" + body;
                }
            }
            yield return "quic_datagram";
            yield return "webtransport_datagram";
            yield return "quic_datagram_uadp";
            yield return "webtransport_datagram_uadp";
            yield return "quic_datagram_json";
            yield return "webtransport_datagram_json";
        }

        /// <summary>
        /// Writes only a bounded mutation header, independently of the production encoder.
        /// </summary>
        internal static ByteString Header(uint length, byte kind = 0, uint revision = 1, uint messageId = 1)
        {
            byte[] bytes = new byte[13];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, length);
            bytes[4] = kind;
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(5), revision);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(9), messageId);
            return new ByteString(bytes);
        }

        /// <summary>
        /// Requires the exact failure discriminator instead of accepting any exception.
        /// </summary>
        internal static void Fails(Action action, FlowCodecError expected)
        {
            FlowCodecException exception = Assert.Throws<FlowCodecException>(() => action())!;
            Assert.That(exception.Error, Is.EqualTo(expected));
        }
    }
}
