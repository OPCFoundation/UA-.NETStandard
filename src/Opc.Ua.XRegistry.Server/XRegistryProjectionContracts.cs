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
using Opc.Ua.XRegistry;

namespace Opc.Ua.XRegistry.Server
{
    /// <summary>
    /// Provides the immutable registry snapshot consumed by the shared browseable projection engine.
    /// </summary>
    public interface IXRegistryProjectionSnapshot
    {
        /// <summary>
        /// Gets the registry-level labels.
        /// </summary>
        ImmutableSortedDictionary<string, string> Labels { get; }

        /// <summary>
        /// Enumerates the groups to project.
        /// </summary>
        IEnumerable<IXRegistryProjectionGroup> Groups { get; }
    }

    /// <summary>
    /// Describes one xRegistry group in a browseable projection snapshot.
    /// </summary>
    public interface IXRegistryProjectionGroup
    {
        /// <summary>
        /// Gets the xRegistry group id.
        /// </summary>
        string GroupId { get; }

        /// <summary>
        /// Gets the group xid.
        /// </summary>
        string Xid { get; }

        /// <summary>
        /// Gets the display name.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Gets the description.
        /// </summary>
        string Description { get; }

        /// <summary>
        /// Gets the entity epoch.
        /// </summary>
        long Epoch { get; }

        /// <summary>
        /// Gets the group labels.
        /// </summary>
        ImmutableSortedDictionary<string, string> Labels { get; }

        /// <summary>
        /// Enumerates the resources owned by the group.
        /// </summary>
        IEnumerable<IXRegistryProjectionResource> Resources { get; }
    }

    /// <summary>
    /// Describes one xRegistry resource in a browseable projection snapshot.
    /// </summary>
    public interface IXRegistryProjectionResource
    {
        /// <summary>
        /// Gets the owning group id.
        /// </summary>
        string GroupId { get; }

        /// <summary>
        /// Gets the xRegistry resource id.
        /// </summary>
        string ResourceId { get; }

        /// <summary>
        /// Gets the resource xid.
        /// </summary>
        string Xid { get; }

        /// <summary>
        /// Gets the display name.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Gets the description.
        /// </summary>
        string Description { get; }

        /// <summary>
        /// Gets the projected version id returned from create calls.
        /// </summary>
        string VersionId { get; }

        /// <summary>
        /// Gets the resource format.
        /// </summary>
        string Format { get; }

        /// <summary>
        /// Gets the resource content type.
        /// </summary>
        string ContentType { get; }

        /// <summary>
        /// Gets the entity epoch.
        /// </summary>
        long Epoch { get; }

        /// <summary>
        /// Gets the resource creation time.
        /// </summary>
        DateTime CreatedAt { get; }

        /// <summary>
        /// Gets the resource modification time.
        /// </summary>
        DateTime ModifiedAt { get; }

        /// <summary>
        /// Gets the resource labels.
        /// </summary>
        ImmutableSortedDictionary<string, string> Labels { get; }
    }

    /// <summary>
    /// Owns the FileType behavior attached to a projected resource node.
    /// </summary>
    public interface IXRegistryProjectedResourceFile : IDisposable
    {
        /// <summary>
        /// Opens a write handle for a create call that requested one.
        /// </summary>
        ServiceResult TryOpenWriteHandle(ISystemContext context, out uint fileHandle);

        /// <summary>
        /// Applies new persisted-content metadata after reconciliation.
        /// </summary>
        void ApplyResource(IXRegistryProjectionResource resource);
    }

    /// <summary>
    /// Optional additive capability for projections whose <see cref="IXRegistryProjectedResourceFile"/>
    /// can service Open/Read/Write/Close/GetPosition/SetPosition calls arriving through a
    /// <em>different</em> FileState (the logical Resource's inherited FileType members) while
    /// sharing the same handle table and writer reservation with the version's own FileState.
    /// </summary>
    /// <remarks>
    /// The engine wires the logical Resource's FileType methods to closures that pin the
    /// resolved default Version at Open time and forward every subsequent call to the same
    /// file-manager instance that services the Version's own node, so a write reservation
    /// taken through either access path is naturally shared.
    /// </remarks>
    public interface IXRegistryProjectedResourceFileHandleForwarder
    {
        /// <summary>
        /// Opens a file handle (read or write) on behalf of a logical Resource node.
        /// </summary>
        ServiceResult ForwardOpen(
            ISystemContext context, MethodState method, NodeId objectId,
            byte mode, ref uint fileHandle);

