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
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server;

namespace Opc.Ua.XRegistry.Server
{
    /// <summary>
    /// Serves the xRegistry registration lifecycle (§5.2) and auto-bootstrap (§10.1): a writer
    /// creates a resource, writes the document bytes, and closes it; on <c>Close</c> the server
    /// computes an independent content key from the document via the configured
    /// <see cref="IResourceContentIdProvider"/> (§6.6) and makes the document reachable by its
    /// Opaque content-id NodeId (§6.4), without changing the Version's structural <c>Xid</c>.
    /// </summary>
    /// <remarks>
    /// Deliberately left unsealed: subclassing is the server-side extension seam a domain registry
    /// uses to serve its own companion model on top of the base one.
    /// </remarks>
    public class XRegistryRegistrationNodeManager : AsyncCustomNodeManager
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
            : base(
                server,
                configuration,
                server.Telemetry.CreateLogger<XRegistryRegistrationNodeManager>(),
                (options ?? new XRegistryServerOptions()).RegistryNamespaceUri)
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
        /// <param name="cancellationToken">Cancels model loading.</param>
        /// <returns>The predefined nodes of the xRegistry base model.</returns>
        protected override ValueTask<NodeStateCollection> LoadPredefinedNodesAsync(
            ISystemContext context,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<NodeStateCollection>(
                new NodeStateCollection().AddOpcUaXRegistry(context));
        }

        /// <summary>
        /// Materializes the registry root from the compiled model. Groups and resource versions are
        /// then created at runtime through the model's own lifecycle Methods.
        /// </summary>
        /// <param name="externalReferences">External reference sink (unused).</param>
        /// <param name="cancellationToken">Cancels address-space creation.</param>
        public override async ValueTask CreateAddressSpaceAsync(
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken = default)
        {
            using OperationLease operation = BeginOperation(cancellationToken, requireAddressSpace: false);
            try
            {
                await XRegistryNodeManagerStartup.RunAsync(
                    externalReferences,
                    InitializeAddressSpaceAsync,
                    RollbackStartupAsync,
                    cancellationToken).ConfigureAwait(false);
                lock (m_lifetimeGate)
                {
                    m_addressSpaceReady = true;
                }
            }
            finally
            {
                lock (m_lifetimeGate)
                {
                    m_starting = false;
                }
            }
        }

        private async ValueTask InitializeAddressSpaceAsync(
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken)
        {
            await using (await EnterGateAsync(cancellationToken).ConfigureAwait(false))
            {
                await InitializeAddressSpaceLockedAsync(externalReferences, cancellationToken).ConfigureAwait(false);
            }
        }

        private async ValueTask InitializeAddressSpaceLockedAsync(
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken)
        {
            await base.CreateAddressSpaceAsync(externalReferences, cancellationToken)
                .ConfigureAwait(false);

            ushort ns = (ushort)Server.NamespaceUris.GetIndex(m_namespaceUri);

            await CreateRegistryRootAsync(ns, cancellationToken).ConfigureAwait(false);
            if (m_eventsEnabled && m_registry is not null)
            {
                m_eventEmitter = new XRegistryEventEmitter(SystemContext, m_eventSourceUrl);
                m_registry.EventNotifier = EventNotifiers.SubscribeToEvents;
                await AddRootNotifierAsync(m_registry, cancellationToken).ConfigureAwait(false);
                if (!externalReferences.TryGetValue(
                        Ua.ObjectIds.Server,
                        out IList<IReference>? serverReferences))
                {
                    externalReferences[Ua.ObjectIds.Server] =
                        serverReferences = [];
                }
                serverReferences.Add(new NodeStateReference(
                    ReferenceTypeIds.HasNotifier,
                    false,
                    m_registry.NodeId));
            }
        }

        private async ValueTask RollbackStartupAsync(CancellationToken cancellationToken)
        {
            await using (await EnterGateAsync(cancellationToken).ConfigureAwait(false))
            {
                ClearRuntimeState();
            }
            await base.DeleteAddressSpaceAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public override async ValueTask DeleteAddressSpaceAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_lifetimeGate)
            {
                if (m_disposeRequested)
                {
                    throw new ObjectDisposedException(nameof(XRegistryRegistrationNodeManager));
                }
                m_stopping = true;
                m_activeTeardowns++;
                if (m_activeOperations == 0)
                {
                    m_operationsDrained.TrySetResult(true);
                }
            }

            try
            {
                await m_operationsDrained.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                await RetryDeletionsAsync(cancellationToken).ConfigureAwait(false);
                await using (await EnterGateAsync(cancellationToken).ConfigureAwait(false))
                {
                    ClearRuntimeState();
                }
                await base.DeleteAddressSpaceAsync(CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                bool dispose;
                lock (m_lifetimeGate)
                {
                    m_activeTeardowns--;
                    dispose = ReserveDisposalLocked();
                }
                if (dispose)
                {
                    DisposeResources();
                }
            }
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            if (!disposing)
            {
                base.Dispose(false);
                return;
            }

            bool dispose;
            lock (m_lifetimeGate)
            {
                m_stopping = true;
                m_disposeRequested = true;
                dispose = ReserveDisposalLocked();
            }
            if (dispose)
            {
                DisposeResources();
            }
        }

        /// <summary>
        /// Materializes the registry root through the source-generated <c>RegistryType</c> factory
        /// so the instance carries the type's mandatory children — a bare
        /// <c>new RegistryState(parent)</c> would omit them and leave the model's group lifecycle
        /// Methods unbound.
        /// </summary>
        /// <param name="ns">The registry namespace index.</param>
        /// <param name="cancellationToken">Cancels root publication.</param>
        private async ValueTask CreateRegistryRootAsync(ushort ns, CancellationToken cancellationToken)
        {
            RegistryState registry = SystemContext.CreateInstanceOfRegistryType(
                parent: null!,
                new QualifiedName(m_registryBrowseName, ns));

            registry.NodeId = new NodeId(XRegistryWellKnown.RegistryObject, ns);
            registry.DisplayName = new LocalizedText(m_registryBrowseName);

            // Everything except RegistryId is Optional on the type, so the factory does not
            // materialize it — including the group lifecycle Methods this manager binds.
            registry.AddSpecVersion(SystemContext)
                .AddXid(SystemContext)
                .AddEpoch(SystemContext)
                .AddCreatedAt(SystemContext)
                .AddModifiedAt(SystemContext)
                .AddCreateGroup(SystemContext)
                .AddGetOrCreateGroup(SystemContext)
                .AddLabels(SystemContext);
            BindAttributeMethods(
                registry.Labels,
                () => registry.Epoch,
                CaptureRegistryLabelsUpdatedLocked);
            if (m_eventsEnabled)
            {
                registry.AddEventSourceUrl(SystemContext);
            }

            SetValue(registry.RegistryId, m_registryId);
            SetValue(registry.SpecVersion, m_specVersion);
            SetValue(registry.Xid, "/");
            SetValue(registry.Epoch, 1u);
            SetValue(registry.CreatedAt, DateTimeUtc.Now);
            SetValue(registry.ModifiedAt, DateTimeUtc.Now);
            SetValue(registry.EventSourceUrl, m_eventSourceUrl);

            registry.CreateGroup?.OnCallAsync = OnCreateGroupAsync;
            registry.GetOrCreateGroup?.OnCallAsync = OnGetOrCreateGroupAsync;

            await AddPredefinedNodeAsync(SystemContext, registry, cancellationToken)
                .ConfigureAwait(false);
            m_registry = registry;
        }

