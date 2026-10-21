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
using Opc.Ua.Machinery.Client;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Machinery.Server.Builders;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Tests;

namespace Opc.Ua.AutoID.Tests
{
    /// <summary>
    /// Reader placement in a hosted Machinery and AutoID server, read back over a real session:
    /// every reader stays reachable from the DI <c>DeviceSet</c> (OPC 10000-100 4.9), a reader can
    /// be an identifiable component of a machine (OPC 40001-1 11) and no reader becomes a machine.
    /// </summary>
    [TestFixture]
    [Category("AutoID")]
    [Category("Composition")]
    [NonParallelizable]
    public sealed class AutoIdPlacementEndToEndTests
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
                .AddAutoIdToMachinery()
                .ConfigureMachinery(async (machinery, ct) =>
                    m_press = await machinery
                        .AddMachine(new QualifiedName(PressName, machinery.InstanceNamespaceIndex))
                        .WithIdentification(identification =>
                        {
                            identification.Manufacturer = new LocalizedText("Contoso");
                            identification.SerialNumber = "PR-1";
                            identification.ProductInstanceUri = "urn:tests:autoid:placement:press:1";
                        })
                        .WithComponents(_ => { })
                        .BuildAsync(ct)
                        .ConfigureAwait(false))
                .ConfigureAutoIdFor<MachineryNodeManager>(async (context, ct) =>
                {
                    QualifiedName Name(string name) => new(name, context.Runtime.InstanceNamespaceIndex);
                    m_gate = await context.Runtime.CreateReaderAsync(Name(GateName), m_gateProvider, ct)
                        .ConfigureAwait(false);
                    m_antenna = await context.Runtime.Reader(Name(AntennaName), new FakeProvider())
                        .WithParent(m_gate.Device)
                        .BuildAsync(ct)
                        .ConfigureAwait(false);
                    m_infeed = await context.Runtime.Reader(Name(InfeedName), m_infeedProvider)
                        .AsComponentOf(m_press!.State)
                        .WithIdentification(nameplate =>
                        {
                            nameplate.Manufacturer = new LocalizedText("Contoso");
                            nameplate.SerialNumber = "R-100";
                            nameplate.ProductCode = "PORTAL-100";
                        })
                        .BuildAsync(ct)
                        .ConfigureAwait(false);
                    m_outfeed = await context.Runtime.Reader(Name(OutfeedName), new FakeProvider())
                        .WithParent(m_press.State)
                        .BuildAsync(ct)
                        .ConfigureAwait(false);
                    configured.TrySetResult(true);
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
            m_session = await AutoIdHostFixture.ConnectAsync(m_endpointUrl, "AutoIdPlacement").ConfigureAwait(false);
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
            m_gateProvider.Dispose();
            m_infeedProvider.Dispose();
        }

        [Test]
        public async Task TheDeviceSetReachesEveryReaderAndOrganizesOnlyTheRootsAsync()
        {
            NodeId deviceSet = ExpandedNodeId.ToNodeId(Di.ObjectIds.DeviceSet, m_session!.NamespaceUris);

            string[] devices = await BrowseNamesAsync(deviceSet).ConfigureAwait(false);
            string[] belowGate = await BrowseNamesAsync(m_gate!.NodeId).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(devices, Does.Contain(GateName), "a default reader");
                Assert.That(devices, Does.Contain(InfeedName), "a component of a machine stays a device");
                Assert.That(devices, Does.Contain(OutfeedName), "DeviceSet does not reach the machine");
                Assert.That(devices, Does.Not.Contain(AntennaName), "only the root of a composed device");
                Assert.That(belowGate, Does.Contain(AntennaName), "the composed device reaches its component");
                Assert.That(m_antenna!.Device.Parent, Is.SameAs(m_gate.Device));
                Assert.That(m_outfeed!.Device.Parent, Is.SameAs(m_press!.State));
            });
        }

        [Test]
        public async Task NoReaderBecomesAMachineAsync()
        {
            var machinery = new MachineryClient(m_session!, NUnitTelemetryContext.Create());
            NodeId machinesFolder = ExpandedNodeId.ToNodeId(Machinery.ObjectIds.Machines, m_session!.NamespaceUris);

            NodeId[] machines = (await machinery.DiscoverMachinesAsync().ConfigureAwait(false)).ToArray()!;
            string[] machineNames = await BrowseNamesAsync(machinesFolder).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(machines, Is.EqualTo(new[] { m_press!.NodeId }));
                Assert.That(machineNames, Does.Not.Contain(InfeedName).And.Not.Contain(OutfeedName)
                    .And.Not.Contain(GateName).And.Not.Contain(AntennaName));
            });
        }

        [Test]
        public async Task TheMachineListsTheReaderAsAnIdentifiableComponentAsync()
        {
            var machinery = new MachineryClient(m_session!, NUnitTelemetryContext.Create());

            var components = new List<MachineEntry>();
            await foreach (MachineEntry entry in machinery.EnumerateComponentsAsync(m_press!.NodeId))
            {
                components.Add(entry);
            }
            NodeId identification = await machinery.ResolveIdentificationAsync(m_infeed!.NodeId).ConfigureAwait(false);
            MachineIdentification? nameplate = await machinery.ReadIdentificationAsync(m_infeed.NodeId)
                .ConfigureAwait(false);
            BrowseResult addIn = await BrowseAsync(
                m_infeed.NodeId, Types.ReferenceTypeIds.HasAddIn, includeSubtypes: false).ConfigureAwait(false);
            BrowseResult properties = await BrowseAsync(
                    identification, Types.ReferenceTypeIds.HasProperty, includeSubtypes: false)
                .ConfigureAwait(false);
            NodeId[] referenced = [.. properties.References.ToArray()!
                .Select(reference => ExpandedNodeId.ToNodeId(reference.NodeId, m_session!.NamespaceUris))];

            Assert.Multiple(() =>
            {
                Assert.That(components.Select(entry => entry.NodeId), Is.EqualTo(new[] { m_infeed.NodeId }),
                    "only the reader made a component is listed, not the one placed below the machine");
                Assert.That(identification.IsNull, Is.False);
                Assert.That(addIn.References.ToArray()!.Select(reference => reference.BrowseName.Name),
                    Does.Contain("Identification"), "the add-in is referenced with HasAddIn");
                Assert.That(ExpandedNodeId.ToNodeId(
                        addIn.References.ToArray()!.Single(reference => reference.BrowseName.Name == "Identification")
                            .TypeDefinition,
                        m_session!.NamespaceUris),
                    Is.EqualTo(ExpandedNodeId.ToNodeId(
                        Machinery.ObjectTypeIds.MachineryComponentIdentificationType, m_session.NamespaceUris)));
                Assert.That(nameplate, Is.Not.Null);
                Assert.That(nameplate!.Manufacturer.Text, Is.EqualTo("Contoso"));
                Assert.That(nameplate.SerialNumber, Is.EqualTo("R-100"));
                Assert.That(nameplate.ProductCode, Is.EqualTo("PORTAL-100"));
                Assert.That(referenced, Does.Contain(m_infeed.Device.Manufacturer!.NodeId)
                    .And.Contain(m_infeed.Device.SerialNumber!.NodeId),
                    "the add-in references the reader's nameplate instead of copying it");
            });
        }

        [Test]
        public async Task TheAutoIdClientFindsEveryReaderInTheDeviceSetAsync()
        {
            var client = new AutoIdClient(m_session!, NUnitTelemetryContext.Create());

            var found = new List<AutoIdReaderEntry>();
            await foreach (AutoIdReaderEntry entry in client.EnumerateReadersAsync())
            {
                found.Add(entry);
            }
            AutoIdDeviceSnapshot infeed = await client.ReadDeviceAsync(
                found.Single(entry => entry.BrowseName.Name == InfeedName)).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(found.Select(entry => entry.BrowseName.Name),
                    Is.EquivalentTo(new[] { GateName, InfeedName, OutfeedName }));
                Assert.That(infeed.SerialNumber, Is.EqualTo("R-100"));
            });
        }

        private async Task<string[]> BrowseNamesAsync(NodeId root)
        {
            BrowseResult result = await BrowseAsync(
                root, ReferenceTypeIds.HierarchicalReferences, includeSubtypes: true).ConfigureAwait(false);
            return [.. result.References.ToArray()!.Select(reference => reference.BrowseName.Name!)];
        }

        private async Task<BrowseResult> BrowseAsync(NodeId root, NodeId referenceTypeId, bool includeSubtypes)
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
                        ReferenceTypeId = referenceTypeId,
                        IncludeSubtypes = includeSubtypes,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                ],
                CancellationToken.None).ConfigureAwait(false);
            BrowseResult result = response.Results[0];
            Assert.That(result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.Good));
            Assert.That(result.ContinuationPoint.IsNull || result.ContinuationPoint.IsEmpty, Is.True);
            return result;
        }

        private void ConfigureServer(OpcUaServerOptions options)
        {
            const string applicationName = "AutoIdPlacement";
            string testRoot = System.IO.Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "autoid-placement",
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

        private const string InstanceNamespaceUri = "urn:tests:autoid:placement:instances";
        private const string PressName = "Press1";
        private const string GateName = "Gate1";
        private const string AntennaName = "Gate1Antenna";
        private const string InfeedName = "Infeed";
        private const string OutfeedName = "Outfeed";

        private readonly InMemoryRfidReaderProvider m_gateProvider = new(TimeSpan.FromMilliseconds(5));
        private readonly InMemoryRfidReaderProvider m_infeedProvider = new(TimeSpan.FromMilliseconds(5));
        private ServiceProvider? m_provider;
        private IHostedService? m_host;
        private ManagedSession? m_session;
        private string m_endpointUrl = string.Empty;
        private IMachineHandle<BaseObjectState>? m_press;
        private AutoIdReader? m_gate;
        private AutoIdReader? m_antenna;
        private AutoIdReader? m_infeed;
        private AutoIdReader? m_outfeed;
    }
}
