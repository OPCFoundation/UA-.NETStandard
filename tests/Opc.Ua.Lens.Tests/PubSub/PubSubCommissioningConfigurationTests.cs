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
using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.MetaData;
using UaLens.Plugins.PubSub;

namespace UaLens.Tests.PubSub
{
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class PubSubCommissioningConfigurationTests
    {
        [TestCase(1, "1024", true)]
        [TestCase(1, "1025", false)]
        [TestCase(2, "32,32", true)]
        [TestCase(2, "32,33", false)]
        [TestCase(2, "0,1024", true)]
        [TestCase(2, "3", false)]
        [TestCase(0, "", false)]
        [TestCase(9, "", false)]
        [TestCase(-1, "1", false)]
        [TestCase(2, "-1,3", false)]
        public void FieldShapeBoundsAreValidatedBeforeAnyRuntimeConstruction(int rank, string dimensions, bool allowed)
        {
            var draft = new PubSubFieldDraft(new PubSubFieldConfiguration { Name = "Value" })
            {
                ValueRank = rank,
                Dimensions = dimensions
            };
            if (allowed)
            {
                PubSubFieldConfiguration field = draft.ToConfiguration();
                Assert.That(field.ValueRank, Is.EqualTo(rank));
                Assert.That(PubSubConfigurationValidation.Inspect(
                    PubSubTestRuntime.Configuration with { Fields = [field] }).IsEmpty, Is.True);
            }
            else
            {
                Assert.That(() => draft.ToConfiguration(), Throws.TypeOf<ArgumentException>());
            }
        }

        [TestCase(false, BuiltInType.Int32, 1)]
        [TestCase(true, BuiltInType.Int32, 1)]
        [TestCase(false, BuiltInType.NodeId, -1)]
        [TestCase(true, BuiltInType.QualifiedName, -1)]
        public void ExtendedSchemasCannotDisableMetadataVersionHeaders(bool json, BuiltInType type, int rank)
        {
            PubSubConfiguration configuration = PubSubTestRuntime.Configuration with
            {
                Profile = json ? PubSubProfile.MqttJson : PubSubProfile.UdpUadp,
                Fields = [new() { Name = "Value", Type = type, ValueRank = rank }],
                UadpDataSetMask = UadpDataSetMessageContentMask.Status,
                JsonDataSetMask = JsonDataSetMessageContentMask.DataSetWriterId
            };
            Assert.That(PubSubContentMasks.Inspect(configuration).ToList().Select(issue => issue.Detail),
                Has.Some.Contains("metadata-version"));
        }

        [Test]
        public void StructureMetadataRetainsPortableTypeDefinitionsWithoutStartingAWorkload()
        {
            var metadata = new DataSetMetaDataType
            {
                Name = "Structured data",
                Namespaces = [Namespaces.OpcUa, "urn:ualens:test:pubsub-model"],
                ConfigurationVersion = new ConfigurationVersionDataType { MajorVersion = 7, MinorVersion = 3 },
                Fields = [new FieldMetaData
                {
                    Name = "Reading", DataType = new NodeId(5001u, 1), BuiltInType = (byte)BuiltInType.ExtensionObject,
                    ValueRank = ValueRanks.Scalar, DataSetFieldId = new Uuid(s_fieldId)
                }],
                StructureDataTypes = [new StructureDescription
                {
                    DataTypeId = new NodeId(5001u, 1), Name = new QualifiedName("ReadingType", 1),
                    StructureDefinition = new StructureDefinition
                    {
                        BaseDataType = DataTypeIds.Structure, DefaultEncodingId = new NodeId(5002u, 1),
                        StructureType = StructureType.Structure,
                        Fields = [new StructureField { Name = "Temperature", DataType = DataTypeIds.Double,
                            ValueRank = ValueRanks.Scalar }]
                    }
                }]
            };
            var key = new DataSetMetaDataKey(PublisherId.FromUInt16(7), 17, 31, Uuid.Empty, 7);
            var schema = PubSubMetadataSchema.Create(key, metadata);
            Assert.That(schema.CanApply, Is.True, schema.Prerequisite);
            PubSubConfiguration captured = PubSubStateCodec.Restore(
                PubSubStateCodec.Capture(schema.Apply(PubSubTestRuntime.Configuration)));
            metadata.StructureDataTypes[0].StructureDefinition.Fields[0].Name = "Mutated";
            DataSetMetaDataType restored = PubSubStackConfiguration.CreateMetadata(captured);
            Assert.That(restored.Namespaces, Is.EqualTo((ArrayOf<string>)
                [Namespaces.OpcUa, "urn:ualens:test:pubsub-model"]));
            Assert.That(restored.StructureDataTypes, Has.Count.EqualTo(1));
            Assert.That(restored.StructureDataTypes[0].StructureDefinition.Fields[0].Name, Is.EqualTo("Temperature"));
            Assert.That(restored.Fields[0].DataType, Is.EqualTo(new NodeId(5001u, 1)));
            Assert.That(captured.Publication, Is.EqualTo(PubSubPublication.Disabled));
            Assert.That(captured.WriteBackEnabled, Is.False);
        }

        [TestCase(1UL, BuiltInType.Byte)]
        [TestCase(255UL, BuiltInType.Byte)]
        [TestCase(256UL, BuiltInType.UInt16)]
        [TestCase(65535UL, BuiltInType.UInt16)]
        [TestCase(65536UL, BuiltInType.UInt32)]
        [TestCase(4294967295UL, BuiltInType.UInt32)]
        [TestCase(4294967296UL, BuiltInType.UInt64)]
        [TestCase(ulong.MaxValue, BuiltInType.UInt64)]
        public void NumericIdentityKeepsUadpWidthAndUsesCanonicalJsonWidth(ulong number, BuiltInType jsonType)
        {
            Assert.That(PubSubIdentity.TryCreate(PublisherIdType.UInt64, number, string.Empty, false, out Variant uadp),
                Is.True);
            Assert.That(uadp.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.UInt64));
            Assert.That(uadp.TryGetValue(out ulong retained), Is.True);
            Assert.That(retained, Is.EqualTo(number));
            Assert.That(PubSubIdentity.TryCreate(PublisherIdType.UInt64, number, string.Empty, true, out Variant json),
                Is.True);
            Assert.That(json.TypeInfo.BuiltInType, Is.EqualTo(jsonType));
            Assert.That(PublisherId.From(json).ToString(),
                Is.EqualTo(number.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        [TestCase(PublisherIdType.Byte, 255UL, true)]
        [TestCase(PublisherIdType.Byte, 256UL, false)]
        [TestCase(PublisherIdType.UInt16, 65535UL, true)]
        [TestCase(PublisherIdType.UInt16, 65536UL, false)]
        [TestCase(PublisherIdType.UInt32, 4294967295UL, true)]
        [TestCase(PublisherIdType.UInt32, 4294967296UL, false)]
        [TestCase(PublisherIdType.UInt64, 0UL, false)]
        [TestCase((PublisherIdType)99, 1UL, false)]
        public void NumericIdentityRejectsZeroAndWidthOverflow(PublisherIdType type, ulong number, bool accepted)
        {
            bool result = PubSubIdentity.TryCreate(type, number, string.Empty, false, out Variant value);
            Assert.That(result, Is.EqualTo(accepted));
            Assert.That(value.IsNull, Is.EqualTo(!accepted));
            if (accepted)
            {
                Assert.That(PublisherId.From(value).Type, Is.EqualTo(type));
            }
        }

        [TestCase("publisher", true, true)]
        [TestCase("123", true, false)]
        [TestCase("123", false, true)]
        [TestCase("644b2f80-a39c-46c2-9ef0-df124061ba6e", true, false)]
        [TestCase("", false, false)]
        [TestCase(" ", false, false)]
        [TestCase("publisher\n", false, false)]
        public void TextIdentityCannotChangeItsMeaningDuringJsonDecoding(string text, bool json, bool accepted)
        {
            Assert.That(PubSubIdentity.TryCreate(PublisherIdType.String, 0, text, json, out Variant value),
                Is.EqualTo(accepted));
            if (accepted)
            {
                Assert.That(value.TryGetValue(out string retained), Is.True);
                Assert.That(retained, Is.EqualTo(text));
            }
            else
            {
                Assert.That(value.IsNull, Is.True);
            }
        }

        [TestCase(96, true)]
        [TestCase(97, false)]
        public void PublisherNameHasAnExactBoundAndGuidRequiresJson(int length, bool accepted)
        {
            Assert.That(PubSubIdentity.TryCreate(PublisherIdType.String, 0, new string('p', length), false, out _),
                Is.EqualTo(accepted));
            string name = s_classId.ToString("D");
            Assert.That(PubSubIdentity.TryCreate(PublisherIdType.Guid, 0, name, false, out _), Is.False);
            Assert.That(PubSubIdentity.TryCreate(PublisherIdType.Guid, 0, name, true, out Variant guid), Is.True);
            Assert.That(guid.TryGetValue(out Uuid actual), Is.True);
            Assert.That((Guid)actual, Is.EqualTo(s_classId));
        }

        [TestCase("unknown-network")]
        [TestCase("unknown-field")]
        [TestCase("missing-publisher")]
        [TestCase("network-picoseconds")]
        [TestCase("dataset-picoseconds")]
        [TestCase("source-picoseconds")]
        [TestCase("server-picoseconds")]
        [TestCase("raw-with-quality")]
        [TestCase("legacy-raw-conflict")]
        public void InvalidMaskCombinationsFailBeforeStackConstruction(string fault)
        {
            PubSubConfiguration configuration = fault switch
            {
                "unknown-network" => PubSubTestRuntime.Configuration with
                {
                    UadpNetworkMask = (UadpNetworkMessageContentMask)uint.MaxValue
                },
                "unknown-field" => PubSubTestRuntime.Configuration with
                {
                    FieldContentMask = (DataSetFieldContentMask)128
                },
                "missing-publisher" => PubSubTestRuntime.Configuration with
                {
                    UadpNetworkMask = PubSubContentMasks.DefaultUadpNetwork & ~UadpNetworkMessageContentMask.PublisherId
                },
                "network-picoseconds" => PubSubTestRuntime.Configuration with
                {
                    UadpNetworkMask = PubSubContentMasks.DefaultUadpNetwork | UadpNetworkMessageContentMask.PicoSeconds
                },
                "dataset-picoseconds" => PubSubTestRuntime.Configuration with
                {
                    UadpDataSetMask = UadpDataSetMessageContentMask.PicoSeconds
                },
                "source-picoseconds" => PubSubTestRuntime.Configuration with
                {
                    FieldContentMask = DataSetFieldContentMask.SourcePicoSeconds
                },
                "server-picoseconds" => PubSubTestRuntime.Configuration with
                {
                    FieldContentMask = DataSetFieldContentMask.ServerPicoSeconds
                },
                "raw-with-quality" => PubSubTestRuntime.Configuration with
                {
                    FieldContentMask = DataSetFieldContentMask.RawData | DataSetFieldContentMask.StatusCode
                },
                "legacy-raw-conflict" => PubSubTestRuntime.Configuration with
                {
                    RawDataEncoding = true,
                    FieldContentMask = DataSetFieldContentMask.ServerTimestamp
                },
                _ => throw new ArgumentOutOfRangeException(nameof(fault))
            };
            Assert.That(PubSubContentMasks.Inspect(configuration).IsEmpty, Is.False);
            Assert.That(() => PubSubStackConfiguration.Build(configuration), Throws.ArgumentException);
            Assert.That(() => PubSubStateCodec.Capture(configuration), Throws.ArgumentException);
        }

        [Test]
        public void StackProjectionRetainsFieldIdsMetadataVersionsIdentitiesAndMasks()
        {
            PubSubConfiguration configuration = PubSubTestRuntime.Configuration with
            {
                Publication = PubSubPublication.Synthetic,
                LocalPublisherIdType = PublisherIdType.UInt64,
                LocalPublisherId = 4294967296UL,
                PublisherFilterType = PublisherIdType.String,
                PublisherFilterName = "peer",
                DataSetClassId = s_classId,
                MetadataMajorVersion = 17,
                MetadataMinorVersion = 3,
                Fields = [new() { Name = "Temperature", Type = BuiltInType.Double, FieldId = s_fieldId }],
                UadpNetworkMask = PubSubContentMasks.DefaultUadpNetwork | UadpNetworkMessageContentMask.Timestamp,
                FieldContentMask = DataSetFieldContentMask.RawData
            };
            PubSubConfiguration restored = PubSubStateCodec.Restore(PubSubStateCodec.Capture(configuration));
            PubSubConfigurationDataType stack = PubSubStackConfiguration.Build(restored);
            PubSubConnectionDataType connection = stack.Connections[0];
            Assert.That(connection.PublisherId.TryGetValue(out ulong local), Is.True);
            Assert.That(local, Is.EqualTo(4294967296UL));
            DataSetReaderDataType reader = connection.ReaderGroups[0].DataSetReaders[0];
            Assert.That(reader.PublisherId.TryGetValue(out string peer), Is.True);
            Assert.That(peer, Is.EqualTo("peer"));
            Assert.That((Guid)reader.DataSetMetaData.DataSetClassId, Is.EqualTo(s_classId));
            Assert.That((Guid)reader.DataSetMetaData.Fields[0].DataSetFieldId, Is.EqualTo(s_fieldId));
            Assert.That(reader.DataSetMetaData.ConfigurationVersion.MajorVersion, Is.EqualTo(17u));
            Assert.That(reader.DataSetMetaData.ConfigurationVersion.MinorVersion, Is.EqualTo(3u));
            Assert.That(reader.DataSetFieldContentMask, Is.EqualTo((uint)DataSetFieldContentMask.RawData));
            Assert.That(connection.WriterGroups[0].MessageSettings.TryGetValue(
                out UadpWriterGroupMessageDataType? settings),
                Is.True);
            Assert.That(settings!.NetworkMessageContentMask, Is.EqualTo((uint)configuration.UadpNetworkMask));
            Assert.That(stack.PublishedDataSets[0].DataSetMetaData.Fields[0].Name, Is.EqualTo("Temperature"));
        }

        [Test]
        public void ArrayAndMatrixMetadataRetainsShapeThroughReadOnlyAdoptionAndPersistence()
        {
            DataSetMetaDataType metadata = Metadata();
            metadata.Fields[0].ValueRank = 2;
            metadata.Fields[0].ArrayDimensions = [2u, 3u];
            metadata.Fields =
            [
                metadata.Fields[0],
                new FieldMetaData
                {
                    Name = "Flags", BuiltInType = (byte)BuiltInType.Boolean, DataType = DataTypeIds.Boolean,
                    ValueRank = 1, ArrayDimensions = [4u]
                }
            ];
            var key = new DataSetMetaDataKey(PublisherId.FromUInt16(7), 17, 31, s_classId, 7);
            var schema = PubSubMetadataSchema.Create(key, metadata);
            Assert.That(schema.CanApply, Is.True, schema.Prerequisite);
            PubSubConfiguration applied = schema.Apply(PubSubTestRuntime.Configuration);
            metadata.Fields[0].ArrayDimensions = [9u, 9u];
            PubSubConfiguration restored = PubSubStateCodec.Restore(PubSubStateCodec.Capture(applied));
            DataSetMetaDataType projected = PubSubStackConfiguration.CreateMetadata(restored);
            Assert.That(projected.Fields[0].ValueRank, Is.EqualTo(2));
            Assert.That(projected.Fields[0].ArrayDimensions, Is.EqualTo((ArrayOf<uint>)[2u, 3u]));
            Assert.That(projected.Fields[0].DataSetFieldId, Is.EqualTo(new Uuid(s_fieldId)));
            Assert.That(projected.Fields[1].ValueRank, Is.EqualTo(1));
            Assert.That(projected.Fields[1].ArrayDimensions, Is.EqualTo((ArrayOf<uint>)[4u]));
            Assert.That(restored.Publication, Is.EqualTo(PubSubPublication.Disabled));
            Assert.That(restored.WriteBackEnabled, Is.False);
        }

        [Test]
        public void FieldDraftRetainsMatrixShapeRatherThanRevertingToScalar()
        {
            var field = new PubSubFieldConfiguration
            {
                Name = "Matrix",
                Type = BuiltInType.Double,
                ValueRank = 2,
                ArrayDimensions = [2u, 3u]
            };
            var draft = new PubSubFieldDraft(field);
            PubSubFieldConfiguration captured = draft.ToConfiguration();
            Assert.That(captured.ValueRank, Is.EqualTo(2));
            Assert.That(captured.ArrayDimensions, Is.EqualTo((ArrayOf<uint>)[2u, 3u]));
        }

        [Test]
        public void MetadataAdoptionCreatesReadOnlyIntentAndClearsEveryUaMapping()
        {
            DataSetMetaDataType metadata = Metadata();
            var key = new DataSetMetaDataKey(PublisherId.FromUInt32(123456), 17, 31, s_classId, 7);
            var schema = PubSubMetadataSchema.Create(key, metadata);
            Assert.That(schema.CanApply, Is.True, schema.Prerequisite);
            PubSubConfiguration active = PubSubTestRuntime.Configuration with
            {
                Publication = PubSubPublication.ServerSource,
                WriteBackEnabled = true,
                ActionResponderEnabled = true,
                AdapterProviderId = "primary",
                ActionObjectNodeId = "i=2253",
                ActionMethodNodeId = "i=11492",
                Fields = [new() { Name = "Old", SourceNodeId = "i=1", TargetNodeId = "i=2" }]
            };
            PubSubConfiguration applied = schema.Apply(active);
            metadata.Fields[0].Name = "Later mutation";
            Assert.That(applied.Fields[0].Name, Is.EqualTo("Temperature"));
            Assert.That(applied.Fields[0].Type, Is.EqualTo(BuiltInType.Double));
            Assert.That(applied.Fields[0].FieldId, Is.EqualTo(s_fieldId));
            Assert.That(applied.Fields[0].SourceNodeId, Is.Empty);
            Assert.That(applied.Fields[0].TargetNodeId, Is.Empty);
            Assert.That(applied.LocalPublisherId, Is.EqualTo(active.LocalPublisherId));
            Assert.That(applied.PublisherFilter, Is.EqualTo(123456UL));
            Assert.That(applied.PublisherFilterType, Is.EqualTo(PublisherIdType.UInt32));
            Assert.That(applied.WriterGroupId, Is.EqualTo(17));
            Assert.That(applied.DataSetWriterId, Is.EqualTo(31));
            Assert.That(applied.ReceiveEnabled, Is.True);
            Assert.That(applied.Publication, Is.EqualTo(PubSubPublication.Disabled));
            Assert.That(applied.WriteBackEnabled, Is.False);
            Assert.That(applied.ActionResponderEnabled, Is.False);
            Assert.That(applied.AdapterProviderId, Is.Empty);
            Assert.That(applied.ActionObjectNodeId, Is.Empty);
            Assert.That(applied.ActionMethodNodeId, Is.Empty);
            Assert.That(active.WriteBackEnabled, Is.True);
        }

        [TestCase("empty")]
        [TestCase("too-many")]
        [TestCase("array")]
        [TestCase("custom-type")]
        [TestCase("unnamed")]
        [TestCase("duplicate")]
        [TestCase("promoted")]
        [TestCase("oversized-string")]
        public void UnsupportedMetadataRemainsVisibleButCannotProduceALossyScalarConfiguration(string fault)
        {
            DataSetMetaDataType metadata = Metadata();
            FieldMetaData field = metadata.Fields[0];
            switch (fault)
            {
                case "empty":
                    metadata.Fields = [];
                    break;
                case "too-many":
                    metadata.Fields = [.. Enumerable.Repeat(field, 33)];
                    break;
                case "array":
                    field.ValueRank = 1;
                    field.ArrayDimensions = [3u, 3u];
                    break;
                case "custom-type":
                    field.DataType = new NodeId(5000u, 2);
                    break;
                case "unnamed":
                    field.Name = null;
                    break;
                case "duplicate":
                    metadata.Fields = [field, CoreUtils.Clone(field)!];
                    break;
                case "promoted":
                    field.FieldFlags = 1;
                    break;
                case "oversized-string":
                    field.MaxStringLength = PubSubConfigurationValidation.MaxValueCharacters + 1u;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(fault));
            }
            var key = new DataSetMetaDataKey(PublisherId.FromUInt16(7), 17, 31, s_classId, 7);
            var schema = PubSubMetadataSchema.Create(key, metadata);
            Assert.That(schema.CanApply, Is.False);
            Assert.That(schema.Prerequisite, Is.Not.Empty);
            Assert.That(() => schema.Apply(PubSubTestRuntime.Configuration), Throws.InvalidOperationException);
        }

        [TestCase(/*lang=json,strict*/ "{\"publication\":\"Disabled\",\"publication\":\"Synthetic\"}")]
        [TestCase(/*lang=json,strict*/ "{\"fields\":[{\"name\":\"A\",\"Name\":\"B\"}]}")]
        [TestCase(/*lang=json,strict*/ "{\"autoStart\":true}")]
        [TestCase(/*lang=json,strict*/ "{\"password\":\"not-a-secret\"}")]
        public void DuplicateOrUnknownConfigurationMembersCannotOverrideReviewedIntent(string json)
        {
            Assert.That(() => PubSubStateCodec.Parse(json), Throws.TypeOf<JsonException>());
        }

        private static DataSetMetaDataType Metadata()
        {
            return new DataSetMetaDataType
            {
                Name = "Sample",
                DataSetClassId = new Uuid(s_classId),
                ConfigurationVersion = new ConfigurationVersionDataType { MajorVersion = 7, MinorVersion = 3 },
                Fields =
                [
                    new FieldMetaData
                    {
                        Name = "Temperature", BuiltInType = (byte)BuiltInType.Double, DataType = DataTypeIds.Double,
                        ValueRank = ValueRanks.Scalar, DataSetFieldId = new Uuid(s_fieldId)
                    }
                ]
            };
        }

        private static readonly Guid s_classId = new("644b2f80-a39c-46c2-9ef0-df124061ba6e");
        private static readonly Guid s_fieldId = new("6b128cc8-4159-42c8-b650-faf208f1e272");
    }
}
