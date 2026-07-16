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
using System.Linq;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Schema;
using Opc.Ua.Schema.Json;

namespace Opc.Ua.PubSub.Schema.Tests
{
    /// <summary>
    /// Exercises PubSub JSON schema generation branches that are not covered by envelope validation tests.
    /// </summary>
    [TestFixture]
    public class PubSubSchemaCoverageTests
    {
        [Test]
        public void CreateDataSetSchemaTreatsNoneAndRawDataAsRawValues()
        {
            var provider = new PubSubSchemaProvider();

            JsonObject noneRoot = CreateDataSetRoot(provider, CreateBuiltInMetaData(), DataSetFieldContentMask.None);
            JsonObject rawRoot = CreateDataSetRoot(provider, CreateBuiltInMetaData(), DataSetFieldContentMask.RawData);

            Assert.Multiple(() =>
            {
                Assert.That(noneRoot["properties"]!["Int64Value"]!["type"]!.GetValue<string>(), Is.EqualTo("string"));
                Assert.That(rawRoot["properties"]!["UInt64Value"]!["pattern"]!.GetValue<string>(), Is.EqualTo("^\\d+$"));
                Assert.That(noneRoot["properties"]!["Int64Value"]!.AsObject().ContainsKey("properties"), Is.False);
                Assert.That(rawRoot["properties"]!["FloatValue"]!["type"]!.AsArray(), Has.Count.EqualTo(2));
            });
        }

        [Test]
        public void CreateDataSetSchemaWrapsEveryDataValueFieldContentMaskMember()
        {
            var provider = new PubSubSchemaProvider();
            const DataSetFieldContentMask mask = DataSetFieldContentMask.StatusCode |
                DataSetFieldContentMask.SourceTimestamp |
                DataSetFieldContentMask.SourcePicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.ServerPicoSeconds;

            JsonObject root = CreateDataSetRoot(provider, CreateBuiltInMetaData(), mask);
            JsonObject value = root["properties"]!["Int64Value"]!.AsObject();
            JsonObject members = value["properties"]!.AsObject();

            Assert.Multiple(() =>
            {
                Assert.That(value["type"]!.GetValue<string>(), Is.EqualTo("object"));
                Assert.That(members.ContainsKey("Value"), Is.True);
                Assert.That(members.ContainsKey("StatusCode"), Is.True);
                Assert.That(members.ContainsKey("SourceTimestamp"), Is.True);
                Assert.That(members.ContainsKey("SourcePicoseconds"), Is.True);
                Assert.That(members.ContainsKey("ServerTimestamp"), Is.True);
                Assert.That(members.ContainsKey("ServerPicoseconds"), Is.True);
                Assert.That(value["required"]!.AsArray().Select(static n => n!.GetValue<string>()),
                    Is.EqualTo(s_valueRequired));
                Assert.That(value["additionalProperties"]!.GetValue<bool>(), Is.False);
            });
        }

        [Test]
        public void CreateDataSetSchemaUsesVerboseStatusCodeObjectAndCompactIntegerStatusCode()
        {
            var provider = new PubSubSchemaProvider();
            const DataSetFieldContentMask mask = DataSetFieldContentMask.StatusCode;

            var compact = (JsonSchemaDocument)provider.CreateDataSetSchema(
                CreateBuiltInMetaData(),
                mask);
            var verbose = (JsonSchemaDocument)provider.CreateDataSetSchema(
                CreateBuiltInMetaData(),
                mask,
                verbose: true);
            JsonObject compactStatus = compact.Root["properties"]!["Int64Value"]!["properties"]!["StatusCode"]!.AsObject();
            JsonObject verboseStatus = verbose.Root["properties"]!["Int64Value"]!["properties"]!["StatusCode"]!.AsObject();

            Assert.Multiple(() =>
            {
                Assert.That(compact.Format, Is.EqualTo(UaSchemaFormat.JsonCompact));
                Assert.That(verbose.Format, Is.EqualTo(UaSchemaFormat.JsonVerbose));
                Assert.That(compactStatus["type"]!.GetValue<string>(), Is.EqualTo("integer"));
                Assert.That(verboseStatus["type"]!.GetValue<string>(), Is.EqualTo("object"));
                Assert.That(verboseStatus["properties"]!["Code"]!["type"]!.GetValue<string>(), Is.EqualTo("integer"));
            });
        }

