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
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.SchemaRegistry;
using Opc.Ua.XRegistry;
using Opc.Ua.XRegistry.Server;

namespace Opc.Ua.EndpointRegistry.Server
{
    /// <summary>
    /// Projects the committed generations of one Endpoint Registry catalog into typed Groups and
    /// metadata-only Messages through the shared xRegistry projection engine.
    /// </summary>
    internal sealed class EndpointRegistryProjection : IXRegistryCollectionProjectionStrategy
    {
        public EndpointRegistryProjection(
            RegistryNativeHost host,
            ISystemContext context,
            ushort endpointNamespaceIndex,
            bool media,
            Func<ISystemContext, RegistryAccessKind, ServiceResult> authorize,
            RegistryMetadataFileBinding metadataFiles)
        {
            m_host = host;
            m_context = context;
            m_namespaceIndex = endpointNamespaceIndex;
            m_media = media;
            m_authorize = authorize;
            m_metadataFiles = metadataFiles;
            m_generation = new XRegistryProjectionGeneration(Snapshot.Empty, null);
        }

        /// <summary>
        /// Rebuilds the projection descriptors from a committed generation.
        /// </summary>
        public void Update(RegistryCommittedState state)
        {
            RegistryObjectValueDataType document = state.CloneDocument();
            var collections = new List<IXRegistryProjectionCollection>();
            var eventGroups = new List<XRegistryProjectionEventGroup>();
            foreach (string collection in m_host.Collections)
            {
                var groups = new List<IXRegistryProjectionCollectionGroup>();
                if (Member(document, collection) is RegistryObjectValueDataType map)
                {
                    foreach (RegistryMemberDataType entry in map.Members)
                    {
                        Group group = CreateGroup(state, collection, entry.Name!);
                        groups.Add(group);
                        var eventResources = new List<XRegistryProjectionEventResource>();
                        foreach (IXRegistryProjectionMetadataResource resource in group.Resources)
                        {
                            var message = (Message)resource;
                            eventResources.Add(new XRegistryProjectionEventResource(group.GroupId, message.ResourceId,
                                message.Xid, message.Epoch, message.Epoch,
                                message.Labels ?? ImmutableSortedDictionary<string, string>.Empty, false,
                                message.VersionId, [])
                            {
                                SourceNodeId = InstanceNode(message.Xid),
                                SourceName = message.Name ?? message.ResourceId,
                                Name = message.Name,
                                Description = message.Description
                            });
                        }
                        eventGroups.Add(new XRegistryProjectionEventGroup(group.GroupId, group.Xid, group.Epoch,
                            group.Labels ?? ImmutableSortedDictionary<string, string>.Empty, false,
                            [.. eventResources])
                        {
                            SourceNodeId = InstanceNode(group.Xid),
                            SourceName = group.Name ?? group.GroupId
                        });
                    }
                }
                collections.Add(new Collection(collection, new QualifiedName(
                    collection == "endpoints" ? BrowseNames.Endpoints : BrowseNames.MessageGroups,
                    m_namespaceIndex), groups.ToArray()));
            }
            Volatile.Write(ref m_generation,
                new XRegistryProjectionGeneration(new Snapshot(collections.ToArray(), RootLabels(document)),
                    new XRegistryProjectionEventSnapshot("/", state.Epoch, RootLabels(document), [.. eventGroups])));
        }

        private NodeId InstanceNode(string xid) =>
            new((m_media ? BrowseNames.MediaEndpointRegistry : BrowseNames.EndpointRegistry) + xid, m_namespaceIndex);

        /// <inheritdoc/>
        public XRegistryProjectionGeneration CaptureProjectionGeneration()
        {
            return Volatile.Read(ref m_generation);
        }

        /// <inheritdoc/>
        public GroupCollectionState CreateCollectionNode(
            BaseObjectState registryNode,
            IXRegistryProjectionCollection collection)
        {
            return collection.Name == "endpoints"
                ? m_media ? new MediaEndpointCollectionState(registryNode) : new EndpointCollectionState(registryNode)
                : new MessageGroupCollectionState(registryNode);
        }

