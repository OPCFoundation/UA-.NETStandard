/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Threading;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Counts the monitored items a create or delete call actually added to or removed
    /// from a subscription. The subscription updates it under its lock in the same step
    /// that changes <see cref="ISubscription.MonitoredItemCount"/>, so a reader can tell
    /// which items of an in-flight call are already part of the subscription.
    /// </summary>
    internal sealed class MonitoredItemCountChange
    {
        /// <summary>
        /// The net number of items added (positive) or removed (negative) so far.
        /// </summary>
        public int Count => Volatile.Read(ref m_count);

        /// <summary>
        /// Records that one item was added to the subscription.
        /// </summary>
        public void Increment()
        {
            Interlocked.Increment(ref m_count);
        }

        /// <summary>
        /// Records that one item was removed from the subscription.
        /// </summary>
        public void Decrement()
        {
            Interlocked.Decrement(ref m_count);
        }

        private int m_count;
    }
}
