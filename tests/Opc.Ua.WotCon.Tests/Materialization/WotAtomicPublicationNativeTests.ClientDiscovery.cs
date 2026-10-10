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

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Tests;
using Opc.Ua.WotCon.Client;
using Opc.Ua.WotCon.Server;
using Opc.Ua.WotCon.Server.Registry;
using Opc.Ua.XRegistry;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    public sealed partial class WotAtomicPublicationNativeTests
    {
        [Test]
        public async Task ClientDiscoversImplicitRegistryGroupAndResourceWithoutMutation()
        {
            WotResource resource = await AddClientDiscoveryResourceAsync().ConfigureAwait(false);
            long generation = m_registry.Current.Generation;

            WotRegistryClient client = await WotRegistryClient.ForServerAsync(
                m_session, NUnitTelemetryContext.Create()).ConfigureAwait(false);
            WotRegistryGroupClient group = await client.OpenGroupAsync(resource.GroupId).ConfigureAwait(false);
            Assert.That(group.GroupId, Is.EqualTo(WotRegistryClient.ThingDescriptionsGroupId));
            Assert.That(group.Kind, Is.EqualTo(WoTDocumentKindEnum.ThingDescription));
            WotRegistryResourceClient opened = await group.OpenResourceAsync(resource.ResourceId).ConfigureAwait(false);
            Assert.That(opened.ResourceNodeId, Is.EqualTo(ResourceId(resource)));
            ByteString downloaded = await opened.DownloadAsync().ConfigureAwait(false);
            Assert.That(downloaded.ToArray(), Is.EqualTo(TestMaterialization.Td("urn:implicit-client-resource")));
            Assert.That(m_registry.Current.Generation, Is.EqualTo(generation));
            Assert.That(m_coordinator.Generation, Is.Zero);
            Assert.That(m_registry.Current.FindResourceByXid(resource.Xid)!.ActiveVersionId, Is.Null.Or.Empty);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public async Task NativeRegistryExposesItsDeclaredHierarchy(int level)
        {
            WotResource resource = await AddClientDiscoveryResourceAsync().ConfigureAwait(false);
            WotRegistryClient client = await WotRegistryClient.ForServerAsync(
                m_session, NUnitTelemetryContext.Create()).ConfigureAwait(false);
            ushort ns = client.RegistryNodeId.NamespaceIndex;
            var group = new NodeId($"WoTRegistry/groups/{resource.GroupId}", ns);
            NodeId logical = ResourceId(resource);
            NodeId versions = NodeId.Null;
            if (level >= 2)
            {
                (_, _, ArrayOf<ReferenceDescription> components) = await m_session.BrowseAsync(
                    null, null, logical, 0, BrowseDirection.Forward, Ua.ReferenceTypeIds.HasComponent, false,
                    (uint)NodeClass.Object, CancellationToken.None).ConfigureAwait(false);
                var folders = components.ToList().Where(reference => reference.BrowseName == new QualifiedName(
                    "Versions", m_session.NamespaceUris.GetIndexOrAppend(XRegistryWellKnown.XRegistryNamespaceUri)))
                    .ToArray();
                Assert.That(folders, Has.Length.EqualTo(1),
                    "The typed Versions container uses the inherited xRegistry-qualified BrowseName.");
                versions = ExpandedNodeId.ToNodeId(folders[0].NodeId, m_session.NamespaceUris);
            }
            var version = new NodeId(
                $"WoTRegistry/groups/{resource.GroupId}/resources/{resource.ResourceId}/versions/v1", ns);
            (NodeId parent, NodeId child, NodeId referenceType) = level switch
            {
                0 => (client.RegistryNodeId, group, Ua.ReferenceTypeIds.Organizes),
                1 => (group, logical, Ua.ReferenceTypeIds.Organizes),
                2 => (logical, versions, Ua.ReferenceTypeIds.HasComponent),
                _ => (versions, version, Ua.ReferenceTypeIds.Organizes)
            };
            (_, _, ArrayOf<ReferenceDescription> forward) = await m_session.BrowseAsync(
                null, null, parent, 0, BrowseDirection.Forward, referenceType, false,
                (uint)NodeClass.Object, CancellationToken.None).ConfigureAwait(false);
            Assert.That(forward.ToList().Count(reference =>
                ExpandedNodeId.ToNodeId(reference.NodeId, m_session.NamespaceUris) == child), Is.EqualTo(1),
                "The declared forward hierarchy must exist independently of HasNotifier.");
            (_, _, ArrayOf<ReferenceDescription> inverse) = await m_session.BrowseAsync(
                null, null, child, 0, BrowseDirection.Inverse, referenceType, false,
                (uint)NodeClass.Object, CancellationToken.None).ConfigureAwait(false);
            Assert.That(inverse.ToList().Count(reference =>
                ExpandedNodeId.ToNodeId(reference.NodeId, m_session.NamespaceUris) == parent), Is.EqualTo(1),
                "The child must identify its actual containing parent through the same reference type.");
        }

        private async Task<WotResource> AddClientDiscoveryResourceAsync()
        {
            await m_server.NodeManagerLifecycle.AddAsync(new WotRegistryNodeManagerFactory(
                new WotRegistryServerOptions
                {
                    AutoRefresh = false,
                    ManagementAccess = new WotManagementAccessPolicy
                    {
                        MinimumSecurityMode = MessageSecurityMode.None,
                        AllowAnonymous = true,
                        RequiredRoleId = Ua.ObjectIds.WellKnownRole_Anonymous
                    }
                }, m_registry, m_coordinator), callerContext: null).ConfigureAwait(false);
            WotResource resource = await AddAsync("implicit-client-resource").ConfigureAwait(false);
            await AwaitStockRegistryProjectionAsync().ConfigureAwait(false);
            await m_session.FetchNamespaceTablesAsync().ConfigureAwait(false);
            return resource;
        }
    }
}
