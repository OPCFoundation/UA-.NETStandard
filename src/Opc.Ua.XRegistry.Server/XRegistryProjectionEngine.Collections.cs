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

namespace Opc.Ua.XRegistry.Server
{
    /// <content>
    /// Projects the collection-qualified Groups and metadata-only Resources of committed
    /// generations below the registry Object.
    /// </content>
    public sealed partial class XRegistryProjectionEngine
    {
        /// <summary>
        /// Removes every node the engine published below the registry Object: document-backed
        /// Groups and Resources, collection-qualified Groups, metadata-only Resources,
        /// engine-created collection views and label Properties. Drains the Xid lookups, unbinds
        /// the registry Methods and releases the registry Object. Host-owned collection views and
        /// the registry Object remain. Removals left by an earlier failed activation are retried
        /// first. A later <see cref="AttachAsync"/> projects the then committed generation again.
        /// </summary>
        public async ValueTask DetachAsync(CancellationToken ct)
        {
            await m_gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (m_registryNode is not BaseObjectState registryNode)
                {
                    return;
                }
                foreach (CollectionEntry collection in m_collections.Values.ToList())
                {
                    DetachCollection(collection);
                }
                await FlushPendingNodeDeletesAsync().ConfigureAwait(false);
                foreach (string groupId in m_groups.Keys.ToList())
                {
                    await RemoveGroupNodeAsync(groupId, CancellationToken.None).ConfigureAwait(false);
                }
                if (registryNode is RegistryState registry && registry.Labels is not null)
                {
                    await SyncLabelPropertiesAsync(
                        registry.Labels,
                        m_registryNodeIdPath,
                        ImmutableSortedDictionary<string, string>.Empty,
                        CancellationToken.None).ConfigureAwait(false);
                    if (m_strategy is not null)
                    {
                        registry.Labels.AddAttribute?.OnCallMethod2Async = null;
                        registry.Labels.RemoveAttribute?.OnCallMethod2Async = null;
                    }
                }
                if (m_strategy is not null)
                {
                    UnwireMethod(registryNode, BrowseNames.CreateGroup);
                    UnwireMethod(registryNode, BrowseNames.GetOrCreateGroup);
                }
                m_resourcesByXid.Clear();
                m_entitiesByXid.Clear();
                m_registryNode = null;
                m_eventEmitter = null;
                m_previousEventSnapshot = null;
                m_latestProjectionSequence = -1;
                m_reportedTransitions.Clear();
                m_reportedTransitionOrder.Clear();
            }
            finally
            {
                m_gate.Release();
            }
        }

        private CollectionProjectionPlan? PrepareCollectionProjection(
            IXRegistryProjectionSnapshot snapshot)
        {
            ArrayOf<IXRegistryProjectionCollection> collections =
                snapshot is IXRegistryCollectionProjectionSnapshot collectionSnapshot
                    ? collectionSnapshot.Collections
                    : ArrayOf<IXRegistryProjectionCollection>.Empty;
            if (m_collectionStrategy is null)
            {
                if (!collections.IsEmpty)
                {
                    throw new InvalidOperationException(
                        "The generation contains Group collections, but the registry projection " +
                        "has no collection projection strategy.");
                }
                return null;
            }
            if (m_strategy is null && snapshot.Groups.Any())
            {
                throw new InvalidOperationException(
                    "A metadata-only registry projection cannot publish document-backed Groups.");
            }

            var plan = new CollectionProjectionPlan();
            var names = new HashSet<string>(StringComparer.Ordinal);
            var xids = new HashSet<string>(StringComparer.Ordinal);
            var nodeIds = new HashSet<NodeId> { m_registryNode!.NodeId };
            foreach (IXRegistryProjectionCollection collection in collections)
            {
                plan.Collections.Add(ValidateCollection(collection, names, xids, nodeIds));
            }
            foreach (DesiredCollection desired in plan.Collections)
            {
                PrepareCollectionNodes(desired);
            }
            return plan;
        }

