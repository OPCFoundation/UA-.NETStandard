// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.Server.Tests.NodeManager
{
    public partial class NodeManagerLifecycleTests
    {
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        public async Task HandoffWorkerOwnsDrainAndPublishesOnlyAfterSuccessfulDrainAsync(bool cancel, bool hosted)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var request = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
            var lifecycle = (NodeManagerLifecycle)m_server.NodeManagerLifecycle;
            LifecycleResult added = await lifecycle.ApplyAsync(
                [LifecycleChange.Add(CreateTrackingNodeManagementFactory(kGeneration1Value, _ => { }))],
                timeout.Token).ConfigureAwait(false);
            NodeManagerRegistration original = RequireRegistration(added.Registrations[0]);
            var pause = new TestHandoffPause();
            var factory = new TestHandoffFactory(
                CreateTrackingNodeManagementFactory(kGeneration2Value, _ => { }), pause);

            await Assert.ThrowsAsync<NotSupportedException>(async () =>
                await lifecycle.ApplyAsync([LifecycleChange.Replace(original.Slot, factory)], timeout.Token)
                    .ConfigureAwait(false)).ConfigureAwait(false);
            Assert.That(factory.Predecessor, Is.Null);
            Assert.That(pause.Entered.Task.IsCompleted, Is.False);

            var forwarder = new HostedNodeManagerLifecycle(new NodeManagerCleanupAuthority(
                m_fixtureCleanupAuthorization, m_fixtureCleanupVerifier, m_fixtureCleanupAudit));
            if (hosted)
            {
                Assert.That(forwarder.QueueHandoff(original, factory, request.Token),
                    Is.EqualTo(StatusCodes.BadShutdown));
                forwarder.Attach(lifecycle);
            }
            StatusCode accepted = hosted
                ? forwarder.QueueHandoff(original, factory, request.Token)
                : lifecycle.QueueHandoff(original, factory, request.Token);
            Assert.That(accepted, Is.EqualTo(StatusCodes.Good));
            try
            {
                await pause.Entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(factory.Predecessor, Is.SameAs(original.NodeManager));
                Assert.That(lifecycle.Revision, Is.EqualTo(added.Revision));
                Assert.That(lifecycle.QueueHandoff(original, factory), Is.EqualTo(StatusCodes.BadWouldBlock));
                if (cancel)
                {
                    request.Cancel();
                }
                else
                {
                    pause.AllowDrain.TrySetResult(true);
                }
                bool reopened = await pause.Finished.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(reopened, Is.EqualTo(cancel));
                Assert.That(original.Slot.TryGetCurrent(out NodeManagerSlotSnapshot current), Is.True);
                Assert.That(current.Generation, Is.EqualTo(original.Generation + (cancel ? 0 : 1)));
            }
            finally
            {
                request.Cancel();
                pause.AllowDrain.TrySetResult(true);
                if (hosted)
                {
                    forwarder.Detach(lifecycle);
                }
            }
        }

        /// <summary>
        /// A publication on another manager while the predecessor drains does not make the successor stale: the
        /// handoff is rebased and commits.
        /// </summary>
        [Test]
        public async Task HandoffWorkerAllowsUnrelatedPublicationAndCommitsSuccessorAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var lifecycle = (NodeManagerLifecycle)m_server.NodeManagerLifecycle;
            LifecycleResult added = await lifecycle.ApplyAsync(
                [LifecycleChange.Add(CreateTrackingNodeManagementFactory(kGeneration1Value, _ => { }))],
                timeout.Token).ConfigureAwait(false);
            NodeManagerRegistration predecessor = RequireRegistration(added.Registrations[0]);
            var pause = new TestHandoffPause();
            var factory = new TestHandoffFactory(
                CreateTrackingNodeManagementFactory(kGeneration2Value, _ => { }), pause);

            Assert.That(lifecycle.QueueHandoff(predecessor, factory, timeout.Token), Is.EqualTo(StatusCodes.Good));
            try
            {
                await pause.Entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                LifecycleResult unrelated = await lifecycle.ApplyAsync(
                    [LifecycleChange.Add(CreateTrackingNodeManagementFactory(
                        kSecondRegistrationValue, _ => { }, kSecondModelNamespaceUri))],
                    timeout.Token).AsTask().WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(unrelated.Revision, Is.GreaterThan(added.Revision));
                Assert.That(RequireRegistration(unrelated.Registrations[0]).Slot,
                    Is.Not.SameAs(predecessor.Slot));
                Assert.That(pause.Finished.Task.IsCompleted, Is.False);

                pause.AllowDrain.TrySetResult(true);
                Assert.That(await pause.Finished.Task.WaitAsync(timeout.Token).ConfigureAwait(false), Is.False);
                Assert.That(lifecycle.Revision, Is.EqualTo(unrelated.Revision + 1));
                Assert.That(predecessor.Slot.TryGetCurrent(out NodeManagerSlotSnapshot current), Is.True);
                Assert.That(current.NodeManager, Is.Not.SameAs(predecessor.NodeManager));
                Assert.That(current.Generation, Is.EqualTo(predecessor.Generation + 1));
            }
            finally
            {
                timeout.Cancel();
                pause.AllowDrain.TrySetResult(true);
            }
        }

        [Test]
        public async Task HandoffWorkerCancelsDrainWithoutReopeningWhenShutdownStartsAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var lifecycle = (NodeManagerLifecycle)m_server.NodeManagerLifecycle;
            LifecycleResult added = await lifecycle.ApplyAsync(
                [LifecycleChange.Add(CreateTrackingNodeManagementFactory(kGeneration1Value, _ => { }))],
                timeout.Token).ConfigureAwait(false);
            NodeManagerRegistration predecessor = RequireRegistration(added.Registrations[0]);
            TrackingLifecycleNodeManager? candidate = null;
            var pause = new TestHandoffPause();
            var factory = new TestHandoffFactory(
                CreateTrackingNodeManagementFactory(kGeneration2Value, manager => candidate = manager), pause);

            Assert.That(lifecycle.QueueHandoff(predecessor, factory, timeout.Token), Is.EqualTo(StatusCodes.Good));
            try
            {
                await pause.Entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                await lifecycle.BeginShutdownAsync(m_server.CurrentInstance, timeout.Token).ConfigureAwait(false);
                Assert.That(await pause.Finished.Task.WaitAsync(timeout.Token).ConfigureAwait(false), Is.False);
                Assert.That(pause.AllowDrain.Task.IsCompleted, Is.False);
                Assert.That(lifecycle.Revision, Is.EqualTo(added.Revision));
                AssertLifecycleValue(candidate);
                Assert.That(candidate.DisposeCount, Is.EqualTo(1));
            }
            finally
            {
                pause.AllowDrain.TrySetResult(true);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task HandoffWorkerDoesNotReopenAfterCorePublicationStartsAsync(bool started)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var lifecycle = (NodeManagerLifecycle)m_server.NodeManagerLifecycle;
            LifecycleResult added = await lifecycle.ApplyAsync(
                [LifecycleChange.Add(CreateTrackingNodeManagementFactory(kGeneration1Value, _ => { }))],
                timeout.Token).ConfigureAwait(false);
            NodeManagerRegistration original = RequireRegistration(added.Registrations[0]);
            var pause = new TestHandoffPause();
            pause.AllowDrain.TrySetResult(true);
            var factory = new TestHandoffFactory(
                CreateTrackingNodeManagementFactory(kGeneration2Value, _ => { }), pause);
            m_server.HostBoundary.Publication = async (plan, _) =>
            {
                await plan.Commit.DecideAsync(null, timeout.Token).ConfigureAwait(false);
                if (started)
                {
                    plan.Progress!.BeginCorePublication();
                }
                throw new InvalidOperationException("Injected publication failure.");
            };

            try
            {
                Assert.That(lifecycle.QueueHandoff(original, factory, timeout.Token), Is.EqualTo(StatusCodes.Good));
                Assert.That(await pause.Finished.Task.WaitAsync(timeout.Token).ConfigureAwait(false),
                    Is.EqualTo(!started));
                Assert.That(lifecycle.Revision, Is.EqualTo(added.Revision));
                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await lifecycle.ApplyAsync([LifecycleChange.Remove(original.Slot)], timeout.Token)
                        .ConfigureAwait(false)).ConfigureAwait(false);
            }
            finally
            {
                m_server.HostBoundary.Publication = null;
            }
        }

        [Test]
        public async Task HandoffWorkerKeepsPublishedSuccessorAfterPostSwapFailureAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var lifecycle = (NodeManagerLifecycle)m_server.NodeManagerLifecycle;
            var host = (IDynamicNodeManagerHost)m_server.CurrentInstance.NodeManager;
            LifecycleResult added = await lifecycle.ApplyAsync(
                [LifecycleChange.Add(CreateTrackingNodeManagementFactory(kGeneration1Value, _ => { }))],
                timeout.Token).ConfigureAwait(false);
            NodeManagerRegistration predecessor = RequireRegistration(added.Registrations[0]);
            TrackingLifecycleNodeManager? candidate = null;
            var pause = new TestHandoffPause();
            pause.AllowDrain.TrySetResult(true);
            var factory = new TestHandoffFactory(
                CreateTrackingNodeManagementFactory(kGeneration2Value, manager => candidate = manager), pause);
            m_server.HostBoundary.Publication = CreatePostSwapPublicationFailure(
                () => true, notifyBeforeFailure: false, new InvalidOperationException("Injected post-swap failure."));

            try
            {
                Assert.That(lifecycle.QueueHandoff(predecessor, factory, timeout.Token), Is.EqualTo(StatusCodes.Good));
                Assert.That(await pause.Finished.Task.WaitAsync(timeout.Token).ConfigureAwait(false), Is.False);
                AssertLifecycleValue(candidate);
                Assert.That(predecessor.Slot.TryGetCurrent(out NodeManagerSlotSnapshot current), Is.True);
                Assert.That(current.NodeManager, Is.SameAs(candidate));
                Assert.That(current.Generation, Is.EqualTo(predecessor.Generation + 1));
                Assert.That(host.RoutingRevision.TryGetGeneration(candidate, out NodeManagerGeneration? generation),
                    Is.True);
                Assert.That(generation!.Number, Is.EqualTo(current.Generation));
                Assert.That(NodeManagerLifecycle.IsSelected(host, generation), Is.True);
                Assert.That(NodeManagerLifecycle.IsSelected(host, predecessor.Identity), Is.False);
            }
            finally
            {
                m_server.HostBoundary.Publication = null;
            }

            LifecycleResult removed = await lifecycle.ApplyAsync(
                [LifecycleChange.Remove(predecessor.Slot)], timeout.Token).ConfigureAwait(false);
            await removed.Retired.WaitAsync(timeout.Token).ConfigureAwait(false);
            Assert.That(removed.CleanupFailure, Is.Null);
            Assert.That(candidate.DisposeCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Supplies an inert factory whose pause can be controlled by the test.
        /// </summary>
        private sealed class TestHandoffFactory(IAsyncNodeManagerFactory inner, TestHandoffPause pause)
            : INodeManagerHandoffFactory
        {
            public IAsyncNodeManager? Predecessor { get; private set; }

            public ArrayOf<string> NamespacesUris => inner.NamespacesUris;

            public ValueTask<IAsyncNodeManager> CreateAsync(
                IServerInternal server, ApplicationConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                throw new AssertionException("The ordinary factory must not be used for a handoff.");
            }

            public async ValueTask<NodeManagerHandoffCandidate> PrepareReplacementAsync(
                IServerInternal stagingServer, ApplicationConfiguration configuration,
                IAsyncNodeManager predecessor, CancellationToken cancellationToken)
            {
                Predecessor = predecessor;
                IAsyncNodeManager successor = await inner.CreateAsync(stagingServer, configuration, cancellationToken)
                    .ConfigureAwait(false);
                return new NodeManagerHandoffCandidate(successor, () => pause);
            }
        }

        /// <summary>
        /// Holds the worker before commit without holding any server admission.
        /// </summary>
        private sealed class TestHandoffPause : INodeManagerHandoffPause
        {
            public TaskCompletionSource<bool> Entered { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public TaskCompletionSource<bool> AllowDrain { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public TaskCompletionSource<bool> Finished { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public async ValueTask DrainAsync(CancellationToken cancellationToken)
            {
                Entered.TrySetResult(true);
                await AllowDrain.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            public void Finish(bool reopen)
            {
                Finished.TrySetResult(reopen);
            }
        }
    }
}
