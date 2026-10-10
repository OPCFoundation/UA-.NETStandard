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
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.PackML;
using Opc.Ua.Scales.Client;
using Opc.Ua.Scales.Server;
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
    [TestFixture]
    [Category("Scales")]
    [Category("Hosting")]
    [NonParallelizable]
    public sealed class ScalesClientServerE2eTests
    {
        private static string s_endpointUrl = string.Empty;
        private readonly Dictionary<string, ScaleHandle> m_scales = [];
        private ScaleSystemHandle? m_system;
        private ServiceProvider m_provider = null!;
        private IHostedService m_host = null!;
        private ManagedSession m_session = null!;
        private ScalesClient m_client = null!;

        [OneTimeSetUp]
        public async Task StartAsync()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOpcUa()
                .AddServer<StandardServer>(ConfigureServer)
                .AddScales(options => options.ZeroSettingRange = 0.04)
                .ConfigureScales(async context =>
                {
                    ScalesNodeManager manager = context.Manager;
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
                        context.CancellationToken);
                    m_scales["Pieces"] = await manager.CreateScaleAsync(
                        Name("Pieces"),
                        ScaleKind.PieceCounting,
                        b =>
                        {
                            ScalesNodeManagerTests.FullScale(b, "Pieces");
                            b.WithAllowedEngineeringUnits(ScaleUnits.Kilogram, ScaleUnits.Gram)
                                .WithProductionPreset(p => p.AllowSelection().AddProduct("Screws", new LocalizedText("M4")).Select("Screws"));
                        },
                        context.CancellationToken);
                    m_scales["Lab"] = await manager.CreateScaleAsync(
                        Name("Lab"),
                        ScaleKind.Laboratory,
                        b => ScalesNodeManagerTests.FullScale(b, "Lab"),
                        context.CancellationToken);
                    m_scales["Truck"] = await manager.CreateScaleAsync(
                        Name("Truck"),
                        ScaleKind.Vehicle,
                        b =>
                        {
                            ScalesNodeManagerTests.FullScale(b, "Truck");
                            b.WithWeighingRange(new WeighingRangeDefinition(0, 60000, 20, 20))
                                .WithProductionPreset(p => p.AddProduct("T1", new LocalizedText("Truck 1")));
                        },
                        context.CancellationToken);
                    m_scales["Recipe"] = await manager.CreateScaleAsync(
                        Name("Recipe"),
                        ScaleKind.Recipe,
                        b => ScalesNodeManagerTests.FullScale(b, "Recipe"),
                        context.CancellationToken);
                    m_scales["Hopper"] = await manager.CreateScaleAsync(
                        Name("Hopper"),
                        ScaleKind.LossInWeight,
                        b => ScalesNodeManagerTests.FullScale(b, "Hopper"),
                        context.CancellationToken);
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
                                    .WithFeederSpeed(0.2, 2.0, ScalesTestUnits.MetrePerSecond, 1.0));
                            }),
                        context.CancellationToken);
                });

            m_provider = services.BuildServiceProvider();
            m_host = m_provider.GetServices<IHostedService>().Single();
            await m_host.StartAsync(CancellationToken.None);
            // The hosted service boots the server and runs ConfigureScales in
            // the background; the address space is complete once the last
            // configurator statement - the scale system - has run.
            for (int ii = 0; ii < 450 && m_system == null; ii++)
            {
                if (m_host is BackgroundService background && background.ExecuteTask is { IsFaulted: true } task)
                {
                    Assert.Fail("The hosted server failed to start: " + task.Exception);
                }
                await Task.Delay(200);
            }
            Assert.That(m_system, Is.Not.Null, "ConfigureScales completed");

            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var clientFixture = new ClientFixture(telemetry);
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

        [Test]
        public async Task ClientDiscoversEveryScaleOnceWithItsKindAsync()
        {
            Assert.That(m_client.IsSupported, Is.True);
            ScaleEntry[] scales = (await m_client.DiscoverScalesAsync()).ToArray()!;
            Assert.That(scales.Count(s => !s.IsScaleSystem), Is.EqualTo(6), "organized twice, reported once");
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

            ServiceResultException? error = Assert.ThrowsAsync<ServiceResultException>(async () => await m_client.SetZeroAsync(id));
            Assert.That(error!.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadOutOfRange));
            Assert.That(await m_client.ReadRegisteredWeightAsync(Id("Hopper")), Is.Not.Null);
            Assert.That(await m_client.TryReadCurrentWeightAsync(id), Is.Not.Null);
            Assert.That(await m_client.TryReadCurrentWeightAsync(m_system!.NodeId), Is.Null, "a scale system has no CurrentWeight");
            ServiceResultException? notFound = Assert.ThrowsAsync<ServiceResultException>(
                async () => await m_client.ReadCurrentWeightAsync(m_system.NodeId));
            Assert.That(notFound!.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadNotFound));
            ServiceResultException? missing = Assert.ThrowsAsync<ServiceResultException>(
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

            ServiceResultException? notFound = Assert.ThrowsAsync<ServiceResultException>(
                async () => await m_client.SelectProductAsync(id, "Nope"));
            Assert.That(notFound!.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadNotFound));
            Assert.ThrowsAsync<ServiceResultException>(async () => await m_client.SelectProductAsync(Id("Lab"), "x"));
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
            ServiceResultException? invalid = Assert.ThrowsAsync<ServiceResultException>(
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
            ServiceResultException? tooFast = Assert.ThrowsAsync<ServiceResultException>(
                async () => await m_client.SetFeederSpeedAsync(checker.Modules[0].NodeId, 9f, ScalesTestUnits.MetrePerSecond));
            Assert.That(tooFast!.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadOutOfRange));
            await m_client.InvokeAsync(m_system.NodeId, BrowseNames.ResetGlobalStatistics);
            ScaleIdentification feederId = await m_client.ReadIdentificationAsync(checker.Modules[0].NodeId);
            Assert.That(feederId.SerialNumber, Is.EqualTo("F1"));
        }

        [Test]
        public async Task ClientBuildsAndProcessesARecipeAsync()
        {
            ScaleHandle scale = m_scales["Recipe"];
            ushort ns = m_session.NamespaceUris.GetIndexOrAppend(Namespaces.Scales);
            NodeId recipe = await m_client.AddRecipeAsync(scale.NodeId, "Bread", new LocalizedText("Bread"));
            Assert.That(recipe.IsNull, Is.False);
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
            await foreach (ScaleNotificationInfo notification in m_client.ObserveNotificationsAsync(scale.NodeId, cancellationToken: timeout.Token))
            {
                received.Add(notification);
                if (received.Any(n => !n.IsAlarm) && received.Any(n => n.IsAlarm))
                {
                    break;
                }
            }
            pump.Cancel();
            await raiser;

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
            await foreach (ScaleNotificationInfo notification in m_client.ObserveNotificationsAsync(checker.NodeId, cancellationToken: timeout.Token))
            {
                if (!notification.IsAlarm)
                {
                    received = notification;
                    break;
                }
            }
            pump.Cancel();
            await raiser;

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
            string testRoot = System.IO.Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                applicationName,
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
