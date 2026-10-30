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
using NUnit.Framework;
using Opc.Ua.DataChannels;
using Opc.Ua.DataChannels.Wire;

namespace Opc.Ua.Core.DataChannels.Tests.Wire
{
    /// <summary>
    /// Checks canonical MSG bodies and separately retained experimental management records.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class UascFlowMessageCodecTests
    {
        private static readonly ByteString s_uadp = ByteString.FromHexString("01010100072a000000");

        /// <summary>
        /// Prefixes exactly one unchanged UADP message with its canonical MSG TypeId.
        /// </summary>
        [Test]
        public void MsgDataPreservesUadpAfterCanonicalTypeId()
        {
            var record = new FlowRecord(FlowRecordKind.Data, 1, 1, s_uadp);
            ByteString encoded = UascFlowMessageCodec.Encode(
                FlowCarrier.SharedUasc, 1, in record, new(), out uint requestId);
            UascFlowMessage decoded = UascFlowMessageCodec.Decode(
                FlowCarrier.SharedUasc, DataChannelUascMessageKind.Msg, (byte)'F', requestId, encoded, new());

            Assert.That(requestId, Is.EqualTo(0x80000001u));
            Assert.That(encoded.ToHexString().ToLowerInvariant(), Is.EqualTo("00fe01010100072a000000"));
            Assert.That(decoded.ChannelId, Is.EqualTo(1u));
            Assert.That(decoded.ApplicationBody.ToArray(), Is.EqualTo(s_uadp.ToArray()));
        }

        /// <summary>
        /// Encodes the TypeId, Revision and OperationId before compact control fields.
        /// </summary>
        [Test]
        public void MsgControlContainsCanonicalTypeIdRevisionOperationAndFields()
        {
            FlowRecord record = FlowControlCodec.Encode(
                new FlowSignal(FlowOperation.End, 1), 1, 11, new());
            ByteString encoded = UascFlowMessageCodec.Encode(
                FlowCarrier.SharedUasc, 1, in record, new(), out uint requestId);
            UascFlowMessage decoded = UascFlowMessageCodec.Decode(
                FlowCarrier.SharedUasc, DataChannelUascMessageKind.Msg, (byte)'F', requestId, encoded, new());

            Assert.That(requestId, Is.Zero);
            Assert.That(encoded.ToHexString().ToLowerInvariant(),
                Is.EqualTo("00ff010000000b0000000a01000000"));
            Assert.That(decoded.ChannelId, Is.EqualTo(1));
            Assert.That(decoded.Record.Revision, Is.EqualTo(1));
            Assert.That(decoded.Record.MessageId, Is.EqualTo(11));
            Assert.That(decoded.Control!.Operation, Is.EqualTo(FlowOperation.End));
        }

        /// <summary>
        /// Carries protected context confirmation in MSG with connection flow zero.
        /// </summary>
        [Test]
        public void MsgCarriesHelloWithZeroRequestId()
        {
            var hello = new FlowHello(
                1,
                FlowCarrier.SharedUasc,
                "urn:local",
                Guid.NewGuid(),
                1,
                new ByteString(new byte[32]),
                FlowWireProtection.Integrity,
                1024,
                4096,
                4);
            FlowRecord record = FlowControlCodec.Encode(hello, 0, 1, new());
            ByteString encoded = UascFlowMessageCodec.Encode(
                FlowCarrier.SharedUasc, 0, in record, new(), out uint requestId);

            UascFlowMessage decoded = UascFlowMessageCodec.Decode(
                FlowCarrier.SharedUasc, DataChannelUascMessageKind.Msg, (byte)'F', requestId, encoded, new());

            Assert.That(decoded.Control, Is.TypeOf<FlowHello>());
            Assert.That(decoded.ChannelId, Is.Zero);
            Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(encoded.Span[2..]), Is.Zero);
            Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(encoded.Span[6..]),
                Is.EqualTo(1));
        }

        /// <summary>
        /// Maps canonical carrier bytes without renumbering logical identities or experimental common records.
        /// </summary>
        [TestCase(DataChannelCarrierKind.DedicatedUasc, 0, 0)]
        [TestCase(DataChannelCarrierKind.SharedUasc, 1, 0)]
        [TestCase(DataChannelCarrierKind.QuicUasc, 2, 1)]
        [TestCase(DataChannelCarrierKind.WebTransportUasc, 3, 2)]
        public void MsgHelloUsesSelectedCarrierTable(
            DataChannelCarrierKind kind,
            int logicalCarrier,
            byte wireCarrier)
        {
            FlowCarrier carrier = FlowModel.ToWire(kind);
            FlowHello hello = Hello((FlowCarrier)logicalCarrier);
            var fixture = new FrozenWire("quic_hello");
            FlowRecord record = FlowControlCodec.Encode(hello, fixture.Revision, fixture.MessageId, new());
            ByteString literal = HelloBody(wireCarrier);
            ByteString encoded = UascFlowMessageCodec.Encode(carrier, 0, in record, new(), out uint requestId);
            UascFlowMessage message = UascFlowMessageCodec.Decode(
                carrier, DataChannelUascMessageKind.Msg, (byte)'F', 0, literal, new());
            Assert.That(message.Control, Is.TypeOf<FlowHello>());
            var decoded = (FlowHello)message.Control!;
            Assert.Multiple(() =>
            {
                Assert.That(requestId, Is.Zero);
                Assert.That(encoded, Is.EqualTo(literal));
                Assert.That(record.Body.Span[5], Is.EqualTo(logicalCarrier));
                Assert.That(message.Record.Body.Span[5], Is.EqualTo(wireCarrier));
                Assert.That(message.Record.Revision, Is.EqualTo(fixture.Revision));
                Assert.That(message.Record.MessageId, Is.EqualTo(fixture.MessageId));
                Assert.That(decoded.Carrier, Is.EqualTo((FlowCarrier)logicalCarrier));
                Assert.That(decoded.Version, Is.EqualTo(hello.Version));
                Assert.That(decoded.Application, Is.EqualTo(hello.Application));
                Assert.That(decoded.Context, Is.EqualTo(hello.Context));
                Assert.That(decoded.Generation, Is.EqualTo(hello.Generation));
                Assert.That(decoded.Challenge, Is.EqualTo(hello.Challenge));
                Assert.That(decoded.Protection, Is.EqualTo(hello.Protection));
                Assert.That(decoded.MaxMessageSize, Is.EqualTo(hello.MaxMessageSize));
                Assert.That(decoded.MaxReceiveBytes, Is.EqualTo(hello.MaxReceiveBytes));
                Assert.That(decoded.MaxFlows, Is.EqualTo(hello.MaxFlows));
            });
        }

        /// <summary>
        /// Refuses foreign carriers and every byte outside the split transport discriminator table.
        /// </summary>
        [TestCase(DataChannelCarrierKind.QuicUasc, 1)]
        [TestCase(DataChannelCarrierKind.WebTransportUasc, 2)]
        public void NativeBaselineHelloRejectsEveryOtherCarrier(
            DataChannelCarrierKind kind,
            byte acceptedCarrier)
        {
            FlowCarrier carrier = FlowModel.ToWire(kind);
            for (int value = 0; value <= byte.MaxValue; value++)
            {
                if (value == acceptedCarrier)
                {
                    continue;
                }
                ByteString body = HelloBody((byte)value);
                AssertCodecError(() => UascFlowMessageCodec.Decode(
                    carrier, DataChannelUascMessageKind.Msg, (byte)'F', 0, body, new()),
                    value <= 2 ? FlowCodecError.CarrierMismatch : FlowCodecError.UnknownDiscriminator);
            }
        }

        /// <summary>
        /// Native baseline identities remain confidential even when their wire byte matches legacy shared UASC.
        /// </summary>
        [TestCase(DataChannelCarrierKind.QuicUasc, 1)]
        [TestCase(DataChannelCarrierKind.WebTransportUasc, 2)]
        public void NativeBaselineHelloCannotSelectIntegrity(
            DataChannelCarrierKind kind,
            byte wireCarrier)
        {
            byte[] body = HelloBody(wireCarrier).ToArray();
            body[10 + 93] = 1;
            FlowCarrier carrier = FlowModel.ToWire(kind);
            AssertCodecError(() => UascFlowMessageCodec.Decode(
                carrier, DataChannelUascMessageKind.Msg, (byte)'F', 0, new ByteString(body), new()),
                FlowCodecError.CarrierMismatch);
        }

        /// <summary>
        /// Native common records and WebTransport management MSG retain their legacy HELLO bytes.
        /// </summary>
        [TestCase(DataChannelCarrierKind.Quic, 2)]
        [TestCase(DataChannelCarrierKind.WebTransport, 3)]
        public void NativeCommonHelloPreservesLegacyDiscriminator(
            DataChannelCarrierKind kind,
            byte wireCarrier)
        {
            FlowCarrier carrier = FlowModel.ToWire(kind);
            var fixture = new FrozenWire("quic_hello");
            FlowRecord record = FlowControlCodec.Encode(Hello(carrier), fixture.Revision, fixture.MessageId, new());
            ByteString encoded = FlowRecordCodec.Encode(in record, new());
            var decoded = (FlowHello)FlowControlCodec.Decode(in record, new());
            Assert.That(encoded.Span[18], Is.EqualTo(wireCarrier));
            Assert.That(decoded.Carrier, Is.EqualTo(carrier));
            if (kind == DataChannelCarrierKind.WebTransport)
            {
                ByteString management = UascFlowMessageCodec.EncodeManagement(
                    0, in record, new(), out uint requestId);
                UascFlowMessage received = UascFlowMessageCodec.DecodeManagement(
                    DataChannelUascMessageKind.Msg, (byte)'F', requestId, encoded, new());
                Assert.That(management, Is.EqualTo(encoded));
                Assert.That(requestId, Is.EqualTo(fixture.MessageId));
                Assert.That(((FlowHello)received.Control!).Carrier, Is.EqualTo(FlowCarrier.WebTransport));
            }
        }

        /// <summary>
        /// Rejects non-final chunks and nonzero RequestIds for both message kinds.
        /// </summary>
        [TestCase(DataChannelUascMessageKind.Msg, (byte)'C', 0u, "NonFinalUasc")]
        [TestCase(DataChannelUascMessageKind.Msg, (byte)'A', 0u, "NonFinalUasc")]
        [TestCase(DataChannelUascMessageKind.Msg, (byte)'F', 1u, "RequestIdMismatch")]
        [TestCase(DataChannelUascMessageKind.Flo, (byte)'F', 0u, "WrongType")]
        [TestCase(DataChannelUascMessageKind.Flc, (byte)'F', 0u, "WrongType")]
        public void UascMessageConstraintsAreChecked(
            DataChannelUascMessageKind kind,
            byte finality,
            uint requestId,
            string error)
        {
            FlowCodecException exception = Assert.Throws<FlowCodecException>(() =>
                UascFlowMessageCodec.Decode(
                    FlowCarrier.SharedUasc, kind, finality, requestId, s_uadp, new()))!;
            Assert.That(exception.Error.ToString(), Is.EqualTo(error));
        }

        /// <summary>
        /// Rejects oversized FLO/FLC bodies, truncated FLC prefixes, and unknown controls.
        /// </summary>
        [Test]
        public void BodyBoundsAndControlSyntaxAreChecked()
        {
            FlowCodecLimits limits = new(maxDataBytes: 8, maxControlBytes: 4, maxRecordBytes: 21);
            AssertCodecError(
                () => UascFlowMessageCodec.Decode(
                    FlowCarrier.SharedUasc, DataChannelUascMessageKind.Msg, (byte)'F', 0x80000001u,
                    ByteString.FromHexString("00fe000000000000000000"), limits),
                FlowCodecError.TooLarge);
            AssertCodecError(
                () => UascFlowMessageCodec.Decode(
                    FlowCarrier.SharedUasc, DataChannelUascMessageKind.Msg, (byte)'F', 0,
                    ByteString.FromHexString("00ff01000000010000"), limits),
                FlowCodecError.Truncated);
            AssertCodecError(
                () => UascFlowMessageCodec.Decode(
                    FlowCarrier.SharedUasc, DataChannelUascMessageKind.Msg, (byte)'F', 0,
                    ByteString.FromHexString("00ff0100000001000000ff"), limits),
                FlowCodecError.UnknownOperation);
            AssertCodecError(
                () => UascFlowMessageCodec.Decode(
                    (FlowCarrier)255, DataChannelUascMessageKind.Msg, (byte)'F', 0, s_uadp, new()),
                FlowCodecError.CarrierMismatch);
        }

        /// <summary>
        /// Rejects alternate encodings of the reserved identifiers and unrelated MSG bodies.
        /// </summary>
        [TestCase("")]
        [TestCase("00")]
        [TestCase("00fd")]
        [TestCase("0100fe00")]
        [TestCase("020000fe000000")]
        [TestCase("0100ff00")]
        [TestCase("020000ff000000")]
        public void MsgRequiresTheCanonicalTwoByteTypeId(string hexadecimal)
        {
            ByteString body = ByteString.FromHexString(hexadecimal);
            AssertCodecError(() => UascFlowMessageCodec.Decode(
                FlowCarrier.SharedUasc, DataChannelUascMessageKind.Msg, (byte)'F', 0, body, new()),
                body.Length < 2 ? FlowCodecError.Truncated : FlowCodecError.WrongType);
        }

        /// <summary>
        /// STATUS and RESET preserve the causative control identifier without using the UASC RequestId.
        /// </summary>
        [TestCase((int)FlowOperation.Status, "00ff010000000b0000000e010000000278563412")]
        [TestCase((int)FlowOperation.Reset, "00ff010000000b0000000b010000000278563412")]
        public void MsgOutcomeCarriesRelatedOperationInWireOrder(int operation, string hexadecimal)
        {
            var outcome = new FlowOutcome((FlowOperation)operation, 1, FlowWireStatus.InvalidArgument, 0x12345678);
            FlowRecord record = FlowControlCodec.Encode(outcome, 1, 11, new());
            ByteString encoded = UascFlowMessageCodec.Encode(
                FlowCarrier.SharedUasc, 1, in record, new(), out uint requestId);
            UascFlowMessage decoded = UascFlowMessageCodec.Decode(
                FlowCarrier.SharedUasc, DataChannelUascMessageKind.Msg, (byte)'F', 0,
                ByteString.FromHexString(hexadecimal), new());
            Assert.Multiple(() =>
            {
                Assert.That(encoded, Is.EqualTo(ByteString.FromHexString(hexadecimal)));
                Assert.That(requestId, Is.Zero);
                Assert.That(decoded.Record.MessageId, Is.EqualTo(11u));
                Assert.That(decoded.Control, Is.TypeOf<FlowOutcome>());
                Assert.That(((FlowOutcome)decoded.Control!).RelatedOperation, Is.EqualTo(0x12345678u));
                Assert.That(((FlowOutcome)decoded.Control!).Status, Is.EqualTo(FlowWireStatus.InvalidArgument));
            });
        }

        /// <summary>
        /// Data capacity includes exactly the two TypeId bytes independently of application credit.
        /// </summary>
        [TestCase(1, false)]
        [TestCase(2, false)]
        [TestCase(3, false)]
        [TestCase(4, true)]
        [TestCase(5, true)]
        public void MsgDataLimitIncludesItsTypeId(int availableBytes, bool accepted)
        {
            var limits = new FlowCodecLimits(maxDataBytes: 2,
                maxRecordBytes: FlowRecordCodec.HeaderSize + availableBytes);
            var record = new FlowRecord(FlowRecordKind.Data, 1, 1, ByteString.FromHexString("0100"));
            ByteString literal = ByteString.FromHexString("00fe0100");
            if (!accepted)
            {
                AssertCodecError(() => UascFlowMessageCodec.Encode(
                    FlowCarrier.SharedUasc, 1, in record, limits, out _), FlowCodecError.TooLarge);
                AssertCodecError(() => UascFlowMessageCodec.Decode(
                    FlowCarrier.SharedUasc, DataChannelUascMessageKind.Msg, (byte)'F', 0x80000001u, literal, limits),
                    FlowCodecError.TooLarge);
                return;
            }
            Assert.That(UascFlowMessageCodec.Encode(
                FlowCarrier.SharedUasc, 1, in record, limits, out _), Is.EqualTo(literal));
            Assert.That(UascFlowMessageCodec.Decode(
                FlowCarrier.SharedUasc, DataChannelUascMessageKind.Msg, (byte)'F', 0x80000001u, literal, limits)
                .ApplicationBody, Is.EqualTo(record.Body));
        }

        /// <summary>
        /// Keeps native transport identity while using unchanged FLO bytes and baseline encoded-byte CREDIT.
        /// </summary>
        [TestCase(DataChannelCarrierKind.QuicUasc)]
        [TestCase(DataChannelCarrierKind.WebTransportUasc)]
        public void NativeBaselinesPreserveUadpBytesAndRequireBaselineCredit(DataChannelCarrierKind kind)
        {
            FlowCarrier carrier = FlowModel.ToWire(kind);
            var data = new FlowRecord(FlowRecordKind.Data, 1, 1, s_uadp);
            ByteString encoded = UascFlowMessageCodec.Encode(carrier, 1, in data, new(), out uint requestId);
            Assert.That(encoded[2..].ToArray(), Is.EqualTo(s_uadp.ToArray()));
            Assert.That(requestId, Is.EqualTo(0x80000001u));

            FlowRecord credit = FlowControlCodec.Encode(new FlowCredit(1, 256, 512), 1, 2, new());
            ByteString body = UascFlowMessageCodec.Encode(carrier, 1, in credit, new(), out requestId);
            UascFlowMessage decoded = UascFlowMessageCodec.Decode(
                carrier, DataChannelUascMessageKind.Msg, (byte)'F', requestId, body, new());
            var value = (FlowCredit)decoded.Control!;
            Assert.That(value.ChannelCredit, Is.EqualTo(256));
            Assert.That(value.ConnectionCredit, Is.EqualTo(512));
            AssertCodecError(
                () => FlowControlCodec.ValidateCarrier(value, carrier),
                FlowCodecError.CarrierMismatch);
        }

        /// <summary>
        /// Carries direct binding proofs inside native baseline FLC, never on an ordinary UASC profile.
        /// </summary>
        [TestCase(DataChannelCarrierKind.QuicUasc)]
        [TestCase(DataChannelCarrierKind.WebTransportUasc)]
        public void DirectBindingProofUsesProtectedNativeBaselineFlc(DataChannelCarrierKind kind)
        {
            FlowCarrier carrier = FlowModel.ToWire(kind);
            ByteString digest = ByteString.FromHexString(new string('1', 64));
            ByteString proof = ByteString.FromHexString(new string('2', 64));
            FlowRecord record = FlowControlCodec.Encode(new FlowBinding(0, digest, proof), 0, 3, new());
            ByteString body = UascFlowMessageCodec.Encode(carrier, 0, in record, new(), out uint requestId);
            UascFlowMessage decoded = UascFlowMessageCodec.Decode(
                carrier, DataChannelUascMessageKind.Msg, (byte)'F', requestId, body, new());
            var binding = (FlowBinding)decoded.Control!;
            Assert.That(binding.Role, Is.Zero);
            Assert.That(binding.TranscriptDigest, Is.EqualTo(digest));
            Assert.That(binding.Proof, Is.EqualTo(proof));
            AssertCodecError(
                () => UascFlowMessageCodec.Decode(
                    FlowCarrier.SharedUasc, DataChannelUascMessageKind.Msg, (byte)'F', 0, body, new()),
                FlowCodecError.CarrierMismatch);
        }

        /// <summary>
        /// Carries both direct association transaction messages inside protected native-baseline FLC.
        /// </summary>
        [TestCase(DataChannelCarrierKind.QuicUasc, (int)FlowOperation.Associate)]
        [TestCase(DataChannelCarrierKind.QuicUasc, (int)FlowOperation.AssociateAck)]
        [TestCase(DataChannelCarrierKind.WebTransportUasc, (int)FlowOperation.Associate)]
        [TestCase(DataChannelCarrierKind.WebTransportUasc, (int)FlowOperation.AssociateAck)]
        public void DirectAssociationControlsUseProtectedNativeBaselineFlc(
            DataChannelCarrierKind kind,
            int operationValue)
        {
            FlowCarrier carrier = FlowModel.ToWire(kind);
            var token = new Guid("11112233-4455-6677-8899-aabbccddeeff");
            FlowOperation operation = (FlowOperation)operationValue;
            var association = new FlowAssociation(operation, 7, 1, token);
            FlowRecord record = FlowControlCodec.Encode(association, 1, 3, new());
            ByteString body = UascFlowMessageCodec.Encode(carrier, 7, in record, new(), out uint requestId);
            UascFlowMessage decoded = UascFlowMessageCodec.Decode(
                carrier, DataChannelUascMessageKind.Msg, (byte)'F', requestId, body, new());
            var received = (FlowAssociation)decoded.Control!;

            Assert.Multiple(() =>
            {
                Assert.That(requestId, Is.Zero);
                Assert.That(decoded.Record.Revision, Is.EqualTo(1));
                Assert.That(decoded.Record.MessageId, Is.EqualTo(3));
                Assert.That(received.Operation, Is.EqualTo(operation));
                Assert.That(received.Flow, Is.EqualTo(7));
                Assert.That(received.Direction, Is.EqualTo(1));
                Assert.That(received.Token, Is.EqualTo(token));
            });
        }

        /// <summary>
        /// Requires the control-only AdmissionId to agree with the internal control value.
        /// </summary>
        [Test]
        public void ControlAdmissionIdIsValidatedDuringEncoding()
        {
            FlowRecord end = FlowControlCodec.Encode(
                new FlowSignal(FlowOperation.End, 1), 1, 11, new());
            AssertCodecError(
                () => UascFlowMessageCodec.Encode(
                    FlowCarrier.SharedUasc, 2, in end, new(), out _),
                FlowCodecError.InvalidFlow);
        }

        /// <summary>
        /// Native management MSG is exactly one common CONTROL record, not an FLC or Service body.
        /// </summary>
        [Test]
        public void ManagementUsesLiteralCommonRecordAndFullWidthRequestId()
        {
            const string literal = "0e0000000104030201efcdab890a44332211";
            FlowRecord record = FlowControlCodec.Encode(
                new FlowSignal(FlowOperation.End, 0x11223344), 0x01020304, 0x89ABCDEF, new());

            ByteString encoded = UascFlowMessageCodec.EncodeManagement(
                0x11223344, in record, new(), out uint requestId);
            UascFlowMessage decoded = UascFlowMessageCodec.DecodeManagement(
                DataChannelUascMessageKind.Msg, (byte)'F', 0x89ABCDEF,
                ByteString.FromHexString(literal), new());

            Assert.Multiple(() =>
            {
                Assert.That(encoded.ToHexString().ToLowerInvariant(), Is.EqualTo(literal));
                Assert.That(requestId, Is.EqualTo(0x89ABCDEFu));
                Assert.That(decoded.ChannelId, Is.EqualTo(0x11223344u));
                Assert.That(decoded.Record.Kind, Is.EqualTo(FlowRecordKind.Control));
                Assert.That(decoded.Record.Revision, Is.EqualTo(0x01020304u));
                Assert.That(decoded.Record.MessageId, Is.EqualTo(0x89ABCDEFu));
                Assert.That(decoded.Record.Body.ToHexString().ToLowerInvariant(), Is.EqualTo("0a44332211"));
                Assert.That(decoded.Control!.Operation, Is.EqualTo(FlowOperation.End));
            });
        }

        /// <summary>
        /// Neither half of the 32-bit correlation value may be dropped or replaced by the flow id.
        /// </summary>
        [TestCase(1u)]
        [TestCase(0x00010001u)]
        [TestCase(0x80000001u)]
        [TestCase(uint.MaxValue)]
        public void ManagementCorrelatesEveryRequestIdBit(uint messageId)
        {
            FlowRecord record = FlowControlCodec.Encode(new FlowSignal(FlowOperation.End, 7), 1, messageId, new());
            ByteString body = UascFlowMessageCodec.EncodeManagement(7, in record, new(), out uint requestId);
            Assert.That(requestId, Is.EqualTo(messageId));
            Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(body.Span[9..]), Is.EqualTo(messageId));
            Assert.That(UascFlowMessageCodec.DecodeManagement(
                DataChannelUascMessageKind.Msg, (byte)'F', messageId, body, new()).ChannelId, Is.EqualTo(7u));
            foreach (uint wrongId in new[] { 0u, messageId ^ 1u, messageId ^ 0x00010000u, messageId ^ 0x80000000u })
            {
                AssertCodecError(() => UascFlowMessageCodec.DecodeManagement(
                    DataChannelUascMessageKind.Msg, (byte)'F', wrongId, body, new()),
                    FlowCodecError.RequestIdMismatch);
            }
        }

        /// <summary>
        /// Only final MSG is accepted; ordinary baseline FLO/FLC cannot select native management.
        /// </summary>
        [TestCase(DataChannelUascMessageKind.Flo, (byte)'F', "WrongType")]
        [TestCase(DataChannelUascMessageKind.Flc, (byte)'F', "WrongType")]
        [TestCase((DataChannelUascMessageKind)255, (byte)'F', "WrongType")]
        [TestCase(DataChannelUascMessageKind.Msg, (byte)'C', "NonFinalUasc")]
        [TestCase(DataChannelUascMessageKind.Msg, (byte)'A', "NonFinalUasc")]
        public void ManagementRejectsWrongMessageKindAndNonfinalChunks(
            DataChannelUascMessageKind kind, byte finality, string error)
        {
            FlowCodecException exception = Assert.Throws<FlowCodecException>(() =>
                UascFlowMessageCodec.DecodeManagement(kind, finality, 11, ManagementEndBody(), new()))!;
            Assert.That(exception.Error.ToString(), Is.EqualTo(error));
        }

        /// <summary>
        /// DATA cannot enter management in either direction and CONTROL must address its actual flow.
        /// </summary>
        [Test]
        public void ManagementRejectsDataAndMismatchedAdmission()
        {
            var data = new FlowRecord(FlowRecordKind.Data, 1, 11, s_uadp);
            AssertCodecError(() => UascFlowMessageCodec.EncodeManagement(1, in data, new(), out _),
                FlowCodecError.WrongType);
            AssertCodecError(() => UascFlowMessageCodec.DecodeManagement(
                DataChannelUascMessageKind.Msg, (byte)'F', 11, FlowRecordCodec.Encode(in data, new()), new()),
                FlowCodecError.WrongType);
            FlowRecord end = FlowControlCodec.Encode(new FlowSignal(FlowOperation.End, 1), 1, 11, new());
            AssertCodecError(() => UascFlowMessageCodec.EncodeManagement(2, in end, new(), out _),
                FlowCodecError.InvalidFlow);
        }

        /// <summary>
        /// Every incomplete prefix and both a trailing byte and a second complete record are refused.
        /// </summary>
        [Test]
        public void ManagementRequiresExactlyOneCompleteRecord()
        {
            ByteString body = ManagementEndBody();
            for (int length = 0; length < body.Length; length++)
            {
                ByteString prefix = new(body.Memory[..length]);
                AssertCodecError(() => UascFlowMessageCodec.DecodeManagement(
                    DataChannelUascMessageKind.Msg, (byte)'F', 11, prefix, new()), FlowCodecError.Truncated);
            }
            foreach (ByteString trailing in new[]
            {
                new ByteString((byte[])[.. body.ToArray(), 0]),
                new ByteString((byte[])[.. body.ToArray(), .. body.ToArray()])
            })
            {
                AssertCodecError(() => UascFlowMessageCodec.DecodeManagement(
                    DataChannelUascMessageKind.Msg, (byte)'F', 11, trailing, new()), FlowCodecError.TrailingBytes);
            }
        }

        /// <summary>
        /// Invalid common headers and control bodies keep their precise parser refusal.
        /// </summary>
        [TestCase("0800000001010000000b0000000a01000000", "InvalidLength")]
        [TestCase("ffffffff01010000000b0000000a01000000", "TooLarge")]
        [TestCase("0e000000ff010000000b0000000a01000000", "UnknownKind")]
        [TestCase("0e0000000101000000000000000a01000000", "InvalidMessageId")]
        [TestCase("0e00000001000000000b0000000a01000000", "InvalidRevision")]
        [TestCase("0e00000001010000000b000000ff01000000", "UnknownOperation")]
        [TestCase("0e00000001010000000b0000000a00000000", "InvalidFlow")]
        [TestCase("0900000001010000000b000000", "Truncated")]
        [TestCase("0d00000001010000000b0000000a010000", "Truncated")]
        [TestCase("0f00000001010000000b0000000a0100000000", "TrailingBytes")]
        public void ManagementRejectsMalformedCommonRecord(string hex, string error)
        {
            FlowCodecException exception = Assert.Throws<FlowCodecException>(() =>
                UascFlowMessageCodec.DecodeManagement(
                    DataChannelUascMessageKind.Msg, (byte)'F', 11, ByteString.FromHexString(hex), new()))!;
            Assert.That(exception.Error.ToString(), Is.EqualTo(error));
        }

        /// <summary>
        /// Control-body and total-record limits apply independently and include their exact boundary.
        /// </summary>
        [TestCase(5, 18, true)]
        [TestCase(6, 19, true)]
        [TestCase(4, 18, false)]
        [TestCase(5, 17, false)]
        public void ManagementBoundsAreInclusiveOnEncodeAndDecode(int controlBytes, int recordBytes, bool allowed)
        {
            var limits = new FlowCodecLimits(maxDataBytes: 0, maxControlBytes: controlBytes, maxRecordBytes: recordBytes);
            FlowRecord end = FlowControlCodec.Encode(new FlowSignal(FlowOperation.End, 1), 1, 11, new());
            if (!allowed)
            {
                AssertCodecError(() => UascFlowMessageCodec.EncodeManagement(1, in end, limits, out _),
                    FlowCodecError.TooLarge);
                AssertCodecError(() => UascFlowMessageCodec.DecodeManagement(
                    DataChannelUascMessageKind.Msg, (byte)'F', 11, ManagementEndBody(), limits), FlowCodecError.TooLarge);
                return;
            }
            ByteString encoded = UascFlowMessageCodec.EncodeManagement(1, in end, limits, out uint requestId);
            UascFlowMessage decoded = UascFlowMessageCodec.DecodeManagement(
                DataChannelUascMessageKind.Msg, (byte)'F', 11, ManagementEndBody(), limits);
            Assert.Multiple(() =>
            {
                Assert.That(encoded, Is.EqualTo(ManagementEndBody()));
                Assert.That(requestId, Is.EqualTo(11u));
                Assert.That(decoded.Control!.Operation, Is.EqualTo(FlowOperation.End));
                Assert.That(decoded.ChannelId, Is.EqualTo(1u));
            });
        }

        /// <summary>
        /// Native management does not inherit the baseline's CREDIT or optional exporter binding protocol.
        /// </summary>
        [Test]
        public void ManagementRejectsBaselineOnlyControlsAndForeignHello()
        {
            FlowControl[] controls =
            [
                new FlowCredit(1, 256, 512),
                new FlowBinding(0, new ByteString(new byte[32]), new ByteString(new byte[32])),
                new FlowAssociation(FlowOperation.Associate, 1, 0, new Guid("11112233-4455-6677-8899-aabbccddeeff")),
                new FlowAssociation(FlowOperation.AssociateAck, 1, 1, new Guid("11112233-4455-6677-8899-aabbccddeeff")),
                new FlowHello(1, FlowCarrier.Quic, "urn:local",
                    new Guid("11112233-4455-6677-8899-aabbccddeeff"), 1,
                    new ByteString(new byte[32]), FlowWireProtection.Confidential, 1024, 4096, 4)
            ];
            foreach (FlowControl control in controls)
            {
                FlowRecord record = FlowControlCodec.Encode(control, control.Flow == 0 ? 0u : 1u, 11, new());
                AssertCodecError(() => UascFlowMessageCodec.EncodeManagement(control.Flow, in record, new(), out _),
                    FlowCodecError.CarrierMismatch);
                AssertCodecError(() => UascFlowMessageCodec.DecodeManagement(
                    DataChannelUascMessageKind.Msg, (byte)'F', 11, FlowRecordCodec.Encode(in record, new()), new()),
                    FlowCodecError.CarrierMismatch);
            }
        }

        /// <summary>
        /// Invalid encode inputs cannot bypass the common header or CONTROL validation.
        /// </summary>
        [Test]
        public void ManagementValidatesEncodeHeadersAndLimits()
        {
            FlowRecord valid = FlowControlCodec.Encode(new FlowSignal(FlowOperation.End, 1), 1, 11, new());
            var zeroId = new FlowRecord(FlowRecordKind.Control, 1, 0, valid.Body);
            var zeroRevision = new FlowRecord(FlowRecordKind.Control, 0, 11, valid.Body);
            var unknown = new FlowRecord((FlowRecordKind)255, 1, 11, valid.Body);
            AssertCodecError(() => UascFlowMessageCodec.EncodeManagement(1, in zeroId, new(), out _),
                FlowCodecError.InvalidMessageId);
            AssertCodecError(() => UascFlowMessageCodec.EncodeManagement(1, in zeroRevision, new(), out _),
                FlowCodecError.InvalidRevision);
            AssertCodecError(() => UascFlowMessageCodec.EncodeManagement(1, in unknown, new(), out _),
                FlowCodecError.WrongType);
            Assert.Throws<ArgumentNullException>(() =>
                UascFlowMessageCodec.EncodeManagement(1, in valid, null!, out _));
            Assert.Throws<ArgumentNullException>(() =>
                UascFlowMessageCodec.DecodeManagement(
                    DataChannelUascMessageKind.Msg, (byte)'F', 11, ManagementEndBody(), null!));
        }

        /// <summary>
        /// Selects the logical carrier on an independently authored HELLO specimen.
        /// </summary>
        private static FlowHello Hello(FlowCarrier carrier)
        {
            var expected = (FlowHello)new FrozenWire("quic_hello").ExpectedControl!;
            return new FlowHello(
                expected.Version, carrier, expected.Application, expected.Context, expected.Generation,
                expected.Challenge, expected.Protection, expected.MaxMessageSize, expected.MaxReceiveBytes,
                expected.MaxFlows);
        }

        /// <summary>
        /// Combines literal fields with the canonical MSG prefix and selected carrier byte.
        /// </summary>
        private static ByteString HelloBody(byte carrier)
        {
            var fixture = new FrozenWire("quic_hello");
            byte[] body = new byte[10 + fixture.ExpectedBody.Length];
            body[1] = 255;
            BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(2), fixture.Revision);
            BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(6), fixture.MessageId);
            fixture.ExpectedBody.Span.CopyTo(body.AsSpan(10));
            body[15] = carrier;
            return new ByteString(body);
        }

        private static ByteString ManagementEndBody()
        {
            return ByteString.FromHexString("0e00000001010000000b0000000a01000000");
        }

        private static void AssertCodecError(Action decode, FlowCodecError expected)
        {
            FlowCodecException exception = Assert.Throws<FlowCodecException>(decode)!;
            Assert.That(exception.Error, Is.EqualTo(expected));
        }
    }
}
