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
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json.Nodes;
using Opc.Ua.Schema;
using Opc.Ua.Schema.Json;

namespace Opc.Ua.PubSub.Schema
{
    /// <summary>
    /// Default PubSub schema provider that generates JSON Schema documents for per-DataSet payload objects.
    /// </summary>
    public sealed class PubSubSchemaProvider : IPubSubSchemaProvider
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PubSubSchemaProvider"/> class.
        /// </summary>
        /// <param name="schemaProvider">Optional type schema provider used for complex field data types.</param>
        /// <param name="resolver">Optional data type definition resolver used for complex field data types.</param>
        public PubSubSchemaProvider(
            ISchemaProvider? schemaProvider = null,
            IDataTypeDefinitionResolver? resolver = null)
        {
            m_schemaProvider = schemaProvider;
            m_resolver = resolver;
        }

        /// <inheritdoc/>
        public IUaSchema CreateDataSetSchema(
            DataSetMetaDataType metaData,
            DataSetFieldContentMask fieldContentMask,
            bool verbose = false)
        {
            if (metaData is null)
            {
                throw new ArgumentNullException(nameof(metaData));
            }

            UaSchemaFormat format = verbose ? UaSchemaFormat.JsonVerbose : UaSchemaFormat.JsonCompact;
            var definitions = new JsonObject();
            var properties = new JsonObject();
            var required = new List<JsonNode?>();
            ArrayOf<FieldMetaData> fields = metaData.Fields;
            if (!fields.IsNull)
            {
                for (int i = 0; i < fields.Count; i++)
                {
                    FieldMetaData field = fields[i];
                    string fieldName = FieldName(field, i);
                    properties[fieldName] = CreateFieldSchema(field, fieldContentMask, format, verbose, definitions);
                    required.Add(fieldName);
                }
            }

            string dataSetName = string.IsNullOrEmpty(metaData.Name) ? DefaultDataSetName : metaData.Name!;
            string documentId = CreateDocumentId(dataSetName);
            var root = new JsonObject
            {
                ["$schema"] = JsonSchemaDialect,
                ["$id"] = documentId,
                ["title"] = dataSetName,
                ["type"] = "object",
                ["properties"] = properties,
                ["additionalProperties"] = false
            };
            if (required.Count > 0)
            {
                root["required"] = new JsonArray(required.ToArray());
            }
            if (definitions.Count > 0)
            {
                root["$defs"] = definitions;
            }

            return new JsonSchemaDocument(format, documentId, root);
        }

        /// <inheritdoc/>
        public IUaSchema CreateDataSetMessageSchema(
            DataSetMetaDataType metaData,
            JsonDataSetMessageContentMask messageContentMask,
            DataSetFieldContentMask fieldContentMask,
            bool verbose = false)
        {
            if (metaData is null)
            {
                throw new ArgumentNullException(nameof(metaData));
            }

            UaSchemaFormat format = verbose ? UaSchemaFormat.JsonVerbose : UaSchemaFormat.JsonCompact;
            string dataSetName = DataSetName(metaData);
            string documentId = CreateDocumentId("dataset-message", dataSetName);
            JsonObject root = CreateDataSetMessageRoot(
                metaData,
                messageContentMask,
                fieldContentMask,
                verbose,
                dataSetName,
                documentId);

            return new JsonSchemaDocument(format, documentId, root);
        }

