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
using Opc.Ua.Server;
using Opc.Ua.XRegistry.Server;

namespace Opc.Ua.XRegistry.Tests
{
    /// <summary>
    /// Hosts the real <see cref="XRegistryProjectionEngine"/> in a live server so that the logical
    /// Resource shape (Group, Resource, Versions, FileType methods) is browseable over a real session.
    /// </summary>
    internal sealed class XRegistryLogicalProjectionNodeManager : AsyncCustomNodeManager
    {
        public XRegistryLogicalProjectionNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            string groupId,
            string resourceId)
            : base(
                server,
                configuration,
                server.Telemetry.CreateLogger<XRegistryLogicalProjectionNodeManager>(),
                XRegistryWellKnown.XRegistryNamespaceUri)
        {
            m_groupId = groupId;
            m_resourceId = resourceId;
        }

        /// <summary>
        /// Gets the projected logical Resource node once the address space is created.
        /// </summary>
        public ResourceState? LogicalResource { get; private set; }

        protected override ValueTask<NodeStateCollection> LoadPredefinedNodesAsync(
            ISystemContext context,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<NodeStateCollection>(
                new NodeStateCollection().AddOpcUaXRegistry(context));
        }

        public override async ValueTask CreateAddressSpaceAsync(
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken = default)
        {
            await base.CreateAddressSpaceAsync(externalReferences, cancellationToken).ConfigureAwait(false);

            ushort ns = (ushort)Server.NamespaceUris.GetIndex(XRegistryWellKnown.XRegistryNamespaceUri);
            var registry = new RegistryState(null)
            {
                NodeId = new NodeId(XRegistryWellKnown.RegistryObject, ns),
                BrowseName = new QualifiedName("TestRegistry", ns),
                DisplayName = new LocalizedText("TestRegistry"),
                TypeDefinitionId = new NodeId(ObjectTypes.RegistryType, ns)
            };
            registry.AddXid(SystemContext)
                .AddCreateGroup(SystemContext)
                .AddGetOrCreateGroup(SystemContext)
                .AddLabels(SystemContext);
            registry.Xid!.Value = "/";

            var context = new XRegistryProjectionContext(
                SystemContext,
                Server.NamespaceUris,
                ns,
                async (node, ct) =>
                {
                    await AddPredefinedNodeAsync(SystemContext, node, ct).ConfigureAwait(false);
                    if (node is ResourceState resource && resource.Versions is not null)
                    {
                        LogicalResource = resource;
                    }
                },
                async (nodeId, ct) =>
                {
                    await DeleteNodeAsync(SystemContext, nodeId, ct).ConfigureAwait(false);
                },
                (ctx, operation) => ServiceResult.Good);
            m_engine = new XRegistryProjectionEngine(
                context,
                new Strategy(ns, m_groupId, m_resourceId),
                "TestRegistry");

            await AddPredefinedNodeAsync(SystemContext, registry, cancellationToken).ConfigureAwait(false);
            await m_engine.AttachAsync(registry, cancellationToken).ConfigureAwait(false);
        }

