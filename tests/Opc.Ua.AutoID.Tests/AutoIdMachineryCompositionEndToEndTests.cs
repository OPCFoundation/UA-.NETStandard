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
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using Opc.Ua.AutoID.Client;
using Opc.Ua.AutoID.Server;
using Opc.Ua.Client;
using Opc.Ua.Di.Server;
using Opc.Ua.Machinery.Client;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Machinery.Server.Builders;
using Opc.Ua.Pumps.Client;
using Opc.Ua.Pumps.Server;
using Opc.Ua.Pumps.Server.Builders;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Tests;

namespace Opc.Ua.AutoID.Tests
{
    /// <summary>
    /// One hosted server composes Machinery, Pumps and AutoID on the Machinery node manager and is
    /// read back over a real session by the AutoID, Machinery and Pumps clients: one <c>DeviceSet</c>
    /// with the readers next to the pump, a machine in the <c>Machines</c> folder, callable AutoID
    /// methods and the capabilities of all companions.
    /// </summary>
    [TestFixture]
    [Category("AutoID")]
    [Category("Composition")]
    [NonParallelizable]
    public sealed class AutoIdMachineryCompositionEndToEndTests
    {
        [OneTimeSetUp]
        public async Task OneTimeSetUpAsync()
        {
            var configured = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOpcUa()
                .AddServer<StandardServer>(ConfigureServer)
                .AddMachinery(options =>
                {
                    options.Parts = MachineryParts.BuildingBlocks;
                    options.InstanceNamespaceUri = InstanceNamespaceUri;
                })
                .AddPumpsToMachinery()
                .AddAutoIdModel(options => options.MaximumScanDuration = TimeSpan.FromSeconds(5))
                .ConfigureMachinery(async (machinery, ct) =>
                    m_press = await machinery
                        .AddMachine(new QualifiedName(PressName, machinery.InstanceNamespaceIndex))
                        .WithIdentification(identification =>
                        {
                            identification.Manufacturer = new LocalizedText("Contoso");
                            identification.SerialNumber = "PR-1";
                            identification.ProductInstanceUri = "urn:tests:autoid:composition:press:1";
                        })
                        .BuildAsync(ct)
                        .ConfigureAwait(false))
                .ConfigurePumpsFor<MachineryNodeManager>(async (context, ct) =>
                    m_pump = await context.Runtime
                        .CreatePumpAsync(new QualifiedName(PumpName, context.Runtime.InstanceNamespaceIndex), ct)
                        .ConfigureAwait(false))
                .ConfigureAutoIdFor<MachineryNodeManager>(async (context, ct) =>
                {
                    m_runtime = context.Runtime;
                    m_owner = context.Manager;
                    QualifiedName Name(string name) => new(name, context.Runtime.InstanceNamespaceIndex);
                    m_gate = await context.Runtime.Reader(Name(GateName), m_rfid)
                        .WithDeviceInfo("Press infeed")
                        .BuildAsync(ct)
                        .ConfigureAwait(false);
                    await context.Runtime.CreateReaderAsync(Name(OpticalName), m_optical, ct).ConfigureAwait(false);
                })
                .ConfigureAutoIdFor<MachineryNodeManager>((context, _) =>
                {
                    m_sharedRuntime = ReferenceEquals(context.Runtime, m_runtime);
                    configured.TrySetResult(true);
                    return default;
                });

            m_provider = services.BuildServiceProvider();
            m_host = m_provider.GetServices<IHostedService>().Single();
            await m_host.StartAsync(CancellationToken.None).ConfigureAwait(false);
            for (int ii = 0; ii < 300 && !configured.Task.IsCompleted; ii++)
            {
                if (m_host is BackgroundService { ExecuteTask: { IsFaulted: true } startup })
                {
                    await startup.ConfigureAwait(false);
                }
                await Task.Delay(100).ConfigureAwait(false);
            }
            Assert.That(configured.Task.IsCompleted, Is.True, "the companion configurators completed");
            m_session = await AutoIdHostFixture.ConnectAsync(m_endpointUrl, "AutoIdComposition").ConfigureAwait(false);
            m_client = new AutoIdClient(m_session, NUnitTelemetryContext.Create());
        }

        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
            if (m_session != null)
            {
                await m_session.DisposeAsync().ConfigureAwait(false);
            }
            if (m_host != null)
            {
                await m_host.StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
            if (m_provider != null)
            {
                await m_provider.DisposeAsync().ConfigureAwait(false);
            }
            m_rfid.Dispose();
            m_optical.Dispose();
        }

