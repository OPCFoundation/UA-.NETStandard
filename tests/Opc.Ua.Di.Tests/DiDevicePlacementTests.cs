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
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Di.Server;
using Opc.Ua.Server;
using Opc.Ua.Server.TestFramework;

namespace Opc.Ua.Di.Tests
{
    /// <summary>
    /// Checks DeviceSet reachability for directly hosted and composed devices.
    /// </summary>
    [TestFixture]
    [Category("DI")]
    public sealed class DiDevicePlacementTests
    {
        [OneTimeSetUp]
        public async Task StartServerAsync()
        {
            m_fixture = new ServerFixture<StandardServer>(t => new StandardServer(t))
            {
                AutoAccept = true,
                SecurityNone = true
            };
            await m_fixture.StartAsync().ConfigureAwait(false);
        }

        [SetUp]
        public async Task CreateManagerAsync()
        {
            m_manager = new DiNodeManager(m_fixture.Server.CurrentInstance, m_fixture.Config);
            await m_manager.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>())
                .ConfigureAwait(false);
            m_deviceSet = m_manager.FindPredefinedNode(NodeId.Create(
                Objects.DeviceSet, DiNodeManager.DiNamespaceUri, m_manager.Server.NamespaceUris))!;
        }

        [TearDown]
        public async Task DisposeManagerAsync()
        {
            await m_manager.DisposeAsync().ConfigureAwait(false);
        }

