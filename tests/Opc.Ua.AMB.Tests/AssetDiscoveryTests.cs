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
using Opc.Ua.Server.Fluent;
using Opc.Ua.Server.NodeManager;

namespace Opc.Ua.AMB.Tests
{
    /// <summary>
    /// Discovers assets through the AMB alias categories of a hosted server
    /// (OPC 10000-110 §8.2).
    /// </summary>
    [TestFixture]
    [Category("AMB")]
    [Category("Hosting")]
    [NonParallelizable]
    public sealed class AssetDiscoveryTests
    {
        private const string SensorUri = "urn:acme:sensor:4711";
        private const string ValveUri = "urn:acme:valve:12";
        private const string PumpUri = "urn:acme:pump:3";

        private static readonly string[] s_twins = ["Left", "Right"];

        [Test]
        public async Task FindAliasFindsTheAssetsOfEachCategoryAsync()
        {
            await using Fleet fleet = await Fleet.StartAsync(nameof(FindAliasFindsTheAssetsOfEachCategoryAsync))
                .ConfigureAwait(false);
            AmbHostedServer server = fleet.Server;
            ushort amb = server.Manager.AmbNamespaceIndex;

            ArrayOf<AliasNameDataType> byUri = await server
                .FindAliasAsync(ObjectIds.AssetsByProductInstanceUri, SensorUri).ConfigureAwait(false);
            ArrayOf<AliasNameDataType> allByUri = await server
                .FindAliasAsync(ObjectIds.AssetsByProductInstanceUri, "urn:acme:%").ConfigureAwait(false);
            ArrayOf<AliasNameDataType> byId = await server
                .FindAliasAsync(ObjectIds.AssetsByAssetId, "S-1").ConfigureAwait(false);
            ArrayOf<AliasNameDataType> unassigned = await server
                .FindAliasAsync(ObjectIds.AssetsByAssetId, AmbBrowseNames.NoAssetIdAssigned).ConfigureAwait(false);
            ArrayOf<AliasNameDataType> everything = await server
                .FindAliasAsync(ObjectIds.Assets, "%").ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(byUri.Count, Is.EqualTo(1));
                Assert.That(byUri[0].AliasName, Is.EqualTo(new QualifiedName(SensorUri, amb)));
                Assert.That(Targets(server, byUri[0]), Is.EqualTo(new[] { fleet.Sensor }));
                Assert.That(allByUri.Count, Is.EqualTo(3));
                Assert.That(byId.Count, Is.EqualTo(1));
                Assert.That(Targets(server, byId[0]), Is.EqualTo(new[] { fleet.Sensor }));

                // §8.2.3: the valve has an AssetId without a value; the pump
                // has none and is not listed by AssetId at all.
                Assert.That(unassigned.Count, Is.EqualTo(1));
                Assert.That(Targets(server, unassigned[0]), Is.EqualTo(new[] { fleet.Valve }));

                // Assets searches both subcategories.
                Assert.That(everything.Count, Is.EqualTo(5));
            });
        }

        [Test]
        public async Task FindAliasValidatesItsArgumentsAsync()
        {
            await using Fleet fleet = await Fleet.StartAsync(nameof(FindAliasValidatesItsArgumentsAsync))
                .ConfigureAwait(false);

            CallMethodResult result = await fleet.Server.CallFindAliasAsync(
                ObjectIds.AssetsByProductInstanceUri,
                "%",
                Ua.ReferenceTypeIds.HasComponent).ConfigureAwait(false);
            CallMethodResult invalidPattern = await fleet.Server.CallFindAliasAsync(
                ObjectIds.AssetsByProductInstanceUri,
                "urn:[",
                NodeId.Null).ConfigureAwait(false);
            ArrayOf<AliasNameDataType> empty = await fleet.Server
                .FindAliasAsync(ObjectIds.AssetsByProductInstanceUri, string.Empty).ConfigureAwait(false);
            ArrayOf<AliasNameDataType> aliasFor = await fleet.Server
                .FindAliasAsync(ObjectIds.AssetsByProductInstanceUri, "%").ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidArgument));
                Assert.That(invalidPattern.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidArgument));
                Assert.That(empty.Count, Is.Zero, "an empty pattern is valid and matches nothing");
                Assert.That(aliasFor.Count, Is.EqualTo(3));
            });
        }

        [Test]
        public async Task OnlyAssetsWithAnAssetIdAreListedByItAsync()
        {
            await using Fleet fleet = await Fleet.StartAsync(nameof(OnlyAssetsWithAnAssetIdAreListedByItAsync))
                .ConfigureAwait(false);
            AmbHostedServer server = fleet.Server;

            IReadOnlyList<ReferenceDescription> aliases = await server.BrowseAsync(
                server.ToNodeId(ObjectIds.AssetsByAssetId),
                Ua.ReferenceTypeIds.Organizes).ConfigureAwait(false);
            ReferenceDescription[] unassigned =
                [.. aliases.Where(alias => alias.BrowseName.Name == AmbBrowseNames.NoAssetIdAssigned)];
            IReadOnlyList<ReferenceDescription> aliasFor = await server.BrowseAsync(
                server.ToNodeId(unassigned[0].NodeId),
                Ua.ReferenceTypeIds.AliasFor).ConfigureAwait(false);
            IReadOnlyList<ReferenceDescription> sensorAliases = await server.BrowseAsync(
                fleet.Sensor,
                Ua.ReferenceTypeIds.AliasFor,
                BrowseDirection.Inverse).ConfigureAwait(false);
            IReadOnlyList<ReferenceDescription> pumpAliases = await server.BrowseAsync(
                fleet.Pump,
                Ua.ReferenceTypeIds.AliasFor,
                BrowseDirection.Inverse).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(aliases, Has.Count.EqualTo(2), "S-1 and NoAssetIdAssigned");
                Assert.That(unassigned, Has.Length.EqualTo(1));
                Assert.That(unassigned[0].TypeDefinition, Is.EqualTo((ExpandedNodeId)Ua.ObjectTypeIds.AliasNameType));
                Assert.That(
                    aliasFor.Select(reference => server.ToNodeId(reference.NodeId)),
                    Is.EqualTo(new[] { fleet.Valve }));
                Assert.That(sensorAliases, Has.Count.EqualTo(2), "the sensor has an alias in each category");
                Assert.That(pumpAliases, Has.Count.EqualTo(1), "§8.2.3: the pump has no AssetId");
                Assert.That(
                    server.Manager.ConformanceUnits.ToArray(),
                    Does.Contain(new QualifiedName("AMB Asset Discovery by AssetId")),
                    "every asset with an AssetId is listed");
            });
        }

        [Test]
        public async Task AssetsThatShareANameShareTheAliasObjectAsync()
        {
            var assets = new List<IAssetHandle>();
            await using AmbHostedServer server = await AmbHostedServer.StartAsync(
                nameof(AssetsThatShareANameShareTheAliasObjectAsync),
                builder => builder
                    .AddOpcUaDi()
                    .AddAssetManagement()
                    .ConfigureDevicesFor<DiNodeManager>(async context =>
                    {
                        IAssetManagement registry = context.GetRequiredService<IAssetManagement>();
                        foreach (string name in s_twins)
                        {
                            IDeviceBuilder<DeviceState> device = await AssetRegistrationTests.CreateDeviceAsync(
                                context,
                                name,
                                "urn:acme:twin").ConfigureAwait(false);
                            assets.Add(await device.RegisterAsAssetAsync(registry).ConfigureAwait(false));
                        }
                    })).ConfigureAwait(false);
            NodeId category = server.ToNodeId(ObjectIds.AssetsByProductInstanceUri);

            IReadOnlyList<ReferenceDescription> shared = await server
                .BrowseAsync(category, Ua.ReferenceTypeIds.Organizes).ConfigureAwait(false);
            IReadOnlyList<ReferenceDescription> both = await server
                .BrowseAsync(server.ToNodeId(shared[0].NodeId), Ua.ReferenceTypeIds.AliasFor).ConfigureAwait(false);
            ArrayOf<AliasNameDataType> found = await server
                .FindAliasAsync(ObjectIds.AssetsByProductInstanceUri, "urn:acme:twin").ConfigureAwait(false);

            await assets[0].UnregisterAsync().ConfigureAwait(false);
            IReadOnlyList<ReferenceDescription> stillShared = await server
                .BrowseAsync(category, Ua.ReferenceTypeIds.Organizes).ConfigureAwait(false);
            IReadOnlyList<ReferenceDescription> one = await server
                .BrowseAsync(server.ToNodeId(stillShared[0].NodeId), Ua.ReferenceTypeIds.AliasFor)
                .ConfigureAwait(false);

            await assets[1].UnregisterAsync().ConfigureAwait(false);
            IReadOnlyList<ReferenceDescription> none = await server
                .BrowseAsync(category, Ua.ReferenceTypeIds.Organizes).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(shared, Has.Count.EqualTo(1), "§8.1: one AliasName object per name");
                Assert.That(both, Has.Count.EqualTo(2));
                Assert.That(found.Count, Is.EqualTo(1));
                Assert.That(Targets(server, found[0]), Is.EquivalentTo(new[] { assets[0].NodeId, assets[1].NodeId }));
                Assert.That(stillShared, Has.Count.EqualTo(1));
                Assert.That(stillShared[0].NodeId, Is.EqualTo(shared[0].NodeId), "the object stays with an asset left");
                Assert.That(
                    one.Select(reference => server.ToNodeId(reference.NodeId)),
                    Is.EqualTo(new[] { assets[1].NodeId }));
                Assert.That(none, Is.Empty, "the object goes with its last asset");
            });
        }

        [Test]
        public async Task TheAliasesFollowTheIdentificationTheApplicationWritesAsync()
        {
            BaseVariableState? productInstanceUri = null;
            BaseVariableState? assetId = null;
            ISystemContext? context = null;
            await using AmbHostedServer server = await AmbHostedServer.StartAsync(
                nameof(TheAliasesFollowTheIdentificationTheApplicationWritesAsync),
                builder => builder
                    .AddOpcUaDi()
                    .AddAssetManagement(options => options.RequireProductInstanceUri = false)
                    .ConfigureAssetManagement(async setup =>
                    {
                        // A machine registered before its ProductInstanceUri is
                        // known, with an AssetId of its own that clients cannot
                        // write.
                        INodeBuilder<BaseObjectState> machine =
                            AssetRegistrationTests.CreateMachine(setup, string.Empty);
                        ushort di = (ushort)setup.Manager.Server.NamespaceUris.GetIndex(Opc.Ua.Di.Namespaces.OpcUaDi);
                        context = setup.Manager.SystemContext;
                        var group = (BaseObjectState)machine.Node.FindChildWithQualifiedName(
                            context,
                            new QualifiedName("Identification", di))!;
                        productInstanceUri = (BaseVariableState)group.FindChildWithQualifiedName(
                            context,
                            new QualifiedName("ProductInstanceUri", di))!;
                        PropertyState own = group.AddProperty<string, VariantBuilder>(
                            "AssetId",
                            Ua.DataTypeIds.String,
                            ValueRanks.Scalar);
                        own.BrowseName = new QualifiedName("AssetId", di);
                        own.NodeId = setup.Manager.New(context, own);
                        own.WrappedValue = Variant.From("PRESS-A");
                        setup.Manager.AddNode(own);
                        assetId = own;
                        await setup.Assets.RegisterAssetAsync(machine, null, setup.CancellationToken)
                            .ConfigureAwait(false);
                    })).ConfigureAwait(false);
            ArrayOf<AliasNameDataType> before = await server
                .FindAliasAsync(ObjectIds.AssetsByProductInstanceUri, "%").ConfigureAwait(false);
            ArrayOf<AliasNameDataType> listedById = await server
                .FindAliasAsync(ObjectIds.AssetsByAssetId, "PRESS-A").ConfigureAwait(false);
            QualifiedName[] unitsBefore = server.Manager.ConformanceUnits.ToArray()!;

            productInstanceUri!.WrappedValue = Variant.From("urn:acme:press:late");
            productInstanceUri.ClearChangeMasks(context!, false);
            assetId!.WrappedValue = Variant.From("PRESS-B");
            assetId.ClearChangeMasks(context!, false);

            ArrayOf<AliasNameDataType> byUri = await WaitForAliasAsync(
                server,
                ObjectIds.AssetsByProductInstanceUri,
                "urn:acme:press:late").ConfigureAwait(false);
            ArrayOf<AliasNameDataType> byId = await WaitForAliasAsync(server, ObjectIds.AssetsByAssetId, "PRESS-B")
                .ConfigureAwait(false);
            ArrayOf<AliasNameDataType> old = await server
                .FindAliasAsync(ObjectIds.AssetsByAssetId, "PRESS-A").ConfigureAwait(false);
            (ArrayOf<QualifiedName> published, _) = await ReadPublishedAsync(server).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(before.Count, Is.Zero, "an empty ProductInstanceUri is not listed");
                Assert.That(listedById.Count, Is.EqualTo(1), "an AssetId outside the binding is listed too");
                Assert.That(unitsBefore, Does.Not.Contain(new QualifiedName("AMB Asset Identification")));
                Assert.That(byUri.Count, Is.EqualTo(1), "the asset is listed once its ProductInstanceUri is set");
                Assert.That(byId.Count, Is.EqualTo(1), "§8.2.3: the alias name is the value of the AssetId");
                Assert.That(old.Count, Is.Zero);
                Assert.That(
                    published.ToArray(),
                    Does.Contain(new QualifiedName("AMB Asset Identification")),
                    "the units are published again");
                Assert.That(
                    published.ToArray(),
                    Does.Not.Contain(new QualifiedName("AMB Configurable Asset Identification")),
                    "the AssetId is not writable");
            });
        }

        private static async Task<ArrayOf<AliasNameDataType>> WaitForAliasAsync(
            AmbHostedServer server,
            ExpandedNodeId category,
            string name)
        {
            // The aliases follow a reported change on the thread pool.
            for (int attempt = 0; attempt < 100; attempt++)
            {
                ArrayOf<AliasNameDataType> found = await server.FindAliasAsync(category, name).ConfigureAwait(false);
                if (found.Count > 0)
                {
                    return found;
                }
                await Task.Delay(50).ConfigureAwait(false);
            }
            return default;
        }

        private static async Task<(ArrayOf<QualifiedName> Units, ArrayOf<string> Profiles)> ReadPublishedAsync(
            AmbHostedServer server)
        {
            for (int attempt = 0; attempt < 100; attempt++)
            {
                DataValue units = await server.ReadAsync(
                    Ua.VariableIds.Server_ServerCapabilities_ConformanceUnits).ConfigureAwait(false);
                DataValue profiles = await server.ReadAsync(
                    Ua.VariableIds.Server_ServerCapabilities_ServerProfileArray).ConfigureAwait(false);
                units.WrappedValue.TryGetValue(out ArrayOf<QualifiedName> unitValues);
                profiles.WrappedValue.TryGetValue(out ArrayOf<string> profileValues);
                if (unitValues.ToArray()!.Contains(new QualifiedName("AMB Asset Identification")) ||
                    attempt == 99)
                {
                    return (unitValues, profileValues);
                }
                await Task.Delay(50).ConfigureAwait(false);
            }
            return default;
        }

        [Test]
        public async Task WritingTheAssetIdMovesTheAliasAsync()
        {
            await using Fleet fleet = await Fleet.StartAsync(nameof(WritingTheAssetIdMovesTheAliasAsync))
                .ConfigureAwait(false);
            AmbHostedServer server = fleet.Server;
            NodeId nodeVersion = server.Manager.FindPredefinedNode<AliasNameCategoryState>(
                server.ToNodeId(ObjectIds.AssetsByAssetId))!.GetNodeVersionProperty()!.NodeId;
            string before = await ReadStringAsync(server, nodeVersion).ConfigureAwait(false);

            StatusCode written = await server.WriteAsync(fleet.SensorAssetId, Variant.From("S-2"))
                .ConfigureAwait(false);

            ArrayOf<AliasNameDataType> old = await server
                .FindAliasAsync(ObjectIds.AssetsByAssetId, "S-1").ConfigureAwait(false);
            ArrayOf<AliasNameDataType> moved = await server
                .FindAliasAsync(ObjectIds.AssetsByAssetId, "S-2").ConfigureAwait(false);
            IReadOnlyList<ReferenceDescription> aliases = await server.BrowseAsync(
                server.ToNodeId(ObjectIds.AssetsByAssetId),
                Ua.ReferenceTypeIds.Organizes).ConfigureAwait(false);
            string after = await ReadStringAsync(server, nodeVersion).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(StatusCode.IsGood(written), Is.True, written.ToString());
                Assert.That(old.Count, Is.Zero);
                Assert.That(moved.Count, Is.EqualTo(1));
                Assert.That(Targets(server, moved[0]), Is.EqualTo(new[] { fleet.Sensor }));
                Assert.That(
                    aliases.Select(alias => alias.BrowseName.Name),
                    Is.EquivalentTo(new[] { "S-2", AmbBrowseNames.NoAssetIdAssigned }));
                Assert.That(after, Is.Not.EqualTo(before), "the category's NodeVersion changes with its aliases");
            });

            // Clearing the AssetId moves the asset to NoAssetIdAssigned, the
            // object the valve is listed with already.
            StatusCode cleared = await server.WriteAsync(fleet.SensorAssetId, Variant.From(string.Empty))
                .ConfigureAwait(false);
            ArrayOf<AliasNameDataType> unassigned = await server
                .FindAliasAsync(ObjectIds.AssetsByAssetId, AmbBrowseNames.NoAssetIdAssigned).ConfigureAwait(false);
            IReadOnlyList<ReferenceDescription> remaining = await server.BrowseAsync(
                server.ToNodeId(ObjectIds.AssetsByAssetId),
                Ua.ReferenceTypeIds.Organizes).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(StatusCode.IsGood(cleared), Is.True, cleared.ToString());
                Assert.That(
                    Targets(server, unassigned[0]),
                    Is.EquivalentTo(new[] { fleet.Sensor, fleet.Valve }));
                Assert.That(remaining, Has.Count.EqualTo(1), "one NoAssetIdAssigned object for both");
            });
        }

        [Test]
        public async Task UnregisteringRemovesTheAliasesAsync()
        {
            await using Fleet fleet = await Fleet.StartAsync(nameof(UnregisteringRemovesTheAliasesAsync))
                .ConfigureAwait(false);
            AmbHostedServer server = fleet.Server;
            NodeId nodeVersion = server.Manager.FindPredefinedNode<AliasNameCategoryState>(
                server.ToNodeId(ObjectIds.AssetsByProductInstanceUri))!.GetNodeVersionProperty()!.NodeId;
            string before = await ReadStringAsync(server, nodeVersion).ConfigureAwait(false);

            server.AssetManagement.TryGetAsset(fleet.Valve, out IAssetHandle? valve);
            await valve!.UnregisterAsync().ConfigureAwait(false);

            ArrayOf<AliasNameDataType> byUri = await server
                .FindAliasAsync(ObjectIds.AssetsByProductInstanceUri, ValveUri).ConfigureAwait(false);
            IReadOnlyList<ReferenceDescription> hasAlias = await server.BrowseAsync(
                fleet.Valve,
                Ua.ReferenceTypeIds.AliasFor,
                BrowseDirection.Inverse).ConfigureAwait(false);
            IReadOnlyList<ReferenceDescription> aliases = await server.BrowseAsync(
                server.ToNodeId(ObjectIds.AssetsByProductInstanceUri),
                Ua.ReferenceTypeIds.Organizes).ConfigureAwait(false);

            string after = await ReadStringAsync(server, nodeVersion).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(byUri.Count, Is.Zero);
                Assert.That(hasAlias, Is.Empty);
                Assert.That(aliases, Has.Count.EqualTo(2));
                Assert.That(after, Is.Not.EqualTo(before));
            });
        }

        [TestCase(AssetDiscovery.None, 0, 0)]
        [TestCase(AssetDiscovery.ProductInstanceUri, 3, 0)]
        [TestCase(AssetDiscovery.AssetId, 0, 2)]
        public async Task DiscoveryCanBeLimitedAsync(AssetDiscovery discovery, int byUri, int byId)
        {
            await using Fleet fleet = await Fleet.StartAsync(
                nameof(DiscoveryCanBeLimitedAsync) + discovery,
                options => options.Discovery = discovery).ConfigureAwait(false);
            AmbHostedServer server = fleet.Server;

            ArrayOf<AliasNameDataType> uriAliases = await server
                .FindAliasAsync(ObjectIds.AssetsByProductInstanceUri, "%").ConfigureAwait(false);
            IReadOnlyList<ReferenceDescription> idAliases = await server.BrowseAsync(
                server.ToNodeId(ObjectIds.AssetsByAssetId),
                Ua.ReferenceTypeIds.Organizes).ConfigureAwait(false);
            QualifiedName[] units = server.Manager.ConformanceUnits.ToArray()!;

            Assert.Multiple(() =>
            {
                Assert.That(uriAliases.Count, Is.EqualTo(byUri));
                Assert.That(idAliases, Has.Count.EqualTo(byId));
                Assert.That(
                    units.Contains(new QualifiedName("AMB Asset Discovery by ProductInstanceUri")),
                    Is.EqualTo(byUri > 0));
                Assert.That(
                    units.Contains(new QualifiedName("AMB Asset Discovery by AssetId")),
                    Is.EqualTo(byId > 0));
            });
        }

        [Test]
        public async Task AssetsRegisteredAfterStartupAreListedAsync()
        {
            // The AMB manager is registered first, so its address space exists
            // when the Device Integration setup registers the assets.
            await using Fleet fleet = await Fleet.StartAsync(
                nameof(AssetsRegisteredAfterStartupAreListedAsync),
                configure: null,
                assetManagementFirst: true).ConfigureAwait(false);

            ArrayOf<AliasNameDataType> all = await fleet.Server
                .FindAliasAsync(ObjectIds.AssetsByProductInstanceUri, "%").ConfigureAwait(false);

            Assert.That(all.Count, Is.EqualTo(3));
        }

        private static NodeId[] Targets(AmbHostedServer server, AliasNameDataType alias)
        {
            return [.. alias.ReferencedNodes.ToArray()!.Select(server.ToNodeId)];
        }

        private static async Task<string> ReadStringAsync(AmbHostedServer server, NodeId nodeId)
        {
            DataValue value = await server.ReadAsync(nodeId).ConfigureAwait(false);
            return value.WrappedValue.TryGetValue(out string text) ? text : string.Empty;
        }

        /// <summary>
        /// A server with three devices: a sensor with a configurable AssetId
        /// <c>S-1</c>, a valve with a configurable AssetId without a value, and
        /// a pump without an AssetId.
        /// </summary>
        private sealed class Fleet : IAsyncDisposable
        {
            public required AmbHostedServer Server { get; init; }

            public NodeId Sensor { get; private set; }

            public NodeId SensorAssetId { get; private set; }

            public NodeId Valve { get; private set; }

            public NodeId Pump { get; private set; }

            public static async Task<Fleet> StartAsync(
                string name,
                Action<AmbServerOptions>? configure = null,
                bool assetManagementFirst = false)
            {
                var ids = new Dictionary<string, (NodeId Device, NodeId AssetId)>();
                AmbHostedServer server = await AmbHostedServer.StartAsync(name, builder =>
                {
                    if (assetManagementFirst)
                    {
                        builder.AddAssetManagement(configure).AddOpcUaDi();
                    }
                    else
                    {
                        builder.AddOpcUaDi().AddAssetManagement(configure);
                    }
                    builder.ConfigureDevicesFor<DiNodeManager>(async context =>
                    {
                        IAssetManagement assets = context.GetRequiredService<IAssetManagement>();
                        foreach ((string device, string uri, bool configurable, string? defaultAssetId) in s_devices)
                        {
                            IDeviceBuilder<DeviceState> created = await AssetRegistrationTests.CreateDeviceAsync(
                                context,
                                device,
                                uri).ConfigureAwait(false);
                            await created.RegisterAsAssetAsync(
                                assets,
                                configurable ? asset => asset.WithConfigurableAssetId(defaultAssetId) : null,
                                context.CancellationToken).ConfigureAwait(false);
                            ids[device] = (created.Device.NodeId, created.Device.AssetId?.NodeId ?? NodeId.Null);
                        }
                    });
                }).ConfigureAwait(false);

                return new Fleet
                {
                    Server = server,
                    Sensor = ids["Sensor"].Device,
                    SensorAssetId = ids["Sensor"].AssetId,
                    Valve = ids["Valve"].Device,
                    Pump = ids["Pump"].Device
                };
            }

            public ValueTask DisposeAsync()
            {
                return Server.DisposeAsync();
            }

            private static readonly (string Device, string Uri, bool Configurable, string? DefaultAssetId)[] s_devices =
            [
                ("Sensor", SensorUri, true, "S-1"),
                ("Valve", ValveUri, true, null),
                ("Pump", PumpUri, false, null)
            ];
        }
    }
}
