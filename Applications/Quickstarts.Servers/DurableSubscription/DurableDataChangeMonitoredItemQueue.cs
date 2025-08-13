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
using System.Threading;
using Opc.Ua;
using Opc.Ua.Server;

namespace Quickstarts.Servers
{
    public class DurableDataChangeMonitoredItemQueue : IDataChangeMonitoredItemQueue
    {
        private const uint kBatchSize = 1000;

        /// <summary>
        /// Invoked when the queue is disposed
        /// </summary>
        public event EventHandler Disposed;

        /// <summary>
        /// Creates an empty queue.
        /// </summary>
        public DurableDataChangeMonitoredItemQueue(
            bool createDurable,
            uint monitoredItemId,
            IBatchPersistor batchPersistor)
        {
            IsDurable = createDurable;
            MonitoredItemId = monitoredItemId;
            m_batchPersistor = batchPersistor;
            m_enqueueBatch = new DataChangeBatch([], kBatchSize, monitoredItemId);
            m_dequeueBatch = m_enqueueBatch;
            QueueSize = 0;
            ItemsInQueue = 0;
        }

        /// <summary>
        /// Creates a queue from a template
        /// </summary>
        public DurableDataChangeMonitoredItemQueue(
            StorableDataChangeQueue queue,
            IBatchPersistor batchPersistor)
        {
            m_batchPersistor = batchPersistor;
            MonitoredItemId = queue.MonitoredItemId;
            IsDurable = queue.IsDurable;
            m_enqueueBatch = queue.EnqueueBatch;
            m_dequeueBatch = queue.DequeueBatch;
            m_dataChangeBatches = queue.DataChangeBatches;
            QueueSize = queue.QueueSize;
            ItemsInQueue = queue.ItemsInQueue;
        }

        /// <inheritdoc/>
        public uint MonitoredItemId { get; }

        /// <summary>
        /// Gets the current queue size.
        /// </summary>
        public uint QueueSize { get; private set; }

        /// <summary>
        /// Gets number of elements actually contained in value queue.
        /// </summary>
        public int ItemsInQueue { get; private set; }

        /// <summary>
        /// Brings the queue with content into a storable format
        /// </summary>
        public StorableDataChangeQueue ToStorableQueue()
        {
            return new StorableDataChangeQueue
            {
                IsDurable = IsDurable,
                MonitoredItemId = MonitoredItemId,
                QueueSize = QueueSize,
                ItemsInQueue = ItemsInQueue,
                DataChangeBatches = m_dataChangeBatches,
                DequeueBatch = m_dequeueBatch,
                EnqueueBatch = m_enqueueBatch
            };
        }

        /// <inheritdoc/>
        public bool IsDurable { get; }

        /// <summary>
        /// Adds the value to the queue.
        /// </summary>
        /// <param name="value">The value to queue.</param>
        /// <param name="error">The error to queue.</param>
        /// <exception cref="InvalidOperationException"></exception>
        public void Enqueue(DataValue value, ServiceResult error)
        {
            if (QueueSize == 0)
            {
                throw new InvalidOperationException("Cannot enqueue Value. Queue size not set.");
            }

            //check for full queue
            if (ItemsInQueue == QueueSize)
            {
                _ = Dequeue(out _, out _);
            }

            m_enqueueBatch.Values.Add((value, m_queueErrors ? error : null));
            ItemsInQueue++;
            HandleEnqueueBatching();
        }

        /// <summary>
        /// persists batches if needed
        /// </summary>
        private void HandleEnqueueBatching()
        {
            // Store the batch if it is full
            if (m_enqueueBatch.Values.Count >= kBatchSize)
            {
                // Special case: if the enqueue and dequeue batch are the same only one batch exists, so no storing is needed
                if (m_dequeueBatch == m_enqueueBatch)
                {
                    m_dequeueBatch = new DataChangeBatch(
                        m_enqueueBatch.Values,
                        kBatchSize,
                        MonitoredItemId);
                    m_enqueueBatch = new DataChangeBatch([], kBatchSize, MonitoredItemId);
                }
                // persist the batch
                else
                {
                    Opc.Ua.Utils.LogDebug("Storing batch for monitored item {0}", MonitoredItemId);

                    var batchToStore = new DataChangeBatch(
                        m_enqueueBatch.Values,
                        kBatchSize,
                        MonitoredItemId);
                    m_dataChangeBatches.Add(batchToStore);
                    if (m_dataChangeBatches.Count > 1)
                    {
                        m_batchPersistor.RequestBatchPersist(m_dataChangeBatches[^2]);
                    }

                    m_enqueueBatch = new DataChangeBatch([], kBatchSize, MonitoredItemId);
                }
            }
        }

        /// <inheritdoc/>
        public void OverwriteLastValue(DataValue value, ServiceResult error)
        {
            if (ItemsInQueue == 0)
            {
                throw new InvalidOperationException("Cannot overwrite Value. Queue is empty.");
            }
            if (m_enqueueBatch.Values.Count > 0)
            {
                m_enqueueBatch.Values[^1] = (value, error);
            }
            else if (m_dataChangeBatches.Count > 0)
            {
                DataChangeBatch batch = m_dataChangeBatches[^1];
                batch.Values[^1] = (value, error);
            }
            else
            {
                m_dequeueBatch.Values[^1] = (value, error);
            }
        }

