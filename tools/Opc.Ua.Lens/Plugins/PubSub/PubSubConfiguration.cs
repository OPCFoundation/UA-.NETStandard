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
using System.Net;
using System.Text.Json.Serialization;
using Opc.Ua;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Eth;
using Opc.Ua.PubSub.Kafka;

namespace UaLens.Plugins.PubSub;

internal enum PubSubProfile
{
    UdpUadp,
    MqttJson,
    MqttUadp,
    DtlsUadp,
    EthernetUadp,
    KafkaJson,
    KafkaUadp
}

internal enum PubSubPublication
{
    Disabled,
    Synthetic,
    ServerSource
}

internal enum PubSubBrokerAuthentication
{
    Unconfigured,
    Anonymous,
    Provider
}

internal enum PubSubKeySource
{
    SecurityKeyService,
    ConfiguredProvider
}

internal enum PubSubReadiness
{
    Ready,
    RequiresConfiguration,
    Unsupported,
    Denied
}

/// <summary>
/// Portable scalar schema and optional explicitly selected UA bindings.
/// </summary>
internal sealed record PubSubFieldConfiguration
{
    public string Name { get; init; } = string.Empty;

    public BuiltInType Type { get; init; } = BuiltInType.Int32;

    public Guid FieldId { get; init; }

    public string SourceNodeId { get; init; } = string.Empty;

    public string TargetNodeId { get; init; } = string.Empty;
}

/// <summary>
/// Safe configuration intent, not a Part 14 configuration dump or a running workload.
/// Providers are selected by registered identifiers, never by assembly or file paths.
/// </summary>
internal sealed record PubSubConfiguration
{
    public PubSubProfile Profile { get; init; } = PubSubProfile.UdpUadp;

    public string Endpoint { get; init; } = string.Empty;

    public string NetworkInterface { get; init; } = string.Empty;

    public string Topic { get; init; } = string.Empty;

    public string TransportProviderId { get; init; } = string.Empty;

    public PubSubBrokerAuthentication BrokerAuthentication { get; init; }

    public string CredentialReference { get; init; } = string.Empty;

    public MessageSecurityMode SecurityMode { get; init; } = MessageSecurityMode.SignAndEncrypt;

    public string SecurityGroupId { get; init; } = string.Empty;

    public string SecurityProviderId { get; init; } = string.Empty;

    public string SecurityKeyServiceEndpoint { get; init; } = string.Empty;

    public PubSubKeySource KeySource { get; init; }

    public bool ReceiveEnabled { get; init; } = true;

    public ulong LocalPublisherId { get; init; } = 2;

    public PublisherIdType LocalPublisherIdType { get; init; } = PublisherIdType.UInt16;

    public string LocalPublisherName { get; init; } = string.Empty;

    public ulong PublisherFilter { get; init; } = 1;

    public PublisherIdType PublisherFilterType { get; init; } = PublisherIdType.UInt16;

    public string PublisherFilterName { get; init; } = string.Empty;

    public ushort WriterGroupId { get; init; } = 100;

    public ushort DataSetWriterId { get; init; } = 1;

    public PubSubPublication Publication { get; init; }

    public int PublishingIntervalMs { get; init; } = 1000;

    public int DurationSeconds { get; init; } = 30;

    public int MaxPublishedMessages { get; init; } = 100;

    public int RetainedMessages { get; init; } = 128;

    public int MaxNetworkMessageBytes { get; init; } = 1500;

    public bool MulticastLoopback { get; init; }

    public uint MetadataMajorVersion { get; init; } = 1;

    public uint MetadataMinorVersion { get; init; }

    public Guid DataSetClassId { get; init; }

    public bool RawDataEncoding { get; init; }

    public UadpNetworkMessageContentMask UadpNetworkMask { get; init; } = PubSubContentMasks.DefaultUadpNetwork;

    public UadpDataSetMessageContentMask UadpDataSetMask { get; init; } = PubSubContentMasks.DefaultUadpDataSet;

    public JsonNetworkMessageContentMask JsonNetworkMask { get; init; } = PubSubContentMasks.DefaultJsonNetwork;

    public JsonDataSetMessageContentMask JsonDataSetMask { get; init; } = PubSubContentMasks.DefaultJsonDataSet;

    public DataSetFieldContentMask FieldContentMask { get; init; } = PubSubContentMasks.DefaultField;

    public string AdapterProviderId { get; init; } = string.Empty;

