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
using System.Threading;
using Opc.Ua;
using Opc.Ua.Server;

namespace Quickstarts.Servers
{
    public class DurableEventMonitoredItemQueue : IEventMonitoredItemQueue
    {
        private const uint kMaxNoOfEntriesCheckedForDuplicateEvents = 1000;
        private const uint kBatchSize = 1000;

        /// <summary>
        /// Invoked when the queue is disposed
        /// </summary>
        public event EventHandler Disposed;

        /// <summary>
        /// Creates an empty queue.
        /// </summary>
        public DurableEventMonitoredItemQueue(bool createDurable, uint monitoredItemId, IBatchPersistor batchPersistor)
        {
            IsDurable = createDurable;
            m_batchPersistor = batchPersistor;
            MonitoredItemId = monitoredItemId;
            QueueSize = 0;
            m_itemsInQueue = 0;
            m_enqueueBatch = new EventBatch(new List<EventFieldList>(), kBatchSize, monitoredItemId);
            m_dequeueBatch = m_enqueueBatch;
        }

        /// <summary>
        /// Creates a queue from a template
        /// </summary>
        public DurableEventMonitoredItemQueue(StorableEventQueue queue, IBatchPersistor batchPersistor)
        {
            IsDurable = queue.IsDurable;
            m_enqueueBatch = queue.EnqueueBatch;
            m_eventBatches = queue.EventBatches;
            m_dequeueBatch = queue.DequeueBatch;
            QueueSize = queue.QueueSize;
            m_itemsInQueue = 0;
            MonitoredItemId = queue.MonitoredItemId;
            m_batchPersistor = batchPersistor;
        }

        #region Public Methods
        /// <inheritdoc/>
        public bool IsDurable { get; }

        /// <inheritdoc/>
        public uint MonitoredItemId { get; }

        /// <inheritdoc/>
        public uint QueueSize { get; protected set; }

        /// <inheritdoc/>
        public int ItemsInQueue => m_itemsInQueue;

        /// <inheritdoc/>
        public bool Dequeue(out EventFieldList value)
        {
            value = null;
            if (m_itemsInQueue > 0)
            {
                if (m_dequeueBatch.IsPersisted)
                {
                    Opc.Ua.Utils.LogDebug("Dequeue was requeusted but queue was not restored for monitoreditem {0} try to restore for 10 ms.", MonitoredItemId);
                    m_batchPersistor.RequestBatchRestore(m_dequeueBatch);

                    if (!SpinWait.SpinUntil(() => !m_dequeueBatch.RestoreInProgress, 10))
                    {
                        Opc.Ua.Utils.LogDebug("Dequeue failed for monitoreditem {0} as queue could not be restored in time.", MonitoredItemId);
                        // Dequeue failed as queue could not be restored in time
                        return false;
                    }
                }

                value = m_dequeueBatch.Events[0];
                m_dequeueBatch.Events.RemoveAt(0);
                m_itemsInQueue--;
                HandleDequeBatching();
                return true;
            }
            return false;
        }

        /// <inheritdoc/>
        public void Enqueue(EventFieldList value)
        {
            if (QueueSize == 0)
            {
                throw new ServiceResultException(StatusCodes.BadInternalError, "Error queueing Event. Queue size is set to 0");
            }

            //Discard oldest
            if (m_itemsInQueue == QueueSize)
            {
                Dequeue(out EventFieldList _);
            }

            m_enqueueBatch.Events.Add(value);
            m_itemsInQueue++;
            HandleEnqueueBatching();
        }

