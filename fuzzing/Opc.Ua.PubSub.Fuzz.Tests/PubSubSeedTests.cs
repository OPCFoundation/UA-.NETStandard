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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Encoding.Uadp;
using Opc.Ua.PubSub.MetaData;
using PubSubJsonDataSetMessage = Opc.Ua.PubSub.Encoding.Json.JsonDataSetMessage;
using PubSubJsonDecoder = Opc.Ua.PubSub.Encoding.Json.JsonDecoder;
using PubSubJsonNetworkMessage = Opc.Ua.PubSub.Encoding.Json.JsonNetworkMessage;

namespace Opc.Ua.Fuzzing
{
    /// <summary>
    /// Independent value oracles for the copied, deterministic PubSub corpus.
    /// </summary>
    [TestFixture]
    [Category("Fuzzing")]
    public sealed class PubSubSeedTests
    {
        [TestCase("metadata-keyframe-verbose.json", "retained-Verbose-Variant", PubSubFieldEncoding.Variant)]
        [TestCase("metadata-keyframe-compact.json", "retained-Compact-Variant", PubSubFieldEncoding.RawData)]
        [TestCase("metadata-keyframe-raw.json", "retained-RawData-Variant", PubSubFieldEncoding.RawData)]
        [TestCase("metadata-keyframe-datavalue.json", "retained-Verbose-DataValue", PubSubFieldEncoding.DataValue)]
        public async Task StoredJsonSeedHasTypedValuesThroughCoreAndPublicDecoderAsync(
            string name,
            string messageId,
            PubSubFieldEncoding encoding)
        {
            byte[] seed = PubSubSeedAssertions.LoadSeed("Json", name);
            PubSubNetworkMessageContext coreContext = FuzzableCode.NewContext();
            PubSubNetworkMessageContext publicContext = FuzzableCode.NewContext();

            PubSubNetworkMessage coreResult = FuzzableCode.DecodePubSubJson(seed, coreContext);
            PubSubNetworkMessage publicResult = await new PubSubJsonDecoder()
                .TryDecodeAsync(seed, publicContext).ConfigureAwait(false);

            // Agreement alone is insufficient: both paths must preserve the actual seed values.
            PubSubSeedAssertions.AssertJsonSeed(coreResult, messageId, encoding);
            PubSubSeedAssertions.AssertJsonSeed(publicResult, messageId, encoding);
            PubSubSeedAssertions.AssertDiagnostics(coreContext, received: 1, dataSets: 1);
            PubSubSeedAssertions.AssertDiagnostics(publicContext, received: 1, dataSets: 1);
            PubSubSeedAssertions.ReplayJsonAdapters(seed);
        }

        [TestCase("metadata-keyframe-variant.uadp", PubSubFieldEncoding.Variant)]
        [TestCase("metadata-keyframe-datavalue.uadp", PubSubFieldEncoding.DataValue)]
        [TestCase("metadata-keyframe-raw.uadp", PubSubFieldEncoding.RawData)]
        public void StoredUadpSeedHasTypedValuesAndUnencryptedHeaders(string name, PubSubFieldEncoding encoding)
        {
            byte[] seed = PubSubSeedAssertions.LoadSeed("Uadp", name);
            PubSubNetworkMessageContext context = FuzzableCode.NewContext();

            PubSubNetworkMessage result = FuzzableCode.DecodeUadp(seed, context);

            PubSubSeedAssertions.AssertUadpSeed(result, encoding);
            PubSubSeedAssertions.AssertDiagnostics(context, received: 1, dataSets: 1);
            Assert.That(
                UadpDecoder.TryReadOuterPrefix(
                    seed, out int prefixLength, out bool securityEnabled, out bool chunkMessage,
                    out PublisherId publisherId, out ushort writerGroupId),
                Is.True);
            Assert.That(prefixLength, Is.EqualTo(36));
            Assert.That(securityEnabled, Is.False);
            Assert.That(chunkMessage, Is.False);
            Assert.That(publisherId, Is.EqualTo(PublisherId.FromUInt16(300)));
            Assert.That(writerGroupId, Is.EqualTo(1));
            PubSubSeedAssertions.ReplayUadpAdapters(seed);
        }

