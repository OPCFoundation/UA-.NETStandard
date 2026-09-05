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
                foreach (LogicalResourceEntry logical in group.LogicalResources.Values)
                {
                    foreach (ResourceEntry version in logical.Versions.Values)
                    {
                        version.File?.Dispose();
                    }
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
            if (m_versionedStrategy is not null)
            {
                await ReconcileVersionedResourcesAsync(entry, group, eventSnapshot, ct)
                    .ConfigureAwait(false);
                return;
            }

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

        private async ValueTask ReconcileVersionedResourcesAsync(
            GroupEntry entry,
            IXRegistryProjectionGroup group,
            XRegistryProjectionEventSnapshot? eventSnapshot,
            CancellationToken ct)
        {
            // Group the snapshot resources by ResourceId.
            var versionsByResource = new Dictionary<string, List<IXRegistryProjectionResource>>(
                StringComparer.Ordinal);
            foreach (IXRegistryProjectionResource resource in group.Resources)
            {
                if (!versionsByResource.TryGetValue(
                        resource.ResourceId,
                        out List<IXRegistryProjectionResource>? list))
                {
                    list = [];
                    versionsByResource[resource.ResourceId] = list;
                }
                list.Add(resource);
            }

            var seenResourceIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, List<IXRegistryProjectionResource>> pair in versionsByResource)
            {
                string resourceId = pair.Key;
                List<IXRegistryProjectionResource> versions = pair.Value;
                seenResourceIds.Add(resourceId);

                // Find the default version for logical resource property delegation.
                IXRegistryProjectionResource defaultVersion = versions
                    .OfType<IXRegistryProjectionResourceMeta>()
                    .FirstOrDefault(m => m.IsDefaultVersion) as IXRegistryProjectionResource
                    ?? versions[0];

                if (!entry.LogicalResources.TryGetValue(
                        resourceId,
                        out LogicalResourceEntry? logical))
                {
                    logical = await CreateLogicalResourceNodeAsync(
                        entry,
                        defaultVersion,
                        eventSnapshot,
                        ct).ConfigureAwait(false);
                    entry.LogicalResources[resourceId] = logical;
                }
                else
                {
                    ApplyLogicalResourceProperties(logical, defaultVersion, eventSnapshot);
                    m_strategy.ConfigureResourceNode(logical.LogicalNode, defaultVersion);
                    if (logical.LogicalNode.MetaLabels is not null &&
                        defaultVersion is IXRegistryProjectionResourceMeta meta)
                    {
                        await SyncLabelPropertiesAsync(
                            logical.LogicalNode.MetaLabels,
                            LogicalResourceMetaNodeIdPath(
                                defaultVersion.GroupId,
                                defaultVersion.ResourceId),
                            meta.MetaLabels,
                            ct).ConfigureAwait(false);
                    }
                    logical.LogicalNode.ClearChangeMasks(
                        m_context.SystemContext,
                        includeChildren: true);
                }

                // Reconcile the per-version nodes under the Versions folder.
                var seenVersions = new HashSet<string>(StringComparer.Ordinal);
                foreach (IXRegistryProjectionResource version in versions)
                {
                    seenVersions.Add(version.VersionId);
                    if (!logical.Versions.TryGetValue(
                            version.VersionId,
                            out ResourceEntry? versionEntry))
                    {
                        versionEntry = await CreateVersionNodeAsync(
                            logical,
                            version,
                            eventSnapshot,
                            ct).ConfigureAwait(false);
                        logical.Versions[version.VersionId] = versionEntry;
                    }
                    else
                    {
                        ApplyVersionProperties(versionEntry, version, eventSnapshot);
                        m_strategy.ConfigureResourceNode(versionEntry.Node, version);
                        if (versionEntry.Node.Labels is not null)
                        {
                            await SyncLabelPropertiesAsync(
                                versionEntry.Node.Labels,
                                VersionNodeIdPath(
                                    version.GroupId,
                                    version.ResourceId,
                                    version.VersionId),
                                version.Labels,
                                ct).ConfigureAwait(false);
                        }
                        versionEntry.Node.ClearChangeMasks(
                            m_context.SystemContext,
                            includeChildren: true);
                    }
                }

                // Remove stale version nodes.
                foreach (string staleVersion in logical.Versions.Keys
                    .Where(v => !seenVersions.Contains(v)).ToList())
                {
                    await RemoveVersionNodeAsync(logical, staleVersion, ct)
                        .ConfigureAwait(false);
                }

                // Delegate the logical Resource's (non-Meta) Labels and inherited
                // FileType Properties to the currently selected default Version, now
                // that its node exists/was updated in the loop above. Runs for both a
                // brand-new logical Resource and an existing one being refreshed.
                if (logical.Versions.TryGetValue(
                        defaultVersion.VersionId,
                        out ResourceEntry? defaultVersionEntry))
                {
                    if (logical.LogicalNode.Labels is not null)
                    {
                        await SyncLabelPropertiesAsync(
                            logical.LogicalNode.Labels,
                            LogicalResourceNodeIdPath(
                                defaultVersion.GroupId,
                                defaultVersion.ResourceId),
                            defaultVersion.Labels,
                            ct).ConfigureAwait(false);
                    }
                    MirrorFileTypeProperties(logical.LogicalNode, defaultVersionEntry.Node);
                    logical.LogicalNode.ClearChangeMasks(
                        m_context.SystemContext,
                        includeChildren: true);
                }
            }

            // Remove stale logical resources.
            foreach (string staleResourceId in entry.LogicalResources.Keys
                .Where(id => !seenResourceIds.Contains(id)).ToList())
            {
                await RemoveLogicalResourceNodeAsync(entry, staleResourceId, ct)
                    .ConfigureAwait(false);
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
            foreach (string logicalResourceId in entry.LogicalResources.Keys.ToList())
            {
                await RemoveLogicalResourceNodeAsync(entry, logicalResourceId, ct)
                    .ConfigureAwait(false);
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
                    node,
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
                if (meta.IsDefaultVersion)
                {
                    m_resourcesByXid[
                        ResourceSubject(resource.GroupId, resource.ResourceId)] = entry.Node;
                    if (FindEventResource(
                            eventSnapshot,
                            resource.GroupId,
                            resource.ResourceId) is { } logicalResource)
                    {
                        m_resourcesByXid[logicalResource.Xid] = entry.Node;
                    }
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

        private async ValueTask<LogicalResourceEntry> CreateLogicalResourceNodeAsync(
            GroupEntry group,
            IXRegistryProjectionResource defaultVersion,
            XRegistryProjectionEventSnapshot? eventSnapshot,
            CancellationToken ct)
        {
            string groupId = defaultVersion.GroupId;
            string resourceId = defaultVersion.ResourceId;

            // Create the logical Resource node — child of the Group.
            ResourceState node = m_strategy.CreateResourceNode(group.Node, defaultVersion);
            NodeId logicalNodeId = LogicalResourceNodeId(groupId, resourceId);
            node.ReferenceTypeId = Opc.Ua.ReferenceTypeIds.Organizes;
            node.Create(
                m_context.SystemContext,
                logicalNodeId,
                new QualifiedName(resourceId, m_context.ModelNamespaceIndex),
                new LocalizedText(defaultVersion.Name),
                assignNodeIds: false);

            // Logical Resource carries Meta-prefixed members, stable Xid, Delete and Labels.
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

            // Delete on the logical Resource always uses Resource-delete semantics.
            node.Delete?.OnCallMethod2Async =
                (c, m, o, i, ot, t) => OnDeleteLogicalResourceAsync(
                    groupId, resourceId, c, i, t);

            WireLabelsContainer(
                node.MetaLabels,
                (c, i, t) => OnAddResourceMetaLabelAsync(groupId, resourceId, c, i, t),
                (c, i, t) => OnRemoveResourceMetaLabelAsync(groupId, resourceId, c, i, t));

            // The logical Resource's (non-Meta) Labels represent "the represented
            // Version's attributes" (per ResourceType), so a mutation is delegated to
            // whichever Version is currently the resolved default — resolved dynamically
            // at call time, exactly like the FileType forwarding above, since the default
            // can change over the node's lifetime. The Property children themselves are
            // kept in sync with the default Version's labels on every reconciliation pass
            // (see ReconcileVersionedResourcesAsync).
            WireLabelsContainer(
                node.Labels,
                (c, i, t) => OnAddResourceLabelAsync(
                    groupId, resourceId, node.VersionId?.Value ?? string.Empty, c, i, t),
                (c, i, t) => OnRemoveResourceLabelAsync(
                    groupId, resourceId, node.VersionId?.Value ?? string.Empty, c, i, t));

            // Create the Versions folder — child of the logical Resource.
            node.AddVersions(m_context.SystemContext);
            ResourceVersionsState versionsFolder = node.Versions!;
            NodeId versionsNodeId = VersionsFolderNodeId(groupId, resourceId);
            versionsFolder.NodeId = versionsNodeId;
            versionsFolder.BrowseName = new QualifiedName(
                "Versions", m_context.ModelNamespaceIndex);
            versionsFolder.ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasComponent;

            var logical = new LogicalResourceEntry(node, versionsFolder, groupId, resourceId);
            ApplyLogicalResourceProperties(logical, defaultVersion, eventSnapshot);
            m_strategy.ConfigureResourceNode(node, defaultVersion);
            m_context.SystemContext.AssignInstanceChildNodeIds(node);
            LinkMethodArguments(node, m_context.SystemContext);

            // Wire into the group.
            group.Node.AddChild(node);
            group.Node.AddReference(Opc.Ua.ReferenceTypeIds.HasNotifier, false, logicalNodeId);
            node.AddReference(Opc.Ua.ReferenceTypeIds.HasNotifier, true, group.Node.NodeId);

            await m_context.AddNodeAsync(node, ct).ConfigureAwait(false);

            // Register the logical resource in the xid index.
            m_resourcesByXid[ResourceSubject(groupId, resourceId)] = node;
            if (FindEventResource(eventSnapshot, groupId, resourceId) is { } logicalResource)
            {
                m_resourcesByXid[logicalResource.Xid] = node;
            }

            if (node.MetaLabels is not null &&
                defaultVersion is IXRegistryProjectionResourceMeta meta)
            {
                await SyncLabelPropertiesAsync(
                    node.MetaLabels,
                    LogicalResourceMetaNodeIdPath(groupId, resourceId),
                    meta.MetaLabels,
                    ct).ConfigureAwait(false);
            }

            // Wire the logical Resource's inherited FileType methods to forward through the
            // resolved default Version's file manager, pinning the Version at Open time.
            WireLogicalResourceFileForwarding(logical);

            return logical;
        }

        /// <summary>
        /// Wires the logical Resource's inherited FileType Open/Read/Write/Close/GetPosition/
        /// SetPosition methods to forward through the resolved default Version's file manager.
        /// The Version is pinned at Open time: once a handle is opened, switching the default
        /// Version does not redirect that handle.
        /// </summary>
        private void WireLogicalResourceFileForwarding(LogicalResourceEntry logical)
        {
            ResourceState node = logical.LogicalNode;

            if (node.Open is not null)
            {
                node.Open.OnCall = new OpenMethodStateMethodCallHandler(
                    (ISystemContext context, MethodState method, NodeId objectId,
                     byte mode, ref uint fileHandle) =>
                    {
                        // Resolve the current default Version by reading VersionId.
                        // Versions is a ConcurrentDictionary: this read happens on an
                        // OPC UA method-dispatch thread, outside m_gate, concurrently
                        // with reconciliation writes under that gate.
                        string? defaultVersionId = node.VersionId?.Value;
                        if (string.IsNullOrEmpty(defaultVersionId) ||
                            !logical.Versions.TryGetValue(defaultVersionId, out ResourceEntry? vEntry) ||
                            vEntry.File is null)
                        {
                            return StatusCodes.BadNotSupported;
                        }

                        if (vEntry.File is not IXRegistryProjectedResourceFileHandleForwarder forwarder)
                        {
                            return StatusCodes.BadNotSupported;
                        }

                        uint underlyingHandle = 0;
                        ServiceResult result = forwarder.ForwardOpen(
                            context, method, objectId, mode, ref underlyingHandle);
                        if (ServiceResult.IsGood(result))
                        {
                            // Allocate an engine-owned synthetic handle: every Version's
                            // own file manager numbers its underlying handles
                            // independently starting from 1, so two different Versions
                            // opened through the logical Resource (e.g. across a default
                            // switch) can otherwise produce the same underlying handle
                            // number. Keying PinnedHandles by that raw number alone would
                            // let a later Open silently overwrite an earlier pin for a
                            // different Version, misrouting/closing the wrong one.
                            uint syntheticHandle = logical.AllocatePinnedHandle();
                            logical.PinnedHandles[syntheticHandle] = new PinnedFileHandle(
                                forwarder, vEntry.Node, underlyingHandle, SessionIdOf(context));
                            fileHandle = syntheticHandle;

                            // Mirror the resolved Version's FileType Properties (Size,
                            // OpenCount, ...) onto the logical Resource promptly, rather
                            // than waiting for the next reconciliation pass.
                            MirrorFileTypeProperties(node, vEntry.Node);
                            node.ClearChangeMasks(m_context.SystemContext, includeChildren: true);
                        }
                        return result;
                    });
            }

            if (node.Close is not null)
            {
                node.Close.OnCallAsync = new CloseMethodStateMethodAsyncCallHandler(
                    async (ISystemContext context, MethodState method, NodeId objectId,
                           uint fileHandle, CancellationToken ct) =>
                    {
                        // Peek only: do not remove the pin until we know either (a) this
                        // is not the owning session, in which case the pin must survive
                        // for the rightful owner's later Close, or (b) the underlying
                        // manager has actually been given the chance to consume the
                        // handle. Removing eagerly here previously let a different
                        // session guess/replay another session's synthetic handle,
                        // strip the pin, and receive the underlying manager's
                        // BadUserAccessDenied — after which the underlying writer
                        // reservation was still held (not released) but the rightful
                        // owner's own later Close could no longer find its pin at this
                        // layer, stranding the handle and its writer slot forever.
                        if (!logical.PinnedHandles.TryGetValue(fileHandle, out PinnedFileHandle pinned))
                        {
                            return new CloseMethodStateResult
                            {
                                ServiceResult = ServiceResult.Create(
                                    StatusCodes.BadInvalidArgument, "Unknown file handle.")
                            };
                        }

                        NodeId callerSessionId = SessionIdOf(context);
                        if (!pinned.SessionId.IsNull &&
                            !callerSessionId.IsNull &&
                            pinned.SessionId != callerSessionId)
                        {
                            // Reject outright without forwarding to the underlying
                            // manager and without removing the pin, so the rightful
                            // owner can still close (and release the writer slot) later.
                            return new CloseMethodStateResult
                            {
                                ServiceResult = ServiceResult.Create(
                                    StatusCodes.BadUserAccessDenied,
                                    "File handle is owned by another session.")
                            };
                        }

                        // Ownership confirmed (or no session context on either side, e.g.
                        // an in-process call): the underlying manager will now either
                        // release the handle on success, or on any failure path reached
                        // past its own session check (unknown handle, commit-authorization
                        // failure, stale content, ...), all of which also remove it from
                        // the underlying manager's own handle table. It is therefore safe
                        // to remove our pin unconditionally after forwarding.
                        ServiceResult result = await pinned.Forwarder.ForwardCloseAsync(
                            context, method, objectId, pinned.UnderlyingHandle, ct)
                            .ConfigureAwait(false);
                        logical.PinnedHandles.TryRemove(fileHandle, out _);

                        // Mirror the pinned Version's FileType Properties (OpenCount,
                        // Size after a commit, ...) onto the logical Resource promptly.
                        MirrorFileTypeProperties(node, pinned.VersionNode);
                        node.ClearChangeMasks(m_context.SystemContext, includeChildren: true);
                        return new CloseMethodStateResult { ServiceResult = result };
                    });
            }

            if (node.Read is not null)
            {
                node.Read.OnCallAsync = new ReadMethodStateMethodAsyncCallHandler(
                    async (ISystemContext context, MethodState method, NodeId objectId,
                           uint fileHandle, int length, CancellationToken ct) =>
                    {
                        if (!logical.PinnedHandles.TryGetValue(fileHandle, out PinnedFileHandle pinned))
                        {
                            return new ReadMethodStateResult
                            {
                                ServiceResult = ServiceResult.Create(
                                    StatusCodes.BadInvalidArgument, "Unknown file handle.")
                            };
                        }
                        (ServiceResult status, ByteString data) = await pinned.Forwarder.ForwardReadAsync(
                            context, method, objectId, pinned.UnderlyingHandle, length, ct)
                            .ConfigureAwait(false);
                        return new ReadMethodStateResult { ServiceResult = status, Data = data };
                    });
            }

            if (node.Write is not null)
            {
                node.Write.OnCall = new WriteMethodStateMethodCallHandler(
                    (ISystemContext context, MethodState method, NodeId objectId,
                     uint fileHandle, ByteString data) =>
                    {
                        if (!logical.PinnedHandles.TryGetValue(fileHandle, out PinnedFileHandle pinned))
                        {
                            return ServiceResult.Create(
                                StatusCodes.BadInvalidArgument, "Unknown file handle.");
                        }
                        return pinned.Forwarder.ForwardWrite(
                            context, method, objectId, pinned.UnderlyingHandle, data);
                    });
            }

            if (node.GetPosition is not null)
            {
                node.GetPosition.OnCall = new GetPositionMethodStateMethodCallHandler(
                    (ISystemContext context, MethodState method, NodeId objectId,
                     uint fileHandle, ref ulong position) =>
                    {
                        if (!logical.PinnedHandles.TryGetValue(fileHandle, out PinnedFileHandle pinned))
                        {
                            return ServiceResult.Create(
                                StatusCodes.BadInvalidArgument, "Unknown file handle.");
                        }
                        return pinned.Forwarder.ForwardGetPosition(
                            context, method, objectId, pinned.UnderlyingHandle, ref position);
                    });
            }

            if (node.SetPosition is not null)
            {
                node.SetPosition.OnCall = new SetPositionMethodStateMethodCallHandler(
                    (ISystemContext context, MethodState method, NodeId objectId,
                     uint fileHandle, ulong position) =>
                    {
                        if (!logical.PinnedHandles.TryGetValue(fileHandle, out PinnedFileHandle pinned))
                        {
                            return ServiceResult.Create(
                                StatusCodes.BadInvalidArgument, "Unknown file handle.");
                        }
                        return pinned.Forwarder.ForwardSetPosition(
                            context, method, objectId, pinned.UnderlyingHandle, position);
                    });
            }
        }

        /// <summary>
        /// Mirrors the inherited FileType Properties (Size, Writable, UserWritable,
        /// OpenCount, MimeType, LastModifiedTime, MaxByteStringLength) from the exact
        /// Version node currently represented by a logical Resource onto that logical
        /// Resource's own node, so a client reading these Properties directly on the
        /// logical Resource observes the represented Version's file state instead of
        /// stale or default values.
        /// </summary>
        /// <summary>
        /// Gets the session a call arrived on, or a null NodeId for an in-process
        /// call or a context without session information.
        /// </summary>
        private static NodeId SessionIdOf(ISystemContext? context)
        {
            return context is ISessionSystemContext { SessionId: { IsNull: false } sessionId }
                ? sessionId
                : NodeId.Null;
        }

        private static void MirrorFileTypeProperties(ResourceState target, ResourceState source)
        {
            if (source.Size is not null)
            {
                SetValue(target.Size, source.Size.Value);
            }
            if (source.Writable is not null)
            {
                SetValue(target.Writable, source.Writable.Value);
            }
            if (source.UserWritable is not null)
            {
                SetValue(target.UserWritable, source.UserWritable.Value);
            }
            if (source.OpenCount is not null)
            {
                SetValue(target.OpenCount, source.OpenCount.Value);
            }
            if (source.MimeType is not null)
            {
                SetValue(target.MimeType, source.MimeType.Value);
            }
            if (source.LastModifiedTime is not null)
            {
                SetValue(target.LastModifiedTime, source.LastModifiedTime.Value);
            }
            if (source.MaxByteStringLength is not null)
            {
                SetValue(target.MaxByteStringLength, source.MaxByteStringLength.Value);
            }
        }

        private async ValueTask<ResourceEntry> CreateVersionNodeAsync(
            LogicalResourceEntry logical,
            IXRegistryProjectionResource version,
            XRegistryProjectionEventSnapshot? eventSnapshot,
            CancellationToken ct)
        {
            string groupId = version.GroupId;
            string resourceId = version.ResourceId;
            string versionId = version.VersionId;

            // The strategy uses the group node to determine the domain type (TD/TM).
            // We find the group entry via m_groups, then pass its GroupState.
            GroupState groupNode = m_groups.TryGetValue(groupId, out GroupEntry? ge)
                ? ge.Node
                : logical.LogicalNode.Parent as GroupState ?? new GroupState(null);
            ResourceState node = m_strategy.CreateResourceNode(groupNode, version);
            NodeId versionNodeId = VersionNodeId(groupId, resourceId, versionId);
            node.ReferenceTypeId = Opc.Ua.ReferenceTypeIds.Organizes;
            node.Create(
                m_context.SystemContext,
                versionNodeId,
                new QualifiedName(versionId, m_context.ModelNamespaceIndex),
                new LocalizedText(version.Name),
                assignNodeIds: false);

            // Version carries its own Xid, Epoch, Labels, CreatedAt, ModifiedAt, Delete.
            node.AddVersionId(m_context.SystemContext)
                .AddFormat(m_context.SystemContext)
                .AddContentType(m_context.SystemContext)
                .AddXid(m_context.SystemContext)
                .AddEpoch(m_context.SystemContext)
                .AddDescription(m_context.SystemContext)
                .AddCreatedAt(m_context.SystemContext)
                .AddModifiedAt(m_context.SystemContext);
            node.AddDelete(m_context.SystemContext);
            node.AddLabels(m_context.SystemContext);
            node.EventNotifier = EventNotifiers.SubscribeToEvents;

            // Delete on a Version always uses Version-delete semantics.
            node.Delete?.OnCallMethod2Async =
                (c, m, o, i, ot, t) => OnDeleteVersionAsync(
                    groupId, resourceId, versionId, c, i, t);

            WireLabelsContainer(
                node.Labels,
                (c, i, t) => OnAddResourceLabelAsync(groupId, resourceId, versionId, c, i, t),
                (c, i, t) => OnRemoveResourceLabelAsync(groupId, resourceId, versionId, c, i, t));

            IXRegistryProjectedResourceFile? file = m_strategy.CreateResourceFile(node, version);
            var entry = new ResourceEntry(node, file, groupId, resourceId, versionId, version.Xid);
            ApplyVersionProperties(entry, version, eventSnapshot);
            m_strategy.ConfigureResourceNode(node, version);
            m_context.SystemContext.AssignInstanceChildNodeIds(node);
            LinkMethodArguments(node, m_context.SystemContext);

            // Wire into the Versions folder and notifier chain.
            logical.VersionsFolder.AddChild(node);
            logical.LogicalNode.AddReference(
                Opc.Ua.ReferenceTypeIds.HasNotifier, false, versionNodeId);
            node.AddReference(
                Opc.Ua.ReferenceTypeIds.HasNotifier, true, logical.LogicalNode.NodeId);

            await m_context.AddNodeAsync(node, ct).ConfigureAwait(false);
            m_resourcesByXid[version.Xid] = node;
            await SyncLabelPropertiesAsync(
                node.Labels!,
                VersionNodeIdPath(groupId, resourceId, versionId),
                version.Labels,
                ct).ConfigureAwait(false);
            return entry;
        }

        private void ApplyLogicalResourceProperties(
            LogicalResourceEntry logical,
            IXRegistryProjectionResource defaultVersion,
            XRegistryProjectionEventSnapshot? eventSnapshot)
        {
            ResourceState node = logical.LogicalNode;
            SetValue(node.ResourceId, defaultVersion.ResourceId);
            node.BrowseName = new QualifiedName(
                defaultVersion.ResourceId,
                m_context.ModelNamespaceIndex);
            // Delegated properties from the selected default Version.
            SetValue(node.VersionId, defaultVersion.VersionId);
            SetValue(node.Format, defaultVersion.Format);
            SetValue(node.ContentType, defaultVersion.ContentType);
            SetValue(node.Epoch, (uint)defaultVersion.Epoch);
            SetValue(node.Name, defaultVersion.Name);
            SetValue(node.Description, defaultVersion.Description);
            if (defaultVersion.CreatedAt != default)
            {
                SetValue(node.CreatedAt, (DateTimeUtc)defaultVersion.CreatedAt);
            }
            SetValue(node.ModifiedAt, (DateTimeUtc)defaultVersion.ModifiedAt);

            // Stable Resource Xid = resource path without version.
            string resourceXid = defaultVersion.Xid;
            int versionsIdx = resourceXid.IndexOf("/versions/", StringComparison.Ordinal);
            if (versionsIdx >= 0)
            {
                resourceXid = resourceXid.Substring(0, versionsIdx);
            }
            SetValue(node.Xid, resourceXid);

            if (defaultVersion is IXRegistryProjectionResourceMeta meta)
            {
                SetValue(node.MetaEpoch, checked((uint)meta.MetaEpoch));
                SetValue(node.MetaCreatedAt, (DateTimeUtc)meta.MetaCreatedAt);
                SetValue(node.MetaModifiedAt, (DateTimeUtc)meta.MetaModifiedAt);

                // Register the logical node in the xid index.
                m_resourcesByXid[
                    ResourceSubject(defaultVersion.GroupId, defaultVersion.ResourceId)] = node;
            }
            else if (FindEventResource(
                    eventSnapshot,
                    defaultVersion.GroupId,
                    defaultVersion.ResourceId) is { } eventResource)
            {
                SetValue(node.MetaEpoch, eventResource.MetaEpoch);
                SetValue(node.MetaCreatedAt, (DateTimeUtc)eventResource.MetaCreatedAt);
                SetValue(node.MetaModifiedAt, (DateTimeUtc)eventResource.MetaModifiedAt);
            }
        }

        private void ApplyVersionProperties(
            ResourceEntry entry,
            IXRegistryProjectionResource version,
            XRegistryProjectionEventSnapshot? eventSnapshot)
        {
            SetValue(entry.Node.ResourceId, version.ResourceId);
            entry.Node.BrowseName = new QualifiedName(
                version.VersionId,
                m_context.ModelNamespaceIndex);
            SetValue(entry.Node.VersionId, version.VersionId);
            SetValue(entry.Node.Format, version.Format);
            SetValue(entry.Node.ContentType, version.ContentType);
            SetValue(entry.Node.Xid, version.Xid);
            SetValue(entry.Node.Epoch, (uint)version.Epoch);
            SetValue(entry.Node.Name, version.Name);
            SetValue(entry.Node.Description, version.Description);
            if (version.CreatedAt != default)
            {
                SetValue(entry.Node.CreatedAt, (DateTimeUtc)version.CreatedAt);
            }
            SetValue(entry.Node.ModifiedAt, (DateTimeUtc)version.ModifiedAt);
            entry.File?.ApplyResource(version);
        }

        private async ValueTask RemoveLogicalResourceNodeAsync(
            GroupEntry group,
            string resourceId,
            CancellationToken ct)
        {
            if (!group.LogicalResources.TryGetValue(
                    resourceId,
                    out LogicalResourceEntry? logical))
            {
                return;
            }
            // Remove all version nodes first.
            foreach (string versionId in logical.Versions.Keys.ToList())
            {
                await RemoveVersionNodeAsync(logical, versionId, ct).ConfigureAwait(false);
            }
            // Remove the logical resource xid mappings.
            foreach (KeyValuePair<string, ResourceState> mapped in m_resourcesByXid
                .Where(m => ReferenceEquals(m.Value, logical.LogicalNode)).ToList())
            {
                m_resourcesByXid.TryRemove(mapped.Key, out _);
            }
            group.Node.RemoveReference(
                Opc.Ua.ReferenceTypeIds.HasNotifier, false, logical.LogicalNode.NodeId);
            group.Node.RemoveChild(logical.LogicalNode);
            await m_context.DeleteNodeAsync(logical.LogicalNode.NodeId, ct).ConfigureAwait(false);
            group.LogicalResources.Remove(resourceId);
        }

        private async ValueTask RemoveVersionNodeAsync(
            LogicalResourceEntry logical,
            string versionId,
            CancellationToken ct)
        {
            if (!logical.Versions.TryGetValue(versionId, out ResourceEntry? entry))
            {
                return;
            }
            entry.File?.Dispose();
            foreach (KeyValuePair<string, ResourceState> mapped in m_resourcesByXid
                .Where(m => ReferenceEquals(m.Value, entry.Node)).ToList())
            {
                m_resourcesByXid.TryRemove(mapped.Key, out _);
            }
            logical.LogicalNode.RemoveReference(
                Opc.Ua.ReferenceTypeIds.HasNotifier, false, entry.Node.NodeId);
            logical.VersionsFolder.RemoveChild(entry.Node);
            await m_context.DeleteNodeAsync(entry.Node.NodeId, ct).ConfigureAwait(false);
            logical.Versions.TryRemove(versionId, out _);
        }

        private async ValueTask<ServiceResult> OnDeleteLogicalResourceAsync(
            string groupId,
            string resourceId,
            ISystemContext context,
            ArrayOf<Variant> input,
            CancellationToken ct)
        {
            ServiceResult access = m_context.CheckManagementAccess(context, "DeleteResource");
            if (ServiceResult.IsBad(access))
            {
                return access;
            }
            long? expectedEpoch = OptionalEpoch(input, 0);

            // Resource-delete: always uses MetaEpoch check, deletes the logical
            // Resource and ALL its Versions.
            ServiceResult result = await m_versionedStrategy!.DeleteProjectedEntityAsync(
                    groupId,
                    resourceId,
                    versionId: string.Empty,
                    deleteLogicalResource: true,
                    expectedEpoch,
                    ct)
                .ConfigureAwait(false);
            await ReconcileProjectionAsync(ct).ConfigureAwait(false);
            return result;
        }

        private async ValueTask<ServiceResult> OnDeleteVersionAsync(
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
            long? expectedEpoch = OptionalEpoch(input, 0);

            // Version-delete: always uses that Version's own Epoch check,
            // deletes ONLY that Version. Last-version/default-reassignment
            // rules are applied by the strategy.
            ServiceResult result = await m_versionedStrategy!.DeleteProjectedEntityAsync(
                    groupId,
                    resourceId,
                    versionId,
                    deleteLogicalResource: false,
                    expectedEpoch,
                    ct)
                .ConfigureAwait(false);
            await ReconcileProjectionAsync(ct).ConfigureAwait(false);
            return result;
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
                ResourceEntry? entry = null;
                if (m_versionedStrategy is not null)
                {
                    if (m_groups.TryGetValue(groupId, out GroupEntry? grp) &&
                        grp.LogicalResources.TryGetValue(
                            resourceId, out LogicalResourceEntry? logical))
                    {
                        logical.Versions.TryGetValue(versionId, out entry);
                    }
                }
                else
                {
                    var key = new ResourceEntryKey(resourceId, string.Empty);
                    if (m_groups.TryGetValue(groupId, out GroupEntry? grp))
                    {
                        grp.Resources.TryGetValue(key, out entry);
                    }
                }

                if (entry?.File is not IXRegistryProjectedContentlessResourceFile contentlessFile)
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

            // In versioned mode, ResourceNodeId identifies the exact VERSION.
            NodeId nodeId = m_versionedStrategy is not null
                ? VersionNodeId(groupId, resourceId, versionId)
                : ResourceNodeId(groupId, resourceId, versionId);

            uint fileHandle = 0;
            await m_gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (requestOpen &&
                    m_groups.TryGetValue(groupId, out GroupEntry? group))
                {
                    ResourceEntry? entry = null;
                    if (m_versionedStrategy is not null)
                    {
                        if (group.LogicalResources.TryGetValue(
                                resourceId,
                                out LogicalResourceEntry? logical))
                        {
                            logical.Versions.TryGetValue(versionId, out entry);
                        }
                    }
                    else
                    {
                        group.Resources.TryGetValue(ResourceKey(resource), out entry);
                    }
                    if (entry?.File is not null)
                    {
                        ServiceResult open = entry.File.TryOpenWriteHandle(context, out fileHandle);
                        if (ServiceResult.IsBad(open))
                        {
                            return open;
                        }
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
            ResourceState node,
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
            long? expectedEpoch = OptionalEpoch(input, 0);
            bool deleteLogicalResource =
                m_resourcesByXid.TryGetValue(
                    ResourceSubject(groupId, resourceId),
                    out ResourceState? logicalResource) &&
                ReferenceEquals(logicalResource, node);
            ServiceResult result = m_versionedStrategy is null
                ? await m_strategy.DeleteResourceAsync(
                        groupId,
                        resourceId,
                        expectedEpoch,
                        ct)
                    .ConfigureAwait(false)
                : await m_versionedStrategy.DeleteProjectedEntityAsync(
                        groupId,
                        resourceId,
                        versionId,
                        deleteLogicalResource,
                        expectedEpoch,
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

        private static string ResourceSubject(string groupId, string resourceId)
        {
            return $"/groups/{groupId}/resources/{resourceId}";
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

        private NodeId LogicalResourceNodeId(string groupId, string resourceId)
        {
            return new NodeId(
                LogicalResourceNodeIdPath(groupId, resourceId),
                m_context.ModelNamespaceIndex);
        }

        private string LogicalResourceNodeIdPath(string groupId, string resourceId)
        {
            return $"{m_registryNodeIdPath}/groups/{groupId}/resources/{resourceId}";
        }

        private string LogicalResourceMetaNodeIdPath(string groupId, string resourceId)
        {
            return $"{LogicalResourceNodeIdPath(groupId, resourceId)}/meta";
        }

        private NodeId VersionsFolderNodeId(string groupId, string resourceId)
        {
            return new NodeId(
                VersionsFolderNodeIdPath(groupId, resourceId),
                m_context.ModelNamespaceIndex);
        }

        private string VersionsFolderNodeIdPath(string groupId, string resourceId)
        {
            return $"{m_registryNodeIdPath}/groups/{groupId}/resources/{resourceId}/versions";
        }

        private NodeId VersionNodeId(string groupId, string resourceId, string versionId)
        {
            return new NodeId(
                VersionNodeIdPath(groupId, resourceId, versionId),
                m_context.ModelNamespaceIndex);
        }

        private string VersionNodeIdPath(string groupId, string resourceId, string versionId)
        {
            return $"{m_registryNodeIdPath}/groups/{groupId}/resources/{resourceId}/versions/{versionId}";
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
                    if (DeprecatedFingerprint(newGroup) is not null)
                    {
                        changes.Add(new XRegistryEventChange(
                            XRegistryEventKind.GroupDeprecated,
                            newGroup.Xid,
                            GroupSourceNode(newGroup)));
                    }
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
            if (DeprecatedFingerprint(resource) is not null)
            {
                changes.Add(new XRegistryEventChange(
                    XRegistryEventKind.ResourceDeprecated,
                    resource.Xid,
                    ResourceSourceNode(resource)));
            }
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
            if (!string.Equals(previous.Name, current.Name, StringComparison.Ordinal))
            {
                resourceChanged.Add("name");
            }
            if (!string.Equals(
                    previous.Description,
                    current.Description,
                    StringComparison.Ordinal))
            {
                resourceChanged.Add("description");
            }
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
                    // In versioned mode, route to the logical Resource node if it
                    // still exists (nearest surviving notifier ancestor).
                    if (m_versionedStrategy is not null &&
                        m_groups.TryGetValue(
                            oldResource.GroupId,
                            out GroupEntry? versGroup) &&
                        versGroup.LogicalResources.TryGetValue(
                            oldResource.ResourceId,
                            out LogicalResourceEntry? logical))
                    {
                        return logical.LogicalNode;
                    }

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
                m_groups.TryGetValue(owner.GroupId, out GroupEntry? groupEntry))
            {
                // In versioned mode, use the logical resource node for routing.
                if (m_versionedStrategy is not null &&
                    groupEntry.LogicalResources.TryGetValue(
                        owner.ResourceId,
                        out LogicalResourceEntry? logicalRes))
                {
                    return logicalRes.LogicalNode;
                }
                if (FindResourceEntry(
                        groupEntry,
                        owner.ResourceId,
                        owner.DefaultVersionId) is { } resourceEntry)
                {
                    return resourceEntry.Node;
                }
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
            // In versioned mode, look in LogicalResources.
            if (group.LogicalResources.TryGetValue(
                    resourceId,
                    out LogicalResourceEntry? logical))
            {
                if (!string.IsNullOrEmpty(versionId) &&
                    logical.Versions.TryGetValue(versionId!, out ResourceEntry? version))
                {
                    return version;
                }
                return logical.Versions.Values.FirstOrDefault();
            }
            return null;
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
                foreach (LogicalResourceEntry logical in group.LogicalResources.Values)
                {
                    if (logical.LogicalNode.NodeId == nodeId)
                    {
                        return logical.LogicalNode;
                    }
                    foreach (ResourceEntry version in logical.Versions.Values)
                    {
                        if (version.Node.NodeId == nodeId)
                        {
                            return version.Node;
                        }
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
            if (!resource.SourceNodeId.IsNull)
            {
                return resource.SourceNodeId;
            }
            // In versioned mode, Resource events use the logical Resource NodeId.
            return m_versionedStrategy is not null
                ? LogicalResourceNodeId(resource.GroupId, resource.ResourceId)
                : ResourceNodeId(
                    resource.GroupId,
                    resource.ResourceId,
                    resource.DefaultVersionId ?? string.Empty);
        }

        private NodeId VersionSourceNode(
            XRegistryProjectionEventVersion version,
            XRegistryProjectionEventResource resource)
        {
            if (!version.SourceNodeId.IsNull)
            {
                return version.SourceNodeId;
            }
            // In versioned mode, Version events use the exact Version NodeId.
            return m_versionedStrategy is not null
                ? VersionNodeId(resource.GroupId, resource.ResourceId, version.VersionId)
                : ResourceNodeId(
                    resource.GroupId,
                    resource.ResourceId,
                    version.VersionId);
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
                changed.Add("xid");
                changed.Add("epoch");
                changed.Add("isdefault");
                if (version.CreatedAt != default)
                {
                    changed.Add("createdat");
                }
                if (version.ModifiedAt != default)
                {
                    changed.Add("modifiedat");
                }
                if (!version.Labels.IsEmpty)
                {
                    changed.Add("labels");
                }
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

            /// <summary>
            /// Flat-mode resources (non-versioned strategy) keyed by (ResourceId, "").
            /// </summary>
            public Dictionary<ResourceEntryKey, ResourceEntry> Resources { get; } = [];

            /// <summary>
            /// Versioned-mode logical resources keyed by ResourceId.
            /// Each entry contains the logical Resource node, its Versions folder
            /// and the per-version entries underneath.
            /// </summary>
            public Dictionary<string, LogicalResourceEntry> LogicalResources { get; } =
                new(StringComparer.Ordinal);
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

        /// <summary>
        /// A file handle pinned by the logical Resource's file forwarding: the
        /// underlying forwarder/handle pair on the exact Version that was the
        /// resolved default at Open time, that Version's node (used to mirror
        /// FileType Properties back onto the logical Resource after Open/Close),
        /// and the session that opened it (so a different session's Close cannot
        /// remove this pin before the underlying manager gets a chance to reject
        /// it — see <see cref="SessionIdOf"/>).
        /// </summary>
        private readonly record struct PinnedFileHandle(
            IXRegistryProjectedResourceFileHandleForwarder Forwarder,
            ResourceState VersionNode,
            uint UnderlyingHandle,
            NodeId SessionId);

        /// <summary>
        /// A logical Resource node with its Versions folder and per-version entries.
        /// Used only when a versioned strategy is active.
        /// </summary>
        private sealed class LogicalResourceEntry
        {
            public LogicalResourceEntry(
                ResourceState logicalNode,
                ResourceVersionsState versionsFolder,
                string groupId,
                string resourceId)
            {
                LogicalNode = logicalNode;
                VersionsFolder = versionsFolder;
                GroupId = groupId;
                ResourceId = resourceId;
            }

            public ResourceState LogicalNode { get; }
            public ResourceVersionsState VersionsFolder { get; }
            public string GroupId { get; }
            public string ResourceId { get; }

            /// <summary>
            /// The exact Version entries under this logical Resource's Versions
            /// folder, keyed by VersionId. A <see cref="ConcurrentDictionary{TKey,TValue}"/>
            /// because the logical Resource's forwarded Open/Read/Write/Close/
            /// GetPosition/SetPosition handlers read this collection directly from
            /// OPC UA method-call dispatch threads, outside the engine's
            /// reconciliation gate, while reconciliation mutates it under that gate
            /// from a different thread/call. A plain <see cref="Dictionary{TKey,TValue}"/>
            /// is not safe for that concurrent read/write pattern.
            /// </summary>
            public ConcurrentDictionary<string, ResourceEntry> Versions { get; } =
                new(StringComparer.Ordinal);

            /// <summary>
            /// Tracks file handles opened via the logical Resource's <c>Open</c>
            /// method, keyed by an engine-allocated synthetic handle unique within
            /// this logical Resource. Each entry pins the exact Version file-manager
            /// (and its node, for FileType Property mirroring) that was the resolved
            /// default at <c>Open</c> time. A synthetic handle is required because
            /// every Version's own file manager allocates its underlying handles
            /// independently starting from 1, so two different Versions opened
            /// through the logical Resource (e.g. across a default switch) can
            /// otherwise produce the same underlying handle number; keying this
            /// collection by that raw number alone would let a later Open silently
            /// overwrite an earlier pin for a different Version, misrouting or
            /// closing the wrong one. The entry is removed on <c>Close</c>.
            /// </summary>
            public ConcurrentDictionary<uint, PinnedFileHandle> PinnedHandles { get; } =
                new();

            private long m_nextPinnedHandle;

            /// <summary>
            /// Allocates a new engine-owned synthetic file handle, unique within this
            /// logical Resource, that never collides with any underlying Version's
            /// own handle numbering.
            /// </summary>
            public uint AllocatePinnedHandle()
                => unchecked((uint)Interlocked.Increment(ref m_nextPinnedHandle));
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
