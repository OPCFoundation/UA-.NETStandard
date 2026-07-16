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
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.MetaData;
using Opc.Ua.Schema;
using PubSubJsonDecoder = Opc.Ua.PubSub.Encoding.Json.JsonDecoder;
using PubSubJsonDataSetMessage = Opc.Ua.PubSub.Encoding.Json.JsonDataSetMessage;
using PubSubJsonEncoder = Opc.Ua.PubSub.Encoding.Json.JsonEncoder;
using PubSubJsonNetworkMessage = Opc.Ua.PubSub.Encoding.Json.JsonNetworkMessage;

namespace Opc.Ua.PubSub.Encoding.Tests
{
    /// <summary>
    /// Tests schema cache and handshake behavior in experimental PubSub adapters.
    /// </summary>
    [TestFixture]
    public sealed class SchemaExchangeHandshakeTests
    {
        /// <summary>
        /// Verifies Avro announcements are emitted once for an unchanged schema and again for a change.
        /// </summary>
        /// <returns>A task that represents the asynchronous test.</returns>
        [Test]
        public async Task AvroEncoder_AnnouncesOnceAndReannouncesOnSchemaChange()
        {
            PubSubNetworkMessageContext context = CreateContext(100, CreateMetaData(includeSecondField: false));
            AvroNetworkMessage first = CreateAvroMessage(string.Empty, includeSecondField: false);
            AvroNetworkMessage changed = CreateAvroMessage(string.Empty, includeSecondField: true);
            AvroNetworkMessageEncoder encoder = new() { DestinationId = "subscriber-a" };

            _ = await encoder.EncodeAsync(first, context);
            AvroSchemaAnnouncement? firstAnnouncement = encoder.LastSchemaAnnouncement;
            _ = await encoder.EncodeAsync(first, context);
            AvroSchemaAnnouncement? secondAnnouncement = encoder.LastSchemaAnnouncement;
            _ = await encoder.EncodeAsync(changed, context);
            AvroSchemaAnnouncement? changedAnnouncement = encoder.LastSchemaAnnouncement;

            Assert.That(firstAnnouncement, Is.Not.Null);
            Assert.That(secondAnnouncement, Is.Null);
            Assert.That(changedAnnouncement, Is.Not.Null);
            Assert.That(changedAnnouncement!.SchemaId, Is.Not.EqualTo(firstAnnouncement!.SchemaId));
        }

#if NET8_0_OR_GREATER && !NET_STANDARD_TESTS
        /// <summary>
        /// Verifies Arrow announcements are emitted once for an unchanged schema and again for a change.
        /// </summary>
        /// <returns>A task that represents the asynchronous test.</returns>
        [Test]
        public async Task ArrowEncoder_AnnouncesOnceAndReannouncesOnSchemaChange()
        {
            PubSubNetworkMessageContext context = CreateContext(100, CreateMetaData(includeSecondField: true));
            ArrowNetworkMessage first = CreateArrowMessage(string.Empty, includeSecondField: false);
            ArrowNetworkMessage changed = CreateArrowMessage(string.Empty, includeSecondField: true);
            ArrowNetworkMessageEncoder encoder = new() { DestinationId = "subscriber-a" };

            _ = await encoder.EncodeAsync(first, context);
            ArrowSchemaAnnouncement? firstAnnouncement = encoder.LastSchemaAnnouncement;
            _ = await encoder.EncodeAsync(first, context);
            ArrowSchemaAnnouncement? secondAnnouncement = encoder.LastSchemaAnnouncement;
            _ = await encoder.EncodeAsync(changed, context);
            ArrowSchemaAnnouncement? changedAnnouncement = encoder.LastSchemaAnnouncement;

            Assert.That(firstAnnouncement, Is.Not.Null);
            Assert.That(secondAnnouncement, Is.Null);
            Assert.That(changedAnnouncement, Is.Not.Null);
            Assert.That(changedAnnouncement!.SchemaId, Is.Not.EqualTo(firstAnnouncement!.SchemaId));
        }
#endif

