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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua
{
    public sealed partial class ClientChannelManager
    {
        /// <summary>
        /// Initializes a managed client channel factory.
        /// </summary>
        /// <param name="configuration">The application configuration.</param>
        /// <param name="telemetry">Telemetry context for logger creation.</param>
        /// <param name="channelFactory">Optional channel binding registry;
        /// defaults to the static <see cref="Opc.Ua.Bindings.TransportBindings.Channels"/>.</param>
        /// <param name="reconnectPolicy">Optional channel-level retry
        /// policy. Defaults to
        /// <see cref="ExponentialBackoffChannelReconnectPolicy"/> with
        /// historical 500&#160;ms → 30&#160;s backoff.</param>
        /// <param name="timeProvider">Optional time provider for backoff
        /// timing. Defaults to <see cref="TimeProvider.System"/>.</param>
        public ClientChannelManager(
            ApplicationConfiguration configuration,
            ITelemetryContext telemetry,
            Bindings.ITransportChannelBindings? channelFactory = null,
            IChannelReconnectPolicy? reconnectPolicy = null,
            TimeProvider? timeProvider = null)
            : this(configuration, channelFactory)
        {
            m_logger = telemetry?.CreateLogger<ClientChannelManager>();
            m_meter = telemetry?.CreateMeter();
            m_metrics = m_meter != null
                ? new ClientChannelManagerMetrics(this, m_meter)
                : null;
            m_reconnectPolicy = reconnectPolicy ?? new ExponentialBackoffChannelReconnectPolicy();
            m_timeProvider = timeProvider ?? TimeProvider.System;
        }

        /// <inheritdoc/>
        public ValueTask<IManagedTransportChannel> GetAsync(
            IReconnectParticipant participant,
            CancellationToken ct = default)
        {
            if (participant == null)
            {
                throw new ArgumentNullException(nameof(participant));
            }

            ConfiguredEndpoint endpoint = participant.Endpoint
                ?? throw new ArgumentException(
                    "Participant.Endpoint is null.", nameof(participant));
            return GetCoreAsync(endpoint, _ => participant, reverseConnection: null, ct);
        }

        /// <inheritdoc/>
        public ValueTask<IManagedTransportChannel> GetAsync(
            IReconnectParticipant participant,
            ITransportWaitingConnection reverseConnection,
            CancellationToken ct = default)
        {
            if (participant == null)
            {
                throw new ArgumentNullException(nameof(participant));
            }
            if (reverseConnection == null)
            {
                throw new ArgumentNullException(nameof(reverseConnection));
            }

            ConfiguredEndpoint endpoint = participant.Endpoint
                ?? throw new ArgumentException(
                    "Participant.Endpoint is null.", nameof(participant));
            return GetCoreAsync(endpoint, _ => participant, reverseConnection, ct);
        }

        /// <inheritdoc/>
        public ValueTask<IManagedTransportChannel> GetAsync(
            ConfiguredEndpoint endpoint,
            Func<IManagedTransportChannel, IReconnectParticipant> participantFactory,
            ITransportWaitingConnection? reverseConnection,
            CancellationToken ct = default)
        {
            if (endpoint == null)
            {
                throw new ArgumentNullException(nameof(endpoint));
            }
            if (participantFactory == null)
            {
                throw new ArgumentNullException(nameof(participantFactory));
            }

            return GetCoreAsync(endpoint, participantFactory, reverseConnection, ct);
        }

        /// <inheritdoc/>
        public ValueTask ReconnectAsync(
            IManagedTransportChannel channel,
            CancellationToken ct = default)
        {
            if (channel == null)
            {
                throw new ArgumentNullException(nameof(channel));
            }
            if (channel is not ManagedTransportChannelLease lease)
            {
                throw new ArgumentException(
                    "Channel was not produced by this manager.", nameof(channel));
            }

            var budget = new RetryBudget(Timeout.InfiniteTimeSpan, m_timeProvider);
            return new ValueTask(lease.Entry.RequestReconnectAsync(budget, ct));
        }

        /// <inheritdoc/>
        public ValueTask ReconnectAsync(
            IManagedTransportChannel channel,
            IRetryBudget budget,
            CancellationToken ct = default)
        {
            if (channel == null)
            {
                throw new ArgumentNullException(nameof(channel));
            }
            if (budget == null)
            {
                throw new ArgumentNullException(nameof(budget));
            }
            if (channel is not ManagedTransportChannelLease lease)
            {
                throw new ArgumentException(
                    "Channel was not produced by this manager.", nameof(channel));
            }

            return AwaitReconnectResultAsync(
                lease.Entry.RequestReconnectAsync(budget, ct));
        }

        /// <inheritdoc/>
        public async ValueTask ReconnectAllAsync(CancellationToken ct = default)
        {
            ChannelEntry[] snapshot;
            lock (m_entries)
            {
                snapshot = [.. m_entries.Values];
            }

            if (snapshot.Length == 0)
            {
                return;
            }

            await Task.WhenAll(
                snapshot.Select(e => e.RequestReconnectAsync(ct)))
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public IReadOnlyList<ManagedChannelDiagnostic> GetChannelDiagnostics()
        {
            ChannelEntry[] snapshot;
            lock (m_entries)
            {
                snapshot = [.. m_entries.Values];
            }

            var diagnostics = new ManagedChannelDiagnostic[snapshot.Length];
            for (int i = 0; i < snapshot.Length; i++)
            {
                diagnostics[i] = snapshot[i].GetDiagnosticSnapshot();
            }

            return diagnostics;
        }

        /// <inheritdoc/>
        [Obsolete("Use GetAsync(ConfiguredEndpoint, Func<IManagedTransportChannel, IReconnectParticipant>, " +
            "ITransportWaitingConnection?, CancellationToken) to bind a participant atomically.")]
        public void RebindParticipant(
            IManagedTransportChannel channel,
            IReconnectParticipant participant)
        {
            if (channel == null)
            {
                throw new ArgumentNullException(nameof(channel));
            }
            if (participant == null)
            {
                throw new ArgumentNullException(nameof(participant));
            }
            if (channel is not ManagedTransportChannelLease lease)
            {
                throw new ArgumentException(
                    "Channel was not produced by this manager.", nameof(channel));
            }
            lease.SwapParticipant(participant);
        }

        /// <inheritdoc/>
        public void UpdateClientCertificate(
            Certificate? clientCertificate,
            CertificateCollection? clientCertificateChain)
        {
            Certificate? previousCertificate;
            CertificateCollection? previousCertificateChain;
            lock (m_certLock)
            {
                previousCertificate = m_clientCertificate;
                previousCertificateChain = m_clientCertificateChain;
                m_clientCertificate = clientCertificate;
                m_clientCertificateChain = clientCertificateChain;
                m_clientCertificateVersion++;
            }

            previousCertificate?.Dispose();
            previousCertificateChain?.Dispose();
        }

        /// <summary>
        /// Returns the current client certificate, if any.
        /// </summary>
        internal Certificate? CurrentClientCertificate
        {
            get
            {
                lock (m_certLock)
                {
                    return m_clientCertificate;
                }
            }
        }

        /// <summary>
        /// Returns the current client certificate chain, if any.
        /// </summary>
        internal CertificateCollection? CurrentClientCertificateChain
        {
            get
            {
                lock (m_certLock)
                {
                    return m_clientCertificateChain;
                }
            }
        }

        internal ApplicationConfiguration Configuration => m_configuration;

        internal Bindings.ITransportChannelBindings? ChannelBindings => m_channelFactory;

        internal IChannelReconnectPolicy ReconnectPolicy => m_reconnectPolicy;

        internal TimeProvider TimeProvider => m_timeProvider;

        internal ILogger? Logger => m_logger;

        internal (Certificate? Certificate, CertificateCollection? Chain, long Version) CurrentClientCertificateSnapshot
        {
            get
            {
                lock (m_certLock)
                {
                    return (m_clientCertificate, m_clientCertificateChain, m_clientCertificateVersion);
                }
            }
        }

        /// <summary>
        /// AsyncLocal flag set by the manager around participant
        /// reactivation calls. When non-zero, the channel wrapper's
        /// <see cref="ITransportChannel.SendRequestAsync"/> bypasses the
        /// ready-state gate so that session-service requests
        /// (ActivateSession, CreateSession, etc.) can complete while the
        /// channel is in
        /// <see cref="ChannelState.TransportConnectedSessionReactivating"/>.
        /// </summary>
        internal static readonly AsyncLocal<int> s_reactivationDepth = new();

        internal static IDisposable EnterReactivationScope()
        {
            s_reactivationDepth.Value++;
            return new ReactivationScope();
        }

        private sealed class ReactivationScope : IDisposable
        {
            public void Dispose()
            {
                s_reactivationDepth.Value--;
            }
        }

        private static async ValueTask AwaitReconnectResultAsync(
            Task<bool> reconnectTask)
        {
            if (!await reconnectTask.ConfigureAwait(false))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadSecureChannelClosed,
                    "Channel reconnect did not complete successfully.");
            }
        }

        private async ValueTask<IManagedTransportChannel> GetCoreAsync(
            ConfiguredEndpoint endpoint,
            Func<IManagedTransportChannel, IReconnectParticipant> participantFactory,
            ITransportWaitingConnection? reverseConnection,
            CancellationToken ct)
        {
            ThrowIfDisposed();

            Certificate? clientCert;
            CertificateCollection? clientChain;
            long clientCertificateVersion;
            lock (m_certLock)
            {
                clientCert = m_clientCertificate;
                clientChain = m_clientCertificateChain;
                clientCertificateVersion = m_clientCertificateVersion;
            }

            ManagedChannelKey key = ManagedChannelKey.FromEndpoint(
                endpoint, clientCert, reverseConnection);

            ChannelEntry entry;
            bool created = false;
            lock (m_entries)
            {
                if (!m_entries.TryGetValue(key, out ChannelEntry? existing) ||
                    existing.State is ChannelState.Closed or ChannelState.Faulted)
                {
                    existing = new ChannelEntry(this, key, endpoint, reverseConnection);
                    m_entries[key] = existing;
                    created = true;
                }
                entry = existing;
            }

            ManagedTransportChannelLease lease;
            try
            {
                if (created)
                {
                    await entry.OpenInitialAsync(clientCert, clientChain, clientCertificateVersion, ct)
                        .ConfigureAwait(false);
                }
                lease = entry.AcquireLease(participantFactory);
            }
            catch
            {
                if (created)
                {
                    lock (m_entries)
                    {
                        m_entries.Remove(key);
                    }
                    await entry.DisposeAsync().ConfigureAwait(false);
                }
                throw;
            }

            return lease;
        }

        internal void RemoveEntryIfPresent(ManagedChannelKey key, ChannelEntry entry)
        {
            lock (m_entries)
            {
                if (m_entries.TryGetValue(key, out ChannelEntry? existing) &&
                    ReferenceEquals(existing, entry))
                {
                    m_entries.Remove(key);
                }
            }
        }

        // Per-instance state for the managed (sharing/refcount) path.
        // Legacy single-shot CreateChannelAsync path uses the existing
        // ClientChannel wrapper and is independent of this state.
        private readonly Dictionary<ManagedChannelKey, ChannelEntry> m_entries = [];
        private readonly Lock m_certLock = new();
        private Certificate? m_clientCertificate;
        private CertificateCollection? m_clientCertificateChain;
        private long m_clientCertificateVersion;
        private readonly IChannelReconnectPolicy m_reconnectPolicy
            = new ExponentialBackoffChannelReconnectPolicy();
        private readonly TimeProvider m_timeProvider = TimeProvider.System;
        private readonly ILogger? m_logger;
    }
}
