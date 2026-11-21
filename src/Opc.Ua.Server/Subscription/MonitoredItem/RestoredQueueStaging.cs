// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Marks the restore of durable subscriptions that stages the storage writes of
    /// the restored monitored item queues (<see cref="IRestorableMonitoredItemQueue"/>).
    /// Only <see cref="SubscriptionManager.RestoreSubscriptionsAsync"/> requests it,
    /// because it completes or aborts the staged queues; a monitored item restored
    /// outside of it keeps writing its queue to storage as before.
    /// </summary>
    internal static class RestoredQueueStaging
    {
        /// <summary>
        /// Whether the monitored items restored in the current asynchronous flow stage
        /// their queues.
        /// </summary>
        public static bool IsRequested => s_current.Value is { IsActive: true };

        /// <summary>
        /// Requests staging for the monitored items restored in the current
        /// asynchronous flow until the returned scope is disposed.
        /// </summary>
        public static IDisposable Begin()
        {
            var scope = new Scope();
            s_current.Value = scope;
            return scope;
        }

        private sealed class Scope : IDisposable
        {
            public bool IsActive => Volatile.Read(ref m_ended) == 0;

            public void Dispose()
            {
                // Work the restore started and that outlives it, e.g. a task a node
                // manager started, captured the scope; ending it stops staging there too.
                Volatile.Write(ref m_ended, 1);
                if (ReferenceEquals(s_current.Value, this))
                {
                    s_current.Value = null;
                }
            }

            private int m_ended;
        }

        private static readonly AsyncLocal<Scope?> s_current = new();
    }
}
