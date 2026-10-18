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
using NUnit.Framework;
using Opc.Ua.DataChannels.Wire;

namespace Opc.Ua.Core.DataChannels.Tests.Wire
{
    /// <summary>
    /// Checks independent public golden bytes without relying on private vectors or self-roundtrip oracles.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class FlowGoldenVectorTests
    {
        /// <summary>
        /// Gets every operation plus the other HELLO carrier layouts and session-grant syntax.
        /// </summary>
        public static IEnumerable<string> Controls => FrozenWire.ControlNames().Concat(
            ["dedicated-uasc_hello", "shared-uasc_hello", "webtransport_hello", "session_open"]);

        /// <summary>
        /// Gets independently authored opaque, UADP and JSON bytes for each applicable envelope.
        /// </summary>
        public static IEnumerable<string> Data => FrozenWire.DataNames();

        /// <summary>
        /// Compares encoding from typed literal inputs directly with fixed header and binary field bytes.
        /// </summary>
        [TestCaseSource(nameof(Controls))]
        public void EveryOperationHasAnIndependentGoldenEncoding(string name)
        {
            var specimen = new FrozenWire(name);
            Assert.That(specimen.ExpectedControl, Is.Not.Null);
            FlowRecord record = FlowControlCodec.Encode(
                specimen.ExpectedControl!, specimen.Revision, specimen.MessageId, new());
            Assert.That(record.Body.ToArray(), Is.EqualTo(specimen.ExpectedBody.ToArray()));
            Assert.That(FlowRecordCodec.Encode(in record, new()).ToArray(),
                Is.EqualTo(specimen.CommonRecord.ToArray()));
            AssertEnvelopeEncoding(specimen, in record);
        }

        /// <summary>
        /// Checks every decoded typed member against independently constructed values, not re-encoding alone.
        /// </summary>
        [TestCaseSource(nameof(Controls))]
        public void GoldenControlsDecodeEveryTypedMember(string name)
        {
            var specimen = new FrozenWire(name);
            FlowWireEnvelope envelope = specimen.Decode();
            Assert.Multiple(() =>
            {
                Assert.That(envelope.ChannelId, Is.EqualTo(specimen.ChannelId));
                Assert.That(envelope.Record.Kind, Is.EqualTo(FlowRecordKind.Control));
                Assert.That(envelope.Record.Revision, Is.EqualTo(specimen.Revision));
                Assert.That(envelope.Record.MessageId, Is.EqualTo(specimen.MessageId));
                Assert.That(envelope.Record.Body.ToArray(), Is.EqualTo(specimen.ExpectedBody.ToArray()));
            });
            AssertControl(envelope.Control, specimen.ExpectedControl!);
        }

        /// <summary>
        /// Verifies independent DATA decoding and encoding without parsing or normalizing inner payload bytes.
        /// </summary>
        [TestCaseSource(nameof(Data))]
        public void GoldenDataEnvelopesPreserveExactPayloadBytes(string name)
        {
            var specimen = new FrozenWire(name);
            FlowWireEnvelope envelope = specimen.Decode();
            Assert.Multiple(() =>
            {
                Assert.That(envelope.Record.Kind, Is.EqualTo(FlowRecordKind.Data));
                Assert.That(envelope.Record.Revision, Is.EqualTo(1));
                Assert.That(envelope.Record.MessageId, Is.EqualTo(1));
                Assert.That(envelope.Record.Body.IsNull, Is.False);
                Assert.That(envelope.Record.Body.ToArray(), Is.EqualTo(specimen.ExpectedBody.ToArray()));
                Assert.That(envelope.Control, Is.Null);
                Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(specimen.CommonRecord.Span),
                    Is.EqualTo(specimen.ExpectedBody.Length + 9));
            });
            if (specimen.Datagram || specimen.Carrier is FlowCarrier.SharedUasc or FlowCarrier.DedicatedUasc)
            {
                Assert.That(envelope.ChannelId, Is.EqualTo(1));
            }
            var independent = new FlowRecord(FlowRecordKind.Data, 1, 1, specimen.ExpectedBody);
            Assert.That(FlowRecordCodec.Encode(in independent, new()).ToArray(),
                Is.EqualTo(specimen.CommonRecord.ToArray()));
            AssertEnvelopeEncoding(specimen, in independent);
        }

        /// <summary>
        /// Prevents an omitted operation from disappearing silently from generated member tests.
        /// </summary>
        [Test]
        public void GoldenCatalogContainsEveryControlOperation()
        {
            FlowOperation[] expected =
            [
                FlowOperation.Hello, FlowOperation.Open, FlowOperation.Accept, FlowOperation.Ready,
                FlowOperation.Prepare, FlowOperation.AcceptUpdate, FlowOperation.Commit, FlowOperation.Ack,
                FlowOperation.AbortUpdate, FlowOperation.Credit, FlowOperation.End, FlowOperation.Reset,
                FlowOperation.Ping, FlowOperation.Pong, FlowOperation.Status
            ];
            FlowOperation[] actual =
                [.. FrozenWire.ControlNames().Select(name => new FrozenWire(name).ExpectedControl!.Operation)];
            Assert.That(actual, Is.EquivalentTo(expected));
            Assert.That(actual, Has.Length.EqualTo(15));
        }