        [Test]
        public void CreateDataSetSchemaMapsRepresentativeBuiltInTypes()
        {
            var provider = new PubSubSchemaProvider();

            JsonObject root = CreateDataSetRoot(provider, CreateBuiltInMetaData(), DataSetFieldContentMask.RawData);
            JsonObject properties = root["properties"]!.AsObject();

            Assert.Multiple(() =>
            {
                Assert.That(properties["Int64Value"]!["pattern"]!.GetValue<string>(), Is.EqualTo("^-?\\d+$"));
                Assert.That(properties["UInt64Value"]!["pattern"]!.GetValue<string>(), Is.EqualTo("^\\d+$"));
                Assert.That(properties["FloatValue"]!["type"]!.AsArray().Select(static n => n!.GetValue<string>()),
                    Is.EqualTo(s_numberTypes));
                Assert.That(properties["DoubleValue"]!["type"]!.AsArray().Select(static n => n!.GetValue<string>()),
                    Is.EqualTo(s_numberTypes));
                Assert.That(properties["Bytes"]!["contentEncoding"]!.GetValue<string>(), Is.EqualTo("base64"));
                Assert.That(properties["Timestamp"]!["format"]!.GetValue<string>(), Is.EqualTo("date-time"));
                Assert.That(properties["GuidValue"]!["format"]!.GetValue<string>(), Is.EqualTo("uuid"));
                Assert.That(properties["Xml"]!["type"]!.GetValue<string>(), Is.EqualTo("string"));
                Assert.That(properties["EnumValue"]!["type"]!.GetValue<string>(), Is.EqualTo("integer"));
                Assert.That(properties["NumberValue"]!["type"]!.AsArray().Select(static n => n!.GetValue<string>()),
                    Is.EqualTo(s_numberTypes));
                Assert.That(properties["UIntegerValue"]!["type"]!.AsArray().Select(static n => n!.GetValue<string>()),
                    Is.EqualTo(s_integerTypes));
            });
        }

        [Test]
        public void CreateDataSetSchemaAddsDefinitionsForStandardObjectTypes()
        {
            var provider = new PubSubSchemaProvider();

            JsonObject root = CreateDataSetRoot(provider, CreateStandardObjectMetaData(), DataSetFieldContentMask.RawData);
            JsonObject definitions = root["$defs"]!.AsObject();

            Assert.Multiple(() =>
            {
                AssertStandardReference(root, "Node", "Ua_NodeId");
                AssertStandardReference(root, "Expanded", "Ua_ExpandedNodeId");
                AssertStandardReference(root, "Qualified", "Ua_QualifiedName");
                AssertStandardReference(root, "Localized", "Ua_LocalizedText");
                AssertStandardReference(root, "VariantValue", "Ua_Variant");
                AssertStandardReference(root, "Extension", "Ua_ExtensionObject");
                AssertStandardReference(root, "DataValue", "Ua_DataValue");
                AssertStandardReference(root, "Diagnostic", "Ua_DiagnosticInfo");
                Assert.That(definitions["Ua_NodeId"]!["properties"]!["Id"]!["type"]!.AsArray(), Has.Count.EqualTo(2));
                Assert.That(definitions["Ua_LocalizedText"]!["properties"]!["Text"]!["type"]!.GetValue<string>(),
                    Is.EqualTo("string"));
            });
        }