        private DesiredCollection ValidateCollection(
            IXRegistryProjectionCollection? collection,
            HashSet<string> names,
            HashSet<string> xids,
            HashSet<NodeId> nodeIds)
        {
            if (collection is null)
            {
                throw new InvalidOperationException("A Group collection descriptor is null.");
            }
            string name = ValidateSegment(collection.Name, "Group collection name", "/");
            if (m_strategy is not null &&
                string.Equals(name, kDocumentCollectionName, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The 'groups' collection of a registry with document-backed Groups is owned by " +
                    "the document projection.");
            }
            if (!names.Add(name))
            {
                throw new InvalidOperationException($"The Group collection '{name}' is listed twice.");
            }

            QualifiedName browseName = collection.BrowseName.IsNull
                ? new QualifiedName(name, m_context.ModelNamespaceIndex)
                : collection.BrowseName;
            NodeId viewNodeId = collection.NodeId.IsNull
                ? new NodeId($"{m_registryNodeIdPath}/{name}", m_context.ModelNamespaceIndex)
                : collection.NodeId;
            var desired = new DesiredCollection(collection, name, browseName);
            if (m_collections.TryGetValue(name, out CollectionEntry? existing) &&
                existing.BrowseName == browseName &&
                (!existing.Owned || existing.Node.NodeId == viewNodeId))
            {
                desired.Existing = existing;
                desired.View = existing.Node;
                viewNodeId = existing.Node.NodeId;
            }
            else if (FindHostCollectionView(browseName) is BaseInstanceState hostView)
            {
                desired.View = hostView as GroupCollectionState ??
                    throw new InvalidOperationException(
                        $"The registry child '{browseName}' of collection '{name}' is not a " +
                        "GroupCollectionType Object.");
                viewNodeId = hostView.NodeId;
            }
            else
            {
                desired.ViewNodeId = viewNodeId;
                desired.Owned = true;
            }
            AddUniqueNodeId(nodeIds, viewNodeId, "/" + name);

            foreach (IXRegistryProjectionCollectionGroup group in collection.Groups)
            {
                desired.Groups.Add(ValidateGroup(desired, group, xids, nodeIds));
            }
            return desired;
        }

        private DesiredGroup ValidateGroup(
            DesiredCollection collection,
            IXRegistryProjectionCollectionGroup? group,
            HashSet<string> xids,
            HashSet<NodeId> nodeIds)
        {
            if (group is null)
            {
                throw new InvalidOperationException(
                    $"A Group descriptor of collection '{collection.Name}' is null.");
            }
            string groupXid = "/" + collection.Name + "/" +
                ValidateSegment(group.GroupId, "GroupId", "/" + collection.Name);
            EntityShape shape = ValidateEntity(
                group,
                XRegistryProjectionEntityRole.Group,
                collection.Name,
                group.GroupId,
                groupXid,
                QualifiedName.Null,
                xids,
                nodeIds);
            var desired = new DesiredGroup(group, shape);
            if (collection.Existing is not null &&
                collection.Existing.Groups.TryGetValue(group.GroupId, out CollectionEntityEntry? existing) &&
                existing.Shape == shape)
            {
                desired.Existing = existing;
            }

            foreach (IXRegistryProjectionMetadataResource resource in group.Resources)
            {
                if (resource is null)
                {
                    throw new InvalidOperationException(
                        $"A Resource descriptor of Group '{groupXid}' is null.");
                }
                string resourceCollection = ValidateSegment(
                    resource.ResourceCollectionName,
                    "Resource collection name",
                    groupXid);
                string resourceXid = groupXid + "/" + resourceCollection + "/" +
                    ValidateSegment(resource.ResourceId, "ResourceId", groupXid);
                EntityShape resourceShape = ValidateEntity(
                    resource,
                    XRegistryProjectionEntityRole.Resource,
                    collection.Name,
                    group.GroupId,
                    resourceXid,
                    resource.ContainerBrowseName,
                    xids,
                    nodeIds);
                var key = new MetadataResourceKey(resourceCollection, resource.ResourceId);
                var desiredResource = new DesiredResource(resource, key, resourceShape);
                if (desired.Existing is not null &&
                    desired.Existing.Resources.TryGetValue(key, out CollectionEntityEntry? existingResource) &&
                    existingResource.Shape == resourceShape)
                {
                    desiredResource.Existing = existingResource;
                }
                desired.Resources.Add(desiredResource);
            }
            return desired;
        }

