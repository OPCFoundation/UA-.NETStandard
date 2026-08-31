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
using Opc.Ua.Security.Certificates;

namespace Opc.Ua
{
    /// <summary>
    /// A coalescing consumer of <see cref="CertificateChangeEvent"/>
    /// notifications: events accepted by the filter are folded into a single
    /// pending state under a lock, and one background task drains that state
    /// until no more work is pending. A burst of events therefore results in
    /// at most one processing pass over the folded state (plus one for
    /// events that arrive while a pass is running), never one pass per
    /// event.
    /// </summary>
    /// <typeparam name="TState">
    /// The folded pending state. A latest-wins fold reproduces a classic
    /// debounce; a set-union fold accumulates every distinct piece of work
    /// from the burst.
    /// </typeparam>
    /// <remarks>
    /// Observer callbacks (<c>OnNext</c>) never block: they only fold the
    /// event into the pending slot and start the pump task if none is
    /// running. Exceptions thrown by the processing callback are routed to
    /// the optional error callback and never tear down the pump.
    /// <see cref="Dispose"/> unsubscribes and discards the pending state; a
    /// processing pass already in flight is allowed to finish.
    /// </remarks>
    internal sealed class CertificateChangePump<TState> : IDisposable
        where TState : class
    {
        /// <summary>
        /// Initializes the pump.
        /// </summary>
        /// <param name="filter">
        /// Decides whether an incoming event is relevant; irrelevant events
        /// are dropped without touching the pending state.
        /// </param>
        /// <param name="accumulate">
        /// Folds an accepted event into the pending state. Receives
        /// <see langword="null"/> when no state is pending yet.
        /// </param>
        /// <param name="processAsync">
        /// Processes one folded pending state on the background pump task.
        /// </param>
        /// <param name="onProcessError">
        /// Invoked with any exception thrown by
        /// <paramref name="processAsync"/> (or a faulted filter/fold); the
        /// pump continues draining afterwards.
        /// </param>
        /// <param name="onPumpStateChanged">
        /// Invoked with the pump task when a drain starts and with
        /// <see langword="null"/> when it finishes, for hosts that must
        /// await in-flight work during shutdown.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// When <paramref name="filter"/>, <paramref name="accumulate"/> or
        /// <paramref name="processAsync"/> is <see langword="null"/>.
        /// </exception>
        public CertificateChangePump(
            Func<CertificateChangeEvent, bool> filter,
            Func<TState?, CertificateChangeEvent, TState> accumulate,
            Func<TState, CancellationToken, ValueTask> processAsync,
            Action<Exception>? onProcessError = null,
            Action<Task?>? onPumpStateChanged = null)
        {
            m_filter = filter ?? throw new ArgumentNullException(nameof(filter));
            m_accumulate = accumulate ?? throw new ArgumentNullException(nameof(accumulate));
            m_processAsync = processAsync ?? throw new ArgumentNullException(nameof(processAsync));
            m_onProcessError = onProcessError;
            m_onPumpStateChanged = onPumpStateChanged;
        }

        /// <summary>
        /// Subscribes the pump to the given event source. At most one
        /// subscription is held; a second call replaces (and disposes) the
        /// previous subscription.
        /// </summary>
        /// <param name="source">The certificate change event source.</param>
        /// <exception cref="ArgumentNullException">
        /// When <paramref name="source"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// When the pump has been disposed.
        /// </exception>
        public void Subscribe(IObservable<CertificateChangeEvent> source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            // Subscribe outside the lock (the source runs arbitrary code),
            // then install under the lock so a concurrent Dispose can never
            // leave a live subscription behind: whichever of the two runs
            // second sees the other's state and cleans up.
            IDisposable subscription = source.Subscribe(new Observer(this));
            IDisposable? previous;
            lock (m_lock)
            {
                if (m_disposed)
                {
                    previous = subscription;
                }
                else
                {
                    previous = m_subscription;
                    m_subscription = subscription;
                }
            }

            previous?.Dispose();

            if (ReferenceEquals(previous, subscription))
            {
                throw new ObjectDisposedException(nameof(CertificateChangePump<TState>));
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            IDisposable? subscription;
            lock (m_lock)
            {
                m_disposed = true;
                m_pending = null;
                subscription = m_subscription;
                m_subscription = null;
            }

            subscription?.Dispose();
        }

        private void OnEvent(CertificateChangeEvent evt)
        {
            try
            {
                if (!m_filter(evt))
                {
                    return;
                }

                lock (m_lock)
                {
                    if (m_disposed)
                    {
                        return;
                    }

                    m_pending = m_accumulate(m_pending, evt);
                    if (m_pumpTask != null)
                    {
                        return;
                    }

                    var task = Task.Run(DrainAsync, CancellationToken.None);
                    m_pumpTask = task;
                    m_onPumpStateChanged?.Invoke(task);
                }
            }
            catch (Exception ex)
            {
                m_onProcessError?.Invoke(ex);
            }
        }

        private async Task DrainAsync()
        {
            while (true)
            {
                TState? state;
                lock (m_lock)
                {
                    state = m_pending;
                    m_pending = null;
                    if (state == null)
                    {
                        m_pumpTask = null;
                        m_onPumpStateChanged?.Invoke(null);
                        return;
                    }
                }

                try
                {
                    await m_processAsync(state, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    m_onProcessError?.Invoke(ex);
                }
            }
        }

        private sealed class Observer : IObserver<CertificateChangeEvent>
        {
            public Observer(CertificateChangePump<TState> owner)
            {
                m_owner = owner;
            }

            public void OnCompleted()
            {
            }

            public void OnError(Exception error)
            {
            }

            public void OnNext(CertificateChangeEvent value)
            {
                m_owner.OnEvent(value);
            }

            private readonly CertificateChangePump<TState> m_owner;
        }

        private readonly Func<CertificateChangeEvent, bool> m_filter;
        private readonly Func<TState?, CertificateChangeEvent, TState> m_accumulate;
        private readonly Func<TState, CancellationToken, ValueTask> m_processAsync;
        private readonly Action<Exception>? m_onProcessError;
        private readonly Action<Task?>? m_onPumpStateChanged;
        private readonly Lock m_lock = new();
        private IDisposable? m_subscription;
        private TState? m_pending;
        private Task? m_pumpTask;
        private bool m_disposed;
    }
}