        /// <summary>
        /// Closes a previously opened handle.
        /// </summary>
        ValueTask<ServiceResult> ForwardCloseAsync(
            ISystemContext context, MethodState method, NodeId objectId,
            uint fileHandle, CancellationToken cancellationToken);

        /// <summary>
        /// Reads from a previously opened handle.
        /// </summary>
        ValueTask<(ServiceResult Status, ByteString Data)> ForwardReadAsync(
            ISystemContext context, MethodState method, NodeId objectId,
            uint fileHandle, int length, CancellationToken cancellationToken);

        /// <summary>
        /// Writes to a previously opened handle.
        /// </summary>
        ServiceResult ForwardWrite(
            ISystemContext context, MethodState method, NodeId objectId,
            uint fileHandle, ByteString data);

        /// <summary>
        /// Gets the current position of a previously opened handle.
        /// </summary>
        ServiceResult ForwardGetPosition(
            ISystemContext context, MethodState method, NodeId objectId,
            uint fileHandle, ref ulong position);

        /// <summary>
        /// Sets the position of a previously opened handle.
        /// </summary>
        ServiceResult ForwardSetPosition(
            ISystemContext context, MethodState method, NodeId objectId,
            uint fileHandle, ulong position);
    }

    /// <summary>
    /// Optional additive capability for projections that can atomically claim
    /// an existing content-less resource for its first write.
    /// </summary>
    public interface IXRegistryProjectedContentlessResourceFile
    {
        /// <summary>
        /// Opens a write handle only when the projected resource still has no
        /// committed content. Returns <see cref="StatusCodes.BadInvalidState"/>
        /// when another writer has already claimed or filled it.
        /// </summary>
        ServiceResult TryOpenContentlessWriteHandle(
            ISystemContext context,
            out uint fileHandle);
    }

    /// <summary>
    /// Supplies the immutable entity metadata needed to derive xRegistry events during reconciliation.
    /// </summary>
    public interface IXRegistryProjectionEventMetadataProvider
    {
        /// <summary>
        /// Captures the current registry, group, resource and version metadata.
        /// </summary>
        XRegistryProjectionEventSnapshot CaptureEventSnapshot();
    }

    /// <summary>
    /// Captures the browseable projection and event metadata from one immutable generation.
    /// </summary>
    public interface IXRegistryProjectionGenerationProvider
    {
        /// <summary>
        /// Captures one generation-bound projection bundle.
        /// </summary>
        XRegistryProjectionGeneration CaptureProjectionGeneration();
    }

    /// <summary>
    /// One immutable projection generation.
    /// </summary>
    public sealed record XRegistryProjectionGeneration(
        IXRegistryProjectionSnapshot Projection,
        XRegistryProjectionEventSnapshot? Events);

    /// <summary>
    /// Supplies Resource Meta independently from the projected Version properties.
    /// </summary>
    public interface IXRegistryProjectionResourceMeta
    {
        /// <summary>Gets the logical Resource Meta epoch.</summary>
        long MetaEpoch { get; }

        /// <summary>Gets the logical Resource Meta labels.</summary>
        ImmutableSortedDictionary<string, string> MetaLabels { get; }

        /// <summary>Gets the logical Resource Meta creation time.</summary>
        DateTime MetaCreatedAt { get; }

        /// <summary>Gets the logical Resource Meta modification time.</summary>
        DateTime MetaModifiedAt { get; }

        /// <summary>Gets whether this Version is the committed default Version.</summary>
        bool IsDefaultVersion { get; }
    }

    /// <summary>
    /// Canonical snapshot of an optional xRegistry deprecated object.
    /// </summary>
    public sealed record XRegistryProjectionDeprecation(
        string CanonicalValue,
        ImmutableSortedDictionary<string, string> Details);