        private EntityShape ValidateEntity(
            IXRegistryProjectionEntity entity,
            XRegistryProjectionEntityRole role,
            string collectionName,
            string groupId,
            string xid,
            QualifiedName container,
            HashSet<string> xids,
            HashSet<NodeId> nodeIds)
        {
            if (entity.Role != role)
            {
                throw new InvalidOperationException(
                    $"The entity '{xid}' is described with role {entity.Role} instead of {role}.");
            }
            if (!string.Equals(entity.CollectionName, collectionName, StringComparison.Ordinal) ||
                !string.Equals(entity.GroupId, groupId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"The entity '{xid}' declares collection '{entity.CollectionName}' and Group " +
                    $"'{entity.GroupId}' but is listed below '/{collectionName}/{groupId}'.");
            }
            if (!string.Equals(entity.Xid, xid, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"The Xid '{entity.Xid}' is not the collection-qualified identity '{xid}'.");
            }
            if (!xids.Add(xid))
            {
                throw new InvalidOperationException($"The entity '{xid}' is listed twice.");
            }

            NodeId nodeId = entity.NodeId.IsNull
                ? new NodeId(EntityNodeIdPath(xid), m_context.ModelNamespaceIndex)
                : entity.NodeId;
            AddUniqueNodeId(nodeIds, nodeId, xid);
            if (entity.Labels is not null)
            {
                foreach (string key in entity.Labels.Keys)
                {
                    AddUniqueNodeId(nodeIds, LabelNodeId(EntityNodeIdPath(xid), key), xid);
                }
            }

            NodeId typeDefinitionId = NodeId.Null;
            if (!entity.TypeDefinitionId.IsNull)
            {
                typeDefinitionId = ExpandedNodeId.ToNodeId(
                    entity.TypeDefinitionId,
                    m_context.NamespaceUris);
                if (typeDefinitionId.IsNull)
                {
                    throw new InvalidOperationException(
                        $"The TypeDefinition '{entity.TypeDefinitionId}' of '{xid}' is not in the " +
                        "server namespace table.");
                }
            }
            return new EntityShape(nodeId, typeDefinitionId, container, GetOptionalMembers(entity));
        }

        private void PrepareCollectionNodes(DesiredCollection desired)
        {
            GroupCollectionState view = desired.View ?? PrepareCollectionView(desired);
            desired.View = view;
            foreach (DesiredGroup group in desired.Groups)
            {
                CollectionEntityEntry groupEntry = group.Existing ?? PrepareGroupNode(view, group);
                if (group.Existing is null)
                {
                    group.Created = groupEntry;
                }
                var groupNode = (GroupState)groupEntry.Node;
                foreach (DesiredResource resource in group.Resources)
                {
                    if (resource.Existing is null)
                    {
                        resource.Created = PrepareResourceNode(groupNode, resource);
                    }
                }
            }
        }

        private GroupCollectionState PrepareCollectionView(DesiredCollection desired)
        {
            ISystemContext context = m_context.SystemContext;
            GroupCollectionState view =
                m_collectionStrategy!.CreateCollectionNode(m_registryNode!, desired.Descriptor) ??
                throw new InvalidOperationException(
                    $"The strategy did not create a view for Group collection '{desired.Name}'.");
            view.Create(
                context,
                desired.ViewNodeId,
                desired.BrowseName,
                new LocalizedText(desired.BrowseName.Name),
                assignNodeIds: false);
            view.ReferenceTypeId = ReferenceTypeIds.Organizes;
            SetValue(view.CollectionName, desired.Name);
            LinkMethodArguments(view, context);
            return view;
        }

        private CollectionEntityEntry PrepareGroupNode(
            GroupCollectionState view,
            DesiredGroup desired)
        {
            ISystemContext context = m_context.SystemContext;
            IXRegistryProjectionCollectionGroup group = desired.Descriptor;
            if (m_collectionStrategy!.CreateEntityNode(view, group) is not GroupState node)
            {
                throw new InvalidOperationException(
                    $"The strategy did not create a GroupType Object for '{group.Xid}'.");
            }
            node.Create(
                context,
                desired.Shape.NodeId,
                new QualifiedName(group.GroupId, m_context.ModelNamespaceIndex),
                new LocalizedText(group.Name ?? group.GroupId),
                assignNodeIds: false);
            node.ReferenceTypeId = ReferenceTypeIds.Organizes;
            node.AddXid(context)
                .AddEpoch(context)
                .AddCollectionName(context)
                .AddDelete(context);
            OptionalMembers members = desired.Shape.Members;
            if ((members & OptionalMembers.Description) != 0)
            {
                node.AddDescription(context);
            }
            if ((members & OptionalMembers.Documentation) != 0)
            {
                node.AddDocumentation(context);
            }
            if ((members & OptionalMembers.CreatedAt) != 0)
            {
                node.AddCreatedAt(context);
            }
            if ((members & OptionalMembers.ModifiedAt) != 0)
            {
                node.AddModifiedAt(context);
            }
            if ((members & OptionalMembers.Labels) != 0)
            {
                node.AddLabels(context);
            }

            var entry = new CollectionEntityEntry(node, view, desired.Shape, group);
            node.Delete?.OnCallMethod2Async =
                (c, m, o, i, ot, t) => OnDeleteCollectionEntityAsync(entry, c, i, t);
            ApplyEntityMetadata(node, group);
            m_collectionStrategy.ConfigureEntityNode(node, group, created: true);
            VerifyTypeDefinition(node, desired.Shape.TypeDefinitionId, group.Xid);
            LinkMethodArguments(node, context);
            return entry;
        }