        /// <summary>
        /// persists batches if needed
        /// </summary>
        private void HandleEnqueueBatching()
        {
            // Store the batch if it is full
            if (m_enqueueBatch.Events.Count >= kBatchSize)
            {
                // Special case: if the enqueue and dequeue batch are the same only one batch exists, so no storing is needed
                if (m_dequeueBatch == m_enqueueBatch)
                {
                    m_dequeueBatch = new EventBatch(m_enqueueBatch.Events, kBatchSize, MonitoredItemId);
                    m_enqueueBatch = new EventBatch(new List<EventFieldList>(), kBatchSize, MonitoredItemId);
                }
                // persist the batch
                else
                {
                    Opc.Ua.Utils.LogDebug("Storing batch for monitored item {0}", MonitoredItemId);

                    var batchToStore = new EventBatch(m_enqueueBatch.Events, kBatchSize, MonitoredItemId);
                    m_eventBatches.Add(batchToStore);
                    //only persist second batch in list, as the first could be needed, for duplicate event check
                    if (m_eventBatches.Count > 1)
                    {
                        m_batchPersistor.RequestBatchPersist(m_eventBatches[m_eventBatches.Count - 2]);
                    }

                    m_enqueueBatch = new EventBatch(new List<EventFieldList>(), kBatchSize, MonitoredItemId);
                }
            }
        }
        /// <summary>
        /// Restores batches if needed
        /// </summary>
        private void HandleDequeBatching()
        {
            // request a restore if the dequeue batch is half empty
            if (m_dequeueBatch.Events.Count <= kBatchSize / 2 && m_eventBatches.Count > 0)
            {
                m_batchPersistor.RequestBatchRestore(m_eventBatches[0]);
            }

            // if the dequeue batch is empty and there are stored batches, set the dequeue batch to the first stored batch
            if (m_dequeueBatch.Events.Count == 0)
            {
                if (m_eventBatches.Count > 0 && m_dequeueBatch != m_enqueueBatch)
                {
                    m_dequeueBatch = m_eventBatches[0];
                    m_eventBatches.RemoveAt(0);

                    // Request a restore for the next batch if there is one
                    if (m_eventBatches.Count > 0)
                    {
                        m_batchPersistor.RequestBatchRestore(m_eventBatches[0]);
                    }
                }
                else
                {
                    //only one batch exists
                    m_dequeueBatch = m_enqueueBatch;
                }
            }
        }

