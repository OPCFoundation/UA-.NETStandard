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
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server;

namespace Opc.Ua.XRegistry.Server
{    /// <summary>
    /// Serves the xRegistry registration lifecycle (§5.2) and auto-bootstrap (§10.1): a writer
    /// creates a resource, writes the document bytes, and closes it; on <c>Close</c> the server
    /// computes the content-derived id + algorithm from the document via the configured
    /// <see cref="IResourceContentIdProvider"/> (§6.6) and <b>dynamically, at runtime</b>, makes the
    /// document reachable by its Opaque content-id NodeId (§6.4). The generic FileType Open/Write/Close
    /// machinery is exercised elsewhere in the stack; this manager focuses on the registry-specific
    /// auto-bootstrap on close and the dynamic runtime creation of the content-addressed fast-path node.
    /// </summary>
    /// <remarks>
    /// Deliberately left unsealed: subclassing is the server-side extension seam a domain registry
    /// uses to serve its own companion model on top of the base one.
    /// </remarks>
    public class XRegistryRegistrationNodeManager : CustomNodeManager2
    {
        /// <summary>
        /// Initializes the registration node manager for the registry namespace.
        /// </summary>
        /// <param name="server">The server that owns the node manager.</param>
        /// <param name="configuration">The application configuration.</param>
        /// <param name="options">The registry server options.</param>
        public XRegistryRegistrationNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            XRegistryServerOptions options)
            : base(server, configuration, (options ?? new XRegistryServerOptions()).RegistryNamespaceUri)
        {
            XRegistryServerOptions opts = options ?? new XRegistryServerOptions();
            opts.Validate();
            m_namespaceUri = opts.RegistryNamespaceUri;
            m_contentIdProvider = opts.ContentIdProvider;
            m_resourceStore = opts.ResourceStore;
            m_registryBrowseName = opts.RegistryBrowseName;
            m_registryId = opts.RegistryId;
            m_specVersion = opts.SpecVersion;
            // Bounds so a remote caller cannot exhaust memory or the address space
            // via the registration Methods: the number of concurrently open upload
            // handles, the cumulative bytes buffered per handle, and the number of
            // permanently registered resource nodes. Configured via the options.
            m_maxConcurrentUploads = opts.MaxConcurrentUploads;
            m_maxResourceBytes = opts.MaxResourceBytes;
            m_maxRegisteredResources = opts.MaxRegisteredResources;
            m_requireEncryptionForReads = opts.RequireEncryptionForReads;
            m_eventsEnabled = opts.EventsEnabled;
            m_eventSourceUrl = opts.EventSourceUrl;
            m_groupsAttributeName = opts.GroupsAttributeName;
            m_resourcesAttributeName = opts.ResourcesAttributeName;
            m_resourceDocumentAttributeName = opts.ResourceDocumentAttributeName;
        }

        /// <summary>
        /// Loads the source-generated xRegistry companion model. The model is compiled into the
        /// assembly by the OPC UA model source generator, so no NodeSet2 XML is parsed at runtime.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <returns>The predefined nodes of the xRegistry base model.</returns>
        protected override NodeStateCollection LoadPredefinedNodes(ISystemContext context)
        {
            return new NodeStateCollection().AddOpcUaXRegistry(context);
        }

        /// <summary>
        /// Materializes the registry root from the compiled model. Groups and resource versions are
        /// then created at runtime through the model's own lifecycle Methods.
        /// </summary>
        /// <param name="externalReferences">External reference sink (unused).</param>
        public override void CreateAddressSpace(
            IDictionary<NodeId, IList<IReference>> externalReferences)
        {
            base.CreateAddressSpace(externalReferences);

            ushort ns = (ushort)Server.NamespaceUris.GetIndex(m_namespaceUri);

            CreateRegistryRoot(ns);
            if (m_eventsEnabled && m_registry is not null)
            {
                m_eventEmitter = new XRegistryEventEmitter(SystemContext, m_eventSourceUrl);
                m_registry.EventNotifier = EventNotifiers.SubscribeToEvents;
                if (!externalReferences.TryGetValue(
                        Opc.Ua.ObjectIds.Server,
                        out IList<IReference>? serverReferences))
                {
                    externalReferences[Opc.Ua.ObjectIds.Server] =
                        serverReferences = new List<IReference>();
                }
                serverReferences.Add(new NodeStateReference(
                    Opc.Ua.ReferenceTypeIds.HasNotifier,
                    false,
                    m_registry.NodeId));
                m_registry.AddReference(
                    Opc.Ua.ReferenceTypeIds.HasNotifier,
                    true,
                    Opc.Ua.ObjectIds.Server);
            }
        }

        /// <summary>
        /// Materializes the registry root through the source-generated <c>RegistryType</c> factory
        /// so the instance carries the type's mandatory children — a bare
        /// <c>new RegistryState(parent)</c> would omit them and leave the model's group lifecycle
        /// Methods unbound.
        /// </summary>
        /// <param name="ns">The registry namespace index.</param>
        private void CreateRegistryRoot(ushort ns)
        {
            RegistryState registry = SystemContext.CreateInstanceOfRegistryType(
                parent: null!,
                new QualifiedName(m_registryBrowseName, ns));

            registry.NodeId = new NodeId(XRegistryWellKnown.RegistryObject, ns);
            registry.DisplayName = new LocalizedText(m_registryBrowseName);

            // Everything except RegistryId is Optional on the type, so the factory does not
            // materialize it — including the group lifecycle Methods this manager binds.
            registry.AddSpecVersion(SystemContext);
            registry.AddXid(SystemContext);
            registry.AddEpoch(SystemContext);
            registry.AddCreatedAt(SystemContext);
            registry.AddModifiedAt(SystemContext);
            registry.AddCreateGroup(SystemContext);
            registry.AddGetOrCreateGroup(SystemContext);
            registry.AddLabels(SystemContext);
            BindAttributeMethods(
                registry.Labels,
                () => registry.Epoch,
                PublishRegistryLabelsUpdated);
            if (m_eventsEnabled)
            {
                registry.AddEventSourceUrl(SystemContext);
            }

            SetValue(registry.RegistryId, m_registryId);
            SetValue(registry.SpecVersion, m_specVersion);
            SetValue(registry.Xid, m_registryId);
            SetValue(registry.Epoch, 1u);
            SetValue(registry.CreatedAt, DateTimeUtc.Now);
            SetValue(registry.ModifiedAt, DateTimeUtc.Now);
            SetValue(registry.EventSourceUrl, m_eventSourceUrl);

            if (registry.CreateGroup != null)
            {
                registry.CreateGroup.OnCallAsync = OnCreateGroupAsync;
            }
            if (registry.GetOrCreateGroup != null)
            {
                registry.GetOrCreateGroup.OnCallAsync = OnGetOrCreateGroupAsync;
            }

            AddPredefinedNode(SystemContext, registry);
            m_registry = registry;
        }