        [TestCase("Json", "metadata-keyframe-compact.json", false)]
        [TestCase("Json", "metadata-keyframe-compact.json", true)]
        [TestCase("Json", "metadata-keyframe-raw.json", false)]
        [TestCase("Json", "metadata-keyframe-raw.json", true)]
        [TestCase("Uadp", "metadata-keyframe-raw.uadp", false)]
        [TestCase("Uadp", "metadata-keyframe-raw.uadp", true)]
        public void MetadataDependentSeedRejectsMissingOrWrongMajorAndRecovers(
            string bucket,
            string name,
            bool wrongMajor)
        {
            byte[] seed = PubSubSeedAssertions.LoadSeed(bucket, name);
            PubSubNetworkMessageContext context = FuzzableCode.NewContext();
            bool isJson = bucket == "Json";
            var key = new DataSetMetaDataKey(
                PublisherId.FromUInt16(300), (ushort)(isJson ? 0 : 1), 1,
                PubSubSeedAssertions.DataSetClassId, 1);
            if (wrongMajor)
            {
                DataSetMetaDataType incompatible = FuzzableCode.CreateMetaData();
                incompatible.ConfigurationVersion.MajorVersion = 9;
                context.MetaDataRegistry.Register(key, incompatible);
            }
            else
            {
                context.MetaDataRegistry.Remove(key);
            }
            Assert.That(
                context.MetaDataRegistry.TryGet(key, out _),
                Is.EqualTo(wrongMajor ? MetaDataMatchResult.MajorVersionMismatch : MetaDataMatchResult.NotFound));

            if (isJson)
            {
                PubSubNetworkMessage rejected = FuzzableCode.DecodePubSubJson(seed, context);
                // The JSON envelope survives; no DataSetMessage may be mistaken for a successful decode.
                Assert.That(rejected, Is.TypeOf<PubSubJsonNetworkMessage>());
                Assert.That(rejected.PublisherId, Is.EqualTo(PublisherId.FromString("300")));
                Assert.That(rejected.DataSetMessages.Count, Is.Zero);
                PubSubSeedAssertions.AssertDiagnostics(context, received: 1, failed: 1, resolverErrors: 1);
            }
            else
            {
                Assert.That(FuzzableCode.DecodeUadp(seed, context), Is.Null);
                PubSubSeedAssertions.AssertDiagnostics(context, invalid: 1, resolverErrors: wrongMajor ? 1 : 0);
            }

            // Reinstall only the requested identity; the other writer-group registration was never removed.
            context.MetaDataRegistry.Register(key, FuzzableCode.CreateMetaData());
            if (isJson)
            {
                string messageId = name == "metadata-keyframe-compact.json"
                    ? "retained-Compact-Variant" : "retained-RawData-Variant";
                PubSubSeedAssertions.AssertJsonSeed(
                    FuzzableCode.DecodePubSubJson(seed, context), messageId, PubSubFieldEncoding.RawData);
                PubSubSeedAssertions.AssertDiagnostics(
                    context, received: 2, dataSets: 1, failed: 1, resolverErrors: 1);
            }
            else
            {
                PubSubSeedAssertions.AssertUadpSeed(
                    FuzzableCode.DecodeUadp(seed, context), PubSubFieldEncoding.RawData);
                PubSubSeedAssertions.AssertDiagnostics(
                    context, received: 1, dataSets: 1, invalid: 1, resolverErrors: wrongMajor ? 1 : 0);
            }
        }

        [Test]
        [SetCulture("fr-FR")]
        public void ProducerReproducesEveryStoredSeedUnderNonDefaultCulture()
        {
            Assert.That(CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator, Is.EqualTo(","));
            string[] expectedNames =
            [
                "Json/metadata-keyframe-verbose.json",
                "Json/metadata-keyframe-compact.json",
                "Json/metadata-keyframe-raw.json",
                "Json/metadata-keyframe-datavalue.json",
                "Uadp/metadata-keyframe-variant.uadp",
                "Uadp/metadata-keyframe-datavalue.uadp",
                "Uadp/metadata-keyframe-raw.uadp",
                "Chunks/metadata-keyframe-complete.bin",
                "Chunks/metadata-keyframe-chunk-000.bin",
                "Chunks/metadata-keyframe-chunk-001.bin",
                "Chunks/metadata-keyframe-chunk-002.bin"
            ];
            (string Bucket, string Name, byte[] Data)[] generated = [.. Testcases.CreatePubSubSeeds()];

            Assert.That(
                generated.Select(static seed => $"{seed.Bucket}/{seed.Name}"),
                Is.EquivalentTo(expectedNames));
            foreach ((string bucket, string name, byte[] data) in generated)
            {
                byte[] stored = PubSubSeedAssertions.LoadSeed(bucket, name);
                Assert.That(data, Is.EqualTo(stored), $"{bucket}/{name} must remain byte-for-byte reproducible.");
            }
        }

