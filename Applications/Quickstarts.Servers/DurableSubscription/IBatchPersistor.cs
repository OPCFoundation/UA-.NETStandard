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
        /// <param name="batch"></param>
        void RequestBatchPersist(BatchBase batch);

        /// <summary>
        /// Persist a batch in the main thread
        /// </summary>
        /// <param name="batch"></param>
        void PersistSynchronously(BatchBase batch);

        /// <summary>
        /// Request that a batch shall be restored in a background thread
        /// </summary>
        /// <param name="batch"></param>
        void RequestBatchRestore(BatchBase batch);

        /// <summary>
        /// Restore a batch in the main thread
        /// </summary>
        /// <param name="batch"></param>
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
