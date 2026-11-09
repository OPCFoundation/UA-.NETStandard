/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
 * THE SOFTWARE.
 * ======================================================================*/

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Historian;

namespace Opc.Ua.Server.Tests.NodeManager
{
    public sealed partial class NodeManagerLifecycleTests
    {
        /// <summary>
        /// A detected wrapper of an already-live resource needs an explicit cleanup lease.
        /// </summary>
        [Test]
        public async Task UndeclaredForwardedLiveOwnerIsRejectedBeforePreparationAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await using BorrowedOwnershipLiveResource owner = await CreateBorrowedOwnershipLiveResourceAsync(
                kModelNamespaceUri + ":BorrowedRejected", timeout.Token).ConfigureAwait(false);
            var candidate = new BorrowedOwnershipCandidate(owner);
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory.SetupGet(value => value.NamespacesUris).Returns([owner.NamespaceUri]);
            factory.Setup(value => value.CreateAsync(
                    It.IsAny<IServerInternal>(), It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IAsyncNodeManager>(candidate.NodeManager));
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            var host = (IDynamicNodeManagerBatchHost)owner.Server.NodeManager;
            NodeManagerRoutingTable.RoutingSnapshot before = host.RoutingRevision;
            NodeManagerGenerationReference binding = owner.Source.Generation;
            Assert.That(owner.Raw.Server, Is.SameAs(owner.Server));
            Assert.That(owner.Source.NodeManager, Is.SameAs(owner.Raw));
            Assert.That(candidate.NodeManager, Is.Not.InstanceOf<AsyncNodeManagerAdapter>());
            Assert.That(candidate.NodeManager.SyncNodeManager, Is.InstanceOf<SyncNodeManagerAdapter>());
            Assert.That(((SyncNodeManagerAdapter)candidate.NodeManager.SyncNodeManager).AsyncNodeManager,
                Is.SameAs(owner.Raw));
            Assert.That(binding.TryGetGeneration(out NodeManagerGeneration? originalGeneration), Is.True);
            Assert.That(originalGeneration!.Gate.State, Is.EqualTo(NodeManagerGenerationState.Live));
            await AssertBorrowedOwnershipCanEmitAsync(owner, "before rejection", timeout.Token).ConfigureAwait(false);

            IPreparedNodeManagerBatch? unexpected = null;
            try
            {
                NodeManagerBorrowedResourceException? failure =
                    await Assert.ThrowsAsync<NodeManagerBorrowedResourceException>(async () =>
                    {
                        unexpected = await lifecycle.PrepareAsync(
                            [NodeManagerBatchChange.Add(factory.Object)], timeout.Token).ConfigureAwait(false);
                    }).ConfigureAwait(false);
                Assert.That(failure!.Candidate, Is.SameAs(candidate.NodeManager));
                Assert.That(owner.Source.Generation, Is.SameAs(binding));
                Assert.That(host.RoutingRevision, Is.SameAs(before));
                Assert.That(host.RoutingRevision.VisibleNodeManagers, Has.None.SameAs(candidate.NodeManager));
                Assert.That(candidate.PreparedState, Is.Zero);
                Assert.That(candidate.CreateCount, Is.Zero);
                Assert.That(candidate.Lease.RollbackCount, Is.Zero);
                Assert.That(candidate.Lease.RetireCount, Is.Zero);
                factory.Verify(value => value.CreateAsync(
                    It.IsAny<IServerInternal>(), It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()),
                    Times.Once);
                AssertBorrowedOwnershipNotDestroyed(owner, candidate);
                await AssertBorrowedOwnershipCanEmitAsync(owner, "after rejection", timeout.Token)
                    .ConfigureAwait(false);
            }
            finally
            {
                if (unexpected is not null)
                {
                    await unexpected.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Abandoning a prepared candidate reverses its private work without destroying its live owner.
        /// </summary>
        [Test]
        public async Task AbandonedBorrowedCandidateRollsBackWithoutDestroyingLiveOwnerAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await using BorrowedOwnershipLiveResource owner = await CreateBorrowedOwnershipLiveResourceAsync(
                kModelNamespaceUri + ":BorrowedAbort", timeout.Token).ConfigureAwait(false);
            var candidate = new BorrowedOwnershipCandidate(owner);
            var factory = new BorrowedOwnershipFactory(owner.NamespaceUri, candidate.Lease);
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            var host = (IDynamicNodeManagerBatchHost)owner.Server.NodeManager;
            NodeManagerRoutingTable.RoutingSnapshot before = host.RoutingRevision;
            NodeManagerGenerationReference binding = owner.Source.Generation;
            await AssertBorrowedOwnershipCanEmitAsync(owner, "before preparation", timeout.Token)
                .ConfigureAwait(false);

            IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(factory)], timeout.Token).ConfigureAwait(false);
            await using (prepared.ConfigureAwait(false))
            {
                Assert.That(prepared.IsCommitted, Is.False);
                Assert.That(factory.BorrowedCreateCount, Is.EqualTo(1));
                Assert.That(factory.LegacyCreateCount, Is.Zero);
                Assert.That(candidate.CreateCount, Is.EqualTo(1));
                Assert.That(candidate.PreparedState, Is.EqualTo(1));
                Assert.That(candidate.Lease.RollbackCount, Is.Zero);
                Assert.That(candidate.Lease.RetireCount, Is.Zero);
                Assert.That(host.RoutingRevision, Is.SameAs(before));
                Assert.That(host.RoutingRevision.VisibleNodeManagers, Has.None.SameAs(candidate.NodeManager));
                Assert.That(owner.Source.Generation, Is.SameAs(binding));
                AssertBorrowedOwnershipNotDestroyed(owner, candidate);
                await AssertBorrowedOwnershipCanEmitAsync(owner, "while prepared", timeout.Token)
                    .ConfigureAwait(false);
            }

            // Disposing the prepared owner again must not repeat the successful rollback.
            await prepared.DisposeAsync().ConfigureAwait(false);
            Assert.That(candidate.PreparedState, Is.Zero);
            Assert.That(candidate.Lease.RollbackCount, Is.EqualTo(1));
            Assert.That(candidate.Lease.RollbackObservedState, Is.EqualTo(1));
            Assert.That(candidate.Lease.RetireCount, Is.Zero);
            Assert.That(host.RoutingRevision, Is.SameAs(before));
            Assert.That(owner.Source.Generation, Is.SameAs(binding));
            AssertBorrowedOwnershipNotDestroyed(owner, candidate);
            await AssertBorrowedOwnershipCanEmitAsync(owner, "after rollback", timeout.Token)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// A pending durable decision prevents rollback until publication commits or is abandoned.
        /// </summary>
        /// <param name="commit">Whether the durable decision succeeds.</param>
        [TestCase(false)]
        [TestCase(true)]
        public async Task BorrowedPublicationDecisionSerializesRollbackAsync(bool commit)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await using BorrowedOwnershipLiveResource owner = await CreateBorrowedOwnershipLiveResourceAsync(
                kModelNamespaceUri + ":BorrowedDecision", timeout.Token).ConfigureAwait(false);
            var candidate = new BorrowedOwnershipCandidate(owner);
            var factory = new BorrowedOwnershipFactory(owner.NamespaceUri, candidate.Lease);
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(factory)], timeout.Token).ConfigureAwait(false);
            NodeManagerRegistration registration = prepared.Registrations[0];
            BorrowedNodeManagerLeaseState lease = RequireLifecycleValue(
                ChangePreparer.GetBorrowedLease(candidate.NodeManager));
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var failure = new IOException("The durable decision failed.");
            Task<NodeManagerBatchResult> publication = prepared.CommitAsync(
                async cancellationToken =>
                {
                    entered.TrySetResult(true);
                    await release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                    if (!commit)
                    {
                        throw failure;
                    }
                },
                timeout.Token).AsTask();

