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
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.TestFramework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    public sealed partial class NodeManagerLifecycleTests
    {
        private static readonly TimeSpan s_readinessTimeout = TimeSpan.FromSeconds(5);

        private const string kReadinessProbeNamespaceUri = kModelNamespaceUri + ":ReadinessProbe";

        /// <summary>
        /// Readiness runs on the published manager, can register a real child, and remains
        /// part of the Add operation even when a synchronous factory exposes both adapters.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        [Category("NodeManagerReadiness")]
        public async Task AddAsyncReadinessPublishesAndAwaitsNestedRegistrationAsync(bool synchronousFactory)
        {
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            using var timeout = new CancellationTokenSource(s_readinessTimeout);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            ReadinessLifecycleNodeManager participant = null;
            TrackingLifecycleNodeManager childManager = null;
            NodeManagerRegistration observed = null;
            NodeManagerRegistration child = null;

            Task<NodeManagerRegistration> addTask = StartReadinessChangeAsync(
                ReadinessChange.Add,
                null,
                synchronousFactory,
                kGeneration1Value,
                manager =>
                {
                    participant = manager;
                    manager.ReadinessCallback = async ct =>
                    {
                        Assert.That(ct, Is.EqualTo(timeout.Token));
                        observed = FindReadinessRegistration();
                        Assert.That(observed.Generation, Is.EqualTo(1));
                        AssertReadinessManager(observed, manager, synchronousFactory);
                        await AssertReadinessValueAsync(kModelNamespaceUri, kGeneration1Value)
                            .ConfigureAwait(false);

                        child = await lifecycle.AddAsync(
                            CreateTrackingNodeManagementFactory(
                                kSecondRegistrationValue,
                                created => childManager = created,
                                kSecondModelNamespaceUri,
                                ReadinessNodeId(kModelNamespaceUri, kRootNodeId)),
                            null,
                            ct).AsTask().WaitAsync(s_readinessTimeout, ct).ConfigureAwait(false);
                        Assert.That(child.Id, Is.Not.EqualTo(observed.Id));
                        Assert.That(child.Generation, Is.EqualTo(1));
                        await AssertReadinessValueAsync(kSecondModelNamespaceUri, kSecondRegistrationValue)
                            .ConfigureAwait(false);
                        entered.TrySetResult(true);
                        await release.Task.WaitAsync(ct).ConfigureAwait(false);
                    };
                },
                timeout.Token);

            NodeManagerRegistration added = null;
            try
            {
                await entered.Task.WaitAsync(s_readinessTimeout).ConfigureAwait(false);
                Assert.That(addTask.IsCompleted, Is.False, "Add must await the participant's unfinished work.");
                Assert.That(lifecycle.Registrations, Has.Count.EqualTo(2));
                Assert.That(participant.ReadinessCount, Is.EqualTo(1));
                Assert.That(participant.ReadinessCompletedCount, Is.Zero);
                Assert.That(participant.DeleteAddressSpaceCount, Is.Zero);
            }
            finally
            {
                release.TrySetResult(true);
                added = await addTask.WaitAsync(s_readinessTimeout).ConfigureAwait(false);
            }

            Assert.That(added, Is.SameAs(observed));
            Assert.That(FindReadinessRegistration(), Is.SameAs(added));
            Assert.That(participant.ReadinessCount, Is.EqualTo(1));
            Assert.That(participant.ReadinessCompletedCount, Is.EqualTo(1));
            await AssertReadinessValueAsync(kModelNamespaceUri, kGeneration1Value).ConfigureAwait(false);
            await RemoveReadinessRegistrationAsync(child).ConfigureAwait(false);
            await RemoveReadinessRegistrationAsync(added, participant, synchronousFactory).ConfigureAwait(false);
            Assert.That(childManager.DeleteAddressSpaceCount, Is.EqualTo(1));
            Assert.That(childManager.DisposeCount, Is.EqualTo(1));
            Assert.That(participant.DeleteAddressSpaceCount, Is.EqualTo(1));
            Assert.That(lifecycle.Registrations, Is.Empty);
            await AssertReadinessNodeUnknownAsync(kModelNamespaceUri).ConfigureAwait(false);
            await AssertReadinessNodeUnknownAsync(kSecondModelNamespaceUri).ConfigureAwait(false);
        }

        /// <summary>
        /// All replacement-factory overloads run readiness on committed gen2, not on a
        /// prepared candidate. The old manager is deliberately an async reload participant.
        /// </summary>
        [TestCase(ReadinessChange.Reload, false)]
        [TestCase(ReadinessChange.Reload, true)]
        [TestCase(ReadinessChange.ShadowReload, false)]
        [TestCase(ReadinessChange.ShadowReload, true)]
        [TestCase(ReadinessChange.ImmediateReload, false)]
        [TestCase(ReadinessChange.ImmediateReload, true)]
        [Category("NodeManagerReadiness")]
        public async Task ReloadReadinessObservesCommittedGenerationAndAwaitsNestedRegistrationAsync(
            ReadinessChange change,
            bool synchronousFactory)
        {
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            using var timeout = new CancellationTokenSource(s_readinessTimeout);
            TrackingLifecycleNodeManager originalManager = null;
            NodeManagerRegistration original = await lifecycle.AddAsync(
                CreateTrackingNodeManagementFactory(
                    kGeneration1Value,
                    manager => originalManager = manager),
                null,
                timeout.Token).AsTask().WaitAsync(s_readinessTimeout).ConfigureAwait(false);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            ReadinessLifecycleNodeManager participant = null;
            TrackingLifecycleNodeManager childManager = null;
            NodeManagerRegistration observed = null;
            NodeManagerRegistration child = null;

            Task<NodeManagerRegistration> reloadTask = StartReadinessChangeAsync(
                change,
                original,
                synchronousFactory,
                kGeneration2Value,
                manager =>
                {
                    participant = manager;
                    manager.ReadinessCallback = async ct =>
                    {
                        Assert.That(ct, Is.EqualTo(timeout.Token));
                        observed = FindReadinessRegistration();
                        Assert.That(observed.Id, Is.EqualTo(original.Id));
                        Assert.That(observed.Generation, Is.EqualTo(2));
                        Assert.That(observed, Is.Not.SameAs(original));
                        AssertReadinessManager(observed, manager, synchronousFactory);
                        await AssertReadinessValueAsync(kModelNamespaceUri, kGeneration2Value)
                            .ConfigureAwait(false);
                        child = await lifecycle.AddAsync(
                            CreateTrackingNodeManagementFactory(
                                kSecondRegistrationValue,
                                created => childManager = created,
                                kSecondModelNamespaceUri,
                                ReadinessNodeId(kModelNamespaceUri, kRootNodeId)),
                            null,
                            ct).AsTask().WaitAsync(s_readinessTimeout, ct).ConfigureAwait(false);
                        Assert.That(child.Id, Is.Not.EqualTo(original.Id));
                        await AssertReadinessValueAsync(kSecondModelNamespaceUri, kSecondRegistrationValue)
                            .ConfigureAwait(false);
                        entered.TrySetResult(true);
                        await release.Task.WaitAsync(ct).ConfigureAwait(false);
                    };
                },
                timeout.Token);

            NodeManagerRegistration next = null;
            try
            {
                await entered.Task.WaitAsync(s_readinessTimeout).ConfigureAwait(false);
                Assert.That(reloadTask.IsCompleted, Is.False, "Reload must await committed-generation readiness.");
                Assert.That(lifecycle.Registrations, Has.Count.EqualTo(2));
                Assert.That(participant.ReadinessCount, Is.EqualTo(1));
                Assert.That(participant.ReadinessCompletedCount, Is.Zero);
                Assert.That(participant.DeleteAddressSpaceCount, Is.Zero);
            }
            finally
            {
                release.TrySetResult(true);
                next = await reloadTask.WaitAsync(s_readinessTimeout).ConfigureAwait(false);
            }

            Assert.That(next, Is.SameAs(observed));
            Assert.That(FindReadinessRegistration(), Is.SameAs(next));
            Assert.That(next.Id, Is.EqualTo(original.Id));
            Assert.That(next.Generation, Is.EqualTo(2));
            Assert.That(participant.ReadinessCount, Is.EqualTo(1));
            Assert.That(participant.ReadinessCompletedCount, Is.EqualTo(1));
            Assert.That(originalManager.DeleteAddressSpaceCount, Is.EqualTo(1));
            Assert.That(originalManager.DisposeCount, Is.EqualTo(1));
            await AssertReadinessValueAsync(kModelNamespaceUri, kGeneration2Value).ConfigureAwait(false);
            await RemoveReadinessRegistrationAsync(child).ConfigureAwait(false);
            await RemoveReadinessRegistrationAsync(next, participant, synchronousFactory).ConfigureAwait(false);
            Assert.That(childManager.DeleteAddressSpaceCount, Is.EqualTo(1));
            Assert.That(childManager.DisposeCount, Is.EqualTo(1));
            Assert.That(participant.DeleteAddressSpaceCount, Is.EqualTo(1));
            Assert.That(lifecycle.Registrations, Is.Empty);
            await AssertReadinessNodeUnknownAsync(kModelNamespaceUri).ConfigureAwait(false);
        }

        /// <summary>
        /// Adds the optional-participant invariant to the existing preparation rollback
        /// tests: a candidate with real, partly created nodes is never made ready.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        [Category("NodeManagerReadiness")]
        public async Task PreparationFailureNeverInvokesReadinessAsync(bool reload)
        {
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            using var timeout = new CancellationTokenSource(s_readinessTimeout);
            NodeManagerRegistration original = null;
            TrackingLifecycleNodeManager originalManager = null;
            if (reload)
            {
                original = await lifecycle.AddAsync(
                    CreateTrackingNodeManagementFactory(
                        kGeneration1Value,
                        manager => originalManager = manager),
                    null,
                    timeout.Token).AsTask().WaitAsync(s_readinessTimeout).ConfigureAwait(false);
            }

            var expected = new SentinelException("Readiness candidate preparation failed.");
            ReadinessLifecycleNodeManager participant = null;
            Task<NodeManagerRegistration> operation = StartReadinessChangeAsync(
                reload ? ReadinessChange.Reload : ReadinessChange.Add,
                original,
                synchronousFactory: false,
                kGeneration2Value,
                manager =>
                {
                    participant = manager;
                    manager.PreparationFailure = expected;
                },
                timeout.Token);
            SentinelException failure = await ExpectReadinessExceptionAsync<SentinelException>(operation)
                .ConfigureAwait(false);

            Assert.That(failure, Is.SameAs(expected));
            Assert.That(participant.ReadinessCount, Is.Zero);
            Assert.That(participant.ReadinessCompletedCount, Is.Zero);
            Assert.That(participant.DeleteAddressSpaceCount, Is.EqualTo(1));
            Assert.That(participant.DisposeCount, Is.EqualTo(1));
            Assert.That(participant.Find(ReadinessNodeId(kModelNamespaceUri, kValueNodeId)), Is.Null);
            if (reload)
            {
                Assert.That(lifecycle.Registrations, Has.Count.EqualTo(1));
                Assert.That(FindReadinessRegistration(), Is.SameAs(original));
                Assert.That(originalManager.DeleteAddressSpaceCount, Is.Zero);
                Assert.That(originalManager.DisposeCount, Is.Zero);
                await AssertReadinessValueAsync(kModelNamespaceUri, kGeneration1Value).ConfigureAwait(false);
                await RemoveReadinessRegistrationAsync(original).ConfigureAwait(false);
            }
            else
            {
                await AssertReadinessNodeUnknownAsync(kModelNamespaceUri).ConfigureAwait(false);
            }
            Assert.That(lifecycle.Registrations, Is.Empty);
        }

        /// <summary>
        /// An Add callback exception is postcommit and preserves the exact live handle
        /// for explicit cleanup and a later, independent registration.
        /// </summary>
        [Test]
        [Category("NodeManagerReadiness")]
        public async Task AddAsyncReadinessFailureRetainsRegistrationForCleanupAsync()
        {
            using var timeout = new CancellationTokenSource(s_readinessTimeout);
            var expected = new SentinelException("Published Add readiness failed.");
            ReadinessLifecycleNodeManager participant = null;
            NodeManagerRegistration observed = null;
            Task<NodeManagerRegistration> operation = StartReadinessChangeAsync(
                ReadinessChange.Add,
                null,
                synchronousFactory: false,
                kGeneration1Value,
                manager =>
                {
                    participant = manager;
                    manager.ReadinessCallback = async ct =>
                    {
                        Assert.That(ct, Is.EqualTo(timeout.Token));
                        observed = FindReadinessRegistration();
                        await AssertReadinessValueAsync(kModelNamespaceUri, kGeneration1Value)
                            .ConfigureAwait(false);
                        throw expected;
                    };
                },
                timeout.Token);
            InvalidOperationException failure = await ExpectReadinessExceptionAsync<InvalidOperationException>(
                operation).ConfigureAwait(false);

            Assert.That(failure.Message, Does.Contain("added").And.Contain("readiness"));
            Assert.That(failure.InnerException, Is.SameAs(expected));
            Assert.That(FindReadinessRegistration(), Is.SameAs(observed));
            Assert.That(observed.Generation, Is.EqualTo(1));
            Assert.That(observed.NodeManager, Is.SameAs(participant));
            Assert.That(participant.ReadinessCount, Is.EqualTo(1));
            Assert.That(participant.ReadinessCompletedCount, Is.Zero);
            Assert.That(participant.DeleteAddressSpaceCount, Is.Zero);
            Assert.That(participant.DisposeCount, Is.Zero);
            await AssertReadinessValueAsync(kModelNamespaceUri, kGeneration1Value).ConfigureAwait(false);
            await AssertReadinessCleanupAndRecoveryAsync(observed, participant).ConfigureAwait(false);
        }

        /// <summary>
        /// Both cancellation from the participant and a cancelled caller whose
        /// participant returns normally must fail Add without losing committed identity.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        [Category("NodeManagerReadiness")]
        public async Task AddAsyncReadinessCancellationRetainsRegistrationForCleanupAsync(bool observeCancellation)
        {
            using var caller = new CancellationTokenSource(s_readinessTimeout);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            ReadinessLifecycleNodeManager participant = null;
            NodeManagerRegistration observed = null;
            CancellationToken receivedToken = default;
            OperationCanceledException callbackCancellation = null;
            Task<NodeManagerRegistration> operation = StartReadinessChangeAsync(
                ReadinessChange.Add,
                null,
                synchronousFactory: false,
                kGeneration1Value,
                manager =>
                {
                    participant = manager;
                    manager.ReadinessCallback = async ct =>
                    {
                        receivedToken = ct;
                        observed = FindReadinessRegistration();
                        await AssertReadinessValueAsync(kModelNamespaceUri, kGeneration1Value)
                            .ConfigureAwait(false);
                        entered.TrySetResult(true);
                        // Intentionally independent of ct: the false case requires the
                        // lifecycle's post-callback cancellation check to reject success.
                        await release.Task.WaitAsync(s_readinessTimeout, CancellationToken.None).ConfigureAwait(false);
                        if (observeCancellation)
                        {
                            try
                            {
                                ct.ThrowIfCancellationRequested();
                            }
                            catch (OperationCanceledException exception)
                            {
                                callbackCancellation = exception;
                                throw;
                            }
                        }
                    };
                },
                caller.Token);

            InvalidOperationException failure = null;
            try
            {
                await entered.Task.WaitAsync(s_readinessTimeout).ConfigureAwait(false);
                Assert.That(receivedToken, Is.EqualTo(caller.Token));
                Assert.That(operation.IsCompleted, Is.False);
                caller.Cancel();
            }
            finally
            {
                release.TrySetResult(true);
                failure = await ExpectReadinessExceptionAsync<InvalidOperationException>(operation)
                    .ConfigureAwait(false);
            }

            Assert.That(failure.Message, Does.Contain("added").And.Contain("readiness"));
            Assert.That(failure.InnerException, Is.InstanceOf<OperationCanceledException>());
            Assert.That(
                ((OperationCanceledException)failure.InnerException).CancellationToken,
                Is.EqualTo(caller.Token));
            if (observeCancellation)
            {
                Assert.That(failure.InnerException, Is.SameAs(callbackCancellation));
            }
            Assert.That(FindReadinessRegistration(), Is.SameAs(observed));
            Assert.That(observed.Generation, Is.EqualTo(1));
            Assert.That(observed.NodeManager, Is.SameAs(participant));
            Assert.That(participant.ReadinessCount, Is.EqualTo(1));
            Assert.That(participant.ReadinessCompletedCount, Is.EqualTo(observeCancellation ? 0 : 1));
            Assert.That(participant.DeleteAddressSpaceCount, Is.Zero);
            Assert.That(participant.DisposeCount, Is.Zero);
            await AssertReadinessValueAsync(kModelNamespaceUri, kGeneration1Value).ConfigureAwait(false);
            await AssertReadinessCleanupAndRecoveryAsync(observed, participant).ConfigureAwait(false);
        }

        /// <summary>
        /// Callback failures in every reload mode report the authoritative next handle,
        /// including cancellation, and release the readiness claim for explicit cleanup.
        /// </summary>
        [TestCase(ReadinessChange.Reload, false)]
        [TestCase(ReadinessChange.Reload, true)]
        [TestCase(ReadinessChange.ShadowReload, false)]
        [TestCase(ReadinessChange.ShadowReload, true)]
        [TestCase(ReadinessChange.ImmediateReload, false)]
        [TestCase(ReadinessChange.ImmediateReload, true)]
        [Category("NodeManagerReadiness")]
        public async Task ReloadReadinessFailureReportsExactCommittedRegistrationAsync(
            ReadinessChange change,
            bool cancel)
        {
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            using var caller = new CancellationTokenSource(s_readinessTimeout);
            TrackingLifecycleNodeManager originalManager = null;
            NodeManagerRegistration original = await lifecycle.AddAsync(
                CreateTrackingNodeManagementFactory(
                    kGeneration1Value,
                    manager => originalManager = manager),
                null,
                caller.Token).AsTask().WaitAsync(s_readinessTimeout).ConfigureAwait(false);
            Exception expected = cancel
                ? new OperationCanceledException("Committed reload readiness cancelled.", caller.Token)
                : new SentinelException("Committed reload readiness failed.");
            ReadinessLifecycleNodeManager participant = null;
            NodeManagerRegistration observed = null;
            Task<NodeManagerRegistration> operation = StartReadinessChangeAsync(
                change,
                original,
                synchronousFactory: false,
                kGeneration2Value,
                manager =>
                {
                    participant = manager;
                    manager.ReadinessCallback = async ct =>
                    {
                        Assert.That(ct, Is.EqualTo(caller.Token));
                        observed = FindReadinessRegistration();
                        Assert.That(observed.Id, Is.EqualTo(original.Id));
                        Assert.That(observed.Generation, Is.EqualTo(2));
                        await AssertReadinessValueAsync(kModelNamespaceUri, kGeneration2Value)
                            .ConfigureAwait(false);
                        if (cancel)
                        {
                            caller.Cancel();
                            Assert.That(ct.IsCancellationRequested, Is.True);
                        }
                        throw expected;
                    };
                },
                caller.Token);
            NodeManagerReloadCommittedException failure =
                await ExpectReadinessExceptionAsync<NodeManagerReloadCommittedException>(operation)
                    .ConfigureAwait(false);

            Assert.That(failure.Message, Does.Contain("replacement NodeManager is live"));
            Assert.That(failure.InnerException, Is.SameAs(expected));
            Assert.That(failure.Registration, Is.SameAs(observed));
            Assert.That(FindReadinessRegistration(), Is.SameAs(failure.Registration));
            Assert.That(failure.Registration.Id, Is.EqualTo(original.Id));
            Assert.That(failure.Registration.Generation, Is.EqualTo(2));
            Assert.That(failure.Registration.NodeManager, Is.SameAs(participant));
            Assert.That(participant.ReadinessCount, Is.EqualTo(1));
            Assert.That(participant.ReadinessCompletedCount, Is.Zero);
            Assert.That(participant.DeleteAddressSpaceCount, Is.Zero);
            Assert.That(participant.DisposeCount, Is.Zero);
            Assert.That(originalManager.DeleteAddressSpaceCount, Is.EqualTo(1));
            Assert.That(originalManager.DisposeCount, Is.EqualTo(1));
            await AssertReadinessValueAsync(kModelNamespaceUri, kGeneration2Value).ConfigureAwait(false);
            await AssertReadinessCleanupAndRecoveryAsync(failure.Registration, participant).ConfigureAwait(false);
        }

        /// <summary>
        /// Readiness reserves only its generation. Conflicts must reject before invoking
        /// factories or deletion, not wait for readiness while retaining the global gate.
        /// </summary>
        [TestCase(ReadinessChange.Add)]
        [TestCase(ReadinessChange.Reload)]
        [TestCase(ReadinessChange.ShadowReload)]
        [TestCase(ReadinessChange.ImmediateReload)]
        [Category("NodeManagerReadiness")]
        public async Task ReadinessRejectsSameHandleChangesWithoutBlockingIndependentRegistrationAsync(
            ReadinessChange change)
        {
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            using var timeout = new CancellationTokenSource(s_readinessTimeout);
            NodeManagerRegistration original = null;
            if (change != ReadinessChange.Add)
            {
                original = await lifecycle.AddAsync(
                    CreateNodeManagementFactory(kGeneration1Value, includeEuRange: false),
                    null,
                    timeout.Token).AsTask().WaitAsync(s_readinessTimeout).ConfigureAwait(false);
            }

            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            ReadinessLifecycleNodeManager participant = null;
            NodeManagerRegistration observed = null;
            Task<NodeManagerRegistration> operation = StartReadinessChangeAsync(
                change,
                original,
                synchronousFactory: false,
                kGeneration2Value,
                manager =>
                {
                    participant = manager;
                    manager.ReadinessCallback = async ct =>
                    {
                        observed = FindReadinessRegistration();
                        entered.TrySetResult(true);
                        await release.Task.WaitAsync(ct).ConfigureAwait(false);
                    };
                },
                timeout.Token);
            var asyncReplacement = new Mock<IAsyncNodeManagerFactory>(MockBehavior.Strict);
            var syncReplacement = new Mock<INodeManagerFactory>(MockBehavior.Strict);
            NodeManagerRegistration completed = null;

            try
            {
                await entered.Task.WaitAsync(s_readinessTimeout).ConfigureAwait(false);
                Assert.That(operation.IsCompleted, Is.False);
                InvalidOperationException removeFailure =
                    await ExpectReadinessExceptionAsync<InvalidOperationException>(
                        lifecycle.RemoveAsync(observed, null, timeout.Token).AsTask()).ConfigureAwait(false);
                Assert.That(removeFailure.Message, Does.Contain("completing readiness"));

                ReadinessChange[] reloads =
                [
                    ReadinessChange.Reload,
                    ReadinessChange.ShadowReload,
                    ReadinessChange.ImmediateReload
                ];
                foreach (ReadinessChange reload in reloads)
                {
                    InvalidOperationException reloadFailure =
                        await ExpectReadinessExceptionAsync<InvalidOperationException>(
                            InvokeReadinessChangeAsync(
                                lifecycle,
                                reload,
                                observed,
                                asyncReplacement.Object,
                                timeout.Token).AsTask()).ConfigureAwait(false);
                    Assert.That(reloadFailure.Message, Does.Contain("completing readiness"));

                    // One held Add covers the sync competitor overloads too; the other
                    // cases focus on ownership of each distinct reload operation.
                    if (change == ReadinessChange.Add)
                    {
                        InvalidOperationException syncFailure =
                            await ExpectReadinessExceptionAsync<InvalidOperationException>(
                                InvokeReadinessChangeAsync(
                                    lifecycle,
                                    reload,
                                    observed,
                                    syncReplacement.Object,
                                    timeout.Token).AsTask()).ConfigureAwait(false);
                        Assert.That(syncFailure.Message, Does.Contain("completing readiness"));
                    }
                }

                asyncReplacement.Verify(
                    factory => factory.CreateAsync(
                        It.IsAny<IServerInternal>(),
                        It.IsAny<ApplicationConfiguration>(),
                        It.IsAny<CancellationToken>()),
                    Times.Never);
                syncReplacement.Verify(
                    factory => factory.Create(
                        It.IsAny<IServerInternal>(),
                        It.IsAny<ApplicationConfiguration>()),
                    Times.Never);
                Assert.That(participant.DeleteAddressSpaceCount, Is.Zero);
                Assert.That(participant.DisposeCount, Is.Zero);
                Assert.That(FindReadinessRegistration(), Is.SameAs(observed));

                TrackingLifecycleNodeManager independentManager = null;
                NodeManagerRegistration independent = await lifecycle.AddAsync(
                    CreateTrackingNodeManagementFactory(
                        kSecondRegistrationValue,
                        manager => independentManager = manager,
                        kSecondModelNamespaceUri),
                    null,
                    timeout.Token).AsTask().WaitAsync(s_readinessTimeout).ConfigureAwait(false);
                Assert.That(independent.Id, Is.Not.EqualTo(observed.Id));
                await AssertReadinessValueAsync(kSecondModelNamespaceUri, kSecondRegistrationValue)
                    .ConfigureAwait(false);
                await RemoveReadinessRegistrationAsync(independent).ConfigureAwait(false);
                Assert.That(independentManager.DeleteAddressSpaceCount, Is.EqualTo(1));
                Assert.That(independentManager.DisposeCount, Is.EqualTo(1));
                await AssertReadinessNodeUnknownAsync(kSecondModelNamespaceUri).ConfigureAwait(false);
                await AssertReadinessValueAsync(kModelNamespaceUri, kGeneration2Value).ConfigureAwait(false);
                Assert.That(operation.IsCompleted, Is.False);
                Assert.That(participant.ReadinessCompletedCount, Is.Zero);
                Assert.That(participant.DeleteAddressSpaceCount, Is.Zero);
                Assert.That(participant.DisposeCount, Is.Zero);
                Assert.That(lifecycle.Registrations, Has.Count.EqualTo(1));
            }
            finally
            {
                release.TrySetResult(true);
                completed = await operation.WaitAsync(s_readinessTimeout).ConfigureAwait(false);
            }

            Assert.That(completed, Is.SameAs(observed));
            Assert.That(participant.ReadinessCount, Is.EqualTo(1));
            Assert.That(participant.ReadinessCompletedCount, Is.EqualTo(1));
            await RemoveReadinessRegistrationAsync(completed).ConfigureAwait(false);
            Assert.That(participant.DeleteAddressSpaceCount, Is.EqualTo(1));
            Assert.That(participant.DisposeCount, Is.EqualTo(1));
            Assert.That(lifecycle.Registrations, Is.Empty);
            await AssertReadinessNodeUnknownAsync(kModelNamespaceUri).ConfigureAwait(false);
        }

        /// <summary>
        /// Detached manager destruction may remove its real child through the lifecycle.
        /// Removal and retired-drain claims still prevent duplicate destruction while that
        /// callback is suspended, including a shadow generation with a real monitored item.
        /// </summary>
        [TestCase(ReadinessCleanup.Remove)]
        [TestCase(ReadinessCleanup.Reload)]
        [TestCase(ReadinessCleanup.ShadowDrain)]
        [Category("NodeManagerReadiness")]
        public async Task DetachedDeletionRemovesRealDependentRegistrationWithSingleCleanupAsync(
            ReadinessCleanup cleanup)
        {
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            using var timeout = new CancellationTokenSource(s_readinessTimeout);
            var deleteEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseDelete = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            ReadinessLifecycleNodeManager parentManager = null;
            TrackingLifecycleNodeManager childManager = null;
            TrackingLifecycleNodeManager replacementManager = null;
            NodeManagerRegistration child = null;
            NodeManagerRegistration original = await StartReadinessChangeAsync(
                ReadinessChange.Add,
                null,
                synchronousFactory: false,
                kGeneration1Value,
                manager =>
                {
                    parentManager = manager;
                    manager.ReadinessCallback = async ct =>
                    {
                        child = await lifecycle.AddAsync(
                            CreateTrackingNodeManagementFactory(
                                kSecondRegistrationValue,
                                created => childManager = created,
                                kSecondModelNamespaceUri,
                                ReadinessNodeId(kModelNamespaceUri, kRootNodeId)),
                            null,
                            ct).AsTask().WaitAsync(s_readinessTimeout, ct).ConfigureAwait(false);
                    };
                    manager.DeleteCallback = async ct =>
                    {
                        // If an earlier assertion fails, the existing ordered fixture
                        // shutdown owns all remaining registrations, including the child.
                        if (lifecycle.IsShuttingDown)
                        {
                            return;
                        }
                        deleteEntered.TrySetResult(true);
                        await releaseDelete.Task.WaitAsync(s_readinessTimeout, ct).ConfigureAwait(false);
                        using var nestedTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        nestedTimeout.CancelAfter(s_readinessTimeout);
                        await lifecycle.RemoveAsync(child, null, nestedTimeout.Token)
                            .AsTask().WaitAsync(s_readinessTimeout, ct).ConfigureAwait(false);
                    };
                },
                timeout.Token).WaitAsync(s_readinessTimeout).ConfigureAwait(false);

            var services = new ServerTestServices(m_server, m_secureChannelContext);
            uint subscriptionId = 0;
            bool shadowRetired = false;
            Task cleanupTask = null;
            Task<NodeManagerRegistration> reloadTask = null;
            NodeManagerRegistration replacement = null;
            try
            {
                if (cleanup == ReadinessCleanup.ShadowDrain)
                {
                    uint monitoredItemId;
                    (subscriptionId, monitoredItemId) = await CreateSubscriptionWithMonitoredItemAsync(
                        services,
                        ReadinessNodeId(kModelNamespaceUri, kValueNodeId))
                        .WaitAsync(s_readinessTimeout).ConfigureAwait(false);
                    replacement = await lifecycle.ShadowReloadAsync(
                        original,
                        CreateTrackingNodeManagementFactory(
                            kGeneration2Value,
                            manager => replacementManager = manager),
                        timeout.Token).AsTask().WaitAsync(s_readinessTimeout).ConfigureAwait(false);
                    shadowRetired = true;
                    Assert.That(parentManager.DeleteAddressSpaceCount, Is.Zero);
                    Assert.That(parentManager.DisposeCount, Is.Zero);
                    Assert.That(
                        ((BaseVariableState)parentManager.Find(
                            ReadinessNodeId(kModelNamespaceUri, kValueNodeId))).Value,
                        Is.EqualTo(kGeneration1Value));
                    await AssertReadinessValueAsync(kModelNamespaceUri, kGeneration2Value).ConfigureAwait(false);
                    cleanupTask = parentManager.DisposalCompleted;
                    await DeleteMonitoredItemAsync(services, subscriptionId, monitoredItemId)
                        .WaitAsync(s_readinessTimeout).ConfigureAwait(false);
                }
                else if (cleanup == ReadinessCleanup.Reload)
                {
                    reloadTask = RunWithoutExecutionContext(() => lifecycle.ReloadAsync(
                        original,
                        CreateTrackingNodeManagementFactory(
                            kGeneration2Value,
                            manager => replacementManager = manager),
                        null,
                        timeout.Token).AsTask());
                    cleanupTask = reloadTask;
                }
                else
                {
                    cleanupTask = RunWithoutExecutionContext(
                        () => lifecycle.RemoveAsync(original, null, timeout.Token).AsTask());
                }

                await deleteEntered.Task.WaitAsync(s_readinessTimeout).ConfigureAwait(false);
                Assert.That(cleanupTask.IsCompleted, Is.False);
                Assert.That(parentManager.DeleteAddressSpaceCount, Is.EqualTo(1));
                Assert.That(parentManager.DisposeCount, Is.Zero);
                Assert.That(childManager.DeleteAddressSpaceCount, Is.Zero);
                Assert.That(childManager.DisposeCount, Is.Zero);
                Assert.That(lifecycle.Registrations.Find(candidate => candidate.Id == child.Id), Is.SameAs(child));
                await AssertReadinessValueAsync(kSecondModelNamespaceUri, kSecondRegistrationValue)
                    .ConfigureAwait(false);

                InvalidOperationException competingRemove =
                    await ExpectReadinessExceptionAsync<InvalidOperationException>(
                        lifecycle.RemoveAsync(original, null, timeout.Token).AsTask()).ConfigureAwait(false);
                Assert.That(competingRemove.Message, Does.Contain("stale").Or.Contain("not owned"));

                // These real operations also give retired cleanup another opportunity
                // to run while the first destruction callback owns its drain claim.
                TrackingLifecycleNodeManager probeManager = null;
                NodeManagerRegistration probe = await lifecycle.AddAsync(
                    CreateTrackingNodeManagementFactory(
                        kFirstRegistrationValue,
                        manager => probeManager = manager,
                        kReadinessProbeNamespaceUri),
                    null,
                    timeout.Token).AsTask().WaitAsync(s_readinessTimeout).ConfigureAwait(false);
                await AssertReadinessValueAsync(kReadinessProbeNamespaceUri, kFirstRegistrationValue)
                    .ConfigureAwait(false);
                await RemoveReadinessRegistrationAsync(probe).ConfigureAwait(false);
                Assert.That(probeManager.DeleteAddressSpaceCount, Is.EqualTo(1));
                Assert.That(probeManager.DisposeCount, Is.EqualTo(1));
                Assert.That(parentManager.DeleteAddressSpaceCount, Is.EqualTo(1));
                Assert.That(parentManager.DisposeCount, Is.Zero);
                Assert.That(childManager.DeleteAddressSpaceCount, Is.Zero);
                Assert.That(cleanupTask.IsCompleted, Is.False);
                if (cleanup == ReadinessCleanup.Remove)
                {
                    await AssertReadinessNodeUnknownAsync(kModelNamespaceUri).ConfigureAwait(false);
                }
                else
                {
                    Assert.That(FindReadinessRegistration().Generation, Is.EqualTo(2));
                    await AssertReadinessValueAsync(kModelNamespaceUri, kGeneration2Value).ConfigureAwait(false);
                    Assert.That(replacementManager.DeleteAddressSpaceCount, Is.Zero);
                    Assert.That(replacementManager.DisposeCount, Is.Zero);
                }
            }
            finally
            {
                releaseDelete.TrySetResult(true);
                try
                {
                    if (cleanupTask is not null)
                    {
                        await cleanupTask.WaitAsync(s_readinessTimeout).ConfigureAwait(false);
                    }
                }
                finally
                {
                    if (subscriptionId != 0)
                    {
                        await DeleteSubscriptionAsync(services, subscriptionId)
                            .WaitAsync(s_readinessTimeout).ConfigureAwait(false);
                    }
                    if (shadowRetired)
                    {
                        await parentManager.DisposalCompleted.WaitAsync(s_readinessTimeout).ConfigureAwait(false);
                    }
                }
            }

            if (reloadTask is not null)
            {
                replacement = await reloadTask.WaitAsync(s_readinessTimeout).ConfigureAwait(false);
            }
            Assert.That(parentManager.ReadinessCount, Is.EqualTo(1));
            Assert.That(parentManager.ReadinessCompletedCount, Is.EqualTo(1));
            Assert.That(parentManager.DeleteAddressSpaceCount, Is.EqualTo(1));
            Assert.That(parentManager.DisposeCount, Is.EqualTo(1));
            Assert.That(parentManager.Find(ReadinessNodeId(kModelNamespaceUri, kValueNodeId)), Is.Null);
            Assert.That(childManager.DeleteAddressSpaceCount, Is.EqualTo(1));
            Assert.That(childManager.DisposeCount, Is.EqualTo(1));
            Assert.That(lifecycle.Registrations.Find(candidate => candidate.Id == child.Id), Is.Null);
            await AssertReadinessNodeUnknownAsync(kSecondModelNamespaceUri).ConfigureAwait(false);
            await AssertReadinessNodeUnknownAsync(kReadinessProbeNamespaceUri).ConfigureAwait(false);
            if (replacement is not null)
            {
                Assert.That(FindReadinessRegistration(), Is.SameAs(replacement));
                Assert.That(replacement.Id, Is.EqualTo(original.Id));
                Assert.That(replacement.Generation, Is.EqualTo(2));
                await AssertReadinessValueAsync(kModelNamespaceUri, kGeneration2Value).ConfigureAwait(false);
                await RemoveReadinessRegistrationAsync(replacement).ConfigureAwait(false);
                Assert.That(replacementManager.DeleteAddressSpaceCount, Is.EqualTo(1));
                Assert.That(replacementManager.DisposeCount, Is.EqualTo(1));
            }
            Assert.That(lifecycle.Registrations, Is.Empty);
            await AssertReadinessNodeUnknownAsync(kModelNamespaceUri).ConfigureAwait(false);
        }

        private Task<NodeManagerRegistration> StartReadinessChangeAsync(
            ReadinessChange change,
            NodeManagerRegistration original,
            bool synchronousFactory,
            int value,
            Action<ReadinessLifecycleNodeManager> configure,
            CancellationToken cancellationToken)
        {
            ReadinessLifecycleNodeManager CreateManager(
                IServerInternal server,
                ApplicationConfiguration configuration)
            {
                var manager = new ReadinessLifecycleNodeManager(
                    server,
                    configuration,
                    m_logger,
                    value);
                configure(manager);
                return manager;
            }

            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            if (synchronousFactory)
            {
                var factory = new Mock<INodeManagerFactory>();
                factory.Setup(candidate => candidate.Create(
                    It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>()))
                    .Returns((IServerInternal server, ApplicationConfiguration configuration) =>
                        new SyncNodeManagerAdapter(CreateManager(server, configuration)));
                return RunWithoutExecutionContext(() => InvokeReadinessChangeAsync(
                    lifecycle, change, original, factory.Object, cancellationToken).AsTask());
            }
            else
            {
                var factory = new Mock<IAsyncNodeManagerFactory>();
                factory.Setup(candidate => candidate.CreateAsync(
                    It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>()))
                    .Returns((
                        IServerInternal server,
                        ApplicationConfiguration configuration,
                        CancellationToken _) => new ValueTask<IAsyncNodeManager>(
                            CreateManager(server, configuration)));
                return RunWithoutExecutionContext(() => InvokeReadinessChangeAsync(
                    lifecycle, change, original, factory.Object, cancellationToken).AsTask());
            }
        }

        private static ValueTask<NodeManagerRegistration> InvokeReadinessChangeAsync(
            INodeManagerLifecycle lifecycle,
            ReadinessChange change,
            NodeManagerRegistration original,
            IAsyncNodeManagerFactory factory,
            CancellationToken cancellationToken)
        {
            return change switch
            {
                ReadinessChange.Add => lifecycle.AddAsync(factory, null, cancellationToken),
                ReadinessChange.Reload => lifecycle.ReloadAsync(original, factory, null, cancellationToken),
                ReadinessChange.ShadowReload => lifecycle.ShadowReloadAsync(original, factory, cancellationToken),
                ReadinessChange.ImmediateReload => lifecycle.ImmediateReloadAsync(original, factory, cancellationToken),
                _ => throw new ArgumentOutOfRangeException(nameof(change))
            };
        }

        private static ValueTask<NodeManagerRegistration> InvokeReadinessChangeAsync(
            INodeManagerLifecycle lifecycle,
            ReadinessChange change,
            NodeManagerRegistration original,
            INodeManagerFactory factory,
            CancellationToken cancellationToken)
        {
            return change switch
            {
                ReadinessChange.Add => lifecycle.AddAsync(factory, null, cancellationToken),
                ReadinessChange.Reload => lifecycle.ReloadAsync(original, factory, null, cancellationToken),
                ReadinessChange.ShadowReload => lifecycle.ShadowReloadAsync(original, factory, cancellationToken),
                ReadinessChange.ImmediateReload => lifecycle.ImmediateReloadAsync(original, factory, cancellationToken),
                _ => throw new ArgumentOutOfRangeException(nameof(change))
            };
        }

        private NodeManagerRegistration FindReadinessRegistration()
        {
            NodeManagerRegistration registration = m_server.NodeManagerLifecycle.Registrations.Find(
                candidate => candidate.NamespaceUris.Contains(kModelNamespaceUri));
            Assert.That(registration, Is.Not.Null, "Readiness requires an already committed registration.");
            return registration;
        }

        private NodeId ReadinessNodeId(string namespaceUri, uint identifier)
        {
            int namespaceIndex = m_server.CurrentInstance.NamespaceUris.GetIndex(namespaceUri);
            Assert.That(namespaceIndex, Is.GreaterThanOrEqualTo(0));
            return new NodeId(identifier, (ushort)namespaceIndex);
        }

        private async Task AssertReadinessValueAsync(string namespaceUri, int expected)
        {
            DataValue value = await ReadValueAsync(ReadinessNodeId(namespaceUri, kValueNodeId))
                .WaitAsync(s_readinessTimeout).ConfigureAwait(false);
            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(value.WrappedValue.GetInt32(), Is.EqualTo(expected));
        }

        private async Task AssertReadinessNodeUnknownAsync(string namespaceUri)
        {
            DataValue value = await ReadValueAsync(ReadinessNodeId(namespaceUri, kValueNodeId))
                .WaitAsync(s_readinessTimeout).ConfigureAwait(false);
            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
        }

        private static void AssertReadinessManager(
            NodeManagerRegistration registration,
            ReadinessLifecycleNodeManager participant,
            bool synchronousFactory)
        {
            if (synchronousFactory)
            {
                Assert.That(registration.NodeManager, Is.TypeOf<AsyncNodeManagerAdapter>());
                Assert.That(registration.NodeManager.SyncNodeManager, Is.TypeOf<SyncNodeManagerAdapter>());
            }
            else
            {
                Assert.That(registration.NodeManager, Is.SameAs(participant));
            }
        }

        private static async Task<TException> ExpectReadinessExceptionAsync<TException>(Task operation)
            where TException : Exception
        {
            TException failure = null;
            try
            {
                await operation.WaitAsync(s_readinessTimeout).ConfigureAwait(false);
            }
            catch (TException exception)
            {
                failure = exception;
            }
            Assert.That(failure, Is.TypeOf<TException>());
            return failure;
        }

        private async Task RemoveReadinessRegistrationAsync(
            NodeManagerRegistration registration,
            ReadinessLifecycleNodeManager participant = null,
            bool synchronousFactory = false)
        {
            using var timeout = new CancellationTokenSource(s_readinessTimeout);
            await m_server.NodeManagerLifecycle.RemoveAsync(registration, null, timeout.Token)
                .AsTask().WaitAsync(s_readinessTimeout).ConfigureAwait(false);
            if (synchronousFactory)
            {
                // The legacy sync facade does not own disposal of its underlying async
                // manager. Release that test-owned resource only after real removal.
                participant.Dispose();
            }
        }

        private async Task AssertReadinessCleanupAndRecoveryAsync(
            NodeManagerRegistration registration,
            ReadinessLifecycleNodeManager participant)
        {
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            Assert.That(lifecycle.Registrations, Has.Count.EqualTo(1));
            await RemoveReadinessRegistrationAsync(registration).ConfigureAwait(false);
            Assert.That(lifecycle.Registrations, Is.Empty);
            Assert.That(participant.DeleteAddressSpaceCount, Is.EqualTo(1));
            Assert.That(participant.DisposeCount, Is.EqualTo(1));
            await AssertReadinessNodeUnknownAsync(kModelNamespaceUri).ConfigureAwait(false);

            using var timeout = new CancellationTokenSource(s_readinessTimeout);
            TrackingLifecycleNodeManager laterManager = null;
            NodeManagerRegistration later = await lifecycle.AddAsync(
                CreateTrackingNodeManagementFactory(
                    kFirstRegistrationValue,
                    manager => laterManager = manager),
                null,
                timeout.Token).AsTask().WaitAsync(s_readinessTimeout).ConfigureAwait(false);
            Assert.That(later.Id, Is.Not.EqualTo(registration.Id));
            Assert.That(later.Generation, Is.EqualTo(1));
            Assert.That(later.NodeManager, Is.SameAs(laterManager));
            await AssertReadinessValueAsync(kModelNamespaceUri, kFirstRegistrationValue).ConfigureAwait(false);
            await RemoveReadinessRegistrationAsync(later).ConfigureAwait(false);
            Assert.That(laterManager.DeleteAddressSpaceCount, Is.EqualTo(1));
            Assert.That(laterManager.DisposeCount, Is.EqualTo(1));
            Assert.That(participant.ReadinessCount, Is.EqualTo(1));
            Assert.That(lifecycle.Registrations, Is.Empty);
            await AssertReadinessNodeUnknownAsync(kModelNamespaceUri).ConfigureAwait(false);
        }

        private sealed class ReadinessLifecycleNodeManager :
            NodeManagementLifecycleNodeManager,
            INodeManagerReadinessParticipant
        {
            public ReadinessLifecycleNodeManager(
                IServerInternal server,
                ApplicationConfiguration configuration,
                ILogger logger,
                int value)
                : base(server, configuration, logger, kModelNamespaceUri, value)
            {
            }

            public Func<CancellationToken, ValueTask> ReadinessCallback { get; set; }

            public Func<CancellationToken, ValueTask> DeleteCallback { get; set; }

            public Exception PreparationFailure { get; set; }

            public int ReadinessCount => Volatile.Read(ref m_readinessCount);

            public int ReadinessCompletedCount => Volatile.Read(ref m_readinessCompletedCount);

            public int DeleteAddressSpaceCount => Volatile.Read(ref m_deleteAddressSpaceCount);

            public int DisposeCount => Volatile.Read(ref m_disposeCount);

            public Task DisposalCompleted => m_disposed.Task;

            public async ValueTask OnServerReadyAsync(CancellationToken cancellationToken = default)
            {
                Interlocked.Increment(ref m_readinessCount);
                if (ReadinessCallback is not null)
                {
                    await ReadinessCallback(cancellationToken).ConfigureAwait(false);
                }
                Interlocked.Increment(ref m_readinessCompletedCount);
            }

            public override async ValueTask CreateAddressSpaceAsync(
                IDictionary<NodeId, IList<IReference>> externalReferences,
                CancellationToken cancellationToken = default)
            {
                await base.CreateAddressSpaceAsync(externalReferences, cancellationToken).ConfigureAwait(false);
                if (PreparationFailure is not null)
                {
                    throw PreparationFailure;
                }
            }

            public override async ValueTask DeleteAddressSpaceAsync(CancellationToken cancellationToken = default)
            {
                Interlocked.Increment(ref m_deleteAddressSpaceCount);
                if (DeleteCallback is not null)
                {
                    await DeleteCallback(cancellationToken).ConfigureAwait(false);
                }
                await base.DeleteAddressSpaceAsync(cancellationToken).ConfigureAwait(false);
            }

            protected override void Dispose(bool disposing)
            {
                base.Dispose(disposing);
                if (disposing)
                {
                    Interlocked.Increment(ref m_disposeCount);
                    m_disposed.TrySetResult(true);
                }
            }

            private readonly TaskCompletionSource<bool> m_disposed =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            private int m_readinessCount;
            private int m_readinessCompletedCount;
            private int m_deleteAddressSpaceCount;
            private int m_disposeCount;
        }

        /// <summary>
        /// Public lifecycle entry points that own a newly ready generation.
        /// </summary>
        public enum ReadinessChange
        {
            Add,
            Reload,
            ShadowReload,
            ImmediateReload
        }

        /// <summary>
        /// Distinct removal and retired-generation destruction paths.
        /// </summary>
        public enum ReadinessCleanup
        {
            Remove,
            Reload,
            ShadowDrain
        }
    }
}
