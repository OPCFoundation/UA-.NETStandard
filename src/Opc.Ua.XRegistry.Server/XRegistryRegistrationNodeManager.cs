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
using System.Diagnostics.CodeAnalysis;
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
            BindAttributeMethods(registry.Labels, () => registry.Epoch);

            SetValue(registry.RegistryId, m_registryId);
            SetValue(registry.SpecVersion, m_specVersion);
            SetValue(registry.Xid, m_registryId);
            SetValue(registry.Epoch, 1u);
            SetValue(registry.CreatedAt, DateTimeUtc.Now);
            SetValue(registry.ModifiedAt, DateTimeUtc.Now);

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

                GroupState group = CreateGroupNode(groupId);
                return new ValueTask<CreateGroupMethodStateResult>(
                    new CreateGroupMethodStateResult
                    {
                        ServiceResult = ServiceResult.Good,
                        GroupNodeId = group.NodeId
                    });
            }
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

                GroupState group = CreateGroupNode(groupId);
                return new ValueTask<GetOrCreateGroupMethodStateResult>(
                    new GetOrCreateGroupMethodStateResult
                    {
                        ServiceResult = ServiceResult.Good,
                        GroupNodeId = group.NodeId,
                        Created = true
                    });
            }
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
            BindAttributeMethods(group.Labels, () => group.Epoch);

            SetValue(group.GroupId, groupId);
            SetValue(group.Xid, groupId);
            SetValue(group.Epoch, 1u);
            SetValue(group.CreatedAt, DateTimeUtc.Now);
            SetValue(group.ModifiedAt, DateTimeUtc.Now);

            m_registry?.AddChild(group);
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

            lock (m_gate)
            {
                if (!m_groupsByNodeId.TryGetValue(groupNodeId, out GroupState? group))
                {
                    return Failed(StatusCodes.BadNodeIdUnknown);
                }

                string assigned = string.IsNullOrEmpty(versionId)
                    ? NextVersionId(resourceId)
                    : versionId;
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

                ResourceState resource = CreateResourceNode(group, resourceId, assigned);
                m_resources[key] = resource;
                Interlocked.Increment(ref m_registeredResourceCount);

                uint handle = requestFileOpen ? OpenWriteHandle(resource) : 0;
                return new ValueTask<(ServiceResult, ResourceState?, uint, string, bool)>(
                    (ServiceResult.Good, resource, handle, assigned, true));
            }

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
        private ResourceState CreateResourceNode(GroupState group, string resourceId, string versionId)
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
            BindAttributeMethods(resource.Labels, () => resource.Epoch);

            SetValue(resource.ResourceId, resourceId);
            SetValue(resource.VersionId, versionId);
            SetValue(resource.Epoch, 1u);
            SetValue(resource.CreatedAt, DateTimeUtc.Now);
            SetValue(resource.ModifiedAt, DateTimeUtc.Now);

            BindFileMethods(resource);
            if (resource.Delete != null)
            {
                resource.Delete.OnCallAsync = (ctx, m, id, epoch, ct) => IsWriteChannelSecure(ctx)
                    ? OnDeleteResourceAsync(resource, epoch)
                    : InsecureDelete();
            }

            group.AddChild(resource);
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
            lock (m_gate)
            {
                if (!IsEpochCurrent(resource.Epoch, expectedEpoch))
                {
                    return new DeleteMethodStateResult { ServiceResult = StatusCodes.BadInvalidState };
                }

                storeKey = StoreKeyOf(resource);
                if (!RemoveResourceLocked(resource))
                {
                    // A concurrent Delete already removed it; nothing left to do and nothing to
                    // release a second time.
                    return new DeleteMethodStateResult { ServiceResult = ServiceResult.Good };
                }
            }

            _ = await m_resourceStore.DeleteAsync(storeKey).ConfigureAwait(false);
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
            lock (m_gate)
            {
                if (!IsEpochCurrent(group.Epoch, expectedEpoch))
                {
                    return new DeleteMethodStateResult { ServiceResult = StatusCodes.BadInvalidState };
                }

                foreach (KeyValuePair<ResourceKey, ResourceState> entry in new List<KeyValuePair<ResourceKey, ResourceState>>(m_resources))
                {
                    if (entry.Key.GroupNodeId == group.NodeId)
                    {
                        storeKeys.Add(StoreKeyOf(entry.Value));
                        RemoveResourceLocked(entry.Value);
                    }
                }

                if (group.GroupId?.Value is string groupId)
                {
                    m_groups.Remove(groupId);
                }
                m_groupsByNodeId.Remove(group.NodeId);
                DeleteNode(SystemContext, group.NodeId);
            }

            foreach (string storeKey in storeKeys)
            {
                _ = await m_resourceStore.DeleteAsync(storeKey).ConfigureAwait(false);
            }
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
                    ? OnFileWriteAsync(resource, handle, data)
                    : new ValueTask<WriteMethodStateResult>(new WriteMethodStateResult
                    {
                        ServiceResult = StatusCodes.BadSecurityModeInsufficient
                    });
            }
            if (resource.Read != null)
            {
                resource.Read.OnCallAsync = (ctx, m, id, handle, length, ct) => IsReadChannelSecure(ctx)
                    ? OnFileReadAsync(resource, handle, length)
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
        /// <see cref="XRegistryServerOptions.RequireEncryptionForReads"/>.
        /// </summary>
        private ValueTask<OpenMethodStateResult> OnFileOpenAsync(
            ResourceState resource,
            byte mode,
            ISystemContext context)
        {
            lock (m_gate)
            {
                bool writing = (mode & kWriteMode) != 0;
                if (writing ? !IsWriteChannelSecure(context) : !IsReadChannelSecure(context))
                {
                    return new ValueTask<OpenMethodStateResult>(new OpenMethodStateResult
                    {
                        ServiceResult = StatusCodes.BadSecurityModeInsufficient
                    });
                }
                if (writing && m_fileHandles.Count >= m_maxConcurrentUploads)
                {
                    return new ValueTask<OpenMethodStateResult>(new OpenMethodStateResult
                    {
                        ServiceResult = StatusCodes.BadTooManyOperations
                    });
                }

                uint handle = writing
                    ? OpenWriteHandle(resource)
                    : OpenReadHandle(resource);
                return new ValueTask<OpenMethodStateResult>(new OpenMethodStateResult
                {
                    ServiceResult = ServiceResult.Good,
                    FileHandle = handle
                });
            }
        }

        private ValueTask<WriteMethodStateResult> OnFileWriteAsync(
            ResourceState resource,
            uint fileHandle,
            ByteString data)
        {
            lock (m_gate)
            {
                if (!TryGetHandle(resource, fileHandle, out ResourceFileHandle? entry) || !entry.Writing)
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
                    if (entry.Buffer.Count + data.Span.Length > m_maxResourceBytes)
                    {
                        return new ValueTask<WriteMethodStateResult>(new WriteMethodStateResult
                        {
                            ServiceResult = StatusCodes.BadRequestTooLarge
                        });
                    }
                    entry.Buffer.AddRange(data.Span.ToArray());
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
            int length)
        {
            ResourceFileHandle? entry;
            int position;
            lock (m_gate)
            {
                if (!TryGetHandle(resource, fileHandle, out entry) || entry.Writing)
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
                if (!TryGetHandle(resource, fileHandle, out entry))
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
                return new CloseMethodStateResult { ServiceResult = ServiceResult.Good };
            }

            if (m_contentIdProvider == null)
            {
                return new CloseMethodStateResult { ServiceResult = StatusCodes.BadNotSupported };
            }

            byte[] document = [.. entry.Buffer];
            string format = resource.Format?.Value ?? kDefaultFormat;
            ByteString contentId = m_contentIdProvider.ComputeContentId(format, document);

            // Replace the stored document wholesale. A plain write at offset 0 leaves any trailing
            // bytes of a larger previous version in place, which would corrupt the resource.
            _ = await m_resourceStore.DeleteAsync(entry.StoreKey).ConfigureAwait(false);
            await m_resourceStore.WriteAsync(entry.StoreKey, 0, document).ConfigureAwait(false);

            lock (m_gate)
            {
                // The resource can have been deleted while the store call was in flight. Its
                // fast-path reference is already released, so publishing here would strand a node
                // that nothing can ever release and re-create a document the delete removed.
                if (!IsRegisteredLocked(resource))
                {
                    return new CloseMethodStateResult { ServiceResult = StatusCodes.BadInvalidState };
                }

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
                if (resource.Epoch != null)
                {
                    resource.Epoch.Value++;
                }
            }

            return new CloseMethodStateResult { ServiceResult = ServiceResult.Good };
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

        private uint OpenWriteHandle(ResourceState resource)
        {
            uint handle = ++m_nextFileHandle;
            m_fileHandles[handle] = new ResourceFileHandle(
                StoreKeyOf(resource), resource.NodeId, writing: true);
            return handle;
        }

        private uint OpenReadHandle(ResourceState resource)
        {
            uint handle = ++m_nextFileHandle;
            m_fileHandles[handle] = new ResourceFileHandle(
                StoreKeyOf(resource), resource.NodeId, writing: false);
            return handle;
        }

        /// <summary>
        /// Resolves a file handle for a call made on <paramref name="resource"/>. The handle has to
        /// belong to that resource: handles are server-wide and sequential, so without this check a
        /// caller could drive another resource's document — or another caller's in-flight upload —
        /// through its own resource's Methods. The caller holds <see cref="m_gate"/>.
        /// </summary>
        /// <param name="resource">The resource whose Method was invoked.</param>
        /// <param name="fileHandle">The handle supplied by the caller.</param>
        /// <param name="entry">The resolved handle when it is valid for this resource.</param>
        private bool TryGetHandle(
            ResourceState resource,
            uint fileHandle,
            [NotNullWhen(true)] out ResourceFileHandle? entry)
        {
            if (!m_fileHandles.TryGetValue(fileHandle, out entry))
            {
                return false;
            }
            if (entry.ResourceNodeId != resource.NodeId)
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

        private string NextVersionId(string resourceId)
        {
            m_versionCounters.TryGetValue(resourceId, out uint current);
            current++;
            m_versionCounters[resourceId] = current;
            return current.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

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

            public bool Writing { get; } = writing;

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
        private void BindAttributeMethods(AttributesState? labels, Func<PropertyState<uint>?> epoch)
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
                        ? OnAddAttributeAsync(labels, epoch(), key, value, expectedEpoch)
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
                        ? OnRemoveAttributeAsync(labels, epoch(), key, expectedEpoch)
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
            uint expectedEpoch)
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
                return new ValueTask<AddAttributeMethodStateResult>(
                    new AddAttributeMethodStateResult { ServiceResult = ServiceResult.Good });
            }
        }

        /// <summary>
        /// Handles <c>AttributesType.RemoveAttribute(Key, ExpectedEpoch)</c>.
        /// </summary>
        internal ValueTask<RemoveAttributeMethodStateResult> OnRemoveAttributeAsync(
            AttributesState labels,
            PropertyState<uint>? epoch,
            string key,
            uint expectedEpoch)
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
                return new ValueTask<RemoveAttributeMethodStateResult>(
                    new RemoveAttributeMethodStateResult { ServiceResult = ServiceResult.Good });
            }
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
        private readonly Dictionary<string, uint> m_versionCounters = [];
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
        private RegistryState? m_registry;
        private uint m_nextInstanceId = XRegistryWellKnown.FirstDynamicInstance;
        private uint m_nextFileHandle;
        private int m_registeredResourceCount;
        private const byte kWriteMode = 2;
        private const string kDefaultFormat = "avro";
    }
}
