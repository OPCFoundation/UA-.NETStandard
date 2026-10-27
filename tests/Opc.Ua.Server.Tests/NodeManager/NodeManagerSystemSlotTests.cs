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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.TestFramework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Covers role-based system identities independently of routing-table list positions.
    /// </summary>
    [TestFixture]
    public sealed class NodeManagerSystemSlotTests
    {
        /// <summary>
        /// Configured namespace order survives failed creation and restart without publishing candidate discoveries.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        [NonParallelizable]
        public async Task InitialFactoryNamespaceBootstrapOrderSurvivesAbortAndRestartAsync(
            bool failFirst,
            bool mixedFactories)
        {
            const string firstUri = "urn:opcfoundation:tests:bootstrap:first";
            const string secondUri = "urn:opcfoundation:tests:bootstrap:second";
            const string discoveredUri = "urn:opcfoundation:tests:bootstrap:discovered";
            string pki = Path.Combine(Path.GetTempPath(),
                nameof(NodeManagerSystemSlotTests), Guid.NewGuid().ToString("N"));
            StandardServer? owner = null;
            NamespaceTable? sharedNamespaces = null;
            int firstIndexDuringCreate = -1;
            int secondIndexDuringCreate = -1;
            var creationOrder = new List<string>();
            var addressSpaceOrder = new List<string>();
            IAsyncNodeManager? asyncManager = null;
            INodeManager? syncManager = null;
            bool reject = failFirst;
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory.SetupGet(value => value.NamespacesUris)
                .Returns(mixedFactories ? [secondUri, firstUri] : [firstUri, firstUri, secondUri]);
            factory.Setup(value => value.CreateAsync(It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()))
                .Returns((IServerInternal staged, ApplicationConfiguration _, CancellationToken _) =>
                {
                    creationOrder.Add("async");
                    IServerInternal shared = owner!.CurrentInstance;
                    sharedNamespaces = shared.NamespaceUris;
                    firstIndexDuringCreate = sharedNamespaces.GetIndex(firstUri);
                    secondIndexDuringCreate = sharedNamespaces.GetIndex(secondUri);
                    staged.NamespaceUris.GetIndexOrAppend(discoveredUri);
                    Assert.That(sharedNamespaces.GetIndex(discoveredUri), Is.EqualTo(-1),
                        "Only configured declarations are bootstrap metadata; candidate discoveries stay private.");
                    if (reject)
                    {
                        throw new InvalidOperationException("Injected initial factory creation failure.");
                    }
                    var manager = new Mock<IAsyncNodeManager>();
                    manager.SetupGet(value => value.NamespaceUris).Returns([firstUri, secondUri, discoveredUri]);
                    manager.Setup(value => value.CreateAddressSpaceAsync(
                        It.IsAny<IDictionary<NodeId, IList<IReference>>>(), It.IsAny<CancellationToken>()))
                        .Callback(() => addressSpaceOrder.Add("async"));
                    asyncManager = manager.Object;
                    Assert.That(shared.NodeManager.AsyncNodeManagers, Does.Not.Contain(manager.Object));
                    return new ValueTask<IAsyncNodeManager>(manager.Object);
                });
            var syncFactory = new Mock<INodeManagerFactory>();
            syncFactory.SetupGet(value => value.NamespacesUris).Returns([firstUri, firstUri]);
            syncFactory.Setup(value => value.Create(
                It.IsAny<IServerInternal>(), It.IsAny<ApplicationConfiguration>()))
                .Returns(() =>
                {
                    creationOrder.Add("sync");
                    var manager = new Mock<INodeManager>();
                    manager.SetupGet(value => value.NamespaceUris).Returns([firstUri]);
                    manager.Setup(value => value.CreateAddressSpace(
                        It.IsAny<IDictionary<NodeId, IList<IReference>>>()))
                        .Callback(() => addressSpaceOrder.Add("sync"));
                    syncManager = manager.Object;
                    return manager.Object;
                });
            var fixture = new ServerFixture<StandardServer>(telemetry =>
            {
                owner = new StandardServer(telemetry);
                owner.AddNodeManager(factory.Object);
                if (mixedFactories)
                {
                    owner.AddNodeManager(syncFactory.Object);
                }
                return owner;
            })
            {
                UriScheme = Utils.UriSchemeOpcTcp,
                SecurityNone = true,
                AutoAccept = true
            };
            try
            {
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    creationOrder.Clear();
                    addressSpaceOrder.Clear();
                    if (reject)
                    {
                        ServiceResultException? failure = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                            await fixture.StartAsync(pki).ConfigureAwait(false)).ConfigureAwait(false);
                        Assert.That(failure!.InnerException, Is.TypeOf<InvalidOperationException>()
                            .With.Message.EqualTo("Injected initial factory creation failure."));
                        Assert.That(creationOrder, Has.Count.EqualTo(1));
                        Assert.That(creationOrder[0], Is.EqualTo("async"));
                        Assert.That(addressSpaceOrder, Is.Empty);
                    }
                    else
                    {
                        StandardServer started = await fixture.StartAsync(pki).ConfigureAwait(false);
                        Assert.That(started.CurrentInstance.NamespaceUris.GetIndex(discoveredUri),
                            Is.GreaterThan(3), "A successful initial publication retains candidate discoveries.");
                        string[] expectedOrder = mixedFactories ? ["async", "sync"] : ["async"];
                        Assert.That(creationOrder, Is.EqualTo(expectedOrder));
                        Assert.That(addressSpaceOrder, Is.EqualTo(expectedOrder));
                        IReadOnlyList<IAsyncNodeManager> managers =
                            started.CurrentInstance.NodeManager.AsyncNodeManagers;
                        if (mixedFactories)
                        {
                            Assert.That(managers[managers.Count - 2], Is.SameAs(asyncManager));
                            Assert.That(managers[managers.Count - 1].SyncNodeManager, Is.SameAs(syncManager));
                        }
                        else
                        {
                            Assert.That(managers[managers.Count - 1], Is.SameAs(asyncManager));
                        }
                    }
                    using (Assert.EnterMultipleScope())
                    {
                        Assert.That(firstIndexDuringCreate, Is.EqualTo(2));
                        Assert.That(secondIndexDuringCreate, Is.EqualTo(3),
                            "Repeated declarations must not consume an extra namespace index.");
                        Assert.That(sharedNamespaces!.GetIndex(Ua.Namespaces.OpcUa + "Diagnostics"), Is.EqualTo(4));
                        Assert.That(sharedNamespaces.GetIndex(discoveredUri) >= 0, Is.EqualTo(!reject));
                    }
                    await fixture.StopAsync().ConfigureAwait(false);
                    reject = false;
                }
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
                if (Directory.Exists(pki))
                {
                    Directory.Delete(pki, recursive: true);
                }
            }
        }

        /// <summary>
        /// Hosted master shutdown joins the same attempt as server stop and preserves its failure for retry.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        [NonParallelizable]
        public async Task HostedMasterShutdownJoinsServerStopAttemptAsync(bool failDelete)
        {
            string pki = Path.Combine(Path.GetTempPath(),
                nameof(NodeManagerSystemSlotTests), Guid.NewGuid().ToString("N"));
            var fixture = new ServerFixture<SystemLifetimeServer>(telemetry => new SystemLifetimeServer(telemetry))
            {
                UriScheme = Utils.UriSchemeOpcTcp,
                SecurityNone = true,
                AutoAccept = true
            };
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                SystemLifetimeServer server = await fixture.StartAsync(pki).ConfigureAwait(false);
                IMasterNodeManager master = server.CurrentInstance.NodeManager;
                SystemLifetimeCore core = server.Core!;
                core.DeleteFailuresRemaining = failDelete ? 1 : 0;
                server.BeforeStopping = () =>
                {
                    entered.TrySetResult(true);
                    return new ValueTask(release.Task);
                };
                Task masterStop = master.ShutdownAsync().AsTask();
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                Task serverStop = server.StopAsync().AsTask();
                release.TrySetResult(true);
                if (failDelete)
                {
                    AggregateException? masterFailure = await Assert.ThrowsAsync<AggregateException>(
                        () => masterStop.WaitAsync(TimeSpan.FromSeconds(10))).ConfigureAwait(false);
                    AggregateException? serverFailure = await Assert.ThrowsAsync<AggregateException>(
                        () => serverStop.WaitAsync(TimeSpan.FromSeconds(10))).ConfigureAwait(false);
                    Assert.That(serverFailure, Is.SameAs(masterFailure));
                    Assert.That(core.DeleteCount, Is.EqualTo(1));
                    Assert.That(core.DisposeCount, Is.Zero);
                }
                else
                {
                    await Task.WhenAll(masterStop, serverStop).WaitAsync(TimeSpan.FromSeconds(10))
                        .ConfigureAwait(false);
                }
                Assert.That(server.StopCount, Is.EqualTo(1));
                Assert.That(server.ListenerCount, Is.Zero);
                server.BeforeStopping = null;
                await master.ShutdownAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                await server.StopAsync().ConfigureAwait(false);
                Assert.That(server.StopCount, Is.EqualTo(failDelete ? 2 : 1));
                Assert.That(core.DeleteCount, Is.EqualTo(failDelete ? 2 : 1));
                Assert.That(core.DisposeCount, Is.EqualTo(1));
            }
            finally
            {
                release.TrySetResult(true);
                await fixture.StopAsync().ConfigureAwait(false);
                if (Directory.Exists(pki))
                {
                    Directory.Delete(pki, recursive: true);
                }
            }
        }

        /// <summary>
        /// A master-only shutdown closes its owner's listeners as well as retiring every generation.
        /// </summary>
        [Test]
        [NonParallelizable]
        public async Task HostedMasterShutdownClosesOwnerTransportsAsync()
        {
            string pki = Path.Combine(Path.GetTempPath(),
                nameof(NodeManagerSystemSlotTests), Guid.NewGuid().ToString("N"));
            var fixture = new ServerFixture<SystemLifetimeServer>(telemetry => new SystemLifetimeServer(telemetry))
            {
                UriScheme = Utils.UriSchemeOpcTcp,
                SecurityNone = true,
                AutoAccept = true
            };
            try
            {
                SystemLifetimeServer server = await fixture.StartAsync(pki).ConfigureAwait(false);
                IMasterNodeManager master = server.CurrentInstance.NodeManager;
                Assert.That(server.ListenerCount, Is.GreaterThan(0));
                await master.ShutdownAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                Assert.That(server.ListenerCount, Is.Zero);
                Assert.That(server.StopCount, Is.EqualTo(1));
                Assert.That(((NodeManagerLifecycle)server.NodeManagerLifecycle).HasPendingShutdownCleanup, Is.False);
                await server.StopAsync().ConfigureAwait(false);
                Assert.That(server.StopCount, Is.EqualTo(1), "The owner must retain the completed stop attempt.");
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
                if (Directory.Exists(pki))
                {
                    Directory.Delete(pki, recursive: true);
                }
            }
        }

        /// <summary>
        /// A failed session shutdown retains its serving dependencies and retries before physical disposal.
        /// </summary>
        [Test]
        [NonParallelizable]
        public async Task FailedSessionShutdownRetainsSystemDependenciesUntilRetryAsync()
        {
            string pki = Path.Combine(Path.GetTempPath(),
                nameof(NodeManagerSystemSlotTests), Guid.NewGuid().ToString("N"));
            var fixture = new ServerFixture<SystemLifetimeServer>(telemetry => new SystemLifetimeServer(telemetry))
            {
                UriScheme = Utils.UriSchemeOpcTcp,
                SecurityNone = true,
                AutoAccept = true
            };
            try
            {
                SystemLifetimeServer server = await fixture.StartAsync(pki).ConfigureAwait(false);
                IServerInternal instance = server.CurrentInstance;
                var master = (MasterNodeManager)instance.NodeManager;
                SystemLifetimeCore core = server.Core!;
                SystemLifetimeSessions sessions = server.Sessions!;
                sessions.ShutdownFailuresRemaining = 1;
                AggregateException? failure = await Assert.ThrowsAsync<AggregateException>(
                    () => server.StopAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10))).ConfigureAwait(false);
                Assert.That(failure!.Flatten().InnerExceptions,
                    Has.Some.TypeOf<IOException>().With.Message.EqualTo("Injected session shutdown failure."));
                Assert.That(server.CurrentInstance, Is.SameAs(instance));
                Assert.That(master.CoreNodeManager, Is.SameAs(core));
                Assert.That(core.DeleteCount, Is.Zero);
                Assert.That(core.DisposeCount, Is.Zero);
                Assert.That(sessions.ShutdownCount, Is.EqualTo(1));
                Assert.That(sessions.DisposeCount, Is.Zero);
                Assert.That(server.ListenerCount, Is.Zero);
                await server.StopAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                Assert.That(sessions.ShutdownCount, Is.EqualTo(2));
                Assert.That(sessions.DisposeCount, Is.EqualTo(1));
                Assert.That(core.DeleteCount, Is.EqualTo(1));
                Assert.That(core.DisposeCount, Is.EqualTo(1));
                await server.StopAsync().ConfigureAwait(false);
                Assert.That(server.StopCount, Is.EqualTo(2));
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
                if (Directory.Exists(pki))
                {
                    Directory.Delete(pki, recursive: true);
                }
            }
        }

        /// <summary>
        /// System-only startup reconciles injected readiness and shutdown retains failed system cleanup for retry.
        /// </summary>
        [TestCase(false, false, false)]
        [TestCase(true, false, false)]
        [TestCase(false, true, false)]
        [TestCase(true, false, true)]
        [NonParallelizable]
        public async Task SystemOnlyStartupReconcilesReadinessAndShutdownRetriesCleanupAsync(
            bool failDelete,
            bool cancelDrain,
            bool disposeServer)
        {
            string pki = Path.Combine(Path.GetTempPath(),
                nameof(NodeManagerSystemSlotTests), Guid.NewGuid().ToString("N"));
            var fixture = new ServerFixture<SystemLifetimeServer>(telemetry => new SystemLifetimeServer(telemetry))
            {
                UriScheme = Utils.UriSchemeOpcTcp,
                SecurityNone = true,
                AutoAccept = true
            };
            SystemLifetimeServer? server = null;
            try
            {
                server = await fixture.StartAsync(pki).ConfigureAwait(false);
                var lifecycle = (NodeManagerLifecycle)server.NodeManagerLifecycle;
                IServerInternal instance = server.CurrentInstance;
                var master = (MasterNodeManager)instance.NodeManager;
                SystemLifetimeCore core = server.Core!;
                Assert.That(lifecycle.Registrations.Count, Is.Zero,
                    "Protected roles must not appear as application registrations.");
                Assert.That(lifecycle.Slots.Count, Is.Zero);
                Assert.That(core.ReadyCount, Is.EqualTo(1),
                    "An empty application change set must still reconcile protected roles.");
                Assert.That(core.ReadyWhileRunning, Is.True);
                Assert.That(master.CoreNodeManager, Is.SameAs(core));
                Assert.That(master.Dispose, Throws.InvalidOperationException.With.Message.Contains("owned"));
                await Assert.ThatAsync(
                    () => master.DisposeAsync().AsTask(),
                    Throws.InvalidOperationException.With.Message.Contains("owned")).ConfigureAwait(false);
                Assert.That(core.DisposeCount, Is.Zero);
                Assert.That(master.CoreNodeManager, Is.SameAs(core));
                ArrayOf<NodeManagerSlot> systems = ((IDynamicNodeManagerHost)master).SystemSlots;
                for (int ii = 0; ii < systems.Count; ii++)
                {
                    NodeManagerSlot slot = systems[ii];
                    NodeManagerGeneration generation = slot.Current!;
                    var registration = new NodeManagerRegistration(slot.Id, generation.Number, generation.NodeManager);
                    await Assert.ThatAsync(
                        () => lifecycle.PrepareAsync([NodeManagerBatchChange.Remove(registration)]).AsTask(),
                        Throws.InvalidOperationException.With.Message.Contains("System slots")).ConfigureAwait(false);
                }
                if (cancelDrain)
                {
                    NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(core);
                    Assert.That(generation.Gate.TryEnter(out GenerationGate.Lease? admission), Is.True);
                    try
                    {
                        using var cancellation = new CancellationTokenSource();
                        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                        Task drain = lifecycle.CompleteShutdownAsync(instance, retireSystems: true, cancellation.Token)
                            .AsTask();
                        while (master.CoreNodeManager is not null)
                        {
                            await Task.Delay(10, timeout.Token).ConfigureAwait(false);
                        }
                        cancellation.Cancel();
                        await Assert.ThatAsync(() => drain,
                            Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
                        Assert.That(server.CurrentInstance, Is.SameAs(instance));
                        Assert.That(lifecycle.HasPendingShutdownCleanup, Is.True);
                        Assert.That(generation.Gate.State, Is.EqualTo(NodeManagerGenerationState.Retiring));
                        Assert.That(core.DeleteCount, Is.Zero);
                        Assert.That(core.DisposeCount, Is.Zero);
                    }
                    finally
                    {
                        admission!.Dispose();
                    }
                }
                core.DeleteFailuresRemaining = failDelete ? 1 : 0;
                if (failDelete)
                {
                    await Assert.ThatAsync(
                        () => (disposeServer
                            ? server.DisposeAsync().AsTask()
                            : server.ShutdownInternalsAsync().AsTask())
                            .WaitAsync(TimeSpan.FromSeconds(10)),
                        Throws.TypeOf<AggregateException>()).ConfigureAwait(false);
                    Assert.That(server.CurrentInstance, Is.SameAs(instance));
                    Assert.That(master.CoreNodeManager, Is.Null,
                        "Failed destruction must not republish a withdrawn system generation.");
                    Assert.That(master.ConfigurationNodeManager, Is.Null);
                    Assert.That(lifecycle.RetiredNodeManagerCount, Is.EqualTo(1));
                    Assert.That(core.DisposeCount, Is.Zero);
                    Assert.That(master.Dispose, Throws.InvalidOperationException.With.Message.Contains("owned"),
                        "A withdrawn generation still owns its cleanup dependencies until retirement finishes.");
                }
                await (disposeServer ? server.DisposeAsync().AsTask() : server.ShutdownInternalsAsync().AsTask())
                    .WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                Assert.That(master.CoreNodeManager, Is.Null);
                Assert.That(master.ConfigurationNodeManager, Is.Null);
                Assert.That(master.AsyncNodeManagers, Is.Empty);
                Assert.That(lifecycle.RetiredNodeManagerCount, Is.Zero);
                Assert.That(core.DeleteCount, Is.EqualTo(failDelete ? 2 : 1));
                Assert.That(core.DisposeCount, Is.EqualTo(1));
                Assert.That(core.ReadyCount, Is.EqualTo(1));
                Assert.That(() => server.CurrentInstance,
                    Throws.TypeOf<ServiceResultException>());
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
                server?.Dispose();
                if (Directory.Exists(pki))
                {
                    Directory.Delete(pki, recursive: true);
                }
            }
        }

        /// <summary>
        /// System access follows the exact slot's current generation, including withdrawal.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void SystemAccessorsFollowTheirCurrentSlot(bool core)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            using (MasterNodeManager master = DeterministicServerMock.CreateMasterNodeManager(server, []))
            {
                Func<IAsyncNodeManager?> read = core
                    ? () => master.CoreNodeManager
                    : () => master.ConfigurationNodeManager;
                IAsyncNodeManager original = read()!;
                NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(original);
                var configuration = new Mock<IConfigurationNodeManager>();
                configuration.As<IDiagnosticsNodeManager>();
                IAsyncNodeManager replacement = core
                    ? new Mock<ICoreNodeManager>().Object
                    : configuration.Object;
                var next = new NodeManagerGeneration(
                    generation.Slot, generation.Number + 1, replacement, NodeManagerGenerationState.Live);
                try
                {
                    generation.Slot.Unpublish(generation);
                    Assert.That(read(), Is.Null);
                    if (!core)
                    {
                        Assert.That(master.DiagnosticsNodeManager, Is.Null);
                    }
                    generation.Slot.Publish(next);
                    Assert.That(read(), Is.SameAs(replacement));
                    if (!core)
                    {
                        Assert.That(master.DiagnosticsNodeManager, Is.SameAs(replacement));
                    }
                }
                finally
                {
                    generation.Slot.Publish(generation);
                }
            }
        }

        /// <summary>
        /// An empty lifecycle must not require a dynamic publication host to complete legacy shutdown.
        /// </summary>
        [Test]
        public async Task EmptyShutdownDoesNotRequireDynamicHostAsync()
        {
            using var server = new StandardServer(Opc.Ua.Tests.NUnitTelemetryContext.Create());
            var lifecycle = (NodeManagerLifecycle)server.NodeManagerLifecycle;
            var instance = new Mock<IServerInternal>();
            await lifecycle.CompleteShutdownAsync(instance.Object, retireSystems: true).ConfigureAwait(false);
            Assert.That(lifecycle.HasPendingShutdownCleanup, Is.False);
            instance.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Hosts only the protected roles with an injected core implementation.
        /// </summary>
        private sealed class SystemLifetimeServer(ITelemetryContext telemetry) : StandardServer(telemetry)
        {
            /// <summary>
            /// Gets the injected core generation.
            /// </summary>
            internal SystemLifetimeCore? Core { get; private set; }

            /// <summary>
            /// Gets the real session manager with shutdown fault injection.
            /// </summary>
            internal SystemLifetimeSessions? Sessions { get; private set; }

            /// <summary>
            /// Gets or sets a deterministic barrier before the owner's shutdown stages.
            /// </summary>
            internal Func<ValueTask>? BeforeStopping { get; set; }

            /// <summary>
            /// Gets the number of owner shutdown attempts.
            /// </summary>
            internal int StopCount => Volatile.Read(ref m_stopCount);

            /// <summary>
            /// Gets the number of transports retained by the owner.
            /// </summary>
            internal int ListenerCount => TransportListeners.Count;

            /// <summary>
            /// Runs the actual server shutdown without stopping the fixture's transport twice.
            /// </summary>
            internal ValueTask ShutdownInternalsAsync()
            {
                return OnServerStoppingAsync();
            }

            /// <inheritdoc/>
            protected override async ValueTask OnServerStoppingAsync(CancellationToken cancellationToken = default)
            {
                Interlocked.Increment(ref m_stopCount);
                if (BeforeStopping is { } before)
                {
                    await before().ConfigureAwait(false);
                }
                await base.OnServerStoppingAsync(cancellationToken).ConfigureAwait(false);
            }

            /// <inheritdoc/>
            protected override IMainNodeManagerFactory CreateMainNodeManagerFactory(
                IServerInternal server,
                ApplicationConfiguration configuration)
            {
                var ordinary = new MainNodeManagerFactory(configuration, server);
                var factory = new Mock<IMainNodeManagerFactory>();
                factory.Setup(value => value.CreateConfigurationNodeManager())
                    .Returns(ordinary.CreateConfigurationNodeManager);
                factory.Setup(value => value.CreateCoreNodeManager(It.IsAny<ushort>()))
                    .Returns((ushort index) => Core = new SystemLifetimeCore(server, configuration, index));
                return factory.Object;
            }

            /// <inheritdoc/>
            protected override ISessionManager CreateSessionManager(
                IServerInternal server,
                ApplicationConfiguration configuration)
            {
                return Sessions = new SystemLifetimeSessions(server, configuration);
            }

            /// <summary>Number of admitted owner shutdown attempts.</summary>
            private int m_stopCount;
        }

        /// <summary>
        /// Keeps real session shutdown behavior while injecting an independently retryable failure.
        /// </summary>
        private sealed class SystemLifetimeSessions(
            IServerInternal server,
            ApplicationConfiguration configuration) : SessionManager(server, configuration)
        {
            /// <summary>
            /// Gets or sets the remaining injected shutdown failures.
            /// </summary>
            internal int ShutdownFailuresRemaining { get; set; }

            /// <summary>
            /// Gets the number of shutdown attempts.
            /// </summary>
            internal int ShutdownCount { get; private set; }

            /// <summary>
            /// Gets the number of physical disposal callbacks.
            /// </summary>
            internal int DisposeCount { get; private set; }

            /// <inheritdoc/>
            public override ValueTask ShutdownAsync(CancellationToken cancellationToken = default)
            {
                ShutdownCount++;
                if (ShutdownFailuresRemaining > 0)
                {
                    ShutdownFailuresRemaining--;
                    throw new IOException("Injected session shutdown failure.");
                }
                return base.ShutdownAsync(cancellationToken);
            }

            /// <inheritdoc/>
            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    DisposeCount++;
                }
                base.Dispose(disposing);
            }
        }

        /// <summary>
        /// Preserves real core behavior while observing readiness and checkpointed cleanup.
        /// </summary>
        private sealed class SystemLifetimeCore(
            IServerInternal server,
            ApplicationConfiguration configuration,
            ushort namespaceIndex) : CoreNodeManager(server, configuration, namespaceIndex),
            INodeManagerReadinessNotification
        {
            /// <summary>
            /// Gets the number of readiness callbacks.
            /// </summary>
            internal int ReadyCount { get; private set; }

            /// <summary>
            /// Gets whether runtime initialization preceded readiness.
            /// </summary>
            internal bool ReadyWhileRunning { get; private set; }

            /// <summary>
            /// Gets or sets the remaining injected destruction failures.
            /// </summary>
            internal int DeleteFailuresRemaining { get; set; }

            /// <summary>
            /// Gets the number of attempted destruction callbacks.
            /// </summary>
            internal int DeleteCount { get; private set; }

            /// <summary>
            /// Gets the number of disposal callbacks.
            /// </summary>
            internal int DisposeCount { get; private set; }

            /// <inheritdoc/>
            public ValueTask OnServerReadyAsync(CancellationToken cancellationToken = default)
            {
                ReadyCount++;
                ReadyWhileRunning = Server.IsRunning;
                return default;
            }

            /// <inheritdoc/>
            public override ValueTask DeleteAddressSpaceAsync(CancellationToken cancellationToken = default)
            {
                DeleteCount++;
                if (DeleteFailuresRemaining > 0)
                {
                    DeleteFailuresRemaining--;
                    throw new IOException("Injected core destruction failure.");
                }
                return base.DeleteAddressSpaceAsync(cancellationToken);
            }

            /// <inheritdoc/>
            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    DisposeCount++;
                }
                base.Dispose(disposing);
            }
        }
    }
}