        /// <summary>
        /// Checks malformed literal records independently of either production serialization direction.
        /// </summary>
        [TestCase("08000000000100000001000000", "InvalidLength")]
        [TestCase("090000007f0100000001000000", "UnknownKind")]
        [TestCase("09000000000000000001000000", "InvalidRevision")]
        [TestCase("09000000000100000000000000", "InvalidMessageId")]
        [TestCase("ffffffff000100000001000000", "TooLarge")]
        [TestCase("0c0000000001000000010000006b39", "Truncated")]
        [TestCase("090000000001000000010000009e", "TrailingBytes")]
        public void GoldenMalformedRecordsHaveExactError(string hex, string error)
        {
            var bytes = ByteString.FromHexString(hex);
            FlowCodecException exception = Assert.Throws<FlowCodecException>(() =>
                FlowRecordCodec.DecodeExact(bytes, new()))!;
            Assert.That(exception.Error.ToString(), Is.EqualTo(error));
        }

        /// <summary>
        /// Requires adapters to remove external bytes; arbitrary suffixes are not interpreted as signatures.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void UascPayloadRejectsUnremovedExternalBytes(bool shared)
        {
            var specimen = new FrozenWire(shared ? "shared-uasc_opaque" : "dedicated-uasc_opaque");
            byte[] bytes = [.. specimen.Bytes.ToArray(), 0xA5, 0, 0xFF];
            var unremoved = new ByteString(bytes);
            FrozenWire.Fails(() => FlowCarrierCodec.DecodeUascPayload(
                specimen.Carrier, (byte)'F', 0, unremoved, new()),
                FlowCodecError.TrailingBytes);
            Assert.That(specimen.Decode().Record.Body.ToArray(), Is.EqualTo("k9!"u8.ToArray()));
        }

        /// <summary>
        /// Keeps failure categories stable without attributing lifecycle or authentication decisions to the codec.
        /// </summary>
        [TestCase("Truncated", "InvalidArgument")]
        [TestCase("InvalidLength", "InvalidArgument")]
        [TestCase("UnknownKind", "Unsupported")]
        [TestCase("TooLarge", "TooLarge")]
        [TestCase("InvalidMessageId", "InvalidArgument")]
        [TestCase("InvalidRevision", "InvalidArgument")]
        [TestCase("TrailingBytes", "InvalidArgument")]
        [TestCase("WrongType", "InvalidArgument")]
        [TestCase("OutOfRange", "InvalidArgument")]
        [TestCase("InvalidUtf8", "InvalidArgument")]
        [TestCase("InvalidUnicode", "InvalidArgument")]
        [TestCase("InvalidChallenge", "InvalidArgument")]
        [TestCase("InvalidUri", "InvalidArgument")]
        [TestCase("InvalidEnumeration", "InvalidArgument")]
        [TestCase("InvalidDescriptor", "InvalidArgument")]
        [TestCase("UnknownOperation", "Unsupported")]
        [TestCase("UnknownDiscriminator", "Unsupported")]
        [TestCase("UnsupportedVersion", "Unsupported")]
        [TestCase("InvalidFlow", "InvalidArgument")]
        [TestCase("RequestIdMismatch", "InvalidArgument")]
        [TestCase("NonFinalUasc", "InvalidArgument")]
        [TestCase("DatagramControl", "Unsupported")]
        [TestCase("CarrierMismatch", "InvalidArgument")]
        [TestCase("InvalidStream", "InvalidArgument")]
        public void WireErrorsHaveExactStatusCategories(string error, string status)
        {
            Assert.That(Enum.TryParse(error, out FlowCodecError value), Is.True);
            var exception = new FlowCodecException(value);
            Assert.That(exception.Error, Is.EqualTo(value));
            Assert.That(exception.Status.ToString(), Is.EqualTo(status));
        }

        /// <summary>
        /// Checks carrier output against fixed specimen bytes, not another codec's computed expectations.
        /// </summary>
        private static void AssertEnvelopeEncoding(FrozenWire specimen, in FlowRecord record)
        {
            ByteString encoded;
            if (specimen.Carrier is FlowCarrier.DedicatedUasc or FlowCarrier.SharedUasc)
            {
                encoded = FlowCarrierCodec.EncodeUascPayload(
                    specimen.Carrier, specimen.ChannelId, in record, new(), out uint requestId);
                Assert.That(requestId, Is.Zero);
            }
            else if (specimen.Datagram)
            {
                encoded = FlowCarrierCodec.EncodeDatagram(
                    specimen.Carrier, specimen.ChannelId, in record, specimen.Bytes.Length, new());
            }
            else
            {
                encoded = FlowRecordCodec.Encode(in record, new());
            }
            Assert.That(encoded.ToArray(), Is.EqualTo(specimen.Bytes.ToArray()));
        }