        private CollectionEntityEntry PrepareResourceNode(GroupState group, DesiredResource desired)
        {
            ISystemContext context = m_context.SystemContext;
            IXRegistryProjectionMetadataResource resource = desired.Descriptor;
            NodeState container = resource.ContainerBrowseName.IsNull
                ? group
                : group.FindChild(context, resource.ContainerBrowseName) as BaseObjectState ??
                    throw new InvalidOperationException(
                        $"The Group '{group.NodeId}' has no Object '{resource.ContainerBrowseName}' " +
                        $"to organize '{resource.Xid}'.");
            if (m_collectionStrategy!.CreateEntityNode(container, resource) is not MetadataResourceState node)
            {
                throw new InvalidOperationException(
                    $"The strategy did not create a MetadataResourceType Object for '{resource.Xid}'.");
            }
            node.Create(
                context,
                desired.Shape.NodeId,
                new QualifiedName(resource.ResourceId, m_context.ModelNamespaceIndex),
                new LocalizedText(resource.Name ?? resource.ResourceId),
                assignNodeIds: false);
            node.ReferenceTypeId = ReferenceTypeIds.Organizes;
            node.AddXid(context)
                .AddEpoch(context)
                .AddDelete(context);
            OptionalMembers members = desired.Shape.Members;
            if ((members & OptionalMembers.VersionId) != 0)
            {
                node.AddVersionId(context);
            }
            if ((members & OptionalMembers.Name) != 0)
            {
                node.AddName(context);
            }
            if ((members & OptionalMembers.Description) != 0)
            {
                node.AddDescription(context);
            }
            if ((members & OptionalMembers.Documentation) != 0)
            {
                node.AddDocumentation(context);
            }
            if ((members & OptionalMembers.CreatedAt) != 0)
            {
                node.AddCreatedAt(context);
            }
            if ((members & OptionalMembers.ModifiedAt) != 0)
            {
                node.AddModifiedAt(context);
            }
            if ((members & OptionalMembers.Labels) != 0)
            {
                node.AddLabels(context);
            }

            // The Metadata component is the read-only JSON view of the same admitted state,
            // never the Resource's document. Serving it belongs to the strategy.
            SetValue(node.Metadata?.Writable, false);
            SetValue(node.Metadata?.UserWritable, false);

            var entry = new CollectionEntityEntry(node, container, desired.Shape, resource);
            node.Delete?.OnCallMethod2Async =
                (c, m, o, i, ot, t) => OnDeleteCollectionEntityAsync(entry, c, i, t);
            ApplyEntityMetadata(node, resource);
            m_collectionStrategy.ConfigureEntityNode(node, resource, created: true);
            VerifyTypeDefinition(node, desired.Shape.TypeDefinitionId, resource.Xid);
            LinkMethodArguments(node, context);
            return entry;
        }

        private async ValueTask RemoveStaleCollectionEntitiesAsync(CollectionProjectionPlan plan)
        {
            var retainedCollections = new HashSet<CollectionEntry>();
            foreach (DesiredCollection desired in plan.Collections)
            {
                if (desired.Existing is not CollectionEntry collection)
                {
                    continue;
                }
                retainedCollections.Add(collection);
                var retainedGroups = new HashSet<string>(StringComparer.Ordinal);
                foreach (DesiredGroup group in desired.Groups)
                {
                    if (group.Existing is not CollectionEntityEntry groupEntry)
                    {
                        continue;
                    }
                    retainedGroups.Add(group.Descriptor.GroupId);
                    var retainedResources = new HashSet<MetadataResourceKey>(
                        group.Resources
                            .Where(resource => resource.Existing is not null)
                            .Select(resource => resource.Key));
                    foreach (KeyValuePair<MetadataResourceKey, CollectionEntityEntry> stale in groupEntry.Resources
                        .Where(resource => !retainedResources.Contains(resource.Key))
                        .ToList())
                    {
                        groupEntry.Resources.Remove(stale.Key);
                        DetachEntity(stale.Value);
                    }
                }
                foreach (KeyValuePair<string, CollectionEntityEntry> stale in collection.Groups
                    .Where(group => !retainedGroups.Contains(group.Key))
                    .ToList())
                {
                    collection.Groups.Remove(stale.Key);
                    DetachGroup(stale.Value);
                }
            }
            foreach (CollectionEntry stale in m_collections.Values
                .Where(collection => !retainedCollections.Contains(collection))
                .ToList())
            {
                DetachCollection(stale);
            }

            // Every removal, including removals a failed activation left behind, completes
            // before the first node of the new generation is added.
            await FlushPendingNodeDeletesAsync().ConfigureAwait(false);
        }

