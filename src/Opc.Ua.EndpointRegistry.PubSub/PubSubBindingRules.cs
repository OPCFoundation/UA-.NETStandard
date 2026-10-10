/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * SPDX-License-Identifier: MIT
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using Opc.Ua.XRegistry;

namespace Opc.Ua.EndpointRegistry.PubSub
{
    /// <summary>
    /// Pure OPC 30455 / Part 14 MQTT correspondence rules. Reading these values has no
    /// transport, credential or configuration side effects.
    /// </summary>
    public static class PubSubBindingRules
    {
        /// <summary>Part 14 MQTT JSON transport profile.</summary>
        public const string JsonProfile = "http://opcfoundation.org/UA-Profile/Transport/pubsub-mqtt-json";

        /// <summary>Part 14 MQTT UADP transport profile.</summary>
        public const string UadpProfile = "http://opcfoundation.org/UA-Profile/Transport/pubsub-mqtt-uadp";

        /// <summary>
        /// Checks PublishesTo or SubscribesTo correspondence. A null writer selects its
        /// WriterGroup; a nonnull reader selects SubscribesTo.
        /// </summary>
        public static ArrayOf<RegistryDiagnosticDataType> CheckEndpoint(
            RegistryObjectValueDataType endpoint,
            PubSubConnectionDataType connection,
            WriterGroupDataType? group = null,
            DataSetWriterDataType? writer = null,
            DataSetReaderDataType? reader = null)
        {
            var issues = new List<RegistryDiagnosticDataType>();
            try
            {
                EndpointRegistryRules.ValidateEndpoint(endpoint);
            }
            catch (RegistryRuleException error)
            {
                return [error.ToDiagnostic()];
            }
            RegistryObjectValueDataType options = Object(endpoint, "protocoloptions");
            string protocol = Selector(Text(endpoint, "protocol"));
            if (protocol is not ("MQTT/5.0" or "MQTT/3.1.1"))
            {
                return [Issue("U_PROTOCOL", "protocol", "Only MQTT Endpoints have a Part 14 binding.")];
            }
            if (Get(options, "deployed") is RegistryBooleanValueDataType { Value: false })
            {
                issues.Add(Issue("E_ABSTRACT_ENDPOINT", "protocoloptions/deployed", "A binding requires a deployed Endpoint."));
            }
            if (ContentType(connection) is null)
            {
                issues.Add(Issue("U_TRANSPORT_PROFILE", "connection/TransportProfileUri", "Only MQTT JSON and UADP profiles bind."));
            }
            if (MqttVersion(connection) is not { } version || protocol != "MQTT/" + version)
            {
                issues.Add(Issue("E_MQTT_VERSION", "connection/ConnectionProperties/MqttVersion", "An explicit matching Publisher MQTT version is required."));
            }
            string? address = BrokerUrl(connection);
            bool advertised = false;
            if (Get(options, "endpoints") is RegistryArrayValueDataType addresses)
            {
                foreach (RegistryValueDataType value in addresses.Items)
                {
                    advertised |= value is RegistryObjectValueDataType entry && Text(entry, "uri") == address;
                }
            }
            if (address is null || !advertised)
            {
                issues.Add(Issue("E_BROKER_ADDRESS", "connection/Address/Url", "A path-free broker URL must be advertised by the Endpoint."));
            }
            (string queue, BrokerTransportQualityOfService guarantee) = Queue(group, writer, reader);
            string path = reader is not null ? "DataSetReader/TransportSettings" :
                writer is not null ? "DataSetWriter/TransportSettings" : "WriterGroup/TransportSettings";
            if (reader is not null)
            {
                if (!reader.TransportSettings.TryGetValue(out BrokerDataSetReaderTransportDataType? _))
                {
                    issues.Add(Issue("U_TRANSPORT_TYPE", path, "A broker reader transport is required."));
                    return issues.ToArray();
                }
            }
            else if (group is null ||
                !group.TransportSettings.TryGetValue(out BrokerWriterGroupTransportDataType? _) ||
                writer is not null && !writer.TransportSettings.TryGetValue(out BrokerDataSetWriterTransportDataType? _))
            {
                issues.Add(Issue("U_TRANSPORT_TYPE", path, "A broker publisher transport is required."));
                return issues.ToArray();
            }
            bool producer = Roles(endpoint, "producer") && !Roles(endpoint, "consumer") && !Roles(endpoint, "subscriber");
            bool consumer = Roles(endpoint, "consumer") && Text(options, "topicfilter") is not null;
            bool session = reader is not null || producer;
            if (reader is not null)
            {
                if (!consumer)
                {
                    issues.Add(Issue("E_BINDING_ROLE", "usage", "A reader requires a consumer Endpoint with a topic filter."));
                }
                else
                {
                    string expected = Text(options, "topicfilter")!;
                    if (Text(options, "sharedsubscriptiongroup") is { } shared)
                    {
                        expected = "$share/" + shared + "/" + expected;
                    }
                    if (queue != expected)
                    {
                        issues.Add(Issue("E_TOPIC", path + "/QueueName", "The reader queue must equal the Endpoint filter."));
                    }
                }
            }
            else
            {
                if (queue.Length == 0)
                {
                    issues.Add(Issue("E_QUEUE", path + "/QueueName", "A bound publisher names its queue."));
                    return issues.ToArray();
                }
                if (!ValidTopic(queue))
                {
                    issues.Add(Issue("E_MQTT_TOPIC", path + "/QueueName", "A publish queue has no wildcards or null characters."));
                    return issues.ToArray();
                }
                string? scope = writer is null ? GroupQueueUsed(group!) ? null : "E_UNUSED_QUEUE" :
                    OwnQueue(writer) && WriterQueueValid(group!) ? null : "E_WRITER_QUEUE";
                if (scope is not null)
                {
                    issues.Add(Issue(scope, path + "/QueueName", "The selected component does not own a valid publishing queue."));
                }
                if (producer)
                {
                    if (Text(options, "topic") != queue)
                    {
                        issues.Add(Issue("E_TOPIC", path + "/QueueName", "The producer topic differs from the publisher queue."));
                    }
                }
                else if (consumer)
                {
                    if (!TopicMatches(Text(options, "topicfilter")!, queue))
                    {
                        issues.Add(Issue("E_TOPIC", path + "/QueueName", "The consumer filter does not match the publisher queue."));
                    }
                }
                else
                {
                    issues.Add(Issue("E_BINDING_ROLE", "usage", "A publisher requires a producer or message-delivering consumer Endpoint."));
                }
            }
            if (Qos(guarantee) is not { } qos || qos != (Integer(options, "qos") ?? 0))
            {
                issues.Add(Issue("E_DELIVERY_GUARANTEE", path + "/RequestedDeliveryGuarantee",
                    "The effective delivery guarantee differs from Endpoint QoS."));
            }
            if (session && protocol == "MQTT/5.0" && Integer(options, "sessionexpiryinterval") is { } expiry &&
                (!Property(connection, "connection-Session Expiry Interval", out Variant interval) ||
                !interval.TryGetValue(out uint configured) || configured != expiry))
            {
                issues.Add(Issue("E_SESSION_EXPIRY", "connection/ConnectionProperties/connection-Session Expiry Interval",
                    "The session expiry differs from the Endpoint."));
            }
            return issues.ToArray();
        }

