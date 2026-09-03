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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.XRegistry;

namespace Opc.Ua.XRegistry.Server
{
    /// <summary>
    /// Projects immutable xRegistry snapshots into a stable browseable group/resource tree.
    /// </summary>
    public sealed class XRegistryProjectionEngine : IDisposable
    {
        /// <summary>
        /// Initializes a new projection engine.
        /// </summary>
        public XRegistryProjectionEngine(
            XRegistryProjectionContext context,
            IXRegistryProjectionStrategy strategy,
            string registryNodeIdPath)
        {
            m_context = context ?? throw new ArgumentNullException(nameof(context));
            m_strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
            m_registryNodeIdPath = string.IsNullOrEmpty(registryNodeIdPath)
                ? throw new ArgumentException("The registry NodeId path is required.", nameof(registryNodeIdPath))
                : registryNodeIdPath;
            m_eventOptions = context.EventOptions;
            m_versionedStrategy = strategy as IXRegistryVersionedProjectionStrategy;
            m_generationProvider = strategy as IXRegistryProjectionGenerationProvider;
            if (m_eventOptions?.EventsEnabled == true)
            {
                _ = m_generationProvider ??
                    throw new ArgumentException(
                        "A generation-bound projection provider is required when xRegistry events " +
                        "are enabled.",
                        nameof(strategy));
            }
        }

