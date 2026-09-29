/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Opc.Ua.XRegistry;

namespace Opc.Ua.EndpointRegistry
{
#pragma warning disable CS8602 // Native JSON shape is validated before semantic traversal.
#pragma warning disable CS8604 // Native JSON shape is validated before semantic traversal.
#pragma warning disable CA1307 // Python parity uses char/string APIs available across all target frameworks.
#pragma warning disable CA1820 // Normative string comparisons are kept visually close to the Python source.
#pragma warning disable CA1859 // Public-style helper signatures are kept readable across target frameworks.
#pragma warning disable CA1861 // Small constant arrays mirror the Python source at each rule site.
#pragma warning disable CA1865 // The assembly targets frameworks without every char overload.
#pragma warning disable CA2249 // IndexOf(char) is used for cross-target compatibility.
#pragma warning disable RCS0056 // Normative diagnostics and generated regular expressions are intentionally literal.
#pragma warning disable SYSLIB1045 // The assembly targets frameworks where GeneratedRegex is unavailable.
    /// <summary>
    /// Validates Endpoint Registry and Message Definition native xRegistry documents.
    /// </summary>
    public static class EndpointRegistryRules
    {
        /// <summary>
        /// Validates a Message Definition resource or sole Version document.
        /// </summary>
        public static void ValidateMessage(RegistryObjectValueDataType message, string? group = null)
        {
            ValidateMessage(message, ParseGroup(group));
        }

        /// <summary>
        /// Validates a Message Group document.
        /// </summary>
        public static void ValidateMessageGroup(RegistryObjectValueDataType group, string? groupId = null)
        {
            Object(group, "messagegroup");
            s_schemas.Validate(group, "messagegroup");
            CommonMetadata(group, "messagegroupid");
            foreach (string kind in s_selectorKinds)
            {
                if (TryString(group, kind, out string? value))
                {
                    NonEmpty(value!, kind);
                    if (value!.Split('/').Any(static part => part.Length == 0) ||
                        kind == "envelope" && !value.Contains('/'))
                    {
                        Fail("E_MESSAGE_SELECTOR", kind, "a reusable Group requires a well-formed selector and versioned envelope");
                    }
                }
            }
            string? xid = groupId is null ? OptionalString(group, "xid") : "/messagegroups/" + groupId;
            if (xid is not null)
            {
                Reference(xid, "xid", "group");
                string id = xid.Split('/')[2];
                if ((OptionalString(group, "messagegroupid") ?? id) != id ||
                    (OptionalString(group, "xid") ?? xid) != xid)
                {
                    Fail("E_IDENTITY", xid, "Message Group key, identifier and Xid disagree");
                }
            }
            ValidateContainer(group, xid);
        }

        /// <summary>
        /// Validates a Message-bearing container and its inline Message collection.
        /// </summary>
        public static void ValidateContainer(RegistryObjectValueDataType container, string? groupXid = null)
        {
            CollectionDiscovery(container, "messages");
            if (!TryObject(container, "messages", out RegistryObjectValueDataType? messages))
            {
                return;
            }
            foreach (KeyValuePair<string, RegistryValueDataType> entry in Members(messages!))
            {
                GroupIdentity("messages", entry.Key);
                RegistryObjectValueDataType message = Object(entry.Value, "messages/" + entry.Key);
                if (Has(message, "messageid") && OptionalString(message, "messageid") != entry.Key)
                {
                    Fail("E_IDENTITY", "messages/" + entry.Key, "Message key and messageid differ");
                }
                if (groupXid is not null &&
                    (OptionalString(message, "xid") ?? groupXid + "/messages/" + entry.Key) != groupXid + "/messages/" + entry.Key)
                {
                    Fail("E_IDENTITY", "messages/" + entry.Key, "Message Xid disagrees with its owning Group");
                }
                ValidateMessage(message, container);
            }
        }

        /// <summary>
        /// Validates an Endpoint document.
        /// </summary>
        public static void ValidateEndpoint(RegistryObjectValueDataType endpoint, bool media = false)
        {
            Object(endpoint, "endpoint");
            try
            {
                s_schemas.Validate(endpoint, "endpoint", media);
            }
            catch (RegistryRuleException) when (media &&
                (OptionalString(endpoint, "protocol") is "RIST-Simple/2020" or "RIST-Main/2024"))
            {
                s_schemas.Validate(endpoint, "endpoint");
            }
            CommonMetadata(endpoint, "endpointid");
            if (TryString(endpoint, "xid", out string? xid))
            {
                string[] parts = xid!.Split('/');
                if (parts.Length != 3 || parts[0] != string.Empty || parts[1] != "endpoints")
                {
                    Fail("E_XID", "xid", "an Endpoint Xid identifies one Group in endpoints");
                }
                GroupIdentity("endpoints", parts[2]);
                if ((OptionalString(endpoint, "endpointid") ?? parts[2]) != parts[2])
                {
                    Fail("E_IDENTITY", "xid", "the Endpoint identifier and Xid disagree");
                }
            }
            IReadOnlyList<string> usage = StringArray(endpoint, "usage", "usage");
            string protocol = CanonicalSelector(OptionalString(endpoint, "protocol") ?? string.Empty, "protocol");
            if (Has(endpoint, "protocol"))
            {
                NonEmpty(OptionalString(endpoint, "protocol")!, "protocol");
            }
            if (usage.Count != 1 && !(usage.Count == 2 && usage.Contains("subscriber") && usage.Contains("consumer") &&
                s_combined.Contains(protocol)))
            {
                Fail("E_USAGE", "usage", "only the protocol-defined subscriber/consumer combination is allowed");
            }
            if (TryString(endpoint, "channel", out string? channel))
            {
                NonEmpty(channel!, "channel");
            }
            RegistryObjectValueDataType options = ObjectOrEmpty(endpoint, "protocoloptions");
            if (TryGet(options, "deployed", out RegistryValueDataType? deployed) && deployed is not RegistryBooleanValueDataType)
            {
                Fail("E_TYPE", "protocoloptions/deployed", "deployed is a Boolean");
            }
            foreach (string key in new[] { "endpoints", "authorization" })
            {
                if (TryGet(options, key, out RegistryValueDataType? item) && item is not RegistryArrayValueDataType)
                {
                    Fail("E_TYPE", "protocoloptions/" + key, "an array is required");
                }
            }
            ProtocolSemantics(protocol, usage, options, media);
            ValidateEnvelopeOptions(endpoint);
            foreach (string item in StringArray(endpoint, "messagegroups", "messagegroups"))
            {
                Reference(item, "messagegroups", "group");
            }
            ValidateContainer(endpoint, OptionalString(endpoint, "xid"));
            if (media)
            {
                MediaSemantics(endpoint);
            }
        }

        /// <summary>
        /// Validates a complete Endpoint Registry document and its Message reference graph.
        /// </summary>
        public static void ValidateRegistry(RegistryObjectValueDataType registry, bool media = false)
        {
            Object(registry, "registry");
            s_schemas.Validate(registry, "registry", media);
            CommonMetadata(registry);
            GroupIdentity("registries", RequireString(registry, "registryid"));
            foreach ((string collection, string singular) in new[] { ("endpoints", "endpoint"), ("messagegroups", "messagegroup") })
            {
                CollectionDiscovery(registry, collection);
                if (!TryObject(registry, collection, out RegistryObjectValueDataType? groups))
                {
                    continue;
                }
                foreach (KeyValuePair<string, RegistryValueDataType> item in Members(groups!))
                {
                    GroupIdentity(collection, item.Key);
                    RegistryObjectValueDataType group = Object(item.Value, "/" + collection + "/" + item.Key);
                    string xid = "/" + collection + "/" + item.Key;
                    if ((OptionalString(group, singular + "id") ?? item.Key) != item.Key ||
                        (OptionalString(group, "xid") ?? xid) != xid)
                    {
                        Fail("E_IDENTITY", xid, "collection key, identifier and Xid must agree");
                    }
                    if (collection == "endpoints")
                    {
                        ValidateEndpoint(group, media);
                    }
                    else
                    {
                        ValidateMessageGroup(group, item.Key);
                    }
                    ValidateContainer(group, xid);
                }
            }
            ValidateReferenceGraph(MessageIndex(registry, true));
        }

        /// <summary>
        /// Validates a standalone Message Registry document.
        /// </summary>
        public static void ValidateMessageRegistry(RegistryObjectValueDataType registry)
        {
            Object(registry, "registry");
            s_schemas.Validate(registry, "message-registry");
            CommonMetadata(registry);
            GroupIdentity("registries", RequireString(registry, "registryid"));
            CollectionDiscovery(registry, "messagegroups");
            if (TryObject(registry, "messagegroups", out RegistryObjectValueDataType? groups))
            {
                foreach (KeyValuePair<string, RegistryValueDataType> item in Members(groups!))
                {
                    GroupIdentity("messagegroups", item.Key);
                    ValidateMessageGroup(Object(item.Value, "/messagegroups/" + item.Key), item.Key);
                }
            }
            ValidateReferenceGraph(MessageIndex(registry, false));
        }

