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
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using Opc.Ua.AMB.Client;
using Opc.Ua.AMB.Server.Assets;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Di.Server;
using Opc.Ua.Di.Server.Hosting;
using Opc.Ua.Machinery.Server;
using Opc.Ua.PackML;
using Opc.Ua.Pumps.Server;
using Opc.Ua.Pumps.Server.Builders;
using Opc.Ua.Scales.Client;
using Opc.Ua.Scales.Server;
using Opc.Ua.Scales.Server.Builders;
using Opc.Ua.Scales.Server.Runtime;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Tests;

namespace Opc.Ua.Scales.Tests
{
    /// <summary>
    /// Drives a hosted OPC 40200 server with the Scales client over a real
    /// session. This is the test that decides whether the two packages agree:
    /// everything else checks one side in isolation.
    /// </summary>
    [TestFixture(false)]
    [TestFixture(true)]
    [Category("Scales")]
    [Category("Hosting")]
    [NonParallelizable]
    public sealed class ScalesClientServerE2eTests
    {
        /// <summary>
        /// Selects standalone Scales or the shared Machinery/Pumps/Scales host.
        /// </summary>
        public ScalesClientServerE2eTests(bool composed)
        {
            m_composed = composed;
        }

        private readonly bool m_composed;
        private const uint s_analogConditionSourceId = 41;
        private const uint s_edgeConditionSourceId = 42;
        private readonly RecipeConditionTests.RecipeClock m_conditionClock = new();
        private static string s_endpointUrl = string.Empty;
        private readonly Dictionary<string, ScaleHandle> m_scales = [];
        private ScaleSystemHandle? m_system;
        private ServiceProvider m_provider = null!;
        private IHostedService m_host = null!;
        private ManagedSession m_session = null!;
        private ScalesClient m_client = null!;
        private ClientFixture? m_clientFixture;
        private ScalesRuntime m_runtime = null!;
        private PumpsRuntime? m_pumps;
        private IPumpBuilder? m_pump;
        private IAssetHandle? m_scaleAsset;
        private IAssetHandle? m_pumpAsset;

        [OneTimeSetUp]
        public async Task StartAsync()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IScalePriceCalculator>(
                new RoundedScalePriceCalculator(2, MidpointRounding.ToEven));
            IOpcUaServerBuilder serverBuilder = services.AddOpcUa()
                .AddServer<StandardServer>(ConfigureServer);
            var configured = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            if (m_composed)
            {
                serverBuilder.AddMachinery(options =>
                    {
                        options.Parts = MachineryParts.BuildingBlocks;
                        options.InstanceNamespaceUri = "urn:opcua:scales:composition:instances";
                    })
                    .AddScalesToMachinery(options => options.ZeroSettingRange = 0.04)
                    .AddPumpsToMachinery()
                    .AddAssetManagement()
                    .ConfigurePumpsFor<MachineryNodeManager>(async (context, ct) =>
                    {
                        m_pumps = context.Runtime;
                        m_pump = await m_pumps.CreatePumpAsync(
                            new QualifiedName("FeedPump", m_pumps.InstanceNamespaceIndex), ct);
                        m_pump.WithNameplate(new Pumps.PumpNameplate
                        {
                            NodeId = NodeId.Null,
                            ProductInstanceUri = "urn:composition:pump:feed",
                            Manufacturer = new LocalizedText("Contoso"),
                            SerialNumber = "P-1"
                        });
                        m_pump.Measurements.SetAnalog(Pumps.BrowseNames.MassFlow, 2.5);
                        m_pumpAsset = await context.GetRequiredService<IAssetManagement>().RegisterAssetAsync(
                            context.Manager.TopologyElement(m_pump.Pump).Node,
                            asset => asset.WithConfigurableAssetId("P-1"),
                            ct);
                    })
                    .ConfigureScalesFor<MachineryNodeManager>(async (context, ct) =>
                    {
                        await ConfigureAsync(context.Runtime, context.GetRequiredService<IScalePriceCalculator>(), ct);
                        m_scaleAsset = await context.GetRequiredService<IAssetManagement>().RegisterAssetAsync(
                            context.Manager.Device(m_scales["Simple"].Scale).Node,
                            asset => asset.WithConfigurableAssetId("S-1"),
                            ct);
                    })
                    .ConfigureScalesFor<MachineryNodeManager>((context, _) =>
                    {
                        Assert.That(context.Runtime, Is.SameAs(m_runtime),
                            "successive configurators share the same runtime");
                        configured.TrySetResult(true);
                        return default;
                    });
            }
            else
            {
                serverBuilder.AddScales(options => options.ZeroSettingRange = 0.04).ConfigureScales(
                    async context =>
                    {
                        await ConfigureAsync(context.Manager.Runtime,
                            context.GetRequiredService<IScalePriceCalculator>(), context.CancellationToken);
                        configured.TrySetResult(true);
                    });
            }

            m_provider = services.BuildServiceProvider();
            m_host = m_provider.GetServices<IHostedService>().Single();
            await m_host.StartAsync(CancellationToken.None);
            for (int ii = 0; ii < 450 && !configured.Task.IsCompleted; ii++)
            {
                if (m_host is BackgroundService background && background.ExecuteTask is { IsFaulted: true } task)
                {
                    Assert.Fail("The hosted server failed to start: " + task.Exception);
                }
                await Task.Delay(200);
            }
            Assert.That(configured.Task.IsCompleted, Is.True, "all companion configurators completed");

            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            m_clientFixture = new ClientFixture(telemetry);
            ClientFixture clientFixture = m_clientFixture;
            await clientFixture.LoadClientConfigurationAsync(
                System.IO.Path.Combine(TestContext.CurrentContext.WorkDirectory, nameof(ScalesClientServerE2eTests), "client-pki"));
            EndpointDescription? endpoint = await CoreClientUtils.SelectEndpointAsync(
                clientFixture.Config,
                s_endpointUrl,
                useSecurity: false,
                discoverTimeout: 15000,
                telemetry);
            Assert.That(endpoint, Is.Not.Null, "the server offers an endpoint");
            m_session = await new ManagedSessionBuilder(clientFixture.Config, telemetry)
                .UseEndpoint(new ConfiguredEndpoint(null, endpoint!, EndpointConfiguration.Create(clientFixture.Config)))
                .WithSessionName(nameof(ScalesClientServerE2eTests))
                .ConnectAsync();
            m_client = m_session.Scales(telemetry);
        }

        [OneTimeTearDown]
        public async Task StopAsync()
        {
            if (m_session != null)
            {
                await m_session.DisposeAsync();
            }
            m_clientFixture?.Dispose();
            if (m_host != null)
            {
                await m_host.StopAsync(CancellationToken.None);
            }
            if (m_provider != null)
            {
                await m_provider.DisposeAsync();
            }
        }

        private NodeId Id(string scale) => m_scales[scale].NodeId;

        /// <summary>
        /// Builds the same runtime surface in standalone and composed hosts.
        /// </summary>
        private async ValueTask ConfigureAsync(
            ScalesRuntime manager, IScalePriceCalculator priceCalculator, CancellationToken ct)
        {
            m_runtime = manager;
            QualifiedName Name(string name) => new(name, manager.InstanceNamespaceIndex);

            m_scales["Simple"] = await manager.CreateScaleAsync(
                Name("Simple"),
                ScaleKind.Simple,
                b =>
                {
                    ScalesNodeManagerTests.FullScale(b, "Simple");
                    b.WithAllowedEngineeringUnits(ScaleUnits.Kilogram, ScaleUnits.Gram)
                        .WithProductionPreset(p => p
                            .AllowSelection()
                            .AllowManagement()
                            .AddProduct("Apples", new LocalizedText("Apples"))
                            .AddProduct("Pears", new LocalizedText("Pears"))
                            .Select("Apples"));
                },
                ct);
            m_scales["Pieces"] = await manager.CreateScaleAsync(
                Name("Pieces"),
                ScaleKind.PieceCounting,
                b =>
                {
                    ScalesNodeManagerTests.FullScale(b, "Pieces");
                    b.WithAllowedEngineeringUnits(ScaleUnits.Kilogram, ScaleUnits.Gram)
                        .WithPieceCountingTotals()
                        .WithProductionPreset(p => p
                            .AllowSelection()
                            .AddProduct("Screws", new LocalizedText("M4"))
                            .Select("Screws"));
                },
                ct);
            m_scales["Lab"] = await manager.CreateScaleAsync(
                Name("Lab"),
                ScaleKind.Laboratory,
                b => ScalesNodeManagerTests.FullScale(b, "Lab"),
                ct);
            m_scales["Truck"] = await manager.CreateScaleAsync(
                Name("Truck"),
                ScaleKind.Vehicle,
                b =>
                {
                    ScalesNodeManagerTests.FullScale(b, "Truck");
                    b.WithWeighingRange(new WeighingRangeDefinition(0, 60000, 20, 20))
                        .WithVehicleTotals(_ => VehicleWeightAggregation.Signed)
                        .WithProductionPreset(p => p.AddProduct("T1", new LocalizedText("Truck 1")));
                },
                ct);
            m_scales["Recipe"] = await manager.CreateScaleAsync(
                Name("Recipe"),
                ScaleKind.Recipe,
                b =>
                {
                    ScalesNodeManagerTests.FullScale(b, "Recipe");
                    b.WithRecipeFiles().WithProductionPreset(p => p.AllowSelection()
                        .AddProduct("BATCH", new LocalizedText("Batch")).Select("BATCH"));
                },
                ct);
            RecipeController recipes = m_scales["Recipe"].Recipes!;
            recipes.EnableConditionWaits(TimeSpan.FromMinutes(5), m_conditionClock);
            recipes.RegisterConditionSource(
                s_analogConditionSourceId,
                NodeId.Null,
                new LocalizedText("Analog condition source"),
                BuiltInType.Double,
                ScaleUnits.Kilogram);
            recipes.RegisterConditionSource(
                s_edgeConditionSourceId,
                NodeId.Null,
                new LocalizedText("Boolean edge source"),
                BuiltInType.Boolean);
            foreach ((string name, ScaleKind kind) in new[]
            {
                ("Catch", ScaleKind.Catchweigher),
                ("Price", ScaleKind.AutomaticWeightPriceLabeler),
                ("Totalizing", ScaleKind.TotalizingHopper)
            })
            {
                ScaleHandle scale = await manager.CreateScaleAsync(Name(name), kind,
                    b => ScaleItemProcessingTests.Configure(b, kind, kind == ScaleKind.AutomaticWeightPriceLabeler
                        ? priceCalculator : null), ct);
                m_scales[name] = scale;
                if (scale.ProductionPreset!.ActiveProduct is AutomaticWeightPriceLabelerProductState product)
                {
                    ScaleItemProcessingTests.ConfigurePrice(manager.SystemContext, product, manager.NamespaceIndices.Scales);
                }
            }

            m_scales["Hopper"] = await manager.CreateScaleAsync(
                Name("Hopper"),
                ScaleKind.LossInWeight,
                b => ScalesNodeManagerTests.FullScale(b, "Hopper"),
                ct);
            m_system = await manager.CreateScaleSystemAsync(
                Name("Line"),
                s => s
                    .WithIdentification(ScalesNodeManagerTests.Identity("LINE"))
                    .WithProductionOutput()
                    .WithPackMLState()
                    .AddScale("Checker", ScaleKind.Checkweigher, b =>
                    {
                        ScalesNodeManagerTests.FullScale(b, "Checker");
                        b.AddFeederModule("Infeed", f => f
                                .WithIdentification(ScalesNodeManagerTests.Identity("F1"))
                                .WithFeederSpeed(0.2, 2.0, ScalesTestUnits.MetrePerSecond, 1.0))
                            .AddPrinterModule("Labeler", p => p
                                .WithIdentification(ScalesNodeManagerTests.Identity("PR1")))
                            .AddWeighingModule("Bridge", w => ScalesNodeManagerTests.FullScale(w, "WM1"));
                    }),
                ct);
        }