        [OneTimeTearDown]
        public async Task StopServerAsync()
        {
            await m_fixture.StopAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task DeviceUnderDisconnectedFolderIsAutomaticallyOrganizedAsync()
        {
            BaseObjectState folder = await AddFolderAsync("Disconnected").ConfigureAwait(false);

            DeviceState device = (await m_manager.CreateDeviceAsync(
                Name("Reader"), folder).ConfigureAwait(false)).Device;

            Assert.That(device.Parent, Is.SameAs(folder));
            AssertOrganized(device, true);
            Assert.That(m_manager.IsReachableFrom(m_deviceSet, device), Is.True);
        }

        [Test]
        public async Task ComposedDeviceLinksOnlyItsOutermostComponentAsync()
        {
            BaseObjectState folder = await AddFolderAsync("Disconnected").ConfigureAwait(false);
            var root = new DeviceState(folder)
            {
                NodeId = Id("Root"),
                BrowseName = Name("Root"),
                ReferenceTypeId = Types.ReferenceTypeIds.HasComponent
            };
            folder.AddChild(root);
            await m_manager.AddPredefinedNodeAsync(root).ConfigureAwait(false);

            DeviceState nested = (await m_manager.CreateDeviceAsync(
                Name("Nested"), root).ConfigureAwait(false)).Device;

            AssertOrganized(root, true);
            AssertOrganized(nested, false);
            Assert.That(m_manager.IsReachableFrom(m_deviceSet, nested), Is.True);
            Assert.That(m_manager.TryEnsureDeviceSetReachability(nested), Is.True);
            Assert.That(m_manager.TryEnsureDeviceSetReachability(root), Is.True);
            AssertOrganized(nested, false);
        }

        [Test]
        public async Task ReachableParentDoesNotAddARedundantDirectReferenceAsync()
        {
            BaseObjectState folder = await AddFolderAsync("Reachable").ConfigureAwait(false);
            m_deviceSet.AddReference(Types.ReferenceTypeIds.Organizes, false, folder.NodeId);

            DeviceState device = (await m_manager.CreateDeviceAsync(
                Name("Nested"), folder).ConfigureAwait(false)).Device;

            AssertOrganized(device, false);
            Assert.That(m_manager.IsReachableFrom(m_deviceSet, device), Is.True);
        }

        [Test]
        public async Task DirectDeviceSetChildNeedsNoAdditionalReferenceAsync()
        {
            DeviceState device = (await m_manager.CreateDeviceAsync(Name("Direct")).ConfigureAwait(false)).Device;

            Assert.That(device.Parent, Is.SameAs(m_deviceSet));
            Assert.That(device.ReferenceTypeId, Is.EqualTo(Types.ReferenceTypeIds.Organizes));
            Assert.That(m_manager.TryEnsureDeviceSetReachability(device), Is.True);
            AssertOrganized(device, false);
        }

        [Test]
        public async Task NonHierarchicalParentDoesNotTurnAnUnrelatedDeviceIntoTheComposedRootAsync()
        {
            var unrelated = new DeviceState(null)
            {
                NodeId = Id("Unrelated"),
                BrowseName = Name("Unrelated")
            };
            var folder = new BaseObjectState(unrelated)
            {
                NodeId = Id("Condition"),
                BrowseName = Name("Condition"),
                ReferenceTypeId = Types.ReferenceTypeIds.HasCondition
            };
            unrelated.AddChild(folder);
            await m_manager.AddPredefinedNodeAsync(folder).ConfigureAwait(false);

            DeviceState device = (await m_manager.CreateDeviceAsync(
                Name("Nested"), folder).ConfigureAwait(false)).Device;

            AssertOrganized(device, true);
            AssertOrganized(unrelated, false);
        }

        [Test]
        public async Task ReachabilityTraversesHierarchicalReferencesAndTerminatesCyclesAsync()
        {
            BaseObjectState first = await AddFolderAsync("First").ConfigureAwait(false);
            BaseObjectState second = await AddFolderAsync("Second").ConfigureAwait(false);
            BaseObjectState target = await AddFolderAsync("Target").ConfigureAwait(false);
            first.AddReference(Types.ReferenceTypeIds.HasComponent, false, second.NodeId);
            second.AddReference(Types.ReferenceTypeIds.Organizes, false, first.NodeId);
            second.AddReference(Types.ReferenceTypeIds.HasProperty, false, target.NodeId);

            Assert.That(m_manager.IsReachableFrom(first, first), Is.True);
            Assert.That(m_manager.IsReachableFrom(first, target), Is.True);
            Assert.That(m_manager.IsReachableFrom(first, m_deviceSet), Is.False);
        }

        [TestCase(false, false, false)]
        [TestCase(true, false, true)]
        [TestCase(false, true, true)]
        public async Task ReachabilityRejectsNonHierarchicalInverseAndRemoteReferencesAsync(
            bool inverse, bool remote, bool hierarchical)
        {
            BaseObjectState first = await AddFolderAsync("First").ConfigureAwait(false);
            BaseObjectState target = await AddFolderAsync("Target").ConfigureAwait(false);
            ExpandedNodeId targetId = remote
                ? new ExpandedNodeId(target.NodeId, "urn:remote", 1)
                : target.NodeId;
            first.AddReference(
                hierarchical ? Types.ReferenceTypeIds.HasComponent : Types.ReferenceTypeIds.HasCondition,
                inverse, targetId);

            Assert.That(m_manager.IsReachableFrom(first, target), Is.False);
        }

        [Test]
        public async Task ReachabilityIncludesOnlyHierarchicalChildrenAsync()
        {
            BaseObjectState parent = await AddFolderAsync("Parent").ConfigureAwait(false);
            var child = new BaseObjectState(parent)
            {
                NodeId = Id("Child"),
                ReferenceTypeId = Types.ReferenceTypeIds.HasComponent
            };
            parent.AddChild(child);
            Assert.That(m_manager.IsReachableFrom(parent, child), Is.True);

            child.ReferenceTypeId = Types.ReferenceTypeIds.HasCondition;
            Assert.That(m_manager.IsReachableFrom(parent, child), Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task RuntimeRegistrationLinksComponentsButNotTypeDeclarationsAsync(bool declaration)
        {
            var device = new DeviceState(null)
            {
                NodeId = Id("Component"),
                BrowseName = Name("Component"),
                IsPartOfTypeHierarchy = declaration
            };

            await m_manager.AddPredefinedNodeAsync(device).ConfigureAwait(false);

            AssertOrganized(device, !declaration);
        }

        [Test]
        public async Task GenericComponentStatesUseTheModelTypeForComposedRootPlacementAsync()
        {
            var root = new BaseObjectState(null)
            {
                NodeId = Id("GenericRoot"),
                BrowseName = Name("GenericRoot"),
                TypeDefinitionId = NodeId.Create(
                    ObjectTypes.ComponentType, DiNodeManager.DiNamespaceUri, m_manager.Server.NamespaceUris)
            };
            var child = new BaseObjectState(root)
            {
                NodeId = Id("GenericChild"),
                BrowseName = Name("GenericChild"),
                ReferenceTypeId = Types.ReferenceTypeIds.HasComponent,
                TypeDefinitionId = root.TypeDefinitionId
            };
            root.AddChild(child);

            await m_manager.AddPredefinedNodeAsync(root).ConfigureAwait(false);

            Assert.That(m_deviceSet.ReferenceExists(
                Types.ReferenceTypeIds.Organizes, false, root.NodeId), Is.True);
            Assert.That(m_deviceSet.ReferenceExists(
                Types.ReferenceTypeIds.Organizes, false, child.NodeId), Is.False);
            Assert.That(m_manager.IsReachableFrom(m_deviceSet, child), Is.True);
        }

        [Test]
        public async Task DeletingAComposedRootRemovesItsAutomaticDeviceSetLinkAsync()
        {
            BaseObjectState folder = await AddFolderAsync("Disconnected").ConfigureAwait(false);
            DeviceState root = (await m_manager.CreateDeviceAsync(
                Name("Root"), folder).ConfigureAwait(false)).Device;
            DeviceState child = (await m_manager.CreateDeviceAsync(
                Name("Child"), root).ConfigureAwait(false)).Device;
            AssertOrganized(root, true);
            AssertOrganized(child, false);

            Assert.That(await m_manager.DeleteNodeAsync(
                m_manager.SystemContext, root.NodeId).ConfigureAwait(false), Is.True);

            AssertOrganized(root, false);
            Assert.That(m_manager.FindPredefinedNode(root.NodeId), Is.Null);
            Assert.That(m_manager.FindPredefinedNode(child.NodeId), Is.Null);
            Assert.That(m_manager.FindPredefinedNode(folder.NodeId), Is.SameAs(folder));
        }

        [Test]
        public async Task MissingDeviceSetReturnsFalseWithoutChangingTheDeviceAsync()
        {
            await using var manager = new DiNodeManager(m_fixture.Server.CurrentInstance, m_fixture.Config);
            var device = new DeviceState(null) { NodeId = Id("Unregistered") };

            Assert.That(manager.TryEnsureDeviceSetReachability(device), Is.False);
            var references = new List<IReference>();
            device.GetReferences(manager.SystemContext, references);
            Assert.That(references, Is.Empty);
        }

        [Test]
        public void PlacementHelpersRejectNullNodes()
        {
            Assert.Throws<ArgumentNullException>(() => m_manager.IsReachableFrom(null!, m_deviceSet));
            Assert.Throws<ArgumentNullException>(() => m_manager.IsReachableFrom(m_deviceSet, null!));
            Assert.Throws<ArgumentNullException>(() => m_manager.TryEnsureDeviceSetReachability(null!));
        }

        private async Task<BaseObjectState> AddFolderAsync(string name)
        {
            var folder = new BaseObjectState(null)
            {
                NodeId = Id(name),
                BrowseName = Name(name),
                TypeDefinitionId = Opc.Ua.ObjectTypeIds.FolderType
            };
            await m_manager.AddPredefinedNodeAsync(folder).ConfigureAwait(false);
            return folder;
        }

        private void AssertOrganized(ComponentState device, bool expected)
        {
            Assert.That(m_deviceSet.ReferenceExists(
                Types.ReferenceTypeIds.Organizes, false, device.NodeId), Is.EqualTo(expected));
            Assert.That(device.ReferenceExists(
                Types.ReferenceTypeIds.Organizes, true, m_deviceSet.NodeId), Is.EqualTo(expected));
        }

        private QualifiedName Name(string name)
        {
            return new QualifiedName(name, m_manager.InstanceNamespaceIndex);
        }

        private NodeId Id(string name)
        {
            return new NodeId(name, m_manager.InstanceNamespaceIndex);
        }

        private ServerFixture<StandardServer> m_fixture = null!;
        private DiNodeManager m_manager = null!;
        private NodeState m_deviceSet = null!;
    }
}
