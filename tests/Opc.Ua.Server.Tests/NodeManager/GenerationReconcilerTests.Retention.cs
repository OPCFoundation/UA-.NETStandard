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
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    public sealed partial class GenerationReconcilerTests
    {
        /// <summary>
        /// Pending reconciliation owns its generation until superseded. The result, issue snapshot,
        /// ordinary exception, and completed retry task do not retain it afterward. This isolates
        /// reconciliation and does not execute physical retirement.
        /// </summary>
        [Test]
        public async Task SupersededReconciliationReleasesManagerWhileResultAndRetryTaskRemainAliveAsync()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                var reconciler = new GenerationReconciler(server.Object);
                (LifecycleResult result,
                    WeakReference<IAsyncNodeManager> manager,
                    WeakReference<NodeManagerGeneration> generation,
                    int[] attempts) = await CreatePendingRetentionControlAsync(reconciler).ConfigureAwait(false);
                await Task.Yield();

                ArrayOf<NodeManagerReconciliationIssue> pendingIssues = result.Issues;
                Task retry = result.RetryReconciliationAsync().AsTask();
                await retry.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                RetireAndUnpublishReconciliationGeneration(generation);
                await Task.Yield();
                CollectReconciliationTargets();
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(attempts[0], Is.EqualTo(2));
                    Assert.That(IsReconciliationTargetAlive(manager), Is.True);
                    Assert.That(IsReconciliationTargetAlive(generation), Is.True);
                    Assert.That(result.Slots[0].TryGetCurrent(out _), Is.False);
                    Assert.That(pendingIssues.Count, Is.EqualTo(1));
                    Assert.That(result.Issues.Count, Is.EqualTo(1));
                    Assert.That(result.Issues[0].Stage, Is.EqualTo(NodeManagerReconciliationStage.Readiness));
                    Assert.That(result.Issues[0].IsRetryable, Is.True);
                    Assert.That(result.Issues[0].IsSuperseded, Is.False);
                }

                reconciler.SupersedeReconciliations();
                await Task.Yield();
                CollectReconciliationTargets();
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(IsReconciliationTargetAlive(manager), Is.False);
                    Assert.That(IsReconciliationTargetAlive(generation), Is.False);
                    Assert.That(result.Registrations[0].TryGetNodeManager(out _), Is.False);
                    Assert.That(result.Registrations[0].TryGetRegistration(out _), Is.False);
                    Assert.That(result.PublishedSlots[0].Slot, Is.SameAs(result.Slots[0]));
                    Assert.That(result.PublishedSlots[0].Generation, Is.EqualTo(1));
                    Assert.That(result.Issues.Count, Is.EqualTo(1));
                    Assert.That(result.Issues[0].IsRetryable, Is.False);
                    Assert.That(result.Issues[0].IsSuperseded, Is.True);
                    Assert.That(result.Issues[0].InitialFailure, Is.SameAs(result.CleanupFailure));
                    Assert.That(pendingIssues[0].InitialFailure, Is.SameAs(result.CleanupFailure));
                    Assert.That(pendingIssues[0].IsRetryable, Is.True);
                    Assert.That(retry.Status, Is.EqualTo(TaskStatus.RanToCompletion));
                }
                await result.RetryReconciliationAsync().ConfigureAwait(false);
                Assert.That(attempts[0], Is.EqualTo(2));
                GC.KeepAlive(result);
                GC.KeepAlive(pendingIssues);
                GC.KeepAlive(retry);
                GC.KeepAlive(reconciler);
            }
        }

        /// <summary>
        /// Creates unfinished work and returns non-owning observations and its public result.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static async Task<(
            LifecycleResult Result,
            WeakReference<IAsyncNodeManager> Manager,
            WeakReference<NodeManagerGeneration> Generation,
            int[] Attempts)> CreatePendingRetentionControlAsync(GenerationReconciler reconciler)
        {
            IAsyncNodeManager? manager = Mock.Of<IAsyncNodeManager>();
            NodeManagerGeneration? generation = NodeManagerGenerationRegistry.GetOrCreate(manager);
            NodeManagerRegistration? registration = new(Guid.NewGuid(), generation.Number, manager);
            NodeManagerSlot slot = registration.Slot;
            var managerReference = new WeakReference<IAsyncNodeManager>(manager);
            var generationReference = new WeakReference<NodeManagerGeneration>(generation);
            var failure = new InvalidOperationException("Reconciliation failed.");
            int[] attempts = [0];
            GenerationReconciler.ReconciliationWork? work = reconciler.CreateReconciliation(
                () => Mock.Of<IDisposable>());
            work.Publish(1);
            await work.RunAsync(
                NodeManagerReconciliationStage.Readiness,
                [generation],
                CreateFailingRetentionCallback(manager, failure, attempts)).ConfigureAwait(false);
            work.Seal();
            var result = new LifecycleResult(
                [new NodeManagerRegistrationSnapshot(registration)],
                0,
                failure,
                1,
                [slot],
                Task.CompletedTask,
                work.Ticket,
                [new NodeManagerGenerationSnapshot(slot, generation.Number)]);
            manager = null;
            generation = null;
            registration = null;
            work = null;
            return (result, managerReference, generationReference, attempts);
        }

        /// <summary>
        /// Captures the manager only in the pending callback, not in the returned counter.
        /// </summary>
        private static Func<ValueTask> CreateFailingRetentionCallback(
            IAsyncNodeManager manager,
            Exception failure,
            int[] attempts)
        {
            return () =>
            {
                attempts[0]++;
                GC.KeepAlive(manager);
                throw failure;
            };
        }

        /// <summary>
        /// Retires and unpublishes the generation outside the collection scope.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void RetireAndUnpublishReconciliationGeneration(
            WeakReference<NodeManagerGeneration> reference)
        {
            if (!reference.TryGetTarget(out NodeManagerGeneration? generation))
            {
                throw new InvalidOperationException("The pending generation was collected before supersession.");
            }
            generation.Gate.Retire();
            generation.Slot.Unpublish(generation);
        }

        /// <summary>
        /// Checks a weak target without extending its lifetime into a collection cycle.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool IsReconciliationTargetAlive<T>(WeakReference<T> reference)
            where T : class
        {
            return reference.TryGetTarget(out _);
        }

        /// <summary>
        /// Performs full collection cycles after the producer and retirement scopes have returned.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void CollectReconciliationTargets()
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