        /// <inheritdoc/>
        public IUaSchema CreateNetworkMessageSchema(
            DataSetMetaDataType metaData,
            JsonNetworkMessageContentMask networkContentMask,
            JsonDataSetMessageContentMask messageContentMask,
            DataSetFieldContentMask fieldContentMask,
            bool verbose = false)
        {
            if (metaData is null)
            {
                throw new ArgumentNullException(nameof(metaData));
            }

            UaSchemaFormat format = verbose ? UaSchemaFormat.JsonVerbose : UaSchemaFormat.JsonCompact;
            string dataSetName = DataSetName(metaData);
            string documentId = CreateDocumentId("ua-data", dataSetName);
            string dataSetMessageId = CreateDocumentId("dataset-message", dataSetName);
            var definitions = new JsonObject
            {
                ["DataSetMessage"] = CreateDataSetMessageRoot(
                    metaData,
                    messageContentMask,
                    fieldContentMask,
                    verbose,
                    dataSetName,
                    dataSetMessageId)
            };
            var properties = new JsonObject
            {
                ["MessageType"] = Const(JsonNetworkMessageTypeData),
                ["Messages"] = CreateMessagesSchema(networkContentMask)
            };
            if ((networkContentMask & JsonNetworkMessageContentMask.NetworkMessageHeader) != 0)
            {
                properties["MessageId"] = new JsonObject { ["type"] = "string" };
            }
            if ((networkContentMask & JsonNetworkMessageContentMask.PublisherId) != 0)
            {
                properties["PublisherId"] = PublisherIdSchema();
            }
            if ((networkContentMask & JsonNetworkMessageContentMask.WriterGroupName) != 0)
            {
                properties["WriterGroupName"] = new JsonObject { ["type"] = "string" };
            }
            if ((networkContentMask & JsonNetworkMessageContentMask.DataSetClassId) != 0)
            {
                properties["DataSetClassId"] = new JsonObject { ["type"] = "string", ["format"] = "uuid" };
            }
            if ((networkContentMask & JsonNetworkMessageContentMask.ReplyTo) != 0)
            {
                properties["ReplyTo"] = ArrayOf(new JsonObject { ["type"] = "string" });
            }

            JsonObject root = CreateObjectDocument(
                documentId,
                dataSetName + " ua-data NetworkMessage",
                properties,
                s_networkMessageRequired);
            root["$defs"] = definitions;

            return new JsonSchemaDocument(format, documentId, root);
        }

        /// <inheritdoc/>
        public IUaSchema CreateMetaDataMessageSchema(
            DataSetMetaDataType metaData,
            bool verbose = false)
        {
            if (metaData is null)
            {
                throw new ArgumentNullException(nameof(metaData));
            }

            UaSchemaFormat format = verbose ? UaSchemaFormat.JsonVerbose : UaSchemaFormat.JsonCompact;
            string dataSetName = DataSetName(metaData);
            string documentId = CreateDocumentId("ua-metadata", dataSetName);
            var properties = new JsonObject
            {
                ["MessageId"] = new JsonObject { ["type"] = "string" },
                ["MessageType"] = Const(JsonNetworkMessageTypeMetaData),
                ["PublisherId"] = PublisherIdSchema(),
                ["DataSetWriterId"] = Integer(ushort.MinValue, ushort.MaxValue),
                ["DataSetClassId"] = new JsonObject { ["type"] = "string", ["format"] = "uuid" },
                ["MetaData"] = new JsonObject
                {
                    ["type"] = "object",
                    ["additionalProperties"] = true
                }
            };
            JsonObject root = CreateObjectDocument(
                documentId,
                dataSetName + " ua-metadata message",
                properties,
                s_metaDataMessageRequired);

            return new JsonSchemaDocument(format, documentId, root);
        }

        private JsonObject CreateFieldSchema(
            FieldMetaData field,
            DataSetFieldContentMask fieldContentMask,
            UaSchemaFormat format,
            bool verbose,
            JsonObject definitions)
        {
            JsonObject rawSchema = ApplyValueRank(
                () => CreateElementSchema(field, format, verbose, definitions),
                field.ValueRank);
            if (IsRawDataMask(fieldContentMask))
            {
                return rawSchema;
            }
            return CreateDataValueSchema(rawSchema, fieldContentMask, verbose);
        }

        private JsonObject CreateElementSchema(
            FieldMetaData field,
            UaSchemaFormat format,
            bool verbose,
            JsonObject definitions)
        {
            BuiltInType builtInType = GetBuiltInType(field);
            if (builtInType != BuiltInType.Null)
            {
                return CreateBuiltInSchema(builtInType, verbose, definitions);
            }

            return CreateComplexTypeSchema(field.DataType, format, definitions);
        }