        [Test]
        public void JsonModesDifferInPayloadShapeNotOnlyMessageId()
        {
            using var verbose = JsonDocument.Parse(
                PubSubSeedAssertions.LoadSeed("Json", "metadata-keyframe-verbose.json"));
            using var compact = JsonDocument.Parse(
                PubSubSeedAssertions.LoadSeed("Json", "metadata-keyframe-compact.json"));
            using var raw = JsonDocument.Parse(PubSubSeedAssertions.LoadSeed("Json", "metadata-keyframe-raw.json"));
            JsonElement verbosePayload = verbose.RootElement.GetProperty("Messages")[0].GetProperty("Payload");
            JsonElement compactPayload = compact.RootElement.GetProperty("Messages")[0].GetProperty("Payload");
            JsonElement rawPayload = raw.RootElement.GetProperty("Messages")[0].GetProperty("Payload");
            string[] names = ["Running", "Count", "Temperature", "Label"];
            int[] typeCodes = [1, 6, 11, 12];
            JsonValueKind[] bareKinds =
                [JsonValueKind.True, JsonValueKind.Number, JsonValueKind.Number, JsonValueKind.String];

            for (int i = 0; i < names.Length; i++)
            {
                JsonElement wrapped = verbosePayload.GetProperty(names[i]);
                Assert.That(wrapped.ValueKind, Is.EqualTo(JsonValueKind.Object), names[i]);
                Assert.That(wrapped.GetProperty("Type").GetInt32(), Is.EqualTo(typeCodes[i]), names[i]);
                Assert.That(wrapped.GetProperty("Body").ValueKind, Is.EqualTo(bareKinds[i]), names[i]);
                Assert.That(compactPayload.GetProperty(names[i]).ValueKind, Is.EqualTo(bareKinds[i]), names[i]);
                Assert.That(rawPayload.GetProperty(names[i]).ValueKind, Is.EqualTo(bareKinds[i]), names[i]);
                Assert.That(
                    compactPayload.GetProperty(names[i]).GetRawText(),
                    Is.EqualTo(wrapped.GetProperty("Body").GetRawText()), names[i]);
                Assert.That(
                    rawPayload.GetProperty(names[i]).GetRawText(),
                    Is.EqualTo(wrapped.GetProperty("Body").GetRawText()), names[i]);
            }
            Assert.That(compactPayload.GetProperty("Running").GetBoolean(), Is.True);
            Assert.That(compactPayload.GetProperty("Count").GetInt32(), Is.EqualTo(42));
            Assert.That(compactPayload.GetProperty("Temperature").GetDouble(), Is.EqualTo(21.5));
            Assert.That(compactPayload.GetProperty("Label").GetString(), Is.EqualTo("pubsub"));
        }

        [Test]
        public async Task PublicJsonCancellationPrecedesDecodeAndPreservesTokenAsync()
        {
            byte[] seed = PubSubSeedAssertions.LoadSeed("Json", "metadata-keyframe-raw.json");
            PubSubNetworkMessageContext context = FuzzableCode.NewContext();
            var decoder = new PubSubJsonDecoder();
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync().ConfigureAwait(false);

            await Assert.ThatAsync(
                async () =>
                {
                    _ = await decoder.TryDecodeAsync(seed, context, cancellation.Token).ConfigureAwait(false);
                },
                Throws.TypeOf<OperationCanceledException>()
                    .With.Property(nameof(OperationCanceledException.CancellationToken)).EqualTo(cancellation.Token))
                .ConfigureAwait(false);
            PubSubSeedAssertions.AssertDiagnostics(context);

            PubSubNetworkMessage decoded = await decoder.TryDecodeAsync(seed, context).ConfigureAwait(false);
            PubSubSeedAssertions.AssertJsonSeed(decoded, "retained-RawData-Variant", PubSubFieldEncoding.RawData);
            PubSubSeedAssertions.AssertDiagnostics(context, received: 1, dataSets: 1);
        }
    }

    /// <summary>
    /// Shared assertions use literal expectations, not a message produced by the encoder under test.
    /// </summary>
    internal static class PubSubSeedAssertions
    {
        internal static DateTimeOffset SeedTime => new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

        internal static Uuid DataSetClassId => new("aabbccdd-1122-3344-5566-778899aabbcc");

        internal static byte[] LoadSeed(string bucket, string name)
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Testcases", bucket, name);
            Assert.That(File.Exists(path), Is.True, $"Required copied PubSub seed is missing: {path}");
            return File.ReadAllBytes(path);
        }