        /// <summary>
        /// Validates an extension definition contract.
        /// </summary>
        public static void ValidateExtensionDefinition(RegistryObjectValueDataType definition)
        {
            Object(definition, "extension");
            string[] fields = ["identity", "version", "selectors", "optionModel", "semanticRules", "defaults", "nativeDependencies", "claims"];
            if (!Members(definition).Select(static item => item.Key).OrderBy(static item => item, StringComparer.Ordinal)
                .SequenceEqual(fields.OrderBy(static item => item, StringComparer.Ordinal)))
            {
                Fail("E_EXTENSION_DEFINITION", "extension", "all eight explicit contract fields are required");
            }
            Uri(RequireString(definition, "identity"), "extension/identity");
            NonEmpty(RequireString(definition, "version"), "extension/version");
            foreach (string key in new[] { "selectors", "semanticRules", "nativeDependencies", "claims" })
            {
                RegistryArrayValueDataType values = Array(Require(definition, key), "extension/" + key);
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (RegistryValueDataType item in values.Items)
                {
                    string value = NonEmptyString(item, "extension/" + key);
                    if (!seen.Add(value))
                    {
                        Fail("E_EXTENSION_DEFINITION", "extension/" + key, "duplicate declarations are ambiguous");
                    }
                }
                if (key == "selectors" && values.Items.Count == 0)
                {
                    Fail("E_EXTENSION_DEFINITION", "extension/selectors", "at least one selector is required");
                }
            }
            foreach (string selector in StringArray(definition, "selectors", "extension/selectors"))
            {
                string[] parts = selector.Split(':');
                if (parts.Length != 2 || parts[1].Length == 0 || parts[0] is not ("protocol" or "envelope"))
                {
                    Fail("E_EXTENSION_DEFINITION", "extension/selectors", "use protocol:<selector> or envelope:<selector>");
                }
            }
            RegistryObjectValueDataType model = Object(Require(definition, "optionModel"), "extension/optionModel");
            RegistryObjectValueDataType defaults = Object(Require(definition, "defaults"), "extension/defaults");
            if (OptionalString(model, "type") != "object" || !TryObject(model, "attributes", out RegistryObjectValueDataType? attributes))
            {
                Fail("E_EXTENSION_DEFINITION", "extension/optionModel", "a Core object attribute model is required");
                throw new InvalidOperationException();
            }
            SupportedModel(model, "extension/optionModel");
            foreach (KeyValuePair<string, RegistryValueDataType> item in Members(defaults))
            {
                if (!Has(attributes!, item.Key))
                {
                    Fail("E_EXTENSION_DEFINITION", "extension/defaults", "defaults require declared attributes");
                }
            }
            RegistryValues.Validate(definition);
        }

        /// <summary>
        /// Returns a materialized Message by overlaying supplied authorized Message definitions.
        /// </summary>
        public static RegistryObjectValueDataType MaterializeMessage(string referenceUri, RegistryObjectValueDataType supplied)
        {
            return MaterializeMessage(referenceUri, supplied, []);
        }

        /// <summary>
        /// Overlays a Message over its base metadata and validates the result.
        /// </summary>
        public static RegistryObjectValueDataType OverlayMessage(RegistryObjectValueDataType baseMessage, RegistryObjectValueDataType current)
        {
            RegistryObjectValueDataType stripped = CloneObject(baseMessage);
            foreach (string key in new[] { "messageid", "versionid", "xid", "self", "epoch", "createdat", "modifiedat", "versions" })
            {
                Remove(stripped, key);
            }
            RegistryObjectValueDataType result = Overlay(stripped, current);
            ValidateMessage(result);
            return result;
        }

        private static void ValidateMessage(RegistryObjectValueDataType message, RegistryObjectValueDataType? group)
        {
            Object(message, "message");
            s_schemas.Validate(message, "message");
            DeclaredTemplates(message, s_messageAttributes.Value);
            CommonMetadata(message, "messageid");
            if (TryString(message, "xid", out string? xid))
            {
                if (!xid!.StartsWith("/", StringComparison.Ordinal))
                {
                    Fail("E_XID", "xid", "an entity Xid is registry-relative");
                }
                Reference(xid, "xid", "message");
                string[] parts = xid.Split('/');
                if ((OptionalString(message, "messageid") ?? parts[4]) != parts[4] ||
                    (parts.Length == 7 && (OptionalString(message, "versionid") ?? parts[6]) != parts[6]))
                {
                    Fail("E_IDENTITY", "xid", "Message or Version identifier disagrees with its Xid");
                }
            }
            foreach (string forbidden in new[] { "document", "message", "messagebase64", "messageurl", "payload", "payloadbase64" })
            {
                if (Has(message, forbidden))
                {
                    Fail("E_MESSAGE_DOCUMENT", forbidden, "Message Resources have hasdocument=false");
                }
            }
            RegistryObjectValueDataType versions = ObjectOrEmpty(message, "versions");
            if (TryInt64(message, "versionscount", out long count) && Has(message, "versions") && count != versions.Members.Count)
            {
                Fail("E_MESSAGE_VERSION", "versionscount", "sole-Version count disagrees");
            }
            foreach (KeyValuePair<string, RegistryValueDataType> item in Members(versions))
            {
                GroupIdentity("versions", item.Key);
                RegistryObjectValueDataType version = Object(item.Value, "versions/" + item.Key);
                if ((Has(version, "versionid") && OptionalString(version, "versionid") != item.Key) ||
                    (Has(message, "versionid") && OptionalString(message, "versionid") != item.Key))
                {
                    Fail("E_MESSAGE_VERSION", "versions", "sole-Version identity disagrees");
                }
                if (Has(version, "versions"))
                {
                    Fail("E_MESSAGE_VERSION", "versions", "nested or historical Version collections are not allowed");
                }
                ValidateMessage(version, group);
                foreach (KeyValuePair<string, RegistryValueDataType> member in Members(version))
                {
                    if (!s_versionIgnored.Contains(member.Key) && TryGet(message, member.Key, out RegistryValueDataType? other) &&
                        !RegistryRuleValues.JsonEqual(member.Value, other!))
                    {
                        Fail("E_MESSAGE_VERSION", "versions/" + item.Key, "logical metadata and sole Version disagree");
                    }
                }
            }
            if (group is not null)
            {
                foreach (string kind in s_selectorKinds)
                {
                    if (TryString(group, kind, out string? parent) &&
                        (!TryString(message, kind, out string? child) || !SelectorAgrees(parent!, child!, kind)))
                    {
                        Fail("E_MESSAGE_SELECTOR", kind, "Message must explicitly match its Group selector");
                    }
                }
            }
            if (Has(message, "envelope") && !Has(message, "envelopemetadata"))
            {
                Fail("E_MESSAGE_ENVELOPE", "envelopemetadata", "an envelope requires its metadata constraints");
            }
            if (Has(message, "protocol") && !Has(message, "protocoloptions"))
            {
                Fail("E_MESSAGE_PROTOCOL", "protocoloptions", "a protocol requires its Message constraint object");
            }
            ValidateEnvelopeOptions(message);
            if (TryGet(message, "protocoloptions", out RegistryValueDataType? templateOptions))
            {
                ScanMessageTemplates(templateOptions!, string.Empty, false);
            }
            MessageProtocol(message);
            foreach (string key in new[] { "envelope", "protocol", "dataschemaformat", "datacontenttype" })
            {
                if (TryString(message, key, out string? value))
                {
                    NonEmpty(value!, key);
                }
            }
            if (TryString(message, "datacontenttype", out string? contentType))
            {
                ContentTypeKey(contentType!, "datacontenttype");
            }
            if (TryString(message, "dataschemaformat", out string? format) &&
                (!format!.Contains('/') || format.Split('/').Any(static part => part.Length == 0)))
            {
                Fail("E_SCHEMA_FORMAT", "dataschemaformat", "schema format must identify a version");
            }
            if (Has(message, "dataschema") && Has(message, "dataschemauri"))
            {
                Fail("E_SCHEMA_REFERENCE", "dataschema", "inline and referenced schemas are mutually exclusive");
            }
            if ((Has(message, "dataschema") || Has(message, "dataschemauri")) && !Has(message, "dataschemaformat"))
            {
                Fail("E_SCHEMA_FORMAT", "dataschemaformat", "schema content or reference requires a format");
            }
            if (Has(message, "dataschema") && string.Equals(format, "apachearrow/1.0", StringComparison.OrdinalIgnoreCase))
            {
                Fail("E_SCHEMA_INLINE", "dataschema", "an Apache Arrow schema document is binary and has no inline form");
            }
            foreach ((string key, string kind) in new[] { ("basemessageuri", "message"), ("dataschemaxid", "schema") })
            {
                if (TryString(message, key, out string? value))
                {
                    if (key == "dataschemaxid" && !value!.StartsWith("/", StringComparison.Ordinal))
                    {
                        Fail("E_XID", key, "dataschemaxid requires a same-registry Xid, not an external URI");
                    }
                    Reference(value!, key, kind);
                }
            }
            if (TryString(message, "dataschemauri", out string? schemaUri))
            {
                if (schemaUri!.StartsWith("/", StringComparison.Ordinal))
                {
                    Reference(schemaUri, "dataschemauri", "schema");
                }
                else
                {
                    Uri(schemaUri, "dataschemauri");
                }
            }
            CloudEvents(message);
            ContentTypeDeclarations(message);
        }
        private static void ProtocolSemantics(string protocol, IReadOnlyList<string> usage, RegistryObjectValueDataType options, bool media)
        {
            if (s_baseProtocols.Contains(protocol))
            {
                HashSet<string> own = KnownOptions(protocol);
                HashSet<string> known = s_knownProtocolOptions.Value;
                string[] invalid = [.. Members(options).Select(static item => item.Key).Where(key => known.Contains(key) && !own.Contains(key)).OrderBy(static key => key, StringComparer.Ordinal)];
                if (invalid.Length > 0)
                {
                    Fail("E_OPTION_PROTOCOL", "protocoloptions", "option belongs to another protocol: " + string.Join(", ", invalid));
                }
            }
            if (protocol.StartsWith("MQTT/", StringComparison.Ordinal) && s_baseProtocols.Contains(protocol))
            {
                if (TryString(options, "topic", out string? topic))
                {
                    MqttTopic(topic!, "protocoloptions/topic", false);
                }
                if (TryString(options, "topicfilter", out string? filter))
                {
                    MqttTopic(filter!, "protocoloptions/topicfilter", true);
                }
                if (Has(options, "topic") && Has(options, "topicfilter"))
                {
                    Fail("E_MQTT_ADDRESSING", "protocoloptions", "topic and topicfilter are mutually exclusive");
                }
                if (TryString(options, "willtopic", out string? willTopic))
                {
                    MqttTopic(willTopic!, "protocoloptions/willtopic", false);
                }
                if (TryString(options, "willmessage", out string? willMessage))
                {
                    Reference(willMessage!, "protocoloptions/willmessage", "message");
                }
                if (TryInt64(options, "qos", out long qos) && qos is < 0 or > 2)
                {
                    Fail("E_MQTT_QOS", "protocoloptions/qos", "QoS is 0, 1 or 2");
                }
                if (TryInt64(options, "sessionexpiryinterval", out long expiry) && expiry > uint.MaxValue)
                {
                    Fail("E_MQTT_SESSION", "protocoloptions/sessionexpiryinterval", "requires UInt32 seconds");
                }
                if (TryString(options, "sharedsubscriptiongroup", out string? group))
                {
                    string text = TemplateLiteral(NonEmpty(group!, "protocoloptions/sharedsubscriptiongroup"));
                    if (!Has(options, "topicfilter") || text.IndexOfAny(['/','+','#','\0']) >= 0)
                    {
                        Fail("E_MQTT_SHARED", "protocoloptions", "shared group requires a filter and a bare valid group name");
                    }
                    if (filter is not null && filter.StartsWith("$share/", StringComparison.Ordinal))
                    {
                        Fail("E_MQTT_SHARED", "protocoloptions/topicfilter", "do not supply two shared-subscription prefixes");
                    }
                    if (OptionalBoolean(options, "nolocal") is true)
                    {
                        Fail("E_MQTT_SHARED", "protocoloptions/nolocal", "MQTT 5 forbids No Local on a shared subscription");
                    }
                }
                if (TryInt64(options, "retainhandling", out long retainHandling) && retainHandling is < 0 or > 2)
                {
                    Fail("E_MQTT_RETAIN", "protocoloptions/retainhandling", "retainhandling is 0, 1 or 2");
                }
                if (Has(options, "subscriptionidentifier"))
                {
                    Fail("E_RUNTIME_STATE", "protocoloptions/subscriptionidentifier", "subscription identifiers are runtime state");
                }
            }
            else if (protocol == "KAFKA")
            {
                if (usage.SequenceEqual(new[] { "consumer" }) && !Has(options, "consumergroup"))
                {
                    Fail("E_KAFKA_GROUP", "protocoloptions/consumergroup", "a consumer must identify the existing group");
                }
                if (usage.SequenceEqual(new[] { "subscriber" }) && Has(options, "consumergroup"))
                {
                    Fail("E_KAFKA_GROUP", "protocoloptions/consumergroup", "a subscriber creates, rather than joins, a group");
                }
                if (!usage.Contains("consumer") && (Has(options, "autooffsetreset") || Has(options, "enableautocommit")))
                {
                    Fail("E_KAFKA_ROLE", "protocoloptions", "offset and commit options apply only to a consumer");
                }
                if (TryInt64(options, "acks", out long acks) && acks is < -1 or > 1)
                {
                    Fail("E_KAFKA_ACKS", "protocoloptions/acks", "acks must be -1, 0 or 1");
                }
            }
            else if (protocol == "NATS")
            {
                if (TryString(options, "subject", out string? subject))
                {
                    NatsSubject(subject!, "protocoloptions/subject", false);
                }
                if (TryString(options, "subjectfilter", out string? subjectFilter))
                {
                    NatsSubject(subjectFilter!, "protocoloptions/subjectfilter", true);
                }
                if (Has(options, "subject") && Has(options, "subjectfilter"))
                {
                    Fail("E_NATS_ADDRESSING", "protocoloptions", "subject and subjectfilter are mutually exclusive");
                }
                if (Has(options, "queuegroup") && !Has(options, "subjectfilter"))
                {
                    Fail("E_NATS_GROUP", "protocoloptions/queuegroup", "a queue group requires a subject filter");
                }
            }
            else if (protocol == "HTTP")
            {
                if (TryString(options, "method", out string? method) && !s_token.IsMatch(TemplateLiteral(method!)))
                {
                    Fail("E_HTTP_METHOD", "protocoloptions/method", "an HTTP method token is required");
                }
                ValidateHttpHeaders(options, "protocoloptions/headers", true);
            }
            Addresses(protocol, options);
            Authorization(options);
            PublicOptions(options, "protocoloptions");
        }

