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
using System.Linq;
using NUnit.Framework;
using Opc.Ua.DataChannels.Wire;

namespace Opc.Ua.Core.DataChannels.Tests.Wire
{
    /// <summary>
    /// Checks carrier payload envelopes without simulating TLS, UASC protection or native transport framing.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class FlowCarrierCodecTests
    {
        /// <summary>
        /// Checks RequestId and final-chunk obligations using independent records.
        /// </summary>
        [TestCase(false, 1u, 'F', "RequestIdMismatch")]
        [TestCase(false, 2u, 'F', "RequestIdMismatch")]
        [TestCase(true, 1u, 'F', "RequestIdMismatch")]
        [TestCase(false, 1u, 'C', "NonFinalUasc")]
        [TestCase(false, 1u, 'A', "NonFinalUasc")]
        [TestCase(true, 0u, 'C', "NonFinalUasc")]
        [TestCase(true, 0u, 'A', "NonFinalUasc")]
        [TestCase(false, 1u, 'X', "NonFinalUasc")]
        [TestCase(true, 0u, '\0', "NonFinalUasc")]
        public void UascEnvelopeConstraintsAreChecked(bool shared, uint requestId, char finality, string error)
        {
            var frozen = new FrozenWire(shared ? "shared-uasc_opaque" : "dedicated-uasc_opaque");
            FlowCodecException exception = Assert.Throws<FlowCodecException>(() =>
                FlowCarrierCodec.DecodeUascPayload(frozen.Carrier, (byte)finality, requestId,
                    frozen.CarrierPayload, new()))!;
            Assert.That(exception.Error.ToString(), Is.EqualTo(error));
        }

        /// <summary>
        /// Requires DATA flow addressing and exact shared prefix/control-body agreement.
        /// </summary>
        [TestCase("shared-uasc_opaque", 0u)]
        [TestCase("control_accept", 2u)]
        [TestCase("shared-uasc_hello", 1u)]
        [TestCase("dedicated-uasc_opaque", 0u)]
        [TestCase("dedicated-uasc_opaque", 2u)]
        [TestCase("dedicated-uasc_hello", 1u)]
        public void ChannelPrefixMustMatchTheBodyAndProfile(string name, uint channel)
        {
            var frozen = new FrozenWire(name);
            byte[] payload = frozen.CarrierPayload.ToArray();
            BinaryPrimitives.WriteUInt32LittleEndian(payload, channel);
            FrozenWire.Fails(() => FlowCarrierCodec.DecodeUascPayload(
                frozen.Carrier, (byte)'F', 0, new ByteString(payload), new()), FlowCodecError.InvalidFlow);
        }

        /// <summary>
        /// Keeps the ChannelId outside the common length and uses little endian on both applicable envelopes.
        /// </summary>
        [Test]
        public void ChannelIdIsLittleEndianAndOutsideRecordLength()
        {
            FlowRecord record = new FrozenWire("quic_opaque").Decode().Record;
            const uint channel = 0x78563412;
            const string expected = "123456780c0000000001000000010000006b3921";
            ByteString shared = FlowCarrierCodec.EncodeUascPayload(
                FlowCarrier.SharedUasc, channel, in record, new(), out uint requestId);
            Assert.That(shared.ToHexString().ToLowerInvariant(), Is.EqualTo(expected));
            Assert.That(requestId, Is.Zero);
            foreach (FlowCarrier carrier in new[] { FlowCarrier.Quic, FlowCarrier.WebTransport })
            {
                ByteString datagram = FlowCarrierCodec.EncodeDatagram(carrier, channel, in record, 20, new());
                Assert.That(datagram.ToHexString().ToLowerInvariant(), Is.EqualTo(expected));
                Assert.That(FlowCarrierCodec.DecodeDatagram(carrier, datagram, 20, new()).ChannelId,
                    Is.EqualTo(channel));
            }
        }

        /// <summary>
        /// Preserves unsigned maximum ChannelId and record identifiers while requiring zero RequestId.
        /// </summary>
        [Test]
        public void EnvelopeIdentifiersPreserveUnsignedMaxima()
        {
            var dedicated = ByteString.FromHexString("010000000c00000000ffffffffffffffff6b3921");
            var prefixed = ByteString.FromHexString("ffffffff0c00000000ffffffffffffffff6b3921");
            var record = new FlowRecord(FlowRecordKind.Data, uint.MaxValue, uint.MaxValue,
                new ByteString("k9!"u8.ToArray()));
            Assert.That(FlowCarrierCodec.DecodeUascPayload(
                FlowCarrier.DedicatedUasc, (byte)'F', 0, dedicated, new()).Record.MessageId,
                Is.EqualTo(uint.MaxValue));
            Assert.That(FlowCarrierCodec.EncodeUascPayload(FlowCarrier.DedicatedUasc, 1, in record, new(),
                out uint requestId).ToArray(), Is.EqualTo(dedicated.ToArray()));
            Assert.That(requestId, Is.Zero);
            Assert.That(FlowCarrierCodec.DecodeUascPayload(
                FlowCarrier.SharedUasc, (byte)'F', 0, prefixed, new()).ChannelId, Is.EqualTo(uint.MaxValue));
            Assert.That(FlowCarrierCodec.EncodeUascPayload(FlowCarrier.SharedUasc, uint.MaxValue,
                in record, new(), out requestId).ToArray(), Is.EqualTo(prefixed.ToArray()));
            Assert.That(requestId, Is.Zero);
            foreach (FlowCarrier carrier in new[] { FlowCarrier.Quic, FlowCarrier.WebTransport })
            {
                Assert.That(FlowCarrierCodec.DecodeDatagram(carrier, prefixed, 20, new()).ChannelId,
                    Is.EqualTo(uint.MaxValue));
                Assert.That(FlowCarrierCodec.EncodeDatagram(
                    carrier, uint.MaxValue, in record, 20, new()).ToArray(), Is.EqualTo(prefixed.ToArray()));
            }
        }

        /// <summary>
        /// Rejects truncated and concatenated UASC payloads rather than treating them as native stream segments.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void UascPayloadRejectsEveryTruncationAndConcatenation(bool shared)
        {
            var specimen = new FrozenWire(shared ? "shared-uasc_opaque" : "dedicated-uasc_opaque");
            const uint requestId = 0;
            for (int size = 0; size < specimen.Bytes.Length; size++)
            {
                ByteString shortened = specimen.Bytes[..size];
                FrozenWire.Fails(() => FlowCarrierCodec.DecodeUascPayload(
                    specimen.Carrier, (byte)'F', requestId, shortened, new()), FlowCodecError.Truncated);
            }
            ByteString concatenated = new(specimen.Bytes.ToArray().Concat(specimen.Bytes.ToArray()).ToArray());
            FrozenWire.Fails(() => FlowCarrierCodec.DecodeUascPayload(
                specimen.Carrier, (byte)'F', requestId, concatenated, new()), FlowCodecError.TrailingBytes);
        }

        /// <summary>
        /// Includes the ChannelId in native capacity, with before/at/after boundary checks.
        /// </summary>
        [TestCase("quic", 19, false)]
        [TestCase("quic", 20, true)]
        [TestCase("quic", 21, true)]
        [TestCase("webtransport", 19, false)]
        [TestCase("webtransport", 20, true)]
        [TestCase("webtransport", 21, true)]
        public void NativeDatagramCapacityIncludesExternalChannelPrefix(string name, int capacity, bool accepted)
        {
            var frozen = new FrozenWire(name + "_datagram");
            FlowRecord record = new FrozenWire(name + "_opaque").Decode().Record;
            if (!accepted)
            {
                FrozenWire.Fails(() => FlowCarrierCodec.DecodeDatagram(frozen.Carrier, frozen.Bytes, capacity, new()),
                    FlowCodecError.TooLarge);
                FrozenWire.Fails(() => FlowCarrierCodec.EncodeDatagram(frozen.Carrier, 1, in record, capacity, new()),
                    FlowCodecError.TooLarge);
            }
            else
            {
                Assert.That(FlowCarrierCodec.DecodeDatagram(frozen.Carrier, frozen.Bytes, capacity, new()).Record.Body
                    .ToArray(), Is.EqualTo("k9!"u8.ToArray()));
                Assert.That(FlowCarrierCodec.EncodeDatagram(frozen.Carrier, 1, in record, capacity, new()).ToArray(),
                    Is.EqualTo(frozen.Bytes.ToArray()));
            }
        }

        /// <summary>
        /// Checks native empty DATA, zero channels, CONTROL refusal and unavailable datagram capacity.
        /// </summary>
        [TestCase("quic")]
        [TestCase("webtransport")]
        public void DatagramEnvelopeConstraintsAreChecked(string name)
        {
            FlowCarrier carrier = name == "quic" ? FlowCarrier.Quic : FlowCarrier.WebTransport;
            var empty = new FlowRecord(FlowRecordKind.Data, 1, 1, ByteString.Empty);
            ByteString encoded = FlowCarrierCodec.EncodeDatagram(carrier, 1, in empty, 17, new());
            Assert.That(encoded.Length, Is.EqualTo(17));
            Assert.That(FlowCarrierCodec.DecodeDatagram(carrier, encoded, 17, new()).Record.Body.IsEmpty, Is.True);
            FrozenWire.Fails(() => FlowCarrierCodec.EncodeDatagram(carrier, 0, in empty, 17, new()),
                FlowCodecError.InvalidFlow);
            byte[] zero = encoded.ToArray();
            BinaryPrimitives.WriteUInt32LittleEndian(zero, 0);
            FrozenWire.Fails(() => FlowCarrierCodec.DecodeDatagram(carrier, new ByteString(zero), 17, new()),
                FlowCodecError.InvalidFlow);
            FrozenWire.Fails(() => FlowCarrierCodec.DecodeDatagram(carrier, encoded, 0, new()),
                FlowCodecError.TooLarge);
            var control = new FlowRecord(FlowRecordKind.Control, 1, 1, new ByteString(new byte[] { 0xFF }));
            FrozenWire.Fails(() => FlowCarrierCodec.EncodeDatagram(carrier, 1, in control, 100, new()),
                FlowCodecError.DatagramControl);
            byte[] input = encoded.ToArray();
            input[8] = 1;
            FrozenWire.Fails(() => FlowCarrierCodec.DecodeDatagram(carrier, new ByteString(input), 17, new()),
                FlowCodecError.DatagramControl);
        }

        /// <summary>
        /// Forbids fragmentation and concatenation inside an individual datagram.
        /// </summary>
        [TestCase("quic_datagram")]
        [TestCase("webtransport_datagram")]
        public void NativeDatagramsRejectEveryTruncationAndEveryTail(string name)
        {
            var frozen = new FrozenWire(name);
            for (int length = 0; length < frozen.Bytes.Length; length++)
            {
                ByteString shortened = frozen.Bytes[..length];
                FrozenWire.Fails(() => FlowCarrierCodec.DecodeDatagram(frozen.Carrier, shortened, 1024, new()),
                    FlowCodecError.Truncated);
            }
            foreach (byte[] tail in new[] { [0], frozen.Bytes.ToArray() })
            {
                ByteString input = new(frozen.Bytes.ToArray().Concat(tail).ToArray());
                FrozenWire.Fails(() => FlowCarrierCodec.DecodeDatagram(frozen.Carrier, input, 1024, new()),
                    FlowCodecError.TrailingBytes);
            }
        }

        /// <summary>
        /// Allows explicit UASC chunk/footer-derived bounds without implementing security.
        /// </summary>
        [TestCase(false, 0)]
        [TestCase(false, 33)]
        [TestCase(true, 0)]
        [TestCase(true, 33)]
        public void UascCapacityUsesCallerSuppliedPostSecurityRecordLimit(bool shared, int footer)
        {
            var frozen = new FrozenWire(shared ? "shared-uasc_opaque" : "dedicated-uasc_opaque");
            int capacity = 24 + footer + 4 + 16;
            var limits = new FlowCodecLimits(maxRecordBytes: capacity - 24 - footer - 4);
            Assert.That(frozen.Decode(limits).Record.Body.Length, Is.EqualTo(3));
            var lower = new FlowCodecLimits(maxRecordBytes: limits.MaxRecordBytes - 1);
            FrozenWire.Fails(() => frozen.Decode(lower), FlowCodecError.TooLarge);
        }

        /// <summary>
        /// Rejects a nonzero native stream or unsupported delivery on either UASC profile.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void UascOpenRequiresStreamZeroAndOrderedDelivery(bool shared)
        {
            FlowCarrier carrier = shared ? FlowCarrier.SharedUasc : FlowCarrier.DedicatedUasc;
            var nativeStream = new FlowOpen(1, 4, FlowControlCodecTests.Parameters(),
                FlowWireAuthorization.Configured, null);
            FrozenWire.Fails(() => FlowControlCodec.ValidateCarrier(nativeStream, carrier),
                FlowCodecError.InvalidStream);
            var datagram = new FlowOpen(1, 0, FlowControlCodecTests.Parameters(
                delivery: FlowWireDelivery.Datagram, maxRate: 100), FlowWireAuthorization.Configured, null);
            FrozenWire.Fails(() => FlowControlCodec.ValidateCarrier(datagram, carrier), FlowCodecError.CarrierMismatch);
            var valid = new FlowOpen(1, 0, FlowControlCodecTests.Parameters(), FlowWireAuthorization.Configured, null);
            FlowRecord record = FlowControlCodec.Encode(valid, 1, 1, new());
            ByteString body = FlowCarrierCodec.EncodeUascPayload(carrier, 1, in record, new(), out uint requestId);
            Assert.That(
                FlowCarrierCodec.DecodeUascPayload(carrier, (byte)'F', requestId, body, new()).Control!.Operation,
                Is.EqualTo(FlowOperation.Open));
        }

        /// <summary>
        /// Checks carrier-only semantics without admitting an identity, grant or native stream.
        /// </summary>
        [Test]
        public void CarrierSpecificConstraintsAreChecked()
        {
            FlowControl hello = new FrozenWire("quic_hello").Decode().Control!;
            FrozenWire.Fails(() => FlowControlCodec.ValidateCarrier(hello, FlowCarrier.WebTransport),
                FlowCodecError.CarrierMismatch);
            foreach (FlowCarrier carrier in new[]
            {
                FlowCarrier.DedicatedUasc, FlowCarrier.Quic, FlowCarrier.WebTransport
            })
            {
                FrozenWire.Fails(() => FlowControlCodec.ValidateCarrier(new FlowCredit(1, 0, 0), carrier),
                    FlowCodecError.CarrierMismatch);
            }
            FrozenWire.Fails(() => FlowControlCodec.ValidateCarrier(new FlowSignal(FlowOperation.Ready, 2),
                FlowCarrier.DedicatedUasc), FlowCodecError.InvalidFlow);
            var tight = new FlowAccept(1, 0, FlowControlCodecTests.Parameters(maxReceiveBytes: 1040));
            FrozenWire.Fails(() => FlowControlCodec.ValidateCarrier(tight, FlowCarrier.SharedUasc),
                FlowCodecError.InvalidDescriptor);
            var exact = new FlowAccept(1, 0, FlowControlCodecTests.Parameters(maxReceiveBytes: 1041));
            FlowControlCodec.ValidateCarrier(exact, FlowCarrier.SharedUasc);
            Assert.That(exact.Parameters.Limits.MaxReceiveBytes, Is.EqualTo(1041));
            FrozenWire.Fails(() => FlowControlCodec.ValidateCarrier(new FlowPrepare(1,
                new FlowWireLimits(1024, 1040, 0, 0)), FlowCarrier.SharedUasc), FlowCodecError.InvalidDescriptor);
            foreach (FlowCarrier carrier in new[] { FlowCarrier.Quic, FlowCarrier.WebTransport })
            {
                var integrity = new FlowAccept(1, 4,
                    FlowControlCodecTests.Parameters(protection: FlowWireProtection.Integrity));
                FrozenWire.Fails(() => FlowControlCodec.ValidateCarrier(integrity, carrier),
                    FlowCodecError.CarrierMismatch);
                var unverifiedStream = new FlowAccept(1, ulong.MaxValue, FlowControlCodecTests.Parameters());
                FlowControlCodec.ValidateCarrier(unverifiedStream, carrier);
                Assert.That(unverifiedStream.Stream, Is.EqualTo(ulong.MaxValue));
            }
        }

        /// <summary>
        /// Refuses native integrity-only claims without deciding whether a UASC source policy permits integrity.
        /// </summary>
        [TestCase("dedicated-uasc", true)]
        [TestCase("shared-uasc", true)]
        [TestCase("quic", false)]
        [TestCase("webtransport", false)]
        public void HelloProtectionMatchesIntrinsicCarrierCapabilities(string carrier, bool allowedSyntax)
        {
            byte[] body = new FrozenWire(carrier + "_hello").ExpectedBody.ToArray();
            body[93] = 1;
            var record = new FlowRecord(FlowRecordKind.Control, 0, 99, new ByteString(body));
            if (!allowedSyntax)
            {
                FrozenWire.Fails(() => FlowControlCodec.Decode(in record, new()), FlowCodecError.CarrierMismatch);
            }
            else
            {
                var hello = (FlowHello)FlowControlCodec.Decode(in record, new());
                Assert.That(hello.Protection, Is.EqualTo(FlowWireProtection.Integrity));
                FlowControlCodec.ValidateCarrier(hello, carrier == "dedicated-uasc" ?
                    FlowCarrier.DedicatedUasc : FlowCarrier.SharedUasc);
                FlowRecord encoded = FlowControlCodec.Encode(hello, 0, 99, new());
                Assert.That(encoded.Body.ToArray(), Is.EqualTo(body));
            }
        }

        /// <summary>
        /// Prevents unknown or wrong carrier selectors from silently selecting another format.
        /// </summary>
        [Test]
        public void CarrierSelectorsRejectUnknownOrWrongProfile()
        {
            FlowRecord record = new FrozenWire("quic_opaque").Decode().Record;
            foreach (FlowCarrier carrier in new[] { FlowCarrier.Quic, FlowCarrier.WebTransport, (FlowCarrier)99 })
            {
                FrozenWire.Fails(() => FlowCarrierCodec.DecodeUascPayload(carrier, (byte)'F', 1,
                    ByteString.Empty, new()), FlowCodecError.CarrierMismatch);
                FrozenWire.Fails(() => FlowCarrierCodec.EncodeUascPayload(carrier, 1, in record, new(), out _),
                    FlowCodecError.CarrierMismatch);
            }
            foreach (FlowCarrier carrier in new[]
            {
                FlowCarrier.DedicatedUasc, FlowCarrier.SharedUasc, (FlowCarrier)99
            })
            {
                FrozenWire.Fails(() => FlowCarrierCodec.DecodeDatagram(carrier, ByteString.Empty, 100, new()),
                    FlowCodecError.CarrierMismatch);
                FrozenWire.Fails(() => FlowCarrierCodec.EncodeDatagram(carrier, 1, in record, 100, new()),
                    FlowCodecError.CarrierMismatch);
            }
            Assert.Throws<ArgumentOutOfRangeException>(() => FlowCarrierCodec.DecodeDatagram(
                FlowCarrier.Quic, ByteString.Empty, -1, new()));
            Assert.Throws<ArgumentOutOfRangeException>(() => FlowCarrierCodec.EncodeDatagram(
                FlowCarrier.Quic, 1, in record, -1, new()));
        }

        /// <summary>
        /// Applies dedicated implicit-flow and shared prefix consistency on encoding too.
        /// </summary>
        [Test]
        public void UascEncodingRejectsInconsistentAddresses()
        {
            var data = new FlowRecord(FlowRecordKind.Data, 1, 1, ByteString.Empty);
            FrozenWire.Fails(() => FlowCarrierCodec.EncodeUascPayload(
                FlowCarrier.DedicatedUasc, 2, in data, new(), out _), FlowCodecError.InvalidFlow);
            FrozenWire.Fails(() => FlowCarrierCodec.EncodeUascPayload(
                FlowCarrier.SharedUasc, 0, in data, new(), out _), FlowCodecError.InvalidFlow);
            FlowRecord control = FlowControlCodec.Encode(new FlowSignal(FlowOperation.Ready, 1), 1, 1, new());
            FrozenWire.Fails(() => FlowCarrierCodec.EncodeUascPayload(
                FlowCarrier.SharedUasc, 2, in control, new(), out _), FlowCodecError.InvalidFlow);
            FrozenWire.Fails(() => FlowCarrierCodec.EncodeUascPayload(
                FlowCarrier.DedicatedUasc, 0, in control, new(), out _), FlowCodecError.InvalidFlow);
        }
    }
}