        [Test]
        public void CreateDataSetSchemaAppliesArrayAndAnyValueRanks()
        {
            var provider = new PubSubSchemaProvider();

            JsonObject root = CreateDataSetRoot(provider, CreateArrayMetaData(), DataSetFieldContentMask.RawData);
            JsonObject oneDimension = root["properties"]!["OneDimension"]!.AsObject();
            JsonObject twoDimensions = root["properties"]!["TwoDimensions"]!.AsObject();
            JsonObject any = root["properties"]!["AnyRank"]!.AsObject();
            JsonObject scalarOrArray = root["properties"]!["ScalarOrArray"]!.AsObject();
            JsonObject oneOrMore = root["properties"]!["OneOrMore"]!.AsObject();

            Assert.Multiple(() =>
            {
                Assert.That(oneDimension["type"]!.GetValue<string>(), Is.EqualTo("array"));
                Assert.That(twoDimensions["items"]!["type"]!.GetValue<string>(), Is.EqualTo("array"));
                Assert.That(any["oneOf"]!.AsArray(), Has.Count.EqualTo(2));
                Assert.That(scalarOrArray["oneOf"]!.AsArray(), Has.Count.EqualTo(2));
                Assert.That(oneOrMore["type"]!.GetValue<string>(), Is.EqualTo("array"));
            });
        }

        [Test]
        public void CreateDataSetSchemaResolvesComplexTypeThroughInjectedProviderAndResolver()
        {
            var registry = new DataTypeDefinitionRegistry();
            UaTypeDescription type = CreateStructureDescription();
            registry.Add(type);
            IUaSchemaGenerator generator = CreateJsonSchemaGenerator();
            var schemaProvider = new DefaultSchemaProvider(registry, [generator]);
            var provider = new PubSubSchemaProvider(schemaProvider, registry);

            JsonObject root = CreateDataSetRoot(provider, CreateComplexMetaData(), DataSetFieldContentMask.RawData);

            Assert.Multiple(() =>
            {
                Assert.That(root["properties"]!["Complex"]!["$ref"]!.GetValue<string>(),
                    Is.EqualTo("#/$defs/ComplexRecord"));
                Assert.That(root["$defs"]!["ComplexRecord"], Is.Not.Null);
            });
        }

