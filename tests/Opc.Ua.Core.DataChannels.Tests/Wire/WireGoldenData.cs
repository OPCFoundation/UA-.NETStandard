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
using Opc.Ua.DataChannels.Wire;

namespace Opc.Ua.Core.DataChannels.Tests.Wire
{
    /// <summary>
    /// Holds redistributable literals authored from factual wire layouts with neutral test content.
    /// Header lengths are frozen numbers, not calculated from encoder output or payload length.
    /// </summary>
    internal sealed partial class FrozenWire
    {
        /// <summary>
        /// Selects literal headers and bodies without consulting a production codec or name mapper.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        private static Definition Describe(string name)
        {
            switch (name)
            {
                case "dedicated-uasc_hello":
                    return Hello(FlowCarrier.DedicatedUasc, "00", "000000007300000001000000001f000000");
                case "shared-uasc_hello":
                    return Hello(FlowCarrier.SharedUasc, "01", "000000007300000001000000001f000000");
                case "quic_hello":
                    return Hello(FlowCarrier.Quic, "02", "7300000001000000001f000000");
                case "webtransport_hello":
                    return Hello(FlowCarrier.WebTransport, "03", "7300000001000000001f000000");
                case "control_open":
                    return SharedControl(new FlowOpen(1, 0, Descriptor(), FlowWireAuthorization.Configured, null),
                        1, 33, "0100000091000000010100000021000000",
                        "01010000000000000000000000" + kParametersHex + "00");
                case "control_accept":
                    return SharedControl(new FlowAccept(1, 0, Descriptor()),
                        1, 34, "0100000090000000010100000022000000",
                        "02010000000000000000000000" + kParametersHex);
                case "control_ready":
                    return SharedControl(new FlowSignal(FlowOperation.Ready, 1),
                        1, 35, "010000000e000000010100000023000000", "0301000000");
                case "control_prepare":
                    return SharedControl(new FlowPrepare(1, new FlowWireLimits(2048, 8192, 75.5, 7)),
                        2, 36, "010000001f000000010200000024000000",
                        "040100000000080000002000000000000000e0524007");
                case "control_accept-update":
                    return SharedControl(new FlowSignal(FlowOperation.AcceptUpdate, 1),
                        2, 37, "010000000e000000010200000025000000", "0501000000");
                case "control_commit":
                    return SharedControl(new FlowCommit(1, 17),
                        2, 38, "0100000012000000010200000026000000", "060100000011000000");
                case "control_ack":
                    return SharedControl(new FlowSignal(FlowOperation.Ack, 1),
                        2, 39, "010000000e000000010200000027000000", "0701000000");
                case "control_abort-update":
                    return SharedControl(new FlowSignal(FlowOperation.AbortUpdate, 1),
                        2, 40, "010000000e000000010200000028000000", "0801000000");
                case "control_credit":
                    return SharedControl(new FlowCredit(1, 88, 144),
                        1, 41, "0100000016000000010100000029000000", "09010000005800000090000000");
                case "control_end":
                    return SharedControl(new FlowSignal(FlowOperation.End, 1),
                        1, 42, "010000000e00000001010000002a000000", "0a01000000");
                case "control_reset":
                    return SharedControl(new FlowOutcome(FlowOperation.Reset, 1, FlowWireStatus.Closed),
                        1, 43, "010000000f00000001010000002b000000", "0b0100000008");
                case "control_ping":
                    return SharedControl(new FlowProbe(FlowOperation.Ping, 0, 9007199254740993),
                        0, 44, "000000001600000001000000002c000000", "0c000000000100000000002000");
                case "control_pong":
                    return SharedControl(new FlowProbe(FlowOperation.Pong, 0, 9007199254740995),
                        0, 45, "000000001600000001000000002d000000", "0d000000000300000000002000");
                case "control_status":
                    return SharedControl(new FlowOutcome(FlowOperation.Status, 1, FlowWireStatus.WouldBlock),
                        1, 46, "010000000f00000001010000002e000000", "0e010000000a");
                case "control_bind":
                    return new Definition(FlowCarrier.Quic, false, 0, 0, 48,
                        "53000000010000000030000000",
                        "0f0020000000" +
                        "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f" +
                        "20000000" +
                        "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f",
                        null,
                        new FlowBinding(0,
                            ByteString.FromHexString(
                                "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f"),
                            ByteString.FromHexString(
                                "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f")));
                case "control_associate":
                    return new Definition(FlowCarrier.Quic, false, 1, 1, 49,
                        "1f000000010100000031000000",
                        "10010000000033221111554477668899aabbccddeeff",
                        null,
                        new FlowAssociation(FlowOperation.Associate, 1, 0,
                            new Guid("11112233-4455-6677-8899-aabbccddeeff")));
                case "control_associate-ack":
                    return new Definition(FlowCarrier.Quic, false, 1, 1, 50,
                        "1f000000010100000032000000",
                        "11010000000033221111554477668899aabbccddeeff",
                        null,
                        new FlowAssociation(FlowOperation.AssociateAck, 1, 0,
                            new Guid("11112233-4455-6677-8899-aabbccddeeff")));
                case "session_open":
                    return new Definition(FlowCarrier.Quic, false, 1, 1, 47,
                        "a100000001010000002f000000",
                        "01010000000400000000000000" + kParametersHex + "01ddccbba62010504090abc0d0e0f01020",
                        null,
                        new FlowOpen(1, 4, Descriptor(), FlowWireAuthorization.Session,
                            new Guid("a6bbccdd-1020-4050-90ab-c0d0e0f01020")));
                default:
                    return Data(name);
            }
        }