        [Test]
        public async Task ClientDiscoversEveryScaleOnceWithItsKindAsync()
        {
            Assert.That(m_client.IsSupported, Is.True);
            ScaleEntry[] scales = (await m_client.DiscoverScalesAsync()).ToArray()!;
            ScaleEntry[] devices = [.. scales.Where(s => !s.IsScaleSystem)];
            Assert.That(
                devices.Select(scale => scale.NodeId).Distinct().Count(),
                Is.EqualTo(devices.Length),
                "organized twice, reported once");
            Assert.That(devices.Length, Is.GreaterThanOrEqualTo(m_scales.Count));
            foreach (ScaleHandle expected in m_scales.Values)
            {
                Assert.That(devices.Count(scale => scale.NodeId == expected.NodeId), Is.EqualTo(1));
            }
            Assert.That(scales.Single(s => s.IsScaleSystem).BrowseName.Name, Is.EqualTo("Line"));
            Assert.That(scales.Single(s => s.BrowseName.Name == "Truck").Kind, Is.EqualTo(ScaleKind.Vehicle));
            Assert.That(scales.Single(s => s.BrowseName.Name == "Hopper").Kind, Is.EqualTo(ScaleKind.LossInWeight));

            var systemScales = new List<ScaleEntry>();
            await foreach (ScaleEntry entry in m_client.EnumerateSystemScalesAsync(m_system!.NodeId))
            {
                systemScales.Add(entry);
            }
            Assert.That(systemScales, Has.Count.EqualTo(1));
            Assert.That(systemScales[0].Kind, Is.EqualTo(ScaleKind.Checkweigher));
            Assert.That(await m_client.GetKindAsync(NodeId.Null), Is.Null);
        }