    public bool WriteBackEnabled { get; init; }

    public bool ActionResponderEnabled { get; init; }

    public ushort ActionWriterId { get; init; } = 2;

    public ushort ActionTargetId { get; init; } = 1;

    public string ActionName { get; init; } = "SampleAction";

    public string ActionObjectNodeId { get; init; } = string.Empty;

    public string ActionMethodNodeId { get; init; } = string.Empty;

    public string ActionResponseTopic { get; init; } = string.Empty;

    [JsonConverter(typeof(PubSubFieldArrayJsonConverter))]
    public ArrayOf<PubSubFieldConfiguration> Fields { get; init; } =
    [
        new() { Name = "BoolToggle", Type = BuiltInType.Boolean },
        new() { Name = "Int32", Type = BuiltInType.Int32 },
        new() { Name = "DateTime", Type = BuiltInType.DateTime }
    ];

    [JsonIgnore]
    public bool IsBroker => Profile is PubSubProfile.MqttJson or PubSubProfile.MqttUadp or
        PubSubProfile.KafkaJson or PubSubProfile.KafkaUadp;

    [JsonIgnore]
    public bool IsJson => Profile is PubSubProfile.MqttJson or PubSubProfile.KafkaJson;

    [JsonIgnore]
    public bool UsesServerAdapter => Publication == PubSubPublication.ServerSource ||
        WriteBackEnabled || ActionResponderEnabled;

    [JsonIgnore]
    public bool IsBoundedWorkload => Publication != PubSubPublication.Disabled ||
        WriteBackEnabled || ActionResponderEnabled;

    [JsonIgnore]
    public DataSetFieldContentMask EffectiveFieldMask =>
        RawDataEncoding ? DataSetFieldContentMask.RawData : FieldContentMask;

    [JsonIgnore]
    public string TransportProfileUri => Profile switch
    {
        PubSubProfile.UdpUadp or PubSubProfile.DtlsUadp => Profiles.PubSubUdpUadpTransport,
        PubSubProfile.MqttJson => Profiles.PubSubMqttJsonTransport,
        PubSubProfile.MqttUadp => Profiles.PubSubMqttUadpTransport,
        PubSubProfile.EthernetUadp => EthProfiles.PubSubEthUadpTransport,
        PubSubProfile.KafkaJson => KafkaProfiles.PubSubKafkaJsonTransport,
        PubSubProfile.KafkaUadp => KafkaProfiles.PubSubKafkaUadpTransport,
        _ => throw new ArgumentOutOfRangeException(nameof(Profile))
    };
}

/// <summary>
/// Transient consent. It is deliberately absent from workspace serialization.
/// </summary>
internal sealed record PubSubStartAuthorization(
    bool AllowUnsecured = false,
    bool AllowPublication = false,
    bool AllowWriteBack = false,
    bool AllowResponder = false,
    bool AllowAnonymousBroker = false);

internal sealed record PubSubPrerequisite(string Area, PubSubReadiness Readiness, string Detail);

/// <summary>
/// Validates bounded, non-secret configuration before providers or transports are touched.
/// </summary>
internal static class PubSubConfigurationValidation
{
    public static ArrayOf<PubSubPrerequisite> Inspect(PubSubConfiguration configuration, bool requireEndpoint = true)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var issues = new List<PubSubPrerequisite>();
        if (configuration.Endpoint is null || configuration.NetworkInterface is null || configuration.Topic is null ||
            configuration.TransportProviderId is null || configuration.CredentialReference is null ||
            configuration.SecurityProviderId is null || configuration.SecurityGroupId is null ||
            configuration.SecurityKeyServiceEndpoint is null ||
            configuration.LocalPublisherName is null || configuration.PublisherFilterName is null ||
            configuration.AdapterProviderId is null || configuration.ActionName is null ||
            configuration.ActionResponseTopic is null || configuration.ActionObjectNodeId is null ||
            configuration.ActionMethodNodeId is null)
        {
            return [new PubSubPrerequisite("Configuration", PubSubReadiness.RequiresConfiguration,
                "Configuration text fields cannot be null.")];
        }
        if (!Enum.IsDefined(configuration.Profile) || !Enum.IsDefined(configuration.Publication) ||
            !Enum.IsDefined(configuration.BrokerAuthentication) || !Enum.IsDefined(configuration.KeySource) ||
            configuration.SecurityMode is not (MessageSecurityMode.None or MessageSecurityMode.Sign or
                MessageSecurityMode.SignAndEncrypt))
        {
            Add("Configuration", "Select a supported transport, publication and security mode.");
        }
        ValidateBound(configuration.PublishingIntervalMs, 100, 60000, "Publishing interval (milliseconds)");
        ValidateBound(configuration.DurationSeconds, 1, 600, "Workload duration (seconds)");
        ValidateBound(configuration.MaxPublishedMessages, 1, 10000, "Publication sample limit");
        ValidateBound(configuration.RetainedMessages, 1, 512, "Retained messages");
        ValidateBound(configuration.MaxNetworkMessageBytes, 512, 65507, "Network message bytes");
        ValidateText(configuration.NetworkInterface, 256, "Network interface");
        ValidateText(configuration.Topic, 256, "Topic");
        ValidateText(configuration.ActionResponseTopic, 256, "Action response topic");
        ValidateText(configuration.ActionName, 64, "Action name");
        ValidateReference(configuration.TransportProviderId, "Transport provider");
        ValidateReference(configuration.CredentialReference, "Credential reference");
        ValidateReference(configuration.SecurityProviderId, "Security provider");
        ValidateReference(configuration.SecurityGroupId, "Security group");
        ValidateReference(configuration.AdapterProviderId, "UA adapter provider");