        /// <summary>
        /// Handles <c>RegistryType.CreateGroup(GroupId) → GroupNodeId</c>. Fails with
        /// <see cref="StatusCodes.BadNodeIdExists"/> when the group id is already taken.
        /// </summary>
        internal ValueTask<CreateGroupMethodStateResult> OnCreateGroupAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            string groupId,
            CancellationToken cancellationToken)
        {
            if (!IsWriteChannelSecure(context))
            {
                return new ValueTask<CreateGroupMethodStateResult>(
                    new CreateGroupMethodStateResult
                    {
                        ServiceResult = StatusCodes.BadSecurityModeInsufficient
                    });
            }
            if (string.IsNullOrEmpty(groupId))
            {
                return new ValueTask<CreateGroupMethodStateResult>(
                    new CreateGroupMethodStateResult
                    {
                        ServiceResult = StatusCodes.BadInvalidArgument
                    });
            }

            GroupState created;
            lock (m_gate)
            {
                if (m_groups.ContainsKey(groupId))
                {
                    return new ValueTask<CreateGroupMethodStateResult>(
                        new CreateGroupMethodStateResult
                        {
                            ServiceResult = StatusCodes.BadNodeIdExists
                        });
                }

                created = CreateGroupNode(groupId);
            }
            PublishGroupCreated(created);
            return new ValueTask<CreateGroupMethodStateResult>(
                new CreateGroupMethodStateResult
                {
                    ServiceResult = ServiceResult.Good,
                    GroupNodeId = created.NodeId
                });
        }

        /// <summary>
        /// Handles <c>RegistryType.GetOrCreateGroup(GroupId) → (GroupNodeId, Created)</c>, the
        /// idempotent counterpart of <c>CreateGroup</c>.
        /// </summary>
        internal ValueTask<GetOrCreateGroupMethodStateResult> OnGetOrCreateGroupAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            string groupId,
            CancellationToken cancellationToken)
        {
            if (!IsWriteChannelSecure(context))
            {
                return new ValueTask<GetOrCreateGroupMethodStateResult>(
                    new GetOrCreateGroupMethodStateResult
                    {
                        ServiceResult = StatusCodes.BadSecurityModeInsufficient
                    });
            }
            if (string.IsNullOrEmpty(groupId))
            {
                return new ValueTask<GetOrCreateGroupMethodStateResult>(
                    new GetOrCreateGroupMethodStateResult
                    {
                        ServiceResult = StatusCodes.BadInvalidArgument
                    });
            }

            GroupState created;
            lock (m_gate)
            {
                if (m_groups.TryGetValue(groupId, out GroupState? existing))
                {
                    return new ValueTask<GetOrCreateGroupMethodStateResult>(
                        new GetOrCreateGroupMethodStateResult
                        {
                            ServiceResult = ServiceResult.Good,
                            GroupNodeId = existing.NodeId,
                            Created = false
                        });
                }

                created = CreateGroupNode(groupId);
            }
            PublishGroupCreated(created);
            return new ValueTask<GetOrCreateGroupMethodStateResult>(
                new GetOrCreateGroupMethodStateResult
                {
                    ServiceResult = ServiceResult.Good,
                    GroupNodeId = created.NodeId,
                    Created = true
                });
        }

        /// <summary>
        /// Creates and publishes a <c>GroupType</c> instance under the registry root. The caller
        /// holds <see cref="m_gate"/>.
        /// </summary>
        /// <param name="groupId">The group id.</param>
        /// <returns>The created group.</returns>
        private GroupState CreateGroupNode(string groupId)
        {
            ushort ns = (ushort)Server.NamespaceUris.GetIndex(m_namespaceUri);
            GroupState group = SystemContext.CreateInstanceOfGroupType(
                m_registry!,
                new QualifiedName(groupId, ns));

            group.NodeId = new NodeId(m_nextInstanceId++, ns);
            group.DisplayName = new LocalizedText(groupId);
            group.ReferenceTypeId = Opc.Ua.ReferenceTypeIds.Organizes;

            // The resource lifecycle Methods and the metadata below are Optional on the type, so
            // they have to be materialized explicitly.
            group.AddXid(SystemContext);
            group.AddEpoch(SystemContext);
            group.AddCreatedAt(SystemContext);
            group.AddModifiedAt(SystemContext);
            group.AddCreateResource(SystemContext);
            group.AddGetOrCreateResource(SystemContext);
            group.AddDelete(SystemContext);
            group.AddLabels(SystemContext);
            BindAttributeMethods(
                group.Labels,
                () => group.Epoch,
                () => PublishGroupLabelsUpdated(group));

            SetValue(group.GroupId, groupId);
            SetValue(group.Xid, groupId);
            SetValue(group.Epoch, 1u);
            SetValue(group.CreatedAt, DateTimeUtc.Now);
            SetValue(group.ModifiedAt, DateTimeUtc.Now);
            if (m_eventsEnabled)
            {
                group.EventNotifier = EventNotifiers.SubscribeToEvents;
            }

            m_registry?.AddChild(group);
            if (m_eventsEnabled && m_registry is not null)
            {
                m_registry.AddReference(
                    Opc.Ua.ReferenceTypeIds.HasNotifier,
                    false,
                    group.NodeId);
                group.AddReference(
                    Opc.Ua.ReferenceTypeIds.HasNotifier,
                    true,
                    m_registry.NodeId);
            }
            AddPredefinedNode(SystemContext, group);
            m_groups[groupId] = group;

            if (group.CreateResource != null)
            {
                group.CreateResource.OnCallAsync = OnCreateResourceAsync;
            }
            if (group.GetOrCreateResource != null)
            {
                group.GetOrCreateResource.OnCallAsync = OnGetOrCreateResourceAsync;
            }
            m_groupsByNodeId[group.NodeId] = group;
            if (group.Delete != null)
            {
                group.Delete.OnCallAsync = (ctx, m, id, epoch, ct) => IsWriteChannelSecure(ctx)
                    ? OnDeleteGroupAsync(group, epoch)
                    : InsecureDelete();
            }
            return group;
        }

        /// <summary>
        /// Handles <c>GroupType.CreateResource(ResourceId, VersionId, RequestFileOpen)</c> and
        /// returns <c>(ResourceNodeId, AssignedVersionId, FileHandle)</c>. Fails with
        /// <see cref="StatusCodes.BadNodeIdExists"/> when that exact version already exists.
        /// </summary>
        internal async ValueTask<CreateResourceMethodStateResult> OnCreateResourceAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            string resourceId,
            string versionId,
            bool requestFileOpen,
            CancellationToken cancellationToken)
        {
            if (!IsWriteChannelSecure(context))
            {
                return new CreateResourceMethodStateResult
                {
                    ServiceResult = StatusCodes.BadSecurityModeInsufficient
                };
            }

            (ServiceResult result, ResourceState? resource, uint fileHandle, string assigned, bool _) =
                await CreateResourceCoreAsync(objectId, resourceId, versionId, requestFileOpen, false)
                    .ConfigureAwait(false);

            return new CreateResourceMethodStateResult
            {
                ServiceResult = result,
                ResourceNodeId = resource?.NodeId ?? NodeId.Null,
                AssignedVersionId = assigned,
                FileHandle = fileHandle
            };
        }

        /// <summary>
        /// Handles <c>GroupType.GetOrCreateResource(...)</c>, the idempotent counterpart that also
        /// reports whether the version was created.
        /// </summary>
        internal async ValueTask<GetOrCreateResourceMethodStateResult> OnGetOrCreateResourceAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            string resourceId,
            string versionId,
            bool requestFileOpen,
            CancellationToken cancellationToken)
        {
            if (!IsWriteChannelSecure(context))
            {
                return new GetOrCreateResourceMethodStateResult
                {
                    ServiceResult = StatusCodes.BadSecurityModeInsufficient
                };
            }

            (ServiceResult result, ResourceState? resource, uint fileHandle, string assigned, bool created) =
                await CreateResourceCoreAsync(objectId, resourceId, versionId, requestFileOpen, true)
                    .ConfigureAwait(false);

            return new GetOrCreateResourceMethodStateResult
            {
                ServiceResult = result,
                ResourceNodeId = resource?.NodeId ?? NodeId.Null,
                AssignedVersionId = assigned,
                FileHandle = fileHandle,
                Created = created
            };
        }

        /// <summary>
        /// Shared implementation of the two resource-creation Methods.
        /// </summary>
        private ValueTask<(ServiceResult Result, ResourceState? Resource, uint FileHandle,
            string AssignedVersionId, bool Created)> CreateResourceCoreAsync(
            NodeId groupNodeId,
            string resourceId,
            string versionId,
            bool requestFileOpen,
            bool getOrCreate)
        {
            if (string.IsNullOrEmpty(resourceId))
            {
                return Failed(StatusCodes.BadInvalidArgument);
            }

            ResourceState? createdResource = null;
            GroupState? owningGroup = null;
            string assignedVersion = string.Empty;
            uint createdHandle = 0;
            bool firstVersion = false;
            lock (m_gate)
            {
                if (!m_groupsByNodeId.TryGetValue(groupNodeId, out GroupState? group))
                {
                    return Failed(StatusCodes.BadNodeIdUnknown);
                }

                string assigned = string.IsNullOrEmpty(versionId)
                    ? NextVersionId(group.NodeId, resourceId)
                    : versionId;
                assignedVersion = assigned;
                var key = new ResourceKey(group.NodeId, resourceId, assigned);

                // The upload bound applies to every path that hands out a write handle, not just to
                // the one that creates a resource.
                if (requestFileOpen && m_fileHandles.Count >= m_maxConcurrentUploads)
                {
                    return Failed(StatusCodes.BadTooManyOperations);
                }

                if (m_resources.TryGetValue(key, out ResourceState? existing))
                {
                    if (!getOrCreate)
                    {
                        return Failed(StatusCodes.BadNodeIdExists);
                    }

                    uint existingHandle = requestFileOpen ? OpenWriteHandle(existing) : 0;
                    return new ValueTask<(ServiceResult, ResourceState?, uint, string, bool)>(
                        (ServiceResult.Good, existing, existingHandle, assigned, false));
                }

                if (Volatile.Read(ref m_registeredResourceCount) >= m_maxRegisteredResources)
                {
                    return Failed(StatusCodes.BadTooManyOperations);
                }

                var logicalKey = new ResourceIdentityKey(group.NodeId, resourceId);
                firstVersion = !m_resourceMetaEpochs.TryGetValue(logicalKey, out uint metaEpoch);
                metaEpoch = firstVersion ? 1u : metaEpoch + 1u;
                m_resourceMetaEpochs[logicalKey] = metaEpoch;

                ResourceState resource = CreateResourceNode(group, resourceId, assigned, metaEpoch);
                m_resources[key] = resource;
                m_defaultVersions[logicalKey] = assigned;
                ApplyMetaEpochLocked(logicalKey, metaEpoch);
                Interlocked.Increment(ref m_registeredResourceCount);

                createdHandle = requestFileOpen ? OpenWriteHandle(resource) : 0;
                createdResource = resource;
                owningGroup = group;
            }
            PublishResourceCreated(owningGroup!, createdResource!, firstVersion);
            return new ValueTask<(ServiceResult, ResourceState?, uint, string, bool)>(
                (ServiceResult.Good, createdResource, createdHandle, assignedVersion, true));

            static ValueTask<(ServiceResult, ResourceState?, uint, string, bool)> Failed(StatusCode code)
            {
                return new ValueTask<(ServiceResult, ResourceState?, uint, string, bool)>(
                    (new ServiceResult(code), null, 0u, string.Empty, false));
            }
        }

        /// <summary>
        /// Creates and publishes a <c>ResourceType</c> instance under a group. Because
        /// <c>ResourceType</c> is a <c>FileType</c>, the document is transferred through the
        /// inherited file Methods. The caller holds <see cref="m_gate"/>.
        /// </summary>
        private ResourceState CreateResourceNode(
            GroupState group,
            string resourceId,
            string versionId,
            uint metaEpoch)
        {
            ushort ns = (ushort)Server.NamespaceUris.GetIndex(m_namespaceUri);
            string browseName = resourceId + ":" + versionId;
            ResourceState resource = SystemContext.CreateInstanceOfResourceType(
                group,
                new QualifiedName(browseName, ns));

            resource.NodeId = new NodeId(m_nextInstanceId++, ns);
            resource.DisplayName = new LocalizedText(browseName);
            resource.ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasComponent;

            resource.AddVersionId(SystemContext);
            resource.AddFormat(SystemContext);
            resource.AddXid(SystemContext);
            resource.AddEpoch(SystemContext);
            resource.AddCreatedAt(SystemContext);
            resource.AddModifiedAt(SystemContext);
            resource.AddDelete(SystemContext);
            resource.AddLabels(SystemContext);
            if (m_eventsEnabled)
            {
                resource.AddMetaEpoch(SystemContext);
                resource.AddMetaModifiedAt(SystemContext);
            }
            BindAttributeMethods(
                resource.Labels,
                () => m_eventsEnabled ? resource.MetaEpoch : resource.Epoch,
                () => PublishResourceLabelsUpdated(resource));

            SetValue(resource.ResourceId, resourceId);
            SetValue(resource.VersionId, versionId);
            SetValue(resource.Epoch, 1u);
            SetValue(resource.CreatedAt, DateTimeUtc.Now);
            SetValue(resource.ModifiedAt, DateTimeUtc.Now);
            SetValue(resource.MetaEpoch, metaEpoch);
            SetValue(resource.MetaModifiedAt, DateTimeUtc.Now);
            if (m_eventsEnabled)
            {
                resource.EventNotifier = EventNotifiers.SubscribeToEvents;
            }

            BindFileMethods(resource);
            if (resource.Delete != null)
            {
                resource.Delete.OnCallAsync = (ctx, m, id, epoch, ct) => IsWriteChannelSecure(ctx)
                    ? OnDeleteResourceAsync(resource, epoch)
                    : InsecureDelete();
            }

            group.AddChild(resource);
            if (m_eventsEnabled)
            {
                group.AddReference(
                    Opc.Ua.ReferenceTypeIds.HasNotifier,
                    false,
                    resource.NodeId);
                resource.AddReference(
                    Opc.Ua.ReferenceTypeIds.HasNotifier,
                    true,
                    group.NodeId);
            }
            AddPredefinedNode(SystemContext, resource);
            return resource;
        }

        /// <summary>
        /// Handles <c>ResourceType.Delete(ExpectedEpoch)</c>. The epoch is an optimistic-concurrency
        /// check: a caller that read the resource at an older epoch is rejected rather than silently
        /// deleting someone else's newer version.
        /// </summary>
        internal async ValueTask<DeleteMethodStateResult> OnDeleteResourceAsync(
            ResourceState resource,
            uint expectedEpoch)
        {
            string storeKey;
            List<XRegistryEventChange>? changes = null;
            lock (m_gate)
            {
                if (!IsEpochCurrent(resource.Epoch, expectedEpoch))
                {
                    return new DeleteMethodStateResult { ServiceResult = StatusCodes.BadInvalidState };
                }

                storeKey = StoreKeyOf(resource);
                ResourceKey eventKey = default;
                GroupState? eventGroup = null;
                bool hasEventIdentity = m_eventsEnabled &&
                    TryGetResourceKeyLocked(resource, out eventKey) &&
                    m_groupsByNodeId.TryGetValue(eventKey.GroupNodeId, out eventGroup);
                if (!RemoveResourceLocked(resource))
                {
                    // A concurrent Delete already removed it; nothing left to do and nothing to
                    // release a second time.
                    return new DeleteMethodStateResult { ServiceResult = ServiceResult.Good };
                }

                if (hasEventIdentity)
                {
                    var logicalKey = new ResourceIdentityKey(
                        eventKey.GroupNodeId,
                        eventKey.ResourceId);
                    changes = BuildResourceDeletionChangesLocked(
                        eventGroup!,
                        eventKey,
                        resource,
                        logicalKey);
                }
            }

            _ = await m_resourceStore.DeleteAsync(storeKey).ConfigureAwait(false);
            ReportChanges(changes);
            return new DeleteMethodStateResult { ServiceResult = ServiceResult.Good };
        }

        /// <summary>
        /// Handles <c>GroupType.Delete(ExpectedEpoch)</c>, removing the group and every resource
        /// version it owns.
        /// </summary>
        internal async ValueTask<DeleteMethodStateResult> OnDeleteGroupAsync(
            GroupState group,
            uint expectedEpoch)
        {
            var storeKeys = new List<string>();
            List<XRegistryEventChange>? changes = null;
            lock (m_gate)
            {
                if (!IsEpochCurrent(group.Epoch, expectedEpoch))
                {
                    return new DeleteMethodStateResult { ServiceResult = StatusCodes.BadInvalidState };
                }

                if (m_eventsEnabled)
                {
                    changes = [];
                }
                var resources = new List<KeyValuePair<ResourceKey, ResourceState>>(m_resources);
                foreach (KeyValuePair<ResourceKey, ResourceState> entry in resources
                    .Where(entry => entry.Key.GroupNodeId == group.NodeId)
                    .OrderBy(entry => entry.Key.ResourceId, StringComparer.Ordinal)
                    .ThenBy(entry => entry.Key.VersionId, StringComparer.Ordinal))
                {
                    storeKeys.Add(StoreKeyOf(entry.Value));
                    if (changes is not null)
                    {
                        changes.Add(new XRegistryEventChange(
                            XRegistryEventKind.VersionDeleted,
                            VersionSubject(entry.Key),
                            entry.Value.NodeId));
                    }
                    RemoveResourceLocked(entry.Value);
                }
                if (changes is not null)
                {
                    foreach (IGrouping<string, KeyValuePair<ResourceKey, ResourceState>> logical in
                        resources.Where(entry => entry.Key.GroupNodeId == group.NodeId)
                            .GroupBy(entry => entry.Key.ResourceId, StringComparer.Ordinal))
                    {
                        KeyValuePair<ResourceKey, ResourceState> first = logical.First();
                        changes.Add(new XRegistryEventChange(
                            XRegistryEventKind.ResourceDeleted,
                            ResourceSubject(first.Key),
                            first.Value.NodeId));
                    }
                }

                string deletedGroupId = group.GroupId?.Value ?? string.Empty;
                if (group.GroupId?.Value is string groupId)
                {
                    m_groups.Remove(groupId);
                }
                m_groupsByNodeId.Remove(group.NodeId);
                if (m_eventsEnabled && m_registry is not null)
                {
                    m_registry.RemoveReference(
                        Opc.Ua.ReferenceTypeIds.HasNotifier,
                        false,
                        group.NodeId);
                    uint registryEpoch = BumpEntity(m_registry.Epoch, m_registry.ModifiedAt);
                    changes!.Add(new XRegistryEventChange(
                        XRegistryEventKind.GroupDeleted,
                        GroupSubject(deletedGroupId),
                        group.NodeId));
                    changes.Add(new XRegistryEventChange(
                        XRegistryEventKind.RegistryUpdated,
                        RegistrySubject(),
                        m_registry.NodeId,
                        registryEpoch,
                        Changed: CollectionChanged(m_groupsAttributeName)));
                }
                DeleteNode(SystemContext, group.NodeId);
            }

            foreach (string storeKey in storeKeys)
            {
                _ = await m_resourceStore.DeleteAsync(storeKey).ConfigureAwait(false);
            }
            ReportChanges(changes);
            return new DeleteMethodStateResult { ServiceResult = ServiceResult.Good };
        }

        /// <summary>
        /// Removes a resource, its content-addressed fast-path node, any file handles still open on
        /// it and its registration slot. Idempotent: a resource that is already gone is left alone,
        /// so a repeated or racing Delete cannot double-release the shared fast-path reference or
        /// drift the registration count. The caller holds <see cref="m_gate"/>.
        /// </summary>
        /// <param name="resource">The resource to remove.</param>
        /// <returns><c>true</c> when this call removed the resource.</returns>
        private bool RemoveResourceLocked(ResourceState resource)
        {
            var keys = new List<ResourceKey>();
            foreach (KeyValuePair<ResourceKey, ResourceState> entry in m_resources)
            {
                if (ReferenceEquals(entry.Value, resource))
                {
                    keys.Add(entry.Key);
                }
            }

            if (keys.Count == 0)
            {
                // Already removed by a concurrent Delete, or by its group's Delete.
                return false;
            }

            foreach (ResourceKey key in keys)
            {
                m_resources.Remove(key);

                // Drop the version counter once the last version of a resource is gone, otherwise a
                // create/delete loop with fresh ids grows the map without bound —
                // MaxRegisteredResources does not bound it because a delete frees the slot.
                var counterKey = new VersionCounterKey(key.GroupNodeId, key.ResourceId);
                bool anyLeft = false;
                foreach (ResourceKey remaining in m_resources.Keys)
                {
                    if (remaining.GroupNodeId == key.GroupNodeId &&
                        string.Equals(remaining.ResourceId, key.ResourceId, StringComparison.Ordinal))
                    {
                        anyLeft = true;
                        break;
                    }
                }
                if (!anyLeft)
                {
                    m_versionCounters.Remove(counterKey);
                    var logicalKey = new ResourceIdentityKey(key.GroupNodeId, key.ResourceId);
                    m_resourceMetaEpochs.Remove(logicalKey);
                    m_defaultVersions.Remove(logicalKey);
                }
            }

            if (resource.Xid?.Value is string xid && xid.Length > 0)
            {
                // The fast-path node is shared by every resource with the same bytes, so only drop
                // it once the last of them is gone.
                ReleaseFastPathNode(xid);
            }

            // Handles outlive the node otherwise, holding the upload budget forever and letting a
            // caller keep driving a document that no longer has a resource.
            var orphaned = new List<uint>();
            foreach (KeyValuePair<uint, ResourceFileHandle> handle in m_fileHandles)
            {
                if (handle.Value.ResourceNodeId == resource.NodeId)
                {
                    orphaned.Add(handle.Key);
                }
            }
            foreach (uint handle in orphaned)
            {
                m_fileHandles.Remove(handle);
            }

            if (m_eventsEnabled && resource.Parent is GroupState group)
            {
                group.RemoveReference(
                    Opc.Ua.ReferenceTypeIds.HasNotifier,
                    false,
                    resource.NodeId);
            }
            DeleteNode(SystemContext, resource.NodeId);
            Interlocked.Decrement(ref m_registeredResourceCount);
            return true;
        }

        /// <summary>
        /// Binds the inherited <c>FileType</c> Methods so a document is transferred with the
        /// standard file operations rather than a registry-specific mechanism.
        /// </summary>
        private void BindFileMethods(ResourceState resource)
        {
            if (resource.Open != null)
            {
                resource.Open.OnCallAsync = (ctx, m, id, mode, ct) =>
                    OnFileOpenAsync(resource, mode, ctx);
            }
            if (resource.Write != null)
            {
                resource.Write.OnCallAsync = (ctx, m, id, handle, data, ct) => IsWriteChannelSecure(ctx)
                    ? OnFileWriteAsync(resource, handle, data, ctx)
                    : new ValueTask<WriteMethodStateResult>(new WriteMethodStateResult
                    {
                        ServiceResult = StatusCodes.BadSecurityModeInsufficient
                    });
            }
            if (resource.Read != null)
            {
                resource.Read.OnCallAsync = (ctx, m, id, handle, length, ct) => IsReadChannelSecure(ctx)
                    ? OnFileReadAsync(resource, handle, length, ctx)
                    : new ValueTask<ReadMethodStateResult>(new ReadMethodStateResult
                    {
                        ServiceResult = StatusCodes.BadSecurityModeInsufficient
                    });
            }
            if (resource.Close != null)
            {
                // Close is gated on the mode of the handle being closed, not unconditionally on the
                // write requirement: a read handle opened on a channel the read policy allows has
                // to be closable on that same channel, or it leaks and consumes the handle budget.
                resource.Close.OnCallAsync = (ctx, m, id, handle, ct) =>
                    OnFileCloseAsync(resource, handle, ctx);
            }
        }

        /// <summary>
        /// Opens the resource's document. Opening for writing is a mutation, so it needs a
        /// <c>SignAndEncrypt</c> channel; opening for reading follows
        /// <see cref="XRegistryServerOptions.RequireEncryptionForReads"/>. The mode bits are the
        /// standard <c>FileType</c> ones (OPC 10000-5 §C): Read = 1, Write = 2, EraseExisting = 4,
        /// Append = 8. A write that does not erase starts from the document already stored, so a
        /// partial rewrite does not silently truncate the rest of it.
        /// </summary>
        /// <param name="resource">The resource whose file is opened.</param>
        /// <param name="mode">The FileType open mode bits.</param>
        /// <param name="context">The system context, used to apply the channel-security policy.</param>
        private async ValueTask<OpenMethodStateResult> OnFileOpenAsync(
            ResourceState resource,
            byte mode,
            ISystemContext context)
        {
            bool wantsRead = (mode & kReadMode) != 0;
            bool wantsWrite = (mode & kWriteMode) != 0;
            bool erase = (mode & kEraseExistingMode) != 0;
            bool append = (mode & kAppendMode) != 0;

            if (!wantsRead && !wantsWrite)
            {
                return Failed(StatusCodes.BadInvalidArgument);
            }
            if (wantsRead && wantsWrite)
            {
                // FileType does not define a simultaneous read+write handle.
                return Failed(StatusCodes.BadInvalidArgument);
            }
            if (!wantsWrite && (erase || append))
            {
                // EraseExisting and Append only qualify a write.
                return Failed(StatusCodes.BadInvalidArgument);
            }
            if (erase && append)
            {
                return Failed(StatusCodes.BadInvalidArgument);
            }

            uint handle;
            ResourceFileHandle entry;
            lock (m_gate)
            {
                if (wantsWrite ? !IsWriteChannelSecure(context) : !IsReadChannelSecure(context))
                {
                    return Failed(StatusCodes.BadSecurityModeInsufficient);
                }
                if (wantsWrite && m_fileHandles.Count >= m_maxConcurrentUploads)
                {
                    return Failed(StatusCodes.BadTooManyOperations);
                }

                // Take the slot under the lock so the bound is honoured atomically, then seed the
                // buffer outside it. The caller cannot use the handle before Open returns, so the
                // window in which it is not yet seeded is not observable.
                handle = wantsWrite
                    ? OpenWriteHandle(resource, context)
                    : OpenReadHandle(resource, context);
                entry = m_fileHandles[handle];
                entry.Ready = !wantsWrite || erase;
            }

            if (!entry.Ready)
            {
                ByteString existing = await m_resourceStore
                    .ReadAsync(entry.StoreKey, 0, int.MaxValue)
                    .ConfigureAwait(false);

                lock (m_gate)
                {
                    if (!m_fileHandles.ContainsKey(handle))
                    {
                        // The resource was deleted while the store call was in flight.
                        return Failed(StatusCodes.BadInvalidState);
                    }
                    if (!existing.IsNull)
                    {
                        entry.Buffer.AddRange(existing.Span.ToArray());
                    }
                    entry.Position = append ? entry.Buffer.Count : 0;
                    entry.Ready = true;
                }
            }

            UpdateFileProperties(resource);
            return new OpenMethodStateResult
            {
                ServiceResult = ServiceResult.Good,
                FileHandle = handle
            };

            static OpenMethodStateResult Failed(StatusCode code)
            {
                return new OpenMethodStateResult { ServiceResult = new ServiceResult(code) };
            }
        }

        /// <summary>
        /// Keeps the inherited <c>FileType</c> <c>Size</c> and <c>OpenCount</c> Properties current,
        /// so a client that reads them before opening sees the real values.
        /// </summary>
        /// <param name="resource">The resource whose file Properties are refreshed.</param>
        private void UpdateFileProperties(ResourceState resource)
        {
            lock (m_gate)
            {
                ushort open = 0;
                foreach (ResourceFileHandle handle in m_fileHandles.Values)
                {
                    if (handle.ResourceNodeId == resource.NodeId)
                    {
                        open++;
                    }
                }
                SetValue(resource.OpenCount, open);
            }
        }

        private ValueTask<WriteMethodStateResult> OnFileWriteAsync(
            ResourceState resource,
            uint fileHandle,
            ByteString data,
            ISystemContext context)
        {
            lock (m_gate)
            {
                if (!TryGetHandle(resource, fileHandle, context, out ResourceFileHandle? entry) ||
                    !entry.Writing || !entry.Ready)
                {
                    return new ValueTask<WriteMethodStateResult>(new WriteMethodStateResult
                    {
                        ServiceResult = StatusCodes.BadInvalidState
                    });
                }

                // Any Write — even an empty one — marks the handle as carrying a document, so Close
                // commits it. A handle that was never written to must leave the resource alone.
                entry.Dirty = true;
                if (!data.IsNull && data.Span.Length > 0)
                {
                    ReadOnlySpan<byte> span = data.Span;
                    if (entry.Position + span.Length > m_maxResourceBytes)
                    {
                        return new ValueTask<WriteMethodStateResult>(new WriteMethodStateResult
                        {
                            ServiceResult = StatusCodes.BadRequestTooLarge
                        });
                    }

                    // Overwrite at the cursor and extend past the end, so a write-open that did not
                    // erase replaces only the bytes it covers instead of truncating the document.
                    int overwrite = Math.Min(span.Length, entry.Buffer.Count - entry.Position);
                    for (int i = 0; i < overwrite; i++)
                    {
                        entry.Buffer[entry.Position + i] = span[i];
                    }
                    for (int i = overwrite; i < span.Length; i++)
                    {
                        entry.Buffer.Add(span[i]);
                    }
                    entry.Position += span.Length;
                }
                return new ValueTask<WriteMethodStateResult>(new WriteMethodStateResult
                {
                    ServiceResult = ServiceResult.Good
                });
            }
        }

        private async ValueTask<ReadMethodStateResult> OnFileReadAsync(
            ResourceState resource,
            uint fileHandle,
            int length,
            ISystemContext context)
        {
            ResourceFileHandle? entry;
            int position;
            lock (m_gate)
            {
                if (!TryGetHandle(resource, fileHandle, context, out entry) || entry.Writing)
                {
                    return new ReadMethodStateResult { ServiceResult = StatusCodes.BadInvalidState };
                }
                if (length <= 0)
                {
                    return new ReadMethodStateResult
                    {
                        ServiceResult = ServiceResult.Good,
                        Data = ByteString.From([])
                    };
                }

                // Take the cursor and reserve the range under the lock. Reading Position outside it
                // would let two concurrent Reads on one handle both start at the same offset, so
                // both would return the same bytes and the cursor would then skip a slice.
                position = entry.Position;
                entry.Position = position + length;
            }

            // Read the slice the caller asked for straight out of the store rather than
            // materializing the whole document, which is what the FileType access model implies.
            // StoreKey and Writing are immutable for the life of the handle, so they are safe here.
            ByteString chunk = await m_resourceStore
                .ReadAsync(entry.StoreKey, position, length)
                .ConfigureAwait(false);

            lock (m_gate)
            {
                int read = chunk.IsNull ? 0 : chunk.Length;

                // The reservation was optimistic: a short read at the end of the document has to
                // pull the cursor back to the real end, unless another Read has moved past it since.
                if (entry.Position == position + length)
                {
                    entry.Position = position + read;
                }

                if (chunk.IsNull)
                {
                    return new ReadMethodStateResult
                    {
                        ServiceResult = ServiceResult.Good,
                        Data = ByteString.From([])
                    };
                }

                return new ReadMethodStateResult
                {
                    ServiceResult = ServiceResult.Good,
                    Data = chunk
                };
            }
        }

        /// <summary>
        /// Closes a file handle. Closing a write handle that was written to finalizes the upload:
        /// the content-derived id is computed from the accumulated document (§6.6), the document is
        /// committed to the resource store, and the Opaque content-id fast-path node is published
        /// (§10.1). Closing a read handle, or a write handle nothing was written through, only
        /// releases the handle.
        /// </summary>
        /// <param name="resource">The resource the Method was invoked on.</param>
        /// <param name="fileHandle">The handle to close.</param>
        /// <param name="context">The system context, used to apply the channel-security policy.</param>
        private async ValueTask<CloseMethodStateResult> OnFileCloseAsync(
            ResourceState resource,
            uint fileHandle,
            ISystemContext context)
        {
            ResourceFileHandle? entry;
            lock (m_gate)
            {
                if (!TryGetHandle(resource, fileHandle, context, out entry))
                {
                    return new CloseMethodStateResult { ServiceResult = StatusCodes.BadInvalidState };
                }

                // Committing a document is a mutation and needs an encrypted channel; releasing a
                // read handle only needs whatever the read policy demands, otherwise a read handle
                // opened on a permitted channel could never be closed and would leak.
                bool permitted = entry.Writing && entry.Dirty
                    ? IsWriteChannelSecure(context)
                    : IsReadChannelSecure(context);
                if (!permitted)
                {
                    return new CloseMethodStateResult
                    {
                        ServiceResult = StatusCodes.BadSecurityModeInsufficient
                    };
                }

                m_fileHandles.Remove(fileHandle);
            }

            // A write handle nothing was written through must leave the resource exactly as it was.
            // GetOrCreateResource hands out a write handle even when it returned an existing
            // version, and the caller releases it without writing; committing here would erase that
            // version's document, its content id and its fast-path node.
            if (!entry.Writing || !entry.Dirty)
            {
                UpdateFileProperties(resource);
                return new CloseMethodStateResult { ServiceResult = ServiceResult.Good };
            }

            if (m_contentIdProvider == null)
            {
                UpdateFileProperties(resource);
                return new CloseMethodStateResult { ServiceResult = StatusCodes.BadNotSupported };
            }

            byte[] document = [.. entry.Buffer];
            string format = resource.Format?.Value ?? kDefaultFormat;
            ByteString contentId = m_contentIdProvider.ComputeContentId(format, document);

            // Replace the stored document wholesale. A plain write at offset 0 leaves any trailing
            // bytes of a larger previous version in place, which would corrupt the resource.
            _ = await m_resourceStore.DeleteAsync(entry.StoreKey).ConfigureAwait(false);
            await m_resourceStore.WriteAsync(entry.StoreKey, 0, ByteString.From(document))
                .ConfigureAwait(false);

            bool stillRegistered;
            List<XRegistryEventChange>? changes = null;
            lock (m_gate)
            {
                // The resource can have been deleted while the store call was in flight. Its
                // fast-path reference is already released, so publishing here would strand a node
                // that nothing can ever release and re-create a document the delete removed.
                stillRegistered = IsRegisteredLocked(resource);
                if (stillRegistered)
                {
                    string xid = contentId.ToHexString();
                    string previousXid = resource.Xid?.Value ?? string.Empty;
                    if (!string.Equals(previousXid, xid, StringComparison.Ordinal))
                    {
                        // The document changed, so this resource no longer resolves to its previous
                        // content id; drop that reference before taking one on the new id.
                        if (previousXid.Length > 0)
                        {
                            ReleaseFastPathNode(previousXid);
                        }
                        PublishFastPathNode(contentId, xid, document);
                    }

                    SetValue(resource.Xid, xid);
                    SetValue(resource.Format, format);
                    SetValue(resource.ModifiedAt, DateTimeUtc.Now);
                    SetValue(resource.Size, (ulong)document.Length);
                    if (resource.Epoch != null)
                    {
                        resource.Epoch.Value++;
                    }
                    if (m_eventsEnabled &&
                        TryGetResourceKeyLocked(resource, out ResourceKey key))
                    {
                        var logicalKey = new ResourceIdentityKey(key.GroupNodeId, key.ResourceId);
                        uint epoch = resource.Epoch?.Value ?? 0;
                        uint metaEpoch = m_resourceMetaEpochs.GetValueOrDefault(logicalKey);
                        ImmutableArray<string> changed =
                        [
                            "epoch",
                            "modifiedat",
                            m_resourceDocumentAttributeName
                        ];
                        changes =
                        [
                            new XRegistryEventChange(
                                XRegistryEventKind.VersionUpdated,
                                VersionSubject(key),
                                resource.NodeId,
                                epoch,
                                Changed: changed)
                        ];
                        if (m_defaultVersions.TryGetValue(logicalKey, out string? defaultVersion) &&
                            string.Equals(defaultVersion, key.VersionId, StringComparison.Ordinal))
                        {
                            changes.Add(new XRegistryEventChange(
                                XRegistryEventKind.ResourceUpdated,
                                ResourceSubject(key),
                                resource.NodeId,
                                epoch,
                                metaEpoch,
                                changed));
                        }
                    }
                }
            }

            if (!stillRegistered)
            {
                // The delete already ran its own store cleanup, so the bytes written above would be
                // orphaned. Store keys are the resource NodeId and instance ids only ever increase,
                // so the key is never reused and nothing would ever collect them.
                _ = await m_resourceStore.DeleteAsync(entry.StoreKey).ConfigureAwait(false);
                return new CloseMethodStateResult { ServiceResult = StatusCodes.BadInvalidState };
            }

            UpdateFileProperties(resource);
            ReportChanges(changes);
            return new CloseMethodStateResult { ServiceResult = ServiceResult.Good };
        }

        /// <summary>
        /// Releases every file handle a closing session still holds. Without this an abandoned
        /// session's handles keep consuming the upload budget for the lifetime of the server.
        /// </summary>
        /// <param name="context">The operation context.</param>
        /// <param name="sessionId">The session that is closing.</param>
        /// <param name="deleteSubscriptions">Whether the session's subscriptions are deleted.</param>
        public override void SessionClosing(
            OperationContext context,
            NodeId sessionId,
            bool deleteSubscriptions)
        {
            lock (m_gate)
            {
                var orphaned = new List<uint>();
                foreach (KeyValuePair<uint, ResourceFileHandle> handle in m_fileHandles)
                {
                    if (handle.Value.SessionId == sessionId)
                    {
                        orphaned.Add(handle.Key);
                    }
                }
                foreach (uint handle in orphaned)
                {
                    m_fileHandles.Remove(handle);
                }
            }

            base.SessionClosing(context, sessionId, deleteSubscriptions);
        }

        /// <summary>
        /// Tests whether a resource is still registered. The caller holds <see cref="m_gate"/>.
        /// </summary>
        /// <param name="resource">The resource to test.</param>
        private bool IsRegisteredLocked(ResourceState resource)
        {
            foreach (ResourceState registered in m_resources.Values)
            {
                if (ReferenceEquals(registered, resource))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Publishes the Opaque content-id node so a decoder that received the id on the wire
        /// reaches the document in a single Read, and takes a reference on it. The node is
        /// content-addressed and therefore <b>shared</b> by every resource whose document has the
        /// same bytes, so its lifetime is refcounted rather than tied to any one resource. The
        /// caller holds <see cref="m_gate"/>.
        /// </summary>
        /// <param name="contentId">The content-derived id.</param>
        /// <param name="xid">The hex form of <paramref name="contentId"/>, used as the ref key.</param>
        /// <param name="document">The document bytes published as the node's value.</param>
        private void PublishFastPathNode(ByteString contentId, string xid, byte[] document)
        {
            m_fastPathReferences.TryGetValue(xid, out int references);
            m_fastPathReferences[xid] = references + 1;

            ushort ns = (ushort)Server.NamespaceUris.GetIndex(m_namespaceUri);
            var fastPathNodeId = new NodeId(contentId, ns);
            if (Find(fastPathNodeId) != null)
            {
                // Another resource already published the identical document; this call only added
                // a reference. De-duplication is the point of a content-derived identity.
                return;
            }

            var node = new BaseDataVariableState(null)
            {
                NodeId = fastPathNodeId,
                BrowseName = new QualifiedName("RegisteredResource", ns),
                DisplayName = new LocalizedText("RegisteredResource"),
                TypeDefinitionId = Opc.Ua.VariableTypeIds.BaseDataVariableType,
                ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasComponent,
                DataType = Opc.Ua.DataTypeIds.ByteString,
                ValueRank = ValueRanks.Scalar,
                AccessLevel = AccessLevels.CurrentRead,
                UserAccessLevel = AccessLevels.CurrentRead,
                Historizing = false,
                Value = new Variant(ByteString.From(document))
            };

            AddPredefinedNode(SystemContext, node);
        }

        /// <summary>
        /// Drops one reference on a content-addressed fast-path node, unpublishing it only once the
        /// last resource that resolves to those bytes has let it go. The caller holds
        /// <see cref="m_gate"/>.
        /// </summary>
        /// <param name="xid">The hex content id whose reference is released.</param>
        private void ReleaseFastPathNode(string xid)
        {
            if (!m_fastPathReferences.TryGetValue(xid, out int references))
            {
                return;
            }
            if (references > 1)
            {
                m_fastPathReferences[xid] = references - 1;
                return;
            }

            m_fastPathReferences.Remove(xid);
            var contentId = ByteString.FromHexString(xid);
            if (!contentId.IsNull)
            {
                ushort ns = (ushort)Server.NamespaceUris.GetIndex(m_namespaceUri);
                DeleteNode(SystemContext, new NodeId(contentId, ns));
            }
        }

        private uint OpenWriteHandle(ResourceState resource, ISystemContext? context = null)
        {
            uint handle = ++m_nextFileHandle;
            m_fileHandles[handle] = new ResourceFileHandle(
                StoreKeyOf(resource), resource.NodeId, writing: true)
            {
                SessionId = SessionIdOf(context),
                Ready = true
            };
            return handle;
        }

        private uint OpenReadHandle(ResourceState resource, ISystemContext? context = null)
        {
            uint handle = ++m_nextFileHandle;
            m_fileHandles[handle] = new ResourceFileHandle(
                StoreKeyOf(resource), resource.NodeId, writing: false)
            {
                SessionId = SessionIdOf(context),
                Ready = true
            };
            return handle;
        }

        /// <summary>
        /// Gets the session a call arrived on, or a null NodeId for an in-process call.
        /// </summary>
        /// <param name="context">The system context of the call.</param>
        private static NodeId SessionIdOf(ISystemContext? context)
        {
            return context is ISessionSystemContext { SessionId: { IsNull: false } sessionId }
                ? sessionId
                : NodeId.Null;
        }

        /// <summary>
        /// Resolves a file handle for a call made on <paramref name="resource"/>. The handle has to
        /// belong to that resource: handles are server-wide and sequential, so without this check a
        /// caller could drive another resource's document — or another caller's in-flight upload —
        /// through its own resource's Methods. The caller holds <see cref="m_gate"/>.
        /// </summary>
        /// <param name="resource">The resource whose Method was invoked.</param>
        /// <param name="fileHandle">The handle supplied by the caller.</param>
        /// <param name="context">The system context, used to check the owning session.</param>
        /// <param name="entry">The resolved handle when it is valid for this resource.</param>
        private bool TryGetHandle(
            ResourceState resource,
            uint fileHandle,
            ISystemContext context,
            [NotNullWhen(true)] out ResourceFileHandle? entry)
        {
            if (!m_fileHandles.TryGetValue(fileHandle, out entry))
            {
                return false;
            }
            if (entry.ResourceNodeId != resource.NodeId || entry.SessionId != SessionIdOf(context))
            {
                entry = null;
                return false;
            }
            return true;
        }

        private static string StoreKeyOf(ResourceState resource)
        {
            return resource.NodeId.ToString() ?? string.Empty;
        }

        /// <summary>
        /// Assigns the next free version identifier for a resource. The counter is scoped to the
        /// owning group, and the candidate is advanced past any version the caller created
        /// explicitly, so an auto-assigned id can never collide with an existing one. The caller
        /// holds <see cref="m_gate"/>.
        /// </summary>
        /// <param name="groupNodeId">The group that owns the resource.</param>
        /// <param name="resourceId">The resource whose next version is assigned.</param>
        private string NextVersionId(NodeId groupNodeId, string resourceId)
        {
            var counterKey = new VersionCounterKey(groupNodeId, resourceId);
            m_versionCounters.TryGetValue(counterKey, out uint current);

            string candidate;
            do
            {
                current++;
                candidate = current.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            while (m_resources.ContainsKey(new ResourceKey(groupNodeId, resourceId, candidate)));

            m_versionCounters[counterKey] = current;
            return candidate;
        }

        private void PublishGroupCreated(GroupState group)
        {
            if (!m_eventsEnabled || m_registry is null)
            {
                return;
            }
            List<XRegistryEventChange> changes;
            lock (m_gate)
            {
                uint registryEpoch = BumpEntity(m_registry.Epoch, m_registry.ModifiedAt);
                changes =
                [
                    new XRegistryEventChange(
                        XRegistryEventKind.GroupCreated,
                        GroupSubject(group.GroupId?.Value ?? string.Empty),
                        group.NodeId,
                        group.Epoch?.Value),
                    new XRegistryEventChange(
                        XRegistryEventKind.RegistryUpdated,
                        RegistrySubject(),
                        m_registry.NodeId,
                        registryEpoch,
                        Changed: CollectionChanged(m_groupsAttributeName))
                ];
            }
            ReportChanges(changes);
        }

        private void PublishResourceCreated(
            GroupState group,
            ResourceState resource,
            bool firstVersion)
        {
            if (!m_eventsEnabled)
            {
                return;
            }
            List<XRegistryEventChange> changes;
            lock (m_gate)
            {
                if (!TryGetResourceKeyLocked(resource, out ResourceKey key))
                {
                    return;
                }
                uint epoch = resource.Epoch?.Value ?? 0;
                uint metaEpoch = resource.MetaEpoch?.Value ?? 0;
                changes =
                [
                    new XRegistryEventChange(
                        XRegistryEventKind.VersionCreated,
                        VersionSubject(key),
                        resource.NodeId,
                        epoch)
                ];
                if (firstVersion)
                {
                    uint groupEpoch = BumpEntity(group.Epoch, group.ModifiedAt);
                    changes.Add(new XRegistryEventChange(
                        XRegistryEventKind.ResourceCreated,
                        ResourceSubject(key),
                        resource.NodeId,
                        epoch,
                        metaEpoch));
                    changes.Add(new XRegistryEventChange(
                        XRegistryEventKind.GroupUpdated,
                        GroupSubject(group.GroupId?.Value ?? string.Empty),
                        group.NodeId,
                        groupEpoch,
                        Changed: CollectionChanged(m_resourcesAttributeName)));
                }
                else
                {
                    changes.Add(new XRegistryEventChange(
                        XRegistryEventKind.ResourceUpdated,
                        ResourceSubject(key),
                        resource.NodeId,
                        epoch,
                        metaEpoch,
                        VersionCollectionChanged()));
                }
            }
            ReportChanges(changes);
        }

        private void PublishRegistryLabelsUpdated()
        {
            if (!m_eventsEnabled || m_registry is null)
            {
                return;
            }
            SetValue(m_registry.ModifiedAt, DateTimeUtc.Now);
            ReportChanges(
            [
                new XRegistryEventChange(
                    XRegistryEventKind.RegistryUpdated,
                    RegistrySubject(),
                    m_registry.NodeId,
                    m_registry.Epoch?.Value,
                    Changed: ImmutableArray.Create("epoch", "labels", "modifiedat"))
            ]);
        }

        private void PublishGroupLabelsUpdated(GroupState group)
        {
            if (!m_eventsEnabled)
            {
                return;
            }
            SetValue(group.ModifiedAt, DateTimeUtc.Now);
            ReportChanges(
            [
                new XRegistryEventChange(
                    XRegistryEventKind.GroupUpdated,
                    GroupSubject(group.GroupId?.Value ?? string.Empty),
                    group.NodeId,
                    group.Epoch?.Value,
                    Changed: ImmutableArray.Create("epoch", "labels", "modifiedat"))
            ]);
        }

        private void PublishResourceLabelsUpdated(ResourceState resource)
        {
            if (!m_eventsEnabled)
            {
                return;
            }
            XRegistryEventChange? change = null;
            lock (m_gate)
            {
                if (TryGetResourceKeyLocked(resource, out ResourceKey key))
                {
                    var logicalKey = new ResourceIdentityKey(key.GroupNodeId, key.ResourceId);
                    uint metaEpoch = resource.MetaEpoch?.Value ?? 0;
                    m_resourceMetaEpochs[logicalKey] = metaEpoch;
                    ApplyMetaEpochLocked(logicalKey, metaEpoch);
                    SetValue(resource.MetaModifiedAt, DateTimeUtc.Now);
                    change = new XRegistryEventChange(
                        XRegistryEventKind.ResourceUpdated,
                        ResourceSubject(key),
                        resource.NodeId,
                        resource.Epoch?.Value,
                        metaEpoch,
                        ImmutableArray.Create("meta.epoch", "meta.labels", "meta.modifiedat"));
                }
            }
            if (change is not null)
            {
                ReportChanges([change]);
            }
        }

        private List<XRegistryEventChange> BuildResourceDeletionChangesLocked(
            GroupState group,
            ResourceKey deletedKey,
            ResourceState deleted,
            ResourceIdentityKey logicalKey)
        {
            var changes = new List<XRegistryEventChange>
            {
                new(
                    XRegistryEventKind.VersionDeleted,
                    VersionSubject(deletedKey),
                    deleted.NodeId)
            };
            List<KeyValuePair<ResourceKey, ResourceState>> remaining = m_resources
                .Where(entry =>
                    entry.Key.GroupNodeId == deletedKey.GroupNodeId &&
                    string.Equals(entry.Key.ResourceId, deletedKey.ResourceId, StringComparison.Ordinal))
                .ToList();
            if (remaining.Count == 0)
            {
                uint groupEpoch = BumpEntity(group.Epoch, group.ModifiedAt);
                changes.Add(new XRegistryEventChange(
                    XRegistryEventKind.ResourceDeleted,
                    ResourceSubject(deletedKey),
                    deleted.NodeId));
                changes.Add(new XRegistryEventChange(
                    XRegistryEventKind.GroupUpdated,
                    GroupSubject(group.GroupId?.Value ?? string.Empty),
                    group.NodeId,
                    groupEpoch,
                    Changed: CollectionChanged(m_resourcesAttributeName)));
                return changes;
            }

            uint metaEpoch = m_resourceMetaEpochs.GetValueOrDefault(logicalKey) + 1u;
            m_resourceMetaEpochs[logicalKey] = metaEpoch;
            if (!m_defaultVersions.TryGetValue(logicalKey, out string? defaultVersion) ||
                string.Equals(defaultVersion, deletedKey.VersionId, StringComparison.Ordinal))
            {
                defaultVersion = remaining
                    .OrderBy(entry => entry.Key.VersionId, StringComparer.Ordinal)
                    .Last().Key.VersionId;
                m_defaultVersions[logicalKey] = defaultVersion;
            }
            ApplyMetaEpochLocked(logicalKey, metaEpoch);
            KeyValuePair<ResourceKey, ResourceState> current =
                remaining.First(entry => entry.Key.VersionId == defaultVersion);
            changes.Add(new XRegistryEventChange(
                XRegistryEventKind.ResourceUpdated,
                ResourceSubject(deletedKey),
                current.Value.NodeId,
                current.Value.Epoch?.Value,
                metaEpoch,
                VersionCollectionChanged()));
            return changes;
        }

        private void ApplyMetaEpochLocked(ResourceIdentityKey key, uint metaEpoch)
        {
            DateTimeUtc now = DateTimeUtc.Now;
            foreach (KeyValuePair<ResourceKey, ResourceState> entry in m_resources)
            {
                if (entry.Key.GroupNodeId == key.GroupNodeId &&
                    string.Equals(entry.Key.ResourceId, key.ResourceId, StringComparison.Ordinal))
                {
                    SetValue(entry.Value.MetaEpoch, metaEpoch);
                    SetValue(entry.Value.MetaModifiedAt, now);
                }
            }
        }

        private bool TryGetResourceKeyLocked(ResourceState resource, out ResourceKey key)
        {
            foreach (KeyValuePair<ResourceKey, ResourceState> entry in m_resources)
            {
                if (ReferenceEquals(entry.Value, resource))
                {
                    key = entry.Key;
                    return true;
                }
            }
            key = default;
            return false;
        }

        private uint BumpEntity(
            PropertyState<uint>? epoch,
            PropertyState<DateTimeUtc>? modifiedAt)
        {
            BumpEpoch(epoch);
            SetValue(modifiedAt, DateTimeUtc.Now);
            return epoch?.Value ?? 0;
        }

        private ImmutableArray<string> CollectionChanged(string collection)
        {
            return
            [
                collection,
                collection + "count",
                "epoch",
                "modifiedat"
            ];
        }

        private ImmutableArray<string> VersionCollectionChanged()
        {
            return
            [
                "meta.epoch",
                "meta.modifiedat",
                "versions",
                "versionscount"
            ];
        }

        private string RegistrySubject()
        {
            return m_registry?.Xid?.Value ?? m_registryId;
        }

        private string GroupSubject(string groupId)
        {
            return $"/{m_groupsAttributeName}/{groupId}";
        }

        private string ResourceSubject(ResourceKey key)
        {
            return $"{GroupSubject(GroupIdOf(key.GroupNodeId))}/{m_resourcesAttributeName}/{key.ResourceId}";
        }

        private string VersionSubject(ResourceKey key)
        {
            return $"{ResourceSubject(key)}/versions/{key.VersionId}";
        }

        private string GroupIdOf(NodeId groupNodeId)
        {
            return m_groupsByNodeId.TryGetValue(groupNodeId, out GroupState? group)
                ? group.GroupId?.Value ?? string.Empty
                : string.Empty;
        }

        private void ReportChanges(IEnumerable<XRegistryEventChange>? changes)
        {
            if (m_eventEmitter is not null && m_registry is not null && changes is not null)
            {
                m_eventEmitter.Report(m_registry, changes);
            }
        }

        /// <summary>
        /// Identity of a version counter: the counter is per resource within a group, not global.
        /// </summary>
        /// <param name="GroupNodeId">The group that owns the resource.</param>
        /// <param name="ResourceId">The resource id.</param>
        private readonly record struct VersionCounterKey(NodeId GroupNodeId, string ResourceId);

        private readonly record struct ResourceIdentityKey(NodeId GroupNodeId, string ResourceId);

        /// <summary>
        /// Identity of a resource version within a group.
        /// </summary>
        private readonly record struct ResourceKey(NodeId GroupNodeId, string ResourceId, string VersionId);

        /// <summary>
        /// An open file handle on a resource: a bounded upload buffer for a write handle, or a
        /// cursor over the stored document for a read handle.
        /// </summary>
        private sealed class ResourceFileHandle(string storeKey, NodeId resourceNodeId, bool writing)
        {
            public string StoreKey { get; } = storeKey;

            /// <summary>
            /// The resource the handle was opened on. A handle is only valid on that resource, so a
            /// caller cannot drive one resource's document through another resource's Methods.
            /// </summary>
            public NodeId ResourceNodeId { get; } = resourceNodeId;

            /// <summary>
            /// The session that opened the handle, or a null NodeId for an in-process call (the
            /// server's own bootstrap). A handle is only valid to the session that owns it.
            /// </summary>
            public NodeId SessionId { get; set; }

            public bool Writing { get; } = writing;

            /// <summary>
            /// Whether the handle has finished being seeded from the store. A write handle that does
            /// not erase starts from the stored document, which is read outside the lock.
            /// </summary>
            public bool Ready { get; set; }

            /// <summary>
            /// Whether anything was actually written through this handle. Closing a write handle
            /// that was never written to must not commit an empty document over the existing one.
            /// </summary>
            public bool Dirty { get; set; }

            public List<byte> Buffer { get; } = [];
            public ByteString Content { get; set; }
            public int Position { get; set; }
        }

        /// <summary>
        /// Tests whether the caller's secure channel is good enough to mutate the registry. A
        /// resource document and its content-derived identity are integrity-critical, so a write is
        /// only accepted over a <c>SignAndEncrypt</c> channel. A context that carries no channel at
        /// all is an in-process call (the server's own bootstrap or a test) and is allowed.
        /// </summary>
        /// <param name="context">The system context of the call.</param>
        internal static bool IsWriteChannelSecure(ISystemContext context)
        {
            return SecurityModeOf(context) is not MessageSecurityMode mode ||
                mode == MessageSecurityMode.SignAndEncrypt;
        }

        /// <summary>
        /// Tests whether the caller's secure channel is good enough to read a resource. Reads are
        /// allowed on any channel unless
        /// <see cref="XRegistryServerOptions.RequireEncryptionForReads"/> is set.
        /// </summary>
        /// <param name="context">The system context of the call.</param>
        internal bool IsReadChannelSecure(ISystemContext context)
        {
            return !m_requireEncryptionForReads || IsWriteChannelSecure(context);
        }

        private static MessageSecurityMode? SecurityModeOf(ISystemContext context)
        {
            if (context is SessionSystemContext { OperationContext: OperationContext op } &&
                op.ChannelContext?.EndpointDescription is EndpointDescription endpoint)
            {
                return endpoint.SecurityMode;
            }
            return null;
        }

        private static ValueTask<DeleteMethodStateResult> InsecureDelete()
        {
            return new ValueTask<DeleteMethodStateResult>(new DeleteMethodStateResult
            {
                ServiceResult = StatusCodes.BadSecurityModeInsufficient
            });
        }

        /// <summary>
        /// Applies the model's optimistic-concurrency check (§6.6). A non-zero
        /// <paramref name="expectedEpoch"/> that does not equal the entity's current epoch fails the
        /// call and makes no change; <c>0</c> disables the check, which is how a caller deliberately
        /// forces the operation without having read the entity first.
        /// </summary>
        /// <param name="epoch">The entity's epoch, when it exposes one.</param>
        /// <param name="expectedEpoch">The epoch the caller last observed, or 0 to force.</param>
        private static bool IsEpochCurrent(PropertyState<uint>? epoch, uint expectedEpoch)
        {
            return expectedEpoch == 0 || epoch == null || epoch.Value == expectedEpoch;
        }

        private static void SetValue<T>(PropertyState<T>? property, T value)
        {
            if (property != null)
            {
                property.Value = value;
            }
        }

        /// <summary>
        /// Binds the <c>AttributesType</c> Methods on a <c>Labels</c> Object. Both mutate the owning
        /// node, so both take the owner's epoch as an optimistic-concurrency check.
        /// </summary>
        /// <param name="labels">The Labels Object, when the owner exposes one.</param>
        /// <param name="epoch">Accessor for the owning node's epoch.</param>
        /// <param name="changed">Callback invoked after a successful effective mutation.</param>
        private void BindAttributeMethods(
            AttributesState? labels,
            Func<PropertyState<uint>?> epoch,
            Action? changed = null)
        {
            if (labels == null)
            {
                return;
            }

            labels.AddAddAttribute(SystemContext);
            labels.AddRemoveAttribute(SystemContext);

            if (labels.AddAttribute != null)
            {
                labels.AddAttribute.OnCallAsync = (ctx, m, id, key, value, expectedEpoch, ct) =>
                    IsWriteChannelSecure(ctx)
                        ? OnAddAttributeAsync(labels, epoch(), key, value, expectedEpoch, changed)
                        : new ValueTask<AddAttributeMethodStateResult>(
                            new AddAttributeMethodStateResult
                            {
                                ServiceResult = StatusCodes.BadSecurityModeInsufficient
                            });
            }
            if (labels.RemoveAttribute != null)
            {
                labels.RemoveAttribute.OnCallAsync = (ctx, m, id, key, expectedEpoch, ct) =>
                    IsWriteChannelSecure(ctx)
                        ? OnRemoveAttributeAsync(labels, epoch(), key, expectedEpoch, changed)
                        : new ValueTask<RemoveAttributeMethodStateResult>(
                            new RemoveAttributeMethodStateResult
                            {
                                ServiceResult = StatusCodes.BadSecurityModeInsufficient
                            });
            }
        }

        /// <summary>
        /// Handles <c>AttributesType.AddAttribute(Key, Value, ExpectedEpoch)</c>, adding or
        /// replacing a label on the owning node.
        /// </summary>
        internal ValueTask<AddAttributeMethodStateResult> OnAddAttributeAsync(
            AttributesState labels,
            PropertyState<uint>? epoch,
            string key,
            string value,
            uint expectedEpoch,
            Action? changed = null)
        {
            if (string.IsNullOrEmpty(key))
            {
                return new ValueTask<AddAttributeMethodStateResult>(
                    new AddAttributeMethodStateResult { ServiceResult = StatusCodes.BadInvalidArgument });
            }

            lock (m_gate)
            {
                if (!IsEpochCurrent(epoch, expectedEpoch))
                {
                    return new ValueTask<AddAttributeMethodStateResult>(
                        new AddAttributeMethodStateResult { ServiceResult = StatusCodes.BadInvalidState });
                }

                ushort ns = (ushort)Server.NamespaceUris.GetIndex(m_namespaceUri);
                var browseName = new QualifiedName(key, ns);
                if (labels.FindChild(SystemContext, browseName) is PropertyState<string> existing)
                {
                    if (string.Equals(existing.Value, value, StringComparison.Ordinal))
                    {
                        return new ValueTask<AddAttributeMethodStateResult>(
                            new AddAttributeMethodStateResult { ServiceResult = ServiceResult.Good });
                    }
                    existing.Value = value;
                }
                else
                {
                    PropertyState<string> attribute = PropertyState<string>.With<VariantBuilder>(labels, value);
                    attribute.NodeId = new NodeId(m_nextInstanceId++, ns);
                    attribute.BrowseName = browseName;
                    attribute.DisplayName = new LocalizedText(key);
                    attribute.ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasProperty;
                    attribute.TypeDefinitionId = Opc.Ua.VariableTypeIds.PropertyType;
                    attribute.DataType = Opc.Ua.DataTypeIds.String;
                    attribute.ValueRank = ValueRanks.Scalar;
                    attribute.AccessLevel = AccessLevels.CurrentRead;
                    attribute.UserAccessLevel = AccessLevels.CurrentRead;
                    labels.AddChild(attribute);
                    AddPredefinedNode(SystemContext, attribute);
                }

                BumpEpoch(epoch);
            }
            changed?.Invoke();
            return new ValueTask<AddAttributeMethodStateResult>(
                new AddAttributeMethodStateResult { ServiceResult = ServiceResult.Good });
        }

        /// <summary>
        /// Handles <c>AttributesType.RemoveAttribute(Key, ExpectedEpoch)</c>.
        /// </summary>
        internal ValueTask<RemoveAttributeMethodStateResult> OnRemoveAttributeAsync(
            AttributesState labels,
            PropertyState<uint>? epoch,
            string key,
            uint expectedEpoch,
            Action? changed = null)
        {
            lock (m_gate)
            {
                if (!IsEpochCurrent(epoch, expectedEpoch))
                {
                    return new ValueTask<RemoveAttributeMethodStateResult>(
                        new RemoveAttributeMethodStateResult
                        {
                            ServiceResult = StatusCodes.BadInvalidState
                        });
                }

                ushort ns = (ushort)Server.NamespaceUris.GetIndex(m_namespaceUri);
                if (labels.FindChild(SystemContext, new QualifiedName(key, ns)) is not BaseInstanceState attribute)
                {
                    return new ValueTask<RemoveAttributeMethodStateResult>(
                        new RemoveAttributeMethodStateResult { ServiceResult = StatusCodes.BadNotFound });
                }

                labels.RemoveChild(attribute);
                DeleteNode(SystemContext, attribute.NodeId);
                BumpEpoch(epoch);
            }
            changed?.Invoke();
            return new ValueTask<RemoveAttributeMethodStateResult>(
                new RemoveAttributeMethodStateResult { ServiceResult = ServiceResult.Good });
        }

        private static void BumpEpoch(PropertyState<uint>? epoch)
        {
            if (epoch != null)
            {
                epoch.Value++;
            }
        }


        private readonly Lock m_gate = new();
        private readonly Dictionary<string, GroupState> m_groups = [];
        private readonly Dictionary<NodeId, GroupState> m_groupsByNodeId = [];
        private readonly Dictionary<ResourceKey, ResourceState> m_resources = [];
        private readonly Dictionary<uint, ResourceFileHandle> m_fileHandles = [];
        private readonly Dictionary<VersionCounterKey, uint> m_versionCounters = [];
        private readonly Dictionary<ResourceIdentityKey, uint> m_resourceMetaEpochs = [];
        private readonly Dictionary<ResourceIdentityKey, string> m_defaultVersions = [];
        private readonly Dictionary<string, int> m_fastPathReferences = [];
        private readonly string m_namespaceUri;
        private readonly string m_registryBrowseName;
        private readonly string m_registryId;
        private readonly string m_specVersion;
        private readonly IResourceContentIdProvider? m_contentIdProvider;
        private readonly IXRegistryResourceStore m_resourceStore;
        private readonly int m_maxConcurrentUploads;
        private readonly int m_maxResourceBytes;
        private readonly int m_maxRegisteredResources;
        private readonly bool m_requireEncryptionForReads;
        private readonly bool m_eventsEnabled;
        private readonly string m_eventSourceUrl;
        private readonly string m_groupsAttributeName;
        private readonly string m_resourcesAttributeName;
        private readonly string m_resourceDocumentAttributeName;
        private XRegistryEventEmitter? m_eventEmitter;
        private RegistryState? m_registry;
        private uint m_nextInstanceId = XRegistryWellKnown.FirstDynamicInstance;
        private uint m_nextFileHandle;
        private int m_registeredResourceCount;
        private const byte kReadMode = 1;
        private const byte kWriteMode = 2;
        private const byte kEraseExistingMode = 4;
        private const byte kAppendMode = 8;
        private const string kDefaultFormat = "avro";
    }
}
