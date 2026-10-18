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
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Provides a queue for events.
    /// </summary>
    public class EventMonitoredItemQueue : IEventMonitoredItemQueue
    {
        private const uint kMaxNoOfEntriesCheckedForDuplicateEvents = 1000;

        /// <summary>
        /// The number of dequeued slots at the front of <see cref="m_events"/> from which the
        /// list is compacted once they are half of it.
        /// </summary>
        private const int kMinDequeuedSlotsToCompact = 32;

        /// <summary>
        /// Creates an empty queue.
        /// </summary>
        public EventMonitoredItemQueue(bool createDurable, uint monitoredItemId, ITelemetryContext telemetry)
        {
            m_logger = telemetry.CreateLogger<DataChangeMonitoredItemQueue>();
            if (createDurable)
            {
                m_logger.EventMonitoredItemQueueDoesNotSupportDurableQueues();
                throw new ArgumentException(
                    "DataChangeMonitoredItemQueue does not support durable Queues",
                    nameof(createDurable));
            }
            m_events = [];
            MonitoredItemId = monitoredItemId;
            QueueSize = 0;
        }

        /// <inheritdoc/>
        public uint MonitoredItemId { get; }

        /// <inheritdoc/>
        public virtual bool IsDurable => false;

        /// <inheritdoc/>
        public uint QueueSize { get; protected set; }

        /// <inheritdoc/>
        public int ItemsInQueue => m_events.Count - m_head;

        /// <inheritdoc/>
        public virtual bool Dequeue(out EventFieldList value)
        {
            value = null!;
            if (m_head < m_events.Count)
            {
                // advance the head instead of shifting the list: removing the first entry
                // moved every queued event for each dequeued one.
                value = m_events[m_head];
                m_events[m_head++] = null!;
                if (m_head == m_events.Count)
                {
                    m_events.Clear();
                    m_head = 0;
                }
                else if (m_head >= kMinDequeuedSlotsToCompact && m_head * 2 >= m_events.Count)
                {
                    // moves fewer events than were dequeued since the last compaction.
                    CompactEvents();
                }
                return true;
            }
            return false;
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
        }

        /// <inheritdoc/>
        public virtual void Enqueue(EventFieldList value)
        {
            if (QueueSize == 0)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInternalError,
                    "Error queueing Event. Queue size is set to 0");
            }

            //Discard oldest
            if (ItemsInQueue == QueueSize)
            {
                Dequeue(out EventFieldList _);
            }

            m_events.Add(value);
        }

        /// <inheritdoc/>
        public bool IsEventContainedInQueue(IFilterTarget instance)
        {
            // a duplicate is the same instance reported again through another notifier
            // path, so it sits among the most recently queued events: scan from the newest.
            int lowestIndex =
                ItemsInQueue > kMaxNoOfEntriesCheckedForDuplicateEvents
                    ? m_events.Count - (int)kMaxNoOfEntriesCheckedForDuplicateEvents
                    : m_head;

            for (int i = m_events.Count - 1; i >= lowestIndex; i--)
            {
                if (m_events[i] is EventFieldList processedEvent &&
                    IsEventRaisedFor(processedEvent, instance))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Returns true when the queued event was raised for <paramref name="instance"/>, for
        /// the duplicate check of <see cref="IEventMonitoredItemQueue.IsEventContainedInQueue"/>
        /// implementations. A trailing event of a condition that left the filter is queued
        /// with a wrapper of the instance as its handle, which is unwrapped here.
        /// </summary>
        /// <param name="queuedEvent">The queued event.</param>
        /// <param name="instance">The event instance being queued.</param>
        public static bool IsEventRaisedFor(EventFieldList queuedEvent, IFilterTarget instance)
        {
            if (queuedEvent == null)
            {
                return false;
            }

            object? handle = queuedEvent.Handle;
            return ReferenceEquals(instance, handle) ||
                (handle is FilteredRetainTarget wrapper && ReferenceEquals(instance, wrapper.Target));
        }

        /// <inheritdoc/>
        public virtual void SetQueueSize(uint queueSize, bool discardOldest)
        {
            QueueSize = queueSize;
            CompactEvents();

            if (m_events.Count > QueueSize)
            {
                if (discardOldest)
                {
                    m_events.RemoveRange(0, m_events.Count - (int)queueSize);
                }
                else
                {
                    m_events.RemoveRange((int)queueSize, m_events.Count - (int)queueSize);
                }
            }
        }

        /// <summary>
        /// Copies the queued events, ordered oldest to newest.
        /// </summary>
        internal EventFieldList[] CopyQueuedEvents()
        {
            return m_events.GetRange(m_head, ItemsInQueue).ToArray();
        }

        /// <summary>
        /// Removes the dequeued slots at the front of <see cref="m_events"/>.
        /// </summary>
        private void CompactEvents()
        {
            if (m_head > 0)
            {
                m_events.RemoveRange(0, m_head);
                m_head = 0;
            }
        }

        /// <summary>
        /// The events in the queue, oldest first. A dequeued event leaves a null slot at the
        /// front of the list until the list is compacted, so the queued events are the last
        /// <see cref="ItemsInQueue"/> entries.
        /// </summary>
        protected List<EventFieldList> m_events;

        /// <summary>
        /// The index of the oldest queued event in <see cref="m_events"/>.
        /// </summary>
        private int m_head;

        /// <summary>
        /// The logger to use here and in derived classes
        /// </summary>
        protected ILogger m_logger;
    }

    /// <summary>
    /// Source-generated log messages for EventMonitoredItemQueue.
    /// </summary>
    internal static partial class EventMonitoredItemQueueLog
    {
        [LoggerMessage(EventId = ServerEventIds.EventMonitoredItemQueue + 0, Level = LogLevel.Error,
            Message = "EventMonitoredItemQueue does not support durable queues, please provide full " +
                "implementation of IDurableMonitoredItemQueue using " +
                "Server.CreateDurableMonitoredItemQueueFactory to supply own factory")]
        public static partial void EventMonitoredItemQueueDoesNotSupportDurableQueues(this ILogger logger);
    }

}
