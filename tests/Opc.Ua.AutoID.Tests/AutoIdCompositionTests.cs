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
using Opc.Ua.Di.Server.Hosting;
using Opc.Ua.Machinery.Server;
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
                    Does.Contain(nameof(OpcUaAutoIdCompositionExtensions.AddAutoIdToMachinery)),
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
        public void AddAutoIdToMachineryKeepsTheMachineryOwnerAndRegistersOneProvider()
        {
            IOpcUaServerBuilder builder = CreateServerBuilder();
            builder.AddMachinery(options => options.Parts = MachineryParts.BuildingBlocks)
                .AddPumpsToMachinery()
                .AddAutoIdToMachinery(options => options.MaximumTagReadLength = 128)
                .AddAutoIdToMachinery();

            using ServiceProvider services = builder.Services.BuildServiceProvider();
            Assert.Multiple(() =>
            {
                Assert.That(services.GetServices<DiAddressSpaceOwnership>().Single().OwnerName,
                    Is.EqualTo("AddMachinery"));
                Assert.That(services.GetServices<IMachineryModelProvider>()
                    .Count(model => model is AutoIdMachineryModelProvider), Is.EqualTo(1));
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
                Assert.Throws<InvalidOperationException>(() => builder.AddAutoIdToMachinery());
                Assert.Throws<ArgumentNullException>(
                    () => OpcUaAutoIdCompositionExtensions.AddAutoIdToMachinery(null!));
                Assert.Throws<ArgumentNullException>(
                    () => OpcUaAutoIdCompositionExtensions.ConfigureAutoIdFor<MachineryNodeManager>(
                        null!, (_, _) => default));
                Assert.Throws<ArgumentNullException>(() => builder.ConfigureAutoIdFor<MachineryNodeManager>(null!));
            });
            builder.AddMachinery(options => options.Parts = MachineryParts.BuildingBlocks);
            Assert.Throws<ArgumentOutOfRangeException>(
                () => builder.AddAutoIdToMachinery(options => options.MaximumScanDuration = TimeSpan.Zero));
            Assert.That(builder.Services.Any(d => d.ImplementationType == typeof(AutoIdMachineryModelProvider)),
                Is.False, "a rejected registration adds no model");
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
                Assert.That(error.Message, Does.Contain("AddAutoIdToMachinery"));
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
            var provider = new AutoIdMachineryModelProvider(
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
                    providers.Add(new AutoIdMachineryModelProvider());
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
