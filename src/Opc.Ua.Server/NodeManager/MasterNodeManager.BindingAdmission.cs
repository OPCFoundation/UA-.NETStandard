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
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    public partial class MasterNodeManager
    {
        internal BindingAdmissionSuspension DeferBindingAdmissionResumption()
        {
            return m_currentBindingAdmission.Value?.DeferResumption() ?? BindingAdmissionSuspension.Empty;
        }

        private BindingAdmission EnterBindingAdmission(ArrayOf<IMonitoredItem> removingItems = default)
        {
            var admission = new BindingAdmission(this, m_currentBindingAdmission.Value, removingItems);
            m_currentBindingAdmission.Value = admission;
            return admission;
        }

        private readonly AsyncLocal<BindingAdmission?> m_currentBindingAdmission = new();

        private sealed class BindingAdmission(
            MasterNodeManager owner,
            BindingAdmission? previous,
            ArrayOf<IMonitoredItem> removingItems) : IDisposable
        {
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

            public void Dispose()
            {
                lock (m_lock)
                {
                    if (m_disposed)
                    {
                        return;
                    }
                    m_disposed = true;
                    owner.m_currentBindingAdmission.Value = previous;
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