        /// <inheritdoc/>
        public bool IsEventContainedInQueue(IFilterTarget instance)
        {
            int maxCount = m_itemsInQueue > kMaxNoOfEntriesCheckedForDuplicateEvents ? (int)kMaxNoOfEntriesCheckedForDuplicateEvents : m_itemsInQueue;

            for (int i = 0; i < maxCount; i++)
            {
                // Check in the enqueue batch
                if (i < m_enqueueBatch.Events.Count && m_enqueueBatch.Events[i] is EventFieldList processedEvent)
                {
                    if (ReferenceEquals(instance, processedEvent.Handle))
                    {
                        return true;
                    }
                }
                // If the enqueue batch is smaller than maxCount, check in the first stored batch
                else if (i >= m_enqueueBatch.Events.Count && m_eventBatches.Count > 0)
                {
                    int indexInStoredBatch = i - m_enqueueBatch.Events.Count;
                    if (indexInStoredBatch < m_eventBatches[^1].Events.Count && m_eventBatches[^1].Events[indexInStoredBatch] is EventFieldList storedEvent && ReferenceEquals(instance, storedEvent.Handle))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <inheritdoc/>
        public void SetQueueSize(uint queueSize, bool discardOldest)
        {
            QueueSize = queueSize;

            if (m_itemsInQueue > QueueSize)
            {
                //ToDo: Remove files

                int itemsToRemove = (int)(m_itemsInQueue - QueueSize);

                if (discardOldest)
                {
                    // Remove from output batch
                    while (itemsToRemove > 0 && m_dequeueBatch.Events.Count > 0)
                    {
                        // Remove from output batch
                        int removeCount = Math.Min(itemsToRemove, m_dequeueBatch.Events.Count);
                        m_dequeueBatch.Events.RemoveRange(0, removeCount);
                        m_itemsInQueue -= removeCount;
                        itemsToRemove -= removeCount;
                    }

                    // Remove from stored batches if needed
                    while (itemsToRemove > 0 && m_eventBatches.Count > 0)
                    {
                        EventBatch batch = m_eventBatches[0];
                        m_batchPersistor.RestoreSynchronously(batch);
                        int batchCount = batch.Events.Count;

                        if (itemsToRemove >= batchCount)
                        {
                            m_eventBatches.RemoveAt(0);
                            m_itemsInQueue -= batchCount;
                            itemsToRemove -= batchCount;
                        }
                        else
                        {
                            batch.Events.RemoveRange(0, itemsToRemove);
                            m_itemsInQueue -= itemsToRemove;
                            itemsToRemove = 0;
                        }
                    }

                    // Remove from output batch
                    while (itemsToRemove > 0 && m_enqueueBatch.Events.Count > 0)
                    {
                        // Remove from output batch
                        int removeCount = Math.Min(itemsToRemove, m_enqueueBatch.Events.Count);
                        m_enqueueBatch.Events.RemoveRange(0, removeCount);
                        m_itemsInQueue -= removeCount;
                        itemsToRemove -= removeCount;
                    }
                }
                else
                {
                    // Remove from input batch
                    while (itemsToRemove > 0 && m_enqueueBatch.Events.Count > 0)
                    {
                        int removeCount = Math.Min(itemsToRemove, m_enqueueBatch.Events.Count);
                        m_enqueueBatch.Events.RemoveRange(m_enqueueBatch.Events.Count - removeCount, removeCount);
                        m_itemsInQueue -= removeCount;
                        itemsToRemove -= removeCount;
                    }

                    // Remove from stored batches if needed
                    while (itemsToRemove > 0 && m_eventBatches.Count > 0)
                    {
                        EventBatch batch = m_eventBatches[^1];
                        m_batchPersistor.RestoreSynchronously(batch);
                        int batchCount = batch.Events.Count;

                        if (itemsToRemove >= batchCount)
                        {
                            m_eventBatches.RemoveAt(m_eventBatches.Count - 1);
                            m_itemsInQueue -= batchCount;
                            itemsToRemove -= batchCount;
                        }
                        else
                        {
                            batch.Events.RemoveRange(batch.Events.Count - itemsToRemove, itemsToRemove);
                            m_itemsInQueue -= itemsToRemove;
                            itemsToRemove = 0;
                        }
                    }

                    // Remove from output batch
                    while (itemsToRemove > 0 && m_dequeueBatch.Events.Count > 0)
                    {
                        int removeCount = Math.Min(itemsToRemove, m_dequeueBatch.Events.Count);
                        m_dequeueBatch.Events.RemoveRange(m_dequeueBatch.Events.Count - removeCount, removeCount);
                        m_itemsInQueue -= removeCount;
                        itemsToRemove -= removeCount;
                    }
                }
            }
        }

        /// <summary>
        /// Brings the queue with contents into a storable format
        /// </summary>
        /// <returns></returns>
        public StorableEventQueue ToStorableQueue()
        {
            return new StorableEventQueue {
                IsDurable = IsDurable,
                MonitoredItemId = MonitoredItemId,
                DequeueBatch = m_dequeueBatch,
                EnqueueBatch = m_enqueueBatch,
                EventBatches = m_eventBatches,
                QueueSize = QueueSize,
            };
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
            if (disposing)
            {
                Disposed?.Invoke(this, EventArgs.Empty);
            }
        }
        #endregion

        #region Private Fields
        /// <summary>
        /// the contained in the queue
        /// </summary>
        private EventBatch m_enqueueBatch;
        private readonly List<EventBatch> m_eventBatches = new List<EventBatch>();
        private EventBatch m_dequeueBatch;
        private int m_itemsInQueue;
        private readonly IBatchPersistor m_batchPersistor;
        #endregion
    }

    /// <summary>
    /// Batch of events
    /// </summary>
    public class EventBatch : BatchBase
    {
        public EventBatch(List<EventFieldList> events, uint batchSize, uint monitoredItemId) : base(batchSize, monitoredItemId)
        {
            Events = events;
        }
        public List<EventFieldList> Events { get; private set; }

        public override void SetPersisted()
        {
            Events = null;
            IsPersisted = true;
            PersistingInProgress = false;
        }

        public void Restore(List<EventFieldList> events)
        {
            Events = events;
            IsPersisted = false;
            RestoreInProgress = false;
        }
    }

    public class StorableEventQueue
    {
        public bool IsDurable { get; set; }
        public uint MonitoredItemId { get; set; }
        public EventBatch EnqueueBatch { get; set; }
        public List<EventBatch> EventBatches { get; set; }
        public EventBatch DequeueBatch { get; set; }
        public uint QueueSize { get; set; }
    }
}
