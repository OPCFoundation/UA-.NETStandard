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


using Opc.Ua.Server;

namespace Quickstarts.Servers
{
    /// <summary>
    /// A factory for <see cref="IDataChangeMonitoredItemQueue"> and </see> <see cref="IEventMonitoredItemQueue"/>
    /// </summary>
    public class DurableMonitoredItemQueueFactory : IMonitoredItemQueueFactory
    {
        /// <inheritdoc/>
        public bool SupportsDurableQueues => true;
        /// <inheritdoc/>
        public IDataChangeMonitoredItemQueue CreateDataChangeQueue(bool createDurable, uint monitoredItemId)
        {
            //use durable queue only if MI is durable
            if (createDurable)
            {

                return new DurableDataChangeMonitoredItemQueue(createDurable, monitoredItemId);
            }
            else
            {
                return new DataChangeMonitoredItemQueue(createDurable, monitoredItemId);
            }
            
        }

        /// <inheritdoc/>
        public IEventMonitoredItemQueue CreateEventQueue(bool createDurable, uint monitoredItemId)
        {
            //use durable queue only if MI is durable
            if (createDurable)
            {

                return new DurableEventMonitoredItemQueue(createDurable, monitoredItemId);
            }
            else
            {
                return new EventMonitoredItemQueue(createDurable, monitoredItemId);
            }
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
        public DurableEventMonitoredItemQueue(bool createDurable, uint monitoredItemId) : base(false, monitoredItemId)
        {
            IsDurable = createDurable;
        }

        #region Public Methods
        /// <inheritdoc/>
        public override bool IsDurable { get; }
        #endregion
    }

    public class DurableDataChangeMonitoredItemQueue : DataChangeMonitoredItemQueue
    {
        /// <summary>
        /// Creates an empty queue.
        /// </summary>
        public DurableDataChangeMonitoredItemQueue(bool createDurable, uint monitoredItemId) : base(false, monitoredItemId)
        {
            IsDurable = createDurable;
        }

        #region Public Methods
        /// <inheritdoc/>
        public override bool IsDurable { get; }
        #endregion
    }

}