        /// <summary>
        /// Checks the concrete shape and every semantic field of the independently authored control.
        /// </summary>
        private static void AssertControl(FlowControl? actual, FlowControl expected)
        {
            Assert.That(actual, Is.TypeOf(expected.GetType()));
            Assert.That(actual!.Operation, Is.EqualTo(expected.Operation));
            Assert.That(actual.Flow, Is.EqualTo(expected.Flow));
            switch (expected)
            {
                case FlowHello helloExpected:
                    var hello = (FlowHello)actual;
                    Assert.Multiple(() =>
                    {
                        Assert.That(hello.Version, Is.EqualTo(helloExpected.Version));
                        Assert.That(hello.Carrier, Is.EqualTo(helloExpected.Carrier));
                        Assert.That(hello.Application, Is.EqualTo(helloExpected.Application));
                        Assert.That(hello.Context, Is.EqualTo(helloExpected.Context));
                        Assert.That(hello.Generation, Is.EqualTo(helloExpected.Generation));
                        Assert.That(hello.Challenge.ToArray(), Is.EqualTo(helloExpected.Challenge.ToArray()));
                        Assert.That(hello.Protection, Is.EqualTo(helloExpected.Protection));
                        Assert.That(hello.MaxMessageSize, Is.EqualTo(helloExpected.MaxMessageSize));
                        Assert.That(hello.MaxReceiveBytes, Is.EqualTo(helloExpected.MaxReceiveBytes));
                        Assert.That(hello.MaxFlows, Is.EqualTo(helloExpected.MaxFlows));
                    });
                    break;
                case FlowOpen openExpected:
                    var open = (FlowOpen)actual;
                    Assert.That(open.Stream, Is.EqualTo(openExpected.Stream));
                    Assert.That(open.Authorization, Is.EqualTo(openExpected.Authorization));
                    Assert.That(open.Grant, Is.EqualTo(openExpected.Grant));
                    AssertParameters(open.Parameters, openExpected.Parameters);
                    break;
                case FlowAccept acceptExpected:
                    var accept = (FlowAccept)actual;
                    Assert.That(accept.Stream, Is.EqualTo(acceptExpected.Stream));
                    AssertParameters(accept.Parameters, acceptExpected.Parameters);
                    break;
                case FlowPrepare prepareExpected:
                    AssertLimits(((FlowPrepare)actual).Limits, prepareExpected.Limits);
                    break;
                case FlowCommit commitExpected:
                    Assert.That(((FlowCommit)actual).LastOldMessageId, Is.EqualTo(commitExpected.LastOldMessageId));
                    break;
                case FlowCredit creditExpected:
                    var credit = (FlowCredit)actual;
                    Assert.That(credit.ChannelCredit, Is.EqualTo(creditExpected.ChannelCredit));
                    Assert.That(credit.ConnectionCredit, Is.EqualTo(creditExpected.ConnectionCredit));
                    break;
                case FlowProbe probeExpected:
                    Assert.That(((FlowProbe)actual).Timestamp, Is.EqualTo(probeExpected.Timestamp));
                    break;
                case FlowOutcome outcomeExpected:
                    Assert.That(((FlowOutcome)actual).Status, Is.EqualTo(outcomeExpected.Status));
                    break;
            }
        }

        /// <summary>
        /// Checks each descriptor member rather than relying on a serialization round trip.
        /// </summary>
        private static void AssertParameters(FlowWireParameters actual, FlowWireParameters expected)
        {
            Assert.Multiple(() =>
            {
                Assert.That(actual.Resource, Is.EqualTo(expected.Resource));
                Assert.That(actual.Protocol, Is.EqualTo(expected.Protocol));
                Assert.That(actual.ContentType, Is.EqualTo(expected.ContentType));
                Assert.That(actual.Direction, Is.EqualTo(expected.Direction));
                Assert.That(actual.Delivery, Is.EqualTo(expected.Delivery));
                Assert.That(actual.Protection, Is.EqualTo(expected.Protection));
                Assert.That(actual.AllowQueueExpiry, Is.EqualTo(expected.AllowQueueExpiry));
            });
            AssertLimits(actual.Limits, expected.Limits);
        }

        /// <summary>
        /// Checks all independently expected numeric descriptor members.
        /// </summary>
        private static void AssertLimits(FlowWireLimits actual, FlowWireLimits expected)
        {
            Assert.Multiple(() =>
            {
                Assert.That(actual.MaxMessageSize, Is.EqualTo(expected.MaxMessageSize));
                Assert.That(actual.MaxReceiveBytes, Is.EqualTo(expected.MaxReceiveBytes));
                Assert.That(actual.MaxRate, Is.EqualTo(expected.MaxRate));
                Assert.That(actual.Priority, Is.EqualTo(expected.Priority));
            });
        }
    }
}
