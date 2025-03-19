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
    /// Provides a queue for events.
    /// </summary>
    public class EventMonitoredItemQueue : IEventMonitoredItemQueue
    {
        private const UInt32 kMaxNoOfEntriesCheckedForDuplicateEvents = 1000;

        /// <summary>
        /// Creates an empty queue.
        /// </summary>
        public EventMonitoredItemQueue(bool createDurable, uint monitoredItemId)
        {
            if (createDurable)
            {
                Utils.LogError("EventMonitoredItemQueue does not support durable queues, please provide full implementation of IDurableMonitoredItemQueue using Server.CreateDurableMonitoredItemQueueFactory to supply own factory");
                throw new ArgumentException("DataChangeMonitoredItemQueue does not support durable Queues", nameof(createDurable));
            }
            m_events = new List<EventFieldList>();
            m_monitoredItemId = monitoredItemId;
            QueueSize = 0;
        }

        #region Public Methods
        /// <inheritdoc/>
        public uint MonitoredItemId => m_monitoredItemId;

        /// <inheritdoc/>
        public virtual bool IsDurable => false;

        /// <inheritdoc/>
        public uint QueueSize { get; protected set; }

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
        public virtual void Dispose()
        {
            //Only needed for unmanaged resources
        }

        /// <inheritdoc/>
        public void Enqueue(EventFieldList value)
        {
            if (QueueSize == 0)
            {
                throw new ServiceResultException(StatusCodes.BadInternalError, "Error queueing Event. Queue size is set to 0");
            }

            //Discard oldest
            if (m_events.Count == QueueSize)
            {
                Dequeue(out var _);
            }

            m_events.Add(value);
        }

        /// <inheritdoc/>
        public bool IsEventContainedInQueue(IFilterTarget instance)
        {
            int maxCount = m_events.Count > kMaxNoOfEntriesCheckedForDuplicateEvents ? (int)kMaxNoOfEntriesCheckedForDuplicateEvents : m_events.Count;

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
        /// <summary>
        /// the contained in the queue
        /// </summary>
        protected List<EventFieldList> m_events;
        private readonly uint m_monitoredItemId;
        #endregion
    }

}
