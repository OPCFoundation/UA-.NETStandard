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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Fuzzing
{
    /// <summary>
    /// Pins the deterministic, populated messages used by the encoder corpus.
    /// </summary>
    [TestFixture]
    [Category("Fuzzing")]
    public sealed class EncoderCorpusTests
    {
        [Test]
        public void MessageEncoderCatalogContainsAllSevenSeeds()
        {
            string[] names = Testcases.GetMessageEncoders().Select(encoder => encoder.Method.Name).ToArray();

            Assert.That(names, Is.EqualTo(s_seedNames));
            Assert.That(Testcases.SeedTimestamp, Is.EqualTo(new DateTimeUtc(2025, 1, 2, 3, 4, 5)));
        }

        [Test]
        public void SharedMessageEncoderCatalogContainsTheFourCanonicalMessages()
        {
            Assert.That(
                Testcases.MessageEncoders.Select(encoder => encoder.Method.Name),
                Is.EqualTo(EncoderTestMessages.RichNames.ToArray()));
        }

        [TestCase("ReadRequest")]
        [TestCase("ReadResponse")]
        [TestCase("PublishResponse")]
        [TestCase("WriteRequest")]
        public void SharedCanonicalGeneratorsRejectMissingEncoders(string canonicalName)
        {
            Testcases.MessageEncoder encode = Testcases.MessageEncoders.Single(
                encoder => encoder.Method.Name == canonicalName);

            Assert.That(
                () => encode(null),
                Throws.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("encoder"));
        }

        [TestCaseSource(nameof(PersistentRichCorpusCases))]
        public void SharedCanonicalGeneratorsMatchTheRichCorpus(string seedName, string wire)
        {
            string canonicalName = seedName.EndsWith("Rich", StringComparison.Ordinal)
                ? seedName[..^4]
                : seedName;
            Testcases.MessageEncoder encode = Testcases.MessageEncoders.Single(
                encoder => encoder.Method.Name == canonicalName);
            ByteString actual = EncodeCallback(encode, wire);

            Assert.That(actual, Is.EqualTo(EncodeSeed(seedName, wire)));
            EncoderTestMessages.AssertPopulated(EncoderTestMessages.Decode(actual.Span, wire));
        }

        [TestCase("ReadRequest")]
        [TestCase("ReadResponse")]
        [TestCase("PublishResponse")]
        [TestCase("WriteRequest")]
        public void RichSeedFactoriesReturnIndependentPopulatedMessages(string messageName)
        {
            IEncodeable first = EncoderTestMessages.Create(messageName);
            IEncodeable second = EncoderTestMessages.Create(messageName);

            Assert.That(first, Is.Not.SameAs(second));
            EncoderTestMessages.AssertPopulated(first);
            EncoderTestMessages.AssertPopulated(second);
            Assert.That(Utils.IsEqual(first, second), Is.True);

            switch (first)
            {
                case ReadRequest read:
                    read.NodesToRead[2].IndexRange = "4:5";
                    break;
                case ReadResponse read:
                    read.ResponseHeader.ServiceDiagnostics.AdditionalInfo = "changed";
                    break;
                case PublishResponse publish:
                    Assert.That(
                        publish.NotificationMessage.NotificationData[0]
                            .TryGetValue(out DataChangeNotification notification),
                        Is.True);
                    notification.MonitoredItems[0].ClientHandle = 999;
                    break;
                case WriteRequest write:
                    write.NodesToWrite[0].IndexRange = "4:5";
                    break;
                default:
                    throw new ArgumentException("Unknown rich message.", nameof(messageName));
            }

            Assert.That(Utils.IsEqual(first, second), Is.False, "Nested seed objects must not be shared.");
            EncoderTestMessages.AssertPopulated(second);
        }

        [TestCaseSource(nameof(DeterministicEncodingCases))]
        public void FreshCorpusMessagesHaveDeterministicEncodingAndPopulatedDecodes(string seedName, string wire)
        {
            ByteString first = EncodeSeed(seedName, wire);
            ByteString second = EncodeSeed(seedName, wire);
            IEncodeable decoded = EncoderTestMessages.Decode(first.Span, wire);
            IEncodeable decodedAgain = EncoderTestMessages.Decode(second.Span, wire);

            Assert.That(first.IsNull, Is.False);
            Assert.That(first.Length, Is.GreaterThan(64), "Each seed must cross the default segment boundary.");
            Assert.That(second, Is.EqualTo(first), "Freshly constructed seeds must have stable bytes/text.");
            Assert.That(decoded, Is.Not.Null);
            Assert.That(decodedAgain, Is.Not.SameAs(decoded));
            EncoderTestMessages.AssertPopulated(decoded);
            EncoderTestMessages.AssertPopulated(decodedAgain);
            Assert.That(Utils.IsEqual(decoded, decodedAgain), Is.True);
            Assert.That(EncoderTestMessages.Encode(decoded, wire), Is.EqualTo(first));
        }

        [TestCaseSource(nameof(PersistentRichCorpusCases))]
        public async Task CheckedInRichCorpusMatchesTheDeterministicGeneratorAsync(string seedName, string wire)
        {
            string extension = wire == "Binary" ? "bin" : wire.ToLowerInvariant();
            string path = Path.Combine(
                AppContext.BaseDirectory,
                "Testcases",
                seedName.ToLowerInvariant() + "." + extension);
            using var source = File.OpenRead(path);
            using var buffer = new MemoryStream();
            await source.CopyToAsync(buffer).ConfigureAwait(false);
            ByteString actual = ByteString.From(buffer.ToArray());

            Assert.That(actual, Is.EqualTo(EncodeSeed(seedName, wire)));
            IEncodeable decoded = EncoderTestMessages.Decode(actual.Span, wire);
            EncoderTestMessages.AssertPopulated(decoded);
        }

        private static IEnumerable<TestCaseData> DeterministicEncodingCases()
        {
            foreach (string name in s_seedNames)
            {
                foreach (string wire in new[] { "Binary", "Json", "Xml" })
                {
                    yield return new TestCaseData(name, wire);
                }
            }
        }

        private static IEnumerable<TestCaseData> PersistentRichCorpusCases()
        {
            foreach (TestCaseData test in DeterministicEncodingCases())
            {
                if (test.Arguments[0] is "ReadRequestRich" or "ReadResponseRich" or
                    "PublishResponse" or "WriteRequestRich")
                {
                    yield return test;
                }
            }
        }

        private static ByteString EncodeSeed(string seedName, string wire)
        {
            Testcases.MessageEncoder encode = Testcases.GetMessageEncoders()
                .Single(encoder => encoder.Method.Name == seedName);
            return EncodeCallback(encode, wire);
        }

        private static ByteString EncodeCallback(Testcases.MessageEncoder encode, string wire)
        {
            using var stream = new MemoryStream();
            switch (wire)
            {
                case "Binary":
                    using (var encoder = new BinaryEncoder(FuzzableCode.MessageContext))
                    {
                        encode(encoder);
                        return ByteString.From(encoder.CloseAndReturnBuffer());
                    }
                case "Json":
                    using (var encoder = new JsonEncoder(
                        stream,
                        FuzzableCode.MessageContext,
                        JsonEncoderOptions.Verbose))
                    {
                        encode(encoder);
                        encoder.Close();
                        return ByteString.From(stream.ToArray());
                    }
                case "Xml":
                    using (var encoder = new XmlEncoder(FuzzableCode.MessageContext))
                    {
                        encoder.SetMappingTables(
                            FuzzableCode.MessageContext.NamespaceUris,
                            FuzzableCode.MessageContext.ServerUris);
                        encode(encoder);
                        return ByteString.From(Encoding.UTF8.GetBytes(encoder.CloseAndReturnText()));
                    }
                default:
                    throw new ArgumentException("Unknown wire encoding.", nameof(wire));
            }
        }

        private static readonly string[] s_seedNames =
        [
            "ReadRequestRich",
            "ReadResponseRich",
            "PublishResponse",
            "BrowseRequest",
            "WriteRequestRich",
            "DataTypeNodeMessage",
            "VariableNodeMessage"
        ];
    }

    /// <summary>
    /// Shared concrete-value oracles; none of these helpers has a fuzz callback signature.
    /// </summary>
    internal static class EncoderTestMessages
    {
        internal static ArrayOf<string> RichNames { get; } =
        [
            "ReadRequest", "ReadResponse", "PublishResponse", "WriteRequest"
        ];

        internal static IEncodeable Create(string messageName)
        {
            return messageName switch
            {
                "ReadRequest" => Testcases.CreateRichReadRequest(),
                "ReadResponse" => Testcases.CreateRichReadResponse(),
                "PublishResponse" => Testcases.CreatePublishResponse(),
                "WriteRequest" => Testcases.CreateRichWriteRequest(),
                _ => throw new ArgumentException("Unknown rich message.", nameof(messageName))
            };
        }

        internal static ByteString Encode(IEncodeable message, string wire)
        {
            switch (wire)
            {
                case "Binary":
                    return ByteString.From(BinaryEncoder.EncodeMessage(message, FuzzableCode.MessageContext));
                case "Json":
                    return ByteString.From(Encoding.UTF8.GetBytes(
                        FuzzableCode.EncodeJsonMessage(message, JsonEncoderOptions.Verbose)));
                case "Xml":
                    using (var encoder = new XmlEncoder(FuzzableCode.MessageContext))
                    {
                        WriteTypedXmlMessage(encoder, message);
                        return ByteString.From(Encoding.UTF8.GetBytes(encoder.CloseAndReturnText()));
                    }
                default:
                    throw new ArgumentException("Unknown wire encoding.", nameof(wire));
            }
        }

        internal static IEncodeable Decode(ReadOnlySpan<byte> input, string wire, bool strict = true)
        {
            using var stream = new MemoryStream(input.ToArray());
            return wire switch
            {
                "Binary" => FuzzableCode.FuzzBinaryDecoderCore(stream, strict),
                "Json" => FuzzableCode.FuzzJsonDecoderCore(Encoding.UTF8.GetString(input.ToArray()), strict),
                "Xml" => FuzzableCode.FuzzXmlDecoderCore(stream, strict),
                _ => throw new ArgumentException("Unknown wire encoding.", nameof(wire))
            };
        }

        internal static void AssertPopulated(IEncodeable message)
        {
            Assert.That(message, Is.Not.Null, "A swallowed source-decoding failure is not a successful replay.");
            switch (message)
            {
                case ReadRequest request:
                    AssertReadRequest(request);
                    break;
                case ReadResponse response:
                    AssertReadResponse(response);
                    break;
                case PublishResponse publish:
                    AssertPublishResponse(publish);
                    break;
                case WriteRequest write:
                    AssertWriteRequest(write);
                    break;
                case BrowseRequest browse:
                    Assert.That(browse.RequestedMaxReferencesPerNode, Is.EqualTo(10));
                    Assert.That(browse.NodesToBrowse.Count, Is.EqualTo(1));
                    Assert.That(browse.NodesToBrowse[0].NodeId, Is.EqualTo(ObjectIds.ObjectsFolder));
                    Assert.That(browse.NodesToBrowse[0].IncludeSubtypes, Is.True);
                    Assert.That(browse.NodesToBrowse[0].ResultMask, Is.EqualTo((uint)BrowseResultMask.All));
                    Assert.That(browse.RequestHeader.Timestamp, Is.EqualTo(s_timestamp));
                    break;
                case DataTypeNode node:
                    Assert.That(node.NodeId, Is.EqualTo(new NodeId(1234, 2)));
                    Assert.That(node.BrowseName, Is.EqualTo(new QualifiedName("Issue3546DataType", 2)));
                    Assert.That(node.References.Count, Is.EqualTo(2));
                    Assert.That(node.RolePermissions.Count, Is.EqualTo(1));
                    Assert.That(node.DataTypeDefinition.TryGetValue(out StructureDefinition definition), Is.True);
                    Assert.That(definition.Fields.Count, Is.EqualTo(2));
                    Assert.That(definition.Fields[0].DataType, Is.EqualTo(DataTypeIds.UInt32));
                    Assert.That(definition.Fields[1].DataType, Is.EqualTo(DataTypeIds.String));
                    break;
                case VariableNode node:
                    Assert.That(node.NodeId, Is.EqualTo(new NodeId(4321, 2)));
                    Assert.That(node.References.Count, Is.EqualTo(2));
                    Assert.That(node.ArrayDimensions.Count, Is.EqualTo(2));
                    Assert.That(node.ArrayDimensions[0], Is.EqualTo(1));
                    Assert.That(node.ArrayDimensions[1], Is.EqualTo(2));
                    Assert.That(node.Value.TryGetStructure(out Argument argument), Is.True);
                    Assert.That(argument.Name, Is.EqualTo("Sample"));
                    Assert.That(argument.DataType, Is.EqualTo(DataTypeIds.UInt32));
                    break;
                default:
                    throw new ArgumentException($"Unexpected message type {message.GetType().Name}.", nameof(message));
            }
        }

        private static void WriteTypedXmlMessage(XmlEncoder encoder, IEncodeable message)
        {
            // Keep a concrete generic type: EncodeMessage<IEncodeable> emits an IEncodeable root QName.
            switch (message)
            {
                case ReadRequest read:
                    encoder.EncodeMessage(read);
                    break;
                case ReadResponse read:
                    encoder.EncodeMessage(read);
                    break;
                case PublishResponse publish:
                    encoder.EncodeMessage(publish);
                    break;
                case WriteRequest write:
                    encoder.EncodeMessage(write);
                    break;
                case BrowseRequest browse:
                    encoder.EncodeMessage(browse);
                    break;
                case DataTypeNode node:
                    encoder.EncodeMessage(node);
                    break;
                case VariableNode node:
                    encoder.EncodeMessage(node);
                    break;
                default:
                    throw new ArgumentException("Unknown message type.", nameof(message));
            }
        }

        private static void AssertReadRequest(ReadRequest request)
        {
            Assert.That(request.MaxAge, Is.EqualTo(1000));
            Assert.That(request.TimestampsToReturn, Is.EqualTo(TimestampsToReturn.Source));
            Assert.That(request.RequestHeader.Timestamp, Is.EqualTo(s_timestamp));
            Assert.That(request.RequestHeader.RequestHandle, Is.EqualTo(422));
            Assert.That(request.RequestHeader.TimeoutHint, Is.EqualTo(10000));
            Assert.That(request.RequestHeader.ReturnDiagnostics, Is.EqualTo((uint)DiagnosticsMasks.All));
            AssertAdditionalHeader(request.RequestHeader.AdditionalHeader);
            Assert.That(request.NodesToRead.Count, Is.EqualTo(6));
            Assert.That(request.NodesToRead[0].NodeId, Is.EqualTo(new NodeId(123)));
            Assert.That(request.NodesToRead[0].AttributeId, Is.EqualTo(Attributes.UserRolePermissions));
            Assert.That(request.NodesToRead[1].NodeId, Is.EqualTo(new NodeId(4444, 2)));
            Assert.That(request.NodesToRead[1].AttributeId, Is.EqualTo(Attributes.Description));
            Assert.That(request.NodesToRead[2].NodeId, Is.EqualTo(new NodeId("RevisionCounter", 3)));
            Assert.That(request.NodesToRead[2].IndexRange, Is.EqualTo("1:2"));
            Assert.That(request.NodesToRead[2].DataEncoding, Is.EqualTo(new QualifiedName("Default Binary")));
            Assert.That(
                request.NodesToRead[3].NodeId,
                Is.EqualTo(new NodeId(new Guid("00112233-4455-6677-8899-aabbccddeeff"), 1)));
            Assert.That(request.NodesToRead[4].AttributeId, Is.EqualTo(Attributes.AccessLevel));
            Assert.That(
                request.NodesToRead[5].NodeId,
                Is.EqualTo(new NodeId(ByteString.From([66, 22, 55, 44, 11]), 4)));
        }

        private static void AssertReadResponse(ReadResponse response)
        {
            Assert.That(response.ResponseHeader.Timestamp, Is.EqualTo(s_timestamp));
            Assert.That(response.ResponseHeader.RequestHandle, Is.EqualTo(42));
            Assert.That(response.ResponseHeader.StringTable.Count, Is.EqualTo(3));
            Assert.That(response.ResponseHeader.StringTable[0], Is.EqualTo("Hello"));
            Assert.That(response.ResponseHeader.StringTable[1], Is.EqualTo("World"));
            Assert.That(response.ResponseHeader.StringTable[2], Is.EqualTo("Goodbye"));
            AssertAdditionalHeader(response.ResponseHeader.AdditionalHeader);
            DiagnosticInfo diagnostic = response.ResponseHeader.ServiceDiagnostics;
            Assert.That(diagnostic.SymbolicId, Is.EqualTo(0));
            Assert.That(diagnostic.NamespaceUri, Is.EqualTo(1));
            Assert.That(diagnostic.LocalizedText, Is.EqualTo(2));
            Assert.That(diagnostic.AdditionalInfo, Is.EqualTo("NodeId not found"));
            Assert.That(
                diagnostic.InnerStatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadAggregateConfigurationRejected));
            Assert.That(diagnostic.InnerDiagnosticInfo.AdditionalInfo, Is.EqualTo("Invalid index range"));
            Assert.That(
                diagnostic.InnerDiagnosticInfo.InnerStatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadIndexRangeInvalid));
            Assert.That(
                diagnostic.InnerDiagnosticInfo.InnerDiagnosticInfo.AdditionalInfo,
                Is.EqualTo("Invalid channel"));
            Assert.That(
                diagnostic.InnerDiagnosticInfo.InnerDiagnosticInfo.InnerStatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadSecureChannelIdInvalid));
            DiagnosticInfo deepest = diagnostic.InnerDiagnosticInfo.InnerDiagnosticInfo.InnerDiagnosticInfo;
            Assert.That(deepest.AdditionalInfo, Is.EqualTo("Already exists"));
            Assert.That(deepest.InnerStatusCode, Is.EqualTo((StatusCode)StatusCodes.BadAlreadyExists));
            Assert.That(deepest.InnerDiagnosticInfo, Is.Null);
            Assert.That(response.Results.Count, Is.EqualTo(11));
            Assert.That(response.Results[0].WrappedValue.TryGetValue(out string greeting), Is.True);
            Assert.That(greeting, Is.EqualTo("Hello World"));
            AssertSeedTimestamps(response.Results[0]);
            Assert.That(response.Results[1].WrappedValue.TryGetValue(out uint number), Is.True);
            Assert.That(number, Is.EqualTo(12345678));
            Assert.That(response.Results[1].StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadDataLost));
            Assert.That(response.Results[1].ServerTimestamp, Is.EqualTo(new DateTimeUtc(2025, 1, 2)));
            Assert.That(response.Results[2].WrappedValue.TryGetValue(out ByteString bytes), Is.True);
            Assert.That(bytes, Is.EqualTo(ByteString.From([0, 1, 2, 3, 4, 5, 6])));
            Assert.That(response.Results[2].ServerTimestamp, Is.EqualTo(DateTimeUtc.MaxValue));
            Assert.That(response.Results[3].WrappedValue.TryGetValue(out byte smallNumber), Is.True);
            Assert.That(smallNumber, Is.EqualTo(42));
            Assert.That(response.Results[4].WrappedValue.TryGetValue(out ulong largeNumber), Is.True);
            Assert.That(largeNumber, Is.EqualTo(0xbadbeefUL));
            Assert.That(response.Results[5].WrappedValue.TryGetValue(out MatrixOf<byte> matrix), Is.True);
            Assert.That(matrix.IsNull, Is.False);
            Assert.That(matrix.Dimensions, Is.EqualTo(s_dimensions));
            Assert.That(matrix.Span.ToArray(), Is.EqualTo(s_byteMatrix));
            Assert.That(response.Results[6].WrappedValue.TryGetValue(out double real), Is.True);
            Assert.That(real, Is.EqualTo(2025.111));
            Assert.That(response.Results[7].WrappedValue.TryGetValue(out LocalizedText text), Is.True);
            Assert.That(text, Is.EqualTo(new LocalizedText("en-us", "The text")));
            Assert.That(response.Results[8].WrappedValue.TryGetValue(out QualifiedName name), Is.True);
            Assert.That(name, Is.EqualTo(new QualifiedName("The text", 2)));
            Assert.That(response.Results[9].WrappedValue.TryGetStructure(out ThreeDVector vector), Is.True);
            AssertVector(vector, 1, -1, 0);
            Assert.That(response.Results[10].WrappedValue.TryGetValue(out ArrayOf<ExtensionObject> vectors), Is.True);
            Assert.That(vectors.IsNull, Is.False);
            Assert.That(vectors.Count, Is.EqualTo(2));
            Assert.That(vectors[0].TryGetValue(out ThreeDVector firstVector), Is.True);
            AssertVector(firstVector, 1, -1, 0);
            Assert.That(vectors[1].TryGetValue(out ThreeDVector secondVector), Is.True);
            AssertVector(secondVector, 2, -2, 1);
            AssertNestedDiagnostic(response.DiagnosticInfos[0]);
        }

        private static void AssertPublishResponse(PublishResponse response)
        {
            Assert.That(response.SubscriptionId, Is.EqualTo(1234));
            Assert.That(response.MoreNotifications, Is.True);
            Assert.That(response.AvailableSequenceNumbers.Count, Is.EqualTo(4));
            Assert.That(response.AvailableSequenceNumbers[3], Is.EqualTo(4));
            Assert.That(response.ResponseHeader.Timestamp, Is.EqualTo(s_timestamp));
            Assert.That(response.ResponseHeader.StringTable[0], Is.EqualTo("No error occurred"));
            Assert.That(response.NotificationMessage.SequenceNumber, Is.EqualTo(123456));
            Assert.That(response.NotificationMessage.PublishTime, Is.EqualTo(s_timestamp));
            Assert.That(response.NotificationMessage.NotificationData.Count, Is.EqualTo(1));
            Assert.That(
                response.NotificationMessage.NotificationData[0].TryGetValue(out DataChangeNotification notification),
                Is.True);
            Assert.That(notification.MonitoredItems.Count, Is.EqualTo(11));
            Assert.That(notification.MonitoredItems[0].ClientHandle, Is.EqualTo(122));
            Assert.That(notification.MonitoredItems[0].Value.WrappedValue.TryGetValue(out string greeting), Is.True);
            Assert.That(greeting, Is.EqualTo("Hello World"));
            AssertSeedTimestamps(notification.MonitoredItems[0].Value);
            Assert.That(
                notification.MonitoredItems[1].Value.WrappedValue.TryGetValue(out MatrixOf<uint> matrix),
                Is.True);
            Assert.That(matrix.Dimensions, Is.EqualTo(s_dimensions));
            Assert.That(matrix.Span.ToArray(), Is.EqualTo(s_uintMatrix));
            Assert.That(notification.MonitoredItems[2].Value.WrappedValue.TryGetValue(out NodeId numericNode), Is.True);
            Assert.That(numericNode, Is.EqualTo(new NodeId(1000, 2)));
            Assert.That(notification.MonitoredItems[3].Value.WrappedValue.TryGetValue(out NodeId stringNode), Is.True);
            Assert.That(stringNode, Is.EqualTo(new NodeId("Counter", 1)));
            Assert.That(notification.MonitoredItems[4].Value.WrappedValue.TryGetValue(out bool boolean), Is.True);
            Assert.That(boolean, Is.True);
            Assert.That(notification.MonitoredItems[5].Value.WrappedValue.TryGetValue(out byte smallNumber), Is.True);
            Assert.That(smallNumber, Is.EqualTo(123));
            Assert.That(notification.MonitoredItems[6].Value.WrappedValue.TryGetValue(out float single), Is.True);
            Assert.That(single, Is.EqualTo(123.123f));
            Assert.That(notification.MonitoredItems[7].Value.WrappedValue.TryGetValue(out double real), Is.True);
            Assert.That(real, Is.EqualTo(12301232.123));
            Assert.That(notification.MonitoredItems[8].Value.WrappedValue.TryGetValue(out long signed), Is.True);
            Assert.That(signed, Is.EqualTo(-123012321234123L));
            Assert.That(notification.MonitoredItems[9].Value.WrappedValue.TryGetValue(out ulong unsigned), Is.True);
            Assert.That(unsigned, Is.EqualTo(123012321234123UL));
            Assert.That(
                notification.MonitoredItems[10].Value.WrappedValue.TryGetValue(out ArrayOf<uint> array),
                Is.True);
            Assert.That(array.Count, Is.EqualTo(10));
            Assert.That(array[0], Is.EqualTo(1));
            Assert.That(array[9], Is.Zero);
            AssertNestedDiagnostic(notification.DiagnosticInfos[0]);
            Assert.That(response.Results.Count, Is.EqualTo(2));
            Assert.That(response.Results[1], Is.EqualTo((StatusCode)StatusCodes.BadSequenceNumberUnknown));
            AssertNestedDiagnostic(response.DiagnosticInfos[0]);
        }

        private static void AssertWriteRequest(WriteRequest request)
        {
            Assert.That(request.RequestHeader.Timestamp, Is.EqualTo(s_timestamp));
            Assert.That(request.RequestHeader.RequestHandle, Is.EqualTo(422));
            Assert.That(request.RequestHeader.ReturnDiagnostics, Is.EqualTo((uint)DiagnosticsMasks.All));
            Assert.That(request.RequestHeader.AdditionalHeader.IsNull, Is.True);
            Assert.That(request.NodesToWrite.Count, Is.EqualTo(10));
            Assert.That(request.NodesToWrite[0].NodeId, Is.EqualTo(new NodeId(123)));
            Assert.That(request.NodesToWrite[0].IndexRange, Is.EqualTo("1:2"));
            Assert.That(request.NodesToWrite[0].Value.WrappedValue.TryGetValue(out string greeting), Is.True);
            Assert.That(greeting, Is.EqualTo("Hello World"));
            AssertSeedTimestamps(request.NodesToWrite[0].Value);
            Assert.That(request.NodesToWrite[1].AttributeId, Is.EqualTo(Attributes.ValueRank));
            Assert.That(request.NodesToWrite[1].Value.WrappedValue.TryGetValue(out int number), Is.True);
            Assert.That(number, Is.EqualTo(12345));
            Assert.That(request.NodesToWrite[2].Value.WrappedValue.TryGetValue(out float single), Is.True);
            Assert.That(single, Is.EqualTo(123.45f));
            Assert.That(request.NodesToWrite[3].Value.WrappedValue.TryGetValue(out double real), Is.True);
            Assert.That(real, Is.EqualTo(123.45));
            Assert.That(request.NodesToWrite[4].Value.WrappedValue.TryGetValue(out bool boolean), Is.True);
            Assert.That(boolean, Is.True);
            Assert.That(request.NodesToWrite[5].Value.WrappedValue.TryGetValue(out ByteString bytes), Is.True);
            Assert.That(bytes, Is.EqualTo(ByteString.From([1, 2, 3, 4, 5])));
            Assert.That(request.NodesToWrite[6].NodeId, Is.EqualTo(new NodeId("FastCounter", 2)));
            Assert.That(request.NodesToWrite[6].Value.WrappedValue.TryGetValue(out ArrayOf<int> integers), Is.True);
            Assert.That(integers.Span.ToArray(), Is.EqualTo(s_integers));
            Assert.That(
                request.NodesToWrite[7].NodeId,
                Is.EqualTo(new NodeId(ByteString.From([0xaa, 0xbb, 0xcc, 0xdd, 0xee]), 3)));
            Assert.That(request.NodesToWrite[7].Value.WrappedValue.TryGetValue(out ArrayOf<float> singles), Is.True);
            Assert.That(singles.Count, Is.EqualTo(3));
            Assert.That(singles[2], Is.EqualTo(3.3f));
            Assert.That(
                request.NodesToWrite[8].NodeId,
                Is.EqualTo(new NodeId(new Guid("10213243-5465-7687-98a9-bacbdcedfe0f"), 1)));
            Assert.That(request.NodesToWrite[8].Value.WrappedValue.TryGetValue(out ArrayOf<double> doubles), Is.True);
            Assert.That(doubles.Count, Is.EqualTo(3));
            Assert.That(doubles[1], Is.EqualTo(2.2));
            Assert.That(request.NodesToWrite[9].Value.WrappedValue.TryGetValue(out ArrayOf<string> strings), Is.True);
            Assert.That(strings.Span.ToArray(), Is.EqualTo(s_strings));
        }

        private static void AssertAdditionalHeader(ExtensionObject header)
        {
            Assert.That(header.IsNull, Is.False);
            Assert.That(header.TryGetValue(out AdditionalParametersType parameters), Is.True);
            Assert.That(parameters.Parameters.Count, Is.EqualTo(1));
            Assert.That(parameters.Parameters[0].Key, Is.EqualTo(new QualifiedName("traceparent")));
            Assert.That(parameters.Parameters[0].Value.TryGetValue(out string traceParent), Is.True);
            Assert.That(traceParent, Is.EqualTo("00-00112233445566778899aabbccddeeff-0123456789abcdef-01"));
        }

        private static void AssertSeedTimestamps(in DataValue value)
        {
            Assert.That(value.SourceTimestamp, Is.EqualTo(new DateTimeUtc(2025, 1, 2, 3, 5, 5)));
            Assert.That(value.ServerTimestamp, Is.EqualTo(s_timestamp));
            Assert.That(value.SourcePicoseconds, Is.EqualTo(10));
            Assert.That(value.ServerPicoseconds, Is.EqualTo(100));
        }

        private static void AssertNestedDiagnostic(DiagnosticInfo diagnostic)
        {
            Assert.That(diagnostic.AdditionalInfo, Is.EqualTo("Hello World"));
            Assert.That(diagnostic.InnerStatusCode, Is.EqualTo((StatusCode)StatusCodes.BadCertificateHostNameInvalid));
            Assert.That(diagnostic.InnerDiagnosticInfo.AdditionalInfo, Is.EqualTo("Unknown node"));
            Assert.That(
                diagnostic.InnerDiagnosticInfo.InnerStatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadNodeIdUnknown));
            Assert.That(diagnostic.InnerDiagnosticInfo.InnerDiagnosticInfo, Is.Null);
        }

        private static void AssertVector(ThreeDVector vector, double x, double y, double z)
        {
            Assert.That(vector.X, Is.EqualTo(x));
            Assert.That(vector.Y, Is.EqualTo(y));
            Assert.That(vector.Z, Is.EqualTo(z));
        }

        private static readonly DateTimeUtc s_timestamp = new(2025, 1, 2, 3, 4, 5);
        private static readonly int[] s_dimensions = [2, 2, 2];
        private static readonly byte[] s_byteMatrix = [1, 2, 3, 4, 11, 22, 33, 44];
        private static readonly uint[] s_uintMatrix = [1, 2, 3, 4, 11, 22, 33, 44];
        private static readonly int[] s_integers = [1, 2, 3, 4, 5];
        private static readonly string[] s_strings = ["one", "two", "three"];
    }
}
