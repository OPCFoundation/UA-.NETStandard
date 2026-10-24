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
using NUnit.Framework;
using Opc.Ua.DataChannels;
using Opc.Ua.DataChannels.Wire;

namespace Opc.Ua.Core.DataChannels.Tests.Wire
{
    /// <summary>
    /// Checks the separate UASC FLO and FLC application bodies against literal layouts.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class UascFlowMessageCodecTests
    {
        private static readonly ByteString s_uadp = ByteString.FromHexString("01010100072a000000");

        /// <summary>
        /// Encodes one unchanged UADP message directly as the FLO body.
        /// </summary>
        [Test]
        public void FloBodyIsOneUnchangedUadpMessage()
        {
            var record = new FlowRecord(FlowRecordKind.Data, 1, 1, s_uadp);
            ByteString encoded = UascFlowMessageCodec.Encode(
                FlowCarrier.SharedUasc, 1, in record, new(), out uint requestId);
            UascFlowMessage decoded = UascFlowMessageCodec.Decode(
                FlowCarrier.SharedUasc, DataChannelUascMessageKind.Flo, (byte)'F', requestId, encoded, new());

            Assert.That(requestId, Is.Zero);
            Assert.That(encoded.ToArray(), Is.EqualTo(s_uadp.ToArray()));
            Assert.That(decoded.ChannelId, Is.Zero);
            Assert.That(decoded.ApplicationBody.ToArray(), Is.EqualTo(s_uadp.ToArray()));
        }

        /// <summary>
        /// Encodes Revision and OperationId before typed fields in an FLC body.
        /// </summary>
        [Test]
        public void FlcBodyContainsRevisionOperationIdAndTypedControl()
        {
            FlowRecord record = FlowControlCodec.Encode(
                new FlowSignal(FlowOperation.End, 1), 1, 11, new());
            ByteString encoded = UascFlowMessageCodec.Encode(
                FlowCarrier.SharedUasc, 1, in record, new(), out uint requestId);
            UascFlowMessage decoded = UascFlowMessageCodec.Decode(
                FlowCarrier.SharedUasc, DataChannelUascMessageKind.Flc, (byte)'F', requestId, encoded, new());

            Assert.That(requestId, Is.Zero);
            Assert.That(encoded.ToHexString().ToLowerInvariant(),
                Is.EqualTo("010000000b0000000a01000000"));
            Assert.That(decoded.ChannelId, Is.EqualTo(1));
            Assert.That(decoded.Record.Revision, Is.EqualTo(1));
            Assert.That(decoded.Record.MessageId, Is.EqualTo(11));
            Assert.That(decoded.Control!.Operation, Is.EqualTo(FlowOperation.End));
        }

        /// <summary>
        /// Carries protected context confirmation in an FLC body with connection flow zero.
        /// </summary>
        [Test]
        public void FlcCarriesHelloWithZeroRequestId()
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
                FlowCarrier.SharedUasc, DataChannelUascMessageKind.Flc, (byte)'F', requestId, encoded, new());

            Assert.That(decoded.Control, Is.TypeOf<FlowHello>());
            Assert.That(decoded.ChannelId, Is.Zero);
            Assert.That(System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(encoded.Span), Is.Zero);
            Assert.That(System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(encoded.Span[sizeof(uint)..]),
                Is.EqualTo(1));
        }

        /// <summary>
        /// Rejects non-final chunks and nonzero RequestIds for both message kinds.
        /// </summary>
        [TestCase(DataChannelUascMessageKind.Flo, (byte)'C', 0u, "NonFinalUasc")]
        [TestCase(DataChannelUascMessageKind.Flo, (byte)'A', 0u, "NonFinalUasc")]
        [TestCase(DataChannelUascMessageKind.Flo, (byte)'F', 1u, "RequestIdMismatch")]
        [TestCase(DataChannelUascMessageKind.Flc, (byte)'F', 1u, "RequestIdMismatch")]
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
                    FlowCarrier.SharedUasc, DataChannelUascMessageKind.Flo, (byte)'F', 0,
                    new ByteString(new byte[9]), limits),
                FlowCodecError.TooLarge);
            AssertCodecError(
                () => UascFlowMessageCodec.Decode(
                    FlowCarrier.SharedUasc, DataChannelUascMessageKind.Flc, (byte)'F', 0,
                    ByteString.FromHexString("01000000010000"), limits),
                FlowCodecError.Truncated);
            AssertCodecError(
                () => UascFlowMessageCodec.Decode(
                    FlowCarrier.SharedUasc, DataChannelUascMessageKind.Flc, (byte)'F', 0,
                    ByteString.FromHexString("0100000001000000ff"), limits),
                FlowCodecError.UnknownOperation);
            AssertCodecError(
                () => UascFlowMessageCodec.Decode(
                    (FlowCarrier)255, DataChannelUascMessageKind.Flo, (byte)'F', 0, s_uadp, new()),
                FlowCodecError.CarrierMismatch);
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
            Assert.That(encoded.ToArray(), Is.EqualTo(s_uadp.ToArray()));
            Assert.That(requestId, Is.Zero);

            FlowRecord credit = FlowControlCodec.Encode(new FlowCredit(1, 256, 512), 1, 2, new());
            ByteString body = UascFlowMessageCodec.Encode(carrier, 1, in credit, new(), out requestId);
            UascFlowMessage decoded = UascFlowMessageCodec.Decode(
                carrier, DataChannelUascMessageKind.Flc, (byte)'F', requestId, body, new());
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
                carrier, DataChannelUascMessageKind.Flc, (byte)'F', requestId, body, new());
            var binding = (FlowBinding)decoded.Control!;
            Assert.That(binding.Role, Is.Zero);
            Assert.That(binding.TranscriptDigest, Is.EqualTo(digest));
            Assert.That(binding.Proof, Is.EqualTo(proof));
            AssertCodecError(
                () => UascFlowMessageCodec.Decode(
                    FlowCarrier.SharedUasc, DataChannelUascMessageKind.Flc, (byte)'F', 0, body, new()),
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
                carrier, DataChannelUascMessageKind.Flc, (byte)'F', requestId, body, new());
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

        private static void AssertCodecError(Action decode, FlowCodecError expected)
        {
            FlowCodecException exception = Assert.Throws<FlowCodecException>(decode)!;
            Assert.That(exception.Error, Is.EqualTo(expected));
        }
    }
}
