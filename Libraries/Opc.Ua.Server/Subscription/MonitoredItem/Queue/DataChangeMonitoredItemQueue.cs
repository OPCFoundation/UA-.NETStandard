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

namespace Opc.Ua.Server
{
    /// <summary>
    /// Provides an optionally durable queue for data changes used by <see cref="DataChangeQueueHandler"/> and created by <see cref="IMonitoredItemQueueFactory"/>.
    /// </summary>
    public interface IDataChangeMonitoredItemQueue : IDisposable
    {
        /// <summary>
        /// The Id of the MonitoredItem associated with the queue
        /// </summary>
        uint MonitoredItemId { get; }

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
        /// Resets thew queue, sets the new queue size initializes an empty queue (caller handles existing entries).
        /// </summary>
        /// <param name="queueSize">The new queue size.</param>
        /// <param name="queueErrors">Specifies wether errors should be queued.</param>
        void ResetQueue(uint queueSize, bool queueErrors);

        /// <summary>
        /// Adds the value to the queue.
        /// </summary>
        /// <param name="value">The value to queue.</param>
        /// <param name="error">The error to queue.</param>
        void Enqueue(DataValue value, ServiceResult error);

        /// <summary>
        /// Dequeue the oldest value in the queue.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <param name="error">The error associated with the value.</param>
        /// <returns>True if a value was found. False if the queue is empty.</returns>
        bool Dequeue(out DataValue value, out ServiceResult error);

        /// <summary>
        /// returns the the oldest value in the queue without dequeueing. Null if queue is empty
        /// </summary>
        DataValue PeekOldestValue();

        /// <summary>
        /// Replace the last (newest) value in the queue with the provided Value. Used when values are provided faster than the sampling interval
        /// </summary>
        /// <param name="value">The value to queue.</param>
        /// <param name="error">The error to queue.</param>
        void OverwriteLastValue(DataValue value, ServiceResult error);

        /// <summary>
        /// Returns the last (newest) value in the queue without dequeuing
        /// </summary>
        /// <returns>the last value, null if queue is empty</returns>
        DataValue PeekLastValue();
    }
    /// <summary>
    /// Provides a queue for data changes.
    /// </summary>
    public class DataChangeMonitoredItemQueue : IDataChangeMonitoredItemQueue
    {
        /// <summary>
        /// Creates an empty queue.
        /// </summary>
        public DataChangeMonitoredItemQueue(bool createDurable, uint monitoredItemId)
        {
            if (createDurable)
            {
                Utils.LogError("DataChangeMonitoredItemQueue does not support durable queues, please provide full implementation of IDurableMonitoredItemQueue using Server.CreateDurableMonitoredItemQueueFactory to supply own factory");
                throw new ArgumentException("DataChangeMonitoredItemQueue does not support durable Queues", nameof(createDurable));
            }
            m_monitoredItemId = monitoredItemId;
            m_values = null;
            m_errors = null;
            m_start = -1;
            m_end = -1;
        }

        #region Public Methods
        /// <inheritdoc/>
        public uint MonitoredItemId => m_monitoredItemId;

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
        public virtual bool IsDurable => false;


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
                Dequeue(out _, out _);
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
        public virtual void Dispose()
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

        /// <inheritdoc/>
        public DataValue PeekOldestValue()
        {
            // check for empty queue.
            if (m_start < 0)
            {
                return null;
            }

            return m_values[m_start];
        }

        #endregion

        #region Private Fields
        private readonly uint m_monitoredItemId;
        private DataValue[] m_values;
        private ServiceResult[] m_errors;
        private int m_start;
        private int m_end;
        #endregion
    }
}