        public override async ValueTask DeleteAddressSpaceAsync(CancellationToken cancellationToken = default)
        {
            m_engine?.Dispose();
            m_engine = null;
            await base.DeleteAddressSpaceAsync(cancellationToken).ConfigureAwait(false);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                m_engine?.Dispose();
                m_engine = null;
            }
            base.Dispose(disposing);
        }

        private XRegistryProjectionEngine? m_engine;
        private readonly string m_groupId;
        private readonly string m_resourceId;

        private sealed class Strategy : IXRegistryVersionedProjectionStrategy
        {
            public Strategy(ushort ns, string groupId, string resourceId)
            {
                m_ns = ns;
                Current = new Snapshot(groupId, resourceId);
            }

            public IXRegistryProjectionSnapshot Current { get; }

            public GroupState CreateGroupNode(BaseObjectState registryNode, IXRegistryProjectionGroup group)
            {
                return new GroupState(registryNode)
                {
                    TypeDefinitionId = new NodeId(ObjectTypes.GroupType, m_ns)
                };
            }

            public ResourceState CreateResourceNode(GroupState groupNode, IXRegistryProjectionResource resource)
            {
                return new ResourceState(groupNode)
                {
                    TypeDefinitionId = new NodeId(ObjectTypes.ResourceType, m_ns)
                };
            }

            public void ConfigureGroupNode(GroupState node, IXRegistryProjectionGroup group)
            {
            }

            public void ConfigureResourceNode(ResourceState node, IXRegistryProjectionResource resource)
            {
            }

            public IXRegistryProjectedResourceFile? CreateResourceFile(
                ResourceState node,
                IXRegistryProjectionResource resource)
            {
                return null;
            }

            public ValueTask<IXRegistryProjectionGroup?> CreateGroupAsync(string groupId, CancellationToken ct)
            {
                return new ValueTask<IXRegistryProjectionGroup?>((IXRegistryProjectionGroup?)null);
            }

            public ValueTask<(IXRegistryProjectionGroup Group, bool Created)> GetOrCreateGroupAsync(
                string groupId,
                CancellationToken ct)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported);
            }

            public ValueTask<IXRegistryProjectionResource?> CreateResourceAsync(
                string groupId,
                string resourceId,
                CancellationToken ct)
            {
                return new ValueTask<IXRegistryProjectionResource?>((IXRegistryProjectionResource?)null);
            }

            public ValueTask<(IXRegistryProjectionResource Resource, bool Created)> GetOrCreateResourceAsync(
                string groupId,
                string resourceId,
                CancellationToken ct)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported);
            }

            public ValueTask<IXRegistryProjectionResource?> CreateResourceAsync(
                string groupId,
                string resourceId,
                string versionId,
                CancellationToken ct)
            {
                return new ValueTask<IXRegistryProjectionResource?>((IXRegistryProjectionResource?)null);
            }

            public ValueTask<(IXRegistryProjectionResource Resource, bool Created)> GetOrCreateResourceAsync(
                string groupId,
                string resourceId,
                string versionId,
                CancellationToken ct)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported);
            }

            public ValueTask<ServiceResult> DeleteGroupAsync(string groupId, long? epoch, CancellationToken ct)
            {
                return NotSupported();
            }

            public ValueTask<ServiceResult> DeleteResourceAsync(
                string groupId,
                string resourceId,
                long? epoch,
                CancellationToken ct)
            {
                return NotSupported();
            }

            public ValueTask<ServiceResult> DeleteProjectedEntityAsync(
                string groupId,
                string resourceId,
                string versionId,
                bool deleteLogicalResource,
                long? epoch,
                CancellationToken ct)
            {
                return NotSupported();
            }

            public ValueTask<ServiceResult> AddRegistryLabelAsync(
                string key,
                string value,
                long? epoch,
                CancellationToken ct)
            {
                return NotSupported();
            }

            public ValueTask<ServiceResult> RemoveRegistryLabelAsync(string key, long? epoch, CancellationToken ct)
            {
                return NotSupported();
            }

            public ValueTask<ServiceResult> AddGroupLabelAsync(
                string groupId,
                string key,
                string value,
                long? epoch,
                CancellationToken ct)
            {
                return NotSupported();
            }

            public ValueTask<ServiceResult> RemoveGroupLabelAsync(
                string groupId,
                string key,
                long? epoch,
                CancellationToken ct)
            {
                return NotSupported();
            }

            public ValueTask<ServiceResult> AddResourceLabelAsync(
                string groupId,
                string resourceId,
                string key,
                string value,
                long? epoch,
                CancellationToken ct)
            {
                return NotSupported();
            }

            public ValueTask<ServiceResult> RemoveResourceLabelAsync(
                string groupId,
                string resourceId,
                string key,
                long? epoch,
                CancellationToken ct)
            {
                return NotSupported();
            }

            public ValueTask<ServiceResult> AddVersionLabelAsync(
                string groupId,
                string resourceId,
                string versionId,
                string key,
                string value,
                long? epoch,
                CancellationToken ct)
            {
                return NotSupported();
            }

            public ValueTask<ServiceResult> RemoveVersionLabelAsync(
                string groupId,
                string resourceId,
                string versionId,
                string key,
                long? epoch,
                CancellationToken ct)
            {
                return NotSupported();
            }

            public ValueTask<ServiceResult> AddResourceMetaLabelAsync(
                string groupId,
                string resourceId,
                string key,
                string value,
                long? epoch,
                CancellationToken ct)
            {
                return NotSupported();
            }

            public ValueTask<ServiceResult> RemoveResourceMetaLabelAsync(
                string groupId,
                string resourceId,
                string key,
                long? epoch,
                CancellationToken ct)
            {
                return NotSupported();
            }

            private static ValueTask<ServiceResult> NotSupported()
            {
                return new ValueTask<ServiceResult>(new ServiceResult(StatusCodes.BadNotSupported));
            }

            private readonly ushort m_ns;
        }

        private sealed class Snapshot : IXRegistryProjectionSnapshot
        {
            public Snapshot(string groupId, string resourceId)
            {
                Groups = [new Group(groupId, [new ProjectedVersion(groupId, resourceId)])];
            }

            public ImmutableSortedDictionary<string, string> Labels { get; } =
                ImmutableSortedDictionary<string, string>.Empty;

            public IEnumerable<IXRegistryProjectionGroup> Groups { get; }
        }

        private sealed class Group : IXRegistryProjectionGroup
        {
            public Group(string groupId, IEnumerable<IXRegistryProjectionResource> resources)
            {
                GroupId = groupId;
                Resources = resources;
            }

            public string GroupId { get; }

            public string Xid => "/groups/" + GroupId;

            public string Name => GroupId;

            public string Description => string.Empty;

            public long Epoch => 1;

            public ImmutableSortedDictionary<string, string> Labels { get; } =
                ImmutableSortedDictionary<string, string>.Empty;

            public IEnumerable<IXRegistryProjectionResource> Resources { get; }
        }

        private sealed class ProjectedVersion : IXRegistryProjectionResource, IXRegistryProjectionResourceMeta
        {
            public ProjectedVersion(string groupId, string resourceId)
            {
                GroupId = groupId;
                ResourceId = resourceId;
            }

            public string GroupId { get; }

            public string ResourceId { get; }

            public string VersionId => "1";

            public string Xid => $"/groups/{GroupId}/resources/{ResourceId}/versions/1";

            public string Name => ResourceId;

            public string Description => string.Empty;

            public string Format => "json";

            public string ContentType => "application/json";

            public long Epoch => 1;

            public DateTime CreatedAt => s_created;

            public DateTime ModifiedAt => s_created;

            public ImmutableSortedDictionary<string, string> Labels { get; } =
                ImmutableSortedDictionary<string, string>.Empty;

            public long MetaEpoch => 1;

            public ImmutableSortedDictionary<string, string> MetaLabels { get; } =
                ImmutableSortedDictionary<string, string>.Empty;

            public DateTime MetaCreatedAt => s_created;

            public DateTime MetaModifiedAt => s_created;

            public bool IsDefaultVersion => true;

            private static readonly DateTime s_created = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        }
    }
}
