/* ========================================================================
 * Copyright (c) 2005-2024 The OPC Foundation, Inc. All rights reserved.
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
        /// <param name="instance"></param>
        /// <returns></returns>
        bool IsEventContainedInQueue(IFilterTarget instance);

        /// <summary>
        /// true if queue is already full and discarding is not allowed
        /// </summary>
        /// <returns></returns>
        bool SetQueueOverflowIfFull();

        /// <summary>
        /// Adds an event to the queue.
        /// </summary>
        void QueueEvent(EventFieldList fields);

        /// <summary>
        /// Publish Events
        /// </summary>
        /// <param name="context"></param>
        /// <param name="notifications"></param>
        /// <param name="maxNotificationsPerPublish">the maximum number of notifications to enqueue per call</param>
        /// <returns>the number of events that were added to the notification queue</returns>
        uint Publish(OperationContext context, Queue<EventFieldList> notifications, uint maxNotificationsPerPublish);
    }
    /// <summary>
    /// Mangages an event queue for usage by a MonitoredItem
    /// </summary>
    public class EventQueueHandler : IEventQueueHandler
    {
        /// <summary>
        /// Creates a new Queue
        /// </summary>
        /// <param name="createDurable">create a durable queue</param>
        /// <param name="queueFactory">the factory for creating the factory for <see cref="IEventMonitoredItemQueue"/></param>
        /// <param name="monitoredItemId">the id of the monitoredItem associated with the queue</param>
        public EventQueueHandler(bool createDurable, IMonitoredItemQueueFactory queueFactory, uint monitoredItemId)
        {
            m_eventQueue = queueFactory.CreateEventQueue(createDurable, monitoredItemId);
            m_discardOldest = false;
            m_overflow = false;
        }

        /// <summary>
        /// Create an EventQueueHandler from an existing queue
        /// Used for restore after a server restart
        /// </summary>
        public EventQueueHandler(
            IEventMonitoredItemQueue eventQueue,
            bool discardOldest)
        {
            m_eventQueue = eventQueue;
            m_discardOldest = discardOldest;
            m_overflow = false;
        }

        /// <summary>
        /// Sets the queue size.
        /// </summary>
        /// <param name="queueSize">The new queue size.</param>
        /// <param name="discardOldest">Whether to discard the oldest values if the queue overflows.</param>
        public void SetQueueSize(uint queueSize, bool discardOldest)
        {
            m_discardOldest = discardOldest;
            m_eventQueue.SetQueueSize(queueSize, discardOldest);
        }
        /// <summary>
        /// The number of Items in the queue
        /// </summary>
        public int ItemsInQueue => m_eventQueue.ItemsInQueue;

        /// <summary>
        /// True if the queue is overflowing
        /// </summary>
        public bool Overflow => m_overflow;

        /// <summary>
        /// Checks the last 1k queue entries if the event is already in there
        /// </summary>
        /// <param name="instance"></param>
        /// <returns></returns>
        public bool IsEventContainedInQueue(IFilterTarget instance)
        {
            return m_eventQueue.IsEventContainedInQueue(instance);
        }

        /// <summary>
        /// true if queue is already full and discarding is not allowed
        /// </summary>
        /// <returns></returns>
        public bool SetQueueOverflowIfFull()
        {
            if (m_eventQueue.ItemsInQueue >= m_eventQueue.QueueSize && !m_discardOldest)
            {
                m_overflow = true;
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
            if (disposing)
            {
                Utils.SilentDispose(m_eventQueue);
            }
        }

        /// <summary>
        /// Adds an event to the queue.
        /// </summary>
        public virtual void QueueEvent(EventFieldList fields)
        {
            // make space in the queue.
            if (m_eventQueue.ItemsInQueue >= m_eventQueue.QueueSize)
            {
                m_overflow = true;
                if (!m_discardOldest)
                {
                    throw new InvalidOperationException("Queue is full and no discarding of old values is allowed");
                }
                m_eventQueue.Dequeue(out _);
            }
            // queue the event.
            m_eventQueue.Enqueue(fields);
        }

        /// <summary>
        /// Publish Events
        /// </summary>
        /// <param name="context"></param>
        /// <param name="notifications"></param>
        /// <param name="maxNotificationsPerPublish">the maximum number of notifications to enqueue per call</param>
        public uint Publish(OperationContext context, Queue<EventFieldList> notifications, uint maxNotificationsPerPublish)
        {
            uint notificationCount = 0;
            while (notificationCount < maxNotificationsPerPublish && m_eventQueue.Dequeue(out EventFieldList fields))
            {
                foreach (Variant field in fields.EventFields)
                {
                    if (field.Value is StatusResult statusResult)
                    {
                        statusResult.ApplyDiagnosticMasks(context.DiagnosticsMask, context.StringTable);
                    }
                }

                notifications.Enqueue(fields);
                notificationCount++;
            }
            //if overflow event is placed at the end of the queue only set overflow to false if the overflow event still fits into the publish
            m_overflow = m_overflow && notificationCount == maxNotificationsPerPublish && !m_discardOldest;

            return notificationCount;
        }

        private bool m_overflow;
        private bool m_discardOldest;
        private readonly IEventMonitoredItemQueue m_eventQueue;
    }
}