        private static void Addresses(string protocol, RegistryObjectValueDataType options)
        {
            if (!TryGet(options, "endpoints", out RegistryValueDataType? raw))
            {
                return;
            }
            RegistryArrayValueDataType endpoints = Array(raw!, "protocoloptions/endpoints");
            for (int index = 0; index < endpoints.Items.Count; index++)
            {
                string path = "protocoloptions/endpoints/" + index.ToString(CultureInfo.InvariantCulture);
                RegistryObjectValueDataType address = Object(endpoints.Items[index], path);
                if (protocol == "KAFKA")
                {
                    if (!TryGet(address, "bootstrap.servers", out RegistryValueDataType? serversRaw) || serversRaw is not RegistryArrayValueDataType servers || servers.Items.Count == 0 || Has(address, "uri"))
                    {
                        Fail("E_KAFKA_ADDRESS", path, "Kafka uses a non-empty bootstrap.servers list, not uri");
                        throw new InvalidOperationException();
                    }
                    foreach (RegistryValueDataType server in servers.Items)
                    {
                        string candidate = TemplateLiteral(NonEmptyString(server, path));
                        if (!candidate.Contains("://", StringComparison.Ordinal))
                        {
                            candidate = "kafka://" + candidate;
                        }
                        Uri parsed = Uri(candidate, path, explicitPort: true);
                        if (!string.IsNullOrEmpty(parsed.AbsolutePath.Trim('/')) || !string.IsNullOrEmpty(parsed.Query) || !string.IsNullOrEmpty(parsed.Fragment))
                        {
                            Fail("E_KAFKA_ADDRESS", path, "bootstrap entries identify host and port only");
                        }
                    }
                }
                else if (s_baseProtocols.Contains(protocol))
                {
                    if (!TryString(address, "uri", out string? uri))
                    {
                        Fail("E_ADDRESS", path, "this protocol requires a uri address");
                    }
                    string[] schemes = protocol switch
                    {
                        "HTTP" => ["http", "https"],
                        "AMQP/1.0" => ["amqp", "amqps"],
                        "MQTT/3.1.1" or "MQTT/5.0" => ["mqtt", "mqtts", "tcp", "ssl", "wss"],
                        "NATS" => ["nats", "tls", "ws"],
                        _ => []
                    };
                    Uri parsed = Uri(uri!, path + "/uri", true, schemes, protocol == "NATS");
                    if (protocol.StartsWith("MQTT/", StringComparison.Ordinal))
                    {
                        if ((parsed.Scheme is "tcp" or "ssl" or "wss") && parsed.AbsolutePath != "/")
                        {
                            Fail("E_MQTT_ADDRESS", path, "tcp, ssl and wss addresses must not have a path");
                        }
                        if ((parsed.Scheme is "mqtt" or "mqtts") && parsed.AbsolutePath != "/")
                        {
                            MqttTopic(System.Uri.UnescapeDataString(parsed.AbsolutePath[1..]), path + "/uri", false);
                        }
                    }
                }
            }
        }

        private static void Authorization(RegistryObjectValueDataType options)
        {
            if (!TryGet(options, "authorization", out RegistryValueDataType? raw))
            {
                return;
            }
            RegistryArrayValueDataType values = Array(raw!, "protocoloptions/authorization");
            for (int index = 0; index < values.Items.Count; index++)
            {
                string path = "protocoloptions/authorization/" + index.ToString(CultureInfo.InvariantCulture);
                RegistryObjectValueDataType entry = Object(values.Items[index], path);
                if (entry.Members.Count == 0)
                {
                    Fail("E_AUTH_METADATA", path, "an authorization alternative needs selection metadata");
                }
                foreach (string key in new[] { "type", "mechanism" })
                {
                    if (TryString(entry, key, out string? value))
                    {
                        NonEmpty(value!, path + "/" + key);
                    }
                }
                if (Has(entry, "mechanism") && OptionalString(entry, "type") != "SASL")
                {
                    Fail("E_AUTH_MECHANISM", path, "mechanism applies only to SASL");
                }
                foreach (string key in new[] { "authorityuri", "resourceuri" })
                {
                    if (TryString(entry, key, out string? value))
                    {
                        Uri(value!, path + "/" + key, true);
                    }
                }
            }
        }

