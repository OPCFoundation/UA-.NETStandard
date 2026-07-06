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

#nullable enable

// CA2000: test code; the hand-rolled managed-subscription stubs are
// IAsyncDisposable and their ownership is transferred to the manager under
// test (it disposes them on DisposeAsync). CA2000 cannot see through that
// transfer and would be noisy without a real leak risk. Disabled file-level.
#pragma warning disable CA2000

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.MonitoredItems;

namespace Opc.Ua.Subscriptions.Tests
{
    /// <summary>
    /// Deterministic, fully offline unit tests for the internal
    /// <see cref="SubscriptionManager"/>. The background publish
    /// controller launched in the constructor is kept quiescent: the
    /// stub context never marks a subscription as <c>Created</c>, so
    /// <c>GetDesiredPublishWorkerCount</c> returns 0 and no publish
    /// worker is ever spawned. Every test asserts on concrete values
    /// and never depends on wall-clock timing.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("SubscriptionManager")]
    [Category("SubscriptionManagerUnit")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class SubscriptionManagerCoverageTests
    {
        private static SubscriptionManager CreateManager(
            StubSubscriptionManagerContext context,
            DiagnosticsMasks diagnostics = DiagnosticsMasks.None)
        {
            return new SubscriptionManager(
                context, NullLoggerFactory.Instance, diagnostics);
        }

        private static Opc.Ua.OptionsMonitor<SubscriptionOptions> SinglePartitionOptions()
        {
            // DisableUnboundedItemMode selects the inert single-partition
            // wrapper path in Add (no placement policy, factory or idle
            // timer), keeping the manager fully deterministic.
            return new Opc.Ua.OptionsMonitor<SubscriptionOptions>(
                new SubscriptionOptions { DisableUnboundedItemMode = true });
        }

        [Test]
        public async Task ConstructorInitializesQuiescentDefaultsAsync()
        {
            var context = new StubSubscriptionManagerContext();
            SubscriptionManager manager = CreateManager(context);
            await using (manager.ConfigureAwait(false))
            {
                Assert.That(manager.Count, Is.Zero);
                Assert.That(manager.Items, Is.Empty);
                Assert.That(manager.PublishWorkerCount, Is.Zero);
                Assert.That(manager.GoodPublishRequestCount, Is.Zero);
                Assert.That(manager.BadPublishRequestCount, Is.Zero);
                Assert.That(manager.MissingMessageCount, Is.Zero);
                Assert.That(manager.RepublishMessageCount, Is.Zero);
                Assert.That(manager.CreatedCount, Is.Zero);
                Assert.That(manager.Created, Is.Empty);
                Assert.That(manager.MinPublishWorkerCount, Is.EqualTo(2));
                Assert.That(manager.MaxPublishWorkerCount, Is.EqualTo(15));
                Assert.That(manager.TransferSubscriptionsOnRecreate, Is.False);
                Assert.That(manager.PoolNotifications, Is.False);
            }
            // The publish controller stayed parked the whole time.
            Assert.That(context.PublishCallCount, Is.Zero);
        }

        [Test]
        public async Task ConstructorStoresReturnDiagnosticsAsync()
        {
            var context = new StubSubscriptionManagerContext();
            SubscriptionManager manager = CreateManager(context, DiagnosticsMasks.All);
            await using (manager.ConfigureAwait(false))
            {
                Assert.That(manager.ReturnDiagnostics, Is.EqualTo(DiagnosticsMasks.All));
            }
        }

        [Test]
        public async Task MinPublishWorkerCountHonoursChangedAndEqualBranchesAsync()
        {
            var context = new StubSubscriptionManagerContext();
            SubscriptionManager manager = CreateManager(context);
            await using (manager.ConfigureAwait(false))
            {
                Assert.That(manager.MinPublishWorkerCount, Is.EqualTo(2));

                // Equal-value early-return branch: value must be unchanged.
                manager.MinPublishWorkerCount = 2;
                Assert.That(manager.MinPublishWorkerCount, Is.EqualTo(2));

                // Changed-value branch: value updated and controller signalled.
                manager.MinPublishWorkerCount = 7;
                Assert.That(manager.MinPublishWorkerCount, Is.EqualTo(7));

                // Equal-value branch again on the new value.
                manager.MinPublishWorkerCount = 7;
                Assert.That(manager.MinPublishWorkerCount, Is.EqualTo(7));
            }
            // No subscription is Created, so signalling never spawns a worker.
            Assert.That(context.PublishCallCount, Is.Zero);
        }

        [Test]
        public async Task MaxPublishWorkerCountHonoursChangedAndEqualBranchesAsync()
        {
            var context = new StubSubscriptionManagerContext();
            SubscriptionManager manager = CreateManager(context);
            await using (manager.ConfigureAwait(false))
            {
                Assert.That(manager.MaxPublishWorkerCount, Is.EqualTo(15));

                // Equal-value early-return branch.
                manager.MaxPublishWorkerCount = 15;
                Assert.That(manager.MaxPublishWorkerCount, Is.EqualTo(15));

                // Changed-value branch.
                manager.MaxPublishWorkerCount = 30;
                Assert.That(manager.MaxPublishWorkerCount, Is.EqualTo(30));

                // Equal-value branch again.
                manager.MaxPublishWorkerCount = 30;
                Assert.That(manager.MaxPublishWorkerCount, Is.EqualTo(30));
            }
            Assert.That(context.PublishCallCount, Is.Zero);
        }

        [Test]
        public async Task TransferSubscriptionsOnRecreateRoundTripsAsync()
        {
            var context = new StubSubscriptionManagerContext();
            SubscriptionManager manager = CreateManager(context);
            await using (manager.ConfigureAwait(false))
            {
                Assert.That(manager.TransferSubscriptionsOnRecreate, Is.False);
                manager.TransferSubscriptionsOnRecreate = true;
                Assert.That(manager.TransferSubscriptionsOnRecreate, Is.True);
                manager.TransferSubscriptionsOnRecreate = false;
                Assert.That(manager.TransferSubscriptionsOnRecreate, Is.False);
            }
        }

        [Test]
        public async Task PoolNotificationsRoundTripsAsync()
        {
            var context = new StubSubscriptionManagerContext();
            SubscriptionManager manager = CreateManager(context);
            await using (manager.ConfigureAwait(false))
            {
                // PoolNotifications is a single public auto-property that
                // implicitly implements both ISubscriptionManager and
                // IMessageAckQueue, so the manager surface is authoritative.
                Assert.That(manager.PoolNotifications, Is.False);

                manager.PoolNotifications = true;
                Assert.That(manager.PoolNotifications, Is.True);

                manager.PoolNotifications = false;
                Assert.That(manager.PoolNotifications, Is.False);
            }
        }

        [Test]
        public async Task ReturnDiagnosticsRoundTripsAsync()
        {
            var context = new StubSubscriptionManagerContext();
            SubscriptionManager manager = CreateManager(context, DiagnosticsMasks.None);
            await using (manager.ConfigureAwait(false))
            {
                Assert.That(manager.ReturnDiagnostics, Is.EqualTo(DiagnosticsMasks.None));

                manager.ReturnDiagnostics = DiagnosticsMasks.All;
                Assert.That(manager.ReturnDiagnostics, Is.EqualTo(DiagnosticsMasks.All));

                manager.ReturnDiagnostics = DiagnosticsMasks.OperationAll;
                Assert.That(manager.ReturnDiagnostics, Is.EqualTo(DiagnosticsMasks.OperationAll));
            }
        }

        [Test]
        public async Task QueueAsyncThenDropPendingRemovesOnlyMatchingAcksAsync()
        {
            var context = new StubSubscriptionManagerContext();
            SubscriptionManager manager = CreateManager(context);
            await using (manager.ConfigureAwait(false))
            {
                await manager.QueueAsync(new SubscriptionAcknowledgement
                {
                    SubscriptionId = 1u,
                    SequenceNumber = 10u
                }, CancellationToken.None).ConfigureAwait(false);
                await manager.QueueAsync(new SubscriptionAcknowledgement
                {
                    SubscriptionId = 2u,
                    SequenceNumber = 11u
                }, CancellationToken.None).ConfigureAwait(false);
                await manager.QueueAsync(new SubscriptionAcknowledgement
                {
                    SubscriptionId = 1u,
                    SequenceNumber = 12u
                }, CancellationToken.None).ConfigureAwait(false);

                Assert.That(manager.DropPendingForSubscription(1u), Is.EqualTo(2));
                Assert.That(manager.DropPendingForSubscription(1u), Is.Zero);
                Assert.That(manager.DropPendingForSubscription(2u), Is.EqualTo(1));
            }
        }

        [Test]
        public async Task DropPendingForSubscriptionOnEmptyQueueReturnsZeroAsync()
        {
            var context = new StubSubscriptionManagerContext();
            SubscriptionManager manager = CreateManager(context);
            await using (manager.ConfigureAwait(false))
            {
                Assert.That(manager.DropPendingForSubscription(7u), Is.Zero);
            }
        }

        [Test]
        public async Task CompleteAsyncUnknownSubscriptionReturnsWithoutChangeAsync()
        {
            var context = new StubSubscriptionManagerContext();
            SubscriptionManager manager = CreateManager(context);
            await using (manager.ConfigureAwait(false))
            {
                await manager.CompleteAsync(999u, CancellationToken.None).ConfigureAwait(false);
                Assert.That(manager.Count, Is.Zero);
            }
            Assert.That(context.PublishCallCount, Is.Zero);
        }

        [Test]
        public async Task UpdateSignalsControllerWithoutThrowingAsync()
        {
            var context = new StubSubscriptionManagerContext();
            SubscriptionManager manager = CreateManager(context);
            await using (manager.ConfigureAwait(false))
            {
                manager.Update();
                Assert.That(manager.Count, Is.Zero);
            }
            Assert.That(context.PublishCallCount, Is.Zero);
        }

        [Test]
        public async Task DrainAsyncCompletesImmediatelyWhenNoPublishActiveAsync()
        {
            var context = new StubSubscriptionManagerContext();
            SubscriptionManager manager = CreateManager(context);
            await using (manager.ConfigureAwait(false))
            {
                Task drain = manager.DrainAsync(CancellationToken.None);
                Assert.That(drain.IsCompleted, Is.True);
                await drain.ConfigureAwait(false);
            }
        }

        [Test]
        public async Task ResumeWithoutSubscriptionsDoesNotThrowAsync()
        {
            var context = new StubSubscriptionManagerContext();
            SubscriptionManager manager = CreateManager(context);
            await using (manager.ConfigureAwait(false))
            {
                manager.Resume();
                Assert.That(manager.Count, Is.Zero);
            }
            Assert.That(context.PublishCallCount, Is.Zero);
        }

        [Test]
        public async Task PauseWithoutSubscriptionsDoesNotThrowAsync()
        {
            var context = new StubSubscriptionManagerContext();
            SubscriptionManager manager = CreateManager(context);
            await using (manager.ConfigureAwait(false))
            {
                manager.Pause();
                Assert.That(manager.Count, Is.Zero);
            }
        }

        [Test]
        public async Task RecreateSubscriptionsAsyncWithNoSubscriptionsReturnsImmediatelyAsync()
        {
            var context = new StubSubscriptionManagerContext();
            SubscriptionManager manager = CreateManager(context);
            await using (manager.ConfigureAwait(false))
            {
                await manager.RecreateSubscriptionsAsync(null, CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.That(manager.Count, Is.Zero);
            }
        }

        [Test]
        public async Task DisposeAsyncIsIdempotentAsync()
        {
            var context = new StubSubscriptionManagerContext();
            SubscriptionManager manager = CreateManager(context);
            await manager.DisposeAsync().ConfigureAwait(false);
            // Second dispose must be a no-op and must not throw.
            await manager.DisposeAsync().ConfigureAwait(false);
            Assert.That(manager.Count, Is.Zero);
            Assert.That(context.PublishCallCount, Is.Zero);
        }

        [Test]
        public async Task AddRegistersSubscriptionAndReturnsWrapperAsync()
        {
            var context = new StubSubscriptionManagerContext();
            var handler = new NoopNotificationHandler();
            var subscription = new StubManagedSubscription { Id = 1u };
            SubscriptionManager manager = CreateManager(context);
            await using (manager.ConfigureAwait(false))
            {
                context.CreateSubscriptionFactory = (_, _, _) => subscription;

                ISubscription result = manager.Add(handler, SinglePartitionOptions());

                Assert.That(result, Is.Not.Null);
                Assert.That(manager.Count, Is.EqualTo(1));
                Assert.That(manager.Items, Has.Exactly(1).Items);
                Assert.That(context.CreateSubscriptionCallCount, Is.EqualTo(1));
                Assert.That(context.LastCreateQueue, Is.SameAs(manager));
                Assert.That(manager.PublishWorkerCount, Is.Zero);
            }
            Assert.That(context.PublishCallCount, Is.Zero);
        }

        [Test]
        public async Task AddWithNullHandlerThrowsArgumentNullExceptionAsync()
        {
            var context = new StubSubscriptionManagerContext();
            SubscriptionManager manager = CreateManager(context);
            await using (manager.ConfigureAwait(false))
            {
                ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                    () => manager.Add(null!, SinglePartitionOptions()));
                Assert.That(ex.ParamName, Is.EqualTo("handler"));
            }
        }

        [Test]
        public async Task AddWithNullOptionsThrowsArgumentNullExceptionAsync()
        {
            var context = new StubSubscriptionManagerContext();
            var handler = new NoopNotificationHandler();
            SubscriptionManager manager = CreateManager(context);
            await using (manager.ConfigureAwait(false))
            {
                ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                    () => manager.Add(handler, null!));
                Assert.That(ex.ParamName, Is.EqualTo("options"));
            }
        }

        [Test]
        public async Task AddTwoSubscriptionsCountAndItemsReflectBothAsync()
        {
            var context = new StubSubscriptionManagerContext();
            var handler = new NoopNotificationHandler();
            var subscriptions = new Queue<IManagedSubscription>(new IManagedSubscription[]
            {
                new StubManagedSubscription { Id = 1u },
                new StubManagedSubscription { Id = 2u }
            });
            SubscriptionManager manager = CreateManager(context);
            await using (manager.ConfigureAwait(false))
            {
                context.CreateSubscriptionFactory = (_, _, _) => subscriptions.Dequeue();

                manager.Add(handler, SinglePartitionOptions());
                manager.Add(handler, SinglePartitionOptions());

                Assert.That(manager.Count, Is.EqualTo(2));
                Assert.That(manager.Items, Has.Exactly(2).Items);
                Assert.That(context.CreateSubscriptionCallCount, Is.EqualTo(2));
            }
        }

        [Test]
        public async Task MissingMessageCountAggregatesAcrossSubscriptionsAsync()
        {
            var context = new StubSubscriptionManagerContext();
            var handler = new NoopNotificationHandler();
            var subscriptions = new Queue<IManagedSubscription>(new IManagedSubscription[]
            {
                new StubManagedSubscription { Id = 1u, MissingMessageCount = 3 },
                new StubManagedSubscription { Id = 2u, MissingMessageCount = 4 }
            });
            SubscriptionManager manager = CreateManager(context);
            await using (manager.ConfigureAwait(false))
            {
                context.CreateSubscriptionFactory = (_, _, _) => subscriptions.Dequeue();

                manager.Add(handler, SinglePartitionOptions());
                manager.Add(handler, SinglePartitionOptions());

                Assert.That(manager.MissingMessageCount, Is.EqualTo(7));
            }
        }

        [Test]
        public async Task RepublishMessageCountAggregatesAcrossSubscriptionsAsync()
        {
            var context = new StubSubscriptionManagerContext();
            var handler = new NoopNotificationHandler();
            var subscriptions = new Queue<IManagedSubscription>(new IManagedSubscription[]
            {
                new StubManagedSubscription { Id = 1u, RepublishMessageCount = 2 },
                new StubManagedSubscription { Id = 2u, RepublishMessageCount = 5 }
            });
            SubscriptionManager manager = CreateManager(context);
            await using (manager.ConfigureAwait(false))
            {
                context.CreateSubscriptionFactory = (_, _, _) => subscriptions.Dequeue();

                manager.Add(handler, SinglePartitionOptions());
                manager.Add(handler, SinglePartitionOptions());

                Assert.That(manager.RepublishMessageCount, Is.EqualTo(7));
            }
        }

        [Test]
        public async Task CreatedCountIsZeroWhenSubscriptionsAreNotCreatedAsync()
        {
            var context = new StubSubscriptionManagerContext();
            var handler = new NoopNotificationHandler();
            var subscription = new StubManagedSubscription { Id = 1u, Created = false };
            SubscriptionManager manager = CreateManager(context);
            await using (manager.ConfigureAwait(false))
            {
                context.CreateSubscriptionFactory = (_, _, _) => subscription;

                manager.Add(handler, SinglePartitionOptions());

                Assert.That(manager.Count, Is.EqualTo(1));
                Assert.That(manager.CreatedCount, Is.Zero);
                Assert.That(manager.Created, Is.Empty);
                Assert.That(manager.PublishWorkerCount, Is.Zero);
            }
            Assert.That(context.PublishCallCount, Is.Zero);
        }

        [Test]
        public async Task CompleteAsyncRemovesKnownSubscriptionAsync()
        {
            var context = new StubSubscriptionManagerContext();
            var handler = new NoopNotificationHandler();
            var subscription = new StubManagedSubscription { Id = 1u };
            SubscriptionManager manager = CreateManager(context);
            await using (manager.ConfigureAwait(false))
            {
                context.CreateSubscriptionFactory = (_, _, _) => subscription;

                manager.Add(handler, SinglePartitionOptions());
                Assert.That(manager.Count, Is.EqualTo(1));

                await manager.CompleteAsync(1u, CancellationToken.None).ConfigureAwait(false);

                Assert.That(manager.Count, Is.Zero);
                Assert.That(manager.Items, Is.Empty);
                // CompleteAsync only unregisters; it does not dispose the partition.
                Assert.That(subscription.DisposeAsyncCalls, Is.Zero);
            }
        }

        [Test]
        public async Task DisposeAsyncDisposesRegisteredSubscriptionsAsync()
        {
            var context = new StubSubscriptionManagerContext();
            var handler = new NoopNotificationHandler();
            var subscription = new StubManagedSubscription { Id = 1u };
            SubscriptionManager manager = CreateManager(context);
            context.CreateSubscriptionFactory = (_, _, _) => subscription;

            manager.Add(handler, SinglePartitionOptions());
            Assert.That(manager.Count, Is.EqualTo(1));

            await manager.DisposeAsync().ConfigureAwait(false);

            Assert.That(subscription.DisposeAsyncCalls, Is.EqualTo(1));
            Assert.That(manager.Count, Is.Zero);
        }

        [Test]
        public async Task RecreateSubscriptionsAsyncInvokesRecreateOnEachSubscriptionAsync()
        {
            var context = new StubSubscriptionManagerContext();
            var handler = new NoopNotificationHandler();
            var subscription = new StubManagedSubscription { Id = 1u, Created = false };
            SubscriptionManager manager = CreateManager(context);
            await using (manager.ConfigureAwait(false))
            {
                context.CreateSubscriptionFactory = (_, _, _) => subscription;

                manager.Add(handler, SinglePartitionOptions());

                // previousSessionId == null and TransferSubscriptionsOnRecreate
                // defaults to false, so no server transfer is attempted; every
                // subscription is recreated directly.
                await manager.RecreateSubscriptionsAsync(null, CancellationToken.None)
                    .ConfigureAwait(false);

                Assert.That(subscription.RecreateAsyncCalls, Is.EqualTo(1));
            }
            Assert.That(context.PublishCallCount, Is.Zero);
        }

        [Test]
        public async Task ResumeAndPauseNotifyRegisteredSubscriptionsAsync()
        {
            var context = new StubSubscriptionManagerContext();
            var handler = new NoopNotificationHandler();
            var subscription = new StubManagedSubscription { Id = 1u };
            SubscriptionManager manager = CreateManager(context);
            await using (manager.ConfigureAwait(false))
            {
                context.CreateSubscriptionFactory = (_, _, _) => subscription;

                manager.Add(handler, SinglePartitionOptions());

                manager.Resume();
                manager.Pause();

                // Resume notifies with paused=false, then Pause with true.
                Assert.That(subscription.PausedCalls, Has.Count.EqualTo(2));
                Assert.That(subscription.PausedCalls[0], Is.False);
                Assert.That(subscription.PausedCalls[1], Is.True);
            }
            Assert.That(context.PublishCallCount, Is.Zero);
        }

        /// <summary>
        /// Hand-rolled stub for <see cref="ISubscriptionManagerContext"/>.
        /// Keeps the publish controller quiescent: it never returns publish
        /// work and records that <see cref="PublishAsync"/> was not reached.
        /// </summary>
        private sealed class StubSubscriptionManagerContext : ISubscriptionManagerContext
        {
            public int CreateSubscriptionCallCount { get; private set; }

            public IMessageAckQueue? LastCreateQueue { get; private set; }

            public int PublishCallCount => Volatile.Read(ref m_publishCallCount);

            public Func<ISubscriptionNotificationHandler,
                IOptionsMonitor<SubscriptionOptions>, IMessageAckQueue,
                IManagedSubscription>? CreateSubscriptionFactory
            { get; set; }

            public IManagedSubscription CreateSubscription(
                ISubscriptionNotificationHandler handler,
                IOptionsMonitor<SubscriptionOptions> options,
                IMessageAckQueue queue,
                SubscriptionLoadState? loadState = null)
            {
                CreateSubscriptionCallCount++;
                LastCreateQueue = queue;
                if (CreateSubscriptionFactory == null)
                {
                    throw new InvalidOperationException(
                        "CreateSubscriptionFactory was not configured for this test.");
                }
                return CreateSubscriptionFactory(handler, options, queue);
            }

            public ValueTask<PublishResponse> PublishAsync(
                RequestHeader? requestHeader,
                ArrayOf<SubscriptionAcknowledgement> subscriptionAcknowledgements,
                CancellationToken ct = default)
            {
                // Never expected to run: no subscription is marked Created, so
                // the controller spawns zero workers. Record the call so tests
                // can prove the loop stayed quiescent.
                Interlocked.Increment(ref m_publishCallCount);
                return new ValueTask<PublishResponse>(new PublishResponse());
            }

            public ValueTask<TransferSubscriptionsResponse> TransferSubscriptionsAsync(
                RequestHeader? requestHeader,
                ArrayOf<uint> subscriptionIds,
                bool sendInitialValues,
                CancellationToken ct = default)
            {
                return new ValueTask<TransferSubscriptionsResponse>(
                    new TransferSubscriptionsResponse());
            }

            public ValueTask<DeleteSubscriptionsResponse> DeleteSubscriptionsAsync(
                RequestHeader? requestHeader,
                ArrayOf<uint> subscriptionIds,
                CancellationToken ct = default)
            {
                return new ValueTask<DeleteSubscriptionsResponse>(
                    new DeleteSubscriptionsResponse());
            }

            private int m_publishCallCount;
        }

        /// <summary>
        /// Minimal, inert notification handler. None of its callbacks are
        /// invoked because no publish response is ever dispatched.
        /// </summary>
        private sealed class NoopNotificationHandler : ISubscriptionNotificationHandler
        {
            public ValueTask OnDataChangeNotificationAsync(ISubscription subscription,
                uint sequenceNumber, DateTime publishTime,
                ReadOnlyMemory<DataValueChange> notification,
                PublishState publishStateMask, IReadOnlyList<string> stringTable)
            {
                throw new NotSupportedException();
            }

            public ValueTask OnEventDataNotificationAsync(ISubscription subscription,
                uint sequenceNumber, DateTime publishTime,
                ReadOnlyMemory<EventNotification> notification,
                PublishState publishStateMask, IReadOnlyList<string> stringTable)
            {
                throw new NotSupportedException();
            }

            public ValueTask OnKeepAliveNotificationAsync(ISubscription subscription,
                uint sequenceNumber, DateTime publishTime,
                PublishState publishStateMask)
            {
                throw new NotSupportedException();
            }

            public ValueTask OnSubscriptionStateChangedAsync(ISubscription subscription,
                SubscriptionState state, PublishState publishStateMask,
                CancellationToken ct = default)
            {
                throw new NotSupportedException();
            }
        }

        /// <summary>
        /// Hand-rolled stub for the internal <see cref="IManagedSubscription"/>.
        /// Exposes settable identity/counter state and records the small set of
        /// lifecycle callbacks the tests assert on. All dispatch-driven members
        /// throw because they are never reached in these quiescent tests.
        /// </summary>
        private sealed class StubManagedSubscription : IManagedSubscription
        {
            public uint Id { get; init; }

            public bool Created { get; init; }

            public long MissingMessageCount { get; init; }

            public long RepublishMessageCount { get; init; }

            public int DisposeAsyncCalls { get; private set; }

            public int RecreateAsyncCalls { get; private set; }

            public List<bool> PausedCalls { get; } = [];

            public TimeSpan CurrentPublishingInterval => TimeSpan.Zero;

            public byte CurrentPriority => 0;

            public uint CurrentLifetimeCount => 0;

            public uint CurrentKeepAliveCount => 0;

            public bool CurrentPublishingEnabled => false;

            public uint CurrentMaxNotificationsPerPublish => 0;

            public IMonitoredItemCollection MonitoredItems => null!;

            public ValueTask ConditionRefreshAsync(CancellationToken ct = default)
            {
                throw new NotSupportedException();
            }

            public ValueTask<TimeSpan> SetAsDurableAsync(
                TimeSpan lifetime, CancellationToken ct = default)
            {
                throw new NotSupportedException();
            }

            public ValueTask<SetTriggeringResult> SetTriggeringAsync(
                IMonitoredItem triggeringItem,
                IReadOnlyCollection<IMonitoredItem>? linksToAdd = null,
                IReadOnlyCollection<IMonitoredItem>? linksToRemove = null,
                CancellationToken ct = default)
            {
                throw new NotSupportedException();
            }

            public ValueTask OnPublishReceivedAsync(NotificationMessage message,
                IReadOnlyList<uint>? availableSequenceNumbers,
                IReadOnlyList<string> stringTable)
            {
                throw new NotSupportedException();
            }

            public ValueTask<bool> TryCompleteTransferAsync(
                IReadOnlyList<uint> availableSequenceNumbers,
                CancellationToken ct = default)
            {
                throw new NotSupportedException();
            }

            public ValueTask RecreateAsync(CancellationToken ct = default)
            {
                RecreateAsyncCalls++;
                return default;
            }

            public void NotifySubscriptionManagerPaused(bool paused)
            {
                PausedCalls.Add(paused);
            }

            public ValueTask DisposeAsync()
            {
                DisposeAsyncCalls++;
                return default;
            }
        }
    }
}
