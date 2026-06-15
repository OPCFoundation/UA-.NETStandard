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
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Bindings;
using Opc.Ua.Security.Certificates;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace Opc.Ua
{
    /// <summary>
    /// Options for <see cref="ClientChannelManager"/>.
    /// </summary>
    public sealed class ChannelManagerOptions
    {
        /// <summary>
        /// Maximum number of distinct transport channels the manager
        /// may track concurrently. Each unique combination of endpoint,
        /// configuration, client certificate, and reverse-connect flag
        /// counts as one channel. Defaults to 256. Set to <c>0</c> to
        /// disable the cap (legacy unbounded behavior).
        /// </summary>
        /// <remarks>
        /// When the cap is reached and a new channel would have to be
        /// created, <c>GetAsync</c> throws a
        /// <see cref="ServiceResultException"/> with
        /// <see cref="StatusCodes.BadResourceUnavailable"/>. Existing
        /// channels remain available. LRU eviction is intentionally
        /// not implemented yet; that's a follow-up.
        /// </remarks>
        public int MaxChannels { get; init; } = 256;
    }

    /// <summary>
    /// Client side transport channel factory. Manages creation
    /// of transport channels for clients.
    /// </summary>
    public sealed class ClientChannelManager : IClientChannelManager, IAsyncDisposable, IChannelEntryHost, IChannelCertRotationHost
    {
        /// <summary>
        /// Callback to register channel diagnostics
        /// </summary>
        public event Action<ITransportChannel, TransportChannelDiagnostic>? OnDiagnostics;

        /// <summary>
        /// Create client channel factory.
        /// </summary>
        /// <param name="configuration">The application configuration to use</param>
        /// <param name="channelFactory">An optional factory to create channel types
        /// from. Uses the default channel bindings if none is provided</param>
        /// <param name="options">Optional channel manager options.</param>
        public ClientChannelManager(
            ApplicationConfiguration configuration,
            ITransportChannelBindings? channelFactory = null,
            ChannelManagerOptions? options = null)
        {
            m_configuration = configuration;
            m_channelFactory = channelFactory;
            m_options = options ?? new ChannelManagerOptions();
            m_certRotation = new ClientChannelManagerCertRotation(this);
            WireCertificateRotation();
        }

        /// <inheritdoc/>
        public async ValueTask<ITransportChannel> CreateChannelAsync(
            ConfiguredEndpoint endpoint,
            IServiceMessageContext context,
            Certificate? clientCertificate,
            CertificateCollection? clientCertificateChain = null,
            ITransportWaitingConnection? connection = null,
            CancellationToken ct = default)
        {
            ThrowIfDisposed();

            // initialize the channel which will be created with the server.
            ITransportChannel channel;
            if (connection != null)
            {
                channel = await CreateUaBinaryChannelAsync(
                    m_configuration,
                    connection,
                    endpoint.Description,
                    endpoint.Configuration!,
                    clientCertificate,
                    clientCertificateChain,
                    context,
                    m_channelFactory,
                    ct).ConfigureAwait(false);
            }
            else
            {
                channel = await CreateUaBinaryChannelAsync(
                    m_configuration,
                    endpoint.Description,
                    endpoint.Configuration!,
                    clientCertificate,
                    clientCertificateChain,
                    context,
                    m_channelFactory,
                    ct).ConfigureAwait(false);
            }
            if (channel is ISecureChannel secureChannel)
            {
                secureChannel.OnTokenActivated += OnChannelTokenActivated;
            }
            return channel;
        }

        /// <summary>
        /// Called when channel is disposed
        /// </summary>
        /// <param name="channel"></param>
        internal void CloseChannel(ITransportChannel channel)
        {
            if (channel is ISecureChannel secureChannel)
            {
                secureChannel.OnTokenActivated -= OnChannelTokenActivated;
            }
            channel.Dispose();
        }

        /// <summary>
        /// Creates a new UA-binary transport channel if requested. Null otherwise.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        /// <exception cref="ArgumentException"></exception>
        internal static async ValueTask<ITransportChannel> CreateUaBinaryChannelAsync(
            ApplicationConfiguration configuration,
            ITransportWaitingConnection connection,
            EndpointDescription description,
            EndpointConfiguration endpointConfiguration,
            Certificate? clientCertificate,
            CertificateCollection? clientCertificateChain,
            IServiceMessageContext messageContext,
            ITransportChannelBindings? transportChannelBindings = null,
            CancellationToken ct = default)
        {
            // initialize the channel which will be created with the server.
            string uriScheme = new Uri(description.EndpointUrl
                ?? throw new ArgumentException("EndpointUrl cannot be null", nameof(description))).Scheme;
            transportChannelBindings ??= GetDefaultBindingsLazy();
            ITransportChannel channel =
                transportChannelBindings.Create(uriScheme, messageContext.Telemetry)
                ?? throw ServiceResultException.Create(
                    StatusCodes.BadProtocolVersionUnsupported,
                    "Unsupported transport profile for scheme {0}.",
                    uriScheme);

            if (channel is not ISecureChannel secureChannel)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNotSupported,
                    "The transport channel does not support opening.");
            }

            // create a UA channel.
            var settings = new TransportChannelSettings
            {
                Description = description,
                Configuration = endpointConfiguration,
                ClientCertificate = clientCertificate,
                ClientCertificateChain = clientCertificateChain
            };

            try
            {
                if (description.ServerCertificate.Length > 0)
                {
                    settings.ServerCertificate = Utils.ParseCertificateBlob(
                        description.ServerCertificate,
                        messageContext.Telemetry);
                }

                if (configuration != null)
                {
                    settings.CertificateValidator = configuration.CertificateManager;
                }

                settings.NamespaceUris = messageContext.NamespaceUris;
                settings.Factory = messageContext.Factory;

                await secureChannel.OpenAsync(connection, settings, ct).ConfigureAwait(false);
            }
            catch
            {
                // settings.ServerCertificate is allocated above; dispose on
                // failure since the channel never assumed ownership.
                settings.ServerCertificate?.Dispose();
                channel.Dispose();
                throw;
            }

            return channel;
        }

        /// <summary>
        /// Creates a new UA-binary transport channel if requested. Null otherwise.
        /// </summary>
        /// <param name="configuration">The application configuration.</param>
        /// <param name="description">The description for the endpoint.</param>
        /// <param name="endpointConfiguration">The configuration to use with the endpoint.</param>
        /// <param name="clientCertificate">The client certificate.</param>
        /// <param name="clientCertificateChain">The client certificate chain.</param>
        /// <param name="messageContext">The message context to use when serializing the messages.</param>
        /// <param name="transportChannelBindings">Optional bindings to use</param>
        /// <param name="ct">The cancellation token</param>
        /// <exception cref="ServiceResultException"></exception>
        /// <exception cref="ArgumentException"></exception>
        internal static async ValueTask<ITransportChannel> CreateUaBinaryChannelAsync(
            ApplicationConfiguration configuration,
            EndpointDescription description,
            EndpointConfiguration endpointConfiguration,
            Certificate? clientCertificate,
            CertificateCollection? clientCertificateChain,
            IServiceMessageContext messageContext,
            ITransportChannelBindings? transportChannelBindings = null,
            CancellationToken ct = default)
        {
            var endpointUrl = new Uri(description.EndpointUrl
                ?? throw new ArgumentException("EndpointUrl cannot be null", nameof(description)));
            string uriScheme = description.TransportProfileUri switch
            {
                Profiles.UaTcpTransport => Utils.UriSchemeOpcTcp,
                Profiles.HttpsBinaryTransport => Utils.UriSchemeOpcHttps,
                Profiles.HttpsJsonTransport => Utils.UriSchemeOpcHttps,
                Profiles.HttpsOpenApiTransport => Utils.UriSchemeOpcHttpsWebApi,
                Profiles.UaWssTransport => Utils.UriSchemeOpcWss,
                Profiles.UaWssJsonTransport => "opc.wss+json",
                Profiles.WssOpenApiTransport => Utils.UriSchemeOpcWssOpenApi,
                _ => endpointUrl.Scheme
            };

            // initialize the channel which will be created with the server.
            transportChannelBindings ??= GetDefaultBindingsLazy();
            ITransportChannel channel =
                transportChannelBindings.Create(uriScheme, messageContext.Telemetry)
                ?? throw ServiceResultException.Create(
                    StatusCodes.BadProtocolVersionUnsupported,
                    "Unsupported transport profile for scheme {0}.",
                    uriScheme);

            if (channel is not ISecureChannel secureChannel)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNotSupported,
                    "The transport channel does not support opening.");
            }

            // create a UA-TCP channel.
            var settings = new TransportChannelSettings
            {
                Description = description,
                Configuration = endpointConfiguration,
                ClientCertificate = clientCertificate,
                ClientCertificateChain = clientCertificateChain
            };

            try
            {
                if (description.ServerCertificate.Length > 0)
                {
                    settings.ServerCertificate = Utils.ParseCertificateBlob(
                        description.ServerCertificate,
                        messageContext.Telemetry);
                }

                if (configuration != null)
                {
                    settings.CertificateValidator = configuration.CertificateManager;
                }

                settings.NamespaceUris = messageContext.NamespaceUris;
                settings.Factory = messageContext.Factory;

                await secureChannel.OpenAsync(endpointUrl, settings, ct).ConfigureAwait(false);
            }
            catch
            {
                // settings.ServerCertificate is allocated above; dispose on
                // failure since the channel never assumed ownership.
                settings.ServerCertificate?.Dispose();
                channel.Dispose();
                throw;
            }
            return channel;
        }

        /// <summary>
        /// Called when the token is changing
        /// </summary>
        /// <param name="channel"></param>
        /// <param name="token"></param>
        /// <param name="previousToken"></param>
        internal void OnChannelTokenActivated(
            ITransportChannel channel,
            ChannelToken? token,
            ChannelToken? previousToken)
        {
            if (token == null || OnDiagnostics == null)
            {
                // Closed
                return;
            }

            if (previousToken == null)
            {
                // Created
            }

            // Get effective ip address and port
            IUaSCByteTransport? transport = (channel as UaSCUaBinaryTransportChannel)?.Transport;
            IPAddress? remoteIpAddress = GetIPAddress(transport?.RemoteEndpoint);
            int remotePort = GetPort(transport?.RemoteEndpoint);
            IPAddress? localIpAddress = GetIPAddress(transport?.LocalEndpoint);
            int localPort = GetPort(transport?.LocalEndpoint);

            OnDiagnostics.Invoke(channel, new TransportChannelDiagnostic
            {
                Endpoint = channel.EndpointDescription,
                TimeStamp = DateTimeOffset.UtcNow,
                RemoteIpAddress = remoteIpAddress,
                RemotePort = remotePort == -1 ? null : remotePort,
                LocalIpAddress = localIpAddress,
                LocalPort = localPort == -1 ? null : localPort,
                ChannelId = token.ChannelId,
                TokenId = token.TokenId,
                CreatedAt = token.CreatedAt,
                Lifetime = TimeSpan.FromMilliseconds(token.Lifetime),
                Client = ToChannelKey(token.ClientInitializationVector,
                    token.ClientEncryptingKey, token.ClientSigningKey),
                Server = ToChannelKey(token.ServerInitializationVector,
                    token.ServerEncryptingKey, token.ServerSigningKey)
            });

            static ChannelKey? ToChannelKey(byte[]? iv, byte[]? key, byte[]? sk)
            {
                if (iv == null ||
                    key == null ||
                    sk == null ||
                    iv.Length == 0 ||
                    key.Length == 0 ||
                    sk.Length == 0)
                {
                    return null;
                }
                return new ChannelKey(iv, key, sk.Length);
            }
        }

        /// <summary>
        /// Get ip address from endpoint if the endpoint is an
        /// IPEndPoint.  Otherwise return null.
        /// </summary>
        /// <param name="endpoint"></param>
        /// <param name="preferv4"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        internal static IPAddress? GetIPAddress(EndPoint? endpoint, bool preferv4 = false)
        {
            if (endpoint is not IPEndPoint ipe)
            {
                return null;
            }
            IPAddress address = ipe.Address;
            if (preferv4 &&
                address.AddressFamily == AddressFamily.InterNetworkV6 &&
                address.IsIPv4MappedToIPv6)
            {
                return address.MapToIPv4();
            }
            return address;
        }

        /// <summary>
        /// Get port from endpoint if the endpoint is an
        /// IPEndPoint.  Otherwise return -1.
        /// </summary>
        /// <param name="endpoint"></param>
        /// <returns></returns>
        internal static int GetPort(EndPoint? endpoint)
        {
            if (endpoint is IPEndPoint ipe)
            {
                return ipe.Port;
            }
            return -1;
        }


        /// <summary>
        /// Initializes a managed client channel factory.
        /// </summary>
        /// <param name="configuration">The application configuration.</param>
        /// <param name="telemetry">Telemetry context for logger creation.</param>
        /// <param name="channelFactory">Optional channel binding registry;
        /// defaults to a <see cref="Opc.Ua.Bindings.DefaultTransportBindingRegistry"/>
        /// pre-seeded with the raw-socket TCP factories when none is supplied.</param>
        /// <param name="reconnectPolicy">Optional channel-level retry
        /// policy. Defaults to
        /// <see cref="ExponentialBackoffChannelReconnectPolicy"/> with
        /// historical 500&#160;ms → 30&#160;s backoff.</param>
        /// <param name="timeProvider">Optional time provider for backoff
        /// timing. Defaults to <see cref="TimeProvider.System"/>.</param>
        /// <param name="options">Optional channel manager options.</param>
        public ClientChannelManager(
            ApplicationConfiguration configuration,
            ITelemetryContext telemetry,
            Bindings.ITransportChannelBindings? channelFactory = null,
            IChannelReconnectPolicy? reconnectPolicy = null,
            TimeProvider? timeProvider = null,
            ChannelManagerOptions? options = null)
            : this(configuration, channelFactory, options)
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
        public async ValueTask ReconnectAsync(
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
            await ReconnectLeaseAsync(lease, budget, throwOnReconnectFailure: false, ct)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask ReconnectAsync(
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

            await ReconnectLeaseAsync(lease, budget, throwOnReconnectFailure: true, ct)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public ValueTask ReconnectAllAsync(CancellationToken ct = default)
        {
            return m_certRotation.ReconnectAllAsync(ct);
        }

        /// <inheritdoc/>
        public IReadOnlyList<ManagedChannelDiagnostic> GetChannelDiagnostics()
        {
            return m_diagnostics.GetDiagnostics(SnapshotEntries());
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
            m_certRotation.UpdateClientCertificate(clientCertificate, clientCertificateChain);
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

        internal static bool IsReactivationInProgress => s_reactivationDepth.Value > 0;

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

        private async ValueTask ReconnectLeaseAsync(
            ManagedTransportChannelLease lease,
            IRetryBudget budget,
            bool throwOnReconnectFailure,
            CancellationToken ct)
        {
            ChannelEntry entry = lease.Entry;
            if (entry.State is ChannelState.Closed or ChannelState.Faulted)
            {
                entry = await SwapFaultedEntryAsync(lease, ct).ConfigureAwait(false);
            }

            Task<bool> reconnectTask = entry.RequestReconnectAsync(budget, ct);
            if (throwOnReconnectFailure)
            {
                await AwaitReconnectResultAsync(reconnectTask).ConfigureAwait(false);
                return;
            }

            _ = await reconnectTask.ConfigureAwait(false);
        }

        private async ValueTask<ChannelEntry> SwapFaultedEntryAsync(
            ManagedTransportChannelLease lease,
            CancellationToken ct)
        {
            ThrowIfDisposed();

            ChannelEntry original = lease.Entry;
            if (original.State is not (ChannelState.Closed or ChannelState.Faulted))
            {
                return original;
            }

            TimeSpan delay = GetSwapDelay(lease.SwapCount);

            await DelaySwapAsync(delay, ct).ConfigureAwait(false);

            original = lease.Entry;
            if (original.State is not (ChannelState.Closed or ChannelState.Faulted))
            {
                return original;
            }

            (Certificate? clientCert, CertificateCollection? clientChain, long clientCertificateVersion) =
                CurrentClientCertificateSnapshot;

            ChannelEntry fresh;
            bool created = false;
            lock (m_entries)
            {
                if (m_entries.TryGetValue(lease.Key, out ChannelEntry? existing) &&
                    existing.State is not (ChannelState.Closed or ChannelState.Faulted))
                {
                    fresh = existing;
                }
                else
                {
                    if (!m_entries.ContainsKey(lease.Key))
                    {
                        ThrowIfMaxChannelsReached();
                    }

                    fresh = new ChannelEntry(this, lease.Key, lease.Endpoint, lease.ReverseConnection);
                    m_entries[lease.Key] = fresh;
                    created = true;
                }
            }

            if (created)
            {
                try
                {
                    await fresh.OpenInitialAsync(clientCert, clientChain, clientCertificateVersion, ct)
                        .ConfigureAwait(false);
                }
                catch
                {
                    lock (m_entries)
                    {
                        if (m_entries.TryGetValue(lease.Key, out ChannelEntry? existing) &&
                            ReferenceEquals(existing, fresh))
                        {
                            m_entries.Remove(lease.Key);
                        }
                    }
                    await fresh.DisposeAsync(ChannelCloseReason.Faulted).ConfigureAwait(false);
                    throw;
                }
            }

            // The lease was marked released when the original entry tore down
            // during its Faulted disposal. Re-activate it so ReattachParticipant
            // (which guards against attaching released leases) can re-establish
            // the bookkeeping on the fresh entry. This is safe because we hold
            // the only reference to the lease as the caller of ReconnectAsync.
            lease.MarkActiveForSwap();

            fresh.ReattachParticipant(lease, lease.ParticipantFactory);
            if (!ReferenceEquals(original, fresh))
            {
                await original.DetachLeaseForSwapAsync(lease).ConfigureAwait(false);
                lease.RecordSwap();
            }
            return fresh;
        }

        private TimeSpan GetSwapDelay(int swapAttempt)
        {
            TimeSpan delay = GetReconnectPolicyDelay(swapAttempt);
            while (delay < TimeSpan.Zero && swapAttempt > 0)
            {
                swapAttempt--;
                delay = GetReconnectPolicyDelay(swapAttempt);
            }

            return delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
        }

        private TimeSpan GetReconnectPolicyDelay(int attempt)
        {
#if NETSTANDARD2_1 || NET8_0_OR_GREATER
            return m_reconnectPolicy.GetDelay(attempt, budget: null);
#else
            return ChannelReconnectPolicyBudget.GetDelay(
                m_reconnectPolicy,
                attempt,
                budget: null);
#endif
        }

        private Task DelaySwapAsync(TimeSpan delay, CancellationToken ct)
        {
            if (delay <= TimeSpan.Zero)
            {
                return Task.CompletedTask;
            }

            return m_timeProvider.Delay(delay, ct);
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
                bool found = m_entries.TryGetValue(key, out ChannelEntry? existing);
                if (!found ||
                    existing!.State is ChannelState.Closed or ChannelState.Faulted)
                {
                    if (!found)
                    {
                        ThrowIfMaxChannelsReached();
                    }

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

        private void ThrowIfMaxChannelsReached()
        {
            if (m_options.MaxChannels > 0 && m_entries.Count >= m_options.MaxChannels)
            {
                throw new ServiceResultException(
                    StatusCodes.BadResourceUnavailable,
                    $"ClientChannelManager has reached MaxChannels cap ({m_options.MaxChannels}). " +
                    "Dispose unused channels or raise the limit via ChannelManagerOptions.MaxChannels.");
            }

            // TODO: Emit a debounced warning when channel count reaches 90% of MaxChannels.
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
        private readonly ChannelManagerOptions m_options;
        private Certificate? m_clientCertificate;
        private CertificateCollection? m_clientCertificateChain;
        private long m_clientCertificateVersion;
        private readonly IChannelReconnectPolicy m_reconnectPolicy
            = new ExponentialBackoffChannelReconnectPolicy();
        private readonly TimeProvider m_timeProvider = TimeProvider.System;
        private readonly ILogger? m_logger;

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref m_disposed, 1) != 0)
            {
                return;
            }

            m_shutdownCts.Cancel();
            DisposeCertificateRotation();

            Certificate? clientCertificate;
            CertificateCollection? clientCertificateChain;
            lock (m_certLock)
            {
                clientCertificate = m_clientCertificate;
                clientCertificateChain = m_clientCertificateChain;
                m_clientCertificate = null;
                m_clientCertificateChain = null;
            }
            clientCertificate?.Dispose();
            clientCertificateChain?.Dispose();

            ChannelEntry[] snapshot;
            lock (m_entries)
            {
                snapshot = [.. m_entries.Values];
                m_entries.Clear();
            }

            foreach (ChannelEntry entry in snapshot)
            {
                await entry.DisposeAsync(ChannelCloseReason.ManagerDisposed)
                    .ConfigureAwait(false);
            }

            m_meter?.Dispose();
            m_shutdownCts.Dispose();
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref m_disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(ClientChannelManager));
            }
        }

        private void RecordChannelOpen(ChannelEntry entry)
        {
            m_metrics?.RecordChannelOpen(entry);
        }

        private void RecordChannelClosed(ChannelEntry entry, ChannelCloseReason reason)
        {
            m_metrics?.RecordChannelClosed(entry, reason);
        }

        private void RecordChannelActiveChanged(ChannelEntry entry, long delta)
        {
            m_metrics?.RecordChannelActiveChanged(entry, delta);
        }

        private void RecordReconnectAttempt(ChannelEntry entry, string outcome)
        {
            m_metrics?.RecordReconnectAttempt(entry, outcome);
        }

        private void RecordReconnectDuration(ChannelEntry entry, TimeSpan duration, string outcome)
        {
            m_metrics?.RecordReconnectDuration(entry, duration, outcome);
        }

        private void RecordGateWait(ChannelEntry entry, TimeSpan duration)
        {
            m_metrics?.RecordGateWait(entry, duration);
        }

        private void RecordParticipantTimeout(ChannelEntry entry, string participantId)
        {
            m_metrics?.RecordParticipantTimeout(entry, participantId);
        }

        private void RecordParticipantRecreate(ChannelEntry entry, string participantId, bool success)
        {
            m_metrics?.RecordParticipantRecreate(entry, participantId, success);
        }

        internal ChannelEntry[] GetMetricEntriesSnapshot()
        {
            lock (m_entries)
            {
                return [.. m_entries.Values];
            }
        }

        internal enum ChannelCloseReason
        {
            LeaseReleased,
            ManagerDisposed,
            Faulted
        }


        private readonly Meter? m_meter;
        private readonly ClientChannelManagerMetrics? m_metrics;
        private readonly CancellationTokenSource m_shutdownCts = new();
        private int m_disposed;

        private void WireCertificateRotation()
        {
            m_certRotation.WireCertificateRotation();
        }

        private void DisposeCertificateRotation()
        {
            m_certRotation.DisposeCertificateRotation();
        }

        ApplicationConfiguration IChannelCertRotationHost.Configuration => m_configuration;

        ILogger? IChannelCertRotationHost.Logger => m_logger;

        bool IChannelCertRotationHost.IsDisposed => Volatile.Read(ref m_disposed) != 0;

        internal ChannelEntry[] SnapshotEntries()
        {
            lock (m_entries)
            {
                return [.. m_entries.Values];
            }
        }

        ChannelEntry[] IChannelCertRotationHost.SnapshotEntries()
        {
            return SnapshotEntries();
        }

        void IChannelCertRotationHost.ReplaceClientCertificate(
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

        void IChannelCertRotationHost.SetCertificateRotationTask(Task? task)
        {
            m_certificateRotationTask = task;
        }

        private readonly ClientChannelManagerCertRotation m_certRotation;
        private Task? m_certificateRotationTask;

        ILogger? IChannelEntryHost.Logger => m_logger;

        TimeProvider IChannelEntryHost.TimeProvider => m_timeProvider;

        IChannelReconnectPolicy IChannelEntryHost.ReconnectPolicy => m_reconnectPolicy;

        CancellationToken IChannelEntryHost.ShutdownToken => m_shutdownCts.Token;

        Bindings.ITransportChannelBindings? IChannelEntryHost.ChannelFactory => m_channelFactory;

        ApplicationConfiguration IChannelEntryHost.Configuration => m_configuration;

        void IChannelEntryHost.OnEntryStateChanged(ChannelEntry entry, ChannelStateChange change)
        {
            m_diagnostics.EmitStateChanged(entry, change);
        }

        void IChannelEntryHost.OnEntryClosed(ChannelEntry entry, ChannelCloseReason reason)
        {
            RecordChannelClosed(entry, reason);
            m_diagnostics.EmitChannelClosed(entry, reason);
        }

        (Certificate? Certificate, CertificateCollection? Chain, long Version)
            IChannelEntryHost.SnapshotClientCertificate()
        {
            return CurrentClientCertificateSnapshot;
        }

        ValueTask<ITransportChannel> IChannelEntryHost.CreateChannelAsync(
            ConfiguredEndpoint endpoint,
            Certificate? clientCertificate,
            CertificateCollection? clientCertificateChain,
            ITransportWaitingConnection? reverseConnection,
            CancellationToken ct)
        {
            IServiceMessageContext context = m_configuration.CreateMessageContext();
            return CreateChannelAsync(
                endpoint,
                context,
                clientCertificate,
                clientCertificateChain,
                reverseConnection,
                ct);
        }

        Activity? IChannelEntryHost.StartReconnectActivity(ChannelEntry entry)
        {
            return m_diagnostics.StartReconnectActivity(entry);
        }

        void IChannelEntryHost.CompleteReconnectActivity(
            Activity? activity,
            ChannelEntry entry,
            int attemptCount,
            string outcome,
            ServiceResult? error)
        {
            m_diagnostics.CompleteReconnectActivity(activity, entry, attemptCount, outcome, error);
        }

        void IChannelEntryHost.OnEntryReconnectFailed(
            ChannelEntry entry,
            int attempt,
            string outcome,
            ServiceResult? error)
        {
            m_diagnostics.EmitReconnectFailed(entry, attempt, outcome, error);
            RecordReconnectAttempt(entry, outcome);
        }

        void IChannelEntryHost.OnEntryOpened(ChannelEntry entry)
        {
            RecordChannelOpen(entry);
            m_diagnostics.EmitChannelOpened(entry);
        }

        void IChannelEntryHost.OnEntryParticipantAttached(
            ChannelEntry entry,
            string participantId,
            int refCount,
            int participantCount)
        {
            m_diagnostics.EmitParticipantAttached(entry, participantId, refCount, participantCount);
        }

        void IChannelEntryHost.OnEntryParticipantDetached(
            ChannelEntry entry,
            string participantId,
            int refCount,
            int participantCount)
        {
            m_diagnostics.EmitParticipantDetached(entry, participantId, refCount, participantCount);
        }

        void IChannelEntryHost.RecordChannelOpen(ChannelEntry entry)
        {
            RecordChannelOpen(entry);
        }

        void IChannelEntryHost.RecordChannelActiveChanged(ChannelEntry entry, long delta)
        {
            RecordChannelActiveChanged(entry, delta);
        }

        void IChannelEntryHost.RecordReconnectAttempt(ChannelEntry entry, string outcome)
        {
            RecordReconnectAttempt(entry, outcome);
        }

        void IChannelEntryHost.RecordReconnectDuration(
            ChannelEntry entry,
            TimeSpan duration,
            string outcome)
        {
            RecordReconnectDuration(entry, duration, outcome);
        }

        void IChannelEntryHost.RecordGateWait(ChannelEntry entry, TimeSpan duration)
        {
            RecordGateWait(entry, duration);
        }

        void IChannelEntryHost.RecordParticipantTimeout(ChannelEntry entry, string participantId)
        {
            RecordParticipantTimeout(entry, participantId);
        }

        void IChannelEntryHost.RecordParticipantRecreate(ChannelEntry entry, string participantId, bool success)
        {
            RecordParticipantRecreate(entry, participantId, success);
        }

        void IChannelEntryHost.RemoveEntryIfPresent(ManagedChannelKey key, ChannelEntry entry)
        {
            RemoveEntryIfPresent(key, entry);
        }

        void IChannelEntryHost.CloseChannel(ITransportChannel channel)
        {
            CloseChannel(channel);
        }

        /// <summary>
        /// Returns a lazily-initialized
        /// <see cref="DefaultTransportBindingRegistry"/> seeded with the
        /// raw-socket TCP factories AND any optional HTTPS / WSS
        /// factories from <c>Opc.Ua.Bindings.Https</c> when that
        /// assembly is already loaded into the current
        /// <see cref="AppDomain"/>. The fallback is used by the static
        /// <c>CreateUaBinaryChannelAsync</c> paths when the caller did
        /// not supply an <see cref="ITransportChannelBindings"/>.
        /// </summary>
        /// <remarks>
        /// DI consumers do not hit this path - they receive a
        /// dependency-resolved <see cref="ITransportBindingRegistry"/>
        /// from their own <see cref="System.IServiceProvider"/>.
        /// </remarks>
        private static DefaultTransportBindingRegistry GetDefaultBindingsLazy()
        {
            return s_defaultBindings.Value;
        }

        [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
            "Trimming", "IL2026",
            Justification = "Pre-DI fallback path only; DI consumers receive an explicit registry.")]
        private static DefaultTransportBindingRegistry CreateDefaultBindingsRegistry()
        {
            return DefaultTransportBindingRegistry.WithDefaultBindings();
        }

        private static readonly Lazy<DefaultTransportBindingRegistry> s_defaultBindings = new(
            CreateDefaultBindingsRegistry,
            System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);

        private readonly ApplicationConfiguration m_configuration;
        private readonly ClientChannelManagerDiagnostics m_diagnostics = new();
        private readonly ITransportChannelBindings? m_channelFactory;
    }

    /// <summary>
    /// Point-in-time diagnostics for a managed client channel.
    /// </summary>
    /// <param name="Key">The managed channel identity.</param>
    /// <param name="State">The current lifecycle state.</param>
    /// <param name="Refcount">The current lease reference count.</param>
    /// <param name="ParticipantCount">The number of active participants.</param>
    /// <param name="OpenedAt">The timestamp when the current transport was opened.</param>
    /// <param name="LastStateChange">The timestamp of the last state transition.</param>
    /// <param name="LastReconnectAttempt">The last reconnect attempt associated with the state.</param>
    /// <param name="LastError">The last error associated with the state, if any.</param>
    public sealed record ManagedChannelDiagnostic(
        ManagedChannelKey Key,
        ChannelState State,
        int Refcount,
        int ParticipantCount,
        DateTimeOffset OpenedAt,
        DateTimeOffset LastStateChange,
        int LastReconnectAttempt,
        ServiceResult? LastError);
}
