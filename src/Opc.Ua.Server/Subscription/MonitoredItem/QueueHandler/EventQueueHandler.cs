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
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Mangages an event queue for usage by a MonitoredItem
    /// </summary>
    public interface IEventQueueHandler : IDisposable
    {
        /// <summary>
        /// Sets the queue size.
        /// </summary>
        /// <param name="queueSize">The new queue size.</param>
        /// <param name="discardOldest">Whether to discard the oldest values if the queue overflows.</param>
        void SetQueueSize(uint queueSize, bool discardOldest);

        /// <summary>
        /// The number of Items in the queue
        /// </summary>
        int ItemsInQueue { get; }

        /// <summary>
        /// True if the queue is overflowing
        /// </summary>
        bool Overflow { get; }

        /// <summary>
        /// Checks the last 1k queue entries if the event is already in there
        /// </summary>
        bool IsEventContainedInQueue(IFilterTarget instance);

        /// <summary>
        /// true if queue is already full and discarding is not allowed
        /// </summary>
        bool SetQueueOverflowIfFull();

        /// <summary>
        /// Adds an event to the queue.
        /// </summary>
        void QueueEvent(EventFieldList fields);

        /// <summary>
        /// Publish Events
        /// </summary>
        /// <param name="context">Context for the operation</param>
        /// <param name="notifications">Notifications to publish</param>
        /// <param name="maxNotificationsPerPublish">the maximum number of notifications to enqueue per call</param>
        /// <returns>the number of events that were added to the notification queue</returns>
        uint Publish(
            OperationContext context,
            Queue<EventFieldList> notifications,
            uint maxNotificationsPerPublish);
    }

    /// <summary>
    /// Mangages an event queue for usage by a MonitoredItem
    /// </summary>
    public class EventQueueHandler : IEventQueueHandler
    {
        /// <summary>
        /// Creates a new Queue handler
        /// </summary>
        /// <param name="createDurable">create a durable queue</param>
        /// <param name="queueFactory">the factory for creating the factory for <see cref="IEventMonitoredItemQueue"/></param>
        /// <param name="monitoredItemId">the id of the monitoredItem associated with the queue</param>
        /// <param name="telemetry">The telemetry context to use to create obvservability instruments</param>
        public EventQueueHandler(
            bool createDurable,
            IMonitoredItemQueueFactory queueFactory,
            uint monitoredItemId,
            ITelemetryContext telemetry)
            : this(createDurable, queueFactory, monitoredItemId, telemetry, null, null)
        {
        }

        /// <summary>
        /// Creates a new Queue handler
        /// </summary>
        /// <param name="createDurable">create a durable queue</param>
        /// <param name="queueFactory">the factory for creating the factory for <see cref="IEventMonitoredItemQueue"/></param>
        /// <param name="monitoredItemId">the id of the monitoredItem associated with the queue</param>
        /// <param name="telemetry">The telemetry context to use to create obvservability instruments</param>
        /// <param name="discardedEventHandler">Invoked once for every event discarded because the queue is full.</param>
        /// <param name="overflowEventHandler">
        /// Invoked every time the loss of events requires a new EventQueueOverflowEventType event
        /// (Part 5 12.15 EventQueueOverflowCount).
        /// </param>
        public EventQueueHandler(
            bool createDurable,
            IMonitoredItemQueueFactory queueFactory,
            uint monitoredItemId,
            ITelemetryContext telemetry,
            Action? discardedEventHandler,
            Action? overflowEventHandler)
        {
            m_logger = telemetry.CreateLogger<EventQueueHandler>();
            m_eventQueue = queueFactory.CreateEventQueue(createDurable, monitoredItemId);
            m_discardOldest = false;
            m_discardedEventHandler = discardedEventHandler;
            m_overflowEventHandler = overflowEventHandler;
        }

        /// <summary>
        /// Create an EventQueueHandler from an existing queue
        /// Used for restore after a server restart
        /// </summary>
        public EventQueueHandler(
            IEventMonitoredItemQueue eventQueue,
            bool discardOldest,
            ITelemetryContext telemetry)
            : this(eventQueue, discardOldest, telemetry, null, null)
        {
        }

        /// <summary>
        /// Create an EventQueueHandler from an existing queue
        /// Used for restore after a server restart
        /// </summary>
        /// <param name="eventQueue">The queue to take over.</param>
        /// <param name="discardOldest">Whether to discard the oldest events if the queue overflows.</param>
        /// <param name="telemetry">The telemetry context to use to create obvservability instruments</param>
        /// <param name="discardedEventHandler">Invoked once for every event discarded because the queue is full.</param>
        /// <param name="overflowEventHandler">
        /// Invoked every time the loss of events requires a new EventQueueOverflowEventType event
        /// (Part 5 12.15 EventQueueOverflowCount).
        /// </param>
        public EventQueueHandler(
            IEventMonitoredItemQueue eventQueue,
            bool discardOldest,
            ITelemetryContext telemetry,
            Action? discardedEventHandler,
            Action? overflowEventHandler)
        {
            m_logger = telemetry.CreateLogger<EventQueueHandler>();
            m_eventQueue = eventQueue;
            m_discardOldest = discardOldest;
            m_discardedEventHandler = discardedEventHandler;
            m_overflowEventHandler = overflowEventHandler;
        }

        /// <summary>
        /// Sets the queue size.
        /// </summary>
        /// <param name="queueSize">The new queue size.</param>
        /// <param name="discardOldest">Whether to discard the oldest values if the queue overflows.</param>
        public void SetQueueSize(uint queueSize, bool discardOldest)
        {
            // move a pending overflow event to where the new discard policy places it.
            if (discardOldest && !m_discardOldest && m_overflowPositions.Count > 0)
            {
                m_overflowPositions.Clear();
                m_overflowAtStart = true;
            }
            else if (!discardOldest && m_discardOldest && m_overflowAtStart)
            {
                m_overflowAtStart = false;
                m_overflowPositions.Add(0);
            }

            m_discardOldest = discardOldest;

            long discarded = m_eventQueue.ItemsInQueue - (long)queueSize;
            m_eventQueue.SetQueueSize(queueSize, discardOldest);
            if (discarded > 0)
            {
                RemovePendingTransforms(discarded, discardOldest);
            }

            // shrinking the queue loses events, which is signalled like any other overflow
            // (Part 4 5.13.1.5).
            if (discarded > 0)
            {
                if (!discardOldest)
                {
                    // the newest events were removed, so were any overflow events behind them.
                    m_overflowPositions.RemoveAll(position => position > m_eventQueue.ItemsInQueue);
                }
                ReportDiscardedEvents(discarded);
            }
        }

        /// <summary>
        /// The number of Items in the queue
        /// </summary>
        public int ItemsInQueue => m_eventQueue.ItemsInQueue;

        /// <summary>
        /// True if the queue is overflowing, i.e. an EventQueueOverflowEventType event is pending.
        /// </summary>
        public bool Overflow => m_overflowAtStart || m_overflowPositions.Count > 0;

        /// <summary>
        /// Checks the last 1k queue entries if the event is already in there
        /// </summary>
        public bool IsEventContainedInQueue(IFilterTarget instance)
        {
            return m_eventQueue.IsEventContainedInQueue(instance);
        }

        /// <summary>
        /// true if queue is already full and discarding is not allowed
        /// </summary>
        public bool SetQueueOverflowIfFull()
        {
            if (m_eventQueue.ItemsInQueue >= m_eventQueue.QueueSize && !m_discardOldest)
            {
                // the incoming event is discarded.
                ReportDiscardedEvents(1);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Records that events were discarded and, for the first discarded event since the
        /// last overflow event, places a new EventQueueOverflowEventType event: at the beginning
        /// of the queue for discardOldest TRUE, else at the end of the queue as it is now
        /// (Part 4 5.13.1.5). Events queued later are published after it.
        /// </summary>
        /// <param name="count">The number of discarded events.</param>
        private void ReportDiscardedEvents(long count)
        {
            for (long ii = 0; ii < count; ii++)
            {
                m_discardedEventHandler?.Invoke();
            }

            if (m_discardOldest)
            {
                if (!m_overflowAtStart)
                {
                    m_overflowAtStart = true;
                    m_overflowEventHandler?.Invoke();
                }
                return;
            }

            int position = m_eventQueue.ItemsInQueue;
            if (m_overflowPositions.Count == 0 ||
                m_overflowPositions[m_overflowPositions.Count - 1] != position)
            {
                m_overflowPositions.Add(position);
                m_overflowEventHandler?.Invoke();
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                m_eventQueue?.Dispose();
            }
        }

        /// <summary>
        /// Adds an event to the queue.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        public virtual void QueueEvent(EventFieldList fields)
        {
            // make space in the queue.
            if (m_eventQueue.ItemsInQueue >= m_eventQueue.QueueSize)
            {
                if (!m_discardOldest)
                {
                    // the incoming event is lost.
                    ReportDiscardedEvents(1);
                    throw new InvalidOperationException(
                        "Queue is full and no discarding of old values is allowed");
                }
                if (m_eventQueue.Dequeue(out _))
                {
                    RemovePendingTransforms(1, true);
                }
                ReportDiscardedEvents(1);
            }
            // queue the event.
            m_eventQueue.Enqueue(fields);
        }

        /// <summary>
        /// Publish Events
        /// </summary>
        /// <param name="context">System context</param>
        /// <param name="notifications">Notifications</param>
        /// <param name="maxNotificationsPerPublish">the maximum number of notifications to enqueue per call</param>
        public uint Publish(
            OperationContext context,
            Queue<EventFieldList> notifications,
            uint maxNotificationsPerPublish)
        {
            return Publish(context, notifications, maxNotificationsPerPublish, out _);
        }

        /// <summary>
        /// Publish Events
        /// </summary>
        /// <param name="context">System context</param>
        /// <param name="notifications">Notifications</param>
        /// <param name="maxNotificationsPerPublish">the maximum number of notifications to enqueue per call</param>
        /// <param name="overflowEventDue">
        /// True when the caller appends an overflow event to the published events now: with
        /// discardOldest FALSE once every event queued before the loss was published, with
        /// discardOldest TRUE for an event lost while publishing (it precedes the events still
        /// queued).
        /// </param>
        internal uint Publish(
            OperationContext context,
            Queue<EventFieldList> notifications,
            uint maxNotificationsPerPublish,
            out bool overflowEventDue)
        {
            overflowEventDue = false;

            // with discardOldest FALSE a pending overflow event keeps the position it was
            // placed at, so only the events queued before it are published ahead of it.
            long eventsBeforeOverflow = !m_discardOldest && m_overflowPositions.Count > 0
                ? m_overflowPositions[0]
                : long.MaxValue;

            uint notificationCount = 0;
            int consumed = 0;
            bool eventsDropped = false;
            while (notificationCount < maxNotificationsPerPublish &&
                consumed < eventsBeforeOverflow &&
                m_eventQueue.Dequeue(out EventFieldList fields))
            {
                int position = consumed++;
                EventFieldList? transformed = ApplyPendingTransform(fields);
                if (transformed == null)
                {
                    // the event cannot be expressed with the current select clauses: it is
                    // lost, which the client learns from an overflow event at its position.
                    m_discardedEventHandler?.Invoke();
                    eventsDropped = true;
                    if (!m_discardOldest)
                    {
                        if (m_overflowPositions.Count == 0 || m_overflowPositions[0] != position)
                        {
                            m_overflowPositions.Insert(0, position);
                            m_overflowEventHandler?.Invoke();
                        }

                        // the overflow event follows the events published before the loss.
                        eventsBeforeOverflow = consumed;
                    }
                    continue;
                }

                fields = transformed;
                foreach (Variant field in fields.EventFields)
                {
                    StatusResult tmpStatus;
                    bool gotStatus = field.TryGetStructure(out tmpStatus!);
                    StatusResult? statusResult = gotStatus ? tmpStatus : null;
                    if (statusResult is { } status)
                    {
                        status.ApplyDiagnosticMasks(
                            context.DiagnosticsMask,
                            context.StringTable,
                            m_logger);
                    }
                }

                notifications.Enqueue(fields);
                notificationCount++;
            }
            if (m_discardOldest)
            {
                // the caller placed the overflow event in front of the published events. An
                // event lost while publishing is reported in front of the events still queued:
                // appended now when it fits, else at the start of the next publish.
                m_overflowAtStart = false;
                if (eventsDropped)
                {
                    m_overflowEventHandler?.Invoke();
                    if (notificationCount < maxNotificationsPerPublish)
                    {
                        overflowEventDue = true;
                    }
                    else
                    {
                        m_overflowAtStart = true;
                    }
                }
            }
            else if (m_overflowPositions.Count > 0)
            {
                for (int ii = 0; ii < m_overflowPositions.Count; ii++)
                {
                    m_overflowPositions[ii] = Math.Max(
                        0,
                        m_overflowPositions[ii] - consumed);
                }

                // losses that end up at the same position are reported by one overflow event.
                for (int ii = m_overflowPositions.Count - 1; ii > 0; ii--)
                {
                    if (m_overflowPositions[ii] == m_overflowPositions[ii - 1])
                    {
                        m_overflowPositions.RemoveAt(ii);
                    }
                }

                // the caller appends the overflow event when it still fits into the publish
                // and the events queued before it have been published. A queue that
                // transiently returns no event (a durable queue restoring a batch) must not
                // move the overflow event ahead of the events that preceded the loss.
                if (notificationCount < maxNotificationsPerPublish &&
                    (m_overflowPositions[0] == 0 || m_eventQueue.ItemsInQueue == 0))
                {
                    m_overflowPositions.RemoveAt(0);
                    overflowEventDue = true;
                }
            }

            return notificationCount;
        }

        /// <summary>
        /// Brings every event queued now in line with a modification: <paramref name="transform"/>
        /// is applied to each of them when it is published. An event the transform cannot
        /// express any more (it returns <c>null</c>) is dropped at that point and reported
        /// with an overflow event at its position.
        /// </summary>
        /// <remarks>
        /// The queue is not drained: a durable queue may not hand back every queued event at
        /// once, and events taken out of a FIFO queue cannot be put back in front of the
        /// ones that stay, so a drain could reorder events (Part 4 5.13.1.5). Transforms of
        /// successive modifications are composed per segment of queued events.
        /// </remarks>
        /// <param name="transform">Produces the replacement for a queued event.</param>
        internal void TransformQueuedEvents(Func<EventFieldList, EventFieldList?> transform)
        {
            int covered = 0;
            foreach (PendingTransform pending in m_pendingTransforms)
            {
                Func<EventFieldList, EventFieldList?> previous = pending.Transform;
                pending.Transform = fields =>
                {
                    EventFieldList? intermediate = previous(fields);
                    return intermediate == null ? null : transform(intermediate);
                };
                covered += pending.Count;
            }

            int queued = m_eventQueue.ItemsInQueue;
            if (queued > covered)
            {
                m_pendingTransforms.Add(new PendingTransform(queued - covered, transform));
            }
        }

        /// <summary>
        /// Applies the pending transform of the oldest queued events to an event that was
        /// just dequeued for publishing.
        /// </summary>
        /// <returns>The event to publish, or <c>null</c> when it is dropped.</returns>
        private EventFieldList? ApplyPendingTransform(EventFieldList fields)
        {
            if (m_pendingTransforms.Count == 0)
            {
                return fields;
            }

            PendingTransform pending = m_pendingTransforms[0];
            if (--pending.Count == 0)
            {
                m_pendingTransforms.RemoveAt(0);
            }

            try
            {
                return pending.Transform(fields);
            }
            catch (Exception ex)
            {
                m_logger.QueuedEventTransformFailed(ex);
                return null;
            }
        }

        /// <summary>
        /// Accounts for queued events that left the queue without being published.
        /// </summary>
        /// <param name="count">The number of events removed from the queue.</param>
        /// <param name="oldest">True when the oldest events were removed, else the newest.</param>
        private void RemovePendingTransforms(long count, bool oldest)
        {
            if (oldest)
            {
                while (count > 0 && m_pendingTransforms.Count > 0)
                {
                    PendingTransform pending = m_pendingTransforms[0];
                    int removed = (int)Math.Min(count, pending.Count);
                    pending.Count -= removed;
                    count -= removed;
                    if (pending.Count == 0)
                    {
                        m_pendingTransforms.RemoveAt(0);
                    }
                }
                return;
            }

            // the newest events were removed: only the transforms of events still queued stay.
            int remaining = m_eventQueue.ItemsInQueue;
            for (int ii = 0; ii < m_pendingTransforms.Count; ii++)
            {
                PendingTransform pending = m_pendingTransforms[ii];
                if (remaining <= 0)
                {
                    m_pendingTransforms.RemoveRange(ii, m_pendingTransforms.Count - ii);
                    break;
                }
                pending.Count = Math.Min(pending.Count, remaining);
                remaining -= pending.Count;
            }
        }

        /// <summary>
        /// A transform that applies to a number of the oldest queued events.
        /// </summary>
        private sealed class PendingTransform
        {
            public PendingTransform(int count, Func<EventFieldList, EventFieldList?> transform)
            {
                Count = count;
                Transform = transform;
            }

            public int Count { get; set; }

            public Func<EventFieldList, EventFieldList?> Transform { get; set; }
        }

        private readonly List<PendingTransform> m_pendingTransforms = [];
        private bool m_discardOldest;
        private bool m_overflowAtStart;
        private readonly List<int> m_overflowPositions = [];
        private readonly Action? m_discardedEventHandler;
        private readonly Action? m_overflowEventHandler;
        private readonly IEventMonitoredItemQueue m_eventQueue;
        private readonly ILogger m_logger;
    }

    /// <summary>
    /// Source-generated log messages for EventQueueHandler.
    /// </summary>
    internal static partial class EventQueueHandlerLog
    {
        /// <summary>
        /// Logs a queued event that could not be brought in line with a modified filter.
        /// </summary>
        [LoggerMessage(EventId = ServerEventIds.EventQueueHandler + 0, Level = LogLevel.Warning,
            Message = "A queued event could not be brought in line with the modified event filter and is dropped.")]
        public static partial void QueuedEventTransformFailed(this ILogger logger, Exception exception);
    }
}