        private static void MessageProtocol(RegistryObjectValueDataType message)
        {
            string protocol = CanonicalSelector(OptionalString(message, "protocol") ?? string.Empty, "protocol");
            RegistryObjectValueDataType options = ObjectOrEmpty(message, "protocoloptions");
            if (protocol.StartsWith("MQTT/", StringComparison.Ordinal))
            {
                if (TryInt64(options, "qos", out long qos) && qos is < 0 or > 2)
                {
                    Fail("E_MQTT_QOS", "protocoloptions/qos", "QoS is 0, 1 or 2");
                }
                foreach (string key in new[] { "topic_name", "response_topic" })
                {
                    if (TryString(options, key, out string? value))
                    {
                        MqttTopic(MessageTemplateLiteral(value!), "protocoloptions/" + key, false);
                    }
                }
                if (TryInt64(options, "message_expiry_interval", out long expiry) && expiry is < 0 or > uint.MaxValue)
                {
                    Fail("E_MQTT_SESSION", "protocoloptions/message_expiry_interval", "requires UInt32 seconds");
                }
            }
            else if (protocol == "KAFKA")
            {
                if (Has(options, "key") && Has(options, "key_base64"))
                {
                    Fail("E_KAFKA_KEY", "protocoloptions", "text and binary key constraints are mutually exclusive");
                }
                if (TryString(options, "key_base64", out string? encoded))
                {
                    try
                    {
                        Convert.FromBase64String(encoded!);
                    }
                    catch (FormatException)
                    {
                        Fail("E_KAFKA_KEY", "protocoloptions/key_base64", "a binary key requires valid base64");
                    }
                }
                if (TryInt64(options, "partition", out long partition) && partition < 0)
                {
                    Fail("E_KAFKA_PARTITION", "protocoloptions/partition", "a partition is nonnegative");
                }
            }
            else if (protocol == "HTTP")
            {
                if (TryString(options, "method", out string? method) && !s_token.IsMatch(method!))
                {
                    Fail("E_HTTP_METHOD", "protocoloptions/method", "an HTTP method token is required");
                }
                ValidateHttpHeaders(options, "protocoloptions/headers", false);
            }
            else if (protocol == "NATS")
            {
                foreach (string key in new[] { "subject", "reply" })
                {
                    if (TryString(options, key, out string? value))
                    {
                        NatsSubject(MessageTemplateLiteral(value!), "protocoloptions/" + key, false);
                    }
                }
            }
        }

        private static void ValidateEnvelopeOptions(RegistryObjectValueDataType document)
        {
            if (TryString(document, "envelope", out string? envelope))
            {
                NonEmpty(envelope!, "envelope");
            }
            RegistryObjectValueDataType options = ObjectOrEmpty(document, "envelopeoptions");
            if (CanonicalSelector(OptionalString(document, "envelope") ?? string.Empty, "envelope") == "CLOUDEVENTS/1.0" &&
                OptionalString(options, "mode") == "binary" && Has(options, "format"))
            {
                Fail("E_ENVELOPE_MODE", "envelopeoptions", "binary mode must not specify a structured format");
            }
        }

        private static void CloudEvents(RegistryObjectValueDataType message)
        {
            if (CanonicalSelector(OptionalString(message, "envelope") ?? string.Empty, "envelope") != "CLOUDEVENTS/1.0")
            {
                return;
            }
            RegistryObjectValueDataType meta = Object(Require(message, "envelopemetadata"), "envelopemetadata");
            if (Has(meta, "attributes"))
            {
                Fail("E_SOURCE_CONFLICT", "envelopemetadata/attributes", "the pinned model uses a direct declaration map");
            }
            foreach (KeyValuePair<string, RegistryValueDataType> item in Members(meta))
            {
                if (!s_ceName.IsMatch(item.Key))
                {
                    Fail("E_CE_NAME", "envelopemetadata/" + item.Key, "CloudEvents names are lower-case alphanumeric");
                }
                RegistryObjectValueDataType definition = Object(item.Value, "envelopemetadata/" + item.Key);
                if ((item.Key is "specversion" or "id" or "type" or "source") && OptionalBoolean(definition, "required") is false)
                {
                    Fail("E_CE_REQUIRED", "envelopemetadata/" + item.Key, "the CloudEvents required attribute cannot be optional");
                }
            }
            if (TryObject(meta, "dataschema", out RegistryObjectValueDataType? schema) &&
                TryString(schema!, "value", out string? schemaValue) && TryString(message, "dataschemauri", out string? schemaUri) && schemaValue != schemaUri)
            {
                Fail("E_CE_SCHEMA", "envelopemetadata/dataschema", "schema references disagree");
            }
            if (TryObject(meta, "datacontenttype", out RegistryObjectValueDataType? content) &&
                TryString(content!, "value", out string? contentValue) && TryString(message, "datacontenttype", out string? declared) &&
                ContentTypeKey(contentValue!, "datacontenttype") != ContentTypeKey(declared!, "datacontenttype"))
            {
                Fail("E_CE_CONTENT_TYPE", "envelopemetadata/datacontenttype", "duplicate content types disagree");
            }
        }

