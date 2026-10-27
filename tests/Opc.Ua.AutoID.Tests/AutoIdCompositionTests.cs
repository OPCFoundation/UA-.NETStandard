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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Opc.Ua.AutoID.Server;
using Opc.Ua.AutoID.Server.Hosting;
using Opc.Ua.Di.Server;
using Opc.Ua.Di.Server.Builders;
using Opc.Ua.Di.Server.Hosting;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Machinery.Server.Builders;
using Opc.Ua.Pumps.Server;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Server.TestFramework;

namespace Opc.Ua.AutoID.Tests
{
    /// <summary>
    /// AutoID composed into the Machinery node manager, which owns the Device Integration address
    /// space: the hosting registration, the model provider, the shared runtime and its lifetime.
    /// </summary>
    [TestFixture]
    [Category("AutoID")]
    [Category("Composition")]
    [NonParallelizable]
    public sealed class AutoIdCompositionTests
    {
        [Test]
        public void AddAutoIdNextToMachineryIsRefusedAndNamesTheComposition()
        {
            IOpcUaServerBuilder builder = CreateServerBuilder();
            builder.AddMachinery(options => options.Parts = MachineryParts.BuildingBlocks);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => builder.AddAutoId())!;

            Assert.Multiple(() =>
            {
                Assert.That(exception.Message, Does.Contain("already owned by 'AddMachinery'"));
                Assert.That(exception.Message,
                    Does.Contain(nameof(OpcUaAutoIdCompositionExtensions.AddAutoIdModel)),
                    "the failure names the way out");
                Assert.That(builder.Services.Count(d => d.ServiceType == typeof(AutoIdNodeManagerFactory)), Is.Zero);
            });
        }

        [Test]
        public void MachineryAfterAddAutoIdIsRefused()
        {
            IOpcUaServerBuilder builder = CreateServerBuilder();
            builder.AddAutoId();

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => builder.AddMachinery(options => options.Parts = MachineryParts.BuildingBlocks))!;

