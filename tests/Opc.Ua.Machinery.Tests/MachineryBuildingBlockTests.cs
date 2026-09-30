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
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Machinery.Server.Builders;
using Opc.Ua.Machinery.Server.StateMachines;
using DiBrowseNames = Opc.Ua.Di.BrowseNames;

namespace Opc.Ua.Machinery.Tests
{
    /// <summary>
    /// Pins the OPC 40001-1 §7.1 building-block organization.
    /// </summary>
    /// <remarks>
    /// Table 12 of OPC 40001-1 v1.04.1 says the item state, the operation
    /// mode, both counters, monitoring, equipment and notifications
    /// <em>shall</em> be referenced by the <c>MachineryBuildingBlocks</c>
    /// folder, and identification and the components folder <em>may</em> be.
    /// Every conformance unit for those blocks is worded as "has this AddIn
    /// under its MachineryBuildingBlocks folder", so the folder is not
    /// cosmetic: without it none of them is satisfied.
    /// </remarks>
    [TestFixture]
    [Category("Machinery")]
    public sealed class MachineryBuildingBlockTests
    {
        [SetUp]
        public async Task SetUpAsync()
        {
            m_fixture = new MachineryServerFixture(MachineryParts.BuildingBlocks);
            await m_fixture.StartAsync();
            m_context = m_fixture.CreateBuildContext();
        }

        [TearDown]
        public async Task TearDownAsync()
        {
            if (m_fixture != null)
            {
                await m_fixture.DisposeAsync();
            }
        }

        [Test]
        public async Task EveryBlockIsReachableThroughTheOrganizerFolderAsync()
        {
            IMachineHandle<BaseObjectState> machine = await BuildAsync();
            FolderState folder = FindOrganizer(machine.State);

            Assert.That(
                folder.ReferenceTypeId,
                Is.EqualTo(Opc.Ua.ReferenceTypeIds.HasComponent),
                "OPC 40001-1 §7.1 references the organizer with HasComponent.");
            Assert.That(folder.TypeDefinitionId, Is.EqualTo(Opc.Ua.ObjectTypeIds.FolderType));

            Dictionary<QualifiedName, NodeId> referenced = ReferencedBlocks(folder);
            ushort machineryNamespaceIndex = NamespaceIndex(
                Opc.Ua.Machinery.Namespaces.Machinery);
            ushort diNamespaceIndex = NamespaceIndex(Opc.Ua.Di.Namespaces.OpcUaDi);

            // Shall be referenced.
            Assert.That(
                referenced,
                Contains.Key(new QualifiedName(
                    BrowseNames.Monitoring,
                    machineryNamespaceIndex)));
            Assert.That(
                referenced,
                Contains.Key(new QualifiedName(
                    BrowseNames.MachineryItemState,
                    machineryNamespaceIndex)));
            Assert.That(
                referenced,
                Contains.Key(new QualifiedName(
                    BrowseNames.MachineryOperationMode,
                    machineryNamespaceIndex)));
            // MachineryOperationCounterType's DefaultInstanceBrowseName is
            // 2:OperationCounters — the Device Integration namespace.
            Assert.That(
                referenced,
                Contains.Key(new QualifiedName(
                    DiBrowseNames.OperationCounters,
                    diNamespaceIndex)));
            Assert.That(
                referenced,
                Contains.Key(new QualifiedName(
                    BrowseNames.LifetimeCounters,
                    machineryNamespaceIndex)));
            Assert.That(
                referenced,
                Contains.Key(new QualifiedName(
                    BrowseNames.MachineryEquipment,
                    machineryNamespaceIndex)));
            Assert.That(
                referenced,
                Contains.Key(new QualifiedName(
                    BrowseNames.Notifications,
                    machineryNamespaceIndex)));

            // May be referenced; the library does, as §7.1 recommends.
            Assert.That(
                referenced,
                Contains.Key(new QualifiedName(
                    DiBrowseNames.Identification,
                    diNamespaceIndex)));
            Assert.That(
                referenced,
                Contains.Key(new QualifiedName(
                    BrowseNames.Components,
                    machineryNamespaceIndex)));
        }

        [Test]
        public async Task TheOrganizerReferencesTheSameNodeRatherThanACopyAsync()
        {
            IMachineHandle<BaseObjectState> machine = await BuildAsync();
            FolderState folder = FindOrganizer(machine.State);
            Dictionary<QualifiedName, NodeId> referenced = ReferencedBlocks(folder);

            // OPC 40001-1 §7.3: two paths, one node. A duplicated node would
            // give the second path a NodeId of its own.
            var monitoringName = new QualifiedName(
                BrowseNames.Monitoring,
                NamespaceIndex(Opc.Ua.Machinery.Namespaces.Machinery));
            NodeState? direct = machine.State.FindChild(
                m_context!.Context,
                monitoringName);
            Assert.That(direct, Is.Not.Null);
            Assert.That(referenced[monitoringName], Is.EqualTo(direct!.NodeId));

            // The state machine keeps Monitoring/Status as its parent and is
            // reachable from the organizer through a second reference.
            var itemStateName = new QualifiedName(
                BrowseNames.MachineryItemState,
                NamespaceIndex(Opc.Ua.Machinery.Namespaces.Machinery));
            Assert.That(referenced[itemStateName], Is.EqualTo(machine.ItemState!.NodeId));
            NodeState? status = direct.FindChild(
                m_context.Context,
                new QualifiedName(
                    BrowseNames.Status,
                    NamespaceIndex(Opc.Ua.Machinery.Namespaces.Machinery)));
            Assert.That(status, Is.Not.Null);
            Assert.That(
                status!.FindChild(m_context.Context, itemStateName)!.NodeId,
                Is.EqualTo(machine.ItemState.NodeId));
        }