        private JsonObject CreateDataSetMessageRoot(
            DataSetMetaDataType metaData,
            JsonDataSetMessageContentMask messageContentMask,
            DataSetFieldContentMask fieldContentMask,
            bool verbose,
            string dataSetName,
            string documentId)
        {
            var properties = new JsonObject
            {
                ["MessageType"] = DataSetMessageTypeSchema(),
                ["Payload"] = CreatePayloadSchema(metaData, fieldContentMask, verbose)
            };
            if ((messageContentMask & JsonDataSetMessageContentMask.DataSetWriterId) != 0)
            {
                properties["DataSetWriterId"] = Integer(ushort.MinValue, ushort.MaxValue);
            }
            if ((messageContentMask & JsonDataSetMessageContentMask.DataSetWriterName) != 0)
            {
                properties["DataSetWriterName"] = new JsonObject { ["type"] = "string" };
            }
            if ((messageContentMask & JsonDataSetMessageContentMask.PublisherId) != 0)
            {
                properties["PublisherId"] = PublisherIdSchema();
            }
            if ((messageContentMask & JsonDataSetMessageContentMask.WriterGroupName) != 0)
            {
                properties["WriterGroupName"] = new JsonObject { ["type"] = "string" };
            }
            if ((messageContentMask & JsonDataSetMessageContentMask.SequenceNumber) != 0)
            {
                properties["SequenceNumber"] = Integer(uint.MinValue, uint.MaxValue);
            }
            if ((messageContentMask & JsonDataSetMessageContentMask.MetaDataVersion) != 0)
            {
                properties["MetaDataVersion"] = DefinitionObject(new JsonObject
                {
                    ["MajorVersion"] = Integer(uint.MinValue, uint.MaxValue),
                    ["MinorVersion"] = Integer(uint.MinValue, uint.MaxValue)
                });
            }
            if ((messageContentMask & JsonDataSetMessageContentMask.Timestamp) != 0)
            {
                properties["Timestamp"] = DateTimeSchema();
            }
            if ((messageContentMask & JsonDataSetMessageContentMask.Status) != 0)
            {
                properties["Status"] = Integer(uint.MinValue, uint.MaxValue);
            }
            if ((messageContentMask & JsonDataSetMessageContentMask.MinorVersion) != 0)
            {
                properties["MinorVersion"] = Integer(uint.MinValue, uint.MaxValue);
            }

            return CreateObjectDocument(
                documentId,
                dataSetName + " DataSetMessage",
                properties,
                s_dataSetMessageRequired);
        }

        private JsonObject CreatePayloadSchema(
            DataSetMetaDataType metaData,
            DataSetFieldContentMask fieldContentMask,
            bool verbose)
        {
            JsonNode? node = JsonNode.Parse(CreateDataSetSchema(metaData, fieldContentMask, verbose).ToSchemaString());
            return node?.AsObject() ?? throw new InvalidOperationException("The generated DataSet schema is empty.");
        }

        private JsonObject CreateComplexTypeSchema(
            NodeId dataType,
            UaSchemaFormat format,
            JsonObject definitions)
        {
            if (dataType.IsNull)
            {
                return new JsonObject();
            }

            if (m_resolver is not null && m_resolver.TryResolve(dataType, out UaTypeDescription? description))
            {
                return CreateTypeReference(description.TypeId, description.Name, format, definitions);
            }

            return CreateTypeReference(new ExpandedNodeId(dataType), dataType.ToString(), format, definitions);
        }

        private JsonObject CreateTypeReference(
            ExpandedNodeId typeId,
            string keyHint,
            UaSchemaFormat format,
            JsonObject definitions)
        {
            if (m_schemaProvider is null || typeId.IsNull)
            {
                return new JsonObject();
            }

            if (!m_schemaProvider.TryGetSchema(typeId, format, UaSchemaScope.Type, out IUaSchema? schema)
                || schema is null)
            {
                return new JsonObject();
            }

            string key = DefinitionKey(keyHint);
            if (!definitions.ContainsKey(key))
            {
                definitions[key] = JsonNode.Parse(schema.ToSchemaString())?.AsObject() ?? new JsonObject();
            }
            return Ref(key);
        }

