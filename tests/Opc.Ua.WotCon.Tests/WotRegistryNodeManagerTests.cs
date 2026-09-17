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
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.WotCon.Server;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Tests
{
    [TestFixture]
    [Category("WotCon")]
    public sealed class WotRegistryNodeManagerTests
    {
        [Test]
        public async Task ReconcileQueueRetainsVersionUntilQueuedFailureIsDelivered()
        {
            TaskCompletionSource<bool> entered = NewSignal();
            TaskCompletionSource<bool> release = NewSignal();
            var operations = new List<string>();
            bool versionPresent = false;
            using var queue = new WotRegistryReconcileQueue(async change =>
            {
                if (change.Current.Generation == 1)
                {
                    entered.SetResult(true);
                    await release.Task.ConfigureAwait(false);
                    return;
                }
                versionPresent = change.Current.FindResource("g", "r")?.FindVersion("v1") is not null;
                operations.Add(versionPresent ? "create" : "delete");
            });
            WotRegistrySnapshot empty0 = Snapshot(0, hasResource: false);
            WotRegistrySnapshot empty1 = Snapshot(1, hasResource: false);
            WotRegistrySnapshot created2 = Snapshot(2, hasResource: true);
            WotRegistrySnapshot deleted3 = Snapshot(3, hasResource: false);
            queue.Enqueue(Change(empty0, empty1));
            await entered.Task.ConfigureAwait(false);
            try
            {
                queue.Enqueue(Change(empty1, created2));
                queue.Enqueue(() =>
                {
                    Assert.That(versionPresent, Is.True,
                        "A queued failure must run after creation and before the matching Version is retired.");
                    operations.Add("failure");
                    return Task.CompletedTask;
                });
                queue.Enqueue(Change(created2, deleted3));
                release.SetResult(true);
                await queue.CompleteAsync().ConfigureAwait(false);
                Assert.That(operations, Is.EqualTo(s_expectedFailureOperations));
                Assert.That(versionPresent, Is.False);
            }
            finally
            {
                release.TrySetResult(true);
            }
        }

        [Test]
        public async Task CancelledIdleWaitDoesNotDiscardQueuedEvents()
        {
            TaskCompletionSource<bool> entered = NewSignal();
            TaskCompletionSource<bool> release = NewSignal();
            bool delivered = false;
            using var queue = new WotRegistryReconcileQueue(async _ =>
            {
                entered.SetResult(true);
                await release.Task.ConfigureAwait(false);
            });
            queue.Enqueue(Change(Snapshot(0, false), Snapshot(1, false)));
            await entered.Task.ConfigureAwait(false);
            try
            {
                queue.Enqueue(() =>
                {
                    delivered = true;
                    return Task.CompletedTask;
                });
                using var cancellation = new CancellationTokenSource();
                Task waiting = queue.WhenIdleAsync(cancellation.Token).AsTask();
                cancellation.Cancel();
                await Assert.ThatAsync(async () => await waiting.ConfigureAwait(false),
                    Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
                Assert.That(delivered, Is.False);
                release.SetResult(true);
                await queue.CompleteAsync().ConfigureAwait(false);
                Assert.That(delivered, Is.True);
            }
            finally
            {
                release.TrySetResult(true);
            }
        }

        [Test]
        public async Task AcceptedProjectionDispatchSurvivesCallerCancellation()
        {
            TaskCompletionSource<bool> entered = NewSignal();
            TaskCompletionSource<bool> release = NewSignal();
            bool dispatched = false;
            using var queue = new WotRegistryReconcileQueue(async _ =>
            {
                entered.SetResult(true);
                await release.Task.ConfigureAwait(false);
            });
            queue.Enqueue(Change(Snapshot(0, false), Snapshot(1, false)));
            await entered.Task.ConfigureAwait(false);
            try
            {
                using var cancellation = new CancellationTokenSource();
                Task operation = queue.EnqueueAsync(token =>
                {
                    Assert.That(token.CanBeCanceled, Is.False);
                    dispatched = true;
                    return default;
                }, cancellation.Token).AsTask();
                cancellation.Cancel();
                await Assert.ThatAsync(async () => await operation.ConfigureAwait(false),
                    Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
                Assert.That(dispatched, Is.False);
                release.SetResult(true);
                await queue.CompleteAsync().ConfigureAwait(false);
                Assert.That(dispatched, Is.True);
            }
            finally
            {
                release.TrySetResult(true);
            }
        }

        [Test]
        public async Task FailedProjectionDispatchDoesNotPoisonQueueCleanup()
        {
            var failure = new InvalidOperationException("Projection dispatch failed.");
            using var queue = new WotRegistryReconcileQueue(_ => Task.CompletedTask);
            Task operation = queue.EnqueueAsync(_ => throw failure, CancellationToken.None).AsTask();
            await Assert.ThatAsync(async () => await operation.ConfigureAwait(false),
                Throws.TypeOf<InvalidOperationException>().And.SameAs(failure)).ConfigureAwait(false);
            bool laterDelivered = false;
            queue.Enqueue(() =>
            {
                laterDelivered = true;
                return Task.CompletedTask;
            });
            await queue.CompleteAsync().ConfigureAwait(false);
            Assert.That(laterDelivered, Is.True);
        }

        [Test]
        public async Task DisposingQueueCompletesPendingProjectionWaiter()
        {
            TaskCompletionSource<bool> entered = NewSignal();
            TaskCompletionSource<bool> release = NewSignal();
            bool dispatched = false;
            using var queue = new WotRegistryReconcileQueue(async _ =>
            {
                entered.SetResult(true);
                await release.Task.ConfigureAwait(false);
            });
            queue.Enqueue(Change(Snapshot(0, false), Snapshot(1, false)));
            await entered.Task.ConfigureAwait(false);
            try
            {
                Task operation = queue.EnqueueAsync(_ =>
                {
                    dispatched = true;
                    return default;
                }, CancellationToken.None).AsTask();
                queue.Dispose();
                await Assert.ThatAsync(async () => await operation.ConfigureAwait(false),
                    Throws.TypeOf<ObjectDisposedException>()).ConfigureAwait(false);
                release.SetResult(true);
                await queue.WhenIdleAsync().ConfigureAwait(false);
                Assert.That(dispatched, Is.False);
            }
            finally
            {
                release.TrySetResult(true);
            }
        }

        [Test]
        public async Task ReconcileQueuePreservesCreateThenDeleteWhileOccupied()
        {
            TaskCompletionSource<bool> entered = NewSignal();
            TaskCompletionSource<bool> release = NewSignal();
            var generations = new List<long>();
            var interactions = new List<string>();
            bool resourceProjected = false;
            using var queue = new WotRegistryReconcileQueue(async change =>
            {
                generations.Add(change.Current.Generation);
                if (change.Current.Generation == 1)
                {
                    entered.SetResult(true);
                    await release.Task.ConfigureAwait(false);
                }

                bool existed = change.Previous.FindResource("g", "r") is not null;
                bool exists = change.Current.FindResource("g", "r") is not null;
                if (!existed && exists)
                {
                    interactions.Add("create");
                }
                else if (existed && !exists)
                {
                    interactions.Add("delete");
                }
                resourceProjected = exists;
            });

            WotRegistrySnapshot empty0 = Snapshot(0, hasResource: false);
            WotRegistrySnapshot empty1 = Snapshot(1, hasResource: false);
            WotRegistrySnapshot created2 = Snapshot(2, hasResource: true);
            WotRegistrySnapshot deleted3 = Snapshot(3, hasResource: false);
            queue.Enqueue(Change(empty0, empty1));
            await entered.Task.ConfigureAwait(false);

            queue.Enqueue(Change(empty1, created2));
            queue.Enqueue(Change(created2, deleted3));
            release.SetResult(true);
            await queue.WhenIdleAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(generations, Is.EqualTo(s_expectedGenerations));
                Assert.That(interactions, Is.EqualTo(s_expectedInteractions));
                Assert.That(resourceProjected, Is.False);
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ReconcileQueueRestartsAfterWorkerFailure(bool canceled)
        {
            TaskCompletionSource<bool> entered = NewSignal();
            TaskCompletionSource<bool> release = NewSignal();
            Exception failure = canceled
                ? new OperationCanceledException("Reconcile canceled.")
                : new InvalidOperationException("Reconcile failed.");
            var generations = new List<long>();
            using var queue = new WotRegistryReconcileQueue(async change =>
            {
                generations.Add(change.Current.Generation);
                if (change.Current.Generation == 1)
                {
                    entered.SetResult(true);
                    await release.Task.ConfigureAwait(false);
                    throw failure;
                }
            });

            WotRegistrySnapshot empty0 = Snapshot(0, hasResource: false);
            WotRegistrySnapshot empty1 = Snapshot(1, hasResource: false);
            WotRegistrySnapshot created2 = Snapshot(2, hasResource: true);
            queue.Enqueue(Change(empty0, empty1));
            await entered.Task.ConfigureAwait(false);

            Task firstIdle = queue.WhenIdleAsync().AsTask();
            release.SetResult(true);
            await Assert.ThatAsync(
                async () => await firstIdle.ConfigureAwait(false),
                Throws.InstanceOf<Exception>().And.SameAs(failure)).ConfigureAwait(false);
            await Assert.ThatAsync(
                async () => await queue.WhenIdleAsync().ConfigureAwait(false),
                Throws.InstanceOf<Exception>().And.SameAs(failure)).ConfigureAwait(false);

            queue.Enqueue(Change(empty1, created2));
            await queue.CompleteAsync().ConfigureAwait(false);

            Assert.That(generations, Is.EqualTo(s_expectedRestartedGenerations));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ReconcileQueueDrainsPendingChangesAfterWorkerFailure(bool completing)
        {
            TaskCompletionSource<bool> firstEntered = NewSignal();
            TaskCompletionSource<bool> firstRelease = NewSignal();
            TaskCompletionSource<bool> secondEntered = NewSignal();
            TaskCompletionSource<bool> secondRelease = NewSignal();
            var failure = new InvalidOperationException("Reconcile failed.");
            var generations = new List<long>();
            using var queue = new WotRegistryReconcileQueue(async change =>
            {
                generations.Add(change.Current.Generation);
                if (change.Current.Generation == 1)
                {
                    firstEntered.SetResult(true);
                    await firstRelease.Task.ConfigureAwait(false);
                    throw failure;
                }
                if (change.Current.Generation == 2)
                {
                    secondEntered.SetResult(true);
                    await secondRelease.Task.ConfigureAwait(false);
                }
            });

            WotRegistrySnapshot empty0 = Snapshot(0, hasResource: false);
            WotRegistrySnapshot empty1 = Snapshot(1, hasResource: false);
            WotRegistrySnapshot created2 = Snapshot(2, hasResource: true);
            WotRegistrySnapshot deleted3 = Snapshot(3, hasResource: false);
            queue.Enqueue(Change(empty0, empty1));
            await firstEntered.Task.ConfigureAwait(false);
            queue.Enqueue(Change(empty1, created2));
            queue.Enqueue(Change(created2, deleted3));
            Task failedWait = completing
                ? queue.CompleteAsync().AsTask()
                : queue.WhenIdleAsync().AsTask();

            try
            {
                firstRelease.SetResult(true);
                await Assert.ThatAsync(
                    async () => await failedWait.ConfigureAwait(false),
                    Throws.InstanceOf<Exception>().And.SameAs(failure)).ConfigureAwait(false);

                Task started = await Task.WhenAny(
                    secondEntered.Task,
                    Task.Delay(TimeSpan.FromSeconds(10))).ConfigureAwait(false);
                Assert.That(started, Is.SameAs(secondEntered.Task),
                    "Pending changes must resume without another Enqueue call.");

                Task completed = queue.CompleteAsync().AsTask();
                Assert.That(completed.IsCompleted, Is.False,
                    "Completion must await the replacement worker.");
                secondRelease.SetResult(true);
                await completed.ConfigureAwait(false);

                Assert.That(generations, Is.EqualTo(s_expectedGenerations));
            }
            finally
            {
                firstRelease.TrySetResult(true);
                secondRelease.TrySetResult(true);
            }
        }

        private static WotRegistryChangedEventArgs Change(
            WotRegistrySnapshot previous,
            WotRegistrySnapshot current)
        {
            return new WotRegistryChangedEventArgs(
                previous,
                current,
                ["/groups/g/resources/r"],
                projectionOnly: false);
        }

        private static WotRegistrySnapshot Snapshot(long generation, bool hasResource)
        {
            ImmutableDictionary<string, WotResourceGroup> groups =
                ImmutableDictionary<string, WotResourceGroup>.Empty;
            if (hasResource)
            {
                WotResourceVersion version = WotResourceVersion.CreatePlaceholder(
                    "v1",
                    s_unixEpoch);
                var resource = new WotResource(
                    "g",
                    "r",
                    WoTDocumentKindEnum.ThingDescription,
                    [version],
                    defaultVersionId: "v1",
                    epoch: 1);
                var group = new WotResourceGroup(
                    "g",
                    WoTDocumentKindEnum.ThingDescription,
                    ImmutableDictionary<string, WotResource>.Empty.Add("r", resource),
                    epoch: 1);
                groups = groups.Add("g", group);
            }
            return new WotRegistrySnapshot(generation, groups);
        }

        private static TaskCompletionSource<bool> NewSignal()
        {
            return new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private static readonly DateTime s_unixEpoch =
            new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private static readonly long[] s_expectedGenerations = [1, 2, 3];
        private static readonly long[] s_expectedRestartedGenerations = [1, 2];
        private static readonly string[] s_expectedInteractions = ["create", "delete"];
        private static readonly string[] s_expectedFailureOperations = ["create", "failure", "delete"];
    }
}