        [Test]
        public async Task TheOrganizationUnitIsAdvertisedAsync()
        {
            await BuildAsync();

            QualifiedName[] units = [.. m_fixture!.Manager.ConformanceUnits];
            Assert.That(
                units,
                Contains.Item(new QualifiedName("Machinery Building Block Organization")));

            // The state facet composes the organization unit, so it can only
            // be claimed now that the folder exists.
            string[] profiles = [.. m_fixture.Manager.ServerProfiles];
            Assert.That(
                profiles,
                Contains.Item("http://opcfoundation.org/UA-Profile/Machinery/Server/State"));
            Assert.That(
                profiles,
                Contains.Item(
                    "http://opcfoundation.org/UA-Profile/Machinery/Server/Monitoring"));
        }

        [Test]
        public async Task AComponentCarriesItsOwnOrganizerAsync()
        {
            IMachineHandle<BaseObjectState> machine = await BuildAsync();

            var components = new List<BaseInstanceState>();
            NodeState? folder = machine.State.FindChild(
                m_context!.Context,
                new QualifiedName(
                    BrowseNames.Components,
                    NamespaceIndex(Opc.Ua.Machinery.Namespaces.Machinery)));
            Assert.That(folder, Is.Not.Null);
            folder!.GetChildren(m_context.Context, components);
            Assert.That(components, Has.Count.EqualTo(1));

            FolderState componentOrganizer = FindOrganizer(components[0]);
            Dictionary<QualifiedName, NodeId> referenced =
                ReferencedBlocks(componentOrganizer);
            Assert.That(
                referenced,
                Contains.Key(new QualifiedName(
                    DiBrowseNames.Identification,
                    NamespaceIndex(Opc.Ua.Di.Namespaces.OpcUaDi))),
                "A component is a machinery item and organizes its own blocks.");
        }

        private ValueTask<IMachineHandle<BaseObjectState>> BuildAsync()
        {
            return m_context!
                .AddMachine(new QualifiedName("Press-1"))
                .WithIdentification(id =>
                {
                    id.Manufacturer = new LocalizedText("Acme");
                    id.SerialNumber = "SN-0001";
                    id.ProductInstanceUri = "urn:acme:press:1";
                })
                .WithMonitoring(monitoring => monitoring
                    .WithMachineryItemState(MachineryItemStateValue.NotExecuting)
                    .WithOperationMode(MachineryOperationModeValue.Setup))
                .WithComponents(components => components.AddComponent(
                    new QualifiedName("Ram"),
                    component => component.WithIdentification(id =>
                    {
                        id.Manufacturer = new LocalizedText("Acme");
                        id.SerialNumber = "RAM-1";
                    })))
                .WithOperationCounters(counters => counters.WithPowerOnDuration(1))
                .WithLifetimeCounters(counters => counters.AddLifetimeVariable(
                    new QualifiedName("SealLife"),
                    100,
                    50))
                .WithMachineryEquipment(equipment => equipment.AddEquipment(
                    new QualifiedName("Die"),
                    "urn:acme:equipment:die"))
                .WithNotifications()
                .BuildAsync();
        }

        private FolderState FindOrganizer(NodeState machineryItem)
        {
            NodeState? folder = machineryItem.FindChild(
                m_context!.Context,
                new QualifiedName(
                    "MachineryBuildingBlocks",
                    NamespaceIndex(Opc.Ua.Machinery.Namespaces.Machinery)));
            Assert.That(
                folder,
                Is.Not.Null,
                "OPC 40001-1 §7.1 requires the MachineryBuildingBlocks folder.");
            return (FolderState)folder!;
        }

        private Dictionary<QualifiedName, NodeId> ReferencedBlocks(FolderState folder)
        {
            var references = new List<IReference>();
            folder.GetReferences(m_context!.Context, references);
            var blocks = new Dictionary<QualifiedName, NodeId>();
            foreach (IReference reference in references)
            {
                if (reference.IsInverse ||
                    reference.ReferenceTypeId != Opc.Ua.ReferenceTypeIds.HasAddIn)
                {
                    continue;
                }
                var nodeId = ExpandedNodeId.ToNodeId(
                    reference.TargetId,
                    m_fixture!.Manager.Server.NamespaceUris);
                NodeState? target = m_fixture.Manager.FindPredefinedNode(nodeId);
                if (target != null)
                {
                    blocks[target.BrowseName] = nodeId;
                }
            }
            return blocks;
        }

        private ushort NamespaceIndex(string namespaceUri)
        {
            return (ushort)m_fixture!.Manager.Server.NamespaceUris.GetIndex(namespaceUri);
        }

        private MachineryServerFixture? m_fixture;
        private IMachineryBuildContext? m_context;
    }
}
