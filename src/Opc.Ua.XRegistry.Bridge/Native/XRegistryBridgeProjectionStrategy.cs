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

using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.XRegistry.Server;

namespace Opc.Ua.XRegistry.Bridge.Native
{
    /// <summary>
    /// Reuses the existing versioned layout. Context-free legacy strategy mutations
    /// are deliberately unavailable; generated method callbacks use the caller-aware endpoint path.
    /// </summary>
    internal sealed class XRegistryBridgeProjectionStrategy(
        XRegistryBridgeNodeManager manager,
        XRegistryNativeSnapshot snapshot) : IXRegistryVersionedProjectionStrategy
    {
        public XRegistryNativeSnapshot Snapshot { get; set; } = snapshot;

        public IXRegistryProjectionSnapshot Current => Snapshot;

        public GroupState CreateGroupNode(BaseObjectState registryNode, IXRegistryProjectionGroup group)
        {
            return manager.SystemContext.CreateInstanceOfGroupType(registryNode,
                new QualifiedName(group.GroupId, manager.InstanceNamespaceIndex));
        }

        public ResourceState CreateResourceNode(GroupState groupNode, IXRegistryProjectionResource resource)
        {
            return manager.SystemContext.CreateInstanceOfResourceType(groupNode,
                new QualifiedName(resource.ResourceId, manager.InstanceNamespaceIndex));
        }

        public void ConfigureGroupNode(GroupState node, IXRegistryProjectionGroup group)
        {
            manager.ConfigureGroup(node, (XRegistryNativeGroup)group);
        }

        public void ConfigureResourceNode(ResourceState node, IXRegistryProjectionResource resource)
        {
            manager.ConfigureResource(node, (XRegistryNativeResource)resource);
        }

        public IXRegistryProjectedResourceFile CreateResourceFile(
            ResourceState node, IXRegistryProjectionResource resource)
        {
            return manager.BindResourceFile(node, (XRegistryNativeResource)resource, false);
        }

        public ValueTask<IXRegistryProjectionGroup?> CreateGroupAsync(string groupId, CancellationToken ct)
        {
            throw CallerRequired();
        }

        public ValueTask<(IXRegistryProjectionGroup Group, bool Created)> GetOrCreateGroupAsync(
            string groupId, CancellationToken ct)
        {
            throw CallerRequired();
        }

        public ValueTask<IXRegistryProjectionResource?> CreateResourceAsync(
            string groupId, string resourceId, CancellationToken ct)
        {
            throw CallerRequired();
        }

        public ValueTask<(IXRegistryProjectionResource Resource, bool Created)> GetOrCreateResourceAsync(
            string groupId, string resourceId, CancellationToken ct)
        {
            throw CallerRequired();
        }

        public ValueTask<IXRegistryProjectionResource?> CreateResourceAsync(
            string groupId, string resourceId, string versionId, CancellationToken ct)
        {
            throw CallerRequired();
        }

        public ValueTask<(IXRegistryProjectionResource Resource, bool Created)> GetOrCreateResourceAsync(
            string groupId, string resourceId, string versionId, CancellationToken ct)
        {
            throw CallerRequired();
        }

        public ValueTask<ServiceResult> DeleteGroupAsync(string groupId, long? epoch, CancellationToken ct)
        {
            throw CallerRequired();
        }

        public ValueTask<ServiceResult> DeleteResourceAsync(
            string groupId, string resourceId, long? epoch, CancellationToken ct)
        {
            throw CallerRequired();
        }

        public ValueTask<ServiceResult> DeleteProjectedEntityAsync(
            string groupId, string resourceId, string versionId, bool deleteLogicalResource,
            long? epoch, CancellationToken ct)
        {
            throw CallerRequired();
        }

        public ValueTask<ServiceResult> AddRegistryLabelAsync(
            string key, string value, long? epoch, CancellationToken ct)
        {
            throw CallerRequired();
        }

        public ValueTask<ServiceResult> RemoveRegistryLabelAsync(string key, long? epoch, CancellationToken ct)
        {
            throw CallerRequired();
        }

        public ValueTask<ServiceResult> AddGroupLabelAsync(
            string groupId, string key, string value, long? epoch, CancellationToken ct)
        {
            throw CallerRequired();
        }

        public ValueTask<ServiceResult> RemoveGroupLabelAsync(
            string groupId, string key, long? epoch, CancellationToken ct)
        {
            throw CallerRequired();
        }

        public ValueTask<ServiceResult> AddResourceLabelAsync(
            string groupId, string resourceId, string key, string value, long? epoch, CancellationToken ct)
        {
            throw CallerRequired();
        }

        public ValueTask<ServiceResult> RemoveResourceLabelAsync(
            string groupId, string resourceId, string key, long? epoch, CancellationToken ct)
        {
            throw CallerRequired();
        }

        public ValueTask<ServiceResult> AddVersionLabelAsync(
            string groupId, string resourceId, string versionId, string key, string value,
            long? epoch, CancellationToken ct)
        {
            throw CallerRequired();
        }

        public ValueTask<ServiceResult> RemoveVersionLabelAsync(
            string groupId, string resourceId, string versionId, string key, long? epoch, CancellationToken ct)
        {
            throw CallerRequired();
        }

        public ValueTask<ServiceResult> AddResourceMetaLabelAsync(
            string groupId, string resourceId, string key, string value, long? epoch, CancellationToken ct)
        {
            throw CallerRequired();
        }

        public ValueTask<ServiceResult> RemoveResourceMetaLabelAsync(
            string groupId, string resourceId, string key, long? epoch, CancellationToken ct)
        {
            throw CallerRequired();
        }

        private static ServiceResultException CallerRequired()
        {
            return new ServiceResultException(StatusCodes.BadNotSupported,
                "Use the authenticated native method callbacks; context-free provider mutations are disabled.");
        }
    }
}
