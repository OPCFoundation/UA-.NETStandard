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
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
 * THE SOFTWARE.
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.RuntimeNodeSet;
using Opc.Ua.Server.TestFramework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    public sealed partial class NodeManagerLifecycleTests
    {
        /// <summary>
        /// Readiness retries preserve original failure provenance and the original committed identity.
        /// </summary>
        [Test]
        public async Task RetryReadinessPreservesDecisionRevisionSlotsAndInitialFailureAsync()
        {
            var original = new InvalidOperationException("Initial readiness failure.");
            var subsequent = new InvalidOperationException("Second readiness failure.");
            Exception? failure = original;
            ReadinessLifecycleNodeManager? participant = null;
            int decisions = 0;
            int publications = 0;
            await using IPreparedNodeManagerBatch prepared =
                await m_server.NodeManagerLifecycle.PrepareAsync(
                    [NodeManagerBatchChange.Add(CreateRetryReadinessFactory(manager =>
                    {
                        participant = manager;
                        manager.ReadinessCallback = _ => failure is null
                            ? default
                            : new ValueTask(Task.FromException(failure));
                    }))]).ConfigureAwait(false);
            NodeManagerBatchResult result = await prepared.CommitAsync(
                _ =>
                {
                    decisions++;
                    return default;
                },
                () => publications++).ConfigureAwait(false);
            await prepared.DisposeAsync().ConfigureAwait(false);
            Exception? initialCleanupFailure = result.CleanupFailure;
            long revision = result.Revision;
            NodeManagerSlot[] slots = result.Slots.ToArray() ?? [];
            Task retirement = result.Retirement;
            Assert.That((result.Issues.ToArray() ?? []).Single().InitialFailure, Is.SameAs(original));
            using (var canceled = new CancellationTokenSource())
            {
                canceled.Cancel();
                await Assert.ThatAsync(
                    async () => await result.RetryReconciliationAsync(canceled.Token).ConfigureAwait(false),
                    Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
                Assert.That(RequireLifecycleValue(participant).ReadinessCount, Is.EqualTo(1));
            }
            failure = subsequent;
            await result.RetryReconciliationAsync().ConfigureAwait(false);
            using (Assert.EnterMultipleScope())
            {
                Assert.That((result.Issues.ToArray() ?? []).Single().InitialFailure, Is.SameAs(original));
                Assert.That((result.Issues.ToArray() ?? []).Single().CurrentFailure, Is.SameAs(subsequent));
                Assert.That((result.Issues.ToArray() ?? []).Single().Stage,
                    Is.EqualTo(NodeManagerReconciliationStage.Readiness));
                Assert.That((result.Issues.ToArray() ?? []).Single().IsSuperseded, Is.False);
            }
            failure = null;
            await result.RetryReconciliationAsync().ConfigureAwait(false);
            await result.RetryReconciliationAsync().ConfigureAwait(false);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result.Issues.Count, Is.Zero);
                Assert.That(result.CleanupFailure, Is.SameAs(initialCleanupFailure));
                Assert.That(((AggregateException)result.CleanupFailure!).Flatten().InnerExceptions,
                    Does.Contain(original));
                Assert.That(RequireLifecycleValue(participant).ReadinessCount, Is.EqualTo(3));
                Assert.That(decisions, Is.EqualTo(1));
                Assert.That(publications, Is.EqualTo(1));
                Assert.That(result.Revision, Is.EqualTo(revision));
                Assert.That(m_server.NodeManagerLifecycle.Revision, Is.EqualTo(revision));
                Assert.That(result.Slots.ToArray(), Is.EqualTo(slots));
                Assert.That(result.Retirement, Is.SameAs(retirement));
            }
        }

        /// <summary>
        /// Concurrent retry callers share one callback; canceling a waiter cannot cancel admitted work.
        /// </summary>
        [Test]
        public async Task ConcurrentReconciliationRetriesJoinAndCancellationOnlyStopsWaitingAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var canceledWaiter = new CancellationTokenSource();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            ReadinessLifecycleNodeManager? participant = null;
            NodeManagerRegistration? child = null;
            bool fail = true;
            await using IPreparedNodeManagerBatch prepared =
                await m_server.NodeManagerLifecycle.PrepareAsync(
                    [NodeManagerBatchChange.Add(CreateRetryReadinessFactory(manager =>
                    {
                        participant = manager;
                        manager.ReadinessCallback = async ct =>
                        {
                            Assert.That(ct.CanBeCanceled, Is.False);
                            if (fail)
                            {
                                throw new InvalidOperationException("Initial readiness failure.");
                            }
                            child = await m_server.NodeManagerLifecycle.AddAsync(
                                new CountingNodeManagerFactory(
                                    [kSecondModelNamespaceUri],
                                    CreateTrackingNodeManagementFactory(
                                        kSecondRegistrationValue, _ => { }, kSecondModelNamespaceUri).CreateAsync),
                                null,
                                ct).ConfigureAwait(false);
                            entered.TrySetResult(true);
                            await release.Task.ConfigureAwait(false);
                        };
                    }))], timeout.Token).ConfigureAwait(false);
            NodeManagerBatchResult result = await prepared.CommitAsync(static _ => default, timeout.Token)
                .ConfigureAwait(false);
            fail = false;
            Task first = result.RetryReconciliationAsync(canceledWaiter.Token).AsTask();
            await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
            Task second = result.RetryReconciliationAsync(timeout.Token).AsTask();
            try
            {
                canceledWaiter.Cancel();
                await Assert.ThatAsync(async () => await first.ConfigureAwait(false),
                    Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
                Assert.That(second.IsCompleted, Is.False);
                Assert.That(RequireLifecycleValue(participant).ReadinessCount, Is.EqualTo(2));
                Assert.That(child, Is.Not.Null, "Retry callbacks must be free to admit a nested writer.");
            }
            finally
            {
                release.TrySetResult(true);
                await second.WaitAsync(timeout.Token).ConfigureAwait(false);
            }
            Assert.That(result.Issues.Count, Is.Zero);
            Assert.That(RequireLifecycleValue(participant).ReadinessCount, Is.EqualTo(2));
        }

        /// <summary>
        /// A newer removal or replacement invalidates retry ownership without invoking a disposed manager.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task SupersededReconciliationDoesNotInvokeOldGenerationAsync(bool replace)
        {
            ReadinessLifecycleNodeManager? participant = null;
            await using IPreparedNodeManagerBatch prepared =
                await m_server.NodeManagerLifecycle.PrepareAsync(
                    [NodeManagerBatchChange.Add(CreateRetryReadinessFactory(manager =>
                    {
                        participant = manager;
                        manager.ReadinessCallback = _ =>
                            new ValueTask(Task.FromException(new InvalidOperationException("Readiness failed.")));
                    }))]).ConfigureAwait(false);
            NodeManagerBatchResult result = await prepared.CommitAsync(static _ => default).ConfigureAwait(false);
            NodeManagerBatchChange change = replace
                ? NodeManagerBatchChange.Replace(result.Registrations[0],
                    CreateTrackingNodeManagementFactory(kGeneration2Value, _ => { }))
                : NodeManagerBatchChange.Remove(result.Registrations[0]);
            await using IPreparedNodeManagerBatch next =
                await m_server.NodeManagerLifecycle.PrepareAsync([change]).ConfigureAwait(false);
            NodeManagerBatchResult newer = await next.CommitAsync(static _ => default).ConfigureAwait(false);
            await newer.Retirement.ConfigureAwait(false);
            await result.RetryReconciliationAsync().ConfigureAwait(false);
            using (Assert.EnterMultipleScope())
            {
                Assert.That((result.Issues.ToArray() ?? []).Single().IsSuperseded, Is.True);
                Assert.That((result.Issues.ToArray() ?? []).Single().Stage,
                    Is.EqualTo(NodeManagerReconciliationStage.Readiness));
                Assert.That(RequireLifecycleValue(participant).ReadinessCount, Is.EqualTo(1));
                Assert.That(RequireLifecycleValue(participant).DisposeCount, Is.EqualTo(1));
                Assert.That(m_server.NodeManagerLifecycle.Revision, Is.EqualTo(newer.Revision));
            }
        }

        /// <summary>
        /// A failed application publication may have applied state; only that idempotent callback is retried.
        /// </summary>
        [Test]
        public async Task PartialApplicationPublicationRetryDoesNotReplayReadinessAsync()
        {
            ReadinessLifecycleNodeManager? participant = null;
            bool applied = false;
            int publications = 0;
            await using IPreparedNodeManagerBatch prepared =
                await m_server.NodeManagerLifecycle.PrepareAsync(
                    [NodeManagerBatchChange.Add(CreateRetryReadinessFactory(manager => participant = manager))])
                    .ConfigureAwait(false);
            var original = new InvalidOperationException("Failure after partial application publication.");
            NodeManagerBatchResult result = await prepared.CommitAsync(static _ => default, () =>
            {
                applied = true;
                if (++publications == 1)
                {
                    throw original;
                }
            }).ConfigureAwait(false);
            Assert.That(applied, Is.True);
            Assert.That((result.Issues.ToArray() ?? []).Single().Stage,
                Is.EqualTo(NodeManagerReconciliationStage.ApplicationState));
            Assert.That((result.Issues.ToArray() ?? []).Single().InitialFailure, Is.SameAs(original));
            await result.RetryReconciliationAsync().ConfigureAwait(false);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result.Issues.Count, Is.Zero);
                Assert.That(publications, Is.EqualTo(2));
                Assert.That(RequireLifecycleValue(participant).ReadinessCount, Is.EqualTo(1));
            }
        }

        /// <summary>
        /// A stale application callback cannot overwrite a newer durable unit even when its manager remains live.
        /// </summary>
        [Test]
        public async Task PartialApplicationPublicationRetryCannotOverwriteNewerCommittedStateAsync()
        {
            int applicationState = 0;
            int publications = 0;
            await using IPreparedNodeManagerBatch prepared =
                await m_server.NodeManagerLifecycle.PrepareAsync(
                    [NodeManagerBatchChange.Add(CreateTrackingNodeManagementFactory(
                        kGeneration1Value, _ => { }))]).ConfigureAwait(false);
            NodeManagerBatchResult result = await prepared.CommitAsync(static _ => default, () =>
            {
                applicationState = 1;
                if (++publications == 1)
                {
                    throw new InvalidOperationException("Partial application publication.");
                }
            }).ConfigureAwait(false);
            await using IPreparedNodeManagerBatch next =
                await m_server.NodeManagerLifecycle.PrepareAsync(
                    [NodeManagerBatchChange.Add(CreateTrackingNodeManagementFactory(
                        kSecondRegistrationValue, _ => { }, kSecondModelNamespaceUri))]).ConfigureAwait(false);
            NodeManagerBatchResult newer = await next.CommitAsync(
                static _ => default, () => applicationState = 2).ConfigureAwait(false);
            await result.RetryReconciliationAsync().ConfigureAwait(false);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(applicationState, Is.EqualTo(2));
                Assert.That(publications, Is.EqualTo(1));
                Assert.That(result.Registrations[0].Identity.Gate.State, Is.EqualTo(NodeManagerGenerationState.Live));
                Assert.That(result.Issues.Count, Is.EqualTo(1));
                Assert.That(result.Issues[0].Stage, Is.EqualTo(NodeManagerReconciliationStage.ApplicationState));
                Assert.That(result.Issues[0].IsSuperseded, Is.True);
                Assert.That(result.Revision, Is.LessThan(newer.Revision));
            }
        }

        /// <summary>
        /// Retrying binding reconciliation retains successful all-events records rather than replaying callbacks.
        /// </summary>
        [Test]
        public async Task RetryBindingReconciliationPreservesSuccessfulCallbacksAsync()
        {
            var services = new ServerTestServices(m_server, m_secureChannelContext);
            (uint firstSubscription, _) = await CreateSubscriptionAndEventMonitoredItemAsync(
                services, ObjectIds.Server).ConfigureAwait(false);
            (uint secondSubscription, _) = await CreateSubscriptionAndEventMonitoredItemAsync(
                services, ObjectIds.Server).ConfigureAwait(false);
            var attempts = new Dictionary<uint, int>();
            TrackingLifecycleNodeManager? participant = null;
            uint successfulItem = 0;
            bool fail = true;
            try
            {
                await using IPreparedNodeManagerBatch prepared =
                    await m_server.NodeManagerLifecycle.PrepareAsync(
                        [NodeManagerBatchChange.Add(CreateTrackingNodeManagementFactory(
                            kGeneration1Value, manager =>
                            {
                                participant = manager;
                                manager.AllEventsCallback = (item, unsubscribe, _) =>
                                {
                                    if (!unsubscribe)
                                    {
                                        attempts.TryGetValue(item.Id, out int count);
                                        attempts[item.Id] = count + 1;
                                        if (successfulItem == 0)
                                        {
                                            successfulItem = item.Id;
                                        }
                                        else if (item.Id != successfulItem && fail)
                                        {
                                            throw new InvalidOperationException("Second all-events bind failed.");
                                        }
                                    }
                                    return default;
                                };
                            }))]).ConfigureAwait(false);
                NodeManagerBatchResult result = await prepared.CommitAsync(static _ => default).ConfigureAwait(false);
                Assert.That((result.Issues.ToArray() ?? []).Single().Stage,
                    Is.EqualTo(NodeManagerReconciliationStage.Bindings));
                fail = false;
                await result.RetryReconciliationAsync().ConfigureAwait(false);
                await result.RetryReconciliationAsync().ConfigureAwait(false);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(result.Issues.Count, Is.Zero);
                    Assert.That(attempts, Has.Count.EqualTo(2));
                    Assert.That(attempts[successfulItem], Is.EqualTo(1));
                    Assert.That(attempts.Values.Sum(), Is.EqualTo(3));
                }
            }
            finally
            {
                if (participant is not null)
                {
                    participant.AllEventsCallback = (_, _, _) => default;
                }
                await DeleteSubscriptionAsync(services, firstSubscription).ConfigureAwait(false);
                await DeleteSubscriptionAsync(services, secondSubscription).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Reconciliation retries do not wait for or replace independently draining retirement.
        /// </summary>
        [Test]
        public async Task ReconciliationRetryLeavesRetirementIndependentAsync()
        {
            NodeManagerRegistration original = await m_server.NodeManagerLifecycle.AddAsync(
                CreateTrackingNodeManagementFactory(kGeneration1Value, _ => { }), null).ConfigureAwait(false);
            Assert.That(original.Identity.Gate.TryEnter(out int admission), Is.True);
            bool fail = true;
            NodeManagerBatchResult result;
            try
            {
                await using IPreparedNodeManagerBatch prepared =
                    await m_server.NodeManagerLifecycle.PrepareAsync(
                        [NodeManagerBatchChange.Replace(original, CreateRetryReadinessFactory(manager =>
                        {
                            manager.ReadinessCallback = _ => fail
                                ? new ValueTask(Task.FromException(new InvalidOperationException("Readiness failed.")))
                                : default;
                        }))]).ConfigureAwait(false);
                result = await prepared.CommitAsync(static _ => default).ConfigureAwait(false);
                Task retirement = result.Retirement;
                Assert.That(retirement.IsCompleted, Is.False);
                fail = false;
                await result.RetryReconciliationAsync().ConfigureAwait(false);
                Assert.That(result.Issues.Count, Is.Zero);
                Assert.That(result.Retirement, Is.SameAs(retirement));
                Assert.That(retirement.IsCompleted, Is.False);
            }
            finally
            {
                original.Identity.Gate.Exit(admission);
            }
            await result.Retirement.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        }

        /// <summary>
        /// The host retains failed reference observer callbacks without replaying successful subscribers.
        /// </summary>
        [Test]
        public async Task RetryReferenceReconciliationPreservesSuccessfulObserversAsync()
        {
            NodeState objects = await GetObjectsReferenceOwnerAsync(CancellationToken.None).ConfigureAwait(false);
            NodeStateReferenceAdded? previous = objects.OnReferenceAdded;
            int successful = 0;
            int failed = 0;
            int later = 0;
            bool reject = true;
            var original = new InvalidOperationException("Reference observer failed.");
            ushort ns = m_server.CurrentInstance.NamespaceUris.GetIndexOrAppend(kModelNamespaceUri);
            var target = new NodeId(kRootNodeId, ns);
            objects.OnReferenceAdded += (_, _, _, id) =>
            {
                if (id == target)
                {
                    successful++;
                }
            };
            objects.OnReferenceAdded += (_, _, _, id) =>
            {
                if (id == target)
                {
                    failed++;
                    if (reject)
                    {
                        throw original;
                    }
                }
            };
            objects.OnReferenceAdded += (_, _, _, id) =>
            {
                if (id == target)
                {
                    later++;
                }
            };
            try
            {
                await using IPreparedNodeManagerBatch prepared =
                    await m_server.NodeManagerLifecycle.PrepareAsync(
                        [NodeManagerBatchChange.Add(new RuntimeNodeSetNodeManagerFactory(
                            CreateOptions(kModelNamespaceUri, kFirstRegistrationValue)))])
                        .ConfigureAwait(false);
                NodeManagerBatchResult result = await prepared.CommitAsync(static _ => default).ConfigureAwait(false);
                Assert.That((result.Issues.ToArray() ?? []).Single().Stage,
                    Is.EqualTo(NodeManagerReconciliationStage.References));
                Assert.That(((AggregateException)result.Issues[0].InitialFailure).Flatten().InnerExceptions,
                    Does.Contain(original));
                Assert.That(later, Is.EqualTo(1));
                reject = false;
                await result.RetryReconciliationAsync().ConfigureAwait(false);
                await result.RetryReconciliationAsync().ConfigureAwait(false);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(result.Issues.Count, Is.Zero);
                    Assert.That(successful, Is.EqualTo(1));
                    Assert.That(failed, Is.EqualTo(2));
                    Assert.That(later, Is.EqualTo(1));
                    Assert.That(objects.ReferenceExists(ReferenceTypeIds.Organizes, false, target), Is.True);
                }
            }
            finally
            {
                objects.OnReferenceAdded = previous;
            }
        }

        /// <summary>
        /// Creates a readiness participant using the existing lifecycle regression manager.
        /// </summary>
        private IAsyncNodeManagerFactory CreateRetryReadinessFactory(
            Action<ReadinessLifecycleNodeManager> configure)
        {
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory.Setup(value => value.CreateAsync(
                It.IsAny<IServerInternal>(), It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()))
                .Returns((IServerInternal server, ApplicationConfiguration configuration, CancellationToken _) =>
                {
                    var manager = new ReadinessLifecycleNodeManager(
                        server, configuration, m_logger, kGeneration1Value);
                    configure(manager);
                    return new ValueTask<IAsyncNodeManager>(manager);
                });
            return factory.Object;
        }
    }
}
