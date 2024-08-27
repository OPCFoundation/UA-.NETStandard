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
using System.Linq;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Provides an optionally durable queue for events used by <see cref="EventQueueHandler"/> and created by <see cref="IMonitoredItemQueueFactory"/>.
    /// </summary>
    public interface IEventMonitoredItemQueue : IDisposable
    {
        /// <summary>
        /// True if the queue is in durable mode and persists the queue values / supports a large queue size
        /// </summary>
        bool IsDurable { get; }

        /// <summary>
        /// Gets the current queue size.
        /// </summary>
        uint QueueSize { get; }

        /// <summary>
        /// Gets number of elements actually contained in value queue.
        /// </summary>
        int ItemsInQueue { get; }

        /// <summary>
        /// Sets the queue size. If the queue contained entries before strip the existing
        /// </summary>
        /// <param name="queueSize">The new queue size.</param>
        /// <param name="discardOldest">if true remove oldest entries from the queue when the queue size decreases, else remove newest</param>
        void SetQueueSize(uint queueSize, bool discardOldest);

        /// <summary>
        /// Checks the last 1k queue entries if the event is already in there
        /// used to detect duplicate instances of the same event being reported via multiple paths.
        /// </summary>
        /// <param name="instance">the event to chack for duplicates</param>
        /// <returns>true if event already in queue</returns>
        bool IsEventContainedInQueue(IFilterTarget instance);


        /// <summary>
        /// Adds the value to the queue.
        /// </summary>
        /// <param name="value">The value to queue.</param>
        void Enqueue(EventFieldList value);

        /// <summary>
        /// Dequeue the oldest value in the queue.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <returns>True if a value was found. False if the queue is empty.</returns>
        bool Dequeue(out EventFieldList value);
    }
    /// <summary>
    /// Provides a queue for events.
    /// </summary>
    public class EventMonitoredItemQueue : IEventMonitoredItemQueue
    {
        /// <summary>
        /// Creates an empty queue.
        /// </summary>
        public EventMonitoredItemQueue(bool createDurable)
        {
            if (createDurable)
            {
                Utils.LogError("EventMonitoredItemQueue does not support durable queues, please provide full implementation of IDurableMonitoredItemQueue using Server.CreateDurableMonitoredItemQueueFactory to supply own factory");
                throw new ArgumentException("DataChangeMonitoredItemQueue does not support durable Queues", nameof(createDurable));
            }
            m_events = new List<EventFieldList>();
            IsDurable = createDurable;
            QueueSize = 0;
        }

        #region Public Methods
        /// <inheritdoc/>
        public virtual bool IsDurable { get; }

        /// <inheritdoc/>
        public uint QueueSize { get; private set; }

        /// <inheritdoc/>
        public int ItemsInQueue => m_events.Count;

        /// <inheritdoc/>
        public bool Dequeue(out EventFieldList value)
        {
            value = null;
            if (m_events.Any())
            {
                value = m_events.First();
                m_events.RemoveAt(0);
                return true;
            }
            return false;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            //Only needed for unmanaged resources
        }

        /// <inheritdoc/>
        public void Enqueue(EventFieldList value)
        {
            if (m_events.Count == QueueSize && !Dequeue(out var _))
            {
                throw new ServiceResultException(StatusCodes.BadInternalError, "Error queueing Event. Evemt Queue was full but it was not possible to discard the oldest Event");
            }
            m_events.Add(value);
        }

        /// <inheritdoc/>
        public bool IsEventContainedInQueue(IFilterTarget instance)
        {
            int maxCount = m_events.Count > 1000 ? 1000 : m_events.Count;

            for (int i = 0; i < maxCount; i++)
            {
                if (m_events[i] is EventFieldList processedEvent)
                {
                    if (ReferenceEquals(instance, processedEvent.Handle))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <inheritdoc/>
        public void SetQueueSize(uint queueSize, bool discardOldest)
        {
            QueueSize = queueSize;

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
        #endregion

        #region Private Fields
        private readonly List<EventFieldList> m_events;
        #endregion
    }

}