        [Test]
        public void OneOwnerHostsTheMachineThePumpAndTheReaders()
        {
            var owner = (MachineryNodeManager)m_owner!;
            IAsyncNodeManager[] managers = [.. owner.Server.NodeManager.AsyncNodeManagers];

            Assert.Multiple(() =>
            {
                Assert.That(managers.Count(manager => manager is DiNodeManager), Is.EqualTo(1),
                    "one node manager owns the Device Integration address space");
                Assert.That(managers.OfType<AutoIdNodeManager>(), Is.Empty);
                Assert.That(m_sharedRuntime, Is.True, "successive configurators share one runtime");
                Assert.That(m_runtime!.Readers.Count, Is.EqualTo(2));
                Assert.That(m_gate!.NodeId.NamespaceIndex, Is.EqualTo(owner.MachineryInstanceNamespaceIndex));
                Assert.That(m_pump!.NodeId.NamespaceIndex, Is.EqualTo(owner.MachineryInstanceNamespaceIndex));
                Assert.That(m_press!.NodeId.NamespaceIndex, Is.EqualTo(owner.MachineryInstanceNamespaceIndex));
                Assert.That(owner.Server.NamespaceUris.ToArray().Count(uri => uri == Di.Namespaces.OpcUaDi),
                    Is.EqualTo(1));
            });
        }

