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
using System.Text;
using NUnit.Framework;
using Opc.Ua.Core.DataChannels.Tests.Runtime;
using Opc.Ua.DataChannels;
using Opc.Ua.DataChannels.Wire;

namespace Opc.Ua.Core.DataChannels.Tests.Wire
{
    /// <summary>
    /// Locks down the breaking Part 28 binary CONTROL and unified UASC payload revision.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class BinaryControlRevisionTests
    {
        /// <summary>
        /// Model snapshots and binary limits use the same unlimited-rate identity without mutating the caller.
        /// </summary>
        [Test]
        public void ModelAndWireSnapshotsCanonicalizeNegativeZero()
        {
            DataChannelParametersDataType original = FlowLoopback.Parameters();
            original.MaxRate = BitConverter.Int64BitsToDouble(long.MinValue);

            DataChannelParametersDataType snapshot = FlowModel.Copy(original);
            DataChannelParametersDataType roundTrip = FlowModel.FromWire(FlowModel.ToWire(original));

            Assert.Multiple(() =>
            {
                Assert.That(BitConverter.DoubleToInt64Bits(original.MaxRate), Is.EqualTo(long.MinValue));
                Assert.That(BitConverter.DoubleToInt64Bits(snapshot.MaxRate), Is.Zero);
                Assert.That(BitConverter.DoubleToInt64Bits(roundTrip.MaxRate), Is.Zero);
                Assert.That(FlowModel.SameParameters(original, snapshot), Is.True);
                Assert.That(FlowModel.SameParameters(snapshot, roundTrip), Is.True);
            });
        }

        /// <summary>
        /// READY contains only its opcode and little-endian flow identifier.
        /// </summary>
        [Test]
        public void ReadyUsesTheBinaryOpcodeAndFieldOrder()
        {
            FlowRecord encoded = FlowControlCodec.Encode(
                new FlowSignal(FlowOperation.Ready, 1), 1, 1, new FlowCodecLimits());

            Assert.That(encoded.Body.ToArray(), Is.EqualTo(new byte[] { 3, 1, 0, 0, 0 }));
        }

        /// <summary>
        /// An independently supplied binary READY body decodes without a JSON or Service envelope.
        /// </summary>
        [Test]
        public void ReadyDecodesTheLiteralBinaryBody()
        {
            var record = new FlowRecord(
                FlowRecordKind.Control, 1, 1, ByteString.FromHexString("0301000000"));

            FlowControl decoded = FlowControlCodec.Decode(in record, new FlowCodecLimits());

            Assert.Multiple(() =>
            {
                Assert.That(decoded, Is.TypeOf<FlowSignal>());
                Assert.That(decoded.Operation, Is.EqualTo(FlowOperation.Ready));
                Assert.That(decoded.Flow, Is.EqualTo(1));
            });
        }

        /// <summary>
        /// The superseded JSON operation has no binary opcode and is never selected as a fallback.
        /// </summary>
        [Test]
        public void SupersededJsonControlIsUnsupported()
        {
            var record = new FlowRecord(FlowRecordKind.Control, 1, 1,
                new ByteString(Encoding.UTF8.GetBytes(/*lang=json,strict*/ "{\"flow\":1,\"op\":\"ready\"}")));

            FlowCodecException? error = Assert.Throws<FlowCodecException>(() =>
                FlowControlCodec.Decode(in record, new FlowCodecLimits()));

            Assert.That(error!.Status, Is.EqualTo(FlowWireStatus.Unsupported));
        }

        /// <summary>
        /// Both UASC profiles prefix the unchanged UADP body with the canonical MSG TypeId and         use the DATA RequestId (high bit plus ChannelId).
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void UascProfilesUseAnIdenticalMsgPayload(bool shared)
        {
            FlowCarrier carrier = shared ? FlowCarrier.SharedUasc : FlowCarrier.DedicatedUasc;
            var record = new FlowRecord(
                FlowRecordKind.Data, 1, 1, ByteString.FromHexString("0100"));

            ByteString encoded = UascFlowMessageCodec.Encode(
                carrier, 1, in record, new FlowCodecLimits(), out uint requestId);

            Assert.Multiple(() =>
            {
                Assert.That(requestId, Is.EqualTo(0x80000001u));
                Assert.That(encoded.ToHexString().                ToLowerInvariant(),
                    Is.EqualTo("00fe0100"));
            });
        }
    }
}