        /// <inheritdoc/>
        public BaseObjectState CreateEntityNode(NodeState parent, IXRegistryProjectionEntity entity)
        {
            if (entity.Role == XRegistryProjectionEntityRole.Resource)
            {
                return new MessageDefinitionState(parent);
            }
            if (entity.CollectionName != "endpoints")
            {
                return new MessageDefinitionGroupState(parent);
            }
            return m_media ? new MediaEndpointGroupState(parent) : new EndpointGroupState(parent);
        }

        /// <inheritdoc/>
        public void ConfigureEntityNode(BaseObjectState node, IXRegistryProjectionEntity entity, bool created)
        {
            switch (entity)
            {
                case Group group when node is MessageContainerState container:
                    ConfigureGroup(container, group, created);
                    if (created)
                    {
                        container.AddLabels(m_context);
                        RegistryLabelBinding.Bind(container.Labels!, m_context, m_host, group.Xid, m_authorize);
                    }
                    break;
                case Message message when node is MessageDefinitionState definition:
                    if (created)
                    {
                        definition.AddLabels(m_context);
                        RegistryLabelBinding.Bind(definition.Labels!, m_context, m_host, message.Xid, m_authorize);
                        if (message.Record.DataSchema is not null)
                        {
                            definition.AddDataSchema(m_context);
                        }
                        BindMetadata(definition.Metadata, message.Xid);
                    }
                    XRegistryProjectionEngine.SetValue(definition.Snapshot, message.Snapshot);
                    XRegistryProjectionEngine.SetValue(definition.Metadata?.Size, message.MetadataSize);
                    if (message.Record.DataSchema is SchemaContentDataType schema)
                    {
                        XRegistryProjectionEngine.SetValue(definition.DataSchema, schema);
                    }
                    break;
                default:
                    throw new InvalidOperationException("The projected entity and node do not correspond.");
            }
        }

        /// <inheritdoc/>
        public async ValueTask<ServiceResult> DeleteEntityAsync(
            ISystemContext context,
            IXRegistryProjectionEntity entity,
            uint expectedEpoch,
            CancellationToken ct)
        {
            ServiceResult allowed = m_authorize(context, RegistryAccessKind.Write);
            if (ServiceResult.IsBad(allowed))
            {
                return allowed;
            }
            RegistryMutationResultDataType result = await m_host.DeleteAsync(entity.Xid, expectedEpoch, ct)
                .ConfigureAwait(false);
            return result.Issues.Count == 0
                ? new ServiceResult(result.StatusCode)
                : new ServiceResult(result.StatusCode, new LocalizedText(result.Issues[0].Detail));
        }

        private void ConfigureGroup(MessageContainerState node, Group group, bool created)
        {
            if (created)
            {
                node.AddMetadata(m_context);
                BindMetadata(node.Metadata, group.Xid);
                if (group.Envelope is not null)
                {
                    node.AddEnvelope(m_context);
                }
                if (group.Protocol is not null)
                {
                    node.AddProtocol(m_context);
                }
                if (node is EndpointGroupState endpoint && group.Endpoint is EndpointDataType record)
                {
                    if (record.Channel is not null)
                    {
                        endpoint.AddChannel(m_context);
                    }
                    if (group.OptionsType is not null)
                    {
                        EndpointOptionsState options = CreateOptions(endpoint, group.OptionsType);
                        options.Create(m_context, NodeId.Null, new QualifiedName(BrowseNames.Options, m_namespaceIndex),
                            new LocalizedText(BrowseNames.Options), assignNodeIds: false);
                        options.ReferenceTypeId = Ua.ReferenceTypeIds.HasComponent;
                        endpoint.CreateOrReplaceOptions(m_context, options, assignInstanceNodeIds: false);
                    }
                }
            }
            XRegistryProjectionEngine.SetValue(node.Snapshot, group.Snapshot);
            XRegistryProjectionEngine.SetValue(node.Metadata?.Size, group.MetadataSize);
            if (group.Envelope is not null)
            {
                XRegistryProjectionEngine.SetValue(node.Envelope, group.Envelope);
            }
            if (group.Protocol is not null)
            {
                XRegistryProjectionEngine.SetValue(node.Protocol, group.Protocol);
            }
            if (node is EndpointGroupState endpointGroup && group.Endpoint is EndpointDataType endpointRecord)
            {
                XRegistryProjectionEngine.SetValue(endpointGroup.Usage, endpointRecord.Usage);
                if (endpointRecord.Channel is not null)
                {
                    XRegistryProjectionEngine.SetValue(endpointGroup.Channel, endpointRecord.Channel);
                }
                if (endpointGroup.Options is not null && endpointRecord.ProtocolOptions is not null)
                {
                    XRegistryProjectionEngine.SetValue(endpointGroup.Options.Value, endpointRecord.ProtocolOptions);
                }
            }
        }

