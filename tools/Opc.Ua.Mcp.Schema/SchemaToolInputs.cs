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
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Opc.Ua.Bindings;
using Opc.Ua.Schema;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Selects the host's registered definitions or an explicitly selected current UA session.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<SchemaTypeSource>))]
    public enum SchemaTypeSource
    {
        /// <summary>
        /// Registered local definitions; no UA connection is used.
        /// </summary>
        Registered,

        /// <summary>
        /// The current session's DataTypeDefinition attributes.
        /// </summary>
        Session
    }

    /// <summary>
    /// Selects the supported PubSub JSON schema envelope.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<SchemaPubSubKind>))]
    public enum SchemaPubSubKind
    {
        /// <summary>
        /// The fields of a DataSet payload.
        /// </summary>
        Payload,

        /// <summary>
        /// One DataSetMessage with selected header fields.
        /// </summary>
        DataSetMessage,

        /// <summary>
        /// A JSON NetworkMessage with selected envelope and message fields.
        /// </summary>
        NetworkMessage,

        /// <summary>
        /// The existing SDK's metadata-message envelope, not a specialized metadata-body schema.
        /// </summary>
        MetaData
    }

    /// <summary>
    /// Bounded live-list options; host ceilings cannot be raised by tool arguments.
    /// </summary>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed class SchemaListInput
    {
        /// <summary>
        /// Gets or sets whether local or session types are enumerated.
        /// </summary>
        public SchemaTypeSource Source { get; set; }

        /// <summary>
        /// Gets or sets the identifier namespace filter; local enumeration defaults to the standard namespace.
        /// </summary>
        public string? NamespaceUri { get; set; }

        /// <summary>
        /// Gets or sets the remote HasSubtype browse root.
        /// </summary>
        public string RootTypeId { get; set; } = "i=24";

        /// <summary>
        /// Gets or sets the offset into the bounded live enumeration.
        /// </summary>
        public int Offset { get; set; }

        /// <summary>
        /// Gets or sets the maximum returned page size, 1..500 and not above the host type ceiling.
        /// </summary>
        public int MaxResults { get; set; } = 100;
    }

    /// <summary>
    /// Selects one definition without implicitly loading client-side types.
    /// </summary>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed class SchemaInspectInput
    {
        /// <summary>
        /// Gets or sets the required local or namespace-qualified DataType identifier.
        /// </summary>
        [Description(SchemaMcpFilters.TypeIdDescription)]
        public required string TypeId { get; set; }

        /// <summary>
        /// Gets or sets the explicitly selected definition source.
        /// </summary>
        public SchemaTypeSource Source { get; set; }
    }

    /// <summary>
    /// Explicit finite roots for client-side AOT-safe loading.
    /// </summary>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed class SchemaLoadInput
    {
        /// <summary>
        /// Gets or sets the required non-empty list of DataType identifiers.
        /// </summary>
        [Description(SchemaMcpFilters.TypeIdsDescription)]
        [JsonConverter(typeof(SchemaTypeIdArrayConverter))]
        public ArrayOf<string> TypeIds { get; set; } = [];
    }

    /// <summary>
    /// Selects an SDK format and either one root closure or the root's complete bounded namespace.
    /// </summary>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed class SchemaGenerateInput
    {
        /// <summary>
        /// Gets or sets the required root DataType identifier, also selecting the namespace for Namespace scope.
        /// </summary>
        [Description(SchemaMcpFilters.TypeIdDescription)]
        public required string TypeId { get; set; }

        /// <summary>
        /// Gets or sets the explicitly selected definition source.
        /// </summary>
        public SchemaTypeSource Source { get; set; }

        /// <summary>
        /// Gets or sets Xsd, Bsd, JsonCompact, or JsonVerbose.
        /// </summary>
        [JsonConverter(typeof(JsonStringEnumConverter<UaSchemaFormat>))]
        public UaSchemaFormat Format { get; set; } = UaSchemaFormat.JsonCompact;

        /// <summary>
        /// Gets or sets Type or Namespace generation.
        /// </summary>
        [JsonConverter(typeof(JsonStringEnumConverter<UaSchemaScope>))]
        public UaSchemaScope Scope { get; set; }
    }

    /// <summary>
    /// One explicitly named PubSub field; DataType determines the built-in kind without a contradictory hint.
    /// </summary>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed class SchemaPubSubField
    {
        /// <summary>
        /// Gets or sets the required payload field name.
        /// </summary>
        public required string Name { get; set; }

        /// <summary>
        /// Gets or sets the required DataType identifier.
        /// </summary>
        public required string DataTypeId { get; set; }

        /// <summary>
        /// Gets or sets the scalar or array rank, bounded by the host's maximum depth.
        /// </summary>
        public int ValueRank { get; set; } = ValueRanks.Scalar;
    }

    /// <summary>
    /// Explicit PubSub shape and masks, with inline fields or one session metadata Variable.
    /// </summary>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed class SchemaPubSubInput
    {
        /// <summary>
        /// Gets or sets the optional DataSetMetaData Variable identifier; mutually exclusive with inline fields.
        /// </summary>
        [Description(SchemaMcpFilters.MetadataNodeIdDescription)]
        public string? MetadataNodeId { get; set; }

        /// <summary>
        /// Gets or sets the inline DataSet name.
        /// </summary>
        public string Name { get; set; } = "DataSet";

        /// <summary>
        /// Gets or sets the explicitly typed inline fields.
        /// </summary>
        [Description(SchemaMcpFilters.FieldsDescription)]
        [JsonConverter(typeof(SchemaPubSubFieldArrayConverter))]
        public ArrayOf<SchemaPubSubField> Fields { get; set; } = [];

        /// <summary>
        /// Gets or sets the source for definitions referenced by inline fields.
        /// </summary>
        public SchemaTypeSource Source { get; set; }

        /// <summary>
        /// Gets or sets the schema envelope.
        /// </summary>
        public SchemaPubSubKind Kind { get; set; }

        /// <summary>
        /// Gets or sets the required DataSetFieldContentMask numeric bit mask.
        /// </summary>
        [Description("Explicit DataSetFieldContentMask bits: 0..63; RawData cannot combine with quality bits.")]
        public required uint FieldContentMask { get; set; }

        /// <summary>
        /// Gets or sets the explicit JsonDataSetMessageContentMask numeric bit mask.
        /// </summary>
        public required uint MessageContentMask { get; set; }

        /// <summary>
        /// Gets or sets the explicit JsonNetworkMessageContentMask numeric bit mask.
        /// </summary>
        public required uint NetworkContentMask { get; set; }

        /// <summary>
        /// Gets or sets whether verbose OPC UA JSON is described.
        /// </summary>
        public bool Verbose { get; set; }
    }

    /// <summary>
    /// Options for the existing session-independent OPC UA REST OpenAPI generator.
    /// </summary>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed class SchemaOpenApiInput
    {
        /// <summary>
        /// Gets or sets AllServices or Sessionless.
        /// </summary>
        [JsonConverter(typeof(JsonStringEnumConverter<WebApiServiceSet>))]
        public WebApiServiceSet ServiceSet { get; set; } = WebApiServiceSet.AllServices;

        /// <summary>
        /// Gets or sets whether a bounded request/response definition closure is emitted.
        /// </summary>
        public bool IncludeSchemas { get; set; }

        /// <summary>
        /// Gets or sets an informational server URL; it is never fetched or connected.
        /// </summary>
        public string? ServerUrl { get; set; }
    }

    /// <summary>
    /// Reflection-free binding for concrete PubSub field arrays.
    /// </summary>
    public sealed class SchemaPubSubFieldArrayConverter : JsonConverter<ArrayOf<SchemaPubSubField>>
    {
        /// <inheritdoc/>
        public override ArrayOf<SchemaPubSubField> Read(
            ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartArray)
            {
                throw new JsonException("fields must be an array.");
            }
            var fields = new List<SchemaPubSubField>();
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndArray)
                {
                    return [.. fields];
                }
                if (fields.Count == 1024)
                {
                    throw new JsonException("fields is limited to 1024 elements.");
                }
                fields.Add(JsonSerializer.Deserialize(ref reader, SchemaInputJsonContext.Default.SchemaPubSubField)
                    ?? throw new JsonException("A field cannot be null."));
            }
            throw new JsonException("The fields array is incomplete.");
        }

        /// <inheritdoc/>
        public override void Write(
            Utf8JsonWriter writer, ArrayOf<SchemaPubSubField> value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);
            writer.WriteStartArray();
            foreach (SchemaPubSubField field in value)
            {
                JsonSerializer.Serialize(writer, field, SchemaInputJsonContext.Default.SchemaPubSubField);
            }
            writer.WriteEndArray();
        }
    }

    /// <summary>
    /// Reflection-free binding for the explicitly bounded type-root array.
    /// </summary>
    public sealed class SchemaTypeIdArrayConverter : JsonConverter<ArrayOf<string>>
    {
        /// <inheritdoc/>
        public override ArrayOf<string> Read(
            ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartArray)
            {
                throw new JsonException("typeIds must be an array.");
            }
            var ids = new List<string>();
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndArray)
                {
                    return [.. ids];
                }
                if (ids.Count == 512 || reader.TokenType != JsonTokenType.String)
                {
                    throw new JsonException("typeIds requires at most 512 non-null string elements.");
                }
                ids.Add(reader.GetString()!);
            }
            throw new JsonException("The typeIds array is incomplete.");
        }

        /// <inheritdoc/>
        public override void Write(Utf8JsonWriter writer, ArrayOf<string> value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);
            writer.WriteStartArray();
            foreach (string id in value)
            {
                writer.WriteStringValue(id);
            }
            writer.WriteEndArray();
        }
    }

    /// <summary>
    /// Closed JSON metadata for all schema-tool input contracts.
    /// </summary>
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true, UseStringEnumConverter = true)]
    [JsonSerializable(typeof(SchemaListInput))]
    [JsonSerializable(typeof(SchemaInspectInput))]
    [JsonSerializable(typeof(SchemaLoadInput))]
    [JsonSerializable(typeof(SchemaGenerateInput))]
    [JsonSerializable(typeof(SchemaPubSubField))]
    [JsonSerializable(typeof(SchemaPubSubInput))]
    [JsonSerializable(typeof(SchemaOpenApiInput))]
    [JsonSerializable(typeof(Dictionary<string, JsonElement>))]
    internal sealed partial class SchemaInputJsonContext : JsonSerializerContext;
}
