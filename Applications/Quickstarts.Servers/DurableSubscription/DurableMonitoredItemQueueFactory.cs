/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
        private readonly IServiceMessageContext m_messageContext;
        private const string kQueueDirectory = "Queues";
        private const string kBase_filename = "_queue.bin";

        private ConcurrentDictionary<uint, DurableDataChangeMonitoredItemQueue> m_dataChangeQueues = new();
        private ConcurrentDictionary<uint, DurableEventMonitoredItemQueue> m_eventQueues = new();

        /// <inheritdoc/>
        public bool SupportsDurableQueues => true;

        public DurableMonitoredItemQueueFactory(
            ITelemetryContext telemetry,
            IServiceMessageContext messageContext)
        {
            m_telemetry = telemetry;
            m_messageContext = messageContext;
            m_logger = telemetry.CreateLogger<DurableDataChangeMonitoredItemQueue>();
            m_batchPersistor = new BatchPersistor(telemetry);
        }

        /// <inheritdoc/>
        public IDataChangeMonitoredItemQueue CreateDataChangeQueue(
            bool isDurable,
            uint monitoredItemId)
        {
            //use durable queue only if MI is durable
            if (isDurable)
            {
                var queue = new DurableDataChangeMonitoredItemQueue(
                    isDurable,
                    monitoredItemId,
                    m_batchPersistor,
                    m_telemetry);
                queue.Disposed += DataChangeQueueDisposed;
                m_dataChangeQueues.AddOrUpdate(monitoredItemId, queue, (_, _) => queue);
                return queue;
            }

            return new DataChangeMonitoredItemQueue(isDurable, monitoredItemId, m_telemetry);
        }

        /// <inheritdoc/>
        public IEventMonitoredItemQueue CreateEventQueue(bool isDurable, uint monitoredItemId)
        {
            // use durable queue only if MI is durable
            if (isDurable)
            {
                var queue = new DurableEventMonitoredItemQueue(
                    isDurable,
                    monitoredItemId,
                    m_batchPersistor,
                    m_telemetry);
                queue.Disposed += EventQueueDisposed;
                m_eventQueues.AddOrUpdate(monitoredItemId, queue, (_, _) => queue);
                return queue;
            }

            return new EventMonitoredItemQueue(isDurable, monitoredItemId, m_telemetry);
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
        /// Persist the queues of the monitored items with the provided ids.
        /// Deletes the batches of all queues that are not in the list.
        /// </summary>
        public void PersistQueues(IEnumerable<uint> ids, string baseDirectory)
        {
            string targetPath = Path.Combine(baseDirectory, kQueueDirectory);
            if (!Directory.Exists(targetPath))
            {
                Directory.CreateDirectory(targetPath);
            }
            foreach (uint id in ids)
            {
                try
                {
                    if (m_dataChangeQueues.TryGetValue(
                        id,
                        out DurableDataChangeMonitoredItemQueue queue))
                    {
                        string filePath = Path.Combine(targetPath, id + kBase_filename);
                        using FileStream stream = File.Create(filePath);
                        using var encoder = new BinaryEncoder(
                            stream, m_messageContext, true);
                        encoder.WriteStringArray(
                            null, m_messageContext.NamespaceUris.ToArrayOf());
                        encoder.WriteStringArray(
                            null, m_messageContext.ServerUris.ToArrayOf());
                        EncodeDataChangeQueue(encoder, queue.ToStorableQueue());
                        continue;
                    }

                    if (m_eventQueues.TryGetValue(
                        id,
                        out DurableEventMonitoredItemQueue eventQueue))
                    {
                        string filePath = Path.Combine(targetPath, id + kBase_filename);
                        using FileStream stream = File.Create(filePath);
                        using var encoder = new BinaryEncoder(
                            stream, m_messageContext, true);
                        encoder.WriteStringArray(
                            null, m_messageContext.NamespaceUris.ToArrayOf());
                        encoder.WriteStringArray(
                            null, m_messageContext.ServerUris.ToArrayOf());
                        EncodeEventQueue(encoder, eventQueue.ToStorableQueue());
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
                StorableEventQueue template;
                using (FileStream stream = File.OpenRead(targetFile))
                using (var decoder = new BinaryDecoder(
                    stream, m_messageContext, true))
                {
                    ArrayOf<string> nsUris = decoder.ReadStringArray(null);
                    ArrayOf<string> serverUris = decoder.ReadStringArray(null);
                    decoder.SetMappingTables(
                        new NamespaceTable(nsUris.Memory.ToArray()),
                        new StringTable(serverUris.Memory.ToArray()));
                    template = DecodeEventQueue(decoder);
                }
                File.Delete(targetFile);

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
                StorableDataChangeQueue template;
                using (FileStream stream = File.OpenRead(targetFile))
                using (var decoder = new BinaryDecoder(
                    stream, m_messageContext, true))
                {
                    ArrayOf<string> nsUris = decoder.ReadStringArray(null);
                    ArrayOf<string> serverUris = decoder.ReadStringArray(null);
                    decoder.SetMappingTables(
                        new NamespaceTable(nsUris.Memory.ToArray()),
                        new StringTable(serverUris.Memory.ToArray()));
                    template = DecodeDataChangeQueue(decoder);
                }
                File.Delete(targetFile);

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
                    queue?.Dispose();
                }
                foreach (DurableDataChangeMonitoredItemQueue queue in m_dataChangeQueues.Values)
                {
                    queue?.Dispose();
                }
                m_dataChangeQueues = null;
                m_eventQueues = null;
            }
        }

        public static void EncodeDataChangeQueue(BinaryEncoder encoder, StorableDataChangeQueue q)
        {
            encoder.WriteBoolean(null, q.IsDurable);
            encoder.WriteUInt32(null, q.MonitoredItemId);
            encoder.WriteInt32(null, q.ItemsInQueue);
            encoder.WriteUInt32(null, q.QueueSize);
            EncodeDataChangeBatch(encoder, q.EnqueueBatch);
            EncodeDataChangeBatch(encoder, q.DequeueBatch);

            int batchCount = q.DataChangeBatches?.Count ?? 0;
            encoder.WriteInt32(null, batchCount);
            if (q.DataChangeBatches != null)
            {
                for (int i = 0; i < batchCount; i++)
                {
                    EncodeDataChangeBatch(encoder, q.DataChangeBatches[i]);
                }
            }
        }

        public static StorableDataChangeQueue DecodeDataChangeQueue(BinaryDecoder decoder)
        {
            var q = new StorableDataChangeQueue
            {
                IsDurable = decoder.ReadBoolean(null),
                MonitoredItemId = decoder.ReadUInt32(null),
                ItemsInQueue = decoder.ReadInt32(null),
                QueueSize = decoder.ReadUInt32(null),
                EnqueueBatch = DecodeDataChangeBatch(decoder),
                DequeueBatch = DecodeDataChangeBatch(decoder)
            };

            int batchCount = decoder.ReadInt32(null);
            q.DataChangeBatches = new List<DataChangeBatch>(batchCount);
            for (int i = 0; i < batchCount; i++)
            {
                q.DataChangeBatches.Add(DecodeDataChangeBatch(decoder));
            }
            return q;
        }

        internal static void EncodeDataChangeBatch(BinaryEncoder encoder, DataChangeBatch batch)
        {
            bool hasValue = batch != null;
            encoder.WriteBoolean(null, hasValue);
            if (!hasValue)
            {
                return;
            }

            encoder.WriteGuid(null, batch.Id);
            encoder.WriteUInt32(null, batch.BatchSize);
            encoder.WriteUInt32(null, batch.MonitoredItemId);
            encoder.WriteBoolean(null, batch.IsPersisted);

            int count = batch.Values?.Count ?? 0;
            encoder.WriteInt32(null, count);
            if (batch.Values != null)
            {
                for (int i = 0; i < count; i++)
                {
                    (DataValue dv, ServiceResult sr) = batch.Values[i];
                    encoder.WriteDataValue(null, dv);
                    encoder.WriteStatusCode(null,
                        sr?.StatusCode ?? StatusCodes.Good);
                }
            }
        }

        internal static DataChangeBatch DecodeDataChangeBatch(BinaryDecoder decoder)
        {
            bool hasValue = decoder.ReadBoolean(null);
            if (!hasValue)
            {
                return null;
            }

            Uuid id = decoder.ReadGuid(null);
            uint batchSize = decoder.ReadUInt32(null);
            uint monItemId = decoder.ReadUInt32(null);
            bool isPersisted = decoder.ReadBoolean(null);

            int count = decoder.ReadInt32(null);
            var values = new List<(DataValue, ServiceResult)>(count);
            for (int i = 0; i < count; i++)
            {
                DataValue dv = decoder.ReadDataValue(null);
                StatusCode sc = decoder.ReadStatusCode(null);
                ServiceResult sr = sc == StatusCodes.Good
                    ? null : new ServiceResult(sc);
                values.Add((dv, sr));
            }

            var batch = new DataChangeBatch(values, batchSize, monItemId);
            if (isPersisted)
            {
                batch.SetPersisted();
                batch.Restore(values);
            }
            return batch;
        }

        public static void EncodeEventQueue(BinaryEncoder encoder, StorableEventQueue q)
        {
            encoder.WriteBoolean(null, q.IsDurable);
            encoder.WriteUInt32(null, q.MonitoredItemId);
            encoder.WriteUInt32(null, q.QueueSize);
            EncodeEventBatch(encoder, q.EnqueueBatch);
            EncodeEventBatch(encoder, q.DequeueBatch);

            int batchCount = q.EventBatches?.Count ?? 0;
            encoder.WriteInt32(null, batchCount);
            if (q.EventBatches != null)
            {
                for (int i = 0; i < batchCount; i++)
                {
                    EncodeEventBatch(encoder, q.EventBatches[i]);
                }
            }
        }

        public static StorableEventQueue DecodeEventQueue(BinaryDecoder decoder)
        {
            var q = new StorableEventQueue
            {
                IsDurable = decoder.ReadBoolean(null),
                MonitoredItemId = decoder.ReadUInt32(null),
                QueueSize = decoder.ReadUInt32(null),
                EnqueueBatch = DecodeEventBatch(decoder),
                DequeueBatch = DecodeEventBatch(decoder)
            };

            int batchCount = decoder.ReadInt32(null);
            q.EventBatches = new List<EventBatch>(batchCount);
            for (int i = 0; i < batchCount; i++)
            {
                q.EventBatches.Add(DecodeEventBatch(decoder));
            }
            return q;
        }

        internal static void EncodeEventBatch(
            BinaryEncoder encoder, EventBatch batch)
        {
            bool hasValue = batch != null;
            encoder.WriteBoolean(null, hasValue);
            if (!hasValue)
            {
                return;
            }

            encoder.WriteGuid(null, batch.Id);
            encoder.WriteUInt32(null, batch.BatchSize);
            encoder.WriteUInt32(null, batch.MonitoredItemId);
            encoder.WriteBoolean(null, batch.IsPersisted);

            int count = batch.Events?.Count ?? 0;
            encoder.WriteInt32(null, count);
            if (batch.Events != null)
            {
                for (int i = 0; i < count; i++)
                {
                    encoder.WriteEncodeableAsExtensionObject(null, batch.Events[i]);
                }
            }
        }

        internal static EventBatch DecodeEventBatch(BinaryDecoder decoder)
        {
            bool hasValue = decoder.ReadBoolean(null);
            if (!hasValue)
            {
                return null;
            }

            Uuid id = decoder.ReadGuid(null);
            uint batchSize = decoder.ReadUInt32(null);
            uint monItemId = decoder.ReadUInt32(null);
            bool isPersisted = decoder.ReadBoolean(null);

            int count = decoder.ReadInt32(null);
            var events = new List<EventFieldList>(count);
            for (int i = 0; i < count; i++)
            {
                ExtensionObject eo = decoder.ReadExtensionObject(null);
                if (!eo.IsNull &&
                    eo.TryGetValue(out IEncodeable e) &&
                    e is EventFieldList efl)
                {
                    events.Add(efl);
                }
            }

            var batch = new EventBatch(events, batchSize, monItemId);
            if (isPersisted)
            {
                batch.SetPersisted();
                batch.Restore(events);
            }
            return batch;
        }
    }
}