        private void BindMetadata(FileState? file, string xid)
        {
            if (file is null)
            {
                return;
            }
            // The JSON compatibility view is read from the committed generation at Open time.
            m_metadataFiles.Bind(file, () => RegistryValues.ToJson(m_host.ReadValue(m_host.Current, xid).Value),
                m_authorize);
            XRegistryProjectionEngine.SetValue(file.MimeType, "application/json");
        }

        private EndpointOptionsState CreateOptions(EndpointGroupState endpoint, string optionsType)
        {
            return optionsType switch
            {
                nameof(MediaEndpointOptionsState) => new MediaEndpointOptionsState(endpoint),
                nameof(MqttEndpointOptionsState) => new MqttEndpointOptionsState(endpoint),
                nameof(HttpEndpointOptionsState) => new HttpEndpointOptionsState(endpoint),
                nameof(AmqpEndpointOptionsState) => new AmqpEndpointOptionsState(endpoint),
                nameof(KafkaEndpointOptionsState) => new KafkaEndpointOptionsState(endpoint),
                nameof(NatsEndpointOptionsState) => new NatsEndpointOptionsState(endpoint),
                _ => new EndpointOptionsState(endpoint)
            };
        }

        private string? OptionsTypeOf(EndpointProtocolOptionsDataType? options)
        {
            if (options is null)
            {
                return null;
            }
            if (m_media)
            {
                return nameof(MediaEndpointOptionsState);
            }
            return options switch
            {
                EndpointProtocolOptionsMQTT50DataType or EndpointProtocolOptionsMQTT311DataType =>
                    nameof(MqttEndpointOptionsState),
                EndpointProtocolOptionsHTTPDataType => nameof(HttpEndpointOptionsState),
                EndpointProtocolOptionsAMQP10DataType => nameof(AmqpEndpointOptionsState),
                EndpointProtocolOptionsKAFKADataType => nameof(KafkaEndpointOptionsState),
                EndpointProtocolOptionsNATSDataType => nameof(NatsEndpointOptionsState),
                _ => nameof(EndpointOptionsState)
            };
        }

        private Group CreateGroup(RegistryCommittedState state, string collection, string groupId)
        {
            string xid = "/" + collection + "/" + groupId;
            (RegistryRecordDataType record, uint epoch) = m_host.ReadRecord(state, xid);
            string? name = null;
            string? description = null;
            string? documentation = null;
            string? envelope = null;
            string? protocol = null;
            ImmutableSortedDictionary<string, string>? labels = null;
            EndpointDataType? endpoint = null;
            MessageDefinitionMapDataType? messages = null;
            string? optionsType = null;
            switch (record)
            {
                case EndpointDataType value:
                    endpoint = value;
                    (name, description, documentation) = (value.Name, value.Description, value.Documentation);
                    (envelope, protocol) = (value.Envelope, value.Protocol);
                    labels = Labels(value.Labels?.Entries);
                    messages = value.Messages;
                    optionsType = OptionsTypeOf(value.ProtocolOptions);
                    break;
                case MessageGroupDataType value:
                    (name, description, documentation) = (value.Name, value.Description, value.Documentation);
                    (envelope, protocol) = (value.Envelope, value.Protocol);
                    labels = Labels(value.Labels?.Entries);
                    messages = value.Messages;
                    break;
                default:
                    throw new InvalidOperationException("Unexpected Group record " + record.GetType().Name);
            }
            var resources = new List<IXRegistryProjectionMetadataResource>();
            if (messages?.Entries is { } entries)
            {
                foreach (MessageDefinitionMapEntryDataType entry in entries)
                {
                    resources.Add(CreateMessage(state, collection, groupId, entry.Name!));
                }
            }
            return new Group(collection, groupId, xid, epoch, Present(record, "Name", name),
                Present(record, "Description", description), Present(record, "Documentation", documentation),
                Present(record, "Labels", labels), Present(record, "Envelope", envelope),
                Present(record, "Protocol", protocol), endpoint, optionsType,
                Result(record, epoch), MetadataSize(state, xid), resources.ToArray());
        }

