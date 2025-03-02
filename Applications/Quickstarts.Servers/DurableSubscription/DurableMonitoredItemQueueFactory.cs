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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Opc.Ua.Server;

namespace Quickstarts.Servers
{
    /// <summary>
    /// A factory for <see cref="IDataChangeMonitoredItemQueue"> and </see> <see cref="IEventMonitoredItemQueue"/>
    /// </summary>
    public class DurableMonitoredItemQueueFactory : IMonitoredItemQueueFactory
    {
        private static readonly string s_queueDirectory = "Queues";
        private static readonly string s_base_filename = "_queue.txt";
        private ConcurrentDictionary<uint, DurableDataChangeMonitoredItemQueue> m_dataChangeQueues = new ConcurrentDictionary<uint, DurableDataChangeMonitoredItemQueue>();
        private ConcurrentDictionary<uint, DurableEventMonitoredItemQueue> m_eventQueues = new ConcurrentDictionary<uint, DurableEventMonitoredItemQueue>();
        /// <inheritdoc/>
        public bool SupportsDurableQueues => true;
        /// <inheritdoc/>
        public IDataChangeMonitoredItemQueue CreateDataChangeQueue(bool createDurable, uint monitoredItemId)
        {
            //use durable queue only if MI is durable
            if (createDurable)
            {
                var queue = new DurableDataChangeMonitoredItemQueue(createDurable, monitoredItemId);
                queue.Disposed += DataChangeQeueDisposed;
                m_dataChangeQueues.AddOrUpdate(monitoredItemId, queue, (_, _) => queue);
                return queue;
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
                var queue = new DurableEventMonitoredItemQueue(createDurable, monitoredItemId);
                queue.Disposed += EventQueueDisposed;
                m_eventQueues.AddOrUpdate(monitoredItemId, queue, (_, _) => queue);
                return queue;
            }
            else
            {
                return new EventMonitoredItemQueue(createDurable, monitoredItemId);
            }
        }

        private void DataChangeQeueDisposed(object sender, EventArgs eventArgs)
        {
            if (sender is DataChangeMonitoredItemQueue queue)
            {
                m_dataChangeQueues.TryRemove(queue.MonitoredItemId, out _);
            }
        }

        private void EventQueueDisposed(object sender, EventArgs eventArgs)
        {
            if (sender is EventMonitoredItemQueue queue)
            {
                m_eventQueues.TryRemove(queue.MonitoredItemId, out _);
            }
        }

        /// <summary>
        /// Persist the queues of the monitored items with the provided ids
        /// </summary>
        /// <param name="ids">the MonitoredItem ids of the queues to store</param>
        public void PersistQueues(IEnumerable<uint> ids, string baseDirectory)
        {
            string targetPath = Path.Combine(baseDirectory, s_queueDirectory);
            if (!Directory.Exists(targetPath))
            {
                Directory.CreateDirectory(targetPath);
            }
            foreach (uint id in ids)
            {
                try
                {
                    if (m_dataChangeQueues.TryGetValue(id, out DurableDataChangeMonitoredItemQueue queue))
                    {
                        //store
                        string result = JsonConvert.SerializeObject(queue.ToStorableQueue());
                        File.WriteAllText(Path.Combine(targetPath, id + s_base_filename), result);
                        continue;
                    }

                    if (m_eventQueues.TryGetValue(id, out DurableEventMonitoredItemQueue eventQueue))
                    {
                        //store
                        string result = JsonConvert.SerializeObject(eventQueue.ToStorableQueue());
                        File.WriteAllText(Path.Combine(targetPath, id + s_base_filename), result);
                        continue;
                    }
                    Opc.Ua.Utils.LogWarning("Failed to persist queue for monitored item with id {0} as the queue was not known to the server", id);
                }
                catch (Exception ex)
                {
                    Opc.Ua.Utils.LogWarning(ex, "Failed to persist queue for monitored item with id {0}", id);
                }
            }
        }

        /// <summary>
        /// Restore an Event queue
        /// </summary>
        public IEventMonitoredItemQueue RestoreEventQueue(uint id, string baseDirectory)
        {
            string targetFile = Path.Combine(baseDirectory, s_queueDirectory, id + s_base_filename);
            if (!File.Exists(targetFile))
            {
                return null;
            }
            string result = File.ReadAllText(targetFile);
            StorableEventQueue queue = JsonConvert.DeserializeObject<StorableEventQueue>(result);
            return new DurableEventMonitoredItemQueue(queue);
        }

        /// <summary>
        /// Restore a DataChange queue
        /// </summary>
        public IDataChangeMonitoredItemQueue RestoreDataChangeQueue(uint id, string baseDirectory)
        {
            string targetFile = Path.Combine(baseDirectory, s_queueDirectory, id + s_base_filename);
            if (!File.Exists(targetFile))
            {
                return null;
            }
            string result = File.ReadAllText(targetFile);
            StorableDataChangeQueue queue = JsonConvert.DeserializeObject<StorableDataChangeQueue>(result);
            return new DurableDataChangeMonitoredItemQueue(queue);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            foreach (DurableEventMonitoredItemQueue queue in m_eventQueues.Values)
            {
                Opc.Ua.Utils.SilentDispose(queue);
            }
            foreach (DurableDataChangeMonitoredItemQueue queue in m_dataChangeQueues.Values)
            {
                Opc.Ua.Utils.SilentDispose(queue);
            }
            m_dataChangeQueues = null;
            m_eventQueues = null;
        }
    }
}
