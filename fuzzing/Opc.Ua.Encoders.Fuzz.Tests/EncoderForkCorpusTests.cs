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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml.Linq;
using NUnit.Framework;

namespace Opc.Ua.Fuzzing
{
    /// <summary>
    /// Replays captured April 2025 fork messages, never regenerated semantic substitutes.
    /// </summary>
    [TestFixture]
    [Category("Fuzzing")]
    public sealed class EncoderForkCorpusTests
    {
        [TestCaseSource(nameof(OriginalCases))]
        public async Task OriginalMessageBytesMatchCapturedForkProvenanceAsync(string wire, string messageName)
        {
            byte[] input = await ReadOriginalAsync(wire, messageName).ConfigureAwait(false);

            Assert.That(input.Length, Is.GreaterThan(64), "Original messages cross segmented buffer boundaries.");
        }

        [TestCaseSource(nameof(AcceptedOriginalCases))]
        public async Task OriginalBinaryAndXmlMessagesDecodeAsPopulatedTypesAsync(string wire, string messageName)
        {
            byte[] input = await ReadOriginalAsync(wire, messageName).ConfigureAwait(false);

            IEncodeable decoded = DecodeOriginal(input, wire);

            AssertOriginalMessage(decoded, wire, messageName);
        }

        [TestCaseSource(nameof(CallbackCases))]
        public async Task OriginalMessagesReplayExactCallbacksAfterPositiveTypedDecodeAsync(
            string wire,
            string messageName,
            string callbackName)
        {
            byte[] input = await ReadOriginalAsync(wire, messageName).ConfigureAwait(false);
            IEncodeable decoded = DecodeOriginal(input, wire);
            AssertOriginalMessage(decoded, wire, messageName);
            if (wire == "Binary" &&
                (callbackName.StartsWith("Aflfuzz", StringComparison.Ordinal) ||
                    callbackName.EndsWith("Segmented", StringComparison.Ordinal)))
            {
                using MemoryStream segmented = FuzzableCode.PrepareArraySegmentStream(input);
                IEncodeable segmentedDecode = FuzzableCode.FuzzBinaryDecoderCore(segmented, throwAll: true);
                AssertOriginalMessage(segmentedDecode, wire, messageName);
                Assert.That(Utils.IsEqual(decoded, segmentedDecode), Is.True);
            }
            Delegate callback = FuzzMethods.FindFuzzMethod(TestContext.Error, callbackName);
            Assert.That(callback, Is.Not.Null);
            Assert.That(callback.Method.Name, Is.EqualTo(callbackName));
            string[] namespaces = FuzzableCode.MessageContext.NamespaceUris.ToArray();
            string[] servers = FuzzableCode.MessageContext.ServerUris.ToArray();

            FuzzMethods.Replay(callback, input);

            Assert.That(FuzzableCode.MessageContext.NamespaceUris.ToArray(), Is.EqualTo(namespaces));
            Assert.That(FuzzableCode.MessageContext.ServerUris.ToArray(), Is.EqualTo(servers));
            AssertOriginalHash(input, wire, messageName);
            int conversion = callbackName.IndexOf("JsonEncoder", StringComparison.Ordinal);
            if (conversion >= 0)
            {
                string mode = callbackName[(conversion + "JsonEncoder".Length)..];
                AssertJsonGenerations(decoded, wire, messageName, mode.Length == 0 ? "Verbose" : mode);
            }
            else
            {
                AssertWireGenerations(decoded, wire, messageName);
            }
        }

        [TestCase(nameof(ReadResponse), "ListOfByte")]
        [TestCase(nameof(PublishResponse), "ListOfUInt32")]
        public async Task OriginalMalformedXmlMatrixIsRejectedAsInTheForkAsync(
            string messageName,
            string elementName)
        {
            byte[] input = await ReadOriginalAsync("Xml", messageName).ConfigureAwait(false);
            using var stream = new MemoryStream(input, writable: false);
            XDocument document = XDocument.Load(stream);
            XNamespace ns = Namespaces.OpcUaXsd;
            XElement matrix = document.Descendants(ns + "Matrix").First();

            Assert.That(matrix.Elements().Select(element => element.Name.LocalName),
                Is.EqualTo(s_legacyMatrixFields));
            Assert.That(matrix.Element(ns + "Elements").Elements().Single().Name, Is.EqualTo(ns + elementName));
            Assert.That(() => DecodeOriginal(input, "Xml"),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadDecodingError));
        }

        [TestCaseSource(nameof(RejectedXmlCallbackCases))]
        public async Task OriginalMalformedXmlIsReplayedWithoutClaimingSuccessfulDecodingAsync(
            string messageName,
            string callbackName)
        {
            byte[] input = await ReadOriginalAsync("Xml", messageName).ConfigureAwait(false);
            Assert.That(() => DecodeOriginal(input, "Xml"),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadDecodingError));
            Delegate callback = FuzzMethods.FindFuzzMethod(TestContext.Error, callbackName);
            Assert.That(callback, Is.Not.Null);

            FuzzMethods.Replay(callback, input);

