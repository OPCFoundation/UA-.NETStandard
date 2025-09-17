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
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Opc.Ua;
using Opc.Ua.Server;

namespace Quickstarts.Servers
{
    /// <summary>
    /// A factory for <see cref="IDataChangeMonitoredItemQueue"> and </see> <see cref="IEventMonitoredItemQueue"/>
    /// </summary>
    public class DurableMonitoredItemQueueFactory : IMonitoredItemQueueFactory
    {
        private readonly BatchPersistor m_batchPersistor;
        private readonly ILogger m_logger;
        private readonly ITelemetryContext m_telemetry;

        private static readonly JsonSerializerSettings s_settings = new()
        {
            TypeNameHandling = TypeNameHandling.All
        };

        private const string kQueueDirectory = "Queues";
        private const string kBase_filename = "_queue.txt";

        private ConcurrentDictionary<uint, DurableDataChangeMonitoredItemQueue> m_dataChangeQueues = new();
        private ConcurrentDictionary<uint, DurableEventMonitoredItemQueue> m_eventQueues = new();

        /// <inheritdoc/>
        public bool SupportsDurableQueues => true;

        public DurableMonitoredItemQueueFactory(ITelemetryContext telemetry)
        {
            m_telemetry = telemetry;
            m_logger = telemetry.CreateLogger<DurableDataChangeMonitoredItemQueue>();
            m_batchPersistor = new BatchPersistor(telemetry);
        }

        /// <inheritdoc/>
        public IDataChangeMonitoredItemQueue CreateDataChangeQueue(
            bool createDurable,
            uint monitoredItemId)
        {
            //use durable queue only if MI is durable
            if (createDurable)
            {
                var queue = new DurableDataChangeMonitoredItemQueue(
                    createDurable,
                    monitoredItemId,
                    m_batchPersistor,
                    m_telemetry);
                queue.Disposed += DataChangeQueueDisposed;
                m_dataChangeQueues.AddOrUpdate(monitoredItemId, queue, (_, _) => queue);
                return queue;
            }

            return new DataChangeMonitoredItemQueue(createDurable, monitoredItemId, m_telemetry);
        }

        /// <inheritdoc/>
        public IEventMonitoredItemQueue CreateEventQueue(bool createDurable, uint monitoredItemId)
        {
            //use durable queue only if MI is durable
            if (createDurable)
            {
                var queue = new DurableEventMonitoredItemQueue(
                    createDurable,
                    monitoredItemId,
                    m_batchPersistor,
                    m_telemetry);
                queue.Disposed += EventQueueDisposed;
                m_eventQueues.AddOrUpdate(monitoredItemId, queue, (_, _) => queue);
                return queue;
            }

            return new EventMonitoredItemQueue(createDurable, monitoredItemId, m_telemetry);
        }

        private void DataChangeQueueDisposed(object sender, EventArgs eventArgs)
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
        /// Deletes the batches of all queues that are not in the list
        /// </summary>
        /// <param name="ids">the MonitoredItem ids of the queues to store</param>
        public void PersistQueues(IEnumerable<uint> ids, string baseDirectory)
        {
            string targetPath = Path.Combine(baseDirectory, kQueueDirectory);
            if (!Directory.Exists(targetPath))
            {
                Directory.CreateDirectory(targetPath);
            }
            using IDisposable scope = MessageContextExtension.SetScopedContext(m_telemetry);
            foreach (uint id in ids)
            {
                try
                {
                    if (m_dataChangeQueues.TryGetValue(
                        id,
                        out DurableDataChangeMonitoredItemQueue queue))
                    {
                        //store
                        string result = JsonConvert.SerializeObject(
                            queue.ToStorableQueue(),
                            s_settings);
                        File.WriteAllText(Path.Combine(targetPath, id + kBase_filename), result);
                        continue;
                    }

                    if (m_eventQueues.TryGetValue(
                        id,
                        out DurableEventMonitoredItemQueue eventQueue))
                    {
                        //store
                        string result = JsonConvert.SerializeObject(
                            eventQueue.ToStorableQueue(),
                            s_settings);
                        File.WriteAllText(Path.Combine(targetPath, id + kBase_filename), result);
                        continue;
                    }
                    m_logger.LogWarning(
                        "Failed to persist queue for monitored item with id {MonitoredItemId} as the queue was not known to the server",
                        id);
                }
                catch (Exception ex)
                {
                    m_logger.LogWarning(
                        ex,
                        "Failed to persist queue for monitored item with id {MonitoredItemId}",
                        id);
                }
            }
            // Delete batches of all queues that are not in the list
            m_batchPersistor.DeleteBatches(ids);
        }

        /// <summary>
        /// Restore an Event queue
        /// </summary>
        public IEventMonitoredItemQueue RestoreEventQueue(uint id, string baseDirectory)
        {
            try
            {
                string targetFile = Path.Combine(
                    baseDirectory,
                    kQueueDirectory,
                    id + kBase_filename);
                if (!File.Exists(targetFile))
                {
                    return null;
                }
                string result = File.ReadAllText(targetFile);
                File.Delete(targetFile);
                using IDisposable scope = MessageContextExtension.SetScopedContext(m_telemetry);
                StorableEventQueue template = JsonConvert.DeserializeObject<StorableEventQueue>(
                    result,
                    s_settings);

                var queue = new DurableEventMonitoredItemQueue(template, m_batchPersistor);
                m_eventQueues.AddOrUpdate(id, queue, (_, _) => queue);

                return queue;
            }
            catch (Exception ex)
            {
                m_logger.LogWarning(ex, "Failed to restore event change queue");
            }
            return null;
        }

        /// <summary>
        /// Restore a DataChange queue
        /// </summary>
        public IDataChangeMonitoredItemQueue RestoreDataChangeQueue(uint id, string baseDirectory)
        {
            try
            {
                string targetFile = Path.Combine(
                    baseDirectory,
                    kQueueDirectory,
                    id + kBase_filename);
                if (!File.Exists(targetFile))
                {
                    return null;
                }
                string result = File.ReadAllText(targetFile);
                File.Delete(targetFile);
                using IDisposable scope = MessageContextExtension.SetScopedContext(m_telemetry);
                StorableDataChangeQueue template = JsonConvert.DeserializeObject<StorableDataChangeQueue>(
                    result,
                    s_settings);

                var queue = new DurableDataChangeMonitoredItemQueue(template, m_batchPersistor);
                m_dataChangeQueues.AddOrUpdate(id, queue, (_, _) => queue);

                return queue;
            }
            catch (Exception ex)
            {
                m_logger.LogWarning(ex, "Failed to restore data change queue");
            }
            return null;
        }

        /// <summary>
        /// Remove all stored queues and batches that are not in the list
        /// </summary>
        public void CleanStoredQueues(string baseDirectory, IEnumerable<uint> batchesToKeep)
        {
            try
            {
                string targetPath = Path.Combine(baseDirectory, kQueueDirectory);
                if (Directory.Exists(targetPath))
                {
                    Directory.Delete(targetPath, true);
                }
            }
            catch (Exception ex)
            {
                m_logger.LogWarning(ex, "Failed to clean stored queues");
            }

            m_batchPersistor.DeleteBatches(batchesToKeep);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
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
}
