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
    public class DurableMonitoredItemQueueFactory : IMonitoredItemQueueFactory
    {
        /// <inheritdoc/>
        public bool SupportsDurableQueues => true;
        /// <inheritdoc/>
        public IDataChangeMonitoredItemQueue CreateDataChangeQueue(bool createDurable)
        {
            //return new DurableDataChangeMonitoredItemQueue(createDurable);
            return new DurableDataChangeMonitoredItemQueueWithArray(createDurable);
        }

        /// <inheritdoc/>
        public IEventMonitoredItemQueue CreateEventQueue(bool createDurable)
        {
            return new DurableEventMonitoredItemQueue(createDurable);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            //only needed for managed resources
        }
    }

    public class DurableEventMonitoredItemQueue : EventMonitoredItemQueue
    {
        /// <summary>
        /// Creates an empty queue.
        /// </summary>
        public DurableEventMonitoredItemQueue(bool createDurable) : base(false)
        {
            IsDurable = createDurable;
        }

        #region Public Methods
        /// <inheritdoc/>
        public override bool IsDurable { get; }
        #endregion
    }

    public class DurableDataChangeMonitoredItemQueueWithArray : DataChangeMonitoredItemQueue
    {
        /// <summary>
        /// Creates an empty queue.
        /// </summary>
        public DurableDataChangeMonitoredItemQueueWithArray(bool createDurable) : base(false)
        {
            IsDurable = createDurable;
        }

        #region Public Methods
        /// <inheritdoc/>
        public override bool IsDurable { get; }
        #endregion
    }

    /// <summary>
    /// Provides a queue for data changes.
    /// </summary>
    public class DurableDataChangeMonitoredItemQueue : IDataChangeMonitoredItemQueue
    {
        /// <summary>
        /// Creates an empty queue.
        /// </summary>
        public DurableDataChangeMonitoredItemQueue(bool createDurable)
        {
            m_values = new List<DataValue>();
            m_errors = new List<ServiceResult>();
            m_queueSize = 0;
            m_isDurable = createDurable;
            m_queueErrors = false;
        }

        #region Public Methods

        /// <summary>
        /// Gets the current queue size.
        /// </summary>
        public uint QueueSize
        {
            get
            {
                return m_queueSize;
            }
        }

        /// <summary>
        /// Gets number of elements actually contained in value queue.
        /// </summary>
        public int ItemsInQueue
        {
            get
            {
                return m_values.Count;
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
            if (m_queueSize == 0)
            {
                throw new InvalidOperationException("Cannot enqueue Value. Queue size not set.");
            }
            if (m_values.Count == m_queueSize && !Dequeue(out _, out _))
            {
                //Queue is full, but no deque possible
                return;
            }
            m_values.Add(value);

            if (m_queueErrors)
            {
                m_errors.Add(error);
            }
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
            if (m_values.Count != 0)
            {
                m_values.Remove(m_values.Last());

                if (m_queueErrors && m_errors.Count != 0)
                {
                    m_errors.Remove(m_errors.Last());
                }
            }

            m_values.Add(value);

            if (m_queueErrors)
            {
                m_errors.Add(error);
            }
        }

        /// <inheritdoc/>
        public void ResetQueue(uint queueSize, bool queueErrors)
        {
            m_queueErrors = queueErrors;
            m_queueSize = queueSize;

            m_values = new List<DataValue>();
            m_errors = new List<ServiceResult>();
        }

        /// <inheritdoc/>
        public DataValue PeekLastValue()
        {
            if (m_values.Count == 0)
            {
                return null;
            }

            return m_values.Last();
        }

        /// <inheritdoc/>
        public bool Dequeue(out DataValue value, out ServiceResult error)
        {
            value = null;
            error = null;
            if (m_values.Any())
            {
                value = m_values.First();
                m_values.RemoveAt(0);

                if (m_queueErrors && m_errors.Any())
                {
                    error = m_errors.First();
                    m_errors.RemoveAt(0);
                }
                return true;
            }
            return false;
        }

        #endregion

        #region Private Fields
        private List<DataValue> m_values;
        private List<ServiceResult> m_errors;
        private uint m_queueSize;
        private bool m_queueErrors;
        private readonly bool m_isDurable;
        #endregion
    }
}