        private static void ContentTypeDeclarations(RegistryObjectValueDataType message)
        {
            if (!TryString(message, "datacontenttype", out string? declared))
            {
                return;
            }
            string expected = ContentTypeKey(declared!, "datacontenttype");
            string protocol = CanonicalSelector(OptionalString(message, "protocol") ?? string.Empty, "protocol");
            RegistryObjectValueDataType options = ObjectOrEmpty(message, "protocoloptions");
            RegistryObjectValueDataType framing = ObjectOrEmpty(message, "envelopeoptions");
            var candidates = new List<KeyValuePair<string, string>>();
            if (OptionalString(framing, "mode") == "structured" && TryString(framing, "format", out string? format))
            {
                candidates.Add(new KeyValuePair<string, string>("envelopeoptions/format", format!));
            }
            if (protocol == "MQTT/5.0" && TryString(options, "content_type", out string? mqttContent))
            {
                candidates.Add(new KeyValuePair<string, string>("protocoloptions/content_type", mqttContent!));
            }
            else if (protocol == "AMQP/1.0" && TryObject(options, "properties", out RegistryObjectValueDataType? properties) &&
                TryObject(properties!, "content-type", out RegistryObjectValueDataType? content) && TryString(content!, "value", out string? value))
            {
                candidates.Add(new KeyValuePair<string, string>("protocoloptions/properties/content-type/value", value!));
            }
            else if (protocol is "HTTP" or "KAFKA" or "NATS" && TryGet(options, "headers", out RegistryValueDataType? headers))
            {
                foreach (RegistryObjectValueDataType header in HeaderObjects(headers!))
                {
                    if (string.Equals(OptionalString(header, "name") ?? string.Empty, "content-type", StringComparison.OrdinalIgnoreCase) &&
                        TryString(header, "value", out string? headerValue))
                    {
                        candidates.Add(new KeyValuePair<string, string>("protocoloptions/headers", headerValue!));
                    }
                }
            }
            foreach (KeyValuePair<string, string> candidate in candidates)
            {
                if (ContentTypeKey(candidate.Value, candidate.Key) != expected)
                {
                    Fail("E_CONTENT_TYPE", candidate.Key, "explicit Message content-type declarations disagree");
                }
            }
        }
        private static void MediaSemantics(RegistryObjectValueDataType document)
        {
            IReadOnlyList<string> usage = StringArray(document, "usage", "usage");
            if (usage.Count != 1 || usage[0] is not ("producer" or "consumer"))
            {
                Fail("E_MEDIA_USAGE", "usage", "media uses exactly one producer or consumer role");
            }
            if (Has(document, "messagegroups") || Has(document, "envelope") || Has(document, "envelopeoptions") || Has(document, "envelopemetadata"))
            {
                Fail("E_MEDIA_ASSOCIATION", "endpoint", "media Endpoints must not declare Message groups or envelopes");
            }
            if ((TryObject(document, "messages", out RegistryObjectValueDataType? messages) && messages!.Members.Count > 0) ||
                (TryInt64(document, "messagescount", out long count) && count != 0))
            {
                Fail("E_MEDIA_ASSOCIATION", "messages", "media Endpoints must not associate Messages");
            }
            RegistryObjectValueDataType options = ObjectOrEmpty(document, "protocoloptions");
            if (Members(options).Any(static item => s_runtimeMediaKeys.Contains(item.Key)))
            {
                Fail("E_MEDIA_SESSION_STATE", "protocoloptions", "dynamic session state is not static endpoint metadata");
            }
            if (usage[0] == "consumer" && s_inputOptions.Any(key => Has(options, key)))
            {
                Fail("E_MEDIA_CONSUMER_INPUT", "protocoloptions", "consumers omit producer input requirements");
            }
            string protocol = CanonicalSelector(OptionalString(document, "protocol") ?? string.Empty, "protocol");
            if (!s_mediaProtocols.TryGetValue(protocol, out MediaDefinition? definition))
            {
                return;
            }
            if (protocol.StartsWith("RIST-", StringComparison.Ordinal))
            {
                return;
            }
            if (!definition.Usage.Contains(usage[0]))
            {
                Fail("E_MEDIA_DIRECTION", "usage", "the selected mapping does not define this media direction");
            }
            if (TryGet(options, "endpoints", out RegistryValueDataType? rawEndpoints))
            {
                RegistryArrayValueDataType endpoints = Array(rawEndpoints!, "protocoloptions/endpoints");
                for (int index = 0; index < endpoints.Items.Count; index++)
                {
                    string path = "protocoloptions/endpoints/" + index.ToString(CultureInfo.InvariantCulture);
                    RegistryObjectValueDataType address = Object(endpoints.Items[index], path);
                    if (!TryString(address, "uri", out string? uri))
                    {
                        Fail("E_ADDRESS", path, "media mappings use a uri entry point");
                    }
                    Uri(uri!, path + "/uri", true, definition.Schemes, definition.ExplicitPort);
                }
            }
            RegistryObjectValueDataType effective = EffectiveMediaOptions(document, definition);
            if (usage[0] == "producer")
            {
                foreach (string key in s_inputOptions)
                {
                    NonEmpty(OptionalString(effective, key) ?? string.Empty, "protocoloptions/" + key);
                }
                string streamFormat = RequireString(effective, "streamformat");
                string video = RequireString(effective, "videocodec");
                string audio = RequireString(effective, "audiocodec");
                if (!definition.Formats.Contains(streamFormat))
                {
                    Fail("E_MEDIA_FORMAT", "protocoloptions/streamformat", "format is not admitted by this mapping");
                }
                if (video == "none" && audio == "none")
                {
                    Fail("E_MEDIA_EMPTY", "protocoloptions", "at least one media kind must be present");
                }
                if (s_audioCodecs.Contains(video) || s_videoCodecs.Contains(audio))
                {
                    Fail("E_MEDIA_CODEC_KIND", "protocoloptions", "a known codec was assigned to the wrong media kind");
                }
            }
            if (protocol == "SRT" && TryString(options, "streamid", out string? streamId))
            {
                string text = TemplateLiteral(streamId!);
                if (text.Contains('\0') || System.Text.Encoding.UTF8.GetByteCount(text) > 512)
                {
                    Fail("E_SRT_STREAM_ID", "protocoloptions/streamid", "SRT Stream ID exceeds its native 512-byte limit or contains NUL");
                }
            }
            if (protocol == "WEBRTC")
            {
                string? signaling = OptionalString(options, "signaling");
                if (signaling == "WHIP/RFC9725" && usage[0] != "producer")
                {
                    Fail("E_WHIP_DIRECTION", "usage", "WHIP defines producer ingestion");
                }
                if (signaling == "WHEP/draft-ietf-wish-whep-03" && usage[0] != "consumer")
                {
                    Fail("E_WHEP_DIRECTION", "usage", "the pinned WHEP draft defines consumer egress");
                }
                if (signaling == "external")
                {
                    if (!TryString(options, "signalingreference", out string? reference))
                    {
                        Fail("E_SIGNALING_REFERENCE", "protocoloptions/signalingreference", "external signaling needs a versioned specification URI");
                    }
                    Uri(reference!, "protocoloptions/signalingreference", true, ["https"]);
                }
                else if (Has(options, "signalingreference"))
                {
                    Fail("E_SIGNALING_REFERENCE", "protocoloptions/signalingreference", "fixed signaling selectors omit signalingreference");
                }
            }
            if (protocol == "RTP/2")
            {
                if (usage[0] == "producer" && !Has(options, "sessiondescription"))
                {
                    Fail("E_SDP_REQUIRED", "protocoloptions/sessiondescription", "RTP producers need a native session description");
                }
                if (TryString(options, "sessiondescription", out string? sdp))
                {
                    Uri(sdp!, "protocoloptions/sessiondescription", true, ["https"]);
                }
            }
            if (protocol == "RTMP/1.0")
            {
                foreach (string key in new[] { "application", "streamname" })
                {
                    NonEmpty(OptionalString(options, key) ?? string.Empty, "protocoloptions/" + key);
                }
            }
        }

        private static RegistryObjectValueDataType EffectiveMediaOptions(RegistryObjectValueDataType document, MediaDefinition definition)
        {
            RegistryObjectValueDataType options = CloneObject(ObjectOrEmpty(document, "protocoloptions"));
            if (StringArray(document, "usage", "usage").SequenceEqual(new[] { "producer" }))
            {
                foreach (KeyValuePair<string, RegistryValueDataType> item in definition.Defaults)
                {
                    if (!Has(options, item.Key))
                    {
                        Add(options, item.Key, (RegistryValueDataType)item.Value.Clone());
                    }
                }
            }
            return options;
        }

        private static RegistryObjectValueDataType MaterializeMessage(string referenceUri, RegistryObjectValueDataType supplied, HashSet<string> active)
        {
            if (!active.Add(referenceUri))
            {
                Fail("E_MESSAGE_CYCLE", referenceUri, "recursive base Message reference");
            }
            if (!TryObject(supplied, referenceUri, out RegistryObjectValueDataType? current))
            {
                Fail("E_REFERENCE_MISSING", referenceUri, "definition was not supplied");
            }
            ValidateMessage(current!);
            if (Has(current!, "basemessage"))
            {
                Fail("E_SOURCE_CONFLICT", referenceUri, "basemessage is not an alias for the pinned model attribute");
            }
            RegistryObjectValueDataType result = TryString(current!, "basemessageuri", out string? baseUri)
                ? OverlayMessage(MaterializeMessage(baseUri!, supplied, active), current!)
                : OverlayMessage(new RegistryObjectValueDataType { Kind = 5, Members = [] }, current!);
            active.Remove(referenceUri);
            return result;
        }

        private static Dictionary<string, RegistryObjectValueDataType> MessageIndex(RegistryObjectValueDataType document, bool includeEndpoints)
        {
            var result = new Dictionary<string, RegistryObjectValueDataType>(StringComparer.Ordinal);
            foreach (string collection in includeEndpoints ? new[] { "endpoints", "messagegroups" } : ["messagegroups"])
            {
                if (!TryObject(document, collection, out RegistryObjectValueDataType? groups))
                {
                    continue;
                }
                foreach (KeyValuePair<string, RegistryValueDataType> groupEntry in Members(groups!))
                {
                    RegistryObjectValueDataType group = Object(groupEntry.Value, collection + "/" + groupEntry.Key);
                    if (!TryObject(group, "messages", out RegistryObjectValueDataType? messages))
                    {
                        continue;
                    }
                    foreach (KeyValuePair<string, RegistryValueDataType> messageEntry in Members(messages!))
                    {
                        RegistryObjectValueDataType message = Object(messageEntry.Value, "messages/" + messageEntry.Key);
                        string xid = "/" + collection + "/" + groupEntry.Key + "/messages/" + messageEntry.Key;
                        result[xid] = message;
                        if (TryString(message, "versionid", out string? versionId))
                        {
                            result[xid + "/versions/" + versionId] = message;
                        }
                        if (TryObject(message, "versions", out RegistryObjectValueDataType? versions))
                        {
                            foreach (KeyValuePair<string, RegistryValueDataType> version in Members(versions!))
                            {
                                result[xid + "/versions/" + version.Key] = Object(version.Value, "versions/" + version.Key);
                            }
                        }
                    }
                }
            }
            return result;
        }

        private static void ValidateReferenceGraph(Dictionary<string, RegistryObjectValueDataType> supplied)
        {
            var wrapper = new RegistryObjectValueDataType { Kind = 5, Members = [] };
            foreach (KeyValuePair<string, RegistryObjectValueDataType> item in supplied)
            {
                Add(wrapper, item.Key, item.Value);
            }
            foreach (string key in supplied.Keys)
            {
                try
                {
                    MaterializeMessage(key, wrapper);
                }
                catch (RegistryRuleException error) when (error.Code is "E_REFERENCE_MISSING" or "E_SOURCE_CONFLICT")
                {
                }
            }
        }

        private static void SupportedModel(RegistryObjectValueDataType model, string path)
        {
            foreach (KeyValuePair<string, RegistryValueDataType> member in Members(model))
            {
                if (!s_modelKeys.Contains(member.Key))
                {
                    throw RegistryRuleException.Fail("U_EXTENSION_MODEL", path, "the composer does not implement these model keywords: " + member.Key);
                }
            }
            string kind = OptionalString(model, "type") ?? "any";
            if (Has(model, "required") && OptionalBoolean(model, "required") is null)
            {
                Fail("E_EXTENSION_DEFINITION", path, "type is a String and required is a Boolean");
            }
            foreach (string key in new[] { "name", "description" })
            {
                if (Has(model, key) && OptionalString(model, key) is null)
                {
                    Fail("E_EXTENSION_DEFINITION", path + "/" + key, "a String is required");
                }
            }
            if (kind is "array" or "map" && !Has(model, "item"))
            {
                Fail("E_EXTENSION_DEFINITION", path + "/item", "collection models require an item declaration");
            }
            if ((Has(model, "attributes") && kind != "object") || (Has(model, "item") && kind is not ("array" or "map")))
            {
                throw RegistryRuleException.Fail("U_EXTENSION_MODEL", path, "attributes and item require the matching container type");
            }
            if (TryObject(model, "attributes", out RegistryObjectValueDataType? attributes))
            {
                foreach (KeyValuePair<string, RegistryValueDataType> child in Members(attributes!))
                {
                    NonEmpty(child.Key, path + "/attributes");
                    SupportedModel(Object(child.Value, path + "/" + child.Key), path + "/" + child.Key);
                }
            }
            if (TryObject(model, "item", out RegistryObjectValueDataType? item))
            {
                SupportedModel(item!, path + "/item");
            }
        }
        private static void DeclaredTemplates(RegistryValueDataType value, JsonElementHolder attributes)
        {
            DeclaredTemplates(value, attributes.Element);
        }