        /// <summary>
        /// Handles <c>RegistryType.CreateGroup(GroupId) → GroupNodeId</c>. Fails with
        /// <see cref="StatusCodes.BadNodeIdExists"/> when the group id is already taken.
        /// </summary>
        internal async ValueTask<CreateGroupMethodStateResult> OnCreateGroupAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            string groupId,
            CancellationToken cancellationToken)
        {
            using OperationLease operation = BeginOperation(cancellationToken);
            if (!IsWriteChannelSecure(context))
            {
                return new CreateGroupMethodStateResult
                {
                    ServiceResult = StatusCodes.BadSecurityModeInsufficient
                };
            }
            if (string.IsNullOrEmpty(groupId))
            {
                return new CreateGroupMethodStateResult
                {
                    ServiceResult = StatusCodes.BadInvalidArgument
                };
            }

            GroupState created;
            List<XRegistryEventChange>? changes;
            await using (await EnterGateAsync(cancellationToken).ConfigureAwait(false))
            {
                if (m_groups.ContainsKey(groupId))
                {
                    return new CreateGroupMethodStateResult
                    {
                        ServiceResult = StatusCodes.BadNodeIdExists
                    };
                }

                created = await CreateGroupNodeAsync(groupId, CancellationToken.None).ConfigureAwait(false);
                changes = BuildGroupCreatedChangesLocked(created);
            }
            await ReportChangesAsync(changes, m_registry).ConfigureAwait(false);
            return new CreateGroupMethodStateResult
            {
                ServiceResult = ServiceResult.Good,
                GroupNodeId = created.NodeId
            };
        }

        /// <summary>
        /// Handles <c>RegistryType.GetOrCreateGroup(GroupId) → (GroupNodeId, Created)</c>, the
        /// idempotent counterpart of <c>CreateGroup</c>.
        /// </summary>
        internal async ValueTask<GetOrCreateGroupMethodStateResult> OnGetOrCreateGroupAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            string groupId,
            CancellationToken cancellationToken)
        {
            using OperationLease operation = BeginOperation(cancellationToken);
            if (!IsWriteChannelSecure(context))
            {
                return new GetOrCreateGroupMethodStateResult
                {
                    ServiceResult = StatusCodes.BadSecurityModeInsufficient
                };
            }
            if (string.IsNullOrEmpty(groupId))
            {
                return new GetOrCreateGroupMethodStateResult
                {
                    ServiceResult = StatusCodes.BadInvalidArgument
                };
            }

            GroupState created;
            List<XRegistryEventChange>? changes;
            await using (await EnterGateAsync(cancellationToken).ConfigureAwait(false))
            {
                if (m_groups.TryGetValue(groupId, out GroupState? existing))
                {
                    return new GetOrCreateGroupMethodStateResult
                    {
                        ServiceResult = ServiceResult.Good,
                        GroupNodeId = existing.NodeId,
                        Created = false
                    };
                }

                created = await CreateGroupNodeAsync(groupId, CancellationToken.None).ConfigureAwait(false);
                changes = BuildGroupCreatedChangesLocked(created);
            }
            await ReportChangesAsync(changes, m_registry).ConfigureAwait(false);
            return new GetOrCreateGroupMethodStateResult
            {
                ServiceResult = ServiceResult.Good,
                GroupNodeId = created.NodeId,
                Created = true
            };
        }

        /// <summary>
        /// Creates and publishes a <c>GroupType</c> instance under the registry root. The caller
        /// holds <see cref="m_gate"/>.
        /// </summary>
        /// <param name="groupId">The group id.</param>
        /// <param name="cancellationToken">Cancels group publication.</param>
        /// <returns>The created group.</returns>
        private async ValueTask<GroupState> CreateGroupNodeAsync(
            string groupId,
            CancellationToken cancellationToken)
        {
            ushort ns = (ushort)Server.NamespaceUris.GetIndex(m_namespaceUri);
            GroupState group = SystemContext.CreateInstanceOfGroupType(
                m_registry!,
                new QualifiedName(groupId, ns));

            group.NodeId = new NodeId(m_nextInstanceId++, ns);
            group.DisplayName = new LocalizedText(groupId);
            group.ReferenceTypeId = ReferenceTypeIds.Organizes;

            // The resource lifecycle Methods and the metadata below are Optional on the type, so
            // they have to be materialized explicitly.
            group.AddXid(SystemContext)
                .AddEpoch(SystemContext)
                .AddCreatedAt(SystemContext)
                .AddModifiedAt(SystemContext)
                .AddCreateResource(SystemContext)
                .AddGetOrCreateResource(SystemContext)
                .AddDelete(SystemContext)
                .AddLabels(SystemContext);
            BindAttributeMethods(
                group.Labels,
                () => group.Epoch,
                () => CaptureGroupLabelsUpdatedLocked(group));

            SetValue(group.GroupId, groupId);
            SetValue(group.Xid, GroupSubject(groupId));
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
                    ReferenceTypeIds.HasNotifier,
                    false,
                    group.NodeId);
                group.AddReference(
                    ReferenceTypeIds.HasNotifier,
                    true,
                    m_registry.NodeId);
            }
            await AddPredefinedNodeAsync(SystemContext, group, cancellationToken).ConfigureAwait(false);
            m_groups[groupId] = group;

            group.CreateResource?.OnCallAsync = OnCreateResourceAsync;
            group.GetOrCreateResource?.OnCallAsync = OnGetOrCreateResourceAsync;
            m_groupsByNodeId[group.NodeId] = group;
            group.Delete?.OnCallAsync = (ctx, m, id, epoch, ct) => IsWriteChannelSecure(ctx)
                    ? OnDeleteGroupAsync(group, epoch, ct)
                    : InsecureDelete();
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
            using OperationLease operation = BeginOperation(cancellationToken);
            if (!IsWriteChannelSecure(context))
            {
                return new CreateResourceMethodStateResult
                {
                    ServiceResult = StatusCodes.BadSecurityModeInsufficient
                };
            }

            (ServiceResult result, ResourceState? resource, uint fileHandle, string assigned, bool _) =
                await CreateResourceCoreAsync(
                        objectId,
                        resourceId,
                        versionId,
                        requestFileOpen,
                        false,
                        context,
                        cancellationToken)
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
            using OperationLease operation = BeginOperation(cancellationToken);
            if (!IsWriteChannelSecure(context))
            {
                return new GetOrCreateResourceMethodStateResult
                {
                    ServiceResult = StatusCodes.BadSecurityModeInsufficient
                };
            }

            (ServiceResult result, ResourceState? resource, uint fileHandle, string assigned, bool created) =
                await CreateResourceCoreAsync(
                        objectId,
                        resourceId,
                        versionId,
                        requestFileOpen,
                        true,
                        context,
                        cancellationToken)
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
        private async ValueTask<(ServiceResult Result, ResourceState? Resource, uint FileHandle,
            string AssignedVersionId, bool Created)> CreateResourceCoreAsync(
            NodeId groupNodeId,
            string resourceId,
            string versionId,
            bool requestFileOpen,
            bool getOrCreate,
            ISystemContext context,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(resourceId))
            {
                return Failed(StatusCodes.BadInvalidArgument);
            }

            ResourceState? createdResource = null;
            GroupState? owningGroup = null;
            string assignedVersion = string.Empty;
            uint createdHandle = 0;
            bool created = false;
            List<XRegistryEventChange>? changes = null;
            bool handleReturned = false;
            try
            {
                await using (await EnterGateAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (!m_groupsByNodeId.TryGetValue(groupNodeId, out GroupState? group))
                    {
                        return Failed(StatusCodes.BadNodeIdUnknown);
                    }
                    owningGroup = group;

                    var logicalKey = new ResourceIdentityKey(group.NodeId, resourceId);
                    string assigned = versionId;
                    if (string.IsNullOrEmpty(assigned))
                    {
                        assigned = getOrCreate &&
                            m_defaultVersions.TryGetValue(logicalKey, out string? defaultVersion)
                                ? defaultVersion
                                : NextVersionId(group.NodeId, resourceId);
                    }
                    assignedVersion = assigned;
                    var key = new ResourceKey(group.NodeId, resourceId, assigned);

                    if (m_resources.TryGetValue(key, out ResourceState? existing))
                    {
                        if (!getOrCreate)
                        {
                            return Failed(StatusCodes.BadNodeIdExists);
                        }

                        if (requestFileOpen &&
                            m_writeHandlesByResource.ContainsKey(existing.NodeId))
                        {
                            return Failed(StatusCodes.BadNotWritable);
                        }
                        if (requestFileOpen &&
                            m_writeHandlesByResource.Count >= m_maxConcurrentUploads)
                        {
                            return Failed(StatusCodes.BadTooManyOperations);
                        }
                        if (requestFileOpen &&
                            !TryReserveWriteHandleLocked(
                                existing,
                                context,
                                seedStagedContent: false,
                                append: false,
                                out createdHandle))
                        {
                            return Failed(StatusCodes.BadNotWritable);
                        }
                        createdResource = existing;
                    }
                    else
                    {
                        if (requestFileOpen &&
                            m_writeHandlesByResource.Count >= m_maxConcurrentUploads)
                        {
                            return Failed(StatusCodes.BadTooManyOperations);
                        }
                        if (Volatile.Read(ref m_registeredResourceCount) >= m_maxRegisteredResources)
                        {
                            return Failed(StatusCodes.BadTooManyOperations);
                        }

                        DateTimeUtc now = DateTimeUtc.Now;
                        bool firstVersion = !m_resourceMeta.TryGetValue(
                            logicalKey,
                            out ResourceMetaState? meta);
                        ResourceState resource = await CreateResourceNodeAsync(
                            group, resourceId, assigned).ConfigureAwait(false);
                        if (firstVersion)
                        {
                            meta = new ResourceMetaState(1u, now, now);
                            m_resourceMeta.Add(logicalKey, meta);
                        }
                        else
                        {
                            meta!.Epoch++;
                            meta.ModifiedAt = now;
                        }

                        m_resources[key] = resource;
                        m_defaultVersions[logicalKey] = assigned;
                        await ApplyResourceMetaLockedAsync(logicalKey).ConfigureAwait(false);
                        Interlocked.Increment(ref m_registeredResourceCount);

                        if (requestFileOpen &&
                            !TryReserveWriteHandleLocked(
                                resource,
                                context,
                                seedStagedContent: false,
                                append: false,
                                out createdHandle))
                        {
                            await RemoveResourceLockedAsync(resource).ConfigureAwait(false);
                            return Failed(StatusCodes.BadNotWritable);
                        }
                        createdResource = resource;
                        created = true;
                        changes = BuildResourceCreatedChangesLocked(
                            group,
                            resource,
                            firstVersion);
                    }
                }

                if (created)
                {
                    await NotifyResourceMetaAsync(new ResourceIdentityKey(groupNodeId, resourceId))
                        .ConfigureAwait(false);
                }
                await ReportChangesAsync(changes, owningGroup).ConfigureAwait(false);
                if (requestFileOpen)
                {
                    ServiceResult initialized = await InitializeWriteHandleAsync(createdHandle, cancellationToken)
                        .ConfigureAwait(false);
                    if (ServiceResult.IsBad(initialized))
                    {
                        return (initialized, createdResource, 0u, assignedVersion, created);
                    }
                    await UpdateFilePropertiesAsync(createdResource!).ConfigureAwait(false);
                }
                handleReturned = true;
                return (ServiceResult.Good, createdResource, createdHandle, assignedVersion, created);
            }
            finally
            {
                if (!handleReturned && createdHandle != 0)
                {
                    await ReleaseUndisclosedHandleAsync(createdHandle).ConfigureAwait(false);
                }
            }

            static (ServiceResult, ResourceState?, uint, string, bool) Failed(StatusCode code) =>
                (new ServiceResult(code), null, 0u, string.Empty, false);
        }

        /// <summary>
        /// Publishes an exact Version beneath its logical Resource's typed Versions container.
        /// The caller holds <see cref="m_gate"/>.
        /// </summary>
        private async ValueTask<ResourceState> CreateResourceNodeAsync(
            GroupState group,
            string resourceId,
            string versionId)
        {
            var identity = new ResourceIdentityKey(group.NodeId, resourceId);
            bool newLogical = !m_logicalResources.TryGetValue(identity, out ResourceState? logical);
            if (newLogical)
            {
                logical = CreateResourceNode(
                    group,
                    resourceId,
                    resourceId,
                    string.Empty,
                    ResourceSubject(new ResourceKey(group.NodeId, resourceId, string.Empty)));
                logical.AddVersions(SystemContext);
                logical.Versions!.NodeId = new NodeId(m_nextInstanceId++, logical.NodeId.NamespaceIndex);
                logical.Delete!.OnCallAsync = (ctx, m, id, epoch, ct) => IsWriteChannelSecure(ctx)
                    ? OnDeleteLogicalResourceAsync(logical, epoch, ct)
                    : InsecureDelete();
            }

            ResourceState resource = CreateResourceNode(
                logical!.Versions!,
                versionId,
                resourceId,
                versionId,
                VersionSubject(new ResourceKey(group.NodeId, resourceId, versionId)));
            resource.Delete!.OnCallAsync = (ctx, m, id, epoch, ct) => IsWriteChannelSecure(ctx)
                ? OnDeleteResourceAsync(resource, epoch, ct)
                : InsecureDelete();
            bool published = false;
            try
            {
                if (newLogical)
                {
                    group.AddChild(logical);
                    AddResourceNotifier(group, logical);
                    await AddPredefinedNodeAsync(SystemContext, logical).ConfigureAwait(false);
                    m_logicalResources.Add(identity, logical);
                }
                logical.Versions!.AddChild(resource);
                AddResourceNotifier(logical, resource);
                await AddPredefinedNodeAsync(SystemContext, resource).ConfigureAwait(false);
                published = true;
                return resource;
            }
            finally
            {
                if (!published)
                {
                    logical.RemoveReference(ReferenceTypeIds.HasNotifier, false, resource.NodeId);
                    ScheduleNodeDeletionLocked(resource);
                    if (newLogical)
                    {
                        m_logicalResources.Remove(identity);
                        group.RemoveReference(ReferenceTypeIds.HasNotifier, false, logical.NodeId);
                        ScheduleNodeDeletionLocked(logical);
                    }
                }
            }
        }

        private ResourceState CreateResourceNode(
            BaseInstanceState parent,
            string browseName,
            string resourceId,
            string versionId,
            string xid)
        {
            ushort ns = (ushort)Server.NamespaceUris.GetIndex(m_namespaceUri);
            ResourceState resource = SystemContext.CreateInstanceOfResourceType(
                parent, new QualifiedName(browseName, ns));
            resource.NodeId = new NodeId(m_nextInstanceId++, ns);
            resource.DisplayName = new LocalizedText(browseName);
            resource.ReferenceTypeId = ReferenceTypeIds.Organizes;
            resource.AddVersionId(SystemContext)
                .AddFormat(SystemContext)
                .AddContentType(SystemContext)
                .AddXid(SystemContext)
                .AddEpoch(SystemContext)
                .AddCreatedAt(SystemContext)
                .AddModifiedAt(SystemContext)
                .AddDelete(SystemContext)
                .AddLabels(SystemContext)
                .AddMetaEpoch(SystemContext)
                .AddMetaLabels(SystemContext)
                .AddMetaCreatedAt(SystemContext)
                .AddMetaModifiedAt(SystemContext);
            BindAttributeMethods(
                resource.Labels,
                () => resource.Epoch,
                () => CaptureVersionLabelsUpdatedLocked(resource));
            BindMetaAttributeMethods(resource);

            SetValue(resource.ResourceId, resourceId);
            SetValue(resource.VersionId, versionId);
            SetValue(resource.Xid, xid);
            SetValue(resource.Epoch, 1u);
            SetValue(resource.CreatedAt, DateTimeUtc.Now);
            SetValue(resource.ModifiedAt, DateTimeUtc.Now);
            if (m_eventsEnabled)
            {
                resource.EventNotifier = EventNotifiers.SubscribeToEvents;
            }

            BindFileMethods(resource);
            return resource;
        }

        private void AddResourceNotifier(NodeState parent, ResourceState resource)
        {
            if (m_eventsEnabled)
            {
                parent.AddReference(
                    ReferenceTypeIds.HasNotifier,
                    false,
                    resource.NodeId);
                resource.AddReference(
                    ReferenceTypeIds.HasNotifier,
                    true,
                    parent.NodeId);
            }
        }

        /// <summary>
        /// Handles <c>ResourceType.Delete(ExpectedEpoch)</c>. The epoch is an optimistic-concurrency
        /// check: a caller that read the resource at an older epoch is rejected rather than silently
        /// deleting someone else's newer version.
        /// </summary>
        internal async ValueTask<DeleteMethodStateResult> OnDeleteResourceAsync(
            ResourceState resource,
            uint expectedEpoch,
            CancellationToken cancellationToken = default)
        {
            using OperationLease operation = BeginOperation(cancellationToken);
            await RetryDeletionsAsync(cancellationToken).ConfigureAwait(false);
            ResourceIdentityKey logicalKey;
            GroupState? group;
            List<XRegistryEventChange>? changes = null;
            await using (await EnterGateAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!TryGetResourceKeyLocked(resource, out ResourceKey key) ||
                    !m_groupsByNodeId.TryGetValue(key.GroupNodeId, out group))
                {
                    return new DeleteMethodStateResult { ServiceResult = ServiceResult.Good };
                }
                if (!IsEpochCurrent(resource.Epoch, expectedEpoch))
                {
                    return new DeleteMethodStateResult { ServiceResult = StatusCodes.BadInvalidState };
                }

                logicalKey = new ResourceIdentityKey(key.GroupNodeId, key.ResourceId);
                ResourceState logical = m_logicalResources[logicalKey];
                if (!await RemoveResourceLockedAsync(resource).ConfigureAwait(false))
                {
                    // A concurrent Delete already removed it; nothing left to do and nothing to
                    // release a second time.
                    return new DeleteMethodStateResult { ServiceResult = ServiceResult.Good };
                }

                changes = await BuildResourceDeletionChangesLockedAsync(
                    group,
                    key,
                    resource,
                    logicalKey,
                    logical).ConfigureAwait(false);
            }

            await NotifyResourceMetaAsync(logicalKey).ConfigureAwait(false);
            await ReportChangesAsync(changes, group).ConfigureAwait(false);
            return new DeleteMethodStateResult { ServiceResult = ServiceResult.Good };
        }

        private async ValueTask<DeleteMethodStateResult> OnDeleteLogicalResourceAsync(
            ResourceState logical,
            uint expectedEpoch,
            CancellationToken cancellationToken)
        {
            using OperationLease operation = BeginOperation(cancellationToken);
            await RetryDeletionsAsync(cancellationToken).ConfigureAwait(false);
            List<XRegistryEventChange>? changes = null;
            GroupState? group = null;
            await using (await EnterGateAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!TryGetResourceIdentityLocked(logical, out ResourceIdentityKey identity) ||
                    !m_logicalResources.TryGetValue(identity, out ResourceState? current) ||
                    !ReferenceEquals(current, logical))
                {
                    return new DeleteMethodStateResult { ServiceResult = ServiceResult.Good };
                }
                if (!IsEpochCurrent(logical.MetaEpoch, expectedEpoch))
                {
                    return new DeleteMethodStateResult { ServiceResult = StatusCodes.BadInvalidState };
                }
                group = m_groupsByNodeId[identity.GroupNodeId];
                if (m_eventsEnabled)
                {
                    changes = [];
                }
                var versions = m_resources.Where(entry =>
                    entry.Key.GroupNodeId == identity.GroupNodeId &&
                    string.Equals(entry.Key.ResourceId, identity.ResourceId, StringComparison.Ordinal))
                    .OrderBy(entry => entry.Key.VersionId, StringComparer.Ordinal)
                    .ToList();
                foreach (KeyValuePair<ResourceKey, ResourceState> version in versions)
                {
                    changes?.Add(FromSource(
                        new XRegistryEventChange(
                            XRegistryEventKind.VersionDeleted,
                            VersionSubject(version.Key),
                            version.Value.NodeId),
                        version.Value,
                        group));
                    await RemoveResourceLockedAsync(version.Value).ConfigureAwait(false);
                }
                uint epoch = BumpEntity(group.Epoch, group.ModifiedAt);
                changes?.Add(FromSource(
                    new XRegistryEventChange(
                        XRegistryEventKind.ResourceDeleted,
                        logical.Xid!.Value,
                        logical.NodeId),
                    logical,
                    group));
                changes?.Add(FromSource(
                    new XRegistryEventChange(
                        XRegistryEventKind.GroupUpdated,
                        GroupSubject(group.GroupId!.Value),
                        group.NodeId,
                        epoch,
                        Changed: CollectionChanged(m_resourcesAttributeName)),
                    group));
            }
            await ReportChangesAsync(changes, group).ConfigureAwait(false);
            return new DeleteMethodStateResult { ServiceResult = ServiceResult.Good };
        }

        /// <summary>
        /// Handles <c>GroupType.Delete(ExpectedEpoch)</c>, removing the group and every resource
        /// version it owns.
        /// </summary>
        internal async ValueTask<DeleteMethodStateResult> OnDeleteGroupAsync(
            GroupState group,
            uint expectedEpoch,
            CancellationToken cancellationToken = default)
        {
            using OperationLease operation = BeginOperation(cancellationToken);
            await RetryDeletionsAsync(cancellationToken).ConfigureAwait(false);
            List<XRegistryEventChange>? changes = null;
            await using (await EnterGateAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!m_groupsByNodeId.TryGetValue(group.NodeId, out GroupState? current) ||
                    !ReferenceEquals(current, group))
                {
                    return new DeleteMethodStateResult { ServiceResult = ServiceResult.Good };
                }
                if (!IsEpochCurrent(group.Epoch, expectedEpoch))
                {
                    return new DeleteMethodStateResult { ServiceResult = StatusCodes.BadInvalidState };
                }

                if (m_eventsEnabled)
                {
                    changes = [];
                }
                var resources = new List<KeyValuePair<ResourceKey, ResourceState>>(m_resources);
                var logicalResources = m_logicalResources
                    .Where(entry => entry.Key.GroupNodeId == group.NodeId)
                    .OrderBy(entry => entry.Key.ResourceId, StringComparer.Ordinal)
                    .Select(entry => entry.Value)
                    .ToList();
                foreach (KeyValuePair<ResourceKey, ResourceState> entry in resources
                    .Where(entry => entry.Key.GroupNodeId == group.NodeId)
                    .OrderBy(entry => entry.Key.ResourceId, StringComparer.Ordinal)
                    .ThenBy(entry => entry.Key.VersionId, StringComparer.Ordinal))
                {
                    changes?.Add(FromSource(
                            new XRegistryEventChange(
                                XRegistryEventKind.VersionDeleted,
                                VersionSubject(entry.Key),
                                entry.Value.NodeId),
                            entry.Value,
                            m_registry));
                    await RemoveResourceLockedAsync(entry.Value).ConfigureAwait(false);
                }
                if (changes is not null)
                {
                    foreach (ResourceState logical in logicalResources)
                    {
                        changes.Add(FromSource(
                            new XRegistryEventChange(
                                XRegistryEventKind.ResourceDeleted,
                                logical.Xid!.Value,
                                logical.NodeId),
                            logical,
                            m_registry));
                    }
                }

                string deletedGroupId = group.GroupId?.Value ?? string.Empty;
                if (group.GroupId?.Value is string groupId)
                {
                    m_groups.Remove(groupId);
                }
                m_groupsByNodeId.Remove(group.NodeId);
                if (m_registry is not null)
                {
                    uint registryEpoch = BumpEntity(m_registry.Epoch, m_registry.ModifiedAt);
                    if (m_eventsEnabled)
                    {
                        m_registry.RemoveReference(
                            ReferenceTypeIds.HasNotifier,
                            false,
                            group.NodeId);
                        changes!.Add(FromSource(
                            new XRegistryEventChange(
                                XRegistryEventKind.GroupDeleted,
                                GroupSubject(deletedGroupId),
                                group.NodeId),
                            group,
                            m_registry));
                        changes.Add(FromSource(
                            new XRegistryEventChange(
                                XRegistryEventKind.RegistryUpdated,
                                RegistrySubject(),
                                m_registry.NodeId,
                                registryEpoch,
                                Changed: CollectionChanged(m_groupsAttributeName)),
                            m_registry));
                    }
                }
                ScheduleNodeDeletionLocked(group);
            }

            await ReportChangesAsync(changes, m_registry).ConfigureAwait(false);
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
        private async ValueTask<bool> RemoveResourceLockedAsync(ResourceState resource)
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
                if (m_versionContentKeys.Remove(key, out string? contentKey) &&
                    contentKey.Length > 0)
                {
                    await ReleaseFastPathNodeAsync(contentKey).ConfigureAwait(false);
                }

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
                    m_resourceMeta.Remove(logicalKey);
                    m_defaultVersions.Remove(logicalKey);
                    if (m_logicalResources.Remove(logicalKey, out ResourceState? logical))
                    {
                        logical.Parent?.RemoveReference(
                            ReferenceTypeIds.HasNotifier, false, logical.NodeId);
                        ScheduleNodeDeletionLocked(logical);
                    }
                }
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
                if (m_fileHandles.TryGetValue(handle, out ResourceFileHandle? entry))
                {
                    if (entry.Writing)
                    {
                        m_writeHandlesByResource.Remove(entry.ResourceNodeId);
                    }
                    entry.Dispose();
                }
                m_fileHandles.Remove(handle);
            }
            m_writeHandlesByResource.Remove(resource.NodeId);

            if (m_eventsEnabled && resource.Parent is ResourceVersionsState { Parent: ResourceState parentResource })
            {
                parentResource.RemoveReference(
                    ReferenceTypeIds.HasNotifier,
                    false,
                    resource.NodeId);
            }
            ScheduleNodeDeletionLocked(resource);
            m_pendingDeletions!.StoreKeys.Add(StoreKeyOf(resource));
            Interlocked.Decrement(ref m_registeredResourceCount);
            return true;
        }

        /// <summary>
        /// Binds the inherited <c>FileType</c> Methods so a document is transferred with the
        /// standard file operations rather than a registry-specific mechanism.
        /// </summary>
        private void BindFileMethods(ResourceState resource)
        {
            resource.Open?.OnCallAsync = (ctx, m, id, mode, ct) =>
                    OnFileOpenAsync(resource, mode, ctx, ct);
            resource.Write?.OnCallAsync = (ctx, m, id, handle, data, ct) => IsWriteChannelSecure(ctx)
                    ? OnFileWriteAsync(resource, handle, data, ctx, ct)
                    : new ValueTask<WriteMethodStateResult>(new WriteMethodStateResult
                    {
                        ServiceResult = StatusCodes.BadSecurityModeInsufficient
                    });
            resource.Read?.OnCallAsync = (ctx, m, id, handle, length, ct) => IsReadChannelSecure(ctx)
                    ? OnFileReadAsync(resource, handle, length, ctx, ct)
                    : new ValueTask<ReadMethodStateResult>(new ReadMethodStateResult
                    {
                        ServiceResult = StatusCodes.BadSecurityModeInsufficient
                    });
            // Close is gated on the mode of the handle being closed, not unconditionally on the
            // write requirement: a read handle opened on a channel the read policy allows has
            // to be closable on that same channel, or it leaks and consumes the handle budget.
            resource.Close?.OnCallAsync = (ctx, m, id, handle, ct) =>
                OnFileCloseAsync(resource, handle, ctx, ct);
            resource.GetPosition?.OnCallAsync = (ctx, m, id, handle, ct) =>
                OnFileGetPositionAsync(resource, handle, ctx, ct);
            resource.SetPosition?.OnCallAsync = (ctx, m, id, handle, position, ct) =>
                OnFileSetPositionAsync(resource, handle, position, ctx, ct);
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
        /// <param name="cancellationToken">Cancels opening and reading the write baseline.</param>
        private async ValueTask<OpenMethodStateResult> OnFileOpenAsync(
            ResourceState resource,
            byte mode,
            ISystemContext context,
            CancellationToken cancellationToken)
        {
            using OperationLease operation = BeginOperation(cancellationToken);
            bool wantsRead = (mode & kReadMode) != 0;
            bool wantsWrite = (mode & kWriteMode) != 0;
            bool erase = (mode & kEraseExistingMode) != 0;
            bool append = (mode & kAppendMode) != 0;

            if ((!wantsRead && !wantsWrite) || (mode & 0xF0) != 0)
            {
                return Failed(StatusCodes.BadInvalidArgument);
            }
            if (!wantsWrite && erase)
            {
                return Failed(StatusCodes.BadInvalidArgument);
            }

            uint handle = 0;
            ResourceState version;
            bool handleReturned = false;
            try
            {
                await using (await EnterGateAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (wantsWrite ? !IsWriteChannelSecure(context) : !IsReadChannelSecure(context))
                    {
                        return Failed(StatusCodes.BadSecurityModeInsufficient);
                    }
                    if (!TryResolveVersionLocked(resource, out ResourceState? resolved))
                    {
                        return Failed(StatusCodes.BadNodeIdUnknown);
                    }
                    version = resolved;
                    if (!wantsWrite && m_writeHandlesByResource.ContainsKey(version.NodeId))
                    {
                        return Failed(StatusCodes.BadNotReadable);
                    }
                    if (wantsWrite && m_writeHandlesByResource.ContainsKey(version.NodeId))
                    {
                        return Failed(StatusCodes.BadNotWritable);
                    }
                    if (wantsWrite &&
                        m_writeHandlesByResource.Count >= m_maxConcurrentUploads)
                    {
                        return Failed(StatusCodes.BadTooManyOperations);
                    }

                    if (wantsWrite)
                    {
                        if (!TryReserveWriteHandleLocked(
                            version,
                            context,
                            seedStagedContent: !erase,
                            append,
                            out handle,
                            reading: wantsRead,
                            file: resource))
                        {
                            return Failed(StatusCodes.BadNotWritable);
                        }
                    }
                    else
                    {
                        handle = OpenReadHandle(version, resource.NodeId, context, append);
                    }
                }

                if (wantsWrite)
                {
                    ServiceResult initialized = await InitializeWriteHandleAsync(handle, cancellationToken)
                        .ConfigureAwait(false);
                    if (ServiceResult.IsBad(initialized))
                    {
                        return Failed(initialized.StatusCode);
                    }
                }

                await UpdateFilePropertiesAsync(version).ConfigureAwait(false);
                handleReturned = true;
                return new OpenMethodStateResult
                {
                    ServiceResult = ServiceResult.Good,
                    FileHandle = handle
                };
            }
            finally
            {
                if (!handleReturned && handle != 0)
                {
                    await ReleaseUndisclosedHandleAsync(handle).ConfigureAwait(false);
                }
            }

            static OpenMethodStateResult Failed(StatusCode code) =>
                new()
                { ServiceResult = new ServiceResult(code) };
        }

        /// <summary>
        /// Keeps the inherited <c>FileType</c> <c>Size</c> and <c>OpenCount</c> Properties current,
        /// so a client that reads them before opening sees the real values.
        /// </summary>
        /// <param name="resource">The resource whose file Properties are refreshed.</param>
        private async ValueTask UpdateFilePropertiesAsync(ResourceState resource)
        {
            ResourceState? logical = null;
            await using (await EnterGateAsync(CancellationToken.None).ConfigureAwait(false))
            {
                UpdateOpenCountLocked(resource);
                if (TryGetResourceKeyLocked(resource, out ResourceKey key))
                {
                    m_logicalResources.TryGetValue(
                        new ResourceIdentityKey(key.GroupNodeId, key.ResourceId), out logical);
                }
            }
            await resource.ClearChangeMasksAsync(SystemContext, includeChildren: true).ConfigureAwait(false);
            if (logical is not null)
            {
                await NotifyMetadataAsync(logical).ConfigureAwait(false);
            }
        }

        private void UpdateOpenCountLocked(ResourceState resource)
        {
            ushort open = 0;
            foreach (ResourceFileHandle handle in m_fileHandles.Values)
            {
                if (handle.ResourceNodeId == resource.NodeId && !handle.Closing)
                {
                    open++;
                }
            }
            SetValue(resource.OpenCount, open);
            if (TryGetResourceKeyLocked(resource, out ResourceKey key))
            {
                var identity = new ResourceIdentityKey(key.GroupNodeId, key.ResourceId);
                if (m_logicalResources.TryGetValue(identity, out ResourceState? logical) &&
                    DefaultVersionFileLocked(identity) is ResourceState current)
                {
                    XRegistryProjectionEngine.MirrorFileTypeProperties(logical, current);
                }
            }
        }

        private async ValueTask ReleaseUndisclosedHandleAsync(uint handle)
        {
            await using (await EnterGateAsync(CancellationToken.None).ConfigureAwait(false))
            {
                if (m_fileHandles.Remove(handle, out ResourceFileHandle? entry))
                {
                    entry.Dispose();
                    if (entry.Writing &&
                        m_writeHandlesByResource.TryGetValue(entry.ResourceNodeId, out uint writer) &&
                        writer == handle)
                    {
                        m_writeHandlesByResource.Remove(entry.ResourceNodeId);
                    }
                    if (Find(entry.ResourceNodeId) is ResourceState resource)
                    {
                        // Keep the readable count correct without reinvoking the sink that failed.
                        UpdateOpenCountLocked(resource);
                    }
                }
            }
        }

        private async ValueTask<WriteMethodStateResult> OnFileWriteAsync(
            ResourceState resource,
            uint fileHandle,
            ByteString data,
            ISystemContext context,
            CancellationToken cancellationToken)
        {
            using OperationLease operation = BeginOperation(cancellationToken);
            await using (await EnterGateAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!TryGetHandle(resource, fileHandle, context, out ResourceFileHandle? entry) ||
                    !entry.Writing ||
                    !entry.Ready)
                {
                    return new WriteMethodStateResult
                    {
                        ServiceResult = StatusCodes.BadInvalidState
                    };
                }

                if (!data.IsNull && data.Span.Length > 0)
                {
                    ReadOnlySpan<byte> span = data.Span;
                    if (entry.Position + span.Length > m_maxResourceBytes)
                    {
                        return new WriteMethodStateResult
                        {
                            ServiceResult = StatusCodes.BadRequestTooLarge
                        };
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
                    entry.HasAcceptedWrite = true;
                }
                return new WriteMethodStateResult
                {
                    ServiceResult = ServiceResult.Good
                };
            }
        }

        private ValueTask<ReadMethodStateResult> OnFileReadAsync(
            ResourceState resource,
            uint fileHandle,
            int length,
            ISystemContext context,
            CancellationToken cancellationToken)
        {
            return ExecuteFileCursorAsync(
                resource, fileHandle, context, ReadAsync,
                new ReadMethodStateResult { ServiceResult = StatusCodes.BadInvalidState }, cancellationToken);

            async ValueTask<ReadMethodStateResult> ReadAsync(ResourceFileHandle entry)
            {
                int position;
                await using (await EnterGateAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (!entry.Reading || !entry.Ready)
                    {
                        return new ReadMethodStateResult { ServiceResult = StatusCodes.BadInvalidState };
                    }
                    if (length <= 0)
                    {
                        return new ReadMethodStateResult
                        {
                            ServiceResult = StatusCodes.BadInvalidArgument
                        };
                    }
                    if (entry.Writing)
                    {
                        int count = Math.Min(length, entry.Buffer.Count - entry.Position);
                        byte[] data = new byte[count];
                        entry.Buffer.CopyTo(entry.Position, data, 0, count);
                        entry.Position += count;
                        return new ReadMethodStateResult
                        {
                            ServiceResult = ServiceResult.Good,
                            Data = ByteString.From(data)
                        };
                    }
                    if (!entry.HasCommittedContent)
                    {
                        return new ReadMethodStateResult
                        {
                            ServiceResult = ServiceResult.Good,
                            Data = ByteString.Empty
                        };
                    }
                    position = entry.Position;
                }

                ByteString chunk = await m_resourceStore
                    .ReadAsync(entry.StoreKey, position, length, cancellationToken)
                    .ConfigureAwait(false);
                if (chunk.IsNull)
                {
                    return new ReadMethodStateResult { ServiceResult = StatusCodes.BadNotFound };
                }
                await using (await EnterGateAsync(CancellationToken.None).ConfigureAwait(false))
                {
                    if (!TryGetHandle(resource, fileHandle, context, out ResourceFileHandle? current) ||
                        !ReferenceEquals(current, entry))
                    {
                        return new ReadMethodStateResult { ServiceResult = StatusCodes.BadInvalidState };
                    }
                    entry.Position = checked(position + chunk.Length);
                }
                return new ReadMethodStateResult
                {
                    ServiceResult = ServiceResult.Good,
                    Data = chunk
                };
            }
        }

        private ValueTask<GetPositionMethodStateResult> OnFileGetPositionAsync(
            ResourceState resource,
            uint fileHandle,
            ISystemContext context,
            CancellationToken cancellationToken)
        {
            return ExecuteFileCursorAsync(
                resource, fileHandle, context, GetPositionAsync,
                new GetPositionMethodStateResult { ServiceResult = StatusCodes.BadInvalidArgument },
                cancellationToken);

            async ValueTask<GetPositionMethodStateResult> GetPositionAsync(ResourceFileHandle entry)
            {
                await using (await EnterGateAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (!entry.Ready)
                    {
                        return new GetPositionMethodStateResult { ServiceResult = StatusCodes.BadInvalidState };
                    }
                    if (!IsReadChannelSecure(context))
                    {
                        return new GetPositionMethodStateResult
                        {
                            ServiceResult = StatusCodes.BadSecurityModeInsufficient
                        };
                    }
                    return new GetPositionMethodStateResult
                    {
                        ServiceResult = ServiceResult.Good,
                        Position = (ulong)entry.Position
                    };
                }
            }
        }

        private ValueTask<SetPositionMethodStateResult> OnFileSetPositionAsync(
            ResourceState resource,
            uint fileHandle,
            ulong position,
            ISystemContext context,
            CancellationToken cancellationToken)
        {
            return ExecuteFileCursorAsync(
                resource, fileHandle, context, SetPositionAsync,
                new SetPositionMethodStateResult { ServiceResult = StatusCodes.BadInvalidArgument },
                cancellationToken);

            async ValueTask<SetPositionMethodStateResult> SetPositionAsync(ResourceFileHandle entry)
            {
                await using (await EnterGateAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (!entry.Ready)
                    {
                        return new SetPositionMethodStateResult { ServiceResult = StatusCodes.BadInvalidState };
                    }
                    if (entry.Writing ? !IsWriteChannelSecure(context) : !IsReadChannelSecure(context))
                    {
                        return new SetPositionMethodStateResult
                        {
                            ServiceResult = StatusCodes.BadSecurityModeInsufficient
                        };
                    }
                    ulong length = (ulong)(entry.Writing ? entry.Buffer.Count : entry.BaselineLength);
                    entry.Position = checked((int)Math.Min(position, length));
                    return new SetPositionMethodStateResult { ServiceResult = ServiceResult.Good };
                }
            }
        }

        private async ValueTask<TResult> ExecuteFileCursorAsync<TResult>(
            ResourceState resource,
            uint fileHandle,
            ISystemContext context,
            Func<ResourceFileHandle, ValueTask<TResult>> action,
            TResult invalidHandle,
            CancellationToken cancellationToken)
        {
            using OperationLease operation = BeginOperation(cancellationToken);
            ResourceFileHandle entry;
            await using (await EnterGateAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!TryGetHandle(resource, fileHandle, context, out ResourceFileHandle? found))
                {
                    return invalidHandle;
                }
                entry = found;
            }
            return await entry.ExecuteAsync(async () =>
            {
                await using (await EnterGateAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (!TryGetHandle(resource, fileHandle, context, out ResourceFileHandle? current) ||
                        !ReferenceEquals(current, entry))
                    {
                        return invalidHandle;
                    }
                }
                return await action(entry).ConfigureAwait(false);
            }, invalidHandle, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Closes a file handle. Writes are staged until Close. A write Close commits only when at
        /// least one accepted non-empty Write produced bytes different from the committed baseline
        /// captured immediately before Open.
        /// </summary>
        /// <param name="resource">The resource the Method was invoked on.</param>
        /// <param name="fileHandle">The handle to close.</param>
        /// <param name="context">The system context, used to apply the channel-security policy.</param>
        /// <param name="cancellationToken">Cancels Close before it consumes the handle.</param>
        private async ValueTask<CloseMethodStateResult> OnFileCloseAsync(
            ResourceState resource,
            uint fileHandle,
            ISystemContext context,
            CancellationToken cancellationToken)
        {
            using OperationLease operation = BeginOperation(cancellationToken);
            ResourceFileHandle? entry;
            ResourceState version;
            bool dirty;
            await using (await EnterGateAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!TryGetHandle(resource, fileHandle, context, out entry))
                {
                    return new CloseMethodStateResult { ServiceResult = StatusCodes.BadInvalidState };
                }
                if (Find(entry.ResourceNodeId) is not ResourceState registered ||
                    !TryGetResourceKeyLocked(registered, out ResourceKey baselineKey))
                {
                    return new CloseMethodStateResult { ServiceResult = StatusCodes.BadInvalidState };
                }
                version = registered;

                dirty = entry.Writing &&
                    entry.HasAcceptedWrite &&
                    !entry.Baseline.AsSpan().SequenceEqual(entry.Buffer.ToArray());
                bool permitted = dirty
                    ? IsWriteChannelSecure(context)
                    : IsReadChannelSecure(context);
                if (!permitted)
                {
                    return new CloseMethodStateResult
                    {
                        ServiceResult = StatusCodes.BadSecurityModeInsufficient
                    };
                }
                if (dirty &&
                    !string.Equals(
                            m_versionContentKeys.TryGetValue(
                                baselineKey,
                                out string? currentContentKey)
                                ? currentContentKey
                                : string.Empty,
                            entry.BaselineContentKey,
                            StringComparison.Ordinal))
                {
                    m_fileHandles.Remove(fileHandle);
                    entry.Dispose();
                    m_writeHandlesByResource.Remove(entry.ResourceNodeId);
                    UpdateOpenCountLocked(version);
                    return new CloseMethodStateResult
                    {
                        ServiceResult = StatusCodes.BadInvalidState
                    };
                }

                entry.Closing = true;
                entry.Dispose();
            }

            try
            {
                return dirty
                    ? await CommitFileAsync(version, entry).ConfigureAwait(false)
                    : new CloseMethodStateResult { ServiceResult = ServiceResult.Good };
            }
            finally
            {
                try
                {
                    await UpdateFilePropertiesAsync(version).ConfigureAwait(false);
                }
                finally
                {
                    await using (await EnterGateAsync(CancellationToken.None).ConfigureAwait(false))
                    {
                        if (m_fileHandles.TryGetValue(fileHandle, out ResourceFileHandle? current) &&
                            ReferenceEquals(current, entry))
                        {
                            m_fileHandles.Remove(fileHandle);
                        }
                        if (entry.Writing &&
                            m_writeHandlesByResource.TryGetValue(entry.ResourceNodeId, out uint writer) &&
                            writer == fileHandle)
                        {
                            m_writeHandlesByResource.Remove(entry.ResourceNodeId);
                        }
                    }
                }
            }
        }

        private async ValueTask<CloseMethodStateResult> CommitFileAsync(
            ResourceState resource,
            ResourceFileHandle entry)
        {
            if (m_contentIdProvider == null ||
                (entry.BaselineContentKey.Length > 0 &&
                    m_resourceStore is not IXRegistryAtomicResourceStore))
            {
                return new CloseMethodStateResult { ServiceResult = StatusCodes.BadNotSupported };
            }

            byte[] document = [.. entry.Buffer];
            string format = resource.Format?.Value ?? kDefaultFormat;
            ByteString contentId = m_contentIdProvider.ComputeContentId(format, document);
            // Once Close consumes a dirty handle, finish publication without caller cancellation.
            if (m_resourceStore is IXRegistryAtomicResourceStore atomicStore)
            {
                await atomicStore.ReplaceAsync(
                    entry.StoreKey, ByteString.From(document), CancellationToken.None).ConfigureAwait(false);
            }
            else
            {
                // Only initial content reaches this branch. A failed upload and cleanup may have
                // left a longer document, which an offset write cannot truncate on retry.
                _ = await m_resourceStore.DeleteAsync(entry.StoreKey, CancellationToken.None).ConfigureAwait(false);
                bool stored = false;
                try
                {
                    await m_resourceStore.WriteAsync(
                        entry.StoreKey, 0, ByteString.From(document), CancellationToken.None).ConfigureAwait(false);
                    stored = true;
                }
                finally
                {
                    if (!stored)
                    {
                        _ = await m_resourceStore.DeleteAsync(entry.StoreKey, CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                }
            }

            bool stillRegistered;
            List<XRegistryEventChange>? changes = null;
            await using (await EnterGateAsync(CancellationToken.None).ConfigureAwait(false))
            {
                stillRegistered = IsRegisteredLocked(resource);
                if (stillRegistered &&
                    TryGetResourceKeyLocked(resource, out ResourceKey key))
                {
                    string contentKey = contentId.ToHexString();
                    string previousContentKey = m_versionContentKeys.TryGetValue(
                        key,
                        out string? storedContentKey)
                            ? storedContentKey
                            : string.Empty;
                    if (!string.Equals(previousContentKey, contentKey, StringComparison.Ordinal))
                    {
                        if (previousContentKey.Length > 0)
                        {
                            await ReleaseFastPathNodeAsync(previousContentKey).ConfigureAwait(false);
                        }
                        await PublishFastPathNodeAsync(contentId, contentKey, document).ConfigureAwait(false);
                        m_versionContentKeys[key] = contentKey;
                    }

                    SetValue(resource.Format, format);
                    uint epoch = BumpEntity(resource.Epoch, resource.ModifiedAt);
                    SetValue(resource.Size, (ulong)document.Length);
                    var logicalKey = new ResourceIdentityKey(key.GroupNodeId, key.ResourceId);
                    await ApplyDefaultVersionViewLockedAsync(logicalKey).ConfigureAwait(false);
                    if (m_eventsEnabled)
                    {
                        uint metaEpoch = m_resourceMeta.TryGetValue(
                            logicalKey,
                            out ResourceMetaState? meta)
                                ? meta.Epoch
                                : 0;
                        ImmutableArray<string> changed =
                        [
                            "epoch",
                            "modifiedat",
                            m_resourceDocumentAttributeName
                        ];
                        changes =
                        [
                            FromSource(
                                new XRegistryEventChange(
                                    XRegistryEventKind.VersionUpdated,
                                    VersionSubject(key),
                                    resource.NodeId,
                                    epoch,
                                    Changed: changed),
                                resource)
                        ];
                        if (m_defaultVersions.TryGetValue(logicalKey, out string? defaultVersion) &&
                            string.Equals(defaultVersion, key.VersionId, StringComparison.Ordinal))
                        {
                            ResourceState logical = m_logicalResources[logicalKey];
                            changes.Add(FromSource(
                                new XRegistryEventChange(
                                    XRegistryEventKind.ResourceUpdated,
                                    ResourceSubject(key),
                                    logical.NodeId,
                                    epoch,
                                    metaEpoch,
                                    changed),
                                logical));
                        }
                    }
                }
            }

            if (!stillRegistered)
            {
                _ = await m_resourceStore.DeleteAsync(entry.StoreKey, CancellationToken.None).ConfigureAwait(false);
                return new CloseMethodStateResult { ServiceResult = StatusCodes.BadInvalidState };
            }

            await UpdateFilePropertiesAsync(resource).ConfigureAwait(false);
            await ReportChangesAsync(changes, resource).ConfigureAwait(false);
            return new CloseMethodStateResult { ServiceResult = ServiceResult.Good };
        }

        /// <summary>
        /// Releases every file handle a closing session still holds. Without this an abandoned
        /// session's handles keep consuming the upload budget for the lifetime of the server.
        /// </summary>
        /// <param name="context">The operation context.</param>
        /// <param name="sessionId">The session that is closing.</param>
        /// <param name="deleteSubscriptions">Whether the session's subscriptions are deleted.</param>
        /// <param name="cancellationToken">Cancels the wait to release session handles.</param>
        public override async ValueTask SessionClosingAsync(
            OperationContext context,
            NodeId sessionId,
            bool deleteSubscriptions,
            CancellationToken cancellationToken = default)
        {
            using OperationLease operation = BeginOperation(cancellationToken);
            var affected = new HashSet<ResourceState>();
            await using (await EnterGateAsync(cancellationToken).ConfigureAwait(false))
            {
                var orphaned = new List<uint>();
                foreach (KeyValuePair<uint, ResourceFileHandle> handle in m_fileHandles)
                {
                    if (handle.Value.SessionId == sessionId && !handle.Value.Closing)
                    {
                        orphaned.Add(handle.Key);
                    }
                }
                foreach (uint handle in orphaned)
                {
                    if (m_fileHandles.TryGetValue(handle, out ResourceFileHandle? entry))
                    {
                        entry.Dispose();
                        if (entry.Writing)
                        {
                            m_writeHandlesByResource.Remove(entry.ResourceNodeId);
                        }
                        if (Find(entry.ResourceNodeId) is ResourceState resource)
                        {
                            affected.Add(resource);
                        }
                    }
                    m_fileHandles.Remove(handle);
                }
            }

            foreach (ResourceState resource in affected)
            {
                await UpdateFilePropertiesAsync(resource).ConfigureAwait(false);
            }
            await base.SessionClosingAsync(context, sessionId, deleteSubscriptions, cancellationToken)
                .ConfigureAwait(false);
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

        private bool TryResolveVersionLocked(
            ResourceState resource,
            [NotNullWhen(true)] out ResourceState? version)
        {
            version = null;
            if (!TryGetResourceIdentityLocked(resource, out ResourceIdentityKey identity))
            {
                return false;
            }
            version = IsRegisteredLocked(resource) ? resource : DefaultVersionFileLocked(identity);
            return version is not null;
        }

        /// <summary>
        /// Publishes the Opaque content-id node so a decoder that received the id on the wire
        /// reaches the document in a single Read, and takes a reference on it. The node is
        /// content-addressed and therefore <b>shared</b> by every resource whose document has the
        /// same bytes, so its lifetime is refcounted rather than tied to any one resource. The
        /// caller holds <see cref="m_gate"/>.
        /// </summary>
        /// <param name="contentId">The content-derived id.</param>
        /// <param name="contentKey">
        /// The hex form of <paramref name="contentId"/>, used as the ref-count key.
        /// </param>
        /// <param name="document">The document bytes published as the node's value.</param>
        private async ValueTask PublishFastPathNodeAsync(
            ByteString contentId,
            string contentKey,
            byte[] document)
        {
            m_fastPathReferences.TryGetValue(contentKey, out int references);
            m_fastPathReferences[contentKey] = references + 1;

            ushort ns = (ushort)Server.NamespaceUris.GetIndex(m_namespaceUri);
            var fastPathNodeId = new NodeId(contentId, ns);
            if (references > 0 && Find(fastPathNodeId) != null)
            {
                // Another Version already published the identical document; this call only added
                // a reference to the independent content fast path.
                return;
            }

            var node = new BaseDataVariableState(null)
            {
                NodeId = fastPathNodeId,
                BrowseName = new QualifiedName("RegisteredResource", ns),
                DisplayName = new LocalizedText("RegisteredResource"),
                TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
                ReferenceTypeId = ReferenceTypeIds.HasComponent,
                DataType = Ua.DataTypeIds.ByteString,
                ValueRank = ValueRanks.Scalar,
                AccessLevel = AccessLevels.CurrentRead,
                UserAccessLevel = AccessLevels.CurrentRead,
                Historizing = false,
                Value = new Variant(ByteString.From(document))
            };

            await AddPredefinedNodeAsync(SystemContext, node).ConfigureAwait(false);
        }

        /// <summary>
        /// Drops one reference on a content-addressed fast-path node, unpublishing it only once the
        /// last resource that resolves to those bytes has let it go. The caller holds
        /// <see cref="m_gate"/>.
        /// </summary>
        /// <param name="contentKey">The hex content id whose reference is released.</param>
        private ValueTask ReleaseFastPathNodeAsync(string contentKey)
        {
            if (!m_fastPathReferences.TryGetValue(contentKey, out int references))
            {
                return default;
            }
            if (references > 1)
            {
                m_fastPathReferences[contentKey] = references - 1;
                return default;
            }

            m_fastPathReferences.Remove(contentKey);
            var contentId = ByteString.FromHexString(contentKey);
            if (!contentId.IsNull)
            {
                ushort ns = (ushort)Server.NamespaceUris.GetIndex(m_namespaceUri);
                if (Find(new NodeId(contentId, ns)) is { } node)
                {
                    ScheduleNodeDeletionLocked(node);
                }
            }
            return default;
        }

        private bool TryReserveWriteHandleLocked(
            ResourceState resource,
            ISystemContext? context,
            bool seedStagedContent,
            bool append,
            out uint handle,
            bool reading = false,
            ResourceState? file = null)
        {
            handle = 0;
            if (m_writeHandlesByResource.ContainsKey(resource.NodeId) ||
                m_writeHandlesByResource.Count >= m_maxConcurrentUploads)
            {
                return false;
            }
            foreach (ResourceFileHandle existing in m_fileHandles.Values)
            {
                if (existing.ResourceNodeId == resource.NodeId)
                {
                    return false;
                }
            }

            handle = ++m_nextFileHandle;
            var entry = new ResourceFileHandle(
                StoreKeyOf(resource), resource.NodeId, (file ?? resource).NodeId, writing: true, reading)
            {
                SessionId = SessionIdOf(context),
                SeedStagedContent = seedStagedContent,
                Append = append,
                BaselineLength = checked((int)(resource.Size?.Value ?? 0))
            };
            if (TryGetResourceKeyLocked(resource, out ResourceKey key))
            {
                entry.BaselineContentKey = m_versionContentKeys.TryGetValue(
                    key,
                    out string? contentKey)
                        ? contentKey
                        : string.Empty;
            }
            if (entry.BaselineContentKey.Length == 0)
            {
                entry.Ready = true;
            }
            m_fileHandles[handle] = entry;
            m_writeHandlesByResource[resource.NodeId] = handle;
            return true;
        }

        private async ValueTask<ServiceResult> InitializeWriteHandleAsync(
            uint handle,
            CancellationToken cancellationToken)
        {
            ResourceFileHandle entry;
            await using (await EnterGateAsync(CancellationToken.None).ConfigureAwait(false))
            {
                if (!m_fileHandles.TryGetValue(handle, out ResourceFileHandle? found) ||
                    !found.Writing ||
                    found.Closing)
                {
                    return StatusCodes.BadInvalidState;
                }
                entry = found;
                if (entry.Ready)
                {
                    return ServiceResult.Good;
                }
            }

            bool initialized = false;
            try
            {
                var baseline = new byte[entry.BaselineLength];
                int offset = 0;
                do
                {
                    ByteString existing = await m_resourceStore
                        .ReadAsync(entry.StoreKey, offset, baseline.Length - offset, cancellationToken)
                        .ConfigureAwait(false);
                    if (existing.IsNull)
                    {
                        return StatusCodes.BadNotFound;
                    }
                    if ((existing.Length == 0 && offset < baseline.Length) ||
                        existing.Length > baseline.Length - offset)
                    {
                        return StatusCodes.BadUnexpectedError;
                    }
                    existing.Span.CopyTo(baseline.AsSpan(offset));
                    offset += existing.Length;
                }
                while (offset < baseline.Length);

                await using (await EnterGateAsync(CancellationToken.None).ConfigureAwait(false))
                {
                    if (!m_fileHandles.TryGetValue(handle, out ResourceFileHandle? current) ||
                        !ReferenceEquals(current, entry) ||
                        entry.Closing)
                    {
                        return StatusCodes.BadInvalidState;
                    }
                    entry.Baseline = baseline;
                    if (entry.SeedStagedContent)
                    {
                        entry.Buffer.AddRange(baseline);
                    }
                    entry.Position = entry.Append ? entry.Buffer.Count : 0;
                    entry.Ready = true;
                    initialized = true;
                }
                return ServiceResult.Good;
            }
            finally
            {
                if (!initialized)
                {
                    ResourceState? resource;
                    await using (await EnterGateAsync(CancellationToken.None).ConfigureAwait(false))
                    {
                        if (m_fileHandles.TryGetValue(handle, out ResourceFileHandle? current) &&
                            ReferenceEquals(current, entry))
                        {
                            m_fileHandles.Remove(handle);
                            entry.Dispose();
                            m_writeHandlesByResource.Remove(entry.ResourceNodeId);
                        }
                        resource = Find(entry.ResourceNodeId) as ResourceState;
                    }
                    if (resource is not null)
                    {
                        await UpdateFilePropertiesAsync(resource).ConfigureAwait(false);
                    }
                }
            }
        }

        private uint OpenReadHandle(
            ResourceState resource,
            NodeId fileNodeId,
            ISystemContext? context = null,
            bool append = false)
        {
            uint handle = ++m_nextFileHandle;
            m_fileHandles[handle] = new ResourceFileHandle(
                StoreKeyOf(resource), resource.NodeId, fileNodeId, writing: false, reading: true)
            {
                SessionId = SessionIdOf(context),
                Ready = true,
                BaselineLength = checked((int)(resource.Size?.Value ?? 0)),
                HasCommittedContent = TryGetResourceKeyLocked(resource, out ResourceKey key) &&
                    m_versionContentKeys.ContainsKey(key),
                Position = append ? checked((int)(resource.Size?.Value ?? 0)) : 0
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
            if (!m_fileHandles.TryGetValue(fileHandle, out entry) || entry.Closing)
            {
                return false;
            }
            if (entry.FileNodeId != resource.NodeId || entry.SessionId != SessionIdOf(context))
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

        private List<XRegistryEventChange>? BuildGroupCreatedChangesLocked(GroupState group)
        {
            if (m_registry is null)
            {
                return null;
            }
            uint registryEpoch = BumpEntity(m_registry.Epoch, m_registry.ModifiedAt);
            if (!m_eventsEnabled)
            {
                return null;
            }
            return
            [
                FromSource(
                    new XRegistryEventChange(
                        XRegistryEventKind.GroupCreated,
                        GroupSubject(group.GroupId?.Value ?? string.Empty),
                        group.NodeId,
                        group.Epoch?.Value),
                    group),
                FromSource(
                    new XRegistryEventChange(
                        XRegistryEventKind.RegistryUpdated,
                        RegistrySubject(),
                        m_registry.NodeId,
                        registryEpoch,
                        Changed: CollectionChanged(m_groupsAttributeName)),
                    m_registry)
            ];
        }

        private List<XRegistryEventChange>? BuildResourceCreatedChangesLocked(
            GroupState group,
            ResourceState resource,
            bool firstVersion)
        {
            uint groupEpoch = firstVersion
                ? BumpEntity(group.Epoch, group.ModifiedAt)
                : group.Epoch?.Value ?? 0;
            if (!m_eventsEnabled ||
                !TryGetResourceKeyLocked(resource, out ResourceKey key))
            {
                return null;
            }
            uint epoch = resource.Epoch?.Value ?? 0;
            uint metaEpoch = resource.MetaEpoch?.Value ?? 0;
            ResourceState logical = m_logicalResources[new ResourceIdentityKey(key.GroupNodeId, key.ResourceId)];
            List<XRegistryEventChange> changes =
            [
                FromSource(
                    new XRegistryEventChange(
                        XRegistryEventKind.VersionCreated,
                        VersionSubject(key),
                        resource.NodeId,
                        epoch),
                    resource)
            ];
            if (firstVersion)
            {
                changes.Add(FromSource(
                    new XRegistryEventChange(
                        XRegistryEventKind.ResourceCreated,
                        ResourceSubject(key),
                        logical.NodeId,
                        epoch,
                        metaEpoch),
                    logical));
                changes.Add(FromSource(
                    new XRegistryEventChange(
                        XRegistryEventKind.GroupUpdated,
                        GroupSubject(group.GroupId?.Value ?? string.Empty),
                        group.NodeId,
                        groupEpoch,
                        Changed: CollectionChanged(m_resourcesAttributeName)),
                    group));
            }
            else
            {
                changes.Add(FromSource(
                    new XRegistryEventChange(
                        XRegistryEventKind.ResourceUpdated,
                        ResourceSubject(key),
                        logical.NodeId,
                        epoch,
                        metaEpoch,
                        VersionCollectionChanged()),
                    logical));
            }
            return changes;
        }

        private List<XRegistryEventChange>? CaptureRegistryLabelsUpdatedLocked()
        {
            if (m_registry is null)
            {
                return null;
            }
            SetValue(m_registry.ModifiedAt, DateTimeUtc.Now);
            if (!m_eventsEnabled)
            {
                return null;
            }
            return
            [
                FromSource(
                    new XRegistryEventChange(
                        XRegistryEventKind.RegistryUpdated,
                        RegistrySubject(),
                        m_registry.NodeId,
                        m_registry.Epoch?.Value,
                        Changed: ["epoch", "labels", "modifiedat"]),
                    m_registry)
            ];
        }

        private List<XRegistryEventChange>? CaptureGroupLabelsUpdatedLocked(GroupState group)
        {
            SetValue(group.ModifiedAt, DateTimeUtc.Now);
            if (!m_eventsEnabled)
            {
                return null;
            }
            return
            [
                FromSource(
                    new XRegistryEventChange(
                        XRegistryEventKind.GroupUpdated,
                        GroupSubject(group.GroupId?.Value ?? string.Empty),
                        group.NodeId,
                        group.Epoch?.Value,
                        Changed: ["epoch", "labels", "modifiedat"]),
                    group)
            ];
        }

        private List<XRegistryEventChange>? CaptureVersionLabelsUpdatedLocked(
            ResourceState resource)
        {
            SetValue(resource.ModifiedAt, DateTimeUtc.Now);
            if (!m_eventsEnabled ||
                !TryGetResourceKeyLocked(resource, out ResourceKey key))
            {
                return null;
            }
            var logicalKey = new ResourceIdentityKey(key.GroupNodeId, key.ResourceId);
            var changed =
                ImmutableArray.Create("epoch", "labels", "modifiedat");
            var changes = new List<XRegistryEventChange>
            {
                FromSource(
                    new XRegistryEventChange(
                        XRegistryEventKind.VersionUpdated,
                        VersionSubject(key),
                        resource.NodeId,
                        resource.Epoch?.Value,
                        Changed: changed),
                    resource)
            };
            if (m_defaultVersions.TryGetValue(logicalKey, out string? defaultVersion) &&
                string.Equals(defaultVersion, key.VersionId, StringComparison.Ordinal))
            {
                m_resourceMeta.TryGetValue(logicalKey, out ResourceMetaState? meta);
                ResourceState logical = m_logicalResources[logicalKey];
                changes.Add(FromSource(
                    new XRegistryEventChange(
                        XRegistryEventKind.ResourceUpdated,
                        ResourceSubject(key),
                        logical.NodeId,
                        resource.Epoch?.Value,
                        meta?.Epoch ?? 0,
                        changed),
                    logical));
            }
            return changes;
        }

        private async ValueTask<List<XRegistryEventChange>?> BuildResourceDeletionChangesLockedAsync(
            GroupState group,
            ResourceKey deletedKey,
            ResourceState deleted,
            ResourceIdentityKey logicalKey,
            ResourceState logical)
        {
            var remaining = m_resources
                .Where(entry =>
                    entry.Key.GroupNodeId == deletedKey.GroupNodeId &&
                    string.Equals(entry.Key.ResourceId, deletedKey.ResourceId, StringComparison.Ordinal))
                .ToList();
            if (remaining.Count == 0)
            {
                uint groupEpoch = BumpEntity(group.Epoch, group.ModifiedAt);
                if (!m_eventsEnabled)
                {
                    return null;
                }
                return
                [
                    FromSource(
                        new XRegistryEventChange(
                            XRegistryEventKind.VersionDeleted,
                            VersionSubject(deletedKey),
                            deleted.NodeId),
                        deleted,
                        group),
                    FromSource(
                        new XRegistryEventChange(
                            XRegistryEventKind.ResourceDeleted,
                            ResourceSubject(deletedKey),
                            logical.NodeId),
                        logical,
                        group),
                    FromSource(
                    new XRegistryEventChange(
                        XRegistryEventKind.GroupUpdated,
                        GroupSubject(group.GroupId?.Value ?? string.Empty),
                        group.NodeId,
                        groupEpoch,
                        Changed: CollectionChanged(m_resourcesAttributeName)),
                    group)
                ];
            }

            if (!m_resourceMeta.TryGetValue(logicalKey, out ResourceMetaState? meta))
            {
                DateTimeUtc now = DateTimeUtc.Now;
                meta = new ResourceMetaState(1u, now, now);
                m_resourceMeta[logicalKey] = meta;
            }
            meta.Epoch++;
            meta.ModifiedAt = DateTimeUtc.Now;
            if (!m_defaultVersions.TryGetValue(logicalKey, out string? defaultVersion) ||
                string.Equals(defaultVersion, deletedKey.VersionId, StringComparison.Ordinal))
            {
                defaultVersion = remaining
                    .OrderBy(entry => entry.Key.VersionId, StringComparer.Ordinal)
                    .Last().Key.VersionId;
                m_defaultVersions[logicalKey] = defaultVersion;
            }
            await ApplyResourceMetaLockedAsync(logicalKey).ConfigureAwait(false);
            KeyValuePair<ResourceKey, ResourceState> current =
                remaining.First(entry => entry.Key.VersionId == defaultVersion);
            if (!m_eventsEnabled)
            {
                return null;
            }
            return
            [
                FromSource(
                    new XRegistryEventChange(
                        XRegistryEventKind.VersionDeleted,
                        VersionSubject(deletedKey),
                        deleted.NodeId),
                    deleted,
                    logical),
                FromSource(
                    new XRegistryEventChange(
                        XRegistryEventKind.ResourceUpdated,
                        ResourceSubject(deletedKey),
                        logical.NodeId,
                        current.Value.Epoch?.Value,
                        meta.Epoch,
                        VersionCollectionChanged()),
                    logical)
            ];
        }

        private async ValueTask ApplyResourceMetaLockedAsync(ResourceIdentityKey key)
        {
            if (!m_resourceMeta.TryGetValue(key, out ResourceMetaState? meta))
            {
                return;
            }
            foreach (KeyValuePair<ResourceKey, ResourceState> entry in m_resources)
            {
                if (entry.Key.GroupNodeId == key.GroupNodeId &&
                    string.Equals(entry.Key.ResourceId, key.ResourceId, StringComparison.Ordinal))
                {
                    SetValue(entry.Value.MetaEpoch, meta.Epoch);
                    SetValue(entry.Value.MetaCreatedAt, meta.CreatedAt);
                    SetValue(entry.Value.MetaModifiedAt, meta.ModifiedAt);
                    await SynchronizeMetaLabelsLockedAsync(entry.Value.MetaLabels, meta.Labels)
                        .ConfigureAwait(false);
                }
            }
            if (m_logicalResources.TryGetValue(key, out ResourceState? logical))
            {
                SetValue(logical.MetaEpoch, meta.Epoch);
                SetValue(logical.MetaCreatedAt, meta.CreatedAt);
                SetValue(logical.MetaModifiedAt, meta.ModifiedAt);
                await SynchronizeMetaLabelsLockedAsync(logical.MetaLabels, meta.Labels).ConfigureAwait(false);
                await ApplyDefaultVersionViewLockedAsync(key).ConfigureAwait(false);
            }
        }

        private async ValueTask ApplyDefaultVersionViewLockedAsync(ResourceIdentityKey key)
        {
            if (!m_logicalResources.TryGetValue(key, out ResourceState? logical) ||
                DefaultVersionFileLocked(key) is not ResourceState version)
            {
                return;
            }
            SetValue(logical.VersionId, version.VersionId!.Value);
            SetValue(logical.Epoch, version.Epoch!.Value);
            SetValue(logical.CreatedAt, version.CreatedAt!.Value);
            SetValue(logical.ModifiedAt, version.ModifiedAt!.Value);
            SetValue(logical.Format, version.Format!.Value);
            SetValue(logical.ContentType, version.ContentType!.Value);
            XRegistryProjectionEngine.MirrorFileTypeProperties(logical, version);
            var labels = new Dictionary<string, string>(StringComparer.Ordinal);
            var children = new List<BaseInstanceState>();
            version.Labels!.GetChildren(SystemContext, children);
            foreach (BaseInstanceState child in children)
            {
                if (child is PropertyState<string> property &&
                    m_dynamicAttributes.TryGetValue(property.NodeId, out PropertyState<string>? registered) &&
                    ReferenceEquals(property, registered))
                {
                    labels.Add(property.BrowseName.Name!, property.Value);
                }
            }
            await SynchronizeMetaLabelsLockedAsync(logical.Labels, labels).ConfigureAwait(false);
        }

        private async ValueTask SynchronizeMetaLabelsLockedAsync(
            AttributesState? labels,
            IReadOnlyDictionary<string, string> desired)
        {
            if (labels is null)
            {
                return;
            }
            var existing = new Dictionary<string, PropertyState<string>>(StringComparer.Ordinal);
            var children = new List<BaseInstanceState>();
            labels.GetChildren(SystemContext, children);
            foreach (BaseInstanceState child in children)
            {
                if (child is PropertyState<string> property &&
                    m_dynamicAttributes.TryGetValue(property.NodeId, out PropertyState<string>? registered) &&
                    ReferenceEquals(property, registered) &&
                    property.BrowseName.Name is string name)
                {
                    existing[name] = property;
                }
            }
            foreach (KeyValuePair<string, string> value in desired)
            {
                _ = await SetAttributeLockedAsync(labels, value.Key, value.Value).ConfigureAwait(false);
                existing.Remove(value.Key);
            }
            foreach (string stale in existing.Keys)
            {
                _ = await RemoveAttributeLockedAsync(labels, stale).ConfigureAwait(false);
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

        private bool TryGetResourceIdentityLocked(ResourceState resource, out ResourceIdentityKey key)
        {
            if (TryGetResourceKeyLocked(resource, out ResourceKey version))
            {
                key = new ResourceIdentityKey(version.GroupNodeId, version.ResourceId);
                return true;
            }
            foreach (KeyValuePair<ResourceIdentityKey, ResourceState> logical in m_logicalResources)
            {
                if (ReferenceEquals(logical.Value, resource))
                {
                    key = logical.Key;
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
            return ResourceSubject(new ResourceIdentityKey(key.GroupNodeId, key.ResourceId));
        }

        private string ResourceSubject(ResourceIdentityKey key)
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

        private ResourceState? DefaultVersionFileLocked(ResourceIdentityKey key)
        {
            if (!m_defaultVersions.TryGetValue(key, out string? versionId))
            {
                return null;
            }
            m_resources.TryGetValue(
                new ResourceKey(key.GroupNodeId, key.ResourceId, versionId),
                out ResourceState? resource);
            return resource;
        }

        private static XRegistryEventChange FromSource(
            XRegistryEventChange change,
            NodeState source,
            NodeState? notifier = null)
        {
            return change with
            {
                SourceName = source.DisplayName.Text ?? source.BrowseName.Name,
                Notifier = notifier ?? source
            };
        }

        private async ValueTask ReportChangesAsync(
            List<XRegistryEventChange>? changes,
            NodeState? changedNode)
        {
            var changedNodes = new HashSet<NodeState>();
            if (changedNode is not null)
            {
                changedNodes.Add(changedNode);
            }
            if (changes is not null)
            {
                foreach (XRegistryEventChange change in changes)
                {
                    if (change.Notifier is not null)
                    {
                        changedNodes.Add(change.Notifier);
                    }
                }
            }
            foreach (NodeState node in changedNodes)
            {
                await NotifyMetadataAsync(node).ConfigureAwait(false);
            }
            if (m_eventEmitter is not null && m_registry is not null && changes is not null)
            {
                await m_eventEmitter.ReportAsync(m_registry, changes).ConfigureAwait(false);
            }
        }

        private async ValueTask NotifyMetadataAsync(NodeState node)
        {
            var children = new List<BaseInstanceState>();
            node.GetChildren(SystemContext, children);
            foreach (BaseInstanceState child in children)
            {
                if (child is BaseVariableState or AttributesState)
                {
                    await child.ClearChangeMasksAsync(SystemContext, includeChildren: true).ConfigureAwait(false);
                }
            }
            await node.ClearChangeMasksAsync(SystemContext, includeChildren: false).ConfigureAwait(false);
        }

        private async ValueTask NotifyResourceMetaAsync(ResourceIdentityKey identity)
        {
            List<NodeState> nodes;
            await using (await EnterGateAsync(CancellationToken.None).ConfigureAwait(false))
            {
                nodes = [.. m_resources
                    .Where(entry => entry.Key.GroupNodeId == identity.GroupNodeId &&
                        string.Equals(entry.Key.ResourceId, identity.ResourceId, StringComparison.Ordinal))
                    .Select(entry => (NodeState)entry.Value)];
                if (m_logicalResources.TryGetValue(identity, out ResourceState? logical))
                {
                    nodes.Add(logical);
                    nodes.Add(logical.Versions!);
                }
            }
            foreach (NodeState node in nodes)
            {
                await NotifyMetadataAsync(node).ConfigureAwait(false);
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

        private sealed class ResourceMetaState(
            uint epoch,
            DateTimeUtc createdAt,
            DateTimeUtc modifiedAt)
        {
            public uint Epoch { get; set; } = epoch;
            public DateTimeUtc CreatedAt { get; } = createdAt;
            public DateTimeUtc ModifiedAt { get; set; } = modifiedAt;

            public Dictionary<string, string> Labels { get; } =
                new(StringComparer.Ordinal);
        }

        /// <summary>
        /// An open file handle on a resource: a bounded upload buffer for a write handle, or a
        /// cursor over the stored document for a read handle.
        /// </summary>
        private sealed class ResourceFileHandle(
            string storeKey,
            NodeId resourceNodeId,
            NodeId fileNodeId,
            bool writing,
            bool reading) : IDisposable
        {
            public string StoreKey { get; } = storeKey;

            /// <summary>
            /// The exact Version pinned when the handle was opened, including through a logical file.
            /// </summary>
            public NodeId ResourceNodeId { get; } = resourceNodeId;

            /// <summary>
            /// The file object whose Methods may use the handle.
            /// </summary>
            public NodeId FileNodeId { get; } = fileNodeId;

            /// <summary>
            /// The session that opened the handle, or a null NodeId for an in-process call (the
            /// server's own bootstrap). A handle is only valid to the session that owns it.
            /// </summary>
            public NodeId SessionId { get; set; }

            public bool Writing { get; } = writing;
            public bool Reading { get; } = reading;

            /// <summary>
            /// Whether the handle has finished being seeded from the store. A write handle that does
            /// not erase starts from the stored document, which is read outside the lock.
            /// </summary>
            public bool Ready { get; set; }

            public bool Closing { get; set; }
            public bool SeedStagedContent { get; set; }
            public bool Append { get; set; }
            public bool HasAcceptedWrite { get; set; }
            public bool HasCommittedContent { get; set; }
            public int BaselineLength { get; set; }
            public byte[] Baseline { get; set; } = [];
            public string BaselineContentKey { get; set; } = string.Empty;

            public List<byte> Buffer { get; } = [];
            public ByteString Content { get; set; }
            public int Position { get; set; }

            public async ValueTask<TResult> ExecuteAsync<TResult>(
                Func<ValueTask<TResult>> action,
                TResult invalidHandle,
                CancellationToken cancellationToken)
            {
                lock (m_lifetimeLock)
                {
                    if (m_disposed)
                    {
                        return invalidHandle;
                    }
                    m_activeOperations++;
                }
                try
                {
                    await m_cursorGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        return await action().ConfigureAwait(false);
                    }
                    finally
                    {
                        m_cursorGate.Release();
                    }
                }
                finally
                {
                    lock (m_lifetimeLock)
                    {
                        m_activeOperations--;
                        if (m_disposed && m_activeOperations == 0)
                        {
                            m_cursorGate.Dispose();
                        }
                    }
                }
            }

            public void Dispose()
            {
                lock (m_lifetimeLock)
                {
                    if (m_disposed)
                    {
                        return;
                    }
                    m_disposed = true;
                    if (m_activeOperations == 0)
                    {
                        m_cursorGate.Dispose();
                    }
                }
            }

            private readonly SemaphoreSlim m_cursorGate = new(1, 1);
            private readonly Lock m_lifetimeLock = new();
            private int m_activeOperations;
            private bool m_disposed;
        }

        /// <summary>
        /// Tests whether the caller's secure channel is good enough to mutate the registry. A
        /// resource document and its content lookup are integrity-critical, so a write is
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
            property?.Value = value;
        }

        private void BindMetaAttributeMethods(ResourceState resource)
        {
            AttributesState? labels = resource.MetaLabels;
            if (labels is null)
            {
                return;
            }
            labels.AddAddAttribute(SystemContext)
                .AddRemoveAttribute(SystemContext);
            labels.AddAttribute?.OnCallAsync = (ctx, m, id, key, value, expectedEpoch, ct) =>
                    IsWriteChannelSecure(ctx)
                        ? OnAddMetaAttributeAsync(resource, key, value, expectedEpoch, ct)
                        : new ValueTask<AddAttributeMethodStateResult>(
                            new AddAttributeMethodStateResult
                            {
                                ServiceResult = StatusCodes.BadSecurityModeInsufficient
                            });
            labels.RemoveAttribute?.OnCallAsync = (ctx, m, id, key, expectedEpoch, ct) =>
                    IsWriteChannelSecure(ctx)
                        ? OnRemoveMetaAttributeAsync(resource, key, expectedEpoch, ct)
                        : new ValueTask<RemoveAttributeMethodStateResult>(
                            new RemoveAttributeMethodStateResult
                            {
                                ServiceResult = StatusCodes.BadSecurityModeInsufficient
                            });
        }

        private async ValueTask<AddAttributeMethodStateResult> OnAddMetaAttributeAsync(
            ResourceState resource,
            string key,
            string value,
            uint expectedEpoch,
            CancellationToken cancellationToken)
        {
            using OperationLease operation = BeginOperation(cancellationToken);
            if (string.IsNullOrEmpty(key))
            {
                return new AddAttributeMethodStateResult
                {
                    ServiceResult = StatusCodes.BadInvalidArgument
                };
            }

            List<XRegistryEventChange>? changes;
            ResourceIdentityKey logicalKey;
            await using (await EnterGateAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!TryGetResourceIdentityLocked(resource, out logicalKey))
                {
                    return new AddAttributeMethodStateResult
                    {
                        ServiceResult = StatusCodes.BadInvalidState
                    };
                }
                if (!m_resourceMeta.TryGetValue(logicalKey, out ResourceMetaState? meta) ||
                    (expectedEpoch != 0 && meta.Epoch != expectedEpoch))
                {
                    return new AddAttributeMethodStateResult
                    {
                        ServiceResult = StatusCodes.BadInvalidState
                    };
                }
                foreach (KeyValuePair<ResourceKey, ResourceState> version in m_resources)
                {
                    if (version.Key.GroupNodeId == logicalKey.GroupNodeId &&
                        string.Equals(version.Key.ResourceId, logicalKey.ResourceId, StringComparison.Ordinal) &&
                        version.Value.MetaLabels is AttributesState labels &&
                        HasFixedAttributeMemberLocked(labels, key))
                    {
                        return new AddAttributeMethodStateResult
                        {
                            ServiceResult = StatusCodes.BadBrowseNameDuplicated
                        };
                    }
                }
                if (meta.Labels.TryGetValue(key, out string? existing) &&
                    string.Equals(existing, value, StringComparison.Ordinal))
                {
                    return new AddAttributeMethodStateResult { ServiceResult = ServiceResult.Good };
                }
                meta.Labels[key] = value;
                meta.Epoch++;
                meta.ModifiedAt = DateTimeUtc.Now;
                await ApplyResourceMetaLockedAsync(logicalKey).ConfigureAwait(false);
                changes = CaptureResourceMetaUpdatedLocked(logicalKey);
            }
            await NotifyResourceMetaAsync(logicalKey).ConfigureAwait(false);
            await ReportChangesAsync(changes, resource).ConfigureAwait(false);
            return new AddAttributeMethodStateResult { ServiceResult = ServiceResult.Good };
        }

        private async ValueTask<RemoveAttributeMethodStateResult> OnRemoveMetaAttributeAsync(
            ResourceState resource,
            string key,
            uint expectedEpoch,
            CancellationToken cancellationToken)
        {
            using OperationLease operation = BeginOperation(cancellationToken);
            List<XRegistryEventChange>? changes;
            ResourceIdentityKey logicalKey;
            await using (await EnterGateAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!TryGetResourceIdentityLocked(resource, out logicalKey))
                {
                    return new RemoveAttributeMethodStateResult
                    {
                        ServiceResult = StatusCodes.BadInvalidState
                    };
                }
                if (!m_resourceMeta.TryGetValue(logicalKey, out ResourceMetaState? meta) ||
                    (expectedEpoch != 0 && meta.Epoch != expectedEpoch))
                {
                    return new RemoveAttributeMethodStateResult
                    {
                        ServiceResult = StatusCodes.BadInvalidState
                    };
                }
                if (!meta.Labels.Remove(key))
                {
                    return new RemoveAttributeMethodStateResult
                    {
                        ServiceResult = StatusCodes.BadNotFound
                    };
                }
                meta.Epoch++;
                meta.ModifiedAt = DateTimeUtc.Now;
                await ApplyResourceMetaLockedAsync(logicalKey).ConfigureAwait(false);
                changes = CaptureResourceMetaUpdatedLocked(logicalKey);
            }
            await NotifyResourceMetaAsync(logicalKey).ConfigureAwait(false);
            await ReportChangesAsync(changes, resource).ConfigureAwait(false);
            return new RemoveAttributeMethodStateResult { ServiceResult = ServiceResult.Good };
        }

        private List<XRegistryEventChange>? CaptureResourceMetaUpdatedLocked(
            ResourceIdentityKey logicalKey)
        {
            if (!m_eventsEnabled ||
                !m_resourceMeta.TryGetValue(logicalKey, out ResourceMetaState? meta))
            {
                return null;
            }
            if (!m_logicalResources.TryGetValue(logicalKey, out ResourceState? source))
            {
                return null;
            }
            return
            [
                FromSource(
                    new XRegistryEventChange(
                        XRegistryEventKind.ResourceUpdated,
                        ResourceSubject(logicalKey),
                        source.NodeId,
                        source.Epoch?.Value,
                        meta.Epoch,
                        ["meta.epoch", "meta.labels", "meta.modifiedat"]),
                    source)
            ];
        }

        /// <summary>
        /// Binds the <c>AttributesType</c> Methods on a <c>Labels</c> Object. Both mutate the owning
        /// node, so both take the owner's epoch as an optimistic-concurrency check.
        /// </summary>
        /// <param name="labels">The Labels Object, when the owner exposes one.</param>
        /// <param name="epoch">Accessor for the owning node's epoch.</param>
        /// <param name="captureChangesLocked">
        /// Callback that updates the owner's canonical timestamps and captures its event batch.
        /// It is invoked while <see cref="m_gate"/> is held.
        /// </param>
        private void BindAttributeMethods(
            AttributesState? labels,
            Func<PropertyState<uint>?> epoch,
            Func<List<XRegistryEventChange>?>? captureChangesLocked = null)
        {
            if (labels == null)
            {
                return;
            }

            labels.AddAddAttribute(SystemContext)
                .AddRemoveAttribute(SystemContext);

            labels.AddAttribute?.OnCallAsync = (ctx, m, id, key, value, expectedEpoch, ct) =>
                    IsWriteChannelSecure(ctx)
                        ? OnAddBoundAttributeAsync(
                            labels,
                            epoch(),
                            key,
                            value,
                            expectedEpoch,
                            captureChangesLocked,
                            ct)
                        : new ValueTask<AddAttributeMethodStateResult>(
                            new AddAttributeMethodStateResult
                            {
                                ServiceResult = StatusCodes.BadSecurityModeInsufficient
                            });
            labels.RemoveAttribute?.OnCallAsync = (ctx, m, id, key, expectedEpoch, ct) =>
                    IsWriteChannelSecure(ctx)
                        ? OnRemoveBoundAttributeAsync(
                            labels,
                            epoch(),
                            key,
                            expectedEpoch,
                            captureChangesLocked,
                            ct)
                        : new ValueTask<RemoveAttributeMethodStateResult>(
                            new RemoveAttributeMethodStateResult
                            {
                                ServiceResult = StatusCodes.BadSecurityModeInsufficient
                            });
        }

        private async ValueTask<AddAttributeMethodStateResult> OnAddBoundAttributeAsync(
            AttributesState labels,
            PropertyState<uint>? epoch,
            string key,
            string value,
            uint expectedEpoch,
            Func<List<XRegistryEventChange>?>? captureChangesLocked,
            CancellationToken cancellationToken)
        {
            using OperationLease operation = BeginOperation(cancellationToken);
            if (string.IsNullOrEmpty(key))
            {
                return new AddAttributeMethodStateResult
                {
                    ServiceResult = StatusCodes.BadInvalidArgument
                };
            }

            List<XRegistryEventChange>? changes;
            ResourceIdentityKey identity = default;
            bool resourceChanged = false;
            await using (await EnterGateAsync(cancellationToken).ConfigureAwait(false))
            {
                if (labels.Parent is ResourceState resource &&
                    TryResolveVersionLocked(resource, out ResourceState? version))
                {
                    labels = version.Labels!;
                    epoch = version.Epoch;
                    captureChangesLocked = () => CaptureVersionLabelsUpdatedLocked(version);
                }
                if (!IsAttributeOwnerRegisteredLocked(labels))
                {
                    return new AddAttributeMethodStateResult { ServiceResult = StatusCodes.BadNodeIdUnknown };
                }
                if (!IsEpochCurrent(epoch, expectedEpoch))
                {
                    return new AddAttributeMethodStateResult
                    {
                        ServiceResult = StatusCodes.BadInvalidState
                    };
                }
                if (HasFixedAttributeMemberLocked(labels, key))
                {
                    return new AddAttributeMethodStateResult
                    {
                        ServiceResult = StatusCodes.BadBrowseNameDuplicated
                    };
                }
                if (!await SetAttributeLockedAsync(labels, key, value).ConfigureAwait(false))
                {
                    return new AddAttributeMethodStateResult { ServiceResult = ServiceResult.Good };
                }
                BumpEpoch(epoch);
                changes = captureChangesLocked?.Invoke();
                if (labels.Parent is ResourceState owner &&
                    TryGetResourceIdentityLocked(owner, out identity))
                {
                    resourceChanged = true;
                    await ApplyDefaultVersionViewLockedAsync(identity).ConfigureAwait(false);
                }
            }
            if (resourceChanged)
            {
                await NotifyResourceMetaAsync(identity).ConfigureAwait(false);
            }
            await ReportChangesAsync(changes, labels.Parent ?? labels).ConfigureAwait(false);
            return new AddAttributeMethodStateResult { ServiceResult = ServiceResult.Good };
        }

        private async ValueTask<RemoveAttributeMethodStateResult> OnRemoveBoundAttributeAsync(
            AttributesState labels,
            PropertyState<uint>? epoch,
            string key,
            uint expectedEpoch,
            Func<List<XRegistryEventChange>?>? captureChangesLocked,
            CancellationToken cancellationToken)
        {
            using OperationLease operation = BeginOperation(cancellationToken);
            List<XRegistryEventChange>? changes;
            ResourceIdentityKey identity = default;
            bool resourceChanged = false;
            await using (await EnterGateAsync(cancellationToken).ConfigureAwait(false))
            {
                if (labels.Parent is ResourceState resource &&
                    TryResolveVersionLocked(resource, out ResourceState? version))
                {
                    labels = version.Labels!;
                    epoch = version.Epoch;
                    captureChangesLocked = () => CaptureVersionLabelsUpdatedLocked(version);
                }
                if (!IsAttributeOwnerRegisteredLocked(labels))
                {
                    return new RemoveAttributeMethodStateResult { ServiceResult = StatusCodes.BadNodeIdUnknown };
                }
                if (!IsEpochCurrent(epoch, expectedEpoch))
                {
                    return new RemoveAttributeMethodStateResult
                    {
                        ServiceResult = StatusCodes.BadInvalidState
                    };
                }
                if (!await RemoveAttributeLockedAsync(labels, key).ConfigureAwait(false))
                {
                    return new RemoveAttributeMethodStateResult
                    {
                        ServiceResult = StatusCodes.BadNotFound
                    };
                }
                BumpEpoch(epoch);
                changes = captureChangesLocked?.Invoke();
                if (labels.Parent is ResourceState owner &&
                    TryGetResourceIdentityLocked(owner, out identity))
                {
                    resourceChanged = true;
                    await ApplyDefaultVersionViewLockedAsync(identity).ConfigureAwait(false);
                }
            }
            if (resourceChanged)
            {
                await NotifyResourceMetaAsync(identity).ConfigureAwait(false);
            }
            await ReportChangesAsync(changes, labels.Parent ?? labels).ConfigureAwait(false);
            return new RemoveAttributeMethodStateResult { ServiceResult = ServiceResult.Good };
        }

        /// <summary>
        /// Handles <c>AttributesType.AddAttribute(Key, Value, ExpectedEpoch)</c>, adding or
        /// replacing a label on the owning node.
        /// </summary>
        internal async ValueTask<AddAttributeMethodStateResult> OnAddAttributeAsync(
            AttributesState labels,
            PropertyState<uint>? epoch,
            string key,
            string value,
            uint expectedEpoch,
            Action? changed = null,
            CancellationToken cancellationToken = default)
        {
            using OperationLease operation = BeginOperation(cancellationToken);
            if (string.IsNullOrEmpty(key))
            {
                return new AddAttributeMethodStateResult { ServiceResult = StatusCodes.BadInvalidArgument };
            }

            await using (await EnterGateAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!IsEpochCurrent(epoch, expectedEpoch))
                {
                    return new AddAttributeMethodStateResult { ServiceResult = StatusCodes.BadInvalidState };
                }
                if (HasFixedAttributeMemberLocked(labels, key))
                {
                    return new AddAttributeMethodStateResult
                    {
                        ServiceResult = StatusCodes.BadBrowseNameDuplicated
                    };
                }

                if (!await SetAttributeLockedAsync(labels, key, value).ConfigureAwait(false))
                {
                    return new AddAttributeMethodStateResult { ServiceResult = ServiceResult.Good };
                }

                BumpEpoch(epoch);
            }
            await NotifyMetadataAsync(labels.Parent ?? labels).ConfigureAwait(false);
            changed?.Invoke();
            return new AddAttributeMethodStateResult { ServiceResult = ServiceResult.Good };
        }

        /// <summary>
        /// Handles <c>AttributesType.RemoveAttribute(Key, ExpectedEpoch)</c>.
        /// </summary>
        internal async ValueTask<RemoveAttributeMethodStateResult> OnRemoveAttributeAsync(
            AttributesState labels,
            PropertyState<uint>? epoch,
            string key,
            uint expectedEpoch,
            Action? changed = null,
            CancellationToken cancellationToken = default)
        {
            using OperationLease operation = BeginOperation(cancellationToken);
            await using (await EnterGateAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!IsEpochCurrent(epoch, expectedEpoch))
                {
                    return new RemoveAttributeMethodStateResult
                    {
                        ServiceResult = StatusCodes.BadInvalidState
                    };
                }

                if (!await RemoveAttributeLockedAsync(labels, key).ConfigureAwait(false))
                {
                    return new RemoveAttributeMethodStateResult { ServiceResult = StatusCodes.BadNotFound };
                }

                BumpEpoch(epoch);
            }
            await NotifyMetadataAsync(labels.Parent ?? labels).ConfigureAwait(false);
            changed?.Invoke();
            return new RemoveAttributeMethodStateResult { ServiceResult = ServiceResult.Good };
        }

        private async ValueTask<bool> SetAttributeLockedAsync(AttributesState labels, string key, string value)
        {
            if (HasFixedAttributeMemberLocked(labels, key))
            {
                throw new ServiceResultException(
                    StatusCodes.BadBrowseNameDuplicated, "A fixed member already uses the attribute name.");
            }
            ushort ns = (ushort)Server.NamespaceUris.GetIndex(m_namespaceUri);
            var browseName = new QualifiedName(key, ns);
            if (labels.FindChild(SystemContext, browseName) is PropertyState<string> existing)
            {
                if (string.Equals(existing.Value, value, StringComparison.Ordinal))
                {
                    return false;
                }
                existing.Value = value;
                return true;
            }
            var attribute = PropertyState<string>.With<VariantBuilder>(labels, value);
            attribute.NodeId = new NodeId(m_nextInstanceId++, ns);
            attribute.BrowseName = browseName;
            attribute.DisplayName = new LocalizedText(key);
            attribute.ReferenceTypeId = ReferenceTypeIds.HasProperty;
            attribute.TypeDefinitionId = VariableTypeIds.PropertyType;
            attribute.DataType = Ua.DataTypeIds.String;
            attribute.ValueRank = ValueRanks.Scalar;
            attribute.AccessLevel = AccessLevels.CurrentRead;
            attribute.UserAccessLevel = AccessLevels.CurrentRead;
            labels.AddChild(attribute);
            m_dynamicAttributes.Add(attribute.NodeId, attribute);
            await AddPredefinedNodeAsync(SystemContext, attribute).ConfigureAwait(false);
            return true;
        }

        private bool HasFixedAttributeMemberLocked(AttributesState labels, string key)
        {
            ushort ns = (ushort)Server.NamespaceUris.GetIndex(m_namespaceUri);
            BaseInstanceState? existing = labels.FindChild(SystemContext, new QualifiedName(key, ns));
            return existing is not null &&
                (!m_dynamicAttributes.TryGetValue(existing.NodeId, out PropertyState<string>? attribute) ||
                    !ReferenceEquals(attribute, existing));
        }

        private bool IsAttributeOwnerRegisteredLocked(AttributesState labels)
        {
            return labels.Parent switch
            {
                RegistryState registry => ReferenceEquals(m_registry, registry),
                GroupState group => m_groupsByNodeId.TryGetValue(group.NodeId, out GroupState? current) &&
                    ReferenceEquals(current, group),
                ResourceState resource => IsRegisteredLocked(resource),
                _ => false
            };
        }

        private ValueTask<bool> RemoveAttributeLockedAsync(AttributesState labels, string key)
        {
            ushort ns = (ushort)Server.NamespaceUris.GetIndex(m_namespaceUri);
            if (labels.FindChild(
                    SystemContext,
                    new QualifiedName(key, ns)) is not PropertyState<string> attribute ||
                !m_dynamicAttributes.TryGetValue(attribute.NodeId, out PropertyState<string>? registered) ||
                !ReferenceEquals(registered, attribute))
            {
                return new ValueTask<bool>(false);
            }
            labels.RemoveChild(attribute);
            ScheduleNodeDeletionLocked(attribute);
            return new ValueTask<bool>(true);
        }

        private static void BumpEpoch(PropertyState<uint>? epoch)
        {
            if (epoch != null)
            {
                epoch.Value++;
            }
        }

        private async ValueTask<GateLease> EnterGateAsync(CancellationToken cancellationToken = default)
        {
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            return new GateLease(this);
        }

        private void ScheduleNodeDeletionLocked(NodeState node)
        {
            m_pendingDeletions ??= new DeletionBatch();
            int first = m_pendingDeletions.Nodes.Count;
            m_pendingDeletions.AddSubtree(node, SystemContext);
            for (int i = first; i < m_pendingDeletions.Nodes.Count; i++)
            {
                NodeState removed = m_pendingDeletions.Nodes[i];
                if (m_dynamicAttributes.TryGetValue(removed.NodeId, out PropertyState<string>? attribute) &&
                    ReferenceEquals(attribute, removed))
                {
                    m_dynamicAttributes.Remove(removed.NodeId);
                }
            }
        }

        private ValueTask ExitGateAsync()
        {
            DeletionBatch? pending = m_pendingDeletions;
            m_pendingDeletions = null;
            m_gate.Release();
            return pending is null ? default : CompleteDeletionsAsync(pending);
        }

        private async ValueTask RetryDeletionsAsync(CancellationToken cancellationToken)
        {
            DeletionBatch? retry;
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                retry = m_retryDeletions;
                m_retryDeletions = null;
            }
            finally
            {
                m_gate.Release();
            }
            if (retry is not null)
            {
                await CompleteDeletionsAsync(retry).ConfigureAwait(false);
            }
        }

        private async ValueTask CompleteDeletionsAsync(DeletionBatch pending)
        {
            var failures = new List<Exception>();
            foreach (NodeState node in pending.Nodes)
            {
                try
                {
                    await DeletePublishedNodeAsync(node).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    // A sink can fail after unindexing. Complete the rest of the committed
                    // batch before propagating every failure, including storage failures.
                    failures.Add(exception);
                }
                finally
                {
                    await m_gate.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        if (ReferenceEquals(Find(node.NodeId), node))
                        {
                            m_retryDeletions ??= new DeletionBatch();
                            m_retryDeletions.AddSubtree(node, SystemContext);
                        }
                        else if (node is BaseInstanceState { Parent: { } parent } instance)
                        {
                            parent.RemoveChild(instance);
                        }
                    }
                    finally
                    {
                        m_gate.Release();
                    }
                }
            }
            foreach (string storeKey in pending.StoreKeys)
            {
                try
                {
                    _ = await m_resourceStore.DeleteAsync(storeKey, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                    await m_gate.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        m_retryDeletions ??= new DeletionBatch();
                        m_retryDeletions.StoreKeys.Add(storeKey);
                    }
                    finally
                    {
                        m_gate.Release();
                    }
                }
            }
            if (failures.Count == 1)
            {
                ExceptionDispatchInfo.Capture(failures[0]).Throw();
            }
            if (failures.Count > 1)
            {
                throw new AggregateException(
                    "The committed registry deletion could not be fully cleaned up.", failures);
            }
        }

        private async ValueTask DeletePublishedNodeAsync(NodeState node)
        {
            ValueTask<bool> deletion;
            await m_gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!ReferenceEquals(Find(node.NodeId), node))
                {
                    return;
                }
                // Capture the exact instance under the gate, but await callbacks outside it.
                deletion = DeleteNodeAsync(SystemContext, node.NodeId);
            }
            finally
            {
                m_gate.Release();
            }
            await deletion.ConfigureAwait(false);
        }

        private sealed class DeletionBatch
        {
            public List<NodeState> Nodes { get; } = [];
            public HashSet<string> StoreKeys { get; } = new(StringComparer.Ordinal);

            public void AddSubtree(NodeState node, ISystemContext context)
            {
                if (!m_nodes.Add(node))
                {
                    return;
                }
                var children = new List<BaseInstanceState>();
                node.GetChildren(context, children);
                foreach (BaseInstanceState child in children)
                {
                    AddSubtree(child, context);
                }
                // Snapshot every descendant before callbacks can interrupt recursive removal.
                Nodes.Add(node);
            }

            private readonly HashSet<NodeState> m_nodes = [];
        }

        /// <inheritdoc/>
        protected override async ValueTask RemovePredefinedNodeAsync(
            ISystemContext context,
            NodeState node,
            List<LocalReference> referencesToRemove,
            CancellationToken cancellationToken = default)
        {
            ValueTask removal;
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!ReferenceEquals(Find(node.NodeId), node))
                {
                    return;
                }
                // The base unindexes before its first await. A replacement with the same
                // content-id can then be published without the old removal deleting it.
                removal = base.RemovePredefinedNodeAsync(context, node, referencesToRemove, cancellationToken);
            }
            finally
            {
                m_gate.Release();
            }
            await removal.ConfigureAwait(false);
        }

        private OperationLease BeginOperation(
            CancellationToken cancellationToken,
            bool requireAddressSpace = true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_lifetimeGate)
            {
                if (m_stopping)
                {
                    throw new ObjectDisposedException(nameof(XRegistryRegistrationNodeManager));
                }
                if (requireAddressSpace && !m_addressSpaceReady)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidState, "The address space is not ready.");
                }
                if (!requireAddressSpace)
                {
                    if (m_starting || m_addressSpaceReady)
                    {
                        throw new ServiceResultException(StatusCodes.BadInvalidState, "Startup is already active.");
                    }
                    m_starting = true;
                }
                m_activeOperations++;
            }
            return new OperationLease(this);
        }

        private void CompleteOperation()
        {
            bool dispose;
            lock (m_lifetimeGate)
            {
                m_activeOperations--;
                if (m_stopping && m_activeOperations == 0)
                {
                    m_operationsDrained.TrySetResult(true);
                }
                dispose = ReserveDisposalLocked();
            }
            if (dispose)
            {
                DisposeResources();
            }
        }

        private bool ReserveDisposalLocked()
        {
            if (!m_disposeRequested ||
                m_resourcesDisposed ||
                m_activeOperations != 0 ||
                m_activeTeardowns != 0)
            {
                return false;
            }
            m_resourcesDisposed = true;
            return true;
        }

        private void ClearRuntimeState()
        {
            foreach (ResourceFileHandle handle in m_fileHandles.Values)
            {
                handle.Dispose();
            }
            m_fileHandles.Clear();
            m_writeHandlesByResource.Clear();
            m_groups.Clear();
            m_groupsByNodeId.Clear();
            m_resources.Clear();
            m_logicalResources.Clear();
            m_dynamicAttributes.Clear();
            m_versionCounters.Clear();
            m_resourceMeta.Clear();
            m_defaultVersions.Clear();
            m_versionContentKeys.Clear();
            m_fastPathReferences.Clear();
            m_registeredResourceCount = 0;
            m_registry = null;
            m_eventEmitter = null;
            m_addressSpaceReady = false;
        }

        private void DisposeResources()
        {
            ClearRuntimeState();
            m_gate.Dispose();
            base.Dispose(true);
        }

        private readonly struct OperationLease(XRegistryRegistrationNodeManager manager) : IDisposable
        {
            public void Dispose()
            {
                manager.CompleteOperation();
            }
        }

        private readonly struct GateLease(XRegistryRegistrationNodeManager manager) : IAsyncDisposable
        {
            public ValueTask DisposeAsync()
            {
                return manager.ExitGateAsync();
            }
        }

        private readonly Lock m_lifetimeGate = new();

        private readonly TaskCompletionSource<bool> m_operationsDrained =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private int m_activeOperations;
        private int m_activeTeardowns;
        private bool m_stopping;
        private bool m_disposeRequested;
        private bool m_resourcesDisposed;
        private bool m_addressSpaceReady;
        private bool m_starting;
        private readonly SemaphoreSlim m_gate = new(1, 1);
        private DeletionBatch? m_pendingDeletions;
        private DeletionBatch? m_retryDeletions;
        private readonly Dictionary<string, GroupState> m_groups = [];
        private readonly Dictionary<NodeId, GroupState> m_groupsByNodeId = [];
        private readonly Dictionary<ResourceKey, ResourceState> m_resources = [];
        private readonly Dictionary<ResourceIdentityKey, ResourceState> m_logicalResources = [];
        private readonly Dictionary<NodeId, PropertyState<string>> m_dynamicAttributes = [];
        private readonly Dictionary<uint, ResourceFileHandle> m_fileHandles = [];
        private readonly Dictionary<NodeId, uint> m_writeHandlesByResource = [];
        private readonly Dictionary<VersionCounterKey, uint> m_versionCounters = [];
        private readonly Dictionary<ResourceIdentityKey, ResourceMetaState> m_resourceMeta = [];
        private readonly Dictionary<ResourceIdentityKey, string> m_defaultVersions = [];
        private readonly Dictionary<ResourceKey, string> m_versionContentKeys = [];
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
