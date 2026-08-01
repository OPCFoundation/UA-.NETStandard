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
    /// Optional request lifecycle coordination used by server shutdown and NodeManager lifecycle
    /// operations.
    /// </summary>
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
        /// Gets whether a drain that just reached an idle snapshot must repeat because admission
        /// is closed and another waiter may have stopped dispatching.
        /// </summary>
        internal bool RepeatDrainUntilIdleLocked => m_admissionClosed;

        /// <summary>
        /// Atomically prevents admission of new Client requests. Validation scopes admitted
        /// before this call remain tracked and may still register and execute their request.
        /// </summary>
        internal void CloseAdmission()
        {
            m_requestManager.CloseAdmission(this);
        }

        /// <summary>
        /// Registers the current request for an opted-in NodeManager lifecycle operation.
        /// The returned scope marks the request as non-dispatching only after its lifecycle
        /// semaphore wait has been queued.
        /// </summary>
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
        /// Closes admission while the request manager lock is held.
        /// </summary>
        internal void CloseAdmissionLocked()
        {
            m_admissionClosed = true;
        }

        /// <summary>
        /// Marks lifecycle coordination disposed and forgets every waiter while the request
        /// manager lock is held.
        /// </summary>
        internal void DisposeLocked()
        {
            m_admissionClosed = true;
            m_lifecycleWaiters.Clear();
        }

        /// <summary>
        /// Rejects a new validation scope after admission has closed.
        /// </summary>
        /// <exception cref="ServiceResultException">Request admission has already closed.</exception>
        internal void ValidateValidationAdmissionLocked()
        {
            if (m_admissionClosed)
            {
                throw new ServiceResultException(StatusCodes.BadServerHalted);
            }
        }

        /// <summary>
        /// Rejects a request unless it belongs to a validation scope admitted before admission
        /// closed.
        /// </summary>
        /// <param name="validationId">The validation scope that admitted the request.</param>
        /// <param name="activeValidationScopes">The validation scopes still allowed to register requests.</param>
        /// <exception cref="ServiceResultException">The request was not admitted before lifecycle shutdown.</exception>
        internal void ValidateRequestAdmissionLocked(
            long? validationId,
            HashSet<long> activeValidationScopes)
        {
            if (m_admissionClosed &&
                (!validationId.HasValue ||
                    !activeValidationScopes.Contains(validationId.Value)))
            {
                throw new ServiceResultException(StatusCodes.BadServerHalted);
            }
        }

        /// <summary>
        /// Registers an executing request as a lifecycle waiter while the request manager lock is
        /// held.
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

            if (m_lifecycleWaiters.TryGetValue(
                requestId,
                out LifecycleWaiterState? state))
            {
                state.RegisteredCount++;
            }
            else
            {
                m_lifecycleWaiters.Add(requestId, new LifecycleWaiterState());
            }
        }

        /// <summary>
        /// Determines whether a request should be excluded from an in-progress drain because it
        /// has already started waiting on the lifecycle semaphore.
        /// </summary>
        /// <param name="requestId">The request to test against the waiter table.</param>
        /// <returns><c>true</c> when the request is a lifecycle waiter that no longer dispatches.</returns>
        internal bool ShouldExcludeRequestLocked(uint requestId)
        {
            return m_lifecycleWaiters.TryGetValue(
                    requestId,
                    out LifecycleWaiterState? waiterState) &&
                waiterState.WaitingCount > 0;
        }

        /// <summary>
        /// Marks a registered waiter as blocked on the lifecycle semaphore and removes it from
        /// pending drains.
        /// </summary>
        /// <param name="requestId">The request whose lifecycle wait has started.</param>
        /// <param name="requestDrains">The active drain snapshots to update.</param>
        /// <exception cref="InvalidOperationException">The request was not registered as a waiter.</exception>
        internal void MarkWaiterWaitingLocked(
            uint requestId,
            List<RequestManager.RequestDrain> requestDrains)
        {
            if (!m_lifecycleWaiters.TryGetValue(
                requestId,
                out LifecycleWaiterState? state))
            {
                throw new InvalidOperationException(
                    "The lifecycle waiter is no longer registered.");
            }

            state.WaitingCount++;
            if (state.WaitingCount == 1)
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
            if (!m_lifecycleWaiters.TryGetValue(
                requestId,
                out LifecycleWaiterState? state))
            {
                return;
            }

            if (waiting)
            {
                state.WaitingCount--;
            }
            state.RegisteredCount--;
            if (state.RegisteredCount == 0)
            {
                m_lifecycleWaiters.Remove(requestId);
            }
        }

        private readonly RequestManager m_requestManager;
        private readonly Dictionary<uint, LifecycleWaiterState> m_lifecycleWaiters = [];
        private bool m_admissionClosed;

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
            public int RegisteredCount { get; set; } = 1;

            /// <summary>
            /// Gets or sets the number of registered scopes that have started waiting.
            /// </summary>
            public int WaitingCount { get; set; }
        }
    }
}