        private static void DeclaredTemplates(RegistryValueDataType value, System.Text.Json.JsonElement definition)
        {
            string kind = definition.TryGetProperty("type", out System.Text.Json.JsonElement type) ? type.GetString() ?? "any" : "any";
            if (kind == "uritemplate")
            {
                if (value is RegistryStringValueDataType text)
                {
                    MessageTemplateLiteral(text.Value);
                }
            }
            else if (kind == "object" && value is RegistryObjectValueDataType map)
            {
                if (definition.TryGetProperty("attributes", out System.Text.Json.JsonElement attributes))
                {
                    foreach (System.Text.Json.JsonProperty attribute in attributes.EnumerateObject())
                    {
                        if (attribute.Name != "*" && TryGet(map, attribute.Name, out RegistryValueDataType? child))
                        {
                            DeclaredTemplates(child!, attribute.Value);
                        }
                    }
                }
            }
            else if (kind == "array" && value is RegistryArrayValueDataType array && definition.TryGetProperty("item", out System.Text.Json.JsonElement item))
            {
                foreach (RegistryValueDataType child in array.Items)
                {
                    DeclaredTemplates(child, item);
                }
            }
            else if (kind == "map" && value is RegistryObjectValueDataType objectMap && definition.TryGetProperty("item", out System.Text.Json.JsonElement mapItem))
            {
                foreach (KeyValuePair<string, RegistryValueDataType> child in Members(objectMap))
                {
                    DeclaredTemplates(child.Value, mapItem);
                }
            }
        }

        private static string ContentTypeKey(string value, string path)
        {
            NonEmpty(value, path);
            Match match = s_contentType.Match(value);
            if (!match.Success)
            {
                Fail("E_CONTENT_TYPE", path, "a valid MIME media type and unambiguous parameters are required");
            }
            var parameters = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (Match parameter in s_contentTypeParameter.Matches(match.Groups[3].Value))
            {
                string name = parameter.Groups[1].Value.ToLowerInvariant();
                string text = parameter.Groups[2].Value;
                if (parameters.ContainsKey(name))
                {
                    Fail("E_CONTENT_TYPE", path, "duplicate MIME parameter names are ambiguous");
                }
                if (text.StartsWith("\"", StringComparison.Ordinal))
                {
                    text = Regex.Replace(text[1..^1], "\\\\([\\t -~])", "$1");
                }
                parameters[name] = name == "charset" ? text.ToLowerInvariant() : text;
            }
            return match.Groups[1].Value.ToLowerInvariant() + "/" + match.Groups[2].Value.ToLowerInvariant() + ";" +
                string.Join(";", parameters.Select(static item => item.Key + "=" + item.Value));
        }

        private static void MqttTopic(string value, string path, bool filter)
        {
            NonEmpty(value, path);
            string text = TemplateLiteral(value);
            if (System.Text.Encoding.UTF8.GetByteCount(text) > 65535 || text.Contains('\0'))
            {
                Fail("E_MQTT_TOPIC", path, "MQTT topic exceeds its UTF-8 limit or contains NUL");
            }
            if (!filter && text.IndexOfAny(['+', '#']) >= 0)
            {
                Fail("E_MQTT_TOPIC", path, "publish topics cannot contain wildcards");
            }
            if (filter)
            {
                string[] levels = text.Split('/');
                for (int index = 0; index < levels.Length; index++)
                {
                    string level = levels[index];
                    if ((level.Contains('+') && level != "+") ||
                        (level.Contains('#') && (level != "#" || index != levels.Length - 1)))
                    {
                        Fail("E_MQTT_FILTER", path, "wildcards must occupy levels; # must be last");
                    }
                }
            }
        }

        private static void NatsSubject(string value, string path, bool filter)
        {
            string text = TemplateLiteral(NonEmpty(value, path));
            string[] parts = text.Split('.');
            if (parts.Any(static part => part.Length == 0 || part.Any(char.IsWhiteSpace)))
            {
                Fail("E_NATS_SUBJECT", path, "NATS subjects require non-empty space-free tokens");
            }
            for (int index = 0; index < parts.Length; index++)
            {
                string part = parts[index];
                if ((part.Contains('*') || part.Contains('>')) && !(filter && (part == "*" || part == ">" && index == parts.Length - 1)))
                {
                    Fail("E_NATS_SUBJECT", path, "NATS wildcard position or role is invalid");
                }
            }
        }

        private static string MessageTemplateLiteral(string text)
        {
            if (text.Length == 0)
            {
                return text;
            }
            foreach (string name in TemplateNames(text))
            {
                if (!s_templateSymbol.IsMatch(name))
                {
                    Fail("E_TEMPLATE", "protocoloptions", "Message Level 1 template variables must be symbols");
                }
            }
            return TemplateLiteral(text);
        }

        private static void ScanMessageTemplates(RegistryValueDataType value, string key, bool parentTemplate)
        {
            if (value is RegistryStringValueDataType text)
            {
                if ((parentTemplate || s_messageTemplateKeys.Contains(key)) &&
                    (text.Value.IndexOf('{') >= 0 || text.Value.IndexOf('}') >= 0))
                {
                    MessageTemplateLiteral(text.Value);
                }
            }
            else if (value is RegistryArrayValueDataType array)
            {
                foreach (RegistryValueDataType item in array.Items.ToArray())
                {
                    ScanMessageTemplates(item, key, false);
                }
            }
            else if (value is RegistryObjectValueDataType map)
            {
                bool isTemplate = OptionalString(map, "type") == "uritemplate";
                foreach (KeyValuePair<string, RegistryValueDataType> member in Members(map))
                {
                    ScanMessageTemplates(member.Value, member.Key, isTemplate && member.Key == "value");
                }
            }
        }

        private static string TemplateLiteral(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }
            TemplateNames(text);
            return s_template.Replace(text, "x");
        }

        private static IEnumerable<string> TemplateNames(string text)
        {
            NonEmpty(text, "template");
            var result = new List<string>();
            foreach (Match match in s_template.Matches(text))
            {
                result.AddRange(match.Groups[1].Value.Split(','));
            }
            string rest = s_template.Replace(text, "x");
            if (rest.Contains('{') || rest.Contains('}') || result.Any(static name => Regex.IsMatch(name, "%(?![0-9A-Fa-f]{2})")))
            {
                Fail("E_TEMPLATE", "protocoloptions", "only RFC 6570 Level 1 expressions are supported");
            }
            return result;
        }

        private static void ValidateHttpHeaders(RegistryObjectValueDataType options, string path, bool endpoint)
        {
            if (!TryGet(options, "headers", out RegistryValueDataType? headers))
            {
                return;
            }
            int index = 0;
            foreach (RegistryObjectValueDataType header in HeaderObjects(headers!))
            {
                string itemPath = endpoint ? path + "/" + index.ToString(CultureInfo.InvariantCulture) : path;
                if (endpoint && (!Has(header, "name") || !Has(header, "value")))
                {
                    Fail("E_HTTP_HEADER", itemPath, "name and value are required");
                }
                string name = OptionalString(header, "name") ?? string.Empty;
                string value = OptionalString(header, "value") ?? string.Empty;
                string checkedName = endpoint ? TemplateLiteral(name) : name;
                if (!s_token.IsMatch(checkedName) || value.IndexOfAny(['\r', '\n']) >= 0)
                {
                    Fail("E_HTTP_HEADER", itemPath, endpoint ? "invalid header name or line break" : "invalid header constraint");
                }
                index++;
            }
        }

        private static IEnumerable<RegistryObjectValueDataType> HeaderObjects(RegistryValueDataType headers)
        {
            if (headers is RegistryArrayValueDataType array)
            {
                foreach (RegistryValueDataType item in array.Items.ToArray())
                {
                    yield return Object(item, "protocoloptions/headers");
                }
            }
            else if (headers is RegistryObjectValueDataType map)
            {
                foreach (KeyValuePair<string, RegistryValueDataType> item in Members(map))
                {
                    yield return Object(item.Value, "protocoloptions/headers");
                }
            }
        }