        /// <inheritdoc/>
        public void ResetQueue(uint queueSize, bool queueErrors)
        {
            m_enqueueBatch = new DataChangeBatch([], kBatchSize, MonitoredItemId);
            m_dequeueBatch = m_enqueueBatch;
            ItemsInQueue = 0;
            m_queueErrors = queueErrors;
            QueueSize = queueSize;

            foreach (DataChangeBatch batch in m_dataChangeBatches)
            {
                m_batchPersistor.DeleteBatch(batch);
            }

            m_dataChangeBatches.Clear();
        }

        /// <inheritdoc/>
        public DataValue PeekLastValue()
        {
            if (ItemsInQueue == 0)
            {
                return null;
            }

            if (m_enqueueBatch.Values.Count > 0)
            {
                return m_enqueueBatch.Values[^1].Item1;
            }
            else if (m_dataChangeBatches.Count > 0)
            {
                DataChangeBatch batch = m_dataChangeBatches[^1];
                return batch.Values[^1].Item1;
            }
            else
            {
                return m_dequeueBatch.Values[^1].Item1;
            }
        }

        /// <inheritdoc/>
        public bool Dequeue(out DataValue value, out ServiceResult error)
        {
            value = null;
            error = null;

            // check for empty queue.
            if (ItemsInQueue == 0)
            {
                return false;
            }

            if (m_dequeueBatch.IsPersisted)
            {
                Opc.Ua.Utils.LogDebug(
                    "Dequeue was requeusted but queue was not restored for monitoreditem {0} try to restore for 10 ms.",
                    MonitoredItemId);
                m_batchPersistor.RequestBatchRestore(m_dequeueBatch);

                if (!SpinWait.SpinUntil(() => !m_dequeueBatch.RestoreInProgress, 10))
                {
                    Opc.Ua.Utils.LogDebug(
                        "Dequeue failed for monitoreditem {0} as queue could not be restored in time.",
                        MonitoredItemId);
                    // Dequeue failed as queue could not be restored in time
                    return false;
                }
            }

            (value, error) = m_dequeueBatch.Values[0];
            m_dequeueBatch.Values.RemoveAt(0);
            ItemsInQueue--;
            HandleDequeBatching();
            return true;
        }

        /// <summary>
        /// Restores batches if needed
        /// </summary>
        private void HandleDequeBatching()
        {
            // request a restore if the dequeue batch is half empty
            if (m_dequeueBatch.Values.Count <= kBatchSize / 2 && m_dataChangeBatches.Count > 0)
            {
                m_batchPersistor.RequestBatchRestore(m_dataChangeBatches[0]);
            }

            // if the dequeue batch is empty and there are stored batches, set the dequeue batch to the first stored batch
            if (m_dequeueBatch.Values.Count == 0 && m_dequeueBatch != m_enqueueBatch)
            {
                if (m_dataChangeBatches.Count > 0)
                {
                    m_dequeueBatch = m_dataChangeBatches[0];
                    m_dataChangeBatches.RemoveAt(0);

                    // Request a restore for the next batch if there is one
                    if (m_dataChangeBatches.Count > 0)
                    {
                        m_batchPersistor.RequestBatchRestore(m_dataChangeBatches[0]);
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
        public DataValue PeekOldestValue()
        {
            // check for empty queue.
            if (ItemsInQueue == 0)
            {
                return null;
            }

            return m_dequeueBatch.Values[0].Item1;
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
                Disposed?.Invoke(this, EventArgs.Empty);
            }
        }

        private DataChangeBatch m_enqueueBatch;
        private readonly List<DataChangeBatch> m_dataChangeBatches = [];
        private DataChangeBatch m_dequeueBatch;
        private bool m_queueErrors;
        private readonly IBatchPersistor m_batchPersistor;
    }

    /// <summary>
    /// Batch of Datachanges and corresponding errors
    /// </summary>
    public class DataChangeBatch : BatchBase
    {
        public DataChangeBatch(
            List<(DataValue, ServiceResult)> values,
            uint batchSize,
            uint monitoredItemId)
            : base(batchSize, monitoredItemId)
        {
            Values = values;
        }

        public List<(DataValue, ServiceResult)> Values { get; set; }

        public override void SetPersisted()
        {
            Values = null;
            IsPersisted = true;
            PersistingInProgress = false;
        }

        public void Restore(List<(DataValue, ServiceResult)> values)
        {
            Values = values;
            IsPersisted = false;
            RestoreInProgress = false;
        }
    }

    public class StorableDataChangeQueue
    {
        public bool IsDurable { get; set; }

        public uint MonitoredItemId { get; set; }

        public int ItemsInQueue { get; set; }

        public uint QueueSize { get; set; }

        public DataChangeBatch EnqueueBatch { get; set; }

        public List<DataChangeBatch> DataChangeBatches { get; set; }

        public DataChangeBatch DequeueBatch { get; set; }
    }
}