        [Test]
        public void CreateDataSetSchemaHandlesFallbackNamesEmptyFieldsAndNullInputs()
        {
            var provider = new PubSubSchemaProvider();
            var unnamed = new DataSetMetaDataType
            {
                Fields =
                [
                    new FieldMetaData
                    {
                        BuiltInType = (byte)BuiltInType.Boolean,
                        DataType = DataTypeIds.Boolean,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData
                    {
                        Name = string.Empty,
                        BuiltInType = (byte)BuiltInType.Int32,
                        DataType = DataTypeIds.Int32,
                        ValueRank = ValueRanks.Scalar
                    }
                ]
            };

            JsonObject unnamedRoot = CreateDataSetRoot(provider, unnamed, DataSetFieldContentMask.RawData);
            JsonObject emptyRoot = CreateDataSetRoot(provider, new DataSetMetaDataType { Name = string.Empty },
                DataSetFieldContentMask.RawData);

            Assert.Multiple(() =>
            {
                Assert.That(unnamedRoot["title"]!.GetValue<string>(), Is.EqualTo("DataSet"));
                Assert.That(unnamedRoot["properties"]!.AsObject().ContainsKey("Field0"), Is.True);
                Assert.That(unnamedRoot["properties"]!.AsObject().ContainsKey("Field1"), Is.True);
                Assert.That(emptyRoot["properties"]!.AsObject(), Is.Empty);
                Assert.That(emptyRoot.AsObject().ContainsKey("required"), Is.False);
                Assert.That(() => provider.CreateDataSetSchema(null!, DataSetFieldContentMask.RawData),
                    Throws.ArgumentNullException);
                Assert.That(() => provider.CreateDataSetMessageSchema(null!, JsonDataSetMessageContentMask.None,
                    DataSetFieldContentMask.RawData), Throws.ArgumentNullException);
                Assert.That(() => provider.CreateNetworkMessageSchema(null!, JsonNetworkMessageContentMask.NetworkMessageHeader,
                    JsonDataSetMessageContentMask.None, DataSetFieldContentMask.RawData), Throws.ArgumentNullException);
                Assert.That(() => provider.CreateMetaDataMessageSchema(null!), Throws.ArgumentNullException);
            });
        }

        [Test]
        public void CreateEnvelopeSchemasIncludeAllOptionalMaskPropertiesAndDiExtensionRegistersDependencies()
        {
            var provider = new PubSubSchemaProvider();
            DataSetMetaDataType metaData = CreateBuiltInMetaData();
            const JsonDataSetMessageContentMask dataSetMask = JsonDataSetMessageContentMask.DataSetWriterId |
                JsonDataSetMessageContentMask.DataSetWriterName |
                JsonDataSetMessageContentMask.PublisherId |
                JsonDataSetMessageContentMask.WriterGroupName |
                JsonDataSetMessageContentMask.SequenceNumber |
                JsonDataSetMessageContentMask.MetaDataVersion |
                JsonDataSetMessageContentMask.Timestamp |
                JsonDataSetMessageContentMask.Status |
                JsonDataSetMessageContentMask.MinorVersion;
            const JsonNetworkMessageContentMask networkMask = JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.DataSetMessageHeader |
                JsonNetworkMessageContentMask.PublisherId |
                JsonNetworkMessageContentMask.WriterGroupName |
                JsonNetworkMessageContentMask.DataSetClassId |
                JsonNetworkMessageContentMask.ReplyTo;

            JsonObject dataSetMessage = ((JsonSchemaDocument)provider.CreateDataSetMessageSchema(
                metaData,
                dataSetMask,
                DataSetFieldContentMask.RawData)).Root;
            JsonObject networkMessage = ((JsonSchemaDocument)provider.CreateNetworkMessageSchema(
                metaData,
                networkMask,
                dataSetMask,
                DataSetFieldContentMask.RawData)).Root;
            JsonObject metaDataMessage = ((JsonSchemaDocument)provider.CreateMetaDataMessageSchema(metaData, verbose: true)).Root;
            ServiceProvider services = new ServiceCollection().AddOpcUa().AddPubSubSchema().Services.BuildServiceProvider();

            Assert.Multiple(() =>
            {
                Assert.That(dataSetMessage["properties"]!.AsObject().ContainsKey("DataSetWriterName"), Is.True);
                Assert.That(dataSetMessage["properties"]!.AsObject().ContainsKey("PublisherId"), Is.True);
                Assert.That(dataSetMessage["properties"]!["MetaDataVersion"]!["properties"]!["MajorVersion"], Is.Not.Null);
                Assert.That(networkMessage["properties"]!.AsObject().ContainsKey("WriterGroupName"), Is.True);
                Assert.That(networkMessage["properties"]!.AsObject().ContainsKey("ReplyTo"), Is.True);
                Assert.That(metaDataMessage["properties"]!["MetaData"]!["additionalProperties"]!.GetValue<bool>(), Is.True);
                Assert.That(services.GetRequiredService<IPubSubSchemaProvider>(), Is.TypeOf<PubSubSchemaProvider>());
            });
        }

        private static JsonObject CreateDataSetRoot(
            PubSubSchemaProvider provider,
            DataSetMetaDataType metaData,
            DataSetFieldContentMask mask)
        {
            return ((JsonSchemaDocument)provider.CreateDataSetSchema(metaData, mask)).Root;
        }

        private static DataSetMetaDataType CreateBuiltInMetaData()
        {
            return new DataSetMetaDataType
            {
                Name = "BuiltIns",
                Fields =
                [
                    Field("Int64Value", BuiltInType.Int64, DataTypeIds.Int64),
                    Field("UInt64Value", BuiltInType.UInt64, DataTypeIds.UInt64),
                    Field("FloatValue", BuiltInType.Float, DataTypeIds.Float),
                    Field("DoubleValue", BuiltInType.Double, DataTypeIds.Double),
                    Field("Bytes", BuiltInType.ByteString, DataTypeIds.ByteString),
                    Field("Timestamp", BuiltInType.DateTime, DataTypeIds.DateTime),
                    Field("GuidValue", BuiltInType.Guid, DataTypeIds.Guid),
                    Field("Xml", BuiltInType.XmlElement, DataTypeIds.XmlElement),
                    Field("EnumValue", BuiltInType.Enumeration, DataTypeIds.Enumeration),
                    Field("NumberValue", BuiltInType.Number, DataTypeIds.Number),
                    Field("UIntegerValue", BuiltInType.UInteger, DataTypeIds.UInteger)
                ]
            };
        }

        private static DataSetMetaDataType CreateStandardObjectMetaData()
        {
            return new DataSetMetaDataType
            {
                Name = "StandardObjects",
                Fields =
                [
                    Field("Node", BuiltInType.NodeId, DataTypeIds.NodeId),
                    Field("Expanded", BuiltInType.ExpandedNodeId, DataTypeIds.ExpandedNodeId),
                    Field("Qualified", BuiltInType.QualifiedName, DataTypeIds.QualifiedName),
                    Field("Localized", BuiltInType.LocalizedText, DataTypeIds.LocalizedText),
                    Field("VariantValue", BuiltInType.Variant, DataTypeIds.BaseDataType),
                    Field("Extension", BuiltInType.ExtensionObject, DataTypeIds.Structure),
                    Field("DataValue", BuiltInType.DataValue, DataTypeIds.BaseDataType),
                    Field("Diagnostic", BuiltInType.DiagnosticInfo, DataTypeIds.DiagnosticInfo)
                ]
            };
        }

        private static DataSetMetaDataType CreateArrayMetaData()
        {
            return new DataSetMetaDataType
            {
                Name = "Arrays",
                Fields =
                [
                    Field("OneDimension", BuiltInType.Boolean, DataTypeIds.Boolean, ValueRanks.OneDimension),
                    Field("TwoDimensions", BuiltInType.Int32, DataTypeIds.Int32, 2),
                    Field("AnyRank", BuiltInType.String, DataTypeIds.String, ValueRanks.Any),
                    Field("ScalarOrArray", BuiltInType.Double, DataTypeIds.Double, ValueRanks.ScalarOrOneDimension),
                    Field("OneOrMore", BuiltInType.Byte, DataTypeIds.Byte, ValueRanks.OneOrMoreDimensions)
                ]
            };
        }

        private static DataSetMetaDataType CreateComplexMetaData()
        {
            return new DataSetMetaDataType
            {
                Name = "ComplexDataSet",
                Fields =
                [
                    new FieldMetaData
                    {
                        Name = "Complex",
                        BuiltInType = (byte)BuiltInType.Null,
                        DataType = new NodeId(6001, 2),
                        ValueRank = ValueRanks.Scalar
                    }
                ]
            };
        }

        private static UaTypeDescription CreateStructureDescription()
        {
            var definition = new StructureDefinition
            {
                BaseDataType = DataTypeIds.Structure,
                StructureType = StructureType.Structure,
                Fields =
                [
                    new StructureField
                    {
                        Name = "Enabled",
                        DataType = DataTypeIds.Boolean,
                        ValueRank = ValueRanks.Scalar
                    },
                    new StructureField
                    {
                        Name = "Count",
                        DataType = DataTypeIds.Int32,
                        ValueRank = ValueRanks.Scalar
                    }
                ]
            };
            return new UaTypeDescription(
                new ExpandedNodeId(new NodeId(6001, 2)),
                new QualifiedName("ComplexRecord", 2),
                definition,
                "http://opcfoundation.org/UA/PubSub/SchemaTests");
        }

        private static FieldMetaData Field(
            string name,
            BuiltInType builtInType,
            NodeId dataType,
            int valueRank = ValueRanks.Scalar)
        {
            return new FieldMetaData
            {
                Name = name,
                BuiltInType = (byte)builtInType,
                DataType = dataType,
                ValueRank = valueRank
            };
        }

        private static void AssertStandardReference(JsonObject root, string propertyName, string definitionName)
        {
            Assert.That(root["properties"]![propertyName]!["$ref"]!.GetValue<string>(),
                Is.EqualTo("#/$defs/" + definitionName));
            Assert.That(root["$defs"]![definitionName], Is.Not.Null);
        }

        private static IUaSchemaGenerator CreateJsonSchemaGenerator()
        {
            Type generatorType = typeof(JsonSchemaDocument).Assembly.GetType(
                "Opc.Ua.Schema.Json.JsonSchemaGenerator",
                throwOnError: true)!;
            return (IUaSchemaGenerator)Activator.CreateInstance(generatorType, nonPublic: true)!;
        }

        private static readonly string[] s_valueRequired = ["Value"];
        private static readonly string[] s_numberTypes = ["number", "string"];
        private static readonly string[] s_integerTypes = ["integer", "string"];
    }
}
