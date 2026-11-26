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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    public sealed partial class GenerationReclaimerTests
    {
        /// <summary>
        /// A retained result and completed retirement task do not own a successfully retired generation.
        /// </summary>
        [Test]
        public async Task CompletedRetirementReleasesGenerationWhileResultAndTaskRemainAliveAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var reclaimer = new GenerationReclaimer();
            (LifecycleResult result,
                WeakReference<IAsyncNodeManager> manager,
                WeakReference<NodeManagerGeneration> generation,
                WeakReference<GenerationReclaimer.RetiredGeneration> record,
                int[] cleanupCalls) = await CompleteRetirementRetentionControlAsync(reclaimer, timeout.Token)
                    .ConfigureAwait(false);
            Task retirement = result.Retired;
            await Task.Yield();
            CollectRetirementTargets();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(reclaimer.Count, Is.Zero);
                Assert.That(cleanupCalls[0], Is.EqualTo(1));
                Assert.That(cleanupCalls[1], Is.EqualTo(1));
                Assert.That(result.RetirementStatus, Is.EqualTo(NodeManagerRetirementStatus.Completed));
                Assert.That(retirement.Status, Is.EqualTo(TaskStatus.RanToCompletion));
                Assert.That(result.CleanupFailure, Is.Null);
                Assert.That(result.Slots[0].TryGetCurrent(out _), Is.False);
                Assert.That(result.PublishedSlots[0].Slot, Is.SameAs(result.Slots[0]));
                Assert.That(result.PublishedSlots[0].Generation, Is.EqualTo(1));
                Assert.That(IsRetirementTargetAlive(record), Is.False);
                Assert.That(IsRetirementTargetAlive(generation), Is.False);
                Assert.That(IsRetirementTargetAlive(manager), Is.False);
            }
            GC.KeepAlive(result);
            GC.KeepAlive(retirement);
            GC.KeepAlive(reclaimer);
        }

        /// <summary>
        /// A result retains the first cleanup failure while retirement retries independently.
        /// </summary>
        [Test]
        public async Task LifecycleResultRetainsFailureAcrossDelayedRetirementRetryAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var clock = new FakeTimeProvider();
            using var reclaimer = new GenerationReclaimer(clock);
            var context = new ReclamationContext();
            GenerationReclaimer.RetiredGeneration retired = context.CreateRetirement();
            var failure = new InvalidOperationException("The first destruction attempt failed.");
            int destructionAttempts = 0;
            context.Host.Setup(host => host.DestroyAddressSpaceAsync(
                retired.NodeManager, It.IsAny<CancellationToken>())).Returns(() =>
                    Interlocked.Increment(ref destructionAttempts) == 1
                        ? new ValueTask(Task.FromException(failure))
                        : default);
            Mock<IAsyncDisposable> disposal = Mock.Get(retired.NodeManager).As<IAsyncDisposable>();
            NodeManagerSlot slot = retired.Generation.Slot;
            slot.Publish(retired.Generation);
            retired.Generation.Gate.Retire();
            slot.Unpublish(retired.Generation);
            var retirement = Task.WhenAll([retired.Retirement]);
            reclaimer.Enqueue([retired]);
            reclaimer.Activate([retired]);

            try
            {
                InvalidOperationException? observed = await Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await reclaimer.ReclaimReadyAsync().ConfigureAwait(false)).ConfigureAwait(false);
                var result = new LifecycleResult(
                    [],
                    0,
                    observed,
                    1,
                    [slot],
                    retirement,
                    publishedSlots: [new NodeManagerGenerationSnapshot(slot, retired.Generation.Number)]);

                await result.RetryReconciliationAsync(timeout.Token).ConfigureAwait(false);
                clock.Advance(TimeSpan.FromMilliseconds(999));

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(observed, Is.SameAs(failure));
                    Assert.That(result.CleanupFailure, Is.SameAs(failure));
                    Assert.That(result.Retired, Is.SameAs(retirement));
                    Assert.That(result.RetirementStatus, Is.EqualTo(NodeManagerRetirementStatus.Pending));
                    Assert.That(retirement.IsCompleted, Is.False);
                    Assert.That(retired.RetryScheduled, Is.True);
                    Assert.That(reclaimer.Contains(retired), Is.True);
                    Assert.That(destructionAttempts, Is.EqualTo(1));
                    Assert.That(result.Slots[0].TryGetCurrent(out _), Is.False);
                }
                disposal.Verify(value => value.DisposeAsync(), Times.Never);

                clock.Advance(TimeSpan.FromMilliseconds(1));
                await AssertRetirementOutcomeAsync(retirement, failure, timeout.Token).ConfigureAwait(false);

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(result.CleanupFailure, Is.SameAs(failure));
                    Assert.That(result.RetirementStatus, Is.EqualTo(NodeManagerRetirementStatus.CompletedWithFailure));
                    Assert.That(result.PublishedSlots[0].Slot, Is.SameAs(slot));
                    Assert.That(result.PublishedSlots[0].Generation, Is.EqualTo(1));
                    Assert.That(destructionAttempts, Is.EqualTo(2));
                    Assert.That(reclaimer.Count, Is.Zero);
                }
                context.Host.Verify(host => host.FinalizeRetiredGenerationNotificationsAsync(
                    retired.NodeManager, It.IsAny<CancellationToken>()), Times.Once);
                disposal.Verify(value => value.DisposeAsync(), Times.Once);
                GC.KeepAlive(result);
                GC.KeepAlive(retirement);
                GC.KeepAlive(failure);
            }
            finally
            {
                clock.Advance(TimeSpan.FromMinutes(1));
                await reclaimer.CompleteShutdownAsync(timeout.Token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// A failed cleanup followed by a shutdown retry releases its generation while callers retain the outcomes.
        /// </summary>
        [Test]
        public async Task FailedRetirementReleasesGenerationWhileResultAndTasksRemainAliveAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var reclaimer = new GenerationReclaimer();
            (LifecycleResult result,
                Task initialAttempt,
                WeakReference<IAsyncNodeManager> manager,
                WeakReference<NodeManagerGeneration> generation,
                WeakReference<GenerationReclaimer.RetiredGeneration> record,
                Exception failure,
                int[] cleanupCalls) = await CompleteFailedRetirementRetentionControlAsync(
                    reclaimer, timeout.Token).ConfigureAwait(false);
            Task retirement = result.Retired;
            await Task.Yield();
            CollectRetirementTargets();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(reclaimer.Count, Is.Zero);
                Assert.That(cleanupCalls[0], Is.EqualTo(2));
                Assert.That(cleanupCalls[1], Is.EqualTo(1));
                Assert.That(cleanupCalls[2], Is.EqualTo(1));
                Assert.That(initialAttempt.IsFaulted, Is.True);
                Assert.That(initialAttempt.Exception!.InnerExceptions, Has.Exactly(1).SameAs(failure));
                Assert.That(retirement.IsFaulted, Is.True);
                Assert.That(retirement.Exception!.InnerExceptions, Has.Exactly(1).SameAs(failure));
                Assert.That(result.CleanupFailure, Is.SameAs(failure));
                Assert.That(result.RetirementStatus, Is.EqualTo(NodeManagerRetirementStatus.CompletedWithFailure));
                Assert.That(result.Slots[0].TryGetCurrent(out _), Is.False);
                Assert.That(result.PublishedSlots[0].Slot, Is.SameAs(result.Slots[0]));
                Assert.That(result.PublishedSlots[0].Generation, Is.EqualTo(1));
                Assert.That(IsRetirementTargetAlive(record), Is.False);
                Assert.That(IsRetirementTargetAlive(generation), Is.False);
                Assert.That(IsRetirementTargetAlive(manager), Is.False);
            }
            GC.KeepAlive(result);
            GC.KeepAlive(initialAttempt);
            GC.KeepAlive(retirement);
            GC.KeepAlive(failure);
            GC.KeepAlive(reclaimer);
        }

        /// <summary>
        /// Completes physical retirement and returns non-owning observations of its objects.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static async Task<(
            LifecycleResult Result,
            WeakReference<IAsyncNodeManager> Manager,
            WeakReference<NodeManagerGeneration> Generation,
            WeakReference<GenerationReclaimer.RetiredGeneration> Record,
            int[] CleanupCalls)> CompleteRetirementRetentionControlAsync(
            GenerationReclaimer reclaimer,
            CancellationToken cancellationToken)
        {
            ReclamationContext? context = new();
            GenerationReclaimer.RetiredGeneration? retired = context.CreateRetirement();
            var manager = new WeakReference<IAsyncNodeManager>(retired.NodeManager);
            var generation = new WeakReference<NodeManagerGeneration>(retired.Generation);
            var record = new WeakReference<GenerationReclaimer.RetiredGeneration>(retired);
            int[] cleanupCalls = [0, 0];
            context.Host.Setup(host => host.DestroyAddressSpaceAsync(
                It.IsAny<IAsyncNodeManager>(), It.IsAny<CancellationToken>())).Returns(() =>
            {
                Interlocked.Increment(ref cleanupCalls[0]);
                return default;
            });
            Mock.Get(retired.NodeManager).As<IAsyncDisposable>()
                .Setup(disposable => disposable.DisposeAsync()).Returns(() =>
                {
                    Interlocked.Increment(ref cleanupCalls[1]);
                    return default;
                });

            NodeManagerSlot slot = retired.Generation.Slot;
            slot.Publish(retired.Generation);
            retired.Generation.Gate.Retire();
            slot.Unpublish(retired.Generation);
            var result = new LifecycleResult(
                [],
                0,
                null,
                1,
                [slot],
                Task.WhenAll([retired.Retirement]),
                publishedSlots: [new NodeManagerGenerationSnapshot(slot, retired.Generation.Number)]);
            Assert.That(result.RetirementStatus, Is.EqualTo(NodeManagerRetirementStatus.Pending));

            reclaimer.Enqueue([retired]);
            reclaimer.Activate([retired]);
            await reclaimer.ReclaimReadyAsync().ConfigureAwait(false);
            await result.Retired.WaitAsync(cancellationToken).ConfigureAwait(false);
            context = null;
            retired = null;
            return (result, manager, generation, record, cleanupCalls);
        }

        /// <summary>
        /// Completes a failed physical cleanup through the shutdown retry without leaving a delayed retry.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static async Task<(
            LifecycleResult Result,
            Task InitialAttempt,
            WeakReference<IAsyncNodeManager> Manager,
            WeakReference<NodeManagerGeneration> Generation,
            WeakReference<GenerationReclaimer.RetiredGeneration> Record,
            Exception Failure,
            int[] CleanupCalls)> CompleteFailedRetirementRetentionControlAsync(
            GenerationReclaimer reclaimer,
            CancellationToken cancellationToken)
        {
            ReclamationContext? context = new();
            GenerationReclaimer.RetiredGeneration? retired = context.CreateRetirement();
            var manager = new WeakReference<IAsyncNodeManager>(retired.NodeManager);
            var generation = new WeakReference<NodeManagerGeneration>(retired.Generation);
            var record = new WeakReference<GenerationReclaimer.RetiredGeneration>(retired);
            var failure = new InvalidOperationException("The first destruction attempt failed.");
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int[] cleanupCalls = [0, 0, 0];
            context.Host.Setup(host => host.FinalizeRetiredGenerationNotificationsAsync(
                It.IsAny<IAsyncNodeManager>(), It.IsAny<CancellationToken>())).Returns(() =>
            {
                Interlocked.Increment(ref cleanupCalls[2]);
                return default;
            });
            context.Host.Setup(host => host.DestroyAddressSpaceAsync(
                It.IsAny<IAsyncNodeManager>(), It.IsAny<CancellationToken>())).Returns(async () =>
            {
                if (Interlocked.Increment(ref cleanupCalls[0]) == 1)
                {
                    entered.TrySetResult(true);
                    await release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                    throw failure;
                }
            });
            Mock.Get(retired.NodeManager).As<IAsyncDisposable>()
                .Setup(disposable => disposable.DisposeAsync()).Returns(() =>
                {
                    Interlocked.Increment(ref cleanupCalls[1]);
                    return default;
                });

            NodeManagerSlot slot = retired.Generation.Slot;
            slot.Publish(retired.Generation);
            retired.Generation.Gate.Retire();
            slot.Unpublish(retired.Generation);
            var retirement = Task.WhenAll([retired.Retirement]);
            reclaimer.Enqueue([retired]);
            reclaimer.Activate([retired]);

            try
            {
                Task initialAttempt = reclaimer.ReclaimReadyAsync().AsTask();
                await entered.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                reclaimer.StopScheduling();
                release.TrySetResult(true);
                await Assert.ThatAsync(
                    () => initialAttempt,
                    Throws.Exception.SameAs(failure)).ConfigureAwait(false);
                var result = new LifecycleResult(
                    [],
                    0,
                    failure,
                    1,
                    [slot],
                    retirement,
                    publishedSlots: [new NodeManagerGenerationSnapshot(slot, retired.Generation.Number)]);

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(retired.RetryScheduled, Is.False);
                    Assert.That(reclaimer.Contains(retired), Is.True);
                    Assert.That(result.RetirementStatus, Is.EqualTo(NodeManagerRetirementStatus.Pending));
                    Assert.That(cleanupCalls[0], Is.EqualTo(1));
                    Assert.That(cleanupCalls[1], Is.Zero);
                    Assert.That(cleanupCalls[2], Is.EqualTo(1));
                }

                await reclaimer.CompleteShutdownAsync(cancellationToken).ConfigureAwait(false);
                await AssertRetirementOutcomeAsync(retirement, failure, cancellationToken).ConfigureAwait(false);
                context = null;
                retired = null;
                return (result, initialAttempt, manager, generation, record, failure, cleanupCalls);
            }
            finally
            {
                reclaimer.StopScheduling();
                release.TrySetResult(true);
                using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await reclaimer.CompleteShutdownAsync(cleanupTimeout.Token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Checks a weak target without extending its lifetime into a collection cycle.
        /// </summary>
        /// <typeparam name="T">The weakly referenced target type.</typeparam>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool IsRetirementTargetAlive<T>(WeakReference<T> reference)
            where T : class
        {
            return reference.TryGetTarget(out _);
        }

        /// <summary>
        /// Collects after the cleanup scope has returned its non-owning observations.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void CollectRetirementTargets()
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
        }
    }
}