        internal static void AssertJsonSeed(
            PubSubNetworkMessage result,
            string messageId,
            PubSubFieldEncoding encoding)
        {
            Assert.That(result, Is.TypeOf<PubSubJsonNetworkMessage>());
            var message = (PubSubJsonNetworkMessage)result;
            Assert.That(message.MessageId, Is.EqualTo(messageId));
            Assert.That(message.MessageType, Is.EqualTo("ua-data"));
            Assert.That(message.TransportProfileUri, Is.EqualTo(Profiles.PubSubMqttJsonTransport));
            Assert.That(message.PublisherId, Is.EqualTo(PublisherId.FromString("300")));
            Assert.That(message.DataSetClassId, Is.EqualTo(DataSetClassId));
            Assert.That(
                message.WriterGroupId, Is.Null, "JSON resolves metadata using writer group zero, not a wire ID.");
            Assert.That(message.SingleMessageMode, Is.False);
            Assert.That(message.ContentMask, Is.EqualTo(
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.DataSetMessageHeader |
                JsonNetworkMessageContentMask.PublisherId |
                JsonNetworkMessageContentMask.DataSetClassId));
            Assert.That(message.DataSetMessages.Count, Is.EqualTo(1));
            Assert.That(message.DataSetMessages[0], Is.TypeOf<PubSubJsonDataSetMessage>());
            var dataSet = (PubSubJsonDataSetMessage)message.DataSetMessages[0];
            Assert.That(dataSet.MessageTypeName, Is.EqualTo("ua-keyframe"));
            Assert.That(dataSet.ContentMask, Is.EqualTo(
                JsonDataSetMessageContentMask.DataSetWriterId |
                JsonDataSetMessageContentMask.SequenceNumber |
                JsonDataSetMessageContentMask.MetaDataVersion |
                JsonDataSetMessageContentMask.Timestamp |
                JsonDataSetMessageContentMask.MessageType));
            AssertDataSet(dataSet, encoding);
        }

        internal static void AssertUadpSeed(PubSubNetworkMessage result, PubSubFieldEncoding encoding)
        {
            Assert.That(result, Is.TypeOf<UadpNetworkMessage>());
            var message = (UadpNetworkMessage)result;
            Assert.That(message.TransportProfileUri, Is.EqualTo(Profiles.PubSubUdpUadpTransport));
            Assert.That(message.UadpVersion, Is.EqualTo(1));
            Assert.That(message.MessageType, Is.EqualTo(UadpNetworkMessageType.DataSetMessage));
            Assert.That(message.PublisherId, Is.EqualTo(PublisherId.FromUInt16(300)));
            Assert.That(message.WriterGroupId, Is.EqualTo(1));
            Assert.That(message.SequenceNumber, Is.EqualTo(42));
            Assert.That(message.DataSetClassId, Is.EqualTo(DataSetClassId));
            Assert.That(message.Timestamp, Is.EqualTo(new DateTimeUtc(SeedTime)));
            Assert.That(message.SecurityEnabled, Is.False);
            Assert.That(message.ContentMask, Is.EqualTo(
                UadpNetworkMessageContentMask.PublisherId |
                UadpNetworkMessageContentMask.GroupHeader |
                UadpNetworkMessageContentMask.WriterGroupId |
                UadpNetworkMessageContentMask.PayloadHeader |
                UadpNetworkMessageContentMask.SequenceNumber |
                UadpNetworkMessageContentMask.DataSetClassId |
                UadpNetworkMessageContentMask.Timestamp));
            Assert.That(message.DataSetMessages.Count, Is.EqualTo(1));
            Assert.That(message.DataSetMessages[0], Is.TypeOf<UadpDataSetMessage>());
            var dataSet = (UadpDataSetMessage)message.DataSetMessages[0];
            Assert.That(dataSet.FieldEncoding, Is.EqualTo(encoding));
            Assert.That(dataSet.ContentMask, Is.EqualTo(
                UadpDataSetMessageContentMask.SequenceNumber |
                UadpDataSetMessageContentMask.MajorVersion |
                UadpDataSetMessageContentMask.MinorVersion |
                UadpDataSetMessageContentMask.Timestamp));
            AssertDataSet(dataSet, encoding);
        }