        /// <summary>
        /// Binds the engine to the registry object and performs the first reconciliation.
        /// </summary>
        public async ValueTask AttachAsync(BaseObjectState registryNode, CancellationToken ct)
        {
            m_registryNode = registryNode ?? throw new ArgumentNullException(nameof(registryNode));
            registryNode.EventNotifier = EventNotifiers.SubscribeToEvents;
            if (m_eventOptions?.EventsEnabled == true)
            {
                m_eventEmitter = new XRegistryEventEmitter(
                    m_context.SystemContext,
                    m_eventOptions.EventSourceUrl);
                if (registryNode is RegistryState eventRegistry)
                {
                    eventRegistry.AddEventSourceUrl(m_context.SystemContext);
                    SetValue(eventRegistry.EventSourceUrl, m_eventOptions.EventSourceUrl);
                }
            }
            WireMethod(registryNode, BrowseNames.CreateGroup, OnCreateGroupAsync);
            WireMethod(registryNode, BrowseNames.GetOrCreateGroup, OnGetOrCreateGroupAsync);
            if (registryNode is RegistryState registryTyped)
            {
                registryTyped.AddLabels(m_context.SystemContext);
                WireLabelsContainer(
                    registryTyped.Labels,
                    OnAddRegistryLabelAsync,
                    OnRemoveRegistryLabelAsync);
                LinkMethodArguments(registryTyped.Labels, m_context.SystemContext);
            }
            await ReconcileAsync(ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Finds a projected resource node by xid.
        /// </summary>
        public NodeState EventSourceFor(string? xid)
        {
            if (!string.IsNullOrEmpty(xid) &&
                m_resourcesByXid.TryGetValue(xid!, out ResourceState? node))
            {
                return node;
            }
            return m_registryNode!;
        }

        /// <summary>
        /// Reconciles the browseable tree with <see cref="IXRegistryProjectionStrategy.Current"/>.
        /// </summary>
        public ValueTask ReconcileAsync(CancellationToken ct)
        {
            return ReconcileCoreAsync(
                suppliedGeneration: null,
                previousEventSnapshot: null,
                useSuppliedTransition: false,
                emitEvents: true,
                ct);
        }

        /// <summary>
        /// Reconciles the browseable tree to the latest snapshot without
        /// advancing or diffing event state. Supplied immutable transitions
        /// remain the sole authority for event ordering.
        /// </summary>
        public ValueTask ReconcileProjectionAsync(CancellationToken ct)
        {
            return ReconcileCoreAsync(
                suppliedGeneration: null,
                previousEventSnapshot: null,
                useSuppliedTransition: false,
                emitEvents: false,
                ct);
        }

        /// <summary>
        /// Reconciles one supplied immutable generation and, when events are enabled,
        /// diffs it against the supplied previous event snapshot.
        /// </summary>
        public ValueTask ReconcileAsync(
            XRegistryProjectionGeneration generation,
            XRegistryProjectionEventSnapshot? previousEventSnapshot,
            CancellationToken ct)
        {
            return ReconcileCoreAsync(
                generation ?? throw new ArgumentNullException(nameof(generation)),
                previousEventSnapshot,
                useSuppliedTransition: true,
                emitEvents: true,
                ct);
        }

        private async ValueTask ReconcileCoreAsync(
            XRegistryProjectionGeneration? suppliedGeneration,
            XRegistryProjectionEventSnapshot? previousEventSnapshot,
            bool useSuppliedTransition,
            bool emitEvents,
            CancellationToken ct)
        {
            if (m_registryNode is null)
            {
                return;
            }
            await m_gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                XRegistryProjectionGeneration generation = suppliedGeneration ??
                    (m_generationProvider is null
                        ? new XRegistryProjectionGeneration(m_strategy.Current, null)
                        : m_generationProvider.CaptureProjectionGeneration());
                IXRegistryProjectionSnapshot snapshot = generation.Projection;
                XRegistryProjectionEventSnapshot? eventSnapshot = generation.Events;
                if (emitEvents &&
                    m_eventOptions?.EventsEnabled == true &&
                    eventSnapshot is null)
                {
                    throw new InvalidOperationException(
                        "The captured projection generation did not include event metadata.");
                }
                long? projectionSequence = eventSnapshot?.Epoch;
                bool applyProjection = projectionSequence is null ||
                    projectionSequence.Value >= m_latestProjectionSequence;
                if (applyProjection)
                {
                    if (m_registryNode is RegistryState registryTyped &&
                        registryTyped.Labels is not null)
                    {
                        await SyncLabelPropertiesAsync(
                            registryTyped.Labels,
                            m_registryNodeIdPath,
                            snapshot.Labels,
                            ct).ConfigureAwait(false);
                    }

                    var seenGroups = new HashSet<string>(StringComparer.Ordinal);
                    foreach (IXRegistryProjectionGroup group in snapshot.Groups)
                    {
                        seenGroups.Add(group.GroupId);
                        if (!m_groups.TryGetValue(group.GroupId, out GroupEntry? entry))
                        {
                            entry = await CreateGroupNodeAsync(group, ct).ConfigureAwait(false);
                            m_groups[group.GroupId] = entry;
                        }
                        else
                        {
                            ApplyGroupProperties(entry.Node, group);
                            m_strategy.ConfigureGroupNode(entry.Node, group);
                            if (entry.Node.Labels is not null)
                            {
                                await SyncLabelPropertiesAsync(
                                    entry.Node.Labels,
                                    GroupNodeIdPath(group.GroupId),
                                    group.Labels,
                                    ct).ConfigureAwait(false);
                            }
                            entry.Node.ClearChangeMasks(
                                m_context.SystemContext,
                                includeChildren: true);
                        }

                        await ReconcileResourcesAsync(entry, group, eventSnapshot, ct)
                            .ConfigureAwait(false);
                    }

                    foreach (string groupId in m_groups.Keys
                        .Where(id => !seenGroups.Contains(id))
                        .ToList())
                    {
                        await RemoveGroupNodeAsync(groupId, ct).ConfigureAwait(false);
                    }
                    if (projectionSequence is not null)
                    {
                        m_latestProjectionSequence = projectionSequence.Value;
                    }
                }

                if (emitEvents && eventSnapshot is not null)
                {
                    XRegistryProjectionEventSnapshot? previous = useSuppliedTransition
                        ? previousEventSnapshot
                        : m_previousEventSnapshot;
                    if (previous is not null &&
                        m_eventEmitter is not null &&
                        TryMarkReportedTransition(previous.Epoch, eventSnapshot.Epoch))
                    {
                        m_eventEmitter.Report(
                            m_registryNode,
                            DiffEventSnapshots(previous, eventSnapshot));
                    }
                    if (m_previousEventSnapshot is null ||
                        eventSnapshot.Epoch >= m_previousEventSnapshot.Epoch)
                    {
                        m_previousEventSnapshot = eventSnapshot;
                    }
                }
            }
            finally
            {
                m_gate.Release();
            }
        }

        private bool TryMarkReportedTransition(uint previousEpoch, uint currentEpoch)
        {
            ulong transition = ((ulong)previousEpoch << 32) | currentEpoch;
            if (!m_reportedTransitions.Add(transition))
            {
                return false;
            }
            m_reportedTransitionOrder.Enqueue(transition);
            if (m_reportedTransitionOrder.Count > 128)
            {
                m_reportedTransitions.Remove(m_reportedTransitionOrder.Dequeue());
            }
            return true;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (m_disposed)
            {
                return;
            }
            m_disposed = true;
            foreach (GroupEntry group in m_groups.Values)
            {
                foreach (ResourceEntry resource in group.Resources.Values)
                {
                    resource.File?.Dispose();
                }
            }
            m_groups.Clear();
            m_resourcesByXid.Clear();
            m_gate.Dispose();
        }

        /// <summary>
        /// Links generated Method argument properties in a subtree.
        /// </summary>
        public static void LinkMethodArguments(NodeState? node, ISystemContext context)
        {
            if (node is null)
            {
                return;
            }
            if (node is MethodState method)
            {
                var arguments = new List<BaseInstanceState>();
                method.GetChildren(context, arguments);
                foreach (BaseInstanceState child in arguments)
                {
                    if (child is not PropertyState<ArrayOf<Argument>> args)
                    {
                        continue;
                    }
                    if (method.InputArguments is null &&
                        string.Equals(args.BrowseName.Name, Opc.Ua.BrowseNames.InputArguments,
                            StringComparison.Ordinal))
                    {
                        method.InputArguments = args;
                    }
                    else if (method.OutputArguments is null &&
                        string.Equals(args.BrowseName.Name, Opc.Ua.BrowseNames.OutputArguments,
                            StringComparison.Ordinal))
                    {
                        method.OutputArguments = args;
                    }
                }
            }
            var children = new List<BaseInstanceState>();
            node.GetChildren(context, children);
            foreach (BaseInstanceState child in children)
            {
                LinkMethodArguments(child, context);
            }
        }

        /// <summary>
        /// Sets a property value when the property is present.
        /// </summary>
        /// <typeparam name="T">The property value type.</typeparam>
        /// <param name="property">The property to update.</param>
        /// <param name="value">The value to assign.</param>
        public static void SetValue<T>(PropertyState<T>? property, T value)
        {
            property?.Value = value;
        }

        private async ValueTask ReconcileResourcesAsync(
            GroupEntry entry,
            IXRegistryProjectionGroup group,
            XRegistryProjectionEventSnapshot? eventSnapshot,
            CancellationToken ct)
        {
            var seenResources = new HashSet<ResourceEntryKey>();
            foreach (IXRegistryProjectionResource resource in group.Resources)
            {
                ResourceEntryKey key = ResourceKey(resource);
                seenResources.Add(key);
                if (!entry.Resources.TryGetValue(key, out ResourceEntry? res))
                {
                    res = await CreateResourceNodeAsync(entry, resource, eventSnapshot, ct)
                        .ConfigureAwait(false);
                    entry.Resources[key] = res;
                }
                else
                {
                    ApplyResourceProperties(res, resource, eventSnapshot);
                    m_strategy.ConfigureResourceNode(res.Node, resource);
                    if (res.Node.Labels is not null)
                    {
                        await SyncLabelPropertiesAsync(
                            res.Node.Labels,
                            ResourceNodeIdPath(
                                resource.GroupId,
                                resource.ResourceId,
                                resource.VersionId),
                            resource.Labels,
                            ct).ConfigureAwait(false);
                    }
                    if (res.Node.MetaLabels is not null &&
                        resource is IXRegistryProjectionResourceMeta meta)
                    {
                        await SyncLabelPropertiesAsync(
                            res.Node.MetaLabels,
                            ResourceMetaNodeIdPath(
                                resource.GroupId,
                                resource.ResourceId,
                                resource.VersionId),
                            meta.MetaLabels,
                            ct).ConfigureAwait(false);
                    }
                    res.Node.ClearChangeMasks(m_context.SystemContext, includeChildren: true);
                }
            }

            foreach (ResourceEntryKey resourceKey in entry.Resources.Keys
                .Where(id => !seenResources.Contains(id)).ToList())
            {
                await RemoveResourceNodeAsync(entry, resourceKey, ct).ConfigureAwait(false);
            }
        }

        private async ValueTask<GroupEntry> CreateGroupNodeAsync(
            IXRegistryProjectionGroup group,
            CancellationToken ct)
        {
            GroupState node = m_strategy.CreateGroupNode(m_registryNode!, group);
            NodeId nodeId = GroupNodeId(group.GroupId);
            node.ReferenceTypeId = Opc.Ua.ReferenceTypeIds.Organizes;
            node.Create(
                m_context.SystemContext,
                nodeId,
                new QualifiedName(group.GroupId, m_context.ModelNamespaceIndex),
                new LocalizedText(group.Name),
                assignNodeIds: false);
            node.AddCreateResource(m_context.SystemContext)
                .AddGetOrCreateResource(m_context.SystemContext)
                .AddDelete(m_context.SystemContext)
                .AddXid(m_context.SystemContext)
                .AddEpoch(m_context.SystemContext)
                .AddDescription(m_context.SystemContext)
                .AddCreatedAt(m_context.SystemContext)
                .AddModifiedAt(m_context.SystemContext)
                .AddLabels(m_context.SystemContext);
            node.EventNotifier = EventNotifiers.SubscribeToEvents;

            string groupId = group.GroupId;
            node.CreateResource?.OnCallMethod2Async =
                (c, m, o, i, ot, t) => OnCreateResourceAsync(groupId, c, i, ot, t);
            node.GetOrCreateResource?.OnCallMethod2Async =
                (c, m, o, i, ot, t) => OnGetOrCreateResourceAsync(groupId, c, i, ot, t);
            node.Delete?.OnCallMethod2Async = (c, m, o, i, ot, t) => OnDeleteGroupAsync(groupId, c, i, t);
            WireLabelsContainer(
                node.Labels,
                (c, i, t) => OnAddGroupLabelAsync(groupId, c, i, t),
                (c, i, t) => OnRemoveGroupLabelAsync(groupId, c, i, t));

            ApplyGroupProperties(node, group);
            DateTime createdAt = DateTime.UtcNow;
            SetValue(node.CreatedAt, (DateTimeUtc)createdAt);
            SetValue(node.ModifiedAt, (DateTimeUtc)createdAt);
            m_strategy.ConfigureGroupNode(node, group);
            m_context.SystemContext.AssignInstanceChildNodeIds(node);
            LinkMethodArguments(node, m_context.SystemContext);

            m_registryNode!.AddChild(node);
            m_registryNode.AddReference(Opc.Ua.ReferenceTypeIds.HasNotifier, false, nodeId);
            node.AddReference(Opc.Ua.ReferenceTypeIds.HasNotifier, true, m_registryNode.NodeId);

            await m_context.AddNodeAsync(node, ct).ConfigureAwait(false);
            await SyncLabelPropertiesAsync(
                node.Labels!, GroupNodeIdPath(group.GroupId), group.Labels, ct).ConfigureAwait(false);
            return new GroupEntry(node);
        }

        private void ApplyGroupProperties(GroupState node, IXRegistryProjectionGroup group)
        {
            SetValue(node.GroupId, group.GroupId);
            SetValue(node.Xid, group.Xid);
            SetValue(node.Epoch, (uint)group.Epoch);
            SetValue(node.Name, group.Name);
            SetValue(node.Description, group.Description);
        }

        private async ValueTask RemoveGroupNodeAsync(string groupId, CancellationToken ct)
        {
            if (!m_groups.TryGetValue(groupId, out GroupEntry? entry))
            {
                return;
            }
            foreach (ResourceEntryKey resourceId in entry.Resources.Keys.ToList())
            {
                await RemoveResourceNodeAsync(entry, resourceId, ct).ConfigureAwait(false);
            }
            m_registryNode!.RemoveReference(Opc.Ua.ReferenceTypeIds.HasNotifier, false, entry.Node.NodeId);
            m_registryNode.RemoveChild(entry.Node);
            await m_context.DeleteNodeAsync(entry.Node.NodeId, ct).ConfigureAwait(false);
            m_groups.Remove(groupId);
        }

        private async ValueTask<ResourceEntry> CreateResourceNodeAsync(
            GroupEntry group,
            IXRegistryProjectionResource resource,
            XRegistryProjectionEventSnapshot? eventSnapshot,
            CancellationToken ct)
        {
            ResourceState node = m_strategy.CreateResourceNode(group.Node, resource);
            NodeId nodeId = ResourceNodeId(
                resource.GroupId,
                resource.ResourceId,
                resource.VersionId);
            node.ReferenceTypeId = Opc.Ua.ReferenceTypeIds.Organizes;
            node.Create(
                m_context.SystemContext,
                nodeId,
                new QualifiedName(
                    ProjectedResourceBrowseName(resource),
                    m_context.ModelNamespaceIndex),
                new LocalizedText(resource.Name),
                assignNodeIds: false);
            node.AddVersionId(m_context.SystemContext)
                .AddFormat(m_context.SystemContext)
                .AddContentType(m_context.SystemContext)
                .AddXid(m_context.SystemContext)
                .AddEpoch(m_context.SystemContext)
                .AddDescription(m_context.SystemContext)
                .AddCreatedAt(m_context.SystemContext)
                .AddModifiedAt(m_context.SystemContext);
            node.AddMetaEpoch(m_context.SystemContext)
                .AddMetaLabels(m_context.SystemContext)
                .AddMetaCreatedAt(m_context.SystemContext)
                .AddMetaModifiedAt(m_context.SystemContext);
            node.AddDelete(m_context.SystemContext);
            node.AddLabels(m_context.SystemContext);
            node.EventNotifier = EventNotifiers.SubscribeToEvents;

            string groupId = resource.GroupId;
            string resourceId = resource.ResourceId;
            string versionId = resource.VersionId;
            node.Delete?.OnCallMethod2Async =
                (c, m, o, i, ot, t) => OnDeleteResourceAsync(
                    groupId,
                    resourceId,
                    versionId,
                    c,
                    i,
                    t);
            WireLabelsContainer(
                node.Labels,
                (c, i, t) => OnAddResourceLabelAsync(
                    groupId,
                    resourceId,
                    versionId,
                    c,
                    i,
                    t),
                (c, i, t) => OnRemoveResourceLabelAsync(
                    groupId,
                    resourceId,
                    versionId,
                    c,
                    i,
                    t));
            WireLabelsContainer(
                node.MetaLabels,
                (c, i, t) => OnAddResourceMetaLabelAsync(groupId, resourceId, c, i, t),
                (c, i, t) => OnRemoveResourceMetaLabelAsync(groupId, resourceId, c, i, t));

            IXRegistryProjectedResourceFile? file = m_strategy.CreateResourceFile(node, resource);
            var entry = new ResourceEntry(node, file, groupId, resourceId, versionId, resource.Xid);
            ApplyResourceProperties(entry, resource, eventSnapshot);
            m_strategy.ConfigureResourceNode(node, resource);
            m_context.SystemContext.AssignInstanceChildNodeIds(node);
            LinkMethodArguments(node, m_context.SystemContext);

            group.Node.AddChild(node);
            group.Node.AddReference(Opc.Ua.ReferenceTypeIds.HasNotifier, false, nodeId);
            node.AddReference(Opc.Ua.ReferenceTypeIds.HasNotifier, true, group.Node.NodeId);

            await m_context.AddNodeAsync(node, ct).ConfigureAwait(false);
            m_resourcesByXid[resource.Xid] = node;
            await SyncLabelPropertiesAsync(
                node.Labels!,
                ResourceNodeIdPath(groupId, resourceId, versionId),
                resource.Labels,
                ct)
                .ConfigureAwait(false);
            if (node.MetaLabels is not null &&
                resource is IXRegistryProjectionResourceMeta meta)
            {
                await SyncLabelPropertiesAsync(
                    node.MetaLabels,
                    ResourceMetaNodeIdPath(groupId, resourceId, versionId),
                    meta.MetaLabels,
                    ct).ConfigureAwait(false);
            }
            return entry;
        }

        private void ApplyResourceProperties(
            ResourceEntry entry,
            IXRegistryProjectionResource resource,
            XRegistryProjectionEventSnapshot? eventSnapshot)
        {
            SetValue(entry.Node.ResourceId, resource.ResourceId);
            entry.Node.BrowseName = new QualifiedName(
                ProjectedResourceBrowseName(resource),
                m_context.ModelNamespaceIndex);
            SetValue(entry.Node.VersionId, resource.VersionId);
            SetValue(entry.Node.Format, resource.Format);
            SetValue(entry.Node.ContentType, resource.ContentType);
            SetValue(entry.Node.Xid, resource.Xid);
            SetValue(entry.Node.Epoch, (uint)resource.Epoch);
            SetValue(entry.Node.Name, resource.Name);
            SetValue(entry.Node.Description, resource.Description);
            if (resource.CreatedAt != default)
            {
                SetValue(entry.Node.CreatedAt, (DateTimeUtc)resource.CreatedAt);
            }
            SetValue(entry.Node.ModifiedAt, (DateTimeUtc)resource.ModifiedAt);
            if (resource is IXRegistryProjectionResourceMeta meta)
            {
                SetValue(entry.Node.MetaEpoch, checked((uint)meta.MetaEpoch));
                SetValue(entry.Node.MetaCreatedAt, (DateTimeUtc)meta.MetaCreatedAt);
                SetValue(entry.Node.MetaModifiedAt, (DateTimeUtc)meta.MetaModifiedAt);
                if (meta.IsDefaultVersion &&
                    FindEventResource(
                        eventSnapshot,
                        resource.GroupId,
                        resource.ResourceId) is { } logicalResource)
                {
                    m_resourcesByXid[logicalResource.Xid] = entry.Node;
                }
            }
            else if (FindEventResource(
                    eventSnapshot,
                    resource.GroupId,
                    resource.ResourceId) is { } eventResource)
            {
                SetValue(entry.Node.MetaEpoch, eventResource.MetaEpoch);
                SetValue(entry.Node.MetaCreatedAt, (DateTimeUtc)eventResource.MetaCreatedAt);
                SetValue(entry.Node.MetaModifiedAt, (DateTimeUtc)eventResource.MetaModifiedAt);
            }
            entry.File?.ApplyResource(resource);
        }

        private string ProjectedResourceBrowseName(IXRegistryProjectionResource resource)
        {
            if (m_versionedStrategy is null)
            {
                return resource.ResourceId;
            }
            return resource is IXRegistryProjectionResourceMeta { IsDefaultVersion: true }
                ? resource.ResourceId
                : resource.VersionId;
        }

        private static XRegistryProjectionEventResource? FindEventResource(
            XRegistryProjectionEventSnapshot? eventSnapshot,
            string groupId,
            string resourceId)
        {
            if (eventSnapshot is null)
            {
                return null;
            }
            foreach (XRegistryProjectionEventGroup group in eventSnapshot.Groups)
            {
                if (!string.Equals(group.GroupId, groupId, StringComparison.Ordinal))
                {
                    continue;
                }
                return group.Resources.FirstOrDefault(resource =>
                    string.Equals(resource.ResourceId, resourceId, StringComparison.Ordinal));
            }
            return null;
        }

        private async ValueTask RemoveResourceNodeAsync(
            GroupEntry group,
            ResourceEntryKey resourceKey,
            CancellationToken ct)
        {
            if (!group.Resources.TryGetValue(resourceKey, out ResourceEntry? entry))
            {
                return;
            }
            entry.File?.Dispose();
            foreach (KeyValuePair<string, ResourceState> mapped in m_resourcesByXid
                .Where(mapped => ReferenceEquals(mapped.Value, entry.Node)).ToList())
            {
                m_resourcesByXid.TryRemove(mapped.Key, out _);
            }
            group.Node.RemoveReference(Opc.Ua.ReferenceTypeIds.HasNotifier, false, entry.Node.NodeId);
            group.Node.RemoveChild(entry.Node);
            await m_context.DeleteNodeAsync(entry.Node.NodeId, ct).ConfigureAwait(false);
            group.Resources.Remove(resourceKey);
        }

        private async ValueTask<ServiceResult> OnCreateGroupAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            ArrayOf<Variant> input,
            List<Variant> output,
            CancellationToken ct)
        {
            ServiceResult access = m_context.CheckManagementAccess(context, "CreateGroup");
            if (ServiceResult.IsBad(access))
            {
                return access;
            }
            string? groupId = GetString(input, 0);
            if (string.IsNullOrWhiteSpace(groupId))
            {
                return StatusCodes.BadInvalidArgument;
            }
            IXRegistryProjectionGroup? group = await m_strategy
                .CreateGroupAsync(groupId!, ct).ConfigureAwait(false);
            if (group is null)
            {
                return ServiceResult.Create(
                    StatusCodes.BadNodeIdExists, $"Group '{groupId}' already exists.");
            }
            await ReconcileProjectionAsync(ct).ConfigureAwait(false);
            output.Clear();
            output.Add(new Variant(GroupNodeId(group.GroupId)));
            return ServiceResult.Good;
        }

