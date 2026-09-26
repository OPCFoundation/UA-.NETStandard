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
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Completes physical startup admission after an accepted ReverseHello handoff or successful OpenSecureChannel.
    /// </summary>
    internal interface IUaSCHandshakeCompletionSource
    {
        /// <summary>
        /// Releases startup capacity without releasing the physical connection reservation.
        /// </summary>
        void CompleteHandshake();
    }

    /// <summary>
    /// Reserves physical UASC connections independently of channel registration and handoff.
    /// The configured limiter remains owned by the host.
    /// </summary>
    internal sealed class UaScConnectionAdmission
    {
        /// <summary>
        /// Shares host-owned admission policies while tracking this scope's physical connections and fixed deadlines.
        /// </summary>
        public UaScConnectionAdmission(
            int maxChannelCount,
            IConnectionRateLimiter? limiter,
            IServerResourceIsolationProvider? provider = null,
            TimeSpan? handshakeTimeout = null,
            TimeProvider? timeProvider = null,
            ITelemetryContext? telemetry = null)
        {
            TimeSpan timeout = handshakeTimeout ?? TimeSpan.FromMinutes(2);
            if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(2))
            {
                throw new ArgumentOutOfRangeException(nameof(handshakeTimeout));
            }
            m_maxChannelCount = maxChannelCount;
            m_limiter = limiter;
            m_provider = provider;
            m_handshakeTimeout = timeout;
            m_timeProvider = timeProvider ?? TimeProvider.System;
            m_telemetry = telemetry;
            m_logger = telemetry?.CreateLogger<UaScConnectionAdmission>();
        }

        /// <summary>
        /// Creates independent connection tracking and stop state while borrowing the same host policies.
        /// </summary>
        public UaScConnectionAdmission CreateIndependentScope()
        {
            return new UaScConnectionAdmission(
                m_maxChannelCount, m_limiter, m_provider, m_handshakeTimeout, m_timeProvider, m_telemetry);
        }

        /// <summary>
        /// Acquires connection and startup capacity together, reclaiming aggregate capacity at most once.
        /// </summary>
        public bool TryAcquire(
            EndPoint? remoteEndpoint,
            [NotNullWhen(true)] out Lease? lease,
            Func<bool>? tryReclaimCapacity = null)
        {
            lease = null;
            lock (m_lock)
            {
                if (m_stopped)
                {
                    return false;
                }
            }
            ResourceIsolationOwner? owner = m_provider?.ClassifyConnection(remoteEndpoint as IPEndPoint);
            bool rateAdmitted = false;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                IDisposable? connection = null;
                IDisposable? handshake = null;
                try
                {
                    bool resourcesAdmitted = true;
                    if (m_provider != null)
                    {
                        bool connectionAdmitted = m_provider.TryAcquire(
                            ResourceIsolationStage.Connection, owner!, 1, out connection,
                            out ResourceIsolationFailure connectionFailure);
                        if (!connectionAdmitted &&
                            connectionFailure.Reason != ResourceIsolationFailureReason.Capacity)
                        {
                            return false;
                        }
                        // Check both owner ceilings before permitting aggregate-capacity reclamation.
                        bool handshakeAdmitted = m_provider.TryAcquire(
                            ResourceIsolationStage.Handshake, owner!, 1, out handshake,
                            out ResourceIsolationFailure handshakeFailure);
                        if (!handshakeAdmitted &&
                            handshakeFailure.Reason != ResourceIsolationFailureReason.Capacity)
                        {
                            return false;
                        }
                        resourcesAdmitted = connectionAdmitted && handshakeAdmitted;
                    }
                    if (!resourcesAdmitted && (attempt != 0 || tryReclaimCapacity == null))
                    {
                        return false;
                    }
                    if (!rateAdmitted && m_limiter != null)
                    {
                        bool admitted = owner != null &&
                            m_limiter is IResourceIsolationConnectionRateLimiter classifiedLimiter
                            ? classifiedLimiter.TryAdmitConnection(remoteEndpoint, owner, out _)
                            : m_limiter.TryAdmitConnection(remoteEndpoint, out _);
                        if (!admitted)
                        {
                            return false;
                        }
                    }
                    rateAdmitted = true;
                    if (resourcesAdmitted)
                    {
                        lock (m_lock)
                        {
                            if (m_stopped || (m_maxChannelCount > 0 && m_leases.Count >= m_maxChannelCount))
                            {
                                return false;
                            }
                            if (m_deadlineTimer == null)
                            {
                                TimeSpan period = TimeSpan.FromMilliseconds(
                                    Math.Max(1, Math.Min(1000, m_handshakeTimeout.TotalMilliseconds / 2)));
                                m_deadlineTimer = m_timeProvider.CreateTimer(CheckDeadlines, null, period, period);
                            }
                            lease = new Lease(this, connection, handshake, m_timeProvider.GetTimestamp());
                            m_leases.Add(lease);
                            connection = null;
                            handshake = null;
                            return true;
                        }
                    }
                }
                finally
                {
                    try
                    {
                        handshake?.Dispose();
                    }
                    finally
                    {
                        connection?.Dispose();
                    }
                }
                lock (m_lock)
                {
                    if (m_stopped)
                    {
                        return false;
                    }
                }
                // No partial resource leases or admission locks survive into listener cleanup.
                if (tryReclaimCapacity == null || !tryReclaimCapacity())
                {
                    return false;
                }
            }
            return false;
        }

        /// <summary>
        /// Rejects new admissions and closes every tracked connection, collecting cleanup failures.
        /// </summary>
        public void Stop()
        {
            Lease[] leases;
            lock (m_lock)
            {
                m_stopped = true;
                leases = [.. m_leases];
            }

            List<Exception>? errors = null;
            foreach (Lease lease in leases)
            {
                try
                {
                    lease.Dispose();
                }
                catch (Exception ex)
                {
                    (errors ??= []).Add(ex);
                }
            }
            if (errors != null)
            {
                throw new AggregateException("Failed to close admitted UASC connections.", errors);
            }
        }

        /// <summary>
        /// Reopens admission without changing the configured limits or borrowed policies.
        /// </summary>
        public void Start()
        {
            lock (m_lock)
            {
                m_stopped = false;
            }
        }

        /// <summary>
        /// Follows a transport through reverse-connect handoff. Only physical close releases capacity.
        /// </summary>
        internal sealed class Lease :
            IUaSCByteTransport, IUaSCByteTransportLimits, IUaSCHandshakeCompletionSource, IDisposable
        {
            /// <summary>
            /// Takes ownership of the acquired reservations and the original handshake start timestamp.
            /// </summary>
            internal Lease(
                UaScConnectionAdmission owner,
                IDisposable? connection,
                IDisposable? handshake,
                long started)
            {
                m_owner = owner;
                m_connection = connection;
                m_handshake = handshake;
                m_started = started;
            }

            /// <summary>
            /// Gets the attached transport's local endpoint.
            /// </summary>
            public EndPoint? LocalEndpoint => Transport.LocalEndpoint;

            /// <summary>
            /// Gets the attached transport's remote endpoint.
            /// </summary>
            public EndPoint? RemoteEndpoint => Transport.RemoteEndpoint;

            /// <summary>
            /// Gets the capabilities supplied by the attached transport.
            /// </summary>
            public TransportChannelFeatures Features => Transport.Features;

            /// <summary>
            /// Gets the attached transport's implementation identifier.
            /// </summary>
            public string Implementation => Transport.Implementation;

            /// <summary>
            /// Registers pre-attachment physical cleanup, invoking it immediately if the lease is already closed.
            /// </summary>
            public void SetAbortAction(Action abort)
            {
                if (abort == null)
                {
                    throw new ArgumentNullException(nameof(abort));
                }
                lock (m_lock)
                {
                    if (!m_closed)
                    {
                        m_abort = abort;
                        return;
                    }
                }
                abort();
            }

            /// <summary>
            /// Transfers physical-close responsibility to one transport without restarting the handshake deadline.
            /// </summary>
            public void Attach(IUaSCByteTransport transport)
            {
                if (transport == null)
                {
                    throw new ArgumentNullException(nameof(transport));
                }
                lock (m_lock)
                {
                    if (!m_closed)
                    {
                        if (m_transport != null)
                        {
                            throw new InvalidOperationException("The admission lease already has a transport.");
                        }
                        m_transport = transport;
                        return;
                    }
                }
                transport.Close();
                throw new ObjectDisposedException(nameof(Lease));
            }

            /// <summary>
            /// Releases startup capacity once unless closure or expiry has already won the race.
            /// </summary>
            public void CompleteHandshake()
            {
                IDisposable? handshake;
                lock (m_lock)
                {
                    if (m_closed || m_expired)
                    {
                        return;
                    }
                    m_handshakeCompleted = true;
                    handshake = m_handshake;
                    m_handshake = null;
                }
                handshake?.Dispose();
            }

            /// <summary>
            /// Releases reservations after host-side physical closure without aborting the closed connection again.
            /// </summary>
            public void ReleaseAfterTransportClosed()
            {
                lock (m_lock)
                {
                    m_abort = null;
                }
                Close();
            }

            /// <summary>
            /// Connects through the attached transport while retaining admission ownership.
            /// </summary>
            public ValueTask ConnectAsync(Uri url, CancellationToken ct)
            {
                return Transport.ConnectAsync(url, ct);
            }

            /// <summary>
            /// Sends a contiguous chunk through the attached transport.
            /// </summary>
            public ValueTask SendChunkAsync(ReadOnlyMemory<byte> chunk, CancellationToken ct)
            {
                return Transport.SendChunkAsync(chunk, ct);
            }

            /// <summary>
            /// Sends a segmented chunk through the attached transport.
            /// </summary>
            public ValueTask SendChunkAsync(BufferCollection buffers, CancellationToken ct)
            {
                return Transport.SendChunkAsync(buffers, ct);
            }

            /// <summary>
            /// Receives a chunk without detaching the transport from its admission lease.
            /// </summary>
            public ValueTask<ArraySegment<byte>> ReceiveChunkAsync(CancellationToken ct)
            {
                return Transport.ReceiveChunkAsync(ct);
            }

            /// <summary>
            /// Waits for physical cleanup or caller cancellation without releasing reservations itself.
            /// </summary>
            public async Task WaitForCloseAsync(CancellationToken ct)
            {
                var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                using (ct.Register(
                    static state => ((TaskCompletionSource<bool>)state!).TrySetResult(true), cancelled))
                {
                    await Task.WhenAny(m_completion.Task, cancelled.Task).ConfigureAwait(false);
                }
            }

            /// <summary>
            /// Closes the physical connection once and releases all reservations even if cleanup fails.
            /// </summary>
            public void Close()
            {
                IUaSCByteTransport? transport;
                Action? abort;
                IDisposable? connection;
                IDisposable? handshake;
                lock (m_lock)
                {
                    if (m_closed)
                    {
                        return;
                    }
                    m_closed = true;
                    transport = m_transport;
                    abort = m_abort;
                    connection = m_connection;
                    m_connection = null;
                    handshake = m_handshake;
                    m_handshake = null;
                }
                try
                {
                    // Before attachment the lease aborts; afterward the transport owns physical close.
                    if (transport == null)
                    {
                        abort?.Invoke();
                    }
                    else
                    {
                        transport.Close();
                    }
                }
                finally
                {
                    try
                    {
                        handshake?.Dispose();
                    }
                    finally
                    {
                        try
                        {
                            connection?.Dispose();
                        }
                        finally
                        {
                            m_owner.Release(this);
                            m_completion.TrySetResult(true);
                        }
                    }
                }
            }

            /// <summary>
            /// Ends the physical connection and its admission lifetime.
            /// </summary>
            public void Dispose()
            {
                Close();
            }

            /// <summary>
            /// Propagates receive limits when the attached transport supports them.
            /// </summary>
            void IUaSCByteTransportLimits.SetReceiveBufferSize(int receiveBufferSize)
            {
                if (Transport is IUaSCByteTransportLimits limits)
                {
                    limits.SetReceiveBufferSize(receiveBufferSize);
                }
            }

            /// <summary>
            /// Claims expiry against handshake completion using the original, non-renewable deadline.
            /// </summary>
            internal void CheckDeadline()
            {
                lock (m_lock)
                {
                    if (m_closed || m_handshakeCompleted ||
                        m_owner.m_timeProvider.GetElapsedTime(m_started) < m_owner.m_handshakeTimeout)
                    {
                        return;
                    }
                    // Claim expiry atomically with completion; never extend it on partial input or handoff.
                    m_expired = true;
                }
                m_owner.m_logger?.UaScAdmissionHandshakeExpired();
                Close();
            }

            /// <summary>
            /// Gets the transport only after attachment has transferred physical-close responsibility.
            /// </summary>
            private IUaSCByteTransport Transport => m_transport ??
                throw new InvalidOperationException("The admission lease has no transport.");

            /// <summary>
            /// Scope whose connection count and timer include this lease.
            /// </summary>
            private readonly UaScConnectionAdmission m_owner;

            /// <summary>
            /// Serializes attachment, handshake completion, expiry and close.
            /// </summary>
            private readonly Lock m_lock = new();

            /// <summary>
            /// Completes after physical cleanup and reservation release, including failed cleanup.
            /// </summary>
            private readonly TaskCompletionSource<bool> m_completion =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>
            /// Physical transport retained across channel handoff.
            /// </summary>
            private IUaSCByteTransport? m_transport;

            /// <summary>
            /// Physical cleanup callback used before a transport is attached.
            /// </summary>
            private Action? m_abort;

            /// <summary>
            /// Connection reservation retained until physical close.
            /// </summary>
            private IDisposable? m_connection;

            /// <summary>
            /// Startup reservation released on handshake completion or close.
            /// </summary>
            private IDisposable? m_handshake;

            /// <summary>
            /// Original monotonic admission timestamp; handoff never renews it.
            /// </summary>
            private readonly long m_started;

            /// <summary>
            /// Records successful startup before expiry or closure.
            /// </summary>
            private bool m_handshakeCompleted;

            /// <summary>
            /// Prevents completion from reviving an expired startup.
            /// </summary>
            private bool m_expired;

            /// <summary>
            /// Claims physical cleanup and reservation release exactly once.
            /// </summary>
            private bool m_closed;
        }

        /// <summary>
        /// Removes a closed lease and disposes the deadline timer when the scope becomes empty.
        /// </summary>
        private void Release(Lease lease)
        {
            ITimer? timer = null;
            lock (m_lock)
            {
                m_leases.Remove(lease);
                if (m_leases.Count == 0)
                {
                    timer = m_deadlineTimer;
                    m_deadlineTimer = null;
                }
            }
            timer?.Dispose();
        }

        /// <summary>
        /// Checks a stable lease snapshot so one cleanup failure cannot prevent other expirations.
        /// </summary>
        private void CheckDeadlines(object? state)
        {
            Lease[] leases;
            lock (m_lock)
            {
                leases = [.. m_leases];
            }
            foreach (Lease lease in leases)
            {
                try
                {
                    lease.CheckDeadline();
                }
                catch (Exception ex)
                {
                    m_logger?.UaScAdmissionDeadlineCloseFailed(ex);
                }
            }
        }

        /// <summary>
        /// Serializes scope admission, lease tracking and timer ownership.
        /// </summary>
        private readonly Lock m_lock = new();

        /// <summary>
        /// Physical connections whose reservations have not yet been released.
        /// </summary>
        private readonly HashSet<Lease> m_leases = [];

        /// <summary>
        /// Per-scope connection ceiling; nonpositive values disable this local ceiling.
        /// </summary>
        private readonly int m_maxChannelCount;

        /// <summary>
        /// Borrowed host policy that meters admissions without refunding on close.
        /// </summary>
        private readonly IConnectionRateLimiter? m_limiter;

        /// <summary>
        /// Borrowed provider for owner classification and shared resource reservations.
        /// </summary>
        private readonly IServerResourceIsolationProvider? m_provider;

        /// <summary>
        /// Fixed maximum elapsed time from admission to handshake completion.
        /// </summary>
        private readonly TimeSpan m_handshakeTimeout;

        /// <summary>
        /// Monotonic clock and timer source shared with independent scopes.
        /// </summary>
        private readonly TimeProvider m_timeProvider;

        /// <summary>
        /// Reports expiration and cleanup failures without owner identifiers.
        /// </summary>
        private readonly ILogger? m_logger;

        /// <summary>
        /// Telemetry context propagated to independent admission scopes.
        /// </summary>
        private readonly ITelemetryContext? m_telemetry;

        /// <summary>
        /// Deadline scanner owned only while tracked leases remain.
        /// </summary>
        private ITimer? m_deadlineTimer;

        /// <summary>
        /// Rejects new leases until the scope is explicitly restarted.
        /// </summary>
        private bool m_stopped;
    }

    /// <summary>
    /// Source-generated transport admission diagnostics without owner identifiers.
    /// </summary>
    internal static partial class UaScConnectionAdmissionLog
    {
        /// <summary>
        /// Reports a startup timeout without exposing connection owner identifiers.
        /// </summary>
        [LoggerMessage(EventId = CoreEventIds.UaScConnectionAdmission, Level = LogLevel.Debug,
            Message = "Closing a physical connection whose fixed startup handshake deadline expired.")]
        public static partial void UaScAdmissionHandshakeExpired(this ILogger logger);

        /// <summary>
        /// Reports physical cleanup failure while allowing remaining deadline checks to continue.
        /// </summary>
        [LoggerMessage(EventId = CoreEventIds.UaScConnectionAdmission + 1, Level = LogLevel.Warning,
            Message = "Failed to close a physical connection at its startup handshake deadline.")]
        public static partial void UaScAdmissionDeadlineCloseFailed(this ILogger logger, Exception exception);
    }
}
