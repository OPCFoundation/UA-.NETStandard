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
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

// The obsolete RequestCompleted is the direct way to complete a request in these tests.
#pragma warning disable CS0618

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Verifies the lock-free request table of the <see cref="RequestManager"/> and that
    /// drains, Cancel and Session close still see every request started before them while
    /// registrations and completions run concurrently.
    /// </summary>
    [TestFixture]
    [Category("RequestManager")]
    public class RequestTableTests
    {
        [TestCase(1, 16)]
        [TestCase(16, 16)]
        [TestCase(17, 32)]
        [TestCase(4000, 4096)]
        public void CapacityIsAPowerOfTwoOfAtLeastTheProbeLimit(int capacity, int expected)
        {
            Assert.That(new RequestTable<TestEntry>(capacity).Capacity, Is.EqualTo(expected));
        }

        [Test]
        public void AddIsIdempotentAndRemoveSucceedsOnce()
        {
            var table = new RequestTable<TestEntry>(64);
            var entry = new TestEntry(7);
            var sameKey = new TestEntry(7);

            table.Add(entry);
            table.Add(entry);
            table.Add(sameKey);

            Assert.That(table.GetEntries(), Is.EquivalentTo(new[] { entry, sameKey }), "entries are tracked by reference");
            Assert.That(table.Contains(entry), Is.True);
            Assert.That(table.Remove(entry), Is.True);
            Assert.That(table.Remove(entry), Is.False);
            Assert.That(table.Contains(entry), Is.False);
            Assert.That(table.Contains(sameKey), Is.True);
            Assert.That(table.Remove(new TestEntry(7)), Is.False, "an entry never added is not removed");

            // an entry can be added again after it was removed.
            table.Add(entry);
            Assert.That(table.Contains(entry), Is.True);
        }

        [Test]
        public void EntriesBeyondTheProbedSlotsOverflowAndAreStillTracked()
        {
            var table = new RequestTable<TestEntry>(16);
            var entries = new List<TestEntry>();
            for (int ii = 0; ii < 40; ii++)
            {
                var entry = new TestEntry(5);
                entries.Add(entry);
                table.Add(entry);
            }

            Assert.That(table.OverflowCount, Is.EqualTo(40 - 16));
            Assert.That(entries.TrueForAll(table.Contains), Is.True);
            Assert.That(table.GetEntries(), Is.EquivalentTo(entries));
            Assert.That(entries.TrueForAll(table.Remove), Is.True);
            Assert.That(table.GetEntries(), Is.Empty);
            Assert.That(table.OverflowCount, Is.Zero);

            foreach (TestEntry entry in entries)
            {
                table.Add(entry);
            }
            Assert.That(table.Clear(), Is.EquivalentTo(entries));
            Assert.That(table.GetEntries(), Is.Empty);
            Assert.That(entries.TrueForAll(entry => !table.Contains(entry)), Is.True);
        }

        /// <summary>
        /// Writers add and remove their own entries while another thread enumerates: every
        /// entry is removed exactly once and the table ends empty.
        /// </summary>
        [Test]
        public async Task ConcurrentAddRemoveAndEnumerationLeaveTheTableEmptyAsync()
        {
            var table = new RequestTable<TestEntry>(256);
            using var stop = new CancellationTokenSource();
            int failedRemovals = 0;
            int seenByScanner = 0;
            Task scanner = Task.Run(() =>
            {
                while (!stop.IsCancellationRequested)
                {
                    Interlocked.Add(ref seenByScanner, table.GetEntries().Count);
                }
            });
            var writers = new Task[8];
            for (int writer = 0; writer < writers.Length; writer++)
            {
                uint seed = (uint)writer * 1_000_000;
                writers[writer] = Task.Run(() =>
                {
                    var mine = new TestEntry[64];
                    for (int round = 0; round < 2000; round++)
                    {
                        for (int ii = 0; ii < mine.Length; ii++)
                        {
                            mine[ii] = new TestEntry(seed++);
                            table.Add(mine[ii]);
                        }
                        foreach (TestEntry entry in mine)
                        {
                            if (!table.Remove(entry))
                            {
                                Interlocked.Increment(ref failedRemovals);
                            }
                        }
                    }
                });
            }

            await Task.WhenAll(writers).WaitAsync(TimeSpan.FromMinutes(2)).ConfigureAwait(false);
            stop.Cancel();
            await scanner.ConfigureAwait(false);

            Assert.That(failedRemovals, Is.Zero);
            Assert.That(table.GetEntries(), Is.Empty);
            Assert.That(table.OverflowCount, Is.Zero);
            TestContext.Out.WriteLine($"entries seen by the scanner: {seenByScanner}");
        }

        private sealed class TestEntry : IRequestTableEntry
        {
            public TestEntry(uint key)
            {
                TableKey = key;
            }

            public uint TableKey { get; }

            public int TableSlot { get; set; } = RequestTable<TestEntry>.kNoSlot;
        }
        /// <summary>
        /// A drain started while many requests are executing waits for each of
        /// them, however the completions interleave across threads, and completes exactly
        /// once after the last one.
        /// </summary>
        [Test]
        [Category("NodeManagerLifecycle")]
        public async Task DrainWaitsForEveryRequestStartedBeforeItWhileThreadsCompleteThemAsync()
        {
            using RequestManager manager = CreateManager();
            const int count = 2000;
            var lifetimes = new RequestLifetime[count];
            var contexts = new OperationContext[count];
            for (int ii = 0; ii < count; ii++)
            {
                lifetimes[ii] = new RequestLifetime();
                contexts[ii] = CreateContext((uint)ii, lifetimes[ii]);
                manager.RequestReceived(contexts[ii]);
            }

            Task drain = manager.WaitForCurrentRequestsAsync().AsTask();
            Assert.That(drain.IsCompleted, Is.False);

            // Complete all but the last request from several threads, interleaved with
            // requests that start after the drain and must not extend it.
            int next = -1;
            var workers = new Task[8];
            for (int worker = 0; worker < workers.Length; worker++)
            {
                workers[worker] = Task.Run(() =>
                {
                    int index;
                    while ((index = Interlocked.Increment(ref next)) < count - 1)
                    {
                        using var lateLifetime = new RequestLifetime();
                        OperationContext late = CreateContext(uint.MaxValue, lateLifetime);
                        manager.RequestReceived(late);
                        manager.RequestCompleted(contexts[index]);
                        manager.RequestCompleted(late);
                    }
                });
            }
            await Task.WhenAll(workers).ConfigureAwait(false);

            Assert.That(drain.IsCompleted, Is.False, "the drain completed before the last request it waits for");
            manager.RequestCompleted(contexts[count - 1]);
            await drain.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            Assert.That(drain.Status, Is.EqualTo(TaskStatus.RanToCompletion));
            Assert.That(lifetimes.All(lifetime => lifetime.StatusCode == StatusCodes.Good), Is.True,
                "completion marks each lifetime completed without cancelling it");
            foreach (RequestLifetime lifetime in lifetimes)
            {
                lifetime.Dispose();
            }
        }

        /// <summary>
        /// A Session close racing registrations cancels every request of the Session that
        /// was registered before it, exactly once, and none of another Session.
        /// </summary>
        [Test]
        public async Task SessionCloseCancelsEveryEarlierRequestExactlyOnceWhileOthersRegisterAsync()
        {
            using RequestManager manager = CreateManager();
            var closing = new Mock<ISession>();
            closing.Setup(s => s.Id).Returns(new NodeId("Closing", 0));
            var other = new Mock<ISession>();
            other.Setup(s => s.Id).Returns(new NodeId("Other", 0));

            const int count = 1000;
            var before = new List<(OperationContext Context, RequestLifetime Lifetime)>();
            for (int ii = 0; ii < count; ii++)
            {
                var lifetime = new RequestLifetime();
                var context = new OperationContext(
                    new RequestHeader { RequestHandle = (uint)ii }, null!, RequestType.Read, lifetime, closing.Object);
                manager.RequestReceived(context);
                before.Add((context, lifetime));
            }

            using var stop = new CancellationTokenSource();
            int otherCancelled = 0;
            Task churn = Task.Run(() =>
            {
                while (!stop.IsCancellationRequested)
                {
                    using var lifetime = new RequestLifetime();
                    var context = new OperationContext(
                        new RequestHeader { RequestHandle = 1 }, null!, RequestType.Read, lifetime, other.Object);
                    manager.RequestReceived(context);
                    if (lifetime.CancellationToken.IsCancellationRequested)
                    {
                        Interlocked.Increment(ref otherCancelled);
                    }
                    manager.RequestCompleted(context);
                }
            });

            uint aborted = manager.CancelSessionRequests(closing.Object.Id, 0, StatusCodes.BadSessionClosed);
            uint again = manager.CancelSessionRequests(closing.Object.Id, 0, StatusCodes.BadSessionClosed);
            stop.Cancel();
            await churn.ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(aborted, Is.EqualTo(count));
                Assert.That(again, Is.Zero, "a request is cancelled once");
                Assert.That(before.All(entry => entry.Context.OperationStatus.Code == StatusCodes.BadSessionClosed), Is.True);
                Assert.That(otherCancelled, Is.Zero);
            });
            foreach ((OperationContext context, RequestLifetime lifetime) in before)
            {
                manager.RequestCompleted(context);
                lifetime.Dispose();
            }
        }

        /// <summary>
        /// A Cancel by request handle finds every matching request wherever it is in the table.
        /// </summary>
        [Test]
        public void CancelByHandleFindsEveryMatchingRequest()
        {
            using RequestManager manager = CreateManager();
            var session = new Mock<ISession>();
            session.Setup(s => s.Id).Returns(new NodeId("CancelByHandle", 0));
            var lifetimes = new List<RequestLifetime>();
            var matching = new List<OperationContext>();
            for (int ii = 0; ii < 256; ii++)
            {
                var lifetime = new RequestLifetime();
                lifetimes.Add(lifetime);
                uint handle = ii % 2 == 0 ? 42u : (uint)ii;
                var context = new OperationContext(
                    new RequestHeader { RequestHandle = handle }, null!, RequestType.Read, lifetime, session.Object);
                manager.RequestReceived(context);
                if (handle == 42)
                {
                    matching.Add(context);
                }
            }

            manager.CancelRequests(session.Object.Id, 42, out uint cancelCount);

            Assert.That(cancelCount, Is.EqualTo((uint)matching.Count));
            Assert.That(matching.All(context => context.OperationStatus.Code == StatusCodes.BadRequestCancelledByClient), Is.True);
            Assert.That(lifetimes.Count(lifetime => lifetime.CancellationToken.IsCancellationRequested), Is.EqualTo(matching.Count));
            lifetimes.ForEach(lifetime => lifetime.Dispose());
        }

        private static RequestManager CreateManager()
        {
            var server = new Mock<IServerInternal>();
            server.Setup(s => s.Telemetry).Returns(NUnitTelemetryContext.Create());
            return new RequestManager(server.Object);
        }

        private static OperationContext CreateContext(uint requestHandle, RequestLifetime lifetime)
        {
            return new OperationContext(
                new RequestHeader { RequestHandle = requestHandle },
                null,
                RequestType.Read,
                lifetime);
        }
    }
}