        /// <summary>Checks transport and message-mapping facts for HasMessageDefinition.</summary>
        public static ArrayOf<RegistryDiagnosticDataType> CheckMessage(
            RegistryObjectValueDataType message,
            PubSubConnectionDataType connection,
            WriterGroupDataType? group = null,
            DataSetWriterDataType? writer = null,
            DataSetReaderDataType? reader = null,
            RegistryObjectValueDataType? container = null)
        {
            try
            {
                EndpointRegistryRules.ValidateMessage(message);
            }
            catch (RegistryRuleException error)
            {
                return [error.ToDiagnostic()];
            }
            var issues = new List<RegistryDiagnosticDataType>();
            string? content = ContentType(connection);
            if (content is null)
            {
                issues.Add(Issue("U_TRANSPORT_PROFILE", "connection/TransportProfileUri", "Only MQTT JSON and UADP messages bind."));
            }
            else
            {
                ExtensionObject settings = reader?.MessageSettings ?? writer?.MessageSettings ?? ExtensionObject.Null;
                bool mapping = connection.TransportProfileUri == JsonProfile
                    ? reader is null ? settings.TryGetValue(out JsonDataSetWriterMessageDataType? _) :
                        settings.TryGetValue(out JsonDataSetReaderMessageDataType? _)
                    : reader is null ? settings.TryGetValue(out UadpDataSetWriterMessageDataType? _) :
                        settings.TryGetValue(out UadpDataSetReaderMessageDataType? _);
                if (!mapping)
                {
                    issues.Add(Issue("E_MESSAGE_MAPPING", reader is null ? "writer/MessageSettings" : "reader/MessageSettings",
                        "MessageSettings must select the connection mapping."));
                }
                if (Text(message, "datacontenttype") != content)
                {
                    issues.Add(Issue("E_CONTENT_TYPE", "datacontenttype", "The content type differs from the native mapping."));
                }
            }
            if (Get(message, "envelope") is not null || Get(message, "envelopemetadata") is not null)
            {
                issues.Add(Issue("E_ENVELOPE", "envelope", "NetworkMessage headers take the place of an envelope."));
            }
            if (container is not null && Get(container, "envelope") is not null)
            {
                issues.Add(Issue("E_ENVELOPE", "container/envelope", "A bound Message container cannot declare an envelope."));
            }
            if (Text(message, "protocol") is { } protocol)
            {
                if (Selector(protocol) != "MQTT/" + MqttVersion(connection))
                {
                    issues.Add(Issue("E_MQTT_VERSION", "protocol", "The Message MQTT version differs from the connection."));
                }
                RegistryObjectValueDataType options = Object(message, "protocoloptions");
                (string queue, BrokerTransportQualityOfService guarantee) = Queue(group, writer, reader);
                if (Text(options, "topic_name") is { } topic &&
                    (reader is null ? topic != queue : !TopicMatches(Unshare(queue), topic)))
                {
                    issues.Add(Issue("E_TOPIC", "protocoloptions/topic_name", "The Message topic differs from the native queue."));
                }
                if (Integer(options, "qos") is { } qos && qos != Qos(guarantee))
                {
                    issues.Add(Issue("E_DELIVERY_GUARANTEE", "protocoloptions/qos", "The Message QoS differs from the effective delivery guarantee."));
                }
            }
            return issues.ToArray();
        }

