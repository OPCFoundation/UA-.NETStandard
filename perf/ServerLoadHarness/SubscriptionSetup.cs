// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.MonitoredItems;

namespace Opc.Ua.Perf.ServerLoadHarness
{
    /// <summary>
    /// Waits for one managed subscription and its monitored items to be ready for measurement.
    /// </summary>
    internal sealed class SubscriptionSetup
    {
        /// <summary>
        /// Creates a setup observer using the supplied clock or the system clock.
        /// </summary>
        internal SubscriptionSetup(TimeProvider? timeProvider = null)
        {
            m_timeProvider = timeProvider ?? TimeProvider.System;
        }

        /// <summary>
        /// Retains the first subscription failure, including failures reported before waiting starts.
        /// </summary>
        internal void OnStateChanged(SubscriptionState state)
        {
            if (state is SubscriptionState.Error or SubscriptionState.Deleted)
            {
                Interlocked.CompareExchange(ref m_failureState, (int)state, 0);
            }
        }

        /// <summary>
        /// Waits up to one minute for the subscription and all of its items to be created.
        /// </summary>
        /// <exception cref="ServiceResultException">A monitored item was rejected.</exception>
        /// <exception cref="InvalidOperationException">The subscription failed or was deleted.</exception>
        /// <exception cref="TimeoutException">Setup did not complete before the deadline.</exception>
        /// <exception cref="OperationCanceledException">The caller cancelled setup.</exception>
        internal async Task WaitForReadyAsync(ISubscription subscription, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            using var timeout = m_timeProvider.CreateCancellationTokenSource(TimeSpan.FromMinutes(1));
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
            try
            {
                while (true)
                {
                    stop.Token.ThrowIfCancellationRequested();
                    var state = (SubscriptionState)Volatile.Read(ref m_failureState);
                    if (state is SubscriptionState.Error or SubscriptionState.Deleted)
                    {
                        throw new InvalidOperationException(
                            $"Managed subscription setup failed with state {state}.");
                    }

                    bool ready = subscription.Created;
                    foreach (IMonitoredItem item in subscription.MonitoredItems.Items)
                    {
                        ServiceResult error = item.Error;
                        if (ServiceResult.IsBad(error))
                        {
                            throw new ServiceResultException(
                                error.StatusCode,
                                $"Monitored item '{item.Name}' failed during subscription setup: {error}");
                        }
                        ready &= item.Created;
                    }
                    if (ready)
                    {
                        return;
                    }

                    await m_timeProvider.Delay(TimeSpan.FromMilliseconds(50), stop.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException exception) when (timeout.IsCancellationRequested &&
                !ct.IsCancellationRequested)
            {
                throw new TimeoutException("Managed subscription setup did not complete within one minute.", exception);
            }
        }

        private readonly TimeProvider m_timeProvider;
        private int m_failureState;
    }
}