        [Test]
        public async Task ClientReadsRegisteredItemsPricesAndCompletedFillsWithSourceMetadataAsync()
        {
            foreach (string name in new[] { "Catch", "Price" })
            {
                ScaleHandle scale = m_scales[name];
                var product = (CatchweigherProductState)scale.ProductionPreset!.ActiveProduct!;
                ScaleItemReport item = ScaleItemProcessingTests.Item(1, 0.5) with
                {
                    Weight = ScaleItemProcessingTests.Item(1, 0.5).Weight with
                    {
                        WeightId = "equipment-weight",
                        Stable = true,
                        InsideZero = false,
                        CurrentRangeId = 7,
                        TareMode = TareMode.MeasuredTare_1
                    },
                    Length = new ScaleItemMeasurement(
                        20, ScaleUnits.Create("MMT", "mm", "millimetre"),
                        DateTime.UtcNow.AddSeconds(-1), StatusCodes.Uncertain)
                };
                Assert.That(ServiceResult.IsGood(await scale.Catchweigher!.ReportAsync(item)), Is.True);
                ScaleLastItemSnapshot actual = await m_client.ReadLastItemAsync(product.NodeId);
                Assert.That(actual.ItemId.HasValue, Is.True);
                Assert.That(actual.ItemId.Value, Is.EqualTo(item.ItemId));
                Assert.That(actual.MeasuredWeight.Value.Net, Is.EqualTo(0.5));
                Assert.That(actual.MeasuredWeight.HasValue, Is.True);
                Assert.That(actual.MeasuredWeight.Value.WeightId, Is.EqualTo("equipment-weight"));
                Assert.That(actual.MeasuredWeight.Value.Stable, Is.True);
                Assert.That(actual.MeasuredWeight.Value.InsideZero, Is.False);
                Assert.That(actual.MeasuredWeight.Value.CurrentRangeId, Is.EqualTo(7));
                Assert.That(actual.MeasuredWeight.Value.TareMode, Is.EqualTo(TareMode.MeasuredTare_1));
                Assert.That(actual.MeasuredWeight.Timestamp, Is.EqualTo(item.Weight.Timestamp));
                Assert.That(actual.MeasuredLength.Value, Is.EqualTo(20));
                Assert.That(actual.MeasuredLength.StatusCode, Is.EqualTo((StatusCode)StatusCodes.Uncertain));
                Assert.That(actual.MeasuredLength.Timestamp, Is.EqualTo(item.Length!.Timestamp));
                Assert.That(actual.MeasuredWidth.HasValue, Is.False);
                Assert.That(actual.MeasuredWidth.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadNoData));
                if (name == "Price")
                {
                    Assert.That(actual.ItemPrice.Value, Is.EqualTo(0.5));
                    Assert.That(actual.ItemPriceCurrencyUnit.Value.AlphabeticCode, Is.EqualTo("EUR"));
                    Assert.That(actual.UnitPrice.Value, Is.EqualTo(1));
                    Assert.That(actual.UnitPriceCurrencyUnit.Value.NumericCode, Is.EqualTo(978));
                }
                else
                {
                    Assert.That(actual.ItemPrice.HasValue, Is.False);
                }
            }
            ScaleHandle hopper = m_scales["Totalizing"];
            var hopperProduct = (TotalizingHopperProductState)hopper.ProductionPreset!.ActiveProduct!;
            hopperProduct.TipCounter!.WrappedValue = new Variant((ulong)uint.MaxValue);
            Assert.That(ServiceResult.IsGood(
                await hopper.TotalizingHopper!.ReportCompletedFillAsync(ScaleItemProcessingTests.Item(1))), Is.True);
            ScaleField<ulong> tips = await m_client.ReadTipCounterAsync(hopperProduct.NodeId);
            Assert.That(tips.HasValue, Is.True);
            Assert.That(tips.Value, Is.EqualTo((ulong)uint.MaxValue + 1));
            ScaleField<ulong> missing = await m_client.ReadTipCounterAsync(m_scales["Catch"].ProductionPreset!.ActiveProduct!.NodeId);
            Assert.That(missing.HasValue, Is.False);
            Assert.That(StatusCode.IsBad(missing.StatusCode), Is.True);
        }

        /// <summary>
        /// Observes a catchweigher zone through the client before and after its removal.
        /// </summary>
        [Test]
        public async Task ClientAddsAndRemovesCatchweigherZonesAsync()
        {
            var product = (CatchweigherProductState)m_scales["Catch"].ProductionPreset!.ActiveProduct!;
            NodeId zone = await m_client.AddZoneAsync(
                product.NodeId,
                new LocalizedText("Client zone"),
                1,
                2,
                ScaleUnits.Kilogram);
            var browser = new Browser(m_session, new BrowserOptions
            {
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasComponent,
                IncludeSubtypes = false,
                NodeClassMask = (int)NodeClass.Object,
                ResultMask = (uint)BrowseResultMask.All
            });
            bool removed = false;
            try
            {
                Assert.That(zone.IsNull, Is.False);
                ArrayOf<ReferenceDescription> before = await browser.BrowseAsync(product.NodeId);
                Assert.That(
                    before.ToList().Select(reference =>
                        ExpandedNodeId.ToNodeId(reference.NodeId, m_session.NamespaceUris)),
                    Does.Contain(zone));
                await m_client.RemoveZoneAsync(product.NodeId, zone);
                removed = true;
                ArrayOf<ReferenceDescription> after = await browser.BrowseAsync(product.NodeId);
                Assert.That(
                    after.ToList().Select(reference =>
                        ExpandedNodeId.ToNodeId(reference.NodeId, m_session.NamespaceUris)),
                    Does.Not.Contain(zone));
            }
            finally
            {
                if (!removed && !zone.IsNull)
                {
                    await m_client.RemoveZoneAsync(product.NodeId, zone);
                }
            }
        }

        /// <summary>
        /// Rejects an OPC UA null localized zone name before invoking the server.
        /// </summary>
        [Test]
        public async Task ClientRejectsNullLocalizedZoneName()
        {
            var product = (CatchweigherProductState)m_scales["Catch"].ProductionPreset!.ActiveProduct!;

            await Assert.ThrowsAsync<ArgumentException>(async () =>
                await m_client.AddZoneAsync(
                    product.NodeId,
                    LocalizedText.Null,
                    1,
                    2,
                    ScaleUnits.Kilogram));
        }

        [Test]
        public async Task CompanionsShareOneOwnerAndAmbKeepsTheirNativeNodesAsync()
        {
            Assert.That(m_runtime.Manager.Server.NodeManager.AsyncNodeManagers.Count(
                manager => manager is DiNodeManager), Is.EqualTo(1));
            if (!m_composed)
            {
                Assert.That(m_runtime.Manager, Is.TypeOf<ScalesNodeManager>());
                return;
            }

            var owner = (MachineryNodeManager)m_runtime.Manager;
            Assert.Multiple(() =>
            {
                Assert.That(m_pumps!.Manager, Is.SameAs(owner));
                Assert.That(m_pump!.NodeId.NamespaceIndex, Is.EqualTo(owner.MachineryInstanceNamespaceIndex));
                Assert.That(Id("Simple").NamespaceIndex, Is.EqualTo(owner.MachineryInstanceNamespaceIndex));
                Assert.That(owner.FindPredefinedNode(m_pump.NodeId), Is.SameAs(m_pump.Pump));
                Assert.That(owner.FindPredefinedNode(Id("Simple")), Is.SameAs(m_scales["Simple"].Scale));
                Assert.That(m_provider.GetServices<DiAddressSpaceOwnership>().Single().OwnerName,
                    Is.EqualTo("AddMachinery"));
                Assert.That(m_pumpAsset!.IsRegistered, Is.True);
                Assert.That(m_scaleAsset!.IsRegistered, Is.True);
            });
            string[] modelUris =
            [
                Di.Namespaces.OpcUaDi,
                IA.Namespaces.IA,
                Machinery.Namespaces.Machinery,
                Pumps.Namespaces.Pumps,
                PackML.Namespaces.PackML,
                Namespaces.Scales
            ];
            foreach (string uri in modelUris)
            {
                Assert.That(owner.Server.NodeManager.AsyncNodeManagers.Count(manager =>
                    manager.NamespaceUris != null && manager.NamespaceUris.Contains(uri)), Is.EqualTo(1), uri);
            }

            AmbClient assets = m_session.AssetManagement(NUnitTelemetryContext.Create());
            ArrayOf<NodeId> discovered = await assets.DiscoverAssetsAsync();
            Assert.That(discovered.ToArray(), Is.EquivalentTo(new[] { m_pump!.NodeId, Id("Simple") }));
            Assert.That((await assets.ReadAssetAsync(m_pump.NodeId)).Identification.ProductInstanceUri,
                Is.EqualTo("urn:composition:pump:feed"));
            Assert.That((await assets.ReadAssetAsync(Id("Simple"))).Identification.ProductInstanceUri,
                Is.EqualTo("urn:contoso:scale:Simple"));
        }

        [Test]
        public async Task ClientUploadsAndReadsARecipeFileAsync()
        {
            RecipeController recipes = m_scales["Recipe"].Recipes!;
            RecipeState recipe = recipes.AddRecipe("FileRoundTrip", new LocalizedText("File round trip"));
            FileState file = recipe.RecipeFile!;
            ByteString uploaded = default;
            recipes.RecipeFileHandler = (_, content) =>
            {
                uploaded = content;
                return ServiceResult.Good;
            };
            ByteString expected = ByteString.From(new byte[] { 7, 8, 9, 10 });
            uint handle = 0;
            try
            {
                ArrayOf<Variant> output = await m_session.CallAsync(
                    file.NodeId, file.Open!.NodeId, CancellationToken.None, Variant.From((byte)6));
                Assert.That(output[0].TryGetValue(out handle), Is.True);
                await m_session.CallAsync(file.NodeId, file.Write!.NodeId, CancellationToken.None,
                    Variant.From(handle), Variant.From(expected));
                await m_session.CallAsync(
                    file.NodeId, file.Close!.NodeId, CancellationToken.None, Variant.From(handle));
                handle = 0;
                Assert.That(uploaded, Is.EqualTo(expected));
                Assert.That(recipes.ReadRecipeFile(recipe), Is.EqualTo(expected));

                output = await m_session.CallAsync(
                    file.NodeId, file.Open.NodeId, CancellationToken.None, Variant.From((byte)1));
                Assert.That(output[0].TryGetValue(out handle), Is.True);
                output = await m_session.CallAsync(file.NodeId, file.Read!.NodeId, CancellationToken.None,
                    Variant.From(handle), Variant.From(32));
                Assert.That(output[0].TryGetValue(out ByteString received), Is.True);
                Assert.That(received, Is.EqualTo(expected));
            }
            finally
            {
                if (handle != 0)
                {
                    await m_session.CallAsync(
                        file.NodeId, file.Close!.NodeId, CancellationToken.None, Variant.From(handle));
                }
                recipes.RecipeFileHandler = null;
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task NestedModuleEventsReachSystemAndServerAsync(bool serverScope)
        {
            ScaleHandle checker = m_system!.Scales[0];
            ScaleNotifications notifications = serverScope
                ? checker.Modules[0].Notifications
                : checker.WeighingModules[0].Notifications;
            NodeId expectedSource = serverScope
                ? checker.Modules[0].NodeId
                : checker.WeighingModules[0].NodeId;
            NodeId subscriptionSource = serverScope ? Opc.Ua.ObjectIds.Server : m_system.NodeId;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var stopping = new CancellationTokenSource();
            Task raiser = Task.Run(async () =>
            {
                while (!stopping.IsCancellationRequested)
                {
                    notifications.RaiseEvent(
                        ScaleNotificationId.LabelFault, new LocalizedText("nested module"), EventSeverity.Low);
                    await Task.Delay(200, CancellationToken.None);
                }
            });
            ScaleNotificationInfo? received = null;
            try
            {
                await foreach (ScaleNotificationInfo notification in m_client.ObserveNotificationsAsync(
                    subscriptionSource, cancellationToken: timeout.Token))
                {
                    if (!notification.IsAlarm && notification.SourceNode == expectedSource)
                    {
                        received = notification;
                        break;
                    }
                }
            }
            finally
            {
                stopping.Cancel();
                await raiser;
            }
            Assert.That(received, Is.Not.Null);
            Assert.That(received!.DefinedId, Is.EqualTo(ScaleNotificationId.LabelFault));
        }

        [Test]
        public async Task ExistingServerSubscriptionReceivesEventsFromNewCompanionsAsync()
        {
            var filter = new EventFilter
            {
                SelectClauses =
                [
                    new SimpleAttributeOperand
                    {
                        TypeDefinitionId = Opc.Ua.ObjectTypeIds.BaseEventType,
                        BrowsePath = [new QualifiedName(Opc.Ua.BrowseNames.SourceNode)],
                        AttributeId = Attributes.Value
                    }
                ]
            };
            ScaleHandle existing = m_scales["Hopper"];
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var stopping = new CancellationTokenSource();
            Task signaler = Task.Run(async () =>
            {
                while (!stopping.IsCancellationRequested)
                {
                    existing.Notifications.RaiseEvent(
                        ScaleNotificationId.LabelFault, new LocalizedText("ready"), EventSeverity.Low);
                    await Task.Delay(100, CancellationToken.None);
                }
            });
            ScaleHandle? lateScale = null;
            IPumpBuilder? latePump = null;
            var parent = new BaseObjectState(null)
            {
                NodeId = new NodeId("LateCompanionParent", m_runtime.InstanceNamespaceIndex),
                BrowseName = new QualifiedName("LateCompanionParent", m_runtime.InstanceNamespaceIndex),
                DisplayName = new LocalizedText("Late companion parent"),
                TypeDefinitionId = Opc.Ua.ObjectTypeIds.BaseObjectType
            };
            var sources = new HashSet<NodeId>();
            try
            {
                await foreach (EventNotification notification in m_session.DefaultStreaming.SubscribeEventsAsync(
                    Opc.Ua.ObjectIds.Server, filter, ct: timeout.Token))
                {
                    if (!notification.Fields[0].TryGetValue(out NodeId source))
                    {
                        continue;
                    }
                    if (lateScale == null)
                    {
                        if (source != existing.NodeId)
                        {
                            continue;
                        }

                        // Receiving the sentinel proves the server-wide monitored item already exists.
                        stopping.Cancel();
                        await signaler;
                        await m_runtime.Manager.AddPredefinedNodeAsync(parent, timeout.Token);
                        lateScale = await m_runtime.CreateScaleAsync(
                            new QualifiedName("LateScale", m_runtime.InstanceNamespaceIndex), ScaleKind.Simple,
                            builder => ScalesNodeManagerTests.FullScale(builder, "LATE"), parent, timeout.Token);
                        Assert.That(m_runtime.Manager.Server.ServerObject.ReferenceExists(
                            Opc.Ua.ReferenceTypeIds.HasNotifier, false, lateScale.NodeId), Is.True);
                        lateScale.Notifications.RaiseEvent(
                            ScaleNotificationId.LabelFault, new LocalizedText("late scale"), EventSeverity.Low);
                        if (m_pumps != null)
                        {
                            latePump = await m_pumps.CreatePumpAsync(
                                new QualifiedName("LatePump", m_pumps.InstanceNamespaceIndex), parent, timeout.Token);
                            Assert.That(m_runtime.Manager.Server.ServerObject.ReferenceExists(
                                Opc.Ua.ReferenceTypeIds.HasNotifier, false, latePump.NodeId), Is.True);
                            var occurrence = new BaseEventState(null);
                            occurrence.Initialize(m_pumps.SystemContext, latePump.Pump,
                                EventSeverity.Low, new LocalizedText("late pump"));
                            await latePump.Pump.ReportEventAsync(m_pumps.SystemContext, occurrence, timeout.Token);
                        }
                    }
                    else if (source == lateScale.NodeId || (latePump != null && source == latePump.NodeId))
                    {
                        sources.Add(source);
                        if (sources.Count == (m_composed ? 2 : 1))
                        {
                            break;
                        }
                    }
                }
            }
            finally
            {
                stopping.Cancel();
                await signaler;
                if (lateScale != null)
                {
                    await m_runtime.Manager.DeleteNodeAsync(m_runtime.Manager.SystemContext, lateScale.NodeId);
                }
                if (latePump != null)
                {
                    await m_runtime.Manager.DeleteNodeAsync(m_runtime.Manager.SystemContext, latePump.NodeId);
                }
                await m_runtime.Manager.DeleteNodeAsync(m_runtime.Manager.SystemContext, parent.NodeId);
            }
            Assert.That(sources, Has.Count.EqualTo(m_composed ? 2 : 1));
        }

        [Test]
        public async Task ClientReadsIdentificationRangesAndUnitsAsync()
        {
            ScaleIdentification identification = await m_client.ReadIdentificationAsync(Id("Simple"));
            Assert.That(identification.Manufacturer.Text, Is.EqualTo("Contoso Weighing"));
            Assert.That(identification.SerialNumber, Is.EqualTo("Simple"));
            Assert.That(identification.ProductInstanceUri, Is.EqualTo("urn:contoso:scale:Simple"));
            Assert.That(identification.Location, Is.EqualTo("Line 1"));
            Assert.That(identification.YearOfConstruction, Is.EqualTo((ushort)2026));
            Assert.That(identification.MonthOfConstruction, Is.EqualTo((byte)3));
            Assert.That(identification.InitialOperationDate, Is.Not.Null);

            ArrayOf<WeighingRangeDefinition> ranges = await m_client.ReadWeighingRangesAsync(Id("Simple"));
            Assert.That(ranges, Has.Count.EqualTo(2));
            Assert.That(ranges[1].High, Is.EqualTo(6));
            Assert.That(ranges[0].ActualScaleInterval, Is.EqualTo(0.001).Within(1e-12));
            Assert.That(ranges[0].EngineeringUnits!.UnitId, Is.EqualTo(ScaleUnits.Kilogram.UnitId));

            ArrayOf<EUInformation> units = await m_client.ReadAllowedEngineeringUnitsAsync(Id("Simple"));
            Assert.That(
                units.ToArray()!.Select(u => u.UnitId),
                Is.EquivalentTo(new[] { ScaleUnits.Kilogram.UnitId, ScaleUnits.Gram.UnitId }));
            // SetPresetTare takes a unit, so the server publishes the scale's
            // own unit as the allowed one when the builder was given none
            // (OPC 40200 §7.4.3).
            ArrayOf<EUInformation> labUnits = await m_client.ReadAllowedEngineeringUnitsAsync(Id("Lab"));
            Assert.That(labUnits.ToArray()!.Single().UnitId, Is.EqualTo(ScaleUnits.Kilogram.UnitId));
        }

        [Test]
        public async Task ClientZeroesTaresAndRegistersWeightsAsync()
        {
            ScaleHandle scale = m_scales["Simple"];
            NodeId id = scale.NodeId;
            scale.PublishLoad(0.05);
            await m_client.SetZeroAsync(id);
            scale.PublishLoad(1.55);
            ScaleReading reading = await m_client.ReadCurrentWeightAsync(id);
            Assert.That(reading.Gross, Is.EqualTo(1.5).Within(1e-9));
            Assert.That(reading.Stable, Is.True);
            Assert.That(reading.EngineeringUnits!.UnitId, Is.EqualTo(ScaleUnits.Kilogram.UnitId));

            await m_client.SetTareAsync(id);
            scale.PublishLoad(2.05);
            reading = await m_client.ReadCurrentWeightAsync(id);
            Assert.That(reading.Net, Is.EqualTo(0.5).Within(1e-9));
            Assert.That(reading.TareMode, Is.EqualTo(TareMode.MeasuredTare_1));

            await m_client.SetPresetTareAsync(id, 250, ScaleUnits.Gram);
            reading = await m_client.ReadCurrentWeightAsync(id);
            Assert.That(reading.Tare, Is.EqualTo(0.25).Within(1e-9));
            Assert.That(reading.TareMode, Is.EqualTo(TareMode.PresetTare_2));

            await m_client.ClearTareAsync(id);
            ScaleReading? registered = await m_client.RegisterWeightAsync(id);
            Assert.That(registered, Is.Not.Null);
            Assert.That(registered!.Gross, Is.EqualTo(2.0).Within(1e-9));
            Assert.That(registered.WeightId, Is.Not.Null.And.Not.Empty);

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () => await m_client.SetZeroAsync(id));
            Assert.That(error!.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadOutOfRange));
            Assert.That(await m_client.ReadRegisteredWeightAsync(Id("Hopper")), Is.Not.Null);
            Assert.That(await m_client.TryReadCurrentWeightAsync(id), Is.Not.Null);
            Assert.That(await m_client.TryReadCurrentWeightAsync(m_system!.NodeId), Is.Null, "a scale system has no CurrentWeight");
            ServiceResultException? notFound = await Assert.ThrowsAsync<ServiceResultException>(
                async () => await m_client.ReadCurrentWeightAsync(m_system.NodeId));
            Assert.That(notFound!.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadNotFound));
            ServiceResultException? missing = await Assert.ThrowsAsync<ServiceResultException>(
                async () => await m_client.InvokeAsync(id, BrowseNames.DischargeStart));
            Assert.That(missing!.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadNotSupported));
        }

        [Test]
        public async Task ClientManagesAndSelectsProductsAsync()
        {
            NodeId id = Id("Simple");
            ScaleProductInfo[] products = (await m_client.ReadProductsAsync(id)).ToArray()!;
            string[] knownProducts = ["Apples", "Pears"];
            string[] apples = ["Apples"];
            string[] pears = ["Pears"];
            Assert.That(products.Select(p => p.ProductId), Is.SupersetOf(knownProducts));
            Assert.That(products.Single(p => p.ProductId == "Apples").Processing, Is.True);
            Assert.That(await m_client.ReadCurrentProductsAsync(id), Is.EqualTo(apples));

            await m_client.SwitchProductAsync(id, "Pears");
            Assert.That(await m_client.ReadCurrentProductsAsync(id), Is.EqualTo(pears));
            await m_client.DeselectProductAsync(id, "Pears");
            await m_client.SelectProductAsync(id, "Apples");

            NodeId added = await m_client.AddProductAsync(id, "Plums", "Plums");
            Assert.That(added.IsNull, Is.False);
            Assert.That((await m_client.ReadProductsAsync(id)).Contains(p => p.ProductId == "Plums"), Is.True);
            await m_client.RemoveProductAsync(id, "Plums");
            Assert.That((await m_client.ReadProductsAsync(id)).Contains(p => p.ProductId == "Plums"), Is.False);

            ServiceResultException? notFound = await Assert.ThrowsAsync<ServiceResultException>(
                async () => await m_client.SelectProductAsync(id, "Nope"));
            Assert.That(notFound!.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadNotFound));
            await Assert.ThrowsAsync<ServiceResultException>(async () => await m_client.SelectProductAsync(Id("Lab"), "x"));
            Assert.That(await m_client.ReadProductsAsync(Id("Lab")), Is.Empty);
        }

        [Test]
        public async Task ClientDrivesThePackMLStateMachineAsync()
        {
            NodeId id = Id("Lab");
            Assert.That(await m_client.ReadPackMLStateAsync(id), Is.EqualTo(PackMLStateNumbers.Idle));
            await m_client.ExecutePackMLCommandAsync(id, PackMLCommand.Start);
            Assert.That(await m_client.ReadPackMLStateAsync(id), Is.EqualTo(PackMLStateNumbers.Execute));
            await m_client.ExecutePackMLCommandAsync(id, PackMLCommand.Hold);
            Assert.That(await m_client.ReadPackMLStateAsync(id), Is.EqualTo(PackMLStateNumbers.Held));
            await m_client.ExecutePackMLCommandAsync(id, PackMLCommand.Unhold);
            await m_client.ExecutePackMLCommandAsync(id, PackMLCommand.ToComplete);
            Assert.That(await m_client.ReadPackMLStateAsync(id), Is.EqualTo(PackMLStateNumbers.Complete));
            await m_client.ExecutePackMLCommandAsync(id, PackMLCommand.Reset);
            Assert.That(await m_client.ReadPackMLStateAsync(id), Is.EqualTo(PackMLStateNumbers.Idle));
            await m_client.ExecutePackMLCommandAsync(id, PackMLCommand.Stop);
            Assert.That(await m_client.ReadPackMLStateAsync(id), Is.EqualTo(PackMLStateNumbers.Stopped));
            await m_client.ExecutePackMLCommandAsync(id, PackMLCommand.Reset);
            await m_client.ExecutePackMLCommandAsync(id, PackMLCommand.Abort);
            Assert.That(await m_client.ReadPackMLStateAsync(id), Is.EqualTo(PackMLStateNumbers.Aborted));
            ServiceResultException? invalid = await Assert.ThrowsAsync<ServiceResultException>(
                async () => await m_client.ExecutePackMLCommandAsync(id, PackMLCommand.Start));
            // The controller clears Executable on the methods whose cause is not
            // permitted, so the server rejects the call before it reaches the
            // state machine (OPC 10000-4 §5.11.2).
            Assert.That(invalid!.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadNotExecutable));
            await m_client.ExecutePackMLCommandAsync(id, PackMLCommand.Clear);
            await m_client.ExecutePackMLCommandAsync(id, PackMLCommand.Reset);

            Assert.That(await m_client.ReadPackMLStateAsync(m_system!.NodeId), Is.EqualTo(PackMLStateNumbers.Idle));
            await m_client.ExecutePackMLCommandAsync(m_system.NodeId, PackMLCommand.Start);
            Assert.That(m_system.PackML!.CurrentState, Is.EqualTo(PackMLStateNumbers.Execute));
            Assert.That(await m_client.ReadPackMLStateAsync(Id("Truck")), Is.Not.Null);
        }

        [Test]
        public async Task ClientCallsTheTypeSpecificMethodsAsync()
        {
            ScaleHandle pieces = m_scales["Pieces"];
            await m_client.SetReferencePieceWeightAsync(pieces.NodeId, 5, ScaleUnits.Gram);
            await m_client.SetNumberOfReferencePiecesAsync(pieces.NodeId, 10);
            pieces.PublishLoad(0.5);
            Assert.That(pieces.PieceCounting!.CurrentPieceCount, Is.EqualTo(100UL));
            pieces.PublishLoad(0.06);
            await m_client.StartReferenceAsync(pieces.NodeId, 20);
            Assert.That(pieces.PieceCounting.ReferencePieceWeight, Is.EqualTo(0.003).Within(1e-12));
            pieces.PublishLoad(0.06);
            Assert.That(await m_client.RegisterWeightAsync(pieces.NodeId), Is.Not.Null);
            ScalePieceCountingSnapshot totals = await m_client.ReadPieceCountingTotalsAsync(
                pieces.ProductionPreset!.ActiveProduct!.NodeId);
            Assert.That(totals.RegisteredPieceCount.HasValue, Is.True);
            Assert.That(totals.RegisteredPieceCount.Value, Is.EqualTo(20u));
            Assert.That(totals.TotalizedItemCount.HasValue, Is.True);
            Assert.That(totals.TotalizedItemCount.Value, Is.EqualTo(20u));
            Assert.That(totals.TotalizedWeight.HasValue, Is.True);
            Assert.That(totals.TotalizedWeight.Value.Net, Is.EqualTo(0.06).Within(1e-12));

            ScaleHandle lab = m_scales["Lab"];
            await m_client.SetDraftShieldsAsync(lab.NodeId, DraftShieldType.All_3, close: true);
            Assert.That(lab.Laboratory!.Scale.DraftShieldTopClosed!.Value, Is.True);
            await m_client.SetDraftShieldsAsync(lab.NodeId, DraftShieldType.Top_2, close: false);
            await m_client.InvokeAsync(lab.NodeId, BrowseNames.StartIonisator);
            Assert.That(lab.Laboratory.Scale.IonisatorRunning!.Value, Is.True);

            ScaleHandle truck = m_scales["Truck"];
            truck.PublishLoad(40000);
            await m_client.WeighVehicleAsync(truck.NodeId, BrowseNames.InboundWeighing, "T1");
            truck.PublishLoad(15000);
            await m_client.WeighVehicleAsync(truck.NodeId, BrowseNames.OutboundWeighing, "T1");
            var vehicle = (VehicleProductState)truck.ProductionPreset!.Find("T1")!;
            Assert.That(vehicle.DeltaWeight!.Value.Gross, Is.EqualTo(25000).Within(1e-6));

            ScaleHandle hopper = m_scales["Hopper"];
            await m_client.InvokeAsync(hopper.NodeId, BrowseNames.DischargeStart);
            Assert.That(hopper.LossInWeight!.Discharging, Is.True);
            await m_client.InvokeAsync(hopper.NodeId, BrowseNames.DischargeStop);

            ScaleHandle checker = m_system!.Scales[0];
            await m_client.SetFeederSpeedAsync(checker.Modules[0].NodeId, 1.5f, ScalesTestUnits.MetrePerSecond);
            ServiceResultException? tooFast = await Assert.ThrowsAsync<ServiceResultException>(
                async () => await m_client.SetFeederSpeedAsync(checker.Modules[0].NodeId, 9f, ScalesTestUnits.MetrePerSecond));
            Assert.That(tooFast!.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadOutOfRange));
            await m_client.InvokeAsync(m_system.NodeId, BrowseNames.ResetGlobalStatistics);
            ScaleIdentification feederId = await m_client.ReadIdentificationAsync(checker.Modules[0].NodeId);
            Assert.That(feederId.SerialNumber, Is.EqualTo("F1"));
        }

        /// <summary>
        /// Observes element removal separately from removal of the containing recipe.
        /// </summary>
        [Test]
        public async Task ClientRemovesRecipeElementsAndRecipesAsync()
        {
            ScaleHandle scale = m_scales["Recipe"];
            ushort ns = m_session.NamespaceUris.GetIndexOrAppend(Namespaces.Scales);
            NodeId recipe = await m_client.AddRecipeAsync(
                scale.NodeId, "ClientRemoval", new LocalizedText("Client removal"));
            NodeId element = await m_client.AddRecipeElementAsync(
                recipe,
                new NodeId(ObjectTypes.TimerType, ns),
                "Wait",
                [recipe]);

            Assert.That(recipe.IsNull || element.IsNull, Is.False);
            RecipeState recipeState = scale.Recipes!.Recipes["ClientRemoval"];
            var browser = new Browser(m_session, new BrowserOptions
            {
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = Opc.Ua.ReferenceTypeIds.Organizes,
                IncludeSubtypes = false,
                NodeClassMask = (int)NodeClass.Object,
                ResultMask = (uint)BrowseResultMask.All
            });
            ArrayOf<ReferenceDescription> visibleElements =
                await browser.BrowseAsync(recipeState.RecipeElements!.NodeId);
            Assert.That(
                visibleElements.ToList()
                    .Select(reference => ExpandedNodeId.ToNodeId(reference.NodeId, m_session.NamespaceUris)),
                Does.Contain(element));
            await m_client.RemoveRecipeElementAsync(recipe, element);
            visibleElements = await browser.BrowseAsync(recipeState.RecipeElements.NodeId);
            Assert.That(
                visibleElements.ToArray()!
                    .Select(reference => ExpandedNodeId.ToNodeId(reference.NodeId, m_session.NamespaceUris)),
                Does.Not.Contain(element));
            await m_client.RemoveRecipeAsync(scale.NodeId, "ClientRemoval");

            Assert.That(scale.Recipes.Recipes.ContainsKey("ClientRemoval"), Is.False);
        }

        /// <summary>
        /// Completes native asynchronous vehicle methods and reads totals with the original quality and source time.
        /// </summary>
        [TestCase(VehicleWeightAggregation.Signed, 15000)]
        [TestCase(VehicleWeightAggregation.Absolute, 35000)]
        public async Task ClientVehicleTotalsPreserveSignedDeltaAndSourceQualityAsync(
            VehicleWeightAggregation aggregation,
            double expectedTotal)
        {
            ScaleHandle scale = await m_runtime.CreateScaleAsync(
                new QualifiedName("VehicleTotals" + aggregation, m_runtime.InstanceNamespaceIndex),
                ScaleKind.Vehicle,
                builder => builder.WithIdentification(
                        ScalesNodeManagerTests.Identity("VehicleTotals" + aggregation))
                    .WithTypeFeatures()
                    .WithWeighingRange(new WeighingRangeDefinition(0, 60000, 20, 20))
                    .WithVehicleTotals(_ => aggregation)
                    .WithProductionPreset(preset => preset.AddProduct("V", new LocalizedText("Vehicle"))));
            var vehicle = (VehicleProductState)scale.ProductionPreset!.Find("V")!;
            scale.PublishLoad(20000);
            await m_client.WeighVehicleAsync(scale.NodeId, BrowseNames.InboundWeighing, "V");
            ScaleVehicleWeightSnapshot inbound = await m_client.ReadVehicleWeightsAsync(vehicle.NodeId);
            Assert.That(inbound.TotalWeight.HasValue, Is.True);
            Assert.That(inbound.TotalWeight.Value.Net, Is.Zero);
            scale.PublishLoad(30000);
            await m_client.WeighVehicleAsync(scale.NodeId, BrowseNames.OutboundWeighing, "V");
            scale.PublishLoad(40000);
            await m_client.WeighVehicleAsync(scale.NodeId, BrowseNames.InboundWeighing, "V");
            scale.PublishLoad(15000);
            await m_client.WeighVehicleAsync(scale.NodeId, BrowseNames.OutboundWeighing, "V");
            ScaleVehicleWeightSnapshot snapshot = await m_client.ReadVehicleWeightsAsync(vehicle.NodeId);
            Assert.That(snapshot.ProductNodeId, Is.EqualTo(vehicle.NodeId));
            Assert.That(snapshot.InboundWeight.Value.Net, Is.EqualTo(40000));
            Assert.That(snapshot.OutboundWeight.Value.Net, Is.EqualTo(15000));
            Assert.That(snapshot.DeltaWeight.Value.Net, Is.EqualTo(25000));
            Assert.That(snapshot.TotalWeight.Value.Net, Is.EqualTo(expectedTotal));
            Assert.That(snapshot.TotalWeight.Timestamp, Is.EqualTo((DateTime)vehicle.TotalWeight!.Timestamp));
            Assert.That(snapshot.TotalWeightResetDate.HasValue, Is.True);
            Assert.That(snapshot.TotalWeightResetDate.Value,
                Is.EqualTo((DateTime)vehicle.TotalWeightResetDate!.Value));

            var sourceTime = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            vehicle.TotalWeight.Timestamp = sourceTime;
            vehicle.TotalWeight.StatusCode = StatusCodes.UncertainSubNormal;
            snapshot = await m_client.ReadVehicleWeightsAsync(vehicle.NodeId);
            Assert.That(snapshot.TotalWeight.HasValue, Is.True);
            Assert.That(snapshot.TotalWeight.StatusCode, Is.EqualTo(StatusCodes.UncertainSubNormal));
            Assert.That(snapshot.TotalWeight.Timestamp, Is.EqualTo(sourceTime));
            vehicle.TotalWeight.StatusCode = StatusCodes.BadOutOfService;
            snapshot = await m_client.ReadVehicleWeightsAsync(vehicle.NodeId);
            Assert.That(snapshot.TotalWeight.HasValue, Is.False);
            Assert.That(snapshot.TotalWeight.StatusCode, Is.EqualTo(StatusCodes.BadOutOfService));
            Assert.That(snapshot.TotalWeight.Timestamp, Is.EqualTo(sourceTime));
            ServiceResult reset = await scale.Vehicle!.ResetTotalWeightAsync("V");
            Assert.That(ServiceResult.IsGood(reset), Is.True, reset.ToString());
            snapshot = await m_client.ReadVehicleWeightsAsync(vehicle.NodeId);
            Assert.That(snapshot.TotalWeight.HasValue, Is.True);
            Assert.That(snapshot.TotalWeight.Value.Net, Is.Zero);
        }

        /// <summary>
        /// Missing optional vehicle totals are not presented as measured zero weights.
        /// </summary>
        [Test]
        public async Task ClientVehicleWeightsRetainMissingOptionalTotalsAsync()
        {
            ScaleHandle scale = await m_runtime.CreateScaleAsync(
                new QualifiedName("VehicleWithoutTotals", m_runtime.InstanceNamespaceIndex),
                ScaleKind.Vehicle,
                builder => builder.WithIdentification(
                        ScalesNodeManagerTests.Identity("VehicleWithoutTotals"))
                    .WithTypeFeatures()
                    .WithWeighingRange(new WeighingRangeDefinition(0, 60000, 20, 20))
                    .WithProductionPreset(preset => preset.AddProduct("V", new LocalizedText("Vehicle"))));
            ProductState vehicle = scale.ProductionPreset!.Find("V")!;
            ScaleVehicleWeightSnapshot snapshot = await m_client.ReadVehicleWeightsAsync(vehicle.NodeId);
            Assert.That(snapshot.TotalWeight.HasValue, Is.False);
            Assert.That(StatusCode.IsBad(snapshot.TotalWeight.StatusCode), Is.True);
            Assert.That(snapshot.TotalWeightResetDate.HasValue, Is.False);
            Assert.That(StatusCode.IsBad(snapshot.TotalWeightResetDate.StatusCode), Is.True);
        }

        /// <summary>
        /// Requires application permission on a fresh scale and accepts both supported release paths.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ClientRecipeStartRequiresExplicitReleaseOrAllowingCallbackAsync(bool callback)
        {
            ScaleHandle scale = await m_runtime.CreateScaleAsync(
                new QualifiedName($"NoReleaseCheck{callback}", m_runtime.InstanceNamespaceIndex),
                ScaleKind.Recipe,
                builder => builder
                    .WithIdentification(ScalesNodeManagerTests.Identity($"NoReleaseCheck{callback}"))
                    .WithWeighingRange(new WeighingRangeDefinition(0, 3, 0.001, 0.001))
                    .WithTypeFeatures()
                    .WithProductionPreset(preset => preset
                        .AddProduct("BATCH", new LocalizedText("Batch"))
                        .Select("BATCH")));
            Assert.That(scale.PackML, Is.Null);
            ushort scalesNamespaceIndex = m_session.NamespaceUris.GetIndexOrAppend(Namespaces.Scales);
            NodeId recipe = await m_client.AddRecipeAsync(
                scale.NodeId,
                $"NoReleaseCheck{callback}",
                new LocalizedText("No release check"));
            await m_client.AddRecipeElementAsync(
                recipe,
                new NodeId(ObjectTypes.TimerType, scalesNamespaceIndex),
                "Wait",
                [recipe]);

            RecipeController controller = scale.Recipes!;
            bool released = false;
            if (callback)
            {
                controller.SetApplicationReleaseCheck(() => released);
            }
            ServiceResultException? denied = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_client.ProcessRecipeAsync(scale.NodeId, BrowseNames.StartRecipe, recipe));
            Assert.That(denied!.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
            Assert.That(controller.State, Is.EqualTo(RecipeRunState.Idle));
            Assert.That(controller.CurrentElements.Count, Is.Zero);
            Assert.That(controller.ApplicationReleased, Is.False);
            if (callback)
            {
                released = true;
            }
            else
            {
                await controller.SetApplicationReleaseAsync(true).ConfigureAwait(false);
            }
            bool startAttempted = false;
            try
            {
                startAttempted = true;
                await m_client.ProcessRecipeAsync(scale.NodeId, BrowseNames.StartRecipe, recipe);
                Assert.That(scale.Recipes!.State, Is.EqualTo(RecipeRunState.Running));
            }
            finally
            {
                await AbortRecipeIfRunningAsync(scale, recipe, startAttempted);
            }
            if (callback)
            {
                released = false;
            }
            else
            {
                await controller.SetApplicationReleaseAsync(false).ConfigureAwait(false);
            }
            denied = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_client.ProcessRecipeAsync(scale.NodeId, BrowseNames.StartRecipe, recipe));
            Assert.That(denied!.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
            Assert.That(controller.State, Is.EqualTo(RecipeRunState.Idle));
        }

        /// <summary>
        /// Retains product selection during a remote check and releases it for every check outcome.
        /// </summary>
        [TestCase(0, "Allow")]
        [TestCase(0, "Reject")]
        [TestCase(0, "Throw")]
        [TestCase(2, "Allow")]
        [TestCase(2, "Reject")]
        [TestCase(2, "Throw")]
        public async Task ClientRecipeStartRetainsSelectionAndReleasesReservationAsync(int selected, string outcome)
        {
            string name = $"SelectionReservation{selected}{outcome}";
            ScaleHandle scale = await m_runtime.CreateScaleAsync(
                new QualifiedName(name, m_runtime.InstanceNamespaceIndex),
                ScaleKind.Recipe,
                builder => builder
                    .WithIdentification(ScalesNodeManagerTests.Identity(name))
                    .WithWeighingRange(new WeighingRangeDefinition(0, 3, 0.001, 0.001))
                    .WithTypeFeatures()
                    .WithProductionPreset(preset =>
                    {
                        preset.AllowSelection()
                            .AddProduct("A", new LocalizedText("A"))
                            .AddProduct("B", new LocalizedText("B"));
                        if (selected == 2)
                        {
                            preset.Select("A").Select("B");
                        }
                    }));
            RecipeController controller = scale.Recipes!;
            ScaleProductionPreset productionPreset = scale.ProductionPreset!;
            NodeId recipe = await m_client.AddRecipeAsync(scale.NodeId, name, new LocalizedText(name));
            ushort ns = m_session.NamespaceUris.GetIndexOrAppend(Namespaces.Scales);
            await m_client.AddRecipeElementAsync(recipe, new NodeId(ObjectTypes.TimerType, ns), "Wait", [recipe]);
            using var entered = new ManualResetEventSlim(false);
            using var release = new ManualResetEventSlim(false);
            bool released = false;
            int intercepted = 0;
            scale.CommandInterceptor = command =>
            {
                if (command == ScaleCommand.StartRecipe)
                {
                    Interlocked.Increment(ref intercepted);
                }
                return ServiceResult.Good;
            };
            controller.SetApplicationReleaseCheck(() =>
            {
                entered.Set();
                released = release.Wait(TimeSpan.FromSeconds(20));
                if (outcome == "Throw")
                {
                    throw new InvalidOperationException("Release provider failed.");
                }
                return released && outcome == "Allow";
            });
            Task start = m_client.ProcessRecipeAsync(scale.NodeId, BrowseNames.StartRecipe, recipe).AsTask();
            bool callbackEntered = entered.Wait(TimeSpan.FromSeconds(5));
            ServiceResult? selectionResult = null;
            Task<ServiceResult>? selection = null;
            bool selectionCompletedDuringCheck = false;
            try
            {
                if (callbackEntered)
                {
                    selection = Task.Run(() => selected == 0
                        ? productionPreset.Select("A")
                        : productionPreset.Deselect("A"));
                    selectionResult = await selection.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    selectionCompletedDuringCheck = true;
                }
            }
            catch (TimeoutException)
            {
            }
            finally
            {
                release.Set();
            }
            if (selection is not null)
            {
                selectionResult = await selection.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            }
            ServiceResultException? failure = null;
            try
            {
                await start.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
            }
            catch (ServiceResultException exception)
            {
                failure = exception;
            }
            try
            {
                Assert.That(callbackEntered, Is.True);
                Assert.That(selectionCompletedDuringCheck, Is.True);
                Assert.That(released, Is.True);
                Assert.That(selectionResult?.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
                Assert.That(productionPreset.CurrentProducts.Count, Is.EqualTo(selected));
                Assert.That(productionPreset.ActiveProduct, Is.Null);
                Assert.That(intercepted, Is.EqualTo(1));
                if (outcome == "Allow")
                {
                    Assert.That(failure, Is.Null);
                    Assert.That(controller.State, Is.EqualTo(RecipeRunState.Running));
                }
                else
                {
                    Assert.That(failure, Is.Not.Null);
                    Assert.That(StatusCode.IsBad(failure!.StatusCode), Is.True);
                    Assert.That(controller.State, Is.EqualTo(RecipeRunState.Idle));
                }
                ServiceResult after = selected == 0
                    ? productionPreset.Select("A")
                    : productionPreset.Deselect("A");
                Assert.That(ServiceResult.IsGood(after), Is.True, "the reservation is released after every outcome");
                Assert.That(productionPreset.ActiveProduct, Is.Not.Null);
            }
            finally
            {
                await AbortRecipeIfRunningAsync(scale, recipe, startAttempted: true);
            }
        }

        /// <summary>
        /// Allows callback coordination with scale and recipe reads and rejects callback configuration changes.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ClientReleaseCheckRunsOutsideBothLocksAndRejectsReplacementAsync(bool restoreOriginal)
        {
            string name = $"ReleaseCheckRace{restoreOriginal}";
            ScaleHandle scale = await m_runtime.CreateScaleAsync(
                new QualifiedName(name, m_runtime.InstanceNamespaceIndex),
                ScaleKind.Recipe,
                builder =>
                {
                    builder
                        .WithIdentification(ScalesNodeManagerTests.Identity(name))
                        .WithWeighingRange(new WeighingRangeDefinition(0, 3, 0.001, 0.001))
                        .WithTypeFeatures()
                        .WithRecipeFiles()
                        .WithProductionPreset(preset => preset
                            .AddProduct("BATCH", new LocalizedText("Batch"))
                            .Select("BATCH"));
                });
            RecipeController controller = scale.Recipes!;
            Assert.That(scale.PackML, Is.Null);
            ushort scalesNamespaceIndex = m_session.NamespaceUris.GetIndexOrAppend(Namespaces.Scales);
            NodeId recipe = await m_client.AddRecipeAsync(
                scale.NodeId,
                "ReleaseCheckRace",
                new LocalizedText("Release check race"));
            await m_client.AddRecipeElementAsync(
                recipe,
                new NodeId(ObjectTypes.TimerType, scalesNamespaceIndex),
                "Wait",
                [recipe]);

            using var callbackEntered = new ManualResetEventSlim(false);
            using var releaseCallback = new ManualResetEventSlim(false);
            using var scaleLockReleased = new ManualResetEventSlim(false);
            using var controllerLockReleased = new ManualResetEventSlim(false);
            using var scaleWriteCompleted = new ManualResetEventSlim(false);
            Task? scaleLockProbe = null;
            Task? controllerLockProbe = null;
            Task? scaleWriteProbe = null;
            bool callbackAllowed = false;
            Func<bool> originalCheck = () =>
            {
                scaleLockProbe = Task.Run(() =>
                {
                    _ = scale.CurrentReading;
                    scaleLockReleased.Set();
                });
                controllerLockProbe = Task.Run(() =>
                {
                    _ = controller.CurrentElements;
                    controllerLockReleased.Set();
                });
                scaleWriteProbe = Task.Run(() =>
                {
                    scale.PublishLoad(1);
                    scaleWriteCompleted.Set();
                });
                callbackEntered.Set();
                callbackAllowed = releaseCallback.Wait(TimeSpan.FromSeconds(20)) &&
                    scaleLockReleased.Wait(TimeSpan.FromSeconds(5)) &&
                    controllerLockReleased.Wait(TimeSpan.FromSeconds(5)) &&
                    scaleWriteCompleted.Wait(TimeSpan.FromSeconds(5));
                return callbackAllowed;
            };
            controller.SetApplicationReleaseCheck(originalCheck);

            Task start = m_client.ProcessRecipeAsync(scale.NodeId, BrowseNames.StartRecipe, recipe).AsTask();
            bool entered = callbackEntered.Wait(TimeSpan.FromSeconds(5));
            bool scaleLockAvailable = false;
            bool controllerLockAvailable = false;
            bool scaleWriteAvailable = false;
            Task? replaceCheck = null;
            bool replaced = false;
            ServiceResultException? failure = null;
            try
            {
                if (entered)
                {
                    scaleLockAvailable = scaleLockReleased.Wait(TimeSpan.FromSeconds(5));
                    controllerLockAvailable = controllerLockReleased.Wait(TimeSpan.FromSeconds(5));
                    scaleWriteAvailable = scaleWriteCompleted.Wait(TimeSpan.FromSeconds(5));
                    replaceCheck = Task.Run(() =>
                    {
                        controller.SetApplicationReleaseCheck(() => false);
                        if (restoreOriginal)
                        {
                            controller.SetApplicationReleaseCheck(originalCheck);
                        }
                    });
                    try
                    {
                        await replaceCheck.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                        replaced = true;
                    }
                    catch (TimeoutException)
                    {
                    }
                }
            }
            finally
            {
                releaseCallback.Set();
            }
            var cleanupTasks = new List<Task> { start };
            if (replaceCheck is not null)
            {
                cleanupTasks.Add(replaceCheck);
            }
            if (scaleLockProbe is not null)
            {
                cleanupTasks.Add(scaleLockProbe);
            }
            if (controllerLockProbe is not null)
            {
                cleanupTasks.Add(controllerLockProbe);
            }
            if (scaleWriteProbe is not null)
            {
                cleanupTasks.Add(scaleWriteProbe);
            }
            try
            {
                await Task.WhenAll(cleanupTasks).WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            }
            catch (ServiceResultException exception)
            {
                failure = exception;
            }

            Assert.That(entered, Is.True);
            Assert.That(scaleLockAvailable, Is.True);
            Assert.That(controllerLockAvailable, Is.True);
            Assert.That(scaleWriteAvailable, Is.True);
            Assert.That(replaced, Is.True);
            Assert.That(callbackAllowed, Is.True);
            Assert.That(failure?.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
            Assert.That(controller.State, Is.EqualTo(RecipeRunState.Idle));
        }

        [Test]
        public async Task ClientBuildsAndProcessesARecipeAsync()
        {
            ScaleHandle scale = m_scales["Recipe"];
            ushort ns = m_session.NamespaceUris.GetIndexOrAppend(Namespaces.Scales);
            NodeId recipe = await m_client.AddRecipeAsync(scale.NodeId, "Bread", new LocalizedText("Bread"));
            Assert.That(recipe.IsNull, Is.False);
            var product = (RecipeProductState)scale.ProductionPreset!.ActiveProduct!;
            product.RecipeNodeId!.Value = recipe;
            NodeId weighing = await m_client.AddRecipeElementAsync(recipe, new NodeId(ObjectTypes.WeighingType, ns), "Flour", [recipe]);
            await m_client.AddRecipeElementAsync(recipe, new NodeId(ObjectTypes.TimerType, ns), "Rest", [weighing]);

            await m_client.ProcessRecipeAsync(scale.NodeId, BrowseNames.StartRecipe, recipe);
            Assert.That(scale.Recipes!.State, Is.EqualTo(RecipeRunState.Running));
            await m_client.ProcessRecipeAsync(scale.NodeId, BrowseNames.StopRecipe, recipe);
            await m_client.ProcessRecipeAsync(scale.NodeId, BrowseNames.ContinueRecipe, recipe);
            await m_client.ProcessRecipeAsync(scale.NodeId, BrowseNames.SkipCurrentRecipeElement, recipe);
            Assert.That(scale.Recipes.CurrentElements.ToArray()!.Single().BrowseName.Name, Is.EqualTo("Rest"));
            await m_client.ProcessRecipeAsync(scale.NodeId, BrowseNames.AbortRecipe, recipe);
            Assert.That(scale.Recipes.State, Is.EqualTo(RecipeRunState.Idle));
            ScaleRecipeStatus status = await m_client.ReadRecipeStatusAsync(product.NodeId);
            Assert.That(status.RecipeNodeId.HasValue, Is.True);
            Assert.That(status.RecipeNodeId.Value, Is.EqualTo(recipe));
            Assert.That(status.Report.HasValue, Is.True);
            Assert.That(status.Report.Value.Count, Is.GreaterThan(2));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            bool receivedReport = false;
            await foreach (ScaleField<ArrayOf<RecipeReportElementType>> report in m_client.ObserveRecipeReportsAsync(
                product.NodeId, cancellationToken: timeout.Token))
            {
                Assert.That(report.HasValue, Is.True);
                Assert.That(report.Value.Count, Is.EqualTo(status.Report.Value.Count));
                Assert.That(report.StatusCode, Is.EqualTo(status.Report.StatusCode));
                Assert.That(report.Timestamp, Is.EqualTo(status.Report.Timestamp));
                receivedReport = true;
                break;
            }
            Assert.That(receivedReport, Is.True);
        }

        /// <summary>
        /// Verifies an analog condition blocks a recipe until the registered source satisfies it.
        /// </summary>
        [Test]
        public async Task ClientRecipeWaitsForAnalogSourceBeforeProgressingAsync()
        {
            ushort scalesNamespaceIndex = m_session.NamespaceUris.GetIndexOrAppend(Namespaces.Scales);
            ScaleHandle scale = m_scales["Recipe"];
            NodeId recipe = await m_client.AddRecipeAsync(
                scale.NodeId, "AnalogCondition", new LocalizedText("Analog condition"));
            NodeId conditionId = await m_client.AddRecipeElementAsync(
                recipe,
                new NodeId(ObjectTypes.AnalogConditionSleepType, scalesNamespaceIndex),
                "AnalogWait",
                [recipe]);
            NodeId nextId = await m_client.AddRecipeElementAsync(
                recipe,
                new NodeId(ObjectTypes.TimerType, scalesNamespaceIndex),
                "AfterAnalogWait",
                [conditionId]);
            Assert.That(conditionId.IsNull || nextId.IsNull, Is.False);

            var product = (RecipeProductState)scale.ProductionPreset!.ActiveProduct!;
            NodeId previousRecipe = product.RecipeNodeId!.Value;
            bool startAttempted = false;
            try
            {
                product.RecipeNodeId!.Value = recipe;
                await WriteAnalogConditionAsync(conditionId);
                m_conditionClock.Advance(TimeSpan.FromMilliseconds(1));
                ObserveConditionSource(s_analogConditionSourceId, Variant.From(0.0));

                startAttempted = true;
                await m_client.ProcessRecipeAsync(scale.NodeId, BrowseNames.StartRecipe, recipe);
                string[] waitingReport = await ReadRecipeMessagesAsync(product.NodeId);
                Assert.That(waitingReport, Does.Contain("Element 'AnalogWait' started."));
                Assert.That(waitingReport, Does.Not.Contain("Element 'AnalogWait' condition satisfied."));
                Assert.That(waitingReport, Does.Not.Contain("Element 'AfterAnalogWait' started."));

                m_conditionClock.Advance(TimeSpan.FromMilliseconds(1));
                ObserveConditionSource(s_analogConditionSourceId, Variant.From(10.0));
                string[] progressedReport = await WaitForRecipeMessageAsync(
                    product.NodeId,
                    "Element 'AfterAnalogWait' started.");
                Assert.That(progressedReport, Does.Contain("Element 'AnalogWait' condition satisfied."));
            }
            finally
            {
                try
                {
                    await AbortRecipeIfRunningAsync(scale, recipe, startAttempted);
                }
                finally
                {
                    product.RecipeNodeId.Value = previousRecipe;
                }
            }
        }

        /// <summary>
        /// Verifies a Boolean edge condition blocks a recipe until the registered source changes.
        /// </summary>
        [Test]
        public async Task ClientRecipeWaitsForBooleanEdgeBeforeProgressingAsync()
        {
            ushort scalesNamespaceIndex = m_session.NamespaceUris.GetIndexOrAppend(Namespaces.Scales);
            ScaleHandle scale = m_scales["Recipe"];
            NodeId recipe = await m_client.AddRecipeAsync(
                scale.NodeId, "EdgeCondition", new LocalizedText("Edge condition"));
            NodeId conditionId = await m_client.AddRecipeElementAsync(
                recipe,
                new NodeId(ObjectTypes.EdgeTriggeredSleepType, scalesNamespaceIndex),
                "EdgeWait",
                [recipe]);
            NodeId nextId = await m_client.AddRecipeElementAsync(
                recipe,
                new NodeId(ObjectTypes.TimerType, scalesNamespaceIndex),
                "AfterEdgeWait",
                [conditionId]);
            Assert.That(conditionId.IsNull || nextId.IsNull, Is.False);

            var product = (RecipeProductState)scale.ProductionPreset!.ActiveProduct!;
            NodeId previousRecipe = product.RecipeNodeId!.Value;
            bool startAttempted = false;
            try
            {
                product.RecipeNodeId!.Value = recipe;
                await WriteEdgeConditionAsync(conditionId);
                m_conditionClock.Advance(TimeSpan.FromMilliseconds(1));
                ObserveConditionSource(s_edgeConditionSourceId, Variant.From(false));

                startAttempted = true;
                await m_client.ProcessRecipeAsync(scale.NodeId, BrowseNames.StartRecipe, recipe);
                string[] waitingReport = await ReadRecipeMessagesAsync(product.NodeId);
                Assert.That(waitingReport, Does.Contain("Element 'EdgeWait' started."));
                Assert.That(waitingReport, Does.Not.Contain("Element 'EdgeWait' condition satisfied."));
                Assert.That(waitingReport, Does.Not.Contain("Element 'AfterEdgeWait' started."));

                m_conditionClock.Advance(TimeSpan.FromMilliseconds(1));
                ObserveConditionSource(s_edgeConditionSourceId, Variant.From(true));
                string[] progressedReport = await WaitForRecipeMessageAsync(
                    product.NodeId,
                    "Element 'AfterEdgeWait' started.");
                Assert.That(progressedReport, Does.Contain("Element 'EdgeWait' condition satisfied."));
            }
            finally
            {
                try
                {
                    await AbortRecipeIfRunningAsync(scale, recipe, startAttempted);
                }
                finally
                {
                    product.RecipeNodeId.Value = previousRecipe;
                }
            }
        }

        /// <summary>
        /// Writes analog wait settings through the connected OPC UA client session.
        /// </summary>
        private async Task WriteAnalogConditionAsync(NodeId conditionId)
        {
            var condition = (AnalogConditionSleepState)m_runtime.Manager.FindPredefinedNode(conditionId)!;
            condition.TargetThresholdValue!.EngineeringUnits!.Value = ScaleUnits.Kilogram;
            await WriteConditionSettingsAsync(
                [
                    new WriteValue
                    {
                        NodeId = condition.ThresholdValueId!.NodeId,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(Variant.From(
                            s_analogConditionSourceId.ToString(CultureInfo.InvariantCulture)))
                    },
                    new WriteValue
                    {
                        NodeId = condition.TargetThresholdValue!.NodeId,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(Variant.From(10.0))
                    },
                    new WriteValue
                    {
                        NodeId = condition.ConditionMode!.NodeId,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(Variant.From((int)EqualityAndRelationalOperator.Equal_0))
                    }
                ]);
        }

        /// <summary>
        /// Writes rising-edge wait settings through the connected OPC UA client session.
        /// </summary>
        private async Task WriteEdgeConditionAsync(NodeId conditionId)
        {
            var condition = (EdgeTriggeredSleepState)m_runtime.Manager.FindPredefinedNode(conditionId)!;
            await WriteConditionSettingsAsync(
                [
                    new WriteValue
                    {
                        NodeId = condition.ThresholdValueId!.NodeId,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(Variant.From(
                            s_edgeConditionSourceId.ToString(CultureInfo.InvariantCulture)))
                    },
                    new WriteValue
                    {
                        NodeId = condition.TargetThresholdValue!.NodeId,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(Variant.From(true))
                    },
                    new WriteValue
                    {
                        NodeId = condition.ConditionMode!.NodeId,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(Variant.From((int)EdgeOperator.Rising_0))
                    }
                ]);
        }

        /// <summary>
        /// Verifies the service write succeeds and the native async core commits each requested setting.
        /// </summary>
        private async Task WriteConditionSettingsAsync(WriteValue[] writes)
        {
            WriteResponse response = await m_session.WriteAsync(
                null, writes.ToArrayOf(), CancellationToken.None);

            Assert.That(response.Results.Count, Is.EqualTo(writes.Length));
            for (int ii = 0; ii < writes.Length; ii++)
            {
                Assert.That(response.Results[ii], Is.EqualTo((StatusCode)StatusCodes.Good));
                var node = (BaseVariableState)m_runtime.Manager.FindPredefinedNode(writes[ii].NodeId)!;
                Assert.That(node.WrappedValue, Is.EqualTo(writes[ii].Value.WrappedValue));
                Assert.That(node.StatusCode, Is.EqualTo(writes[ii].Value.StatusCode));
            }
        }

        /// <summary>
        /// Publishes a fresh application-supplied sample to the recipe controller.
        /// </summary>
        private void ObserveConditionSource(uint sourceId, Variant value)
        {
            DateTimeUtc timestamp = (DateTimeUtc)m_conditionClock.GetUtcNow().UtcDateTime;
            var sample = new DataValue(value, StatusCodes.Good, timestamp);
            m_scales["Recipe"].Recipes!.ObserveConditionSource(sourceId, sample);
        }

        /// <summary>
        /// Aborts a recipe left active by a test assertion or timeout.
        /// </summary>
        private async Task AbortRecipeIfRunningAsync(
            ScaleHandle scale, NodeId recipe, bool startAttempted)
        {
            RecipeState? activeRecipe = scale.Recipes!.ActiveRecipe;
            if (startAttempted && activeRecipe != null && activeRecipe.NodeId == recipe)
            {
                await m_client.ProcessRecipeAsync(scale.NodeId, BrowseNames.AbortRecipe, recipe);
            }
        }

        /// <summary>
        /// Reads recipe progress from the active product through the typed client.
        /// </summary>
        private async Task<string[]> ReadRecipeMessagesAsync(
            NodeId productId, CancellationToken cancellationToken = default)
        {
            ScaleRecipeStatus status = await m_client.ReadRecipeStatusAsync(productId, cancellationToken);
            Assert.That(status.Report.HasValue, Is.True);
            Assert.That(status.Report.Value.IsNull, Is.False);
            return status.Report.Value.ToList()
                .Select(entry => entry.ReportMessage.Text ?? string.Empty)
                .ToArray();
        }

        /// <summary>
        /// Polls the typed client report until the expected recipe transition is visible.
        /// </summary>
        private async Task<string[]> WaitForRecipeMessageAsync(
            NodeId productId, string expectedMessage)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (true)
            {
                string[] messages = await ReadRecipeMessagesAsync(productId, timeout.Token);
                if (messages.Contains(expectedMessage, StringComparer.Ordinal))
                {
                    return messages;
                }
                await Task.Delay(TimeSpan.FromMilliseconds(20), timeout.Token);
            }
        }

        /// <summary>
        /// Real service writes validate generated notification enums and commit their matching display text.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ClientWritesGeneratedNotificationEnumsAndRejectsSparseGapsAsync(bool category)
        {
            ScaleHandle scale = m_scales["Hopper"];
            ScaleAlarmState alarm = scale.Notifications.EnsureAlarm(ScaleNotificationId.GeneralMemoryFault);
            MultiStateValueDiscreteState variable = category
                ? alarm.NotificationCategory!
                : alarm.NotificationId!;
            PropertyState<LocalizedText> text = variable.ValueAsText!;
            Assert.That(m_runtime.Manager.FindPredefinedNode(variable.NodeId), Is.SameAs(variable));
            Assert.That(m_runtime.Manager.FindPredefinedNode(text.NodeId), Is.SameAs(text));
            Assert.That(variable.OnWriteValueAsync, Is.Not.Null);
            ArrayOf<EnumValueType> entries = variable.EnumValues!.Value;
            Assert.That(entries.Count, Is.GreaterThan(1));
            uint originalValue = variable.WrappedValue.GetUInt32();
            LocalizedText originalText = text.Value;
            StatusCode originalStatus = variable.StatusCode;
            DateTimeUtc originalTimestamp = variable.Timestamp;
            uint rejectedValue = checked((uint)entries.ToList().Min(entry => entry.Value));
            while (entries.ToList().Any(entry => entry.Value == rejectedValue))
            {
                rejectedValue++;
            }
            Assert.That(entries.ToList().Any(entry => entry.Value == rejectedValue), Is.False);
            if (!category)
            {
                Assert.That(rejectedValue, Is.LessThan(entries.ToList().Max(entry => entry.Value)),
                    "NotificationId must exercise an interior sparse gap, not just an upper bound.");
            }
            EnumValueType accepted = entries.ToList().First(entry => entry.Value != originalValue);
            uint acceptedValue = checked((uint)accepted.Value);

            try
            {
                WriteResponse rejected = await m_session.WriteAsync(
                    null,
                    new[]
                    {
                        new WriteValue
                        {
                            NodeId = variable.NodeId,
                            AttributeId = Attributes.Value,
                            Value = new DataValue(Variant.From(rejectedValue))
                        }
                    }.ToArrayOf(),
                    CancellationToken.None);

                Assert.That(rejected.Results.Count, Is.EqualTo(1));
                Assert.That(rejected.Results[0], Is.EqualTo((StatusCode)StatusCodes.BadOutOfRange));
                Assert.That(variable.WrappedValue.GetUInt32(), Is.EqualTo(originalValue));
                Assert.That(variable.StatusCode, Is.EqualTo(originalStatus));
                Assert.That(variable.Timestamp, Is.EqualTo(originalTimestamp));
                Assert.That(text.Value, Is.EqualTo(originalText));

                WriteResponse written = await m_session.WriteAsync(
                    null,
                    new[]
                    {
                        new WriteValue
                        {
                            NodeId = variable.NodeId,
                            AttributeId = Attributes.Value,
                            Value = new DataValue(Variant.From(acceptedValue))
                        }
                    }.ToArrayOf(),
                    CancellationToken.None);

                Assert.That(written.Results.Count, Is.EqualTo(1));
                Assert.That(written.Results[0], Is.EqualTo((StatusCode)StatusCodes.Good));
                Assert.That(variable.WrappedValue.GetUInt32(), Is.EqualTo(acceptedValue));
                Assert.That(variable.StatusCode, Is.EqualTo((StatusCode)StatusCodes.Good));
                Assert.That(text.Value, Is.EqualTo(accepted.DisplayName));

                WriteResponse rejectedAfterCommit = await m_session.WriteAsync(
                    null,
                    new[]
                    {
                        new WriteValue
                        {
                            NodeId = variable.NodeId,
                            AttributeId = Attributes.Value,
                            Value = new DataValue(Variant.From(rejectedValue))
                        }
                    }.ToArrayOf(),
                    CancellationToken.None);

                Assert.That(rejectedAfterCommit.Results.Count, Is.EqualTo(1));
                Assert.That(rejectedAfterCommit.Results[0], Is.EqualTo((StatusCode)StatusCodes.BadOutOfRange));
                Assert.That(variable.WrappedValue.GetUInt32(), Is.EqualTo(acceptedValue));
                Assert.That(text.Value, Is.EqualTo(accepted.DisplayName));
            }
            finally
            {
                variable.WrappedValue = Variant.From(originalValue);
                variable.StatusCode = originalStatus;
                variable.Timestamp = originalTimestamp;
                text.Value = originalText;
                await variable.ClearChangeMasksAsync(m_runtime.Manager.SystemContext, true);
            }
        }

        [Test]
        public async Task ClientStreamsWeightsEventsAndAlarmsAsync()
        {
            ScaleHandle scale = m_scales["Hopper"];
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            ScaleReading? streamed = null;
            await foreach (ScaleReading reading in m_client.ObserveWeightAsync(scale.NodeId, cancellationToken: timeout.Token))
            {
                bool target = Math.Abs(reading.Gross - 3.3) < 1e-9;
                if (streamed == null && !target)
                {
                    scale.PublishLoad(3.3);
                    streamed = reading;
                    continue;
                }
                if (target)
                {
                    streamed = reading;
                    break;
                }
            }
            Assert.That(streamed!.Gross, Is.EqualTo(3.3).Within(1e-9));

            var received = new List<ScaleNotificationInfo>();
            using var pump = new CancellationTokenSource();
            Task raiser = Task.Run(async () =>
            {
                // An event subscription has no initial notification, so the
                // event is raised until the monitored item has seen one.
                while (!pump.IsCancellationRequested)
                {
                    scale.Notifications.RaiseEvent(ScaleNotificationId.LabelFault, new LocalizedText("jam"), EventSeverity.High, "P1");
                    scale.PublishLoad(9.0);
                    scale.PublishLoad(1.0);
                    await Task.Delay(200, CancellationToken.None);
                }
            });
            try
            {
                await foreach (ScaleNotificationInfo notification in m_client.ObserveNotificationsAsync(
                    scale.NodeId, cancellationToken: timeout.Token))
                {
                    received.Add(notification);
                    if (received.Any(n => !n.IsAlarm) && received.Any(n => n.IsAlarm))
                    {
                        break;
                    }
                }
            }
            finally
            {
                pump.Cancel();
                await raiser;
            }

            ScaleNotificationInfo @event = received.First(n => !n.IsAlarm);
            Assert.That(@event.DefinedId, Is.EqualTo(ScaleNotificationId.LabelFault));
            Assert.That(@event.Category, Is.EqualTo(ScaleNotificationCategory.Process));
            string[] auxParameters = ["P1"];
            Assert.That(@event.AuxParameters, Is.EqualTo(auxParameters));
            Assert.That(@event.SourceNode, Is.EqualTo(scale.NodeId));
            ScaleNotificationInfo alarm = received.First(n => n.IsAlarm);
            Assert.That(alarm.NotificationId, Is.AnyOf(601u, 603u));
            Assert.That(alarm.Category, Is.EqualTo(ScaleNotificationCategory.WeighingModule));
            Assert.That(alarm.Active, Is.Not.Null);
        }

        [Test]
        public async Task ClientSubscribesToANestedScaleThroughTheNotifierHierarchyAsync()
        {
            ScaleHandle checker = m_system!.Scales[0];
            var browser = new Browser(m_session, new BrowserOptions
            {
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = Opc.Ua.Types.ReferenceTypeIds.HasNotifier,
                IncludeSubtypes = false,
                NodeClassMask = (int)NodeClass.Object,
                ResultMask = (uint)BrowseResultMask.All
            });
            ArrayOf<ReferenceDescription> notifiers = await browser.BrowseAsync(m_system.NodeId);
            Assert.That(
                notifiers.ToArray()!.Select(r => ExpandedNodeId.ToNodeId(r.NodeId, m_session.NamespaceUris)),
                Does.Contain(checker.NodeId));

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var pump = new CancellationTokenSource();
            Task raiser = Task.Run(async () =>
            {
                while (!pump.IsCancellationRequested)
                {
                    checker.Notifications.RaiseEvent(ScaleNotificationId.LabelFault, new LocalizedText("nested"), EventSeverity.Low);
                    await Task.Delay(200, CancellationToken.None);
                }
            });
            ScaleNotificationInfo? received = null;
            try
            {
                await foreach (ScaleNotificationInfo notification in m_client.ObserveNotificationsAsync(
                    checker.NodeId, cancellationToken: timeout.Token))
                {
                    if (!notification.IsAlarm)
                    {
                        received = notification;
                        break;
                    }
                }
            }
            finally
            {
                pump.Cancel();
                await raiser;
            }

            Assert.That(received, Is.Not.Null);
            Assert.That(received!.SourceNode, Is.EqualTo(checker.NodeId));
            Assert.That(received.DefinedId, Is.EqualTo(ScaleNotificationId.LabelFault));
        }

        [Test]
        public async Task ClientTakesASnapshotAsync()
        {
            ScaleEntry entry = (await m_client.DiscoverScalesAsync()).ToArray()!
                .Single(s => s.BrowseName.Name == "Simple");
            ScaleSnapshot snapshot = await m_client.ReadSnapshotAsync(entry);
            Assert.That(snapshot.Scale, Is.SameAs(entry));
            Assert.That(snapshot.Identification.SerialNumber, Is.EqualTo("Simple"));
            Assert.That(snapshot.WeighingRanges, Has.Count.EqualTo(2));
            Assert.That(snapshot.Products, Is.Not.Empty);
            Assert.That(snapshot.PackMLState, Is.Not.Null);
            Assert.That(snapshot.RegisteredWeight, Is.Not.Null);
            Assert.That(() => m_client.ReadSnapshotAsync(null!).AsTask(), Throws.ArgumentNullException);
            Assert.That(
                () => m_client.ReadSnapshotAsync(entry with { Kind = null, IsScaleSystem = true }).AsTask(),
                Throws.ArgumentException);
        }

        [Test]
        public void ClientHostingRegistersTheFactory()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOpcUa().AddClient(_ => { }).AddScalesClient();
            using ServiceProvider provider = services.BuildServiceProvider();
            Assert.That(provider.GetService<ScalesClientFactory>(), Is.Not.Null);
            Assert.That(provider.GetService<Func<CancellationToken, Task<ScalesClient>>>(), Is.Not.Null);
            Assert.That(() => new ScalesClientFactory(null!, NUnitTelemetryContext.Create()), Throws.ArgumentNullException);
            Assert.That(() => ((Opc.Ua.Client.ISession)null!).Scales(NUnitTelemetryContext.Create()), Throws.ArgumentNullException);
        }

        /// <summary>
        /// Rejects null zone units with the engineeringUnits parameter name.
        /// </summary>
        [Test]
        public async Task AddZoneAsyncRejectsNullEngineeringUnits()
        {
            ArgumentNullException? error = await Assert.ThrowsAsync<ArgumentNullException>(
                async () => await m_client.AddZoneAsync(NodeId.Null, new LocalizedText("Zone"), 1, 2, null!));

            Assert.That(error!.ParamName, Is.EqualTo("engineeringUnits"));
        }

        /// <summary>
        /// Rejects a null recipe identifier with the recipeId parameter name.
        /// </summary>
        [Test]
        public async Task RemoveRecipeAsyncRejectsNullRecipeId()
        {
            ArgumentNullException? error = await Assert.ThrowsAsync<ArgumentNullException>(
                async () => await m_client.RemoveRecipeAsync(NodeId.Null, null!));

            Assert.That(error!.ParamName, Is.EqualTo("recipeId"));
        }

        [Test]
        public void ServerHostingRefusesASecondDiOwner()
        {
            var services = new ServiceCollection();
            IOpcUaServerBuilder builder = services.AddOpcUa().AddServer<StandardServer>(_ => { }).AddScales();
            Assert.That(() => builder.AddScales(), Throws.InvalidOperationException);
            Assert.That(() => builder.ConfigureScales((Action<Server.Hosting.IScalesSetupContext>)null!), Throws.ArgumentNullException);
            Assert.That(() => new ScalesServerOptions { ZeroSettingRange = 2 }.GetType(), Throws.Nothing);
            Assert.That(
                () => new ServiceCollection().AddOpcUa().AddServer<StandardServer>(_ => { }).AddScales(o => o.ZeroSettingRange = 2),
                Throws.InstanceOf<ArgumentOutOfRangeException>());
        }

        private static void ConfigureServer(OpcUaServerOptions options)
        {
            string applicationName = nameof(ScalesClientServerE2eTests);
            // Leave room for ECC certificate filenames under deep net48 artifact paths.
            string testRoot = System.IO.Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "scales",
                Guid.NewGuid().ToString("N"));
            options.ApplicationName = applicationName;
            options.ApplicationUri = "urn:localhost:" + applicationName;
            options.ProductUri = "urn:localhost:" + applicationName + ":product";
            options.PkiRoot = System.IO.Path.Combine(testRoot, "pki");
            options.AutoAcceptUntrustedCertificates = true;
            options.IncludeUnsecurePolicyNone = true;
            options.EndpointUrls.Clear();
            s_endpointUrl = "opc.tcp://localhost:" +
                GetAvailablePort().ToString(CultureInfo.InvariantCulture) + "/" + applicationName;
            options.EndpointUrls.Add(s_endpointUrl);
        }

        private static int GetAvailablePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
        }
    }
}
