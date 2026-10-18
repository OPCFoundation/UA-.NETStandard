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
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Opc.Ua.DataChannels.Wire;

namespace Opc.Ua.Core.DataChannels.Tests.Wire
{
    /// <summary>
    /// Checks every binary operation and malformed field partition without using the encoder as a byte oracle.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class FlowControlCodecTests
    {
        /// <summary>
        /// Gets all opcodes and the conditional Session-grant representation.
        /// </summary>
        public static IEnumerable<string> Controls => FrozenWire.ControlNames().Concat(["session_open"]);

        /// <summary>
        /// Supplies typed carrier-test descriptors independently of production decoding.
        /// </summary>
        internal static FlowWireParameters Parameters(
            FlowWireDelivery delivery = FlowWireDelivery.Ordered,
            FlowWireProtection protection = FlowWireProtection.Confidential,
            uint maxReceiveBytes = 4096,
            double maxRate = 0)
        {
            return new FlowWireParameters(
                "urn:example:wire:sample", "urn:opcfoundation:ua:flow:payload:opaque:1",
                "application/octet-stream", FlowWireDirection.Duplex, delivery, protection,
                new FlowWireLimits(1024, maxReceiveBytes, maxRate, 0), false);
        }

        /// <summary>
        /// Every proper prefix ends inside a required field, including the conditional grant.
        /// </summary>
        [TestCaseSource(nameof(Controls))]
        public void EveryBinaryFieldTruncationIsRejected(string name)
        {
            var fixture = new FrozenWire(name);
            for (int length = 0; length < fixture.ExpectedBody.Length; length++)
            {
                ByteString body = fixture.ExpectedBody[..length];
                FlowCodecException? error = Assert.Throws<FlowCodecException>(() =>
                    Decode(body, fixture.Revision));
                Assert.That(error!.Error, Is.EqualTo(FlowCodecError.Truncated),
                    $"Fixture {name}, body prefix {length}.");
            }
        }

        /// <summary>
        /// A complete operation consumes its body exactly, regardless of the trailing byte's value.
        /// </summary>
        [TestCaseSource(nameof(Controls))]
        public void EveryOperationRejectsTrailingBytes(string name)
        {
            var fixture = new FrozenWire(name);
            foreach (byte suffix in new byte[] { 0, 1, 0xFF })
            {
                byte[] body = [.. fixture.ExpectedBody.ToArray(), suffix];
                AssertError(body, FlowCodecError.TrailingBytes, fixture.Revision);
            }
        }

        /// <summary>
        /// Unknown opcodes are unsupported, not candidates for a JSON or Service decoder.
        /// </summary>
        [Test]
        public void EveryUnknownOpcodeIsUnsupported()
        {
            for (int opcode = 15; opcode <= byte.MaxValue; opcode++)
            {
                var body = new ByteString(new byte[] { (byte)opcode });
                FlowCodecException? error = Assert.Throws<FlowCodecException>(() => Decode(body));
                Assert.Multiple(() =>
                {
                    Assert.That(error!.Error, Is.EqualTo(FlowCodecError.UnknownOperation));
                    Assert.That(error.Status, Is.EqualTo(FlowWireStatus.Unsupported));
                });
            }
        }

        /// <summary>
        /// Every discriminator field rejects all bytes outside its closed value set.
        /// </summary>
        [TestCase("quic_hello", 5, 3)]
        [TestCase("quic_hello", 93, 1)]
        [TestCase("control_open", 114, 2)]
        [TestCase("control_open", 115, 1)]
        [TestCase("control_open", 116, 1)]
        [TestCase("control_open", 135, 1)]
        [TestCase("control_accept", 114, 2)]
        [TestCase("control_accept", 115, 1)]
        [TestCase("control_accept", 116, 1)]
        [TestCase("control_reset", 5, 10)]
        [TestCase("control_status", 5, 10)]
        public void UnknownDiscriminatorsAreUnsupported(string name, int offset, int maximum)
        {
            var fixture = new FrozenWire(name);
            for (int value = maximum + 1; value <= byte.MaxValue; value++)
            {
                byte[] bytes = fixture.ExpectedBody.ToArray();
                bytes[offset] = (byte)value;
                FlowCodecException? error = Assert.Throws<FlowCodecException>(() =>
                    Decode(new ByteString(bytes), fixture.Revision));
                Assert.That(error!.Error, Is.EqualTo(FlowCodecError.UnknownDiscriminator));
                Assert.That(error.Status, Is.EqualTo(FlowWireStatus.Unsupported));
            }
        }

        /// <summary>
        /// A Boolean is not an arbitrary nonzero byte.
        /// </summary>
        [TestCase("control_open")]
        [TestCase("control_accept")]
        public void BooleanAcceptsOnlyZeroAndOne(string name)
        {
            var fixture = new FrozenWire(name);
            for (int value = 0; value <= byte.MaxValue; value++)
            {
                byte[] bytes = fixture.ExpectedBody.ToArray();
                bytes[134] = (byte)value;
                if (value <= 1)
                {
                    FlowControl control = Decode(new ByteString(bytes));
                    FlowWireParameters parameters = control is FlowOpen open
                        ? open.Parameters : ((FlowAccept)control).Parameters;
                    Assert.That(parameters.AllowQueueExpiry, Is.EqualTo(value == 1));
                }
                else
                {
                    AssertError(bytes, FlowCodecError.OutOfRange);
                }
            }
        }

        /// <summary>
        /// Length validation precedes string allocation and distinguishes malformed lengths from local bounds.
        /// </summary>
        [TestCase("quic_hello", 6, 4096)]
        [TestCase("control_open", 13, 4096)]
        [TestCase("control_open", 40, 4096)]
        [TestCase("control_open", 86, 255)]
        public void StringsRejectNullEmptyAndOversizeLengths(string name, int offset, int maximum)
        {
            var fixture = new FrozenWire(name);
            foreach (int length in new[] { -1, int.MinValue, 0, maximum + 1, int.MaxValue })
            {
                byte[] bytes = fixture.ExpectedBody.ToArray();
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), length);
                AssertError(bytes, length <= 0 ? FlowCodecError.InvalidLength : FlowCodecError.TooLarge,
                    fixture.Revision);
            }
        }

        /// <summary>
        /// URI and content-type limits are measured in UTF-8 bytes, with before/at/after boundary checks.
        /// </summary>
        [TestCase("quic_hello", 6, 4096, true)]
        [TestCase("control_open", 13, 4096, true)]
        [TestCase("control_open", 40, 4096, true)]
        [TestCase("control_open", 86, 255, false)]
        public void StringsEnforceTheirExactByteBounds(string name, int offset, int maximum, bool uri)
        {
            var fixture = new FrozenWire(name);
            foreach (int length in new[] { maximum - 1, maximum, maximum + 1 })
            {
                string value = uri ? "urn:x:" + new string('x', length - 6) : new string('x', length);
                ByteString bytes = ReplaceString(fixture.ExpectedBody, offset, Encoding.UTF8.GetBytes(value));
                if (length > maximum)
                {
                    AssertError(bytes.ToArray(), FlowCodecError.TooLarge, fixture.Revision);
                }
                else
                {
                    FlowControl decoded = Decode(bytes, fixture.Revision);
                    FlowRecord encoded = FlowControlCodec.Encode(decoded, fixture.Revision, 99, new());
                    Assert.That(encoded.Body.ToArray(), Is.EqualTo(bytes.ToArray()));
                }
            }
        }

        /// <summary>
        /// Malformed UTF-8 is rejected within a length-delimited field, not interpreted as another operation.
        /// </summary>
        [TestCase("80")]
        [TestCase("c0af")]
        [TestCase("c1bf")]
        [TestCase("c2")]
        [TestCase("c220")]
        [TestCase("e080af")]
        [TestCase("e0a0")]
        [TestCase("eda080")]
        [TestCase("edbfbf")]
        [TestCase("f08080af")]
        [TestCase("f09080")]
        [TestCase("f4908080")]
        [TestCase("f5808080")]
        [TestCase("fe")]
        [TestCase("ff")]
        public void MalformedUtf8FieldsAreRejected(string invalidHex)
        {
            var fixture = new FrozenWire("control_open");
            byte[] invalid = ByteString.FromHexString(invalidHex).ToArray();
            foreach (int offset in new[] { 13, 40, 86 })
            {
                ByteString bytes = ReplaceString(fixture.ExpectedBody, offset, invalid);
                AssertError(bytes.ToArray(), FlowCodecError.InvalidUtf8);
            }
        }

        /// <summary>
        /// String encoding preserves valid Unicode without JSON escaping or replacement.
        /// </summary>
        [TestCase("a")]
        [TestCase("\u00E9")]
        [TestCase("\u20AC")]
        [TestCase("\U0001F642")]
        public void Utf8ContentTypePreservesEveryScalarWidth(string value)
        {
            var fixture = new FrozenWire("control_open");
            byte[] utf8 = new UTF8Encoding(false, true).GetBytes(value);
            ByteString body = ReplaceString(fixture.ExpectedBody, 86, utf8);
            var open = (FlowOpen)Decode(body);
            Assert.That(open.Parameters.ContentType, Is.EqualTo(value));
            FlowRecord encoded = FlowControlCodec.Encode(open, 1, 99, new());
            Assert.That(encoded.Body.ToArray(), Is.EqualTo(body.ToArray()));
        }

        /// <summary>
        /// Encoding never repairs lone UTF-16 surrogates.
        /// </summary>
        [TestCase(0xD800, false)]
        [TestCase(0xDC00, false)]
        [TestCase(0xD800, true)]
        public void EncoderRejectsInvalidUnicode(int surrogate, bool suffix)
        {
            string contentType = new string((char)surrogate, 1) + (suffix ? "x" : string.Empty);
            var original = (FlowOpen)new FrozenWire("control_open").ExpectedControl!;
            FlowWireParameters old = original.Parameters;
            var parameters = new FlowWireParameters(
                old.Resource, old.Protocol, contentType, old.Direction, old.Delivery,
                old.Protection, old.Limits, old.AllowQueueExpiry);
            var changed = new FlowOpen(1, 0, parameters, FlowWireAuthorization.Configured, null);
            FrozenWire.Fails(() => FlowControlCodec.Encode(changed, 1, 1, new()),
                FlowCodecError.InvalidUnicode);
        }

        /// <summary>
        /// The challenge is a raw fixed-size ByteString, never null, variable length or Base64.
        /// </summary>
        [TestCase(-1)]
        [TestCase(0)]
        [TestCase(31)]
        [TestCase(33)]
        [TestCase(int.MaxValue)]
        public void ChallengeRequiresExactlyThirtyTwoBytes(int length)
        {
            byte[] bytes = new FrozenWire("quic_hello").ExpectedBody.ToArray();
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(57), length);
            AssertError(bytes, FlowCodecError.InvalidChallenge, 0);
        }

        /// <summary>
        /// Guid fields retain OPC UA Binary's mixed field order rather than textual or network-byte-order encoding.
        /// </summary>
        [Test]
        public void GuidFieldsUseTheExactBinaryFieldOrder()
        {
            var hello = new FrozenWire("quic_hello");
            Assert.That(hello.ExpectedBody[33..49].ToHexString().ToLowerInvariant(),
                Is.EqualTo("89cb3ed03a96474bb6ab569a74ad1230"));
            var decoded = (FlowHello)Decode(hello.ExpectedBody, 0);
            Assert.That(decoded.Context, Is.EqualTo(new Guid("d03ecb89-963a-4b47-b6ab-569a74ad1230")));
            var session = (FlowOpen)Decode(new FrozenWire("session_open").ExpectedBody);
            Assert.That(session.Grant, Is.EqualTo(new Guid("a6bbccdd-1020-4050-90ab-c0d0e0f01020")));
        }

        /// <summary>
        /// Configured OPEN has no grant bytes or nullable marker; Session OPEN requires exactly one Guid.
        /// </summary>
        [Test]
        public void OpenGrantPresenceIsDeterminedOnlyByAuthorization()
        {
            ByteString configured = new FrozenWire("control_open").ExpectedBody;
            var open = (FlowOpen)Decode(configured);
            Assert.That(open.Grant, Is.Null);
            Assert.That(configured.Length, Is.EqualTo(136));
            byte[] extra = [.. configured.ToArray(), .. new byte[16]];
            AssertError(extra, FlowCodecError.TrailingBytes);
            byte[] missing = configured.ToArray();
            missing[135] = 1;
            AssertError(missing, FlowCodecError.Truncated);
            ByteString session = new FrozenWire("session_open").ExpectedBody;
            Assert.That(session.Length, Is.EqualTo(configured.Length + 16));
            Assert.That(((FlowOpen)Decode(session)).Grant.HasValue, Is.True);
        }

        /// <summary>
        /// UInt64 fields preserve values beyond Double's exact-integer range.
        /// </summary>
        [TestCase(0UL)]
        [TestCase(9007199254740993UL)]
        [TestCase(ulong.MaxValue)]
        public void ProbeTimestampsPreserveAllUnsignedBits(ulong timestamp)
        {
            byte[] body = new FrozenWire("control_ping").ExpectedBody.ToArray();
            BinaryPrimitives.WriteUInt64LittleEndian(body.AsSpan(5), timestamp);
            var probe = (FlowProbe)Decode(new ByteString(body), 0);
            Assert.That(probe.Timestamp, Is.EqualTo(timestamp));
            Assert.That(FlowControlCodec.Encode(probe, 0, 99, new()).Body.ToArray(), Is.EqualTo(body));
        }

        /// <summary>
        /// Binary64 rates reject non-finite and negative values without a textual-number parser.
        /// </summary>
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(double.NegativeInfinity)]
        [TestCase(-1d)]
        [TestCase(-double.Epsilon)]
        public void InvalidRatesAreRejected(double rate)
        {
            byte[] body = new FrozenWire("control_prepare").ExpectedBody.ToArray();
            BinaryPrimitives.WriteInt64LittleEndian(body.AsSpan(13), BitConverter.DoubleToInt64Bits(rate));
            AssertError(body, FlowCodecError.OutOfRange, 2);
        }

        /// <summary>
        /// Equal zero rates produce the same descriptor and wire fingerprint.
        /// </summary>
        [Test]
        public void NegativeZeroRateCanonicalizesToPositiveZero()
        {
            byte[] body = new FrozenWire("control_prepare").ExpectedBody.ToArray();
            BinaryPrimitives.WriteInt64LittleEndian(body.AsSpan(13), long.MinValue);
            var prepare = (FlowPrepare)Decode(new ByteString(body), 2);
            Assert.That(BitConverter.DoubleToInt64Bits(prepare.Limits.MaxRate), Is.Zero);
            FlowRecord encoded = FlowControlCodec.Encode(prepare, 2, 99, new());
            Assert.That(BinaryPrimitives.ReadInt64LittleEndian(encoded.Body.Span[13..]), Is.Zero);
            Assert.That(BitConverter.DoubleToInt64Bits(new FlowWireLimits(1, 18, -0d, 0).MaxRate), Is.Zero);
        }

        /// <summary>
        /// All finite nonnegative rates remain exact, including the smallest and largest positive doubles.
        /// </summary>
        [TestCase(0d)]
        [TestCase(double.Epsilon)]
        [TestCase(75.5d)]
        [TestCase(double.MaxValue)]
        public void ValidRatesPreserveTheirBinaryValue(double rate)
        {
            byte[] body = new FrozenWire("control_prepare").ExpectedBody.ToArray();
            BinaryPrimitives.WriteInt64LittleEndian(body.AsSpan(13), BitConverter.DoubleToInt64Bits(rate));
            var prepare = (FlowPrepare)Decode(new ByteString(body), 2);
            Assert.That(prepare.Limits.MaxRate, Is.EqualTo(rate));
            Assert.That(FlowControlCodec.Encode(prepare, 2, 99, new()).Body.ToArray(), Is.EqualTo(body));
        }

        /// <summary>
        /// Control size admission occurs before typed field decoding or encoded-body allocation.
        /// </summary>
        [TestCaseSource(nameof(Controls))]
        public void EveryOperationHonorsTheControlBodyLimit(string name)
        {
            var fixture = new FrozenWire(name);
            foreach (int size in new[] { fixture.ExpectedBody.Length - 1, fixture.ExpectedBody.Length })
            {
                var limits = new FlowCodecLimits(maxControlBytes: size);
                var record = new FlowRecord(FlowRecordKind.Control, fixture.Revision, 99, fixture.ExpectedBody);
                if (size < fixture.ExpectedBody.Length)
                {
                    FrozenWire.Fails(() => FlowControlCodec.Decode(in record, limits), FlowCodecError.TooLarge);
                    FrozenWire.Fails(() => FlowControlCodec.Encode(
                        fixture.ExpectedControl!, fixture.Revision, 99, limits), FlowCodecError.TooLarge);
                }
                else
                {
                    FlowControl value = FlowControlCodec.Decode(in record, limits);
                    Assert.That(FlowControlCodec.Encode(value, fixture.Revision, 99, limits).Body.ToArray(),
                        Is.EqualTo(fixture.ExpectedBody.ToArray()));
                }
            }
        }

        /// <summary>
        /// Header constraints remain separate from the binary body representation.
        /// </summary>
        [TestCase("quic_hello", 1u)]
        [TestCase("control_open", 0u)]
        [TestCase("control_open", 2u)]
        [TestCase("control_accept", 2u)]
        [TestCase("control_ready", 2u)]
        [TestCase("control_prepare", 1u)]
        [TestCase("control_accept-update", 1u)]
        [TestCase("control_commit", 1u)]
        [TestCase("control_ack", 1u)]
        [TestCase("control_abort-update", 1u)]
        [TestCase("control_ping", 1u)]
        public void OperationRevisionRulesRemainEnforced(string name, uint revision)
        {
            AssertError(new FrozenWire(name).ExpectedBody.ToArray(), FlowCodecError.InvalidRevision, revision);
        }

        /// <summary>
        /// Both UASC profiles charge the FLO address in receive limits and forbid native stream claims.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void UascCarrierChecksIncludeTheFlowAddress(bool shared)
        {
            FlowCarrier carrier = shared ? FlowCarrier.SharedUasc : FlowCarrier.DedicatedUasc;
            var old = (FlowOpen)new FrozenWire("control_open").ExpectedControl!;
            FlowWireParameters p = old.Parameters;
            var tooSmall = new FlowWireParameters(p.Resource, p.Protocol, p.ContentType, p.Direction,
                p.Delivery, p.Protection, new FlowWireLimits(64, 80, 0, 0), false);
            FrozenWire.Fails(() => FlowControlCodec.ValidateCarrier(
                new FlowOpen(1, 0, tooSmall, FlowWireAuthorization.Configured, null), carrier),
                FlowCodecError.InvalidDescriptor);
            FrozenWire.Fails(() => FlowControlCodec.ValidateCarrier(
                new FlowOpen(1, 4, p, FlowWireAuthorization.Configured, null), carrier),
                FlowCodecError.InvalidStream);
        }

        /// <summary>
        /// Keeps operation decoding independent of a production encoder.
        /// </summary>
        private static FlowControl Decode(ByteString body, uint revision = 1)
        {
            var record = new FlowRecord(FlowRecordKind.Control, revision, 99, body);
            return FlowControlCodec.Decode(in record, new());
        }

        /// <summary>
        /// Requires the specific malformed-field outcome rather than any thrown exception.
        /// </summary>
        private static void AssertError(byte[] body, FlowCodecError expected, uint revision = 1)
        {
            FlowCodecException? error = Assert.Throws<FlowCodecException>(() =>
                Decode(new ByteString(body), revision));
            Assert.That(error!.Error, Is.EqualTo(expected));
        }

        /// <summary>
        /// Replaces a literal length-delimited field without invoking either production serialization direction.
        /// </summary>
        private static ByteString ReplaceString(ByteString body, int offset, byte[] value)
        {
            int oldLength = BinaryPrimitives.ReadInt32LittleEndian(body.Span[offset..]);
            byte[] bytes = new byte[body.Length - oldLength + value.Length];
            body.Span[..offset].CopyTo(bytes);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), value.Length);
            value.AsSpan().CopyTo(bytes.AsSpan(offset + 4));
            body.Span[(offset + 4 + oldLength)..].CopyTo(bytes.AsSpan(offset + 4 + value.Length));
            return new ByteString(bytes);
        }
    }
}
