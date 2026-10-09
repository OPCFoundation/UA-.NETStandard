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
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Export;
using UaLens.NodeSets;
using UaLens.NodeSets.Loading;
using UaLens.Telemetry;
using UaLens.ViewModels;
using UaLens.Workspace;

namespace UaLens.Tests.NodeSets
{
    [TestFixture]
    public sealed partial class NodeSetAddressSpaceTests
    {

        [Test]
        public async Task ImportsNamespacesAndSynthesizesInverseCrossFileReferences()
        {
            ushort app = m_graph.NamespaceUris.GetIndexOrAppend("urn:lens:app");
            ushort dependency = m_graph.NamespaceUris.GetIndexOrAppend("urn:lens:dependency");
            Assert.That(app, Is.Not.EqualTo(dependency));
            BrowseResponse folder = await BrowseAsync(ObjectIds.ObjectsFolder, ReferenceTypeIds.Organizes)
                .ConfigureAwait(false);
            Assert.That(
                folder.Results[0].References.ToList().Count(reference => reference.NodeId == new NodeId(1, app)),
                Is.EqualTo(1), "An inverse-only reference must be browsable from the parent, without duplicates.");
            BrowseResponse child = await BrowseAsync(new NodeId(1, app), ReferenceTypeIds.HasComponent)
                .ConfigureAwait(false);
            Assert.That(child.Results[0].References.ToList().Select(reference => reference.NodeId),
                Does.Contain((ExpandedNodeId)new NodeId(2, dependency)));
            BrowseResponse reverse = await BrowseAsync(
                new NodeId(2, dependency), ReferenceTypeIds.HasComponent, BrowseDirection.Inverse)
                .ConfigureAwait(false);
            Assert.That(reverse.Results[0].References.ToList().Select(reference => reference.NodeId),
                Does.Contain((ExpandedNodeId)new NodeId(1, app)));
        }