        private static JsonObject CreateDataValueSchema(
            JsonObject valueSchema,
            DataSetFieldContentMask fieldContentMask,
            bool verbose)
        {
            var properties = new JsonObject
            {
                ["Value"] = valueSchema
            };
            if ((fieldContentMask & DataSetFieldContentMask.StatusCode) != 0)
            {
                properties["StatusCode"] = CreateBuiltInSchema(BuiltInType.StatusCode, verbose, new JsonObject());
            }
            if ((fieldContentMask & DataSetFieldContentMask.SourceTimestamp) != 0)
            {
                properties["SourceTimestamp"] = DateTimeSchema();
            }
            if ((fieldContentMask & DataSetFieldContentMask.SourcePicoSeconds) != 0)
            {
                properties["SourcePicoseconds"] = Integer(ushort.MinValue, ushort.MaxValue);
            }
            if ((fieldContentMask & DataSetFieldContentMask.ServerTimestamp) != 0)
            {
                properties["ServerTimestamp"] = DateTimeSchema();
            }
            if ((fieldContentMask & DataSetFieldContentMask.ServerPicoSeconds) != 0)
            {
                properties["ServerPicoseconds"] = Integer(ushort.MinValue, ushort.MaxValue);
            }

            var required = new List<JsonNode?> { "Value" };
            return new JsonObject
            {
                ["type"] = "object",
                ["properties"] = properties,
                ["required"] = new JsonArray(required.ToArray()),
                ["additionalProperties"] = false
            };
        }

        private static BuiltInType GetBuiltInType(FieldMetaData field)
        {
            var builtInType = (BuiltInType)field.BuiltInType;
            if (builtInType != BuiltInType.Null)
            {
                return builtInType;
            }
            return TypeInfo.GetBuiltInType(field.DataType);
        }

        private static JsonObject CreateBuiltInSchema(BuiltInType type, bool verbose, JsonObject definitions)
        {
            switch (type)
            {
                case BuiltInType.Boolean:
                    return new JsonObject { ["type"] = "boolean" };
                case BuiltInType.SByte:
                    return Integer(sbyte.MinValue, sbyte.MaxValue);
                case BuiltInType.Byte:
                    return Integer(byte.MinValue, byte.MaxValue);
                case BuiltInType.Int16:
                    return Integer(short.MinValue, short.MaxValue);
                case BuiltInType.UInt16:
                    return Integer(ushort.MinValue, ushort.MaxValue);
                case BuiltInType.Int32:
                    return Integer(int.MinValue, int.MaxValue);
                case BuiltInType.UInt32:
                    return Integer(uint.MinValue, uint.MaxValue);
                case BuiltInType.Int64:
                    return IntegerString(signed: true);
                case BuiltInType.UInt64:
                    return IntegerString(signed: false);
                case BuiltInType.Float:
                case BuiltInType.Double:
                case BuiltInType.Number:
                    return TypeArray("number", "string");
                case BuiltInType.Integer:
                case BuiltInType.UInteger:
                    return TypeArray("integer", "string");
                case BuiltInType.String:
                    return new JsonObject { ["type"] = "string" };
                case BuiltInType.DateTime:
                    return DateTimeSchema();
                case BuiltInType.Guid:
                    return new JsonObject { ["type"] = "string", ["format"] = "uuid" };
                case BuiltInType.ByteString:
                    return new JsonObject { ["type"] = "string", ["contentEncoding"] = "base64" };
                case BuiltInType.XmlElement:
                    return new JsonObject { ["type"] = "string" };
                case BuiltInType.Enumeration:
                    return new JsonObject { ["type"] = "integer" };
                case BuiltInType.StatusCode:
                    return verbose ? StatusCodeObject() : Integer(uint.MinValue, uint.MaxValue);
                case BuiltInType.NodeId:
                case BuiltInType.ExpandedNodeId:
                case BuiltInType.QualifiedName:
                case BuiltInType.LocalizedText:
                case BuiltInType.Variant:
                case BuiltInType.ExtensionObject:
                case BuiltInType.DataValue:
                case BuiltInType.DiagnosticInfo:
                    return CreateStandardReference(type, definitions);
                default:
                    return new JsonObject();
            }
        }

