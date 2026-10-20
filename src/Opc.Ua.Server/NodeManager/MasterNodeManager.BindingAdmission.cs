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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    public partial class MasterNodeManager
    {
        /// <summary>
        /// Defers resuming binding admission of the current scope until the returned suspension is disposed.
        /// </summary>
        /// <param name="context">The operation context of the admitted request. A null or unadmitted context
        /// has nothing to defer.</param>
        /// <returns>The suspension to dispose, or an empty one if no admission is active.</returns>
        internal BindingAdmissionSuspension DeferBindingAdmissionResumption(IOperationContext? context)
        {
            return FindBindingAdmission(context)?.DeferResumption() ?? BindingAdmissionSuspension.Empty;
        }

        /// <summary>
        /// Releases the binding admission of the specified request. A null context, or one that is not admitted,
        /// is an ordinary external caller and suspends nothing.
        /// </summary>
        /// <param name="context">The operation context of the admitted request, or null.</param>
        /// <returns>The suspension that restores admission, or an empty one if no admission is active.</returns>
        internal BindingAdmissionSuspension SuspendBindingAdmission(IOperationContext? context)
        {
            return FindBindingAdmission(context)?.Suspend() ?? BindingAdmissionSuspension.Empty;
        }

        /// <summary>
        /// Checks whether the admitted request is removing the monitored item.
        /// </summary>
        /// <param name="context">The operation context of the admitted request, or null.</param>
        /// <param name="monitoredItem">The monitored item.</param>
        /// <returns>True if the item is being removed.</returns>
        internal bool IsRemovingBinding(IOperationContext? context, IMonitoredItem monitoredItem)
        {
            return FindBindingAdmission(context)?.IsRemoving(monitoredItem) == true;
        }

        /// <summary>
        /// Enters a binding admission scope for the specified request.
        /// </summary>
        /// <param name="context">The operation context of the admitted request, or null for a scope without one.
        /// </param>
        /// <param name="removingItems">The monitored items that are being removed in this scope.</param>
        /// <returns>The scope to dispose when the admission ends.</returns>
        private BindingAdmission EnterBindingAdmission(
            IOperationContext? context,
            ArrayOf<IMonitoredItem> removingItems = default)
        {
            var admission = new BindingAdmission(this, FindBindingAdmission(context), context, removingItems);
            if (context is not null)
            {
                lock (m_contextAdmissionLock)
                {
                    m_contextAdmissions.Remove(context);
                    m_contextAdmissions.Add(context, admission);
                }
            }
            return admission;
        }

        /// <summary>
        /// Finds the admission captured for the context. A null context, or one that was never admitted, never
        /// resolves an admission.
        /// </summary>
        /// <param name="context">The operation context, or null.</param>
        /// <returns>The admission, or null if none is active.</returns>
        private BindingAdmission? FindBindingAdmission(IOperationContext? context)
        {
            if (context is null)
            {
                return null;
            }
            lock (m_contextAdmissionLock)
            {
                return m_contextAdmissions.TryGetValue(context, out BindingAdmission? captured) ? captured : null;
            }
        }

        /// <summary>
        /// Restores the enclosing admission for a context when a scope ends.
        /// </summary>
        /// <param name="context">The operation context the scope was captured for.</param>
        /// <param name="ending">The scope that ends.</param>
        /// <param name="previous">The enclosing scope, or null.</param>
        private void ReleaseContextAdmission(
            IOperationContext context,
            BindingAdmission ending,
            BindingAdmission? previous)
        {
            lock (m_contextAdmissionLock)
            {
                if (m_contextAdmissions.TryGetValue(context, out BindingAdmission? current) &&
                    ReferenceEquals(current, ending))
                {
                    m_contextAdmissions.Remove(context);
                    if (previous is not null)
                    {
                        m_contextAdmissions.Add(context, previous);
                    }
                }
            }
        }

        private readonly ConditionalWeakTable<IOperationContext, BindingAdmission> m_contextAdmissions = new();
        private readonly Lock m_contextAdmissionLock = new();

        /// <summary>
        /// Holds the binding admission semaphore for a scope and allows temporarily suspending it.
        /// </summary>
        /// <param name="owner">The master node manager.</param>
        /// <param name="previous">The enclosing admission of the same context chain, or null.</param>
        /// <param name="context">The operation context the scope is captured for, or null.</param>
        /// <param name="removingItems">The monitored items that are being removed.</param>
        private sealed class BindingAdmission(
            MasterNodeManager owner,
            BindingAdmission? previous,
            IOperationContext? context,
            ArrayOf<IMonitoredItem> removingItems) : IDisposable
        {
            /// <summary>
            /// Determines whether a monitored item is being removed in this or an enclosing scope.
            /// </summary>
            /// <param name="monitoredItem">The monitored item.</param>
            /// <returns>True if the item is being removed.</returns>
            public bool IsRemoving(IMonitoredItem monitoredItem)
            {
                lock (m_lock)
                {
                    if (!m_disposed)
                    {
                        foreach (IMonitoredItem removing in removingItems)
                        {
                            if (ReferenceEquals(removing, monitoredItem))
                            {
                                return true;
                            }
                        }
                    }
                }
                return previous?.IsRemoving(monitoredItem) == true;
            }

            /// <summary>
            /// Defers restoring admission after a notification until the returned suspension is disposed.
            /// </summary>
            /// <returns>The suspension to dispose, or an empty one if the scope ended.</returns>
            public BindingAdmissionSuspension DeferResumption()
            {
                lock (m_lock)
                {
                    if (m_disposed)
                    {
                        return BindingAdmissionSuspension.Empty;
                    }
                    m_notificationDeferrals++;
                    return new BindingAdmissionSuspension(CompleteNotificationAsync);
                }
            }

            /// <summary>
            /// Releases the binding semaphore until the returned suspension is disposed.
            /// </summary>
            /// <returns>The suspension that restores admission, or an empty one if the scope ended.</returns>
            public BindingAdmissionSuspension Suspend()
            {
                lock (m_lock)
                {
                    if (m_disposed)
                    {
                        return BindingAdmissionSuspension.Empty;
                    }
                    m_suspensions++;
                    if (m_held)
                    {
                        m_held = false;
                        owner.m_bindingSemaphore.Release();
                    }
                    return new BindingAdmissionSuspension(ResumeAsync);
                }
            }

            /// <inheritdoc/>
            public void Dispose()
            {
                lock (m_lock)
                {
                    if (m_disposed)
                    {
                        return;
                    }
                    m_disposed = true;
                    if (context is not null)
                    {
                        owner.ReleaseContextAdmission(context, this, previous);
                    }
                    if (m_held)
                    {
                        m_held = false;
                        owner.m_bindingSemaphore.Release();
                    }
                }
            }

            private ValueTask CompleteNotificationAsync()
            {
                lock (m_lock)
                {
                    m_notificationDeferrals--;
                }
                return RestoreAdmissionAsync();
            }

            private ValueTask ResumeAsync()
            {
                lock (m_lock)
                {
                    m_suspensions--;
                }
                return RestoreAdmissionAsync();
            }

            private async ValueTask RestoreAdmissionAsync()
            {
                TaskCompletionSource<bool>? resumption = null;
                Task<bool> resumed;
                lock (m_lock)
                {
                    if (m_disposed || m_held || m_suspensions != 0 || m_notificationDeferrals != 0)
                    {
                        return;
                    }
                    if (m_resuming is null)
                    {
                        resumption = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                        m_resuming = resumption;
                    }
                    resumed = m_resuming.Task;
                }
                if (resumption is not null)
                {
                    try
                    {
                        await owner.m_bindingSemaphore.WaitAsync(CancellationToken.None).ConfigureAwait(false);
                        lock (m_lock)
                        {
                            if (m_disposed ||
                                Volatile.Read(ref m_suspensions) != 0 ||
                                Volatile.Read(ref m_notificationDeferrals) != 0)
                            {
                                owner.m_bindingSemaphore.Release();
                            }
                            else
                            {
                                m_held = true;
                            }
                            m_resuming = null;
                        }
                        resumption.TrySetResult(true);
                    }
                    catch (Exception failure) when (failure is not OutOfMemoryException)
                    {
                        lock (m_lock)
                        {
                            m_resuming = null;
                        }
                        resumption.TrySetException(failure);
                    }
                }
                await resumed.ConfigureAwait(false);
            }

            private readonly Lock m_lock = new();
            private TaskCompletionSource<bool>? m_resuming;
            private int m_suspensions;
            private int m_notificationDeferrals;
            private bool m_held = true;
            private bool m_disposed;
        }
    }
}
