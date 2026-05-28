/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 * MIT License - see /Docs/License.md
 * ======================================================================*/

#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.ModelChange;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.Streaming;
using Opc.Ua.Client.Subscriptions.MonitoredItems;
using MonitoringOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;

// Tests run on the default TaskScheduler so CA2007's sync-context
// risk does not apply. The local FakeStreamingSubscription is a
// no-op IAsyncDisposable fake with nothing to dispose; CA2000's leak
// warning does not apply.
#pragma warning disable CA2007, CA2000

namespace Opc.Ua.Client.Tests.ModelChange
{
    /// <summary>
    /// Unit tests for <see cref="ModelChangeTracker"/>. Drives the
    /// tracker through a fake <see cref="IStreamingSubscription"/>
    /// that pushes <see cref="EventNotification"/>s on demand and
    /// uses a <see cref="Mock{INodeCache}"/> to observe cache
    /// invalidation calls.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("ModelChange")]
    [Parallelizable]
    public sealed class ModelChangeTrackerTests
    {
        [Test]
        public void ConstructorWithNullStreamingThrowsArgumentNullException()
        {
            Assert.That(
                () => new ModelChangeTracker(null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public async Task IsTrackingIsFalseBeforeStartAsync()
        {
            var fake = new FakeStreamingSubscription();
            await using var tracker = new ModelChangeTracker(fake);

            Assert.That(tracker.IsTracking, Is.False);
        }

        [Test]
        public async Task IsTrackingIsTrueAfterStartAsync()
        {
            var fake = new FakeStreamingSubscription();
            await using var tracker = new ModelChangeTracker(fake);

            await tracker.StartTrackingAsync();

            Assert.That(tracker.IsTracking, Is.True);
        }

        [Test]
        public async Task StartTrackingAsyncIsIdempotent()
        {
            var fake = new FakeStreamingSubscription();
            await using var tracker = new ModelChangeTracker(fake);

            await tracker.StartTrackingAsync();
            await fake.WaitForSubscribeAsync();
            await tracker.StartTrackingAsync();

            Assert.That(tracker.IsTracking, Is.True);

            // The pump task is started exactly once — a second
            // call must not subscribe again.
            Assert.That(fake.SubscribeCallCount, Is.EqualTo(1));
        }

        [Test]
        public async Task StopTrackingAsyncCompletesPumpTaskAndFlipsIsTrackingToFalse()
        {
            var fake = new FakeStreamingSubscription();
            await using var tracker = new ModelChangeTracker(fake);

            await tracker.StartTrackingAsync();
            await fake.WaitForSubscribeAsync();
            await tracker.StopTrackingAsync();

            Assert.That(tracker.IsTracking, Is.False);
            Assert.That(fake.PumpCancellationObserved, Is.True);
        }

        [Test]
        public async Task DisposeAsyncIsIdempotent()
        {
            var fake = new FakeStreamingSubscription();
            var tracker = new ModelChangeTracker(fake);
            await tracker.StartTrackingAsync();

            await tracker.DisposeAsync();
            // Second dispose must be a no-op (does not throw).
            await tracker.DisposeAsync();

            Assert.That(tracker.IsTracking, Is.False);
        }

        [Test]
        public async Task DisposeAsyncCallsStopTracking()
        {
            var fake = new FakeStreamingSubscription();
            var tracker = new ModelChangeTracker(fake);
            await tracker.StartTrackingAsync();
            await fake.WaitForSubscribeAsync();

            await tracker.DisposeAsync();

            Assert.That(tracker.IsTracking, Is.False);
            Assert.That(fake.PumpCancellationObserved, Is.True);
        }

        [Test]
        public async Task NotificationWithFewerThanThreeFieldsDoesNotRaiseModelChanged()
        {
            var fake = new FakeStreamingSubscription();
            var cache = new Mock<INodeCache>(MockBehavior.Strict);
            await using var tracker = new ModelChangeTracker(fake, cache.Object);

            int raised = 0;
            tracker.ModelChanged += (_, _) => Interlocked.Increment(ref raised);

            await tracker.StartTrackingAsync();
            await fake.WaitForSubscribeAsync();

            // Fields.Count < 3 — fast-path return, no event, no cache call.
            fake.Push(new EventNotification(
                null, ArrayOf.Wrapped<Variant>(default, default)));

            await fake.QuiesceAsync();

            Assert.That(raised, Is.Zero);
            cache.VerifyNoOtherCalls();
        }

        [Test]
        public async Task NotificationWithMalformedFieldsTriggersFullCacheInvalidation()
        {
            var fake = new FakeStreamingSubscription();
            var cache = new Mock<INodeCache>(MockBehavior.Loose);
            await using var tracker = new ModelChangeTracker(fake, cache.Object);

            ModelChangedEventArgs? observed = null;
            tracker.ModelChanged += (_, e) => observed = e;

            await tracker.StartTrackingAsync();
            await fake.WaitForSubscribeAsync();

            // fields[2] is a scalar string — definitively NOT an
            // ExtensionObject[] of ModelChangeStructureDataType, so the
            // tracker must fall back to full cache invalidation.
            fake.Push(new EventNotification(
                null,
                ArrayOf.Wrapped<Variant>(
                    default,
                    default,
                    Variant.From("not-an-extension-object-array"))));

            await fake.QuiesceAsync();

            cache.Verify(c => c.Clear(), Times.Once);
            cache.Verify(c => c.InvalidateNode(It.IsAny<NodeId>()), Times.Never);

            Assert.That(observed, Is.Not.Null);
            Assert.That(observed!.RequiresFullCacheInvalidation, Is.True);
            Assert.That(observed.Changes, Is.Empty);
        }

        [Test]
        public async Task NotificationWithNullChangesFieldTriggersFullCacheInvalidation()
        {
            // changesVariant.IsNull -> boxed is null -> still NOT
            // ExtensionObject[] -> full invalidation path.
            var fake = new FakeStreamingSubscription();
            var cache = new Mock<INodeCache>(MockBehavior.Loose);
            await using var tracker = new ModelChangeTracker(fake, cache.Object);

            ModelChangedEventArgs? observed = null;
            tracker.ModelChanged += (_, e) => observed = e;

            await tracker.StartTrackingAsync();
            await fake.WaitForSubscribeAsync();

            fake.Push(new EventNotification(
                null,
                ArrayOf.Wrapped<Variant>(default, default, default)));

            await fake.QuiesceAsync();

            cache.Verify(c => c.Clear(), Times.Once);
            Assert.That(observed, Is.Not.Null);
            Assert.That(observed!.RequiresFullCacheInvalidation, Is.True);
        }

        [Test]
        public async Task CacheInvalidationExceptionsAreLoggedButDoNotStopTracking()
        {
            var fake = new FakeStreamingSubscription();
            var cache = new Mock<INodeCache>();
            cache.Setup(c => c.Clear())
                .Throws(new InvalidOperationException("boom"));

            await using var tracker = new ModelChangeTracker(fake, cache.Object);

            int raised = 0;
            tracker.ModelChanged += (_, _) => Interlocked.Increment(ref raised);

            await tracker.StartTrackingAsync();
            await fake.WaitForSubscribeAsync();

            // First malformed notification — Clear() throws, must be swallowed,
            // ModelChanged must still fire.
            fake.Push(new EventNotification(
                null,
                ArrayOf.Wrapped<Variant>(
                    default, default, Variant.From("garbage"))));
            await fake.QuiesceAsync();

            // Second notification — pump must still be alive.
            fake.Push(new EventNotification(
                null,
                ArrayOf.Wrapped<Variant>(
                    default, default, Variant.From("garbage-2"))));
            await fake.QuiesceAsync();

            Assert.That(raised, Is.EqualTo(2));
            Assert.That(tracker.IsTracking, Is.True);
        }

        [Test]
        public async Task SubscriberHandlerExceptionsAreSwallowed()
        {
            var fake = new FakeStreamingSubscription();
            await using var tracker = new ModelChangeTracker(fake);

            int raised = 0;
            tracker.ModelChanged += (_, _) =>
            {
                Interlocked.Increment(ref raised);
                throw new InvalidOperationException("subscriber failure");
            };

            await tracker.StartTrackingAsync();
            await fake.WaitForSubscribeAsync();

            fake.Push(new EventNotification(
                null,
                ArrayOf.Wrapped<Variant>(
                    default, default, Variant.From("garbage-1"))));
            await fake.QuiesceAsync();

            // Pump still alive — a second notification is delivered.
            fake.Push(new EventNotification(
                null,
                ArrayOf.Wrapped<Variant>(
                    default, default, Variant.From("garbage-2"))));
            await fake.QuiesceAsync();

            Assert.That(raised, Is.EqualTo(2));
            Assert.That(tracker.IsTracking, Is.True);
        }

        /// <summary>
        /// Fake streaming subscription that exposes a deterministic
        /// <see cref="EventNotification"/> channel for tests. Each call
        /// to <see cref="SubscribeEventsAsync"/> increments
        /// <see cref="SubscribeCallCount"/>, signals
        /// <see cref="WaitForSubscribeAsync"/>, and then yields every
        /// notification pushed via <see cref="Push"/>. The enumerator
        /// completes when <see cref="Complete"/> is called or the
        /// supplied cancellation token fires.
        /// </summary>
        private sealed class FakeStreamingSubscription : IStreamingSubscription
        {
            private readonly Channel<EventNotification> m_channel =
                Channel.CreateUnbounded<EventNotification>(
                    new UnboundedChannelOptions
                    {
                        SingleReader = true,
                        SingleWriter = false
                    });

            private readonly TaskCompletionSource<bool> m_subscribed =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            private int m_subscribeCallCount;
            private int m_pumpCancellationObserved;
            private int m_pushed;
            private int m_handled;

            public int SubscribeCallCount => m_subscribeCallCount;

            public bool PumpCancellationObserved =>
                Volatile.Read(ref m_pumpCancellationObserved) != 0;

            public void Push(EventNotification notification)
            {
                Interlocked.Increment(ref m_pushed);
                m_channel.Writer.TryWrite(notification);
            }

            public void Complete()
            {
                m_channel.Writer.TryComplete();
            }

            public Task<bool> WaitForSubscribeAsync()
            {
                return m_subscribed.Task;
            }

            /// <summary>
            /// Spins briefly until every <see cref="Push"/>ed notification
            /// has been observed by the production-code consumer. The
            /// counter is incremented in the iterator <i>after</i> the
            /// <c>yield return</c>, i.e. once the consumer's
            /// <c>HandleNotification</c> call has returned and the
            /// foreach loop has requested the next item.
            /// </summary>
            public async Task QuiesceAsync()
            {
                for (int i = 0; i < 400; i++)
                {
                    if (Volatile.Read(ref m_handled) >= Volatile.Read(ref m_pushed))
                    {
                        return;
                    }
                    await Task.Delay(5).ConfigureAwait(false);
                }
            }

            public async IAsyncEnumerable<EventNotification> SubscribeEventsAsync(
                NodeId notifierId,
                EventFilter filter,
                MonitoringOptions? options = null,
                [EnumeratorCancellation] CancellationToken ct = default)
            {
                Interlocked.Increment(ref m_subscribeCallCount);
                m_subscribed.TrySetResult(true);

                try
                {
                    await foreach (EventNotification n in m_channel.Reader
                        .ReadAllAsync(ct).ConfigureAwait(false))
                    {
                        yield return n;
                        // Reached only after the consumer's
                        // HandleNotification returned and asked for the
                        // next item, so we can declare this push handled.
                        Interlocked.Increment(ref m_handled);
                    }
                }
                finally
                {
                    if (ct.IsCancellationRequested)
                    {
                        Interlocked.Exchange(ref m_pumpCancellationObserved, 1);
                    }
                }
            }

            public async IAsyncEnumerable<DataValueChange> SubscribeDataChangesAsync(
                NodeId nodeId,
                MonitoringOptions? options = null,
                [EnumeratorCancellation] CancellationToken ct = default)
            {
                await Task.Yield();
                yield break;
            }

            public async IAsyncEnumerable<DataValueChange> SubscribeDataChangesAsync(
                IReadOnlyList<NodeId> nodeIds,
                MonitoringOptions? options = null,
                [EnumeratorCancellation] CancellationToken ct = default)
            {
                await Task.Yield();
                yield break;
            }

            public ValueTask DisposeAsync()
            {
                Complete();
                return default;
            }
        }
    }
}