        /// <summary>
        /// Creates an independent expected descriptor without decoding the golden bytes.
        /// </summary>
        private static FlowWireParameters Descriptor()
        {
            return new FlowWireParameters("urn:example:wire:sample",
                "urn:opcfoundation:ua:flow:payload:opaque:1", "application/octet-stream",
                FlowWireDirection.Duplex, FlowWireDelivery.Ordered, FlowWireProtection.Confidential,
                new FlowWireLimits(1024, 4096, 0, 0), false);
        }

        /// <summary>
        /// Creates HELLO expectations from separate typed values and literal binary fields.
        /// </summary>
        private static Definition Hello(FlowCarrier carrier, string carrierHex, string prefix)
        {
            string body = "0001000000" +
                carrierHex +
                "1700000075726e3a6578616d706c653a776972653a726561646572" +
                "89cb3ed03a96474bb6ab569a74ad1230130000000000000020000000" +
                "707172737475767778797a7b7c7d7e7f808182838485868788898a8b8c8d8e8f" +
                "00001000000080000006000000";
            var control = new FlowHello(1, carrier, "urn:example:wire:reader",
                new Guid("d03ecb89-963a-4b47-b6ab-569a74ad1230"), 19,
                ByteString.FromHexString("707172737475767778797a7b7c7d7e7f808182838485868788898a8b8c8d8e8f"),
                FlowWireProtection.Confidential, 4096, 32768, 6);
            return new Definition(carrier, false, 0, 0, 31, prefix, body, null, control);
        }

        /// <summary>
        /// Records the independent fields and complete literal shared-envelope prefix.
        /// </summary>
        private static Definition SharedControl(
            FlowControl control, uint revision, uint messageId, string prefix, string bodyHex)
        {
            return new Definition(FlowCarrier.SharedUasc, false, control.Flow,
                revision, messageId, prefix, bodyHex, null, control);
        }

        /// <summary>
        /// Selects independent opaque bytes and fixed length prefixes for DATA layouts.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        private static Definition Data(string name)
        {
            int separator = name.IndexOf('_', StringComparison.Ordinal);
            if (separator < 0)
            {
                throw new ArgumentException("Unknown public wire specimen.", nameof(name));
            }
            FlowCarrier carrier = name[..separator] switch
            {
                "dedicated-uasc" => FlowCarrier.DedicatedUasc,
                "shared-uasc" => FlowCarrier.SharedUasc,
                "quic" => FlowCarrier.Quic,
                "webtransport" => FlowCarrier.WebTransport,
                _ => throw new ArgumentException("Unknown public wire carrier.", nameof(name))
            };
            string body = name[(separator + 1)..];
            bool datagram = body.StartsWith("datagram", StringComparison.Ordinal);
            if (datagram)
            {
                body = body.Length == 8 ? "opaque" : body[9..];
            }
            (string header, string hex, string? json) = body switch
            {
                "opaque" => ("0c000000000100000001000000", "6b3921", null),
                "uadp" => ("23000000000100000001000000",
                    "f10112340f4422040302010605080701100901010006edffffff", null),
                "json" => ("a3000000000100000001000000", string.Empty, kJsonPayload),
                "tail" => ("0c000000000100000001000000", "6b392109", null),
                _ => throw new ArgumentException("Unknown public wire body.", nameof(name))
            };
            string address = carrier is FlowCarrier.DedicatedUasc or FlowCarrier.SharedUasc || datagram
                ? "01000000" : string.Empty;
            return new Definition(carrier, datagram, 1, 1, 1, address + header, hex, json, null);
        }

        /// <summary>
        /// Separates literal bytes, transport metadata and typed expectations.
        /// </summary>
        private sealed record Definition(
            FlowCarrier Carrier,
            bool Datagram,
            uint ChannelId,
            uint Revision,
            uint MessageId,
            string PrefixHex,
            string BodyHex,
            string? Json,
            FlowControl? Control);

        /// <summary>Literal binary descriptor fields, independent of the typed descriptor factory.</summary>
        private const string kParametersHex =
            "1700000075726e3a6578616d706c653a776972653a73616d706c65" +
            "2a00000075726e3a6f7063666f756e646174696f6e3a75613a666c6f773a7061796c6f61643a6f70617175653a31" +
            "180000006170706c69636174696f6e2f6f637465742d73747265616d" +
            "020000000400000010000000000000000000000000";

        /// <summary>Independent JSON NetworkMessage-shaped bytes, including intentional outside whitespace.</summary>
        private const string kJsonPayload =
            " {\"MessageId\":\"sample-23\",\"MessageType\":\"ua-data\",\"PublisherId\":\"bench-7\"," +
            "\"Messages\":[{\"DataSetWriterId\":57,\"Payload\":{\"Level\":{\"Type\":6,\"Body\":-19}}}]} \n";
    }
}
