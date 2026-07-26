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
        /// Materializes the registry root from the compiled model and the legacy registration
        /// resource group.
        /// </summary>
        /// <param name="externalReferences">External reference sink (unused).</param>
        public override void CreateAddressSpace(
            IDictionary<NodeId, IList<IReference>> externalReferences)
        {
            base.CreateAddressSpace(externalReferences);

            ushort ns = (ushort)Server.NamespaceUris.GetIndex(m_namespaceUri);

            CreateRegistryRoot(ns);

            var group = new BaseObjectState(null)
            {
                NodeId = new NodeId(XRegistryWellKnown.ResourceGroupObject, ns),
                BrowseName = new QualifiedName("ResourceGroup", ns),
                DisplayName = new LocalizedText("ResourceGroup"),
                TypeDefinitionId = Opc.Ua.ObjectTypeIds.BaseObjectType
            };

            AddMethod(group, XRegistryWellKnown.CreateResourceMethod, ns, "CreateResource", OnCreateResource);
            AddMethod(group, XRegistryWellKnown.WriteMethod, ns, "Write", OnWrite);
            AddMethod(group, XRegistryWellKnown.CloseMethod, ns, "Close", OnClose);
            AddMethod(group, XRegistryWellKnown.DeleteMethod, ns, "Delete", OnDelete);

            AddPredefinedNode(SystemContext, group);
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
                if (requestFileOpen && m_fileHandles.Count >= m_maxConcurrentUploads)
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

            SetValue(resource.ResourceId, resourceId);
            SetValue(resource.VersionId, versionId);
            SetValue(resource.Epoch, 1u);
            SetValue(resource.CreatedAt, DateTimeUtc.Now);
            SetValue(resource.ModifiedAt, DateTimeUtc.Now);

            BindFileMethods(resource);

            group.AddChild(resource);
            AddPredefinedNode(SystemContext, resource);
            return resource;
        }

        /// <summary>
        /// Binds the inherited <c>FileType</c> Methods so a document is transferred with the
        /// standard file operations rather than a registry-specific mechanism.
        /// </summary>
        private void BindFileMethods(ResourceState resource)
        {
            if (resource.Open != null)
            {
                resource.Open.OnCallAsync = (ctx, m, id, mode, ct) => OnFileOpenAsync(resource, mode);
            }
            if (resource.Write != null)
            {
                resource.Write.OnCallAsync = (ctx, m, id, handle, data, ct) => OnFileWriteAsync(handle, data);
            }
            if (resource.Read != null)
            {
                resource.Read.OnCallAsync = (ctx, m, id, handle, length, ct) => OnFileReadAsync(handle, length);
            }
            if (resource.Close != null)
            {
                resource.Close.OnCallAsync = (ctx, m, id, handle, ct) => OnFileCloseAsync(resource, handle);
            }
        }

        private ValueTask<OpenMethodStateResult> OnFileOpenAsync(ResourceState resource, byte mode)
        {
            lock (m_gate)
            {
                bool writing = (mode & kWriteMode) != 0;
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

        private ValueTask<WriteMethodStateResult> OnFileWriteAsync(uint fileHandle, ByteString data)
        {
            lock (m_gate)
            {
                if (!m_fileHandles.TryGetValue(fileHandle, out ResourceFileHandle? entry) || !entry.Writing)
                {
                    return new ValueTask<WriteMethodStateResult>(new WriteMethodStateResult
                    {
                        ServiceResult = StatusCodes.BadInvalidState
                    });
                }
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

        private async ValueTask<ReadMethodStateResult> OnFileReadAsync(uint fileHandle, int length)
        {
            ResourceFileHandle? entry;
            lock (m_gate)
            {
                if (!m_fileHandles.TryGetValue(fileHandle, out entry) || entry.Writing)
                {
                    return new ReadMethodStateResult { ServiceResult = StatusCodes.BadInvalidState };
                }
            }

            if (entry.Content.IsNull)
            {
                entry.Content = await m_resourceStore.ReadAsync(entry.StoreKey).ConfigureAwait(false);
            }

            lock (m_gate)
            {
                ReadOnlySpan<byte> all = entry.Content.IsNull ? default : entry.Content.Span;
                int remaining = all.Length - entry.Position;
                if (remaining <= 0 || length <= 0)
                {
                    return new ReadMethodStateResult
                    {
                        ServiceResult = ServiceResult.Good,
                        Data = ByteString.From([])
                    };
                }

                int take = Math.Min(length, remaining);
                byte[] chunk = all.Slice(entry.Position, take).ToArray();
                entry.Position += take;
                return new ReadMethodStateResult
                {
                    ServiceResult = ServiceResult.Good,
                    Data = ByteString.From(chunk)
                };
            }
        }

        /// <summary>
        /// Closes a file handle. Closing a write handle finalizes the upload: the content-derived
        /// id is computed from the accumulated document (§6.6), the document is committed to the
        /// resource store, and the Opaque content-id fast-path node is published (§10.1).
        /// </summary>
        private async ValueTask<CloseMethodStateResult> OnFileCloseAsync(
            ResourceState resource,
            uint fileHandle)
        {
            ResourceFileHandle? entry;
            lock (m_gate)
            {
                if (!m_fileHandles.TryGetValue(fileHandle, out entry))
                {
                    return new CloseMethodStateResult { ServiceResult = StatusCodes.BadInvalidState };
                }
                m_fileHandles.Remove(fileHandle);
            }

            if (!entry.Writing)
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

            await m_resourceStore.WriteAsync(entry.StoreKey, document).ConfigureAwait(false);

            lock (m_gate)
            {
                SetValue(resource.Xid, contentId.ToHexString());
                SetValue(resource.Format, format);
                SetValue(resource.ModifiedAt, DateTimeUtc.Now);
                if (resource.Epoch != null)
                {
                    resource.Epoch.Value++;
                }
                PublishFastPathNode(contentId, document);
            }

            return new CloseMethodStateResult { ServiceResult = ServiceResult.Good };
        }

        /// <summary>
        /// Publishes the Opaque content-id node so a decoder that received the id on the wire
        /// reaches the document in a single Read. The caller holds <see cref="m_gate"/>.
        /// </summary>
        private void PublishFastPathNode(ByteString contentId, byte[] document)
        {
            ushort ns = (ushort)Server.NamespaceUris.GetIndex(m_namespaceUri);
            var fastPathNodeId = new NodeId(contentId, ns);
            if (Find(fastPathNodeId) != null)
            {
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

        private uint OpenWriteHandle(ResourceState resource)
        {
            uint handle = ++m_nextFileHandle;
            m_fileHandles[handle] = new ResourceFileHandle(StoreKeyOf(resource), true);
            return handle;
        }

        private uint OpenReadHandle(ResourceState resource)
        {
            uint handle = ++m_nextFileHandle;
            m_fileHandles[handle] = new ResourceFileHandle(StoreKeyOf(resource), false);
            return handle;
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
        private sealed class ResourceFileHandle(string storeKey, bool writing)
        {
            public string StoreKey { get; } = storeKey;
            public bool Writing { get; } = writing;
            public List<byte> Buffer { get; } = [];
            public ByteString Content { get; set; }
            public int Position { get; set; }
        }

        private static void SetValue<T>(PropertyState<T>? property, T value)
        {
            if (property != null)
            {
                property.Value = value;
            }
        }

        /// <summary>
        /// Handles <c>CreateResource(ResourceId: String, VersionId: String)</c> and returns
        /// <c>(FileHandle: UInt32, VersionId: String)</c>.
        /// </summary>
        internal ServiceResult OnCreateResource(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            ArrayOf<Variant> inputs,
            List<Variant> outputs)
        {
            _ = inputs[0].TryGetValue(out string? _); // ResourceId (unused by the base lifecycle)
            _ = inputs[1].TryGetValue(out string? versionId);
            if (string.IsNullOrEmpty(versionId))
            {
                versionId = "1";
            }

            uint handle;
            lock (m_gate)
            {
                if (m_buffers.Count >= m_maxConcurrentUploads)
                {
                    return StatusCodes.BadTooManyOperations;
                }
                handle = ++m_nextHandle;
                m_buffers[handle] = [];
                m_versions[handle] = versionId;
            }

            outputs.Add(new Variant(handle));
            outputs.Add(new Variant(versionId));
            return ServiceResult.Good;
        }

        /// <summary>
        /// Handles <c>Write(FileHandle: UInt32, Data: ByteString)</c>, appending the chunk to the
        /// buffer held by the upload handle.
        /// </summary>
        internal ServiceResult OnWrite(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            ArrayOf<Variant> inputs,
            List<Variant> outputs)
        {
            if (!inputs[0].TryGetValue(out uint handle))
            {
                return StatusCodes.BadInvalidArgument;
            }

            _ = inputs[1].TryGetValue(out ByteString data);
            lock (m_gate)
            {
                if (!m_buffers.TryGetValue(handle, out List<byte>? buffer))
                {
                    return StatusCodes.BadNotFound;
                }
                if (!data.IsNull && data.Span.Length > 0)
                {
                    if (buffer.Count + data.Span.Length > m_maxResourceBytes)
                    {
                        return StatusCodes.BadRequestTooLarge;
                    }
                    buffer.AddRange(data.Span.ToArray());
                }
            }

            return ServiceResult.Good;
        }

        /// <summary>
        /// Handles <c>Close(FileHandle: UInt32, Format: String)</c> and returns
        /// <c>(ContentId: ByteString, Algorithm: String)</c>.
        /// </summary>
        internal ServiceResult OnClose(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            ArrayOf<Variant> inputs,
            List<Variant> outputs)
        {
            if (!inputs[0].TryGetValue(out uint handle))
            {
                return StatusCodes.BadInvalidArgument;
            }

            if (!inputs[1].TryGetValue(out string? format) || string.IsNullOrEmpty(format))
            {
                format = "avro";
            }

            if (m_contentIdProvider is null)
            {
                return StatusCodes.BadNotSupported;
            }

            byte[] document;
            lock (m_gate)
            {
                if (!m_buffers.TryGetValue(handle, out List<byte>? buffer))
                {
                    return StatusCodes.BadNotFound;
                }
                document = [.. buffer];
                m_buffers.Remove(handle);
                m_versions.Remove(handle);
            }

            // Auto-bootstrap (§10.1 + §6.6): compute the content-id + algorithm from the document.
            ByteString contentId = m_contentIdProvider.ComputeContentId(format, document);
            string algorithm = m_contentIdProvider.GetAlgorithm(format) ?? string.Empty;

            // Make the document reachable by its Opaque content-id NodeId (§6.4), created at runtime.
            ushort ns = (ushort)Server.NamespaceUris.GetIndex(m_namespaceUri);
            var fastPathNodeId = new NodeId(contentId, ns);

            if (Find(fastPathNodeId) is null)
            {
                if (Volatile.Read(ref m_registeredResourceCount) >= m_maxRegisteredResources)
                {
                    return StatusCodes.BadTooManyOperations;
                }

                var node = new BaseDataVariableState(null)
                {
                    NodeId = fastPathNodeId,
                    BrowseName = new QualifiedName("RegisteredResource", ns),
                    DisplayName = new LocalizedText("RegisteredResource"),
                    TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
                    ReferenceTypeId = ReferenceTypeIds.HasComponent,
                    DataType = Opc.Ua.DataTypeIds.ByteString,
                    ValueRank = ValueRanks.Scalar,
                    AccessLevel = AccessLevels.CurrentRead,
                    UserAccessLevel = AccessLevels.CurrentRead,
                    Historizing = false,
                    Value = new Variant(ByteString.From(document))
                };

                AddPredefinedNode(SystemContext, node);
                Interlocked.Increment(ref m_registeredResourceCount);
            }

            outputs.Add(new Variant(contentId));
            outputs.Add(new Variant(algorithm));
            return ServiceResult.Good;
        }

        /// <summary>
        /// Handles <c>Delete(ContentId: ByteString)</c>. The epoch-match arguments are optional per
        /// the specification (§5.2) and are not required by the base lifecycle.
        /// </summary>
        internal ServiceResult OnDelete(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            ArrayOf<Variant> inputs,
            List<Variant> outputs)
        {
            if (!inputs[0].TryGetValue(out ByteString contentId))
            {
                return StatusCodes.BadInvalidArgument;
            }

            ushort ns = (ushort)Server.NamespaceUris.GetIndex(m_namespaceUri);

            bool removed = DeleteNode(SystemContext, new NodeId(contentId, ns));
            if (removed)
            {
                Interlocked.Decrement(ref m_registeredResourceCount);
            }
            return removed ? ServiceResult.Good : StatusCodes.BadNotFound;
        }

        private static void AddMethod(
            BaseObjectState parent,
            uint id,
            ushort ns,
            string name,
            GenericMethodCalledEventHandler2 handler)
        {
            var method = new MethodState(parent)
            {
                NodeId = new NodeId(id, ns),
                BrowseName = new QualifiedName(name, ns),
                DisplayName = new LocalizedText(name),
                ReferenceTypeId = ReferenceTypeIds.HasComponent,
                Executable = true,
                UserExecutable = true,
                OnCallMethod2 = handler
            };

            parent.AddChild(method);
        }

        private readonly Lock m_gate = new();
        private readonly Dictionary<uint, List<byte>> m_buffers = [];
        private readonly Dictionary<uint, string> m_versions = [];
        private readonly Dictionary<string, GroupState> m_groups = [];
        private readonly Dictionary<NodeId, GroupState> m_groupsByNodeId = [];
        private readonly Dictionary<ResourceKey, ResourceState> m_resources = [];
        private readonly Dictionary<uint, ResourceFileHandle> m_fileHandles = [];
        private readonly Dictionary<string, uint> m_versionCounters = [];
        private readonly string m_namespaceUri;
        private readonly string m_registryBrowseName;
        private readonly string m_registryId;
        private readonly string m_specVersion;
        private readonly IResourceContentIdProvider? m_contentIdProvider;
        private readonly IXRegistryResourceStore m_resourceStore;
        private readonly int m_maxConcurrentUploads;
        private readonly int m_maxResourceBytes;
        private readonly int m_maxRegisteredResources;
        private RegistryState? m_registry;
        private uint m_nextInstanceId = XRegistryWellKnown.FirstDynamicInstance;
        private uint m_nextHandle;
        private uint m_nextFileHandle;
        private int m_registeredResourceCount;
        private const byte kWriteMode = 2;
        private const string kDefaultFormat = "avro";
    }
}
