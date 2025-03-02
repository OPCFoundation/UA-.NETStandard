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
using Opc.Ua;
using Opc.Ua.Server;

namespace Quickstarts.Servers
{
    public class DurableEventMonitoredItemQueue : EventMonitoredItemQueue
    {
        /// <summary>
        /// Invoked when the queue is disposed
        /// </summary>
        public event EventHandler Disposed;

        /// <summary>
        /// Creates an empty queue.
        /// </summary>
        public DurableEventMonitoredItemQueue(bool createDurable, uint monitoredItemId) : base(false, monitoredItemId)
        {
            IsDurable = createDurable;
        }

        /// <summary>
        /// Creates a queue from a template
        /// </summary>
        public DurableEventMonitoredItemQueue(StorableEventQueue queue) : base(false, queue.MonitoredItemId)
        {
            IsDurable = queue.IsDurable;
            m_events = queue.Events;
            QueueSize = queue.QueueSize;
        }

        #region Public Methods
        /// <summary>
        /// Brings the queue with contents into a storable format
        /// </summary>
        /// <returns></returns>
        public StorableEventQueue ToStorableQueue()
        {

            return new StorableEventQueue {
                IsDurable = IsDurable,
                MonitoredItemId = MonitoredItemId,
                Events = m_events,
                QueueSize = QueueSize,
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
    public class StorableEventQueue
    {
        public bool IsDurable { get; set; }
        public uint MonitoredItemId { get; set; }
        public List<EventFieldList> Events { get; set; }
        public uint QueueSize { get; set; }
    }
}
