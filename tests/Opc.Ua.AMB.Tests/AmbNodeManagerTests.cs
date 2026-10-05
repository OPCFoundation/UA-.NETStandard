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
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.AMB.Server;
using Opc.Ua.AMB.Server.Assets;
using Opc.Ua.Di;
using Opc.Ua.Di.Server;
using Opc.Ua.Di.Server.Builders;
using Opc.Ua.Server;
using Opc.Ua.Server.TestFramework;

namespace Opc.Ua.AMB.Tests
{
    /// <summary>
    /// Tests the Asset Management Basics node manager in a hosted server: the
    /// model it loads, where its entry points hang, and the conformance units
    /// and facet it publishes.
    /// </summary>
    [TestFixture]
    [Category("AMB")]
    [Category("Hosting")]
    [NonParallelizable]
    public sealed class AmbNodeManagerTests
    {
        private static readonly QualifiedName s_assetIdentification = new("AMB Asset Identification");

        private static readonly QualifiedName s_configurableAssetIdentification =
            new("AMB Configurable Asset Identification");

        private static readonly QualifiedName s_discoveryByProductInstanceUri =
            new("AMB Asset Discovery by ProductInstanceUri");

        private static readonly QualifiedName s_discoveryByAssetId = new("AMB Asset Discovery by AssetId");

        private const string BaseServerFacet = "http://opcfoundation.org/UA-Profile/AMB/Server/BaseServer";

        private static readonly string[] s_deviceNames = ["Sensor", "Valve"];

