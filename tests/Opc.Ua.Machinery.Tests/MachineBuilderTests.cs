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
using Opc.Ua.Machinery.Server;
using Opc.Ua.Machinery.Server.Builders;
using Opc.Ua.Machinery.Server.StateMachines;
using DiBrowseNames = Opc.Ua.Di.BrowseNames;

namespace Opc.Ua.Machinery.Tests
{
    /// <summary>
    /// Drives the fluent machine builder against a running node manager and
    /// checks what actually lands in the address space.
    /// </summary>
    [TestFixture]
    [Category("Machinery")]
    public sealed class MachineBuilderTests
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
        public async Task MachineIsOrganizedByTheMachinesFolder()
        {
            IMachineHandle<BaseObjectState> machine = await BuildPressAsync();

            Assert.That(machine.NodeId.IsNull, Is.False);
            Assert.That(
                machine.NodeId.NamespaceIndex,
                Is.EqualTo(m_context!.InstanceNamespaceIndex),
                "Machinery instances belong to the application-owned namespace.");

            var children = new List<BaseInstanceState>();
            m_context.MachinesFolder.GetChildren(m_context.Context, children);
            Assert.That(
                children.Exists(child => child.NodeId == machine.NodeId),
                Is.True,
                "OPC 40001-1 organizes machines from the Machines folder.");
            Assert.That(
                machine.State.ReferenceTypeId,
                Is.EqualTo(Opc.Ua.Types.ReferenceTypeIds.Organizes));
        }

        [Test]
        public async Task IdentificationIsAnAddInWithTheMandatoryProperties()
        {
            IMachineHandle<BaseObjectState> machine = await BuildPressAsync();

            var children = new List<BaseInstanceState>();
            machine.State.GetChildren(m_context!.Context, children);
            BaseInstanceState? identification = children.Find(
                child => child.BrowseName.Name == DiBrowseNames.Identification);

            Assert.That(identification, Is.Not.Null);
            Assert.That(
                identification!.ReferenceTypeId,
                Is.EqualTo(Opc.Ua.Types.ReferenceTypeIds.HasAddIn),
                "OPC 40001-1 attaches the building blocks with HasAddIn.");
            Assert.That(identification, Is.InstanceOf<MachineIdentificationState>());

            var typed = (MachineIdentificationState)identification;
            Assert.That(typed.Manufacturer!.Value.Text, Is.EqualTo("Acme"));
            Assert.That(typed.SerialNumber!.Value, Is.EqualTo("SN-0001"));
            Assert.That(typed.ProductInstanceUri!.Value, Is.EqualTo("urn:acme:press:1"));
            Assert.That(typed.Model!.Value.Text, Is.EqualTo("P-500"));
        }

        [Test]
        public void IdentificationWithoutManufacturerIsRejected()
        {
            IMachineBuilder<BaseObjectState> builder =
                m_context!.AddMachine(new QualifiedName("Broken"));

            ServiceResultException exception = Assert.Throws<ServiceResultException>(
                () => builder.WithIdentification(id => id.SerialNumber = "SN"))!;
            Assert.That(exception.Message, Does.Contain("Manufacturer"));
        }

        [Test]
        public async Task MachineryItemStateStartsInTheRequestedState()
        {
            IMachineHandle<BaseObjectState> machine = await BuildPressAsync();

            Assert.That(machine.ItemState, Is.Not.Null);
            Assert.That(
                machine.ItemState!.CurrentState,
                Is.EqualTo(MachineryItemStateValue.NotExecuting));
        }

        [Test]
        public async Task ServerDrivenTransitionUpdatesCurrentState()
        {
            IMachineHandle<BaseObjectState> machine = await BuildPressAsync();

            bool applied = await machine.ItemState!.SetStateAsync(
                MachineryItemStateValue.Executing);

            Assert.That(applied, Is.True);
            Assert.That(
                machine.ItemState.CurrentState,
                Is.EqualTo(MachineryItemStateValue.Executing));

            NodeState stateMachine =
                m_fixture!.Manager.FindPredefinedNode(machine.ItemState.NodeId)!;
            var typed = (MachineryItemState_StateMachineState)stateMachine;
            Assert.That(typed.CurrentState!.Value.Text, Is.EqualTo("Executing"));
            Assert.That(
                typed.CurrentState.Id!.Value,
                Is.EqualTo(
                    new NodeId(
                        MachineryItemState_StateMachineTypeIds.StateIds.Executing,
                        MachineryNamespaceIndex)));
            Assert.That(
                typed.LastTransition!.Value.Text,
                Is.EqualTo("FromNotExecutingToExecuting"));
        }

