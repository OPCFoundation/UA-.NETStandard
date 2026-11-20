// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.AutoID.Server
{
    /// <summary>
    /// Admits operations until it is closed and lets the closer wait until the admitted ones
    /// have finished.
    /// </summary>
    /// <remarks>
    /// Admission and the closing flag are decided under one lock, so an operation is either
    /// admitted before the close and awaited by it, or rejected: there is no window in which an
    /// operation is admitted after the close observed the count.
    /// </remarks>
    internal sealed class OperationTracker
    {
        /// <summary>
        /// Tries to admit an operation.
        /// </summary>
        /// <returns><see langword="false"/> when the tracker is closed.</returns>
        public bool TryEnter()
        {
            lock (m_lock)
            {
                if (m_closed)
                {
                    return false;
                }
                m_active++;
                return true;
            }
        }

        /// <summary>
        /// Ends an admitted operation.
        /// </summary>
        public void Exit()
        {
            TaskCompletionSource<bool>? drained = null;
            lock (m_lock)
            {
                m_active--;
                if (m_active == 0 && m_closed)
                {
                    drained = m_drained;
                }
            }
            drained?.TrySetResult(true);
        }

        /// <summary>
        /// Rejects further operations and completes when the admitted ones have ended.
        /// </summary>
        public Task CloseAsync()
        {
            lock (m_lock)
            {
                m_closed = true;
                if (m_active == 0)
                {
                    m_drained.TrySetResult(true);
                }
            }
            return m_drained.Task;
        }

        private readonly Lock m_lock = new();

        private readonly TaskCompletionSource<bool> m_drained =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private int m_active;
        private bool m_closed;
    }
}