        internal static void AssertDiagnostics(
            PubSubNetworkMessageContext context,
            long received = 0,
            long dataSets = 0,
            long invalid = 0,
            long failed = 0,
            long resolverErrors = 0)
        {
            Assert.That(
                context.Diagnostics.Read(PubSubDiagnosticsCounterKind.ReceivedNetworkMessages), Is.EqualTo(received));
            Assert.That(
                context.Diagnostics.Read(PubSubDiagnosticsCounterKind.ReceivedDataSetMessages), Is.EqualTo(dataSets));
            Assert.That(
                context.Diagnostics.Read(PubSubDiagnosticsCounterKind.ReceivedInvalidNetworkMessages),
                Is.EqualTo(invalid));
            Assert.That(
                context.Diagnostics.Read(PubSubDiagnosticsCounterKind.FailedDataSetMessages), Is.EqualTo(failed));
            Assert.That(
                context.Diagnostics.Read(PubSubDiagnosticsCounterKind.ResolverErrors), Is.EqualTo(resolverErrors));
        }

        internal static void ReplayJsonAdapters(byte[] frame)
        {
            using var stream = new MemoryStream(frame, writable: false);
            FuzzableCode.AflfuzzPubSubJsonDecode(stream);
            Assert.That(stream.Position, Is.EqualTo(frame.Length));
            Assert.That(stream.CanRead, Is.True);
            FuzzableCode.LibfuzzPubSubJsonDecode(frame);
        }

        internal static void ReplayUadpAdapters(byte[] frame)
        {
            using var stream = new MemoryStream(frame, writable: false);
            FuzzableCode.AflfuzzUadpNetworkMessageDecode(stream);
            Assert.That(stream.Position, Is.EqualTo(frame.Length));
            Assert.That(stream.CanRead, Is.True);
            FuzzableCode.LibfuzzUadpNetworkMessageDecode(frame);
        }

        private static void AssertDataSet(PubSubDataSetMessage dataSet, PubSubFieldEncoding encoding)
        {
            Assert.That(dataSet.DataSetWriterId, Is.EqualTo(1));
            Assert.That(dataSet.SequenceNumber, Is.EqualTo(42));
            Assert.That(dataSet.MetaDataVersion.MajorVersion, Is.EqualTo(1));
            Assert.That(dataSet.MetaDataVersion.MinorVersion, Is.EqualTo(2));
            Assert.That(dataSet.Timestamp, Is.EqualTo(new DateTimeUtc(SeedTime)));
            Assert.That(dataSet.Status, Is.EqualTo(StatusCodes.Good));
            Assert.That(dataSet.MessageType, Is.EqualTo(PubSubDataSetMessageType.KeyFrame));
            Assert.That(dataSet.Fields.Count, Is.EqualTo(4));

            Assert.That(dataSet.Fields[0].Value.TryGetValue(out bool running), Is.True);
            Assert.That(running, Is.True);
            Assert.That(dataSet.Fields[1].Value.TryGetValue(out int count), Is.True);
            Assert.That(count, Is.EqualTo(42));
            Assert.That(dataSet.Fields[2].Value.TryGetValue(out double temperature), Is.True);
            Assert.That(temperature, Is.EqualTo(21.5));
            Assert.That(dataSet.Fields[3].Value.TryGetValue(out string label), Is.True);
            Assert.That(label, Is.EqualTo("pubsub"));
            string[] names = ["Running", "Count", "Temperature", "Label"];
            BuiltInType[] types = [BuiltInType.Boolean, BuiltInType.Int32, BuiltInType.Double, BuiltInType.String];
            for (int i = 0; i < dataSet.Fields.Count; i++)
            {
                DataSetField field = dataSet.Fields[i];
                Assert.That(field.Name, Is.EqualTo(names[i]));
                Assert.That(field.Value.TypeInfo.BuiltInType, Is.EqualTo(types[i]), names[i]);
                Assert.That(field.Value.TypeInfo.ValueRank, Is.EqualTo(ValueRanks.Scalar), names[i]);
                Assert.That(field.Encoding, Is.EqualTo(encoding), names[i]);
                bool dataValue = encoding == PubSubFieldEncoding.DataValue;
                StatusCode status = dataValue && i == 1 ? StatusCodes.Uncertain : StatusCodes.Good;
                Assert.That(field.StatusCode, Is.EqualTo(status), names[i]);
                Assert.That(
                    field.SourceTimestamp,
                    Is.EqualTo(dataValue ? new DateTimeUtc(SeedTime) : default), names[i]);
                Assert.That(
                    field.ServerTimestamp,
                    Is.EqualTo(dataValue ? new DateTimeUtc(SeedTime.AddSeconds(1)) : default), names[i]);
                Assert.That(field.SourcePicoSeconds, Is.EqualTo(dataValue ? 10 : 0), names[i]);
                Assert.That(field.ServerPicoSeconds, Is.EqualTo(dataValue ? 20 : 0), names[i]);
            }
        }
    }
}
