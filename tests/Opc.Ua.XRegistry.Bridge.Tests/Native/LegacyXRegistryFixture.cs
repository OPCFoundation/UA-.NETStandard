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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server;

namespace Opc.Ua.XRegistry.Bridge.Tests.Native
{
    internal sealed class LegacyXRegistryFixture(string namespaceUri) : IAsyncNodeManagerFactory
    {
        public ArrayOf<string> NamespacesUris => [namespaceUri];

        public NodeId RootId { get; private set; }

        public ValueTask<IAsyncNodeManager> CreateAsync(
            IServerInternal server, ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            var manager = new LegacyNodeManager(server, configuration, namespaceUri);
            RootId = manager.RootId;
            return new ValueTask<IAsyncNodeManager>(manager);
        }

        private sealed class LegacyNodeManager : AsyncCustomNodeManager
        {
            public LegacyNodeManager(
                IServerInternal server, ApplicationConfiguration configuration, string namespaceUri)
                : base(server, configuration, server.Telemetry.CreateLogger<LegacyNodeManager>(), namespaceUri)
            {
                m_namespaceIndex = server.NamespaceUris.GetIndexOrAppend(namespaceUri);
                RootId = new NodeId("LegacyRoot", m_namespaceIndex);
            }

            public NodeId RootId { get; }

            public override async ValueTask CreateAddressSpaceAsync(
                IDictionary<NodeId, IList<IReference>> externalReferences,
                CancellationToken cancellationToken = default)
            {
                await base.CreateAddressSpaceAsync(externalReferences, cancellationToken).ConfigureAwait(false);
                RegistryState registry = SystemContext.CreateInstanceOfRegistryType(
                    null!, new QualifiedName("LegacyRoot", m_namespaceIndex));
                registry.NodeId = RootId;
                registry.RegistryId!.Value = "legacy-registry";
                registry.AddEpoch(SystemContext).AddXid(SystemContext);
                registry.Epoch!.Value = 0;
                registry.Xid!.Value = "/";
                GroupState group = SystemContext.CreateInstanceOfGroupType(
                    registry, new QualifiedName("g", m_namespaceIndex));
                group.GroupId!.Value = "g";
                group.AddEpoch(SystemContext).AddXid(SystemContext);
                group.Epoch!.Value = 0;
                group.Xid!.Value = "/schemagroups/g";
                group.ReferenceTypeId = ReferenceTypeIds.Organizes;
                registry.AddChild(group);
                foreach (string version in s_versions)
                {
                    ResourceState file = SystemContext.CreateInstanceOfResourceType(
                        group, new QualifiedName(version, m_namespaceIndex));
                    file.ResourceId!.Value = "r";
                    file.AddVersionId(SystemContext).AddEpoch(SystemContext).AddXid(SystemContext);
                    file.VersionId!.Value = version;
                    file.Epoch!.Value = 0;
                    file.Xid!.Value = "/schemagroups/g/schemas/r/versions/" + version;
                    file.ReferenceTypeId = ReferenceTypeIds.Organizes;
                    group.AddChild(file);
                }
                SystemContext.AssignInstanceChildNodeIds(registry);
                registry.AddReference(ReferenceTypeIds.Organizes, true, Ua.ObjectIds.ObjectsFolder);
                if (!externalReferences.TryGetValue(Ua.ObjectIds.ObjectsFolder, out IList<IReference>? references))
                {
                    externalReferences[Ua.ObjectIds.ObjectsFolder] = references = [];
                }
                references.Add(new NodeStateReference(ReferenceTypeIds.Organizes, false, registry.NodeId));
                await AddPredefinedNodeAsync(SystemContext, registry, cancellationToken).ConfigureAwait(false);
            }

            private static readonly string[] s_versions = ["v1", "v2"];
            private readonly ushort m_namespaceIndex;
        }
    }
}
