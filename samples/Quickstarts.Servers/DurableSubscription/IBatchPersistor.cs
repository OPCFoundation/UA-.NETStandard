// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Collections.Generic;

namespace Quickstarts.Servers
{
    /// <summary>
    /// Persists batches of queue values to disk
    /// </summary>
    public interface IBatchPersistor
    {
        /// <summary>
        /// Request that a batch shall be persisted in a background thread
        /// </summary>
        void RequestBatchPersist(BatchBase batch);

        /// <summary>
        /// Persist a batch in the main thread
        /// </summary>
        void PersistSynchronously(BatchBase batch);

        /// <summary>
        /// Request that a batch shall be restored in a background thread
        /// </summary>
        void RequestBatchRestore(BatchBase batch);

        /// <summary>
        /// Restore a batch in the main thread
        /// </summary>
        void RestoreSynchronously(BatchBase batch);

        /// <summary>
        /// Delete all batches from disk for a monitored item
        /// </summary>
        /// <param name="batchesToKeep">MonitoredItemIds of the batches to keep on disk</param>
        void DeleteBatches(IEnumerable<uint> batchesToKeep);

        /// <summary>
        /// Delete a single batch from disk
        /// </summary>
        /// <param name="batchToRemove">The Batch to remove</param>
        void DeleteBatch(BatchBase batchToRemove);
    }
}
