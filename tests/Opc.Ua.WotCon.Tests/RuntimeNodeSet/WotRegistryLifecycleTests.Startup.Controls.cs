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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Server;
using Opc.Ua.Server.Fluent;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.WotCon.Bindings;
using Opc.Ua.WotCon.Server;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.WotCon.Tests.RuntimeNodeSet
{
    public sealed partial class WotRegistryLifecycleTests
    {
        [Test]
        public async Task StoredRegistryStartupCancellationRetainsParentForCleanupAsync()
        {
            var runtime = new StartupRuntimeProbe();
            (int baseline, DeadlineProjectionHost host, _) =
                await PrepareControlledStartupAsync(runtime).ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            Task<NodeManagerRegistration> startup = AddStoredRegistryAsync(cancellation.Token);
            try
            {
                await runtime.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                m_registryRegistration = FindStartupRegistryRegistration();
            }
            finally
            {
                cancellation.Cancel();
            }

            InvalidOperationException failure = Assert.ThrowsAsync<InvalidOperationException>(
                async () => await startup.ConfigureAwait(false))!;
            Assert.That(failure.InnerException, Is.InstanceOf<OperationCanceledException>());
            Assert.That(runtime.ReceivedToken.IsCancellationRequested, Is.True);
            Assert.That(host.DeadlineExpired, Is.False);
            Assert.That(host.AddCalls, Is.EqualTo(1));
            Assert.That(m_registryRegistration.Generation, Is.EqualTo(1));
            Assert.That(m_server.NodeManagerLifecycle.Registrations.Count, Is.EqualTo(baseline + 1));
            Assert.That(StartupResource().LoadState, Is.Not.EqualTo(WoTLoadStateEnum.Active));
            await AssertStartupSensorAsync(readable: false).ConfigureAwait(false);
            await VerifyStartupCleanupAndReentryAsync(baseline).ConfigureAwait(false);
        }

        [Test]
        public async Task StoredRegistryFailedMaterializationIsReportedAndRemovableAsync()
        {
            var runtime = new StartupRuntimeProbe(fail: true);
            runtime.Release.TrySetResult(true);
            (int baseline, DeadlineProjectionHost host, _) =
                await PrepareControlledStartupAsync(runtime).ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));

            InvalidOperationException failure = Assert.ThrowsAsync<InvalidOperationException>(
                async () => await AddStoredRegistryAsync(cancellation.Token).ConfigureAwait(false))!;
            Assert.That(failure.InnerException, Is.InstanceOf<ServiceResultException>());
            Assert.That(((ServiceResultException)failure.InnerException!).StatusCode,
                Is.EqualTo(StatusCodes.BadConfigurationError));
            m_registryRegistration = FindStartupRegistryRegistration();
            Assert.That(host.AddCalls, Is.EqualTo(1));
            Assert.That(host.DeadlineExpired, Is.False);
            Assert.That(runtime.Entered.Task.IsCompleted, Is.True,
                "The failure must come from the real runtime NodeSet preparation.");
            Assert.That(m_server.NodeManagerLifecycle.Registrations.Count, Is.EqualTo(baseline + 1));
            Assert.That(StartupResource().LoadState, Is.EqualTo(WoTLoadStateEnum.Failed));
            await AssertStartupSensorAsync(readable: false).ConfigureAwait(false);
            await VerifyStartupCleanupAndReentryAsync(baseline).ConfigureAwait(false);
        }

        [Test]
        public async Task StoredRegistryRemovalReleasesProjectionsAndAllowsReentryAsync()
        {
            (int baseline, DeadlineProjectionHost host, RemovalDeadlineProjectionHost cleanup) =
                await PrepareControlledStartupAsync().ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            m_registryRegistration = await AddStoredRegistryAsync(cancellation.Token).ConfigureAwait(false);
            Assert.That(host.AddCalls, Is.EqualTo(1));
            Assert.That(StartupResource().LoadState, Is.EqualTo(WoTLoadStateEnum.Active));
            await AssertStartupSensorAsync(readable: true).ConfigureAwait(false);

            await VerifyStartupCleanupAndReentryAsync(baseline).ConfigureAwait(false);
            Assert.That(cleanup.RemoveCalls, Is.EqualTo(1));
            Assert.That(cleanup.DeadlineExpired, Is.False,
                "Parent deletion must not serialize against its dependent projection removal.");
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task StoredRegistryReadinessRejectsConcurrentChangesWithoutOrphansAsync(bool reload)
        {
            var runtime = new StartupRuntimeProbe();
            (int baseline, DeadlineProjectionHost host, RemovalDeadlineProjectionHost cleanup) =
                await PrepareControlledStartupAsync(runtime).ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            Task<NodeManagerRegistration> startup = AddStoredRegistryAsync(cancellation.Token);
            try
            {
                await runtime.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                m_registryRegistration = FindStartupRegistryRegistration();
                Assert.That(startup.IsCompleted, Is.False);
                Assert.That(m_server.NodeManagerLifecycle.Registrations.Count, Is.EqualTo(baseline + 1));
                await AssertStartupSensorAsync(readable: false).ConfigureAwait(false);
                using var conflictDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                InvalidOperationException failure = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                {
                    if (reload)
                    {
                        await m_server.NodeManagerLifecycle.ReloadAsync(
                            m_registryRegistration,
                            new WotRegistryNodeManagerFactory(m_options, m_registry, m_coordinator),
                            callerContext: null,
                            conflictDeadline.Token).ConfigureAwait(false);
                    }
                    else
                    {
                        await m_server.NodeManagerLifecycle.RemoveAsync(
                            m_registryRegistration, callerContext: null, conflictDeadline.Token).ConfigureAwait(false);
                    }
                })!;
                Assert.That(failure.Message, Does.Contain("completing readiness"));
                Assert.That(conflictDeadline.IsCancellationRequested, Is.False);
                Assert.That(cleanup.RemoveCalls, Is.Zero);
            }
            finally
            {
                runtime.Release.TrySetResult(true);
                m_registryRegistration = await startup.ConfigureAwait(false);
            }

            Assert.That(host.DeadlineExpired, Is.False);
            Assert.That(host.AddCalls, Is.EqualTo(1));
            Assert.That(m_server.NodeManagerLifecycle.Registrations.Count, Is.EqualTo(baseline + 2));
            await AssertStartupSensorAsync(readable: true).ConfigureAwait(false);
            await VerifyStartupCleanupAndReentryAsync(baseline).ConfigureAwait(false);
            Assert.That(cleanup.DeadlineExpired, Is.False);
        }

        [Test]
        public async Task StoredRegistryStartupRejectsConcurrentNativeRefreshAsync()
        {
            var runtime = new StartupRuntimeProbe();
            (_, DeadlineProjectionHost host, _) = await PrepareControlledStartupAsync(runtime).ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            Task<NodeManagerRegistration> startup = AddStoredRegistryAsync(cancellation.Token);
            NodeId registryId = NodeId.Null;
            NodeId refreshId = NodeId.Null;
            Variant[] inputs =
            [
                new Variant(ArrayOf<ExtensionObject>.Empty),
                Variant.FromStructure(new WoTRefreshOptionsDataType()),
                new Variant(0u),
                new Variant("startup-concurrent-refresh")
            ];
            try
            {
                await runtime.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                registryId = ExpandedNodeId.ToNodeId(
                    Opc.Ua.WotCon.ObjectIds.WoTRegistry, m_server.CurrentInstance.NamespaceUris);
                refreshId = await FindChildAsync(registryId, Opc.Ua.WotCon.BrowseNames.Refresh).ConfigureAwait(false);
                using var requestCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                using var requestLifetime = new RequestLifetime(requestCancellation.Token);
                m_requestHeader.Timestamp = DateTimeUtc.Now;
                CallResponse response = await m_server.CallAsync(
                    m_secureChannelContext,
                    m_requestHeader,
                    [new CallMethodRequest { ObjectId = registryId, MethodId = refreshId, InputArguments = inputs }],
                    requestLifetime).ConfigureAwait(false);
                Assert.That(response.Results[0].StatusCode, Is.EqualTo(StatusCodes.BadServerTooBusy),
                    "A tracked Refresh request must not wait on the coordinator while startup drains requests.");
                Assert.That(requestCancellation.IsCancellationRequested, Is.False);
                Assert.That(startup.IsCompleted, Is.False);
            }
            finally
            {
                runtime.Release.TrySetResult(true);
                m_registryRegistration = await startup.ConfigureAwait(false);
            }

            Assert.That(host.DeadlineExpired, Is.False);
            Assert.That(host.AddCalls, Is.EqualTo(1));
            await AssertStartupSensorAsync(readable: true).ConfigureAwait(false);
            CallMethodResult refreshed = await CallAsync(registryId, refreshId, inputs).ConfigureAwait(false);
            Assert.That(refreshed.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(m_coordinator.Generation, Is.EqualTo(2),
                "Explicit Refresh must be admitted again after startup completes.");
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task InitialStoredRegistryIsReadyBeforeServerStartupReturnsAsync(bool dependencyInjection)
        {
            await StartStoredInitialServerAsync(dependencyInjection).ConfigureAwait(false);
            (m_requestHeader, m_secureChannelContext) = await m_server
                .CreateAndActivateSessionAsync(TestContext.CurrentContext.Test.Name).ConfigureAwait(false);
            m_requestHeader.Timestamp = DateTimeUtc.Now;

            Assert.That(m_initialStartupHost.DeadlineExpired, Is.False);
            Assert.That(m_initialStartupHost.AddCalls, Is.EqualTo(1));
            Assert.That(m_coordinator.Generation, Is.EqualTo(1),
                "Initial readiness must run once, not again for a changed manager snapshot.");
            Assert.That(m_server.NodeManagerLifecycle.Registrations.Count, Is.EqualTo(1),
                "The static registry is not a runtime registration; only its real projection is.");
            Assert.That(StartupResource().LoadState, Is.EqualTo(WoTLoadStateEnum.Active));
            await AssertStartupSensorAsync(readable: true).ConfigureAwait(false);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task InitialStoredRegistryFailureCleansPublishedDependenciesAsync(bool cancel)
        {
            var readiness = new StartupReadinessProbe();
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            Task<ReferenceServer> startup = StartStoredInitialServerAsync(
                dependencyInjection: false, afterRegistry: readiness, cancellationToken: cancellation.Token);
            try
            {
                await readiness.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                Assert.That(m_server.NodeManagerLifecycle.Registrations.Count, Is.EqualTo(1),
                    "The real sensor projection must be committed before initial startup fails.");
                Assert.That(StartupResource().LoadState, Is.EqualTo(WoTLoadStateEnum.Active));
                if (cancel)
                {
                    cancellation.Cancel();
                    OperationCanceledException failure = Assert.CatchAsync<OperationCanceledException>(
                        async () => await startup.ConfigureAwait(false))!;
                    Assert.That(failure.CancellationToken, Is.EqualTo(readiness.ReceivedToken));
                    Assert.That(readiness.ReceivedToken.IsCancellationRequested, Is.True);
                }
                else
                {
                    readiness.Release.TrySetResult(true);
                    InvalidOperationException failure = Assert.ThrowsAsync<InvalidOperationException>(
                        async () => await startup.ConfigureAwait(false))!;
                    Assert.That(failure, Is.SameAs(readiness.Failure));
                }
            }
            finally
            {
                cancellation.Cancel();
                readiness.Release.TrySetResult(true);
            }

            Assert.That(m_initialStartupHost.DeadlineExpired, Is.False);
            Assert.That(m_initialStartupHost.AddCalls, Is.EqualTo(1));
            Assert.That(m_server.NodeManagerLifecycle.IsShuttingDown, Is.True);
            Assert.That(m_server.NodeManagerLifecycle.Registrations, Is.Empty);
            Assert.That(readiness.DeleteCalls, Is.EqualTo(1));
            Assert.That(readiness.Disposed, Is.True);
            Assert.That(ServiceResult.IsBad(m_server.ServerError), Is.True);
            Assert.That(StartupResource().DefaultVersion!.HasContent, Is.True,
                "Startup cleanup must not delete the persisted input.");
        }

        [Test]
        public async Task ServerRestartKeepsLifecycleUsableForStoredRegistryAsync()
        {
            (int baseline, _, _) = await PrepareControlledStartupAsync().ConfigureAwait(false);
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            m_registryRegistration = await AddStoredRegistryAsync(cancellation.Token).ConfigureAwait(false);
            await AssertStartupSensorAsync(readable: true).ConfigureAwait(false);
            await CloseActiveSessionAsync().ConfigureAwait(false);
            await m_server.StopAsync(cancellation.Token).ConfigureAwait(false);
            Assert.That(lifecycle.IsShuttingDown, Is.True);
            Assert.That(lifecycle.Registrations, Is.Empty);

            m_coordinator.Dispose();
            m_registry.Dispose();
            m_startupStore!.Dispose();
            await ReopenStartupControlStoreAsync().ConfigureAwait(false);
            var host = new DeadlineProjectionHost(new LifecycleWotProjectionHost(lifecycle));
            m_coordinator = new WotMaterializationCoordinator(
                m_registry, host, documentConverter: new SensorConverter());
            await m_server.StartAsync(m_fixture.Config, cancellation.Token).ConfigureAwait(false);
            Assert.That(m_server.NodeManagerLifecycle, Is.SameAs(lifecycle),
                "Injected lifecycle references must remain valid when their server is restarted.");
            Assert.That(lifecycle.IsShuttingDown, Is.False);
            m_registryRegistration = await AddStoredRegistryAsync(cancellation.Token).ConfigureAwait(false);
            (m_requestHeader, m_secureChannelContext) = await m_server
                .CreateAndActivateSessionAsync(TestContext.CurrentContext.Test.Name).ConfigureAwait(false);
            m_requestHeader.Timestamp = DateTimeUtc.Now;
            Assert.That(host.DeadlineExpired, Is.False);
            Assert.That(host.AddCalls, Is.EqualTo(1));
            Assert.That(lifecycle.Registrations.Count, Is.EqualTo(baseline + 2));
            await AssertStartupSensorAsync(readable: true).ConfigureAwait(false);
        }

        private async Task<(int Baseline, DeadlineProjectionHost Host, RemovalDeadlineProjectionHost Cleanup)>
            PrepareControlledStartupAsync(IWotProjectionBindingRuntimeFactory? runtime = null)
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await m_server.NodeManagerLifecycle.RemoveAsync(
                m_registryRegistration, callerContext: null, cancellation.Token).ConfigureAwait(false);
            m_registryRegistration = null!;
            int baseline = m_server.NodeManagerLifecycle.Registrations.Count;
            await WriteStartupControlStoreAsync().ConfigureAwait(false);
            var cleanup = new RemovalDeadlineProjectionHost(
                new LifecycleWotProjectionHost(m_server.NodeManagerLifecycle, runtime));
            var host = new DeadlineProjectionHost(cleanup);
            m_coordinator = new WotMaterializationCoordinator(
                m_registry, host, documentConverter: new SensorConverter());
            return (baseline, host, cleanup);
        }

        private async Task WriteStartupControlStoreAsync()
        {
            m_coordinator.Dispose();
            m_registry.Dispose();
            m_startupStore?.Dispose();
            string storeRoot = Path.Combine(m_pkiRoot, "startup-control-registry");
            using (var store = new FileWotRegistryStore(storeRoot))
            using (var writer = new WotRegistryService(store, m_options.Bounds, m_options.IdentityBindings))
            {
                await writer.InitializeAsync().ConfigureAwait(false);
                await writer.UpsertResourceAsync(new WotUpsertResourceRequest
                {
                    GroupId = WotRegistryGroups.ThingDescriptions,
                    ResourceId = "sensor",
                    Kind = WoTDocumentKindEnum.ThingDescription,
                    Content = ByteString.From(SensorConverter.BuildContent(1))
                }).ConfigureAwait(false);
            }
            await ReopenStartupControlStoreAsync().ConfigureAwait(false);
        }

        private async Task ReopenStartupControlStoreAsync()
        {
            m_startupStore = new FileWotRegistryStore(Path.Combine(m_pkiRoot, "startup-control-registry"));
            m_registry = new WotRegistryService(m_startupStore, m_options.Bounds, m_options.IdentityBindings);
            await m_registry.InitializeAsync().ConfigureAwait(false);
            Assert.That(StartupResource().DefaultVersion!.HasContent, Is.True);
        }

        private Task<NodeManagerRegistration> AddStoredRegistryAsync(CancellationToken cancellationToken)
        {
            return m_server.NodeManagerLifecycle.AddAsync(
                new WotRegistryNodeManagerFactory(m_options, m_registry, m_coordinator),
                callerContext: null, cancellationToken).AsTask();
        }

        private NodeManagerRegistration FindStartupRegistryRegistration()
        {
            return m_server.NodeManagerLifecycle.Registrations.Find(registration =>
                registration.NodeManager is WotRegistryNodeManager manager &&
                ReferenceEquals(manager.Coordinator, m_coordinator)) ??
                throw new InvalidOperationException("The committed registry registration is missing.");
        }

        private WotResource StartupResource()
        {
            return m_registry.Current.FindResource(WotRegistryGroups.ThingDescriptions, "sensor") ??
                throw new InvalidOperationException("The persisted sensor resource is missing.");
        }

        private async Task AssertStartupSensorAsync(bool readable)
        {
            int index = m_server.CurrentInstance.NamespaceUris.GetIndex(kModelNamespaceUri);
            Assert.That(index, Is.GreaterThan(0));
            DataValue value = await ReadValueAsync(new NodeId(kValueNodeId, checked((ushort)index)))
                .ConfigureAwait(false);
            if (readable)
            {
                Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(value.GetValue<int>(-1), Is.EqualTo(1));
            }
            else
            {
                Assert.That(value.StatusCode.Code,
                    Is.EqualTo(StatusCodes.BadNodeIdUnknown).Or.EqualTo(StatusCodes.BadNodeIdInvalid));
            }
        }

        private async Task VerifyStartupCleanupAndReentryAsync(int baseline)
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            NodeManagerRegistration previous = m_registryRegistration;
            await m_server.NodeManagerLifecycle.RemoveAsync(
                previous, callerContext: null, cancellation.Token).ConfigureAwait(false);
            Assert.That(m_server.NodeManagerLifecycle.Registrations.Count, Is.EqualTo(baseline));
            await AssertStartupSensorAsync(readable: false).ConfigureAwait(false);

            m_coordinator.Dispose();
            m_registry.Dispose();
            m_startupStore!.Dispose();
            await ReopenStartupControlStoreAsync().ConfigureAwait(false);
            var host = new DeadlineProjectionHost(new LifecycleWotProjectionHost(m_server.NodeManagerLifecycle));
            m_coordinator = new WotMaterializationCoordinator(
                m_registry, host, documentConverter: new SensorConverter());
            m_registryRegistration = await AddStoredRegistryAsync(cancellation.Token).ConfigureAwait(false);
            Assert.That(m_registryRegistration.Id, Is.Not.EqualTo(previous.Id));
            Assert.That(m_registryRegistration.Generation, Is.EqualTo(1));
            Assert.That(m_server.NodeManagerLifecycle.Registrations.Count, Is.EqualTo(baseline + 2));
            Assert.That(host.AddCalls, Is.EqualTo(1));
            Assert.That(host.DeadlineExpired, Is.False);
            Assert.That(StartupResource().LoadState, Is.EqualTo(WoTLoadStateEnum.Active));
            await AssertStartupSensorAsync(readable: true).ConfigureAwait(false);
        }

        private async Task<ReferenceServer> StartStoredInitialServerAsync(
            bool dependencyInjection,
            IAsyncNodeManagerFactory? afterRegistry = null,
            CancellationToken cancellationToken = default)
        {
            await CloseActiveSessionAsync().ConfigureAwait(false);
            await m_fixture.StopAsync().ConfigureAwait(false);
            await WriteStartupControlStoreAsync().ConfigureAwait(false);
            m_registryRegistration = null!;
            m_fixture = new ServerFixture<ReferenceServer>(telemetry =>
            {
                var server = new StartupTokenReferenceServer(telemetry, cancellationToken);
                m_server = server;
                m_initialStartupHost = new DeadlineProjectionHost(
                    new LifecycleWotProjectionHost(server.NodeManagerLifecycle));
                m_coordinator = new WotMaterializationCoordinator(
                    m_registry, m_initialStartupHost, documentConverter: new SensorConverter());
                if (dependencyInjection)
                {
                    var services = new ServiceCollection();
                    services.AddSingleton(m_options);
                    services.AddSingleton<IWotRegistryService>(m_registry);
                    services.AddSingleton(m_coordinator);
                    services.AddOpcUa().AddWotRegistryServer();
                    using ServiceProvider provider = services.BuildServiceProvider();
                    server.AddNodeManager(provider.GetRequiredService<WotRegistryNodeManagerFactory>());
                }
                else
                {
                    server.AddNodeManager(new WotRegistryNodeManagerFactory(m_options, m_registry, m_coordinator));
                }
                if (afterRegistry is not null)
                {
                    server.AddNodeManager(afterRegistry);
                }
                return server;
            })
            {
                UriScheme = Utils.UriSchemeOpcTcp,
                SecurityNone = true,
                AutoAccept = true
            };
            return await m_fixture.StartAsync(m_pkiRoot).ConfigureAwait(false);
        }

        private DeadlineProjectionHost m_initialStartupHost = null!;

        private sealed class StartupReadinessProbe : IAsyncNodeManagerFactory
        {
            public ArrayOf<string> NamespacesUris { get; } = [kNamespaceUri];

            public TaskCompletionSource<bool> Entered { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public TaskCompletionSource<bool> Release { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public InvalidOperationException Failure { get; } = new("Controlled initial readiness failure.");

            public CancellationToken ReceivedToken { get; private set; }

            public int DeleteCalls => Volatile.Read(ref m_deleteCalls);

            public bool Disposed => Volatile.Read(ref m_disposed) != 0;

            public ValueTask<IAsyncNodeManager> CreateAsync(
                IServerInternal server,
                ApplicationConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<IAsyncNodeManager>(new StartupReadinessNodeManager(server, configuration, this));
            }

            private const string kNamespaceUri = "urn:wot:e2e:startup-probe";
            private int m_deleteCalls;
            private int m_disposed;

            private sealed class StartupReadinessNodeManager(
                IServerInternal server,
                ApplicationConfiguration configuration,
                StartupReadinessProbe owner) : AsyncCustomNodeManager(server, configuration, kNamespaceUri),
                INodeManagerReadinessParticipant
            {
                public async ValueTask OnServerReadyAsync(CancellationToken cancellationToken = default)
                {
                    owner.ReceivedToken = cancellationToken;
                    owner.Entered.TrySetResult(true);
                    await owner.Release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                    throw owner.Failure;
                }

                public override async ValueTask DeleteAddressSpaceAsync(CancellationToken cancellationToken = default)
                {
                    Interlocked.Increment(ref owner.m_deleteCalls);
                    await base.DeleteAddressSpaceAsync(cancellationToken).ConfigureAwait(false);
                }

                protected override void Dispose(bool disposing)
                {
                    if (disposing)
                    {
                        Interlocked.Exchange(ref owner.m_disposed, 1);
                    }
                    base.Dispose(disposing);
                }
            }
        }

        private sealed class StartupRuntimeProbe(bool fail = false) : IWotProjectionBindingRuntimeFactory
        {
            public TaskCompletionSource<bool> Entered { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public TaskCompletionSource<bool> Release { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public CancellationToken ReceivedToken { get; private set; }

            public async ValueTask<IAsyncDisposable?> CreateAsync(
                INodeManagerBuilder builder,
                ArrayOf<WotBindingPlan> bindingPlans,
                CancellationToken cancellationToken = default)
            {
                ReceivedToken = cancellationToken;
                Entered.TrySetResult(true);
                await Release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                if (fail)
                {
                    throw new InvalidOperationException("Controlled startup materialization failure.");
                }
                return null;
            }
        }

        private sealed class StartupTokenReferenceServer(
            ITelemetryContext telemetry, CancellationToken startupToken) : ReferenceServer(telemetry)
        {
            protected override async ValueTask StartApplicationAsync(
                ApplicationConfiguration configuration, CancellationToken cancellationToken = default)
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, startupToken);
                await base.StartApplicationAsync(configuration, linked.Token).ConfigureAwait(false);
            }
        }

        private sealed class RemovalDeadlineProjectionHost(IWotProjectionHost inner) : IWotProjectionHost
        {
            public int RemoveCalls => Volatile.Read(ref m_removeCalls);

            public bool DeadlineExpired => Volatile.Read(ref m_deadlineExpired) != 0;

            public ValueTask<WotProjectionHandle> AddAsync(
                WotProjectionDocument document, CancellationToken cancellationToken = default)
            {
                return inner.AddAsync(document, cancellationToken);
            }

            public ValueTask<WotProjectionHandle> ShadowReloadAsync(
                WotProjectionHandle current, WotProjectionDocument document,
                CancellationToken cancellationToken = default)
            {
                return inner.ShadowReloadAsync(current, document, cancellationToken);
            }

            public ValueTask<WotProjectionHandle> ImmediateReloadAsync(
                WotProjectionHandle current, WotProjectionDocument document,
                CancellationToken cancellationToken = default)
            {
                return inner.ImmediateReloadAsync(current, document, cancellationToken);
            }

            public async ValueTask RemoveAsync(
                WotProjectionHandle handle, CancellationToken cancellationToken = default)
            {
                Interlocked.Increment(ref m_removeCalls);
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                deadline.CancelAfter(TimeSpan.FromSeconds(5));
                try
                {
                    await inner.RemoveAsync(handle, deadline.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (
                    deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                {
                    Interlocked.Exchange(ref m_deadlineExpired, 1);
                    throw;
                }
            }

            private int m_removeCalls;
            private int m_deadlineExpired;
        }
    }
}