        private async ValueTask PublishCollectionProjectionAsync(CollectionProjectionPlan plan)
        {
            foreach (DesiredCollection desired in plan.Collections)
            {
                CollectionEntry collection = desired.Existing ??
                    await AddCollectionViewAsync(desired).ConfigureAwait(false);
                foreach (DesiredGroup group in desired.Groups)
                {
                    CollectionEntityEntry groupEntry;
                    if (group.Existing is not null)
                    {
                        groupEntry = group.Existing;
                        await UpdateEntityAsync(groupEntry, group.Descriptor).ConfigureAwait(false);
                    }
                    else
                    {
                        groupEntry = group.Created!;
                        await AddEntityNodeAsync(groupEntry).ConfigureAwait(false);
                        collection.Groups[group.Descriptor.GroupId] = groupEntry;
                        await SyncEntityLabelsAsync(groupEntry, group.Descriptor).ConfigureAwait(false);
                    }

                    foreach (DesiredResource resource in group.Resources)
                    {
                        if (resource.Existing is not null)
                        {
                            await UpdateEntityAsync(resource.Existing, resource.Descriptor)
                                .ConfigureAwait(false);
                            continue;
                        }
                        CollectionEntityEntry resourceEntry = resource.Created!;
                        await AddEntityNodeAsync(resourceEntry).ConfigureAwait(false);
                        groupEntry.Resources[resource.Key] = resourceEntry;
                        await SyncEntityLabelsAsync(resourceEntry, resource.Descriptor)
                            .ConfigureAwait(false);
                    }
                }
            }
        }

        private async ValueTask<CollectionEntry> AddCollectionViewAsync(DesiredCollection desired)
        {
            GroupCollectionState view = desired.View!;
            if (desired.Owned)
            {
                m_context.SystemContext.AssignInstanceChildNodeIds(view);
                m_registryNode!.AddChild(view);
                await AddPublishedNodeAsync(m_registryNode, view).ConfigureAwait(false);
            }
            else
            {
                SetValue(view.CollectionName, desired.Name);
                view.ClearChangeMasks(m_context.SystemContext, includeChildren: true);
            }
            var collection = new CollectionEntry(desired.Name, view, desired.Owned, desired.BrowseName);
            m_collections[desired.Name] = collection;
            return collection;
        }

        private async ValueTask AddEntityNodeAsync(CollectionEntityEntry entry)
        {
            BaseObjectState node = entry.Node;
            m_context.SystemContext.AssignInstanceChildNodeIds(node);
            entry.Parent.AddChild(node);
            await AddPublishedNodeAsync(entry.Parent, node).ConfigureAwait(false);
            entry.MarkPublished();
            m_entitiesByXid[entry.Entity.Xid] = node;
        }

