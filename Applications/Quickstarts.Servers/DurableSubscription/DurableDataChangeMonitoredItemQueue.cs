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
        public DurableDataChangeMonitoredItemQueue(bool createDurable, uint monitoredItemId, IBatchPersistor batchPersistor)
        {
            IsDurable = createDurable;
            m_monitoredItemId = monitoredItemId;
            m_batchPersistor = batchPersistor;
            m_enqueueBatch = new DataChangeBatch(new List<(DataValue, ServiceResult)>(), kBatchSize, monitoredItemId);
            m_dequeueBatch = m_enqueueBatch;
            m_queueSize = 0;
            m_itemsInQueue = 0;
        }
        /// <summary>
        /// Creates a queue from a template
        /// </summary>
        public DurableDataChangeMonitoredItemQueue(StorableDataChangeQueue queue, IBatchPersistor batchPersistor)
        {
            m_batchPersistor = batchPersistor;
            m_monitoredItemId = queue.MonitoredItemId;
            IsDurable = queue.IsDurable;
            m_enqueueBatch = queue.EnqueueBatch;
            m_dequeueBatch = queue.DequeueBatch;
            m_dataChangeBatches = queue.DataChangeBatches;
            m_queueSize = queue.QueueSize;
            m_itemsInQueue = queue.ItemsInQueue;
        }

        #region Public Methods

        /// <inheritdoc/>
        public uint MonitoredItemId => m_monitoredItemId;

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
                return m_itemsInQueue;
            }
        }
        /// <summary>
        /// Brings the queue with content into a storable format
        /// </summary>
        /// <returns></returns>
        public StorableDataChangeQueue ToStorableQueue()
        {

            return new StorableDataChangeQueue {
                IsDurable = IsDurable,
                MonitoredItemId = MonitoredItemId,
                QueueSize = m_queueSize,
                ItemsInQueue = m_itemsInQueue,
                DataChangeBatches = m_dataChangeBatches,
                DequeueBatch = m_dequeueBatch,
                EnqueueBatch = m_enqueueBatch,
            };
        }

        /// <inheritdoc/>
        public bool IsDurable { get; }

        /// <summary>
        /// Adds the value to the queue.
        /// </summary>
        /// <param name="value">The value to queue.</param>
        /// <param name="error">The error to queue.</param>
        public void Enqueue(DataValue value, ServiceResult error)
        {
            if (QueueSize == 0)
            {
                throw new InvalidOperationException("Cannot enqueue Value. Queue size not set.");
            }

            //check for full queue
            if (m_itemsInQueue == QueueSize)
            {
                _ = Dequeue(out _, out _);
            }

            m_enqueueBatch.Values.Add((value, m_queueErrors ? error : null));
            m_itemsInQueue++;
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
                    m_dequeueBatch = new DataChangeBatch(m_enqueueBatch.Values, kBatchSize, m_monitoredItemId);
                    m_enqueueBatch = new DataChangeBatch(new List<(DataValue, ServiceResult)>(), kBatchSize, m_monitoredItemId);
                }
                // persist the batch
                else
                {
                    Opc.Ua.Utils.LogDebug("Storing batch for monitored item {0}", m_monitoredItemId);

                    var batchToStore = new DataChangeBatch(m_enqueueBatch.Values, kBatchSize, m_monitoredItemId);
                    m_dataChangeBatches.Add(batchToStore);
                    if (m_dataChangeBatches.Count > 1)
                    {
                        m_batchPersistor.RequestBatchPersist(m_dataChangeBatches[m_dataChangeBatches.Count - 2]);
                    }

                    m_enqueueBatch = new DataChangeBatch(new List<(DataValue, ServiceResult)>(), kBatchSize, m_monitoredItemId);
                }
            }

        }
        /// <inheritdoc/>
        public void OverwriteLastValue(DataValue value, ServiceResult error)
        {
            if (m_itemsInQueue == 0)
            {
                throw new InvalidOperationException("Cannot overwrite Value. Queue is empty.");
            }
            if (m_enqueueBatch.Values.Count > 0)
            {
                m_enqueueBatch.Values[m_enqueueBatch.Values.Count - 1] = (value, error);
            }
            else if (m_dataChangeBatches.Count > 0)
            {
                DataChangeBatch batch = m_dataChangeBatches.Last();
                batch.Values[batch.Values.Count - 1] = (value, error);
            }
            else
            {
                m_dequeueBatch.Values[m_dequeueBatch.Values.Count - 1] = (value, error);
            }
        }

        /// <inheritdoc/>
        public void ResetQueue(uint queueSize, bool queueErrors)
        {
            m_enqueueBatch = new DataChangeBatch(new List<(DataValue, ServiceResult)>(), kBatchSize, MonitoredItemId);
            m_dequeueBatch = m_enqueueBatch;
            m_itemsInQueue = 0;
            m_queueErrors = queueErrors;
            m_queueSize = queueSize;

            foreach (DataChangeBatch batch in m_dataChangeBatches)
            {
                m_batchPersistor.DeleteBatch(batch);
            }

            m_dataChangeBatches.Clear();
        }

        /// <inheritdoc/>
        public DataValue PeekLastValue()
        {
            if (m_itemsInQueue == 0)
            {
                return null;
            }

            if (m_enqueueBatch.Values.Count > 0)
            {
                return m_enqueueBatch.Values[m_enqueueBatch.Values.Count - 1].Item1;
            }
            else if (m_dataChangeBatches.Count > 0)
            {
                DataChangeBatch batch = m_dataChangeBatches.Last();
                return batch.Values[batch.Values.Count - 1].Item1;
            }
            else
            {
                return m_dequeueBatch.Values[m_dequeueBatch.Values.Count - 1].Item1;
            }
        }

        /// <inheritdoc/>
        public bool Dequeue(out DataValue value, out ServiceResult error)
        {
            value = null;
            error = null;

            // check for empty queue.
            if (m_itemsInQueue == 0)
            {
                return false;
            }

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

            (value, error) = m_dequeueBatch.Values[0];
            m_dequeueBatch.Values.RemoveAt(0);
            m_itemsInQueue--;
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
                m_batchPersistor.RequestBatchRestore(m_dataChangeBatches.First());
            }

            // if the dequeue batch is empty and there are stored batches, set the dequeue batch to the first stored batch
            if (m_dequeueBatch.Values.Count == 0 && m_dequeueBatch != m_enqueueBatch)
            {
                if (m_dataChangeBatches.Count > 0)
                {
                    m_dequeueBatch = m_dataChangeBatches.First();
                    m_dataChangeBatches.RemoveAt(0);

                    // Request a restore for the next batch if there is one
                    if (m_dataChangeBatches.Count > 0)
                    {
                        m_batchPersistor.RequestBatchRestore(m_dataChangeBatches.First());
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
            if (m_itemsInQueue == 0)
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
                Disposed?.Invoke(this, new EventArgs());
            }
        }
        #endregion

        #region Private Fields
        private readonly uint m_monitoredItemId;
        private DataChangeBatch m_enqueueBatch;
        private readonly List<DataChangeBatch> m_dataChangeBatches = new List<DataChangeBatch>();
        private DataChangeBatch m_dequeueBatch;
        private int m_itemsInQueue;
        private uint m_queueSize;
        private bool m_queueErrors;
        private readonly IBatchPersistor m_batchPersistor;
        #endregion
    }
    /// <summary>
    /// Batch of Datachanges and corresponding errors
    /// </summary>
    public class DataChangeBatch : BatchBase
    {
        public DataChangeBatch(List<(DataValue, ServiceResult)> values, uint batchSize, uint monitoredItemId) : base(batchSize, monitoredItemId)
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
