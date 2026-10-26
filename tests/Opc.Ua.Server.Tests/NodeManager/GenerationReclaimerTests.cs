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

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Verifies independent retirement ownership, exact drains, and staged cleanup retry without a lifecycle writer.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class GenerationReclaimerTests
    {
        /// <summary>
        /// Consumer disposal uses the existing gate and remains observable and retryable at retirement.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task RetiredConsumerDisposalUsesGenerationDrainAndRetriesFailureAsync(bool failFirst)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var reclaimer = new GenerationReclaimer();
            var context = new ReclamationContext();
            var retired = context.CreateRetirement();
            var completedResource = new Mock<IDisposable>();
            var resource = new Mock<IDisposable>();
            int attempts = 0;
            var failure = new InvalidOperationException("Consumer disposal failed.");
            resource.Setup(value => value.Dispose()).Callback(() =>
            {
                context.Host.Verify(value => value.DestroyAddressSpaceAsync(
                    retired.NodeManager, It.IsAny<CancellationToken>()), Times.Never);
                if (++attempts == 1 && failFirst)
                {
                    throw failure;
                }
            });

            Assert.That(retired.Generation.TryDeferDisposal(resource.Object), Is.False,
                "An ordinary live consumer keeps its existing immediate disposal behavior.");
            Assert.That(retired.Generation.Gate.TryEnter(out int admission), Is.True);
            retired.Generation.Gate.Retire();
            try
            {
                Assert.That(retired.Generation.TryDeferDisposal(completedResource.Object), Is.True);
                Assert.That(retired.Generation.TryDeferDisposal(resource.Object), Is.True);
                Assert.That(retired.Generation.TryDeferDisposal(resource.Object), Is.True,
                    "Repeated disposal must transfer ownership only once.");
                Assert.That(retired.Generation.DisposeRetiredResources, Throws.InvalidOperationException);
                completedResource.Verify(value => value.Dispose(), Times.Never);
                resource.Verify(value => value.Dispose(), Times.Never);
            }
            finally
            {
                retired.Generation.Gate.Exit(admission);
            }

            reclaimer.Enqueue([retired]);
            reclaimer.Activate([retired]);
            if (failFirst)
            {
                Exception cleanupFailure = await Assert.CatchAsync<Exception>(
                    async () => await reclaimer.ReclaimReadyAsync().ConfigureAwait(false));
                Exception retirementFailure = await Assert.CatchAsync<Exception>(
                    async () => await retired.Retirement.WaitAsync(timeout.Token).ConfigureAwait(false));
                Assert.That(cleanupFailure, Is.SameAs(failure));
                Assert.That(retirementFailure, Is.SameAs(failure));
                Assert.That(reclaimer.Contains(retired), Is.True);
            }
            await reclaimer.ReclaimReadyAsync().ConfigureAwait(false);
            if (!failFirst)
            {
                await retired.Retirement.WaitAsync(timeout.Token).ConfigureAwait(false);
            }
            Assert.That(retired.Retirement.IsFaulted, Is.EqualTo(failFirst),
                "The original retirement task retains its first failure even after successful cleanup retry.");
            Assert.That(retired.Generation.TryDeferDisposal(resource.Object), Is.False);
            completedResource.Verify(value => value.Dispose(), Times.Once);
            resource.Verify(value => value.Dispose(), Times.Exactly(failFirst ? 2 : 1));
            Assert.That(reclaimer.Contains(retired), Is.False);
        }

        /// <summary>
        /// An unrelated generation finishes while another generation is blocked in its drain or destruction callback.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task IndependentRetirementDoesNotWaitForUnrelatedDrainOrDestructionAsync(bool blockDrain)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var reclaimer = new GenerationReclaimer();
            var context = new ReclamationContext();
            var first = context.CreateRetirement();
            var second = context.CreateRetirement();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Assert.That(first.Generation.Gate.TryEnter(out int admission), Is.True);
            first.Generation.Gate.Retire();
            second.Generation.Gate.Retire();
            context.Host.Setup(value => value.DestroyAddressSpaceAsync(
                first.NodeManager, It.IsAny<CancellationToken>()))
                .Returns(async () =>
                {
                    entered.TrySetResult(true);
                    if (!blockDrain)
                    {
                        await release.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                    }
                });
            if (!blockDrain)
            {
                first.Generation.Gate.Exit(admission);
            }
            reclaimer.Enqueue([first, second]);
            reclaimer.Activate([first, second]);
            try
            {
                reclaimer.Schedule();
                await second.Retirement.WaitAsync(timeout.Token).ConfigureAwait(false);
                if (!blockDrain)
                {
                    await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                }
                Assert.That(first.Retirement.IsCompleted, Is.False);
                Assert.That(reclaimer.Contains(first), Is.True);
                Assert.That(reclaimer.Contains(second), Is.False);
                context.Host.Verify(value => value.DestroyAddressSpaceAsync(
                    second.NodeManager, It.IsAny<CancellationToken>()), Times.Once);
                if (blockDrain)
                {
                    context.Host.Verify(value => value.DestroyAddressSpaceAsync(
                        first.NodeManager, It.IsAny<CancellationToken>()), Times.Never);
                }
            }
            finally
            {
                if (blockDrain)
                {
                    first.Generation.Gate.Exit(admission);
                }
                release.TrySetResult(true);
                await reclaimer.CompleteShutdownAsync(timeout.Token).ConfigureAwait(false);
            }
            Assert.That(reclaimer.Count, Is.Zero);
        }

        /// <summary>
        /// Both the captured view epoch and the exact generation's admitted callback must drain before destruction.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task RetirementWaitsForExactEpochAndGenerationGateAsync(bool releaseEpochFirst)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var reclaimer = new GenerationReclaimer();
            var context = new ReclamationContext();
            var retired = context.CreateRetirement();
            var epoch = new AddressSpaceEpoch();
            Assert.That(epoch.TryPin(out int pin), Is.True);
            retired.Generation.RegisterViewEpoch(epoch);
            Assert.That(retired.Generation.Gate.TryEnter(out int admission), Is.True);
            epoch.Close();
            retired.Generation.Gate.Retire();
            reclaimer.Enqueue([retired]);
            reclaimer.Activate([retired]);
            reclaimer.Schedule();
            if (releaseEpochFirst)
            {
                epoch.Exit(pin);
            }
            else
            {
                retired.Generation.Gate.Exit(admission);
            }
            try
            {
                Assert.That(retired.Generation.IsQuiescent, Is.False);
                await reclaimer.ReclaimReadyAsync().ConfigureAwait(false);
                context.Host.Verify(value => value.DestroyAddressSpaceAsync(
                    retired.NodeManager, It.IsAny<CancellationToken>()), Times.Never);
                Assert.That(retired.Retirement.IsCompleted, Is.False);
            }
            finally
            {
                if (releaseEpochFirst)
                {
                    retired.Generation.Gate.Exit(admission);
                }
                else
                {
                    epoch.Exit(pin);
                }
                await reclaimer.CompleteShutdownAsync(timeout.Token).ConfigureAwait(false);
            }
            context.Host.Verify(value => value.DestroyAddressSpaceAsync(
                retired.NodeManager, It.IsAny<CancellationToken>()), Times.Once);
            Assert.That(retired.Retirement.Status, Is.EqualTo(TaskStatus.RanToCompletion));
        }

        /// <summary>
        /// A failed post-destruction stage is logged and faults retirement, while retry skips every successful stage.
        /// </summary>
        [TestCase("Notifications")]
        [TestCase("References")]
        [TestCase("Destruction")]
        [TestCase("DestroyedReferences")]
        [TestCase("Disposal")]
        public async Task CleanupFailureIsLoggedAndRetriedWithoutReplayingSuccessfulStagesAsync(string stage)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var reclaimer = new GenerationReclaimer();
            var context = new ReclamationContext();
            var retired = context.CreateRetirement();
            Mock<IAsyncDisposable> disposal = Mock.Get(retired.NodeManager).As<IAsyncDisposable>();
            int calls = 0;
            var failure = new InvalidOperationException(stage + " failed.");
            ValueTask FailOnce()
            {
                return ++calls == 1 ? new ValueTask(Task.FromException(failure)) : default;
            }
            Mock<IMasterNodeManager> master = Mock.Get(retired.Server.NodeManager);
            retired.PendingReferences.Add(new LocalReference(
                new NodeId(1), ReferenceTypeIds.HasComponent, false, new NodeId(2)));
            switch (stage)
            {
                case "Notifications":
                    context.Host.Setup(value => value.FinalizeRetiredGenerationNotificationsAsync(
                        retired.NodeManager, It.IsAny<CancellationToken>())).Returns(FailOnce);
                    break;
                case "References":
                    master.Setup(value => value.RemoveReferencesAsync(
                        It.IsAny<List<LocalReference>>(), It.IsAny<CancellationToken>())).Returns(FailOnce);
                    break;
                case "Destruction":
                    context.Host.Setup(value => value.DestroyAddressSpaceAsync(
                        retired.NodeManager, It.IsAny<CancellationToken>())).Returns(FailOnce);
                    break;
                case "DestroyedReferences":
                    context.Host.Setup(value => value.RemoveDestroyedExternalReferencesAsync(
                        retired.NodeManager, It.IsAny<CancellationToken>())).Returns(FailOnce);
                    break;
                case "Disposal":
                    disposal.Setup(value => value.DisposeAsync()).Returns(FailOnce);
                    break;
                default:
                    Assert.Fail("Unknown cleanup stage.");
                    break;
            }
            retired.Generation.Gate.Retire();
            reclaimer.Enqueue([retired]);
            reclaimer.Activate([retired]);

            await Assert.ThatAsync(
                async () => await reclaimer.ReclaimReadyAsync().ConfigureAwait(false),
                Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo(failure.Message)).ConfigureAwait(false);
            await Assert.ThatAsync(
                async () => await retired.Retirement.WaitAsync(timeout.Token).ConfigureAwait(false),
                Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo(failure.Message)).ConfigureAwait(false);
            Assert.That(reclaimer.Count, Is.EqualTo(1));
            disposal.Verify(value => value.DisposeAsync(), Times.Exactly(stage == "Disposal" ? 1 : 0));
            context.Logger.Verify(value => value.Log(
                LogLevel.Error, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(),
                failure, It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);

            await reclaimer.ReclaimReadyAsync().ConfigureAwait(false);
            Assert.That(reclaimer.Count, Is.Zero);
            Assert.That(retired.Retirement.IsFaulted, Is.True, "Retry must not erase an observable failure.");
            Assert.That(retired.PendingReferences, Is.Empty);
            Assert.That(calls, Is.EqualTo(2));
            context.Host.Verify(value => value.FinalizeRetiredGenerationNotificationsAsync(
                retired.NodeManager, It.IsAny<CancellationToken>()),
                Times.Exactly(stage == "Notifications" ? 2 : 1));
            master.Verify(value => value.RemoveReferencesAsync(
                It.IsAny<List<LocalReference>>(), It.IsAny<CancellationToken>()),
                Times.Exactly(stage == "References" ? 2 : 1));
            context.Host.Verify(value => value.DestroyAddressSpaceAsync(
                retired.NodeManager, It.IsAny<CancellationToken>()),
                Times.Exactly(stage == "Destruction" ? 2 : 1));
            context.Host.Verify(value => value.RemoveDestroyedExternalReferencesAsync(
                retired.NodeManager, It.IsAny<CancellationToken>()),
                Times.Exactly(stage == "DestroyedReferences" ? 2 : 1));
            disposal.Verify(value => value.DisposeAsync(), Times.Exactly(stage == "Disposal" ? 2 : 1));
        }

        /// <summary>
        /// Shutdown independently admits pending generations even if normal post-publication activation never ran.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task ShutdownStartsPendingGenerationsIndependentlyAsync(bool blockDrain, bool alreadyAdmitted)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var reclaimer = new GenerationReclaimer();
            var context = new ReclamationContext();
            var first = context.CreateRetirement();
            var second = context.CreateRetirement();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Assert.That(first.Generation.Gate.TryEnter(out int admission), Is.True);
            first.Generation.Gate.Retire();
            second.Generation.Gate.Retire();
            context.Host.Setup(value => value.DestroyAddressSpaceAsync(
                first.NodeManager, It.IsAny<CancellationToken>())).Returns(async () =>
                {
                    entered.TrySetResult(true);
                    await release.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                });
            if (!blockDrain)
            {
                first.Generation.Gate.Exit(admission);
            }
            reclaimer.Enqueue([first, second]);
            if (alreadyAdmitted)
            {
                reclaimer.Activate([first]);
                reclaimer.Schedule();
                if (!blockDrain)
                {
                    await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                }
            }
            Task shutdown = reclaimer.CompleteShutdownAsync(timeout.Token).AsTask();
            try
            {
                await second.Retirement.WaitAsync(timeout.Token).ConfigureAwait(false);
                if (!blockDrain)
                {
                    await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                }
                Assert.That(shutdown.IsCompleted, Is.False);
                Assert.That(first.Retirement.IsCompleted, Is.False);
                Assert.That(reclaimer.Contains(second), Is.False);
            }
            finally
            {
                if (blockDrain)
                {
                    first.Generation.Gate.Exit(admission);
                }
                release.TrySetResult(true);
                await shutdown.WaitAsync(timeout.Token).ConfigureAwait(false);
            }
            Assert.That(reclaimer.Count, Is.Zero);
        }

        /// <summary>
        /// Concurrent drain observers cannot replay an already admitted destruction callback.
        /// </summary>
        [Test]
        public async Task ConcurrentSchedulingClaimsGenerationOnceAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var reclaimer = new GenerationReclaimer();
            var context = new ReclamationContext();
            var retired = context.CreateRetirement();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            context.Host.Setup(value => value.DestroyAddressSpaceAsync(
                retired.NodeManager, It.IsAny<CancellationToken>())).Returns(async () =>
                {
                    entered.TrySetResult(true);
                    await release.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                });
            retired.Generation.Gate.Retire();
            reclaimer.Enqueue([retired]);
            reclaimer.Activate([retired]);
            try
            {
                await Task.WhenAll(Enumerable.Range(0, 8)
                    .Select(_ => Task.Run(reclaimer.Schedule))).WaitAsync(timeout.Token).ConfigureAwait(false);
                await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                await reclaimer.ReclaimReadyAsync().ConfigureAwait(false);
                Assert.That(retired.Retirement.IsCompleted, Is.False);
                context.Host.Verify(value => value.DestroyAddressSpaceAsync(
                    retired.NodeManager, It.IsAny<CancellationToken>()), Times.Once);
            }
            finally
            {
                release.TrySetResult(true);
                await reclaimer.CompleteShutdownAsync(timeout.Token).ConfigureAwait(false);
            }
            Assert.That(reclaimer.Count, Is.Zero);
            Mock.Get(retired.NodeManager).As<IAsyncDisposable>().Verify(value => value.DisposeAsync(), Times.Once);
        }

        /// <summary>
        /// Repeated publication activation and a worker share one transition task, including a failed-task retry.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ConcurrentActivationSharesTransitionFinalizationAndRetriesFailureAsync(bool failFirst)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var reclaimer = new GenerationReclaimer();
            var context = new ReclamationContext();
            var retired = context.CreateRetirement();
            var item = new Mock<IMonitoredItem>();
            item.SetupGet(value => value.NodeId).Returns(new NodeId(1));
            Mock<IMonitoredItemGenerationBinding> binding = item.As<IMonitoredItemGenerationBinding>();
            int marks = 0;
            binding.Setup(value => value.TryInvalidateSource(0, false)).Returns(() =>
            {
                if (Interlocked.Increment(ref marks) == 1 && failFirst)
                {
                    throw new InvalidOperationException("Transition finalization failed.");
                }
                return true;
            });
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int drains = 0;
            Func<CancellationToken, ValueTask>? drain = async ct =>
            {
                Interlocked.Increment(ref drains);
                entered.TrySetResult(true);
                await release.Task.WaitAsync(ct).ConfigureAwait(false);
            };
            var current = new Mock<INodeManagerMonitoredItemSource>();
            current.Setup(value => value.GetNotificationDrain(item.Object.NodeId)).Returns(drain);
            retired.Transition = new NodeManagerLifecycle.MonitoredItemTransition(
                current.Object, null, [], [item.Object], _ => true);
            Assert.That(
                await retired.Transition.AdoptAsync(timeout.Token).ConfigureAwait(false),
                Is.Empty);
            retired.Generation.Gate.Retire();
            reclaimer.Enqueue([retired]);
            reclaimer.Activate([retired]);
            await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
            reclaimer.Schedule();
            await Task.WhenAll(Enumerable.Range(0, 8)
                .Select(_ => Task.Run(() => reclaimer.Activate([retired]))))
                .WaitAsync(timeout.Token).ConfigureAwait(false);
            Assert.That(drains, Is.EqualTo(1));
            Assert.That(marks, Is.Zero);
            release.TrySetResult(true);
            if (failFirst)
            {
                Exception? failure = await retired.Worker!.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(failure, Is.TypeOf<AggregateException>());
                await Assert.ThatAsync(
                    async () => await retired.Retirement.WaitAsync(timeout.Token).ConfigureAwait(false),
                    Throws.TypeOf<AggregateException>()).ConfigureAwait(false);
                Assert.That(marks, Is.EqualTo(1));
                await Task.WhenAll(Enumerable.Range(0, 8)
                    .Select(_ => Task.Run(() => reclaimer.Activate([retired]))))
                    .WaitAsync(timeout.Token).ConfigureAwait(false);
                await reclaimer.ReclaimReadyAsync().ConfigureAwait(false);
            }
            else
            {
                await retired.Retirement.WaitAsync(timeout.Token).ConfigureAwait(false);
            }
            Assert.That(marks, Is.EqualTo(failFirst ? 2 : 1));
            Assert.That(drains, Is.EqualTo(1), "Retry must not repeat completed notification fences.");
            Assert.That(reclaimer.Count, Is.Zero);
            Mock.Get(retired.NodeManager).As<IAsyncDisposable>().Verify(value => value.DisposeAsync(), Times.Once);
        }

        /// <summary>
        /// Shutdown joins an admitted asynchronous disposal, and a cancelled join leaves cleanup retryable.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ShutdownWaitsForAdmittedCleanupAndCanRetryCancelledWaitAsync(bool cancelWait)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var reclaimer = new GenerationReclaimer();
            var context = new ReclamationContext();
            var retired = context.CreateRetirement();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Mock<IAsyncDisposable> disposal = Mock.Get(retired.NodeManager).As<IAsyncDisposable>();
            disposal.Setup(value => value.DisposeAsync()).Returns(async () =>
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
            });
            retired.Generation.Gate.Retire();
            reclaimer.Enqueue([retired]);
            reclaimer.Activate([retired]);
            reclaimer.Schedule();
            await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
            using var cancelled = new CancellationTokenSource();
            Task shutdown = reclaimer.CompleteShutdownAsync(
                cancelWait ? cancelled.Token : timeout.Token).AsTask();
            try
            {
                Assert.That(shutdown.IsCompleted, Is.False);
                Assert.That(retired.Retirement.IsCompleted, Is.False);
                if (cancelWait)
                {
                    cancelled.Cancel();
                    await Assert.ThatAsync(async () => await shutdown.ConfigureAwait(false),
                        Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
                    Assert.That(retired.Retirement.IsCompleted, Is.False);
                }
            }
            finally
            {
                release.TrySetResult(true);
                await reclaimer.CompleteShutdownAsync(timeout.Token).ConfigureAwait(false);
            }
            if (!cancelWait)
            {
                await shutdown.WaitAsync(timeout.Token).ConfigureAwait(false);
            }
            Assert.That(reclaimer.Count, Is.Zero);
            disposal.Verify(value => value.DisposeAsync(), Times.Once);
        }

        /// <summary>
        /// Supplies deterministic server services and a host without sockets or lifecycle writer state.
        /// </summary>
        private sealed class ReclamationContext
        {
            /// <summary>
            /// Creates an empty server with source-generated logging captured for assertions.
            /// </summary>
            internal ReclamationContext()
            {
                var sessions = new Mock<ISessionManager>();
                sessions.Setup(value => value.GetSessions()).Returns([]);
                var subscriptions = new Mock<ISubscriptionManager>();
                subscriptions.Setup(value => value.GetSubscriptions()).Returns([]);
                Logger.Setup(value => value.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
                var factory = new Mock<ILoggerFactory>();
                factory.Setup(value => value.CreateLogger(It.IsAny<string>())).Returns(Logger.Object);
                var telemetry = new Mock<ITelemetryContext>();
                telemetry.SetupGet(value => value.LoggerFactory).Returns(factory.Object);
                m_server.SetupGet(value => value.SessionManager).Returns(sessions.Object);
                m_server.SetupGet(value => value.SubscriptionManager).Returns(subscriptions.Object);
                m_server.SetupGet(value => value.Telemetry).Returns(telemetry.Object);
                Host = m_master.As<IDynamicNodeManagerHost>();
                m_server.SetupGet(value => value.NodeManager).Returns(m_master.Object);
                Host.SetupGet(value => value.GenerationReconciler)
                    .Returns(new GenerationReconciler(m_server.Object));
            }

            /// <summary>
            /// Gets the dynamic host callback mock.
            /// </summary>
            internal Mock<IDynamicNodeManagerHost> Host { get; }

            /// <summary>
            /// Gets the cleanup diagnostic logger.
            /// </summary>
            internal Mock<ILogger> Logger { get; } = new();

            /// <summary>
            /// Creates a distinct live generation ready to be retired by the test.
            /// </summary>
            /// <returns>The exact generation's retirement record.</returns>
            internal GenerationReclaimer.RetiredGeneration CreateRetirement()
            {
                var manager = new Mock<IAsyncNodeManager>();
                manager.As<IAsyncDisposable>();
                var generation = new NodeManagerGeneration(
                    new NodeManagerSlot(), 1, manager.Object, NodeManagerGenerationState.Live);
                return new GenerationReclaimer.RetiredGeneration(
                    m_server.Object, Host.Object, generation, [], needsDetachment: true);
            }

            /// <summary>The owning server mock.</summary>
            private readonly Mock<IServerInternal> m_server = new();

            /// <summary>The host's master-node-manager interface.</summary>
            private readonly Mock<IMasterNodeManager> m_master = new();
        }
    }
}