            try
            {
                await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                Task rollback = lease.RollbackAsync().AsTask();
                Assert.That(rollback.IsCompleted, Is.False);
                Assert.That(candidate.Lease.RollbackCount, Is.Zero);
                AssertBorrowedOwnershipNotDestroyed(owner, candidate);
                release.TrySetResult(true);

                if (commit)
                {
                    NodeManagerBatchResult result = await publication.WaitAsync(timeout.Token).ConfigureAwait(false);
                    Assert.That(result.CleanupFailure, Is.Null);
                    Assert.That(prepared.IsCommitted, Is.True);
                    await Assert.ThrowsAsync<InvalidOperationException>(
                        async () => await rollback.WaitAsync(timeout.Token).ConfigureAwait(false))
                        .ConfigureAwait(false);
                    Assert.That(candidate.Lease.RollbackCount, Is.Zero);
                    Assert.That(candidate.PreparedState, Is.EqualTo(1));
                }
                else
                {
                    IOException? observed = await Assert.ThrowsAsync<IOException>(
                        async () => await publication.WaitAsync(timeout.Token).ConfigureAwait(false))
                        .ConfigureAwait(false);
                    Assert.That(observed, Is.SameAs(failure));
                    await rollback.WaitAsync(timeout.Token).ConfigureAwait(false);
                    Assert.That(prepared.IsCommitted, Is.False);
                    Assert.That(candidate.Lease.RollbackCount, Is.EqualTo(1));
                    Assert.That(candidate.PreparedState, Is.Zero);
                }
                AssertBorrowedOwnershipNotDestroyed(owner, candidate);
            }
            finally
            {
                release.TrySetResult(true);
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                try
                {
                    await prepared.DisposeAsync().AsTask().WaitAsync(cleanup.Token).ConfigureAwait(false);
                }
                finally
                {
                    if (lifecycle.Registrations.ToList().Any(value => ReferenceEquals(value, registration)))
                    {
                        await RemoveAndRetireAsync(lifecycle, registration, cleanup.Token).ConfigureAwait(false);
                    }
                }
            }
        }

        /// <summary>
        /// A partial preparation failure rolls back candidate state and preserves the original error.
        /// </summary>
        [Test]
        public async Task PartiallyPreparedBorrowedCandidateRollsBackAndPropagatesOriginalErrorAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await using BorrowedOwnershipLiveResource owner = await CreateBorrowedOwnershipLiveResourceAsync(
                kModelNamespaceUri + ":BorrowedPartialFailure", timeout.Token).ConfigureAwait(false);
            var original = new IOException("The borrowed candidate failed after its private mutation.");
            var candidate = new BorrowedOwnershipCandidate(owner) { PreparationFailure = original };
            var factory = new BorrowedOwnershipFactory(owner.NamespaceUri, candidate.Lease);
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            var host = (IDynamicNodeManagerBatchHost)owner.Server.NodeManager;
            NodeManagerRoutingTable.RoutingSnapshot before = host.RoutingRevision;
            NodeManagerGenerationReference binding = owner.Source.Generation;

            IPreparedNodeManagerBatch? unexpected = null;
            try
            {
                IOException? failure = await Assert.ThrowsAsync<IOException>(async () =>
                {
                    unexpected = await lifecycle.PrepareAsync(
                        [NodeManagerBatchChange.Add(factory)], timeout.Token).ConfigureAwait(false);
                }).ConfigureAwait(false);
                Assert.That(failure, Is.SameAs(original));
                Assert.That(factory.BorrowedCreateCount, Is.EqualTo(1));
                Assert.That(factory.LegacyCreateCount, Is.Zero);
                Assert.That(candidate.CreateCount, Is.EqualTo(1));
                Assert.That(candidate.PreparedState, Is.Zero);
                Assert.That(candidate.Lease.RollbackCount, Is.EqualTo(1));
                Assert.That(candidate.Lease.RollbackObservedState, Is.EqualTo(1));
                Assert.That(candidate.Lease.RetireCount, Is.Zero);
                Assert.That(host.RoutingRevision, Is.SameAs(before));
                Assert.That(host.RoutingRevision.VisibleNodeManagers, Has.None.SameAs(candidate.NodeManager));
                Assert.That(owner.Source.Generation, Is.SameAs(binding));
                AssertBorrowedOwnershipNotDestroyed(owner, candidate);
                await AssertBorrowedOwnershipCanEmitAsync(owner, "after partial rollback", timeout.Token)
                    .ConfigureAwait(false);
            }
            finally
            {
                if (unexpected is not null)
                {
                    await unexpected.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// A failed unpublished rollback retains its lease and staging resources until the caller retries.
        /// </summary>
        [Test]
        public async Task FailedUnpublishedBorrowedRollbackCanBeRetriedWithoutDestroyingLiveOwnerAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await using BorrowedOwnershipLiveResource owner = await CreateBorrowedOwnershipLiveResourceAsync(
                kModelNamespaceUri + ":BorrowedRollbackRetry", timeout.Token).ConfigureAwait(false);
            var candidate = new BorrowedOwnershipCandidate(owner);
            var original = new IOException("The borrowed candidate rollback failed.");
            candidate.Lease.RollbackFailure = original;
            var factory = new BorrowedOwnershipFactory(owner.NamespaceUri, candidate.Lease);
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            var host = (IDynamicNodeManagerBatchHost)owner.Server.NodeManager;
            NodeManagerRoutingTable.RoutingSnapshot before = host.RoutingRevision;
            NodeManagerGenerationReference binding = owner.Source.Generation;
            Assert.That(binding.TryGetGeneration(out NodeManagerGeneration? originalGeneration), Is.True);
            IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(factory)], timeout.Token).ConfigureAwait(false);
            NodeManagerBorrowedResourceCleanupException? cleanup = null;
            try
            {
                ChangePreparer.StagingServer staging =
                    RequireLifecycleValue(ChangePreparer.GetServer(candidate.NodeManager));
                IHistorianProviderRegistry serving =
                    ((IHistorianRegistryProvider)owner.Server).HistorianRegistry;
                IHistorianProviderRegistry staged =
                    ((IHistorianRegistryProvider)staging).HistorianRegistry;
                var provider = new LifecycleHistorianProvider();
                var stagedNode = new NodeId("borrowed-rollback-staged-provider", owner.Raw.NamespaceIndexes[0]);
                IHistorianProvider? priorServingProvider = serving.Resolve(stagedNode);
                string stagedNamespace = owner.NamespaceUri + ":RollbackRetryPrivate";
                staging.NamespaceUris.GetIndexOrAppend(stagedNamespace);
                staged.RegisterForNode(stagedNode, provider);

                Assert.That(prepared.IsCommitted, Is.False);
                Assert.That(factory.BorrowedCreateCount, Is.EqualTo(1));
                Assert.That(factory.LegacyCreateCount, Is.Zero);
                Assert.That(candidate.CreateCount, Is.EqualTo(1));
                Assert.That(candidate.PreparedState, Is.EqualTo(1));
                Assert.That(staging.IsStaging, Is.True);
                Assert.That(staging.RegisteredFacade, Is.SameAs(candidate.NodeManager));
                Assert.That(staged.Resolve(stagedNode), Is.SameAs(provider));
                Assert.That(serving.Resolve(stagedNode), Is.SameAs(priorServingProvider));
                Assert.That(owner.Server.NamespaceUris.GetIndex(stagedNamespace), Is.EqualTo(-1));
                Assert.That(provider.DisposeCount, Is.Zero);

                Exception? failure = await Assert.CatchAsync<Exception>(async () =>
                    await prepared.DisposeAsync().AsTask().WaitAsync(timeout.Token).ConfigureAwait(false))
                    .ConfigureAwait(false);
                cleanup = FindBorrowedOwnershipCleanupException(failure);
                NodeManagerBorrowedResourceCleanupException retry = RequireLifecycleValue(cleanup);
                Assert.That(retry.InnerException, Is.SameAs(original));
                int failedAttempts = candidate.Lease.RollbackCount;
                Assert.That(failedAttempts, Is.GreaterThan(0));
                Assert.That(candidate.Lease.RollbackObservedState, Is.EqualTo(1));
                Assert.That(candidate.Lease.RetireCount, Is.Zero);
                Assert.That(candidate.PreparedState, Is.EqualTo(1));
                Assert.That(staged.Resolve(stagedNode), Is.SameAs(provider));
                Assert.That(provider.DisposeCount, Is.Zero);
                Assert.That(host.RoutingRevision, Is.SameAs(before));
                Assert.That(host.RoutingRevision.VisibleNodeManagers, Has.None.SameAs(candidate.NodeManager));
                Assert.That(owner.Source.Generation, Is.SameAs(binding));
                Assert.That(binding.TryGetGeneration(out NodeManagerGeneration? failedBound), Is.True);
                Assert.That(failedBound, Is.SameAs(originalGeneration));
                AssertBorrowedOwnershipNotDestroyed(owner, candidate);
                await AssertBorrowedOwnershipCanEmitAsync(owner, "after failed rollback", timeout.Token)
                    .ConfigureAwait(false);

                candidate.Lease.RollbackFailure = null;
                await retry.RetryAsync().AsTask().WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(candidate.Lease.RollbackCount, Is.EqualTo(failedAttempts + 1));
                Assert.That(candidate.Lease.RollbackObservedState, Is.EqualTo(1));
                Assert.That(candidate.Lease.RetireCount, Is.Zero);
                Assert.That(candidate.PreparedState, Is.Zero);
                Assert.That(staging.IsDetached, Is.True);
                Assert.That(staging.RegisteredFacade, Is.Null);
                Assert.That(provider.DisposeCount, Is.EqualTo(1));
                Assert.That(serving.Resolve(stagedNode), Is.SameAs(priorServingProvider));
                Assert.That(owner.Server.NamespaceUris.GetIndex(stagedNamespace), Is.EqualTo(-1));
                Assert.That(host.RoutingRevision, Is.SameAs(before));
                Assert.That(owner.Source.Generation, Is.SameAs(binding));
                Assert.That(binding.TryGetGeneration(out NodeManagerGeneration? retriedBound), Is.True);
                Assert.That(retriedBound, Is.SameAs(originalGeneration));
                AssertBorrowedOwnershipNotDestroyed(owner, candidate);
                await AssertBorrowedOwnershipCanEmitAsync(owner, "after rollback retry", timeout.Token)
                    .ConfigureAwait(false);

                await retry.RetryAsync().AsTask().WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(candidate.Lease.RollbackCount, Is.EqualTo(failedAttempts + 1));
                Assert.That(candidate.Lease.RetireCount, Is.Zero);
                Assert.That(provider.DisposeCount, Is.EqualTo(1));
            }
            finally
            {
                candidate.Lease.RollbackFailure = null;
                using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                try
                {
                    if (cleanup is not null)
                    {
                        await cleanup.RetryAsync().AsTask().WaitAsync(cleanupTimeout.Token).ConfigureAwait(false);
                    }
                }
                finally
                {
                    await prepared.DisposeAsync().AsTask().WaitAsync(cleanupTimeout.Token).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// A prior raw binding keeps its original generation while new admissions use the facade's gate.
        /// Both a pre-publication raw admission and a published event drain before candidate retirement.
        /// </summary>
        /// <param name="replace">Whether a new manager replaces the facade rather than removing it.</param>
        [TestCase(false)]
        [TestCase(true)]
        public async Task PublishedBorrowedEventDrainsBeforeLeaseRetirementAsync(bool replace)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await using BorrowedOwnershipLiveResource owner = await CreateBorrowedOwnershipLiveResourceAsync(
                kModelNamespaceUri + ":BorrowedPublished", timeout.Token).ConfigureAwait(false);
            var candidate = new BorrowedOwnershipCandidate(owner);
            var factory = new BorrowedOwnershipFactory(owner.NamespaceUri, candidate.Lease);
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            var master = (MasterNodeManager)owner.Server.NodeManager;
            var host = (IDynamicNodeManagerBatchHost)master;
            NodeManagerGenerationReference priorBinding = owner.Source.Generation;
            Assert.That(priorBinding.TryGetGeneration(out NodeManagerGeneration? priorGeneration), Is.True);
            candidate.Lease.ObserveRawGenerationAtRetire = priorGeneration;
            await AssertBorrowedOwnershipCanEmitAsync(owner, "before publication", timeout.Token)
                .ConfigureAwait(false);

            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            NodeManagerRegistration? active = null;
            NodeManagerBatchResult? retirement = null;
            TrackingLifecycleNodeManager? replacement = null;
            MasterNodeManager.SourceEmissionAdmission? priorAdmission = null;
            owner.Raw.EventAuthorizationBarrier = async token =>
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(token).ConfigureAwait(false);
            };
            try
            {
                Assert.That(MasterNodeManager.TryCaptureSourceEmission(
                    owner.Server, owner.Raw, out priorAdmission, priorBinding), Is.True);
                Assert.That(priorAdmission.HasValue, Is.True);
                Assert.That(priorAdmission?.Generation, Is.SameAs(priorGeneration));
                Assert.That(priorAdmission?.Generation.Gate, Is.SameAs(priorGeneration!.Gate));

                await using (IPreparedNodeManagerBatch addition = await lifecycle.PrepareAsync(
                    [NodeManagerBatchChange.Add(factory)], timeout.Token).ConfigureAwait(false))
                {
                    NodeManagerBatchResult result = await addition.CommitAsync(_ => default, timeout.Token)
                        .ConfigureAwait(false);
                    active = result.Registrations[0];
                    Assert.That(result.CleanupFailure, Is.Null);
                    Assert.That(addition.IsCommitted, Is.True);
                }

                NodeManagerRegistration published = RequireLifecycleValue(active);
                NodeManagerGeneration generation = published.Identity;
                Assert.That(published.NodeManager, Is.SameAs(candidate.NodeManager));
                Assert.That(owner.Source.Generation, Is.SameAs(priorBinding));
                Assert.That(priorBinding.TryGetGeneration(out NodeManagerGeneration? bound), Is.True);
                Assert.That(bound, Is.SameAs(priorGeneration));
                Assert.That(generation, Is.Not.SameAs(priorGeneration));
                Assert.That(generation.Gate.State, Is.EqualTo(NodeManagerGenerationState.Live));
                Assert.That(host.RoutingRevision.GetGeneration(candidate.NodeManager), Is.SameAs(generation));
                Assert.That(host.RoutingRevision.GetGeneration(candidate.NodeManager).Gate,
                    Is.SameAs(generation.Gate));
                AssertBorrowedOwnershipAdmission(owner, expected: true, generation);
                (object? publishedHandle, IAsyncNodeManager? publishedOwner) = await master.GetManagerHandleAsync(
                    owner.Value.NodeId, timeout.Token).ConfigureAwait(false);
                Assert.That(publishedOwner, Is.SameAs(candidate.NodeManager));
                Assert.That((publishedHandle as NodeHandle)?.Node, Is.SameAs(owner.Value));
                Assert.That(candidate.CreateCount, Is.EqualTo(1));
                Assert.That(candidate.PreparedState, Is.EqualTo(1));
                Assert.That(candidate.Lease.RollbackCount, Is.Zero);
                Assert.That(candidate.Lease.RetireCount, Is.Zero);
                AssertBorrowedOwnershipNotDestroyed(owner, candidate);

                Assert.That(owner.PriorEvents, Is.Empty);
                BaseEventState admittedEvent = CreateBorrowedOwnershipEvent(owner, "admitted before retirement");
                await owner.Root.ReportEventAsync(
                    owner.Server.DefaultSystemContext, admittedEvent, timeout.Token).ConfigureAwait(false);
                await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(generation.Gate.ActiveCount, Is.GreaterThan(0));
                Assert.That(owner.PriorEvents, Is.Empty);

                NodeManagerBatchChange change = replace
                    ? NodeManagerBatchChange.Replace(
                        published,
                        CreateTrackingNodeManagementFactory(
                            kSecondRegistrationValue, value => replacement = value, owner.NamespaceUri),
                        immediate: true)
                    : NodeManagerBatchChange.Remove(published);
                await using (IPreparedNodeManagerBatch removal = await lifecycle.PrepareAsync(
                    [change], timeout.Token).ConfigureAwait(false))
                {
                    retirement = await removal.CommitAsync(_ => default, timeout.Token).ConfigureAwait(false);
                    active = replace ? retirement.Registrations[0] : null;
                    Assert.That(removal.IsCommitted, Is.True);
                }

                Assert.That(retirement.CleanupFailure, Is.Null);
                Assert.That(retirement.Retirement.IsCompleted, Is.False);
                Assert.That(generation.Gate.State, Is.EqualTo(NodeManagerGenerationState.Retiring));
                Assert.That(host.RoutingRevision.VisibleNodeManagers, Has.None.SameAs(candidate.NodeManager));
                Assert.That(priorBinding.TryGetGeneration(out NodeManagerGeneration? retiredBound), Is.True);
                Assert.That(retiredBound, Is.SameAs(priorGeneration));
                AssertBorrowedOwnershipAdmission(owner, expected: false);
                Assert.That(candidate.Lease.RollbackCount, Is.Zero);
                Assert.That(candidate.Lease.RetireCount, Is.Zero);
                Assert.That(candidate.PreparedState, Is.EqualTo(1));
                AssertBorrowedOwnershipNotDestroyed(owner, candidate);

                (object? nextHandle, IAsyncNodeManager? nextOwner) = await master.GetManagerHandleAsync(
                    owner.Value.NodeId, timeout.Token).ConfigureAwait(false);
                if (replace)
                {
                    TrackingLifecycleNodeManager next = RequireLifecycleValue(replacement);
                    Assert.That(nextOwner, Is.SameAs(next));
                    Assert.That((nextHandle as NodeHandle)?.Node, Is.SameAs(next.Find(owner.Value.NodeId)));
                    Assert.That(((BaseVariableState)next.Find(owner.Value.NodeId)!).Value,
                        Is.EqualTo(kSecondRegistrationValue));
                    bool nextAdmitted = MasterNodeManager.TryCaptureSourceEmission(
                        owner.Server, next, out var nextAdmission);
                    try
                    {
                        Assert.That(nextAdmitted, Is.True);
                        Assert.That(nextAdmission?.Generation, Is.SameAs(active!.Identity));
                        Assert.That(nextAdmission?.Generation.Gate, Is.SameAs(active!.Identity.Gate));
                    }
                    finally
                    {
                        nextAdmission?.Dispose();
                    }
                }
                else
                {
                    Assert.That(nextHandle, Is.Null);
                    Assert.That(nextOwner, Is.Null);
                }

                BaseEventState rejectedEvent = CreateBorrowedOwnershipEvent(owner, "after retirement");
                await owner.Root.ReportEventAsync(
                    owner.Server.DefaultSystemContext, rejectedEvent, timeout.Token).ConfigureAwait(false);
                Assert.That(owner.PriorEvents, Is.Empty);
                release.TrySetResult(true);
                await owner.Source.WaitForPendingNotificationsAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(owner.PriorEvents.TryDequeue(out IFilterTarget? priorEvent), Is.True);
                Assert.That(((BaseEventState)priorEvent!).Message!.Value.Text,
                    Is.EqualTo("admitted before retirement"));
                Assert.That(owner.PriorEvents, Is.Empty);

                // The published event is drained, but the earlier raw admission still owns the
                // borrowed resource. It alone must prevent the facade lease from retiring.
                await generation.Gate.WaitForDrainAsync().WaitAsync(timeout.Token).ConfigureAwait(false);
                await generation.WaitForViewEpochsAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(generation.Gate.ActiveCount, Is.Zero);
                Assert.That(priorGeneration!.Gate.ActiveCount, Is.GreaterThan(0));
                Task quiescence = generation.WaitForQuiescenceAsync(timeout.Token).AsTask();
                Assert.That(quiescence.IsCompleted, Is.False);
                Assert.That(retirement.Retirement.IsCompleted, Is.False);
                Assert.That(candidate.Lease.RetireCount, Is.Zero);
                Assert.That(candidate.Lease.RollbackCount, Is.Zero);
                Assert.That(candidate.PreparedState, Is.EqualTo(1));
                AssertBorrowedOwnershipNotDestroyed(owner, candidate);
                priorAdmission?.Dispose();
                priorAdmission = null;

                await quiescence.WaitAsync(timeout.Token).ConfigureAwait(false);
                await retirement.Retirement.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(candidate.PreparedState, Is.Zero);
                Assert.That(candidate.Lease.RollbackCount, Is.Zero);
                Assert.That(candidate.Lease.RetireCount, Is.EqualTo(1));
                Assert.That(candidate.Lease.RawAdmissionsObservedAtRetire, Is.Zero);
                Assert.That(priorBinding.TryGetGeneration(out NodeManagerGeneration? finalBound), Is.True);
                Assert.That(finalBound, Is.SameAs(priorGeneration));
                Assert.That(factory.BorrowedCreateCount, Is.EqualTo(1));
                Assert.That(factory.LegacyCreateCount, Is.Zero);
                AssertBorrowedOwnershipNotDestroyed(owner, candidate);
                AssertBorrowedOwnershipAdmission(owner, expected: false);
            }
            finally
            {
                release.TrySetResult(true);
                priorAdmission?.Dispose();
                owner.Raw.EventAuthorizationBarrier = null;
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                try
                {
                    await owner.Source.WaitForPendingNotificationsAsync(cleanup.Token).ConfigureAwait(false);
                    if (retirement is not null)
                    {
                        await retirement.Retirement.WaitAsync(cleanup.Token).ConfigureAwait(false);
                    }
                }
                finally
                {
                    if (active is not null &&
                        lifecycle.Registrations.ToList().Any(value => ReferenceEquals(value, active)))
                    {
                        await RemoveAndRetireAsync(lifecycle, active, cleanup.Token).ConfigureAwait(false);
                    }
                }
            }
        }

        /// <summary>
        /// A failed retirement keeps its candidate lease and original failure until a later retry succeeds.
        /// </summary>
        [Test]
        public async Task FailedBorrowedRetirementRetainsRetryOwnershipAndReportsOriginalFailureAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await using BorrowedOwnershipLiveResource owner = await CreateBorrowedOwnershipLiveResourceAsync(
                kModelNamespaceUri + ":BorrowedRetry", timeout.Token).ConfigureAwait(false);
            var candidate = new BorrowedOwnershipCandidate(owner);
            var original = new IOException("The first borrowed retirement failed.");
            candidate.Lease.FirstRetirementFailure = original;
            candidate.Lease.BlockRetirementRetry = true;
            var factory = new BorrowedOwnershipFactory(owner.NamespaceUri, candidate.Lease);
            var lifecycle = (NodeManagerLifecycle)m_server.NodeManagerLifecycle;
            var host = (IDynamicNodeManagerBatchHost)owner.Server.NodeManager;
            int retiredBefore = lifecycle.RetiredNodeManagerCount;
            NodeManagerGenerationReference priorBinding = owner.Source.Generation;
            Assert.That(priorBinding.TryGetGeneration(out NodeManagerGeneration? priorGeneration), Is.True);
            NodeManagerRegistration? active = null;
            NodeManagerBatchResult? retirement = null;
            MasterNodeManager.SourceEmissionAdmission? admission = null;
            try
            {
                await using (IPreparedNodeManagerBatch addition = await lifecycle.PrepareAsync(
                    [NodeManagerBatchChange.Add(factory)], timeout.Token).ConfigureAwait(false))
                {
                    NodeManagerBatchResult result = await addition.CommitAsync(_ => default, timeout.Token)
                        .ConfigureAwait(false);
                    active = result.Registrations[0];
                    Assert.That(result.CleanupFailure, Is.Null);
                }
                NodeManagerRegistration published = RequireLifecycleValue(active);
                Assert.That(priorBinding.TryGetGeneration(out NodeManagerGeneration? bound), Is.True);
                Assert.That(bound, Is.SameAs(priorGeneration));
                Assert.That(published.Identity, Is.Not.SameAs(priorGeneration));
                AssertBorrowedOwnershipAdmission(owner, expected: true, published.Identity);
                Assert.That(MasterNodeManager.TryCaptureSourceEmission(
                    owner.Server, owner.Raw, out admission, priorBinding), Is.True);
                Assert.That(admission?.Generation, Is.SameAs(published.Identity));
                Assert.That(admission?.Generation.Gate, Is.SameAs(published.Identity.Gate));

                await using (IPreparedNodeManagerBatch removal = await lifecycle.PrepareAsync(
                    [NodeManagerBatchChange.Remove(published)], timeout.Token).ConfigureAwait(false))
                {
                    retirement = await removal.CommitAsync(_ => default, timeout.Token).ConfigureAwait(false);
                    active = null;
                }
                Assert.That(retirement.CleanupFailure, Is.Null);
                Assert.That(retirement.Retirement.IsCompleted, Is.False);
                Assert.That(candidate.Lease.RetireCount, Is.Zero);
                AssertBorrowedOwnershipAdmission(owner, expected: false);
                admission?.Dispose();
                admission = null;

                await candidate.Lease.RetryEntered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(candidate.Lease.RetireCount, Is.EqualTo(2));
                Assert.That(candidate.Lease.RollbackCount, Is.Zero);
                Assert.That(candidate.PreparedState, Is.EqualTo(1));
                Assert.That(retirement.Retirement.IsCompleted, Is.False);
                Assert.That(lifecycle.RetiredNodeManagerCount, Is.EqualTo(retiredBefore + 1));
                Assert.That(host.RoutingRevision.VisibleNodeManagers, Has.None.SameAs(candidate.NodeManager));
                Assert.That(lifecycle.Registrations.ToArray(), Has.None.SameAs(published));
                AssertBorrowedOwnershipAdmission(owner, expected: false);
                AssertBorrowedOwnershipNotDestroyed(owner, candidate);

                candidate.Lease.ReleaseRetry.TrySetResult(true);
                IOException? failure = await Assert.ThrowsAsync<IOException>(async () =>
                    await retirement.Retirement.WaitAsync(timeout.Token).ConfigureAwait(false)).ConfigureAwait(false);
                Assert.That(failure, Is.SameAs(original));
                Assert.That(candidate.PreparedState, Is.Zero);
                Assert.That(candidate.Lease.RetireCount, Is.EqualTo(2));
                Assert.That(candidate.Lease.RollbackCount, Is.Zero);
                Assert.That(lifecycle.RetiredNodeManagerCount, Is.EqualTo(retiredBefore));
                AssertBorrowedOwnershipNotDestroyed(owner, candidate);
            }
            finally
            {
                admission?.Dispose();
                candidate.Lease.ReleaseRetry.TrySetResult(true);
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                try
                {
                    if (retirement is not null)
                    {
                        try
                        {
                            await retirement.Retirement.WaitAsync(cleanup.Token).ConfigureAwait(false);
                        }
                        catch (IOException failure) when (ReferenceEquals(failure, original))
                        {
                            // Retirement preserves the first failed attempt even after cleanup succeeds.
                        }
                    }
                }
                finally
                {
                    if (active is not null &&
                        lifecycle.Registrations.ToList().Any(value => ReferenceEquals(value, active)))
                    {
                        await RemoveAndRetireAsync(lifecycle, active, cleanup.Token).ConfigureAwait(false);
                    }
                }
            }
        }

        private async ValueTask<BorrowedOwnershipLiveResource> CreateBorrowedOwnershipLiveResourceAsync(
            string namespaceUri,
            CancellationToken cancellationToken)
        {
            IServerInternal server = m_server.CurrentInstance;
            var raw = new BorrowedOwnershipRawNodeManager(
                server, m_server.CurrentConfiguration, m_logger, namespaceUri);
            MonitoredNode2? source = null;
            try
            {
                await raw.CreateAddressSpaceAsync(
                    new Dictionary<NodeId, IList<IReference>>(), cancellationToken).ConfigureAwait(false);
                var rootId = new NodeId(kRootNodeId, raw.NamespaceIndexes[0]);
                var valueId = new NodeId(kValueNodeId, raw.NamespaceIndexes[0]);
                var root = RequireLifecycleValue(raw.Find(rootId) as BaseObjectState);
                var value = RequireLifecycleValue(raw.Find(valueId) as BaseVariableState);
                source = new MonitoredNode2(raw, server, root);
                var priorEvents = new ConcurrentQueue<IFilterTarget>();
                Mock<IEventMonitoredItem> priorItem = CreateBorrowedOwnershipEventItem(server, raw, 713u);
                priorItem.Setup(item => item.QueueEvent(It.IsAny<IFilterTarget>()))
                    .Callback<IFilterTarget>(priorEvents.Enqueue);
                source.Add(priorItem.Object);
                return new BorrowedOwnershipLiveResource(
                    namespaceUri, server, raw, root, value, source, priorItem.Object, priorEvents);
            }
            catch
            {
                try
                {
                    if (source is not null)
                    {
                        await source.DisposeAsync().ConfigureAwait(false);
                    }
                }
                finally
                {
                    await raw.DisposeAsync().ConfigureAwait(false);
                }
                throw;
            }
        }

        private async Task AssertBorrowedOwnershipCanEmitAsync(
            BorrowedOwnershipLiveResource owner,
            string message,
            CancellationToken cancellationToken)
        {
            AssertBorrowedOwnershipAdmission(owner, expected: true);
            AssertBorrowedOwnershipPriorBinding(owner);
            Assert.That(owner.PriorEvents, Is.Empty);
            BaseEventState occurrence = CreateBorrowedOwnershipEvent(owner, message);
            await owner.Root.ReportEventAsync(
                owner.Server.DefaultSystemContext, occurrence, cancellationToken).ConfigureAwait(false);
            await owner.Source.WaitForPendingNotificationsAsync(cancellationToken).ConfigureAwait(false);
            Assert.That(owner.PriorEvents.TryDequeue(out IFilterTarget? notification), Is.True);
            Assert.That(((BaseEventState)notification!).Message!.Value.Text, Is.EqualTo(message));
            Assert.That(owner.PriorEvents, Is.Empty);
        }

        private Mock<IEventMonitoredItem> CreateBorrowedOwnershipEventItem(
            IServerInternal server,
            IAsyncNodeManager nodeManager,
            uint id)
        {
            ISession session = RequireLifecycleValue(
                server.SessionManager.GetSession(m_requestHeader.AuthenticationToken));
            var item = new Mock<IEventMonitoredItem>();
            item.SetupGet(value => value.Id).Returns(id);
            item.SetupGet(value => value.Session).Returns(session);
            item.SetupGet(value => value.EffectiveIdentity).Returns(session.EffectiveIdentity);
            item.SetupGet(value => value.NodeManager).Returns(nodeManager);
            return item;
        }

        private static BaseEventState CreateBorrowedOwnershipEvent(
            BorrowedOwnershipLiveResource owner,
            string message)
        {
            var occurrence = new BaseEventState(null);
            occurrence.Initialize(
                owner.Server.DefaultSystemContext,
                owner.Root,
                EventSeverity.Medium,
                new LocalizedText(message));
            return occurrence;
        }

        private static NodeManagerBorrowedResourceCleanupException? FindBorrowedOwnershipCleanupException(
            Exception? failure)
        {
            if (failure is NodeManagerBorrowedResourceCleanupException cleanup)
            {
                return cleanup;
            }
            if (failure is AggregateException aggregate)
            {
                foreach (Exception inner in aggregate.InnerExceptions)
                {
                    NodeManagerBorrowedResourceCleanupException? found = FindBorrowedOwnershipCleanupException(inner);
                    if (found is not null)
                    {
                        return found;
                    }
                }
            }
            else if (failure?.InnerException is { } inner)
            {
                return FindBorrowedOwnershipCleanupException(inner);
            }
            return null;
        }

        private static void AssertBorrowedOwnershipAdmission(
            BorrowedOwnershipLiveResource owner,
            bool expected,
            NodeManagerGeneration? expectedGeneration = null)
        {
            bool accepted = MasterNodeManager.TryCaptureSourceEmission(
                owner.Server, owner.Raw, out var admission, owner.Source.Generation);
            try
            {
                Assert.That(accepted, Is.EqualTo(expected));
                Assert.That(admission.HasValue, Is.EqualTo(expected));
                if (expectedGeneration is not null)
                {
                    Assert.That(admission?.Generation, Is.SameAs(expectedGeneration));
                    Assert.That(admission?.Generation.Gate, Is.SameAs(expectedGeneration.Gate));
                }
            }
            finally
            {
                admission?.Dispose();
            }

            bool itemAccepted = MasterNodeManager.TryCaptureItemEmission(
                owner.Server, owner.Raw, owner.Source.Generation, out var itemAdmission);
            try
            {
                Assert.That(itemAccepted, Is.EqualTo(expected));
                Assert.That(itemAdmission.HasValue, Is.EqualTo(expected));
                if (expectedGeneration is not null)
                {
                    Assert.That(itemAdmission?.Generation, Is.SameAs(expectedGeneration));
                    Assert.That(itemAdmission?.Generation.Gate, Is.SameAs(expectedGeneration.Gate));
                }
            }
            finally
            {
                itemAdmission?.Dispose();
            }
        }

        private static void AssertBorrowedOwnershipNotDestroyed(
            BorrowedOwnershipLiveResource owner,
            BorrowedOwnershipCandidate candidate)
        {
            candidate.Facade.Verify(
                value => value.DeleteAddressSpaceAsync(It.IsAny<CancellationToken>()), Times.Never);
            candidate.AsyncDisposable.Verify(value => value.DisposeAsync(), Times.Never);
            candidate.Disposable.Verify(value => value.Dispose(), Times.Never);
            AssertBorrowedOwnershipPriorBinding(owner);
            Assert.That(owner.Raw.CreateCount, Is.EqualTo(1));
            Assert.That(owner.Raw.DeleteCount, Is.Zero);
            Assert.That(owner.Raw.DisposeCount, Is.Zero);
            Assert.That(owner.Raw.Find(owner.Root.NodeId), Is.SameAs(owner.Root));
            Assert.That(owner.Raw.Find(owner.Value.NodeId), Is.SameAs(owner.Value));
            Assert.That(owner.Value.Value, Is.EqualTo(BorrowedOwnershipRawNodeManager.InitialValue));
        }

        private static void AssertBorrowedOwnershipPriorBinding(BorrowedOwnershipLiveResource owner)
        {
            Assert.That(owner.Source.EventMonitoredItems.TryGetValue(
                owner.PriorItem.Id, out IEventMonitoredItem? current), Is.True);
            Assert.That(current, Is.SameAs(owner.PriorItem));
            Assert.That(owner.Root.OnReportEventAsync?.Target, Is.SameAs(owner.Source));
        }

        private sealed class BorrowedOwnershipLiveResource(
            string namespaceUri,
            IServerInternal server,
            BorrowedOwnershipRawNodeManager raw,
            BaseObjectState root,
            BaseVariableState value,
            MonitoredNode2 source,
            IEventMonitoredItem priorItem,
            ConcurrentQueue<IFilterTarget> priorEvents) : IAsyncDisposable
        {
            public string NamespaceUri { get; } = namespaceUri;
            public IServerInternal Server { get; } = server;
            public BorrowedOwnershipRawNodeManager Raw { get; } = raw;
            public BaseObjectState Root { get; } = root;
            public BaseVariableState Value { get; } = value;
            public MonitoredNode2 Source { get; } = source;
            public IEventMonitoredItem PriorItem { get; } = priorItem;
            public ConcurrentQueue<IFilterTarget> PriorEvents { get; } = priorEvents;

            public async ValueTask DisposeAsync()
            {
                try
                {
                    await Source.DisposeAsync().ConfigureAwait(false);
                }
                finally
                {
                    try
                    {
                        await Raw.DeleteAddressSpaceAsync().ConfigureAwait(false);
                    }
                    finally
                    {
                        await Raw.DisposeAsync().ConfigureAwait(false);
                    }
                }
            }
        }

        /// <summary>
        /// A real live-server-backed manager with an admitted legacy source before facade publication.
        /// The raw owner is not independently registered in the routing image. Only the event authorization
        /// callback is controlled by the test.
        /// </summary>
        private sealed class BorrowedOwnershipRawNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            Microsoft.Extensions.Logging.ILogger logger,
            string namespaceUri)
            : NodeManagementLifecycleNodeManager(server, configuration, logger, namespaceUri, InitialValue)
        {
            public const int InitialValue = 311;

            public Func<CancellationToken, ValueTask>? EventAuthorizationBarrier { get; set; }

            public int CreateCount => Volatile.Read(ref m_createCount);

            public int DeleteCount => Volatile.Read(ref m_deleteCount);

            public int DisposeCount => Volatile.Read(ref m_disposeCount);

            public override ValueTask CreateAddressSpaceAsync(
                IDictionary<NodeId, IList<IReference>> externalReferences,
                CancellationToken cancellationToken = default)
            {
                Interlocked.Increment(ref m_createCount);
                return base.CreateAddressSpaceAsync(externalReferences, cancellationToken);
            }

            public override async ValueTask<ServiceResult> ValidateRolePermissionsAsync(
                OperationContext operationContext,
                NodeId nodeId,
                PermissionType requestedPermission,
                CancellationToken cancellationToken = default)
            {
                if (requestedPermission == PermissionType.ReceiveEvents && EventAuthorizationBarrier is { } barrier)
                {
                    await barrier(cancellationToken).ConfigureAwait(false);
                }
                return ServiceResult.Good;
            }

            public override async ValueTask DeleteAddressSpaceAsync(CancellationToken cancellationToken = default)
            {
                Interlocked.Increment(ref m_deleteCount);
                await base.DeleteAddressSpaceAsync(cancellationToken).ConfigureAwait(false);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    Interlocked.Increment(ref m_disposeCount);
                }
                base.Dispose(disposing);
            }

            private int m_createCount;
            private int m_deleteCount;
            private int m_disposeCount;
        }

        /// <summary>
        /// A custom facade exposes the real owner through the known synchronous adapter, while preparation
        /// mutates only a private candidate marker.
        /// </summary>
        private sealed class BorrowedOwnershipCandidate
        {
            public BorrowedOwnershipCandidate(BorrowedOwnershipLiveResource owner)
            {
                Facade = CreateLifecycleNodeManager(owner.NamespaceUri);
                AsyncDisposable = Facade.As<IAsyncDisposable>();
                Disposable = Facade.As<IDisposable>();
                Mock<INodeManagerReloadParticipant> reload = Facade.As<INodeManagerReloadParticipant>();
                reload.Setup(value => value.PrepareReloadAsync(
                        It.IsAny<IAsyncNodeManager>(), It.IsAny<CancellationToken>()))
                    .Returns(new ValueTask<ArrayOf<LocalReference>>([]));
                Facade.SetupGet(value => value.SyncNodeManager).Returns(new SyncNodeManagerAdapter(owner.Raw));
                Facade.Setup(value => value.GetManagerHandleAsync(It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
                    .Returns((NodeId nodeId, CancellationToken token) =>
                        owner.Raw.GetManagerHandleAsync(nodeId, token));
                Facade.Setup(value => value.GetNodeMetadataAsync(
                        It.IsAny<OperationContext>(), It.IsAny<object>(), It.IsAny<BrowseResultMask>(),
                        It.IsAny<CancellationToken>()))
                    .Returns((OperationContext context, object handle, BrowseResultMask mask, CancellationToken token) =>
                        owner.Raw.GetNodeMetadataAsync(context, handle, mask, token));
                NodeManager = Facade.Object;
                Lease = new BorrowedOwnershipLease(this);
                Facade.Setup(value => value.CreateAddressSpaceAsync(
                        It.IsAny<IDictionary<NodeId, IList<IReference>>>(), It.IsAny<CancellationToken>()))
                    .Returns((IDictionary<NodeId, IList<IReference>> _, CancellationToken _) => Prepare());
            }

            public Mock<IAsyncNodeManager> Facade { get; }
            public Mock<IAsyncDisposable> AsyncDisposable { get; }
            public Mock<IDisposable> Disposable { get; }
            public IAsyncNodeManager NodeManager { get; }
            public BorrowedOwnershipLease Lease { get; }
            public Exception? PreparationFailure { get; set; }
            public int CreateCount => Volatile.Read(ref m_createCount);
            public int PreparedState => Volatile.Read(ref m_preparedState);

            public void ReleasePrivateState()
            {
                Volatile.Write(ref m_preparedState, 0);
            }

            private ValueTask Prepare()
            {
                Interlocked.Increment(ref m_createCount);
                Volatile.Write(ref m_preparedState, 1);
                return PreparationFailure is { } failure
                    ? new ValueTask(Task.FromException(failure))
                    : default;
            }

            private int m_createCount;
            private int m_preparedState;
        }

        private sealed class BorrowedOwnershipFactory(
            string namespaceUri,
            BorrowedOwnershipLease lease) : INodeManagerBorrowedResourceFactory
        {
            public ArrayOf<string> NamespacesUris { get; } = [namespaceUri];
            public int BorrowedCreateCount => Volatile.Read(ref m_borrowedCreateCount);
            public int LegacyCreateCount => Volatile.Read(ref m_legacyCreateCount);

            public ValueTask CreateBorrowedAsync(
                INodeManagerBorrowedResourcePreparation preparation,
                ApplicationConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Interlocked.Increment(ref m_borrowedCreateCount);
                preparation.DeclareCandidate(lease);
                return default;
            }

            public ValueTask<IAsyncNodeManager> CreateAsync(
                IServerInternal server,
                ApplicationConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                Interlocked.Increment(ref m_legacyCreateCount);
                return new ValueTask<IAsyncNodeManager>(Task.FromException<IAsyncNodeManager>(
                    new InvalidOperationException("The opted-in factory must use CreateBorrowedAsync.")));
            }

            private int m_borrowedCreateCount;
            private int m_legacyCreateCount;
        }

        private sealed class BorrowedOwnershipLease(BorrowedOwnershipCandidate candidate)
            : INodeManagerBorrowedResourceLease
        {
            public IAsyncNodeManager NodeManager => candidate.NodeManager;
            public Exception? RollbackFailure { get; set; }
            public Exception? FirstRetirementFailure { get; set; }
            public NodeManagerGeneration? ObserveRawGenerationAtRetire { get; set; }
            public bool BlockRetirementRetry { get; set; }
            public TaskCompletionSource<bool> RetryEntered { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            public TaskCompletionSource<bool> ReleaseRetry { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            public int RollbackCount => Volatile.Read(ref m_rollbackCount);
            public int RollbackObservedState => Volatile.Read(ref m_rollbackObservedState);
            public int RetireCount => Volatile.Read(ref m_retireCount);
            public long RawAdmissionsObservedAtRetire => Interlocked.Read(ref m_rawAdmissionsObservedAtRetire);

            public ValueTask RollbackAsync(CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Interlocked.Increment(ref m_rollbackCount);
                Volatile.Write(ref m_rollbackObservedState, candidate.PreparedState);
                if (RollbackFailure is { } failure)
                {
                    return new ValueTask(Task.FromException(failure));
                }
                candidate.ReleasePrivateState();
                return default;
            }

            public async ValueTask RetireAsync(CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int attempt = Interlocked.Increment(ref m_retireCount);
                if (ObserveRawGenerationAtRetire is { } rawGeneration)
                {
                    Interlocked.Exchange(ref m_rawAdmissionsObservedAtRetire, rawGeneration.Gate.ActiveCount);
                }
                if (attempt == 1 && FirstRetirementFailure is { } failure)
                {
                    throw failure;
                }
                if (attempt == 2 && BlockRetirementRetry)
                {
                    RetryEntered.TrySetResult(true);
                    await ReleaseRetry.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                candidate.ReleasePrivateState();
            }

            private int m_rollbackCount;
            private int m_rollbackObservedState = -1;
            private int m_retireCount;
            private long m_rawAdmissionsObservedAtRetire = -1;
        }
    }
}
