/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Di.Server;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.Machinery.Tests
{
    [TestFixture]
    [Category("Machinery")]
    public sealed class MachineryResourceLifetimeTests
    {
        [TestCase(true, true)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(false, false)]
        public async Task NeutralAndLegacyResourceAliasesShareIdempotentRegistryAsync(
            bool registerNeutralFirst,
            bool drainNeutralFirst)
        {
            DiNodeManager manager = CreateDiManager();
            var trace = new List<string>();
            var shared = new Probe("shared", trace);
            var second = new Probe("second", trace);
            if (registerNeutralFirst)
            {
                manager.RegisterResource(shared);
                manager.RegisterMachineryResource(shared);
                manager.RegisterMachineryResource(second);
            }
            else
            {
                manager.RegisterMachineryResource(shared);
                manager.RegisterResource(shared);
                manager.RegisterResource(second);
            }

            if (drainNeutralFirst)
            {
                await manager.DisposeResourcesAsync().ConfigureAwait(false);
            }
            else
            {
                await manager.DisposeMachineryResourcesAsync().ConfigureAwait(false);
            }
            await manager.DisposeMachineryResourcesAsync().ConfigureAwait(false);
            await manager.DisposeResourcesAsync().ConfigureAwait(false);

            var later = new Probe("later", trace);
            manager.RegisterMachineryResource(later);
            await manager.DisposeResourcesAsync().ConfigureAwait(false);
            await manager.DisposeAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(trace, Is.EqualTo(new List<string> { "second", "shared", "later" }));
                Assert.That(shared.Count, Is.EqualTo(1));
                Assert.That(second.Count, Is.EqualTo(1));
                Assert.That(later.Count, Is.EqualTo(1));
            });
        }

        [Test]
        public async Task LegacyAliasValidationAndFailuresMatchNeutralRegistryAsync()
        {
            ArgumentNullException nullRegisterManager = Assert.Throws<ArgumentNullException>(
                () => DiNodeManagerMachineryExtensions.RegisterMachineryResource(null!, new Probe("x", [])))!;
            ArgumentNullException nullDrainManager = Assert.Throws<ArgumentNullException>(
                () => DiNodeManagerMachineryExtensions.DisposeMachineryResourcesAsync(null!)
                    .AsTask().GetAwaiter().GetResult())!;

            DiNodeManager manager = CreateDiManager();
            ArgumentNullException nullResource = Assert.Throws<ArgumentNullException>(
                () => manager.RegisterMachineryResource(null!))!;
            var trace = new List<string>();
            var firstError = new InvalidOperationException("first resource failed");
            var lastError = new ArgumentException("last resource failed");
            var first = new Probe("first", trace, firstError);
            var middle = new Probe("middle", trace);
            var last = new Probe("last", trace, lastError);
            manager.RegisterMachineryResource(first);
            manager.RegisterResource(middle);
            manager.RegisterMachineryResource(last);

            AggregateException aggregate = (await Assert.ThrowsAsync<AggregateException>(
                () => manager.DisposeMachineryResourcesAsync().AsTask()).ConfigureAwait(false))!;
            await manager.DisposeResourcesAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(nullRegisterManager.ParamName, Is.EqualTo("manager"));
                Assert.That(nullDrainManager.ParamName, Is.EqualTo("manager"));
                Assert.That(nullResource.ParamName, Is.EqualTo("resource"));
                Assert.That(trace, Is.EqualTo(new List<string> { "last", "middle", "first" }));
                Assert.That(aggregate.InnerExceptions, Has.Count.EqualTo(2));
                Assert.That(aggregate.InnerExceptions[0], Is.SameAs(lastError));
                Assert.That(aggregate.InnerExceptions[1], Is.SameAs(firstError));
                Assert.That(first.Count, Is.EqualTo(1));
                Assert.That(middle.Count, Is.EqualTo(1));
                Assert.That(last.Count, Is.EqualTo(1));
            });
            await manager.DisposeAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task CoordinatorIsCachedPerOwnerAndCanDrainIndependentBatchesAsync()
        {
            DiNodeManager firstOwner = CreateDiManager();
            DiNodeManager secondOwner = CreateDiManager();
            MachineryBuildCoordinator firstCoordinator = MachineryBuildCoordinator.Get(firstOwner);
            MachineryBuildCoordinator secondCoordinator = MachineryBuildCoordinator.Get(secondOwner);
            var trace = new List<string>();
            var firstResource = new Probe("first", trace);
            var secondResource = new Probe("second", trace);
            await AdoptResourceAsync(firstOwner, firstCoordinator, firstResource).ConfigureAwait(false);
            await AdoptResourceAsync(secondOwner, secondCoordinator, secondResource).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(MachineryBuildCoordinator.Get(firstOwner), Is.SameAs(firstCoordinator));
                Assert.That(secondCoordinator, Is.Not.SameAs(firstCoordinator));
            });

            await firstOwner.DisposeResourcesAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(trace, Is.EqualTo(new List<string> { "first" }));
                Assert.That(firstResource.Count, Is.EqualTo(1));
                Assert.That(secondResource.Count, Is.Zero);
            });
            await firstOwner.DisposeAsync().ConfigureAwait(false);
            await secondOwner.DisposeResourcesAsync().ConfigureAwait(false);
            await secondOwner.DisposeAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(trace, Is.EqualTo(new List<string> { "first", "second" }));
                Assert.That(secondResource.Count, Is.EqualTo(1));
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task MachineResourceAdoptionIsReleasedOnAutomaticManagerDisposalAsync(bool machineryOwner)
        {
            DiNodeManager manager = machineryOwner ? CreateMachineryManager() : CreateDiManager();
            MachineryBuildCoordinator coordinator = MachineryBuildCoordinator.Get(manager);
            var trace = new List<string>();
            var first = new Probe("first", trace);
            var second = new Probe("second", trace);
            var root = new BaseObjectState(null)
            {
                NodeId = new NodeId("machine-root", 1),
                BrowseName = new QualifiedName("MachineRoot")
            };
            await manager.AddPredefinedNodeAsync(root).ConfigureAwait(false);
            await coordinator.AdoptMachineResourcesAsync(manager, root, [first, second]).ConfigureAwait(false);

            await manager.DisposeAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(trace, Is.EqualTo(new List<string> { "second", "first" }));
                Assert.That(second.Count, Is.EqualTo(1));
                Assert.That(first.Count, Is.EqualTo(1));
            });
        }

        [Test]
        public async Task ExplicitCoordinatorDrainAndManagerDisposalRemainIdempotentAsync()
        {
            DiNodeManager manager = CreateDiManager();
            MachineryBuildCoordinator coordinator = MachineryBuildCoordinator.Get(manager);
            var trace = new List<string>();
            var resource = new Probe("resource", trace);
            await AdoptResourceAsync(manager, coordinator, resource).ConfigureAwait(false);

            await manager.DisposeResourcesAsync().ConfigureAwait(false);
            await manager.DisposeResourcesAsync().ConfigureAwait(false);
            await manager.DisposeMachineryResourcesAsync().ConfigureAwait(false);
            await manager.DisposeAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(trace, Is.EqualTo(new List<string> { "resource" }));
                Assert.That(resource.Count, Is.EqualTo(1));
            });
        }

        [Test]
        public async Task MachineAdoptionAfterExplicitDrainRearmsOwnerCleanupAsync()
        {
            DiNodeManager manager = CreateDiManager();
            MachineryBuildCoordinator coordinator = MachineryBuildCoordinator.Get(manager);
            var trace = new List<string>();
            for (int batch = 0; batch < 3; batch++)
            {
                var resource = new Probe("batch" + batch, trace);
                await AdoptResourceAsync(manager, coordinator, resource, "machine" + batch).ConfigureAwait(false);
                await manager.DisposeResourcesAsync().ConfigureAwait(false);
                Assert.That(resource.Count, Is.EqualTo(1));
                Assert.That(coordinator.ResourceCount, Is.Zero);
            }
            Assert.That(trace, Is.EqualTo(new List<string> { "batch0", "batch1", "batch2" }));
            await manager.DisposeAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task CoordinatorRejectsNullManagerAndAcceptsEmptyAdoptionAsync()
        {
            ArgumentNullException managerError = Assert.Throws<ArgumentNullException>(
                () => MachineryBuildCoordinator.Get(null!))!;
            DiNodeManager owner = CreateDiManager();
            MachineryBuildCoordinator coordinator = MachineryBuildCoordinator.Get(owner);
            var root = new BaseObjectState(null);
            await coordinator.AdoptMachineResourcesAsync(owner, root, default).ConfigureAwait(false);
            await owner.DisposeAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(managerError.ParamName, Is.EqualTo("manager"));
                Assert.That(root.NodeId, Is.EqualTo(NodeId.Null),
                    "empty adoption does not require an indexed machine");
            });
        }

        private static async Task AdoptResourceAsync(
            DiNodeManager manager,
            MachineryBuildCoordinator coordinator,
            IAsyncDisposable resource,
            string name = "adoption-root")
        {
            var root = new BaseObjectState(null)
            {
                NodeId = new NodeId(name, 1),
                BrowseName = new QualifiedName(name)
            };
            await manager.AddPredefinedNodeAsync(root).ConfigureAwait(false);
            await coordinator.AdoptMachineResourcesAsync(manager, root, [resource]).ConfigureAwait(false);
        }

        private static DiNodeManager CreateDiManager()
        {
            return new DiNodeManager(CreateServer().Object, Configuration());
        }

        private static MachineryNodeManager CreateMachineryManager()
        {
            var options = new MachineryServerOptions { Parts = MachineryParts.BuildingBlocks };
            return new MachineryNodeManager(
                CreateServer().Object,
                Configuration(),
                new IMachineryModelProvider[]
                {
                    new MachineryModelProvider(MachineryParts.BuildingBlocks)
                },
                options);
        }

        private static Mock<IServerInternal> CreateServer()
        {
            var server = new Mock<IServerInternal>();
            var namespaces = new NamespaceTable();
            server.SetupGet(value => value.NamespaceUris).Returns(namespaces);
            server.SetupGet(value => value.ServerUris).Returns(new StringTable());
            server.SetupGet(value => value.TypeTree).Returns(new TypeTable(namespaces));
            server.SetupGet(value => value.Factory).Returns(EncodeableFactory.Create());
            server.SetupGet(value => value.Telemetry).Returns(NUnitTelemetryContext.Create());
            server.SetupGet(value => value.NodeManager).Returns(Mock.Of<IMasterNodeManager>());
            server.SetupGet(value => value.CoreNodeManager).Returns(Mock.Of<ICoreNodeManager>());
            server.SetupGet(value => value.DefaultSystemContext).Returns(new ServerSystemContext(server.Object));
            return server;
        }

        private static ApplicationConfiguration Configuration() => new()
        {
            ApplicationUri = "urn:opcfoundation:machinery-lifetime-tests",
            ServerConfiguration = new ServerConfiguration()
        };

        /// <summary>
        /// Records resource cleanup order and optional failures.
        /// </summary>
        private sealed class Probe : IAsyncDisposable
        {
            private readonly List<string> m_trace;
            private readonly Exception? m_error;

            /// <summary>
            /// Creates a resource with a trace label and an optional failure.
            /// </summary>
            public Probe(string label, List<string> trace, Exception? error = null)
            {
                Label = label;
                m_trace = trace;
                m_error = error;
            }

            /// <summary>
            /// Gets the label recorded when cleanup is attempted.
            /// </summary>
            public string Label { get; }

            /// <summary>
            /// Gets the number of cleanup attempts.
            /// </summary>
            public int Count { get; private set; }

            /// <summary>
            /// Records cleanup and returns any injected failure.
            /// </summary>
            public ValueTask DisposeAsync()
            {
                Count++;
                m_trace.Add(Label);
                return m_error == null ? default : new ValueTask(Task.FromException(m_error));
            }
        }
    }
}
