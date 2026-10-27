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

// CA2000: test code; many disposables are ownership-transferred to test fixtures or short-lived,
// making CA2000 noisy without a real leak risk. Disabled file-level for the suite.
#pragma warning disable CA2000
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.Subscriptions.Fakes;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Subscriptions
{
    /// <summary>
    /// Covers subscription management, Publish workers, and acknowledgement handling.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("SubscriptionManager")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class SubscriptionManagerTests
    {
        [SetUp]
        public void SetUp()
        {
            m_session = new FakeSubscriptionManagerContext();
            m_telemetry = NUnitTelemetryContext.Create();
            m_mockNotificationDataHandler = new Mock<ISubscriptionNotificationHandler>();
            m_subscriptionManager = new SubscriptionManager(
                m_session, m_telemetry.LoggerFactory, DiagnosticsMasks.All);
        }

        [TearDown]
        public void TearDown()
        {
            m_subscriptionManager.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        [Test]
        public async Task AddAndRemoveOfSubscription1Async()
        {
            ILoggerFactory loggerFactory = m_telemetry.LoggerFactory;
            var session = new FakeSubscriptionManagerContext();
            OptionsMonitor<SubscriptionOptions> so1 = OptionsFactory.Create<SubscriptionOptions>();
            OptionsMonitor<SubscriptionOptions> so2 = OptionsFactory.Create<SubscriptionOptions>();

            var ms1 = new FakeManagedSubscription { Id = 1 };
            var ms2 = new FakeManagedSubscription { Id = 2 };

            var sut = new SubscriptionManager(session,
                loggerFactory, DiagnosticsMasks.None);

            session.CreateSubscriptionFactory = (handler, options, queue) =>
            {
                Assert.That(queue, Is.SameAs(sut));
                if (ReferenceEquals(options, so1))
                {
                    return ms1;
                }
                if (ReferenceEquals(options, so2))
                {
                    return ms2;
                }
                throw new InvalidOperationException("unexpected options");
            };

            Assert.That(sut.PublishWorkerCount, Is.Zero);

            // Test adding and removing a subscription from
            ISubscription s1 = sut.Add(m_mockNotificationDataHandler.Object, so1);
            ISubscription s2 = sut.Add(m_mockNotificationDataHandler.Object, so2);
            Assert.That(sut.Count, Is.EqualTo(2));

            ServiceResultException ex = Assert.Throws<ServiceResultException>(() => sut.Add(m_mockNotificationDataHandler.Object, so2));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadAlreadyExists));
            await Task.Delay(100).ConfigureAwait(false); // Give time to workers to start
            Assert.That(sut.PublishWorkerCount, Is.Zero);
            await sut.CompleteAsync(ms1, 1, default).ConfigureAwait(false);
            Assert.That(sut.Count, Is.EqualTo(1));
            Assert.That(sut.Items, Does.Contain(s2));

            Assert.That(sut.PublishControlCycles, Is.GreaterThan(0));
            await sut.DisposeAsync().ConfigureAwait(false);
            Assert.That(sut.Count, Is.Zero);
            Assert.That(sut.PublishWorkerCount, Is.Zero);
            // Verify the so1 subscription was created exactly once.
            Assert.That(session.CreateSubscriptionCalls
                .Count(c => ReferenceEquals(c.Options, so1)), Is.EqualTo(1));
        }

        [Test]
        public async Task AddAndRemoveOfSubscription2Async()
        {
            ILoggerFactory loggerFactory = m_telemetry.LoggerFactory;
            var session = new FakeSubscriptionManagerContext();
            OptionsMonitor<SubscriptionOptions> so1 = OptionsFactory.Create<SubscriptionOptions>();
            OptionsMonitor<SubscriptionOptions> so2 = OptionsFactory.Create<SubscriptionOptions>();

            var ms1 = new FakeManagedSubscription { Id = 1 };
            var ms2 = new FakeManagedSubscription { Id = 2 };

            var sut = new SubscriptionManager(session,
                loggerFactory, DiagnosticsMasks.None);

            session.CreateSubscriptionFactory = (handler, options, queue) =>
            {
                Assert.That(queue, Is.SameAs(sut));
                if (ReferenceEquals(options, so1))
                {
                    return ms1;
                }
                if (ReferenceEquals(options, so2))
                {
                    return ms2;
                }
                throw new InvalidOperationException("unexpected options");
            };

            Assert.That(sut.PublishWorkerCount, Is.Zero);

            // Test adding and removing a subscription from
            ISubscription s1 = sut.Add(m_mockNotificationDataHandler.Object, so1);
            ISubscription s2 = sut.Add(m_mockNotificationDataHandler.Object, so2);
            Assert.That(sut.Count, Is.EqualTo(2));

            ms1.Created = true;
            sut.Update();
            // Wait for the worker controller to spin up workers. CreatedCount=1
            // but MinPublishWorkerCount defaults to 2, so the desired count is 2.
            await WaitForPublishWorkerCountAsync(sut, 2).ConfigureAwait(false);

            await sut.CompleteAsync(ms2, 2, default).ConfigureAwait(false); // Remove s2
            Assert.That(sut.Count, Is.EqualTo(1));
            Assert.That(sut.Items, Does.Not.Contain(s2));

            // Workers stay at 2 because MinPublishWorkerCount=2 (default).
            Assert.That(sut.PublishWorkerCount, Is.EqualTo(2));
            Assert.That(sut.PublishControlCycles, Is.GreaterThan(0));

            await sut.CompleteAsync(ms1, 1, default).ConfigureAwait(false); // Remove s1
            Assert.That(sut.Count, Is.Zero);
            Assert.That(sut.Items, Does.Not.Contain(s1));

            await WaitForPublishWorkerCountAsync(sut, 0).ConfigureAwait(false);

            await sut.DisposeAsync().ConfigureAwait(false);
            Assert.That(sut.Count, Is.Zero);
            Assert.That(sut.PublishWorkerCount, Is.Zero);
        }

        [Test]
        public async Task CompleteForUncreatedIdDoesNotEvictPendingSubscriptionAsync()
        {
            ILoggerFactory loggerFactory = m_telemetry.LoggerFactory;
            var session = new FakeSubscriptionManagerContext();
            OptionsMonitor<SubscriptionOptions> so1 = OptionsFactory.Create<SubscriptionOptions>();

            // Subscription awaiting CreateSubscription
            var ms1 = new FakeManagedSubscription();

            var sut = new SubscriptionManager(session,
                loggerFactory, DiagnosticsMasks.None);

            session.CreateSubscriptionFactory = (handler, options, queue) =>
            {
                Assert.That(queue, Is.SameAs(sut));
                if (ReferenceEquals(options, so1))
                {
                    return ms1;
                }
                throw new InvalidOperationException("unexpected options");
            };

            ISubscription s1 = sut.Add(m_mockNotificationDataHandler.Object, so1);
            Assert.That(sut.Count, Is.EqualTo(1));

            // A deleted subscription acknowledges completion with an id already reset to 0
            await sut.CompleteAsync(new FakeManagedSubscription(), 0, default).ConfigureAwait(false);

            // Verify that the pending subscription still exists
            Assert.That(sut.Count, Is.EqualTo(1));
            Assert.That(sut.Items, Does.Contain(s1));

            await sut.DisposeAsync().ConfigureAwait(false);
            Assert.That(sut.Count, Is.Zero);
            Assert.That(sut.PublishWorkerCount, Is.Zero);
        }

        [Test]
        public async Task ScaleOutAndInOfPublishWorkersAsync()
        {
            ILoggerFactory loggerFactory = m_telemetry.LoggerFactory;
            var session = new FakeSubscriptionManagerContext();
            OptionsMonitor<SubscriptionOptions> so1 = OptionsFactory.Create<SubscriptionOptions>();
            OptionsMonitor<SubscriptionOptions> so2 = OptionsFactory.Create<SubscriptionOptions>();

            var ms1 = new FakeManagedSubscription { Id = 1 };
            var ms2 = new FakeManagedSubscription { Id = 2 };

            var sut = new SubscriptionManager(session,
                loggerFactory, DiagnosticsMasks.None);

            session.CreateSubscriptionFactory = (handler, options, queue) =>
            {
                Assert.That(queue, Is.SameAs(sut));
                if (ReferenceEquals(options, so1))
                {
                    return ms1;
                }
                if (ReferenceEquals(options, so2))
                {
                    return ms2;
                }
                throw new InvalidOperationException("unexpected options");
            };

            Assert.That(sut.PublishWorkerCount, Is.Zero);

            // Test adding and removing a subscription from
            ISubscription s1 = sut.Add(m_mockNotificationDataHandler.Object, so1);
            ISubscription s2 = sut.Add(m_mockNotificationDataHandler.Object, so2);
            Assert.That(sut.Count, Is.EqualTo(2));

            sut.MinPublishWorkerCount = 0;
            ms1.Created = true;
            sut.Update();
            await WaitForPublishWorkerCountAsync(sut, 1).ConfigureAwait(false);

            sut.MinPublishWorkerCount = 8;
            ms2.Created = true;
            sut.Update();
            await WaitForPublishWorkerCountAsync(sut, 8).ConfigureAwait(false);

            sut.MinPublishWorkerCount = 4;
            sut.Update();
            await WaitForPublishWorkerCountAsync(sut, 4).ConfigureAwait(false);

            sut.MinPublishWorkerCount = 0;
            sut.Update();
            await WaitForPublishWorkerCountAsync(sut, 2).ConfigureAwait(false);

            sut.MinPublishWorkerCount = 0;
            sut.MaxPublishWorkerCount = 1;
            sut.Update();
            await WaitForPublishWorkerCountAsync(sut, 1).ConfigureAwait(false);

            await sut.DisposeAsync().ConfigureAwait(false);
            Assert.That(sut.Count, Is.Zero);
            Assert.That(sut.PublishWorkerCount, Is.Zero);

            // Each subscription was created exactly once.
            Assert.That(session.CreateSubscriptionCalls
                .Count(c => ReferenceEquals(c.Options, so1)), Is.EqualTo(1));
            Assert.That(session.CreateSubscriptionCalls
                .Count(c => ReferenceEquals(c.Options, so2)), Is.EqualTo(1));
        }

        /// <summary>
        /// A single pending acknowledgement must not be rounded down to zero
        /// when two workers skip the wait-for-ack path.
        /// </summary>
        /// <param name="moreNotifications">Whether the server requests immediate publishing.</param>
        /// <param name="testCt">Test cancellation token.</param>
        [TestCase(false)]
        [TestCase(true)]
        [CancelAfter(10_000)]
        public async Task SingleAcknowledgementMakesProgressWithMultipleWorkersAsync(
            bool moreNotifications,
            CancellationToken testCt)
        {
            var context = new FakeSubscriptionManagerContext();
            var subscription = new FakeManagedSubscription
            {
                Id = 1,
                Created = true,
                CurrentPublishingInterval = TimeSpan.FromMilliseconds(1),
                CurrentKeepAliveCount = 1
            };
            context.CreateSubscriptionFactory = (_, _, _) => subscription;
            await using var manager = new SubscriptionManager(
                context, m_telemetry.LoggerFactory, DiagnosticsMasks.None)
            {
                MinPublishWorkerCount = 2,
                MaxPublishWorkerCount = 2
            };
            var primed = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var acknowledged = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            int completed = 0;
            context.OnPublishAsync = async (_, acknowledgements, ct) =>
            {
                foreach (SubscriptionAcknowledgement acknowledgement in acknowledgements)
                {
                    if (acknowledgement.SubscriptionId == 1 && acknowledgement.SequenceNumber == 42)
                    {
                        acknowledged.TrySetResult(true);
                    }
                }
                await Task.Delay(20, ct).ConfigureAwait(false);
                if (Interlocked.Increment(ref completed) >= 4)
                {
                    primed.TrySetResult(true);
                }
                return new PublishResponse
                {
                    SubscriptionId = 1,
                    NotificationMessage = new NotificationMessage(),
                    AvailableSequenceNumbers = [],
                    Results = acknowledgements.ConvertAll(_ => StatusCodes.Good),
                    MoreNotifications = moreNotifications,
                    ResponseHeader = new ResponseHeader { ServiceResult = StatusCodes.Good }
                };
            };
            manager.Add(m_mockNotificationDataHandler.Object, new OptionsMonitor<SubscriptionOptions>(
                new SubscriptionOptions { DisableUnboundedItemMode = true }));
            manager.Resume();
            await primed.Task.WaitAsync(testCt).ConfigureAwait(false);
            Assert.That(manager.PublishWorkerCount, Is.EqualTo(2));

            await manager.QueueAsync(subscription, subscription.MessageGeneration,
                new SubscriptionAcknowledgement { SubscriptionId = 1, SequenceNumber = 42 },
                testCt).ConfigureAwait(false);

            await acknowledged.Task.WaitAsync(TimeSpan.FromSeconds(2), testCt).ConfigureAwait(false);
            Assert.That(manager.PublishWorkerCount, Is.EqualTo(2));
        }

        /// <summary>
        /// Deferred acknowledgements survive empty Publish cycles and policy failures,
        /// while retirement removes only the retired subscription's entries.
        /// </summary>
        /// <param name="action">Action applied before the policy releases acknowledgements.</param>
        /// <param name="testCt">Test cancellation token.</param>
        [TestCase("release")]
        [TestCase("drop")]
        [TestCase("fault")]
        [TestCase("publish-fault")]
        [TestCase("complete")]
        [TestCase("complete-cancelled")]
        [CancelAfter(10_000)]
        public async Task DeferredAcknowledgementsAreRetainedUntilReleasedOrDroppedAsync(
            string action,
            CancellationToken testCt)
        {
            var context = new FakeSubscriptionManagerContext();
            int nextSubscription = 0;
            var partitions = new List<FakeManagedSubscription>();
            context.CreateSubscriptionFactory = (_, _, _) =>
            {
                var partition = new FakeManagedSubscription
                {
                    Id = (uint)Interlocked.Increment(ref nextSubscription),
                    Created = true,
                    CurrentPublishingInterval = TimeSpan.FromMilliseconds(1),
                    CurrentKeepAliveCount = 1
                };
                partitions.Add(partition);
                return partition;
            };
            await using var manager = new SubscriptionManager(
                context, m_telemetry.LoggerFactory, DiagnosticsMasks.None)
            {
                MinPublishWorkerCount = 2,
                MaxPublishWorkerCount = 2
            };
            var deferredIds = new ConcurrentDictionary<uint, bool>();
            var sentIds = new ConcurrentQueue<uint>();
            var policyFaulted = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            int release = 0;
            int faultOnce = 0;
            int publishFaultOnce = action == "publish-fault" ? 1 : 0;
            int publishCount = 0;
            context.OnPrepareAcknowledgements = acknowledgements =>
            {
                if (Interlocked.Exchange(ref faultOnce, 0) != 0)
                {
                    policyFaulted.TrySetResult(true);
                    throw new InvalidOperationException("Injected acknowledgement policy failure.");
                }
                if (Volatile.Read(ref release) != 0)
                {
                    return (acknowledgements, []);
                }
                foreach (SubscriptionAcknowledgement acknowledgement in acknowledgements)
                {
                    deferredIds.TryAdd(acknowledgement.SubscriptionId, true);
                }
                return ([], acknowledgements);
            };
            context.OnPublishAsync = async (_, acknowledgements, ct) =>
            {
                if (acknowledgements.Count != 0 && Interlocked.Exchange(ref publishFaultOnce, 0) != 0)
                {
                    throw new ServiceResultException(StatusCodes.BadTimeout);
                }
                foreach (SubscriptionAcknowledgement acknowledgement in acknowledgements)
                {
                    sentIds.Enqueue(acknowledgement.SubscriptionId);
                }
                Interlocked.Increment(ref publishCount);
                await Task.Delay(20, ct).ConfigureAwait(false);
                return new PublishResponse
                {
                    SubscriptionId = 1,
                    NotificationMessage = new NotificationMessage(),
                    AvailableSequenceNumbers = [],
                    Results = acknowledgements.ConvertAll(_ => StatusCodes.Good),
                    ResponseHeader = new ResponseHeader { ServiceResult = StatusCodes.Good }
                };
            };
            for (int i = 0; i < 2; i++)
            {
                manager.Add(m_mockNotificationDataHandler.Object, new OptionsMonitor<SubscriptionOptions>(
                    new SubscriptionOptions { DisableUnboundedItemMode = true }));
            }
            manager.Resume();
            await manager.QueueAsync(partitions[0], partitions[0].MessageGeneration,
                new SubscriptionAcknowledgement { SubscriptionId = 1, SequenceNumber = 10 },
                testCt).ConfigureAwait(false);
            await manager.QueueAsync(partitions[1], partitions[1].MessageGeneration,
                new SubscriptionAcknowledgement { SubscriptionId = 2, SequenceNumber = 11 },
                testCt).ConfigureAwait(false);
            await WaitUntilAsync(() => deferredIds.Count == 2, testCt).ConfigureAwait(false);
            int heldAt = Volatile.Read(ref publishCount);
            await WaitUntilAsync(() => Volatile.Read(ref publishCount) >= heldAt + 2, testCt)
                .ConfigureAwait(false);
            Assert.That(sentIds, Is.Empty);

            if (action == "drop")
            {
                await manager.RunWithPublishingQuiescedAsync(_ =>
                {
                    Assert.That(manager.DropPendingForSubscription(partitions[0], 1), Is.EqualTo(1));
                    return default;
                }, testCt).ConfigureAwait(false);
            }
            else if (action == "fault")
            {
                Volatile.Write(ref faultOnce, 1);
                await policyFaulted.Task.WaitAsync(testCt).ConfigureAwait(false);
            }
            else if (action is "complete" or "complete-cancelled")
            {
                using var completionCancellation = new CancellationTokenSource();
                if (action == "complete-cancelled")
                {
                    await completionCancellation.CancelAsync().ConfigureAwait(false);
                }
                await manager.CompleteAsync(partitions[0], 1, completionCancellation.Token).ConfigureAwait(false);
            }
            Volatile.Write(ref release, 1);

            bool retired = action is "drop" or "complete" or "complete-cancelled";
            int expectedCount = retired ? 1 : 2;
            await WaitUntilAsync(() => sentIds.Count >= expectedCount, testCt).ConfigureAwait(false);
            uint[] expected = retired ? [2u] : [1u, 2u];
            Assert.That(sentIds.ToArray(), Is.EquivalentTo(expected));
            if (action is "fault" or "publish-fault")
            {
                // The failing worker counts the failure after the policy signalled the test,
                // and another worker can finish sending first.
                await WaitUntilAsync(() => manager.BadPublishRequestCount >= 1, testCt).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Acknowledgements stamped by a subscription that has since been reset,
        /// moved to another server id, or whose id the server reused for a sibling
        /// must not be sent: they could confirm a message the client never processed.
        /// </summary>
        /// <param name="change">How the owning subscription changes after queuing.</param>
        /// <param name="testCt">Test cancellation token.</param>
        [TestCase("generation")]
        [TestCase("id-changed")]
        [TestCase("id-reused")]
        [CancelAfter(10_000)]
        public async Task StaleOwnedAcknowledgementsAreNotSentAsync(string change, CancellationToken testCt)
        {
            var context = new FakeSubscriptionManagerContext();
            List<FakeManagedSubscription> partitions = AddPartitionFactory(context);
            await using var manager = new SubscriptionManager(
                context, m_telemetry.LoggerFactory, DiagnosticsMasks.None)
            {
                MinPublishWorkerCount = 1,
                MaxPublishWorkerCount = 1
            };
            var sent = new ConcurrentQueue<SubscriptionAcknowledgement>();
            context.OnPublishAsync = async (_, acknowledgements, ct) =>
            {
                foreach (SubscriptionAcknowledgement acknowledgement in acknowledgements)
                {
                    sent.Enqueue(acknowledgement);
                }
                await Task.Delay(5, ct).ConfigureAwait(false);
                return CreatePublishResponse(3, acknowledgements);
            };
            for (int i = 0; i < 3; i++)
            {
                manager.Add(m_mockNotificationDataHandler.Object, new OptionsMonitor<SubscriptionOptions>(
                    new SubscriptionOptions { DisableUnboundedItemMode = true }));
            }
            FakeManagedSubscription owner = partitions[0];
            await manager.QueueAsync(owner, owner.MessageGeneration,
                new SubscriptionAcknowledgement { SubscriptionId = 1, SequenceNumber = 10 },
                testCt).ConfigureAwait(false);
            switch (change)
            {
                case "generation":
                    owner.MessageGeneration++;
                    break;
                case "id-changed":
                    owner.Id = 4;
                    break;
                default:
                    // The server reused id 1 for a subscription created after the owner.
                    partitions[1].Id = 1;
                    manager.OnSubscriptionCreated(partitions[1], 1);
                    await manager.QueueAsync(partitions[1], partitions[1].MessageGeneration,
                        new SubscriptionAcknowledgement { SubscriptionId = 1, SequenceNumber = 11 },
                        testCt).ConfigureAwait(false);
                    break;
            }
            FakeManagedSubscription control = partitions[2];
            await manager.QueueAsync(control, control.MessageGeneration,
                new SubscriptionAcknowledgement { SubscriptionId = 3, SequenceNumber = 99 },
                testCt).ConfigureAwait(false);

            manager.Resume();

            await WaitUntilAsync(() => sent.Any(ack => ack.SequenceNumber == 99), testCt)
                .ConfigureAwait(false);
            Assert.That(sent.Any(ack => ack.SequenceNumber == 10), Is.False);
            if (change == "id-reused")
            {
                await WaitUntilAsync(() => sent.Any(ack => ack.SequenceNumber == 11), testCt)
                    .ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Completing one subscription must neither cancel nor wait for a Publish
        /// request that is in flight for the session.
        /// </summary>
        /// <param name="testCt">Test cancellation token.</param>
        [Test]
        [CancelAfter(10_000)]
        public async Task CompleteDoesNotInterruptInFlightPublishAsync(CancellationToken testCt)
        {
            var context = new FakeSubscriptionManagerContext();
            List<FakeManagedSubscription> partitions = AddPartitionFactory(context);
            await using var manager = new SubscriptionManager(
                context, m_telemetry.LoggerFactory, DiagnosticsMasks.None)
            {
                MinPublishWorkerCount = 1,
                MaxPublishWorkerCount = 1
            };
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int cancelled = 0;
            context.OnPublishAsync = async (_, acknowledgements, ct) =>
            {
                started.TrySetResult(true);
                try
                {
                    await release.Task.WaitAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    Interlocked.Increment(ref cancelled);
                    throw;
                }
                return CreatePublishResponse(1, acknowledgements);
            };
            for (int i = 0; i < 2; i++)
            {
                manager.Add(m_mockNotificationDataHandler.Object, new OptionsMonitor<SubscriptionOptions>(
                    new SubscriptionOptions { DisableUnboundedItemMode = true }));
            }
            manager.Resume();
            try
            {
                await started.Task.WaitAsync(testCt).ConfigureAwait(false);

                await manager.CompleteAsync(partitions[1], partitions[1].Id, testCt)
                    .AsTask().WaitAsync(TimeSpan.FromSeconds(2), testCt).ConfigureAwait(false);

                Assert.That(Volatile.Read(ref cancelled), Is.Zero);
            }
            finally
            {
                release.TrySetResult(true);
            }
        }

        /// <summary>
        /// The acknowledgement-deferral callback is serialized across publish workers,
        /// matching the classic engine.
        /// </summary>
        /// <param name="testCt">Test cancellation token.</param>
        [Test]
        [CancelAfter(10_000)]
        public async Task DeferralCallbackIsNotInvokedConcurrentlyAsync(CancellationToken testCt)
        {
            var context = new FakeSubscriptionManagerContext();
            List<FakeManagedSubscription> partitions = AddPartitionFactory(context);
            await using var manager = new SubscriptionManager(
                context, m_telemetry.LoggerFactory, DiagnosticsMasks.None)
            {
                MinPublishWorkerCount = 3,
                MaxPublishWorkerCount = 3
            };
            int active = 0;
            int maxActive = 0;
            int calls = 0;
            context.OnPrepareAcknowledgements = acknowledgements =>
            {
                int current = Interlocked.Increment(ref active);
                int observed;
                while ((observed = Volatile.Read(ref maxActive)) < current &&
                    Interlocked.CompareExchange(ref maxActive, current, observed) != observed)
                {
                }
                Thread.Sleep(2);
                Interlocked.Decrement(ref active);
                Interlocked.Increment(ref calls);
                return (acknowledgements, []);
            };
            context.OnPublishAsync = async (_, acknowledgements, ct) =>
            {
                await Task.Delay(1, ct).ConfigureAwait(false);
                return CreatePublishResponse(1, acknowledgements);
            };
            manager.Add(m_mockNotificationDataHandler.Object, new OptionsMonitor<SubscriptionOptions>(
                new SubscriptionOptions { DisableUnboundedItemMode = true }));
            manager.Resume();
            FakeManagedSubscription owner = partitions[0];
            uint sequenceNumber = 0;
            while (Volatile.Read(ref calls) < 10)
            {
                await manager.QueueAsync(owner, owner.MessageGeneration,
                    new SubscriptionAcknowledgement { SubscriptionId = 1, SequenceNumber = ++sequenceNumber },
                    testCt).ConfigureAwait(false);
                await Task.Delay(2, testCt).ConfigureAwait(false);
            }

            Assert.That(Volatile.Read(ref maxActive), Is.EqualTo(1));
        }

        /// <summary>
        /// A growing deferred backlog is reported once, not on every Publish.
        /// </summary>
        /// <param name="testCt">Test cancellation token.</param>
        [Test]
        [CancelAfter(10_000)]
        public async Task DeferredBacklogWarningIsLoggedOnceAsync(CancellationToken testCt)
        {
            var provider = new CapturingLoggerProvider();
            using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(provider));
            var context = new FakeSubscriptionManagerContext();
            List<FakeManagedSubscription> partitions = AddPartitionFactory(context);
            await using var manager = new SubscriptionManager(context, loggerFactory, DiagnosticsMasks.None)
            {
                MinPublishWorkerCount = 1,
                MaxPublishWorkerCount = 1
            };
            int publishes = 0;
            context.OnPrepareAcknowledgements = acknowledgements => ([], acknowledgements);
            context.OnPublishAsync = async (_, acknowledgements, ct) =>
            {
                Interlocked.Increment(ref publishes);
                await Task.Delay(1, ct).ConfigureAwait(false);
                return CreatePublishResponse(1, acknowledgements);
            };
            manager.Add(m_mockNotificationDataHandler.Object, new OptionsMonitor<SubscriptionOptions>(
                new SubscriptionOptions { DisableUnboundedItemMode = true }));
            FakeManagedSubscription owner = partitions[0];
            for (uint sequenceNumber = 1; sequenceNumber <= 1100; sequenceNumber++)
            {
                await manager.QueueAsync(owner, owner.MessageGeneration,
                    new SubscriptionAcknowledgement { SubscriptionId = 1, SequenceNumber = sequenceNumber },
                    testCt).ConfigureAwait(false);
            }
            manager.Resume();

            await WaitUntilAsync(() => provider.WarningCount(kDeferredWarningText) >= 1, testCt)
                .ConfigureAwait(false);
            int publishesAtWarning = Volatile.Read(ref publishes);
            await WaitUntilAsync(() => Volatile.Read(ref publishes) >= publishesAtWarning + 5, testCt)
                .ConfigureAwait(false);

            Assert.That(provider.WarningCount(kDeferredWarningText), Is.EqualTo(1));
        }

        /// <summary>
        /// An acknowledgement for a message older than a Publish response that the response
        /// no longer lists is not sent: the server already removed that message (Part 4,
        /// 5.14.5.2). Listed and newer messages are still acknowledged.
        /// </summary>
        /// <param name="testCt">Test cancellation token.</param>
        [Test]
        [CancelAfter(10_000)]
        public async Task AcknowledgementsForMessagesTheServerNoLongerHoldsAreNotSentAsync(
            CancellationToken testCt)
        {
            var context = new FakeSubscriptionManagerContext();
            List<FakeManagedSubscription> partitions = AddPartitionFactory(context);
            await using var manager = new SubscriptionManager(
                context, m_telemetry.LoggerFactory, DiagnosticsMasks.None)
            {
                MinPublishWorkerCount = 1,
                MaxPublishWorkerCount = 1
            };
            var sent = new ConcurrentQueue<uint>();
            var windowRecorded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int calls = 0;
            context.OnPublishAsync = async (_, acknowledgements, ct) =>
            {
                foreach (SubscriptionAcknowledgement acknowledgement in acknowledgements)
                {
                    sent.Enqueue(acknowledgement.SequenceNumber);
                }
                await Task.Delay(5, ct).ConfigureAwait(false);
                PublishResponse response = CreatePublishResponse(1, acknowledgements);
                if (Interlocked.Increment(ref calls) == 1)
                {
                    response.NotificationMessage = CreateDataMessage(5);
                    response.AvailableSequenceNumbers = [4, 5];
                }
                return response;
            };
            manager.Add(m_mockNotificationDataHandler.Object, new OptionsMonitor<SubscriptionOptions>(
                new SubscriptionOptions { DisableUnboundedItemMode = true }));
            FakeManagedSubscription owner = partitions[0];
            owner.OnPublishReceivedAsyncFunc = (message, _, _) =>
            {
                if (message.SequenceNumber == 5)
                {
                    windowRecorded.TrySetResult(true);
                }
                return default;
            };
            manager.Resume();
            await windowRecorded.Task.WaitAsync(testCt).ConfigureAwait(false);

            foreach (uint sequenceNumber in new uint[] { 3, 4, 9 })
            {
                await manager.QueueAsync(owner, owner.MessageGeneration,
                    new SubscriptionAcknowledgement { SubscriptionId = 1, SequenceNumber = sequenceNumber },
                    testCt).ConfigureAwait(false);
            }

            await WaitUntilAsync(() => sent.Contains(4u) && sent.Contains(9u), testCt).ConfigureAwait(false);
            Assert.That(sent, Does.Not.Contain(3u));
        }

        /// <summary>
        /// A data response that lists no available sequence numbers means the server has no
        /// retransmission queue; the client should then not acknowledge (Part 4, 5.14.5.2).
        /// Other subscriptions are unaffected.
        /// </summary>
        /// <param name="testCt">Test cancellation token.</param>
        [Test]
        [CancelAfter(10_000)]
        public async Task NoAcknowledgementsAreSentWhenTheServerHasNoRetransmissionQueueAsync(
            CancellationToken testCt)
        {
            var context = new FakeSubscriptionManagerContext();
            List<FakeManagedSubscription> partitions = AddPartitionFactory(context);
            await using var manager = new SubscriptionManager(
                context, m_telemetry.LoggerFactory, DiagnosticsMasks.None)
            {
                MinPublishWorkerCount = 1,
                MaxPublishWorkerCount = 1
            };
            var sent = new ConcurrentQueue<SubscriptionAcknowledgement>();
            var windowRecorded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int calls = 0;
            context.OnPublishAsync = async (_, acknowledgements, ct) =>
            {
                foreach (SubscriptionAcknowledgement acknowledgement in acknowledgements)
                {
                    sent.Enqueue(acknowledgement);
                }
                await Task.Delay(5, ct).ConfigureAwait(false);
                PublishResponse response = CreatePublishResponse(1, acknowledgements);
                if (Interlocked.Increment(ref calls) == 1)
                {
                    response.NotificationMessage = CreateDataMessage(5);
                }
                return response;
            };
            for (int i = 0; i < 2; i++)
            {
                manager.Add(m_mockNotificationDataHandler.Object, new OptionsMonitor<SubscriptionOptions>(
                    new SubscriptionOptions { DisableUnboundedItemMode = true }));
            }
            FakeManagedSubscription owner = partitions[0];
            owner.OnPublishReceivedAsyncFunc = (message, _, _) =>
            {
                if (message.SequenceNumber == 5)
                {
                    windowRecorded.TrySetResult(true);
                }
                return default;
            };
            manager.Resume();
            await windowRecorded.Task.WaitAsync(testCt).ConfigureAwait(false);

            await manager.QueueAsync(owner, owner.MessageGeneration,
                new SubscriptionAcknowledgement { SubscriptionId = 1, SequenceNumber = 5 },
                testCt).ConfigureAwait(false);
            FakeManagedSubscription control = partitions[1];
            await manager.QueueAsync(control, control.MessageGeneration,
                new SubscriptionAcknowledgement { SubscriptionId = 2, SequenceNumber = 7 },
                testCt).ConfigureAwait(false);

            await WaitUntilAsync(() => sent.Any(ack => ack.SubscriptionId == 2), testCt).ConfigureAwait(false);
            Assert.That(sent.Any(ack => ack.SubscriptionId == 1), Is.False);
        }

        /// <summary>
        /// A response to a request sent before an id was claimed can come from the id's
        /// previous server subscription; it must not describe the new owner's queue.
        /// </summary>
        /// <param name="testCt">Test cancellation token.</param>
        [Test]
        [CancelAfter(10_000)]
        public async Task ResponsesToRequestsSentBeforeAClaimDoNotPruneAsync(CancellationToken testCt)
        {
            var context = new FakeSubscriptionManagerContext();
            List<FakeManagedSubscription> partitions = AddPartitionFactory(context);
            await using var manager = new SubscriptionManager(
                context, m_telemetry.LoggerFactory, DiagnosticsMasks.None)
            {
                MinPublishWorkerCount = 1,
                MaxPublishWorkerCount = 1
            };
            var sent = new ConcurrentQueue<uint>();
            var firstSent = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseFirst = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int calls = 0;
            context.OnPublishAsync = async (_, acknowledgements, ct) =>
            {
                foreach (SubscriptionAcknowledgement acknowledgement in acknowledgements)
                {
                    sent.Enqueue(acknowledgement.SequenceNumber);
                }
                PublishResponse response = CreatePublishResponse(1, acknowledgements);
                if (Interlocked.Increment(ref calls) == 1)
                {
                    firstSent.TrySetResult(true);
                    await releaseFirst.Task.WaitAsync(ct).ConfigureAwait(false);
                    // A late data response of the id's previous server subscription.
                    response.NotificationMessage = CreateDataMessage(50000);
                    return response;
                }
                await Task.Delay(5, ct).ConfigureAwait(false);
                return response;
            };
            manager.Add(m_mockNotificationDataHandler.Object, new OptionsMonitor<SubscriptionOptions>(
                new SubscriptionOptions { DisableUnboundedItemMode = true }));
            FakeManagedSubscription owner = partitions[0];
            manager.Resume();
            await firstSent.Task.WaitAsync(testCt).ConfigureAwait(false);

            manager.OnSubscriptionCreated(owner, 1);
            releaseFirst.TrySetResult(true);
            await WaitUntilAsync(() => Volatile.Read(ref calls) >= 2, testCt).ConfigureAwait(false);
            await manager.QueueAsync(owner, owner.MessageGeneration,
                new SubscriptionAcknowledgement { SubscriptionId = 1, SequenceNumber = 3 },
                testCt).ConfigureAwait(false);

            await WaitUntilAsync(() => sent.Contains(3u), testCt).ConfigureAwait(false);
            Assert.That(sent, Does.Contain(3u));
        }

        /// <summary>
        /// A response to a request sent before a reused id was claimed is not delivered to the
        /// new owner when the server published its message before the owner was created: it
        /// belongs to the id's previous subscription and would move the owner's cursor past its
        /// own first messages. Messages published later, or without the times to compare, are
        /// delivered as before.
        /// </summary>
        /// <param name="scenario">How the stale message relates to the owner's creation.</param>
        /// <param name="delivered">Whether the stale message is expected to be delivered.</param>
        /// <param name="testCt">Test cancellation token.</param>
        [TestCase("published-before-creation", false)]
        [TestCase("published-after-creation", true)]
        [TestCase("unknown-creation-time", true)]
        [TestCase("unknown-publish-time", true)]
        [CancelAfter(10_000)]
        public async Task MessagesOfAPreviousSubscriptionAreNotDeliveredToTheNewOwnerAsync(
            string scenario, bool delivered, CancellationToken testCt)
        {
            var createdAt = new DateTimeUtc(new DateTime(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc));
            var context = new FakeSubscriptionManagerContext();
            List<FakeManagedSubscription> partitions = AddPartitionFactory(context);
            await using var manager = new SubscriptionManager(
                context, m_telemetry.LoggerFactory, DiagnosticsMasks.None)
            {
                MinPublishWorkerCount = 1,
                MaxPublishWorkerCount = 1
            };
            var firstSent = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseFirst = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int calls = 0;
            context.OnPublishAsync = async (_, acknowledgements, ct) =>
            {
                PublishResponse response = CreatePublishResponse(1, acknowledgements);
                int call = Interlocked.Increment(ref calls);
                if (call == 1)
                {
                    firstSent.TrySetResult(true);
                    await releaseFirst.Task.WaitAsync(ct).ConfigureAwait(false);
                    response.NotificationMessage = CreateDataMessage(50000);
                    response.NotificationMessage.PublishTime = scenario switch
                    {
                        "published-after-creation" => createdAt.AddMilliseconds(1000),
                        "unknown-publish-time" => DateTimeUtc.MinValue,
                        _ => createdAt.AddMilliseconds(-1000)
                    };
                    return response;
                }
                if (call == 2)
                {
                    response.NotificationMessage = CreateDataMessage(1);
                    response.NotificationMessage.PublishTime = createdAt.AddMilliseconds(2000);
                }
                await Task.Delay(5, ct).ConfigureAwait(false);
                return response;
            };
            manager.Add(m_mockNotificationDataHandler.Object, new OptionsMonitor<SubscriptionOptions>(
                new SubscriptionOptions { DisableUnboundedItemMode = true }));
            FakeManagedSubscription owner = partitions[0];
            var received = new ConcurrentQueue<uint>();
            owner.OnPublishReceivedAsyncFunc = (message, _, _) =>
            {
                received.Enqueue(message.SequenceNumber);
                return default;
            };
            manager.Resume();
            await firstSent.Task.WaitAsync(testCt).ConfigureAwait(false);

            manager.OnSubscriptionCreated(
                owner, 1, scenario == "unknown-creation-time" ? default : createdAt);
            releaseFirst.TrySetResult(true);

            await WaitUntilAsync(() => received.Contains(1u), testCt).ConfigureAwait(false);
            Assert.That(received.Contains(50000u), Is.EqualTo(delivered));
        }

        /// <summary>
        /// While a stale subscription and its replacement share a reused id, Publish
        /// responses are dispatched to the most recently created one.
        /// </summary>
        /// <param name="testCt">Test cancellation token.</param>
        [Test]
        [CancelAfter(10_000)]
        public async Task ResponsesForAReusedIdAreDispatchedToTheNewestOwnerAsync(CancellationToken testCt)
        {
            var context = new FakeSubscriptionManagerContext();
            List<FakeManagedSubscription> partitions = AddPartitionFactory(context);
            await using var manager = new SubscriptionManager(
                context, m_telemetry.LoggerFactory, DiagnosticsMasks.None)
            {
                MinPublishWorkerCount = 1,
                MaxPublishWorkerCount = 1
            };
            context.OnPublishAsync = async (_, acknowledgements, ct) =>
            {
                await Task.Delay(5, ct).ConfigureAwait(false);
                return CreatePublishResponse(1, acknowledgements);
            };
            for (int i = 0; i < 2; i++)
            {
                manager.Add(m_mockNotificationDataHandler.Object, new OptionsMonitor<SubscriptionOptions>(
                    new SubscriptionOptions { DisableUnboundedItemMode = true }));
            }
            int staleDeliveries = 0;
            var newestDelivered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            partitions[0].OnPublishReceivedAsyncFunc = (_, _, _) =>
            {
                Interlocked.Increment(ref staleDeliveries);
                return default;
            };
            partitions[1].OnPublishReceivedAsyncFunc = (_, _, _) =>
            {
                newestDelivered.TrySetResult(true);
                return default;
            };
            partitions[1].Id = 1;
            manager.OnSubscriptionCreated(partitions[1], 1);
            manager.Resume();

            await newestDelivered.Task.WaitAsync(testCt).ConfigureAwait(false);
            Assert.That(Volatile.Read(ref staleDeliveries), Is.Zero);
        }

        /// <summary>
        /// A subscription whose reused id a newer subscription claimed stays superseded when
        /// the newer one completes first: its acknowledgements are not sent and responses for
        /// the id are not dispatched to it.
        /// </summary>
        /// <param name="testCt">Test cancellation token.</param>
        [Test]
        [CancelAfter(10_000)]
        public async Task SupersededOwnerStaysStaleAfterTheNewerOwnerCompletesAsync(CancellationToken testCt)
        {
            var context = new FakeSubscriptionManagerContext();
            List<FakeManagedSubscription> partitions = AddPartitionFactory(context);
            await using var manager = new SubscriptionManager(
                context, m_telemetry.LoggerFactory, DiagnosticsMasks.None)
            {
                MinPublishWorkerCount = 1,
                MaxPublishWorkerCount = 1
            };
            var sent = new ConcurrentQueue<SubscriptionAcknowledgement>();
            int publishes = 0;
            context.OnPublishAsync = async (_, acknowledgements, ct) =>
            {
                foreach (SubscriptionAcknowledgement acknowledgement in acknowledgements)
                {
                    sent.Enqueue(acknowledgement);
                }
                Interlocked.Increment(ref publishes);
                await Task.Delay(5, ct).ConfigureAwait(false);
                return CreatePublishResponse(1, acknowledgements);
            };
            for (int i = 0; i < 3; i++)
            {
                manager.Add(m_mockNotificationDataHandler.Object, new OptionsMonitor<SubscriptionOptions>(
                    new SubscriptionOptions { DisableUnboundedItemMode = true }));
            }
            FakeManagedSubscription superseded = partitions[0];
            FakeManagedSubscription newer = partitions[1];
            FakeManagedSubscription control = partitions[2];
            int supersededDeliveries = 0;
            superseded.OnPublishReceivedAsyncFunc = (_, _, _) =>
            {
                Interlocked.Increment(ref supersededDeliveries);
                return default;
            };
            manager.OnSubscriptionCreated(superseded, 1);
            newer.Id = 1;
            manager.OnSubscriptionCreated(newer, 1);
            await manager.CompleteAsync(newer, 1, testCt).ConfigureAwait(false);
            manager.Resume();

            await manager.QueueAsync(superseded, superseded.MessageGeneration,
                new SubscriptionAcknowledgement { SubscriptionId = 1, SequenceNumber = 7 },
                testCt).ConfigureAwait(false);
            await manager.QueueAsync(control, control.MessageGeneration,
                new SubscriptionAcknowledgement { SubscriptionId = 3, SequenceNumber = 9 },
                testCt).ConfigureAwait(false);

            await WaitUntilAsync(() => sent.Any(ack => ack.SubscriptionId == 3), testCt).ConfigureAwait(false);
            int observed = Volatile.Read(ref publishes);
            await WaitUntilAsync(() => Volatile.Read(ref publishes) >= observed + 3, testCt).ConfigureAwait(false);
            Assert.That(sent.Any(ack => ack.SubscriptionId == 1), Is.False);
            Assert.That(Volatile.Read(ref supersededDeliveries), Is.Zero);
        }

        /// <summary>
        /// Without a deferral policy, acknowledgements are validated and sent without the
        /// policy round-trip, and stale ones are still discarded.
        /// </summary>
        /// <param name="testCt">Test cancellation token.</param>
        [Test]
        [CancelAfter(10_000)]
        public async Task AcknowledgementsAreSentWithoutPolicyRoundTripWhenNoPolicyIsRegisteredAsync(
            CancellationToken testCt)
        {
            var context = new FakeSubscriptionManagerContext();
            List<FakeManagedSubscription> partitions = AddPartitionFactory(context);
            await using var manager = new SubscriptionManager(
                context, m_telemetry.LoggerFactory, DiagnosticsMasks.None)
            {
                MinPublishWorkerCount = 1,
                MaxPublishWorkerCount = 1
            };
            var sent = new ConcurrentQueue<SubscriptionAcknowledgement>();
            context.OnPublishAsync = async (_, acknowledgements, ct) =>
            {
                foreach (SubscriptionAcknowledgement acknowledgement in acknowledgements)
                {
                    sent.Enqueue(acknowledgement);
                }
                await Task.Delay(5, ct).ConfigureAwait(false);
                return CreatePublishResponse(1, acknowledgements);
            };
            manager.Add(m_mockNotificationDataHandler.Object, new OptionsMonitor<SubscriptionOptions>(
                new SubscriptionOptions { DisableUnboundedItemMode = true }));
            FakeManagedSubscription owner = partitions[0];
            manager.Resume();

            await manager.QueueAsync(owner, owner.MessageGeneration - 1,
                new SubscriptionAcknowledgement { SubscriptionId = 1, SequenceNumber = 4 },
                testCt).ConfigureAwait(false);
            await manager.QueueAsync(owner, owner.MessageGeneration,
                new SubscriptionAcknowledgement { SubscriptionId = 1, SequenceNumber = 5 },
                testCt).ConfigureAwait(false);

            await WaitUntilAsync(() => sent.Any(ack => ack.SequenceNumber == 5), testCt).ConfigureAwait(false);
            Assert.That(sent.Any(ack => ack.SequenceNumber == 4), Is.False, "A stale acknowledgement is not sent.");
            Assert.That(context.PrepareCalls, Is.Zero);
        }

        /// <summary>
        /// The batch is validated again after the deferral callback: an owner that is retired
        /// while the callback runs does not acknowledge, while other subscriptions still do.
        /// </summary>
        /// <param name="testCt">Test cancellation token.</param>
        [Test]
        [CancelAfter(10_000)]
        public async Task AcknowledgementsOfAnOwnerRetiredDuringTheCallbackAreNotSentAsync(
            CancellationToken testCt)
        {
            var context = new FakeSubscriptionManagerContext();
            List<FakeManagedSubscription> partitions = AddPartitionFactory(context);
            await using var manager = new SubscriptionManager(
                context, m_telemetry.LoggerFactory, DiagnosticsMasks.None)
            {
                MinPublishWorkerCount = 1,
                MaxPublishWorkerCount = 1
            };
            var sent = new ConcurrentQueue<SubscriptionAcknowledgement>();
            int retired = 0;
            for (int i = 0; i < 2; i++)
            {
                manager.Add(m_mockNotificationDataHandler.Object, new OptionsMonitor<SubscriptionOptions>(
                    new SubscriptionOptions { DisableUnboundedItemMode = true }));
            }
            FakeManagedSubscription owner = partitions[0];
            FakeManagedSubscription control = partitions[1];
            context.OnPrepareAcknowledgements = acknowledgements =>
            {
                // Simulates the owner being recreated while the application decides.
                if (acknowledgements.Any(ack => ack.SubscriptionId == 1) &&
                    Interlocked.Exchange(ref retired, 1) == 0)
                {
                    owner.MessageGeneration++;
                }
                return (acknowledgements, []);
            };
            context.OnPublishAsync = async (_, acknowledgements, ct) =>
            {
                foreach (SubscriptionAcknowledgement acknowledgement in acknowledgements)
                {
                    sent.Enqueue(acknowledgement);
                }
                await Task.Delay(5, ct).ConfigureAwait(false);
                return CreatePublishResponse(1, acknowledgements);
            };
            manager.Resume();

            await manager.QueueAsync(owner, owner.MessageGeneration,
                new SubscriptionAcknowledgement { SubscriptionId = 1, SequenceNumber = 7 },
                testCt).ConfigureAwait(false);
            await WaitUntilAsync(() => Volatile.Read(ref retired) != 0, testCt).ConfigureAwait(false);
            await manager.QueueAsync(control, control.MessageGeneration,
                new SubscriptionAcknowledgement { SubscriptionId = 2, SequenceNumber = 9 },
                testCt).ConfigureAwait(false);

            await WaitUntilAsync(() => sent.Any(ack => ack.SubscriptionId == 2), testCt).ConfigureAwait(false);
            Assert.That(sent.Any(ack => ack.SubscriptionId == 1), Is.False,
                "An acknowledgement whose owner was retired during the callback must not be sent.");
        }

        /// <summary>
        /// An acknowledgement provider decides instead of the legacy deferral policy: deferred
        /// acknowledgements are presented again with later requests until the provider sends
        /// them, and their handles identify the owner's message.
        /// </summary>
        /// <param name="testCt">Test cancellation token.</param>
        [Test]
        [CancelAfter(10_000)]
        public async Task AcknowledgementProviderDefersAndPresentsAgainAsync(CancellationToken testCt)
        {
            var context = new FakeSubscriptionManagerContext();
            List<FakeManagedSubscription> partitions = AddPartitionFactory(context);
            var provider = new TestAcknowledgementProvider();
            context.AcknowledgementProvider = provider;
            context.OnPrepareAcknowledgements = acknowledgements => (acknowledgements, []);
            await using var manager = new SubscriptionManager(
                context, m_telemetry.LoggerFactory, DiagnosticsMasks.None)
            {
                MinPublishWorkerCount = 1,
                MaxPublishWorkerCount = 1
            };
            var sent = new ConcurrentQueue<SubscriptionAcknowledgement>();
            context.OnPublishAsync = async (_, acknowledgements, ct) =>
            {
                foreach (SubscriptionAcknowledgement acknowledgement in acknowledgements)
                {
                    sent.Enqueue(acknowledgement);
                }
                await Task.Delay(5, ct).ConfigureAwait(false);
                return CreatePublishResponse(1, acknowledgements);
            };
            manager.Add(m_mockNotificationDataHandler.Object, new OptionsMonitor<SubscriptionOptions>(
                new SubscriptionOptions { DisableUnboundedItemMode = true }));
            FakeManagedSubscription owner = partitions[0];
            var expected = new AcknowledgementHandle(
                owner.GetAcknowledgementScope(owner.MessageGeneration), 5);
            provider.Defer = handle => handle == expected && !provider.Release;
            manager.Resume();

            await manager.QueueAsync(owner, owner.MessageGeneration,
                new SubscriptionAcknowledgement { SubscriptionId = 1, SequenceNumber = 5 },
                testCt).ConfigureAwait(false);
            await WaitUntilAsync(() => provider.Presented(expected) >= 3, testCt).ConfigureAwait(false);
            Assert.That(sent, Is.Empty, "A deferred acknowledgement is not sent.");

            provider.Release = true;
            await WaitUntilAsync(() => sent.Any(ack => ack.SequenceNumber == 5), testCt).ConfigureAwait(false);
            Assert.That(context.PrepareCalls, Is.Zero, "The legacy policy is not applied with a provider.");
        }

        /// <summary>
        /// A provider that throws defers the whole batch (fail-closed); the acknowledgements
        /// are sent once a later decision approves them.
        /// </summary>
        /// <param name="testCt">Test cancellation token.</param>
        [Test]
        [CancelAfter(10_000)]
        public async Task AcknowledgementProviderFailureDefersTheBatchAsync(CancellationToken testCt)
        {
            var context = new FakeSubscriptionManagerContext();
            List<FakeManagedSubscription> partitions = AddPartitionFactory(context);
            var provider = new TestAcknowledgementProvider { FailuresLeft = 2 };
            context.AcknowledgementProvider = provider;
            await using var manager = new SubscriptionManager(
                context, m_telemetry.LoggerFactory, DiagnosticsMasks.None)
            {
                MinPublishWorkerCount = 1,
                MaxPublishWorkerCount = 1
            };
            var sent = new ConcurrentQueue<uint>();
            context.OnPublishAsync = async (_, acknowledgements, ct) =>
            {
                foreach (SubscriptionAcknowledgement acknowledgement in acknowledgements)
                {
                    sent.Enqueue(acknowledgement.SequenceNumber);
                }
                await Task.Delay(5, ct).ConfigureAwait(false);
                return CreatePublishResponse(1, acknowledgements);
            };
            manager.Add(m_mockNotificationDataHandler.Object, new OptionsMonitor<SubscriptionOptions>(
                new SubscriptionOptions { DisableUnboundedItemMode = true }));
            FakeManagedSubscription owner = partitions[0];
            manager.Resume();

            await manager.QueueAsync(owner, owner.MessageGeneration,
                new SubscriptionAcknowledgement { SubscriptionId = 1, SequenceNumber = 8 },
                testCt).ConfigureAwait(false);

            await WaitUntilAsync(() => sent.Contains(8u), testCt).ConfigureAwait(false);
            Assert.That(Volatile.Read(ref provider.FailuresLeft), Is.Negative, "The provider failed twice first.");
            Assert.That(sent.Count(sequenceNumber => sequenceNumber == 8u), Is.EqualTo(1),
                "The acknowledgement is only sent once the provider approved it.");
        }

        /// <summary>
        /// Every acknowledgement ends exactly once and is reported to the observer: a sent
        /// one with the server's result, a stale one as retired, a deferred one when its
        /// owner retires, and the remaining ones as abandoned when the manager is disposed.
        /// </summary>
        /// <param name="testCt">Test cancellation token.</param>
        [Test]
        [CancelAfter(15_000)]
        public async Task EveryAcknowledgementIsReportedExactlyOnceAsync(CancellationToken testCt)
        {
            var context = new FakeSubscriptionManagerContext();
            List<FakeManagedSubscription> partitions = AddPartitionFactory(context);
            var provider = new TestAcknowledgementProvider();
            var observer = new TestAcknowledgementObserver();
            context.AcknowledgementProvider = provider;
            context.AcknowledgementObserver = observer;
            var manager = new SubscriptionManager(
                context, m_telemetry.LoggerFactory, DiagnosticsMasks.None)
            {
                MinPublishWorkerCount = 1,
                MaxPublishWorkerCount = 1
            };
            context.OnPublishAsync = async (_, acknowledgements, ct) =>
            {
                await Task.Delay(5, ct).ConfigureAwait(false);
                PublishResponse response = CreatePublishResponse(1, acknowledgements);
                response.Results = acknowledgements.ConvertAll(ack => ack.SequenceNumber == 2
                    ? StatusCodes.BadSequenceNumberUnknown
                    : StatusCodes.Good);
                return response;
            };
            for (int i = 0; i < 3; i++)
            {
                manager.Add(m_mockNotificationDataHandler.Object, new OptionsMonitor<SubscriptionOptions>(
                    new SubscriptionOptions { DisableUnboundedItemMode = true }));
            }
            FakeManagedSubscription sending = partitions[0];
            FakeManagedSubscription retiring = partitions[1];
            FakeManagedSubscription disposing = partitions[2];
            static AcknowledgementHandle Handle(FakeManagedSubscription owner, long generation, uint sequenceNumber) =>
                new(owner.GetAcknowledgementScope(generation), sequenceNumber);
            AcknowledgementHandle acknowledged = Handle(sending, sending.MessageGeneration, 1);
            AcknowledgementHandle rejected = Handle(sending, sending.MessageGeneration, 2);
            AcknowledgementHandle retired = Handle(retiring, retiring.MessageGeneration, 4);
            AcknowledgementHandle abandoned = Handle(disposing, disposing.MessageGeneration, 5);
            provider.Defer = handle => handle == retired || handle == abandoned;
            manager.Resume();

            foreach ((FakeManagedSubscription owner, long generation, uint sequenceNumber) in new[]
            {
                (sending, sending.MessageGeneration, 1u),
                (sending, sending.MessageGeneration, 2u),
                (sending, sending.MessageGeneration - 1, 3u),
                (retiring, retiring.MessageGeneration, 4u),
                (disposing, disposing.MessageGeneration, 5u)
            })
            {
                await manager.QueueAsync(owner, generation,
                    new SubscriptionAcknowledgement { SubscriptionId = owner.Id, SequenceNumber = sequenceNumber },
                    testCt).ConfigureAwait(false);
            }
            await WaitUntilAsync(() => observer.Count(acknowledged) == 1 &&
                observer.Count(rejected) == 1 &&
                observer.CountBySequenceNumber(3) == 1 &&
                provider.Presented(retired) > 0 &&
                provider.Presented(abandoned) > 0, testCt).ConfigureAwait(false);
            Assert.That(manager.DropPendingForSubscription(retiring, retiring.Id), Is.EqualTo(1));
            await manager.DisposeAsync().ConfigureAwait(false);

            Assert.That(observer.Disposition(acknowledged), Is.EqualTo(AcknowledgementDisposition.Acknowledged));
            Assert.That(observer.Disposition(rejected), Is.EqualTo(AcknowledgementDisposition.NotRetained));
            Assert.That(observer.All.Single(outcome => outcome.Handle.SequenceNumber == 3).Disposition,
                Is.EqualTo(AcknowledgementDisposition.OwnerRetired), "A stale acknowledgement is retired, not sent.");
            Assert.That(observer.Disposition(retired), Is.EqualTo(AcknowledgementDisposition.OwnerRetired));
            Assert.That(observer.Disposition(abandoned), Is.EqualTo(AcknowledgementDisposition.Abandoned));
            Assert.That(observer.All.GroupBy(outcome => outcome.Handle).All(group => group.Count() == 1), Is.True,
                "Every acknowledgement ends exactly once.");
        }

        /// <summary>
        /// Acknowledgement provider used by the provider tests.
        /// </summary>
        private sealed class TestAcknowledgementProvider : ISubscriptionAcknowledgementProvider
        {
            public Func<AcknowledgementHandle, bool> Defer { get; set; } = _ => false;

            public bool Release { get; set; }

            public int FailuresLeft;

            public int Presented(AcknowledgementHandle handle)
            {
                return m_presented.TryGetValue(handle, out int count) ? count : 0;
            }

            public ValueTask<AcknowledgementDecision> DecideAsync(
                ArrayOf<AcknowledgementHandle> acknowledgements,
                CancellationToken ct = default)
            {
                if (Interlocked.Decrement(ref FailuresLeft) >= 0)
                {
                    throw new InvalidOperationException("Injected provider failure.");
                }
                var deferred = new List<int>();
                for (int i = 0; i < acknowledgements.Count; i++)
                {
                    m_presented.AddOrUpdate(acknowledgements[i], 1, (_, count) => count + 1);
                    if (Defer(acknowledgements[i]))
                    {
                        deferred.Add(i);
                    }
                }
                return new ValueTask<AcknowledgementDecision>(new AcknowledgementDecision(deferred.ToArray()));
            }

            private readonly ConcurrentDictionary<AcknowledgementHandle, int> m_presented = new();
        }

        /// <summary>
        /// Acknowledgement observer used by the provider tests.
        /// </summary>
        private sealed class TestAcknowledgementObserver : ISubscriptionAcknowledgementObserver
        {
            public IReadOnlyList<AcknowledgementOutcome> All => [.. m_outcomes];

            public int Count(AcknowledgementHandle handle)
            {
                return m_outcomes.Count(outcome => outcome.Handle == handle);
            }

            public int CountBySequenceNumber(uint sequenceNumber)
            {
                return m_outcomes.Count(outcome => outcome.Handle.SequenceNumber == sequenceNumber);
            }

            public AcknowledgementDisposition Disposition(AcknowledgementHandle handle)
            {
                return m_outcomes.Single(outcome => outcome.Handle == handle).Disposition;
            }

            public ValueTask OnCompletedAsync(
                ArrayOf<AcknowledgementOutcome> outcomes,
                CancellationToken ct = default)
            {
                foreach (AcknowledgementOutcome outcome in outcomes)
                {
                    m_outcomes.Enqueue(outcome);
                }
                return default;
            }

            private readonly ConcurrentQueue<AcknowledgementOutcome> m_outcomes = new();
        }

        /// <summary>
        /// Acknowledgements a deferral handler adds itself are attributed to the live owner of
        /// their id and validated like queued ones; those without a live owner are not sent.
        /// </summary>
        /// <param name="testCt">Test cancellation token.</param>
        [Test]
        [CancelAfter(10_000)]
        public async Task HandlerCreatedAcknowledgementsAreValidatedAsync(CancellationToken testCt)
        {
            var context = new FakeSubscriptionManagerContext();
            List<FakeManagedSubscription> partitions = AddPartitionFactory(context);
            await using var manager = new SubscriptionManager(
                context, m_telemetry.LoggerFactory, DiagnosticsMasks.None)
            {
                MinPublishWorkerCount = 1,
                MaxPublishWorkerCount = 1
            };
            var sent = new ConcurrentQueue<SubscriptionAcknowledgement>();
            int added = 0;
            context.OnPrepareAcknowledgements = acknowledgements =>
            {
                if (acknowledgements.Count != 0 && Interlocked.Exchange(ref added, 1) == 0)
                {
                    acknowledgements.Add(new SubscriptionAcknowledgement { SubscriptionId = 1, SequenceNumber = 21 });
                    acknowledgements.Add(new SubscriptionAcknowledgement { SubscriptionId = 77, SequenceNumber = 22 });
                }
                return (acknowledgements, []);
            };
            context.OnPublishAsync = async (_, acknowledgements, ct) =>
            {
                foreach (SubscriptionAcknowledgement acknowledgement in acknowledgements)
                {
                    sent.Enqueue(acknowledgement);
                }
                await Task.Delay(5, ct).ConfigureAwait(false);
                return CreatePublishResponse(1, acknowledgements);
            };
            manager.Add(m_mockNotificationDataHandler.Object, new OptionsMonitor<SubscriptionOptions>(
                new SubscriptionOptions { DisableUnboundedItemMode = true }));
            FakeManagedSubscription owner = partitions[0];
            manager.Resume();

            await manager.QueueAsync(owner, owner.MessageGeneration,
                new SubscriptionAcknowledgement { SubscriptionId = 1, SequenceNumber = 20 },
                testCt).ConfigureAwait(false);

            await WaitUntilAsync(() => sent.Any(ack => ack.SequenceNumber == 21), testCt).ConfigureAwait(false);
            Assert.That(sent.Select(ack => ack.SequenceNumber), Does.Contain(20u));
            Assert.That(sent.Any(ack => ack.SubscriptionId == 77), Is.False,
                "An acknowledgement without a live owner must not be sent.");
        }

        /// <summary>
        /// Acknowledgements the server rejects are logged: invalid subscription ids at
        /// information level, sequence numbers the server already removed at debug level.
        /// </summary>
        /// <param name="invalidSubscription">Whether the server reports an invalid
        /// subscription id rather than an unknown sequence number.</param>
        /// <param name="testCt">Test cancellation token.</param>
        [TestCase(true)]
        [TestCase(false)]
        [CancelAfter(10_000)]
        public async Task RejectedAcknowledgementsAreLoggedAsync(bool invalidSubscription, CancellationToken testCt)
        {
            StatusCode code = invalidSubscription
                ? StatusCodes.BadSubscriptionIdInvalid
                : StatusCodes.BadSequenceNumberUnknown;
            LogLevel level = invalidSubscription ? LogLevel.Information : LogLevel.Debug;
            var provider = new CapturingLoggerProvider();
            using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder
                .SetMinimumLevel(LogLevel.Trace)
                .AddProvider(provider));
            var context = new FakeSubscriptionManagerContext();
            List<FakeManagedSubscription> partitions = AddPartitionFactory(context);
            await using var manager = new SubscriptionManager(context, loggerFactory, DiagnosticsMasks.None)
            {
                MinPublishWorkerCount = 1,
                MaxPublishWorkerCount = 1
            };
            context.OnPublishAsync = async (_, acknowledgements, ct) =>
            {
                await Task.Delay(5, ct).ConfigureAwait(false);
                PublishResponse response = CreatePublishResponse(1, acknowledgements);
                response.Results = acknowledgements.ConvertAll(_ => code);
                return response;
            };
            manager.Add(m_mockNotificationDataHandler.Object, new OptionsMonitor<SubscriptionOptions>(
                new SubscriptionOptions { DisableUnboundedItemMode = true }));
            FakeManagedSubscription owner = partitions[0];
            manager.Resume();
            await manager.QueueAsync(owner, owner.MessageGeneration,
                new SubscriptionAcknowledgement { SubscriptionId = 1, SequenceNumber = 3 },
                testCt).ConfigureAwait(false);

            await WaitUntilAsync(() => provider.Count(level, kRejectedText) >= 1, testCt).ConfigureAwait(false);
        }

        [Test]
        public async Task SendPublishRequestsWithSuccessAsync()
        {
            ILoggerFactory loggerFactory = m_telemetry.LoggerFactory;
            var session = new FakeSubscriptionManagerContext();
            OptionsMonitor<SubscriptionOptions> so1 = OptionsFactory.Create<SubscriptionOptions>();
            var ms1 = new FakeManagedSubscription { Id = 1, Created = true };
            var sut = new SubscriptionManager(session,
                loggerFactory, DiagnosticsMasks.None);
            session.CreateSubscriptionFactory = (handler, options, queue) =>
            {
                Assert.That(ReferenceEquals(options, so1), Is.True);
                Assert.That(queue, Is.SameAs(sut));
                return ms1;
            };
            // Test adding subscription
            ISubscription s1 = sut.Add(m_mockNotificationDataHandler.Object, so1);
            Assert.That(sut.Count, Is.EqualTo(1));
            sut.MaxPublishWorkerCount = 1;

            // Ack received immediately
            ms1.OnPublishReceivedAsyncFunc = (n, _, _) =>
                sut.QueueAsync(ms1, ms1.MessageGeneration, new SubscriptionAcknowledgement
                {
                    SubscriptionId = 1,
                    SequenceNumber = n.SequenceNumber
                }, default);

            session.OnPublishAsync = (h, s, ct) =>
                new ValueTask<PublishResponse>(new PublishResponse
                {
                    AvailableSequenceNumbers = [],
                    NotificationMessage = new NotificationMessage
                    {
                        SequenceNumber = h!.RequestHandle
                    },
                    Results = s.ConvertAll(_ => StatusCodes.Good),
                    SubscriptionId = 1,
                    MoreNotifications = false,
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Good,
                        StringTable = []
                    }
                });

            sut.Resume();
            await Task.Delay(1000).ConfigureAwait(false);

            await sut.DisposeAsync().ConfigureAwait(false);
            Assert.That(sut.Count, Is.Zero);
            Assert.That(sut.PublishWorkerCount, Is.Zero);
            // The so1 subscription was created exactly once.
            Assert.That(session.CreateSubscriptionCalls
                .Count(c => ReferenceEquals(c.Options, so1)), Is.EqualTo(1));
        }

        /// <summary>
        /// A revised publishing interval times keep-alive count beyond
        /// <see cref="TimeSpan.MaxValue"/> must not fault the publish workers
        /// (which the controller would respawn in a hot loop without ever
        /// sending a Publish).
        /// </summary>
        [Test]
        [CancelAfter(10_000)]
        public async Task PublishWorkerSurvivesOverflowingKeepAliveProductAsync(
            CancellationToken testCt)
        {
            ILoggerFactory loggerFactory = m_telemetry.LoggerFactory;
            var session = new FakeSubscriptionManagerContext();
            OptionsMonitor<SubscriptionOptions> so1 = OptionsFactory.Create<SubscriptionOptions>();
            var ms1 = new FakeManagedSubscription
            {
                Id = 1,
                Created = true,
                CurrentPublishingInterval = TimeSpan.FromHours(1),
                CurrentKeepAliveCount = uint.MaxValue
            };
            var sut = new SubscriptionManager(session,
                loggerFactory, DiagnosticsMasks.None);
            session.CreateSubscriptionFactory = (_, _, _) => ms1;
            sut.Add(m_mockNotificationDataHandler.Object, so1);
            sut.MaxPublishWorkerCount = 1;

            var published = new TaskCompletionSource<uint>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            session.OnPublishAsync = (h, s, ct) =>
            {
                published.TrySetResult(h!.TimeoutHint);
                return new ValueTask<PublishResponse>(new PublishResponse
                {
                    AvailableSequenceNumbers = [],
                    NotificationMessage = new NotificationMessage(),
                    Results = s.ConvertAll(_ => StatusCodes.Good),
                    SubscriptionId = 1,
                    MoreNotifications = false,
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Good,
                        StringTable = []
                    }
                });
            };

            sut.Resume();
            try
            {
                uint timeoutHint = await published.Task.WaitAsync(testCt).ConfigureAwait(false);
                Assert.That(timeoutHint, Is.GreaterThan(0u));
            }
            finally
            {
                await sut.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// When a secondary partition of a multi-partition snapshot fails to
        /// attach, the already registered primary must be rolled back: the
        /// caller never receives the wrapper and could not dispose it.
        /// </summary>
        [Test]
        public async Task RestoreGroupRollsBackPrimaryWhenSecondaryFailsAsync()
        {
            var session = new FakeSubscriptionManagerContext
            {
                OnTransferSubscriptionsAsync = (_, _, _, _) =>
                    new ValueTask<TransferSubscriptionsResponse>(
                        new TransferSubscriptionsResponse
                        {
                            ResponseHeader = new ResponseHeader
                            {
                                ServiceResult = StatusCodes.BadServiceUnsupported
                            }
                        })
            };
            var primary = new FakeManagedSubscription { Id = 7, Created = true };
            int created = 0;
            session.CreateSubscriptionFactory = (_, _, _) =>
            {
                if (++created == 1)
                {
                    return primary;
                }
                throw ServiceResultException.Create(StatusCodes.BadOutOfMemory,
                    "Secondary partition could not be created.");
            };
            var sut = new SubscriptionManager(session,
                m_telemetry.LoggerFactory, DiagnosticsMasks.None);
            await using (sut.ConfigureAwait(false))
            {
                var options = new SubscriptionOptions();
                SubscriptionStateSnapshot[] snapshots =
                [
                    SubscriptionStateSnapshot.AsOptions(options, 7, [], []),
                    SubscriptionStateSnapshot.AsOptions(options, 8, [], [])
                ];

                await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await sut.RestoreGroupAsync(m_mockNotificationDataHandler.Object,
                        snapshots, true, default).ConfigureAwait(false));

                Assert.That(sut.Count, Is.Zero);
                Assert.That(sut.Items, Is.Empty);
                Assert.That(primary.DisposeAsyncCalls, Is.GreaterThan(0));
            }
        }

        [Test]
        public void TransferSubscriptionsOnRecreateSetAndGet()
        {
            m_subscriptionManager.TransferSubscriptionsOnRecreate = true;
            Assert.That(m_subscriptionManager.TransferSubscriptionsOnRecreate, Is.True);
        }

        [Test]
        public void ReturnDiagnosticsSetAndGet()
        {
            m_subscriptionManager.ReturnDiagnostics = DiagnosticsMasks.All;
            Assert.That(m_subscriptionManager.ReturnDiagnostics, Is.EqualTo(DiagnosticsMasks.All));
        }

        /// <summary>
        /// Temporary server-identifier loss retains workers without bypassing pool limits or actual removals.
        /// Initial uncreated subscriptions do not start workers.
        /// </summary>
        [Test]
        public async Task PublishWorkersSurviveUncreatedSubscriptionsAndStillHonorPoolLimitsAsync()
        {
            var session = new FakeSubscriptionManagerContext();
            OptionsMonitor<SubscriptionOptions> firstOptions = OptionsFactory.Create<SubscriptionOptions>();
            OptionsMonitor<SubscriptionOptions> secondOptions = OptionsFactory.Create<SubscriptionOptions>();
            var first = new FakeManagedSubscription { Id = 1 };
            var second = new FakeManagedSubscription { Id = 2 };
            session.CreateSubscriptionFactory = (_, options, _) => ReferenceEquals(options, firstOptions)
                ? first
                : second;
            await using var manager = new SubscriptionManager(session, m_telemetry.LoggerFactory, DiagnosticsMasks.None)
            {
                MinPublishWorkerCount = 0,
                MaxPublishWorkerCount = 4
            };
            manager.Add(m_mockNotificationDataHandler.Object, firstOptions);
            manager.Add(m_mockNotificationDataHandler.Object, secondOptions);
            Assert.That(manager.CreatedCount, Is.Zero);
            Assert.That(manager.PublishWorkerCount, Is.Zero);

            first.Created = true;
            second.Created = true;
            manager.Update();
            await WaitForPublishWorkerCountAsync(manager, 2).ConfigureAwait(false);

            first.Created = false;
            second.Created = false;
            manager.MinPublishWorkerCount = 3;
            await WaitForPublishWorkerCountAsync(manager, 3).ConfigureAwait(false);
            Assert.That(manager.CreatedCount, Is.Zero);
            Assert.That(manager.Count, Is.EqualTo(2));

            manager.MinPublishWorkerCount = 0;
            manager.MaxPublishWorkerCount = 1;
            await WaitForPublishWorkerCountAsync(manager, 1).ConfigureAwait(false);

            first.Created = true;
            second.Created = true;
            manager.MaxPublishWorkerCount = 2;
            manager.Update();
            await WaitForPublishWorkerCountAsync(manager, 2).ConfigureAwait(false);
            await manager.CompleteAsync(first, 1, CancellationToken.None).ConfigureAwait(false);
            await WaitForPublishWorkerCountAsync(manager, 1).ConfigureAwait(false);
            await manager.CompleteAsync(second, 2, CancellationToken.None).ConfigureAwait(false);
            await WaitForPublishWorkerCountAsync(manager, 0).ConfigureAwait(false);
            Assert.That(manager.Count, Is.Zero);
        }

        /// <summary>
        /// A never-created subscription cannot retain the workers of a different, removed subscription.
        /// </summary>
        /// <param name="classic">Whether the established subscription belongs to the classic session API.</param>
        [TestCase(false)]
        [TestCase(true)]
        public async Task PublishWorkersDoNotTreatPendingSubscriptionsAsRecoveredAsync(bool classic)
        {
            var session = new FakeSubscriptionManagerContext();
            OptionsMonitor<SubscriptionOptions> establishedOptions = OptionsFactory.Create<SubscriptionOptions>();
            OptionsMonitor<SubscriptionOptions> pendingOptions = OptionsFactory.Create<SubscriptionOptions>();
            var established = new FakeManagedSubscription { Id = 1, Created = true };
            var pending = new FakeManagedSubscription();
            session.CreateSubscriptionFactory = (_, options, _) => ReferenceEquals(options, establishedOptions)
                ? established
                : pending;
            await using var manager = new SubscriptionManager(session, m_telemetry.LoggerFactory, DiagnosticsMasks.None)
            {
                MinPublishWorkerCount = 0,
                MaxPublishWorkerCount = 4
            };
            if (classic)
            {
                session.SessionOwnedSubscriptionIds.Add(1);
                manager.Update();
            }
            else
            {
                manager.Add(m_mockNotificationDataHandler.Object, establishedOptions);
            }
            await WaitForPublishWorkerCountAsync(manager, 1).ConfigureAwait(false);
            ISubscription remaining = manager.Add(m_mockNotificationDataHandler.Object, pendingOptions);
            if (classic)
            {
                session.SessionOwnedSubscriptionIds.Clear();
                manager.Update();
            }
            else
            {
                await manager.CompleteAsync(established, 1, CancellationToken.None).ConfigureAwait(false);
            }

            await WaitForPublishWorkerCountAsync(manager, 0).ConfigureAwait(false);
            Assert.That(manager.CreatedCount, Is.Zero);
            Assert.That(manager.Items, Is.EquivalentTo([remaining]));
        }

        [Test]
        public void MinPublishWorkerCountSetAndGet()
        {
            m_subscriptionManager.MinPublishWorkerCount = 5;
            Assert.That(m_subscriptionManager.MinPublishWorkerCount, Is.EqualTo(5));
        }

        [Test]
        public void MaxPublishWorkerCountSetAndGet()
        {
            m_subscriptionManager.MaxPublishWorkerCount = 10;
            Assert.That(m_subscriptionManager.MaxPublishWorkerCount, Is.EqualTo(10));
        }

        [Test]
        public void ItemsReturnsSubscriptions()
        {
            var mockSubscription = new FakeManagedSubscription();
            m_session.CreateSubscriptionFactory =
                (handler, options, queue) => mockSubscription;

            m_subscriptionManager.Add(m_mockNotificationDataHandler.Object,
                Mock.Of<IOptionsMonitor<SubscriptionOptions>>());
            Assert.That(m_subscriptionManager.Items, Has.Exactly(1).Items);
        }

        [Test]
        public void CountReturnsSubscriptionCount()
        {
            var mockSubscription = new FakeManagedSubscription();
            m_session.CreateSubscriptionFactory =
                (handler, options, queue) => mockSubscription;

            m_subscriptionManager.Add(m_mockNotificationDataHandler.Object,
                Mock.Of<IOptionsMonitor<SubscriptionOptions>>());
            Assert.That(m_subscriptionManager.Count, Is.EqualTo(1));
        }

        [Test]
        public void GoodPublishRequestCountReturnsCount()
        {
            Assert.That(m_subscriptionManager.GoodPublishRequestCount, Is.Zero);
        }

        [Test]
        public void BadPublishRequestCountReturnsCount()
        {
            Assert.That(m_subscriptionManager.BadPublishRequestCount, Is.Zero);
        }

        [Test]
        public void PublishWorkerCountReturnsCount()
        {
            Assert.That(m_subscriptionManager.PublishWorkerCount, Is.Zero);
        }

        [Test]
        public async Task DisposeAsyncDisposesSubscriptionsAsync()
        {
            var mockSubscription = new FakeManagedSubscription();
            m_session.CreateSubscriptionFactory =
                (handler, options, queue) => mockSubscription;

            m_subscriptionManager.Add(m_mockNotificationDataHandler.Object,
                Mock.Of<IOptionsMonitor<SubscriptionOptions>>());
            await m_subscriptionManager.DisposeAsync().ConfigureAwait(false);
            Assert.That(mockSubscription.DisposeAsyncCalls, Is.EqualTo(1));
        }

        [Test]
        public void UpdateTriggersPublishController()
        {
            m_subscriptionManager.Update();// No exception means success
        }

        [Test]
        public async Task QueueAsyncQueuesAcknowledgementAsync()
        {
            var ack = new SubscriptionAcknowledgement();
            var owner = new FakeManagedSubscription();
            await m_subscriptionManager.QueueAsync(owner, owner.MessageGeneration, ack, CancellationToken.None)
                .ConfigureAwait(false);
            // No exception means success
        }

        [Test]
        public void QueueAsyncRejectsMissingOwner()
        {
            Assert.That(
                async () => await m_subscriptionManager.QueueAsync(null!, 0, new SubscriptionAcknowledgement(),
                    CancellationToken.None).ConfigureAwait(false),
                Throws.ArgumentNullException);
        }

        [Test]
        public async Task CompleteAsyncCompletesSubscriptionAsync()
        {
            var mockSubscription = new FakeManagedSubscription { Id = 1 };
            m_session.CreateSubscriptionFactory =
                (handler, options, queue) => mockSubscription;

            m_subscriptionManager.Add(m_mockNotificationDataHandler.Object,
                Mock.Of<IOptionsMonitor<SubscriptionOptions>>());
            await m_subscriptionManager.CompleteAsync(mockSubscription, 1, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(m_subscriptionManager.Items, Is.Empty);
        }

        [Test]
        public void AddAddsSubscription()
        {
            var mockSubscription = new FakeManagedSubscription();
            m_session.CreateSubscriptionFactory =
                (handler, options, queue) => mockSubscription;

            ISubscription subscription = m_subscriptionManager.Add(m_mockNotificationDataHandler.Object,
                Mock.Of<IOptionsMonitor<SubscriptionOptions>>());

            Assert.That(subscription, Is.Not.Null);
            Assert.That(m_subscriptionManager.Items, Has.Exactly(1).Items);
        }

        [Test]
        public void ResumeResumesSubscriptions()
        {
            m_subscriptionManager.Resume();// No exception means success
        }

        [Test]
        public void PausePausesSubscriptions()
        {
            m_subscriptionManager.Pause();// No exception means success
        }

        [Test]
        public async Task PublishingQuiescencePreservesConcurrentPauseAsync()
        {
            var subscription = new FakeManagedSubscription();
            m_session.CreateSubscriptionFactory =
                (handler, options, queue) => subscription;
            m_subscriptionManager.Add(m_mockNotificationDataHandler.Object,
                Mock.Of<IOptionsMonitor<SubscriptionOptions>>());
            m_subscriptionManager.Resume();

            var entered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            Task quiesced = m_subscriptionManager
                .RunWithPublishingQuiescedAsync(async ct =>
                {
                    entered.TrySetResult(true);
                    await release.Task.WaitAsync(ct).ConfigureAwait(false);
                }, CancellationToken.None)
                .AsTask();

            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5))
                .ConfigureAwait(false);
            m_subscriptionManager.Pause();
            release.TrySetResult(true);
            await quiesced.WaitAsync(TimeSpan.FromSeconds(5))
                .ConfigureAwait(false);

            Assert.That(subscription.NotifySubscriptionManagerPausedCalls,
                Is.EqualTo(s_resumedThenPaused));
        }

        [Test]
        [CancelAfter(30_000)]
        public async Task DisposeAsyncCancelsActivePublishingQuiescenceAsync(
            CancellationToken testCt)
        {
            var entered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var cancelled = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            Task quiesced = m_subscriptionManager
                .RunWithPublishingQuiescedAsync(async ct =>
                {
                    entered.TrySetResult(true);
                    try
                    {
                        await Task.Delay(Timeout.Infinite, ct)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        cancelled.TrySetResult(true);
                        throw;
                    }
                }, testCt)
                .AsTask();

            await entered.Task.WaitAsync(testCt).ConfigureAwait(false);
            Task dispose = m_subscriptionManager.DisposeAsync().AsTask();

            await cancelled.Task.WaitAsync(testCt).ConfigureAwait(false);
            await dispose.WaitAsync(testCt).ConfigureAwait(false);
            Assert.That(async () => await quiesced.ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
        }

        [Test]
        [CancelAfter(30_000)]
        public async Task PublishingQuiescenceCompletesWhileAWorkerWaitsForAReadyChannelAsync(
            CancellationToken testCt)
        {
            // Regression for #4508: a worker parked inside the service call
            // waiting for the channel to become ready holds an active publish
            // request. The recreate that would make the channel ready awaits
            // the drain, so an unbounded drain deadlocks. The drain must abort
            // the parked attempt instead of waiting for it forever.
            ILoggerFactory loggerFactory = m_telemetry.LoggerFactory;
            var session = new FakeSubscriptionManagerContext();
            var subscription = new FakeManagedSubscription { Id = 1, Created = true };
            var sut = new SubscriptionManager(session, loggerFactory, DiagnosticsMasks.None);
            try
            {
                session.CreateSubscriptionFactory = (_, _, _) => subscription;
                var publishCalled = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                var resumed = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                // Never completes on its own: the only way out is cancellation,
                // exactly like ChannelEntry.WaitForReadyAsync on a faulted channel.
                var readyGate = new TaskCompletionSource<PublishResponse>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                int publishAttempts = 0;
                int resumeBaseline = int.MaxValue;
                // One delegate for the whole test. Swapping it after the quiesce
                // would race the worker: RunWithPublishingQuiescedAsync resumes
                // publishing in its finally, so the worker can re-arm before the
                // swap lands and then park on the old delegate forever.
                session.OnPublishAsync = (_, _, publishCt) =>
                {
                    int attempt = Interlocked.Increment(ref publishAttempts);
                    publishCalled.TrySetResult(true);
                    if (attempt > Volatile.Read(ref resumeBaseline))
                    {
                        resumed.TrySetResult(true);
                    }
                    return new ValueTask<PublishResponse>(readyGate.Task.WaitAsync(publishCt));
                };

                sut.MinPublishWorkerCount = 1;
                sut.MaxPublishWorkerCount = 1;
                sut.Add(m_mockNotificationDataHandler.Object,
                    Mock.Of<IOptionsMonitor<SubscriptionOptions>>());
                sut.Resume();
                await publishCalled.Task.WaitAsync(testCt).ConfigureAwait(false);

                // The worker is parked in the gate and cannot start another
                // attempt while publishing is quiesced, so this baseline is
                // stable: any later attempt proves the worker survived the abort.
                Volatile.Write(ref resumeBaseline, Volatile.Read(ref publishAttempts));

                bool operationRan = false;
                int dropped = -1;
                await sut.RunWithPublishingQuiescedAsync(_ =>
                {
                    operationRan = true;
                    // The documented contract: ingress is quiesced here, so
                    // dropping pending acknowledgements is still safe.
                    dropped = sut.DropPendingForSubscription(subscription, 1);
                    return default;
                }, testCt).ConfigureAwait(false);

                Assert.Multiple(() =>
                {
                    Assert.That(operationRan, Is.True,
                        "the quiesced operation must run instead of deadlocking on the drain.");
                    Assert.That(dropped, Is.Zero);
                    Assert.That(publishAttempts, Is.GreaterThan(0));
                });

                // The worker survived the abort and resumes publishing.
                await resumed.Task.WaitAsync(TimeSpan.FromSeconds(10), testCt).ConfigureAwait(false);
            }
            finally
            {
                await sut.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        [CancelAfter(30_000)]
        public async Task PublishingQuiescenceWaitsForAckRollbackAsync(
            CancellationToken testCt)
        {
            ILoggerFactory loggerFactory = m_telemetry.LoggerFactory;
            var session = new FakeSubscriptionManagerContext();
            var subscription = new FakeManagedSubscription
            {
                Id = 1,
                Created = true
            };
            var sut = new SubscriptionManager(session,
                loggerFactory, DiagnosticsMasks.None);
            try
            {
                session.CreateSubscriptionFactory = (_, _, _) => subscription;
                var publishCalled = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                var publishGate = new TaskCompletionSource<PublishResponse>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                session.OnPublishAsync = (_, acknowledgements, _) =>
                {
                    Assert.That(acknowledgements, Has.Count.EqualTo(1));
                    publishCalled.TrySetResult(true);
                    return new ValueTask<PublishResponse>(publishGate.Task);
                };

                sut.MinPublishWorkerCount = 1;
                sut.MaxPublishWorkerCount = 1;
                sut.Add(m_mockNotificationDataHandler.Object,
                    Mock.Of<IOptionsMonitor<SubscriptionOptions>>());
                await sut.QueueAsync(subscription, subscription.MessageGeneration, new SubscriptionAcknowledgement
                {
                    SubscriptionId = 1,
                    SequenceNumber = 7
                }, testCt).ConfigureAwait(false);
                sut.Resume();
                await publishCalled.Task.WaitAsync(testCt).ConfigureAwait(false);

                var actionEntered = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                int queued = 0;
                Task quiesced = sut.RunWithPublishingQuiescedAsync(_ =>
                {
                    // The aborted attempt has rolled its acknowledgement back before this runs.
                    queued = sut.QueuedAcknowledgementCount;
                    sut.Pause();
                    actionEntered.TrySetResult(true);
                    return default;
                }, testCt).AsTask();

                await Task.Delay(100, testCt).ConfigureAwait(false);
                Assert.That(actionEntered.Task.IsCompleted, Is.False);

                publishGate.TrySetException(
                    new ServiceResultException(StatusCodes.BadNotConnected));
                await actionEntered.Task.WaitAsync(testCt).ConfigureAwait(false);
                await quiesced.WaitAsync(testCt).ConfigureAwait(false);

                Assert.That(queued, Is.EqualTo(1));
            }
            finally
            {
                await sut.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task RecreateSubscriptionsAsyncRecreatesSubscriptionsAsync()
        {
            var mockSubscription = new FakeManagedSubscription();
            m_session.CreateSubscriptionFactory =
                (handler, options, queue) => mockSubscription;

            m_subscriptionManager.Add(m_mockNotificationDataHandler.Object, Mock.Of<IOptionsMonitor<SubscriptionOptions>>());
            await m_subscriptionManager.RecreateSubscriptionsAsync(null, CancellationToken.None).ConfigureAwait(false);
            Assert.That(mockSubscription.RecreateAsyncCalls, Is.EqualTo(1));
        }

        [Test]
        public async Task DrainAsyncReturnsImmediatelyWhenNoPublishesActiveAsync()
        {
            // No worker started — counter is zero — DrainAsync should
            // complete synchronously.
            using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await m_subscriptionManager
                .DrainAsync(ct.Token).ConfigureAwait(false);
        }

        [Test]
        [CancelAfter(30_000)]
        public async Task DrainAsyncWaitsForInFlightPublishToCompleteAsync(
            CancellationToken testCt)
        {
            ILoggerFactory loggerFactory = m_telemetry.LoggerFactory;
            var session = new FakeSubscriptionManagerContext();
            OptionsMonitor<SubscriptionOptions> options =
                OptionsFactory.Create<SubscriptionOptions>();
            var ms1 = new FakeManagedSubscription { Id = 1u, Created = true };

            var sut = new SubscriptionManager(session,
                loggerFactory, DiagnosticsMasks.None);
            try
            {
                session.CreateSubscriptionFactory = (handler, opts, queue) =>
                {
                    Assert.That(ReferenceEquals(opts, options), Is.True);
                    Assert.That(queue, Is.SameAs(sut));
                    return ms1;
                };

                // Block the publish call so a worker stays "in flight".
                var publishGate = new TaskCompletionSource<PublishResponse>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                var publishCalled = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);

                session.OnPublishAsync = (h, a, ct) =>
                {
                    publishCalled.TrySetResult(true);
                    return new ValueTask<PublishResponse>(publishGate.Task);
                };

                sut.MaxPublishWorkerCount = 1;
                sut.MinPublishWorkerCount = 1;
                ISubscription _ = sut.Add(
                    m_mockNotificationDataHandler.Object, options);
                sut.Resume();

                // Wait until the worker has called PublishAsync at least
                // once so the active-publish counter is non-zero.
                await publishCalled.Task.WaitAsync(testCt).ConfigureAwait(false);

                // Pause is soft: it stops *new* publishes from being
                // issued, but the in-flight publish call is still
                // outstanding. Drain must wait for it.
                sut.Pause();

                using var drainCts = CancellationTokenSource
                    .CreateLinkedTokenSource(testCt);
                drainCts.CancelAfter(TimeSpan.FromMilliseconds(300));
                try
                {
                    await sut.DrainAsync(drainCts.Token).ConfigureAwait(false);
                    Assert.Fail(
                        "DrainAsync must not return while a publish is " +
                        "in flight; expected OperationCanceledException.");
                }
                catch (OperationCanceledException)
                {
                    // expected — drain timed out because the publish is
                    // still in flight.
                }

                // Complete the publish, releasing the worker.
                publishGate.TrySetResult(new PublishResponse
                {
                    AvailableSequenceNumbers = [],
                    NotificationMessage = new NotificationMessage
                    {
                        SequenceNumber = 1u
                    },
                    Results = [],
                    SubscriptionId = 1,
                    MoreNotifications = false,
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Good,
                        StringTable = []
                    }
                });

                // The publish worker decrements the counter in finally,
                // so DrainAsync now returns.
                using var drainCts2 = CancellationTokenSource
                    .CreateLinkedTokenSource(testCt);
                drainCts2.CancelAfter(TimeSpan.FromSeconds(5));
                await sut.DrainAsync(drainCts2.Token).ConfigureAwait(false);
            }
            finally
            {
                await sut.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Regression for #4113. A subscription resets its server side id to
        /// zero before its message processor drains and completes, so
        /// <see cref="IMessageAckQueue.CompleteAsync"/> must retire the
        /// subscription by instance. Resolving it by id would evict an
        /// arbitrary other subscription that has been added but not created
        /// yet — those all share id zero — leaving that subscription without
        /// publish dispatch.
        /// </summary>
        [Test]
        [CancelAfter(30_000)]
        public async Task CompleteAsyncRetiresOnlyTheCompletedSubscriptionWhenOthersArePendingCreationAsync()
        {
            ILoggerFactory loggerFactory = m_telemetry.LoggerFactory;
            var session = new FakeSubscriptionManagerContext();
            OptionsMonitor<SubscriptionOptions> pendingOptions =
                OptionsFactory.Create<SubscriptionOptions>();
            OptionsMonitor<SubscriptionOptions> disposingOptions =
                OptionsFactory.Create<SubscriptionOptions>();

            // Not created yet — the server has not assigned an id.
            var pending = new FakeManagedSubscription { Id = 0u };
            // Created, then deleted on the server, which resets Id to zero.
            var disposing = new FakeManagedSubscription { Id = 7u, Created = true };

            var sut = new SubscriptionManager(session,
                loggerFactory, DiagnosticsMasks.None);
            await using (sut.ConfigureAwait(false))
            {
                session.CreateSubscriptionFactory = (handler, options, queue)
                    => ReferenceEquals(options, pendingOptions) ? pending : disposing;

                ISubscription pendingSubscription = sut.Add(
                    m_mockNotificationDataHandler.Object, pendingOptions);
                ISubscription disposingSubscription = sut.Add(
                    m_mockNotificationDataHandler.Object, disposingOptions);
                Assert.That(sut.Count, Is.EqualTo(2));

                // Server side delete resets the id; the message processor
                // still passes the last known server id to CompleteAsync.
                disposing.Id = 0u;
                disposing.Created = false;

                await sut.CompleteAsync(disposing, 7u, CancellationToken.None)
                    .ConfigureAwait(false);

                Assert.That(sut.Count, Is.EqualTo(1));
                Assert.That(sut.Items, Does.Contain(pendingSubscription));
                Assert.That(sut.Items, Does.Not.Contain(disposingSubscription));
            }
        }

        /// <summary>
        /// A publish response that arrives after a subscription was retired
        /// must not make the manager delete anything on the server: the
        /// retired server side id is remembered even though the subscription
        /// already reset its own id to zero.
        /// </summary>
        [Test]
        [CancelAfter(30_000)]
        public async Task CompleteAsyncRemembersRetiredServerIdAfterIdWasResetAsync(
            CancellationToken testCt)
        {
            ILoggerFactory loggerFactory = m_telemetry.LoggerFactory;
            var session = new FakeSubscriptionManagerContext();
            OptionsMonitor<SubscriptionOptions> retiredOptions =
                OptionsFactory.Create<SubscriptionOptions>();
            OptionsMonitor<SubscriptionOptions> keptOptions =
                OptionsFactory.Create<SubscriptionOptions>();

            var retired = new FakeManagedSubscription { Id = 7u, Created = true };
            var kept = new FakeManagedSubscription { Id = 8u, Created = true };

            var sut = new SubscriptionManager(session,
                loggerFactory, DiagnosticsMasks.None);
            await using (sut.ConfigureAwait(false))
            {
                session.CreateSubscriptionFactory = (handler, options, queue)
                    => ReferenceEquals(options, retiredOptions) ? retired : kept;

                sut.Add(m_mockNotificationDataHandler.Object, retiredOptions);
                sut.Add(m_mockNotificationDataHandler.Object, keptOptions);

                // Server side delete of the retired subscription.
                retired.Id = 0u;
                retired.Created = false;
                await sut.CompleteAsync(retired, 7u, CancellationToken.None)
                    .ConfigureAwait(false);

                // The still late in-flight publish response for id 7.
                var publishSeen = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                session.OnPublishAsync = (h, a, ct) =>
                {
                    publishSeen.TrySetResult(true);
                    return new ValueTask<PublishResponse>(
                        CreatePublishResponse(7u, h!.RequestHandle));
                };

                sut.MinPublishWorkerCount = 1;
                sut.MaxPublishWorkerCount = 1;
                sut.Resume();

                await publishSeen.Task.WaitAsync(testCt).ConfigureAwait(false);
                await Task.Delay(250, testCt).ConfigureAwait(false);

                Assert.That(session.DeleteCalls, Is.Empty,
                    "A publish response for a retired subscription must not " +
                    "trigger DeleteSubscriptions.");
            }
        }

        /// <summary>
        /// A publish response can overtake the CreateSubscription continuation
        /// that assigns the server side id. While any subscription is still
        /// awaiting its id, an unresolved subscription id must not be deleted
        /// on the server — doing so would silently kill a healthy subscription.
        /// </summary>
        [Test]
        [CancelAfter(30_000)]
        public async Task PublishWorkerDefersDeletingUnknownSubscriptionWhileCreationIsPendingAsync(
            CancellationToken testCt)
        {
            ILoggerFactory loggerFactory = m_telemetry.LoggerFactory;
            var session = new FakeSubscriptionManagerContext();
            OptionsMonitor<SubscriptionOptions> createdOptions =
                OptionsFactory.Create<SubscriptionOptions>();
            OptionsMonitor<SubscriptionOptions> pendingOptions =
                OptionsFactory.Create<SubscriptionOptions>();

            var created = new FakeManagedSubscription { Id = 1u, Created = true };
            var pending = new FakeManagedSubscription { Id = 0u, IsCreationInProgress = true };

            var sut = new SubscriptionManager(session,
                loggerFactory, DiagnosticsMasks.None);
            await using (sut.ConfigureAwait(false))
            {
                session.CreateSubscriptionFactory = (handler, options, queue)
                    => ReferenceEquals(options, createdOptions) ? created : pending;

                sut.Add(m_mockNotificationDataHandler.Object, createdOptions);
                sut.Add(m_mockNotificationDataHandler.Object, pendingOptions);

                int publishCount = 0;
                session.OnPublishAsync = (h, a, ct) =>
                {
                    Interlocked.Increment(ref publishCount);
                    return new ValueTask<PublishResponse>(
                        CreatePublishResponse(4242u, h!.RequestHandle));
                };

                sut.MinPublishWorkerCount = 1;
                sut.MaxPublishWorkerCount = 1;
                sut.Resume();

                await WaitUntilAsync(() => Volatile.Read(ref publishCount) >= 3,
                    testCt).ConfigureAwait(false);
                Assert.That(session.DeleteCalls, Is.Empty,
                    "An unresolved subscription id must not be deleted while " +
                    "another subscription is still awaiting its id.");

                // Once nothing is pending creation the orphan is cleaned up.
                pending.Id = 2u;
                pending.Created = true;
                pending.IsCreationInProgress = false;
                sut.Update();

                await WaitUntilAsync(() => session.DeleteCallsCount > 0, testCt)
                    .ConfigureAwait(false);
                IReadOnlyList<FakeSubscriptionManagerContext.DeleteCall> deleteCalls =
                    session.DeleteCalls;
                Assert.That(deleteCalls[0].SubscriptionIds.ToList(),
                    Does.Contain(4242u));
            }
        }

        /// <summary>
        /// The creation can complete between the worker's id lookup and its
        /// pending-creation check (the id is assigned before the in-progress
        /// flag clears). The freshly created subscription must receive the
        /// response instead of being deleted as an orphan.
        /// </summary>
        [Test]
        [CancelAfter(30_000)]
        public async Task PublishWorkerDoesNotDeleteSubscriptionCreatedDuringLookupAsync(
            CancellationToken testCt)
        {
            var session = new FakeSubscriptionManagerContext();
            OptionsMonitor<SubscriptionOptions> options =
                OptionsFactory.Create<SubscriptionOptions>();
            var creating = new FakeManagedSubscription { Id = 0u, Created = true };
            int armed = 0;
            creating.OnIsCreationInProgress = () =>
            {
                if (Volatile.Read(ref armed) != 0)
                {
                    // The creation completes right as the worker checks.
                    creating.Id = 4242u;
                }
                return false;
            };

            var sut = new SubscriptionManager(session,
                m_telemetry.LoggerFactory, DiagnosticsMasks.None);
            await using (sut.ConfigureAwait(false))
            {
                session.CreateSubscriptionFactory = (_, _, _) => creating;
                sut.Add(m_mockNotificationDataHandler.Object, options);

                session.OnPublishAsync = (h, a, ct) =>
                {
                    Volatile.Write(ref armed, 1);
                    return new ValueTask<PublishResponse>(
                        CreatePublishResponse(4242u, h!.RequestHandle));
                };

                sut.MinPublishWorkerCount = 1;
                sut.MaxPublishWorkerCount = 1;
                sut.Resume();

                await WaitUntilAsync(() => creating.OnPublishReceivedCalls.Count > 0,
                    testCt).ConfigureAwait(false);
                Assert.That(session.DeleteCalls, Is.Empty);
            }
        }

        /// <summary>
        /// The creation is still pending when the worker first looks, and
        /// completes (id assigned, in-progress flag cleared) before the
        /// worker decides what to do with the unresolved response. The
        /// orphan delete must be decided on the same pending snapshot the
        /// lookup used, so the healthy subscription is never deleted.
        /// </summary>
        [Test]
        [CancelAfter(30_000)]
        public async Task PublishWorkerDoesNotDeleteSubscriptionCreatedAfterPendingCheckAsync(
            CancellationToken testCt)
        {
            var session = new FakeSubscriptionManagerContext();
            OptionsMonitor<SubscriptionOptions> options =
                OptionsFactory.Create<SubscriptionOptions>();
            var creating = new FakeManagedSubscription { Id = 0u, Created = true };
            int armed = 0;
            int pendingReads = 0;
            creating.OnIsCreationInProgress = () =>
            {
                if (Volatile.Read(ref armed) == 0)
                {
                    return true;
                }
                if (Interlocked.Increment(ref pendingReads) == 1)
                {
                    // Still pending at the first check after the response.
                    return true;
                }
                // The creation completes right after that first check.
                creating.Id = 4242u;
                return false;
            };

            var sut = new SubscriptionManager(session,
                m_telemetry.LoggerFactory, DiagnosticsMasks.None);
            await using (sut.ConfigureAwait(false))
            {
                session.CreateSubscriptionFactory = (_, _, _) => creating;
                sut.Add(m_mockNotificationDataHandler.Object, options);

                session.OnPublishAsync = (h, a, ct) =>
                {
                    Volatile.Write(ref armed, 1);
                    return new ValueTask<PublishResponse>(
                        CreatePublishResponse(4242u, h!.RequestHandle));
                };

                sut.MinPublishWorkerCount = 1;
                sut.MaxPublishWorkerCount = 1;
                sut.Resume();

                await WaitUntilAsync(() => creating.OnPublishReceivedCalls.Count > 0,
                    testCt).ConfigureAwait(false);
                Assert.That(session.DeleteCalls, Is.Empty);
            }
        }

        /// <summary>
        /// A subscription created through the classic <c>Session.AddSubscription</c>
        /// API is unknown to this manager's registry, but it is live and owned by
        /// the application. Deleting it as abandoned takes it down on the server
        /// while the caller still believes it is streaming, which shows up as a
        /// twin that silently stops updating.
        /// </summary>
        [Test]
        [CancelAfter(30_000)]
        public async Task PublishWorkerKeepsSubscriptionOwnedBySessionAsync(
            CancellationToken testCt)
        {
            using ILoggerFactory loggerFactory = LoggerFactory.Create(b => b.AddDebug());
            var session = new FakeSubscriptionManagerContext();
            OptionsMonitor<SubscriptionOptions> createdOptions =
                OptionsFactory.Create<SubscriptionOptions>();

            var created = new FakeManagedSubscription { Id = 1u, Created = true };

            var sut = new SubscriptionManager(session,
                loggerFactory, DiagnosticsMasks.None);
            await using (sut.ConfigureAwait(false))
            {
                session.CreateSubscriptionFactory = (handler, options, queue) => created;
                sut.Add(m_mockNotificationDataHandler.Object, createdOptions);

                // The session holds this one outside the manager's registry.
                session.SessionOwnedSubscriptionIds.Add(4242u);

                int publishCount = 0;
                session.OnPublishAsync = (h, a, ct) =>
                {
                    Interlocked.Increment(ref publishCount);
                    return new ValueTask<PublishResponse>(
                        CreatePublishResponse(4242u, h!.RequestHandle));
                };

                sut.MinPublishWorkerCount = 1;
                sut.MaxPublishWorkerCount = 1;
                sut.Resume();

                await WaitUntilAsync(() => Volatile.Read(ref publishCount) >= 5,
                    testCt).ConfigureAwait(false);

                Assert.That(session.DeleteCalls, Is.Empty,
                    "A subscription the session owns must never be deleted as abandoned.");
                Assert.That(session.SessionDispatchCount, Is.GreaterThan(0),
                    "Notifications for a session-owned subscription must be delivered to it.");
            }
        }

        /// <summary>
        /// A session with only a classic subscription still needs a Publish worker.
        /// The manager does not own that subscription, but it owns the shared Publish
        /// pipeline that dispatches responses to it.
        /// </summary>
        [Test]
        [CancelAfter(30_000)]
        public async Task ClassicSessionSubscriptionStartsPublishWorkerAsync(
            CancellationToken testCt)
        {
            ILoggerFactory loggerFactory = m_telemetry.LoggerFactory;
            var session = new FakeSubscriptionManagerContext();
            session.SessionOwnedSubscriptionIds.Add(4242u);
            var publishSeen = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            session.OnPublishAsync = (header, acknowledgements, ct) =>
            {
                publishSeen.TrySetResult(true);
                return new ValueTask<PublishResponse>(
                    CreatePublishResponse(4242u, header!.RequestHandle));
            };

            var sut = new SubscriptionManager(
                session,
                loggerFactory,
                DiagnosticsMasks.None)
            {
                MinPublishWorkerCount = 1,
                MaxPublishWorkerCount = 1
            };
            await using (sut.ConfigureAwait(false))
            {
                sut.Resume();
                sut.Update();

                await publishSeen.Task.WaitAsync(testCt).ConfigureAwait(false);
                await WaitUntilAsync(
                    () => session.SessionDispatchCount > 0,
                    testCt).ConfigureAwait(false);
                await WaitForPublishWorkerCountAsync(sut, 1).ConfigureAwait(false);

                Assert.Multiple(() =>
                {
                    Assert.That(sut.Count, Is.Zero);
                    Assert.That(sut.PublishWorkerCount, Is.EqualTo(1));
                    Assert.That(session.DeleteCalls, Is.Empty);
                });
            }
        }

        /// <summary>
        /// The publish controller must never size the pool from the default
        /// Min/MaxPublishWorkerCount. It runs synchronously up to its first
        /// wait, i.e. inside the constructor, so sizing there happened before
        /// the owner could configure the pool: a session that already carries
        /// subscriptions got MinPublishWorkerCount (default 2) workers that
        /// were torn down again by the first resize, and PublishWorkerCount
        /// transiently reported that overshoot.
        /// </summary>
        [Test]
        [CancelAfter(30_000)]
        public async Task PublishWorkerPoolIsNeverSizedFromDefaultsBeforeConfigurationAsync(
            CancellationToken testCt)
        {
            ILoggerFactory loggerFactory = m_telemetry.LoggerFactory;
            var session = new FakeSubscriptionManagerContext();
            session.SessionOwnedSubscriptionIds.Add(4242u);
            // Model a transport that does not complete synchronously - the
            // manager has no created subscription to derive a publish
            // interval from, so an instantly completing fake would spin the
            // worker at full speed for the duration of the test.
            session.OnPublishAsync = async (header, acknowledgements, ct) =>
            {
                await Task.Delay(5, ct).ConfigureAwait(false);
                return CreatePublishResponse(4242u, header!.RequestHandle);
            };

            var sut = new SubscriptionManager(session,
                loggerFactory, DiagnosticsMasks.None);

            await using (sut.ConfigureAwait(false))
            {
                // Nothing has signalled the controller yet, so the defaults
                // are still in force. Guard that they still make the
                // scenario meaningful: the bug only shows when the default
                // minimum inflates the count the session alone asks for.
                Assert.Multiple(() =>
                {
                    Assert.That(sut.MinPublishWorkerCount,
                        Is.GreaterThan(session.SessionSubscriptionCount),
                        "Defaults no longer inflate the pool; pick a scenario " +
                        "that still exercises constructor-time sizing.");
                    Assert.That(sut.PublishWorkerCount, Is.Zero,
                        "The constructor must not size the publish worker pool.");
                });

                sut.MinPublishWorkerCount = 1;
                sut.MaxPublishWorkerCount = 1;
                sut.Resume();
                sut.Update();

                await WaitForPublishWorkerCountAsync(sut, 1).ConfigureAwait(false);

                // The pool must stay at the configured maximum while the
                // workers publish - a worker that exits is reaped before its
                // replacement is created, so the two are never counted
                // together.
                var sw = Stopwatch.StartNew();
                while (sw.Elapsed < TimeSpan.FromSeconds(1))
                {
                    Assert.That(sut.PublishWorkerCount, Is.EqualTo(1));
                    await Task.Delay(10, testCt).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// While an identifier stays unresolved the server keeps answering with
        /// the same undeliverable response. The worker must back off instead of
        /// republishing immediately - otherwise it busy-spins, hammers the
        /// server and floods the log until the creation window closes.
        /// </summary>
        [Test]
        [CancelAfter(30_000)]
        public async Task PublishWorkerThrottlesWhileSubscriptionIdIsUnresolvedAsync(
            CancellationToken testCt)
        {
            const int kMaxExpectedPublishes = 100;

            ILoggerFactory loggerFactory = m_telemetry.LoggerFactory;
            var session = new FakeSubscriptionManagerContext();
            OptionsMonitor<SubscriptionOptions> createdOptions =
                OptionsFactory.Create<SubscriptionOptions>();
            OptionsMonitor<SubscriptionOptions> pendingOptions =
                OptionsFactory.Create<SubscriptionOptions>();

            var created = new FakeManagedSubscription { Id = 1u, Created = true };
            var pending = new FakeManagedSubscription { Id = 0u, IsCreationInProgress = true };

            var sut = new SubscriptionManager(session,
                loggerFactory, DiagnosticsMasks.None);
            await using (sut.ConfigureAwait(false))
            {
                session.CreateSubscriptionFactory = (handler, options, queue)
                    => ReferenceEquals(options, createdOptions) ? created : pending;

                sut.Add(m_mockNotificationDataHandler.Object, createdOptions);
                sut.Add(m_mockNotificationDataHandler.Object, pendingOptions);

                int publishCount = 0;
                session.OnPublishAsync = (h, a, ct) =>
                {
                    Interlocked.Increment(ref publishCount);
                    return new ValueTask<PublishResponse>(
                        CreatePublishResponse(4242u, h!.RequestHandle));
                };

                sut.MinPublishWorkerCount = 1;
                sut.MaxPublishWorkerCount = 1;
                sut.Resume();

                await WaitUntilAsync(() => Volatile.Read(ref publishCount) >= 3,
                    testCt).ConfigureAwait(false);
                await Task.Delay(500, testCt).ConfigureAwait(false);

                Assert.That(Volatile.Read(ref publishCount),
                    Is.LessThan(kMaxExpectedPublishes),
                    "The publish worker must throttle while a subscription id " +
                    "cannot be resolved instead of republishing in a tight loop.");
                Assert.That(session.DeleteCalls, Is.Empty);
            }
        }

        /// <summary>
        /// A genuine orphan must be deleted exactly once. The server keeps
        /// answering with the orphan until the delete takes effect, so without
        /// retiring the identifier the worker issues a DeleteSubscriptions call
        /// for every single publish response.
        /// </summary>
        [Test]
        [CancelAfter(30_000)]
        public async Task PublishWorkerDeletesUnknownSubscriptionOnlyOnceAsync(
            CancellationToken testCt)
        {
            ILoggerFactory loggerFactory = m_telemetry.LoggerFactory;
            var session = new FakeSubscriptionManagerContext();
            OptionsMonitor<SubscriptionOptions> options =
                OptionsFactory.Create<SubscriptionOptions>();
            var created = new FakeManagedSubscription { Id = 1u, Created = true };

            var sut = new SubscriptionManager(session,
                loggerFactory, DiagnosticsMasks.None);
            await using (sut.ConfigureAwait(false))
            {
                session.CreateSubscriptionFactory = (handler, opts, queue) => created;
                sut.Add(m_mockNotificationDataHandler.Object, options);

                int publishCount = 0;
                session.OnPublishAsync = (h, a, ct) =>
                {
                    Interlocked.Increment(ref publishCount);
                    return new ValueTask<PublishResponse>(
                        CreatePublishResponse(4242u, h!.RequestHandle));
                };

                sut.MinPublishWorkerCount = 1;
                sut.MaxPublishWorkerCount = 1;
                sut.Resume();

                await WaitUntilAsync(() => session.DeleteCallsCount > 0, testCt)
                    .ConfigureAwait(false);
                await Task.Delay(500, testCt).ConfigureAwait(false);

                IReadOnlyList<FakeSubscriptionManagerContext.DeleteCall> deleteCalls =
                    session.DeleteCalls;
                Assert.That(deleteCalls, Has.Count.EqualTo(1),
                    "The orphaned subscription must be deleted exactly once.");
                Assert.That(deleteCalls[0].SubscriptionIds.ToList(),
                    Does.Contain(4242u));
            }
        }

        /// <summary>
        /// The publish controller must not lose worker pool resize signals.
        /// Re-arming its control wait on every loop iteration abandoned the
        /// previous waiter inside the auto reset event, so a later signal was
        /// handed to the dead waiter and the pool stopped resizing.
        /// </summary>
        [Test]
        [CancelAfter(30_000)]
        public async Task PublishControllerKeepsResizingAfterWorkersExitAsync(
            CancellationToken testCt)
        {
            const int kForcedWorkerExits = 2;

            ILoggerFactory loggerFactory = m_telemetry.LoggerFactory;
            var session = new FakeSubscriptionManagerContext();
            OptionsMonitor<SubscriptionOptions> options =
                OptionsFactory.Create<SubscriptionOptions>();
            var subscription = new FakeManagedSubscription { Id = 1u, Created = true };

            var sut = new SubscriptionManager(session,
                loggerFactory, DiagnosticsMasks.None);
            await using (sut.ConfigureAwait(false))
            {
                session.CreateSubscriptionFactory = (handler, opts, queue) => subscription;
                sut.Add(m_mockNotificationDataHandler.Object, options);

                int publishCount = 0;
                session.OnPublishAsync = async (h, a, ct) =>
                {
                    if (Interlocked.Increment(ref publishCount) <= kForcedWorkerExits)
                    {
                        // Terminates the worker task, which wakes the
                        // controller through the worker task instead of
                        // through its control wait.
                        throw new OperationCanceledException();
                    }
                    // Park the replacement worker so the controller is idle
                    // when the resize signal below is raised.
                    await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
                    return new PublishResponse();
                };

                sut.MaxPublishWorkerCount = 5;
                sut.MinPublishWorkerCount = 1;
                sut.Resume();

                await WaitUntilAsync(
                    () => Volatile.Read(ref publishCount) > kForcedWorkerExits,
                    testCt).ConfigureAwait(false);
                await WaitForPublishWorkerCountAsync(sut, 1).ConfigureAwait(false);

                // Single signal — under the defect it was swallowed by one of
                // the waiters abandoned while the workers were exiting.
                sut.MinPublishWorkerCount = 3;

                await WaitForPublishWorkerCountAsync(sut, 3).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Adding and retiring subscriptions concurrently must leave exactly
        /// the subscriptions that were not retired registered — the scenario
        /// from #4113 reduced to the manager surface. The surviving
        /// subscriptions are deliberately left pending creation (id zero),
        /// which is what made them eligible victims of the id based lookup.
        /// </summary>
        [Test]
        [CancelAfter(60_000)]
        public async Task ConcurrentAddAndCompleteRetireOnlyCompletedSubscriptionsAsync()
        {
            const int kWorkers = 8;
            const int kIterations = 25;

            ILoggerFactory loggerFactory = m_telemetry.LoggerFactory;
            var session = new FakeSubscriptionManagerContext();
            var sut = new SubscriptionManager(session,
                loggerFactory, DiagnosticsMasks.None);
            await using (sut.ConfigureAwait(false))
            {
                int nextId = 0;
                var survivors = new ConcurrentBag<ISubscription>();
                var partitions = new ConcurrentDictionary<
                    IOptionsMonitor<SubscriptionOptions>, FakeManagedSubscription>();
                session.CreateSubscriptionFactory =
                    (handler, options, queue) => partitions[options];

                await Task.WhenAll(Enumerable.Range(0, kWorkers)
                    .Select(_ => Task.Run(async () =>
                    {
                        for (int i = 0; i < kIterations; i++)
                        {
                            // Added but not created yet — shares id zero with
                            // every other subscription awaiting creation.
                            OptionsMonitor<SubscriptionOptions> survivorOptions =
                                OptionsFactory.Create<SubscriptionOptions>();
                            partitions[survivorOptions] = new FakeManagedSubscription();
                            survivors.Add(sut.Add(
                                m_mockNotificationDataHandler.Object, survivorOptions));

                            OptionsMonitor<SubscriptionOptions> churnOptions =
                                OptionsFactory.Create<SubscriptionOptions>();
                            uint churnId = (uint)Interlocked.Increment(ref nextId);
                            var churn = new FakeManagedSubscription
                            {
                                Id = churnId,
                                Created = true
                            };
                            partitions[churnOptions] = churn;
                            sut.Add(m_mockNotificationDataHandler.Object, churnOptions);

                            // Mimic the server side delete, which resets the id
                            // before the message processor drains and completes.
                            churn.Id = 0u;
                            churn.Created = false;
                            await sut.CompleteAsync(churn, churnId, CancellationToken.None)
                                .ConfigureAwait(false);
                            await Task.Yield();
                        }
                    }))).ConfigureAwait(false);

                Assert.That(sut.Count, Is.EqualTo(kWorkers * kIterations));
                Assert.That(sut.Items, Is.EquivalentTo(survivors));
            }
        }

        private static PublishResponse CreatePublishResponse(
            uint subscriptionId, uint sequenceNumber)
        {
            return new PublishResponse
            {
                AvailableSequenceNumbers = [],
                NotificationMessage = new NotificationMessage
                {
                    SequenceNumber = sequenceNumber
                },
                Results = [],
                SubscriptionId = subscriptionId,
                MoreNotifications = false,
                ResponseHeader = new ResponseHeader
                {
                    ServiceResult = StatusCodes.Good,
                    StringTable = []
                }
            };
        }

        private static async Task WaitUntilAsync(Func<bool> condition,
            CancellationToken ct, TimeSpan? timeout = null)
        {
            timeout ??= TimeSpan.FromSeconds(10);
            var sw = Stopwatch.StartNew();
            while (!condition())
            {
                Assert.That(sw.Elapsed, Is.LessThan(timeout.Value),
                    "Timed out waiting for the expected condition.");
                await Task.Delay(25, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Registers a factory that creates created fake partitions with ids 1, 2, ...
        /// </summary>
        private static List<FakeManagedSubscription> AddPartitionFactory(FakeSubscriptionManagerContext context)
        {
            var partitions = new List<FakeManagedSubscription>();
            var partitionsLock = new Lock();
            int next = 0;
            context.CreateSubscriptionFactory = (_, _, _) =>
            {
                var partition = new FakeManagedSubscription
                {
                    Id = (uint)Interlocked.Increment(ref next),
                    Created = true,
                    CurrentPublishingInterval = TimeSpan.FromMilliseconds(1),
                    CurrentKeepAliveCount = 1
                };
                lock (partitionsLock)
                {
                    partitions.Add(partition);
                }
                return partition;
            };
            return partitions;
        }

        /// <summary>
        /// Creates a data notification message with the given sequence number.
        /// </summary>
        private static NotificationMessage CreateDataMessage(uint sequenceNumber)
        {
            return new NotificationMessage
            {
                SequenceNumber = sequenceNumber,
                NotificationData =
                [
                    new ExtensionObject(new DataChangeNotification
                    {
                        MonitoredItems = [new MonitoredItemNotification()]
                    })
                ]
            };
        }

        /// <summary>
        /// Creates a good keep-alive Publish response acknowledging every request entry.
        /// </summary>
        private static PublishResponse CreatePublishResponse(
            uint subscriptionId, ArrayOf<SubscriptionAcknowledgement> acknowledgements)
        {
            return new PublishResponse
            {
                SubscriptionId = subscriptionId,
                NotificationMessage = new NotificationMessage(),
                AvailableSequenceNumbers = [],
                Results = acknowledgements.ConvertAll(_ => StatusCodes.Good),
                ResponseHeader = new ResponseHeader { ServiceResult = StatusCodes.Good }
            };
        }

        /// <summary>
        /// Polls <see cref="SubscriptionManager.PublishWorkerCount"/> until it
        /// matches the expected value or the timeout elapses. Replaces fixed
        /// <c>Task.Delay</c>-based waits, which are flaky on slow CI runners.
        /// </summary>
        private static async Task WaitForPublishWorkerCountAsync(
            SubscriptionManager sut,
            int expected,
            TimeSpan? timeout = null)
        {
            timeout ??= TimeSpan.FromSeconds(5);
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < timeout.Value && sut.PublishWorkerCount != expected)
            {
                await Task.Delay(25).ConfigureAwait(false);
            }
            Assert.That(sut.PublishWorkerCount, Is.EqualTo(expected),
                $"Expected PublishWorkerCount={expected} within {timeout.Value}; " +
                $"got {sut.PublishWorkerCount} after {sw.ElapsedMilliseconds} ms.");
        }

        /// <summary>
        /// Records log messages from every logger it creates.
        /// </summary>
        private sealed class CapturingLoggerProvider : ILoggerProvider, ILogger
        {
            /// <summary>
            /// Counts warnings whose message contains <paramref name="text"/>.
            /// </summary>
            public int WarningCount(string text)
            {
                return Count(LogLevel.Warning, text);
            }

            /// <summary>
            /// Counts messages at <paramref name="level"/> that contain <paramref name="text"/>.
            /// </summary>
            public int Count(LogLevel level, string text)
            {
                lock (m_lock)
                {
                    return m_entries.Count(entry =>
                        entry.Level == level && entry.Message.Contains(text, StringComparison.Ordinal));
                }
            }

            /// <inheritdoc/>
            public ILogger CreateLogger(string categoryName)
            {
                return this;
            }

            /// <inheritdoc/>
            public void Dispose()
            {
            }

            /// <inheritdoc/>
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            {
                return null;
            }

            /// <inheritdoc/>
            public bool IsEnabled(LogLevel logLevel)
            {
                return true;
            }

            /// <inheritdoc/>
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
                Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (m_lock)
                {
                    m_entries.Add((logLevel, formatter(state, exception)));
                }
            }

            private readonly Lock m_lock = new();
            private readonly List<(LogLevel Level, string Message)> m_entries = [];
        }

        private const string kRejectedText = "The server rejected acknowledgements";
        private const string kDeferredWarningText = "deferred by the acknowledgement policy";
        private static readonly bool[] s_resumedThenPaused = [false, true];
        private FakeSubscriptionManagerContext m_session;
        private ITelemetryContext m_telemetry;
        private Mock<ISubscriptionNotificationHandler> m_mockNotificationDataHandler;
#pragma warning disable NUnit1032 // Disposed via DisposeAsync in TearDown
        private SubscriptionManager m_subscriptionManager;
#pragma warning restore NUnit1032
    }
}
