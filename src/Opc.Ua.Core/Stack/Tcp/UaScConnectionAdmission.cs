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
        void CompleteHandshake();
    }

    /// <summary>
    /// Reserves physical UASC connections independently of channel registration and handoff.
    /// The configured limiter remains owned by the host.
    /// </summary>
    internal sealed class UaScConnectionAdmission
    {
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

        public UaScConnectionAdmission CreateIndependentScope()
        {
            return new UaScConnectionAdmission(
                m_maxChannelCount, m_limiter, m_provider, m_handshakeTimeout, m_timeProvider, m_telemetry);
        }

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

        public void Start()
        {
            lock (m_lock)
            {
                m_stopped = false;
            }
        }

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
        /// Follows a transport through reverse-connect handoff. Only physical close releases capacity.
        /// </summary>
        internal sealed class Lease :
            IUaSCByteTransport, IUaSCByteTransportLimits, IUaSCHandshakeCompletionSource, IDisposable
        {
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

            public EndPoint? LocalEndpoint => Transport.LocalEndpoint;
            public EndPoint? RemoteEndpoint => Transport.RemoteEndpoint;
            public TransportChannelFeatures Features => Transport.Features;
            public string Implementation => Transport.Implementation;

            private IUaSCByteTransport Transport => m_transport ??
                throw new InvalidOperationException("The admission lease has no transport.");

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

            public void ReleaseAfterTransportClosed()
            {
                lock (m_lock)
                {
                    m_abort = null;
                }
                Close();
            }

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

            public ValueTask ConnectAsync(Uri url, CancellationToken ct)
            {
                return Transport.ConnectAsync(url, ct);
            }

            public ValueTask SendChunkAsync(ReadOnlyMemory<byte> chunk, CancellationToken ct)
            {
                return Transport.SendChunkAsync(chunk, ct);
            }

            public ValueTask SendChunkAsync(BufferCollection buffers, CancellationToken ct)
            {
                return Transport.SendChunkAsync(buffers, ct);
            }

            public ValueTask<ArraySegment<byte>> ReceiveChunkAsync(CancellationToken ct)
            {
                return Transport.ReceiveChunkAsync(ct);
            }

            public async Task WaitForCloseAsync(CancellationToken ct)
            {
                var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                using (ct.Register(
                    static state => ((TaskCompletionSource<bool>)state!).TrySetResult(true), cancelled))
                {
                    await Task.WhenAny(m_completion.Task, cancelled.Task).ConfigureAwait(false);
                }
            }

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

            public void Dispose()
            {
                Close();
            }

            void IUaSCByteTransportLimits.SetReceiveBufferSize(int receiveBufferSize)
            {
                if (Transport is IUaSCByteTransportLimits limits)
                {
                    limits.SetReceiveBufferSize(receiveBufferSize);
                }
            }

            private readonly UaScConnectionAdmission m_owner;
            private readonly Lock m_lock = new();
            private readonly TaskCompletionSource<bool> m_completion =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            private IUaSCByteTransport? m_transport;
            private Action? m_abort;
            private IDisposable? m_connection;
            private IDisposable? m_handshake;
            private readonly long m_started;
            private bool m_handshakeCompleted;
            private bool m_expired;
            private bool m_closed;
        }

        private readonly Lock m_lock = new();
        private readonly HashSet<Lease> m_leases = [];
        private readonly int m_maxChannelCount;
        private readonly IConnectionRateLimiter? m_limiter;
        private readonly IServerResourceIsolationProvider? m_provider;
        private readonly TimeSpan m_handshakeTimeout;
        private readonly TimeProvider m_timeProvider;
        private readonly ILogger? m_logger;
        private readonly ITelemetryContext? m_telemetry;
        private ITimer? m_deadlineTimer;
        private bool m_stopped;
    }

    /// <summary>
    /// Source-generated transport admission diagnostics without owner identifiers.
    /// </summary>
    internal static partial class UaScConnectionAdmissionLog
    {
        [LoggerMessage(EventId = CoreEventIds.UaScConnectionAdmission, Level = LogLevel.Debug,
            Message = "Closing a physical connection whose fixed startup handshake deadline expired.")]
        public static partial void UaScAdmissionHandshakeExpired(this ILogger logger);

        [LoggerMessage(EventId = CoreEventIds.UaScConnectionAdmission + 1, Level = LogLevel.Warning,
            Message = "Failed to close a physical connection at its startup handshake deadline.")]
        public static partial void UaScAdmissionDeadlineCloseFailed(this ILogger logger, Exception exception);
    }
}