        private Message CreateMessage(RegistryCommittedState state, string collection, string groupId, string id)
        {
            string xid = "/" + collection + "/" + groupId + "/messages/" + id;
            (RegistryRecordDataType value, uint epoch) = m_host.ReadRecord(state, xid);
            var record = (MessageDefinitionDataType)value;
            return new Message(collection, groupId, id, xid, epoch,
                Present(record, "Name", record.Name), Present(record, "Description", record.Description),
                Present(record, "Documentation", record.Documentation),
                Present(record, "Labels", Labels(record.Labels?.Entries)),
                Present(record, "VersionId", record.VersionId),
                record, Result(record, epoch), MetadataSize(state, xid),
                new QualifiedName(BrowseNames.Messages, m_namespaceIndex));
        }

        private ulong MetadataSize(RegistryCommittedState state, string xid)
        {
            return (ulong)RegistryValues.ToJson(m_host.ReadValue(state, xid).Value).Length;
        }

        private static T? Present<T>(RegistryRecordDataType record, string field, T? value)
            where T : class
        {
            foreach (string present in record.PresentFields)
            {
                if (present == field)
                {
                    return value;
                }
            }
            return null;
        }

        private static ImmutableSortedDictionary<string, string>? Labels<TEntry>(ArrayOf<TEntry>? entries)
            where TEntry : IEncodeable
        {
            if (entries is not { } values)
            {
                return null;
            }
            ImmutableSortedDictionary<string, string>.Builder labels =
                ImmutableSortedDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
            foreach (TEntry entry in values)
            {
                (string? name, string? value) = entry switch
                {
                    EndpointLabelsEntryDataType e => (e.Name, e.Value),
                    MessageGroupLabelsEntryDataType e => (e.Name, e.Value),
                    MessageDefinitionLabelsEntryDataType e => (e.Name, e.Value),
                    _ => (null, null)
                };
                if (name is not null && value is not null)
                {
                    labels[name] = value;
                }
            }
            return labels.ToImmutable();
        }

        private static RegistryReadResultDataType Result(RegistryRecordDataType record, uint epoch)
        {
            return new RegistryReadResultDataType
            {
                StatusCode = StatusCodes.Good,
                Epoch = epoch,
                Document = new ExtensionObject(record),
                ContinuationPoint = ByteString.Empty,
                Issues = []
            };
        }

        private static RegistryValueDataType? Member(RegistryObjectValueDataType map, string name)
        {
            foreach (RegistryMemberDataType member in map.Members)
            {
                if (member.Name == name)
                {
                    return member.Value;
                }
            }
            return null;
        }

        private static ImmutableSortedDictionary<string, string> RootLabels(RegistryObjectValueDataType document)
        {
            var labels = ImmutableSortedDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
            if (Member(document, "labels") is RegistryObjectValueDataType values)
            {
                foreach (RegistryMemberDataType member in values.Members)
                {
                    if (member.Value is not RegistryStringValueDataType text || text.Value is null || member.Name is null)
                    {
                        throw new ServiceResultException(StatusCodes.BadInvalidState, "Committed labels are not Strings.");
                    }
                    labels.Add(member.Name, text.Value);
                }
            }
            return labels.ToImmutable();
        }

        private sealed class Snapshot : IXRegistryCollectionProjectionSnapshot
        {
            public static readonly Snapshot Empty = new([], ImmutableSortedDictionary<string, string>.Empty);

            public Snapshot(ArrayOf<IXRegistryProjectionCollection> collections,
                ImmutableSortedDictionary<string, string> labels)
            {
                Collections = collections;
                Labels = labels;
            }

            public ImmutableSortedDictionary<string, string> Labels { get; }

            public IEnumerable<IXRegistryProjectionGroup> Groups => [];

            public ArrayOf<IXRegistryProjectionCollection> Collections { get; }
        }

        private sealed class Collection(
            string name,
            QualifiedName browseName,
            ArrayOf<IXRegistryProjectionCollectionGroup> groups) : IXRegistryProjectionCollection
        {
            public string Name { get; } = name;

            public QualifiedName BrowseName { get; } = browseName;

            public NodeId NodeId => NodeId.Null;

            public ArrayOf<IXRegistryProjectionCollectionGroup> Groups { get; } = groups;
        }