        private static JsonObject CreateStandardReference(BuiltInType type, JsonObject definitions)
        {
            string key = "Ua_" + type;
            if (!definitions.ContainsKey(key))
            {
                definitions[key] = type switch
                {
                    BuiltInType.NodeId => StandardNodeId(),
                    BuiltInType.ExpandedNodeId => StandardExpandedNodeId(),
                    BuiltInType.QualifiedName => StandardQualifiedName(),
                    BuiltInType.LocalizedText => StandardLocalizedText(),
                    BuiltInType.StatusCode => StatusCodeObject(),
                    BuiltInType.Variant => new JsonObject { ["type"] = "object" },
                    BuiltInType.ExtensionObject => new JsonObject { ["type"] = "object" },
                    BuiltInType.DataValue => new JsonObject { ["type"] = "object" },
                    BuiltInType.DiagnosticInfo => new JsonObject { ["type"] = "object" },
                    _ => new JsonObject()
                };
            }
            return Ref(key);
        }

        private static JsonObject ApplyValueRank(Func<JsonObject> elementFactory, int valueRank)
        {
            switch (valueRank)
            {
                case ValueRanks.Scalar:
                    return elementFactory();
                case ValueRanks.Any:
                case ValueRanks.ScalarOrOneDimension:
                    var options = new List<JsonNode?>
                    {
                        elementFactory(),
                        ArrayOf(elementFactory())
                    };
                    return new JsonObject
                    {
                        ["oneOf"] = new JsonArray(options.ToArray())
                    };
                case ValueRanks.OneOrMoreDimensions:
                    return ArrayOf(elementFactory());
                default:
                    JsonObject node = elementFactory();
                    for (int i = 0; i < valueRank; i++)
                    {
                        node = ArrayOf(node);
                    }
                    return node;
            }
        }

        private static JsonObject ArrayOf(JsonObject items)
        {
            return new JsonObject
            {
                ["type"] = "array",
                ["items"] = items
            };
        }

        private static JsonObject DateTimeSchema()
        {
            return new JsonObject { ["type"] = "string", ["format"] = "date-time" };
        }

        private static JsonObject Const(string value)
        {
            return new JsonObject { ["const"] = value };
        }

        private static JsonObject CreateMessagesSchema(JsonNetworkMessageContentMask networkContentMask)
        {
            if ((networkContentMask & JsonNetworkMessageContentMask.SingleDataSetMessage) != 0)
            {
                return new JsonObject
                {
                    ["type"] = "object",
                    ["$ref"] = "#/$defs/DataSetMessage"
                };
            }

            return ArrayOf(Ref("DataSetMessage"));
        }

        private static JsonObject CreateObjectDocument(
            string documentId,
            string title,
            JsonObject properties,
            string[] required)
        {
            var requiredNodes = new List<JsonNode?>(required.Length);
            foreach (string name in required)
            {
                requiredNodes.Add(name);
            }
            return new JsonObject
            {
                ["$schema"] = JsonSchemaDialect,
                ["$id"] = documentId,
                ["title"] = title,
                ["type"] = "object",
                ["properties"] = properties,
                ["required"] = new JsonArray(requiredNodes.ToArray()),
                ["additionalProperties"] = false
            };
        }

        private static JsonObject DataSetMessageTypeSchema()
        {
            var values = new List<JsonNode?>
            {
                JsonDataSetMessageTypeKeyFrame,
                JsonDataSetMessageTypeDeltaFrame
            };
            return new JsonObject { ["enum"] = new JsonArray(values.ToArray()) };
        }

