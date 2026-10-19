/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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