        private abstract class Entity : IXRegistryProjectionDomainShape
        {
            protected Entity(
                string collection,
                string groupId,
                string xid,
                uint epoch,
                string? name,
                string? description,
                string? documentation,
                ImmutableSortedDictionary<string, string>? labels,
                RegistryReadResultDataType snapshot,
                ulong metadataSize)
            {
                MetadataSize = metadataSize;
                CollectionName = collection;
                GroupId = groupId;
                Xid = xid;
                Epoch = epoch;
                Name = name;
                Description = description;
                Documentation = documentation;
                Labels = labels;
                Snapshot = snapshot;
            }

            public string CollectionName { get; }

            public string GroupId { get; }

            public string Xid { get; }

            public NodeId NodeId => NodeId.Null;

            public ExpandedNodeId TypeDefinitionId => ExpandedNodeId.Null;

            public uint Epoch { get; }

            public string? Name { get; }

            public string? Description { get; }

            public string? Documentation { get; }

            public DateTimeUtc CreatedAt => DateTimeUtc.MinValue;

            public DateTimeUtc ModifiedAt => DateTimeUtc.MinValue;

            public ImmutableSortedDictionary<string, string>? Labels { get; }

            public RegistryReadResultDataType Snapshot { get; }

            public ulong MetadataSize { get; }

            public abstract string DomainShape { get; }
        }

        private sealed class Group : Entity, IXRegistryProjectionCollectionGroup
        {
            public Group(
                string collection,
                string groupId,
                string xid,
                uint epoch,
                string? name,
                string? description,
                string? documentation,
                ImmutableSortedDictionary<string, string>? labels,
                string? envelope,
                string? protocol,
                EndpointDataType? endpoint,
                string? optionsType,
                RegistryReadResultDataType snapshot,
                ulong metadataSize,
                ArrayOf<IXRegistryProjectionMetadataResource> resources)
                : base(collection, groupId, xid, epoch, name, description, documentation, labels, snapshot, metadataSize)
            {
                Envelope = envelope;
                Protocol = protocol;
                Endpoint = endpoint;
                OptionsType = optionsType;
                Resources = resources;
            }

            public XRegistryProjectionEntityRole Role => XRegistryProjectionEntityRole.Group;

            public string? Envelope { get; }

            public string? Protocol { get; }

            public EndpointDataType? Endpoint { get; }

            public string? OptionsType { get; }

            public ArrayOf<IXRegistryProjectionMetadataResource> Resources { get; }

            public override string DomainShape =>
                (Envelope is null ? "-" : "E") + (Protocol is null ? "-" : "P") +
                (Endpoint?.Channel is null ? "-" : "C") + "|" + OptionsType;
        }

        private sealed class Message : Entity, IXRegistryProjectionMetadataResource
        {
            public Message(
                string collection,
                string groupId,
                string resourceId,
                string xid,
                uint epoch,
                string? name,
                string? description,
                string? documentation,
                ImmutableSortedDictionary<string, string>? labels,
                string? versionId,
                MessageDefinitionDataType record,
                RegistryReadResultDataType snapshot,
                ulong metadataSize,
                QualifiedName container)
                : base(collection, groupId, xid, epoch, name, description, documentation, labels, snapshot, metadataSize)
            {
                ResourceId = resourceId;
                VersionId = versionId;
                Record = record;
                ContainerBrowseName = container;
            }

            public XRegistryProjectionEntityRole Role => XRegistryProjectionEntityRole.Resource;

            public string ResourceCollectionName => "messages";

            public string ResourceId { get; }

            public string? VersionId { get; }

            public MessageDefinitionDataType Record { get; }

            public QualifiedName ContainerBrowseName { get; }

            public override string DomainShape => Record.DataSchema is null ? "-" : "S";
        }

        private readonly RegistryNativeHost m_host;
        private readonly ISystemContext m_context;
        private readonly ushort m_namespaceIndex;
        private readonly bool m_media;
        private readonly Func<ISystemContext, RegistryAccessKind, ServiceResult> m_authorize;
        private readonly RegistryMetadataFileBinding m_metadataFiles;
        private XRegistryProjectionGeneration m_generation;
    }
}
