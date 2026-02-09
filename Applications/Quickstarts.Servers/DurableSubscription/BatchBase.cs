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

using System.Threading;
using Opc.Ua;

namespace Quickstarts.Servers
{
    /// <summary>
    /// Base class for a batch of data
    /// </summary>
    public abstract class BatchBase
    {
        protected BatchBase(uint batchSize, uint monitoredItemId)
        {
            BatchSize = batchSize;
            Id = Uuid.NewUuid();
            IsPersisted = false;
            MonitoredItemId = monitoredItemId;
        }

        /// <summary>
        /// The unique Id of the batch
        /// </summary>
        public Uuid Id { get; }

        /// <summary>
        /// The number of values in the batch
        /// </summary>
        public uint BatchSize { get; }

        /// <summary>
        /// The Id of the Monitored Item owning the batch
        /// </summary>
        public uint MonitoredItemId { get; }

        /// <summary>
        /// The batch has been persisted to disk
        /// </summary>
        public bool IsPersisted { get; protected set; }

        /// <summary>
        /// Restore is currently in progress in a background thread
        /// </summary>
        public bool RestoreInProgress { get; set; }

        /// <summary>
        /// Peristing is currently in progress in a background thread
        /// </summary>
        public bool PersistingInProgress { get; set; }

        /// <summary>
        /// Marks the batch as persisted and removes the data from memory
        /// </summary>
        public abstract void SetPersisted();

        /// <summary>
        /// Cancel this token to stop the persisting of the batch
        /// </summary>
        public CancellationTokenSource CancelBatchPersist { get; set; }
    }
}