        private async ValueTask AddPublishedNodeAsync(NodeState parent, BaseInstanceState node)
        {
            try
            {
                await m_context.AddNodeAsync(node, CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // The host may have registered part of the node; the next activation removes it
                // before it adds the node again.
                parent.RemoveChild(node);
                m_pendingNodeDeletes.Add(node.NodeId);
                throw;
            }
        }

        private async ValueTask UpdateEntityAsync(
            CollectionEntityEntry entry,
            IXRegistryProjectionEntity entity)
        {
            entry.Entity = entity;
            ApplyEntityMetadata(entry.Node, entity);
            m_collectionStrategy!.ConfigureEntityNode(entry.Node, entity, created: false);
            await SyncEntityLabelsAsync(entry, entity).ConfigureAwait(false);
            entry.Node.ClearChangeMasks(m_context.SystemContext, includeChildren: true);
        }

        private ValueTask SyncEntityLabelsAsync(
            CollectionEntityEntry entry,
            IXRegistryProjectionEntity entity)
        {
            AttributesState? labels = entry.Node switch
            {
                GroupState group => group.Labels,
                MetadataResourceState resource => resource.Labels,
                _ => null
            };
            return labels is null || entity.Labels is null
                ? default
                : SyncLabelPropertiesAsync(
                    labels,
                    EntityNodeIdPath(entity.Xid),
                    entity.Labels,
                    CancellationToken.None);
        }

        private void DetachCollection(CollectionEntry collection)
        {
            m_collections.Remove(collection.Name);
            foreach (KeyValuePair<string, CollectionEntityEntry> group in collection.Groups.ToList())
            {
                collection.Groups.Remove(group.Key);
                DetachGroup(group.Value);
            }
            if (collection.Owned)
            {
                m_registryNode!.RemoveChild(collection.Node);
                m_pendingNodeDeletes.Add(collection.Node.NodeId);
            }
        }

        private void DetachGroup(CollectionEntityEntry group)
        {
            foreach (KeyValuePair<MetadataResourceKey, CollectionEntityEntry> resource in group.Resources.ToList())
            {
                group.Resources.Remove(resource.Key);
                DetachEntity(resource.Value);
            }
            DetachEntity(group);
        }

        private void DetachEntity(CollectionEntityEntry entry)
        {
            entry.MarkDetached();
            string xid = entry.Entity.Xid;
            if (m_entitiesByXid.TryGetValue(xid, out BaseObjectState? mapped) &&
                ReferenceEquals(mapped, entry.Node))
            {
                m_entitiesByXid.TryRemove(xid, out _);
            }
            entry.Parent.RemoveChild(entry.Node);
            m_pendingNodeDeletes.Add(entry.Node.NodeId);
        }

        private async ValueTask FlushPendingNodeDeletesAsync()
        {
            int completed = 0;
            try
            {
                for (; completed < m_pendingNodeDeletes.Count; completed++)
                {
                    await m_context.DeleteNodeAsync(
                        m_pendingNodeDeletes[completed],
                        CancellationToken.None).ConfigureAwait(false);
                }
            }
            finally
            {
                m_pendingNodeDeletes.RemoveRange(0, completed);
            }
        }

        private void DetachCollectionEntries()
        {
            foreach (CollectionEntry collection in m_collections.Values)
            {
                foreach (CollectionEntityEntry group in collection.Groups.Values)
                {
                    foreach (CollectionEntityEntry resource in group.Resources.Values)
                    {
                        resource.MarkDetached();
                    }
                    group.MarkDetached();
                }
            }
            m_collections.Clear();
            m_entitiesByXid.Clear();
            m_pendingNodeDeletes.Clear();
        }

        private async ValueTask<ServiceResult> OnDeleteCollectionEntityAsync(
            CollectionEntityEntry entry,
            ISystemContext context,
            ArrayOf<Variant> input,
            CancellationToken ct)
        {
            IXRegistryProjectionEntity entity = entry.Entity;
            ServiceResult access = m_context.CheckManagementAccess(
                context,
                entity.Role == XRegistryProjectionEntityRole.Group ? "DeleteGroup" : "DeleteResource");
            if (ServiceResult.IsBad(access))
            {
                return access;
            }
            if (!entry.IsPublished)
            {
                return ServiceResult.Create(
                    StatusCodes.BadNodeIdUnknown,
                    $"The entity '{entity.Xid}' is no longer projected by this node.");
            }
            uint expectedEpoch = 0;
            if (input.Count > 0 && !input[0].TryGetValue(out expectedEpoch))
            {
                return StatusCodes.BadInvalidArgument;
            }

            ServiceResult result = await m_collectionStrategy!
                .DeleteEntityAsync(context, entity, expectedEpoch, ct)
                .ConfigureAwait(false);
            try
            {
                // The commit is irrevocable: activation is not abandoned with the caller.
                await ReconcileProjectionAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex) when (!ServiceResult.IsBad(result))
            {
                return new ServiceResult(
                    StatusCodes.Uncertain,
                    new LocalizedText(
                        $"The deletion of '{entity.Xid}' was committed, but the committed " +
                        "generation could not be activated."),
                    ex);
            }
            return result;
        }

        private BaseInstanceState? FindHostCollectionView(QualifiedName browseName)
        {
            BaseInstanceState? child = m_registryNode!.FindChild(m_context.SystemContext, browseName);
            if (child is null)
            {
                return null;
            }
            foreach (CollectionEntry collection in m_collections.Values)
            {
                if (collection.Owned && ReferenceEquals(collection.Node, child))
                {
                    return null;
                }
            }
            return child;
        }

        private void AddUniqueNodeId(HashSet<NodeId> nodeIds, NodeId nodeId, string owner)
        {
            bool documentPath = m_strategy is not null &&
                nodeId.NamespaceIndex == m_context.ModelNamespaceIndex &&
                nodeId.TryGetValue(out string identifier) &&
                identifier.StartsWith(
                    m_registryNodeIdPath + "/" + kDocumentCollectionName + "/",
                    StringComparison.Ordinal);
            if (documentPath || !nodeIds.Add(nodeId))
            {
                throw new InvalidOperationException(
                    $"The NodeId '{nodeId}' of '{owner}' is not unique in the registry projection.");
            }
        }

        private string EntityNodeIdPath(string xid)
        {
            return m_registryNodeIdPath + xid;
        }

        private void UnwireMethod(BaseObjectState parent, string browseName)
        {
            ushort xRegistryNs = (ushort)m_context.NamespaceUris.GetIndex(
                XRegistryWellKnown.XRegistryNamespaceUri);
            MethodState? method =
                parent.FindChild(m_context.SystemContext, new QualifiedName(browseName, xRegistryNs)) as MethodState
                ?? parent.FindChild(
                    m_context.SystemContext,
                    new QualifiedName(browseName, m_context.ModelNamespaceIndex)) as MethodState;
            method?.OnCallMethod2Async = null;
        }

        private static void ApplyEntityMetadata(BaseObjectState node, IXRegistryProjectionEntity entity)
        {
            node.DisplayName = new LocalizedText(entity.Name ?? LocalIdOf(entity));
            if (node is GroupState group)
            {
                SetValue(group.GroupId, entity.GroupId);
                SetNullableString(group.Name, entity.Name);
                SetValue(group.Xid, entity.Xid);
                SetValue(group.Epoch, entity.Epoch);
                SetValue(group.CollectionName, entity.CollectionName);
                ApplyOptionalValues(
                    entity,
                    group.Description,
                    group.Documentation,
                    group.CreatedAt,
                    group.ModifiedAt);
            }
            else if (node is MetadataResourceState resource &&
                entity is IXRegistryProjectionMetadataResource metadata)
            {
                SetValue(resource.ResourceId, metadata.ResourceId);
                SetValue(resource.HasDocument, false);
                SetValue(resource.MaxVersions, 1u);
                SetValue(resource.Xid, entity.Xid);
                SetValue(resource.Epoch, entity.Epoch);
                if (metadata.VersionId is not null)
                {
                    SetValue(resource.VersionId, metadata.VersionId);
                }
                if (entity.Name is not null)
                {
                    SetValue(resource.Name, entity.Name);
                }
                ApplyOptionalValues(
                    entity,
                    resource.Description,
                    resource.Documentation,
                    resource.CreatedAt,
                    resource.ModifiedAt);
            }
        }

        private static void ApplyOptionalValues(
            IXRegistryProjectionEntity entity,
            PropertyState<string>? description,
            PropertyState<string>? documentation,
            PropertyState<DateTimeUtc>? createdAt,
            PropertyState<DateTimeUtc>? modifiedAt)
        {
            if (entity.Description is not null)
            {
                SetValue(description, entity.Description);
            }
            if (entity.Documentation is not null)
            {
                SetValue(documentation, entity.Documentation);
            }
            if (!entity.CreatedAt.IsNull)
            {
                SetValue(createdAt, entity.CreatedAt);
            }
            if (!entity.ModifiedAt.IsNull)
            {
                SetValue(modifiedAt, entity.ModifiedAt);
            }
        }

        private static void SetNullableString(PropertyState<string>? property, string? value)
        {
            if (property is null)
            {
                return;
            }
            if (value is null)
            {
                ((BaseVariableState)property).Value = Variant.Null;
                return;
            }
            property.Value = value;
        }

        private static void VerifyTypeDefinition(BaseObjectState node, NodeId expected, string xid)
        {
            if (node.TypeDefinitionId.IsNull ||
                (!expected.IsNull && node.TypeDefinitionId != expected))
            {
                throw new InvalidOperationException(
                    $"The entity '{xid}' is committed as '{expected}', but the strategy created a " +
                    $"node of type '{node.TypeDefinitionId}'.");
            }
        }

        private static OptionalMembers GetOptionalMembers(IXRegistryProjectionEntity entity)
        {
            OptionalMembers members = OptionalMembers.None;
            if (entity.Role == XRegistryProjectionEntityRole.Resource && entity.Name is not null)
            {
                members |= OptionalMembers.Name;
            }
            if (entity.Description is not null)
            {
                members |= OptionalMembers.Description;
            }
            if (entity.Documentation is not null)
            {
                members |= OptionalMembers.Documentation;
            }
            if (!entity.CreatedAt.IsNull)
            {
                members |= OptionalMembers.CreatedAt;
            }
            if (!entity.ModifiedAt.IsNull)
            {
                members |= OptionalMembers.ModifiedAt;
            }
            if (entity.Labels is not null)
            {
                members |= OptionalMembers.Labels;
            }
            if (entity is IXRegistryProjectionMetadataResource { VersionId: not null })
            {
                members |= OptionalMembers.VersionId;
            }
            return members;
        }

        private static string LocalIdOf(IXRegistryProjectionEntity entity)
        {
            return entity is IXRegistryProjectionMetadataResource resource
                ? resource.ResourceId
                : entity.GroupId;
        }

        private static string ValidateSegment(string? value, string description, string owner)
        {
            if (string.IsNullOrEmpty(value) || value!.Contains('/', StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"The {description} '{value}' below '{owner}' is empty or contains '/'.");
            }
            return value!;
        }

        [Flags]
        private enum OptionalMembers
        {
            None = 0,
            Name = 1,
            Description = 2,
            Documentation = 4,
            CreatedAt = 8,
            ModifiedAt = 16,
            Labels = 32,
            VersionId = 64
        }

        private readonly record struct MetadataResourceKey(string CollectionName, string ResourceId);

        private readonly record struct EntityShape(
            NodeId NodeId,
            NodeId TypeDefinitionId,
            QualifiedName Container,
            OptionalMembers Members);

        private sealed class CollectionProjectionPlan
        {
            public List<DesiredCollection> Collections { get; } = [];
        }

        private sealed class DesiredCollection
        {
            public DesiredCollection(
                IXRegistryProjectionCollection descriptor,
                string name,
                QualifiedName browseName)
            {
                Descriptor = descriptor;
                Name = name;
                BrowseName = browseName;
            }

            public IXRegistryProjectionCollection Descriptor { get; }
            public string Name { get; }
            public QualifiedName BrowseName { get; }
            public CollectionEntry? Existing { get; set; }
            public GroupCollectionState? View { get; set; }
            public NodeId ViewNodeId { get; set; }
            public bool Owned { get; set; }
            public List<DesiredGroup> Groups { get; } = [];
        }

        private sealed class DesiredGroup
        {
            public DesiredGroup(IXRegistryProjectionCollectionGroup descriptor, EntityShape shape)
            {
                Descriptor = descriptor;
                Shape = shape;
            }

            public IXRegistryProjectionCollectionGroup Descriptor { get; }
            public EntityShape Shape { get; }
            public CollectionEntityEntry? Existing { get; set; }
            public CollectionEntityEntry? Created { get; set; }
            public List<DesiredResource> Resources { get; } = [];
        }

        private sealed class DesiredResource
        {
            public DesiredResource(
                IXRegistryProjectionMetadataResource descriptor,
                MetadataResourceKey key,
                EntityShape shape)
            {
                Descriptor = descriptor;
                Key = key;
                Shape = shape;
            }

            public IXRegistryProjectionMetadataResource Descriptor { get; }
            public MetadataResourceKey Key { get; }
            public EntityShape Shape { get; }
            public CollectionEntityEntry? Existing { get; set; }
            public CollectionEntityEntry? Created { get; set; }
        }

        /// <summary>
        /// A collection view below the registry Object. A host-owned view is reused, never
        /// deleted, and only organizes the projected Groups.
        /// </summary>
        private sealed class CollectionEntry
        {
            public CollectionEntry(
                string name,
                GroupCollectionState node,
                bool owned,
                QualifiedName browseName)
            {
                Name = name;
                Node = node;
                Owned = owned;
                BrowseName = browseName;
            }

            public string Name { get; }
            public GroupCollectionState Node { get; }
            public bool Owned { get; }
            public QualifiedName BrowseName { get; }
            public Dictionary<string, CollectionEntityEntry> Groups { get; } =
                new(StringComparer.Ordinal);
        }

        /// <summary>
        /// A projected collection-qualified Group or metadata-only Resource. The Delete handler
        /// reads <see cref="Entity"/> and the publication state from Method-dispatch threads
        /// outside the reconciliation gate.
        /// </summary>
        private sealed class CollectionEntityEntry
        {
            public CollectionEntityEntry(
                BaseObjectState node,
                NodeState parent,
                EntityShape shape,
                IXRegistryProjectionEntity entity)
            {
                Node = node;
                Parent = parent;
                Shape = shape;
                m_entity = entity;
            }

            public BaseObjectState Node { get; }
            public NodeState Parent { get; }
            public EntityShape Shape { get; }

            public IXRegistryProjectionEntity Entity
            {
                get => Volatile.Read(ref m_entity);
                set => Volatile.Write(ref m_entity, value);
            }

            public bool IsPublished => Volatile.Read(ref m_state) == kPublished;

            public Dictionary<MetadataResourceKey, CollectionEntityEntry> Resources { get; } = [];

            public void MarkPublished()
            {
                Volatile.Write(ref m_state, kPublished);
            }

            public void MarkDetached()
            {
                Volatile.Write(ref m_state, kDetached);
            }

            private const int kPublished = 1;
            private const int kDetached = 2;
            private IXRegistryProjectionEntity m_entity;
            private int m_state;
        }

        private const string kDocumentCollectionName = "groups";
        private readonly IXRegistryCollectionProjectionStrategy? m_collectionStrategy;
        private readonly Dictionary<string, CollectionEntry> m_collections = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, BaseObjectState> m_entitiesByXid =
            new(StringComparer.Ordinal);
        private readonly List<NodeId> m_pendingNodeDeletes = [];
    }
}