            Assert.That(exception.Message, Does.Contain("already owned by 'AddAutoId'"));
        }

        [Test]
        public void AddAutoIdModelKeepsTheMachineryOwnerAndRegistersOneProvider()
        {
            IOpcUaServerBuilder builder = CreateServerBuilder();
            builder.AddMachinery(options => options.Parts = MachineryParts.BuildingBlocks)
                .AddPumpsToMachinery()
                .AddAutoIdModel(options => options.MaximumTagReadLength = 128)
                .AddAutoIdModel();

            using ServiceProvider services = builder.Services.BuildServiceProvider();
            Assert.Multiple(() =>
            {
                Assert.That(services.GetServices<DiAddressSpaceOwnership>().Single().OwnerName,
                    Is.EqualTo("AddMachinery"));
                Assert.That(services.GetServices<Opc.Ua.Di.Server.IDiModelProvider>()
                    .Count(model => model is AutoIdModelProvider), Is.EqualTo(1));
                Assert.That(services.GetServices<IMachineryModelProvider>()
                    .Count(model => model is PumpsMachineryModelProvider), Is.EqualTo(1));
                Assert.That(builder.Services.Any(d => d.ServiceType == typeof(AutoIdNodeManagerFactory)), Is.False,
                    "no second node manager is registered");
                Assert.That(services.GetRequiredService<IOptions<AutoIdServerOptions>>().Value.MaximumTagReadLength,
                    Is.EqualTo(128u));
                Assert.Throws<InvalidOperationException>(() => builder.AddAutoId());
            });
        }

        [Test]
        public void CompositionRejectsInvalidRegistration()
        {
            IOpcUaServerBuilder builder = CreateServerBuilder();
            Assert.Multiple(() =>
            {
                Assert.Throws<ArgumentNullException>(
                    () => OpcUaAutoIdCompositionExtensions.AddAutoIdModel(null!));
                Assert.Throws<ArgumentNullException>(
                    () => OpcUaAutoIdCompositionExtensions.ConfigureAutoIdFor<MachineryNodeManager>(
                        null!, (_, _) => default));
                Assert.Throws<ArgumentNullException>(() => builder.ConfigureAutoIdFor<MachineryNodeManager>(null!));
            });
            builder.AddMachinery(options => options.Parts = MachineryParts.BuildingBlocks);
            Assert.Throws<ArgumentOutOfRangeException>(
                () => builder.AddAutoIdModel(options => options.MaximumScanDuration = TimeSpan.Zero));
            Assert.That(builder.Services.Any(d => d.ImplementationType == typeof(AutoIdModelProvider)),
                Is.False, "a rejected registration adds no model");
        }

        [Test]
        public void AutoIdModelCanBeRegisteredBeforeAnyOwner()
        {
            IOpcUaServerBuilder builder = CreateServerBuilder();

            builder.AddAutoIdModel().AddOpcUaDi();

            using ServiceProvider services = builder.Services.BuildServiceProvider();
            Assert.That(services.GetServices<DiAddressSpaceOwnership>().Single().OwnerName,
                Is.EqualTo("AddOpcUaDi"));
            Assert.That(services.GetServices<Opc.Ua.Di.Server.IDiModelProvider>().Single(),
                Is.TypeOf<AutoIdModelProvider>());
        }

        [Test]
        public async Task ComposedReadersLiveBelowTheSharedDeviceSetAsync()
        {
            await using var fixture = new ComposedOwner();
            await fixture.StartAsync();
            AutoIdRuntime runtime = fixture.Manager.CreateAutoIdRuntime();

            AutoIdReader gate = await runtime.Reader(fixture.Name("Gate1"), new InMemoryRfidReaderProvider())
                .WithDeviceInfo("Press infeed")
                .BuildAsync();
            AutoIdReader optical = await runtime.CreateReaderAsync(
                fixture.Name("Optical1"), new InMemoryScanReaderProvider(AutoIdReaderKind.Optical));

            NodeState deviceSet = fixture.DeviceSet;
            ushort autoIdNs = (ushort)fixture.Manager.SystemContext.NamespaceUris.GetIndex(AutoIdModel.NamespaceUri);
            Assert.Multiple(() =>
            {
                Assert.That(fixture.Manager.CreateAutoIdRuntime(), Is.SameAs(runtime), "one runtime per owner");
                Assert.That(runtime.Manager, Is.SameAs(fixture.Manager));
                Assert.That(runtime.InstanceNamespaceIndex,
                    Is.EqualTo(fixture.Manager.MachineryInstanceNamespaceIndex));
                Assert.That(runtime.Readers.ToArray(), Is.EqualTo(new[] { gate, optical }));
                Assert.That(gate.Device.Parent, Is.SameAs(deviceSet));
                Assert.That(deviceSet.FindChild(fixture.Manager.SystemContext, fixture.Name("Optical1")),
                    Is.SameAs(optical.Device));
                Assert.That(gate.Device.TypeDefinitionId, Is.EqualTo(ExpandedNodeId.ToNodeId(
                    ObjectTypeIds.RfidReaderDeviceType, fixture.Manager.SystemContext.NamespaceUris)));
                Assert.That(fixture.Manager.ConformanceUnits.ToArray(),
                    Does.Contain(new QualifiedName(AutoIdProfiles.SyncAccessUnit, autoIdNs))
                        .And.Contain(new QualifiedName("Machinery Find Machines")),
                    "the owner advertises the AutoID units next to its own");
                Assert.That(fixture.Manager.ServerProfiles.ToArray(),
                    Does.Contain(AutoIdProfiles.BaseSyncServer).And.Contain(AutoIdProfiles.SimpleSyncServer));
            });
            AssertOwnedSubtree(fixture.Manager, gate.Device);
        }

        [Test]
        public async Task CreateAutoIdRuntimeRequiresTheAutoIdModelAsync()
        {
            await using var fixture = new ComposedOwner();
            await fixture.StartAsync(withAutoId: false);

            ServiceResultException error = Assert.Throws<ServiceResultException>(
                () => fixture.Manager.CreateAutoIdRuntime())!;

            Assert.Multiple(() =>
            {
                Assert.That(error.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadConfigurationError));
                Assert.That(error.Message, Does.Contain("AddAutoIdModel"));
                Assert.Throws<ArgumentNullException>(() => DiNodeManagerAutoIdExtensions.CreateAutoIdRuntime(null!));
            });
        }

        [Test]
        public async Task AnApplicationOwnedDiOwnerComposesAutoIdAsync()
        {
            var server = new ServerFixture<StandardServer>(telemetry => new StandardServer(telemetry))
            {
                AutoAccept = true,
                SecurityNone = true
            };
            StandardServer started = await server.StartAsync();
            try
            {
                var owner = new ApplicationOwner(started.CurrentInstance, server.Config);
                await owner.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>());
                AutoIdRuntime runtime = owner.CreateAutoIdRuntime();
                var provider = new FakeProvider();
                AutoIdReader reader = await runtime.CreateReaderAsync(
                    new QualifiedName("Gate1", runtime.InstanceNamespaceIndex), provider);

                Assert.Multiple(() =>
                {
                    Assert.That(runtime.InstanceNamespaceIndex, Is.EqualTo(owner.InstanceNamespaceIndex));
                    Assert.That(owner.FindPredefinedNode(reader.NodeId), Is.SameAs(reader.Device));
                    Assert.That(reader.Device.Parent!.BrowseName.Name, Is.EqualTo("DeviceSet"));
                });
                await owner.DisposeAsync();
                Assert.That(provider.DetachCalls, Is.EqualTo(1), "the owner released the shared runtime");
            }
            finally
            {
                await server.StopAsync();
            }
        }

        [Test]
        public async Task TheStandaloneManagerReturnsItsOwnRuntimeAsync()
        {
            await using var fixture = new AutoIdManagerFixture();
            await fixture.StartAsync();

            Assert.That(fixture.Manager.CreateAutoIdRuntime(), Is.SameAs(fixture.Runtime));
        }

        [Test]
        public async Task TheProviderLoadsAutoIdOnceAndKeepsTheOwnersDeviceIntegrationNodesAsync()
        {
            await using var fixture = new ComposedOwner();
            await fixture.StartAsync();
            ServerSystemContext context = fixture.Manager.SystemContext;
            var provider = new AutoIdModelProvider(
                Options.Create(new AutoIdServerOptions { AdditionalNamespaceUris = ["urn:tests:autoid:extra"] }));
            var nodes = new NodeStateCollection();
            ServiceResultException error = Assert.Throws<ServiceResultException>(
                () => provider.AddPredefinedNodes(nodes, context))!;
            Assert.That(error.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadConfigurationError));

            new MachineryModelProvider(MachineryParts.BuildingBlocks).AddPredefinedNodes(nodes, context);
            var deviceSetId = ExpandedNodeId.ToNodeId(Di.ObjectIds.DeviceSet, context.NamespaceUris);
            NodeState ownersDeviceSet = nodes.Single(node => node.NodeId == deviceSetId);
            int withoutAutoId = nodes.Count;
            provider.AddPredefinedNodes(nodes, context);
            int withAutoId = nodes.Count;
            provider.AddPredefinedNodes(nodes, context);

            Assert.Multiple(() =>
            {
                Assert.That(withAutoId, Is.GreaterThan(withoutAutoId));
                Assert.That(nodes, Has.Count.EqualTo(withAutoId), "a second load adds nothing");
                Assert.That(nodes.Select(node => node.NodeId).Distinct().Count(), Is.EqualTo(nodes.Count));
                Assert.That(nodes.Single(node => node.NodeId == deviceSetId), Is.SameAs(ownersDeviceSet),
                    "the owner's DeviceSet is kept, not loaded again");
                Assert.That(provider.NamespaceUris.ToArray(),
                    Is.EqualTo(new[] { AutoIdModel.NamespaceUri, "urn:tests:autoid:extra" }));
                Assert.That(provider.Order,
                    Is.GreaterThan(new MachineryModelProvider(MachineryParts.BuildingBlocks).Order));
                Assert.Throws<ArgumentNullException>(() => provider.AddPredefinedNodes(null!, context));
                Assert.Throws<ArgumentNullException>(() => provider.AddPredefinedNodes(nodes, null!));
            });
        }

        [Test]
        public async Task AFailedComposedCreationIsRolledBackAsync()
        {
            await using var fixture = new ComposedOwner();
            await fixture.StartAsync();
            AutoIdRuntime runtime = fixture.Manager.CreateAutoIdRuntime();
            var failing = new FakeProvider();
            runtime.ReaderRegisteredForTest = _ => throw new InvalidOperationException("injected");

            await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await runtime.CreateReaderAsync(fixture.Name("Gate1"), failing));
            runtime.ReaderRegisteredForTest = null;
            AutoIdReader reader = await runtime.CreateReaderAsync(fixture.Name("Gate1"), new FakeProvider());

            Assert.Multiple(() =>
            {
                Assert.That(failing.DetachCalls, Is.EqualTo(1), "the failed reader detached its provider");
                Assert.That(runtime.Readers.ToArray(), Is.EqualTo(new[] { reader }));
                Assert.That(fixture.Manager.FindPredefinedNode(reader.NodeId), Is.SameAs(reader.Device));
                Assert.That(fixture.DeviceSet.FindChild(fixture.Manager.SystemContext, fixture.Name("Gate1")),
                    Is.SameAs(reader.Device));
            });
        }

        [Test]
        public async Task ACancelledComposedCreationRegistersNothingAsync()
        {
            await using var fixture = new ComposedOwner();
            await fixture.StartAsync();
            AutoIdRuntime runtime = fixture.Manager.CreateAutoIdRuntime();
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();

            await Assert.ThatAsync(
                async () => await runtime.CreateReaderAsync(fixture.Name("Gate1"), new FakeProvider(), cancelled.Token),
                Throws.InstanceOf<OperationCanceledException>());
            AutoIdReader reader = await runtime.CreateReaderAsync(fixture.Name("Gate1"), new FakeProvider());
            ServiceResultException duplicate = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await runtime.CreateReaderAsync(fixture.Name("Gate1"), new FakeProvider())))!;

            Assert.Multiple(() =>
            {
                Assert.That(runtime.Readers.ToArray(), Is.EqualTo(new[] { reader }));
                Assert.That(duplicate.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadBrowseNameDuplicated));
            });
        }

        [Test]
        public async Task DisposingTheOwnerStopsTheComposedReadersAsync()
        {
            await using var fixture = new ComposedOwner();
            await fixture.StartAsync();
            AutoIdRuntime runtime = fixture.Manager.CreateAutoIdRuntime();
            var provider = new FakeProvider();
            await runtime.CreateReaderAsync(fixture.Name("Gate1"), provider);

            await fixture.Manager.DisposeAsync();

            await Assert.MultipleAsync(async () =>
            {
                Assert.That(provider.DetachCalls, Is.EqualTo(1), "the owner disposed the composed runtime");
                await Assert.ThrowsAsync<ObjectDisposedException>(
                    async () => await runtime.CreateReaderAsync(fixture.Name("Late"), new FakeProvider()));
            });
        }

        [Test]
        public async Task AReaderBecomesAComponentOfAMachineAndStaysADeviceAsync()
        {
            await using var fixture = new ComposedOwner();
            await fixture.StartAsync();
            AutoIdRuntime runtime = fixture.Manager.CreateAutoIdRuntime();
            IMachineHandle<BaseObjectState> press = await AddPressAsync(fixture, withComponents: true);

            AutoIdReader reader = await runtime.Reader(fixture.Name("Infeed"), new FakeProvider())
                .AsComponentOf(press.State)
                .WithIdentification(nameplate =>
                {
                    nameplate.Manufacturer = new LocalizedText("Contoso");
                    nameplate.SerialNumber = "R-1";
                    nameplate.Model = new LocalizedText("Portal 100");
                    nameplate.DeviceManual = "https://example.com/manual";
                })
                .BuildAsync();

            ServerSystemContext context = fixture.Manager.SystemContext;
            NodeState components = Components(fixture, press.State)!;
            AutoIdDeviceState device = reader.Device;
            var identification = (BaseObjectState)device.FindChild(
                context, new QualifiedName(Di.BrowseNames.Identification, fixture.Manager.DiNamespaceIndex))!;
            Assert.Multiple(() =>
            {
                Assert.That(device.Parent, Is.SameAs(components),
                    "Components references the component (OPC 40001-1 11.2)");
                Assert.That(device.ReferenceTypeId, Is.EqualTo(Types.ReferenceTypeIds.HasComponent));
                Assert.That(fixture.DeviceSet.ReferenceExists(
                    Types.ReferenceTypeIds.Organizes, false, device.NodeId), Is.True,
                    "the reader stays reachable from DeviceSet (OPC 10000-100 4.9)");
                Assert.That(fixture.MachinesFolder.ReferenceExists(
                    Types.ReferenceTypeIds.Organizes, false, device.NodeId), Is.False,
                    "a component is not a machine");
                Assert.That(identification, Is.Not.Null);
                Assert.That(identification.ReferenceTypeId, Is.EqualTo(Types.ReferenceTypeIds.HasAddIn));
                Assert.That(identification.TypeDefinitionId, Is.EqualTo(ExpandedNodeId.ToNodeId(
                    Machinery.ObjectTypeIds.MachineryComponentIdentificationType, context.NamespaceUris)));
                Assert.That(identification.ReferenceExists(
                    Types.ReferenceTypeIds.HasProperty, false, device.Manufacturer!.NodeId), Is.True,
                    "the add-in references the nameplate instead of copying it");
                Assert.That(identification.ReferenceExists(
                    Types.ReferenceTypeIds.HasProperty, false, device.SerialNumber!.NodeId), Is.True);
                Assert.That(identification.ReferenceExists(
                    Types.ReferenceTypeIds.HasProperty, false, device.Model!.NodeId), Is.True);
                Assert.That(identification.ReferenceExists(
                    Types.ReferenceTypeIds.HasProperty, false, device.DeviceManual!.NodeId), Is.False,
                    "OPC 40001-1 10.2 does not recommend DeviceManual for components");
                Assert.That(device.ProductInstanceUri, Is.Null, "a component without a URI omits it");
            });
            AssertOwnedSubtree(fixture.Manager, device);
        }

        [Test]
        public async Task AComponentNeedsAMachineWithComponentsAndRegistersNothingOtherwiseAsync()
        {
            await using var fixture = new ComposedOwner();
            await fixture.StartAsync();
            AutoIdRuntime runtime = fixture.Manager.CreateAutoIdRuntime();
            IMachineHandle<BaseObjectState> press = await AddPressAsync(fixture, withComponents: false);
            var provider = new FakeProvider();

            ServiceResultException error = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await runtime
                    .Reader(fixture.Name("Infeed"), provider)
                    .AsComponentOf(press.State)
                    .WithIdentification(Nameplate)
                    .BuildAsync()))!;
            AutoIdReader retried = await runtime.CreateReaderAsync(fixture.Name("Infeed"), new FakeProvider());

            Assert.Multiple(() =>
            {
                Assert.That(error.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadConfigurationError));
                Assert.That(error.Message, Does.Contain("WithComponents"));
                Assert.That(provider.AttachCalls, Is.Zero, "nothing was created");
                Assert.That(runtime.Readers.ToArray(), Is.EqualTo(new[] { retried }));
                Assert.That(retried.Device.Parent, Is.SameAs(fixture.DeviceSet));
            });
        }

        [Test]
        public async Task AComponentNeedsTheMandatoryIdentificationAsync()
        {
            await using var fixture = new ComposedOwner();
            await fixture.StartAsync();
            AutoIdRuntime runtime = fixture.Manager.CreateAutoIdRuntime();
            IMachineHandle<BaseObjectState> press = await AddPressAsync(fixture, withComponents: true);

            ServiceResultException none = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await runtime
                    .Reader(fixture.Name("Infeed"), new FakeProvider())
                    .AsComponentOf(press.State)
                    .BuildAsync()))!;
            ServiceResultException noSerial = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await runtime
                    .Reader(fixture.Name("Infeed"), new FakeProvider())
                    .AsComponentOf(press.State)
                    .WithIdentification(nameplate => nameplate.Manufacturer = new LocalizedText("Contoso"))
                    .BuildAsync()))!;

            Assert.Multiple(() =>
            {
                Assert.That(none.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadConfigurationError));
                Assert.That(none.Message, Does.Contain("Manufacturer and SerialNumber"));
                Assert.That(noSerial.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadConfigurationError));
                Assert.That(noSerial.Message, Does.Contain("SerialNumber").And.Not.Contain("Manufacturer and"));
                Assert.That(runtime.Readers.Count, Is.Zero);
                Assert.That(Components(fixture, press.State)!.FindChild(
                    fixture.Manager.SystemContext, fixture.Name("Infeed")), Is.Null);
            });
        }

        [Test]
        public async Task AComponentNeedsAMachineOfTheOwnerAsync()
        {
            await using var fixture = new ComposedOwner();
            await fixture.StartAsync();
            AutoIdRuntime runtime = fixture.Manager.CreateAutoIdRuntime();
            var foreign = new BaseObjectState(null)
            {
                BrowseName = fixture.Name("Foreign"),
                NodeId = new NodeId("Foreign", fixture.Manager.MachineryInstanceNamespaceIndex)
            };

            ServiceResultException error = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await runtime
                    .Reader(fixture.Name("Infeed"), new FakeProvider())
                    .AsComponentOf(foreign)
                    .WithIdentification(Nameplate)
                    .BuildAsync()))!;

            Assert.Multiple(() =>
            {
                Assert.That(error.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadConfigurationError));
                Assert.That(error.Message, Does.Contain("not a node of the AutoID owner"));
                Assert.That(runtime.Readers.Count, Is.Zero);
            });
        }

        [Test]
        public async Task AFailedComponentCreationIsRolledBackAsync()
        {
            await using var fixture = new ComposedOwner();
            await fixture.StartAsync();
            AutoIdRuntime runtime = fixture.Manager.CreateAutoIdRuntime();
            IMachineHandle<BaseObjectState> press = await AddPressAsync(fixture, withComponents: true);
            NodeState components = Components(fixture, press.State)!;
            ServerSystemContext context = fixture.Manager.SystemContext;
            int deviceSetReferences = CountReferences(context, fixture.DeviceSet);
            runtime.ReaderRegisteredForTest = _ => throw new InvalidOperationException("injected");

            await Assert.ThrowsAsync<InvalidOperationException>(async () => await runtime
                .Reader(fixture.Name("Infeed"), new FakeProvider())
                .AsComponentOf(press.State)
                .WithIdentification(Nameplate)
                .BuildAsync());
            int afterFailure = CountReferences(context, fixture.DeviceSet);
            NodeState? leftOver = components.FindChild(context, fixture.Name("Infeed"));
            runtime.ReaderRegisteredForTest = null;
            AutoIdReader reader = await runtime.Reader(fixture.Name("Infeed"), new FakeProvider())
                .AsComponentOf(press.State)
                .WithIdentification(Nameplate)
                .BuildAsync();

            Assert.Multiple(() =>
            {
                Assert.That(afterFailure, Is.EqualTo(deviceSetReferences), "the DeviceSet reference was removed");
                Assert.That(leftOver, Is.Null, "the component was removed from Components");
                Assert.That(runtime.Readers.ToArray(), Is.EqualTo(new[] { reader }));
                Assert.That(fixture.Manager.FindPredefinedNode(reader.NodeId), Is.SameAs(reader.Device));
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task AReaderBecomesAComponentOfABoundCompanionComponentsAddInAsync(bool nested)
        {
            await using var fixture = new ComposedOwner();
            await fixture.StartAsync();
            AutoIdRuntime runtime = fixture.Manager.CreateAutoIdRuntime();
            IMachineryBuildContext build = fixture.Manager.CreateMachineryBuildContext();
            IMachineBuilder<BaseObjectState> press = build.AddMachine(fixture.Name("Press1"))
                .WithIdentification(identification =>
                {
                    identification.Manufacturer = new LocalizedText("Contoso");
                    identification.SerialNumber = "PR-1";
                    identification.ProductInstanceUri = "urn:tests:autoid:press:1";
                });
            NodeState container = press.State;
            if (nested)
            {
                BaseObjectState assembly = build.Context.CreateInstanceOfBaseObjectType(
                    press.State, fixture.Name("Assembly"));
                assembly.ReferenceTypeId = Types.ReferenceTypeIds.HasComponent;
                press.State.AddChild(assembly);
                container = assembly;
            }
            // Named in the companion's namespace, as a companion model declares it.
            var companionName = new QualifiedName(
                Machinery.BrowseNames.Components,
                (ushort)build.Context.NamespaceUris.GetIndex(AutoIdModel.NamespaceUri));
            Machinery.MachineComponentsState bound = Machinery.OpcUaMachineryExtensions
                .CreateInstanceOfMachineComponentsType(build.Context, container, companionName);
            bound.ReferenceTypeId = Types.ReferenceTypeIds.HasAddIn;
            container.AddChild(bound);
            await press.WithComponents(bound, _ => { }).BuildAsync();

            AutoIdReader reader = await runtime.Reader(fixture.Name("Infeed"), new FakeProvider())
                .AsComponentOf(press.State)
                .WithIdentification(Nameplate)
                .BuildAsync();

            Assert.Multiple(() =>
            {
                Assert.That(bound.Parent, Is.SameAs(container), "the binding keeps its parent");
                Assert.That(bound.BrowseName, Is.EqualTo(companionName), "the binding keeps its name");
                Assert.That(reader.Device.Parent, Is.SameAs(bound));
                Assert.That(fixture.DeviceSet.ReferenceExists(
                    Types.ReferenceTypeIds.Organizes, false, reader.NodeId), Is.True);
                Assert.That(reader.Device.FindChild(
                    fixture.Manager.SystemContext,
                    new QualifiedName(Di.BrowseNames.Identification, fixture.Manager.DiNamespaceIndex)), Is.Not.Null);
            });
        }

        [Test]
        public async Task AMachineWithoutBuildingBlocksOffersItsOnlyComponentsChildAsync()
        {
            await using var fixture = new ComposedOwner();
            await fixture.StartAsync();
            AutoIdRuntime runtime = fixture.Manager.CreateAutoIdRuntime();
            BaseObjectState cell = await AddBareMachineAsync(fixture, "Cell", "Components");
            BaseObjectState line = await AddBareMachineAsync(fixture, "Line", "Components", "MoreComponents");

            AutoIdReader reader = await runtime.Reader(fixture.Name("Infeed"), new FakeProvider())
                .AsComponentOf(cell)
                .WithIdentification(Nameplate)
                .BuildAsync();
            var provider = new FakeProvider();
            ServiceResultException ambiguous = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await runtime
                    .Reader(fixture.Name("Outfeed"), provider)
                    .AsComponentOf(line)
                    .WithIdentification(Nameplate)
                    .BuildAsync()))!;

            Assert.Multiple(() =>
            {
                Assert.That(((BaseInstanceState)reader.Device.Parent!).Parent, Is.SameAs(cell));
                Assert.That(ambiguous.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadConfigurationError));
                Assert.That(ambiguous.Message, Does.Contain("more than one Components add-in"));
                Assert.That(provider.AttachCalls, Is.Zero);
                Assert.That(runtime.Readers.ToArray(), Is.EqualTo(new[] { reader }));
            });
        }

        /// <summary>
        /// Registers a machine built without the Machinery builders: no <c>MachineryBuildingBlocks</c>
        /// folder, and <c>MachineComponentsType</c> children named in the companion's namespace.
        /// </summary>
        private static async Task<BaseObjectState> AddBareMachineAsync(
            ComposedOwner fixture,
            string name,
            params string[] componentsNames)
        {
            ServerSystemContext context = fixture.Manager.SystemContext;
            ushort companion = (ushort)context.NamespaceUris.GetIndex(AutoIdModel.NamespaceUri);
            var machine = new BaseObjectState(null)
            {
                SymbolicName = name,
                BrowseName = fixture.Name(name),
                DisplayName = new LocalizedText(name),
                TypeDefinitionId = Ua.ObjectTypeIds.BaseObjectType,
                NodeId = new NodeId(name, fixture.Manager.MachineryInstanceNamespaceIndex)
            };
            foreach (string componentsName in componentsNames)
            {
                machine.AddChild(new BaseObjectState(machine)
                {
                    SymbolicName = componentsName,
                    BrowseName = new QualifiedName(componentsName, companion),
                    DisplayName = new LocalizedText(componentsName),
                    TypeDefinitionId = ExpandedNodeId.ToNodeId(
                        Machinery.ObjectTypeIds.MachineComponentsType,
                        context.NamespaceUris),
                    ReferenceTypeId = Types.ReferenceTypeIds.HasAddIn,
                    NodeId = new NodeId(name + "." + componentsName, fixture.Manager.MachineryInstanceNamespaceIndex)
                });
            }
            await fixture.Manager.AddPredefinedNodeAsync(machine);
            return machine;
        }

        private static void Nameplate(DeviceIdentificationData nameplate)
        {
            nameplate.Manufacturer = new LocalizedText("Contoso");
            nameplate.SerialNumber = "R-1";
        }

        private static async Task<IMachineHandle<BaseObjectState>> AddPressAsync(
            ComposedOwner fixture,
            bool withComponents)
        {
            IMachineryBuildContext context = fixture.Manager.CreateMachineryBuildContext();
            IMachineBuilder<BaseObjectState> press = context.AddMachine(fixture.Name("Press1"))
                .WithIdentification(identification =>
                {
                    identification.Manufacturer = new LocalizedText("Contoso");
                    identification.SerialNumber = "PR-1";
                    identification.ProductInstanceUri = "urn:tests:autoid:press:1";
                });
            if (withComponents)
            {
                press = press.WithComponents(_ => { });
            }
            return await press.BuildAsync();
        }

        private static BaseInstanceState? Components(ComposedOwner fixture, NodeState machine)
        {
            ServerSystemContext context = fixture.Manager.SystemContext;
            return machine.FindChild(context, new QualifiedName(
                Machinery.BrowseNames.Components,
                (ushort)context.NamespaceUris.GetIndex(Machinery.Namespaces.Machinery)));
        }

        private static int CountReferences(ISystemContext context, NodeState node)
        {
            var references = new List<IReference>();
            node.GetReferences(context, references);
            return references.Count;
        }

        /// <summary>
        /// Checks that the reader and its children are registered in the owner with NodeIds in the
        /// Machinery instance namespace.
        /// </summary>
        private static void AssertOwnedSubtree(MachineryNodeManager manager, NodeState node)
        {
            Assert.That(node.NodeId.NamespaceIndex, Is.EqualTo(manager.MachineryInstanceNamespaceIndex));
            Assert.That(manager.FindPredefinedNode(node.NodeId), Is.SameAs(node));
            var children = new List<BaseInstanceState>();
            node.GetChildren(manager.SystemContext, children);
            foreach (BaseInstanceState child in children)
            {
                AssertOwnedSubtree(manager, child);
            }
        }

        private static IOpcUaServerBuilder CreateServerBuilder()
        {
            return new ServiceCollection().AddOpcUa().AddServer(options =>
            {
                options.ApplicationName = "AutoIdCompositionTests";
                options.ApplicationUri = "urn:localhost:OPCFoundation:AutoIdCompositionTests";
            });
        }

        /// <summary>
        /// A Device Integration owner of the application that loads the AutoID model itself.
        /// </summary>
        private sealed class ApplicationOwner : DiNodeManager
        {
            /// <summary>
            /// Creates the owner.
            /// </summary>
            public ApplicationOwner(IServerInternal server, ApplicationConfiguration configuration)
                : base(server, configuration, AutoIdModel.NamespaceUri)
            {
            }

            /// <inheritdoc/>
            protected override ValueTask<NodeStateCollection> LoadPredefinedNodesAsync(
                ISystemContext context,
                CancellationToken cancellationToken = default)
            {
                if (!context.EncodeableFactory.ContainsEncodeableType(DataTypeIds.ScanSettings))
                {
                    context.EncodeableFactory.Builder.AddAutoIdTypeSystem().Commit();
                }
                return new ValueTask<NodeStateCollection>(new NodeStateCollection().AddAutoIdTypeSystem(context));
            }

            /// <inheritdoc/>
            protected override async ValueTask DisposeAsyncCore()
            {
                try
                {
                    await this.DisposeMachineryResourcesAsync();
                }
                finally
                {
                    await base.DisposeAsyncCore();
                }
            }
        }

        /// <summary>
        /// Presents a plain Device Integration provider to a directly constructed Machinery owner.
        /// </summary>
        private sealed class PlainProviderAdapter : IMachineryModelProvider
        {
            /// <summary>
            /// Wraps the provider.
            /// </summary>
            public PlainProviderAdapter(Opc.Ua.Di.Server.IDiModelProvider provider)
            {
                m_provider = provider;
            }

            /// <inheritdoc/>
            public int Order => m_provider.Order;

            /// <inheritdoc/>
            public ArrayOf<string> NamespaceUris => m_provider.NamespaceUris;

            /// <inheritdoc/>
            public void AddPredefinedNodes(NodeStateCollection nodes, ISystemContext context)
            {
                m_provider.AddPredefinedNodes(nodes, context);
            }

            private readonly Opc.Ua.Di.Server.IDiModelProvider m_provider;
        }

        /// <summary>
        /// Hosts a Machinery node manager directly, with the providers deliberately out of order.
        /// </summary>
        private sealed class ComposedOwner : IAsyncDisposable
        {
            /// <summary>
            /// Gets the Machinery owner.
            /// </summary>
            public MachineryNodeManager Manager { get; private set; } = null!;

            /// <summary>
            /// Gets the owner's Device Integration <c>DeviceSet</c>.
            /// </summary>
            public NodeState DeviceSet => Manager.FindPredefinedNode(
                ExpandedNodeId.ToNodeId(Di.ObjectIds.DeviceSet, Manager.SystemContext.NamespaceUris))!;

            /// <summary>
            /// Gets the owner's Machinery <c>Machines</c> folder.
            /// </summary>
            public NodeState MachinesFolder => Manager.FindPredefinedNode(
                ExpandedNodeId.ToNodeId(Machinery.ObjectIds.Machines, Manager.SystemContext.NamespaceUris))!;

            /// <summary>
            /// Starts the server and creates the owner's address space.
            /// </summary>
            public async Task StartAsync(bool withAutoId = true)
            {
                m_fixture = new ServerFixture<StandardServer>(telemetry => new StandardServer(telemetry))
                {
                    AutoAccept = true,
                    SecurityNone = true
                };
                StandardServer server = await m_fixture.StartAsync();
                var providers = new List<IMachineryModelProvider>();
                if (withAutoId)
                {
                    providers.Add(new PlainProviderAdapter(new AutoIdModelProvider()));
                }
                providers.Add(new MachineryModelProvider(MachineryParts.BuildingBlocks));
                Manager = new MachineryNodeManager(
                    server.CurrentInstance,
                    m_fixture.Config,
                    providers.ToArray(),
                    new MachineryServerOptions
                    {
                        Parts = MachineryParts.BuildingBlocks,
                        InstanceNamespaceUri = "urn:tests:autoid:composed:instances"
                    });
                await Manager.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>());
            }

            /// <summary>
            /// Creates a browse name in the owner's instance namespace.
            /// </summary>
            public QualifiedName Name(string name)
            {
                return new QualifiedName(name, Manager.MachineryInstanceNamespaceIndex);
            }

            /// <inheritdoc/>
            public async ValueTask DisposeAsync()
            {
                if (Manager != null)
                {
                    await Manager.DisposeAsync();
                }
                if (m_fixture != null)
                {
                    await m_fixture.StopAsync();
                }
            }

            /// <summary>The hosting server fixture.</summary>
            private ServerFixture<StandardServer>? m_fixture;
        }
    }
}
