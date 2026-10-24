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
        /// Keeps the ChannelId outside the common length in native datagrams.
        /// </summary>
        [Test]
        public void ChannelIdIsLittleEndianAndOutsideRecordLength()
        {
            FlowRecord record = new FrozenWire("quic_opaque").Decode().Record;
            const uint channel = 0x78563412;
            const string expected = "123456780c0000000001000000010000006b3921";
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
            var prefixed = ByteString.FromHexString("ffffffff0c00000000ffffffffffffffff6b3921");
            var record = new FlowRecord(FlowRecordKind.Data, uint.MaxValue, uint.MaxValue,
                new ByteString("k9!"u8.ToArray()));
            foreach (FlowCarrier carrier in new[] { FlowCarrier.Quic, FlowCarrier.WebTransport })
            {
                Assert.That(FlowCarrierCodec.DecodeDatagram(carrier, prefixed, 20, new()).ChannelId,
                    Is.EqualTo(uint.MaxValue));
                Assert.That(FlowCarrierCodec.EncodeDatagram(
                    carrier, uint.MaxValue, in record, 20, new()).ToArray(), Is.EqualTo(prefixed.ToArray()));
            }
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
        /// Checks carrier-only semantics without admitting an identity, grant or native stream.
        /// </summary>
        [Test]
        public void CarrierSpecificConstraintsAreChecked()
        {
            FlowControl hello = new FrozenWire("quic_hello").Decode().Control!;
            FrozenWire.Fails(() => FlowControlCodec.ValidateCarrier(hello, FlowCarrier.WebTransport),
                FlowCodecError.CarrierMismatch);
            FlowControlCodec.ValidateCarrier(new FlowCredit(1, 0, 0), FlowCarrier.DedicatedUasc);
            foreach (FlowCarrier carrier in new[] { FlowCarrier.Quic, FlowCarrier.WebTransport })
            {
                FrozenWire.Fails(() => FlowControlCodec.ValidateCarrier(new FlowCredit(1, 0, 0), carrier),
                    FlowCodecError.CarrierMismatch);
            }
            FlowControlCodec.ValidateCarrier(new FlowSignal(FlowOperation.Ready, 2), FlowCarrier.DedicatedUasc);
            var tight = new FlowAccept(1, 0, FlowControlCodecTests.Parameters(maxReceiveBytes: 1023));
            FrozenWire.Fails(() => FlowControlCodec.ValidateCarrier(tight, FlowCarrier.SharedUasc),
                FlowCodecError.InvalidDescriptor);
            var exact = new FlowAccept(1, 0, FlowControlCodecTests.Parameters(maxReceiveBytes: 1024));
            FlowControlCodec.ValidateCarrier(exact, FlowCarrier.SharedUasc);
            Assert.That(exact.Parameters.Limits.MaxReceiveBytes, Is.EqualTo(1024));
            FrozenWire.Fails(() => FlowControlCodec.ValidateCarrier(new FlowPrepare(1,
                new FlowWireLimits(1024, 1023, 0, 0)), FlowCarrier.SharedUasc), FlowCodecError.InvalidDescriptor);
            FrozenWire.Fails(() => FlowControlCodec.ValidateCarrier(
                new FlowAccept(1, 0, FlowControlCodecTests.Parameters(maxReceiveBytes: 1036)),
                FlowCarrier.Quic), FlowCodecError.InvalidDescriptor);
            FlowControlCodec.ValidateCarrier(
                new FlowAccept(1, 0, FlowControlCodecTests.Parameters(maxReceiveBytes: 1037)),
                FlowCarrier.Quic);
            FrozenWire.Fails(() => FlowControlCodec.ValidateCarrier(
                new FlowAccept(1, 0, FlowControlCodecTests.Parameters(
                    FlowWireDelivery.Datagram, maxReceiveBytes: 1040)),
                FlowCarrier.Quic), FlowCodecError.InvalidDescriptor);
            FlowControlCodec.ValidateCarrier(
                new FlowAccept(1, 0, FlowControlCodecTests.Parameters(
                    FlowWireDelivery.Datagram, maxReceiveBytes: 1041)),
                FlowCarrier.Quic);
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

    }
}