        /// <summary>Gets the explicitly named MQTT version, rejecting absent, ambiguous and BestAvailable values.</summary>
        public static string? MqttVersion(PubSubConnectionDataType connection)
        {
            return Property(connection, "MqttVersion", out Variant value) &&
                value.TryGetValue(out string? version) && version is "5.0" or "3.1.1" ? version : null;
        }

        /// <summary>Gets a path-free MQTT broker URL, or null for unsupported addresses.</summary>
        public static string? BrokerUrl(PubSubConnectionDataType connection)
        {
            if (!connection.Address.TryGetValue(out NetworkAddressUrlDataType? address) ||
                !Uri.TryCreate(address!.Url, UriKind.Absolute, out Uri? url) ||
                url.Scheme is not ("mqtt" or "mqtts" or "wss") || url.Host.Length == 0 ||
                url.Query.Length != 0 || url.Fragment.Length != 0)
            {
                return null;
            }
            string raw = address.Url!;
            int authority = raw.IndexOf("://", StringComparison.Ordinal) + 3;
            return raw.IndexOf('/', authority) >= 0 ? null : raw;
        }

        /// <summary>Gets the content type selected by the native transport profile.</summary>
        public static string? ContentType(PubSubConnectionDataType connection)
        {
            return connection.TransportProfileUri switch
            {
                JsonProfile => "application/json",
                UadpProfile => "application/opcua+uadp",
                _ => null
            };
        }

        /// <summary>Gets the effective queue and delivery guarantee with writer fallback.</summary>
        public static (string Queue, BrokerTransportQualityOfService Guarantee) Queue(
            WriterGroupDataType? group, DataSetWriterDataType? writer = null, DataSetReaderDataType? reader = null)
        {
            if (reader is not null)
            {
                return reader.TransportSettings.TryGetValue(out BrokerDataSetReaderTransportDataType? transport)
                    ? (transport!.QueueName ?? string.Empty, transport.RequestedDeliveryGuarantee) : (string.Empty, 0);
            }
            string queue = string.Empty;
            BrokerTransportQualityOfService guarantee = 0;
            if (group is not null && group.TransportSettings.TryGetValue(out BrokerWriterGroupTransportDataType? settings))
            {
                queue = settings!.QueueName ?? string.Empty;
                guarantee = settings.RequestedDeliveryGuarantee;
            }
            if (writer is not null && writer.TransportSettings.TryGetValue(out BrokerDataSetWriterTransportDataType? own))
            {
                if (!string.IsNullOrEmpty(own!.QueueName))
                {
                    queue = own.QueueName;
                }
                if ((int)own.RequestedDeliveryGuarantee != 0)
                {
                    guarantee = own.RequestedDeliveryGuarantee;
                }
            }
            return (queue, guarantee);
        }

        /// <summary>Maps Part 14 effective delivery guarantees to MQTT QoS.</summary>
        public static int? Qos(BrokerTransportQualityOfService guarantee)
        {
            return (int)guarantee switch { 1 or 3 => 0, 2 => 1, 4 => 2, _ => null };
        }

