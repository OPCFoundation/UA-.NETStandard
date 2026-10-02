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
                m_eventQueue.Dequeue(out _);
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
        /// With discardOldest FALSE: true when every event queued before the pending overflow
        /// event was published and the caller appends the overflow event now.
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
            while (notificationCount < maxNotificationsPerPublish &&
                notificationCount < eventsBeforeOverflow &&
                m_eventQueue.Dequeue(out EventFieldList fields))
            {
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
                // the caller placed the overflow event in front of the published events.
                m_overflowAtStart = false;
            }
            else if (m_overflowPositions.Count > 0)
            {
                for (int ii = 0; ii < m_overflowPositions.Count; ii++)
                {
                    m_overflowPositions[ii] = Math.Max(
                        0,
                        m_overflowPositions[ii] - (int)notificationCount);
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
        /// Replaces every queued event with the result of <paramref name="rebuild"/> and keeps
        /// the queue order. An event the callback cannot rebuild (it returns <c>null</c>) is
        /// dropped and reported through <see cref="Overflow"/>.
        /// </summary>
        /// <param name="rebuild">Produces the replacement for a queued event.</param>
        internal void RebuildQueuedEvents(Func<EventFieldList, EventFieldList?> rebuild)
        {
            int count = m_eventQueue.ItemsInQueue;
            var rebuilt = new List<EventFieldList>(count);
            var dropPositions = new List<int>();
            int[] droppedBefore = new int[count + 1];
            for (int ii = 0; ii < count; ii++)
            {
                droppedBefore[ii + 1] = droppedBefore[ii];
                if (!DequeueWithRetry(out EventFieldList fields))
                {
                    // the queue no longer hands back the events it reports as queued.
                    m_logger.RebuildDequeueFailed(ii, count);
                    for (int jj = ii + 2; jj <= count; jj++)
                    {
                        droppedBefore[jj] = droppedBefore[ii];
                    }
                    break;
                }
                EventFieldList? replacement = rebuild(fields);
                if (replacement == null)
                {
                    droppedBefore[ii + 1]++;
                    if (dropPositions.Count == 0 || dropPositions[dropPositions.Count - 1] != rebuilt.Count)
                    {
                        dropPositions.Add(rebuilt.Count);
                    }
                    continue;
                }
                rebuilt.Add(replacement);
            }

            foreach (EventFieldList fields in rebuilt)
            {
                m_eventQueue.Enqueue(fields);
            }

            if (dropPositions.Count == 0)
            {
                return;
            }

            // pending overflow markers keep their place relative to the remaining events.
            for (int ii = 0; ii < m_overflowPositions.Count; ii++)
            {
                int position = Math.Min(m_overflowPositions[ii], count);
                m_overflowPositions[ii] = position - droppedBefore[position];
            }

            for (int ii = 0; ii < droppedBefore[count]; ii++)
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

            foreach (int position in dropPositions)
            {
                if (!m_overflowPositions.Contains(position))
                {
                    m_overflowPositions.Add(position);
                    m_overflowEventHandler?.Invoke();
                }
            }
            m_overflowPositions.Sort();
        }

        /// <summary>
        /// Dequeues an event, tolerating a durable queue that transiently reports no event
        /// while it restores a persisted batch. The retry is bounded, because a queue that
        /// permanently stops handing back the events it reports as queued would otherwise
        /// spin a server thread forever.
        /// </summary>
        /// <param name="fields">The event that was dequeued.</param>
        private bool DequeueWithRetry(out EventFieldList fields)
        {
            var spinWait = new SpinWait();
            for (int attempt = 0; attempt <= kMaxDequeueAttempts; attempt++)
            {
                if (m_eventQueue.Dequeue(out fields))
                {
                    return true;
                }

                if (m_eventQueue.ItemsInQueue == 0)
                {
                    return false;
                }

                spinWait.SpinOnce();
            }

            fields = null!;
            return false;
        }

        /// <summary>
        /// The number of attempts to dequeue an event the queue reports as queued before the
        /// queue is treated as broken.
        /// </summary>
        private const int kMaxDequeueAttempts = 10000;

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
        /// Logs an event queue that stopped handing back its queued events during a rebuild.
        /// </summary>
        [LoggerMessage(EventId = ServerEventIds.EventQueueHandler + 0, Level = LogLevel.Warning,
            Message = "Event queue returned {Dequeued} of {Queued} queued events for a rebuild.")]
        public static partial void RebuildDequeueFailed(this ILogger logger, int dequeued, int queued);
    }
}
