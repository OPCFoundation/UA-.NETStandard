// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using static Opc.Ua.Server.Tests.NodeManager.LifecycleInspection;

namespace Opc.Ua.Server.Tests.NodeManager
{
    public sealed partial class NodeManagerLifecycleTests
    {
        /// <summary>
        /// Stale captures do not exclude fresh invocations or ordinary lifecycle publication.
        /// </summary>
        [Test]
        public async Task WriterAdmitsConcurrentInvocationsAndRejectsStaleCaptureBeforeFactoryEffectsAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            ILifecycleCapture stale = lifecycle.CaptureChanges();
            RequireRegistration(
                (await CommitChangeAsync(
                lifecycle, LifecycleChange.Add(
                CreateTrackingNodeManagementFactory(
                kGeneration1Value, _ => { })), timeout.Token).ConfigureAwait(false)).Registrations[0]);
            var factory = new CountingNodeManagerFactory(
                [kSecondModelNamespaceUri],
                CreateTrackingNodeManagementFactory(
                    kSecondRegistrationValue, _ => { }, kSecondModelNamespaceUri).CreateAsync);
            long revision = lifecycle.Revision;
            await using (ILifecycleWriter invocation = await stale.BeginAsync(timeout.Token)
                .ConfigureAwait(false))
            {
                Assert.That(invocation.IsCurrent, Is.False);
                await Assert.ThrowsAsync<NodeManagerLifecycleConflictException>(async () =>
                    await invocation.PrepareAsync([LifecycleChange.Add(factory)], timeout.Token).ConfigureAwait(false))
                    .ConfigureAwait(false);
                await using ILifecycleWriter peer = await lifecycle.CaptureChanges()
                    .BeginAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(peer.IsCurrent, Is.True);
                LifecycleResult publicationResult1 = await CommitChangeAsync(
                    lifecycle, LifecycleChange.Add(factory), timeout.Token).ConfigureAwait(false);
                NodeManagerRegistration added = RequireRegistration(publicationResult1.Registrations[0]);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(factory.CreateCount, Is.EqualTo(1));
                    Assert.That(added.Generation, Is.EqualTo(1));
                    Assert.That(lifecycle.Revision, Is.GreaterThan(revision));
                    Assert.That(peer.IsCurrent, Is.False);
                }
            }
        }

        /// <summary>
        /// Two prepared invocations can coexist; the loser conflicts before its durable decision without factory retry.
        /// </summary>
        [Test]
        public async Task CapturedInvocationsRejectStaleCommitBeforeDecisionWithoutFactoryRetryAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            ILifecycleCapture capture = lifecycle.CaptureChanges();
            await using ILifecycleWriter first = await capture.BeginAsync(timeout.Token).ConfigureAwait(false);
            await using ILifecycleWriter second = await capture.BeginAsync(timeout.Token).ConfigureAwait(false);
            TrackingLifecycleNodeManager? candidate = null;
            var factory = new CountingNodeManagerFactory(
                [kSecondModelNamespaceUri],
                CreateTrackingNodeManagementFactory(
                    kSecondRegistrationValue, manager => candidate = manager, kSecondModelNamespaceUri).CreateAsync);
            await using IPreparedChanges firstBatch = await first.PrepareAsync(
                [LifecycleChange.Add(CreateTrackingNodeManagementFactory(
                    kGeneration1Value, _ => { }))], timeout.Token).ConfigureAwait(false);
            await using IPreparedChanges secondBatch = await second.PrepareAsync(
                [LifecycleChange.Add(factory)], timeout.Token).ConfigureAwait(false);
            LifecycleResult committed = await firstBatch.CommitAsync(_ => default, timeout.Token)
                .ConfigureAwait(false);
            int decisions = 0;
            await Assert.ThatAsync(() => secondBatch.CommitAsync(_ =>
            {
                decisions++;
                return default;
            }, timeout.Token).AsTask(), Throws.TypeOf<NodeManagerRoutingConflictException>()).ConfigureAwait(false);
            await second.DisposeAsync().ConfigureAwait(false);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(decisions, Is.Zero);
                Assert.That(factory.CreateCount, Is.EqualTo(1));
                Assert.That(secondBatch.IsCommitted, Is.False);
                Assert.That(first.IsCurrent, Is.True);
                Assert.That(second.IsCurrent, Is.False);
                Assert.That(lifecycle.Revision, Is.EqualTo(committed.Revision));
                Assert.That(RequireLifecycleValue(candidate).DisposeCount, Is.EqualTo(1));
            }
        }

        /// <summary>
        /// Sequential own publications remain current until an unrelated publication changes the serving view.
        /// </summary>
        [Test]
        public async Task CapturedInvocationObservesOwnSequentialPublicationsButNotUnrelatedPublicationAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            await using ILifecycleWriter invocation = await lifecycle.CaptureChanges()
                .BeginAsync(timeout.Token).ConfigureAwait(false);
            await using IPreparedChanges first = await invocation.PrepareAsync(
                [LifecycleChange.Add(CreateTrackingNodeManagementFactory(
                    kGeneration1Value, _ => { }))], timeout.Token).ConfigureAwait(false);
            LifecycleResult added = await first.CommitAsync(_ => default, timeout.Token).ConfigureAwait(false);
            Assert.That(invocation.IsCurrent, Is.True);
            await using IPreparedChanges second = await invocation.PrepareAsync(
                [LifecycleChange.Replace(added.Registrations[0].Slot, CreateTrackingNodeManagementFactory(
                    kGeneration2Value, _ => { }))], timeout.Token).ConfigureAwait(false);
            LifecycleResult replaced = await second.CommitAsync(_ => default, timeout.Token)
                .ConfigureAwait(false);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(invocation.IsCurrent, Is.True);
                Assert.That(replaced.Revision, Is.GreaterThan(added.Revision));
                Assert.That(replaced.Registrations[0].Generation, Is.EqualTo(2));
            }
            RequireRegistration(
                (await CommitChangeAsync(lifecycle, LifecycleChange.Add(CreateTrackingNodeManagementFactory(
                kSecondRegistrationValue, _ => { }, kSecondModelNamespaceUri)), timeout.Token).ConfigureAwait(
                    false)).Registrations[0]);
            var rejectedFactory = new CountingNodeManagerFactory(
                [kModelNamespaceUri], CreateTrackingNodeManagementFactory(kGeneration1Value, _ => { }).CreateAsync);
            await Assert.ThatAsync(
                () => invocation.PrepareAsync(
                [LifecycleChange.Replace(
                replaced.Registrations[0].Slot, rejectedFactory)], timeout.Token).AsTask(),
                Throws.TypeOf<NodeManagerLifecycleConflictException>()).ConfigureAwait(false);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(invocation.IsCurrent, Is.False);
                Assert.That(rejectedFactory.CreateCount, Is.Zero);
            }
        }

        /// <summary>
        /// Publication factories can await nested lifecycle work; its revision is not silently incorporated.
        /// </summary>
        [Test]
        public async Task CapturedInvocationFactoryAllowsNestedPublicationAndRejectsStaleDecisionAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            await using ILifecycleWriter invocation = await lifecycle.CaptureChanges()
                .BeginAsync(timeout.Token).ConfigureAwait(false);
            TrackingLifecycleNodeManager? candidate = null;
            IAsyncNodeManagerFactory inner = CreateTrackingNodeManagementFactory(
                kGeneration1Value, manager => candidate = manager);
            var factory = new CountingNodeManagerFactory([kModelNamespaceUri], async (server, config, token) =>
            {
                RequireRegistration(
                    (await CommitChangeAsync(lifecycle, LifecycleChange.Add(CreateTrackingNodeManagementFactory(
                    kSecondRegistrationValue, _ => { }, kSecondModelNamespaceUri)), token).ConfigureAwait(
                        false)).Registrations[0]);
                return await inner.CreateAsync(server, config, token).ConfigureAwait(false);
            });
            await using IPreparedChanges batch = await invocation.PrepareAsync(
                [LifecycleChange.Add(factory)], timeout.Token).ConfigureAwait(false);
            int decisions = 0;
            await Assert.ThatAsync(() => batch.CommitAsync(_ =>
            {
                decisions++;
                return default;
            }, timeout.Token).AsTask(), Throws.TypeOf<NodeManagerRoutingConflictException>()).ConfigureAwait(false);
            await invocation.DisposeAsync().ConfigureAwait(false);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(decisions, Is.Zero);
                Assert.That(factory.CreateCount, Is.EqualTo(1));
                Assert.That(RequireLifecycleValue(candidate).DisposeCount, Is.EqualTo(1));
                Assert.That(GetBranchRegistrations(lifecycle).Count, Is.EqualTo(1));
                Assert.That(m_server.CurrentInstance.NamespaceUris.GetIndex(kModelNamespaceUri), Is.EqualTo(-1));
            }
        }

        /// <summary>
        /// Reconciliation can publish a newer view before an own commit returns without refreshing its evidence.
        /// </summary>
        [Test]
        public async Task CapturedInvocationDoesNotAbsorbPublicationFromItsReadinessCallbackAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            await using ILifecycleWriter invocation = await lifecycle.CaptureChanges()
                .BeginAsync(timeout.Token).ConfigureAwait(false);
            var factory = new CountingNodeManagerFactory([kModelNamespaceUri], (server, config, _) =>
                new ValueTask<IAsyncNodeManager>(new ReadinessLifecycleNodeManager(
                    server, config, m_logger, kGeneration1Value)
                {
                    ReadinessCallback = async token =>
                    {
                        RequireRegistration(
                            (await CommitChangeAsync(lifecycle, LifecycleChange.Add(CreateTrackingNodeManagementFactory(
                            kSecondRegistrationValue, _ => { }, kSecondModelNamespaceUri)), token)
                                .ConfigureAwait(false)).Registrations[0]);
                    }
                }));
            await using IPreparedChanges batch = await invocation.PrepareAsync(
                [LifecycleChange.Add(factory)], timeout.Token).ConfigureAwait(false);
            LifecycleResult committed = await batch.CommitAsync(_ => default, timeout.Token)
                .ConfigureAwait(false);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(batch.IsCommitted, Is.True);
                Assert.That(invocation.IsCurrent, Is.False);
                Assert.That(lifecycle.Revision, Is.GreaterThan(committed.Revision));
                Assert.That(factory.CreateCount, Is.EqualTo(1));
                Assert.That(GetBranchRegistrations(lifecycle).Count, Is.EqualTo(2));
            }
        }

        /// <summary>
        /// Canceled invocation admission and canceled unit preparation leave no operation preventing shutdown.
        /// </summary>
        [Test]
        public async Task CapturedInvocationCancellationReleasesAdmissionAndPreparedOperationLifetimesAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var lifecycle = (NodeManagerLifecycle)m_server.NodeManagerLifecycle;
            ILifecycleCapture capture = lifecycle.CaptureChanges();
            await Assert.ThatAsync(() => capture.BeginAsync(cancellation.Token).AsTask(),
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
            var factory = new CountingNodeManagerFactory(
                [kModelNamespaceUri], CreateTrackingNodeManagementFactory(kGeneration1Value, _ => { }).CreateAsync);
            await using (ILifecycleWriter invocation = await capture.BeginAsync(timeout.Token)
                .ConfigureAwait(false))
            {
                await Assert.ThatAsync(
                    () => invocation.PrepareAsync([LifecycleChange.Add(factory)], cancellation.Token).AsTask(),
                    Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
                Assert.That(invocation.IsCurrent, Is.True);
            }
            await lifecycle.BeginShutdownAsync(m_server.CurrentInstance, timeout.Token).ConfigureAwait(false);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(factory.CreateCount, Is.Zero);
                Assert.That(lifecycle.IsShuttingDown, Is.True);
                Assert.That(GetBranchRegistrations(lifecycle), Is.Empty);
            }
        }

        /// <summary>
        /// An invocation retains shutdown ownership through its unpublished transaction cleanup.
        /// </summary>
        [Test]
        public async Task WriterShutdownWaitsForInvocationAndDisposesItsPreparedTransactionsAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var lifecycle = (NodeManagerLifecycle)m_server.NodeManagerLifecycle;
            TrackingLifecycleNodeManager? candidate = null;
            ILifecycleWriter invocation = await lifecycle.CaptureChanges().BeginAsync(timeout.Token)
                .ConfigureAwait(false);
            try
            {
                IPreparedChanges batch = await invocation.PrepareAsync(
                    [LifecycleChange.Add(CreateTrackingNodeManagementFactory(
                        kGeneration1Value, manager => candidate = manager))], timeout.Token).ConfigureAwait(false);
                Task shutdown = lifecycle.BeginShutdownAsync(m_server.CurrentInstance, timeout.Token).AsTask();
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(shutdown.IsCompleted, Is.False);
                    Assert.That(lifecycle.IsShuttingDown, Is.True, "Shutdown immediately closes new admission.");
                    Assert.That(batch.IsCommitted, Is.False);
                }
                await invocation.DisposeAsync().ConfigureAwait(false);
                await shutdown.WaitAsync(timeout.Token).ConfigureAwait(false);
                AssertLifecycleValue(candidate);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(candidate.DisposeCount, Is.EqualTo(1));
                    Assert.That(lifecycle.IsShuttingDown, Is.True);
                    Assert.That(GetBranchRegistrations(lifecycle), Is.Empty);
                }
            }
            finally
            {
                await invocation.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