    /// <summary>
    /// Immutable event-relevant registry snapshot.
    /// </summary>
    public sealed record XRegistryProjectionEventSnapshot(
        string Xid,
        uint Epoch,
        ImmutableSortedDictionary<string, string> Labels,
        ImmutableArray<XRegistryProjectionEventGroup> Groups);

    /// <summary>
    /// Immutable event-relevant group snapshot.
    /// </summary>
    public sealed record XRegistryProjectionEventGroup(
        string GroupId,
        string Xid,
        uint Epoch,
        ImmutableSortedDictionary<string, string> Labels,
        bool Deprecated,
        ImmutableArray<XRegistryProjectionEventResource> Resources)
    {
        /// <summary>Gets the materialized group node used as the event source.</summary>
        public NodeId SourceNodeId { get; init; }

        /// <summary>Gets the source name retained for deleted events.</summary>
        public string? SourceName { get; init; }

        /// <summary>
        /// Gets the canonical deprecated object. When absent, <see cref="Deprecated"/> is used as
        /// the compatibility representation.
        /// </summary>
        public XRegistryProjectionDeprecation? Deprecation { get; init; }
    }

    /// <summary>
    /// Immutable event-relevant resource snapshot.
    /// </summary>
    public sealed record XRegistryProjectionEventResource(
        string GroupId,
        string ResourceId,
        string Xid,
        uint Epoch,
        uint MetaEpoch,
        ImmutableSortedDictionary<string, string> Labels,
        bool Deprecated,
        string? DefaultVersionId,
        ImmutableArray<XRegistryProjectionEventVersion> Versions)
    {
        /// <summary>Gets the default Version file used as the Resource event source.</summary>
        public NodeId SourceNodeId { get; init; }

        /// <summary>Gets the source name retained for deleted events.</summary>
        public string? SourceName { get; init; }

        /// <summary>
        /// Gets the current projected Resource name.
        /// </summary>
        public string? Name { get; init; }

        /// <summary>
        /// Gets the current projected Resource description.
        /// </summary>
        public string? Description { get; init; }

        /// <summary>Gets the Resource Meta creation time.</summary>
        public DateTime MetaCreatedAt { get; init; }

        /// <summary>Gets the Resource Meta modification time.</summary>
        public DateTime MetaModifiedAt { get; init; }

        /// <summary>
        /// Gets the canonical Resource Meta deprecated object. When absent,
        /// <see cref="Deprecated"/> is used as the compatibility representation.
        /// </summary>
        public XRegistryProjectionDeprecation? Deprecation { get; init; }
    }

    /// <summary>
    /// Immutable event-relevant version snapshot.
    /// </summary>
    public sealed record XRegistryProjectionEventVersion(
        string VersionId,
        string Xid,
        uint Epoch,
        ImmutableSortedDictionary<string, string> Attributes)
    {
        /// <summary>Gets the materialized Version file used as the event source.</summary>
        public NodeId SourceNodeId { get; init; }

        /// <summary>Gets the source name retained for deleted events.</summary>
        public string? SourceName { get; init; }

        /// <summary>Gets the Version labels.</summary>
        public ImmutableSortedDictionary<string, string> Labels { get; init; } =
            ImmutableSortedDictionary<string, string>.Empty;

        /// <summary>Gets the Version creation time.</summary>
        public DateTime CreatedAt { get; init; }

        /// <summary>Gets the Version modification time.</summary>
        public DateTime ModifiedAt { get; init; }
    }

    /// <summary>
    /// Supplies domain-specific node creation, mutation and metadata behavior to the shared engine.
    /// </summary>
    public interface IXRegistryProjectionStrategy
    {
        /// <summary>
        /// Gets the current immutable snapshot.
        /// </summary>
        IXRegistryProjectionSnapshot Current { get; }

        /// <summary>
        /// Creates the concrete group node for a group.
        /// </summary>
        GroupState CreateGroupNode(BaseObjectState registryNode, IXRegistryProjectionGroup group);

        /// <summary>
        /// Creates the concrete resource node for a resource.
        /// </summary>
        ResourceState CreateResourceNode(GroupState groupNode, IXRegistryProjectionResource resource);

        /// <summary>
        /// Called after the shared engine wires common group behavior.
        /// </summary>
        void ConfigureGroupNode(GroupState node, IXRegistryProjectionGroup group);

