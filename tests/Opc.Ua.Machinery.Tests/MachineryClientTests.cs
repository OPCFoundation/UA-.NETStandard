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
using System.Text;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.StateMachines;
using Opc.Ua.Machinery.Client;
using Opc.Ua.Machinery.Result;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Machinery.Server.Builders;
using Opc.Ua.Machinery.Server.Results;
using Opc.Ua.Machinery.Server.StateMachines;
using Opc.Ua.Tests;

namespace Opc.Ua.Machinery.Tests
{
    /// <summary>
    /// Drives <see cref="MachineryClient"/> against a real in-process address
    /// space, so the browse paths it resolves are the ones the server actually
    /// builds.
    /// </summary>
    [TestFixture]
    [Category("Machinery")]
    public sealed class MachineryClientTests
    {
        [SetUp]
        public async Task SetUpAsync()
        {
            m_fixture = new MachineryServerFixture();
            await m_fixture.StartAsync();

            m_machine = await m_fixture.CreateBuildContext()
                .AddMachine(new QualifiedName("Press-1"))
                .WithIdentification(id =>
                {
                    id.Manufacturer = new LocalizedText("Acme");
                    id.Model = new LocalizedText("P-500");
                    id.SerialNumber = "SN-0001";
                    id.ProductInstanceUri = "urn:acme:press:1";
                    id.Location = "Hall 3";
                    id.YearOfConstruction = 2024;
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
                .WithResultManagement(results => results
                    .WithInMemoryStore()
                    .WithFileTransfer())
                .BuildAsync();

            m_session = MachineryInProcessSessionBridge.Build(m_fixture);
            m_client = new MachineryClient(
                m_session.Object,
                NUnitTelemetryContext.Create());
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
        public async Task ProcessValuesOfAVendorSubtypeAreFoundAsync()
        {
            IProcessValueHandle? standard = null;
            IProcessValueHandle? vendor = null;
            IMachineHandle<BaseObjectState> machine = await m_fixture!.CreateBuildContext()
                .AddMachine(new QualifiedName("Oven-1"))
                .WithIdentification(id =>
                {
                    id.Manufacturer = new LocalizedText("Acme");
                    id.SerialNumber = "OV-1";
                    id.ProductInstanceUri = "urn:acme:oven:1";
                })
                .WithProcessValue(new QualifiedName("Pressure"), pv => pv.WithValue(1).Bind(out standard))
                .WithProcessValue(new QualifiedName("Temperature"), pv => pv.WithValue(2).Bind(out vendor))
                .BuildAsync();

            // A vendor model derives its own process value type (OPC 40001-2
            // allows it); the instance carries that subtype as its type.
            ushort processValueNamespace = (ushort)m_fixture.Manager.Server.NamespaceUris.GetIndex(
                Opc.Ua.Machinery.ProcessValues.Namespaces.MachineryProcessValues);
            var processValueType = new NodeId(
                Opc.Ua.Machinery.ProcessValues.ObjectTypes.ProcessValueType,
                processValueNamespace);
            var vendorType = new NodeId("AcmeTemperatureValueType", vendor!.NodeId.NamespaceIndex);
            m_fixture.Manager.Server.TypeTree.AddSubtype(vendorType, processValueType);
            vendor.State.TypeDefinitionId = vendorType;

            var found = new List<MachineEntry>();
            await foreach (MachineEntry entry in m_client!.EnumerateProcessValuesAsync(machine.NodeId))
            {
                found.Add(entry);
            }

            Assert.That(found.ConvertAll(entry => entry.NodeId), Is.EquivalentTo(new[] { standard!.NodeId, vendor.NodeId }));
            Assert.That(found.Find(entry => entry.NodeId == vendor.NodeId)!.TypeDefinitionId, Is.EqualTo(vendorType));
        }

        [Test]
        public void MachinesFolderResolvesFromTheMachineryNamespace()
        {
            Assert.That(m_client!.MachinesFolderId.IsNull, Is.False);
            Assert.That(
                m_client.MachinesFolderId.TryGetValue(out uint identifier) &&
                    identifier == Objects.Machines,
                Is.True);
        }

        [Test]
        public async Task ItEnumeratesTheMachinesBelowTheFolder()
        {
            var machines = new List<MachineEntry>();
            await foreach (MachineEntry entry in m_client!.EnumerateMachinesAsync())
            {
                machines.Add(entry);
            }

            Assert.That(machines, Has.Count.EqualTo(1));
            Assert.That(machines[0].BrowseName.Name, Is.EqualTo("Press-1"));
            Assert.That(machines[0].NodeId, Is.EqualTo(m_machine!.NodeId));
        }

        [Test]
        public async Task ItReadsTheIdentificationAddIn()
        {
            MachineIdentification? identification = await m_client!
                .ReadIdentificationAsync(m_machine!.NodeId);

            Assert.That(identification, Is.Not.Null);
            Assert.That(identification!.Manufacturer.Text, Is.EqualTo("Acme"));
            Assert.That(identification.Model.Text, Is.EqualTo("P-500"));
            Assert.That(identification.SerialNumber, Is.EqualTo("SN-0001"));
            Assert.That(identification.ProductInstanceUri, Is.EqualTo("urn:acme:press:1"));
            Assert.That(identification.Location, Is.EqualTo("Hall 3"));
            Assert.That(identification.YearOfConstruction, Is.EqualTo(2024));
        }

        [Test]
        public async Task ItEnumeratesComponents()
        {
            var components = new List<MachineEntry>();
            await foreach (MachineEntry entry in m_client!
                .EnumerateComponentsAsync(m_machine!.NodeId))
            {
                components.Add(entry);
            }

            Assert.That(components, Has.Count.EqualTo(1));
            Assert.That(components[0].BrowseName.Name, Is.EqualTo("Ram"));
        }

        [Test]
        public async Task ItReadsBothStateMachines()
        {
            FiniteStateSnapshot? state = await m_client!.GetItemStateAsync(m_machine!.NodeId);
            FiniteStateSnapshot? mode = await m_client.GetOperationModeAsync(m_machine.NodeId);

            Assert.That(state, Is.Not.Null);
            Assert.That(state!.CurrentState.Text, Is.EqualTo("NotExecuting"));
            Assert.That(mode, Is.Not.Null);
            Assert.That(mode!.CurrentState.Text, Is.EqualTo("Setup"));
        }

        [Test]
        public async Task ItSeesAServerDrivenTransition()
        {
            await m_machine!.ItemState!.SetStateAsync(MachineryItemStateValue.Executing);

            FiniteStateSnapshot? state = await m_client!.GetItemStateAsync(m_machine.NodeId);
            Assert.That(state!.CurrentState.Text, Is.EqualTo("Executing"));
            Assert.That(
                state.LastTransition.Text,
                Is.EqualTo("FromNotExecutingToExecuting"));
        }

        [Test]
        public async Task ItDownloadsAPublishedResult()
        {
            byte[] payload = Encoding.UTF8.GetBytes("client-payload");
            await m_machine!.Results!.PublishAsync(
                new MachineryResult(
                    new ResultDataType
                    {
                        ResultMetaData = new ResultMetaDataType { ResultId = "C-1" }
                    },
                    new ByteString(payload)));

            ByteString downloaded = await m_client!
                .DownloadResultAsync(m_machine.NodeId, "C-1");

            Assert.That(downloaded.Span.ToArray(), Is.EqualTo(payload));
        }

        [Test]
        public void DownloadingAnUnknownResultFails()
        {
            Assert.ThrowsAsync<ServiceResultException>(
                async () => await m_client!.DownloadResultAsync(m_machine!.NodeId, "missing"));
        }

        [Test]
        public async Task AbsentBlocksAreReportedAsNullRatherThanThrowing()
        {
            IMachineHandle<BaseObjectState> bare = await m_fixture!.CreateBuildContext()
                .AddMachine(new QualifiedName("Bare"))
                .BuildAsync();

            Assert.That(await m_client!.ReadIdentificationAsync(bare.NodeId), Is.Null);
            Assert.That(await m_client.GetItemStateAsync(bare.NodeId), Is.Null);
            Assert.That(await m_client.GetOperationModeAsync(bare.NodeId), Is.Null);
            Assert.That(await m_client.ResultManagementAsync(bare.NodeId), Is.Null);
            Assert.That(await m_client.JobManagementAsync(bare.NodeId), Is.Null);

            var itemSnapshots = new List<FiniteStateSnapshot>();
            await foreach (FiniteStateSnapshot snapshot in m_client.ObserveItemStateAsync(
                bare.NodeId))
            {
                itemSnapshots.Add(snapshot);
            }
            Assert.That(itemSnapshots, Is.Empty);

            var modeSnapshots = new List<FiniteStateSnapshot>();
            await foreach (FiniteStateSnapshot snapshot in m_client.ObserveOperationModeAsync(
                bare.NodeId))
            {
                modeSnapshots.Add(snapshot);
            }
            Assert.That(modeSnapshots, Is.Empty);
        }

        [Test]
        public async Task OptionalPartsTheServerDoesNotLoadAreReportedAsAbsentAsync()
        {
            await using var fixture = new MachineryServerFixture(MachineryParts.BuildingBlocks);
            await fixture.StartAsync().ConfigureAwait(false);
            IMachineHandle<BaseObjectState> bare = await fixture.CreateBuildContext()
                .AddMachine(new QualifiedName("BuildingBlocksOnly"))
                .BuildAsync()
                .ConfigureAwait(false);
            Mock<ISession> session = MachineryInProcessSessionBridge.Build(fixture);
            var client = new MachineryClient(session.Object, NUnitTelemetryContext.Create());

            NamespaceTable namespaceUris = fixture.Manager.Server.NamespaceUris;
            Assert.That(
                namespaceUris.GetIndex(Opc.Ua.Machinery.Jobs.Namespaces.MachineryJobs),
                Is.LessThan(0),
                "The server must not publish the Jobs model for this test to mean anything.");
            Assert.That(
                namespaceUris.GetIndex(Opc.Ua.Machinery.Result.Namespaces.MachineryResult),
                Is.LessThan(0));

            Assert.That(await client.JobManagementAsync(bare.NodeId).ConfigureAwait(false), Is.Null);
            Assert.That((await client.ResolveResultManagementAsync(bare.NodeId).ConfigureAwait(false)).IsNull, Is.True);
            Assert.That(await client.ResultManagementAsync(bare.NodeId).ConfigureAwait(false), Is.Null);
            Assert.That((await client.ReadJobOrdersAsync(bare.NodeId).ConfigureAwait(false)).Count, Is.Zero);
            Assert.That((await client.ReadJobResponsesAsync(bare.NodeId).ConfigureAwait(false)).Count, Is.Zero);
        }

        [Test]
        public void DownloadResultRequiresAResultId()
        {
            Assert.ThrowsAsync<ArgumentException>(
                async () => await m_client!.DownloadResultAsync(m_machine!.NodeId, string.Empty));
        }

        [Test]
        public async Task JobReadersReturnNothingForAMachineWithoutJobManagementAsync()
        {
            NodeId machine = m_machine!.NodeId;
            Assert.That((await m_client!.ReadJobOrdersAsync(machine)).Count, Is.Zero);
            Assert.That((await m_client.ReadJobResponsesAsync(machine)).Count, Is.Zero);
            Assert.That(await m_client.ReadJobOrderParametersAsync(machine, "J-1"), Is.Null);
            Assert.That(await m_client.ReadJobResponseParametersAsync(machine, "J-1"), Is.Null);

            Assert.ThrowsAsync<ArgumentException>(
                async () => await m_client.ReadJobOrderParametersAsync(machine, string.Empty));
            Assert.ThrowsAsync<ArgumentException>(
                async () => await m_client.ReadJobResponseParametersAsync(machine, string.Empty));
        }

        [Test]
        public async Task EnergyReadersReturnNothingWhereNothingIsMeteredAsync()
        {
            // The in-process bridge browses children regardless of the
            // reference type, so following Contains itself is covered over
            // the wire by MachineryEndToEndTests; here only the absent cases.
            var readings = new List<MachineryMeteringPoint>();
            await foreach (MachineryMeteringPoint reading in m_client!.ReadSubMetersAsync(NodeId.Null))
            {
                readings.Add(reading);
            }
            Assert.That(readings, Is.Empty);

            var points = new List<MachineEntry>();
            await foreach (MachineEntry entry in m_client.EnumerateMeteringPointsAsync(NodeId.Null))
            {
                points.Add(entry);
            }
            Assert.That(points, Is.Empty);
        }

        [Test]
        public void SessionMachineryExtensionBuildsAClient()
        {
            MachineryClient client = m_session!.Object.Machinery(NUnitTelemetryContext.Create());
            Assert.That(client.MachinesFolderId.IsNull, Is.False);

            Assert.Throws<ArgumentNullException>(
                () => SessionMachineryExtensions.Machinery(
                    null!,
                    NUnitTelemetryContext.Create()));
            Assert.Throws<ArgumentNullException>(
                () => m_session.Object.Machinery(null!));
        }

        private MachineryServerFixture? m_fixture;
        private Mock<ISession>? m_session;
        private MachineryClient? m_client;
        private IMachineHandle<BaseObjectState>? m_machine;
    }
}
