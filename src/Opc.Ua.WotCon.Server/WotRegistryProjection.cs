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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.WotCon.Server.Registry;
using Opc.Ua.XRegistry;

namespace Opc.Ua.WotCon.Server
{
    /// <summary>
    /// Materializes the WoT Connectivity 1.1 registry snapshot as browseable
    /// xRegistry Objects beneath the stable <c>WoTRegistry</c> node:
    /// <c>ThingDescriptionGroupType</c>/<c>ThingModelGroupType</c> group Objects
    /// and their <c>ThingDescriptionFileType</c>/<c>ThingModelFileType</c>
    /// document resources. Every group and resource is (re)created, updated and
    /// removed to mirror the immutable snapshot, with deterministic NodeIds
    /// derived from the registry Xid, notifier references up the
    /// registry &#8594; group &#8594; resource chain, and the xRegistry CRUD /
    /// FileType / document Methods wired to the injected registry service.
    /// </summary>
    internal sealed class WotRegistryProjection : IDisposable
    {
        public WotRegistryProjection(
            WotRegistryNodeManager manager,
            IWotRegistryService registry,
            WotRegistryServerOptions options,
            ILogger logger)
        {
            m_manager = manager ?? throw new ArgumentNullException(nameof(manager));
            m_registry = registry ?? throw new ArgumentNullException(nameof(registry));
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            m_logger = logger;
            m_modelNs = (ushort)manager.Server.NamespaceUris.GetIndex(Namespaces.WotCon);
        }

        /// <summary>
        /// Binds the projection to the well-known registry Object, wires the
        /// registry-level CreateGroup/GetOrCreateGroup Methods, materializes
        /// and wires its Labels (AttributesType) container, and performs the
        /// first reconcile.
        /// </summary>
        public async ValueTask AttachAsync(BaseObjectState registryNode, CancellationToken ct)
        {
            m_registryNode = registryNode ?? throw new ArgumentNullException(nameof(registryNode));
            registryNode.EventNotifier = EventNotifiers.SubscribeToEvents;
            WireMethod(registryNode, XRegistry.BrowseNames.CreateGroup, OnCreateGroupAsync);
            WireMethod(registryNode, XRegistry.BrowseNames.GetOrCreateGroup, OnGetOrCreateGroupAsync);
            if (registryNode is RegistryState registryTyped)
            {
                registryTyped.AddLabels(m_manager.SystemContext);
                WireLabelsContainer(
                    registryTyped.Labels, OnAddRegistryLabelAsync, OnRemoveRegistryLabelAsync);
                LinkMethodArguments(registryTyped.Labels, m_manager.SystemContext);
            }
            await ReconcileAsync(ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Finds the browseable resource node used as an event source, or the
        /// registry node when the resource is unknown.
        /// </summary>
        public NodeState EventSourceFor(string? xid)
        {
            if (!string.IsNullOrEmpty(xid) &&
                m_resourcesByXid.TryGetValue(xid!, out WoTDocumentState? node))
            {
                return node;
            }
            return m_registryNode!;
        }

        /// <summary>
        /// Reconciles the browseable projection with the current registry
        /// snapshot: creates, updates and removes group and resource nodes.
        /// Never re-triggers materialization.
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
                WotRegistrySnapshot snapshot = m_registry.Current;

                if (m_registryNode is RegistryState registryTyped && registryTyped.Labels is not null)
                {
                    await SyncLabelPropertiesAsync(
                        registryTyped.Labels, RegistryNodeIdPath, snapshot.Labels, ct)
                        .ConfigureAwait(false);
                }

                var seenGroups = new HashSet<string>(StringComparer.Ordinal);
                foreach (WotResourceGroup group in snapshot.Groups.Values)
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
                        if (entry.Node.Labels is not null)
                        {
                            await SyncLabelPropertiesAsync(
                                entry.Node.Labels, GroupNodeIdPath(group.GroupId), group.Labels, ct)
                                .ConfigureAwait(false);
                        }
                        entry.Node.ClearChangeMasks(m_manager.SystemContext, includeChildren: true);
                    }

                    var seenResources = new HashSet<string>(StringComparer.Ordinal);
                    foreach (WotResource resource in group.Resources.Values)
                    {
                        seenResources.Add(resource.ResourceId);
                        if (!entry.Resources.TryGetValue(resource.ResourceId, out ResourceEntry? res))
                        {
                            res = await CreateResourceNodeAsync(entry, resource, ct)
                                .ConfigureAwait(false);
                            entry.Resources[resource.ResourceId] = res;
                        }
                        else
                        {
                            ApplyResourceProperties(res, resource);
                            if (res.Node.Labels is not null)
                            {
                                await SyncLabelPropertiesAsync(
                                    res.Node.Labels,
                                    ResourceNodeIdPath(resource.GroupId, resource.ResourceId),
                                    resource.Labels,
                                    ct).ConfigureAwait(false);
                            }
                            res.Node.ClearChangeMasks(m_manager.SystemContext, includeChildren: true);
                        }
                    }

                    foreach (string resourceId in entry.Resources.Keys
                        .Where(id => !seenResources.Contains(id)).ToList())
                    {
                        await RemoveResourceNodeAsync(entry, resourceId, ct).ConfigureAwait(false);
                    }
                }

