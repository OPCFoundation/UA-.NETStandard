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
using System.Linq;
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
        private readonly SubscriptionStoreEncoding m_encoding;
        private readonly IServiceMessageContext m_messageContext;
        private const string kQueueDirectory = "Queues";
        private const string kBase_filename = "_queue.dat";

        private ConcurrentDictionary<uint, DurableDataChangeMonitoredItemQueue> m_dataChangeQueues = new();
        private ConcurrentDictionary<uint, DurableEventMonitoredItemQueue> m_eventQueues = new();

        /// <inheritdoc/>
        public bool SupportsDurableQueues => true;

        public DurableMonitoredItemQueueFactory(
            ITelemetryContext telemetry,
            IServiceMessageContext messageContext,
            SubscriptionStoreEncoding encoding = SubscriptionStoreEncoding.Json)
        {
            m_telemetry = telemetry;
            m_encoding = encoding;
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
                        using IEncoder encoder = CreateEncoder(stream);
                        EncodeDataChangeQueue(encoder, queue.ToStorableQueue());
                        continue;
                    }

                    if (m_eventQueues.TryGetValue(
                        id,
                        out DurableEventMonitoredItemQueue eventQueue))
                    {
                        string filePath = Path.Combine(targetPath, id + kBase_filename);
                        using FileStream stream = File.Create(filePath);
                        using IEncoder encoder = CreateEncoder(stream);
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
                using (IDecoder decoder = CreateDecoder(stream))
                {
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
                using (IDecoder decoder = CreateDecoder(stream))
                {
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

        private static void EncodeDataChangeQueue(
            IEncoder encoder, StorableDataChangeQueue q)
        {
            encoder.WriteBoolean("IsDurable", q.IsDurable);
            encoder.WriteUInt32("MonitoredItemId", q.MonitoredItemId);
            encoder.WriteInt32("ItemsInQueue", q.ItemsInQueue);
            encoder.WriteUInt32("QueueSize", q.QueueSize);
            EncodeDataChangeBatch(encoder, "EnqueueBatch", q.EnqueueBatch);
            EncodeDataChangeBatch(encoder, "DequeueBatch", q.DequeueBatch);

            int batchCount = q.DataChangeBatches?.Count ?? 0;
            encoder.WriteInt32("DataChangeBatchCount", batchCount);
            if (q.DataChangeBatches != null)
            {
                for (int i = 0; i < batchCount; i++)
                {
                    EncodeDataChangeBatch(
                        encoder, "DCBatch_" + i, q.DataChangeBatches[i]);
                }
            }
        }

        private static StorableDataChangeQueue DecodeDataChangeQueue(IDecoder decoder)
        {
            var q = new StorableDataChangeQueue
            {
                IsDurable = decoder.ReadBoolean("IsDurable"),
                MonitoredItemId = decoder.ReadUInt32("MonitoredItemId"),
                ItemsInQueue = decoder.ReadInt32("ItemsInQueue"),
                QueueSize = decoder.ReadUInt32("QueueSize"),
                EnqueueBatch = DecodeDataChangeBatch(decoder, "EnqueueBatch"),
                DequeueBatch = DecodeDataChangeBatch(decoder, "DequeueBatch"),
            };

            int batchCount = decoder.ReadInt32("DataChangeBatchCount");
            q.DataChangeBatches = new List<DataChangeBatch>(batchCount);
            for (int i = 0; i < batchCount; i++)
            {
                q.DataChangeBatches.Add(
                    DecodeDataChangeBatch(decoder, "DCBatch_" + i));
            }
            return q;
        }

        private static void EncodeDataChangeBatch(
            IEncoder encoder, string prefix, DataChangeBatch batch)
        {
            bool hasValue = batch != null;
            encoder.WriteBoolean(prefix + "_HasValue", hasValue);
            if (!hasValue)
            {
                return;
            }

            encoder.WriteGuid(prefix + "_Id", batch.Id);
            encoder.WriteUInt32(prefix + "_BatchSize", batch.BatchSize);
            encoder.WriteUInt32(prefix + "_MonItemId", batch.MonitoredItemId);
            encoder.WriteBoolean(prefix + "_IsPersisted", batch.IsPersisted);

            int count = batch.Values?.Count ?? 0;
            encoder.WriteInt32(prefix + "_ValueCount", count);
            if (batch.Values != null)
            {
                for (int i = 0; i < count; i++)
                {
                    (DataValue dv, ServiceResult sr) = batch.Values[i];
                    encoder.WriteDataValue(prefix + "_DV_" + i, dv);
                    encoder.WriteStatusCode(prefix + "_SR_" + i,
                        sr?.StatusCode ?? StatusCodes.Good);
                }
            }
        }

        private static DataChangeBatch DecodeDataChangeBatch(
            IDecoder decoder, string prefix)
        {
            bool hasValue = decoder.ReadBoolean(prefix + "_HasValue");
            if (!hasValue)
            {
                return null;
            }

            Uuid id = decoder.ReadGuid(prefix + "_Id");
            uint batchSize = decoder.ReadUInt32(prefix + "_BatchSize");
            uint monItemId = decoder.ReadUInt32(prefix + "_MonItemId");
            bool isPersisted = decoder.ReadBoolean(prefix + "_IsPersisted");

            int count = decoder.ReadInt32(prefix + "_ValueCount");
            var values = new List<(DataValue, ServiceResult)>(count);
            for (int i = 0; i < count; i++)
            {
                DataValue dv = decoder.ReadDataValue(prefix + "_DV_" + i);
                StatusCode sc = decoder.ReadStatusCode(prefix + "_SR_" + i);
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
        private static void EncodeEventQueue(
            IEncoder encoder, StorableEventQueue q)
        {
            encoder.WriteBoolean("IsDurable", q.IsDurable);
            encoder.WriteUInt32("MonitoredItemId", q.MonitoredItemId);
            encoder.WriteUInt32("QueueSize", q.QueueSize);
            EncodeEventBatch(encoder, "EnqueueBatch", q.EnqueueBatch);
            EncodeEventBatch(encoder, "DequeueBatch", q.DequeueBatch);

            int batchCount = q.EventBatches?.Count ?? 0;
            encoder.WriteInt32("EventBatchCount", batchCount);
            if (q.EventBatches != null)
            {
                for (int i = 0; i < batchCount; i++)
                {
                    EncodeEventBatch(
                        encoder, "EvBatch_" + i, q.EventBatches[i]);
                }
            }
        }

        private static StorableEventQueue DecodeEventQueue(IDecoder decoder)
        {
            var q = new StorableEventQueue
            {
                IsDurable = decoder.ReadBoolean("IsDurable"),
                MonitoredItemId = decoder.ReadUInt32("MonitoredItemId"),
                QueueSize = decoder.ReadUInt32("QueueSize"),
                EnqueueBatch = DecodeEventBatch(decoder, "EnqueueBatch"),
                DequeueBatch = DecodeEventBatch(decoder, "DequeueBatch"),
            };

            int batchCount = decoder.ReadInt32("EventBatchCount");
            q.EventBatches = new List<EventBatch>(batchCount);
            for (int i = 0; i < batchCount; i++)
            {
                q.EventBatches.Add(
                    DecodeEventBatch(decoder, "EvBatch_" + i));
            }
            return q;
        }

        private static void EncodeEventBatch(
            IEncoder encoder, string prefix, EventBatch batch)
        {
            bool hasValue = batch != null;
            encoder.WriteBoolean(prefix + "_HasValue", hasValue);
            if (!hasValue)
            {
                return;
            }

            encoder.WriteGuid(prefix + "_Id", batch.Id);
            encoder.WriteUInt32(prefix + "_BatchSize", batch.BatchSize);
            encoder.WriteUInt32(prefix + "_MonItemId", batch.MonitoredItemId);
            encoder.WriteBoolean(prefix + "_IsPersisted", batch.IsPersisted);

            int count = batch.Events?.Count ?? 0;
            encoder.WriteInt32(prefix + "_EventCount", count);
            if (batch.Events != null)
            {
                for (int i = 0; i < count; i++)
                {
                    encoder.WriteEncodeableAsExtensionObject(
                        prefix + "_Ev_" + i, batch.Events[i]);
                }
            }
        }

        private static EventBatch DecodeEventBatch(
            IDecoder decoder, string prefix)
        {
            bool hasValue = decoder.ReadBoolean(prefix + "_HasValue");
            if (!hasValue)
            {
                return null;
            }

            Uuid id = decoder.ReadGuid(prefix + "_Id");
            uint batchSize = decoder.ReadUInt32(prefix + "_BatchSize");
            uint monItemId = decoder.ReadUInt32(prefix + "_MonItemId");
            bool isPersisted = decoder.ReadBoolean(prefix + "_IsPersisted");

            int count = decoder.ReadInt32(prefix + "_EventCount");
            var events = new List<EventFieldList>(count);
            for (int i = 0; i < count; i++)
            {
                ExtensionObject eo = decoder.ReadExtensionObject(prefix + "_Ev_" + i);
                if (!eo.IsNull &&
                    eo.TryGetEncodeable(out IEncodeable e) &&
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

        private IEncoder CreateEncoder(Stream stream)
        {
            return m_encoding switch
            {
                SubscriptionStoreEncoding.Binary =>
                    new BinaryEncoder(stream, m_messageContext, true),
                _ => new JsonEncoder(stream, m_messageContext)
            };
        }

        private IDecoder CreateDecoder(Stream stream)
        {
            return m_encoding switch
            {
                SubscriptionStoreEncoding.Binary =>
                    new BinaryDecoder(stream, m_messageContext, true),
                _ => new JsonDecoder(stream, m_messageContext)
            };
        }
    }
}