        [Test]
        public async Task ReadsTypedValuesAndDataTypeDefinitionsWithoutSession()
        {
            ushort dependency = m_graph.NamespaceUris.GetIndexOrAppend("urn:lens:dependency");
            ReadResponse response = await m_graph.ReadAsync(
                [
                    new ReadValueId { NodeId = new NodeId(2, dependency), AttributeId = Attributes.Value },
                    new ReadValueId { NodeId = new NodeId(2, dependency), AttributeId = Attributes.DataType },
                    new ReadValueId { NodeId = new NodeId(3, dependency), AttributeId = Attributes.DataTypeDefinition },
                    new ReadValueId { NodeId = new NodeId(2, dependency), AttributeId = Attributes.Executable }
                ]).ConfigureAwait(false);
            Assert.That(response.Results[0].WrappedValue.TryGetValue(out int value), Is.True);
            Assert.That(value, Is.EqualTo(42));
            Assert.That(response.Results[1].WrappedValue.TryGetValue(out NodeId dataType), Is.True);
            Assert.That(dataType, Is.EqualTo(DataTypeIds.Int32));
            Assert.That(response.Results[2].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(response.Results[2].WrappedValue.TryGetValue(out ExtensionObject definition), Is.True);
            Assert.That(definition.TryGetValue(out StructureDefinition? structure), Is.True);
            Assert.That(structure!.Fields[0].Name, Is.EqualTo("Reading"));
            Assert.That(response.Results[3].StatusCode, Is.EqualTo(StatusCodes.BadAttributeIdInvalid));
        }

        [Test]
        public async Task BrowseFiltersUseCustomReferenceSubtypesAndPreserveMissingTargets()
        {
            ushort app = m_graph.NamespaceUris.GetIndexOrAppend("urn:lens:app");
            BrowseResponse response = await m_graph.BrowseAsync(
                [new BrowseDescription
                {
                    NodeId = new NodeId(1, app),
                    ReferenceTypeId = ReferenceTypeIds.Aggregates,
                    IncludeSubtypes = true,
                    BrowseDirection = BrowseDirection.Forward,
                    NodeClassMask = (uint)NodeClass.Variable
                }]).ConfigureAwait(false);
            Assert.That(response.Results[0].References, Has.Count.EqualTo(1));
            BrowseResponse dangling = await BrowseAsync(new NodeId(1, app), ReferenceTypeIds.Organizes)
                .ConfigureAwait(false);
            Assert.That(dangling.Results[0].References.ToList().Single().DisplayName.Text, Does.Contain("unresolved"));
            Assert.That(m_graph.UnresolvedReferenceCount, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public async Task ReadsUnknownNodesAndBrowsesUnknownNodesWithExplicitStatus()
        {
            var missing = new NodeId("missing", 0);
            BrowseResponse browse = await BrowseAsync(missing, ReferenceTypeIds.References).ConfigureAwait(false);
            ReadResponse read = await m_graph.ReadAsync(
                [new ReadValueId { NodeId = missing, AttributeId = Attributes.BrowseName }]).ConfigureAwait(false);
            Assert.That(browse.Results[0].StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            Assert.That(read.Results[0].StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
        }

        [Test]
        public async Task RemoteServerReferencesCannotResolveToLocalNodesWithTheSameId()
        {
            NodeSetDocument remote = ReadDocument("""
            <UANodeSet xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd">
              <NamespaceUris><Uri>urn:lens:remote</Uri></NamespaceUris>
              <ServerUris><Uri>urn:remote:server</Uri></ServerUris>
              <UAObject NodeId="ns=1;i=1" BrowseName="1:RemoteSource">
                <References><Reference ReferenceType="i=35">svr=1;i=85</Reference></References>
              </UAObject>
            </UANodeSet>
            """, "remote.xml");
            NodeSetAddressSpace graph = await CreateAsync(remote).ConfigureAwait(false);
            ushort ns = graph.NamespaceUris.GetIndexOrAppend("urn:lens:remote");
            BrowseResponse response = await graph.BrowseAsync(
                [new BrowseDescription
                {
                    NodeId = new NodeId(1, ns),
                    ReferenceTypeId = ReferenceTypeIds.Organizes,
                    BrowseDirection = BrowseDirection.Forward
                }]).ConfigureAwait(false);
            ReferenceDescription target = response.Results[0].References.ToList().Single();
            Assert.That(target.NodeId.ServerIndex, Is.GreaterThan(0));
            Assert.That(target.NodeClass, Is.EqualTo(NodeClass.Unspecified));
            Assert.That(target.DisplayName.Text, Does.Contain("unresolved"));
        }

        [Test]
        public async Task ResolvesNamespaceQualifiedPathsAndReturnsCompleteAuthoredXml()
        {
            ushort app = m_graph.NamespaceUris.GetIndexOrAppend("urn:lens:app");
            ushort dependency = m_graph.NamespaceUris.GetIndexOrAppend("urn:lens:dependency");
            ArrayOf<NodeId> matches = await m_graph.ResolvePathAsync(
                ObjectIds.ObjectsFolder, $"/{app}:Machine/{dependency}:Reading").ConfigureAwait(false);
            Assert.That(matches.ToArray(), Is.EqualTo([new NodeId(2, dependency)]));
            string xml = await m_graph.ReadNodeXmlAsync(new NodeId(3, dependency)).ConfigureAwait(false);
            Assert.That(xml, Does.Contain("Reading").And.Contain("Definition").And.Contain("urn:lens:dependency"));
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
            var exported = UANodeSet.Read(stream);
            Assert.That(exported!.Items, Has.Length.EqualTo(1));
        }

        [Test]
        public async Task ExistingBrowserLoadsOfflineTypesAndOrphansWithoutCallingSession()
        {
            var browser = new BrowserViewModel(
                Telemetry(), () => null, InlineWorkspaceDispatcher.Instance);
            browser.SetOfflineSource(m_graph);
            Assert.That(browser.Roots, Has.Count.EqualTo(1));
            await browser.LoadChildrenAsync(browser.Roots[0]).ConfigureAwait(false);
            Assert.That(browser.Roots[0].Children.Any(node => node.NodeId == ObjectIds.TypesFolder), Is.True);
            await browser.SetViewKindAsync(BrowseViewKind.ObjectTypes, default).ConfigureAwait(false);
            await browser.LoadChildrenAsync(browser.Roots[0]).ConfigureAwait(false);
            Assert.That(browser.Roots[0].Children.Any(node => node.NodeId == ObjectTypeIds.BaseObjectType), Is.True);
            await browser.SetViewKindAsync(BrowseViewKind.AllNodes, default).ConfigureAwait(false);
            ushort app = m_graph.NamespaceUris.GetIndexOrAppend("urn:lens:app");
            NodeViewModel group = browser.Roots.Single(node => node.IndexNamespace == app);
            await browser.LoadChildrenAsync(group).ConfigureAwait(false);
            Assert.That(group.Children.Any(node => node.Text == "Orphan"), Is.True);
            Assert.That(browser.ShowOfflineNode(new NodeId(99, app))!.Text, Is.EqualTo("Orphan"));
            browser.SetOfflineSource(null);
            Assert.That(browser.Roots, Is.Empty);
            Assert.That(browser.CurrentViewKind, Is.EqualTo(BrowseViewKind.Objects));
            Assert.That(browser.ViewKinds.ToArray(), Does.Not.Contain(BrowseViewKind.AllNodes));
        }

        [TestCase("ObjectTypes", "i=58")]
        [TestCase("VariableTypes", "i=62")]
        [TestCase("DataTypes", "i=24")]
        [TestCase("ReferenceTypes", "i=31")]
        public async Task OfflineTypeViewsCanExpandTheirFolderRoots(string kindName, string expectedRoot)
        {
            var browser = new BrowserViewModel(
                Telemetry(), () => null, InlineWorkspaceDispatcher.Instance);
            browser.SetOfflineSource(m_graph);
            await browser.SetViewKindAsync(Enum.Parse<BrowseViewKind>(kindName), default).ConfigureAwait(false);
            await browser.LoadChildrenAsync(browser.Roots[0]).ConfigureAwait(false);
            Assert.That(browser.Roots[0].HasLoadError, Is.False);
            Assert.That(
                browser.Roots[0].Children.Select(node => node.NodeId), Does.Contain(NodeId.Parse(expectedRoot)));
        }

        [Test]
        public async Task OfflineTypeViewsExposeInstanceDeclarationsAsWellAsSubtypes()
        {
            ushort app = m_graph.NamespaceUris.GetIndexOrAppend("urn:lens:app");
            var browser = new BrowserViewModel(
                Telemetry(), () => null, InlineWorkspaceDispatcher.Instance);
            browser.SetOfflineSource(m_graph);
            await browser.SetViewKindAsync(BrowseViewKind.ObjectTypes, default).ConfigureAwait(false);
            NodeViewModel type = browser.ShowOfflineNode(new NodeId(10, app))!;
            await browser.LoadChildrenAsync(type).ConfigureAwait(false);
            Assert.That(type.Children.Single().NodeClass, Is.EqualTo(NodeClass.Method));
            Assert.That(type.Children.Single().Text, Is.EqualTo("Run"));
            ReadResponse method = await m_graph.ReadAsync(
                [new ReadValueId { NodeId = new NodeId(11, app), AttributeId = Attributes.Executable }])
                .ConfigureAwait(false);
            Assert.That(method.Results[0].WrappedValue.TryGetValue(out bool executable), Is.True);
            Assert.That(executable, Is.True);
        }

        [Test]
        public async Task OfflineInspectorFormatsRolePermissionsWithTheGraphMessageContext()
        {
            NodeSetDocument document = ReadDocument($$"""
            <UANodeSet xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd">
              <NamespaceUris><Uri>urn:lens:roles</Uri></NamespaceUris>
              <UAObject NodeId="ns=1;i=1" BrowseName="1:Secured">
                <RolePermissions>
                  <RolePermission Permissions="1">{{ObjectIds.WellKnownRole_Anonymous}}</RolePermission>
                </RolePermissions>
              </UAObject>
            </UANodeSet>
            """, "roles.xml");
            NodeSetAddressSpace graph = await CreateAsync(document).ConfigureAwait(false);
            await using var vm = new MainViewModel(Telemetry(), dispatcher: InlineWorkspaceDispatcher.Instance);
            using var inspector = new NodeAttributesViewModel(
                vm.Telemetry, vm.Connection, InlineWorkspaceDispatcher.Instance, () => graph);
            await inspector.LoadAsync(
                new NodeId(1, graph.NamespaceUris.GetIndexOrAppend("urn:lens:roles")), NodeClass.Object)
                .ConfigureAwait(false);
            Assert.That(inspector.Rows.Single(row => row.Name == "RolePermissions").Value,
                Does.Contain("Anonymous").And.Contain("Browse"));
        }

        [Test]
        public void DuplicateNodeIdsAndCyclicSubtypeDefinitionsFailBeforePublication()
        {
            NodeSetDocument duplicate = ReadDocument(kApp, "duplicate.xml");
            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await CreateAsync(duplicate).ConfigureAwait(false));
            NodeSetDocument cycle = ReadDocument("""
            <UANodeSet xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd">
              <NamespaceUris><Uri>urn:lens:cycle</Uri></NamespaceUris>
              <UAObjectType NodeId="ns=1;i=1" BrowseName="1:First">
                <References><Reference ReferenceType="i=45" IsForward="false">ns=1;i=2</Reference></References>
              </UAObjectType>
              <UAObjectType NodeId="ns=1;i=2" BrowseName="1:Second">
                <References><Reference ReferenceType="i=45" IsForward="false">ns=1;i=1</Reference></References>
              </UAObjectType>
            </UANodeSet>
            """, "cycle.xml");
            Assert.ThrowsAsync<InvalidOperationException>(async () => await CreateAsync(cycle).ConfigureAwait(false));
        }

        [Test]
        public void CancellationStopsImportReadBrowseAndPathResolution()
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            Assert.ThrowsAsync<TaskCanceledException>(async () =>
                await new NodeSetAddressSpaceFactory(Telemetry()).CreateAsync(
                    [ReadDocument(kApp, "app.xml")], cancellation.Token).ConfigureAwait(false));
            Assert.ThrowsAsync<OperationCanceledException>(async () => await m_graph.ReadAsync(
                [new ReadValueId { NodeId = ObjectIds.ObjectsFolder, AttributeId = Attributes.BrowseName }],
                cancellation.Token).ConfigureAwait(false));
            Assert.ThrowsAsync<OperationCanceledException>(async () => await m_graph.BrowseAsync(
                [new BrowseDescription { NodeId = ObjectIds.ObjectsFolder }], cancellation.Token)
                .ConfigureAwait(false));
            Assert.ThrowsAsync<OperationCanceledException>(async () => await m_graph.ResolvePathAsync(
                ObjectIds.ObjectsFolder, "/Machine", cancellation.Token).ConfigureAwait(false));
        }
    }
}
