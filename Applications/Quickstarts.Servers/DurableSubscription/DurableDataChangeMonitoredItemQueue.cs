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
using Opc.Ua;
using Opc.Ua.Server;

namespace Quickstarts.Servers
{
    public class DurableDataChangeMonitoredItemQueue : DataChangeMonitoredItemQueue
    {
        /// <summary>
        /// Invoked when the queue is disposed
        /// </summary>
        public event EventHandler Disposed;

        /// <summary>
        /// Creates an empty queue.
        /// </summary>
        public DurableDataChangeMonitoredItemQueue(bool createDurable, uint monitoredItemId) : base(false, monitoredItemId)
        {
            IsDurable = createDurable;
        }
        /// <summary>
        /// Creates a queue from a template
        /// </summary>
        public DurableDataChangeMonitoredItemQueue(StorableDataChangeQueue queue) : base(false, queue.MonitoredItemId)
        {
            IsDurable = queue.IsDurable;
            m_start = queue.Start;
            m_end = queue.End;
            m_values = queue.Values;
            m_errors = queue.Errors;
        }

        #region Public Methods
        /// <summary>
        /// Brings the queue with content into a storable format
        /// </summary>
        /// <returns></returns>
        public StorableDataChangeQueue ToStorableQueue()
        {

            return new StorableDataChangeQueue {
                IsDurable = IsDurable,
                MonitoredItemId = MonitoredItemId,
                Start = m_start,
                End = m_end,
                Values = m_values,
                Errors = m_errors
            };
        }

        /// <inheritdoc/>
        public override bool IsDurable { get; }

        public override void Dispose()
        {
            Disposed?.Invoke(this, new EventArgs());
        }
        #endregion
    }

    public class StorableDataChangeQueue
    {
        public bool IsDurable { get; set; }
        public uint MonitoredItemId { get; set; }
        public int Start { get; set; }
        public int End { get; set; }
        public DataValue[] Values { get; set; }
        public ServiceResult[] Errors { get; set; }
    }
}