                foreach (string groupId in m_groups.Keys
                    .Where(id => !seenGroups.Contains(id)).ToList())
                {
                    await RemoveGroupNodeAsync(groupId, ct).ConfigureAwait(false);
                }
            }
            finally
            {
                m_gate.Release();
            }
        }

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

        // ---- group nodes -------------------------------------------------

        private async ValueTask<GroupEntry> CreateGroupNodeAsync(
            WotResourceGroup group, CancellationToken ct)
        {
            bool tm = group.Kind == WoTDocumentKindEnum.ThingModel;
            GroupState node = tm
                ? new ThingModelGroupState(m_registryNode)
                : new ThingDescriptionGroupState(m_registryNode);
            NodeId nodeId = GroupNodeId(group.GroupId);
            node.ReferenceTypeId = Ua.ReferenceTypeIds.Organizes;
            node.TypeDefinitionId = ExpandedNodeId.ToNodeId(
                tm ? ObjectTypeIds.ThingModelGroupType : ObjectTypeIds.ThingDescriptionGroupType,
                m_manager.Server.NamespaceUris);
            node.Create(
                m_manager.SystemContext, nodeId,
                new QualifiedName(group.GroupId, m_modelNs), new LocalizedText(group.Name),
                assignNodeIds: false);

            node.AddCreateResource(m_manager.SystemContext);
            node.AddGetOrCreateResource(m_manager.SystemContext);
            node.AddDelete(m_manager.SystemContext);
            node.AddXid(m_manager.SystemContext);
            node.AddEpoch(m_manager.SystemContext);
            node.AddName(m_manager.SystemContext);
            node.AddDescription(m_manager.SystemContext);
            node.AddCreatedAt(m_manager.SystemContext);
            node.AddModifiedAt(m_manager.SystemContext);
            node.AddLabels(m_manager.SystemContext);
            node.EventNotifier = EventNotifiers.SubscribeToEvents;

            string groupId = group.GroupId;
            WoTDocumentKindEnum kind = group.Kind;
            if (node.CreateResource is not null)
            {
                node.CreateResource.OnCallMethod2Async =
                    (c, m, o, i, ot, t) => OnCreateResourceAsync(groupId, kind, c, i, ot, t);
            }
            if (node.GetOrCreateResource is not null)
            {
                node.GetOrCreateResource.OnCallMethod2Async =
                    (c, m, o, i, ot, t) => OnGetOrCreateResourceAsync(groupId, kind, c, i, ot, t);
            }
            if (node.Delete is not null)
            {
                node.Delete.OnCallMethod2Async =
                    (c, m, o, i, ot, t) => OnDeleteGroupAsync(groupId, c, i, t);
            }
            WireLabelsContainer(
                node.Labels,
                (c, i, t) => OnAddGroupLabelAsync(groupId, c, i, t),
                (c, i, t) => OnRemoveGroupLabelAsync(groupId, c, i, t));

            ApplyGroupProperties(node, group);
            DateTime createdAt = DateTime.UtcNow;
            SetValue(node.CreatedAt, (DateTimeUtc)createdAt);
            SetValue(node.ModifiedAt, (DateTimeUtc)createdAt);
            m_manager.SystemContext.AssignInstanceChildNodeIds(node);
            LinkMethodArguments(node, m_manager.SystemContext);

            m_registryNode!.AddChild(node);
            m_registryNode.AddReference(Ua.ReferenceTypeIds.HasNotifier, false, nodeId);
            node.AddReference(Ua.ReferenceTypeIds.HasNotifier, true, m_registryNode.NodeId);

            await m_manager.AddPredefinedNodeAsync(node, ct).ConfigureAwait(false);
            var entry = new GroupEntry(node, group.Kind);
            await SyncLabelPropertiesAsync(
                node.Labels!, GroupNodeIdPath(group.GroupId), group.Labels, ct).ConfigureAwait(false);
            return entry;
        }

        private void ApplyGroupProperties(GroupState node, WotResourceGroup group)
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
            m_registryNode!.RemoveReference(Ua.ReferenceTypeIds.HasNotifier, false, entry.Node.NodeId);
            m_registryNode.RemoveChild(entry.Node);
            await m_manager.DeleteNodeAsync(m_manager.SystemContext, entry.Node.NodeId, ct)
                .ConfigureAwait(false);
            m_groups.Remove(groupId);
        }

        // ---- resource nodes ----------------------------------------------

        private async ValueTask<ResourceEntry> CreateResourceNodeAsync(
            GroupEntry group, WotResource resource, CancellationToken ct)
        {
            bool tm = resource.Kind == WoTDocumentKindEnum.ThingModel;
            WoTDocumentState node = tm
                ? new ThingModelFileState(group.Node)
                : new ThingDescriptionFileState(group.Node);
            NodeId nodeId = ResourceNodeId(resource.GroupId, resource.ResourceId);
            node.ReferenceTypeId = Ua.ReferenceTypeIds.Organizes;
            node.TypeDefinitionId = ExpandedNodeId.ToNodeId(
                tm ? ObjectTypeIds.ThingModelFileType : ObjectTypeIds.ThingDescriptionFileType,
                m_manager.Server.NamespaceUris);
            node.Create(
                m_manager.SystemContext, nodeId,
                new QualifiedName(resource.ResourceId, m_modelNs),
                new LocalizedText(resource.Name), assignNodeIds: false);

            // Optional xRegistry registry metadata children.
            node.AddVersionId(m_manager.SystemContext);
            node.AddFormat(m_manager.SystemContext);
            node.AddContentType(m_manager.SystemContext);
            node.AddXid(m_manager.SystemContext);
            node.AddEpoch(m_manager.SystemContext);
            node.AddName(m_manager.SystemContext);
            node.AddDescription(m_manager.SystemContext);
            node.AddCreatedAt(m_manager.SystemContext);
            node.AddModifiedAt(m_manager.SystemContext);
            node.AddDesiredVersionId(m_manager.SystemContext);
            node.AddActiveVersionId(m_manager.SystemContext);
            node.AddIsDefault(m_manager.SystemContext);
            node.AddContentDigest(m_manager.SystemContext);
            node.AddValidationOutcome(m_manager.SystemContext);
            node.AddMaterializedNodeCount(m_manager.SystemContext);
            node.AddRootNodeId(m_manager.SystemContext);
            node.AddRefreshGeneration(m_manager.SystemContext);
            node.AddLastRefreshTime(m_manager.SystemContext);
            node.AddDelete(m_manager.SystemContext);
            node.AddValidate(m_manager.SystemContext);
            node.AddSetEnabled(m_manager.SystemContext);
            node.AddSetDefaultVersion(m_manager.SystemContext);
            node.AddLabels(m_manager.SystemContext);
            node.EventNotifier = EventNotifiers.SubscribeToEvents;

            if (node is ThingDescriptionFileState td)
            {
                td.AddThingId(m_manager.SystemContext);
                td.AddThingTitle(m_manager.SystemContext);
                td.AddBaseUri(m_manager.SystemContext);
            }
            else if (node is ThingModelFileState tmNode)
            {
                tmNode.AddModelTitle(m_manager.SystemContext);
                tmNode.AddModelVersion(m_manager.SystemContext);
                tmNode.AddDerivedTypeNodeId(m_manager.SystemContext);
            }

            string groupId = resource.GroupId;
            string resourceId = resource.ResourceId;
            WoTDocumentKindEnum kind = resource.Kind;
            if (node.Delete is not null)
            {
                node.Delete.OnCallMethod2Async =
                    (c, m, o, i, ot, t) => OnDeleteResourceAsync(groupId, resourceId, c, i, t);
            }
            if (node.Validate is not null)
            {
                node.Validate.OnCallMethod2Async =
                    (c, m, o, i, ot, t) => OnValidateAsync(groupId, resourceId, c, ot, t);
            }
            if (node.SetEnabled is not null)
            {
                node.SetEnabled.OnCallMethod2Async =
                    (c, m, o, i, ot, t) => OnSetEnabledAsync(groupId, resourceId, c, i, t);
            }
            if (node.SetDefaultVersion is not null)
            {
                node.SetDefaultVersion.OnCallMethod2Async =
                    (c, m, o, i, ot, t) => OnSetDefaultVersionAsync(groupId, resourceId, c, i, t);
            }
            WireLabelsContainer(
                node.Labels,
                (c, i, t) => OnAddResourceLabelAsync(groupId, resourceId, c, i, t),
                (c, i, t) => OnRemoveResourceLabelAsync(groupId, resourceId, c, i, t));

            // FileType transfer for the document body (commit-on-close).
            var file = new WotResourceFileManager(
                node,
                m_options.Bounds.MaxOpenFileHandles,
                m_options.Bounds.MaxDocumentBytes,
                (bytes, session, token) => CommitDocumentAsync(groupId, resourceId, kind, bytes, token));

            ApplyResourceProperties(new ResourceEntry(node, file, groupId, resourceId, kind), resource);
            m_manager.SystemContext.AssignInstanceChildNodeIds(node);
            LinkMethodArguments(node, m_manager.SystemContext);

            group.Node.AddChild(node);
            group.Node.AddReference(Ua.ReferenceTypeIds.HasNotifier, false, nodeId);
            node.AddReference(Ua.ReferenceTypeIds.HasNotifier, true, group.Node.NodeId);

            await m_manager.AddPredefinedNodeAsync(node, ct).ConfigureAwait(false);
            m_resourcesByXid[BuildXid(resource.GroupId, resource.ResourceId)] = node;
            var entry = new ResourceEntry(node, file, groupId, resourceId, kind);
            await SyncLabelPropertiesAsync(
                node.Labels!, ResourceNodeIdPath(groupId, resourceId), resource.Labels, ct)
                .ConfigureAwait(false);
            return entry;
        }

        private void ApplyResourceProperties(ResourceEntry entry, WotResource resource)
        {
            WoTDocumentState node = entry.Node;
            WotResourceVersion? version = resource.DefaultVersion;
            WotResourceVersion? active = resource.ActiveVersion ?? version;

            SetValue(node.ResourceId, resource.ResourceId);
            SetValue(node.VersionId, version?.VersionId ?? string.Empty);
            SetValue(node.Format, version?.Format ?? string.Empty);
            SetValue(node.ContentType, version?.ContentType ?? "application/td+json");
            SetValue(node.Xid, resource.Xid);
            SetValue(node.Epoch, (uint)resource.Epoch);
            SetValue(node.Name, resource.Name);
            SetValue(node.Description, resource.Description);
            if (version is not null)
            {
                SetValue(node.CreatedAt, (DateTimeUtc)version.CreatedAt);
            }
            SetValue(node.ModifiedAt, (DateTimeUtc)(version?.ModifiedAt ?? DateTime.UtcNow));

            SetValue(node.DocumentKind, resource.Kind);
            SetValue(node.Enabled, resource.Enabled);
            SetValue(node.LoadState, resource.LoadState);
            SetValue(node.DesiredVersionId, resource.DesiredVersionId ?? string.Empty);
            SetValue(node.ActiveVersionId, resource.ActiveVersionId ?? string.Empty);
            SetValue(node.IsDefault, version is not null &&
                string.Equals(version.VersionId, resource.DefaultVersionId, StringComparison.Ordinal));
            SetValue(node.ContentDigest, (ByteString)(version?.Digest ?? Array.Empty<byte>()));
            if (resource.Validation is not null)
            {
                SetValue(node.ValidationOutcome, resource.Validation);
            }
            SetValue(node.MaterializedNodeCount, (uint)resource.MaterializedNodeCount);
            SetValue(node.RootNodeId, resource.RootNodeId ?? NodeId.Null);
            SetValue(node.RefreshGeneration, resource.RefreshGeneration);
            SetValue(node.LastRefreshTime, (DateTimeUtc)resource.LastRefreshTime);

            if (node is ThingDescriptionFileState td)
            {
                SetValue(td.ThingId, resource.ThingId ?? string.Empty);
                SetValue(td.ThingTitle, resource.Title ?? string.Empty);
            }
            else if (node is ThingModelFileState tmNode)
            {
                SetValue(tmNode.ModelTitle, resource.Title ?? string.Empty);
                SetValue(tmNode.DerivedTypeNodeId, resource.RootNodeId ?? NodeId.Null);
            }

            byte[] content = active?.Content.ToArray() ?? Array.Empty<byte>();
            entry.File?.UpdatePersistedContent(content, version?.ContentType);
        }

        private async ValueTask RemoveResourceNodeAsync(
            GroupEntry group, string resourceId, CancellationToken ct)
        {
            if (!group.Resources.TryGetValue(resourceId, out ResourceEntry? entry))
            {
                return;
            }
            entry.File?.Dispose();
            m_resourcesByXid.TryRemove(BuildXid(entry.GroupId, entry.ResourceId), out _);
            group.Node.RemoveReference(Ua.ReferenceTypeIds.HasNotifier, false, entry.Node.NodeId);
            group.Node.RemoveChild(entry.Node);
            await m_manager.DeleteNodeAsync(m_manager.SystemContext, entry.Node.NodeId, ct)
                .ConfigureAwait(false);
            group.Resources.Remove(resourceId);
        }

        // ---- method handlers ---------------------------------------------

        private async ValueTask<ServiceResult> OnCreateGroupAsync(
            ISystemContext context, MethodState method, NodeId objectId,
            ArrayOf<Variant> input, List<Variant> output, CancellationToken ct)
        {
            ServiceResult access = m_manager.CheckManagementAccess(context, "CreateGroup");
            if (ServiceResult.IsBad(access))
            {
                return access;
            }
            string? groupId = GetString(input, 0);
            if (string.IsNullOrWhiteSpace(groupId))
            {
                return StatusCodes.BadInvalidArgument;
            }
            WotResourceGroup? group = await m_registry
                .TryCreateGroupAsync(groupId!, KindForGroup(groupId!), cancellationToken: ct)
                .ConfigureAwait(false);
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
            ISystemContext context, MethodState method, NodeId objectId,
            ArrayOf<Variant> input, List<Variant> output, CancellationToken ct)
        {
            ServiceResult access = m_manager.CheckManagementAccess(context, "GetOrCreateGroup");
            if (ServiceResult.IsBad(access))
            {
                return access;
            }
            string? groupId = GetString(input, 0);
            if (string.IsNullOrWhiteSpace(groupId))
            {
                return StatusCodes.BadInvalidArgument;
            }
            bool existed = m_registry.Current.FindGroup(NormalizeId(groupId!)) is not null;
            WotResourceGroup group = await m_registry
                .GetOrCreateGroupAsync(groupId!, KindForGroup(groupId!), cancellationToken: ct)
                .ConfigureAwait(false);
            await ReconcileAsync(ct).ConfigureAwait(false);
            output.Clear();
            output.Add(new Variant(GroupNodeId(group.GroupId)));
            output.Add(new Variant(!existed));
            return ServiceResult.Good;
        }

        private async ValueTask<ServiceResult> OnCreateResourceAsync(
            string groupId, WoTDocumentKindEnum kind, ISystemContext context,
            ArrayOf<Variant> input, List<Variant> output, CancellationToken ct)
        {
            ServiceResult access = m_manager.CheckManagementAccess(context, "CreateResource");
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
            WotResource? resource = await m_registry
                .TryCreateResourceAsync(groupId, resourceId!, kind, ct).ConfigureAwait(false);
            if (resource is null)
            {
                return ServiceResult.Create(
                    StatusCodes.BadNodeIdExists,
                    $"Resource '{resourceId}' already exists in group '{groupId}'.");
            }
            await ReconcileAsync(ct).ConfigureAwait(false);
            return CompleteResourceOutput(
                resource.GroupId, resource.ResourceId, requestOpen, context, output, created: null);
        }

        private async ValueTask<ServiceResult> OnGetOrCreateResourceAsync(
            string groupId, WoTDocumentKindEnum kind, ISystemContext context,
            ArrayOf<Variant> input, List<Variant> output, CancellationToken ct)
        {
            ServiceResult access = m_manager.CheckManagementAccess(context, "GetOrCreateResource");
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
            (WotResource resource, bool created) = await m_registry
                .GetOrCreateResourceAsync(groupId, resourceId!, kind, ct).ConfigureAwait(false);
            await ReconcileAsync(ct).ConfigureAwait(false);
            return CompleteResourceOutput(
                resource.GroupId, resource.ResourceId, requestOpen, context, output, created);
        }

        private ServiceResult CompleteResourceOutput(
            string groupId, string resourceId, bool requestOpen,
            ISystemContext context, List<Variant> output, bool? created)
        {
            NodeId nodeId = ResourceNodeId(groupId, resourceId);
            uint fileHandle = 0;
            if (requestOpen &&
                m_groups.TryGetValue(groupId, out GroupEntry? group) &&
                group.Resources.TryGetValue(resourceId, out ResourceEntry? entry) &&
                entry.File is not null)
            {
                ServiceResult open = entry.File.TryOpenWriteHandle(
                    (context as ISessionSystemContext)?.SessionId, out fileHandle);
                if (ServiceResult.IsBad(open))
                {
                    return open;
                }
            }
            WotResource? resource = m_registry.Current.FindResource(groupId, resourceId);
            output.Clear();
            output.Add(new Variant(nodeId));
            output.Add(new Variant(resource?.DefaultVersionId ?? string.Empty));
            output.Add(new Variant(fileHandle));
            if (created is { } wasCreated)
            {
                output.Add(new Variant(wasCreated));
            }
            return ServiceResult.Good;
        }

        private async ValueTask<ServiceResult> OnDeleteGroupAsync(
            string groupId, ISystemContext context, ArrayOf<Variant> input, CancellationToken ct)
        {
            ServiceResult access = m_manager.CheckManagementAccess(context, "DeleteGroup");
            if (ServiceResult.IsBad(access))
            {
                return access;
            }
            long? epoch = OptionalEpoch(input, 0);
            WotRegistryMutationResult result = await m_registry
                .DeleteGroupAsync(groupId, epoch, ct).ConfigureAwait(false);
            await ReconcileAsync(ct).ConfigureAwait(false);
            return ToServiceResult(result);
        }

        private async ValueTask<ServiceResult> OnDeleteResourceAsync(
            string groupId, string resourceId, ISystemContext context,
            ArrayOf<Variant> input, CancellationToken ct)
        {
            ServiceResult access = m_manager.CheckManagementAccess(context, "DeleteResource");
            if (ServiceResult.IsBad(access))
            {
                return access;
            }
            long? epoch = OptionalEpoch(input, 0);
            WotRegistryMutationResult result = await m_registry
                .DeleteResourceAsync(groupId, resourceId, epoch, ct).ConfigureAwait(false);
            await ReconcileAsync(ct).ConfigureAwait(false);
            return ToServiceResult(result);
        }

        private async ValueTask<ServiceResult> OnValidateAsync(
            string groupId, string resourceId, ISystemContext context,
            List<Variant> output, CancellationToken ct)
        {
            ServiceResult access = m_manager.CheckManagementAccess(context, "Validate");
            if (ServiceResult.IsBad(access))
            {
                return access;
            }
            WoTValidationOutcomeDataType outcome;
            try
            {
                outcome = await m_registry.ValidateResourceAsync(groupId, resourceId, ct)
                    .ConfigureAwait(false);
            }
            catch (ServiceResultException ex)
            {
                return ex.Result;
            }
            await ReconcileAsync(ct).ConfigureAwait(false);
            output.Clear();
            output.Add(new Variant(new ExtensionObject(outcome)));
            return ServiceResult.Good;
        }

        private async ValueTask<ServiceResult> OnSetEnabledAsync(
            string groupId, string resourceId, ISystemContext context,
            ArrayOf<Variant> input, CancellationToken ct)
        {
            ServiceResult access = m_manager.CheckManagementAccess(context, "SetEnabled");
            if (ServiceResult.IsBad(access))
            {
                return access;
            }
            if (GetBoolOrNull(input, 0) is not { } enabled)
            {
                return ServiceResult.Create(
                    StatusCodes.BadInvalidArgument, "The Enabled argument is required.");
            }
            long? epoch = OptionalEpoch(input, 1);
            WotRegistryMutationResult result = await m_registry
                .SetEnabledAsync(groupId, resourceId, enabled, epoch, ct).ConfigureAwait(false);
            return ToServiceResult(result);
        }

        private async ValueTask<ServiceResult> OnSetDefaultVersionAsync(
            string groupId, string resourceId, ISystemContext context,
            ArrayOf<Variant> input, CancellationToken ct)
        {
            ServiceResult access = m_manager.CheckManagementAccess(context, "SetDefaultVersion");
            if (ServiceResult.IsBad(access))
            {
                return access;
            }
            string? versionId = GetString(input, 0);
            if (string.IsNullOrEmpty(versionId))
            {
                return ServiceResult.Create(
                    StatusCodes.BadInvalidArgument, "The VersionId argument is required.");
            }
            long? epoch = OptionalEpoch(input, 1);
            WotRegistryMutationResult result = await m_registry
                .SetDefaultVersionAsync(groupId, resourceId, versionId!, epoch, ct)
                .ConfigureAwait(false);
            return ToServiceResult(result);
        }

        private async ValueTask<ServiceResult> CommitDocumentAsync(
            string groupId, string resourceId, WoTDocumentKindEnum kind,
            byte[] content, CancellationToken ct)
        {
            var request = new WotUpsertResourceRequest
            {
                GroupId = groupId,
                ResourceId = resourceId,
                Kind = kind,
                Content = content,
                ContentType = kind == WoTDocumentKindEnum.ThingModel
                    ? "application/tm+json"
                    : "application/td+json",
                Format = kind == WoTDocumentKindEnum.ThingModel ? "WoT-TM/1.1" : "WoT-TD/1.1",
                SetAsDefault = true
            };
            WotRegistryMutationResult result = await m_registry
                .UpsertResourceAsync(request, ct).ConfigureAwait(false);
            // A validation failure still stores the version (Warning): the bytes
            // are never lost and the previous active projection is retained.
            return result.Outcome == WoTOutcomeEnum.Rejected || result.Outcome == WoTOutcomeEnum.Failed
                ? ServiceResult.Create(StatusCodes.BadInvalidState, result.Message)
                : ServiceResult.Good;
        }

        // ---- labels --------------------------------------------------------

        /// <summary>
        /// Wires the AddAttribute/RemoveAttribute Method handlers on a
        /// materialized Labels (AttributesType) container, instantiating the
        /// two optional Method children when not already present.
        /// </summary>
        private void WireLabelsContainer(
            AttributesState? labels,
            Func<ISystemContext, ArrayOf<Variant>, CancellationToken, ValueTask<ServiceResult>> onAdd,
            Func<ISystemContext, ArrayOf<Variant>, CancellationToken, ValueTask<ServiceResult>> onRemove)
        {
            if (labels is null)
            {
                return;
            }
            labels.AddAddAttribute(m_manager.SystemContext);
            labels.AddRemoveAttribute(m_manager.SystemContext);
            if (labels.AddAttribute is not null)
            {
                labels.AddAttribute.OnCallMethod2Async = (c, m, o, i, ot, t) => onAdd(c, i, t);
            }
            if (labels.RemoveAttribute is not null)
            {
                labels.RemoveAttribute.OnCallMethod2Async = (c, m, o, i, ot, t) => onRemove(c, i, t);
            }
        }

        /// <summary>
        /// Reconciles the browsable label Property children of a Labels
        /// container against the desired dictionary: adds/updates changed
        /// values, and removes labels no longer present. Ordinal enumeration
        /// of <paramref name="desired"/> keeps materialization order
        /// deterministic.
        /// </summary>
        private async ValueTask SyncLabelPropertiesAsync(
            AttributesState labels,
            string basePath,
            ImmutableSortedDictionary<string, string> desired,
            CancellationToken ct)
        {
            ISystemContext context = m_manager.SystemContext;
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
                    context, new QualifiedName(label.Key, m_modelNs));
                created.NodeId = LabelNodeId(basePath, label.Key);
                created.Value = label.Value;
                await m_manager.AddPredefinedNodeAsync(created, ct).ConfigureAwait(false);
            }

            foreach (KeyValuePair<string, PropertyState<string>> stale in existing
                .Where(kv => !desired.ContainsKey(kv.Key)).ToList())
            {
                labels.RemoveChild(stale.Value);
                await m_manager.DeleteNodeAsync(m_manager.SystemContext, stale.Value.NodeId, ct)
                    .ConfigureAwait(false);
            }
        }

        private NodeId LabelNodeId(string basePath, string key)
            => new NodeId($"{basePath}/labels/{key}", m_modelNs);

        private async ValueTask<ServiceResult> OnAddRegistryLabelAsync(
            ISystemContext context, ArrayOf<Variant> input, CancellationToken ct)
        {
            ServiceResult access = m_manager.CheckManagementAccess(context, "AddAttribute");
            if (ServiceResult.IsBad(access))
            {
                return access;
            }
            string? key = GetString(input, 0);
            string value = GetString(input, 1) ?? string.Empty;
            long? epoch = OptionalEpoch(input, 2);
            WotRegistryMutationResult result;
            try
            {
                result = await m_registry
                    .AddRegistryLabelAsync(key ?? string.Empty, value, epoch, ct)
                    .ConfigureAwait(false);
            }
            catch (ServiceResultException ex)
            {
                return ex.Result;
            }
            await ReconcileAsync(ct).ConfigureAwait(false);
            return ToServiceResult(result);
        }

        private async ValueTask<ServiceResult> OnRemoveRegistryLabelAsync(
            ISystemContext context, ArrayOf<Variant> input, CancellationToken ct)
        {
            ServiceResult access = m_manager.CheckManagementAccess(context, "RemoveAttribute");
            if (ServiceResult.IsBad(access))
            {
                return access;
            }
            string? key = GetString(input, 0);
            long? epoch = OptionalEpoch(input, 1);
            WotRegistryMutationResult result;
            try
            {
                result = await m_registry
                    .RemoveRegistryLabelAsync(key ?? string.Empty, epoch, ct)
                    .ConfigureAwait(false);
            }
            catch (ServiceResultException ex)
            {
                return ex.Result;
            }
            await ReconcileAsync(ct).ConfigureAwait(false);
            return ToServiceResult(result);
        }

        private async ValueTask<ServiceResult> OnAddGroupLabelAsync(
            string groupId, ISystemContext context, ArrayOf<Variant> input, CancellationToken ct)
        {
            ServiceResult access = m_manager.CheckManagementAccess(context, "AddAttribute");
            if (ServiceResult.IsBad(access))
            {
                return access;
            }
            string? key = GetString(input, 0);
            string value = GetString(input, 1) ?? string.Empty;
            long? epoch = OptionalEpoch(input, 2);
            WotRegistryMutationResult result;
            try
            {
                result = await m_registry
                    .AddGroupLabelAsync(groupId, key ?? string.Empty, value, epoch, ct)
                    .ConfigureAwait(false);
            }
            catch (ServiceResultException ex)
            {
                return ex.Result;
            }
            await ReconcileAsync(ct).ConfigureAwait(false);
            return ToServiceResult(result);
        }

        private async ValueTask<ServiceResult> OnRemoveGroupLabelAsync(
            string groupId, ISystemContext context, ArrayOf<Variant> input, CancellationToken ct)
        {
            ServiceResult access = m_manager.CheckManagementAccess(context, "RemoveAttribute");
            if (ServiceResult.IsBad(access))
            {
                return access;
            }
            string? key = GetString(input, 0);
            long? epoch = OptionalEpoch(input, 1);
            WotRegistryMutationResult result;
            try
            {
                result = await m_registry
                    .RemoveGroupLabelAsync(groupId, key ?? string.Empty, epoch, ct)
                    .ConfigureAwait(false);
            }
            catch (ServiceResultException ex)
            {
                return ex.Result;
            }
            await ReconcileAsync(ct).ConfigureAwait(false);
            return ToServiceResult(result);
        }

        private async ValueTask<ServiceResult> OnAddResourceLabelAsync(
            string groupId, string resourceId, ISystemContext context,
            ArrayOf<Variant> input, CancellationToken ct)
        {
            ServiceResult access = m_manager.CheckManagementAccess(context, "AddAttribute");
            if (ServiceResult.IsBad(access))
            {
                return access;
            }
            string? key = GetString(input, 0);
            string value = GetString(input, 1) ?? string.Empty;
            long? epoch = OptionalEpoch(input, 2);
            WotRegistryMutationResult result;
            try
            {
                result = await m_registry
                    .AddResourceLabelAsync(groupId, resourceId, key ?? string.Empty, value, epoch, ct)
                    .ConfigureAwait(false);
            }
            catch (ServiceResultException ex)
            {
                return ex.Result;
            }
            await ReconcileAsync(ct).ConfigureAwait(false);
            return ToServiceResult(result);
        }

        private async ValueTask<ServiceResult> OnRemoveResourceLabelAsync(
            string groupId, string resourceId, ISystemContext context,
            ArrayOf<Variant> input, CancellationToken ct)
        {
            ServiceResult access = m_manager.CheckManagementAccess(context, "RemoveAttribute");
            if (ServiceResult.IsBad(access))
            {
                return access;
            }
            string? key = GetString(input, 0);
            long? epoch = OptionalEpoch(input, 1);
            WotRegistryMutationResult result;
            try
            {
                result = await m_registry
                    .RemoveResourceLabelAsync(groupId, resourceId, key ?? string.Empty, epoch, ct)
                    .ConfigureAwait(false);
            }
            catch (ServiceResultException ex)
            {
                return ex.Result;
            }
            await ReconcileAsync(ct).ConfigureAwait(false);
            return ToServiceResult(result);
        }

        // ---- helpers -----------------------------------------------------

        private WoTDocumentKindEnum KindForGroup(string groupId)
            => string.Equals(NormalizeId(groupId), WotRegistryGroups.ThingModels, StringComparison.Ordinal)
                ? WoTDocumentKindEnum.ThingModel
                : WoTDocumentKindEnum.ThingDescription;

        private static string NormalizeId(string id)
            => id.Trim().ToLowerInvariant();

        private static string BuildXid(string groupId, string resourceId)
            => $"/groups/{groupId}/resources/{resourceId}";

        private const string RegistryNodeIdPath = "WoTRegistry";

        private static string GroupNodeIdPath(string groupId)
            => "WoTRegistry/groups/" + groupId;

        private static string ResourceNodeIdPath(string groupId, string resourceId)
            => $"WoTRegistry/groups/{groupId}/resources/{resourceId}";

        private NodeId GroupNodeId(string groupId)
            => new NodeId(GroupNodeIdPath(groupId), m_modelNs);

        private NodeId ResourceNodeId(string groupId, string resourceId)
            => new NodeId(ResourceNodeIdPath(groupId, resourceId), m_modelNs);

        private void WireMethod(
            BaseObjectState parent, string browseName, GenericMethodCalledEventHandler2Async handler)
        {
            MethodState? method =
                parent.FindChild(m_manager.SystemContext, new QualifiedName(browseName, XRegistryNs))
                    as MethodState
                ?? parent.FindChild(m_manager.SystemContext, new QualifiedName(browseName, m_modelNs))
                    as MethodState;
            if (method is not null)
            {
                method.OnCallMethod2Async = handler;
            }
        }

        private ushort XRegistryNs
            => (ushort)m_manager.Server.NamespaceUris.GetIndex(XRegistry.Namespaces.XRegistry);

        /// <summary>
        /// Links the <see cref="MethodState.InputArguments"/> /
        /// <see cref="MethodState.OutputArguments"/> Properties of every Method in
        /// the subtree from their materialized child nodes. The generated
        /// instance factories add the argument nodes as plain children without
        /// setting these Properties, which the server's Call argument validation
        /// requires.
        /// </summary>
        internal static void LinkMethodArguments(NodeState? node, ISystemContext context)
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
                        string.Equals(args.BrowseName.Name, Ua.BrowseNames.InputArguments,
                            StringComparison.Ordinal))
                    {
                        method.InputArguments = args;
                    }
                    else if (method.OutputArguments is null &&
                        string.Equals(args.BrowseName.Name, Ua.BrowseNames.OutputArguments,
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

        private static void SetValue<T>(PropertyState<T>? property, T value)
        {
            if (property is not null)
            {
                property.Value = value;
            }
        }

        private static string? GetString(ArrayOf<Variant> input, int index)
            => index < input.Count && input[index].AsBoxedObject(Variant.BoxingBehavior.Legacy) is string s
                ? s : null;

        private static bool GetBool(ArrayOf<Variant> input, int index, bool fallback)
            => GetBoolOrNull(input, index) ?? fallback;

        private static bool? GetBoolOrNull(ArrayOf<Variant> input, int index)
            => index < input.Count && input[index].AsBoxedObject(Variant.BoxingBehavior.Legacy) is bool b
                ? b : null;

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

        private static ServiceResult ToServiceResult(WotRegistryMutationResult result)
        {
            return result.Outcome switch
            {
                WoTOutcomeEnum.Success or WoTOutcomeEnum.Warning or WoTOutcomeEnum.Unchanged
                    => ServiceResult.Good,
                WoTOutcomeEnum.Rejected
                    => ServiceResult.Create(StatusCodes.BadInvalidState, result.Message),
                _ => ServiceResult.Create(StatusCodes.BadNodeIdUnknown, result.Message)
            };
        }

        private sealed class GroupEntry
        {
            public GroupEntry(GroupState node, WoTDocumentKindEnum kind)
            {
                Node = node;
                Kind = kind;
            }

            public GroupState Node { get; }
            public WoTDocumentKindEnum Kind { get; }
            public Dictionary<string, ResourceEntry> Resources { get; }
                = new(StringComparer.Ordinal);
        }

        private sealed class ResourceEntry
        {
            public ResourceEntry(
                WoTDocumentState node, WotResourceFileManager? file,
                string groupId, string resourceId, WoTDocumentKindEnum kind)
            {
                Node = node;
                File = file;
                GroupId = groupId;
                ResourceId = resourceId;
                Kind = kind;
            }

            public WoTDocumentState Node { get; }
            public WotResourceFileManager? File { get; }
            public string GroupId { get; }
            public string ResourceId { get; }
            public WoTDocumentKindEnum Kind { get; }
        }

        private readonly WotRegistryNodeManager m_manager;
        private readonly IWotRegistryService m_registry;
        private readonly WotRegistryServerOptions m_options;
        private readonly ILogger m_logger;
        private readonly ushort m_modelNs;
        private readonly SemaphoreSlim m_gate = new(1, 1);
        private readonly Dictionary<string, GroupEntry> m_groups = new(StringComparer.Ordinal);
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, WoTDocumentState>
            m_resourcesByXid = new(StringComparer.Ordinal);
        private BaseObjectState? m_registryNode;
        private bool m_disposed;
    }
}
