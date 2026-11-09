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
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using ModelContextProtocol;
using Opc.Ua.Mcp.Serialization;
using Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Existing transport families. Kafka remains managed and reports the missing policy seam before activation.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<PubSubTransportKind>))]
    public enum PubSubTransportKind
    {
        /// <summary>UDP datagrams.</summary>
        Udp,

        /// <summary>DTLS-protected UDP datagrams.</summary>
        Dtls,

        /// <summary>MQTT broker transport.</summary>
        Mqtt,

        /// <summary>Managed Kafka broker transport.</summary>
        Kafka,

        /// <summary>Raw Ethernet frames.</summary>
        Ethernet
    }

    /// <summary>
    /// Wire encoding, not a display serialization preference.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<PubSubMessageEncoding>))]
    public enum PubSubMessageEncoding
    {
        /// <summary>Native UADP NetworkMessages.</summary>
        Uadp,

        /// <summary>Native OPC UA JSON NetworkMessages.</summary>
        Json
    }

    /// <summary>
    /// A structural PublisherId retaining its wire discriminator.
    /// </summary>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record PubSubPublisherIdInput
    {
        /// <summary>
        /// Gets the exact wire kind; Guid is valid only for JSON.
        /// </summary>
        [JsonConverter(typeof(JsonStringEnumConverter<PublisherIdType>))]
        public required PublisherIdType Type { get; init; }

        /// <summary>
        /// Gets the number or string of that kind. UInt64 may use a decimal string to preserve all 64 bits.
        /// </summary>
        public required JsonElement Value { get; init; }
    }

    /// <summary>
    /// One declared DataSet field with stable identity and an explicit scalar/array shape.
    /// </summary>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record PubSubFieldInput
    {
        /// <summary>
        /// Creates a field for direct construction with explicit required name and type.
        /// </summary>
        public PubSubFieldInput()
        {
        }

        /// <summary>
        /// Preserves omitted scalar shape and optional values during source-generated JSON construction.
        /// </summary>
        [JsonConstructor]
        [SetsRequiredMembers]
        public PubSubFieldInput(
            string name, BuiltInType type, int valueRank = ValueRanks.Scalar,
            string? id = null, JsonElement? initialValue = null)
        {
            Name = name;
            Type = type;
            ValueRank = valueRank;
            Id = id;
            InitialValue = initialValue;
        }

        /// <summary>
        /// Gets the case-sensitive field name.
        /// </summary>
        [JsonRequired]
        public required string Name { get; init; }

        /// <summary>
        /// Gets the UA built-in type.
        /// </summary>
        [JsonRequired]
        [JsonConverter(typeof(JsonStringEnumConverter<BuiltInType>))]
        public required BuiltInType Type { get; init; }

        /// <summary>
        /// Gets -1 for a scalar or 1 for a bounded one-dimensional array.
        /// </summary>
        public int ValueRank { get; init; } = ValueRanks.Scalar;

        /// <summary>
        /// Gets the optional stable DataSetFieldId GUID. Empty uses a deterministic field-order identity.
        /// </summary>
        public string? Id { get; init; }

        /// <summary>
        /// Gets an optional typed initial JSON value; omission uses the built-in default.
        /// </summary>
        public JsonElement? InitialValue { get; init; }
    }

    /// <summary>
    /// An explicit broker/datagram/Layer-2 target. Secrets never appear in the endpoint.
    /// </summary>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record PubSubConnectionInput
    {
        /// <summary>
        /// Creates a connection for direct construction with an explicit transport and endpoint.
        /// </summary>
        public PubSubConnectionInput()
        {
        }

        /// <summary>
        /// Preserves the default encoding and empty ancillary-topic list during source-generated JSON construction.
        /// </summary>
        [JsonConstructor]
        [SetsRequiredMembers]
        public PubSubConnectionInput(
            PubSubTransportKind transport, string endpoint, PubSubMessageEncoding encoding = PubSubMessageEncoding.Uadp,
            string? networkInterface = null, string? topic = null, string? metadataTopic = null,
            ArrayOf<string> auxiliaryTopics = default, string? userName = null, string? credentialReference = null)
        {
            Transport = transport;
            Endpoint = endpoint;
            Encoding = encoding;
            NetworkInterface = networkInterface;
            Topic = topic;
            MetadataTopic = metadataTopic;
            AuxiliaryTopics = auxiliaryTopics.IsNull ? [] : auxiliaryTopics;
            UserName = userName;
            CredentialReference = credentialReference;
        }

        /// <summary>
        /// Gets the selected existing transport family.
        /// </summary>
        [JsonRequired]
        public required PubSubTransportKind Transport { get; init; }

        /// <summary>
        /// Gets the exact caller-supplied endpoint, subject to the host's independent rules.
        /// </summary>
        [JsonRequired]
        public required string Endpoint { get; init; }

        /// <summary>
        /// Gets the encoding sent on the wire.
        /// </summary>
        public PubSubMessageEncoding Encoding { get; init; }

        /// <summary>
        /// Gets the explicit interface required for datagram and Ethernet transports.
        /// </summary>
        public string? NetworkInterface { get; init; }

        /// <summary>
        /// Gets the exact data topic required for broker transports; wildcards are not accepted.
        /// </summary>
        public string? Topic { get; init; }

        /// <summary>
        /// Gets the exact metadata topic required for broker transports.
        /// </summary>
        public string? MetadataTopic { get; init; }

        /// <summary>
        /// Gets explicitly requested ancillary broker topics. Each requires a separate exact host grant.
        /// </summary>
        [JsonConverter(typeof(McpStringArrayJsonConverter))]
        public ArrayOf<string> AuxiliaryTopics { get; init; } = [];

        /// <summary>
        /// Gets the optional MQTT user name. Passwords are supplied only by an authorized secret reference.
        /// </summary>
        public string? UserName { get; init; }

        /// <summary>
        /// Gets the optional alias:/issued: password reference bound to the exact endpoint resource.
        /// </summary>
        public string? CredentialReference { get; init; }
    }

    /// <summary>
    /// Message security material represented only by a protected concatenated SKS key reference.
    /// </summary>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record PubSubSecurityInput
    {
        /// <summary>
        /// Creates security settings for direct construction with an explicit protected key identity.
        /// </summary>
        public PubSubSecurityInput()
        {
        }

        /// <summary>
        /// Preserves SignAndEncrypt when the optional mode is omitted from source-generated JSON input.
        /// </summary>
        [JsonConstructor]
        [SetsRequiredMembers]
        public PubSubSecurityInput(
            string groupId, string policyUri, string keyReference, string resource, uint tokenId,
            DateTimeOffset expiresAt, MessageSecurityMode mode = MessageSecurityMode.SignAndEncrypt)
        {
            GroupId = groupId;
            PolicyUri = policyUri;
            KeyReference = keyReference;
            Resource = resource;
            TokenId = tokenId;
            ExpiresAt = expiresAt;
            Mode = mode;
        }

        /// <summary>
        /// Gets Sign or SignAndEncrypt. None is represented by omitting this object.
        /// </summary>
        [JsonConverter(typeof(JsonStringEnumConverter<MessageSecurityMode>))]
        public MessageSecurityMode Mode { get; init; } = MessageSecurityMode.SignAndEncrypt;

        /// <summary>
        /// Gets the exact SecurityGroupId.
        /// </summary>
        [JsonRequired]
        public required string GroupId { get; init; }

        /// <summary>
        /// Gets the PubSub AES-CTR security policy URI.
        /// </summary>
        [JsonRequired]
        public required string PolicyUri { get; init; }

        /// <summary>
        /// Gets the opaque key reference; the raw SKS key is never a tool argument.
        /// </summary>
        [JsonRequired]
        public required string KeyReference { get; init; }

        /// <summary>
        /// Gets the resource binding returned when the key was stored.
        /// </summary>
        [JsonRequired]
        public required string Resource { get; init; }

        /// <summary>
        /// Gets the SKS token identifier.
        /// </summary>
        [JsonRequired]
        public required uint TokenId { get; init; }

        /// <summary>
        /// Gets the key expiry returned by SKS. It may not exceed the stored reference's expiry.
        /// </summary>
        [JsonRequired]
        public required DateTimeOffset ExpiresAt { get; init; }
    }

    /// <summary>
    /// One named, initially stopped application. Creating and validating never start a transport.
    /// </summary>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record PubSubApplicationInput
    {
        /// <summary>
        /// Creates an application plan for direct construction with explicit required identity and schema.
        /// </summary>
        public PubSubApplicationInput()
        {
        }

        /// <summary>
        /// Preserves omitted identifiers, dataset name and publishing interval in source-generated JSON input.
        /// </summary>
        [JsonConstructor]
        [SetsRequiredMembers]
        public PubSubApplicationInput(
            string name, string mode, PubSubConnectionInput connection, PubSubPublisherIdInput publisherId,
            ArrayOf<PubSubFieldInput> fields, ushort writerGroupId = 1, ushort dataSetWriterId = 1,
            string dataSetName = "McpDataSet", string? dataSetClassId = null,
            int publishingIntervalMilliseconds = 100, PubSubSecurityInput? security = null)
        {
            Name = name;
            Mode = mode;
            Connection = connection;
            PublisherId = publisherId;
            Fields = fields;
            WriterGroupId = writerGroupId;
            DataSetWriterId = dataSetWriterId;
            DataSetName = dataSetName;
            DataSetClassId = dataSetClassId;
            PublishingIntervalMilliseconds = publishingIntervalMilliseconds;
            Security = security;
        }

        /// <summary>
        /// Gets the caller's runtime name, not an authority token.
        /// </summary>
        [JsonRequired]
        public required string Name { get; init; }

        /// <summary>
        /// Gets Publisher or Subscriber.
        /// </summary>
        [JsonRequired]
        public required string Mode { get; init; }

        /// <summary>
        /// Gets the target connection.
        /// </summary>
        [JsonRequired]
        public required PubSubConnectionInput Connection { get; init; }

        /// <summary>
        /// Gets the discriminator-preserving publisher identity.
        /// </summary>
        [JsonRequired]
        public required PubSubPublisherIdInput PublisherId { get; init; }

        /// <summary>
        /// Gets the WriterGroupId.
        /// </summary>
        public ushort WriterGroupId { get; init; } = 1;

        /// <summary>
        /// Gets the DataSetWriterId.
        /// </summary>
        public ushort DataSetWriterId { get; init; } = 1;

        /// <summary>
        /// Gets the DataSet name.
        /// </summary>
        public string DataSetName { get; init; } = "McpDataSet";

        /// <summary>
        /// Gets the optional DataSetClassId GUID.
        /// </summary>
        public string? DataSetClassId { get; init; }

        /// <summary>
        /// Gets the publish interval in milliseconds.
        /// </summary>
        public int PublishingIntervalMilliseconds { get; init; } = 100;

        /// <summary>
        /// Gets the bounded field schema.
        /// </summary>
        [JsonRequired]
        [JsonConverter(typeof(PubSubInputArrayConverter<PubSubFieldInput>))]
        public required ArrayOf<PubSubFieldInput> Fields { get; init; }

        /// <summary>
        /// Gets optional message-layer security; JSON cannot silently discard it.
        /// </summary>
        public PubSubSecurityInput? Security { get; init; }
    }

    /// <summary>
    /// One update whose value must match the runtime's declared field type.
    /// </summary>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record PubSubFieldValueInput
    {
        /// <summary>
        /// Gets the exact declared field name.
        /// </summary>
        public required string Name { get; init; }

        /// <summary>
        /// Gets the field's scalar or array JSON value.
        /// </summary>
        public required JsonElement Value { get; init; }
    }

    /// <summary>
    /// A bounded atomic sample update. Transport publication occurs on the next scheduled sample.
    /// </summary>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record PubSubPublishInput
    {
        /// <summary>
        /// Gets the explicitly updated fields.
        /// </summary>
        [JsonConverter(typeof(PubSubInputArrayConverter<PubSubFieldValueInput>))]
        public required ArrayOf<PubSubFieldValueInput> Fields { get; init; }
    }

    /// <summary>
    /// Source-generated array elements shared by the SDK binder and the displayed schema.
    /// </summary>
    /// <typeparam name="T">One concrete input object.</typeparam>
    public sealed class PubSubInputArrayConverter<T> : McpInputArrayJsonConverter<T>
        where T : class
    {
        /// <inheritdoc/>
        protected override JsonTypeInfo<T> ElementType =>
            (JsonTypeInfo<T>)(McpPubSubExpansionJsonContext.Default.GetTypeInfo(typeof(T)) ??
                throw new InvalidOperationException("Missing PubSub input metadata."));
    }

    /// <summary>
    /// Exact AOT-safe MCP input contracts.
    /// </summary>
    [JsonSourceGenerationOptions(
        PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
        Converters = new[]
        {
            typeof(PubSubInputArrayConverter<PubSubFieldInput>),
            typeof(PubSubInputArrayConverter<PubSubFieldRenameInput>),
            typeof(PubSubInputArrayConverter<PubSubFieldValueInput>),
            typeof(PubSubInputArrayConverter<PubSubVariableInput>),
            typeof(PubSubInputArrayConverter<PubSubFieldIndexInput>),
            typeof(PubSubInputArrayConverter<PubSubNodeInput>)
        })]
    [JsonSerializable(typeof(PubSubApplicationInput))]
    [JsonSerializable(typeof(PubSubApplicationSettingsInput))]
    [JsonSerializable(typeof(PubSubBridgeSettingsInput))]
    [JsonSerializable(typeof(ArrayOf<PubSubFieldInput>), TypeInfoPropertyName = "PubSubFields")]
    [JsonSerializable(typeof(ArrayOf<PubSubFieldRenameInput>), TypeInfoPropertyName = "PubSubRenames")]
    [JsonSerializable(typeof(ArrayOf<PubSubFieldValueInput>), TypeInfoPropertyName = "PubSubFieldValues")]
    [JsonSerializable(typeof(ArrayOf<PubSubVariableInput>), TypeInfoPropertyName = "PubSubVariables")]
    [JsonSerializable(typeof(ArrayOf<PubSubFieldIndexInput>), TypeInfoPropertyName = "PubSubFieldIndices")]
    [JsonSerializable(typeof(ArrayOf<PubSubNodeInput>), TypeInfoPropertyName = "PubSubNodes")]
    [JsonSerializable(typeof(PubSubConnectionInput))]
    [JsonSerializable(typeof(PubSubPublisherIdInput))]
    [JsonSerializable(typeof(PubSubFieldInput))]
    [JsonSerializable(typeof(PubSubFieldValueInput))]
    [JsonSerializable(typeof(PubSubPublishInput))]
    [JsonSerializable(typeof(PubSubSecurityInput))]
    [JsonSerializable(typeof(PubSubVariableInput))]
    [JsonSerializable(typeof(PubSubVariablesInput))]
    [JsonSerializable(typeof(PubSubVersionInput))]
    [JsonSerializable(typeof(PubSubFieldIndexInput))]
    [JsonSerializable(typeof(PubSubFieldIndicesInput))]
    [JsonSerializable(typeof(PubSubNodeInput))]
    [JsonSerializable(typeof(PubSubNodesInput))]
    [JsonSerializable(typeof(PubSubBridgeInput))]
    [JsonSerializable(typeof(PubSubFieldRenameInput))]
    [JsonSerializable(typeof(PubSubActionBindingInput))]
    [JsonSerializable(typeof(UserTokenType))]
    internal sealed partial class McpPubSubExpansionJsonContext : JsonSerializerContext;

    /// <summary>
    /// SDK binding options distinct from the generated context's static initialization.
    /// </summary>
    internal static class McpPubSubToolJson
    {
        /// <summary>
        /// Gets domain metadata composed with the SDK's generated protocol and scalar metadata.
        /// No reflection resolver is registered.
        /// </summary>
        internal static JsonSerializerOptions Options { get; } = CreateToolOptions();

        /// <summary>
        /// Retains SDK protocol converters while preferring the explicit bounded domain contracts.
        /// </summary>
        private static JsonSerializerOptions CreateToolOptions()
        {
            var options = new JsonSerializerOptions(McpJsonUtilities.DefaultOptions)
            {
                TypeInfoResolver = JsonTypeInfoResolver.Combine(
                    McpPubSubExpansionJsonContext.Default, McpJsonUtilities.DefaultOptions.TypeInfoResolver)
            };
            options.Converters.Add(new PubSubInputArrayConverter<PubSubFieldInput>());
            options.Converters.Add(new PubSubInputArrayConverter<PubSubFieldRenameInput>());
            options.Converters.Add(new PubSubInputArrayConverter<PubSubFieldValueInput>());
            options.Converters.Add(new PubSubInputArrayConverter<PubSubVariableInput>());
            options.Converters.Add(new PubSubInputArrayConverter<PubSubFieldIndexInput>());
            options.Converters.Add(new PubSubInputArrayConverter<PubSubNodeInput>());
            options.MakeReadOnly();
            return options;
        }
    }
}