        private static JsonObject DefinitionObject(JsonObject properties, params string[] required)
        {
            var schema = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = properties,
                ["additionalProperties"] = false
            };
            if (required.Length > 0)
            {
                var requiredNodes = new List<JsonNode?>(required.Length);
                foreach (string name in required)
                {
                    requiredNodes.Add(name);
                }
                schema["required"] = new JsonArray(requiredNodes.ToArray());
            }
            return schema;
        }

        private static JsonObject Integer(long minimum, long maximum)
        {
            return new JsonObject
            {
                ["type"] = "integer",
                ["minimum"] = minimum,
                ["maximum"] = maximum
            };
        }

        private static JsonObject IntegerString(bool signed)
        {
            return new JsonObject
            {
                ["type"] = "string",
                ["pattern"] = signed ? "^-?\\d+$" : "^\\d+$"
            };
        }

        private static bool IsRawDataMask(DataSetFieldContentMask fieldContentMask)
        {
            return fieldContentMask is DataSetFieldContentMask.None or DataSetFieldContentMask.RawData;
        }

        private static JsonObject Ref(string defName)
        {
            return new JsonObject { ["$ref"] = "#/$defs/" + defName };
        }

        private static JsonObject PublisherIdSchema()
        {
            return new JsonObject { ["type"] = "string" };
        }

        private static JsonObject StandardExpandedNodeId()
        {
            return DefinitionObject(new JsonObject
            {
                ["IdType"] = Integer(byte.MinValue, 3),
                ["Id"] = TypeArray("string", "integer"),
                ["Namespace"] = TypeArray("string", "integer"),
                ["ServerUri"] = TypeArray("string", "integer")
            }, "Id");
        }

        private static JsonObject StandardLocalizedText()
        {
            return DefinitionObject(new JsonObject
            {
                ["Locale"] = new JsonObject { ["type"] = "string" },
                ["Text"] = new JsonObject { ["type"] = "string" }
            });
        }

        private static JsonObject StandardNodeId()
        {
            return DefinitionObject(new JsonObject
            {
                ["IdType"] = Integer(byte.MinValue, 3),
                ["Id"] = TypeArray("string", "integer"),
                ["Namespace"] = TypeArray("string", "integer")
            }, "Id");
        }

        private static JsonObject StandardQualifiedName()
        {
            return DefinitionObject(new JsonObject
            {
                ["Name"] = new JsonObject { ["type"] = "string" },
                ["Uri"] = TypeArray("string", "integer")
            }, "Name");
        }

        private static JsonObject StatusCodeObject()
        {
            return DefinitionObject(new JsonObject
            {
                ["Code"] = Integer(uint.MinValue, uint.MaxValue),
                ["Symbol"] = new JsonObject { ["type"] = "string" }
            });
        }

        private static JsonObject TypeArray(string first, string second)
        {
            var types = new List<JsonNode?> { first, second };
            return new JsonObject { ["type"] = new JsonArray(types.ToArray()) };
        }

        private static string CreateDocumentId(string dataSetName)
        {
            return "urn:opcua:pubsub:dataset:" + Uri.EscapeDataString(dataSetName) + ".schema.json";
        }

        private static string CreateDocumentId(string kind, string dataSetName)
        {
            return "urn:opcua:pubsub:" + kind + ":" + Uri.EscapeDataString(dataSetName) + ".schema.json";
        }

        private static string DataSetName(DataSetMetaDataType metaData)
        {
            return string.IsNullOrEmpty(metaData.Name) ? DefaultDataSetName : metaData.Name!;
        }

        private static string DefinitionKey(string keyHint)
        {
            if (string.IsNullOrEmpty(keyHint))
            {
                return "Type";
            }

            char[] buffer = new char[keyHint.Length];
            int count = 0;
            for (int i = 0; i < keyHint.Length; i++)
            {
                char c = keyHint[i];
                buffer[count++] = char.IsLetterOrDigit(c) ? c : '_';
            }
            return new string(buffer, 0, count);
        }

        private static string FieldName(FieldMetaData field, int index)
        {
            if (!string.IsNullOrEmpty(field.Name))
            {
                return field.Name!;
            }
            return string.Format(CultureInfo.InvariantCulture, "Field{0}", index);
        }

        private const string DefaultDataSetName = "DataSet";
        private const string JsonSchemaDialect = "https://json-schema.org/draft/2020-12/schema";
        private const string JsonDataSetMessageTypeDeltaFrame = "ua-deltaframe";
        private const string JsonDataSetMessageTypeKeyFrame = "ua-keyframe";
        private const string JsonNetworkMessageTypeData = "ua-data";
        private const string JsonNetworkMessageTypeMetaData = "ua-metadata";

        private static readonly string[] s_dataSetMessageRequired = { "MessageType", "Payload" };
        private static readonly string[] s_metaDataMessageRequired = { "MessageType", "MetaData" };
        private static readonly string[] s_networkMessageRequired = { "MessageType", "Messages" };

        private readonly ISchemaProvider? m_schemaProvider;
        private readonly IDataTypeDefinitionResolver? m_resolver;
    }
}