        [Test]
        public async Task OperationModeIsDrivenTheSameWay()
        {
            IMachineHandle<BaseObjectState> machine = await BuildPressAsync();

            Assert.That(machine.OperationMode, Is.Not.Null);
            Assert.That(
                await machine.OperationMode!.SetModeAsync(
                    MachineryOperationModeValue.Processing),
                Is.True);
            Assert.That(
                machine.OperationMode.CurrentMode,
                Is.EqualTo(MachineryOperationModeValue.Processing));
        }

        [Test]
        public async Task ComponentsCarryComponentIdentification()
        {
            IMachineHandle<BaseObjectState> machine = await m_context!
                .AddMachine(new QualifiedName("Press-2"))
                .WithIdentification(id =>
                {
                    id.Manufacturer = new LocalizedText("Acme");
                    id.SerialNumber = "SN-0002";
                    id.ProductInstanceUri = "urn:acme:press:2";
                })
                .WithComponents(components => components.AddComponent(
                    new QualifiedName("Ram"),
                    component => component.WithIdentification(id =>
                    {
                        id.Manufacturer = new LocalizedText("Acme");
                        id.SerialNumber = "RAM-1";
                        id.DeviceRevision = "C";
                    })))
                .BuildAsync();

            BaseInstanceState components = FindChild(machine.State, BrowseNames.Components);
            BaseInstanceState ram = FindChild(components, "Ram");
            BaseInstanceState identification = FindChild(ram, DiBrowseNames.Identification);

            Assert.That(identification, Is.InstanceOf<MachineryComponentIdentificationState>());
            var typed = (MachineryComponentIdentificationState)identification;
            Assert.That(typed.SerialNumber!.Value, Is.EqualTo("RAM-1"));
            Assert.That(typed.DeviceRevision!.Value, Is.EqualTo("C"));
        }

        [Test]
        public async Task NotificationsAreAnEventNotifier()
        {
            IMachineHandle<BaseObjectState> machine = await BuildPressAsync();

            var notifications = (BaseObjectState)FindChild(
                machine.State,
                BrowseNames.Notifications);
            Assert.That(
                notifications.EventNotifier,
                Is.EqualTo(EventNotifiers.SubscribeToEvents));
        }

        [Test]
        public async Task ConformanceUnitsFollowWhatWasBuilt()
        {
            await BuildPressAsync();

            QualifiedName[] units = [.. m_fixture!.Manager.ConformanceUnits];
            Assert.That(units, Contains.Item(new QualifiedName("Machinery Find Machines")));
            Assert.That(
                units,
                Contains.Item(new QualifiedName("Machinery Machine Identification")));
            Assert.That(units, Contains.Item(new QualifiedName("Machinery Monitoring")));
            Assert.That(
                units,
                Contains.Item(new QualifiedName("Machinery MachineryItem State")));
            Assert.That(units, Contains.Item(new QualifiedName("Machinery Operation Mode")));
            Assert.That(
                units,
                Has.No.Member(new QualifiedName("Machinery Job Management Base")),
                "A part that was not built must not be advertised.");
        }

        [Test]
        public void PartsNotConfiguredAreRefused()
        {
            IMachineBuilder<BaseObjectState> builder =
                m_context!.AddMachine(new QualifiedName("NoJobs"));

            ServiceResultException exception =
                Assert.Throws<ServiceResultException>(() => builder.WithJobManagement())!;
            Assert.That(exception.Message, Does.Contain("MachineryParts.Jobs"));
        }

        [Test]
        public async Task DuplicateMachineNamesAreRefused()
        {
            await BuildPressAsync();

            Assert.Throws<ServiceResultException>(
                () => m_context!.AddMachine(new QualifiedName("Press-1")));
        }

        private ushort MachineryNamespaceIndex =>
            (ushort)m_fixture!.Manager.Server.NamespaceUris.GetIndex(Namespaces.Machinery);

        private ValueTask<IMachineHandle<BaseObjectState>> BuildPressAsync()
        {
            return m_context!
                .AddMachine(new QualifiedName("Press-1"))
                .WithIdentification(id =>
                {
                    id.Manufacturer = new LocalizedText("Acme");
                    id.Model = new LocalizedText("P-500");
                    id.SerialNumber = "SN-0001";
                    id.ProductInstanceUri = "urn:acme:press:1";
                    id.DeviceClass = "Press";
                    id.YearOfConstruction = 2024;
                    id.MonthOfConstruction = 6;
                })
                .WithMonitoring(monitoring => monitoring
                    .WithMachineryItemState(MachineryItemStateValue.NotExecuting)
                    .WithOperationMode()
                    .WithHealth(health => health
                        .WithDeviceHealth(Opc.Ua.Di.DeviceHealthEnumeration.FAILURE))
                    .WithProcess())
                .WithNotifications()
                .WithOperationCounters(counters => counters
                    .WithPowerOnDuration(1234.5)
                    .WithOperationDuration(900.25))
                .BuildAsync();
        }

