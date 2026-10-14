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
using System.Threading;
using Opc.Ua.Types;

namespace Opc.Ua
{
    /// <summary>
    /// Tracks the lifetime and cancellation state of a single request.
    /// </summary>
    public sealed class RequestLifetime : IDisposable
    {
        /// <summary>
        /// Initializes a new instance of the RequestLifetime class.
        /// </summary>
        /// <param name="externalTokens">
        /// Tokens whose cancellation also cancels this request. The lifetime owns the registrations
        /// and releases them when it completes.
        /// </param>
        public RequestLifetime(params CancellationToken[] externalTokens)
        {
            m_cts = new CancellationTokenSource();
            CancellationToken = m_cts.Token;
            m_externalRegistrations = externalTokens is { Length: > 0 }
                ? new CancellationTokenRegistration[externalTokens.Length]
                : [];
            try
            {
                for (int i = 0; i < m_externalRegistrations.Length; i++)
                {
                    m_externalRegistrations[i] = RegisterExternal(externalTokens[i]);
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        /// <summary>
        /// Initializes a lifetime that is cancelled by either token or once
        /// <paramref name="timeout"/> elapses.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is the lifetime of a server request. The timeout and the tokens are watched by
        /// one periodic sweep for all requests (<see cref="RequestDeadlineMonitor"/>, 20 ms)
        /// instead of a timer and two token registrations per request: the tokens are the
        /// server's and the transport's, shared by every request, so registering with them
        /// made all worker threads contend for the same registration locks.
        /// </para>
        /// <para>
        /// A timeout therefore fires up to one sweep period late, and a cancelled token
        /// cancels <see cref="CancellationToken"/> up to one sweep period later. A token that
        /// is already cancelled cancels the lifetime at once, and <see cref="TryCancel"/>
        /// checks the tokens first, so a token cancelled before an explicit cancellation still
        /// owns the terminal state. The timeout and the tokens claim the terminal state
        /// through <see cref="TryCancel"/> with <see cref="StatusCodes.Good"/>, so they race
        /// with explicit cancellation like before.
        /// </para>
        /// </remarks>
        /// <param name="timeout">The time after which the request is cancelled, or
        /// <see cref="TimeSpan.Zero"/> or <see cref="Timeout.InfiniteTimeSpan"/> for none.</param>
        /// <param name="first">A token that cancels the request.</param>
        /// <param name="second">Another token that cancels the request.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="timeout"/> is negative
        /// and not <see cref="Timeout.InfiniteTimeSpan"/>.</exception>
        public RequestLifetime(TimeSpan timeout, CancellationToken first, CancellationToken second)
            : this(RequestDeadlineMonitor.Shared, timeout, first, second)
        {
        }

        /// <summary>
        /// Initializes a lifetime that is cancelled by either token or once
        /// <paramref name="timeout"/> elapses, watched by <paramref name="monitor"/>.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="timeout"/> is negative
        /// and not <see cref="Timeout.InfiniteTimeSpan"/>.</exception>
        internal RequestLifetime(
            RequestDeadlineMonitor monitor,
            TimeSpan timeout,
            CancellationToken first,
            CancellationToken second)
        {
            if (timeout < TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout));
            }

            m_cts = new CancellationTokenSource();
            CancellationToken = m_cts.Token;
            m_externalRegistrations = [];
            m_first = first.CanBeCanceled ? first : default;
            m_second = second.CanBeCanceled && second != first ? second : default;
            m_deadline = timeout > TimeSpan.Zero ? monitor.GetDeadline(timeout) : long.MaxValue;

            if (IsLinkedTokenCancelled())
            {
                TryCancelCore(StatusCodes.Good);
                return;
            }

            if (m_deadline != long.MaxValue || m_first.CanBeCanceled || m_second.CanBeCanceled)
            {
                m_monitor = monitor;
                monitor.Add(this);
            }
        }

        /// <summary>
        /// A default instance of the RequestLifetime class that is already completed and cannot be cancelled.
        /// </summary>
        public static RequestLifetime None => s_default.Value;

        /// <summary>
        /// Gets the token that will be cancelled when the request is aborted.
        /// </summary>
        public CancellationToken CancellationToken { get; }

        /// <summary>
        /// Gets the derived UA StatusCode for the cancellation.
        /// </summary>
        public StatusCode StatusCode => new(unchecked((uint)Volatile.Read(ref m_state)));

        /// <summary>
        /// Gets or sets the optional sink that is notified when the request
        /// parks (suspends waiting for an out-of-band completion, such as a held
        /// <c>Publish</c>). When set, the request-processing worker is released
        /// at the park point instead of remaining blocked for the whole wait.
        /// <c>null</c> (the default) preserves the legacy inline behavior.
        /// </summary>
        /// <remarks>
        /// Endpoints supply the sink only for eligible requests. Handlers notify the existing sink rather
        /// than replacing it, and follow the terminal waiting-point contract of <see cref="IRequestParkSink"/>.
        /// The server's global decoupling switch may keep the worker attached even when a sink is present.
        /// </remarks>
        public IRequestParkSink? ParkSink { get; set; }

        /// <summary>
        /// Attempts to cancel the request and assigns the corresponding status code.
        /// </summary>
        /// <remarks>
        /// Concurrent callers (a client Cancel, a Session close and the request timeout) race to
        /// cancel the same request. Only the first caller claims the cancellation, so its status
        /// code is the one reported and a later caller can never overwrite it.
        /// </remarks>
        /// <returns><c>true</c> when this call cancelled the request.</returns>
        /// <param name="statusCode">The status code reported when this call wins the cancellation.</param>
        public bool TryCancel(StatusCode statusCode)
        {
            // A linked token that the monitor has not swept yet was cancelled first; like its
            // registration would have, it owns the terminal state.
            if (IsLinkedTokenCancelled())
            {
                TryCancelCore(StatusCodes.Good);
                return false;
            }

            return TryCancelCore(statusCode);
        }

        /// <summary>
        /// Whether the monitor has to cancel the lifetime: its deadline passed or a linked token
        /// was cancelled. Called by the monitor under its shard lock.
        /// </summary>
        /// <param name="now">The monitor's current time.</param>
        internal bool IsDue(long now)
        {
            return now >= m_deadline || IsLinkedTokenCancelled();
        }

        /// <summary>
        /// Cancels the lifetime because <see cref="IsDue"/> reported it; called by the monitor
        /// outside its locks.
        /// </summary>
        internal void OnDue()
        {
            TryCancelCore(StatusCodes.Good);
        }

        /// <summary>
        /// The monitor shard that holds the lifetime, or <c>null</c> when it is not monitored.
        /// </summary>
        internal RequestDeadlineMonitor.Shard? m_monitorShard;

        /// <summary>
        /// The previous lifetime in the monitor shard, written under the shard lock.
        /// </summary>
        internal RequestLifetime? m_monitorPrevious;

        /// <summary>
        /// The next lifetime in the monitor shard, written under the shard lock.
        /// </summary>
        internal RequestLifetime? m_monitorNext;

        private bool IsLinkedTokenCancelled()
        {
            return m_first.IsCancellationRequested || m_second.IsCancellationRequested;
        }

        private bool TryCancelCore(StatusCode statusCode)
        {
            long requested = c_cancelled | c_cancelling | statusCode.Code;
            if (Interlocked.CompareExchange(ref m_state, requested, 0) != 0)
            {
                return false;
            }

            try
            {
                // the terminal state is owned now; nothing is left for the sweep to do.
                m_monitor?.Remove(this);
                m_cts.Cancel();
                return true;
            }
            finally
            {
                long observed;
                long completed;
                do
                {
                    observed = Volatile.Read(ref m_state);
                    completed = observed & ~c_cancelling;
                }
                while (Interlocked.CompareExchange(ref m_state, completed, observed) != observed);

                if ((observed & c_completed) != 0)
                {
                    DisposeResources();
                }
            }
        }

        /// <summary>
        /// Completes the lifetime and prevents further changes.
        /// </summary>
        public void MarkCompleted()
        {
            Dispose();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            long observed;
            long completed;
            do
            {
                observed = Volatile.Read(ref m_state);
                if ((observed & c_completed) != 0)
                {
                    return;
                }
                completed = observed | c_completed;
            }
            while (Interlocked.CompareExchange(ref m_state, completed, observed) != observed);

            if ((observed & c_cancelling) == 0)
            {
                DisposeResources();
            }
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Registers an external token so that its cancellation claims the terminal state.
        /// </summary>
        /// <param name="token">The external token.</param>
        /// <returns>The registration that the lifetime releases when it completes.</returns>
        private CancellationTokenRegistration RegisterExternal(CancellationToken token)
        {
            return token.Register(
                static state => ((RequestLifetime)state!).TryCancel(StatusCodes.Good),
                this);
        }

        /// <summary>
        /// Releases the external registrations and the cancellation source once the lifetime is
        /// completed and no cancellation callback is running.
        /// </summary>
        private void DisposeResources()
        {
            foreach (CancellationTokenRegistration registration in m_externalRegistrations)
            {
                registration.Dispose();
            }
            m_monitor?.Remove(this);
            m_cts.Dispose();
        }

        // The lower bits publish the winning status together with its terminal ownership.
        private const long c_cancelled = 1L << 32;
        private const long c_cancelling = 1L << 33;
        private const long c_completed = 1L << 34;
        private static readonly Lazy<RequestLifetime> s_default = new(
            () =>
            {
                var r = new RequestLifetime();
                r.MarkCompleted();
                return r;
            });
        private readonly CancellationTokenSource m_cts;
        private readonly CancellationTokenRegistration[] m_externalRegistrations;
        private readonly RequestDeadlineMonitor? m_monitor;
        private readonly CancellationToken m_first;
        private readonly CancellationToken m_second;
        private readonly long m_deadline = long.MaxValue;
        private long m_state;
    }
}