        /// <summary>
        /// Called after the shared engine wires common resource behavior.
        /// </summary>
        void ConfigureResourceNode(ResourceState node, IXRegistryProjectionResource resource);

        /// <summary>
        /// Creates optional FileType behavior for a resource.
        /// </summary>
        IXRegistryProjectedResourceFile? CreateResourceFile(
            ResourceState node,
            IXRegistryProjectionResource resource);

        /// <summary>
        /// Creates a group.
        /// </summary>
        ValueTask<IXRegistryProjectionGroup?> CreateGroupAsync(string groupId, CancellationToken ct);

        /// <summary>
        /// Gets or creates a group.
        /// </summary>
        ValueTask<(IXRegistryProjectionGroup Group, bool Created)> GetOrCreateGroupAsync(
            string groupId,
            CancellationToken ct);

        /// <summary>
        /// Creates a resource in a group.
        /// </summary>
        ValueTask<IXRegistryProjectionResource?> CreateResourceAsync(
            string groupId,
            string resourceId,
            CancellationToken ct);

        /// <summary>
        /// Gets or creates a resource in a group.
        /// </summary>
        ValueTask<(IXRegistryProjectionResource Resource, bool Created)> GetOrCreateResourceAsync(
            string groupId,
            string resourceId,
            CancellationToken ct);

        /// <summary>
        /// Deletes a group.
        /// </summary>
        ValueTask<ServiceResult> DeleteGroupAsync(
            string groupId,
            long? epoch,
            CancellationToken ct);

        /// <summary>
        /// Deletes a resource.
        /// </summary>
        ValueTask<ServiceResult> DeleteResourceAsync(
            string groupId,
            string resourceId,
            long? epoch,
            CancellationToken ct);

        /// <summary>
        /// Adds or replaces a registry label.
        /// </summary>
        ValueTask<ServiceResult> AddRegistryLabelAsync(
            string key,
            string value,
            long? epoch,
            CancellationToken ct);

        /// <summary>
        /// Removes a registry label.
        /// </summary>
        ValueTask<ServiceResult> RemoveRegistryLabelAsync(
            string key,
            long? epoch,
            CancellationToken ct);

        /// <summary>
        /// Adds or replaces a group label.
        /// </summary>
        ValueTask<ServiceResult> AddGroupLabelAsync(
            string groupId,
            string key,
            string value,
            long? epoch,
            CancellationToken ct);

        /// <summary>
        /// Removes a group label.
        /// </summary>
        ValueTask<ServiceResult> RemoveGroupLabelAsync(
            string groupId,
            string key,
            long? epoch,
            CancellationToken ct);

        /// <summary>
        /// Adds or replaces a resource label.
        /// </summary>
        ValueTask<ServiceResult> AddResourceLabelAsync(
            string groupId,
            string resourceId,
            string key,
            string value,
            long? epoch,
            CancellationToken ct);

        /// <summary>
        /// Removes a resource label.
        /// </summary>
        ValueTask<ServiceResult> RemoveResourceLabelAsync(
            string groupId,
            string resourceId,
            string key,
            long? epoch,
            CancellationToken ct);
    }

    /// <summary>
    /// Additive strategy contract for projections that materialize one file per Version.
    /// </summary>
    public interface IXRegistryVersionedProjectionStrategy : IXRegistryProjectionStrategy
    {
        /// <summary>Creates an explicit or server-assigned Version.</summary>
        ValueTask<IXRegistryProjectionResource?> CreateResourceAsync(
            string groupId,
            string resourceId,
            string versionId,
            CancellationToken ct);

        /// <summary>Gets or creates an explicit or server-assigned Version.</summary>
        ValueTask<(IXRegistryProjectionResource Resource, bool Created)> GetOrCreateResourceAsync(
            string groupId,
            string resourceId,
            string versionId,
            CancellationToken ct);

        /// <summary>
        /// Atomically verifies the caller-observed Resource or Version role and
        /// deletes that entity using the corresponding epoch space.
        /// </summary>
        ValueTask<ServiceResult> DeleteProjectedEntityAsync(
            string groupId,
            string resourceId,
            string versionId,
            bool deleteLogicalResource,
            long? epoch,
            CancellationToken ct);

