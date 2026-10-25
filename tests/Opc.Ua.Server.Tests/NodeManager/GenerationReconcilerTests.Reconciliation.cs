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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    public sealed partial class GenerationReconcilerTests
    {
        /// <summary>
        /// Successful application publication can itself publish a newer view without becoming a failure.
        /// </summary>
        [Test]
        public async Task SuccessfulApplicationCallbackChangingRoutingIsNotSupersededAsync()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                var host = new Mock<IMasterNodeManager>();
                NodeManagerRoutingTable.RoutingSnapshot view = NodeManagerRoutingTable.RoutingSnapshot.Empty;
                view.SetRevision(1);
                host.As<IDynamicNodeManagerBatchHost>().SetupGet(value => value.RoutingRevision).Returns(() => view);
                server.SetupGet(value => value.NodeManager).Returns(host.Object);
                var reconciler = new GenerationReconciler(server.Object);
                GenerationReconciler.ReconciliationWork work = reconciler.CreateReconciliation(() =>
                    throw new AssertionException("Successful callbacks must not be retried."));
                work.Publish(1);
                int callbacks = 0;
                await work.RunAsync(NodeManagerReconciliationStage.ApplicationState, [], () =>
                {
                    callbacks++;
                    view = NodeManagerRoutingTable.RoutingSnapshot.Empty;
                    view.SetRevision(2);
                    reconciler.SupersedeReconciliations();
                    return default;
                }, requireOriginalView: true).ConfigureAwait(false);
                work.Seal();
                await work.Ticket.RetryAsync(CancellationToken.None).ConfigureAwait(false);
                Assert.That(work.Ticket.Issues.Count, Is.Zero);
                Assert.That(callbacks, Is.EqualTo(1));
                Assert.That(view.Revision, Is.EqualTo(2));
            }
        }

        /// <summary>
        /// Unrelated view changes retain global removal notifications but invalidate stale application retries.
        /// </summary>
        [TestCase(NodeManagerReconciliationStage.Identity)]
        [TestCase(NodeManagerReconciliationStage.Namespaces)]
        [TestCase(NodeManagerReconciliationStage.ModelChange)]
        [TestCase(NodeManagerReconciliationStage.SemanticChange)]
        public async Task GlobalReconciliationSurvivesUnrelatedPublicationAsync(NodeManagerReconciliationStage stage)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                var host = new Mock<IMasterNodeManager>();
                NodeManagerRoutingTable.RoutingSnapshot view = NodeManagerRoutingTable.RoutingSnapshot.Empty;
                view.SetRevision(1);
                host.As<IDynamicNodeManagerBatchHost>().SetupGet(value => value.RoutingRevision).Returns(() => view);
                server.SetupGet(value => value.NodeManager).Returns(host.Object);
                var reconciler = new GenerationReconciler(server.Object);
                var operation = new Mock<IDisposable>();
                GenerationReconciler.ReconciliationWork work =
                    reconciler.CreateReconciliation(() => operation.Object);
                work.Publish(1);
                int applicationCallbacks = 0;
                var original = new InvalidOperationException("Application publication failed.");
                await work.RunAsync(NodeManagerReconciliationStage.ApplicationState, [], () =>
                {
                    applicationCallbacks++;
                    throw original;
                }, requireOriginalView: true).ConfigureAwait(false);
                int notifications = 0;
                await work.RunAsync(stage, [], () =>
                {
                    notifications++;
                    view = NodeManagerRoutingTable.RoutingSnapshot.Empty;
                    view.SetRevision(2);
                    reconciler.SupersedeReconciliations();
                    if (notifications == 1)
                    {
                        throw new InvalidOperationException("Notification must be retried.");
                    }
                    return default;
                }).ConfigureAwait(false);
                Assert.That(work.Ticket.Issues.Count, Is.EqualTo(2));
                Assert.That(work.Ticket.Issues[0].IsSuperseded, Is.True);
                Assert.That(work.Ticket.Issues[0].InitialFailure, Is.SameAs(original));
                Assert.That(work.Ticket.Issues[1].IsSuperseded, Is.False);
                Assert.That(work.Ticket.Issues[1].IsRetryable, Is.True);
                int laterNotifications = 0;
                await work.RunAsync(stage, [], () =>
                {
                    laterNotifications++;
                    return default;
                }).ConfigureAwait(false);
                work.Seal();
                await work.Ticket.RetryAsync(CancellationToken.None).ConfigureAwait(false);
                await work.Ticket.RetryAsync(CancellationToken.None).ConfigureAwait(false);
                Assert.That(applicationCallbacks, Is.EqualTo(1));
                Assert.That(notifications, Is.EqualTo(2));
                Assert.That(laterNotifications, Is.EqualTo(1));
                Assert.That(work.Ticket.Issues.Count, Is.EqualTo(1));
                Assert.That(work.Ticket.Issues[0].IsSuperseded, Is.True);
                Assert.That(work.Ticket.Issues[0].IsRetryable, Is.False);
                Assert.That(work.Ticket.Issues[0].InitialFailure, Is.SameAs(original));
                operation.Verify(value => value.Dispose(), Times.Once);
            }
        }

        /// <summary>
        /// Retirement preserves an admitted successful retry but supersedes a retry that still fails.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ReconciliationRetryRetiringDuringAdmittedCallbackPreservesOutcomeAsync(bool failRetry)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                var reconciler = new GenerationReconciler(server.Object);
                var generation = new NodeManagerGeneration(
                    new NodeManagerSlot(), 1, Mock.Of<IAsyncNodeManager>(), NodeManagerGenerationState.Live);
                var operation = new Mock<IDisposable>();
                GenerationReconciler.ReconciliationWork work =
                    reconciler.CreateReconciliation(() => operation.Object);
                work.Publish(1);
                var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var original = new InvalidOperationException("Initial readiness failure.");
                var retryFailure = new InvalidOperationException("Admitted retry failed.");
                int attempts = 0;
                int successfulCallbacks = 0;
                await work.RunAsync(NodeManagerReconciliationStage.Readiness, [generation], async () =>
                {
                    if (++attempts == 1)
                    {
                        throw original;
                    }
                    entered.TrySetResult(true);
                    await release.Task.ConfigureAwait(false);
                    if (failRetry)
                    {
                        throw retryFailure;
                    }
                    successfulCallbacks++;
                }).ConfigureAwait(false);
                work.Seal();
                var result = new NodeManagerBatchResult(
                    [], 0, original, 1, [], Task.CompletedTask, work.Ticket);
                Task retry = result.RetryReconciliationAsync().AsTask();
                try
                {
                    await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                    generation.Gate.Retire();
                    reconciler.SupersedeReconciliations();
                    Assert.That(result.Issues[0].CurrentFailure, Is.SameAs(original));
                    Assert.That(result.Issues[0].IsSuperseded, Is.False);
                }
                finally
                {
                    release.TrySetResult(true);
                    await retry.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                }
                await result.RetryReconciliationAsync().ConfigureAwait(false);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(attempts, Is.EqualTo(2));
                    Assert.That(successfulCallbacks, Is.EqualTo(failRetry ? 0 : 1));
                    Assert.That(result.CleanupFailure, Is.SameAs(original));
                    Assert.That(result.Revision, Is.EqualTo(1));
                    Assert.That(result.Issues.Count, Is.EqualTo(failRetry ? 1 : 0));
                }
                if (failRetry)
                {
                    using (Assert.EnterMultipleScope())
                    {
                        Assert.That(result.Issues[0].InitialFailure, Is.SameAs(original));
                        Assert.That(result.Issues[0].IsSuperseded, Is.True);
                        Assert.That(result.Issues[0].IsRetryable, Is.False);
                    }
                }
                operation.Verify(value => value.Dispose(), Times.Once);
            }
        }

        /// <summary>
        /// Independently owned cleanup remains an explicit issue rather than a success-shaped retry.
        /// </summary>
        [Test]
        public async Task ReconciliationRetryPreservesIndependentlyOwnedCleanupIssueAsync()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                var reconciler = new GenerationReconciler(server.Object);
                GenerationReconciler.ReconciliationWork work = reconciler.CreateReconciliation(() =>
                    throw new AssertionException("No callback work should be admitted."));
                work.Publish(1);
                var original = new InvalidOperationException("Independent retirement failed.");
                work.ReportOwnedFailure(NodeManagerReconciliationStage.Retirement, original);
                work.Seal();
                await work.Ticket.RetryAsync(CancellationToken.None).ConfigureAwait(false);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(work.Ticket.Issues.Count, Is.EqualTo(1));
                    Assert.That(work.Ticket.Issues[0].IsRetryable, Is.False);
                    Assert.That(work.Ticket.Issues[0].IsSuperseded, Is.False);
                    Assert.That(work.Ticket.Issues[0].InitialFailure, Is.SameAs(original));
                    Assert.That(work.Ticket.Issues[0].CurrentFailure, Is.SameAs(original));
                }
            }
        }

        /// <summary>
        /// Failed session binding retries retain earlier successful session records, including callback cancellation.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ReconciliationRetryRetainsSuccessfulSessionBindingsAsync(bool cancel)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            using (var events = new EventManager(server.Object, 100, 100))
            {
                server.SetupGet(value => value.EventManager).Returns(events);
                var first = new Mock<ISession>();
                var second = new Mock<ISession>();
                first.SetupGet(value => value.Id).Returns(new NodeId("First", 2));
                second.SetupGet(value => value.Id).Returns(new NodeId("Second", 2));
                first.SetupGet(value => value.Activated).Returns(true);
                second.SetupGet(value => value.Activated).Returns(true);
                first.SetupGet(value => value.EffectiveIdentity).Returns(new UserIdentity());
                second.SetupGet(value => value.EffectiveIdentity).Returns(new UserIdentity());
                var sessions = new Mock<ISessionManager>();
                sessions.Setup(value => value.GetSessions()).Returns([first.Object, second.Object]);
                server.SetupGet(value => value.SessionManager).Returns(sessions.Object);
                var reconciler = new GenerationReconciler(server.Object);
                var manager = new Mock<IAsyncNodeManager>();
                var generation = new NodeManagerGeneration(
                    new NodeManagerSlot(), 1, manager.Object, NodeManagerGenerationState.Live);
                bool reject = true;
                Exception original = cancel
                    ? new OperationCanceledException("The participant canceled its own binding.")
                    : new InvalidOperationException("The participant rejected its binding.");
                manager.Setup(value => value.SessionActivatedAsync(
                    It.IsAny<OperationContext>(), second.Object.Id, It.IsAny<CancellationToken>()))
                    .Returns(() => reject ? new ValueTask(Task.FromException(original)) : default);
                GenerationReconciler.ReconciliationWork work =
                    reconciler.CreateReconciliation(() => Mock.Of<IDisposable>());
                work.Publish(1);
                await work.RunAsync(NodeManagerReconciliationStage.Bindings, [generation], async () =>
                {
                    GenerationReconciler.Bindings bindings =
                        await reconciler.BindAsync(generation, CancellationToken.None).ConfigureAwait(false);
                    await reconciler.ReconcileAsync(generation, bindings, CancellationToken.None).ConfigureAwait(false);
                }).ConfigureAwait(false);
                work.Seal();
                Assert.That(work.Ticket.Issues[0].InitialFailure, Is.SameAs(original));
                Assert.That(generation.SessionBindings[first.Object.Id].Binding, Is.Not.Null);
                reject = false;
                await work.Ticket.RetryAsync(CancellationToken.None).ConfigureAwait(false);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(work.Ticket.Issues.Count, Is.Zero);
                    Assert.That(generation.SessionBindings, Has.Count.EqualTo(2));
                }
                manager.Verify(value => value.SessionActivatedAsync(
                    It.IsAny<OperationContext>(), first.Object.Id, It.IsAny<CancellationToken>()), Times.Once);
                manager.Verify(value => value.SessionActivatedAsync(
                    It.IsAny<OperationContext>(), second.Object.Id, It.IsAny<CancellationToken>()), Times.Exactly(2));
                manager.Verify(value => value.SessionClosingAsync(
                    It.IsAny<OperationContext>(), first.Object.Id, false, It.IsAny<CancellationToken>()), Times.Never);
            }
        }

        /// <summary>
        /// Successful model event delivery is not replayed when a later semantic event fails.
        /// </summary>
        [Test]
        public async Task ReconciliationCheckpointsKeepSuccessfulEventStagesAcrossRetryAsync()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                var reconciler = new GenerationReconciler(server.Object);
                var generation = new NodeManagerGeneration(
                    new NodeManagerSlot(), 1, Mock.Of<IAsyncNodeManager>(), NodeManagerGenerationState.Live);
                var operation = new Mock<IDisposable>();
                int admissions = 0;
                GenerationReconciler.ReconciliationWork work = reconciler.CreateReconciliation(() =>
                {
                    admissions++;
                    return operation.Object;
                });
                work.Publish(1);
                int modelEvents = 0;
                int semanticEvents = 0;
                bool reject = true;
                await work.RunAsync(NodeManagerReconciliationStage.ModelChange, [generation], () =>
                {
                    modelEvents++;
                    return default;
                }).ConfigureAwait(false);
                var original = new InvalidOperationException("Semantic event delivery failed.");
                await work.RunAsync(NodeManagerReconciliationStage.SemanticChange, [generation], () =>
                {
                    semanticEvents++;
                    return reject ? new ValueTask(Task.FromException(original)) : default;
                }).ConfigureAwait(false);
                work.Seal();
                Assert.That(work.Ticket.Issues[0].InitialFailure, Is.SameAs(original));
                reject = false;
                await work.Ticket.RetryAsync(CancellationToken.None).ConfigureAwait(false);
                await work.Ticket.RetryAsync(CancellationToken.None).ConfigureAwait(false);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(work.Ticket.Issues.Count, Is.Zero);
                    Assert.That(modelEvents, Is.EqualTo(1));
                    Assert.That(semanticEvents, Is.EqualTo(2));
                    Assert.That(admissions, Is.EqualTo(1));
                }
                operation.Verify(value => value.Dispose(), Times.Once);
            }
        }

        /// <summary>
        /// A disposed lifecycle owner produces an explicit terminal issue without invoking the callback.
        /// </summary>
        [Test]
        public async Task ReconciliationRetryReportsUnavailableLifecycleWithoutInvokingCallbackAsync()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                var reconciler = new GenerationReconciler(server.Object);
                var generation = new NodeManagerGeneration(
                    new NodeManagerSlot(), 1, Mock.Of<IAsyncNodeManager>(), NodeManagerGenerationState.Live);
                var unavailable = new ObjectDisposedException("Lifecycle");
                GenerationReconciler.ReconciliationWork work = reconciler.CreateReconciliation(() => throw unavailable);
                work.Publish(1);
                int callbacks = 0;
                var original = new InvalidOperationException("Initial readiness failure.");
                await work.RunAsync(NodeManagerReconciliationStage.Readiness, [generation], () =>
                {
                    callbacks++;
                    throw original;
                }).ConfigureAwait(false);
                work.Seal();
                await work.Ticket.RetryAsync(CancellationToken.None).ConfigureAwait(false);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(callbacks, Is.EqualTo(1));
                    Assert.That(work.Ticket.Issues[0].IsSuperseded, Is.True);
                    Assert.That(work.Ticket.Issues[0].InitialFailure, Is.SameAs(original));
                    Assert.That(work.Ticket.Issues[0].CurrentFailure, Is.SameAs(unavailable));
                }
            }
        }

        /// <summary>
        /// Publication admission conflicts retain retryable work and do not execute a callback without ownership.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ReconciliationRetryRetainsPendingWorkAfterAdmissionConflictAsync(bool invalidContext)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                var reconciler = new GenerationReconciler(server.Object);
                var generation = new NodeManagerGeneration(
                    new NodeManagerSlot(), 1, Mock.Of<IAsyncNodeManager>(), NodeManagerGenerationState.Live);
                var operation = new Mock<IDisposable>();
                bool busy = true;
                Exception admissionFailure = invalidContext
                    ? new InvalidOperationException("A durable decision callback cannot admit lifecycle work.")
                    : new ServiceResultException(StatusCodes.BadServerTooBusy);
                GenerationReconciler.ReconciliationWork work = reconciler.CreateReconciliation(() => busy
                    ? throw admissionFailure
                    : operation.Object);
                work.Publish(1);
                int callbacks = 0;
                var original = new InvalidOperationException("Initial failure.");
                await work.RunAsync(NodeManagerReconciliationStage.Readiness, [generation], () =>
                {
                    callbacks++;
                    return busy ? new ValueTask(Task.FromException(original)) : default;
                }).ConfigureAwait(false);
                work.Seal();
                await work.Ticket.RetryAsync(CancellationToken.None).ConfigureAwait(false);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(callbacks, Is.EqualTo(1));
                    Assert.That(work.Ticket.Issues[0].IsSuperseded, Is.False);
                    Assert.That(work.Ticket.Issues[0].InitialFailure, Is.SameAs(original));
                    Assert.That(work.Ticket.Issues[0].CurrentFailure, Is.SameAs(admissionFailure));
                }
                busy = false;
                await work.Ticket.RetryAsync(CancellationToken.None).ConfigureAwait(false);
                Assert.That(callbacks, Is.EqualTo(2));
                Assert.That(work.Ticket.Issues.Count, Is.Zero);
                operation.Verify(value => value.Dispose(), Times.Once);
            }
        }
    }
}
