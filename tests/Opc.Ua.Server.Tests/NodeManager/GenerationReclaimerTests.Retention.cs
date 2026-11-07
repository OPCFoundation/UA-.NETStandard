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
        /// Checks a weak target without extending its lifetime into a collection cycle.
        /// </summary>
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