        private async ValueTask<ServiceResult> OnGetOrCreateGroupAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            ArrayOf<Variant> input,
            List<Variant> output,
            CancellationToken ct)
        {
            ServiceResult access = m_context.CheckManagementAccess(context, "GetOrCreateGroup");
            if (ServiceResult.IsBad(access))
            {
                return access;
            }
            string? groupId = GetString(input, 0);
            if (string.IsNullOrWhiteSpace(groupId))
            {
                return StatusCodes.BadInvalidArgument;
            }
            (IXRegistryProjectionGroup group, bool created) = await m_strategy
                .GetOrCreateGroupAsync(groupId!, ct).ConfigureAwait(false);
            await ReconcileProjectionAsync(ct).ConfigureAwait(false);
            output.Clear();
            output.Add(new Variant(GroupNodeId(group.GroupId)));
            output.Add(new Variant(created));
            return ServiceResult.Good;
        }

        private async ValueTask<ServiceResult> OnCreateResourceAsync(
            string groupId,
            ISystemContext context,
            ArrayOf<Variant> input,
            List<Variant> output,
            CancellationToken ct)
        {
            ServiceResult access = m_context.CheckManagementAccess(context, "CreateResource");
            if (ServiceResult.IsBad(access))
            {
                return access;
            }
            string? resourceId = GetString(input, 0);
            string versionId = GetString(input, 1) ?? string.Empty;
            bool requestOpen = GetBool(input, 2, false);
            if (string.IsNullOrWhiteSpace(resourceId))
            {
                return StatusCodes.BadInvalidArgument;
            }
            IXRegistryProjectionResource? resource = m_versionedStrategy is null
                ? await m_strategy.CreateResourceAsync(groupId, resourceId!, ct)
                    .ConfigureAwait(false)
                : await m_versionedStrategy.CreateResourceAsync(
                        groupId,
                        resourceId!,
                        versionId,
                        ct)
                    .ConfigureAwait(false);
            if (resource is null)
            {
                if (requestOpen)
                {
                    (bool handled, ServiceResult result) =
                        await TryOpenExistingContentlessResourceAsync(
                                groupId,
                                resourceId!,
                                versionId,
                                context,
                                output,
                                ct)
                            .ConfigureAwait(false);
                    if (handled)
                    {
                        return result;
                    }
                }
                return ServiceResult.Create(
                    StatusCodes.BadNodeIdExists,
                    $"Resource '{resourceId}' already exists in group '{groupId}'.");
            }
            await ReconcileProjectionAsync(ct).ConfigureAwait(false);
            return await CompleteResourceOutputAsync(
                resource,
                requestOpen,
                context,
                output,
                created: null,
                ct)
                .ConfigureAwait(false);
        }

