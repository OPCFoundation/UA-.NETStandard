/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
        public bool IsPersisted
        {
            get => Volatile.Read(ref m_isPersisted);
            protected set => Volatile.Write(ref m_isPersisted, value);
        }

        /// <summary>
        /// Restore is currently in progress in a background thread
        /// </summary>
        public bool RestoreInProgress { get; set; }

        /// <summary>
        /// Peristing is currently in progress in a background thread
        /// </summary>
        public bool PersistingInProgress
        {
            get => Volatile.Read(ref m_persistingInProgress);
            set => Volatile.Write(ref m_persistingInProgress, value);
        }

        /// <summary>
        /// Marks the batch as persisted and removes the data from memory
        /// </summary>
        public abstract void SetPersisted();

        /// <summary>
        /// Cancel this token to stop the persisting of the batch
        /// </summary>
        public CancellationTokenSource? CancelBatchPersist { get; set; }

        /// <summary>
        /// Records whether the batch data has been written to disk and released from memory.
        /// </summary>
        private bool m_isPersisted;

        /// <summary>
        /// Records whether a background persistence operation currently owns the batch.
        /// </summary>
        private bool m_persistingInProgress;
    }
}
