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
using System.Collections.Generic;
using System.Threading;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Optional request lifecycle coordination used by NodeManager lifecycle operations.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="RequestManager"/> owns ordinary request tracking. This extension adds the
    /// lifecycle state that only NodeManager replacement needs: the set of executing requests
    /// that have stopped dispatching because they are waiting for the lifecycle semaphore.
    /// </para>
    /// <para>
    /// The transitions are: registered lifecycle waiter -> semaphore waiter, and semaphore
    /// waiter -> unregistered. A NodeManager lifecycle drain depends on those transitions so it
    /// can drain the requests that are in flight without waiting on a request that is itself
    /// queued behind the lifecycle semaphore.
    /// </para>
    /// </remarks>
    internal sealed class RequestManagerLifecycleExtension
    {
        /// <summary>
        /// Creates coordination state for the request manager that owns the request table lock.
        /// </summary>
        /// <param name="requestManager">The request manager whose lock protects this extension.</param>
        /// <exception cref="ArgumentNullException"><paramref name="requestManager"/> is <c>null</c>.</exception>
        internal RequestManagerLifecycleExtension(RequestManager requestManager)
        {
            m_requestManager = requestManager ??
                throw new ArgumentNullException(nameof(requestManager));
        }

        /// <summary>
        /// Registers the current request for an opted-in NodeManager lifecycle operation.
        /// The returned scope marks the request as non-dispatching only after its lifecycle
        /// semaphore wait has been queued.
        /// </summary>
        /// <remarks>
        /// This prevents a self-wait during shutdown: a service request that initiates or joins
        /// shutdown stops dispatching to NodeManagers before it waits for the shared lifecycle
        /// semaphore, so the request drain can exclude it safely.
        /// </remarks>
        /// <returns>A scope that unregisters the request when the lifecycle operation exits.</returns>
        internal RequestLifecycleWaiterScope EnterLifecycleWaiter()
        {
            uint? currentRequestId = m_requestManager.GetCurrentRequestIdForLifecycleExtension();
            if (!currentRequestId.HasValue ||
                currentRequestId.Value == uint.MaxValue)
            {
                throw new InvalidOperationException(
                    "A lifecycle waiter can only be registered by an executing request.");
            }

            uint requestId = currentRequestId.Value;
            m_requestManager.EnterLifecycleWaiter(this, requestId);
            return new RequestLifecycleWaiterScope(this, requestId);
        }

        /// <summary>
        /// Marks lifecycle coordination disposed and forgets every waiter while the request
        /// manager lock is held.
        /// </summary>
        internal void DisposeLocked()
        {
            m_requestsWaitingForLifecycle.Clear();
        }

        /// <summary>
        /// Records that an executing request entered lifecycle coordination while the request
        /// manager lock is held.
        /// </summary>
        /// <param name="requestId">The request that will wait for lifecycle serialization.</param>
        /// <param name="requests">The active request table used to prove the request is still executing.</param>
        /// <exception cref="InvalidOperationException">The request is no longer active.</exception>
        internal void EnterWaiterLocked(
            uint requestId,
            IReadOnlyDictionary<uint, OperationContext> requests)
        {
            if (!requests.ContainsKey(requestId))
            {
                throw new InvalidOperationException(
                    "The lifecycle waiter request is no longer active.");
            }

            if (m_requestsWaitingForLifecycle.TryGetValue(
                requestId,
                out LifecycleWaiterState? state))
            {
                state.ScopeCount++;
            }
            else
            {
                m_requestsWaitingForLifecycle.Add(requestId, new LifecycleWaiterState());
            }
        }

        /// <summary>
        /// Determines whether a request should be excluded from an in-progress drain.
        /// </summary>
        /// <remarks>
        /// Only a registered request that has also called
        /// <see cref="RequestLifecycleWaiterScope.MarkSemaphoreWaitStarted"/> is excluded.
        /// Registration alone is not enough because the request may still be dispatching to a
        /// NodeManager until its semaphore wait is actually queued.
        /// </remarks>
        /// <param name="requestId">The request to test against the waiter table.</param>
        /// <returns><c>true</c> when the request is a lifecycle waiter that no longer dispatches.</returns>
        internal bool ShouldExcludeRequestLocked(uint requestId)
        {
            return m_requestsWaitingForLifecycle.TryGetValue(
                    requestId,
                    out LifecycleWaiterState? waiterState) &&
                waiterState.SemaphoreWaitCount > 0;
        }

        /// <summary>
        /// Moves a registered lifecycle waiter into the semaphore-waiting state and removes it
        /// from pending drain snapshots.
        /// </summary>
        /// <param name="requestId">The request whose lifecycle wait has started.</param>
        /// <param name="requestDrains">The active drain snapshots to update.</param>
        /// <exception cref="InvalidOperationException">The request was not registered as a waiter.</exception>
        internal void MarkWaiterWaitingLocked(
            uint requestId,
            List<RequestManager.RequestDrain> requestDrains)
        {
            if (!m_requestsWaitingForLifecycle.TryGetValue(
                requestId,
                out LifecycleWaiterState? state))
            {
                throw new InvalidOperationException(
                    "The lifecycle waiter is no longer registered.");
            }

            state.SemaphoreWaitCount++;
            if (state.SemaphoreWaitCount == 1)
            {
                for (int ii = requestDrains.Count - 1; ii >= 0; ii--)
                {
                    if (requestDrains[ii].Exclude(requestId))
                    {
                        requestDrains.RemoveAt(ii);
                    }
                }
            }
        }

        /// <summary>
        /// Unregisters a lifecycle waiter and removes its state when the last nested scope exits.
        /// </summary>
        /// <param name="requestId">The request that is leaving lifecycle coordination.</param>
        /// <param name="waiting">Whether the scope had started waiting on the lifecycle semaphore.</param>
        internal void ExitWaiterLocked(uint requestId, bool waiting)
        {
            if (!m_requestsWaitingForLifecycle.TryGetValue(
                requestId,
                out LifecycleWaiterState? state))
            {
                return;
            }

            if (waiting)
            {
                state.SemaphoreWaitCount--;
            }
            state.ScopeCount--;
            if (state.ScopeCount == 0)
            {
                m_requestsWaitingForLifecycle.Remove(requestId);
            }
        }

        private readonly RequestManager m_requestManager;
        private readonly Dictionary<uint, LifecycleWaiterState> m_requestsWaitingForLifecycle = [];

        /// <summary>
        /// Unregisters one lifecycle-waiting request.
        /// </summary>
        internal sealed class RequestLifecycleWaiterScope : IDisposable
        {
            /// <summary>
            /// Creates a scope that unregisters the request from lifecycle coordination.
            /// </summary>
            /// <param name="extension">The extension that owns the waiter state.</param>
            /// <param name="requestId">The request registered by the scope.</param>
            public RequestLifecycleWaiterScope(
                RequestManagerLifecycleExtension extension,
                uint requestId)
            {
                m_extension = extension;
                m_requestId = requestId;
            }

            /// <summary>
            /// Records the point where the request has stopped dispatching and is waiting on the
            /// lifecycle semaphore.
            /// </summary>
            /// <exception cref="ObjectDisposedException">The scope has already been disposed.</exception>
            internal void MarkSemaphoreWaitStarted()
            {
                lock (m_lock)
                {
                    if (m_extension is null)
                    {
                        throw new ObjectDisposedException(
                            nameof(RequestLifecycleWaiterScope));
                    }
                    if (!m_waiting)
                    {
                        m_extension.MarkLifecycleWaiterWaiting(m_requestId);
                        m_waiting = true;
                    }
                }
            }

            /// <summary>
            /// Leaves lifecycle coordination and re-includes the request in future drains when it
            /// had started waiting.
            /// </summary>
            public void Dispose()
            {
                RequestManagerLifecycleExtension? extension;
                bool waiting;
                lock (m_lock)
                {
                    extension = m_extension;
                    m_extension = null;
                    waiting = m_waiting;
                }
                extension?.ExitLifecycleWaiter(m_requestId, waiting);
            }

            private RequestManagerLifecycleExtension? m_extension;
            private readonly uint m_requestId;
            private readonly Lock m_lock = new();
            private bool m_waiting;
        }

        private void MarkLifecycleWaiterWaiting(uint requestId)
        {
            m_requestManager.MarkLifecycleWaiterWaiting(this, requestId);
        }

        private void ExitLifecycleWaiter(uint requestId, bool waiting)
        {
            m_requestManager.ExitLifecycleWaiter(this, requestId, waiting);
        }

        /// <summary>
        /// Counts the nested lifecycle waiter scopes for a single request. The request is excluded
        /// from drains while at least one scope has started waiting.
        /// </summary>
        private sealed class LifecycleWaiterState
        {
            /// <summary>
            /// Gets or sets the number of active scopes registered for the request.
            /// </summary>
            public int ScopeCount { get; set; } = 1;

            /// <summary>
            /// Gets or sets the number of registered scopes that have started waiting.
            /// </summary>
            public int SemaphoreWaitCount { get; set; }
        }
    }
}
