/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * SPDX-License-Identifier: MIT
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.XRegistry;

namespace Opc.Ua.EndpointRegistry.PubSub
{
    public sealed partial class EndpointRegistryPubSubBinding
    {
        /// <summary>
        /// Admits explicit authenticated native discovery observations through a configured
        /// Publisher binding. The host must authenticate the identity before calling this API.
        /// Older and duplicate messages cannot change metadata, epochs or retained tombstones.
        /// Remote observations never create References to unhosted PubSub Objects.
        /// </summary>
        public async ValueTask<RemotePubSubObservationResult> ObserveRemoteAsync(
            RemotePubSubObservation observation, CancellationToken cancellationToken = default)
        {
            if (observation is null)
            {
                throw new ArgumentNullException(nameof(observation));
            }
            await m_remoteGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!m_remotePublishers.TryGetValue(observation.BindingId, out RemotePublisherState? state) ||
                    observation.AuthenticatedIdentity != state.Binding.AuthenticatedIdentity ||
                    !observation.Topic.StartsWith(state.Binding.TopicPrefix + "/", StringComparison.Ordinal))
                {
                    return Rejected("E_DISCOVERY_IDENTITY", "discovery", "The identity, Publisher binding and Topic prefix must be explicitly authorized.");
                }
                bool connectionTopic = observation.Topic == state.Binding.ConnectionTopic;
                if (!connectionTopic && !observation.Topic.StartsWith(
                    state.Binding.TopicPrefix + "/metadata/", StringComparison.Ordinal))
                {
                    return Rejected("E_DISCOVERY_TOPIC", "discovery/Topic", "Only configured connection and metadata Topics are admitted.");
                }
                if (string.IsNullOrEmpty(observation.MessageId) || observation.Timestamp == default)
                {
                    return Rejected("M_DISCOVERY_ORDER", "discovery", "Timestamp and MessageId are required for replay protection.");
                }
                if (state.ProcessedIds.Contains(observation.MessageId) ||
                    state.Topics.TryGetValue(observation.Topic, out RemoteTopicState? previous) &&
                    observation.Timestamp <= previous.Timestamp)
                {
                    return Rejected("E_DISCOVERY_REPLAY", "discovery", "An older, equal-time or already processed discovery message is ignored.");
                }
                if (state.ProcessedIds.Count >= 8192)
                {
                    return Rejected("E_DISCOVERY_CAPACITY", "discovery", "The binding replay budget is exhausted; reconfigure the authenticated binding.");
                }
                if (observation.ExpiresAt is not null && state.Binding.MqttVersion != "5.0")
                {
                    return Rejected("E_DISCOVERY_EXPIRY", "discovery/ExpiresAt", "Only an explicitly established MQTT 5 Publisher supplies message expiry.");
                }
                PubSubConnectionDataType? connection = observation.Connection is null ? null :
                    (PubSubConnectionDataType)observation.Connection.Clone();
                if (!observation.RetainedCleared && connectionTopic &&
                    (connection is null || !Utils.IsEqual(connection.PublisherId, state.Binding.PublisherId)))
                {
                    return Rejected("E_DISCOVERY_PUBLISHER", "connection/PublisherId", "The announcement PublisherId differs from its configured binding.");
                }
                if (!connectionTopic && observation.Connection is not null)
                {
                    return Rejected("E_DISCOVERY_TOPIC", "discovery/Topic", "A connection announcement must use its configured connection Topic.");
                }
                if (connectionTopic && observation.MetaData.Count != 0 ||
                    !connectionTopic && !observation.RetainedCleared && observation.MetaData.Count == 0)
                {
                    return Rejected("E_DISCOVERY_TOPIC", "discovery/Topic", "Connection and metadata announcements retain independent Topic lifetimes.");
                }
                var metadata = new List<PubSubDataSetMetaDataDiscoveryResult>();
                foreach (PubSubDataSetMetaDataDiscoveryResult entry in observation.MetaData)
                {
                    if (entry.PublisherId != PublisherId.From(state.Binding.PublisherId) ||
                        StatusCode.IsBad(entry.StatusCode) || entry.DataSetMetaData is null)
                    {
                        return Rejected("E_DISCOVERY_METADATA", "metadata", "Metadata must be authenticated for the configured Publisher identity.");
                    }
                    metadata.Add(entry with { DataSetMetaData = (DataSetMetaDataType)entry.DataSetMetaData.Clone() });
                }
                var topic = new RemoteTopicState(observation.Timestamp, observation.ExpiresAt,
                    observation.RetainedCleared, connection, metadata.ToArray());
                state.ProcessedIds.Add(observation.MessageId);
                state.Topics[observation.Topic] = topic;
                ArrayOf<RegistryDiagnosticDataType> issues = await SurfaceRemoteAsync(state, cancellationToken)
                    .ConfigureAwait(false);
                return new RemotePubSubObservationResult(true, issues);
            }
            finally
            {
                m_remoteGate.Release();
            }
        }

        /// <summary>
        /// Removes expired MQTT 5 observations at or after their expiry instant. Tombstones and
        /// replay watermarks remain, so an expired retained message cannot resurrect an entry.
        /// Also used by the service-owned periodic expiry check.
        /// </summary>
        public async ValueTask ExpireRemoteAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
        {
            await m_remoteGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                foreach (RemotePublisherState state in m_remotePublishers.Values)
                {
                    bool changed = false;
                    foreach (RemoteTopicState topic in state.Topics.Values)
                    {
                        if (!topic.Cleared && topic.ExpiresAt is { } expiry && expiry <= now)
                        {
                            topic.Cleared = true;
                            changed = true;
                        }
                    }
                    if (changed)
                    {
                        await SurfaceRemoteAsync(state, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                m_remoteGate.Release();
            }
        }

        private void InitializeRemoteBindings()
        {
            foreach (RemotePubSubPublisherBinding binding in m_options.RemotePublishers)
            {
                var facts = new PubSubConnectionDataType
                {
                    Address = new ExtensionObject(new NetworkAddressUrlDataType { Url = binding.BrokerUrl })
                };
                if (string.IsNullOrEmpty(binding.Id) || string.IsNullOrEmpty(binding.AuthenticatedIdentity) ||
                    !PubSubBindingRules.ValidTopic(binding.TopicPrefix) ||
                    !binding.ConnectionTopic.StartsWith(binding.TopicPrefix + "/connection/", StringComparison.Ordinal) ||
                    PubSubBindingRules.BrokerUrl(facts) is null ||
                    binding.MqttVersion is not (null or "3.1.1" or "5.0") || binding.PublisherId.IsNull ||
                    m_remotePublishers.ContainsKey(binding.Id))
                {
                    throw new ArgumentException("Remote Publisher bindings require unique IDs, authorized Topics, identity and broker facts.");
                }
                // Configuration values remain server-owned even if the caller later edits its options.
                var copy = new RemotePubSubPublisherBinding
                {
                    Id = binding.Id,
                    PublisherId = binding.PublisherId,
                    TopicPrefix = binding.TopicPrefix,
                    ConnectionTopic = binding.ConnectionTopic,
                    AuthenticatedIdentity = binding.AuthenticatedIdentity,
                    BrokerUrl = binding.BrokerUrl,
                    MqttVersion = binding.MqttVersion
                };
                m_remotePublishers.Add(copy.Id, new RemotePublisherState(copy));
            }
        }

        private async ValueTask<ArrayOf<RegistryDiagnosticDataType>> SurfaceRemoteAsync(
            RemotePublisherState state, CancellationToken cancellationToken)
        {
            var desired = new HashSet<string>(StringComparer.Ordinal);
            var issues = new List<RegistryDiagnosticDataType>();
            string prefix = PubSubSurfaceBuilder.Identifier(m_options.RemoteIdentifierPrefix, state.Binding.Id) + "-";
            PubSubConnectionDataType? connection = null;
            DateTimeOffset now = m_options.TimeProvider.GetUtcNow();
            if (state.Topics.TryGetValue(state.Binding.ConnectionTopic, out RemoteTopicState? announced) &&
                !announced.Cleared && (announced.ExpiresAt is null || announced.ExpiresAt > now))
            {
                connection = announced.Connection is null ? null : (PubSubConnectionDataType)announced.Connection.Clone();
            }
            if (connection is not null && state.Binding.MqttVersion is { } version)
            {
                // Part 14 connection discovery deliberately omits Address and configuration
                // properties. Only independent, authorized Publisher facts fill these fields.
                connection.Address = new ExtensionObject(new NetworkAddressUrlDataType
                {
                    NetworkInterface = string.Empty,
                    Url = state.Binding.BrokerUrl
                });
                connection.ConnectionProperties =
                [
                    new KeyValuePair { Key = new QualifiedName("MqttVersion"), Value = Variant.From(version) }
                ];
                foreach (WriterGroupDataType group in connection.WriterGroups.ToArray() ?? [])
                {
                    var selections = new List<DataSetWriterDataType?> { null };
                    foreach (DataSetWriterDataType writer in group.DataSetWriters)
                    {
                        if (PubSubBindingRules.OwnQueue(writer))
                        {
                            selections.Add(writer);
                        }
                    }
                    foreach (DataSetWriterDataType? writer in selections)
                    {
                        string id = PubSubSurfaceBuilder.Identifier(prefix,
                            group.WriterGroupId.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/" +
                            (writer?.DataSetWriterId.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "group"));
                        string xid = "/endpoints/" + id;
                        var messages = new List<RegistryMemberDataType>();
                        var datasets = new List<DataSetMetaDataType>();
                        bool complete = true;
                        foreach (DataSetWriterDataType component in SelectedWriters(group, writer))
                        {
                            DataSetMetaDataType? metadata = RemoteMetadata(state, group.WriterGroupId, component.DataSetWriterId, now);
                            if (metadata?.ConfigurationVersion is null)
                            {
                                complete = false;
                                issues.Add(PubSubBindingRules.Issue("M_METADATA", "metadata",
                                    "Every surfaced writer requires an authenticated retained DataSet metadata observation."));
                                break;
                            }
                            var schemaIssues = new List<RegistryDiagnosticDataType>();
                            SchemaRegistry.SchemaReferenceDataType? schema = Association(
                                PubSubSurfaceBuilder.Key(connection, group, component, metadata), metadata, schemaIssues);
                            if (schemaIssues.Count != 0)
                            {
                                complete = false;
                                issues.AddRange(schemaIssues);
                                break;
                            }
                            datasets.Add(metadata);
                            string messageId = "writer-" + component.DataSetWriterId.ToString(System.Globalization.CultureInfo.InvariantCulture);
                            RegistryObjectValueDataType message = PubSubSurfaceBuilder.Message(messageId, connection, group, component, schema);
                            if (PubSubBindingRules.CheckMessage(message, connection, group, component).Count != 0)
                            {
                                complete = false;
                                break;
                            }
                            messages.Add(PubSubSurfaceBuilder.Member(messageId, message));
                        }
                        if (!complete)
                        {
                            continue;
                        }
                        try
                        {
                            RegistryObjectValueDataType endpoint = PubSubSurfaceBuilder.Endpoint(id, connection, group, writer,
                                messages.ToArray(), PubSubSurfaceBuilder.Fingerprint(m_context, connection, [], datasets.ToArray()));
                            EnsureCommitted(await m_remoteProvider.ReplaceAsync(xid,
                                m_host.Mapper.Project(endpoint, nameof(EndpointDataType)), cancellationToken: cancellationToken)
                                .ConfigureAwait(false));
                            desired.Add(xid);
                        }
                        catch (ServiceResultException error) when (error.StatusCode == StatusCodes.BadInvalidArgument)
                        {
                            issues.Add(PubSubBindingRules.Issue("E_DISCOVERY_CONFIGURATION", "connection", error.Message));
                        }
                    }
                }
            }
            else if (connection is not null)
            {
                issues.Add(PubSubBindingRules.Issue("M_MQTT_VERSION", "binding/MqttVersion", "The Publisher MQTT version is not independently established."));
            }
            foreach (string path in ProviderPaths(prefix))
            {
                if (!desired.Contains(path))
                {
                    EnsureCommitted(await m_remoteProvider.DeleteAsync(path, cancellationToken: cancellationToken)
                        .ConfigureAwait(false));
                }
            }
            return issues.ToArray();
        }

        private static DataSetMetaDataType? RemoteMetadata(
            RemotePublisherState state, ushort groupId, ushort writerId, DateTimeOffset now)
        {
            DataSetMetaDataType? selected = null;
            DateTimeOffset newest = default;
            foreach (RemoteTopicState topic in state.Topics.Values)
            {
                if (topic.Cleared || topic.ExpiresAt <= now)
                {
                    continue;
                }
                foreach (PubSubDataSetMetaDataDiscoveryResult metadata in topic.MetaData)
                {
                    if (metadata.WriterGroupId == groupId && metadata.DataSetWriterId == writerId && topic.Timestamp > newest)
                    {
                        newest = topic.Timestamp;
                        selected = metadata.DataSetMetaData;
                    }
                }
            }
            return selected;
        }

        private static RemotePubSubObservationResult Rejected(string code, string path, string detail)
        {
            return new RemotePubSubObservationResult(false, [PubSubBindingRules.Issue(code, path, detail)]);
        }

        private sealed class RemotePublisherState(RemotePubSubPublisherBinding binding)
        {
            public RemotePubSubPublisherBinding Binding { get; } = binding;

            public Dictionary<string, RemoteTopicState> Topics { get; } = new(StringComparer.Ordinal);

            public HashSet<string> ProcessedIds { get; } = new(StringComparer.Ordinal);
        }

        private sealed class RemoteTopicState(
            DateTimeOffset timestamp, DateTimeOffset? expiresAt, bool cleared,
            PubSubConnectionDataType? connection, ArrayOf<PubSubDataSetMetaDataDiscoveryResult> metadata)
        {
            public DateTimeOffset Timestamp { get; } = timestamp;

            public DateTimeOffset? ExpiresAt { get; } = expiresAt;

            public bool Cleared { get; set; } = cleared;

            public PubSubConnectionDataType? Connection { get; } = connection;

            public ArrayOf<PubSubDataSetMetaDataDiscoveryResult> MetaData { get; } = metadata;

        }

        private readonly Dictionary<string, RemotePublisherState> m_remotePublishers = new(StringComparer.Ordinal);
    }
}
