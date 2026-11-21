// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Bindings;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Admission control of a <see cref="StandardServer"/>: the server-owned resource
    /// isolation and rate limiting, the session-establishment permits that
    /// CreateSession and ActivateSession acquire, and the listener settings derived
    /// from them.
    /// </summary>
    /// <remarks>
    /// The component only owns what the server created itself. A provider or budget
    /// that the host supplied through the public server properties is borrowed and
    /// never replaced or disposed.
    /// </remarks>
    [SuppressMessage(
        "Design",
        "CA1001:Types that own disposable fields should be disposable",
        Justification = "The owning server releases the rate limiter and the isolation provider as separate shutdown stages.")]
    internal sealed class SessionAdmission
    {
        /// <summary>
        /// The prefix of the machine-readable retry-after token encoded in a
        /// <c>BadServerTooBusy</c> fault's <c>AdditionalInfo</c> (for example
        /// <c>RetryAfterMs=2000</c>). The client's reconnect path parses this;
        /// the literal is duplicated in the client and must stay in sync.
        /// </summary>
        public const string RetryAfterHintPrefix = "RetryAfterMs=";

        /// <summary>
        /// Creates the admission control of a server.
        /// </summary>
        /// <param name="server">The server whose admission is controlled.</param>
        /// <param name="timeProvider">The time provider of the server.</param>
        public SessionAdmission(StandardServer server, TimeProvider timeProvider)
        {
            m_server = server;
            m_timeProvider = timeProvider;
        }

        /// <summary>
        /// The rate limiter provider, server-owned or host-supplied.
        /// </summary>
        public IServerRateLimiterProvider? RateLimiterProvider
        {
            get => m_rateLimiterProvider;
            set
            {
                // Dispose a provider the server created before replacing it, so its
                // RateLimiter timers are not leaked. Caller-supplied providers are
                // owned by the caller and left untouched.
                if (m_ownsRateLimiterProvider &&
                    !ReferenceEquals(m_rateLimiterProvider, value))
                {
                    m_rateLimiterProvider?.Dispose();
                }

                m_rateLimiterProvider = value;
                m_ownsRateLimiterProvider = false;
            }
        }

        /// <summary>
        /// The parking policy the endpoints use: the host policy, plus CreateSession and
        /// ActivateSession while a queueing session-establishment limiter is active, so a
        /// handshake waiting for a permit releases its request worker like a held Publish.
        /// </summary>
        public IRequestParkingPolicy? RequestParkingPolicy =>
            m_rateLimiterProvider is IQueuedSessionEstablishmentLimiter &&
            (!m_ownsRateLimiterProvider || m_server.RateLimitOptions.SessionEstablishmentQueueLimit > 0)
                ? m_sessionEstablishmentParking ??= new SessionEstablishmentParkingPolicy(m_server)
                : m_server.RequestParkingPolicy;

        /// <summary>
        /// Injects the configured handshake timeout, listener backlog and connection
        /// rate limiter into the transport listener settings.
        /// </summary>
        /// <param name="settings">The listener settings being assembled.</param>
        public void ConfigureTransportListenerSettings(TransportListenerSettings settings)
        {
            settings.HandshakeTimeout = m_server.ResourceIsolationOptions.HandshakeTimeout;
            IServerRateLimiterProvider? provider = m_rateLimiterProvider;
            if (provider != null)
            {
                if (provider.ListenBacklog > 0)
                {
                    settings.ListenBacklog = provider.ListenBacklog;
                }

                settings.ConnectionRateLimiter = provider.ConnectionRateLimiter;
            }
        }

        /// <summary>
        /// Recreates server-owned isolation from validated limits without replacing a borrowed provider or budget.
        /// </summary>
        public void InitializeResourceIsolation(
            ApplicationConfiguration configuration,
            ITelemetryContext telemetry)
        {
            StandardServer server = m_server;
            server.ResourceIsolationOptions.ValidateHandshakeTimeout();
            if ((server.ResourceIsolationProvider != null &&
                !ReferenceEquals(server.ResourceIsolationProvider, m_ownedResourceIsolationProvider)) ||
                server.ResourceIsolationOptions.Mode == ServerResourceIsolationMode.SharedOnly)
            {
                ResetOwnedResourceIsolation();
                return;
            }
            bool ownsBudget = server.ChunkReassemblyBudget == null ||
                ReferenceEquals(server.ChunkReassemblyBudget, m_ownedChunkReassemblyBudget);
            ChunkReassemblyBudget budget = ownsBudget
                ? global::Opc.Ua.Bindings.ChunkReassemblyBudget.CreateDefault(
                    EndpointConfiguration.Create(configuration))
                : server.ChunkReassemblyBudget!;
            ServerResourceIsolationPlan plan = server.ResourceIsolationOptions.CreateRuntimePlan(
                configuration, server.RateLimitOptions, budget);
            var provider = new DefaultServerResourceIsolationProvider(
                plan, telemetry, server.SessionBindingProvider ?? server, server.ResourceIsolationClassifier);
            ResetOwnedResourceIsolation();
            m_ownedResourceIsolationProvider = provider;
            if (ownsBudget)
            {
                m_ownedChunkReassemblyBudget = budget;
                server.ChunkReassemblyBudget = budget;
            }
            server.ResourceIsolationProvider = m_ownedResourceIsolationProvider;
        }

        /// <summary>
        /// Recreates server-owned rate limits against the active isolation plan while preserving borrowed providers.
        /// </summary>
        public void InitializeRateLimiting()
        {
            if (m_rateLimiterProvider != null && !m_ownsRateLimiterProvider)
            {
                return;
            }
            var provider = new DefaultServerRateLimiterProvider(
                m_server.RateLimitOptions,
                m_server.ResourceIsolationProvider as DefaultServerResourceIsolationProvider,
                m_timeProvider);
            m_rateLimiterProvider?.Dispose();
            m_rateLimiterProvider = provider;
            m_ownsRateLimiterProvider = true;
        }

        /// <summary>
        /// Releases a server-owned rate limiter provider and forgets the current provider.
        /// </summary>
        public void DisposeOwnedRateLimiterProvider()
        {
            if (m_ownsRateLimiterProvider)
            {
                m_rateLimiterProvider?.Dispose();
            }
            m_rateLimiterProvider = null;
        }

        /// <summary>
        /// Acquires an admission permit for a single session establishment
        /// operation (<c>CreateSession</c> / <c>ActivateSession</c>), throwing
        /// <c>BadServerTooBusy</c> when the server is at capacity.
        /// </summary>
        /// <returns>
        /// A lease that MUST be disposed when the operation completes, or
        /// <c>null</c> when neither isolation nor rate limiting supplies a lease.
        /// </returns>
        /// <exception cref="ServiceResultException">
        /// The server is too busy to admit the operation.
        /// </exception>
        /// <exception cref="ServerBusyException"></exception>
        public IDisposable? BeginSessionEstablishmentOrThrow(
            SecureChannelContext channelContext,
            NodeId authenticationToken)
        {
            IDisposable? isolationLease = null;
            IDisposable? rateLimitLease = null;
            try
            {
                IServerResourceIsolationProvider? isolation = m_server.ResourceIsolationProvider;
                if (isolation != null && !isolation.TryAcquire(
                    ResourceIsolationStage.SessionEstablishment,
                    isolation.Classify(channelContext, authenticationToken, sessionEstablishment: true),
                    1,
                    out isolationLease,
                    out ResourceIsolationFailure failure))
                {
                    throw CreateServerTooBusyException(failure.RetryAfter);
                }
                IServerRateLimiterProvider? provider = m_rateLimiterProvider;
                if (provider != null && !provider.TryAcquireSessionEstablishment(
                    out rateLimitLease, out TimeSpan? retryAfter))
                {
                    throw CreateServerTooBusyException(retryAfter);
                }
                IDisposable? lease = isolationLease;
                if (lease == null)
                {
                    lease = rateLimitLease;
                }
                else if (rateLimitLease != null)
                {
                    lease = new SessionEstablishmentLease(lease, rateLimitLease);
                }
                return lease;
            }
            catch
            {
                try
                {
                    rateLimitLease?.Dispose();
                }
                finally
                {
                    isolationLease?.Dispose();
                }
                throw;
            }
        }

        /// <summary>
        /// Acquires the session-establishment permits like
        /// <see cref="BeginSessionEstablishmentOrThrow"/>, but lets a queueing rate limiter
        /// (<see cref="IQueuedSessionEstablishmentLimiter"/>, see
        /// ServerRateLimitOptions.SessionEstablishmentQueueLimit) wait for a permit.
        /// </summary>
        /// <returns>A lease that MUST be disposed when the operation completes, or <c>null</c>.</returns>
        /// <exception cref="ServiceResultException">The server is too busy to admit the operation.</exception>
        public async ValueTask<IDisposable?> BeginSessionEstablishmentOrThrowAsync(
            SecureChannelContext channelContext,
            NodeId authenticationToken,
            RequestLifetime requestLifetime)
        {
            if (m_rateLimiterProvider is not IQueuedSessionEstablishmentLimiter queued)
            {
                return BeginSessionEstablishmentOrThrow(channelContext, authenticationToken);
            }

            IDisposable? isolationLease = null;
            try
            {
                IServerResourceIsolationProvider? isolation = m_server.ResourceIsolationProvider;
#pragma warning disable CA2000 // disposed in the catch below or owned by the returned lease
                if (isolation != null && !isolation.TryAcquire(
                    ResourceIsolationStage.SessionEstablishment,
                    isolation.Classify(channelContext, authenticationToken, sessionEstablishment: true),
                    1,
                    out isolationLease,
                    out ResourceIsolationFailure failure))
#pragma warning restore CA2000
                {
                    throw CreateServerTooBusyException(failure.RetryAfter);
                }

                ValueTask<(bool Acquired, IDisposable? Lease, TimeSpan? RetryAfter)> pending =
                    queued.AcquireSessionEstablishmentAsync(requestLifetime.CancellationToken);
                if (!pending.IsCompleted)
                {
                    // The operation waits in the limiter queue: release the request
                    // worker (like a held Publish) so queued handshakes cannot starve
                    // the requests of established sessions.
                    requestLifetime.ParkSink?.NotifyParked();
                }
                (bool acquired, IDisposable? rateLimitLease, TimeSpan? retryAfter) =
                    await pending.ConfigureAwait(false);
                if (!acquired)
                {
                    throw CreateServerTooBusyException(retryAfter);
                }

                if (isolationLease == null)
                {
                    return rateLimitLease;
                }
                return new SessionEstablishmentLease(isolationLease, rateLimitLease);
            }
            catch
            {
                isolationLease?.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Releases only server-owned isolation resources, preserving host replacements.
        /// </summary>
        public void ResetOwnedResourceIsolation()
        {
            StandardServer server = m_server;
            if (m_ownedResourceIsolationProvider != null)
            {
                if (ReferenceEquals(server.ResourceIsolationProvider, m_ownedResourceIsolationProvider))
                {
                    server.ResourceIsolationProvider = null;
                }
                m_ownedResourceIsolationProvider.Dispose();
                m_ownedResourceIsolationProvider = null;
            }
            if (m_ownedChunkReassemblyBudget != null)
            {
                if (ReferenceEquals(server.ChunkReassemblyBudget, m_ownedChunkReassemblyBudget))
                {
                    server.ChunkReassemblyBudget = null;
                }
                m_ownedChunkReassemblyBudget = null;
            }
        }

        /// <summary>
        /// Creates a <c>BadServerTooBusy</c> fault carrying a retry-after hint so a
        /// cooperating client can back off deterministically.
        /// </summary>
        /// <remarks>
        /// The hint is encoded in the fault's <c>AdditionalInfo</c> as a
        /// machine-readable <c>RetryAfterMs=N</c> token (parsed by the client's
        /// adaptive reconnect policy) as well as in the human-readable message.
        /// Note that <c>AdditionalInfo</c> only reaches the client when it requests
        /// diagnostics; otherwise the client relies on its server-busy backoff.
        /// </remarks>
        /// <param name="retryAfter">The suggested wait before retrying, if known.</param>
        /// <returns>The exception to throw.</returns>
        private static ServerBusyException CreateServerTooBusyException(TimeSpan? retryAfter)
        {
            if (retryAfter.HasValue)
            {
                long retryAfterMs = (long)Math.Ceiling(retryAfter.Value.TotalMilliseconds);
                string additionalInfo = Utils.Format("{0}{1}", RetryAfterHintPrefix, retryAfterMs);
                return new ServerBusyException(
                    new ServiceResult(
                        null,
                        StatusCodes.BadServerTooBusy,
                        new LocalizedText(
                            Utils.Format(
                                "The server is too busy to establish a session. Retry after {0} ms.",
                                retryAfterMs)),
                        additionalInfo),
                    retryAfter);
            }

            return new ServerBusyException(
                new ServiceResult(
                    StatusCodes.BadServerTooBusy,
                    new LocalizedText("The server is too busy to establish a session.")),
                null);
        }

        /// <summary>
        /// Adds session establishment to the host-supplied parking policy.
        /// </summary>
        private sealed class SessionEstablishmentParkingPolicy(StandardServer server) : IRequestParkingPolicy
        {
            /// <inheritdoc/>
            public bool CanPark(IServiceRequest request)
            {
                return request is CreateSessionRequest or ActivateSessionRequest ||
                    server.RequestParkingPolicy?.CanPark(request) == true;
            }
        }

        /// <summary>
        /// Releases both session-establishment permits once, even if rate-limit cleanup fails.
        /// </summary>
        /// <param name="isolation">Owned resource-isolation permit.</param>
        /// <param name="rateLimit">Optional owned rate-limit permit.</param>
        private sealed class SessionEstablishmentLease(IDisposable isolation, IDisposable? rateLimit) : IDisposable
        {
            /// <summary>
            /// Returns both permits without allowing repeated disposal to release capacity twice.
            /// </summary>
            public void Dispose()
            {
                if (Interlocked.Exchange(ref m_disposed, 1) != 0)
                {
                    return;
                }
                try
                {
                    m_rateLimit?.Dispose();
                }
                finally
                {
                    m_isolation.Dispose();
                }
            }

            /// <summary>
            /// Resource-isolation permit released even when rate-limit cleanup throws.
            /// </summary>
            private readonly IDisposable m_isolation = isolation;

            /// <summary>
            /// Rate-limit permit, if this operation was subject to rate limiting.
            /// </summary>
            private readonly IDisposable? m_rateLimit = rateLimit;

            /// <summary>
            /// Claims ownership of releasing both permits exactly once.
            /// </summary>
            private int m_disposed;
        }

        private readonly StandardServer m_server;
        private readonly TimeProvider m_timeProvider;
        private IServerRateLimiterProvider? m_rateLimiterProvider;
        private bool m_ownsRateLimiterProvider;
        private DefaultServerResourceIsolationProvider? m_ownedResourceIsolationProvider;
        private ChunkReassemblyBudget? m_ownedChunkReassemblyBudget;
        private SessionEstablishmentParkingPolicy? m_sessionEstablishmentParking;
    }
}