        [Test]
        public async Task HealthMaterialisesTheDeviceIntegrationChildrenAsync()
        {
            await BuildPressAsync();

            // The generated AddHealth runs the folder factory with
            // forInstance: true, which skips every optional child, so both
            // children have to be built by the builder. A bare folder is what
            // a Machinery client would find nothing in.
            BaseInstanceState monitoring = FindChild(
                m_context!.MachinesFolder.FindChild(
                    m_context.Context,
                    new QualifiedName("Press-1", m_context.InstanceNamespaceIndex))!,
                BrowseNames.Monitoring);
            BaseInstanceState health = FindChild(monitoring, BrowseNames.Health);

            var deviceHealth = FindChild(health, DiBrowseNames.DeviceHealth)
                as BaseVariableState;
            Assert.That(deviceHealth, Is.Not.Null);
            Assert.That(
                deviceHealth!.BrowseName.NamespaceIndex,
                Is.EqualTo(
                    (ushort)m_fixture!.Manager.Server.NamespaceUris.GetIndex(
                        Opc.Ua.Di.Namespaces.OpcUaDi)),
                "DeviceHealth belongs to the Device Integration namespace.");
            Assert.That(
                deviceHealth.DataType,
                Is.EqualTo(NodeId.Create(
                    Opc.Ua.Di.DataTypes.DeviceHealthEnumeration,
                    Opc.Ua.Di.Namespaces.OpcUaDi,
                    m_fixture.Manager.Server.NamespaceUris)));
            Assert.That(
                deviceHealth.WrappedValue.TryGetValue(
                    out Opc.Ua.Di.DeviceHealthEnumeration value),
                Is.True);
            Assert.That(value, Is.EqualTo(Opc.Ua.Di.DeviceHealthEnumeration.FAILURE));

            Assert.That(
                FindChild(health, DiBrowseNames.DeviceHealthAlarms),
                Is.InstanceOf<FolderState>());
        }

        [Test]
        public async Task EquipmentLifeIsMaterialisedFromTheInterfaceAsync()
        {
            IMachineHandle<BaseObjectState> machine = await m_context!
                .AddMachine(new QualifiedName("Tooled"))
                .WithMachineryEquipment(equipment => equipment.AddEquipment(
                    new QualifiedName("UpperDie"),
                    "urn:acme:equipment:die",
                    die => die.WithEquipmentLife(
                        remaining: 118_400,
                        startValue: 250_000,
                        warningValues: s_dieWarningValues.ToArrayOf())))
                .BuildAsync();

            BaseInstanceState folder = FindChild(
                machine.State,
                BrowseNames.MachineryEquipment);
            BaseInstanceState die = FindChild(folder, "UpperDie");

            // EquipmentLife is declared on IMachineryEquipmentType, an
            // interface: the placeholder factory never creates it, so its
            // absence would go unnoticed without this.
            var life = FindChild(die, BrowseNames.EquipmentLife)
                as Opc.Ua.Di.LifetimeVariableState;
            Assert.That(life, Is.Not.Null);
            Assert.That(life!.WrappedValue.TryGetValue(out double remaining), Is.True);
            Assert.That(remaining, Is.EqualTo(118_400.0));
            Assert.That(
                life.StartValue!.WrappedValue.TryGetValue(out double startValue),
                Is.True);
            Assert.That(startValue, Is.EqualTo(250_000.0));
            Assert.That(
                life.LimitValue!.WrappedValue.TryGetValue(out double limitValue),
                Is.True);
            Assert.That(
                limitValue,
                Is.Zero,
                "LimitValue is mandatory on LifetimeVariableType and defaults to zero.");
            Assert.That(life.WarningValues, Is.Not.Null);
            Assert.That(
                life.NodeId.NamespaceIndex,
                Is.EqualTo(m_context.InstanceNamespaceIndex));
        }

        private static readonly double[] s_dieWarningValues = [25_000.0];

        private BaseInstanceState FindChild(NodeState parent, string browseName)
        {
            var children = new List<BaseInstanceState>();
            parent.GetChildren(m_context!.Context, children);
            BaseInstanceState? match = children.Find(
                child => child.BrowseName.Name == browseName);
            Assert.That(match, Is.Not.Null, $"'{browseName}' is missing below '{parent.BrowseName}'.");
            return match!;
        }

        private MachineryServerFixture? m_fixture;
        private IMachineryBuildContext? m_context;
    }
}