        if (!PubSubIdentity.TryCreate(configuration.LocalPublisherIdType, configuration.LocalPublisherId,
                configuration.LocalPublisherName, configuration.IsJson, out _) ||
            !PubSubIdentity.TryCreate(configuration.PublisherFilterType, configuration.PublisherFilter,
                configuration.PublisherFilterName, configuration.IsJson, out _) ||
            configuration.WriterGroupId == 0 || configuration.DataSetWriterId == 0 ||
            configuration.ActionWriterId == 0 || configuration.ActionTargetId == 0)
        {
            Add("Identity", "Use bounded, correctly typed nonzero publisher/writer/Action IDs; no wildcards.");
        }
        issues.AddRange(PubSubContentMasks.Inspect(configuration));
        if (!configuration.ReceiveEnabled && configuration.Publication == PubSubPublication.Disabled &&
            !configuration.ActionResponderEnabled)
        {
            Add("Workload", "Enable reception, publication, or an explicitly configured Action responder.");
        }
        if (configuration.WriteBackEnabled && !configuration.ReceiveEnabled)
        {
            Add("Write-back", "UA write-back requires reception.");
        }
        if (configuration.ActionResponderEnabled && !configuration.ReceiveEnabled)
        {
            Add("Actions", "An Action responder requires reception and its configured reader-group security context.");
        }
        if (configuration.IsJson && configuration.SecurityMode != MessageSecurityMode.None)
        {
            Add("Security", "JSON does not use the UADP security wrapper. Select TLS and explicitly select None.");
        }
        if (configuration.SecurityMode != MessageSecurityMode.None &&
            (string.IsNullOrEmpty(configuration.SecurityProviderId) ||
             string.IsNullOrEmpty(configuration.SecurityGroupId) ||
             (configuration.KeySource == PubSubKeySource.SecurityKeyService &&
              string.IsNullOrEmpty(configuration.SecurityKeyServiceEndpoint))) && requireEndpoint)
        {
            Add("Security", "Select a registered key provider/group and, for SKS, its pinned service endpoint.");
        }
        if (configuration.SecurityMode == MessageSecurityMode.None &&
            (!string.IsNullOrEmpty(configuration.SecurityProviderId) ||
             !string.IsNullOrEmpty(configuration.SecurityGroupId) ||
             !string.IsNullOrEmpty(configuration.SecurityKeyServiceEndpoint)))
        {
            Add("Security", "Remove unused security-provider settings or select message security.");
        }
        if (configuration.SecurityKeyServiceEndpoint.Length > 0 &&
            (!IsKeyServiceEndpoint(configuration.SecurityKeyServiceEndpoint) ||
             configuration.KeySource != PubSubKeySource.SecurityKeyService))
        {
            Add("Security", "Only SKS uses an endpoint; credentials, query parameters and fragments are forbidden.");
        }
        if (configuration.IsBroker)
        {
            if (requireEndpoint && (string.IsNullOrWhiteSpace(configuration.Topic) ||
                configuration.BrokerAuthentication == PubSubBrokerAuthentication.Unconfigured))
            {
                Add("Broker", "Specify a topic and select Anonymous or a configured credential provider.");
            }
            if (configuration.Topic.Contains('+', StringComparison.Ordinal) ||
                configuration.Topic.Contains('#', StringComparison.Ordinal) ||
                configuration.Topic.Contains('*', StringComparison.Ordinal) ||
                configuration.ActionResponseTopic.Contains('*', StringComparison.Ordinal) ||
                configuration.ActionResponseTopic.Contains('+', StringComparison.Ordinal) ||
                configuration.ActionResponseTopic.Contains('#', StringComparison.Ordinal))
            {
                Add("Broker", "Use explicit topics, not wildcard subscriptions or response destinations.");
            }
            if (configuration.Profile is PubSubProfile.KafkaJson or PubSubProfile.KafkaUadp &&
                (!IsKafkaTopic(configuration.Topic) ||
                 !IsKafkaTopic(configuration.ActionResponseTopic)))
            {
                Add("Kafka", "Use bounded Kafka topic names containing only letters, digits, dot, dash or underscore.");
            }
            if (configuration.BrokerAuthentication == PubSubBrokerAuthentication.Provider &&
                (string.IsNullOrEmpty(configuration.CredentialReference) ||
                 string.IsNullOrEmpty(configuration.TransportProviderId)) && requireEndpoint)
            {
                Add("Credentials", "Select a registered transport provider and a non-secret credential reference.");
            }
            if (configuration.BrokerAuthentication != PubSubBrokerAuthentication.Provider &&
                !string.IsNullOrEmpty(configuration.CredentialReference))
            {
                Add("Credentials", "A credential reference requires Provider authentication; no anonymous fallback.");
            }
        }
        else if (!string.IsNullOrEmpty(configuration.Topic) ||
            !string.IsNullOrEmpty(configuration.CredentialReference) ||
            !string.IsNullOrEmpty(configuration.ActionResponseTopic) ||
            configuration.BrokerAuthentication != PubSubBrokerAuthentication.Unconfigured)
        {
            Add("Transport", "Datagram profiles do not use broker topics or broker credentials.");
        }

