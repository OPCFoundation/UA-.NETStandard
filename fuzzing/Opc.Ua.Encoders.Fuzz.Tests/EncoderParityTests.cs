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
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;
using NUnit.Framework;
using Opc.Ua.Bindings;

namespace Opc.Ua.Fuzzing
{
    /// <summary>
    /// Regression coverage for segmented decoding and the JSON conversion callback families.
    /// </summary>
    [TestFixture]
    [Category("Fuzzing")]
    public sealed class EncoderParityTests
    {
        [TestCaseSource(nameof(PositiveCallbackCases))]
        public void SpanCallbacksReplayPopulatedRichMessages(string callbackName, string messageName)
        {
            CallbackCase target = FindCallback(callbackName);
            IEncodeable original = EncoderTestMessages.Create(messageName);
            EncoderTestMessages.AssertPopulated(original);
            ByteString input = EncoderTestMessages.Encode(original, target.Wire);
            IEncodeable decoded = EncoderTestMessages.Decode(input.Span, target.Wire);
            EncoderTestMessages.AssertPopulated(decoded);
            Assert.That(Utils.IsEqual(original, decoded), Is.True);

            target.Span(input.Span);

            Assert.That(EncoderTestMessages.Encode(original, target.Wire), Is.EqualTo(input));
        }

        [TestCaseSource(nameof(PositiveCallbackCases))]
        public void AflCallbacksReplayTheSamePopulatedRichMessages(string callbackName, string messageName)
        {
            CallbackCase target = FindCallback(callbackName);
            IEncodeable original = EncoderTestMessages.Create(messageName);
            EncoderTestMessages.AssertPopulated(original);
            ByteString input = EncoderTestMessages.Encode(original, target.Wire);
            IEncodeable decoded = EncoderTestMessages.Decode(input.Span, target.Wire);
            EncoderTestMessages.AssertPopulated(decoded);
            Assert.That(Utils.IsEqual(original, decoded), Is.True);

            ReplayAfl(target, input, true);

            Assert.That(EncoderTestMessages.Encode(original, target.Wire), Is.EqualTo(input));
        }

        [TestCaseSource(nameof(TruncatedCallbackCases))]
        public void SpanCallbacksRejectTruncatedSourceRatherThanExecutingASuccessPath(
            string callbackName,
            bool truncateLastByte)
        {
            CallbackCase target = FindCallback(callbackName);
            ByteString input = TruncatedReadRequest(target.Wire, truncateLastByte);
            AssertMalformedSource(input, target.Wire);

            target.Span(input.Span);

            Assert.That(EncoderTestMessages.Decode(input.Span, target.Wire, false), Is.Null);
        }

        [TestCaseSource(nameof(TruncatedCallbackCases))]
        public void AflCallbacksRejectTheSameTruncatedSource(string callbackName, bool truncateLastByte)
        {
            CallbackCase target = FindCallback(callbackName);
            ByteString input = TruncatedReadRequest(target.Wire, truncateLastByte);
            AssertMalformedSource(input, target.Wire);

            ReplayAfl(target, input, false);

            Assert.That(EncoderTestMessages.Decode(input.Span, target.Wire, false), Is.Null);
        }

