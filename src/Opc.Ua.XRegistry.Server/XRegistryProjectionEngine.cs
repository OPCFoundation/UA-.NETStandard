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
        }

        /// <summary>
        /// Binds the engine to the registry object and performs the first reconciliation.
        /// </summary>
        public async ValueTask AttachAsync(BaseObjectState registryNode, CancellationToken ct)
        {
            m_registryNode = registryNode ?? throw new ArgumentNullException(nameof(registryNode));
            registryNode.EventNotifier = EventNotifiers.SubscribeToEvents;
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
        public async ValueTask ReconcileAsync(CancellationToken ct)
        {
            if (m_registryNode is null)
            {
                return;
            }
            await m_gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                IXRegistryProjectionSnapshot snapshot = m_strategy.Current;
                if (m_registryNode is RegistryState registryTyped && registryTyped.Labels is not null)
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
                        entry.Node.ClearChangeMasks(m_context.SystemContext, includeChildren: true);
                    }

                    await ReconcileResourcesAsync(entry, group, ct).ConfigureAwait(false);
                }

                foreach (string groupId in m_groups.Keys.Where(id => !seenGroups.Contains(id)).ToList())
                {
                    await RemoveGroupNodeAsync(groupId, ct).ConfigureAwait(false);
                }
            }
            finally
            {
                m_gate.Release();
            }
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
            CancellationToken ct)
        {
            var seenResources = new HashSet<string>(StringComparer.Ordinal);
            foreach (IXRegistryProjectionResource resource in group.Resources)
            {
                seenResources.Add(resource.ResourceId);
                if (!entry.Resources.TryGetValue(resource.ResourceId, out ResourceEntry? res))
                {
                    res = await CreateResourceNodeAsync(entry, resource, ct).ConfigureAwait(false);
                    entry.Resources[resource.ResourceId] = res;
                }
                else
                {
                    ApplyResourceProperties(res, resource);
                    m_strategy.ConfigureResourceNode(res.Node, resource);
                    if (res.Node.Labels is not null)
                    {
                        await SyncLabelPropertiesAsync(
                            res.Node.Labels,
                            ResourceNodeIdPath(resource.GroupId, resource.ResourceId),
                            resource.Labels,
                            ct).ConfigureAwait(false);
                    }
                    res.Node.ClearChangeMasks(m_context.SystemContext, includeChildren: true);
                }
            }

            foreach (string resourceId in entry.Resources.Keys
                .Where(id => !seenResources.Contains(id)).ToList())
            {
                await RemoveResourceNodeAsync(entry, resourceId, ct).ConfigureAwait(false);
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
            foreach (string resourceId in entry.Resources.Keys.ToList())
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
            CancellationToken ct)
        {
            ResourceState node = m_strategy.CreateResourceNode(group.Node, resource);
            NodeId nodeId = ResourceNodeId(resource.GroupId, resource.ResourceId);
            node.ReferenceTypeId = Opc.Ua.ReferenceTypeIds.Organizes;
            node.Create(
                m_context.SystemContext,
                nodeId,
                new QualifiedName(resource.ResourceId, m_context.ModelNamespaceIndex),
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
            node.AddDelete(m_context.SystemContext);
            node.AddLabels(m_context.SystemContext);
            node.EventNotifier = EventNotifiers.SubscribeToEvents;

            string groupId = resource.GroupId;
            string resourceId = resource.ResourceId;
            node.Delete?.OnCallMethod2Async =
                (c, m, o, i, ot, t) => OnDeleteResourceAsync(groupId, resourceId, c, i, t);
            WireLabelsContainer(
                node.Labels,
                (c, i, t) => OnAddResourceLabelAsync(groupId, resourceId, c, i, t),
                (c, i, t) => OnRemoveResourceLabelAsync(groupId, resourceId, c, i, t));

            IXRegistryProjectedResourceFile? file = m_strategy.CreateResourceFile(node, resource);
            var entry = new ResourceEntry(node, file, groupId, resourceId);
            ApplyResourceProperties(entry, resource);
            m_strategy.ConfigureResourceNode(node, resource);
            m_context.SystemContext.AssignInstanceChildNodeIds(node);
            LinkMethodArguments(node, m_context.SystemContext);

            group.Node.AddChild(node);
            group.Node.AddReference(Opc.Ua.ReferenceTypeIds.HasNotifier, false, nodeId);
            node.AddReference(Opc.Ua.ReferenceTypeIds.HasNotifier, true, group.Node.NodeId);

            await m_context.AddNodeAsync(node, ct).ConfigureAwait(false);
            m_resourcesByXid[resource.Xid] = node;
            await SyncLabelPropertiesAsync(
                node.Labels!, ResourceNodeIdPath(groupId, resourceId), resource.Labels, ct)
                .ConfigureAwait(false);
            return entry;
        }

        private void ApplyResourceProperties(
            ResourceEntry entry,
            IXRegistryProjectionResource resource)
        {
            SetValue(entry.Node.ResourceId, resource.ResourceId);
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
            entry.File?.ApplyResource(resource);
        }

        private async ValueTask RemoveResourceNodeAsync(
            GroupEntry group,
            string resourceId,
            CancellationToken ct)
        {
            if (!group.Resources.TryGetValue(resourceId, out ResourceEntry? entry))
            {
                return;
            }
            entry.File?.Dispose();
            m_resourcesByXid.TryRemove(ResourceNodeXid(entry.GroupId, entry.ResourceId), out _);
            group.Node.RemoveReference(Opc.Ua.ReferenceTypeIds.HasNotifier, false, entry.Node.NodeId);
            group.Node.RemoveChild(entry.Node);
            await m_context.DeleteNodeAsync(entry.Node.NodeId, ct).ConfigureAwait(false);
            group.Resources.Remove(resourceId);
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
            await ReconcileAsync(ct).ConfigureAwait(false);
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
            await ReconcileAsync(ct).ConfigureAwait(false);
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
            bool requestOpen = GetBool(input, 2, false);
            if (string.IsNullOrWhiteSpace(resourceId))
            {
                return StatusCodes.BadInvalidArgument;
            }
            IXRegistryProjectionResource? resource = await m_strategy
                .CreateResourceAsync(groupId, resourceId!, ct).ConfigureAwait(false);
            if (resource is null)
            {
                return ServiceResult.Create(
                    StatusCodes.BadNodeIdExists,
                    $"Resource '{resourceId}' already exists in group '{groupId}'.");
            }
            await ReconcileAsync(ct).ConfigureAwait(false);
            return await CompleteResourceOutputAsync(
                resource.GroupId, resource.ResourceId, requestOpen, context, output, created: null, ct)
                .ConfigureAwait(false);
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
            bool requestOpen = GetBool(input, 2, false);
            if (string.IsNullOrWhiteSpace(resourceId))
            {
                return StatusCodes.BadInvalidArgument;
            }
            (IXRegistryProjectionResource resource, bool created) = await m_strategy
                .GetOrCreateResourceAsync(groupId, resourceId!, ct).ConfigureAwait(false);
            await ReconcileAsync(ct).ConfigureAwait(false);
            return await CompleteResourceOutputAsync(
                resource.GroupId, resource.ResourceId, requestOpen, context, output, created, ct)
                .ConfigureAwait(false);
        }

        private async ValueTask<ServiceResult> CompleteResourceOutputAsync(
            string groupId,
            string resourceId,
            bool requestOpen,
            ISystemContext context,
            List<Variant> output,
            bool? created,
            CancellationToken ct)
        {
            NodeId nodeId = ResourceNodeId(groupId, resourceId);
            uint fileHandle = 0;
            await m_gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (requestOpen &&
                    m_groups.TryGetValue(groupId, out GroupEntry? group) &&
                    group.Resources.TryGetValue(resourceId, out ResourceEntry? entry) &&
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

            IXRegistryProjectionResource? resource = FindResource(groupId, resourceId);
            output.Clear();
            output.Add(new Variant(nodeId));
            output.Add(new Variant(resource?.VersionId ?? string.Empty));
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
            await ReconcileAsync(ct).ConfigureAwait(false);
            return result;
        }

        private async ValueTask<ServiceResult> OnDeleteResourceAsync(
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
            ServiceResult result = await m_strategy
                .DeleteResourceAsync(groupId, resourceId, OptionalEpoch(input, 0), ct)
                .ConfigureAwait(false);
            await ReconcileAsync(ct).ConfigureAwait(false);
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
            await ReconcileAsync(ct).ConfigureAwait(false);
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
            await ReconcileAsync(ct).ConfigureAwait(false);
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
            await ReconcileAsync(ct).ConfigureAwait(false);
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
            await ReconcileAsync(ct).ConfigureAwait(false);
            return result;
        }

        private async ValueTask<ServiceResult> OnAddResourceLabelAsync(
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
            ServiceResult result = await m_strategy
                .AddResourceLabelAsync(groupId, resourceId, GetString(input, 0) ?? string.Empty,
                    GetString(input, 1) ?? string.Empty, OptionalEpoch(input, 2), ct)
                .ConfigureAwait(false);
            await ReconcileAsync(ct).ConfigureAwait(false);
            return result;
        }

        private async ValueTask<ServiceResult> OnRemoveResourceLabelAsync(
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
            ServiceResult result = await m_strategy
                .RemoveResourceLabelAsync(groupId, resourceId, GetString(input, 0) ?? string.Empty,
                    OptionalEpoch(input, 1), ct)
                .ConfigureAwait(false);
            await ReconcileAsync(ct).ConfigureAwait(false);
            return result;
        }

        private IXRegistryProjectionResource? FindResource(string groupId, string resourceId)
        {
            foreach (IXRegistryProjectionGroup group in m_strategy.Current.Groups)
            {
                if (!string.Equals(group.GroupId, groupId, StringComparison.Ordinal))
                {
                    continue;
                }
                foreach (IXRegistryProjectionResource resource in group.Resources)
                {
                    if (string.Equals(resource.ResourceId, resourceId, StringComparison.Ordinal))
                    {
                        return resource;
                    }
                }
            }
            return null;
        }

        private NodeId GroupNodeId(string groupId)
        {
            return new NodeId(GroupNodeIdPath(groupId), m_context.ModelNamespaceIndex);
        }

        private NodeId ResourceNodeId(string groupId, string resourceId)
        {
            return new NodeId(ResourceNodeIdPath(groupId, resourceId), m_context.ModelNamespaceIndex);
        }

        private string GroupNodeIdPath(string groupId)
        {
            return $"{m_registryNodeIdPath}/groups/{groupId}";
        }

        private string ResourceNodeIdPath(string groupId, string resourceId)
        {
            return $"{m_registryNodeIdPath}/groups/{groupId}/resources/{resourceId}";
        }

        private static string ResourceNodeXid(string groupId, string resourceId)
        {
            return $"/groups/{groupId}/resources/{resourceId}";
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

        private sealed class GroupEntry
        {
            public GroupEntry(GroupState node)
            {
                Node = node;
            }

            public GroupState Node { get; }

            public Dictionary<string, ResourceEntry> Resources { get; } = new(StringComparer.Ordinal);
        }

        private sealed class ResourceEntry
        {
            public ResourceEntry(
                ResourceState node,
                IXRegistryProjectedResourceFile? file,
                string groupId,
                string resourceId)
            {
                Node = node;
                File = file;
                GroupId = groupId;
                ResourceId = resourceId;
            }

            public ResourceState Node { get; }
            public IXRegistryProjectedResourceFile? File { get; }
            public string GroupId { get; }
            public string ResourceId { get; }
        }

        private readonly XRegistryProjectionContext m_context;
        private readonly IXRegistryProjectionStrategy m_strategy;
        private readonly string m_registryNodeIdPath;
        private readonly SemaphoreSlim m_gate = new(1, 1);
        private readonly Dictionary<string, GroupEntry> m_groups = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, ResourceState> m_resourcesByXid = new(StringComparer.Ordinal);
        private BaseObjectState? m_registryNode;
        private bool m_disposed;
    }
}