        /// <summary>
        /// Verifies decoder cache hits avoid the resolver and misses invoke it once.
        /// </summary>
        /// <returns>A task that represents the asynchronous test.</returns>
        [Test]
        public async Task AvroDecoder_CacheHitAndMiss_UseResolverCorrectly()
        {
            ByteString schema = ByteString.From(System.Text.Encoding.UTF8.GetBytes("{\"name\":\"T\"}"));
            ByteString schemaId = SchemaCache.ComputeSchemaId(schema, SchemaCache.AvroFormat);
            string schemaKey = SchemaCache.ToKey(schemaId);
            PubSubNetworkMessageContext context = CreateContext(100, CreateMetaData(includeSecondField: false));
            AvroNetworkMessage message = CreateAvroMessage(schemaKey, includeSecondField: false);
            AvroNetworkMessageEncoder encoder = new();
            ReadOnlyMemory<byte> frame = await encoder.EncodeAsync(message, context);
            CountingResolver resolver = new(schemaId, schema, SchemaCache.AvroFormat);
            AvroNetworkMessageDecoder hitDecoder = new() { SchemaResolver = resolver };
            hitDecoder.SchemaCache.Add(schemaId, schema, SchemaCache.AvroFormat);

            PubSubNetworkMessage? hit = await hitDecoder.TryDecodeAsync(frame, context);
            AvroNetworkMessageDecoder missDecoder = new() { SchemaResolver = resolver };
            PubSubNetworkMessage? miss = await missDecoder.TryDecodeAsync(frame, context);

            Assert.That(hit, Is.Not.Null);
            Assert.That(miss, Is.Not.Null);
            Assert.That(resolver.CallCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies announcement ingest rejects mismatched fingerprints.
        /// </summary>
        [Test]
        public void SchemaCache_AddRejectsMismatchedAnnouncement()
        {
            SchemaCache cache = new();
            AvroSchemaAnnouncement bad = new(ByteString.From(1, 2, 3, 4, 5, 6, 7, 8), "{}", null);

            Assert.That(() => cache.Add(bad), Throws.InvalidOperationException);
        }

        /// <summary>
        /// Verifies JSON schema announcements use real JSON Schema text and cache verification.
        /// </summary>
        [Test]
        public void JsonSchemaAnnouncementCreatesSchemaAndCaches()
        {
            DataSetMetaDataType metaData = CreateMetaData(includeSecondField: true);
            PubSubNetworkMessageContext context = CreateContext(100, metaData);
            PubSubJsonNetworkMessage message = CreateJsonMessage(metaData);
            using ServiceProvider services = new ServiceCollection()
                .AddOpcUa()
                .AddSchemaGeneration()
                .Services
                .BuildServiceProvider();
            DataTypeDefinitionRegistry registry = services.GetRequiredService<DataTypeDefinitionRegistry>();
            registry.Add(CreateEnumDescription());
            DataSetJsonSchemaProvider provider = new(
                services.GetRequiredService<ISchemaProvider>(),
                registry);
            SchemaCache cache = new();

            JsonSchemaAnnouncement announcement = SchemaExchangeMessages.CreateJsonAnnouncement(
                message,
                context,
                provider);
            cache.Add(announcement);

            JsonNode root = JsonNode.Parse(announcement.SchemaJson)!;
            Assert.Multiple(() =>
            {
                Assert.That(
                    root["$schema"]!.GetValue<string>(),
                    Is.EqualTo("https://json-schema.org/draft/2020-12/schema"));
                Assert.That(root["$defs"]!["SchemaExchangeDataSet"]!["properties"]!["Temperature"], Is.Not.Null);
                Assert.That(root["$defs"]!["SchemaExchangeDataSet"]!["properties"]!["Enabled"], Is.Not.Null);
                Assert.That(cache.TryGet(announcement.SchemaId, out SchemaCacheEntry entry), Is.True);
                Assert.That(entry.Format, Is.EqualTo(SchemaCache.JsonFormat));
            });
        }

        /// <summary>
        /// Verifies JSON schema announcement creation validates required arguments.
        /// </summary>
        [Test]
        public void JsonSchemaAnnouncementCreationRejectsNullArguments()
        {
            DataSetMetaDataType metaData = CreateMetaData(includeSecondField: false);
            PubSubNetworkMessageContext context = CreateContext(100, metaData);
            PubSubJsonNetworkMessage message = CreateJsonMessage(metaData);
            var provider = new DeterministicJsonSchemaProvider();

            Assert.Multiple(() =>
            {
                Assert.That(
                    () => SchemaExchangeMessages.CreateJsonAnnouncement(null!, context, provider),
                    Throws.ArgumentNullException);
                Assert.That(
                    () => SchemaExchangeMessages.CreateJsonAnnouncement(message, null!, provider),
                    Throws.ArgumentNullException);
                Assert.That(
                    () => SchemaExchangeMessages.CreateJsonAnnouncement(message, context, null!),
                    Throws.ArgumentNullException);
            });
        }

        /// <summary>
        /// Verifies JSON schema announcement creation falls back to message-level metadata.
        /// </summary>
        [Test]
        public void JsonSchemaAnnouncementCreationUsesMessageMetaDataFallback()
        {
            DataSetMetaDataType metaData = CreateMetaData(includeSecondField: false);
            PubSubJsonNetworkMessage message = new()
            {
                PublisherId = PublisherId.FromString("publisher"),
                WriterGroupId = 1,
                DataSetClassId = new Uuid(new Guid("8c2f8f1c-c9a1-48b0-a90b-7d8f6e725001")),
                MetaData = metaData,
                DataSetMessages = []
            };

            JsonSchemaAnnouncement announcement = SchemaExchangeMessages.CreateJsonAnnouncement(
                message,
                CreateContext(100, metaData),
                new DeterministicJsonSchemaProvider());

            Assert.That(announcement.SchemaJson, Does.Contain("Temperature"));
        }

        /// <summary>
        /// Verifies the DataSet JSON Schema provider handles empty metadata.
        /// </summary>
        [Test]
        public void JsonSchemaProviderHandlesEmptyMetadata()
        {
            using ServiceProvider services = new ServiceCollection()
                .AddOpcUa()
                .AddSchemaGeneration()
                .Services
                .BuildServiceProvider();
            DataSetJsonSchemaProvider provider = new(
                services.GetRequiredService<ISchemaProvider>(),
                services.GetRequiredService<DataTypeDefinitionRegistry>());

            string schemaJson = provider.CreateJsonSchema(new DataSetMetaDataType { Name = "Empty" });

            JsonNode root = JsonNode.Parse(schemaJson)!;
            Assert.Multiple(() =>
            {
                Assert.That(root["title"]!.GetValue<string>(), Is.EqualTo("Empty"));
                Assert.That(root["$defs"]!["Empty"]!["properties"]!.AsObject(), Has.Count.EqualTo(0));
            });
        }

        /// <summary>
        /// Verifies the default JSON Schema provider validates constructor and method arguments.
        /// </summary>
        [Test]
        public void JsonSchemaProviderRejectsNullArguments()
        {
            using ServiceProvider services = new ServiceCollection()
                .AddOpcUa()
                .AddSchemaGeneration()
                .Services
                .BuildServiceProvider();
            ISchemaProvider schemaProvider = services.GetRequiredService<ISchemaProvider>();
            DataTypeDefinitionRegistry registry = services.GetRequiredService<DataTypeDefinitionRegistry>();
            DataSetJsonSchemaProvider provider = new(schemaProvider, registry);

            Assert.Multiple(() =>
            {
                Assert.That(
                    () => new DataSetJsonSchemaProvider(null!, registry),
                    Throws.ArgumentNullException);
                Assert.That(
                    () => new DataSetJsonSchemaProvider(schemaProvider, null!),
                    Throws.ArgumentNullException);
                Assert.That(
                    () => provider.CreateJsonSchema(null!),
                    Throws.ArgumentNullException);
            });
        }

        /// <summary>
        /// Verifies JSON Schema generation covers scalar, array, DataType NodeId and verbose branches.
        /// </summary>
        [Test]
        public void JsonSchemaProviderGeneratesValidSchemasForFieldShapes()
        {
            using ServiceProvider services = new ServiceCollection()
                .AddOpcUa()
                .AddSchemaGeneration()
                .Services
                .BuildServiceProvider();
            DataSetJsonSchemaProvider provider = new(
                services.GetRequiredService<ISchemaProvider>(),
                services.GetRequiredService<DataTypeDefinitionRegistry>());
            DataSetMetaDataType metaData = CreateArrayAndNodeIdMetaData();

            string compact = provider.CreateJsonSchema(metaData);
            string verbose = provider.CreateJsonSchema(metaData, verbose: true);
            JsonNode compactRoot = JsonNode.Parse(compact)!;
            JsonNode verboseRoot = JsonNode.Parse(verbose)!;

            Assert.Multiple(() =>
            {
                Assert.That(compactRoot["$schema"]!.GetValue<string>(), Does.Contain("2020-12"));
                Assert.That(verboseRoot["$schema"]!.GetValue<string>(), Does.Contain("2020-12"));
                Assert.That(compact, Does.Contain("ScalarBuiltIn"));
                Assert.That(compact, Does.Contain("ArrayBuiltIn"));
                Assert.That(compact, Does.Contain("NodeIdField"));
                Assert.That(verbose, Does.Contain("ArrayAndNodeIdDataSet"));
            });
        }

        /// <summary>
        /// Verifies the default JSON encoder path does not produce schema announcements.
        /// </summary>
        /// <returns>A task that represents the asynchronous test.</returns>
        [Test]
        public async Task JsonEncoderDefaultDisabledProducesNoAnnouncementAndSameFrame()
        {
            DataSetMetaDataType metaData = CreateMetaData(includeSecondField: false);
            PubSubNetworkMessageContext context = CreateContext(100, metaData);
            PubSubJsonNetworkMessage message = CreateJsonMessage(metaData);
            PubSubJsonEncoder baseline = new();
            PubSubJsonEncoder encoder = new()
            {
                SchemaProvider = new DeterministicJsonSchemaProvider()
            };

            ReadOnlyMemory<byte> baselineFrame = await baseline.EncodeAsync(message, context);
            ReadOnlyMemory<byte> frame = await encoder.EncodeAsync(message, context);

            Assert.Multiple(() =>
            {
                Assert.That(encoder.EnableSchemaExchange, Is.False);
                Assert.That(encoder.LastSchemaAnnouncement, Is.Null);
                Assert.That(frame.Span.SequenceEqual(baselineFrame.Span), Is.True);
            });
        }

        /// <summary>
        /// Verifies enabled JSON schema exchange announces once for an unchanged schema.
        /// </summary>
        /// <returns>A task that represents the asynchronous test.</returns>
        [Test]
        public async Task JsonEncoderEnabledAnnouncesOnce()
        {
            DataSetMetaDataType metaData = CreateMetaData(includeSecondField: false);
            PubSubNetworkMessageContext context = CreateContext(100, metaData);
            PubSubJsonNetworkMessage message = CreateJsonMessage(metaData);
            PubSubJsonEncoder encoder = new()
            {
                EnableSchemaExchange = true,
                SchemaProvider = new DeterministicJsonSchemaProvider(),
                DestinationId = "subscriber-a"
            };

            _ = await encoder.EncodeAsync(message, context);
            JsonSchemaAnnouncement? firstAnnouncement = encoder.LastSchemaAnnouncement;
            _ = await encoder.EncodeAsync(message, context);
            JsonSchemaAnnouncement? secondAnnouncement = encoder.LastSchemaAnnouncement;

            Assert.Multiple(() =>
            {
                Assert.That(firstAnnouncement, Is.Not.Null);
                Assert.That(secondAnnouncement, Is.Null);
                Assert.That(encoder.SchemaCache.TryGet(firstAnnouncement!.SchemaId, out SchemaCacheEntry entry), Is.True);
                Assert.That(entry.Format, Is.EqualTo(SchemaCache.JsonFormat));
            });
        }

        /// <summary>
        /// Verifies enabled JSON schema exchange re-announces when the DataSet schema changes.
        /// </summary>
        /// <returns>A task that represents the asynchronous test.</returns>
        [Test]
        public async Task JsonEncoderEnabledReannouncesAfterSchemaChange()
        {
            DataSetMetaDataType firstMetaData = CreateMetaData(includeSecondField: false);
            DataSetMetaDataType changedMetaData = CreateMetaData(includeSecondField: true);
            PubSubJsonNetworkMessage first = CreateJsonMessage(firstMetaData);
            PubSubJsonNetworkMessage changed = CreateJsonMessage(changedMetaData);
            PubSubJsonEncoder encoder = new()
            {
                EnableSchemaExchange = true,
                SchemaProvider = new DeterministicJsonSchemaProvider(),
                DestinationId = "subscriber-a"
            };

            _ = await encoder.EncodeAsync(first, CreateContext(100, firstMetaData));
            JsonSchemaAnnouncement? firstAnnouncement = encoder.LastSchemaAnnouncement;
            _ = await encoder.EncodeAsync(changed, CreateContext(100, changedMetaData));
            JsonSchemaAnnouncement? changedAnnouncement = encoder.LastSchemaAnnouncement;

            Assert.Multiple(() =>
            {
                Assert.That(firstAnnouncement, Is.Not.Null);
                Assert.That(changedAnnouncement, Is.Not.Null);
                Assert.That(changedAnnouncement!.SchemaId, Is.Not.EqualTo(firstAnnouncement!.SchemaId));
            });
        }

        /// <summary>
        /// Verifies enabled JSON schema exchange without a provider is a no-op.
        /// </summary>
        /// <returns>A task that represents the asynchronous test.</returns>
        [Test]
        public async Task JsonEncoderEnabledWithNullProviderProducesNoAnnouncementAndSameFrame()
        {
            DataSetMetaDataType metaData = CreateMetaData(includeSecondField: false);
            PubSubNetworkMessageContext context = CreateContext(100, metaData);
            PubSubJsonNetworkMessage message = CreateJsonMessage(metaData);
            PubSubJsonEncoder baseline = new();
            PubSubJsonEncoder encoder = new() { EnableSchemaExchange = true };

            ReadOnlyMemory<byte> baselineFrame = await baseline.EncodeAsync(message, context);
            ReadOnlyMemory<byte> frame = await encoder.EncodeAsync(message, context);

            Assert.Multiple(() =>
            {
                Assert.That(encoder.LastSchemaAnnouncement, Is.Null);
                Assert.That(frame.Span.SequenceEqual(baselineFrame.Span), Is.True);
            });
        }

        /// <summary>
        /// Verifies disabling JSON schema exchange clears the last pending announcement.
        /// </summary>
        /// <returns>A task that represents the asynchronous test.</returns>
        [Test]
        public async Task JsonEncoderDisablingSchemaExchangeClearsLastAnnouncement()
        {
            DataSetMetaDataType metaData = CreateMetaData(includeSecondField: false);
            PubSubJsonEncoder encoder = new()
            {
                EnableSchemaExchange = true,
                SchemaProvider = new DeterministicJsonSchemaProvider()
            };

            _ = await encoder.EncodeAsync(CreateJsonMessage(metaData), CreateContext(100, metaData));
            encoder.EnableSchemaExchange = false;

            Assert.That(encoder.LastSchemaAnnouncement, Is.Null);
        }

        /// <summary>
        /// Verifies JSON decoder ingest caches the announcement and rejects mismatched SchemaIds.
        /// </summary>
        [Test]
        public void JsonDecoderIngestCachesAndVerifiesSchemaId()
        {
            string schemaJson = "{\"type\":\"object\"}";
            JsonSchemaAnnouncement announcement = new(
                JsonSchemaAnnouncement.ComputeSchemaId(schemaJson),
                schemaJson,
                null);
            JsonSchemaAnnouncement bad = new(ByteString.From(1, 2, 3, 4, 5, 6, 7, 8), schemaJson, null);
            PubSubJsonDecoder decoder = new();

            typeof(PubSubJsonDecoder).GetMethod(nameof(PubSubJsonDecoder.Ingest))!.Invoke(
                decoder,
                [announcement]);

            Assert.Multiple(() =>
            {
                Assert.That(decoder.SchemaCache.TryGet(announcement.SchemaId, out SchemaCacheEntry entry), Is.True);
                Assert.That(entry.Format, Is.EqualTo(SchemaCache.JsonFormat));
                Assert.That(() => decoder.Ingest(bad), Throws.InvalidOperationException);
            });
        }

        /// <summary>
        /// Verifies JSON schema cache helpers normalize JSON format names and validate announcements.
        /// </summary>
        [Test]
        public void SchemaCacheJsonHelpersNormalizeAndValidate()
        {
            string schemaJson = "{\"type\":\"object\",\"properties\":{\"Temperature\":{\"type\":\"number\"}}}";
            ByteString schemaBytes = ByteString.From(System.Text.Encoding.UTF8.GetBytes(schemaJson));
            ByteString schemaId = SchemaCache.ComputeSchemaId(schemaBytes, " JSON ");
            JsonSchemaAnnouncement announcement = new(schemaId, schemaJson, null);
            SchemaCache cache = new();

            cache.Add(announcement);

            Assert.Multiple(() =>
            {
                Assert.That(schemaId, Is.EqualTo(JsonSchemaAnnouncement.ComputeSchemaId(schemaJson)));
                Assert.That(cache.TryGet(schemaId, out SchemaCacheEntry entry), Is.True);
                Assert.That(entry.Format, Is.EqualTo(SchemaCache.JsonFormat));
                Assert.That(() => cache.Add((JsonSchemaAnnouncement)null!), Throws.ArgumentNullException);
                Assert.That(
                    () => SchemaCache.ComputeSchemaId(schemaBytes, "unsupported"),
                    Throws.ArgumentException);
            });
        }

        /// <summary>
        /// Verifies that a resolver returning bytes inconsistent with the requested SchemaId is
        /// treated as "schema not available" (<see cref="SchemaCache.TryGetOrResolve"/> returns
        /// <c>false</c>) rather than letting the verification exception escape a Try* call.
        /// </summary>
        [Test]
        public void SchemaCache_TryGetOrResolve_InconsistentResolverResult_ReturnsFalseWithoutThrowing()
        {
            SchemaCache cache = new();
            ByteString requested = ByteString.From(1, 2, 3, 4, 5, 6, 7, 8);
            ByteString inconsistentSchema = ByteString.From(System.Text.Encoding.UTF8.GetBytes("{\"name\":\"Other\"}"));
            InconsistentResolver resolver = new(inconsistentSchema, SchemaCache.AvroFormat);

            bool available = true;
            Assert.That(() => available = cache.TryGetOrResolve(requested, resolver, out _), Throws.Nothing);
            Assert.That(available, Is.False);
        }

        private static AvroNetworkMessage CreateAvroMessage(string schemaId, bool includeSecondField)
        {
            return new AvroNetworkMessage
            {
                PublisherId = PublisherId.FromString("publisher"),
                WriterGroupId = 1,
                DataSetClassId = new Uuid(new Guid("8c2f8f1c-c9a1-48b0-a90b-7d8f6e725001")),
                SchemaId = schemaId,
                MetaData = CreateMetaData(includeSecondField),
                DataSetMessages = [CreateAvroDataSetMessage(includeSecondField)]
            };
        }

        private static PubSubJsonNetworkMessage CreateJsonMessage(DataSetMetaDataType metaData)
        {
            return new PubSubJsonNetworkMessage
            {
                PublisherId = PublisherId.FromString("publisher"),
                WriterGroupId = 1,
                DataSetClassId = new Uuid(new Guid("8c2f8f1c-c9a1-48b0-a90b-7d8f6e725001")),
                MetaData = metaData,
                DataSetMessages =
                [
                    new PubSubJsonDataSetMessage
                    {
                        DataSetWriterId = 100,
                        MessageType = PubSubDataSetMessageType.KeyFrame,
                        MetaDataVersion = new ConfigurationVersionDataType { MajorVersion = 1, MinorVersion = 0 }
                    }
                ]
            };
        }

#if NET8_0_OR_GREATER && !NET_STANDARD_TESTS
        private static ArrowNetworkMessage CreateArrowMessage(string schemaId, bool includeSecondField)
        {
            return new ArrowNetworkMessage
            {
                PublisherId = PublisherId.FromString("publisher"),
                WriterGroupId = 1,
                DataSetClassId = new Uuid(new Guid("8c2f8f1c-c9a1-48b0-a90b-7d8f6e725001")),
                SchemaId = schemaId,
                MetaData = CreateMetaData(includeSecondField),
                DataSetMessages = [CreateArrowDataSetMessage(includeSecondField)]
            };
        }
#endif

        private static AvroDataSetMessage CreateAvroDataSetMessage(bool includeSecondField)
        {
            DataSetField[] fields = includeSecondField
                ?
                [
                    new DataSetField
                    {
                        Name = "Temperature",
                        Value = new Variant(23.5),
                        Encoding = PubSubFieldEncoding.RawData
                    },
                    new DataSetField
                    {
                        Name = "Enabled",
                        Value = new Variant(true),
                        Encoding = PubSubFieldEncoding.RawData
                    }
                ]
                :
                [
                    new DataSetField
                    {
                        Name = "Temperature",
                        Value = new Variant(23.5),
                        Encoding = PubSubFieldEncoding.RawData
                    }
                ];
            AvroDataSetMessage message = new()
            {
                DataSetWriterId = 100,
                MessageType = PubSubDataSetMessageType.KeyFrame,
                MetaDataVersion = new ConfigurationVersionDataType { MajorVersion = 1, MinorVersion = 0 },
                Fields = fields
            };
            return message;
        }

#if NET8_0_OR_GREATER && !NET_STANDARD_TESTS
        private static ArrowDataSetMessage CreateArrowDataSetMessage(bool includeSecondField)
        {
            DataSetField[] fields = includeSecondField
                ?
                [
                    new DataSetField
                    {
                        Name = "Temperature",
                        Value = new Variant(23.5),
                        Encoding = PubSubFieldEncoding.RawData
                    },
                    new DataSetField
                    {
                        Name = "Enabled",
                        Value = new Variant(true),
                        Encoding = PubSubFieldEncoding.RawData
                    }
                ]
                :
                [
                    new DataSetField
                    {
                        Name = "Temperature",
                        Value = new Variant(23.5),
                        Encoding = PubSubFieldEncoding.RawData
                    }
                ];
            ArrowDataSetMessage message = new()
            {
                DataSetWriterId = 100,
                MessageType = PubSubDataSetMessageType.KeyFrame,
                MetaDataVersion = new ConfigurationVersionDataType { MajorVersion = 1, MinorVersion = 0 },
                Fields = fields
            };
            return message;
        }
#endif

        private static DataSetMetaDataType CreateMetaData(bool includeSecondField)
        {
            FieldMetaData[] fields = includeSecondField
                ?
                [
                    new FieldMetaData
                    {
                        Name = "Temperature",
                        BuiltInType = (byte)BuiltInType.Double,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData
                    {
                        Name = "Enabled",
                        BuiltInType = (byte)BuiltInType.Boolean,
                        ValueRank = ValueRanks.Scalar
                    }
                ]
                :
                [
                    new FieldMetaData
                    {
                        Name = "Temperature",
                        BuiltInType = (byte)BuiltInType.Double,
                        ValueRank = ValueRanks.Scalar
                    }
                ];
            return new DataSetMetaDataType
            {
                Name = "SchemaExchangeDataSet",
                ConfigurationVersion = new ConfigurationVersionDataType { MajorVersion = 1, MinorVersion = 0 },
                Fields = fields
            };
        }

        private static DataSetMetaDataType CreateArrayAndNodeIdMetaData()
        {
            return new DataSetMetaDataType
            {
                Name = "ArrayAndNodeIdDataSet",
                ConfigurationVersion = new ConfigurationVersionDataType { MajorVersion = 1, MinorVersion = 0 },
                Fields =
                [
                    new FieldMetaData
                    {
                        Name = "ScalarBuiltIn",
                        BuiltInType = (byte)BuiltInType.Double,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData
                    {
                        Name = "ArrayBuiltIn",
                        BuiltInType = (byte)BuiltInType.Int32,
                        ValueRank = ValueRanks.OneDimension,
                        ArrayDimensions = [3]
                    },
                    new FieldMetaData
                    {
                        Name = "NodeIdField",
                        DataType = DataTypeIds.String,
                        BuiltInType = (byte)BuiltInType.Null,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData
                    {
                        Name = "EnumField",
                        DataType = new NodeId(5000, 1),
                        BuiltInType = (byte)BuiltInType.Null,
                        ValueRank = ValueRanks.Scalar
                    }
                ]
            };
        }

        private static UaTypeDescription CreateEnumDescription()
        {
            var definition = new EnumDefinition
            {
                Fields =
                [
                    new EnumField { Name = "Red", Value = 0 },
                    new EnumField { Name = "Green", Value = 1 }
                ]
            };
            return new UaTypeDescription(
                new ExpandedNodeId(new NodeId(5000, 1)),
                new QualifiedName("JsonSchemaColor", 1),
                definition,
                "urn:opcua:pubsub:json-schema");
        }

        private static PubSubNetworkMessageContext CreateContext(ushort writerId, DataSetMetaDataType metaData)
        {
            DataSetMetaDataRegistry registry = new();
            PublisherId publisherId = PublisherId.FromString("publisher");
            Uuid dataSetClassId = new(new Guid("8c2f8f1c-c9a1-48b0-a90b-7d8f6e725001"));
            DataSetMetaDataKey key = new(publisherId, 1, writerId, dataSetClassId, 1);
            registry.Register(in key, metaData);
            return new PubSubNetworkMessageContext(
                ServiceMessageContext.CreateEmpty(null!),
                registry,
                new PubSubDiagnostics(PubSubDiagnosticsLevel.High),
                TimeProvider.System);
        }

        private sealed class CountingResolver : ISchemaResolver
        {
            private readonly ByteString _schemaId;
            private readonly ByteString _schema;
            private readonly string _format;

            internal CountingResolver(ByteString schemaId, ByteString schema, string format)
            {
                _schemaId = schemaId;
                _schema = schema;
                _format = format;
            }

            internal int CallCount { get; private set; }

            public bool TryResolve(ByteString schemaId, out (ByteString schema, string format) result)
            {
                CallCount++;
                if (schemaId.Span.SequenceEqual(_schemaId.Span))
                {
                    result = (_schema, _format);
                    return true;
                }
                result = default;
                return false;
            }
        }

        private sealed class InconsistentResolver : ISchemaResolver
        {
            private readonly ByteString _schema;
            private readonly string _format;

            internal InconsistentResolver(ByteString schema, string format)
            {
                _schema = schema;
                _format = format;
            }

            public bool TryResolve(ByteString schemaId, out (ByteString schema, string format) result)
            {
                result = (_schema, _format);
                return true;
            }
        }

        private sealed class DeterministicJsonSchemaProvider : IDataSetJsonSchemaProvider
        {
            public string CreateJsonSchema(DataSetMetaDataType metaData, bool verbose = false)
            {
                using System.IO.MemoryStream stream = new();
                using (System.Text.Json.Utf8JsonWriter writer = new(stream))
                {
                    writer.WriteStartObject();
                    writer.WriteString("type", "object");
                    writer.WriteBoolean("verbose", verbose);
                    writer.WriteStartArray("fields");
                    if (!metaData.Fields.IsNull)
                    {
                        for (int i = 0; i < metaData.Fields.Count; i++)
                        {
                            writer.WriteStringValue(metaData.Fields[i].Name ?? string.Empty);
                        }
                    }
                    writer.WriteEndArray();
                    writer.WriteEndObject();
                }
                return System.Text.Encoding.UTF8.GetString(stream.ToArray());
            }
        }
    }
}
