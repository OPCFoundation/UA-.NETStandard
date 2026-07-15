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
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.MetaData;

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

#if NET8_0_OR_GREATER
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

#if NET8_0_OR_GREATER
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

#if NET8_0_OR_GREATER
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
    }
}
