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
            MonitoredItemId = monitoredItemId;
            m_values = null;
            m_errors = null;
            m_start = -1;
            m_end = -1;
        }

        #region Public Methods
        /// <inheritdoc/>
        public uint MonitoredItemId { get; }

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
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Overridable method to dispose of resources.
        /// </summary>
        protected virtual void Dispose(bool disposing)
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
            var values = new DataValue[length];
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
        /// <summary>
        /// the stored data values
        /// </summary>
        protected DataValue[] m_values;
        /// <summary>
        /// the stored errors
        /// </summary>
        protected ServiceResult[] m_errors;
        /// <summary>
        /// the start of the buffer
        /// </summary>
        protected int m_start;
        /// <summary>
        /// the end of the buffer
        /// </summary>
        protected int m_end;
        #endregion
    }
}