        private async ValueTask<(bool Handled, ServiceResult Result)>
            TryOpenExistingContentlessResourceAsync(
            string groupId,
            string resourceId,
            string versionId,
            ISystemContext context,
            List<Variant> output,
            CancellationToken ct)
        {
            await m_gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var key = new ResourceEntryKey(
                    resourceId,
                    m_versionedStrategy is null ? string.Empty : versionId);
                if (!m_groups.TryGetValue(groupId, out GroupEntry? group) ||
                    !group.Resources.TryGetValue(key, out ResourceEntry? entry) ||
                    entry.File is not IXRegistryProjectedContentlessResourceFile contentlessFile)
                {
                    return (false, ServiceResult.Good);
                }

                ServiceResult open = contentlessFile.TryOpenContentlessWriteHandle(
                    context,
                    out uint fileHandle);
                if (ServiceResult.IsBad(open))
                {
                    if (open.StatusCode == StatusCodes.BadInvalidState)
                    {
                        return (
                            true,
                            ServiceResult.Create(
                                StatusCodes.BadNodeIdExists,
                                $"Resource '{resourceId}' already contains document content."));
                    }
                    return (true, open);
                }

                output.Clear();
                output.Add(new Variant(entry.Node.NodeId));
                output.Add(new Variant(entry.VersionId));
                output.Add(new Variant(fileHandle));
                return (true, ServiceResult.Good);
            }
            finally
            {
                m_gate.Release();
            }
        }

        private async ValueTask<ServiceResult> OnGetOrCreateResourceAsync(
            string groupId,
            ISystemContext context,
            ArrayOf<Variant> input,
            List<Variant> output,
            CancellationToken ct)
        {
            ServiceResult access = m_context.CheckManagementAccess(context, "GetOrCreateResource");
            if (ServiceResult.IsBad(access))
            {
                return access;
            }
            string? resourceId = GetString(input, 0);
            string versionId = GetString(input, 1) ?? string.Empty;
            bool requestOpen = GetBool(input, 2, false);
            if (string.IsNullOrWhiteSpace(resourceId))
            {
                return StatusCodes.BadInvalidArgument;
            }
            (IXRegistryProjectionResource resource, bool created) = m_versionedStrategy is null
                ? await m_strategy.GetOrCreateResourceAsync(groupId, resourceId!, ct)
                    .ConfigureAwait(false)
                : await m_versionedStrategy.GetOrCreateResourceAsync(
                        groupId,
                        resourceId!,
                        versionId,
                        ct)
                    .ConfigureAwait(false);
            await ReconcileProjectionAsync(ct).ConfigureAwait(false);
            return await CompleteResourceOutputAsync(
                resource,
                requestOpen,
                context,
                output,
                created,
                ct)
                .ConfigureAwait(false);
        }

        private async ValueTask<ServiceResult> CompleteResourceOutputAsync(
            IXRegistryProjectionResource resource,
            bool requestOpen,
            ISystemContext context,
            List<Variant> output,
            bool? created,
            CancellationToken ct)
        {
            string groupId = resource.GroupId;
            string resourceId = resource.ResourceId;
            string versionId = resource.VersionId;
            NodeId nodeId = ResourceNodeId(groupId, resourceId, versionId);
            uint fileHandle = 0;
            await m_gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (requestOpen &&
                    m_groups.TryGetValue(groupId, out GroupEntry? group) &&
                    group.Resources.TryGetValue(
                        ResourceKey(resource),
                        out ResourceEntry? entry) &&
                    entry.File is not null)
                {
                    ServiceResult open = entry.File.TryOpenWriteHandle(context, out fileHandle);
                    if (ServiceResult.IsBad(open))
                    {
                        return open;
                    }
                }
            }
            finally
            {
                m_gate.Release();
            }

            output.Clear();
            output.Add(new Variant(nodeId));
            output.Add(new Variant(versionId));
            output.Add(new Variant(fileHandle));
            if (created is { } wasCreated)
            {
                output.Add(new Variant(wasCreated));
            }
            return ServiceResult.Good;
        }

        private async ValueTask<ServiceResult> OnDeleteGroupAsync(
            string groupId,
            ISystemContext context,
            ArrayOf<Variant> input,
            CancellationToken ct)
        {
            ServiceResult access = m_context.CheckManagementAccess(context, "DeleteGroup");
            if (ServiceResult.IsBad(access))
            {
                return access;
            }
            ServiceResult result = await m_strategy
                .DeleteGroupAsync(groupId, OptionalEpoch(input, 0), ct).ConfigureAwait(false);
            await ReconcileProjectionAsync(ct).ConfigureAwait(false);
            return result;
        }

        private async ValueTask<ServiceResult> OnDeleteResourceAsync(
            string groupId,
            string resourceId,
            string versionId,
            ISystemContext context,
            ArrayOf<Variant> input,
            CancellationToken ct)
        {
            ServiceResult access = m_context.CheckManagementAccess(context, "DeleteResource");
            if (ServiceResult.IsBad(access))
            {
                return access;
            }
            ServiceResult result = m_versionedStrategy is null
                ? await m_strategy.DeleteResourceAsync(
                        groupId,
                        resourceId,
                        OptionalEpoch(input, 0),
                        ct)
                    .ConfigureAwait(false)
                : await m_versionedStrategy.DeleteVersionAsync(
                        groupId,
                        resourceId,
                        versionId,
                        OptionalEpoch(input, 0),
                        ct)
                    .ConfigureAwait(false);
            await ReconcileProjectionAsync(ct).ConfigureAwait(false);
            return result;
        }

        private void WireLabelsContainer(
            AttributesState? labels,
            Func<ISystemContext, ArrayOf<Variant>, CancellationToken, ValueTask<ServiceResult>> onAdd,
            Func<ISystemContext, ArrayOf<Variant>, CancellationToken, ValueTask<ServiceResult>> onRemove)
        {
            if (labels is null)
            {
                return;
            }
            labels.AddAddAttribute(m_context.SystemContext)
                .AddRemoveAttribute(m_context.SystemContext);
            labels.AddAttribute?.OnCallMethod2Async = (c, m, o, i, ot, t) => onAdd(c, i, t);
            labels.RemoveAttribute?.OnCallMethod2Async = (c, m, o, i, ot, t) => onRemove(c, i, t);
        }

        private async ValueTask SyncLabelPropertiesAsync(
            AttributesState labels,
            string basePath,
            ImmutableSortedDictionary<string, string> desired,
            CancellationToken ct)
        {
            ISystemContext context = m_context.SystemContext;
            var existing = new Dictionary<string, PropertyState<string>>(StringComparer.Ordinal);
            var children = new List<BaseInstanceState>();
            labels.GetChildren(context, children);
            foreach (BaseInstanceState child in children)
            {
                if (child is PropertyState<string> property && property.BrowseName.Name is string name)
                {
                    existing[name] = property;
                }
            }

            foreach (KeyValuePair<string, string> label in desired)
            {
                if (existing.TryGetValue(label.Key, out PropertyState<string>? property))
                {
                    if (!string.Equals(property.Value, label.Value, StringComparison.Ordinal))
                    {
                        property.Value = label.Value;
                        property.ClearChangeMasks(context, includeChildren: false);
                    }
                    continue;
                }
                PropertyState<string> created = labels.AddAttribute_Placeholder(
                    context,
                    new QualifiedName(label.Key, m_context.ModelNamespaceIndex));
                created.NodeId = LabelNodeId(basePath, label.Key);
                created.Value = label.Value;
                await m_context.AddNodeAsync(created, ct).ConfigureAwait(false);
            }

            foreach (KeyValuePair<string, PropertyState<string>> stale in existing
                .Where(kv => !desired.ContainsKey(kv.Key)).ToList())
            {
                labels.RemoveChild(stale.Value);
                await m_context.DeleteNodeAsync(stale.Value.NodeId, ct).ConfigureAwait(false);
            }
        }

        private NodeId LabelNodeId(string basePath, string key)
        {
            return new NodeId($"{basePath}/labels/{key}", m_context.ModelNamespaceIndex);
        }

        private async ValueTask<ServiceResult> OnAddRegistryLabelAsync(
            ISystemContext context,
            ArrayOf<Variant> input,
            CancellationToken ct)
        {
            ServiceResult access = m_context.CheckManagementAccess(context, "AddAttribute");
            if (ServiceResult.IsBad(access))
            {
                return access;
            }
            ServiceResult result = await m_strategy
                .AddRegistryLabelAsync(GetString(input, 0) ?? string.Empty, GetString(input, 1) ?? string.Empty,
                    OptionalEpoch(input, 2), ct)
                .ConfigureAwait(false);
            await ReconcileProjectionAsync(ct).ConfigureAwait(false);
            return result;
        }

        private async ValueTask<ServiceResult> OnRemoveRegistryLabelAsync(
            ISystemContext context,
            ArrayOf<Variant> input,
            CancellationToken ct)
        {
            ServiceResult access = m_context.CheckManagementAccess(context, "RemoveAttribute");
            if (ServiceResult.IsBad(access))
            {
                return access;
            }
            ServiceResult result = await m_strategy
                .RemoveRegistryLabelAsync(GetString(input, 0) ?? string.Empty, OptionalEpoch(input, 1), ct)
                .ConfigureAwait(false);
            await ReconcileProjectionAsync(ct).ConfigureAwait(false);
            return result;
        }

        private async ValueTask<ServiceResult> OnAddGroupLabelAsync(
            string groupId,
            ISystemContext context,
            ArrayOf<Variant> input,
            CancellationToken ct)
        {
            ServiceResult access = m_context.CheckManagementAccess(context, "AddAttribute");
            if (ServiceResult.IsBad(access))
            {
                return access;
            }
            ServiceResult result = await m_strategy
                .AddGroupLabelAsync(groupId, GetString(input, 0) ?? string.Empty,
                    GetString(input, 1) ?? string.Empty, OptionalEpoch(input, 2), ct)
                .ConfigureAwait(false);
            await ReconcileProjectionAsync(ct).ConfigureAwait(false);
            return result;
        }

        private async ValueTask<ServiceResult> OnRemoveGroupLabelAsync(
            string groupId,
            ISystemContext context,
            ArrayOf<Variant> input,
            CancellationToken ct)
        {
            ServiceResult access = m_context.CheckManagementAccess(context, "RemoveAttribute");
            if (ServiceResult.IsBad(access))
            {
                return access;
            }
            ServiceResult result = await m_strategy
                .RemoveGroupLabelAsync(groupId, GetString(input, 0) ?? string.Empty,
                    OptionalEpoch(input, 1), ct)
                .ConfigureAwait(false);
            await ReconcileProjectionAsync(ct).ConfigureAwait(false);
            return result;
        }

        private async ValueTask<ServiceResult> OnAddResourceLabelAsync(
            string groupId,
            string resourceId,
            string versionId,
            ISystemContext context,
            ArrayOf<Variant> input,
            CancellationToken ct)
        {
            ServiceResult access = m_context.CheckManagementAccess(context, "AddAttribute");
            if (ServiceResult.IsBad(access))
            {
                return access;
            }
            ServiceResult result = m_versionedStrategy is null
                ? await m_strategy.AddResourceLabelAsync(
                        groupId,
                        resourceId,
                        GetString(input, 0) ?? string.Empty,
                        GetString(input, 1) ?? string.Empty,
                        OptionalEpoch(input, 2),
                        ct)
                    .ConfigureAwait(false)
                : await m_versionedStrategy.AddVersionLabelAsync(
                        groupId,
                        resourceId,
                        versionId,
                        GetString(input, 0) ?? string.Empty,
                        GetString(input, 1) ?? string.Empty,
                        OptionalEpoch(input, 2),
                        ct)
                    .ConfigureAwait(false);
            await ReconcileProjectionAsync(ct).ConfigureAwait(false);
            return result;
        }

        private async ValueTask<ServiceResult> OnRemoveResourceLabelAsync(
            string groupId,
            string resourceId,
            string versionId,
            ISystemContext context,
            ArrayOf<Variant> input,
            CancellationToken ct)
        {
            ServiceResult access = m_context.CheckManagementAccess(context, "RemoveAttribute");
            if (ServiceResult.IsBad(access))
            {
                return access;
            }
            ServiceResult result = m_versionedStrategy is null
                ? await m_strategy.RemoveResourceLabelAsync(
                        groupId,
                        resourceId,
                        GetString(input, 0) ?? string.Empty,
                        OptionalEpoch(input, 1),
                        ct)
                    .ConfigureAwait(false)
                : await m_versionedStrategy.RemoveVersionLabelAsync(
                        groupId,
                        resourceId,
                        versionId,
                        GetString(input, 0) ?? string.Empty,
                        OptionalEpoch(input, 1),
                        ct)
                    .ConfigureAwait(false);
            await ReconcileProjectionAsync(ct).ConfigureAwait(false);
            return result;
        }

        private async ValueTask<ServiceResult> OnAddResourceMetaLabelAsync(
            string groupId,
            string resourceId,
            ISystemContext context,
            ArrayOf<Variant> input,
            CancellationToken ct)
        {
            ServiceResult access = m_context.CheckManagementAccess(context, "AddAttribute");
            if (ServiceResult.IsBad(access))
            {
                return access;
            }
            if (m_versionedStrategy is null)
            {
                return StatusCodes.BadNotSupported;
            }
            ServiceResult result = await m_versionedStrategy.AddResourceMetaLabelAsync(
                    groupId,
                    resourceId,
                    GetString(input, 0) ?? string.Empty,
                    GetString(input, 1) ?? string.Empty,
                    OptionalEpoch(input, 2),
                    ct)
                .ConfigureAwait(false);
            await ReconcileProjectionAsync(ct).ConfigureAwait(false);
            return result;
        }

        private async ValueTask<ServiceResult> OnRemoveResourceMetaLabelAsync(
            string groupId,
            string resourceId,
            ISystemContext context,
            ArrayOf<Variant> input,
            CancellationToken ct)
        {
            ServiceResult access = m_context.CheckManagementAccess(context, "RemoveAttribute");
            if (ServiceResult.IsBad(access))
            {
                return access;
            }
            if (m_versionedStrategy is null)
            {
                return StatusCodes.BadNotSupported;
            }
            ServiceResult result = await m_versionedStrategy.RemoveResourceMetaLabelAsync(
                    groupId,
                    resourceId,
                    GetString(input, 0) ?? string.Empty,
                    OptionalEpoch(input, 1),
                    ct)
                .ConfigureAwait(false);
            await ReconcileProjectionAsync(ct).ConfigureAwait(false);
            return result;
        }

        private NodeId GroupNodeId(string groupId)
        {
            return new NodeId(GroupNodeIdPath(groupId), m_context.ModelNamespaceIndex);
        }

        private NodeId ResourceNodeId(
            string groupId,
            string resourceId,
            string versionId = "")
        {
            return new NodeId(
                ResourceNodeIdPath(groupId, resourceId, versionId),
                m_context.ModelNamespaceIndex);
        }

        private string GroupNodeIdPath(string groupId)
        {
            return $"{m_registryNodeIdPath}/groups/{groupId}";
        }

        private string ResourceNodeIdPath(
            string groupId,
            string resourceId,
            string versionId = "")
        {
            string path = $"{m_registryNodeIdPath}/groups/{groupId}/resources/{resourceId}";
            return m_versionedStrategy is null
                ? path
                : $"{path}/versions/{versionId}";
        }

        private string ResourceMetaNodeIdPath(
            string groupId,
            string resourceId,
            string versionId)
        {
            return $"{ResourceNodeIdPath(groupId, resourceId, versionId)}/meta";
        }

        private ResourceEntryKey ResourceKey(IXRegistryProjectionResource resource)
        {
            return new ResourceEntryKey(
                resource.ResourceId,
                m_versionedStrategy is null ? string.Empty : resource.VersionId);
        }

        private void WireMethod(
            BaseObjectState parent,
            string browseName,
            GenericMethodCalledEventHandler2Async handler)
        {
            ushort xRegistryNs = (ushort)m_context.NamespaceUris.GetIndex(XRegistryWellKnown.XRegistryNamespaceUri);
            MethodState? method =
                parent.FindChild(m_context.SystemContext, new QualifiedName(browseName, xRegistryNs)) as MethodState
                ?? parent.FindChild(
                    m_context.SystemContext,
                    new QualifiedName(browseName, m_context.ModelNamespaceIndex)) as MethodState;
            method?.OnCallMethod2Async = handler;
        }

        private static string? GetString(ArrayOf<Variant> input, int index)
        {
            return index < input.Count && input[index].AsBoxedObject(Variant.BoxingBehavior.Legacy) is string s
                ? s : null;
        }

        private static bool GetBool(ArrayOf<Variant> input, int index, bool fallback)
        {
            return index < input.Count && input[index].AsBoxedObject(Variant.BoxingBehavior.Legacy) is bool b
                ? b : fallback;
        }

        private static long? OptionalEpoch(ArrayOf<Variant> input, int index)
        {
            if (index >= input.Count)
            {
                return null;
            }
            return input[index].AsBoxedObject(Variant.BoxingBehavior.Legacy) switch
            {
                uint u => u == 0 ? null : u,
                int i => i == 0 ? null : i,
                long l => l == 0 ? null : l,
                _ => null
            };
        }

        private List<XRegistryEventChange> DiffEventSnapshots(
            XRegistryProjectionEventSnapshot previous,
            XRegistryProjectionEventSnapshot current)
        {
            var changes = new List<XRegistryEventChange>();
            NodeId registryNodeId = m_registryNode!.NodeId;
            if (!previous.Labels.SequenceEqual(current.Labels))
            {
                changes.Add(new XRegistryEventChange(
                    XRegistryEventKind.RegistryUpdated,
                    current.Xid,
                    registryNodeId,
                    current.Epoch,
                    Changed: ImmutableArray.Create("epoch", "labels", "modifiedat")));
            }

            Dictionary<string, XRegistryProjectionEventGroup> oldGroups =
                previous.Groups.ToDictionary(group => group.GroupId, StringComparer.Ordinal);
            Dictionary<string, XRegistryProjectionEventGroup> newGroups =
                current.Groups.ToDictionary(group => group.GroupId, StringComparer.Ordinal);

            foreach (XRegistryProjectionEventGroup oldGroup in previous.Groups
                .Where(group => !newGroups.ContainsKey(group.GroupId)))
            {
                foreach (XRegistryProjectionEventResource resource in oldGroup.Resources)
                {
                    foreach (XRegistryProjectionEventVersion version in resource.Versions)
                    {
                        changes.Add(new XRegistryEventChange(
                            XRegistryEventKind.VersionDeleted,
                            version.Xid,
                            VersionSourceNode(version, resource)));
                    }
                    changes.Add(new XRegistryEventChange(
                        XRegistryEventKind.ResourceDeleted,
                        resource.Xid,
                        ResourceSourceNode(resource)));
                }
                changes.Add(new XRegistryEventChange(
                    XRegistryEventKind.GroupDeleted,
                    oldGroup.Xid,
                    GroupSourceNode(oldGroup)));
                AddRegistryCollectionUpdated(changes, current);
            }

            foreach (XRegistryProjectionEventGroup newGroup in current.Groups)
            {
                if (!oldGroups.TryGetValue(newGroup.GroupId, out XRegistryProjectionEventGroup? oldGroup))
                {
                    changes.Add(new XRegistryEventChange(
                        XRegistryEventKind.GroupCreated,
                        newGroup.Xid,
                        GroupSourceNode(newGroup),
                        newGroup.Epoch));
                    AddRegistryCollectionUpdated(changes, current);
                    foreach (XRegistryProjectionEventResource resource in newGroup.Resources)
                    {
                        AddCreatedResource(changes, newGroup, resource);
                    }
                    continue;
                }
                DiffGroup(changes, oldGroup, newGroup);
            }
            return RouteEventChanges(changes, previous, current);
        }

        private void DiffGroup(
            List<XRegistryEventChange> changes,
            XRegistryProjectionEventGroup previous,
            XRegistryProjectionEventGroup current)
        {
            var groupChanged = new List<string>();
            if (!previous.Labels.SequenceEqual(current.Labels))
            {
                groupChanged.Add("labels");
            }
            if (previous.Epoch != current.Epoch)
            {
                groupChanged.Add("epoch");
                groupChanged.Add("modifiedat");
            }
            string? previousDeprecated = DeprecatedFingerprint(previous);
            string? currentDeprecated = DeprecatedFingerprint(current);
            if (!string.Equals(
                    previousDeprecated,
                    currentDeprecated,
                    StringComparison.Ordinal))
            {
                groupChanged.Add("deprecated");
                changes.Add(new XRegistryEventChange(
                    currentDeprecated is not null
                        ? XRegistryEventKind.GroupDeprecated
                        : XRegistryEventKind.GroupUndeprecated,
                    current.Xid,
                    GroupSourceNode(current)));
            }

            Dictionary<string, XRegistryProjectionEventResource> oldResources =
                previous.Resources.ToDictionary(resource => resource.ResourceId, StringComparer.Ordinal);
            Dictionary<string, XRegistryProjectionEventResource> newResources =
                current.Resources.ToDictionary(resource => resource.ResourceId, StringComparer.Ordinal);
            foreach (XRegistryProjectionEventResource oldResource in previous.Resources
                .Where(resource => !newResources.ContainsKey(resource.ResourceId)))
            {
                foreach (XRegistryProjectionEventVersion version in oldResource.Versions)
                {
                    changes.Add(new XRegistryEventChange(
                        XRegistryEventKind.VersionDeleted,
                        version.Xid,
                        VersionSourceNode(version, oldResource)));
                }
                changes.Add(new XRegistryEventChange(
                    XRegistryEventKind.ResourceDeleted,
                    oldResource.Xid,
                    ResourceSourceNode(oldResource)));
                AddCollectionChanged(groupChanged, m_eventOptions!.ResourcesAttributeName);
            }
            foreach (XRegistryProjectionEventResource newResource in current.Resources)
            {
                if (!oldResources.TryGetValue(
                        newResource.ResourceId,
                        out XRegistryProjectionEventResource? oldResource))
                {
                    AddCreatedResource(changes, current, newResource);
                    AddCollectionChanged(groupChanged, m_eventOptions!.ResourcesAttributeName);
                }
                else
                {
                    DiffResource(changes, oldResource, newResource);
                }
            }
            if (groupChanged.Count > 0)
            {
                changes.Add(new XRegistryEventChange(
                    XRegistryEventKind.GroupUpdated,
                    current.Xid,
                    GroupSourceNode(current),
                    current.Epoch,
                    Changed: groupChanged.ToImmutableArray()));
            }
        }

        private void AddCreatedResource(
            List<XRegistryEventChange> changes,
            XRegistryProjectionEventGroup group,
            XRegistryProjectionEventResource resource)
        {
            changes.Add(new XRegistryEventChange(
                XRegistryEventKind.ResourceCreated,
                resource.Xid,
                ResourceSourceNode(resource),
                resource.Epoch,
                resource.MetaEpoch));
            foreach (XRegistryProjectionEventVersion version in resource.Versions)
            {
                changes.Add(new XRegistryEventChange(
                    XRegistryEventKind.VersionCreated,
                    version.Xid,
                    VersionSourceNode(version, resource),
                    version.Epoch));
            }
        }

        private void DiffResource(
            List<XRegistryEventChange> changes,
            XRegistryProjectionEventResource previous,
            XRegistryProjectionEventResource current)
        {
            var resourceChanged = new List<string>();
            if (!previous.Labels.SequenceEqual(current.Labels))
            {
                resourceChanged.Add("meta.labels");
            }
            if (previous.MetaEpoch != current.MetaEpoch)
            {
                resourceChanged.Add("meta.epoch");
                resourceChanged.Add("meta.modifiedat");
            }
            else if (previous.MetaModifiedAt != current.MetaModifiedAt)
            {
                resourceChanged.Add("meta.modifiedat");
            }
            string? previousDeprecated = DeprecatedFingerprint(previous);
            string? currentDeprecated = DeprecatedFingerprint(current);
            if (!string.Equals(
                    previousDeprecated,
                    currentDeprecated,
                    StringComparison.Ordinal))
            {
                resourceChanged.Add("meta.deprecated");
                changes.Add(new XRegistryEventChange(
                    currentDeprecated is not null
                        ? XRegistryEventKind.ResourceDeprecated
                        : XRegistryEventKind.ResourceUndeprecated,
                    current.Xid,
                    ResourceSourceNode(current)));
            }

            Dictionary<string, XRegistryProjectionEventVersion> oldVersions =
                previous.Versions.ToDictionary(version => version.VersionId, StringComparer.Ordinal);
            Dictionary<string, XRegistryProjectionEventVersion> newVersions =
                current.Versions.ToDictionary(version => version.VersionId, StringComparer.Ordinal);
            foreach (XRegistryProjectionEventVersion oldVersion in previous.Versions
                .Where(version => !newVersions.ContainsKey(version.VersionId)))
            {
                changes.Add(new XRegistryEventChange(
                    XRegistryEventKind.VersionDeleted,
                    oldVersion.Xid,
                    VersionSourceNode(oldVersion, previous)));
                AddVersionCollectionChanged(resourceChanged);
            }
            foreach (XRegistryProjectionEventVersion newVersion in current.Versions)
            {
                if (!oldVersions.TryGetValue(
                        newVersion.VersionId,
                        out XRegistryProjectionEventVersion? oldVersion))
                {
                    changes.Add(new XRegistryEventChange(
                        XRegistryEventKind.VersionCreated,
                        newVersion.Xid,
                        VersionSourceNode(newVersion, current),
                        newVersion.Epoch));
                    AddVersionCollectionChanged(resourceChanged);
                    continue;
                }
                List<string> versionChanged = ChangedKeys(
                    oldVersion.Attributes,
                    newVersion.Attributes);
                if (!oldVersion.Labels.SequenceEqual(newVersion.Labels))
                {
                    versionChanged.Add("labels");
                }
                if (oldVersion.Epoch != newVersion.Epoch)
                {
                    versionChanged.Add("epoch");
                    versionChanged.Add("modifiedat");
                }
                else if (oldVersion.ModifiedAt != newVersion.ModifiedAt)
                {
                    versionChanged.Add("modifiedat");
                }
                if (versionChanged.Count > 0)
                {
                    changes.Add(new XRegistryEventChange(
                        XRegistryEventKind.VersionUpdated,
                        newVersion.Xid,
                        VersionSourceNode(newVersion, current),
                        newVersion.Epoch,
                        Changed: versionChanged.ToImmutableArray()));
                    if (string.Equals(
                            current.DefaultVersionId,
                            newVersion.VersionId,
                            StringComparison.Ordinal))
                    {
                        resourceChanged.AddRange(versionChanged);
                    }
                }
            }

            if (!string.Equals(
                    previous.DefaultVersionId,
                    current.DefaultVersionId,
                    StringComparison.Ordinal))
            {
                resourceChanged.Add("meta.defaultversionid");
                AddDefaultVersionAttributes(resourceChanged, previous, previous.DefaultVersionId);
                AddDefaultVersionAttributes(resourceChanged, current, current.DefaultVersionId);
            }

            if (resourceChanged.Count > 0)
            {
                changes.Add(new XRegistryEventChange(
                    XRegistryEventKind.ResourceUpdated,
                    current.Xid,
                    ResourceSourceNode(current),
                    current.Epoch,
                    current.MetaEpoch,
                    resourceChanged.ToImmutableArray()));
            }
        }

        private void AddRegistryCollectionUpdated(
            List<XRegistryEventChange> changes,
            XRegistryProjectionEventSnapshot current)
        {
            string attribute = m_eventOptions!.GroupsAttributeName;
            changes.Add(new XRegistryEventChange(
                XRegistryEventKind.RegistryUpdated,
                current.Xid,
                m_registryNode!.NodeId,
                current.Epoch,
                Changed: ImmutableArray.Create(
                    attribute,
                    attribute + "count",
                    "epoch",
                    "modifiedat")));
        }

        private List<XRegistryEventChange> RouteEventChanges(
            List<XRegistryEventChange> changes,
            XRegistryProjectionEventSnapshot previous,
            XRegistryProjectionEventSnapshot current)
        {
            for (int index = 0; index < changes.Count; index++)
            {
                XRegistryEventChange change = changes[index];
                NodeState notifier = ResolveEventNotifier(change, previous, current);
                changes[index] = change with
                {
                    SourceName = ResolveSourceName(change.Subject, previous, current),
                    Notifier = notifier
                };
            }
            return changes;
        }

        private NodeState ResolveEventNotifier(
            XRegistryEventChange change,
            XRegistryProjectionEventSnapshot previous,
            XRegistryProjectionEventSnapshot current)
        {
            if (change.Kind == XRegistryEventKind.GroupDeleted)
            {
                return m_registryNode!;
            }
            if (change.Kind == XRegistryEventKind.ResourceDeleted)
            {
                XRegistryProjectionEventResource? oldResource =
                    FindResourceBySubject(previous, change.Subject);
                if (oldResource is not null &&
                    m_groups.TryGetValue(oldResource.GroupId, out GroupEntry? group))
                {
                    return group.Node;
                }
                return m_registryNode!;
            }
            if (change.Kind == XRegistryEventKind.VersionDeleted)
            {
                XRegistryProjectionEventResource? oldResource =
                    FindVersionOwner(previous, change.Subject);
                if (oldResource is not null)
                {
                    XRegistryProjectionEventResource? currentResource =
                        FindResourceBySubject(current, oldResource.Xid);
                    if (currentResource is not null &&
                        m_groups.TryGetValue(
                            currentResource.GroupId,
                            out GroupEntry? currentGroup) &&
                        FindResourceEntry(
                            currentGroup,
                            currentResource.ResourceId,
                            currentResource.DefaultVersionId) is { } resource)
                    {
                        return resource.Node;
                    }
                    if (m_groups.TryGetValue(oldResource.GroupId, out GroupEntry? survivingGroup))
                    {
                        return survivingGroup.Node;
                    }
                }
                return m_registryNode!;
            }

            NodeState? live = FindLiveNode(change.SourceNodeId);
            if (live is not null)
            {
                return live;
            }
            XRegistryProjectionEventResource? owner =
                FindVersionOwner(current, change.Subject) ??
                FindResourceBySubject(current, change.Subject);
            if (owner is not null &&
                m_groups.TryGetValue(owner.GroupId, out GroupEntry? groupEntry) &&
                FindResourceEntry(
                    groupEntry,
                    owner.ResourceId,
                    owner.DefaultVersionId) is { } resourceEntry)
            {
                return resourceEntry.Node;
            }
            return m_registryNode!;
        }

        private ResourceEntry? FindResourceEntry(
            GroupEntry group,
            string resourceId,
            string? versionId)
        {
            if (m_versionedStrategy is null)
            {
                group.Resources.TryGetValue(
                    new ResourceEntryKey(resourceId, string.Empty),
                    out ResourceEntry? resource);
                return resource;
            }
            if (!string.IsNullOrEmpty(versionId) &&
                group.Resources.TryGetValue(
                    new ResourceEntryKey(resourceId, versionId),
                    out ResourceEntry? version))
            {
                return version;
            }
            return group.Resources.Values.FirstOrDefault(candidate =>
                string.Equals(candidate.ResourceId, resourceId, StringComparison.Ordinal));
        }

        private NodeState? FindLiveNode(NodeId nodeId)
        {
            if (nodeId.IsNull)
            {
                return null;
            }
            if (m_registryNode?.NodeId == nodeId)
            {
                return m_registryNode;
            }
            foreach (GroupEntry group in m_groups.Values)
            {
                if (group.Node.NodeId == nodeId)
                {
                    return group.Node;
                }
                foreach (ResourceEntry resource in group.Resources.Values)
                {
                    if (resource.Node.NodeId == nodeId)
                    {
                        return resource.Node;
                    }
                }
            }
            return null;
        }

        private static string ResolveSourceName(
            string subject,
            XRegistryProjectionEventSnapshot previous,
            XRegistryProjectionEventSnapshot current)
        {
            return FindSourceName(current, subject) ??
                FindSourceName(previous, subject) ??
                subject;
        }

        private static string? FindSourceName(
            XRegistryProjectionEventSnapshot snapshot,
            string subject)
        {
            foreach (XRegistryProjectionEventGroup group in snapshot.Groups)
            {
                if (string.Equals(group.Xid, subject, StringComparison.Ordinal))
                {
                    return group.SourceName ?? group.GroupId;
                }
                foreach (XRegistryProjectionEventResource resource in group.Resources)
                {
                    if (string.Equals(resource.Xid, subject, StringComparison.Ordinal))
                    {
                        return resource.SourceName ?? resource.ResourceId;
                    }
                    foreach (XRegistryProjectionEventVersion version in resource.Versions)
                    {
                        if (string.Equals(version.Xid, subject, StringComparison.Ordinal))
                        {
                            return version.SourceName ?? version.VersionId;
                        }
                    }
                }
            }
            return null;
        }

        private static XRegistryProjectionEventResource? FindResourceBySubject(
            XRegistryProjectionEventSnapshot snapshot,
            string subject)
        {
            return snapshot.Groups
                .SelectMany(group => group.Resources)
                .FirstOrDefault(resource =>
                    string.Equals(resource.Xid, subject, StringComparison.Ordinal));
        }

        private static XRegistryProjectionEventResource? FindVersionOwner(
            XRegistryProjectionEventSnapshot snapshot,
            string subject)
        {
            return snapshot.Groups
                .SelectMany(group => group.Resources)
                .FirstOrDefault(resource => resource.Versions.Any(version =>
                    string.Equals(version.Xid, subject, StringComparison.Ordinal)));
        }

        private NodeId GroupSourceNode(XRegistryProjectionEventGroup group)
        {
            return group.SourceNodeId.IsNull
                ? GroupNodeId(group.GroupId)
                : group.SourceNodeId;
        }

        private NodeId ResourceSourceNode(XRegistryProjectionEventResource resource)
        {
            return resource.SourceNodeId.IsNull
                ? ResourceNodeId(
                    resource.GroupId,
                    resource.ResourceId,
                    resource.DefaultVersionId ?? string.Empty)
                : resource.SourceNodeId;
        }

        private NodeId VersionSourceNode(
            XRegistryProjectionEventVersion version,
            XRegistryProjectionEventResource resource)
        {
            return version.SourceNodeId.IsNull
                ? ResourceNodeId(
                    resource.GroupId,
                    resource.ResourceId,
                    version.VersionId)
                : version.SourceNodeId;
        }

        private static List<string> ChangedKeys(
            ImmutableSortedDictionary<string, string> previous,
            ImmutableSortedDictionary<string, string> current)
        {
            return previous.Keys.Concat(current.Keys)
                .Distinct(StringComparer.Ordinal)
                .Where(key =>
                    !previous.TryGetValue(key, out string? oldValue) ||
                    !current.TryGetValue(key, out string? newValue) ||
                    !string.Equals(oldValue, newValue, StringComparison.Ordinal))
                .ToList();
        }

        private static string? DeprecatedFingerprint(XRegistryProjectionEventGroup group)
        {
            return CanonicalDeprecatedFingerprint(group.Deprecation) ??
                (group.Deprecated ? "true" : null);
        }

        private static string? DeprecatedFingerprint(
            XRegistryProjectionEventResource resource)
        {
            return CanonicalDeprecatedFingerprint(resource.Deprecation) ??
                (resource.Deprecated ? "true" : null);
        }

        private static string? CanonicalDeprecatedFingerprint(
            XRegistryProjectionDeprecation? deprecation)
        {
            if (deprecation is null)
            {
                return null;
            }
            return string.Concat(
                deprecation.CanonicalValue,
                "\u001f",
                string.Join(
                    "\u001e",
                    deprecation.Details.Select(pair => $"{pair.Key}\u001d{pair.Value}")));
        }

        private static void AddCollectionChanged(List<string> changed, string attribute)
        {
            changed.Add(attribute);
            changed.Add(attribute + "count");
            changed.Add("epoch");
            changed.Add("modifiedat");
        }

        private static void AddVersionCollectionChanged(List<string> changed)
        {
            changed.Add("meta.epoch");
            changed.Add("meta.modifiedat");
            changed.Add("versions");
            changed.Add("versionscount");
        }

        private static void AddDefaultVersionAttributes(
            List<string> changed,
            XRegistryProjectionEventResource resource,
            string? versionId)
        {
            XRegistryProjectionEventVersion? version = resource.Versions
                .FirstOrDefault(candidate =>
                    string.Equals(candidate.VersionId, versionId, StringComparison.Ordinal));
            if (version is not null)
            {
                changed.Add("versionid");
                changed.AddRange(version.Attributes.Keys);
            }
        }

        private sealed class GroupEntry
        {
            public GroupEntry(GroupState node)
            {
                Node = node;
            }

            public GroupState Node { get; }

            public Dictionary<ResourceEntryKey, ResourceEntry> Resources { get; } = [];
        }

        private readonly record struct ResourceEntryKey(string ResourceId, string VersionId);

        private sealed class ResourceEntry
        {
            public ResourceEntry(
                ResourceState node,
                IXRegistryProjectedResourceFile? file,
                string groupId,
                string resourceId,
                string versionId,
                string xid)
            {
                Node = node;
                File = file;
                GroupId = groupId;
                ResourceId = resourceId;
                VersionId = versionId;
                Xid = xid;
            }

            public ResourceState Node { get; }
            public IXRegistryProjectedResourceFile? File { get; }
            public string GroupId { get; }
            public string ResourceId { get; }
            public string VersionId { get; }
            public string Xid { get; }
        }

        private readonly XRegistryProjectionContext m_context;
        private readonly IXRegistryProjectionStrategy m_strategy;
        private readonly IXRegistryVersionedProjectionStrategy? m_versionedStrategy;
        private readonly IXRegistryProjectionGenerationProvider? m_generationProvider;
        private readonly XRegistryServerOptions? m_eventOptions;
        private readonly string m_registryNodeIdPath;
        private readonly SemaphoreSlim m_gate = new(1, 1);
        private readonly Dictionary<string, GroupEntry> m_groups = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, ResourceState> m_resourcesByXid = new(StringComparer.Ordinal);
        private readonly HashSet<ulong> m_reportedTransitions = [];
        private readonly Queue<ulong> m_reportedTransitionOrder = [];
        private BaseObjectState? m_registryNode;
        private XRegistryEventEmitter? m_eventEmitter;
        private XRegistryProjectionEventSnapshot? m_previousEventSnapshot;
        private long m_latestProjectionSequence = -1;
        private bool m_disposed;
    }
}
