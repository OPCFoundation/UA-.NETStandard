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
        {
            SystemContext = systemContext ?? throw new ArgumentNullException(nameof(systemContext));
            NamespaceUris = namespaceUris ?? throw new ArgumentNullException(nameof(namespaceUris));
            ModelNamespaceIndex = modelNamespaceIndex;
            AddNodeAsync = addNodeAsync ?? throw new ArgumentNullException(nameof(addNodeAsync));
            DeleteNodeAsync = deleteNodeAsync ?? throw new ArgumentNullException(nameof(deleteNodeAsync));
            CheckManagementAccess = checkManagementAccess ??
                throw new ArgumentNullException(nameof(checkManagementAccess));
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
    }
}
