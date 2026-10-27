/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.AutoID.Server;
using Opc.Ua.AutoID.Server.Hosting;
using Opc.Ua.Di.Server;
using Opc.Ua.Di.Server.Hosting;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Tests;

namespace Opc.Ua.AutoID.Tests
{
    [TestFixture]
    [Category("AutoID")]
    [Category("Composition")]
    public sealed class AutoIdMachineryConfigurationOrderTests
    {
        [Test]
        public async Task StandaloneAutoIdLoadsNeutralProviderOnceAfterOwnerModelsAsync()
        {
            var services = new ServiceCollection();
            services.AddOpcUa()
                .AddServer(options => options.ApplicationName = "AutoIdNeutralProvider")
                .AddAutoId()
                .AddDiModel<StandaloneProbeProvider>();
            using ServiceProvider provider = services.BuildServiceProvider();
            AutoIdNodeManagerFactory factory = provider.GetRequiredService<AutoIdNodeManagerFactory>();
            Mock<IServerInternal> server = CreateOfflineServer();
            var configuration = new ApplicationConfiguration
            {
                ApplicationUri = "urn:tests:autoid-neutral-provider",
                ServerConfiguration = new ServerConfiguration()
            };
            await using var core = new CoreNodeManager(server.Object, configuration);
            server.SetupGet(value => value.CoreNodeManager).Returns(core);
            await core.ImportNodesAsync(core.SystemContext,
                new NodeStateCollection().AddOpcUa(core.SystemContext), isInternal: true).ConfigureAwait(false);
            var manager = (AutoIdNodeManager)await factory.CreateAsync(
                server.Object, configuration).ConfigureAwait(false);
            try
            {
                await manager.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>())
                    .ConfigureAwait(false);
                var probe = (StandaloneProbeProvider)provider.GetServices<IDiModelProvider>().Single();
                Assert.Multiple(() =>
                {
                    Assert.That(probe.CallCount, Is.EqualTo(1));
                    Assert.That(probe.SawDiModel, Is.True);
                    Assert.That(probe.SawAutoIdModel, Is.True);
                    Assert.That(factory.NamespacesUris.ToArray(), Does.Contain(StandaloneProbeProvider.NamespaceUri));
                    Assert.That(manager.NamespaceUris.ToArray(), Does.Contain(StandaloneProbeProvider.NamespaceUri));
                    Assert.That(manager.FindPredefinedNode(NodeId.Create(
                        "neutral-probe", StandaloneProbeProvider.NamespaceUri, manager.SystemContext.NamespaceUris))!
                        .BrowseName.Name, Is.EqualTo("NeutralProbe"));
                });
            }
            finally
            {
                await manager.DisposeAsync().ConfigureAwait(false);
            }
        }

        public sealed class StandaloneProbeProvider : IDiModelProvider
        {
            public const string NamespaceUri = "urn:tests:autoid:neutral-probe";
            public int Order => 0;
            public ArrayOf<string> NamespaceUris => [NamespaceUri];
            public int CallCount { get; private set; }
            public bool SawDiModel { get; private set; }
            public bool SawAutoIdModel { get; private set; }