        [TestCaseSource(nameof(SegmentBoundaryCases))]
        public void SegmentedStreamsPreserveValuesAndCanonicalBytesAcrossBoundaries(string messageName, int segmentSize)
        {
            IEncodeable original = EncoderTestMessages.Create(messageName);
            ByteString input = EncoderTestMessages.Encode(original, "Binary");
            using MemoryStream stream = FuzzableCode.PrepareArraySegmentStream(input.Span, segmentSize);

            Assert.That(stream, Is.TypeOf<ArraySegmentStream>());
            Assert.That(stream.CanSeek, Is.True);
            Assert.That(stream.Length, Is.EqualTo(input.Length));
            Assert.That(stream.Position, Is.Zero);
            AssertSegmentLayout(input, segmentSize);

            IEncodeable decoded = FuzzableCode.FuzzBinaryDecoderCore(stream, true);
            EncoderTestMessages.AssertPopulated(decoded);
            Assert.That(Utils.IsEqual(original, decoded), Is.True);
            ByteString canonical = EncoderTestMessages.Encode(decoded, "Binary");
            Assert.That(canonical, Is.EqualTo(input));
            FuzzableCode.FuzzBinaryEncoderIndempotentCore(canonical.ToArray(), original, true);

            using MemoryStream truncated = FuzzableCode.PrepareArraySegmentStream(input.Span[..^1], segmentSize);
            Assert.That(FuzzableCode.FuzzBinaryDecoderCore(truncated), Is.Null);
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void SegmentedStreamsRejectNonPositiveSegmentSizes(int segmentSize)
        {
            ByteString input = EncoderTestMessages.Encode(Testcases.CreateRichReadRequest(), "Binary");

            Assert.That(
                () => FuzzableCode.PrepareArraySegmentStream(input.Span, segmentSize),
                Throws.TypeOf<ArgumentOutOfRangeException>().With.Property("ParamName").EqualTo("segmentSize"));
            Assert.That(input.Length, Is.GreaterThan(64));
        }

        [Test]
        public void EmptySegmentedInputHasOneEmptyBufferAndAnEndOfStream()
        {
            using MemoryStream stream = FuzzableCode.PrepareArraySegmentStream(ReadOnlySpan<byte>.Empty, 1);

            Assert.That(stream, Is.TypeOf<ArraySegmentStream>());
            Assert.That(stream.Length, Is.Zero);
            Assert.That(stream.Position, Is.Zero);
            Assert.That(stream.ReadByte(), Is.EqualTo(-1));
            BufferCollection buffers = ((ArraySegmentStream)stream).GetBuffers(nameof(EncoderParityTests));
            Assert.That(buffers.Count, Is.EqualTo(1));
            Assert.That(buffers[0].Count, Is.Zero);
        }

        [Test]
        public void SegmentedCallbacksPreserveACompleteMessageEndingAtABufferBoundary()
        {
            ReadRequest original = Testcases.CreateRichReadRequest();
            ByteString initial = EncoderTestMessages.Encode(original, "Binary");
            original.RequestHeader.AuditEntryId = new string('a', 64 - initial.Length % 64);
            ByteString input = EncoderTestMessages.Encode(original, "Binary");

            Assert.That(input.Length % 64, Is.Zero);
            Assert.That(input.Length, Is.GreaterThan(64));
            foreach (CallbackCase callback in s_callbacks.Where(
                callback => callback.Span.Method.Name.EndsWith("Segmented", StringComparison.Ordinal)))
            {
                callback.Span(input.Span);
                ReplayAfl(callback, input, true);
            }
            using MemoryStream stream = FuzzableCode.PrepareArraySegmentStream(input.Span);
            IEncodeable decoded = FuzzableCode.FuzzBinaryDecoderCore(stream, true);
            Assert.That(Utils.IsEqual(original, decoded), Is.True);
            Assert.That(EncoderTestMessages.Encode(decoded, "Binary"), Is.EqualTo(input));
        }

        [Test]
        public void CallbackDiscoveryIncludesAllNewPairsWithoutDuplicateSegmentedAflTargets()
        {
            string[] spans = FuzzMethods.FindFuzzMethods(typeof(FuzzMethods.LibFuzzSpan))
                .Select(callback => callback.Method.Name)
                .ToArray();
            string[] afl = FuzzMethods.FindFuzzMethods(typeof(FuzzMethods.AflFuzzStream))
                .Concat(FuzzMethods.FindFuzzMethods(typeof(FuzzMethods.AflFuzzString)))
                .Select(callback => callback.Method.Name)
                .ToArray();

            Assert.That(s_callbacks.Length, Is.EqualTo(16));
            Assert.That(s_callbacks.Select(callback => callback.Span.Method.Name).Distinct().Count(), Is.EqualTo(16));
            Assert.That(s_callbacks.Count(callback => !callback.Span.Method.Name.EndsWith(
                "Segmented",
                StringComparison.Ordinal)), Is.EqualTo(13));
            foreach (CallbackCase target in s_callbacks)
            {
                Assert.That(spans.Count(name => name == target.Span.Method.Name), Is.EqualTo(1));
                Assert.That(afl.Count(name => name == target.Afl.Method.Name), Is.EqualTo(1));
            }
            Assert.That(afl, Does.Not.Contain("AflfuzzBinaryDecoderSegmented"));
            Assert.That(afl, Does.Not.Contain("AflfuzzBinaryEncoderSegmented"));
            Assert.That(afl, Does.Not.Contain("AflfuzzBinaryEncoderIndempotentSegmented"));
            Assert.That(spans.Concat(afl).All(name => name.StartsWith("Libfuzz", StringComparison.Ordinal) ||
                name.StartsWith("Aflfuzz", StringComparison.Ordinal)), Is.True);
        }

        [Test]
        public void MessageContextsHaveDeterministicIndependentNamespaceAndServerTables()
        {
            ServiceMessageContext first = FuzzableCode.CreateMessageContext();
            ServiceMessageContext second = FuzzableCode.CreateMessageContext();

            Assert.That(first, Is.Not.SameAs(second));
            Assert.That(first.NamespaceUris, Is.Not.SameAs(second.NamespaceUris));
            Assert.That(first.ServerUris, Is.Not.SameAs(second.ServerUris));
            Assert.That(first.NamespaceUris.ToArray(), Is.EqualTo(s_namespaceUris));
            Assert.That(second.NamespaceUris.ToArray(), Is.EqualTo(s_namespaceUris));
            Assert.That(first.ServerUris.GetIndex("urn:opcfoundation:fuzzing:server"), Is.Zero);
            Assert.That(second.ServerUris.Count, Is.EqualTo(1));

            first.NamespaceUris.Append("urn:encoder-test:private");
            first.ServerUris.Append("urn:encoder-test:private-server");

            Assert.That(first.NamespaceUris.Count, Is.EqualTo(6));
            Assert.That(first.ServerUris.Count, Is.EqualTo(2));
            Assert.That(second.NamespaceUris.ToArray(), Is.EqualTo(s_namespaceUris));
            Assert.That(second.ServerUris.Count, Is.EqualTo(1));
            Assert.That(FuzzableCode.MessageContext.NamespaceUris.ToArray(), Is.EqualTo(s_namespaceUris));
            Assert.That(FuzzableCode.MessageContext.ServerUris.Count, Is.EqualTo(1));
        }

        [Test]
        public void StandardMessageDecoderResolvesTheXmlWrapperAndRegisteredModelQualifiedName()
        {
            ReadRequest message = Testcases.CreateRichReadRequest();
            Assert.That(
                FuzzableCode.MessageContext.Factory.TryGetEncodeableType(message.TypeId, out IEncodeableType type),
                Is.True);
            var name = new XmlQualifiedName(nameof(ReadRequest), Namespaces.OpcUa);
            Assert.That(type.XmlName, Is.EqualTo(name));
            Assert.That(FuzzableCode.MessageContext.Factory.TryGetType(name, out IType resolved), Is.True);
            Assert.That(resolved, Is.SameAs(type));
            ByteString serialized = EncoderTestMessages.Encode(message, "Xml");
            IEncodeable decoded = EncoderTestMessages.Decode(serialized.Span, "Xml");
            Assert.That(Utils.IsEqual(message, decoded), Is.True);
        }

        [TestCaseSource(nameof(JsonModeCases))]
        public void JsonModesPreserveZeroDiagnosticStringTableIndices(string modeName)
        {
            JsonEncoderOptions options = FindMode(modeName);
            ReadResponse message = Testcases.CreateRichReadResponse();
            message.ResponseHeader.ServiceDiagnostics = new DiagnosticInfo
            {
                SymbolicId = 0,
                NamespaceUri = 0,
                Locale = 0,
                LocalizedText = 0
            };
            string serialized = FuzzableCode.EncodeJsonMessage(message, options);
            using JsonDocument document = JsonDocument.Parse(serialized);
            JsonElement diagnostic = document.RootElement
                .GetProperty("UaBody").GetProperty("ResponseHeader").GetProperty("ServiceDiagnostics");

            Assert.That(diagnostic.GetProperty("SymbolicId").GetInt32(), Is.Zero);
            Assert.That(diagnostic.GetProperty("NamespaceUri").GetInt32(), Is.Zero);
            Assert.That(diagnostic.GetProperty("Locale").GetInt32(), Is.Zero);
            Assert.That(diagnostic.GetProperty("LocalizedText").GetInt32(), Is.Zero);
            FuzzableCode.FuzzJsonRoundTripCore(message, options);
        }

        [Test]
        public void JsonModeCatalogMapsSupportedLegacyOptionsWithoutRetiredWireShapes()
        {
            ArrayOf<JsonEncoderOptions> modes = FuzzableCode.JsonEncodingModes;

            Assert.That(modes.IsNull, Is.False);
            Assert.That(modes.Count, Is.EqualTo(5));
            Assert.That(modes.ToArray().Select(mode => mode.Name), Is.EqualTo(s_modeNames));
            Assert.That(modes[0], Is.EqualTo(JsonEncoderOptions.Verbose));
            Assert.That(modes[1], Is.EqualTo(JsonEncoderOptions.Compact));
            Assert.That(modes[2], Is.EqualTo(JsonEncoderOptions.RawData));
            Assert.That(modes[3], Is.EqualTo(JsonEncoderOptions.Compact with
            {
                Name = "LegacyReversible",
                IgnoreDefaultValues = false,
                ForceNamespaceUri = false
            }));
            Assert.That(modes[4], Is.EqualTo(JsonEncoderOptions.RawData with { Name = "LegacyNonReversible" }));
        }

        [TestCaseSource(nameof(RichJsonModeCases))]
        public void JsonRoundTripCorePreservesRichTypedValuesAndCanonicalText(string messageName, string modeName)
        {
            IEncodeable original = EncoderTestMessages.Create(messageName);
            JsonEncoderOptions options = FindMode(modeName);
            EncoderTestMessages.AssertPopulated(original);

            FuzzableCode.FuzzJsonRoundTripCore(original, options);

            string serialized = FuzzableCode.EncodeJsonMessage(original, options);
            IEncodeable decoded = FuzzableCode.DecodeJsonWithMetadata(
                serialized,
                original,
                options,
                FuzzableCode.MessageContext);
            EncoderTestMessages.AssertPopulated(decoded);
            Assert.That(Utils.IsEqual(original, decoded), Is.True);
            Assert.That(FuzzableCode.EncodeJsonMessage(decoded, options), Is.EqualTo(serialized));
        }

        [TestCaseSource(nameof(JsonModeCases))]
        public void JsonModesEncodeNamespaceOneEnumsAndDefaultValuesAsConfigured(string modeName)
        {
            JsonEncoderOptions options = FindMode(modeName);
            ReadRequest request = Testcases.CreateRichReadRequest();
            request.MaxAge = 0;
            string serialized = FuzzableCode.EncodeJsonMessage(request, options);
            using JsonDocument document = JsonDocument.Parse(serialized);
            JsonElement body = document.RootElement.GetProperty("UaBody");
            JsonElement timestampMode = body.GetProperty("TimestampsToReturn");

            // The message envelope retains its type id; RawData suppresses artifacts inside UaBody.
            Assert.That(document.RootElement.GetProperty("UaTypeId").GetString(), Is.EqualTo("i=629"));
            JsonElement header = body.GetProperty("RequestHeader").GetProperty("AdditionalHeader");
            Assert.That(header.TryGetProperty("UaTypeId", out _), Is.EqualTo(!options.SuppressArtifacts));
            // A structure's abstract Variant still carries its type; retired unwrapped Variants are not emitted.
            Assert.That(
                header.GetProperty("Parameters")[0].GetProperty("Value").GetProperty("UaType").GetInt32(),
                Is.EqualTo((int)BuiltInType.String));
            Assert.That(
                body.TryGetProperty("MaxAge", out JsonElement maxAge),
                Is.EqualTo(!options.IgnoreDefaultValues));
            if (!options.IgnoreDefaultValues)
            {
                Assert.That(maxAge.GetDouble(), Is.Zero);
            }
            if (options.EnumerationAsNumber)
            {
                Assert.That(timestampMode.ValueKind, Is.EqualTo(JsonValueKind.Number));
                Assert.That(timestampMode.GetInt32(), Is.EqualTo((int)TimestampsToReturn.Source));
            }
            else
            {
                Assert.That(timestampMode.ValueKind, Is.EqualTo(JsonValueKind.String));
                Assert.That(timestampMode.GetString(), Is.EqualTo("Source_0"));
            }
            string prefix = options.ForceNamespaceUri ? "nsu=urn:opcfoundation:fuzzing:application;" : "ns=1;";
            Assert.That(
                body.GetProperty("NodesToRead")[3].GetProperty("NodeId").GetString(),
                Is.EqualTo(prefix + "g=00112233-4455-6677-8899-aabbccddeeff"));
            FuzzableCode.FuzzJsonRoundTripCore(request, options);
        }

        [TestCaseSource(nameof(JsonModeCases))]
        public void StatusCodeSymbolsFollowTheSelectedJsonModeOptions(string modeName)
        {
            JsonEncoderOptions options = FindMode(modeName);
            var response = new ReadResponse
            {
                Results = [new DataValue(Variant.From("status payload"), StatusCodes.BadDataLost)]
            };
            string serialized = FuzzableCode.EncodeJsonMessage(response, options);
            using JsonDocument document = JsonDocument.Parse(serialized);
            JsonElement value = document.RootElement.GetProperty("UaBody").GetProperty("Results")[0];
            JsonElement status = value.GetProperty("Status");

            Assert.That(value.GetProperty("Value").GetString(), Is.EqualTo("status payload"));
            Assert.That(status.ValueKind, Is.EqualTo(JsonValueKind.Object));
            Assert.That(status.GetProperty("Code").GetUInt32(), Is.EqualTo(StatusCodes.BadDataLost));
            Assert.That(
                status.TryGetProperty("Symbol", out JsonElement symbol),
                Is.EqualTo(!options.OmitStatusCodeSymbol && !options.SuppressArtifacts));
            if (!options.OmitStatusCodeSymbol && !options.SuppressArtifacts)
            {
                Assert.That(symbol.GetString(), Is.EqualTo("BadDataLost"));
            }
        }

        [TestCase("RawData")]
        [TestCase("LegacyNonReversible")]
        public void RawDataRequiresTypeMetadataAndRestoresPopulatedValues(string modeName)
        {
            JsonEncoderOptions options = FindMode(modeName);
            WriteRequest original = Testcases.CreateRichWriteRequest();
            string serialized = FuzzableCode.EncodeJsonMessage(original, options);
            string metadata = FuzzableCode.EncodeJsonMessage(original, options with { SuppressArtifacts = false });
            using JsonDocument raw = JsonDocument.Parse(serialized);
            using JsonDocument schema = JsonDocument.Parse(metadata);

            Assert.That(raw.RootElement.GetProperty("UaTypeId").GetString(), Is.EqualTo("i=671"));
            Assert.That(CountTypeArtifacts(raw.RootElement.GetProperty("UaBody")), Is.Zero);
            Assert.That(CountTypeArtifacts(schema.RootElement.GetProperty("UaBody")), Is.GreaterThanOrEqualTo(10));
            Assert.That(
                raw.RootElement.GetProperty("UaBody").GetProperty("NodesToWrite").GetArrayLength(),
                Is.EqualTo(10));

            string restored = FuzzableCode.RestoreJsonArtifacts(serialized, metadata, FuzzableCode.MessageContext);
            using JsonDocument result = JsonDocument.Parse(restored);
            Assert.That(CountTypeArtifacts(result.RootElement), Is.EqualTo(CountTypeArtifacts(schema.RootElement)));
            IEncodeable decoded = FuzzableCode.DecodeJsonWithMetadata(
                serialized,
                original,
                options,
                FuzzableCode.MessageContext);
            EncoderTestMessages.AssertPopulated(decoded);
            Assert.That(Utils.IsEqual(original, decoded), Is.True);
            FuzzableCode.FuzzJsonEncoderIndempotentCore(serialized, original, options);

            JsonObject withoutEnvelopeType = JsonNode.Parse(serialized).AsObject();
            Assert.That(withoutEnvelopeType.Remove("UaTypeId"), Is.True);
            IEncodeable restoredEnvelope = FuzzableCode.DecodeJsonWithMetadata(
                withoutEnvelopeType.ToJsonString(),
                original,
                options,
                FuzzableCode.MessageContext);
            EncoderTestMessages.AssertPopulated(restoredEnvelope);
            Assert.That(Utils.IsEqual(original, restoredEnvelope), Is.True);
        }

        [Test]
        public void MetadataRestorationAddsOnlyMissingArtifactsAndDoesNotRepairPayloadScalars()
        {
            const string serialized =
                """{"UaBody":{"Results":[{"Value":"changed"},{"Value":99}],"Sequence":8}}""";

            string restored = FuzzableCode.RestoreJsonArtifacts(
                serialized,
                k_artifactSchema,
                FuzzableCode.MessageContext);

            using JsonDocument result = JsonDocument.Parse(restored);
            JsonElement body = result.RootElement.GetProperty("UaBody");
            JsonElement values = body.GetProperty("Results");
            Assert.That(result.RootElement.GetProperty("UaTypeId").GetString(), Is.EqualTo("i=634"));
            Assert.That(values.GetArrayLength(), Is.EqualTo(2));
            Assert.That(values[0].GetProperty("UaType").GetInt32(), Is.EqualTo(12));
            Assert.That(values[0].GetProperty("Value").GetString(), Is.EqualTo("changed"));
            Assert.That(values[1].GetProperty("UaType").GetInt32(), Is.EqualTo(7));
            Assert.That(values[1].GetProperty("Value").GetInt32(), Is.EqualTo(99));
            Assert.That(body.GetProperty("Sequence").GetInt32(), Is.EqualTo(8));
            Assert.That(CountTypeArtifacts(result.RootElement), Is.EqualTo(3));
        }

        [Test]
        public void MetadataRestorationDoesNotOverwriteExistingIncorrectArtifacts()
        {
            const string serialized = """
                {"UaTypeId":"i=999",
                 "UaBody":{"Results":[{"UaType":6,"Value":"changed"},{"Value":99}],"Sequence":8}}
                """;

            string restored = FuzzableCode.RestoreJsonArtifacts(
                serialized,
                k_artifactSchema,
                FuzzableCode.MessageContext);

            using JsonDocument result = JsonDocument.Parse(restored);
            JsonElement values = result.RootElement.GetProperty("UaBody").GetProperty("Results");
            Assert.That(result.RootElement.GetProperty("UaTypeId").GetString(), Is.EqualTo("i=999"));
            Assert.That(values[0].GetProperty("UaType").GetInt32(), Is.EqualTo(6));
            Assert.That(values[0].GetProperty("Value").GetString(), Is.EqualTo("changed"));
            Assert.That(values[1].GetProperty("UaType").GetInt32(), Is.EqualTo(7));
        }

        [TestCase("""{"UaBody":[]}""", "payload shape")]
        [TestCase("""{"UaBody":{"Results":{},"Sequence":7}}""", "payload shape")]
        [TestCase("""{"UaBody":{"Results":[{"Value":null},{"Value":42}],"Sequence":7}}""", "payload shape")]
        [TestCase("""{"UaBody":{"Results":[{"Value":"original"},{"Value":42}]}}""", "lost field 'Sequence'")]
        [TestCase("""{"UaBody":{"Results":[{},{"Value":42}],"Sequence":7}}""", "lost field 'Value'")]
        [TestCase("""{"UaBody":{"Results":[{"Value":"original"}],"Sequence":7}}""", "array length")]
        [TestCase("""{"UaBody":{"Results":[{"Value":"original"},{"Value":42},{}],"Sequence":7}}""", "array length")]
        [TestCase(
            """{"UaBody":{"Results":[{"Value":"original","Unexpected":1},{"Value":42}],"Sequence":7}}""",
            "Unexpected RawData JSON field 'Unexpected'")]
        public void MetadataRestorationRejectsStructuralPayloadCorruption(string serialized, string failure)
        {
            Assert.That(
                () => FuzzableCode.RestoreJsonArtifacts(serialized, k_artifactSchema, FuzzableCode.MessageContext),
                Throws.TypeOf<InvalidOperationException>().With.Message.Contains(failure));
        }

        [TestCaseSource(nameof(JsonModeCases))]
        public void StrictJsonOracleDetectsSemanticCorruptionInsteadOfRepairingIt(string modeName)
        {
            JsonEncoderOptions options = FindMode(modeName);
            ReadRequest original = Testcases.CreateRichReadRequest();
            JsonNode payload = JsonNode.Parse(FuzzableCode.EncodeJsonMessage(original, options));
            payload["UaBody"]["MaxAge"] = 1001;
            string corrupted = payload.ToJsonString();

            IEncodeable decoded = FuzzableCode.DecodeJsonWithMetadata(
                corrupted,
                original,
                options,
                FuzzableCode.MessageContext);
            Assert.That(decoded, Is.TypeOf<ReadRequest>());
            Assert.That(((ReadRequest)decoded).MaxAge, Is.EqualTo(1001));
            Assert.That(((ReadRequest)decoded).NodesToRead.Count, Is.EqualTo(6));
            Assert.That(original.MaxAge, Is.EqualTo(1000));
            Assert.That(Utils.IsEqual(original, decoded), Is.False);
            Assert.That(
                () => FuzzableCode.FuzzJsonEncoderIndempotentCore(corrupted, original, options),
                Throws.TypeOf<InvalidOperationException>().With.Message.Contains("JSON semantic round-trip failed"));
        }

        [TestCaseSource(nameof(JsonModeCases))]
        public void StrictJsonOracleRejectsArrayTruncation(string modeName)
        {
            JsonEncoderOptions options = FindMode(modeName);
            ReadRequest original = Testcases.CreateRichReadRequest();
            JsonNode payload = JsonNode.Parse(FuzzableCode.EncodeJsonMessage(original, options));
            JsonArray nodes = payload["UaBody"]["NodesToRead"].AsArray();
            nodes.RemoveAt(5);

            Assert.That(original.NodesToRead.Count, Is.EqualTo(6));
            Assert.That(nodes.Count, Is.EqualTo(5));
            string failure = options.SuppressArtifacts ? "array length" : "JSON semantic round-trip failed";
            Assert.That(
                () => FuzzableCode.FuzzJsonEncoderIndempotentCore(payload.ToJsonString(), original, options),
                Throws.TypeOf<InvalidOperationException>().With.Message.Contains(failure));
        }

        [TestCaseSource(nameof(JsonModeCases))]
        public void StrictJsonOracleRejectsNonCanonicalTextEvenWhenValuesMatch(string modeName)
        {
            JsonEncoderOptions options = FindMode(modeName);
            ReadRequest original = Testcases.CreateRichReadRequest();
            string canonical = FuzzableCode.EncodeJsonMessage(original, options);
            string nonCanonical = canonical + " ";
            IEncodeable decoded = FuzzableCode.DecodeJsonWithMetadata(
                nonCanonical,
                original,
                options,
                FuzzableCode.MessageContext);

            EncoderTestMessages.AssertPopulated(decoded);
            Assert.That(Utils.IsEqual(original, decoded), Is.True);
            Assert.That(FuzzableCode.EncodeJsonMessage(decoded, options), Is.EqualTo(canonical));
            Assert.That(
                () => FuzzableCode.FuzzJsonEncoderIndempotentCore(nonCanonical, original, options),
                Throws.TypeOf<InvalidOperationException>().With.Message.Contains("Idempotent JSON encoding failed"));
        }

        [TestCaseSource(nameof(JsonModeCases))]
        public void TypedMetadataDecodeAndStrictOracleRejectChangedMessageType(string modeName)
        {
            JsonEncoderOptions options = FindMode(modeName);
            ReadRequest original = Testcases.CreateRichReadRequest();
            JsonNode payload = JsonNode.Parse(FuzzableCode.EncodeJsonMessage(original, options));
            payload["UaTypeId"] = "i=671";
            string corrupted = payload.ToJsonString();

            Assert.That(
                () => FuzzableCode.DecodeJsonWithMetadata(corrupted, original, options, FuzzableCode.MessageContext),
                Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo(
                    "JSON message type changed during encoding."));
            Assert.That(
                () => FuzzableCode.FuzzJsonEncoderIndempotentCore(corrupted, original, options),
                Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo(
                    "JSON message type changed during encoding."));
        }

        [TestCaseSource(nameof(JsonModeCases))]
        public void GeneratedJsonDecodeErrorsEscapeTheStrictOracle(string modeName)
        {
            JsonEncoderOptions options = FindMode(modeName);
            ReadRequest original = Testcases.CreateRichReadRequest();
            string serialized = FuzzableCode.EncodeJsonMessage(original, options);
            string truncated = serialized[..^1];

            Assert.That(FuzzableCode.FuzzJsonDecoderCore(truncated), Is.Null);
            Assert.That(
                () => FuzzableCode.FuzzJsonEncoderIndempotentCore(truncated, original, options),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo((StatusCode)StatusCodes.BadDecodingError));
            Assert.That(original.NodesToRead[2].IndexRange, Is.EqualTo("1:2"));
        }

        [TestCaseSource(nameof(JsonModeCases))]
        public void GeneratedRoundTripEncodingLimitFailuresAreNotSwallowed(string modeName)
        {
            JsonEncoderOptions options = FindMode(modeName);
            ServiceMessageContext context = FuzzableCode.CreateMessageContext();
            context.MaxStringLength = 4;
            ReadRequest original = Testcases.CreateRichReadRequest();

            Assert.That(
                () => FuzzableCode.FuzzJsonRoundTripCore(original, options, context),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo((StatusCode)StatusCodes.BadEncodingLimitsExceeded));
            Assert.That(original.NodesToRead[2].NodeId, Is.EqualTo(new NodeId("RevisionCounter", 3)));
            Assert.That(FuzzableCode.MessageContext.MaxStringLength, Is.GreaterThan(context.MaxStringLength));
        }

        [TestCase(nameof(StatusCodes.BadDecodingError), "None", true)]
        [TestCase(nameof(StatusCodes.BadDecodingError), "EndOfStream", true)]
        [TestCase(nameof(StatusCodes.BadDecodingError), "Xml", true)]
        [TestCase(nameof(StatusCodes.BadDecodingError), "Json", true)]
        [TestCase(nameof(StatusCodes.BadDecodingError), "Format", true)]
        [TestCase(nameof(StatusCodes.BadDecodingError), "NestedExpected", true)]
        [TestCase(nameof(StatusCodes.BadDecodingError), "NestedUnexpected", false)]
        [TestCase(nameof(StatusCodes.BadDecodingError), "InvalidOperation", false)]
        [TestCase(nameof(StatusCodes.BadDecodingError), "Io", false)]
        [TestCase(nameof(StatusCodes.BadEncodingLimitsExceeded), "None", true)]
        [TestCase(nameof(StatusCodes.BadEncodingLimitsExceeded), "InvalidOperation", false)]
        [TestCase(nameof(StatusCodes.BadUnexpectedError), "None", false)]
        [TestCase(nameof(StatusCodes.BadUnexpectedError), "Format", false)]
        public void SourceFailurePolicyOnlySwallowsExpectedStatusAndInnerExceptions(
            string statusName,
            string innerKind,
            bool expected)
        {
            StatusCode statusCode = statusName switch
            {
                nameof(StatusCodes.BadDecodingError) => StatusCodes.BadDecodingError,
                nameof(StatusCodes.BadEncodingLimitsExceeded) => StatusCodes.BadEncodingLimitsExceeded,
                nameof(StatusCodes.BadUnexpectedError) => StatusCodes.BadUnexpectedError,
                _ => throw new ArgumentException("Unknown status.", nameof(statusName))
            };
            var failure = new ServiceResultException(
                statusCode,
                "injected source failure",
                CreateInnerFailure(innerKind));
            ByteString input = EncoderTestMessages.Encode(Testcases.CreateRichReadRequest(), "Xml");
            using var stream = new NonSeekableInputStream(input.Span, failure);

            if (expected)
            {
                Assert.That(FuzzableCode.FuzzXmlDecoderCore(stream), Is.Null);
            }
            else
            {
                Assert.That(
                    () => FuzzableCode.FuzzXmlDecoderCore(stream),
                    Throws.TypeOf<ServiceResultException>().And.SameAs(failure));
            }
            Assert.That(stream.ReadCalls, Is.GreaterThan(0));

            using var strictStream = new NonSeekableInputStream(input.Span, failure);
            Assert.That(
                () => FuzzableCode.FuzzXmlDecoderCore(strictStream, true),
                Throws.TypeOf<ServiceResultException>().And.SameAs(failure));
            Assert.That(strictStream.ReadCalls, Is.GreaterThan(0));
        }

        [TestCase("Verbose")]
        [TestCase("Compact")]
        [TestCase("RawData")]
        [TestCase("LegacyNonReversible")]
        public void UriModesPreserveAbsoluteIdentifiersAcrossNamespaceTableReordering(string modeName)
        {
            ServiceMessageContext sourceContext = FuzzableCode.CreateMessageContext();
            ServiceMessageContext targetContext = CreateReorderedContext();
            JsonEncoderOptions options = FindMode(modeName);
            ReadRequest original = Testcases.CreateRichReadRequest();
            string serialized = FuzzableCode.EncodeJsonMessage(original, options, sourceContext);

            IEncodeable decoded = FuzzableCode.DecodeJsonWithMetadata(serialized, original, options, targetContext);

            Assert.That(decoded, Is.TypeOf<ReadRequest>());
            AssertRemappedIdentifiers(original, (ReadRequest)decoded, sourceContext, targetContext);
            ReadRequest remapped = CreateRemappedRequest(sourceContext, targetContext);
            Assert.That(Utils.IsEqual(remapped, decoded), Is.True);
            Assert.That(FuzzableCode.EncodeJsonMessage(decoded, options, targetContext), Is.EqualTo(serialized));
            FuzzableCode.FuzzJsonEncoderIndempotentCore(serialized, remapped, options, targetContext);
        }

        [Test]
        public void IndexModeNeedsMappingTablesToPreserveAbsoluteIdentifiersInADifferentContext()
        {
            ServiceMessageContext sourceContext = FuzzableCode.CreateMessageContext();
            ServiceMessageContext targetContext = CreateReorderedContext();
            ReadRequest original = Testcases.CreateRichReadRequest();
            JsonEncoderOptions options = FuzzableCode.LegacyReversibleOptions;
            string serialized = FuzzableCode.EncodeJsonMessage(original, options, sourceContext);
            IEncodeable sameContext = FuzzableCode.DecodeJsonWithMetadata(serialized, original, options, sourceContext);
            EncoderTestMessages.AssertPopulated(sameContext);
            Assert.That(Utils.IsEqual(original, sameContext), Is.True);

            var unmapped = (ReadRequest)FuzzableCode.DecodeJsonWithMetadata(
                serialized,
                original,
                options,
                targetContext);
            Assert.That(unmapped.NodesToRead[3].NodeId.NamespaceIndex, Is.EqualTo(1));
            Assert.That(
                NodeId.ToExpandedNodeId(unmapped.NodesToRead[3].NodeId, targetContext.NamespaceUris).NamespaceUri,
                Is.EqualTo("urn:opcfoundation:fuzzing:types"));

            using var decoder = new JsonDecoder(serialized, targetContext);
            decoder.SetMappingTables(sourceContext.NamespaceUris, sourceContext.ServerUris);
            IEncodeable decoded = decoder.ReadEncodeable<IEncodeable>("UaBody", original.TypeId);
            Assert.That(decoded, Is.TypeOf<ReadRequest>());
            AssertRemappedIdentifiers(original, (ReadRequest)decoded, sourceContext, targetContext);
            Assert.That(Utils.IsEqual(CreateRemappedRequest(sourceContext, targetContext), decoded), Is.True);
            Assert.That(FuzzableCode.EncodeJsonMessage(decoded, options, targetContext), Is.Not.EqualTo(serialized));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void XmlDecoderResolvesStandardQNameToAPopulatedReadRequest(bool usePrefix)
        {
            ReadRequest original = Testcases.CreateRichReadRequest();
            string serialized = Encoding.UTF8.GetString(EncoderTestMessages.Encode(original, "Xml").ToArray());
            XDocument document = XDocument.Parse(serialized);
            Assert.That(document.Root.Name, Is.EqualTo(XName.Get(nameof(ReadRequest), Namespaces.OpcUaXsd)));
            if (usePrefix)
            {
                document.Root.Attribute("xmlns")?.Remove();
                document.Root.SetAttributeValue(XNamespace.Xmlns + "ua", Namespaces.OpcUaXsd);
                serialized = document.ToString(SaveOptions.DisableFormatting);
                Assert.That(serialized, Does.Contain("<ua:ReadRequest"));
            }
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(serialized));

            IEncodeable decoded = FuzzableCode.FuzzXmlDecoderCore(stream, true);

            EncoderTestMessages.AssertPopulated(decoded);
            Assert.That(Utils.IsEqual(original, decoded), Is.True);
        }

        [Test]
        public void XmlDecoderRejectsTheRightLocalTypeNameInTheWrongNamespace()
        {
            string valid = Encoding.UTF8.GetString(
                EncoderTestMessages.Encode(Testcases.CreateRichReadRequest(), "Xml").ToArray());
            XDocument document = XDocument.Parse(valid);
            document.Root.Name = XName.Get(nameof(ReadRequest), "urn:encoder-test:wrong-namespace");
            document.Root.SetAttributeValue("xmlns", "urn:encoder-test:wrong-namespace");
            string wrongNamespace = document.ToString(SaveOptions.DisableFormatting);
            Assert.That(document.Root.Name.LocalName, Is.EqualTo(nameof(ReadRequest)));
            Assert.That(document.Root.Name.NamespaceName, Is.EqualTo("urn:encoder-test:wrong-namespace"));
            ByteString input = ByteString.From(Encoding.UTF8.GetBytes(wrongNamespace));
            AssertMalformedSource(input, "Xml");
            foreach (CallbackCase target in s_callbacks.Where(callback => callback.Wire == "Xml"))
            {
                target.Span(input.Span);
                ReplayAfl(target, input, false);
            }
        }

        [Test]
        public void MalformedXmlIsRejectedWithItsParserExceptionPreservedInStrictMode()
        {
            ByteString input = ByteString.From(Encoding.UTF8.GetBytes(
                """<ReadRequest xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd"""));

            Assert.That(EncoderTestMessages.Decode(input.Span, "Xml", false), Is.Null);
            Assert.That(
                () => EncoderTestMessages.Decode(input.Span, "Xml"),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo((StatusCode)StatusCodes.BadDecodingError)
                    .And.Property(nameof(Exception.InnerException)).InstanceOf<XmlException>());
        }

        private static IEnumerable<TestCaseData> JsonModeCases()
        {
            foreach (string name in s_modeNames)
            {
                yield return new TestCaseData(name);
            }
        }

        private static IEnumerable<TestCaseData> RichJsonModeCases()
        {
            for (int i = 0; i < EncoderTestMessages.RichNames.Count; i++)
            {
                foreach (string modeName in s_modeNames)
                {
                    yield return new TestCaseData(EncoderTestMessages.RichNames[i], modeName);
                }
            }
        }

        private static JsonEncoderOptions FindMode(string name)
        {
            return FuzzableCode.JsonEncodingModes.ToArray().Single(mode => mode.Name == name);
        }

        private static Exception CreateInnerFailure(string kind)
        {
            return kind switch
            {
                "None" => null,
                "EndOfStream" => new EndOfStreamException("truncated"),
                "Xml" => new XmlException("malformed"),
                "Json" => new JsonException("malformed"),
                "Format" => new FormatException("malformed"),
                "NestedExpected" => new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded),
                "NestedUnexpected" => new ServiceResultException(StatusCodes.BadUnexpectedError),
                "InvalidOperation" => new InvalidOperationException("unexpected implementation failure"),
                "Io" => new IOException("unexpected infrastructure failure"),
                _ => throw new ArgumentException("Unknown inner exception.", nameof(kind))
            };
        }