        [Test]
        public async Task TheModelAndItsEntryPointsAreInTheAddressSpaceAsync()
        {
            await using AmbHostedServer server = await AmbHostedServer.StartAsync(
                nameof(TheModelAndItsEntryPointsAreInTheAddressSpaceAsync),
                builder => builder.AddOpcUaDi().AddAssetManagement()).ConfigureAwait(false);

            DataValue typeName = await server.ReadAsync(
                server.ToNodeId(ObjectTypeIds.IMaintenanceEventType),
                Attributes.BrowseName).ConfigureAwait(false);
            IReadOnlyList<ReferenceDescription> objects = await server.BrowseAsync(
                Ua.ObjectIds.ObjectsFolder,
                Ua.ReferenceTypeIds.Organizes).ConfigureAwait(false);
            IReadOnlyList<ReferenceDescription> aliases = await server.BrowseAsync(
                Ua.ObjectIds.Aliases,
                Ua.ReferenceTypeIds.Organizes).ConfigureAwait(false);
            IReadOnlyList<ReferenceDescription> locations = await server.BrowseAsync(
                Ua.ObjectIds.Locations,
                Ua.ReferenceTypeIds.Organizes).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(typeName.WrappedValue.TryGetValue(out QualifiedName name), Is.True);
                Assert.That(name.Name, Is.EqualTo(BrowseNames.IMaintenanceEventType));
                Assert.That(server.Manager.AmbNamespaceIndex, Is.Not.Zero);
                Assert.That(server.Manager.InstanceNamespaceIndex, Is.Not.EqualTo(server.Manager.AmbNamespaceIndex));
                Assert.That(
                    server.Server.NamespaceUris.GetString(server.Manager.InstanceNamespaceIndex),
                    Is.EqualTo(AmbServerOptions.DefaultInstanceNamespaceUri));

                // The base namespace's Locations object is in the
                // address space, and the AMB location entry points hang below it.
                Assert.That(Targets(server, objects), Does.Contain(Ua.ObjectIds.Locations), "0:Locations");
                Assert.That(
                    Targets(server, locations),
                    Is.SupersetOf(new[]
                    {
                        server.ToNodeId(ObjectIds.HierarchicalLocations),
                        server.ToNodeId(ObjectIds.OperationalLocations)
                    }));
                Assert.That(Targets(server, aliases), Does.Contain(server.ToNodeId(ObjectIds.Assets)));
                Assert.That(server.Server.Factory.ContainsEncodeableType(DataTypeIds.RootCauseDataType), Is.True);
            });
        }

        [Test]
        public async Task AServerWithoutAssetsClaimsNothingAsync()
        {
            await using AmbHostedServer server = await AmbHostedServer.StartAsync(
                nameof(AServerWithoutAssetsClaimsNothingAsync),
                builder => builder.AddOpcUaDi().AddAssetManagement()).ConfigureAwait(false);

            (ArrayOf<QualifiedName> units, ArrayOf<string> profiles) =
                await ReadPublishedAsync(server).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(server.Manager.ConformanceUnits.Count, Is.Zero);
                Assert.That(server.Manager.ServerProfiles.Count, Is.Zero);
                Assert.That(units.ToArray(), Does.Not.Contain(s_assetIdentification));
                Assert.That(profiles.ToArray(), Does.Not.Contain(BaseServerFacet));
            });
        }

        [Test]
        public async Task TheFacetIsPublishedWithItsMandatoryUnitAsync()
        {
            await using AmbHostedServer server = await AmbHostedServer.StartAsync(
                nameof(TheFacetIsPublishedWithItsMandatoryUnitAsync),
                builder => builder
                    .AddOpcUaDi()
                    .AddAssetManagement()
                    .ConfigureDevicesFor<DiNodeManager>(async context =>
                    {
                        IAssetManagement assets = context.GetRequiredService<IAssetManagement>();
                        foreach (string name in s_deviceNames)
                        {
                            IDeviceBuilder<DeviceState> device = await AssetRegistrationTests.CreateDeviceAsync(
                                context,
                                name,
                                "urn:acme:" + name).ConfigureAwait(false);
                            await device.RegisterAsAssetAsync(assets).ConfigureAwait(false);
                        }
                    })).ConfigureAwait(false);

            (ArrayOf<QualifiedName> units, ArrayOf<string> profiles) =
                await ReadPublishedAsync(server).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(
                    server.Manager.ConformanceUnits.ToArray(),
                    Is.EquivalentTo(new[]
                    {
                        s_assetIdentification,
                        s_discoveryByProductInstanceUri
                    }),
                    "no version information and, §8.2.3, no AssetId to be listed by");
                Assert.That(server.Manager.ServerProfiles.ToArray(), Is.EqualTo(new[] { BaseServerFacet }));
                Assert.That(units.ToArray(), Does.Contain(s_assetIdentification));
                Assert.That(units.ToArray(), Does.Not.Contain(s_configurableAssetIdentification),
                    "the AssetIds are not configurable");
                Assert.That(profiles.ToArray(), Does.Contain(BaseServerFacet));
            });
        }

        [Test]
        public async Task AConfigurableAssetIdCountsOnlyWhenEveryAssetHasOneAsync()
        {
            await using AmbHostedServer server = await AmbHostedServer.StartAsync(
                nameof(AConfigurableAssetIdCountsOnlyWhenEveryAssetHasOneAsync),
                builder => builder
                    .AddOpcUaDi()
                    .AddAssetManagement(options => options.UseFileSystemStores(System.IO.Path.Combine(
                        TestContext.CurrentContext.WorkDirectory,
                        nameof(AConfigurableAssetIdCountsOnlyWhenEveryAssetHasOneAsync),
                        Guid.NewGuid().ToString("N"))))
                    .ConfigureDevicesFor<DiNodeManager>(async context =>
                    {
                        IAssetManagement assets = context.GetRequiredService<IAssetManagement>();
                        IDeviceBuilder<DeviceState> sensor = await AssetRegistrationTests.CreateDeviceAsync(
                            context,
                            "Sensor",
                            "urn:acme:sensor").ConfigureAwait(false);
                        await sensor.RegisterAsAssetAsync(assets, asset => asset.WithConfigurableAssetId())
                            .ConfigureAwait(false);
                        IDeviceBuilder<DeviceState> valve = await AssetRegistrationTests.CreateDeviceAsync(
                            context,
                            "Valve",
                            "urn:acme:valve").ConfigureAwait(false);
                        await valve.RegisterAsAssetAsync(assets).ConfigureAwait(false);
                    })).ConfigureAwait(false);

            Assert.That(
                server.Manager.ConformanceUnits.ToArray(),
                Does.Not.Contain(s_configurableAssetIdentification),
                "the valve's AssetId is not configurable");

            IAssetHandle? valve = null;
            foreach (IAssetHandle asset in server.AssetManagement.Assets)
            {
                if (asset.BrowseName.Name == "Valve")
                {
                    valve = asset;
                }
            }
            await valve!.UnregisterAsync().ConfigureAwait(false);

            (ArrayOf<QualifiedName> units, _) = await ReadPublishedAsync(server).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(
                    server.Manager.ConformanceUnits.ToArray(),
                    Is.SupersetOf(new[] { s_assetIdentification, s_configurableAssetIdentification }));
                Assert.That(
                    units.ToArray(),
                    Does.Contain(s_configurableAssetIdentification),
                    "a change after startup is published again");
            });
        }

        [Test]
        public async Task AUnitTheAssetsNoLongerMeetIsWithdrawnAsync()
        {
            IAssetHandle? sensor = null;
            await using AmbHostedServer server = await AmbHostedServer.StartAsync(
                nameof(AUnitTheAssetsNoLongerMeetIsWithdrawnAsync),
                builder => builder
                    .AddOpcUaDi()
                    .AddAssetManagement()
                    .ConfigureDevicesFor<DiNodeManager>(async context =>
                    {
                        IDeviceBuilder<DeviceState> device = await AssetRegistrationTests.CreateDeviceAsync(
                            context,
                            "Sensor",
                            "urn:acme:sensor").ConfigureAwait(false);
                        sensor = await device.RegisterAsAssetAsync(context.GetRequiredService<IAssetManagement>())
                            .ConfigureAwait(false);
                    })).ConfigureAwait(false);
            (ArrayOf<QualifiedName> before, ArrayOf<string> profilesBefore) =
                await ReadPublishedAsync(server).ConfigureAwait(false);

            await sensor!.UnregisterAsync().ConfigureAwait(false);
            (ArrayOf<QualifiedName> after, ArrayOf<string> profilesAfter) =
                await ReadPublishedAsync(server).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(before.ToArray(), Does.Contain(s_assetIdentification));
                Assert.That(profilesBefore.ToArray(), Does.Contain(BaseServerFacet));
                Assert.That(after.ToArray(), Does.Not.Contain(s_assetIdentification), "no asset is left");
                Assert.That(after.ToArray(), Does.Not.Contain(s_discoveryByAssetId));
                Assert.That(profilesAfter.ToArray(), Does.Not.Contain(BaseServerFacet), "the facet is withdrawn too");
            });
        }

        [Test]
        public async Task AMemoryStoreIsNotPersistentEnoughAsync()
        {
            await using AmbHostedServer server = await AmbHostedServer.StartAsync(
                nameof(AMemoryStoreIsNotPersistentEnoughAsync),
                builder => builder
                    .AddOpcUaDi()
                    .AddAssetManagement()
                    .ConfigureDevicesFor<DiNodeManager>(async context =>
                    {
                        IDeviceBuilder<DeviceState> sensor = await AssetRegistrationTests.CreateDeviceAsync(
                            context,
                            "Sensor",
                            "urn:acme:sensor").ConfigureAwait(false);
                        await sensor.RegisterAsAssetAsync(
                            context.GetRequiredService<IAssetManagement>(),
                            asset => asset.WithConfigurableAssetId()).ConfigureAwait(false);
                    })).ConfigureAwait(false);

            Assert.That(
                server.Manager.ConformanceUnits.ToArray(),
                Does.Contain(s_assetIdentification).And.Not.Contain(s_configurableAssetIdentification),
                "AMB Configurable Asset Identification asks for persistent storage");
        }

        [Test]
        public async Task TheManagerWorksWithoutHostingAsync()
        {
            var assets = new AssetManagement();
            var fixture = new ServerFixture<StandardServer>(telemetry =>
            {
                var standard = new StandardServer(telemetry);
                standard.AddNodeManager(new AmbNodeManagerFactory(assets));
                return standard;
            })
            {
                AutoAccept = true,
                SecurityNone = true
            };
            StandardServer server = await fixture.StartAsync().ConfigureAwait(false);
            try
            {
                AmbNodeManager manager = assets.Manager!;
                Assert.Multiple(() =>
                {
                    Assert.That(manager, Is.Not.Null);
                    Assert.That(manager.Assets, Is.SameAs(assets));
                    Assert.That(
                        manager.FindPredefinedNode<NodeState>(ExpandedNodeId.ToNodeId(
                            ObjectTypeIds.DocumentationLinksType,
                            manager.Server.NamespaceUris)),
                        Is.Not.Null,
                        "the AMB model is loaded");
                    Assert.Throws<InvalidOperationException>(
                        () => _ = new AmbNodeManager(server.CurrentInstance, fixture.Config, assets),
                        "one registry serves one manager");
                });
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
            Assert.That(assets.Manager, Is.Null, "disposing the manager disconnects it");
        }

        private static HashSet<NodeId> Targets(AmbHostedServer server, IReadOnlyList<ReferenceDescription> references)
        {
            return [.. references.Select(reference => server.ToNodeId(reference.NodeId))];
        }

        private static async Task<(ArrayOf<QualifiedName> Units, ArrayOf<string> Profiles)> ReadPublishedAsync(
            AmbHostedServer server)
        {
            DataValue units = await server.ReadAsync(
                Ua.VariableIds.Server_ServerCapabilities_ConformanceUnits).ConfigureAwait(false);
            DataValue profiles = await server.ReadAsync(
                Ua.VariableIds.Server_ServerCapabilities_ServerProfileArray).ConfigureAwait(false);
            units.WrappedValue.TryGetValue(out ArrayOf<QualifiedName> unitValues);
            profiles.WrappedValue.TryGetValue(out ArrayOf<string> profileValues);
            return (unitValues, profileValues);
        }
    }
}
