/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Verifies per-item event ordering without serializing independent monitored-item permission checks.
    /// </summary>
    [TestFixture]
    [Category("MonitoredNode")]
    public sealed class EventDeliveryOrderRegressionTests
    {
        /// <summary>
        /// Verifies that a blocked event item retains enqueue order while another item continues delivery
        /// independently.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ParallelEventItemsKeepEnqueueOrderWhileIndependentItemsProgressAsync(bool serverNode)
        {
            var node = new BaseObjectState(null) { NodeId = serverNode ? ObjectIds.Server : new NodeId(100, 1) };
            var server = new Mock<IServerInternal>();
            var manager = new Mock<IAsyncNodeManager>();
            IFilterTarget first = Mock.Of<IFilterTarget>();
            IFilterTarget second = Mock.Of<IFilterTarget>();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var independentFirst = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var secondEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int blocked = 0;
            int delivered = 0;
            manager.Setup(value => value.ValidateEventRolePermissionsAsync(
                    It.IsAny<IEventMonitoredItem>(), It.IsAny<IFilterTarget>(), It.IsAny<CancellationToken>()))
                .Returns(async (IEventMonitoredItem _, IFilterTarget target, CancellationToken ct) =>
                {
                    if (ReferenceEquals(target, first) && Interlocked.CompareExchange(ref blocked, 1, 0) == 0)
                    {
                        entered.TrySetResult(true);
                        await release.Task.WaitAsync(ct).ConfigureAwait(false);
                    }
                    return ServiceResult.Good;
                });
            using var monitored = new MonitoredNode2(manager.Object, server.Object, node, !serverNode);
            var queues = new ConcurrentQueue<int>[2];
            for (int i = 0; i < queues.Length; i++)
            {
                var queue = new ConcurrentQueue<int>();
                queues[i] = queue;
                var item = new Mock<IEventMonitoredItem>();
                item.SetupGet(value => value.Id).Returns((uint)i + 1);
                item.Setup(value => value.QueueEvent(It.IsAny<IFilterTarget>()))
                    .Callback<IFilterTarget>(target =>
                    {
                        int value = ReferenceEquals(target, first) ? 1 : 2;
                        queue.Enqueue(value);
                        if (value == 1 && !release.Task.IsCompleted)
                        {
                            independentFirst.TrySetResult(true);
                        }
                        if (value == 2)
                        {
                            secondEntered.TrySetResult(true);
                        }
                        if (Interlocked.Increment(ref delivered) == 4)
                        {
                            finished.TrySetResult(true);
                        }
                    });
                monitored.Add(item.Object);
            }
            ISystemContext context = Mock.Of<ISystemContext>();
            await monitored.OnReportEventAsync(context, node, first).ConfigureAwait(false);
            bool independentProgress = false;
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                await monitored.OnReportEventAsync(context, node, second).ConfigureAwait(false);
                await Task.WhenAny(independentFirst.Task, secondEntered.Task)
                    .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                independentProgress = independentFirst.Task.IsCompleted;
            }
            finally
            {
                release.TrySetResult(true);
            }
            await finished.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(independentProgress, Is.True);
                Assert.That(queues[0].ToArray(), Is.EqualTo(s_order));
                Assert.That(queues[1].ToArray(), Is.EqualTo(s_order));
            });
        }

        /// <summary>
        /// Every item gets the events of a notifier in the order they were reported when
        /// the items of each event are queued in ranges on several threads, also when some
        /// permission checks complete asynchronously.
        /// </summary>
        [Test]
        public async Task SplitEventsKeepTheOrderOfEveryItemAsync([Values(false, true)] bool asyncPermissions)
        {
            const int itemCount = 200;
            const int eventCount = 50;
            var node = new BaseObjectState(null) { NodeId = ObjectIds.Server };
            IFilterTarget[] events = CreateEvents(eventCount);
            var manager = new Mock<IAsyncNodeManager>();
            manager.Setup(value => value.ValidateEventRolePermissionsAsync(
                    It.IsAny<IEventMonitoredItem>(), It.IsAny<IFilterTarget>(), It.IsAny<CancellationToken>()))
                .Returns(async (IEventMonitoredItem item, IFilterTarget target, CancellationToken _) =>
                {
                    if (asyncPermissions && (item.Id + Array.IndexOf(events, target)) % 5 == 0)
                    {
                        await Task.Yield();
                    }
                    return ServiceResult.Good;
                });
            using var monitored = new MonitoredNode2(manager.Object, new Mock<IServerInternal>().Object, node)
            {
                ParallelEventDeliveryBudget = -1,
                MinItemsPerEventDeliveryRange = 8
            };
            ConcurrentQueue<int>[] queues = AddRecordingItems(monitored, itemCount, events, null);

            ISystemContext context = Mock.Of<ISystemContext>();
            foreach (IFilterTarget target in events)
            {
                await monitored.OnReportEventAsync(context, node, target).ConfigureAwait(false);
            }
            await monitored.WaitForPendingNotificationsAsync(CancellationToken.None)
                .AsTask().WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);

            int[] expected = [.. Enumerable.Range(0, eventCount)];
            for (int ii = 0; ii < queues.Length; ii++)
            {
                Assert.That(queues[ii].ToArray(), Is.EqualTo(expected), $"item {ii + 1}");
            }
        }

        /// <summary>
        /// The ranges of an event are queued at the same time on different threads.
        /// </summary>
        [Test]
        public async Task SplitEventIsQueuedOnSeveralThreadsAtOnceAsync()
        {
            Assume.That(MonitoredNode2.MaxEventDeliveryRanges, Is.GreaterThan(1), "needs two cores");

            var node = new BaseObjectState(null) { NodeId = ObjectIds.Server };
            IFilterTarget[] events = CreateEvents(1);
            var manager = new Mock<IAsyncNodeManager>();
            manager.Setup(value => value.ValidateEventRolePermissionsAsync(
                    It.IsAny<IEventMonitoredItem>(), It.IsAny<IFilterTarget>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ServiceResult>(ServiceResult.Good));
            using var monitored = new MonitoredNode2(manager.Object, new Mock<IServerInternal>().Object, node)
            {
                ParallelEventDeliveryBudget = -1,
                MinItemsPerEventDeliveryRange = 16
            };

            // the second item queued waits inside QueueEvent until another item is queued
            // at the same time, which only a delivery on another thread can do.
            int calls = 0;
            int inside = 0;
            int mostInside = 0;
            bool concurrent = false;
            AddRecordingItems(monitored, 64, events, () =>
            {
                int now = Interlocked.Increment(ref inside);
                InterlockedMax(ref mostInside, now);
                try
                {
                    if (Interlocked.Increment(ref calls) == 2)
                    {
                        concurrent = SpinWait.SpinUntil(
                            () => Volatile.Read(ref mostInside) > 1,
                            TimeSpan.FromSeconds(10));
                    }
                }
                finally
                {
                    Interlocked.Decrement(ref inside);
                }
            });

            await monitored.OnReportEventAsync(Mock.Of<ISystemContext>(), node, events[0]).ConfigureAwait(false);
            await monitored.WaitForPendingNotificationsAsync(CancellationToken.None)
                .AsTask().WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);

            Assert.That(calls, Is.EqualTo(64));
            Assert.That(concurrent, Is.True);
        }

        /// <summary>
        /// No item gets the next event before every item of a split event got the previous
        /// one, also while the delivery to one item waits for its permission check.
        /// </summary>
        [Test]
        public async Task NextEventWaitsForEveryItemOfASplitEventAsync()
        {
            const int itemCount = 100;
            const uint blockedItem = 50;
            var node = new BaseObjectState(null) { NodeId = ObjectIds.Server };
            IFilterTarget[] events = CreateEvents(2);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var manager = new Mock<IAsyncNodeManager>();
            manager.Setup(value => value.ValidateEventRolePermissionsAsync(
                    It.IsAny<IEventMonitoredItem>(), It.IsAny<IFilterTarget>(), It.IsAny<CancellationToken>()))
                .Returns(async (IEventMonitoredItem item, IFilterTarget target, CancellationToken ct) =>
                {
                    if (item.Id == blockedItem && ReferenceEquals(target, events[0]))
                    {
                        entered.TrySetResult(true);
                        await release.Task.WaitAsync(ct).ConfigureAwait(false);
                    }
                    return ServiceResult.Good;
                });
            using var monitored = new MonitoredNode2(manager.Object, new Mock<IServerInternal>().Object, node)
            {
                ParallelEventDeliveryBudget = -1,
                MinItemsPerEventDeliveryRange = 8
            };
            ConcurrentQueue<int>[] queues = AddRecordingItems(monitored, itemCount, events, null);

            ISystemContext context = Mock.Of<ISystemContext>();
            int[][] beforeRelease;
            try
            {
                await monitored.OnReportEventAsync(context, node, events[0]).ConfigureAwait(false);
                await monitored.OnReportEventAsync(context, node, events[1]).ConfigureAwait(false);
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                bool othersQueued = SpinWait.SpinUntil(
                    () => queues.Sum(queue => queue.Count) >= itemCount - 1,
                    TimeSpan.FromSeconds(10));
                Assert.That(othersQueued, Is.True, "the other items got the first event");

                // give a wrongly started second event time to arrive.
                await Task.Delay(100).ConfigureAwait(false);
                beforeRelease = [.. queues.Select(queue => queue.ToArray())];
            }
            finally
            {
                release.TrySetResult(true);
            }
            await monitored.WaitForPendingNotificationsAsync(CancellationToken.None)
                .AsTask().WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);

            for (int ii = 0; ii < itemCount; ii++)
            {
                int[] expectedBefore = ii + 1 == blockedItem ? [] : [0];
                Assert.That(beforeRelease[ii], Is.EqualTo(expectedBefore), $"item {ii + 1} before release");
                Assert.That(queues[ii].ToArray(), Is.EqualTo(s_twoEvents), $"item {ii + 1}");
            }
        }

        private static IFilterTarget[] CreateEvents(int count)
        {
            var events = new IFilterTarget[count];
            for (int ii = 0; ii < count; ii++)
            {
                events[ii] = new Mock<IFilterTarget>().Object;
            }
            return events;
        }

        /// <summary>
        /// Adds items with ids 1 to <paramref name="count"/> that record the index of each
        /// event queued to them.
        /// </summary>
        private static ConcurrentQueue<int>[] AddRecordingItems(
            MonitoredNode2 monitored,
            int count,
            IFilterTarget[] events,
            Action? onQueued)
        {
            var queues = new ConcurrentQueue<int>[count];
            for (int ii = 0; ii < count; ii++)
            {
                var queue = new ConcurrentQueue<int>();
                queues[ii] = queue;
                var item = new Mock<IEventMonitoredItem>();
                item.SetupGet(value => value.Id).Returns((uint)ii + 1);
                item.Setup(value => value.QueueEvent(It.IsAny<IFilterTarget>()))
                    .Callback<IFilterTarget>(target =>
                    {
                        queue.Enqueue(Array.IndexOf(events, target));
                        onQueued?.Invoke();
                    });
                monitored.Add(item.Object);
            }
            return queues;
        }

        private static void InterlockedMax(ref int location, int value)
        {
            int current = Volatile.Read(ref location);
            while (value > current)
            {
                int previous = Interlocked.CompareExchange(ref location, value, current);
                if (previous == current)
                {
                    return;
                }
                current = previous;
            }
        }

        /// <summary>
        /// Defines the event order that each monitored item must preserve independently.
        /// </summary>
        private static readonly int[] s_order = [1, 2];

        private static readonly int[] s_twoEvents = [0, 1];
    }
}