        private static void PublicOptions(RegistryValueDataType value, string path)
        {
            if (value is RegistryArrayValueDataType array)
            {
                for (int index = 0; index < array.Items.Count; index++)
                {
                    PublicOptions(array.Items[index], path + "/" + index.ToString(CultureInfo.InvariantCulture));
                }
            }
            else if (value is RegistryObjectValueDataType map)
            {
                foreach (KeyValuePair<string, RegistryValueDataType> item in Members(map))
                {
                    if (s_secretKeys.Contains(item.Key.ToLowerInvariant()))
                    {
                        Fail("E_SECRET", path + "/" + item.Key, "credential values belong outside public metadata");
                    }
                    PublicOptions(item.Value, path + "/" + item.Key);
                }
                string name = (OptionalString(map, "name") ?? string.Empty).ToLowerInvariant();
                if (name is "authorization" or "proxy-authorization" or "cookie" or "set-cookie")
                {
                    Fail("E_SECRET", path, "secret-bearing HTTP headers are not public metadata");
                }
            }
            else if (value is RegistryStringValueDataType text)
            {
                TemplateLiteral(text.Value);
            }
        }

        private static Uri Uri(string value, string path, bool templated = false, string[]? schemes = null, bool explicitPort = false)
        {
            NonEmpty(value, path);
            string candidate = templated ? TemplateLiteral(value) : value;
            if (!System.Uri.TryCreate(candidate, UriKind.Absolute, out Uri? parsed) || string.IsNullOrEmpty(parsed.Scheme))
            {
                Fail("E_URI", path, "a valid absolute URI is required");
            }
            if (schemes is not null && !schemes.Contains(parsed.Scheme, StringComparer.OrdinalIgnoreCase))
            {
                Fail("E_URI_SCHEME", path, "scheme is not admitted");
            }
            if (!string.IsNullOrEmpty(parsed.UserInfo))
            {
                Fail("E_SECRET", path, "URI userinfo is not public catalog metadata");
            }
            if (explicitPort && parsed.IsDefaultPort)
            {
                Fail("E_URI_PORT", path, "an explicit port is required");
            }
            if (!string.IsNullOrEmpty(parsed.Authority) && string.IsNullOrEmpty(parsed.Host))
            {
                Fail("E_URI", path, "an address requires a host");
            }
            return parsed;
        }

        private static string Reference(string value, string path, string kind)
        {
            NonEmpty(value, path);
            if (!value.StartsWith("/", StringComparison.Ordinal))
            {
                Uri(value, path);
                return value;
            }
            string[] parts = value.Split('/').Skip(1).ToArray();
            try
            {
                if (parts.Length < 2)
                {
                    throw new FormatException();
                }
                foreach (string part in parts)
                {
                    GroupIdentity("ids", part);
                }
            }
            catch (FormatException)
            {
                Fail("E_XID", path, "a concrete registry-relative Xid is required");
            }
            if (kind == "group" && (parts.Length != 2 || parts[0] != "messagegroups"))
            {
                Fail("E_REFERENCE_ROLE", path, "a local Message Group reference is required");
            }
            if (kind == "message" && !(parts.Length is 4 or 6 && parts[2] == "messages" && (parts.Length == 4 || parts[4] == "versions")))
            {
                Fail("E_REFERENCE_ROLE", path, "a Message Resource or sole Version reference is required");
            }
            if (kind == "schema" && !(parts.Length is 4 or 6 && parts[0] == "schemagroups" && parts[2] == "schemas" && (parts.Length == 4 || parts[4] == "versions")))
            {
                Fail("E_REFERENCE_ROLE", path, "a schema Resource or Version Xid is required");
            }
            return value;
        }