        if (string.IsNullOrEmpty(configuration.Endpoint))
        {
            if (requireEndpoint)
            {
                Add("Endpoint", "Choose a destination/listener endpoint. Opening this document starts no traffic.");
            }
        }
        else if (configuration.Endpoint.Length > 1024 ||
            !Uri.TryCreate(configuration.Endpoint, UriKind.Absolute, out Uri? endpoint) ||
            !string.IsNullOrEmpty(endpoint.UserInfo) || !string.IsNullOrEmpty(endpoint.Query) ||
            !string.IsNullOrEmpty(endpoint.Fragment) || HasControlCharacters(configuration.Endpoint))
        {
            Add("Endpoint", "Use a bounded endpoint URI without credentials, query parameters or fragments.");
        }
        else
        {
            string expectedScheme = configuration.Profile switch
            {
                PubSubProfile.UdpUadp => "opc.udp",
                PubSubProfile.DtlsUadp => "opc.dtls",
                PubSubProfile.EthernetUadp => "opc.eth",
                PubSubProfile.MqttJson or PubSubProfile.MqttUadp => "mqtt",
                PubSubProfile.KafkaJson or PubSubProfile.KafkaUadp => "kafka",
                _ => string.Empty
            };
            if (endpoint.Scheme != expectedScheme &&
                !(expectedScheme == "mqtt" && endpoint.Scheme == "mqtts") &&
                !(expectedScheme == "kafka" && endpoint.Scheme == "kafkas"))
            {
                Add("Endpoint", "The endpoint scheme does not match the selected transport profile.");
            }
            if (endpoint.AbsolutePath != "/" && endpoint.AbsolutePath.Length != 0)
            {
                Add("Endpoint", "Configure topics separately; endpoint paths are not supported by this document.");
            }
            if (configuration.Profile is PubSubProfile.UdpUadp or PubSubProfile.DtlsUadp)
            {
                if (endpoint.Port is < 1 or > 65535 || !IPAddress.TryParse(endpoint.Host, out IPAddress? address))
                {
                    Add("Endpoint", "UDP/DTLS requires a literal IP address and explicit port.");
                }
                else if (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) ||
                    (configuration.Profile == PubSubProfile.DtlsUadp &&
                     (address.IsIPv6Multicast || address.Equals(IPAddress.Broadcast) ||
                      (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
                       address.GetAddressBytes()[0] is >= 224 and <= 239))))
                {
                    Add("Endpoint", "Use an explicit destination; DTLS requires a unicast peer, not broadcast.");
                }
            }
            else if (string.IsNullOrEmpty(endpoint.Host))
            {
                Add("Endpoint", "An explicit broker or Ethernet destination is required.");
            }
        }
        if (configuration.Profile is PubSubProfile.UdpUadp or PubSubProfile.DtlsUadp or PubSubProfile.EthernetUadp &&
            string.IsNullOrWhiteSpace(configuration.NetworkInterface) && requireEndpoint)
        {
            Add("Interface", "Select the local network interface explicitly; the document does not choose a NIC.");
        }
        if (configuration.Profile is PubSubProfile.DtlsUadp or PubSubProfile.EthernetUadp &&
            string.IsNullOrEmpty(configuration.TransportProviderId) && requireEndpoint)
        {
            Add("Provider", "This profile requires an installed and explicitly configured transport provider.");
        }
        if (configuration.Fields.IsNull || configuration.Fields.Count is < 1 or > MaxFields)
        {
            Add("Schema", "Configure between 1 and 32 scalar fields.");
        }
        else
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            var identifiers = new HashSet<Guid>();
            var targets = new HashSet<ExpandedNodeId>();
            foreach (PubSubFieldConfiguration field in configuration.Fields)
            {
                if (field is null)
                {
                    Add("Schema", "Null fields are not supported.");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(field.Name) || field.Name.Length > 64 ||
                    HasControlCharacters(field.Name) || !names.Add(field.Name) || !IsScalarType(field.Type))
                {
                    Add("Schema", "Use unique bounded field names and supported scalar built-in types.");
                }
                if (field.FieldId != Guid.Empty && !identifiers.Add(field.FieldId))
                {
                    Add("Schema", "Nonempty dataset field IDs must be unique.");
                }
                if (configuration.Publication == PubSubPublication.Synthetic &&
                    field.Type is not (BuiltInType.Boolean or BuiltInType.Int32 or BuiltInType.Double or
                        BuiltInType.DateTime or BuiltInType.String))
                {
                    Add("Synthetic source", "Synthetic fields support Boolean, Int32, Double, DateTime and String.");
                }
                ValidateNode(field.SourceNodeId, configuration.Publication == PubSubPublication.ServerSource, "Source");
                ValidateNode(field.TargetNodeId, configuration.WriteBackEnabled, "Write-back target");
                if (configuration.WriteBackEnabled && IsPortableNodeId(field.TargetNodeId) &&
                    !targets.Add(ExpandedNodeId.Parse(field.TargetNodeId)))
                {
                    Add("Write-back", "Map each field to a distinct UA target; duplicate targets are not authorized.");
                }
            }
        }
        if (configuration.UsesServerAdapter && string.IsNullOrEmpty(configuration.AdapterProviderId) && requireEndpoint)
        {
            Add("UA adapter", "Select a registered server-session adapter provider; the primary session is optional.");
        }
        ValidateNode(configuration.ActionObjectNodeId, configuration.ActionResponderEnabled, "Action object");
        ValidateNode(configuration.ActionMethodNodeId, configuration.ActionResponderEnabled, "Action method");
        if (configuration.ActionResponderEnabled &&
            (configuration.ActionWriterId == 0 || configuration.ActionTargetId == 0 ||
             string.IsNullOrWhiteSpace(configuration.ActionName)))
        {
            Add("Actions", "A responder requires a nonzero writer/target pair and an explicit Action name.");
        }
        return [.. issues];

        void Add(string area, string message)
        {
            issues.Add(new PubSubPrerequisite(area, PubSubReadiness.RequiresConfiguration, message));
        }

        void ValidateBound(int value, int minimum, int maximum, string name)
        {
            if (value < minimum || value > maximum)
            {
                Add("Bounds", string.Create(CultureInfo.InvariantCulture, $"{name} must be {minimum}–{maximum}."));
            }
        }

        void ValidateText(string value, int length, string name)
        {
            if (value is null || value.Length > length || HasControlCharacters(value))
            {
                Add("Configuration", name + " is invalid or too long.");
            }
        }

        void ValidateReference(string reference, string name)
        {
            if (!IsProviderReference(reference))
            {
                Add("References", name + " must be an opaque registered identifier, not a path, URI or secret.");
            }
        }

        void ValidateNode(string identifier, bool required, string name)
        {
            if (string.IsNullOrEmpty(identifier) && (!required || !requireEndpoint))
            {
                return;
            }
            if (!IsPortableNodeId(identifier))
            {
                Add("UA mapping", name + " requires an ExpandedNodeId with a namespace URI (or namespace zero).");
            }
        }
    }

    public static void RequireValid(PubSubConfiguration configuration, bool requireEndpoint = true)
    {
        ArrayOf<PubSubPrerequisite> issues = Inspect(configuration, requireEndpoint);
        if (issues.Count != 0)
        {
            throw new ArgumentException(issues[0].Area + ": " + issues[0].Detail, nameof(configuration));
        }
    }

    public static void RequireAuthorization(PubSubConfiguration configuration, PubSubStartAuthorization authorization)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        bool plaintextBroker = configuration.IsBroker &&
            !configuration.Endpoint.StartsWith("mqtts:", StringComparison.OrdinalIgnoreCase) &&
            !configuration.Endpoint.StartsWith("kafkas:", StringComparison.OrdinalIgnoreCase);
        if ((configuration.SecurityMode == MessageSecurityMode.None || plaintextBroker) &&
            !authorization.AllowUnsecured)
        {
            throw new InvalidOperationException(
                "Explicitly authorize the selected message/transport security limitations.");
        }
        if (configuration.Publication != PubSubPublication.Disabled && !authorization.AllowPublication)
        {
            throw new InvalidOperationException(
                "Explicitly authorize the bounded publication to the displayed target.");
        }
        if (configuration.WriteBackEnabled && !authorization.AllowWriteBack)
        {
            throw new InvalidOperationException("Explicitly authorize the selected UA write-back targets.");
        }
        if (configuration.ActionResponderEnabled && !authorization.AllowResponder)
        {
            throw new InvalidOperationException("Explicitly authorize the selected UA method responder.");
        }
        if (configuration.IsBroker && configuration.BrokerAuthentication == PubSubBrokerAuthentication.Anonymous &&
            !authorization.AllowAnonymousBroker)
        {
            throw new InvalidOperationException(
                "Explicitly authorize anonymous broker authentication; it is not a fallback.");
        }
    }

    public static bool IsProviderReference(string reference)
    {
        if (reference is null || reference.Length > 96)
        {
            return false;
        }
        foreach (char character in reference)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_' or '.'))
            {
                return false;
            }
        }
        return !reference.Contains("..", StringComparison.Ordinal);
    }

    public static bool IsPortableNodeId(string identifier)
    {
        return identifier is { Length: > 0 and <= 1024 } && !HasControlCharacters(identifier) &&
            ExpandedNodeId.TryParse(identifier, out ExpandedNodeId nodeId) && !nodeId.IsNull &&
            nodeId.ServerIndex == 0 && (nodeId.NamespaceIndex == 0 || !string.IsNullOrEmpty(nodeId.NamespaceUri));
    }

    public static bool IsKeyServiceEndpoint(string endpoint)
    {
        return endpoint is { Length: > 0 and <= 1024 } && !HasControlCharacters(endpoint) &&
            Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? uri) && !string.IsNullOrEmpty(uri.Host) &&
            string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) &&
            string.IsNullOrEmpty(uri.Fragment) && uri.Scheme is "opc.tcp" or "https" or "opc.https" or "opc.wss";
    }

    public static bool IsKafkaTopic(string topic)
    {
        if (topic is null || topic.Length > 240 || topic is "." or "..")
        {
            return false;
        }
        foreach (char character in topic)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_' or '.'))
            {
                return false;
            }
        }
        return true;
    }

    public static bool IsScalarType(BuiltInType type)
    {
        return type is >= BuiltInType.Boolean and <= BuiltInType.ByteString or
            BuiltInType.NodeId or BuiltInType.ExpandedNodeId or BuiltInType.StatusCode or
            BuiltInType.QualifiedName or BuiltInType.LocalizedText;
    }

    public static bool HasControlCharacters(string text)
    {
        foreach (char character in text)
        {
            if (char.IsControl(character))
            {
                return true;
            }
        }
        return false;
    }

    public const int MaxFields = 32;
    public const int MaxMetadata = 32;
    public const int MaxEvidence = 128;
    public const int MaxValueCharacters = 512;
    public const int MaxDiscoveryResponses = 64;
}