            AssertOriginalHash(input, "Xml", messageName);
        }

        [TestCase("Binary", 11, 8)]
        [TestCase("Xml", 8, 8)]
        [TestCase("Json", 7, 7)]
        public void OriginalReplayCatalogPinsEveryApplicableModeAndRoute(string wire, int spanCount, int aflCount)
        {
            string[] callbacks = FuzzMethods.Delegates
                .SelectMany(FuzzMethods.FindFuzzMethods)
                .Select(callback => callback.Method.Name)
                .Where(name => name.StartsWith("Libfuzz" + wire, StringComparison.Ordinal) ||
                    name.StartsWith("Aflfuzz" + wire, StringComparison.Ordinal))
                .ToArray();

            Assert.That(callbacks, Is.EquivalentTo(CallbackNames(wire)));
            Assert.That(callbacks.Count(name => name.StartsWith("Libfuzz", StringComparison.Ordinal)),
                Is.EqualTo(spanCount));
            Assert.That(callbacks.Count(name => name.StartsWith("Aflfuzz", StringComparison.Ordinal)),
                Is.EqualTo(aflCount));
            Assert.That(FuzzableCode.JsonEncodingModes.ToArray().Select(mode => mode.Name),
                Is.EqualTo(s_jsonModes));
        }

        [TestCaseSource(nameof(LegacyJsonCases))]
        [Category("UnsupportedLegacyEnvelope")]
        public async Task OriginalJsonMessagesHaveUnsupportedLegacyEnvelopeAsync(string wire, string messageName)
        {
            byte[] input = await ReadOriginalAsync(wire, messageName).ConfigureAwait(false);
            string json = Encoding.UTF8.GetString(input);
            using JsonDocument document = JsonDocument.Parse(input);
            JsonElement root = document.RootElement;
            int typeId = messageName switch
            {
                nameof(ReadRequest) => 629,
                nameof(ReadResponse) => 632,
                nameof(PublishResponse) => 827,
                nameof(WriteRequest) => 671,
                _ => throw new ArgumentException("Unknown original message.", nameof(messageName))
            };

            Assert.That(root.EnumerateObject().Select(property => property.Name), Is.EqualTo(s_legacyEnvelope));
            Assert.That(root.GetProperty("TypeId").ValueKind, Is.EqualTo(JsonValueKind.Object));
            Assert.That(root.GetProperty("TypeId").GetProperty("Id").GetInt32(), Is.EqualTo(typeId));
            Assert.That(root.GetProperty("Body").ValueKind, Is.EqualTo(JsonValueKind.Object));
            Assert.That(root.TryGetProperty("UaTypeId", out _), Is.False);
            Assert.That(root.TryGetProperty("UaBody", out _), Is.False);
            AssertLegacyBody(root.GetProperty("Body"), messageName);
            Assert.That(
                () => FuzzableCode.FuzzJsonDecoderCore(json, throwAll: true),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadDecodingError)
                    .And.Message.EqualTo("Parsing encountered invalid information. " + json)
                    .And.Property(nameof(Exception.InnerException)).Null);
            TestContext.Out.WriteLine(
                $"GAP: {messageName}.json, unsupported TypeId/Body legacy envelope; " +
                "strict decoder throws ServiceResultException, BadDecodingError (0x80070000). " +
                "All 14 applicable JSON callbacks remain without positive original-byte coverage.\n" +
                "Message: Parsing encountered invalid information. " + json);
        }

        private static IEnumerable<TestCaseData> OriginalCases()
        {
            return s_originals.Select(original => new TestCaseData(original.Wire, original.Message));
        }

        private static IEnumerable<TestCaseData> AcceptedOriginalCases()
        {
            return s_originals.Where(original => IsAcceptedOriginal(original.Wire, original.Message))
                .Select(original => new TestCaseData(original.Wire, original.Message));
        }

        private static IEnumerable<TestCaseData> LegacyJsonCases()
        {
            return s_originals.Where(original => original.Wire == "Json")
                .Select(original => new TestCaseData(original.Wire, original.Message));
        }

        private static IEnumerable<TestCaseData> CallbackCases()
        {
            foreach (var original in s_originals.Where(original => IsAcceptedOriginal(original.Wire, original.Message)))
            {
                foreach (string callback in CallbackNames(original.Wire))
                {
                    yield return new TestCaseData(original.Wire, original.Message, callback);
                }
            }
        }

        private static IEnumerable<TestCaseData> RejectedXmlCallbackCases()
        {
            foreach (string message in new[] { nameof(ReadResponse), nameof(PublishResponse) })
            {
                foreach (string callback in CallbackNames("Xml"))
                {
                    yield return new TestCaseData(message, callback);
                }
            }
        }

        private static bool IsAcceptedOriginal(string wire, string message)
        {
            // The pinned fork itself rejects both response XML files' nested ListOf matrix encoding.
            return wire == "Binary" || (wire == "Xml" && message is nameof(ReadRequest) or nameof(WriteRequest));
        }

        private static IEnumerable<string> CallbackNames(string wire)
        {
            foreach (string route in new[] { "Libfuzz", "Aflfuzz" })
            {
                yield return route + wire + "Decoder";
                yield return route + wire + "Encoder";
                yield return route + wire + "EncoderIndempotent";
                foreach (string mode in s_jsonModes)
                {
                    if (wire != "Json" || mode != "Verbose")
                    {
                        yield return route + wire + (wire == "Json" ? "Encoder" : "JsonEncoder") +
                            (mode == "Verbose" ? string.Empty : mode);
                    }
                }
            }
            if (wire == "Binary")
            {
                yield return nameof(FuzzableCode.LibfuzzBinaryDecoderSegmented);
                yield return nameof(FuzzableCode.LibfuzzBinaryEncoderSegmented);
                yield return nameof(FuzzableCode.LibfuzzBinaryEncoderIndempotentSegmented);
            }
        }

        private static async Task<byte[]> ReadOriginalAsync(string wire, string messageName)
        {
            string extension = wire == "Binary" ? "bin" : wire.ToLowerInvariant();
            string path = Path.Combine(
                AppContext.BaseDirectory,
                "Assets",
                "ForkMessages",
                wire,
                messageName.ToLowerInvariant() + "." + extension);
            using var source = File.OpenRead(path);
            using var buffer = new MemoryStream();
            await source.CopyToAsync(buffer).ConfigureAwait(false);
            byte[] input = buffer.ToArray();

            AssertOriginalHash(input, wire, messageName);
            return input;
        }

        private static void AssertOriginalHash(byte[] input, string wire, string messageName)
        {
            var original = s_originals.Single(item => item.Wire == wire && item.Message == messageName);
            Assert.That(input.Length, Is.EqualTo(original.Length));
#if NET6_0_OR_GREATER
            byte[] digest = SHA256.HashData(input);
#else
            using var sha256 = SHA256.Create();
            byte[] digest = sha256.ComputeHash(input);
#endif
            Assert.That(
                CoreUtils.ToHexString(digest),
                Is.EqualTo(original.Sha256),
                "The copied fork bytes must not be normalized, translated or regenerated.");
        }

        private static IEncodeable DecodeOriginal(byte[] input, string wire)
        {
            using var stream = new MemoryStream(input, writable: false);
            return wire switch
            {
                "Binary" => FuzzableCode.FuzzBinaryDecoderCore(stream, throwAll: true),
                "Xml" => FuzzableCode.FuzzXmlDecoderCore(stream, throwAll: true),
                _ => throw new ArgumentException("Only Binary/XML originals use this typed replay.", nameof(wire))
            };
        }

        private static void AssertOriginalMessage(IEncodeable decoded, string wire, string messageName)
        {
            Type expectedType = messageName switch
            {
                nameof(ReadRequest) => typeof(ReadRequest),
                nameof(ReadResponse) => typeof(ReadResponse),
                nameof(PublishResponse) => typeof(PublishResponse),
                nameof(WriteRequest) => typeof(WriteRequest),
                _ => throw new ArgumentException("Unknown original message.", nameof(messageName))
            };
            var original = s_originals.Single(item => item.Wire == wire && item.Message == messageName);
            DateTimeUtc timestamp = DateTimeUtc.Parse(original.Timestamp, CultureInfo.InvariantCulture);
            Assert.That(decoded, Is.TypeOf(expectedType), "A tolerated rejection is not a compatible source decode.");

            switch (decoded)
            {
                case ReadRequest read:
                    Assert.That(read.RequestHeader.Timestamp, Is.EqualTo(timestamp));
                    AssertReadRequest(read, wire);
                    break;
                case ReadResponse read:
                    Assert.That(read.ResponseHeader.Timestamp, Is.EqualTo(timestamp));
                    AssertReadResponse(read, wire);
                    break;
                case PublishResponse publish:
                    Assert.That(publish.ResponseHeader.Timestamp, Is.EqualTo(timestamp));
                    AssertPublishResponse(publish, wire);
                    break;
                case WriteRequest write:
                    Assert.That(write.RequestHeader.Timestamp, Is.EqualTo(timestamp));
                    AssertWriteRequest(write, wire);
                    break;
            }
        }

        private static void AssertReadRequest(ReadRequest request, string wire)
        {
            AssertRequestHeader(request.RequestHeader);
            AssertAdditionalHeader(request.RequestHeader.AdditionalHeader);
            Assert.That(request.MaxAge, Is.EqualTo(1000));
            Assert.That(request.TimestampsToReturn, Is.EqualTo(TimestampsToReturn.Source));
            Assert.That(request.NodesToRead.ToArray().Select(node => node.NodeId), Is.EqualTo(new[]
            {
                new NodeId(123),
                new NodeId(4444, 2),
                new NodeId("RevisionCounter", 3),
                new NodeId(new Guid(wire == "Binary"
                    ? "043ca476-ffe5-4357-bbf8-85e3ee064689"
                    : "ecad8d48-3bf7-46b9-9d12-bb52deb71f93")),
                new NodeId(4444, 2),
                new NodeId(ByteString.From([66, 22, 55, 44, 11]))
            }));
            Assert.That(request.NodesToRead.ToArray().Select(node => node.AttributeId), Is.EqualTo(s_readAttributes));
            Assert.That(request.NodesToRead[2].IndexRange, Is.EqualTo("1:2"));
            Assert.That(request.NodesToRead[2].DataEncoding.IsNull, Is.True);
        }

        private static void AssertReadResponse(ReadResponse response, string wire)
        {
            Assert.That(response.ResponseHeader.RequestHandle, Is.EqualTo(42));
            Assert.That(response.ResponseHeader.ServiceResult, Is.EqualTo((StatusCode)StatusCodes.Good));
            Assert.That(response.ResponseHeader.StringTable.ToArray(), Is.EqualTo(s_readStrings));
            AssertAdditionalHeader(response.ResponseHeader.AdditionalHeader);
            AssertDiagnostics(response.ResponseHeader.ServiceDiagnostics,
                s_serviceDiagnostics, "NodeId not found");
            Assert.That(response.Results.Count, Is.EqualTo(11));
            Assert.That(response.Results[0].WrappedValue.TryGetValue(out string greeting), Is.True);
            Assert.That(greeting, Is.EqualTo("Hello World"));
            DateTimeUtc captured = CapturedTime(wire == "Binary"
                ? "2025-04-01T16:03:22.0278537Z"
                : "2025-04-01T16:03:31.1133521Z");
            AssertTimestamps(response.Results[0], captured + TimeSpan.FromMinutes(1), captured, 10, 100);
            Assert.That(response.Results[1].WrappedValue.TryGetValue(out uint number), Is.True);
            Assert.That(number, Is.EqualTo(12345678));
            Assert.That(response.Results[1].StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadDataLost));
            Assert.That(response.Results[1].ServerTimestamp, Is.EqualTo(new DateTimeUtc(2025, 3, 31, 22)));
            Assert.That(response.Results[2].WrappedValue.TryGetValue(out ByteString bytes), Is.True);
            Assert.That(bytes, Is.EqualTo(ByteString.From([0, 1, 2, 3, 4, 5, 6])));
            Assert.That(response.Results[2].ServerTimestamp, Is.EqualTo(DateTimeUtc.MaxValue));
            Assert.That(response.Results[3].WrappedValue.TryGetValue(out byte small), Is.True);
            Assert.That(small, Is.EqualTo(42));
            Assert.That(response.Results[4].WrappedValue.TryGetValue(out ulong unsigned), Is.True);
            Assert.That(unsigned, Is.EqualTo(0xbadbeefUL));
            Assert.That(response.Results[5].WrappedValue.TryGetValue(out MatrixOf<byte> matrix), Is.True);
            Assert.That(matrix.Dimensions, Is.EqualTo(s_dimensions));
            Assert.That(matrix.Span.ToArray(), Is.EqualTo(s_byteMatrix));
            Assert.That(response.Results[6].WrappedValue.TryGetValue(out double real), Is.True);
            Assert.That(real, Is.EqualTo(2025.111));
            Assert.That(response.Results[7].WrappedValue.TryGetValue(out LocalizedText text), Is.True);
            Assert.That(text, Is.EqualTo(new LocalizedText("en-us", "The text")));
            Assert.That(response.Results[8].WrappedValue.TryGetValue(out QualifiedName name), Is.True);
            Assert.That(name, Is.EqualTo(new QualifiedName("The text", 2)));
            for (int i = 6; i <= 8; i++)
            {
                Assert.That(response.Results[i].StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadTooManyOperations));
                Assert.That(response.Results[i].SourceTimestamp, Is.EqualTo(captured));
            }
            Assert.That(response.Results[9].WrappedValue.TryGetStructure(out ThreeDVector vector), Is.True);
            AssertVector(vector);
            Assert.That(response.Results[10].WrappedValue.TryGetValue(out ArrayOf<ExtensionObject> vectors), Is.True);
            Assert.That(vectors.Count, Is.EqualTo(2));
            foreach (ExtensionObject item in vectors)
            {
                Assert.That(item.TryGetValue(out ThreeDVector element), Is.True);
                AssertVector(element);
            }
            Assert.That(response.DiagnosticInfos.Count, Is.EqualTo(1));
            AssertNestedDiagnostic(response.DiagnosticInfos[0]);
        }

        private static void AssertPublishResponse(PublishResponse response, string wire)
        {
            Assert.That(response.ResponseHeader.RequestHandle, Is.EqualTo(42));
            Assert.That(response.ResponseHeader.ServiceResult, Is.EqualTo((StatusCode)StatusCodes.Good));
            Assert.That(response.ResponseHeader.StringTable.ToArray(), Is.EqualTo(s_publishStrings));
            Assert.That(response.SubscriptionId, Is.EqualTo(1234));
            Assert.That(response.MoreNotifications, Is.True);
            Assert.That(response.AvailableSequenceNumbers.ToArray(), Is.EqualTo(s_sequenceNumbers));
            Assert.That(response.NotificationMessage.SequenceNumber, Is.EqualTo(123456));
            DateTimeUtc captured = CapturedTime(wire == "Binary"
                ? "2025-04-01T16:03:27.6517200Z"
                : "2025-04-01T16:03:31.8846442Z");
            Assert.That(response.NotificationMessage.PublishTime, Is.EqualTo(captured));
            Assert.That(response.NotificationMessage.NotificationData.Count, Is.EqualTo(1));
            Assert.That(response.NotificationMessage.NotificationData[0]
                .TryGetValue(out DataChangeNotification notification), Is.True);
            Assert.That(notification.MonitoredItems.ToArray().Select(item => item.ClientHandle),
                Is.EqualTo(s_clientHandles));
            Assert.That(notification.MonitoredItems[0].Value.WrappedValue.TryGetValue(out string greeting), Is.True);
            Assert.That(greeting, Is.EqualTo("Hello World"));
            AssertTimestamps(notification.MonitoredItems[0].Value,
                captured + TimeSpan.FromMinutes(1), captured, 10, 100);
            Assert.That(notification.MonitoredItems[1].Value.WrappedValue.TryGetValue(out MatrixOf<uint> matrix),
                Is.True);
            Assert.That(matrix.Dimensions, Is.EqualTo(s_dimensions));
            Assert.That(matrix.Span.ToArray(), Is.EqualTo(s_uintMatrix));
            AssertTimestamps(notification.MonitoredItems[1].Value,
                captured + TimeSpan.FromSeconds(1), captured, 10, 100);
            for (int i = 2; i <= 3; i++)
            {
                Assert.That(notification.MonitoredItems[i].Value.WrappedValue.TryGetValue(out NodeId nodeId), Is.True);
                Assert.That(nodeId, Is.EqualTo(new NodeId(1000)));
            }
            AssertTimestamps(notification.MonitoredItems[2].Value,
                captured + TimeSpan.FromDays(1), captured, 0, 0);
            Assert.That(notification.MonitoredItems[4].Value.WrappedValue.TryGetValue(out bool boolean), Is.True);
            Assert.That(boolean, Is.True);
            Assert.That(notification.MonitoredItems[5].Value.WrappedValue.TryGetValue(out byte small), Is.True);
            Assert.That(small, Is.EqualTo(123));
            Assert.That(notification.MonitoredItems[6].Value.WrappedValue.TryGetValue(out float single), Is.True);
            Assert.That(single, Is.EqualTo(123.123f));
            Assert.That(notification.MonitoredItems[7].Value.WrappedValue.TryGetValue(out double real), Is.True);
            Assert.That(real, Is.EqualTo(12301232.123));
            Assert.That(notification.MonitoredItems[8].Value.WrappedValue.TryGetValue(out long signed), Is.True);
            Assert.That(signed, Is.EqualTo(-123012321234123L));
            Assert.That(notification.MonitoredItems[9].Value.WrappedValue.TryGetValue(out ulong unsigned), Is.True);
            Assert.That(unsigned, Is.EqualTo(123012321234123UL));
            Assert.That(notification.MonitoredItems[10].Value.WrappedValue.TryGetValue(out ArrayOf<uint> array),
                Is.True);
            Assert.That(array.ToArray(), Is.EqualTo(s_uintArray));
            Assert.That(notification.MonitoredItems.ToArray().Select(item => item.Value.StatusCode),
                Is.All.EqualTo((StatusCode)StatusCodes.Good));
            Assert.That(notification.DiagnosticInfos.Count, Is.Zero);
            Assert.That(response.Results.Count, Is.Zero);
            Assert.That(response.DiagnosticInfos.Count, Is.EqualTo(1));
            AssertNestedDiagnostic(response.DiagnosticInfos[0]);
        }

        private static void AssertWriteRequest(WriteRequest request, string wire)
        {
            AssertRequestHeader(request.RequestHeader);
            Assert.That(request.RequestHeader.AdditionalHeader.IsNull, Is.True);
            Assert.That(request.NodesToWrite.ToArray().Select(node => node.NodeId), Is.EqualTo(new[]
            {
                new NodeId(123), new NodeId(124), new NodeId(125), new NodeId(126), new NodeId(127), new NodeId(128),
                new NodeId("s=\"FastCounter\"", 2),
                new NodeId(ByteString.From([0xaa, 0xbb, 0xcc, 0xdd, 0xee]), 3),
                new NodeId(new Guid(wire == "Binary"
                    ? "f3d6a9a8-cb8c-448d-ad1d-1d80237a9736"
                    : "11b4c177-c253-437e-a2bd-20fa3e20edad")),
                new NodeId(132, 3)
            }));
            Assert.That(request.NodesToWrite.ToArray().Select(node => node.AttributeId), Is.EqualTo(s_writeAttributes));
            Assert.That(request.NodesToWrite[0].IndexRange, Is.EqualTo("1:2"));
            Assert.That(request.NodesToWrite[0].Value.WrappedValue.TryGetValue(out string greeting), Is.True);
            Assert.That(greeting, Is.EqualTo("Hello World"));
            AssertTimestamps(request.NodesToWrite[0].Value,
                CapturedTime(wire == "Binary" ? "2025-04-01T16:04:28.0594019Z" : "2025-04-01T16:04:32.6097615Z"),
                CapturedTime(wire == "Binary" ? "2025-04-01T16:03:28.0594012Z" : "2025-04-01T16:03:32.6097611Z"),
                10, 100);
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
            Assert.That(request.NodesToWrite[6].Value.WrappedValue.TryGetValue(out ArrayOf<int> integers), Is.True);
            Assert.That(integers.ToArray(), Is.EqualTo(s_integers));
            Assert.That(request.NodesToWrite[7].Value.WrappedValue.TryGetValue(out ArrayOf<float> singles), Is.True);
            Assert.That(singles.ToArray(), Is.EqualTo(s_singles));
            Assert.That(request.NodesToWrite[8].Value.WrappedValue.TryGetValue(out ArrayOf<double> doubles), Is.True);
            Assert.That(doubles.ToArray(), Is.EqualTo(s_doubles));
            Assert.That(request.NodesToWrite[9].Value.WrappedValue.TryGetValue(out ArrayOf<string> strings), Is.True);
            Assert.That(strings.ToArray(), Is.EqualTo(s_strings));
            Assert.That(request.NodesToWrite.ToArray().Select(node => node.Value.StatusCode),
                Is.All.EqualTo((StatusCode)StatusCodes.Good));
        }

        private static void AssertRequestHeader(RequestHeader header)
        {
            Assert.That(header.RequestHandle, Is.EqualTo(422));
            Assert.That(header.TimeoutHint, Is.EqualTo(10000));
            Assert.That(header.ReturnDiagnostics, Is.EqualTo(1023));
        }

        private static void AssertAdditionalHeader(ExtensionObject header)
        {
            Assert.That(header.TryGetValue(out AdditionalParametersType parameters), Is.True);
            Assert.That(parameters.Parameters.Count, Is.EqualTo(1));
            Assert.That(parameters.Parameters[0].Key, Is.EqualTo(new QualifiedName("traceparent")));
            Assert.That(parameters.Parameters[0].Value.TryGetValue(out string trace), Is.True);
            Assert.That(trace, Is.EqualTo(kTraceParent));
        }

        private static void AssertVector(ThreeDVector vector)
        {
            Assert.That(vector.X, Is.EqualTo(1));
            Assert.That(vector.Y, Is.EqualTo(-1));
            Assert.That(vector.Z, Is.Zero);
        }

        private static void AssertNestedDiagnostic(DiagnosticInfo diagnostic)
        {
            AssertDiagnostics(diagnostic, s_nestedDiagnostics, "Hello World");
        }

        private static void AssertDiagnostics(DiagnosticInfo diagnostic, StatusCode[] statuses, string firstMessage)
        {
            for (int i = 0; i < statuses.Length; i++)
            {
                Assert.That(diagnostic, Is.Not.Null);
                Assert.That(diagnostic.SymbolicId, Is.EqualTo(-1));
                Assert.That(diagnostic.NamespaceUri, Is.EqualTo(-1));
                Assert.That(diagnostic.Locale, Is.EqualTo(-1));
                Assert.That(diagnostic.LocalizedText, Is.EqualTo(-1));
                Assert.That(diagnostic.AdditionalInfo, Is.EqualTo(i == 0 ? firstMessage : "Hello World"));
                Assert.That(diagnostic.InnerStatusCode, Is.EqualTo(statuses[i]));
                diagnostic = diagnostic.InnerDiagnosticInfo;
            }
            Assert.That(diagnostic, Is.Null);
        }

        private static void AssertTimestamps(
            in DataValue value,
            DateTimeUtc source,
            DateTimeUtc server,
            ushort sourcePicoseconds,
            ushort serverPicoseconds)
        {
            Assert.That(value.SourceTimestamp, Is.EqualTo(source));
            Assert.That(value.ServerTimestamp, Is.EqualTo(server));
            Assert.That(value.SourcePicoseconds, Is.EqualTo(sourcePicoseconds));
            Assert.That(value.ServerPicoseconds, Is.EqualTo(serverPicoseconds));
        }

        private static DateTimeUtc CapturedTime(string timestamp)
        {
            return DateTimeUtc.Parse(timestamp, CultureInfo.InvariantCulture);
        }

        private static void AssertWireGenerations(IEncodeable source, string wire, string messageName)
        {
            // Canonical OUTPUT is compared with later generations, never substituted for the original input.
            byte[] encoded = EncodeDecodedMessage(source, wire);
            IEncodeable second = DecodeOriginal(encoded, wire);
            AssertOriginalMessage(second, wire, messageName);
            Assert.That(Utils.IsEqual(source, second), Is.True, "First-generation encoding must preserve the source.");
            byte[] canonical = EncodeDecodedMessage(second, wire);
            Assert.That(canonical, Is.EqualTo(encoded));
            IEncodeable third = DecodeOriginal(canonical, wire);
            AssertOriginalMessage(third, wire, messageName);
            Assert.That(Utils.IsEqual(second, third), Is.True);
        }

        private static byte[] EncodeDecodedMessage(IEncodeable message, string wire)
        {
            if (wire == "Binary")
            {
                return BinaryEncoder.EncodeMessage(message, FuzzableCode.MessageContext);
            }
            using var encoder = new XmlEncoder(FuzzableCode.MessageContext);
            encoder.EncodeMessage(message, message.TypeId);
            return Encoding.UTF8.GetBytes(encoder.CloseAndReturnText());
        }

        private static void AssertJsonGenerations(IEncodeable source, string wire, string messageName, string mode)
        {
            JsonEncoderOptions options = FuzzableCode.JsonEncodingModes.ToArray().Single(option => option.Name == mode);
            FuzzableCode.FuzzJsonRoundTripCore(source, options);
            string encoded = FuzzableCode.EncodeJsonMessage(source, options);
            IEncodeable second = FuzzableCode.DecodeJsonWithMetadata(
                encoded, source, options, FuzzableCode.MessageContext);
            AssertOriginalMessage(second, wire, messageName);
            Assert.That(Utils.IsEqual(source, second), Is.True, mode);
            string canonical = FuzzableCode.EncodeJsonMessage(second, options);
            Assert.That(canonical, Is.EqualTo(encoded), mode);
            IEncodeable third = FuzzableCode.DecodeJsonWithMetadata(
                canonical, source, options, FuzzableCode.MessageContext);
            AssertOriginalMessage(third, wire, messageName);
            Assert.That(Utils.IsEqual(second, third), Is.True, mode);

            // Challenge the existing oracles on derived output, not on translated or mutated corpus input.
            Assert.That(
                () => FuzzableCode.FuzzJsonEncoderIndempotentCore(encoded + " ", source, options),
                Throws.TypeOf<EncodingFidelityException>()
                    .With.Message.EqualTo($"Idempotent JSON encoding failed. Type={messageName}."));
            switch (third)
            {
                case ReadRequest read:
                    read.RequestHeader.RequestHandle++;
                    break;
                case ReadResponse read:
                    read.ResponseHeader.RequestHandle++;
                    break;
                case PublishResponse publish:
                    publish.ResponseHeader.RequestHandle++;
                    break;
                case WriteRequest write:
                    write.RequestHeader.RequestHandle++;
                    break;
            }
            Assert.That(Utils.IsEqual(source, third), Is.False);
            Assert.That(
                () => FuzzableCode.FuzzJsonEncoderIndempotentCore(encoded, third, options),
                Throws.TypeOf<EncodingFidelityException>()
                    .With.Message.EqualTo($"JSON semantic round-trip failed. Type={messageName}, Mode={mode}."));
            AssertOriginalMessage(source, wire, messageName);
        }

        private static void AssertLegacyBody(JsonElement body, string messageName)
        {
            bool request = messageName is nameof(ReadRequest) or nameof(WriteRequest);
            JsonElement header = body.GetProperty(request ? "RequestHeader" : "ResponseHeader");
            Assert.That(header.GetProperty("RequestHandle").GetUInt32(), Is.EqualTo(request ? 422 : 42));
            Assert.That(header.GetProperty("Timestamp").GetString(),
                Is.EqualTo(s_originals.Single(item => item.Wire == "Json" && item.Message == messageName).Timestamp));
            JsonElement variant;
            switch (messageName)
            {
                case nameof(ReadRequest):
                    Assert.That(body.GetProperty("NodesToRead").GetArrayLength(), Is.EqualTo(6));
                    Assert.That(body.GetProperty("NodesToRead")[0].GetProperty("NodeId").GetProperty("Id").GetInt32(),
                        Is.EqualTo(123));
                    variant = header.GetProperty("AdditionalHeader").GetProperty("Body")
                        .GetProperty("Parameters")[0].GetProperty("Value");
                    break;
                case nameof(ReadResponse):
                    Assert.That(body.GetProperty("Results").GetArrayLength(), Is.EqualTo(11));
                    variant = body.GetProperty("Results")[0].GetProperty("Value");
                    break;
                case nameof(PublishResponse):
                    JsonElement notification = body.GetProperty("NotificationMessage");
                    Assert.That(notification.GetProperty("SequenceNumber").GetUInt32(), Is.EqualTo(123456));
                    JsonElement items = notification.GetProperty("NotificationData")[0]
                        .GetProperty("Body").GetProperty("MonitoredItems");
                    Assert.That(items.GetArrayLength(), Is.EqualTo(11));
                    variant = items[0].GetProperty("Value").GetProperty("Value");
                    break;
                case nameof(WriteRequest):
                    Assert.That(body.GetProperty("NodesToWrite").GetArrayLength(), Is.EqualTo(10));
                    Assert.That(body.GetProperty("NodesToWrite")[0].GetProperty("NodeId").GetProperty("Id").GetInt32(),
                        Is.EqualTo(123));
                    variant = body.GetProperty("NodesToWrite")[0].GetProperty("Value").GetProperty("Value");
                    break;
                default:
                    throw new ArgumentException("Unknown original message.", nameof(messageName));
            }
            Assert.That(variant.GetProperty("Type").GetInt32(), Is.EqualTo(12));
            Assert.That(variant.GetProperty("Body").GetString(),
                Is.EqualTo(messageName == nameof(ReadRequest) ? kTraceParent : "Hello World"));
            Assert.That(variant.TryGetProperty("UaType", out _), Is.False);
        }

        private const string kTraceParent = "00-480e22a2781fe54d992d878662248d94-b4b37b64bb3f6141-00";
        private static readonly string[] s_legacyMatrixFields = ["Elements", "Dimensions"];
        private static readonly string[] s_legacyEnvelope = ["TypeId", "Body"];
        private static readonly int[] s_dimensions = [2, 2, 2];
        private static readonly uint[] s_readAttributes = [25, 5, 13, 4, 17, 24];
        private static readonly uint[] s_writeAttributes = [13, 15, 13, 13, 13, 13, 13, 13, 13, 13];
        private static readonly string[] s_readStrings = ["Hello", "World", "Goodbye"];
        private static readonly string[] s_publishStrings = ["No error occurred"];
        private static readonly byte[] s_byteMatrix = [1, 2, 3, 4, 11, 22, 33, 44];
        private static readonly uint[] s_uintMatrix = [1, 2, 3, 4, 11, 22, 33, 44];
        private static readonly uint[] s_sequenceNumbers = [1, 2, 3, 4];
        private static readonly uint[] s_clientHandles = [122, 123, 124, 125, 125, 126, 127, 128, 129, 130, 131];
        private static readonly uint[] s_uintArray = [1, 2, 3, 4, 5, 6, 7, 8, 9, 0];
        private static readonly int[] s_integers = [1, 2, 3, 4, 5];
        private static readonly float[] s_singles = [1.1f, 2.2f, 3.3f];
        private static readonly double[] s_doubles = [1.1, 2.2, 3.3];
        private static readonly string[] s_strings = ["one", "two", "three"];
        private static readonly StatusCode[] s_serviceDiagnostics =
        [
            StatusCodes.BadAggregateConfigurationRejected, StatusCodes.BadIndexRangeInvalid,
            StatusCodes.BadSecureChannelIdInvalid, StatusCodes.BadAlreadyExists
        ];
        private static readonly StatusCode[] s_nestedDiagnostics =
            [StatusCodes.BadCertificateHostNameInvalid, StatusCodes.BadNodeIdUnknown];
        private static readonly string[] s_jsonModes =
            ["Verbose", "Compact", "RawData", "LegacyReversible", "LegacyNonReversible"];

        // SHA-256 and lengths were captured from the fork's Testcases.{Binary,Json,Xml} files.
        // Dates and GUIDs deliberately differ between encodings; no generator is consulted.
        private static readonly (string Wire, string Message, int Length, string Sha256, string Timestamp)[]
            s_originals =
        [
            ("Binary", nameof(ReadRequest), 286,
                "E3784C1601B5D64BB409677D0B9F505F85BB44F581C05C87AE412BE28461E5BC", "2025-04-01T16:03:21.1443128Z"),
            ("Binary", nameof(ReadResponse), 650,
                "9A47DB38B190C0ABBB9D8A4EB6060C170BABE77728A51B6CF80FDBA8DC220BDB", "2025-04-01T16:03:27.2566961Z"),
            ("Binary", nameof(PublishResponse), 466,
                "D928EBAA3D115A1A972D5975EBCD7C081A432E5ED32C9AA82DC42EEFEA913F04", "2025-04-01T16:03:27.6557070Z"),
            ("Binary", nameof(WriteRequest), 509,
                "A8187E9FDEEB0E17AFFBFF3456E911BC39A19971B163A735D197330CAA0D3584", "2025-04-01T16:03:28.0588252Z"),
            ("Json", nameof(ReadRequest), 776,
                "E2FAEC5C2DC442E4866C7112E828017DD3790ED25C8D8854531F7A717F132EDF", "2025-04-01T16:03:28.5765948Z"),
            ("Json", nameof(ReadResponse), 2367,
                "CC74A0EA870ACB2DC899DECBA868E0920CC18E02552B7FB7B4F2066A87E66441", "2025-04-01T16:03:28.9988675Z"),
            ("Json", nameof(PublishResponse), 2042,
                "12D98201BDEA7BD466F14907C9EEAD8773ABA3753D1C439CE6BC7B611CE51A21", "2025-04-01T16:03:29.528844Z"),
            ("Json", nameof(WriteRequest), 2219,
                "2B9D9F9E2A9394C5A08BB2F97567C612E781FCCE37336A43E6C95D825465C31E", "2025-04-01T16:03:29.8977872Z"),
            ("Xml", nameof(ReadRequest), 2073,
                "314464A7F63DB0DC931F387EA0038A53FAE0942DCD4C495CC22FD6A84F1671F7", "2025-04-01T16:03:30.2701254Z"),
            ("Xml", nameof(ReadResponse), 9855,
                "FA57DE52547618BED1F4E663EF6DE6D81948E33A3FB291CDFFDA255379E0A79F", "2025-04-01T16:03:31.1134096Z"),
            ("Xml", nameof(PublishResponse), 12947,
                "BF30ED99E2E2FBAEEC9F4C86357F94F45576DF4C7BF287AF6E566520E6129190", "2025-04-01T16:03:31.884721Z"),
            ("Xml", nameof(WriteRequest), 7073,
                "D81D9AD7B68E3CAE5648D81E0E6E20213358EB3198B1D61A0A080A19E460C185", "2025-04-01T16:03:32.6097561Z")
        ];
    }
}