        private static void CommonMetadata(RegistryObjectValueDataType document, params string[] identifiers)
        {
            foreach (string name in new[] { "registryid", "versionid" }.Concat(identifiers))
            {
                if (TryString(document, name, out string? value))
                {
                    try
                    {
                        GroupIdentity("ids", value!);
                    }
                    catch (FormatException)
                    {
                        Fail("E_IDENTITY", name, "a concrete xRegistry identifier is required");
                    }
                }
            }
            foreach (string name in new[] { "self", "documentation" })
            {
                if (TryString(document, name, out string? value))
                {
                    Uri(value!, name);
                }
            }
            foreach (string name in new[] { "createdat", "modifiedat" })
            {
                if (TryString(document, name, out string? value))
                {
                    if (!s_timestamp.IsMatch(value!) || !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                    {
                        Fail("E_TIMESTAMP", name, "invalid calendar date or time");
                    }
                }
            }
            if (TryGet(document, "epoch", out RegistryValueDataType? epoch) && (!RegistryRuleValues.TryInt64(epoch!, out long number) || number < 1 || number > uint.MaxValue))
            {
                Fail("E_EPOCH", "epoch", "the native epoch is a positive UInt32");
            }
        }

        private static void CollectionDiscovery(RegistryObjectValueDataType document, string collection)
        {
            string count = collection + "count";
            string url = collection + "url";
            if (TryGet(document, count, out RegistryValueDataType? countValue))
            {
                if (!RegistryRuleValues.TryInt64(countValue!, out long number) || number < 0)
                {
                    Fail("E_COLLECTION_COUNT", count, "a nonnegative integer count is required");
                }
                if (TryObject(document, collection, out RegistryObjectValueDataType? items) && number != items!.Members.Count)
                {
                    Fail("E_COLLECTION_COUNT", count, "the complete inline collection and its count disagree");
                }
            }
            if (TryString(document, url, out string? value))
            {
                Uri(value!, url);
            }
        }

        private static string CanonicalSelector(string value, string kind)
        {
            string upper = value.ToUpperInvariant();
            if (kind == "protocol")
            {
                return upper switch
                {
                    "MQTT" => "MQTT/5.0",
                    "AMQP" => "AMQP/1.0",
                    _ => upper
                };
            }
            return kind == "envelope" ? upper : value;
        }

        private static bool SelectorAgrees(string parent, string child, string kind)
        {
            string first = CanonicalSelector(parent, kind);
            string second = CanonicalSelector(child, kind);
            return first == second || kind == "envelope" && !first.Contains('/') && second.StartsWith(first + "/", StringComparison.Ordinal);
        }

        private static void GroupIdentity(string collection, string value)
        {
            if (string.IsNullOrEmpty(value) || value.Contains('/') || value.Contains('~'))
            {
                throw new FormatException(collection);
            }
        }

        private static HashSet<string> KnownOptions(string protocol)
        {
            return s_protocolOptions.Value.TryGetValue(protocol, out HashSet<string>? result) ? result : [];
        }

        private static Dictionary<string, HashSet<string>> LoadProtocolOptions()
        {
            var result = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            using System.Text.Json.JsonDocument model = s_schemas.LoadJsonDocument("resolved-endpoint-model.json");
            System.Text.Json.JsonElement ifValues = model.RootElement.GetProperty("groups").GetProperty("endpoints")
                .GetProperty("attributes").GetProperty("protocol").GetProperty("ifvalues");
            foreach (System.Text.Json.JsonProperty protocol in ifValues.EnumerateObject())
            {
                string key = CanonicalSelector(protocol.Name, "protocol");
                System.Text.Json.JsonElement attrs = protocol.Value.GetProperty("siblingattributes").GetProperty("protocoloptions").GetProperty("attributes");
                result[key] = [.. attrs.EnumerateObject().Select(static item => item.Name)];
            }
            return result;
        }

        private static Dictionary<string, MediaDefinition> LoadMediaDefinitions()
        {
            var result = new Dictionary<string, MediaDefinition>(StringComparer.Ordinal);
            using System.Text.Json.JsonDocument media = s_schemas.LoadJsonDocument("media-extension.json");
            foreach (System.Text.Json.JsonProperty protocol in media.RootElement.GetProperty("protocols").EnumerateObject())
            {
                string name = CanonicalSelector(protocol.Name, "protocol");
                System.Text.Json.JsonElement value = protocol.Value;
                var defaults = new Dictionary<string, RegistryValueDataType>(StringComparer.Ordinal);
                if (value.TryGetProperty("defaults", out System.Text.Json.JsonElement defaultValues))
                {
                    foreach (System.Text.Json.JsonProperty item in defaultValues.EnumerateObject())
                    {
                        defaults[item.Name] = RegistryValues.Parse(System.Text.Encoding.UTF8.GetBytes(item.Value.GetRawText()));
                    }
                }
                result[name] = new MediaDefinition(
                    [.. value.GetProperty("usage").EnumerateArray().Select(static item => item.GetString()!)],
                    [.. value.GetProperty("schemes").EnumerateArray().Select(static item => item.GetString()!)],
                    value.TryGetProperty("explicitPort", out System.Text.Json.JsonElement explicitPort) && explicitPort.GetBoolean(),
                    [.. value.TryGetProperty("formats", out System.Text.Json.JsonElement formats) ? formats.EnumerateArray().Select(static item => item.GetString()!) : []],
                    defaults);
            }
            return result;
        }
        private static RegistryObjectValueDataType? ParseGroup(string? group)
        {
            return group is null ? null : Object(RegistryValues.Parse(System.Text.Encoding.UTF8.GetBytes(group)), "group");
        }

        private static RegistryObjectValueDataType CloneObject(RegistryObjectValueDataType value)
        {
            return (RegistryObjectValueDataType)value.Clone();
        }

        private static RegistryObjectValueDataType Overlay(RegistryObjectValueDataType left, RegistryObjectValueDataType right)
        {
            RegistryObjectValueDataType result = CloneObject(left);
            foreach (KeyValuePair<string, RegistryValueDataType> item in Members(right))
            {
                if (TryObject(result, item.Key, out RegistryObjectValueDataType? leftChild) && item.Value is RegistryObjectValueDataType rightChild)
                {
                    Set(result, item.Key, Overlay(leftChild!, rightChild));
                }
                else
                {
                    Set(result, item.Key, (RegistryValueDataType)item.Value.Clone());
                }
            }
            return result;
        }

        private static void Add(RegistryObjectValueDataType value, string name, RegistryValueDataType item)
        {
            var members = value.Members.ToList();
            members.Add(new RegistryMemberDataType { Name = name, Value = item });
            value.Members = [.. members];
        }

        private static void Set(RegistryObjectValueDataType value, string name, RegistryValueDataType item)
        {
            var members = value.Members.ToList();
            int index = members.FindIndex(member => member.Name == name);
            var member = new RegistryMemberDataType { Name = name, Value = item };
            if (index < 0)
            {
                members.Add(member);
            }
            else
            {
                members[index] = member;
            }
            value.Members = [.. members];
        }

        private static void Remove(RegistryObjectValueDataType value, string name)
        {
            value.Members = [.. value.Members.ToArray().Where(member => member.Name != name)];
        }

        private static RegistryValueDataType Require(RegistryObjectValueDataType value, string name)
        {
            if (TryGet(value, name, out RegistryValueDataType? item))
            {
                return item!;
            }
            Fail("E_SCHEMA", name, "a required property is missing");
            throw new InvalidOperationException();
        }

        private static string RequireString(RegistryObjectValueDataType value, string name)
        {
            return NonEmptyString(Require(value, name), name);
        }

        private static bool TryString(RegistryObjectValueDataType value, string name, out string? text)
        {
            text = OptionalString(value, name);
            return text is not null;
        }

        private static bool TryObject(RegistryObjectValueDataType value, string name, out RegistryObjectValueDataType? child)
        {
            if (TryGet(value, name, out RegistryValueDataType? item) && item is RegistryObjectValueDataType map && item.Kind == 5)
            {
                child = map;
                return true;
            }
            child = null;
            return false;
        }

        private static bool TryInt64(RegistryObjectValueDataType value, string name, out long result)
        {
            if (TryGet(value, name, out RegistryValueDataType? item) && RegistryRuleValues.TryInt64(item!, out result))
            {
                return true;
            }
            result = 0;
            return false;
        }

        private static RegistryObjectValueDataType ObjectOrEmpty(RegistryObjectValueDataType value, string name)
        {
            return RegistryRuleValues.ObjectOrEmpty(value, name);
        }

        private static RegistryObjectValueDataType Object(RegistryValueDataType value, string path)
        {
            return RegistryRuleValues.Object(value, path);
        }

        private static RegistryArrayValueDataType Array(RegistryValueDataType value, string path)
        {
            return RegistryRuleValues.Array(value, path);
        }

        private static string NonEmptyString(RegistryValueDataType value, string path)
        {
            return RegistryRuleValues.NonEmptyString(value, path);
        }

        private static string NonEmpty(string value, string path)
        {
            if (string.IsNullOrEmpty(value))
            {
                Fail("E_STRING", path, "a non-empty String is required");
            }
            return value;
        }

        private static string? OptionalString(RegistryObjectValueDataType value, string name)
        {
            return RegistryRuleValues.OptionalString(value, name);
        }

        private static bool? OptionalBoolean(RegistryObjectValueDataType value, string name)
        {
            return RegistryRuleValues.OptionalBoolean(value, name);
        }

        private static IReadOnlyList<string> StringArray(RegistryObjectValueDataType value, string name, string path)
        {
            return RegistryRuleValues.StringArray(value, name, path);
        }

        private static bool Has(RegistryObjectValueDataType value, string name)
        {
            return RegistryRuleValues.Has(value, name);
        }

        private static bool TryGet(RegistryObjectValueDataType value, string name, out RegistryValueDataType? item)
        {
            return RegistryRuleValues.TryGet(value, name, out item);
        }

        private static IEnumerable<KeyValuePair<string, RegistryValueDataType>> Members(RegistryObjectValueDataType value)
        {
            return RegistryRuleValues.Members(value);
        }

        private static void Fail(string code, string path, string detail)
        {
            throw RegistryRuleException.Fail(code, path, detail);
        }

        private sealed class MediaDefinition
        {
            public MediaDefinition(string[] usage, string[] schemes, bool explicitPort, string[] formats, Dictionary<string, RegistryValueDataType> defaults)
            {
                Usage = usage;
                Schemes = schemes;
                ExplicitPort = explicitPort;
                Formats = formats;
                Defaults = defaults;
            }

            public string[] Usage { get; }

            public string[] Schemes { get; }

            public bool ExplicitPort { get; }

            public string[] Formats { get; }

            public Dictionary<string, RegistryValueDataType> Defaults { get; }
        }

        private sealed class JsonElementHolder
        {
            public JsonElementHolder(System.Text.Json.JsonElement element)
            {
                Element = element.Clone();
            }

            public System.Text.Json.JsonElement Element { get; }
        }

        private static JsonElementHolder LoadMessageAttributes()
        {
            using System.Text.Json.JsonDocument model = s_schemas.LoadJsonDocument("resolved-message-model.json");
            return new JsonElementHolder(model.RootElement.GetProperty("groups").GetProperty("messagegroups")
                .GetProperty("resources").GetProperty("messages").GetProperty("attributes"));
        }

        private static HashSet<string> AllKnownProtocolOptions()
        {
            return [.. s_protocolOptions.Value.Values.SelectMany(static item => item)];
        }

        private static readonly NativeJsonSchemaSet s_schemas = new();
        private static readonly Lazy<JsonElementHolder> s_messageAttributes = new(LoadMessageAttributes);
        private static readonly Lazy<Dictionary<string, HashSet<string>>> s_protocolOptions = new(LoadProtocolOptions);
        private static readonly Lazy<HashSet<string>> s_knownProtocolOptions = new(AllKnownProtocolOptions);
        private static readonly Dictionary<string, MediaDefinition> s_mediaProtocols = LoadMediaDefinitions();
        private static readonly Regex s_token = new("^[!#$%&'*+.^_`|~0-9A-Za-z-]+$", RegexOptions.CultureInvariant);
        private static readonly Regex s_template = new("\\{([A-Za-z0-9_.%]+(?:,[A-Za-z0-9_.%]+)*)\\}", RegexOptions.CultureInvariant);
        private static readonly Regex s_templateSymbol = new("^[A-Za-z0-9_]+$", RegexOptions.CultureInvariant);
        private static readonly Regex s_timestamp = new("^\\d{4}-\\d{2}-\\d{2}T\\d{2}:\\d{2}:\\d{2}(?:\\.\\d+)?(?:Z|[+-]\\d{2}:\\d{2})$", RegexOptions.CultureInvariant);
        private static readonly Regex s_ceName = new("^[a-z0-9]+$", RegexOptions.CultureInvariant);
        private static readonly Regex s_contentType = new("^[ \\t]*([!#$%&'*+.^_`|~0-9A-Za-z-]+)/([!#$%&'*+.^_`|~0-9A-Za-z-]+)((?:[ \\t]*;[ \\t]*[!#$%&'*+.^_`|~0-9A-Za-z-]+[ \\t]*=[ \\t]*(?:[!#$%&'*+.^_`|~0-9A-Za-z-]+|\\\"(?:[\\t !#-\\[\\]-~]|\\\\[\\t -~])*\\\"))*)[ \\t]*$", RegexOptions.CultureInvariant);
        private static readonly Regex s_contentTypeParameter = new("[ \\t]*;[ \\t]*([!#$%&'*+.^_`|~0-9A-Za-z-]+)[ \\t]*=[ \\t]*([!#$%&'*+.^_`|~0-9A-Za-z-]+|\\\"(?:[\\t !#-\\[\\]-~]|\\\\[\\t -~])*\\\")", RegexOptions.CultureInvariant);
        private static readonly HashSet<string> s_combined = ["MQTT/3.1.1", "MQTT/5.0", "AMQP/1.0", "NATS"];
        private static readonly HashSet<string> s_baseProtocols = ["MQTT/3.1.1", "MQTT/5.0", "AMQP/1.0", "HTTP", "KAFKA", "NATS"];
        private static readonly HashSet<string> s_secretKeys = ["password", "passphrase", "privatekey", "private_key", "access_token", "bearertoken", "credentials", "secret", "username", "icepassword", "iceufrag", "srtpkey"];
        private static readonly HashSet<string> s_runtimeMediaKeys = ["sdp", "offer", "answer", "icecandidates", "fingerprint", "ssrc", "sessionurl"];
        private static readonly HashSet<string> s_inputOptions = ["streamformat", "videocodec", "audiocodec"];
        private static readonly HashSet<string> s_videoCodecs = ["H264", "H265", "VP8", "VP9", "AV1"];
        private static readonly HashSet<string> s_audioCodecs = ["AAC", "Opus", "PCMA", "PCMU"];
        private static readonly HashSet<string> s_modelKeys = ["name", "type", "description", "required", "default", "enum", "minimum", "maximum", "item", "attributes", "ifvalues"];
        private static readonly HashSet<string> s_versionIgnored = ["xid", "self", "epoch", "createdat", "modifiedat"];
        private static readonly HashSet<string> s_messageTemplateKeys = ["correlation_data", "path", "topic"];
        private static readonly string[] s_selectorKinds = ["envelope", "protocol"];
    }
}
