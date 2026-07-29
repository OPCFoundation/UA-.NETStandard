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
        internal RequestManagerLifecycleExtension(RequestManager requestManager)
        {
            m_requestManager = requestManager ??
                throw new ArgumentNullException(nameof(requestManager));
        }

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

        internal void CloseAdmissionLocked()
        {
            m_admissionClosed = true;
        }

        internal void DisposeLocked()
        {
            m_admissionClosed = true;
            m_lifecycleWaiters.Clear();
        }

        internal void ValidateValidationAdmissionLocked()
        {
            if (m_admissionClosed)
            {
                throw new ServiceResultException(StatusCodes.BadServerHalted);
            }
        }

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

        internal bool ShouldExcludeRequestLocked(uint requestId)
        {
            return m_lifecycleWaiters.TryGetValue(
                    requestId,
                    out LifecycleWaiterState? waiterState) &&
                waiterState.WaitingCount > 0;
        }

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
            public RequestLifecycleWaiterScope(
                RequestManagerLifecycleExtension extension,
                uint requestId)
            {
                m_extension = extension;
                m_requestId = requestId;
            }

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

        private sealed class LifecycleWaiterState
        {
            public int RegisteredCount { get; set; } = 1;

            public int WaitingCount { get; set; }
        }
    }
}