            public void AddPredefinedNodes(NodeStateCollection nodes, ISystemContext context)
            {
                CallCount++;
                SawDiModel = nodes.Any(node => node.NodeId == NodeId.Create(
                    Di.Objects.DeviceSet, Di.Namespaces.OpcUaDi, context.NamespaceUris));
                SawAutoIdModel = nodes.Any(node => node.NodeId == ExpandedNodeId.ToNodeId(
                    Opc.Ua.AutoID.ObjectTypeIds.AutoIdDeviceType, context.NamespaceUris));
                nodes.Add(new BaseObjectState(null)
                {
                    NodeId = NodeId.Create("neutral-probe", NamespaceUri, context.NamespaceUris),
                    BrowseName = new QualifiedName("NeutralProbe"),
                    DisplayName = new LocalizedText("NeutralProbe")
                });
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task MachineryCallbacksCompleteBeforeAutoIdCallbackAsync(bool registerAutoIdCallbackFirst)
        {
            var services = new ServiceCollection();
            IOpcUaServerBuilder builder = services.AddOpcUa()
                .AddServer(options =>
                {
                    options.ApplicationName = "AutoIdMachineryOrder";
                    options.ApplicationUri = "urn:tests:autoid-machinery-order";
                })
                .AddMachinery(options => options.Parts = MachineryParts.BuildingBlocks);

            var trace = new List<string>();
            var machineryStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            object? machineryMarker = null;
            object? markerObservedByAutoId = null;
            void AddAutoIdConfigurator()
            {
                builder.ConfigureAutoIdFor<MachineryNodeManager>((context, _) =>
                {
                    trace.Add("autoid");
                    markerObservedByAutoId = machineryMarker;
                    Assert.That(context.Manager, Is.TypeOf<MachineryNodeManager>());
                    return default;
                });
            }

            if (registerAutoIdCallbackFirst)
            {
                AddAutoIdConfigurator();
                builder.AddAutoIdToMachinery();
            }
            else
            {
                builder.AddAutoIdToMachinery();
            }

            builder.ConfigureMachineryFor<MachineryNodeManager>(async (_, _) =>
            {
                trace.Add("machinery-start");
                machineryStarted.TrySetResult(true);
                await gate.Task.ConfigureAwait(false);
                machineryMarker = new object();
                trace.Add("machinery-complete");
            });
            builder.ConfigureMachineryFor<MachineryNodeManager>(_ =>
            {
                trace.Add("machinery-second");
                Assert.That(machineryMarker, Is.Not.Null);
                return default;
            });
            if (!registerAutoIdCallbackFirst)
            {
                AddAutoIdConfigurator();
            }

            using ServiceProvider provider = services.BuildServiceProvider();
            Mock<IServerInternal> server = CreateOfflineServer();
            var configuration = new ApplicationConfiguration
            {
                ApplicationUri = "urn:tests:autoid-machinery-order",
                ServerConfiguration = new ServerConfiguration()
            };
            MachineryNodeManager manager = (MachineryNodeManager)await provider
                .GetRequiredService<MachineryNodeManagerFactory>()
                .CreateAsync(server.Object, configuration)
                .ConfigureAwait(false);
            try
            {
                await manager.AddPredefinedNodeAsync(CreateIndexedFolder(
                    NodeId.Create(Di.Objects.DeviceSet, Di.Namespaces.OpcUaDi,
                        manager.SystemContext.NamespaceUris),
                    "DeviceSet")).ConfigureAwait(false);
                await manager.AddPredefinedNodeAsync(CreateIndexedFolder(
                    NodeId.Create(Machinery.Objects.Machines, Machinery.Namespaces.Machinery,
                        manager.SystemContext.NamespaceUris),
                    "Machines")).ConfigureAwait(false);
                await manager.AddPredefinedNodeAsync(CreateIndexedFolder(
                    ExpandedNodeId.ToNodeId(Opc.Ua.AutoID.ObjectTypeIds.AutoIdDeviceType,
                        manager.SystemContext.NamespaceUris),
                    "AutoIdDeviceType")).ConfigureAwait(false);

                Task pipeline = provider.GetRequiredService<IDiPostSetupRunner>()
                    .RunAsync(manager, CancellationToken.None).AsTask();
                Task firstCompletion = await Task.WhenAny(machineryStarted.Task, pipeline).ConfigureAwait(false);
                if (firstCompletion == pipeline)
                {
                    await pipeline.ConfigureAwait(false);
                }
                await machineryStarted.Task.ConfigureAwait(false);
                Assert.That(trace, Is.EqualTo(new List<string> { "machinery-start" }),
                    "the AutoID callback must not start while the asynchronous Machinery callback is pending");
                gate.SetResult(true);
                await pipeline.ConfigureAwait(false);

                Assert.Multiple(() =>
                {
                    Assert.That(trace, Is.EqualTo(new List<string>
                    {
                        "machinery-start", "machinery-complete", "machinery-second", "autoid"
                    }));
                    Assert.That(markerObservedByAutoId, Is.SameAs(machineryMarker));
                    Assert.That(manager.CreateAutoIdRuntime().Manager, Is.SameAs(manager));
                });
            }
            finally
            {
                await manager.DisposeAsync().ConfigureAwait(false);
            }
        }

        private static BaseObjectState CreateIndexedFolder(NodeId nodeId, string name)
        {
            return new BaseObjectState(null)
            {
                NodeId = nodeId,
                BrowseName = new QualifiedName(name),
                DisplayName = new LocalizedText(name),
                TypeDefinitionId = Ua.ObjectTypeIds.FolderType,
                ReferenceTypeId = ReferenceTypeIds.Organizes
            };
        }

        private static Mock<IServerInternal> CreateOfflineServer()
        {
            var server = new Mock<IServerInternal>();
            var namespaces = new NamespaceTable();
            var master = new Mock<IMasterNodeManager>();
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var queues = new MonitoredItemQueueFactory(telemetry);
            server.SetupGet(value => value.NamespaceUris).Returns(namespaces);
            server.SetupGet(value => value.ServerUris).Returns(new StringTable());
            server.SetupGet(value => value.TypeTree).Returns(new TypeTable(namespaces));
            server.SetupGet(value => value.Factory).Returns(EncodeableFactory.Create());
            server.SetupGet(value => value.Telemetry).Returns(telemetry);
            server.SetupGet(value => value.MonitoredItemQueueFactory).Returns(queues);
            server.SetupGet(value => value.NodeManager).Returns(master.Object);
            server.SetupGet(value => value.SessionManager).Returns(Mock.Of<ISessionManager>());
            server.SetupGet(value => value.DefaultSystemContext).Returns(new ServerSystemContext(server.Object));
            master.SetupGet(value => value.ConfigurationNodeManager).Returns(Mock.Of<IConfigurationNodeManager>());
            var configuration = new ApplicationConfiguration
            {
                ApplicationUri = "urn:tests:autoid-machinery-order",
                ServerConfiguration = new ServerConfiguration()
            };
            server.SetupGet(value => value.CoreNodeManager).Returns(Mock.Of<ICoreNodeManager>());
            master.SetupGet(value => value.CoreNodeManager).Returns(Mock.Of<ICoreNodeManager>());
            return server;
        }
    }
}
