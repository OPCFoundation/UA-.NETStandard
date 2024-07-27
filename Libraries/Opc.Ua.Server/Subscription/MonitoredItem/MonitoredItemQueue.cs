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
    /// A factory for <see cref="IDataChangeMonitoredItemQueue"> and </see> <see cref="IEventMonitoredItemQueue"/>
    /// </summary>
    public class MonitoredItemQueueFactory : IMonitoredItemQueueFactory
    {
        /// <inheritdoc/>
        public bool SupportsDurableQueues => false;
        /// <inheritdoc/>
        public IDataChangeMonitoredItemQueue CreateDataValueQueue(bool createDurable)
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
            m_isDurable = createDurable;
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
                if (m_values == null)
                {
                    return 0;
                }

                if (m_start < m_end)
                {
                    return m_end - m_start - 1;
                }

                return m_values.Length - m_start + m_end - 1;
            }
        }
        /// <inheritdoc/>
        public bool IsDurable => m_isDurable;


        /// <summary>
        /// Adds the value to the queue.
        /// </summary>
        /// <param name="value">The value to queue.</param>
        /// <param name="error">The error to queue.</param>
        public void Enqueue(DataValue value, ServiceResult error)
        {
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
        public void SetQueueSize(uint queueSize, bool queueErrors)
        {
            int length = (int)queueSize;

            if (length < 1)
            {
                length = 1;
            }

            int start = m_start;
            int end = m_end;

            // create new queue.
            DataValue[] values = new DataValue[length];
            ServiceResult[] errors = null;

            if (queueErrors)
            {
                errors = new ServiceResult[length];
            }

            // copy existing values.
            List<DataValue> existingValues = null;
            List<ServiceResult> existingErrors = null;

            if (m_start >= 0)
            {
                existingValues = new List<DataValue>();
                existingErrors = new List<ServiceResult>();

                DataValue value = null;
                ServiceResult error = null;

                while (Dequeue(out value, out error))
                {
                    existingValues.Add(value);
                    existingErrors.Add(error);
                }
            }

            // update internals.
            m_values = values;
            m_errors = errors;
            m_start = -1;
            m_end = 0;

            // requeue the data.
            if (existingValues != null)
            {
                for (int ii = 0; ii < existingValues.Count; ii++)
                {
                    Enqueue(existingValues[ii], existingErrors[ii]);
                }
            }
        }

        /// <inheritdoc/>
        public DataValue PeekLastValue()
        {
            if (m_start < 0)
            {
                return null;
            }

            return m_values[m_start];
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
        private readonly bool m_isDurable;
        private DataValue[] m_values;
        private ServiceResult[] m_errors;
        private int m_start;
        private int m_end;
        #endregion
    }

    /// <summary>
    /// Provides a queue for events
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
            m_isDurable = createDurable;
        }

        #region Public Methods
        /// <inheritdoc/>
        public bool IsDurable => m_isDurable;

        /// <inheritdoc/>
        public uint QueueSize => throw new NotImplementedException();

        /// <inheritdoc/>
        public int ItemsInQueue => throw new NotImplementedException();

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
            m_events.Add(value);
        }

        /// <inheritdoc/>
        public bool IsEventContainedInQueue(IFilterTarget instance)
        {
            // check for duplicate instances being reported via multiple paths.
            for (int ii = 0; ii < m_events.Count; ii++)
            {
                if (m_events[ii] is EventFieldList processedEvent)
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
            m_queueSize = queueSize;

            if (m_events.Count > m_queueSize)
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
        private uint m_queueSize;
        private readonly List<EventFieldList> m_events;
        private readonly bool m_isDurable;
        #endregion
    }
}