        /// <summary>Adds or replaces a Version label.</summary>
        ValueTask<ServiceResult> AddVersionLabelAsync(
            string groupId,
            string resourceId,
            string versionId,
            string key,
            string value,
            long? epoch,
            CancellationToken ct);

        /// <summary>Removes a Version label.</summary>
        ValueTask<ServiceResult> RemoveVersionLabelAsync(
            string groupId,
            string resourceId,
            string versionId,
            string key,
            long? epoch,
            CancellationToken ct);

        /// <summary>Adds or replaces a Resource Meta label.</summary>
        ValueTask<ServiceResult> AddResourceMetaLabelAsync(
            string groupId,
            string resourceId,
            string key,
            string value,
            long? epoch,
            CancellationToken ct);

        /// <summary>Removes a Resource Meta label.</summary>
        ValueTask<ServiceResult> RemoveResourceMetaLabelAsync(
            string groupId,
            string resourceId,
            string key,
            long? epoch,
            CancellationToken ct);
    }

    /// <summary>
    /// Carries the server seams required by <see cref="XRegistryProjectionEngine"/>.
    /// </summary>
    public sealed class XRegistryProjectionContext
    {
        /// <summary>
        /// Initializes a new projection context.
        /// </summary>
        public XRegistryProjectionContext(
            ISystemContext systemContext,
            NamespaceTable namespaceUris,
            ushort modelNamespaceIndex,
            Func<NodeState, CancellationToken, ValueTask> addNodeAsync,
            Func<NodeId, CancellationToken, ValueTask> deleteNodeAsync,
            Func<ISystemContext, string, ServiceResult> checkManagementAccess)
            : this(
                systemContext,
                namespaceUris,
                modelNamespaceIndex,
                addNodeAsync,
                deleteNodeAsync,
                checkManagementAccess,
                null)
        {
        }

        /// <summary>
        /// Initializes a new projection context with optional xRegistry event configuration.
        /// </summary>
        public XRegistryProjectionContext(
            ISystemContext systemContext,
            NamespaceTable namespaceUris,
            ushort modelNamespaceIndex,
            Func<NodeState, CancellationToken, ValueTask> addNodeAsync,
            Func<NodeId, CancellationToken, ValueTask> deleteNodeAsync,
            Func<ISystemContext, string, ServiceResult> checkManagementAccess,
            XRegistryServerOptions? eventOptions)
        {
            SystemContext = systemContext ?? throw new ArgumentNullException(nameof(systemContext));
            NamespaceUris = namespaceUris ?? throw new ArgumentNullException(nameof(namespaceUris));
            ModelNamespaceIndex = modelNamespaceIndex;
            AddNodeAsync = addNodeAsync ?? throw new ArgumentNullException(nameof(addNodeAsync));
            DeleteNodeAsync = deleteNodeAsync ?? throw new ArgumentNullException(nameof(deleteNodeAsync));
            CheckManagementAccess = checkManagementAccess ??
                throw new ArgumentNullException(nameof(checkManagementAccess));
            EventOptions = eventOptions;
            EventOptions?.Validate();
        }

        /// <summary>
        /// Gets the system context.
        /// </summary>
        public ISystemContext SystemContext { get; }

        /// <summary>
        /// Gets the server namespace table.
        /// </summary>
        public NamespaceTable NamespaceUris { get; }

        /// <summary>
        /// Gets the namespace index used for deterministic instance NodeIds.
        /// </summary>
        public ushort ModelNamespaceIndex { get; }

        /// <summary>
        /// Gets the callback that registers a new node.
        /// </summary>
        public Func<NodeState, CancellationToken, ValueTask> AddNodeAsync { get; }

        /// <summary>
        /// Gets the callback that deletes a node.
        /// </summary>
        public Func<NodeId, CancellationToken, ValueTask> DeleteNodeAsync { get; }

        /// <summary>
        /// Gets the access check callback for management methods.
        /// </summary>
        public Func<ISystemContext, string, ServiceResult> CheckManagementAccess { get; }

        /// <summary>
        /// Gets optional generic xRegistry event configuration.
        /// </summary>
        public XRegistryServerOptions? EventOptions { get; }
    }
}