        [Test]
        public async Task TheReadersShareTheDeviceSetWithThePumpAndStayOutOfTheMachinesFolderAsync()
        {
            NodeId deviceSet = ExpandedNodeId.ToNodeId(Di.ObjectIds.DeviceSet, m_session!.NamespaceUris);
            NodeId machines = ExpandedNodeId.ToNodeId(Machinery.ObjectIds.Machines, m_session.NamespaceUris);

            string[] objects = await BrowseNamesAsync(Opc.Ua.ObjectIds.ObjectsFolder).ConfigureAwait(false);
            string[] devices = await BrowseNamesAsync(deviceSet).ConfigureAwait(false);
            string[] machineNames = await BrowseNamesAsync(machines).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(objects.Count(name => name == "DeviceSet"), Is.EqualTo(1),
                    "Device Integration is loaded once");
                Assert.That(devices, Does.Contain(GateName).And.Contain(OpticalName).And.Contain(PumpName));
                Assert.That(machineNames, Does.Contain(PressName).And.Contain(PumpName));
                Assert.That(machineNames, Does.Not.Contain(GateName).And.Not.Contain(OpticalName));
            });
        }

        [Test]
        public async Task TheAutoIdClientFindsAndScansTheComposedReadersAsync()
        {
            var found = new List<AutoIdReaderEntry>();
            await foreach (AutoIdReaderEntry entry in m_client!.EnumerateReadersAsync())
            {
                found.Add(entry);
            }
            AutoIdReaderEntry gate = found.Single(entry => entry.BrowseName.Name == GateName);
            ByteString epc = AutoIdTestData.Bytes(0xE2, 0x03, 0x03, 0x03);
            await m_rfid.PlaceTagAsync(new InMemoryRfidTag(epc) { Antenna = 1 }).ConfigureAwait(false);
            try
            {
                AutoIdScanResponse response = await m_client.ScanAsync(
                    gate, AutoIdTestData.Settings(duration: 500, dataAvailable: true)).ConfigureAwait(false);
                AutoIdDeviceSnapshot snapshot = await m_client.ReadDeviceAsync(gate).ConfigureAwait(false);

                Assert.Multiple(() =>
                {
                    Assert.That(
                        found.ToDictionary(entry => entry.BrowseName.Name!, entry => entry.Kind),
                        Is.EquivalentTo(new Dictionary<string, AutoIdReaderKind>
                        {
                            [GateName] = AutoIdReaderKind.Rfid,
                            [OpticalName] = AutoIdReaderKind.Optical
                        }));
                    Assert.That(response.Status, Is.EqualTo(AutoIdOperationStatusEnumeration.SUCCESS));
                    Assert.That(response.Results.ToArray()!.OfType<RfidScanResult>()
                        .Any(result => result.ScanData.Epc.UId == epc), Is.True);
                    Assert.That(snapshot.DeviceInfo, Is.EqualTo("Press infeed"));
                    Assert.That(snapshot.LastScanStatus, Is.EqualTo(StatusCodes.Good));
                });
            }
            finally
            {
                await m_rfid.RemoveTagAsync(epc).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task TheMachineryAndPumpsClientsStillFindTheirInstancesAsync()
        {
            var machinery = new MachineryClient(m_session!, NUnitTelemetryContext.Create());
            var pumps = new PumpsClient(m_session!, NUnitTelemetryContext.Create());

            NodeId[] machines = (await machinery.DiscoverMachinesAsync().ConfigureAwait(false)).ToArray()!;
            NodeId[] discoveredPumps = (await pumps.DiscoverPumpsAsync().ConfigureAwait(false)).ToArray()!;

            Assert.Multiple(() =>
            {
                Assert.That(machines, Does.Contain(m_press!.NodeId).And.Contain(m_pump!.NodeId));
                Assert.That(machines, Does.Not.Contain(m_gate!.NodeId));
                Assert.That(discoveredPumps, Is.EqualTo(new[] { m_pump.NodeId }));
            });
        }

        [Test]
        public async Task TheServerAdvertisesTheCapabilitiesOfEveryComposedCompanionAsync()
        {
            ManagedSession session = m_session!;
            DataValue profiles = await session.ReadValueAsync(
                Opc.Ua.VariableIds.Server_ServerCapabilities_ServerProfileArray).ConfigureAwait(false);
            DataValue units = await session.ReadValueAsync(
                Opc.Ua.VariableIds.Server_ServerCapabilities_ConformanceUnits).ConfigureAwait(false);
            Assert.That(profiles.WrappedValue.TryGetValue(out ArrayOf<string> profileArray), Is.True);
            Assert.That(units.WrappedValue.TryGetValue(out ArrayOf<QualifiedName> unitArray), Is.True);
            ushort autoId = (ushort)session.NamespaceUris.GetIndex(AutoIdModel.NamespaceUri);
            string[] published = profileArray.ToArray()!;

            Assert.Multiple(() =>
            {
                Assert.That(published, Does.Contain(AutoIdProfiles.SimpleSyncServer)
                    .And.Contain(AutoIdProfiles.BaseSyncServer)
                    .And.Contain(AutoIdProfiles.BaseAsyncServer)
                    .And.Not.Contain(AutoIdProfiles.FullServer));
                Assert.That(unitArray.ToArray(), Does.Contain(new QualifiedName(AutoIdProfiles.DeviceTypeUnit, autoId))
                    .And.Contain(new QualifiedName(AutoIdProfiles.SyncAccessUnit, autoId))
                    .And.Contain(new QualifiedName("Machinery Find Machines"))
                    .And.Contain(new QualifiedName("DI DeviceTopology")));
                Assert.That(m_owner!.ServerProfiles.ToArray(), Is.SubsetOf(published));
            });
        }

        private async Task<string[]> BrowseNamesAsync(NodeId root)
        {
            BrowseResponse response = await m_session!.BrowseAsync(
                null,
                null,
                0,
                [
                    new BrowseDescription
                    {
                        NodeId = root,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                ],
                CancellationToken.None).ConfigureAwait(false);
            BrowseResult result = response.Results[0];
            Assert.That(result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.Good));
            Assert.That(result.ContinuationPoint.IsNull || result.ContinuationPoint.IsEmpty, Is.True);
            return [.. result.References.ToArray()!.Select(reference => reference.BrowseName.Name!)];
        }

        private void ConfigureServer(OpcUaServerOptions options)
        {
            const string applicationName = "AutoIdComposition";
            string testRoot = System.IO.Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "autoid-composition",
                Guid.NewGuid().ToString("N")[..8]);
            options.ApplicationName = applicationName;
            options.ApplicationUri = "urn:localhost:" + applicationName;
            options.ProductUri = "urn:localhost:" + applicationName + ":product";
            options.PkiRoot = System.IO.Path.Combine(testRoot, "pki");
            options.AutoAcceptUntrustedCertificates = true;
            options.IncludeUnsecurePolicyNone = true;
            options.EndpointUrls.Clear();
            m_endpointUrl = "opc.tcp://localhost:" +
                AutoIdHostFixture.GetAvailablePort().ToString(CultureInfo.InvariantCulture) +
                "/" +
                applicationName;
            options.EndpointUrls.Add(m_endpointUrl);
        }

        private const string InstanceNamespaceUri = "urn:tests:autoid:composition:instances";
        private const string PressName = "Press1";
        private const string PumpName = "FeedPump";
        private const string GateName = "Gate1";
        private const string OpticalName = "Optical1";

        private readonly InMemoryRfidReaderProvider m_rfid = new(TimeSpan.FromMilliseconds(5));
        private readonly InMemoryScanReaderProvider m_optical =
            new(AutoIdReaderKind.Optical, TimeSpan.FromMilliseconds(5));
        private ServiceProvider? m_provider;
        private IHostedService? m_host;
        private ManagedSession? m_session;
        private AutoIdClient? m_client;
        private string m_endpointUrl = string.Empty;
        private AutoIdRuntime? m_runtime;
        private DiNodeManager? m_owner;
        private AutoIdReader? m_gate;
        private IPumpBuilder? m_pump;
        private IMachineHandle<BaseObjectState>? m_press;
        private bool m_sharedRuntime;
    }
}