        private static void AssertSegmentLayout(ByteString input, int segmentSize)
        {
            using MemoryStream stream = FuzzableCode.PrepareArraySegmentStream(input.Span, segmentSize);
            BufferCollection buffers = ((ArraySegmentStream)stream).GetBuffers(nameof(EncoderParityTests));
            Assert.That(buffers.Count, Is.EqualTo((input.Length + segmentSize - 1) / segmentSize));
            int offset = 0;
            for (int i = 0; i < buffers.Count; i++)
            {
                int length = Math.Min(segmentSize, input.Length - offset);
                Assert.That(buffers[i].Offset, Is.Zero);
                Assert.That(buffers[i].Count, Is.EqualTo(length));
                Assert.That(
                    buffers[i].AsSpan().ToArray(),
                    Is.EqualTo(input.Span.Slice(offset, length).ToArray()));
                if (i > 0)
                {
                    Assert.That(buffers[i].Array, Is.Not.SameAs(buffers[i - 1].Array));
                }
                offset += length;
            }
            Assert.That(offset, Is.EqualTo(input.Length));
        }

        private static int CountTypeArtifacts(JsonElement element)
        {
            int count = 0;
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (property.Name is "UaType" or "UaTypeId")
                    {
                        count++;
                    }
                    count += CountTypeArtifacts(property.Value);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in element.EnumerateArray())
                {
                    count += CountTypeArtifacts(item);
                }
            }
            return count;
        }

        private static ServiceMessageContext CreateReorderedContext()
        {
            ServiceMessageContext context = FuzzableCode.CreateMessageContext();
            context.NamespaceUris.Update(
            [
                Namespaces.OpcUa,
                "urn:opcfoundation:fuzzing:types",
                "urn:opcfoundation:fuzzing:diagnostics",
                "urn:opcfoundation:fuzzing:application",
                "urn:opcfoundation:fuzzing:devices"
            ]);
            return context;
        }

        private static ReadRequest CreateRemappedRequest(
            ServiceMessageContext sourceContext,
            ServiceMessageContext targetContext)
        {
            ReadRequest remapped = Testcases.CreateRichReadRequest();
            foreach (ReadValueId value in remapped.NodesToRead)
            {
                ExpandedNodeId absolute = NodeId.ToExpandedNodeId(value.NodeId, sourceContext.NamespaceUris);
                value.NodeId = ExpandedNodeId.ToNodeId(absolute, targetContext.NamespaceUris);
            }
            return remapped;
        }

        private static void AssertRemappedIdentifiers(
            ReadRequest original,
            ReadRequest decoded,
            ServiceMessageContext sourceContext,
            ServiceMessageContext targetContext)
        {
            Assert.That(decoded.MaxAge, Is.EqualTo(1000));
            Assert.That(decoded.TimestampsToReturn, Is.EqualTo(TimestampsToReturn.Source));
            Assert.That(decoded.NodesToRead.Count, Is.EqualTo(6));
            Assert.That(original.NodesToRead[3].NodeId.NamespaceIndex, Is.EqualTo(1));
            Assert.That(decoded.NodesToRead[3].NodeId.NamespaceIndex, Is.EqualTo(3));
            Assert.That(decoded.NodesToRead[1].NodeId.NamespaceIndex, Is.EqualTo(4));
            Assert.That(decoded.NodesToRead[2].NodeId.NamespaceIndex, Is.EqualTo(2));
            Assert.That(decoded.NodesToRead[5].NodeId.NamespaceIndex, Is.EqualTo(1));
            for (int i = 0; i < original.NodesToRead.Count; i++)
            {
                Assert.That(
                    NodeId.ToExpandedNodeId(decoded.NodesToRead[i].NodeId, targetContext.NamespaceUris),
                    Is.EqualTo(NodeId.ToExpandedNodeId(original.NodesToRead[i].NodeId, sourceContext.NamespaceUris)));
                Assert.That(decoded.NodesToRead[i].AttributeId, Is.EqualTo(original.NodesToRead[i].AttributeId));
            }
            Assert.That(decoded.NodesToRead[2].IndexRange, Is.EqualTo("1:2"));
        }

        private static IEnumerable<TestCaseData> PositiveCallbackCases()
        {
            foreach (CallbackCase target in s_callbacks)
            {
                for (int i = 0; i < EncoderTestMessages.RichNames.Count; i++)
                {
                    yield return new TestCaseData(target.Span.Method.Name, EncoderTestMessages.RichNames[i]);
                }
            }
        }

        private static IEnumerable<TestCaseData> TruncatedCallbackCases()
        {
            foreach (CallbackCase target in s_callbacks)
            {
                yield return new TestCaseData(target.Span.Method.Name, true);
                yield return new TestCaseData(target.Span.Method.Name, false);
            }
        }

        private static IEnumerable<TestCaseData> SegmentBoundaryCases()
        {
            for (int i = 0; i < EncoderTestMessages.RichNames.Count; i++)
            {
                foreach (int segmentSize in s_segmentSizes)
                {
                    yield return new TestCaseData(EncoderTestMessages.RichNames[i], segmentSize);
                }
            }
        }

        private static CallbackCase FindCallback(string name)
        {
            return s_callbacks.Single(callback => callback.Span.Method.Name == name);
        }

        private static ByteString TruncatedReadRequest(string wire, bool truncateLastByte)
        {
            ByteString valid = EncoderTestMessages.Encode(Testcases.CreateRichReadRequest(), wire);
            int length = truncateLastByte ? valid.Length - 1 : valid.Length / 2;
            return ByteString.From(valid.Span[..length]);
        }

        private static void AssertMalformedSource(ByteString input, string wire)
        {
            Assert.That(input.Length, Is.GreaterThan(32), "Exercise truncated payloads, not just empty envelopes.");
            Assert.That(EncoderTestMessages.Decode(input.Span, wire, false), Is.Null);
            Assert.That(
                () => EncoderTestMessages.Decode(input.Span, wire),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo((StatusCode)StatusCodes.BadDecodingError));
        }

        private static void ReplayAfl(CallbackCase target, ByteString input, bool completeMessage)
        {
            if (target.Afl is FuzzMethods.AflFuzzString textCallback)
            {
                textCallback(Encoding.UTF8.GetString(input.ToArray()));
                return;
            }

            Assert.That(target.Afl, Is.InstanceOf<FuzzMethods.AflFuzzStream>());
            using var stream = new NonSeekableInputStream(input.Span);
            ((FuzzMethods.AflFuzzStream)target.Afl)(stream);
            Assert.That(stream.BytesRead, Is.GreaterThan(0), "The AFL stream wrapper must consume its input.");
            if (completeMessage)
            {
                Assert.That(stream.BytesRead, Is.EqualTo(input.Length));
            }
        }

        private sealed record CallbackCase(FuzzMethods.LibFuzzSpan Span, Delegate Afl, string Wire);

        private sealed class NonSeekableInputStream : Stream
        {
            public NonSeekableInputStream(ReadOnlySpan<byte> input, ServiceResultException failure = null)
            {
                m_stream = new MemoryStream(input.ToArray(), false);
                m_failure = failure;
            }

            public override bool CanRead => m_stream.CanRead;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            internal int BytesRead { get; private set; }
            internal int ReadCalls { get; private set; }

            public override int Read(byte[] buffer, int offset, int count)
            {
                ReadCalls++;
                if (m_failure != null)
                {
                    throw m_failure;
                }
                int read = m_stream.Read(buffer, offset, count);
                BytesRead += read;
                return read;
            }

            public override void Flush()
            {
                throw new NotSupportedException();
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotSupportedException();
            }

            public override void SetLength(long value)
            {
                throw new NotSupportedException();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new NotSupportedException();
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    m_stream.Dispose();
                }
                base.Dispose(disposing);
            }

            private readonly MemoryStream m_stream;
            private readonly ServiceResultException m_failure;
        }

        private const string k_artifactSchema =
            """
            {"UaTypeId":"i=634",
             "UaBody":{"Results":[{"UaType":12,"Value":"original"},{"UaType":7,"Value":42}],"Sequence":7}}
            """;

        private static readonly string[] s_modeNames =
        [
            "Verbose", "Compact", "RawData", "LegacyReversible", "LegacyNonReversible"
        ];
        private static readonly string[] s_namespaceUris =
        [
            Namespaces.OpcUa,
            "urn:opcfoundation:fuzzing:application",
            "urn:opcfoundation:fuzzing:devices",
            "urn:opcfoundation:fuzzing:diagnostics",
            "urn:opcfoundation:fuzzing:types"
        ];
        private static readonly int[] s_segmentSizes = [1, 2, 7, 63, 64, 65];
        private static readonly CallbackCase[] s_callbacks =
        [
            new(
                FuzzableCode.LibfuzzBinaryDecoderSegmented,
                new FuzzMethods.AflFuzzStream(FuzzableCode.AflfuzzBinaryDecoder),
                "Binary"),
            new(
                FuzzableCode.LibfuzzBinaryEncoderSegmented,
                new FuzzMethods.AflFuzzStream(FuzzableCode.AflfuzzBinaryEncoder),
                "Binary"),
            new(
                FuzzableCode.LibfuzzBinaryEncoderIndempotentSegmented,
                new FuzzMethods.AflFuzzStream(FuzzableCode.AflfuzzBinaryEncoderIndempotent),
                "Binary"),
            new(
                FuzzableCode.LibfuzzJsonEncoderRawData,
                new FuzzMethods.AflFuzzString(FuzzableCode.AflfuzzJsonEncoderRawData),
                "Json"),
            new(
                FuzzableCode.LibfuzzJsonEncoderLegacyReversible,
                new FuzzMethods.AflFuzzString(FuzzableCode.AflfuzzJsonEncoderLegacyReversible),
                "Json"),
            new(
                FuzzableCode.LibfuzzJsonEncoderLegacyNonReversible,
                new FuzzMethods.AflFuzzString(FuzzableCode.AflfuzzJsonEncoderLegacyNonReversible),
                "Json"),
            new(
                FuzzableCode.LibfuzzBinaryJsonEncoder,
                new FuzzMethods.AflFuzzStream(FuzzableCode.AflfuzzBinaryJsonEncoder),
                "Binary"),
            new(
                FuzzableCode.LibfuzzBinaryJsonEncoderCompact,
                new FuzzMethods.AflFuzzStream(FuzzableCode.AflfuzzBinaryJsonEncoderCompact),
                "Binary"),
            new(
                FuzzableCode.LibfuzzBinaryJsonEncoderRawData,
                new FuzzMethods.AflFuzzStream(FuzzableCode.AflfuzzBinaryJsonEncoderRawData),
                "Binary"),
            new(
                FuzzableCode.LibfuzzBinaryJsonEncoderLegacyReversible,
                new FuzzMethods.AflFuzzStream(FuzzableCode.AflfuzzBinaryJsonEncoderLegacyReversible),
                "Binary"),
            new(
                FuzzableCode.LibfuzzBinaryJsonEncoderLegacyNonReversible,
                new FuzzMethods.AflFuzzStream(FuzzableCode.AflfuzzBinaryJsonEncoderLegacyNonReversible),
                "Binary"),
            new(
                FuzzableCode.LibfuzzXmlJsonEncoder,
                new FuzzMethods.AflFuzzStream(FuzzableCode.AflfuzzXmlJsonEncoder),
                "Xml"),
            new(
                FuzzableCode.LibfuzzXmlJsonEncoderCompact,
                new FuzzMethods.AflFuzzStream(FuzzableCode.AflfuzzXmlJsonEncoderCompact),
                "Xml"),
            new(
                FuzzableCode.LibfuzzXmlJsonEncoderRawData,
                new FuzzMethods.AflFuzzStream(FuzzableCode.AflfuzzXmlJsonEncoderRawData),
                "Xml"),
            new(
                FuzzableCode.LibfuzzXmlJsonEncoderLegacyReversible,
                new FuzzMethods.AflFuzzStream(FuzzableCode.AflfuzzXmlJsonEncoderLegacyReversible),
                "Xml"),
            new(
                FuzzableCode.LibfuzzXmlJsonEncoderLegacyNonReversible,
                new FuzzMethods.AflFuzzStream(FuzzableCode.AflfuzzXmlJsonEncoderLegacyNonReversible),
                "Xml")
        ];
    }
}