        /// <summary>Tests whether a writer names its own broker queue.</summary>
        public static bool OwnQueue(DataSetWriterDataType writer)
        {
            return writer.TransportSettings.TryGetValue(out BrokerDataSetWriterTransportDataType? transport) &&
                !string.IsNullOrEmpty(transport!.QueueName);
        }

        /// <summary>Tests whether a WriterGroup queue carries at least one writer.</summary>
        public static bool GroupQueueUsed(WriterGroupDataType group)
        {
            foreach (DataSetWriterDataType writer in group.DataSetWriters)
            {
                if (!OwnQueue(writer))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Tests the Part 14 single-writer NetworkMessage restriction for writer-owned queues.</summary>
        public static bool WriterQueueValid(WriterGroupDataType group)
        {
            return group.DataSetWriters.Count == 1 ||
                group.MessageSettings.TryGetValue(out JsonWriterGroupMessageDataType? json) &&
                ((int)json!.NetworkMessageContentMask & 4) != 0 ||
                group.MessageSettings.TryGetValue(out UadpWriterGroupMessageDataType? uadp) && (int)uadp!.DataSetOrdering == 2;
        }

        /// <summary>Matches MQTT filters including the reserved dollar-topic wildcard rule.</summary>
        public static bool TopicMatches(string filter, string topic)
        {
            string[] filters = filter.Split('/');
            string[] levels = topic.Split('/');
            if (topic.Length > 0 && topic[0] == '$' && filters[0] is "+" or "#")
            {
                return false;
            }
            for (int index = 0; index < filters.Length; index++)
            {
                if (filters[index] == "#")
                {
                    return index == filters.Length - 1;
                }
                if (index >= levels.Length || filters[index] != "+" && filters[index] != levels[index])
                {
                    return false;
                }
            }
            return filters.Length == levels.Length;
        }

        internal static RegistryDiagnosticDataType Issue(string code, string path, string detail)
        {
            return new RegistryDiagnosticDataType
            {
                StatusCode = StatusCodes.BadInvalidArgument,
                Code = code,
                Path = path.Split('/'),
                Detail = detail
            };
        }

        internal static RegistryValueDataType? Get(RegistryObjectValueDataType value, string name)
        {
            foreach (RegistryMemberDataType member in value.Members)
            {
                if (member.Name == name)
                {
                    return member.Value;
                }
            }
            return null;
        }

        internal static string? Text(RegistryObjectValueDataType value, string name)
        {
            return (Get(value, name) as RegistryStringValueDataType)?.Value;
        }

        internal static RegistryObjectValueDataType Object(RegistryObjectValueDataType value, string name)
        {
            return Get(value, name) as RegistryObjectValueDataType ?? new RegistryObjectValueDataType { Kind = 5, Members = [] };
        }

        private static long? Integer(RegistryObjectValueDataType value, string name)
        {
            if (Get(value, name) is not RegistryNumberValueDataType number)
            {
                return null;
            }
            return long.TryParse(System.Text.Encoding.UTF8.GetString(RegistryValues.ToJson(number).ToArray()),
                NumberStyles.Float, CultureInfo.InvariantCulture, out long result) ? result : null;
        }

        internal static RegistryNumberValueDataType Number(uint value)
        {
            byte[] bytes = new BigInteger(value).ToByteArray();
            Array.Reverse(bytes);
            return new RegistryNumberValueDataType { Kind = 3, Coefficient = ByteString.From(bytes), IsInteger = true };
        }

        private static bool Property(PubSubConnectionDataType connection, string name, out Variant value)
        {
            int found = 0;
            value = Variant.Null;
            foreach (KeyValuePair property in connection.ConnectionProperties)
            {
                if (property.Key.NamespaceIndex == 0 && property.Key.Name == name)
                {
                    value = property.Value;
                    found++;
                }
            }
            return found == 1;
        }

        private static bool Roles(RegistryObjectValueDataType endpoint, string role)
        {
            if (Get(endpoint, "usage") is RegistryArrayValueDataType usage)
            {
                foreach (RegistryValueDataType value in usage.Items)
                {
                    if (value is RegistryStringValueDataType { Value: var text } && text == role)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        internal static bool ValidTopic(string queue)
        {
            return queue.Length > 0 && queue.IndexOfAny(['+', '#', '\0']) < 0;
        }

        private static string Selector(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant();

        private static string Unshare(string queue)
        {
            if (!queue.StartsWith("$share/", StringComparison.Ordinal))
            {
                return queue;
            }
            int separator = queue.IndexOf('/', 7);
            return separator < 0 ? string.Empty : queue.Substring(separator + 1);
        }
    }
}
