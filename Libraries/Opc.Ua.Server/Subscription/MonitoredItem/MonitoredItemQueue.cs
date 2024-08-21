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
using Newtonsoft.Json.Linq;

namespace Opc.Ua.Server
{
    /// <summary>
    /// A factory for <see cref="IDataChangeMonitoredItemQueue"> and </see> <see cref="IEventMonitoredItemQueue"/>
    /// </summary>
    public class MonitoredItemQueueFactory : IMonitoredItemQueueFactory
    {
        /// <inheritdoc/>
        public bool SupportsDurableQueues => false;
        /// <inheritdoc/>
        public IDataChangeMonitoredItemQueue CreateDataChangeQueue(bool createDurable)
        {
            return new DataChangeMonitoredItemQueue(createDurable);
        }

        /// <inheritdoc/>
        public IEventMonitoredItemQueue CreateEventQueue(bool createDurable)
        {
            return new EventMonitoredItemQueue(createDurable);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            //only needed for managed resources
        }
    }
    /// <summary>
    /// Provides a queue for data changes.
    /// </summary>
    public class DataChangeMonitoredItemQueue : IDataChangeMonitoredItemQueue
    {
        /// <summary>
        /// Creates an empty queue.
        /// </summary>
        public DataChangeMonitoredItemQueue(bool createDurable)
        {
            if (createDurable)
            {
                Utils.LogError("DataChangeMonitoredItemQueue does not support durable queues, please provide full implementation of IDurableMonitoredItemQueue using Server.CreateDurableMonitoredItemQueueFactory to supply own factory");
                throw new ServiceResultException(StatusCodes.BadInternalError);
            }

            m_values = null;
            m_errors = null;
            m_start = -1;
            m_end = -1;
            IsDurable = createDurable;
        }

        #region Public Methods

        /// <summary>
        /// Gets the current queue size.
        /// </summary>
        public uint QueueSize
        {
            get
            {
                if (m_values == null)
                {
                    return 0;
                }

                return (uint)m_values.Length;
            }
        }

        /// <summary>
        /// Gets number of elements actually contained in value queue.
        /// </summary>
        public int ItemsInQueue
        {
            get
            {
                if (m_values == null || m_start == -1)
                {
                    return 0;
                }

                if (m_start < m_end)
                {
                    return m_end - m_start;
                }

                return m_values.Length - m_start + m_end;
            }
        }
        /// <inheritdoc/>
        public virtual bool IsDurable { get; }


        /// <summary>
        /// Adds the value to the queue.
        /// </summary>
        /// <param name="value">The value to queue.</param>
        /// <param name="error">The error to queue.</param>
        public void Enqueue(DataValue value, ServiceResult error)
        {
            if (m_values == null || m_values.Length == 0)
            {
                throw new InvalidOperationException("Cannot enqueue Value. Queue size not set.");
            }

            //check for full queue
            if (ItemsInQueue == m_values.Length)
            {
                _ = Dequeue(out _, out _);
            }

            // check for empty queue.
            if (m_start < 0)
            {
                m_start = 0;
                m_end = 1;

                m_values[m_start] = value;

                if (m_errors != null)
                {
                    m_errors[m_start] = error;
                }

                return;
            }

            int next = m_end;

            // check for wrap around.
            if (next >= m_values.Length)
            {
                next = 0;
            }

            // add value.
            m_values[next] = value;

            if (m_errors != null)
            {
                m_errors[next] = error;
            }

            m_end = next + 1;
        }
        /// <inheritdoc/>
        public void Dispose()
        {
            //only needed for unmanaged resources
        }

        /// <inheritdoc/>
        public void OverwriteLastValue(DataValue value, ServiceResult error)
        {
            if (ItemsInQueue == 0)
            {
                throw new InvalidOperationException("Cannot overwrite Value. Queue is empty.");
            }

            int last = m_end - 1;

            if (last < 0)
            {
                last = m_values.Length - 1;
            }

            // replace last value and error.
            m_values[last] = value;

            if (m_errors != null)
            {
                m_errors[last] = error;
            }
        }

        /// <inheritdoc/>
        public void ResetQueue(uint queueSize, bool queueErrors)
        {
            int length = (int)queueSize;

            // create new queue.
            DataValue[] values = new DataValue[length];
            ServiceResult[] errors = null;

            if (queueErrors)
            {
                errors = new ServiceResult[length];
            }
            // update internals.
            m_values = values;
            m_errors = errors;
            m_start = -1;
            m_end = 0;
        }

        /// <inheritdoc/>
        public DataValue PeekLastValue()
        {
            if (m_start < 0)
            {
                return null;
            }

            int last = m_end - 1;

            if (last < 0)
            {
                last = m_values.Length - 1;
            }

            return m_values[last];
        }

        /// <inheritdoc/>
        public bool Dequeue(out DataValue value, out ServiceResult error)
        {
            value = null;
            error = null;

            // check for empty queue.
            if (m_start < 0)
            {
                return false;
            }

            value = m_values[m_start];
            m_values[m_start] = null;

            if (m_errors != null)
            {
                error = m_errors[m_start];
                m_errors[m_start] = null;
            }

            m_start++;

            // check if queue has been emptied.
            if (m_start == m_end)
            {
                m_start = -1;
                m_end = 0;
            }

            // check for wrap around.
            else if (m_start >= m_values.Length)
            {
                m_start = 0;
            }

            return true;
        }

        #endregion

        #region Private Fields
        private DataValue[] m_values;
        private ServiceResult[] m_errors;
        private int m_start;
        private int m_end;
        #endregion
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
                throw new ServiceResultException(StatusCodes.BadInternalError);
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
