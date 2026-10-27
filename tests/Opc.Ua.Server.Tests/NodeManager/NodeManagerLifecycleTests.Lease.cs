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
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    public sealed partial class NodeManagerLifecycleTests
    {
        /// <summary>
        /// Application destruction and disposal retain both serving system roles until their callbacks finish.
        /// </summary>
        [Test]
        public async Task ApplicationShutdownCleanupKeepsSystemDependenciesLiveAsync()
        {
            var master = (MasterNodeManager)m_server.CurrentInstance.NodeManager;
            NodeManagerGeneration core = NodeManagerGenerationRegistry.GetOrCreate(master.CoreNodeManager!);
            NodeManagerGeneration configuration = NodeManagerGenerationRegistry.GetOrCreate(
                master.ConfigurationNodeManager!);
            void AssertDependencies()
            {
                Assert.That(master.CoreNodeManager, Is.SameAs(core.NodeManager));
                Assert.That(master.ConfigurationNodeManager, Is.SameAs(configuration.NodeManager));
                Assert.That(core.Gate.State, Is.EqualTo(NodeManagerGenerationState.Live));
                Assert.That(configuration.Gate.State, Is.EqualTo(NodeManagerGenerationState.Live));
            }
            Mock<IAsyncNodeManager> manager = CreateLifecycleNodeManager(
                "urn:opcfoundation.org:Tests:NodeManagerLifecycle:ShutdownDependencies");
            manager.Setup(value => value.DeleteAddressSpaceAsync(It.IsAny<CancellationToken>()))
                .Callback(AssertDependencies).Returns(default(ValueTask));
            manager.As<IDisposable>().Setup(value => value.Dispose()).Callback(AssertDependencies);
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory.Setup(value => value.CreateAsync(It.IsAny<IServerInternal>(),
                It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(manager.Object);
            await m_server.NodeManagerLifecycle.AddAsync(factory.Object, null).ConfigureAwait(false);
            try
            {
                await m_server.ShutdownInternalsAsync().AsTask()
                    .WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                manager.Verify(value => value.DeleteAddressSpaceAsync(It.IsAny<CancellationToken>()), Times.Once);
                manager.As<IDisposable>().Verify(value => value.Dispose(), Times.Once);
                Assert.That(master.CoreNodeManager, Is.Null);
                Assert.That(master.ConfigurationNodeManager, Is.Null);
                AssertServerInternalsDisposed();
            }
            finally
            {
                await FinishShutdownTestAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Shutdown signals accepted producers while their serving generation can still finish callbacks.
        /// </summary>
        [Test]
        public async Task ShutdownSignalsProducerBeforeWaitingForItsGenerationAdmissionAsync()
        {
            Mock<IAsyncNodeManager> manager = CreateLifecycleNodeManager(
                "urn:opcfoundation.org:Tests:NodeManagerLifecycle:ShutdownProducer");
            GenerationGate.Lease? admission = null;
            manager.As<INodeManagerShutdown>().Setup(value => value.PrepareForShutdownAsync()).Returns(() =>
            {
                if (admission is not null)
                {
                    Assert.That(NodeManagerGenerationRegistry.GetOrCreate(manager.Object).Gate.State,
                        Is.EqualTo(NodeManagerGenerationState.Live));
                    admission.Dispose();
                    admission = null;
                }
                return default;
            });
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory.Setup(value => value.CreateAsync(It.IsAny<IServerInternal>(),
                It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(manager.Object);
            NodeManagerRegistration registration = await m_server.NodeManagerLifecycle
                .AddAsync(factory.Object, null).ConfigureAwait(false);
            Assert.That(registration.Identity.Gate.TryEnter(out admission), Is.True);
            Task shutdown = RunWithoutExecutionContext(() => m_server.ShutdownInternalsAsync().AsTask());
            try
            {
                await shutdown.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                manager.As<INodeManagerShutdown>().Verify(value => value.PrepareForShutdownAsync(), Times.AtLeastOnce);
                AssertServerInternalsDisposed();
            }
            finally
            {
                Interlocked.Exchange(ref admission, null)?.Dispose();
                await shutdown.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                await FinishShutdownTestAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Additional lifecycle handles cannot hide serving generations from the server's shutdown owner.
        /// </summary>
        [Test]
        public async Task AdditionalLifecycleHandleUsesServerGenerationOwnerAsync()
        {
            var lifecycle = (NodeManagerLifecycle)m_server.NodeManagerLifecycle;
            using var handle = new NodeManagerLifecycle(m_server);
            TrackingLifecycleNodeManager? manager = null;
            NodeManagerRegistration registration = await handle.AddAsync(
                CreateTrackingNodeManagementFactory(kFirstRegistrationValue, value => manager = value), null)
                .ConfigureAwait(false);
            AssertLifecycleValue(manager);
            try
            {
                Assert.That(lifecycle.Registrations.ToArray(), Does.Contain(registration));
                Assert.That(handle.Registrations.ToArray(), Does.Contain(registration));
                handle.Dispose();
                Assert.That(handle.IsShuttingDown, Is.True);
                Assert.That(lifecycle.IsShuttingDown, Is.False);
                await Assert.ThatAsync(
                    () => handle.PrepareAsync([NodeManagerBatchChange.Remove(registration)]).AsTask(),
                    Throws.TypeOf<ObjectDisposedException>()).ConfigureAwait(false);
                await m_server.ShutdownInternalsAsync().AsTask()
                    .WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                Assert.That(lifecycle.Registrations.Count, Is.Zero);
                Assert.That(lifecycle.RetiredNodeManagerCount, Is.Zero);
                Assert.That(manager.DeleteAddressSpaceCount, Is.EqualTo(1));
                Assert.That(manager.DisposeCount, Is.EqualTo(1));
                AssertServerInternalsDisposed();
            }
            finally
            {
                await FinishShutdownTestAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Shutdown intent must not suppress the close callback of a session bound to a serving generation.
        /// </summary>
        [Test]
        public async Task ServerShutdownDeliversSessionClosingBeforeGenerationRetirementAsync()
        {
            TrackingLifecycleNodeManager? manager = null;
            await m_server.NodeManagerLifecycle.AddAsync(
                CreateTrackingNodeManagementFactory(kFirstRegistrationValue, value => manager = value), null)
                .ConfigureAwait(false);
            AssertLifecycleValue(manager);
            ISession session = m_server.CurrentInstance.SessionManager.GetSession(
                m_requestHeader.AuthenticationToken)!;
            NodeId sessionId = session.Id;
            Assert.That(manager.ActivatedSessionIds.ToArray(), Does.Contain(sessionId));
            try
            {
                await m_server.ShutdownInternalsAsync().AsTask()
                    .WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                Assert.That(manager.ClosedSessionIds.ToArray(), Has.Exactly(1).EqualTo(sessionId));
                Assert.That(manager.DeleteAddressSpaceCount, Is.EqualTo(1));
                Assert.That(manager.DisposeCount, Is.EqualTo(1));
                AssertServerInternalsDisposed();
            }
            finally
            {
                await FinishShutdownTestAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Freezing lifecycle mutations leaves serving generations available for final session callbacks.
        /// </summary>
        [Test]
        public async Task ShutdownFreezePreservesServingAdmissionsUntilRemovalAsync()
        {
            var lifecycle = (NodeManagerLifecycle)m_server.NodeManagerLifecycle;
            NodeManagerRegistration registration = await lifecycle.AddAsync(
                CreateTrackingNodeManagementFactory(kFirstRegistrationValue, _ => { }), null)
                .ConfigureAwait(false);
            await lifecycle.BeginShutdownAsync(m_server.CurrentInstance).ConfigureAwait(false);
            GenerationGate.Lease? admission = null;
            try
            {
                Assert.That(lifecycle.IsShuttingDown, Is.True);
                Assert.That(registration.Identity.Gate.State, Is.EqualTo(NodeManagerGenerationState.Live));
                Assert.That(registration.Identity.Gate.TryEnter(out admission), Is.True);
                await Assert.ThatAsync(
                    () => lifecycle.PrepareAsync([NodeManagerBatchChange.Remove(registration)]).AsTask(),
                    Throws.InvalidOperationException.With.Message.Contains("shutting down")).ConfigureAwait(false);
            }
            finally
            {
                admission?.Dispose();
                await m_server.ShutdownInternalsAsync().AsTask()
                    .WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                await FinishShutdownTestAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Shutdown withdraws routing before waiting for an already admitted generation to drain.
        /// </summary>
        [Test]
        public async Task ShutdownPublishesRemovalBeforeWaitingForAdmittedGenerationAsync()
        {
            var lifecycle = (NodeManagerLifecycle)m_server.NodeManagerLifecycle;
            TrackingLifecycleNodeManager? manager = null;
            NodeManagerRegistration registration = await lifecycle.AddAsync(
                CreateTrackingNodeManagementFactory(kFirstRegistrationValue, value => manager = value), null)
                .ConfigureAwait(false);
            AssertLifecycleValue(manager);
            var master = (MasterNodeManager)m_server.CurrentInstance.NodeManager;
            Assert.That(registration.Identity.Gate.TryEnter(out GenerationGate.Lease? admission), Is.True);
            Task shutdown = RunWithoutExecutionContext(() => m_server.ShutdownInternalsAsync().AsTask());
            try
            {
                await WaitForNodeManagerVisibilityAsync(master, manager, visible: false).ConfigureAwait(false);
                Assert.That(lifecycle.Registrations.ToArray(), Does.Not.Contain(registration));
                Assert.That(registration.Identity.Gate.State, Is.EqualTo(NodeManagerGenerationState.Retiring));
                Assert.That(shutdown.IsCompleted, Is.False);
                Assert.That(manager.DeleteAddressSpaceCount, Is.Zero);
                Assert.That(manager.DisposeCount, Is.Zero);
            }
            finally
            {
                admission!.Dispose();
                await shutdown.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                await FinishShutdownTestAsync().ConfigureAwait(false);
            }
            Assert.That(manager.DeleteAddressSpaceCount, Is.EqualTo(1));
            Assert.That(manager.DisposeCount, Is.EqualTo(1));
            Assert.That(lifecycle.RetiredNodeManagerCount, Is.Zero);
        }

        /// <summary>
        /// Failed application cleanup retains system resources and retries only unfinished retirement stages.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ShutdownCleanupFailureRetainsInternalsAndSystemDependenciesForRetryAsync(bool failDisposal)
        {
            var lifecycle = (NodeManagerLifecycle)m_server.NodeManagerLifecycle;
            TrackingLifecycleNodeManager? manager = null;
            await lifecycle.AddAsync(
                CreateTrackingNodeManagementFactory(kFirstRegistrationValue, value => manager = value), null)
                .ConfigureAwait(false);
            AssertLifecycleValue(manager);
            TrackingLifecycleNodeManager? independent = null;
            await lifecycle.AddAsync(CreateTrackingNodeManagementFactory(
                kSecondRegistrationValue, value => independent = value,
                "urn:opcfoundation.org:Tests:NodeManagerLifecycle:ShutdownIndependent"), null).ConfigureAwait(false);
            AssertLifecycleValue(independent);
            IServerInternal server = m_server.CurrentInstance;
            var core = server.NodeManager.CoreNodeManager;
            if (failDisposal)
            {
                manager.DisposeFailuresRemaining = 1;
            }
            else
            {
                manager.DeleteAddressSpaceFailuresRemaining = 1;
            }
            try
            {
                await Assert.ThatAsync(
                    () => m_server.ShutdownInternalsAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)),
                    Throws.TypeOf<AggregateException>()).ConfigureAwait(false);
                Assert.That(m_server.CurrentInstance, Is.SameAs(server));
                Assert.That(server.NodeManager.CoreNodeManager, Is.SameAs(core));
                Assert.That(lifecycle.Registrations.Count, Is.Zero);
                Assert.That(lifecycle.RetiredNodeManagerCount, Is.EqualTo(1));
                Assert.That(independent.DeleteAddressSpaceCount, Is.EqualTo(1));
                Assert.That(independent.DisposeCount, Is.EqualTo(1));
                Assert.That(manager.DeleteAddressSpaceCount, Is.EqualTo(1));
                Assert.That(manager.DisposeCount, Is.EqualTo(failDisposal ? 1 : 0));
                await m_server.ShutdownInternalsAsync().AsTask()
                    .WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                Assert.That(manager.DeleteAddressSpaceCount, Is.EqualTo(failDisposal ? 1 : 2));
                Assert.That(manager.DisposeCount, Is.EqualTo(failDisposal ? 2 : 1));
                Assert.That(independent.DeleteAddressSpaceCount, Is.EqualTo(1));
                Assert.That(independent.DisposeCount, Is.EqualTo(1));
                Assert.That(lifecycle.RetiredNodeManagerCount, Is.Zero);
                AssertServerInternalsDisposed();
            }
            finally
            {
                await FinishShutdownTestAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Failed shutdown publication notifications cannot skip independent application cleanup or retire dependencies.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task ShutdownPostSwapFailureStillReclaimsApplicationsBeforeRetiringSystemsAsync(
            bool notifyBeforeFailure,
            bool failCleanup)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var server = (ServerInternalData)m_server.CurrentInstance;
            IMasterNodeManager master = server.NodeManager;
            NodeManagerGeneration core = NodeManagerGenerationRegistry.GetOrCreate(master.CoreNodeManager!);
            NodeManagerGeneration configuration = NodeManagerGenerationRegistry.GetOrCreate(
                master.ConfigurationNodeManager!);
            var failure = new InvalidOperationException("The shutdown publication notification failed.");
            bool failPublication = false;
            Mock<IMasterNodeManager> proxy = CreatePostSwapPublicationFailureHost(
                master, () => failPublication, notifyBeforeFailure, failure);
            PropertyInfo nodeManagerProperty = typeof(ServerInternalData).GetProperty(nameof(ServerInternalData.NodeManager))
                ?? throw new AssertionException("The bound master NodeManager property was not found.");
            nodeManagerProperty.SetValue(server, proxy.Object);
            TrackingLifecycleNodeManager? manager = null;
            TrackingLifecycleNodeManager? independent = null;
            try
            {
                var lifecycle = (NodeManagerLifecycle)m_server.NodeManagerLifecycle;
                await lifecycle.AddAsync(CreateTrackingNodeManagementFactory(
                    kFirstRegistrationValue, value => manager = value), timeout.Token).ConfigureAwait(false);
                await lifecycle.AddAsync(CreateTrackingNodeManagementFactory(
                    kSecondRegistrationValue, value => independent = value,
                    "urn:opcfoundation.org:Tests:NodeManagerLifecycle:ShutdownPostSwapIndependent"),
                    timeout.Token).ConfigureAwait(false);
                AssertLifecycleValue(manager);
                AssertLifecycleValue(independent);
                manager.DeleteAddressSpaceFailuresRemaining = failCleanup ? 1 : 0;
                failPublication = true;

                Exception? observed = await Assert.CatchAsync<Exception>(() =>
                    lifecycle.CompleteShutdownAsync(server, retireSystems: true, timeout.Token).AsTask())
                    .ConfigureAwait(false);

                Assert.That(observed, Is.Not.Null);
                Assert.That(observed!.ToString(), Does.Contain(failure.Message));
                Assert.That(independent.DeleteAddressSpaceCount, Is.EqualTo(1));
                Assert.That(independent.DisposeCount, Is.EqualTo(1));
                Assert.That(manager.DeleteAddressSpaceCount, Is.EqualTo(1));
                Assert.That(manager.DisposeCount, Is.EqualTo(failCleanup ? 0 : 1));
                Assert.That(lifecycle.Registrations.Count, Is.Zero);
                Assert.That(lifecycle.RetiredNodeManagerCount, Is.EqualTo(failCleanup ? 1 : 0));
                Assert.That(core.Gate.State, Is.EqualTo(NodeManagerGenerationState.Live));
                Assert.That(configuration.Gate.State, Is.EqualTo(NodeManagerGenerationState.Live));
                if (failCleanup)
                {
                    Assert.That(observed, Is.TypeOf<AggregateException>());
                    Assert.That(((AggregateException)observed).Flatten().InnerExceptions, Has.Count.EqualTo(2));
                    Assert.That(((AggregateException)observed).Flatten().InnerExceptions,
                        Has.Exactly(1).TypeOf<SentinelException>());
                }
                else
                {
                    Assert.That(observed, Is.TypeOf<InvalidOperationException>());
                }

                failPublication = false;
                await lifecycle.CompleteShutdownAsync(server, timeout.Token).ConfigureAwait(false);
                Assert.That(manager.DeleteAddressSpaceCount, Is.EqualTo(failCleanup ? 2 : 1));
                Assert.That(manager.DisposeCount, Is.EqualTo(1));
                Assert.That(independent.DeleteAddressSpaceCount, Is.EqualTo(1));
                Assert.That(independent.DisposeCount, Is.EqualTo(1));
                Assert.That(lifecycle.RetiredNodeManagerCount, Is.Zero);
                Assert.That(core.Gate.State, Is.EqualTo(NodeManagerGenerationState.Live));
                Assert.That(configuration.Gate.State, Is.EqualTo(NodeManagerGenerationState.Live));
            }
            finally
            {
                if (manager is not null)
                {
                    manager.DeleteAddressSpaceFailuresRemaining = 0;
                }
                nodeManagerProperty.SetValue(server, master);
                await FinishShutdownTestAsync().ConfigureAwait(false);
            }
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task CommittedBatchReleasesOperationLeaseBeforeOptionalDisposalAsync(
            bool failPublication, bool disposeBeforeShutdown)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var lifecycle = (NodeManagerLifecycle)m_server.NodeManagerLifecycle;
            IServerInternal server = m_server.CurrentInstance;
            TrackingLifecycleNodeManager? manager = null;
            IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(CreateTrackingNodeManagementFactory(
                    kFirstRegistrationValue, value => manager = value))], null, timeout.Token)
                .ConfigureAwait(false);
            AssertLifecycleValue(manager);
            var failure = new IOException("The committed publication callback failed.");
            bool drainedWithoutDisposal;
            try
            {
                NodeManagerBatchResult result = await prepared.CommitAsync(_ => default, () =>
                {
                    if (failPublication)
                    {
                        throw failure;
                    }
                }, timeout.Token).ConfigureAwait(false);
                Assert.That(prepared.IsCommitted, Is.True);
                Assert.That(result.Registrations.Count, Is.EqualTo(1));
                if (failPublication)
                {
                    Assert.That(result.CleanupFailure, Is.TypeOf<AggregateException>());
                    Assert.That(RequireLifecycleValue(result.CleanupFailure as AggregateException)
                        .Flatten().InnerExceptions,
                        Does.Contain(failure));
                }
                else
                {
                    Assert.That(result.CleanupFailure, Is.Null);
                }
                if (disposeBeforeShutdown)
                {
                    await prepared.DisposeAsync().ConfigureAwait(false);
                }
                Task shutdown = lifecycle.BeginShutdownAsync(server, timeout.Token).AsTask();
                drainedWithoutDisposal = shutdown.IsCompleted;
                await prepared.DisposeAsync().ConfigureAwait(false);
                await prepared.DisposeAsync().ConfigureAwait(false);
                await shutdown.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(manager.DisposeCount, Is.Zero, "Disposing the committed owner must not undo publication.");
                Assert.That(GetBranchRegistration(lifecycle, result.Registrations[0].Id),
                    Is.SameAs(result.Registrations[0]));
            }
            finally
            {
                await prepared.DisposeAsync().ConfigureAwait(false);
                await m_server.ShutdownInternalsAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(manager.DeleteAddressSpaceCount, Is.EqualTo(1));
                Assert.That(manager.DisposeCount, Is.EqualTo(1));
                AssertServerInternalsDisposed();
                await FinishShutdownTestAsync().ConfigureAwait(false);
            }
            Assert.That(drainedWithoutDisposal, Is.True,
                "A completed commit, including a committed warning, must not retain an operation until DisposeAsync.");
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task UncommittedBatchRetainsOperationLeaseUntilAbortAsync(bool rejectDecision)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var lifecycle = (NodeManagerLifecycle)m_server.NodeManagerLifecycle;
            IServerInternal server = m_server.CurrentInstance;
            TrackingLifecycleNodeManager? candidate = null;
            IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(CreateTrackingNodeManagementFactory(
                    kFirstRegistrationValue, value => candidate = value))], null, timeout.Token)
                .ConfigureAwait(false);
            AssertLifecycleValue(candidate);
            try
            {
                if (rejectDecision)
                {
                    await Assert.ThatAsync(() => prepared.CommitAsync(
                        _ => throw new IOException("Confirmed noncommit."), timeout.Token).AsTask(),
                        Throws.TypeOf<IOException>()).ConfigureAwait(false);
                }
                Task shutdown = lifecycle.BeginShutdownAsync(server, timeout.Token).AsTask();
                Assert.That(shutdown.IsCompleted, Is.False,
                    "Unpublished candidate cleanup still owns the operation until it is asynchronously disposed.");
                Assert.That(prepared.IsCommitted, Is.False);
                Assert.That(candidate.DisposeCount, Is.Zero);
                await prepared.DisposeAsync().ConfigureAwait(false);
                await prepared.DisposeAsync().ConfigureAwait(false);
                await shutdown.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(candidate.DeleteAddressSpaceCount, Is.EqualTo(1));
                Assert.That(candidate.DisposeCount, Is.EqualTo(1));
                Assert.That(GetBranchRegistrations(lifecycle).IsEmpty, Is.True);
            }
            finally
            {
                await prepared.DisposeAsync().ConfigureAwait(false);
                await m_server.ShutdownInternalsAsync(timeout.Token).ConfigureAwait(false);
                AssertServerInternalsDisposed();
                await FinishShutdownTestAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task CommittedBatchKeepsOperationLeaseWhileReadinessAndDisposalArePendingAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            ReadinessLifecycleNodeManager? manager = null;
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory.Setup(value => value.CreateAsync(It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()))
                .Returns((IServerInternal server, ApplicationConfiguration configuration, CancellationToken _) =>
                {
                    manager = new ReadinessLifecycleNodeManager(
                        server, configuration, m_logger, kFirstRegistrationValue)
                    {
                        ReadinessCallback = async _ =>
                        {
                            entered.TrySetResult(true);
                            await release.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                        }
                    };
                    return new ValueTask<IAsyncNodeManager>(manager);
                });
            var lifecycle = (NodeManagerLifecycle)m_server.NodeManagerLifecycle;
            IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(factory.Object)], null, timeout.Token).ConfigureAwait(false);
            AssertLifecycleValue(manager);
            Task<NodeManagerBatchResult> commit = prepared.CommitAsync(_ => default, timeout.Token).AsTask();
            Task? disposal = null;
            Task? shutdown = null;
            try
            {
                await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(prepared.IsCommitted, Is.True);
                disposal = prepared.DisposeAsync().AsTask();
                shutdown = lifecycle.BeginShutdownAsync(m_server.CurrentInstance, timeout.Token).AsTask();
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(commit.IsCompleted, Is.False);
                    Assert.That(disposal.IsCompleted, Is.False);
                    Assert.That(shutdown.IsCompleted, Is.False);
                    Assert.That(manager.DisposeCount, Is.Zero);
                }
            }
            finally
            {
                release.TrySetResult(true);
                await commit.WaitAsync(timeout.Token).ConfigureAwait(false);
                await prepared.DisposeAsync().ConfigureAwait(false);
                if (disposal is not null)
                {
                    await disposal.WaitAsync(timeout.Token).ConfigureAwait(false);
                }
                if (shutdown is not null)
                {
                    await shutdown.WaitAsync(timeout.Token).ConfigureAwait(false);
                }
                Assert.That(manager.ReadinessCompletedCount, Is.EqualTo(1));
                await m_server.ShutdownInternalsAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(manager.DisposeCount, Is.EqualTo(1));
                AssertServerInternalsDisposed();
                await FinishShutdownTestAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task ServerShutdownWaitsForBatchDecisionBeforeDisposingHostAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var lifecycle = (NodeManagerLifecycle)m_server.NodeManagerLifecycle;
            TrackingLifecycleNodeManager? manager = null;
            IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(CreateTrackingNodeManagementFactory(
                    kFirstRegistrationValue, value => manager = value))], null, timeout.Token)
                .ConfigureAwait(false);
            AssertLifecycleValue(manager);
            Task<NodeManagerBatchResult> commit = prepared.CommitAsync(async _ =>
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
            }, timeout.Token).AsTask();
            Task? shutdown = null;
            try
            {
                await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                shutdown = m_server.ShutdownInternalsAsync(timeout.Token).AsTask();
                Assert.That(shutdown.IsCompleted, Is.False,
                    "Server shutdown must wait while the durable decision is pending.");
                Assert.That(manager.DisposeCount, Is.Zero);

                release.TrySetResult(true);
                NodeManagerBatchResult result = await commit.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(prepared.IsCommitted, Is.True);
                Assert.That(result.Registrations.Count, Is.EqualTo(1));
                await shutdown.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(manager.DeleteAddressSpaceCount, Is.EqualTo(1));
                Assert.That(manager.DisposeCount, Is.EqualTo(1));
                AssertServerInternalsDisposed();
            }
            finally
            {
                release.TrySetResult(true);
                await prepared.DisposeAsync().ConfigureAwait(false);
                if (shutdown is null)
                {
                    await m_server.ShutdownInternalsAsync(timeout.Token).ConfigureAwait(false);
                }
                else
                {
                    await shutdown.WaitAsync(timeout.Token).ConfigureAwait(false);
                }
                AssertServerInternalsDisposed();
                await FinishShutdownTestAsync().ConfigureAwait(false);
            }
        }
    }
}
