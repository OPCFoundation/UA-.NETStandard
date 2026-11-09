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
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Redundancy;

namespace Opc.Ua.Client.Redundancy
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: a client replica set. Two or more client
    /// processes elect a leader (via <see cref="ILeaderElection"/>) that holds the
    /// active <see cref="ManagedSession"/> and subscriptions; followers stand by per
    /// <see cref="ClientStandbyMode"/> and take over on leader loss. The leader
    /// publishes a protected <see cref="SessionHandoff"/> of its server session through an
    /// <see cref="ISharedKeyValueStore"/> so a promoted follower can create its leader
    /// session as a takeover of that server session (reusing the AuthenticationToken to
    /// ActivateSession against a HotAndMirrored server), or fall back to its standby session
    /// and recreate or transfer the subscriptions otherwise.
    /// </summary>
    public sealed class ClientReplicaCoordinator : IAsyncDisposable
    {
        /// <summary>
        /// Creates a client replica coordinator.
        /// </summary>
        public ClientReplicaCoordinator(
            ClientReplicaOptions options,
            ILeaderElection election,
            ISharedKeyValueStore sessionStore,
            IRecordProtector recordProtector,
            ITelemetryContext telemetry)
        {
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            m_election = election ?? throw new ArgumentNullException(nameof(election));
            m_store = sessionStore ?? throw new ArgumentNullException(nameof(sessionStore));
            m_protector = recordProtector ?? throw new ArgumentNullException(nameof(recordProtector));
            if (telemetry == null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            if (options.CreateSessionAsync == null)
            {
                throw new ArgumentException(
                    "ClientReplicaOptions.CreateSessionAsync must be set.", nameof(options));
            }

            // Fail-closed: secrets must not be written in cleartext to a networked store.
            if (sessionStore is not InMemorySharedKeyValueStore && recordProtector is NullRecordProtector)
            {
                throw new InvalidOperationException(
                    "A non-in-memory session store requires a real IRecordProtector; refusing to mirror " +
                    "client session secrets in cleartext.");
            }

            m_logger = telemetry.CreateLogger<ClientReplicaCoordinator>();
            m_telemetry = telemetry;
            m_backgroundWork = new BackgroundTaskScope(
                nameof(ClientReplicaCoordinator),
                telemetry,
                maxConcurrency: 1);
            m_election.LeadershipChanged += OnLeadershipChanged;
        }

        /// <summary>
        /// True when this replica is the active leader.
        /// </summary>
        public bool IsLeader => m_election.IsLeader;

        /// <summary>
        /// The active managed session, or null when this replica has none yet.
        /// </summary>
        public ManagedSession? CurrentSession => m_session;

        /// <summary>
        /// Raised after this replica's role changes; true when promoted to leader.
        /// </summary>
        public event Action<bool>? RoleChanged;

        /// <summary>
        /// Starts election and, for Warm/Hot standby, connects the standby session.
        /// </summary>
        public async ValueTask StartAsync(CancellationToken ct = default)
        {
            if (m_options.Mode != ClientStandbyMode.Cold)
            {
                m_session = await m_options.CreateSessionAsync!(ct).ConfigureAwait(false);
            }
            m_election.Start();
        }

        private void OnLeadershipChanged(bool isLeader)
        {
            m_backgroundWork.Run(
                nameof(HandleRoleChangeAsync),
                async _ => await HandleRoleChangeAsync(isLeader).ConfigureAwait(false));
        }

        private async Task HandleRoleChangeAsync(bool isLeader)
        {
            try
            {
                isLeader = m_election.IsLeader;
                if (isLeader)
                {
                    await PromoteToLeaderAsync(m_cts.Token).ConfigureAwait(false);
                    if (m_election.IsLeader)
                    {
                        RaiseRoleChanged(true);
                    }
                    return;
                }

                ManagedSession? demoted = null;
                bool abandon = false;
                if (m_session != null &&
                    (m_options.Mode == ClientStandbyMode.Cold || m_sessionServedLeader))
                {
                    // A Warm/Hot session that served as leader still carries the leader's
                    // subscriptions and (with token reuse) the shared AuthenticationToken.
                    // Kept alive, it would keep publishing next to the new leader and its
                    // reconnects would take the mirrored session back. Tear it down and,
                    // for Warm/Hot, reconnect a genuine standby session.
                    demoted = m_session;
                    // With token reuse the new leader reactivates this very server
                    // session: stop using it locally but never CloseSession it.
                    abandon = m_options.EnableTokenReuse && m_sessionServedLeader;
                    m_session = null;
                    m_sessionServedLeader = false;
                    m_adoptedSession = null;
                    demoted.SessionConfigurationChanged -= OnSessionConfigurationChanged;
                    if (ReferenceEquals(m_publishedSession, demoted))
                    {
                        m_publishedSession = null;
                    }
                }

                if (demoted != null)
                {
                    await DisposeDemotedSessionAsync(demoted, abandon).ConfigureAwait(false);
                }

                // The demotion is reported once the leader session is stopped, before and
                // independent of the standby connect: a slow or failing connect must not
                // keep the application acting as leader.
                if (!m_election.IsLeader)
                {
                    RaiseRoleChanged(false);
                }

                if (demoted != null &&
                    m_options.Mode != ClientStandbyMode.Cold &&
                    !m_election.IsLeader &&
                    m_session == null)
                {
                    await ConnectStandbySessionAsync(m_cts.Token).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                m_logger.ClientReplicaRoleChangeLeaderIsLeader(
                    ex,
                    isLeader);
            }
        }

        private void RaiseRoleChanged(bool isLeader)
        {
            try
            {
                RoleChanged?.Invoke(isLeader);
            }
            catch (Exception ex)
            {
                m_logger.ClientReplicaRoleChangeLeaderIsLeader(ex, isLeader);
            }
        }

        private async ValueTask DisposeDemotedSessionAsync(ManagedSession demoted, bool abandon)
        {
            try
            {
                if (abandon)
                {
                    await demoted.AbandonServerSessionAsync().ConfigureAwait(false);
                }
                else
                {
                    await demoted.DisposeAsync().ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                m_logger.DisposingDemotedLeaderSessionFailed(ex);
            }
        }

        private async ValueTask ConnectStandbySessionAsync(CancellationToken ct)
        {
            try
            {
                m_session = await m_options.CreateSessionAsync!(ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The replica stays without a standby session; the next promotion
                // creates one (EnsureLeaderSessionAsync) like a cold standby would.
                m_logger.ConnectingStandbySessionAfterDemotionFailed(ex);
            }
        }

        private async Task PromoteToLeaderAsync(CancellationToken ct)
        {
            int attempt = 0;

            while (!ct.IsCancellationRequested && m_election.IsLeader)
            {
                try
                {
                    bool fastActivated = await EnsureLeaderSessionAsync(ct).ConfigureAwait(false);
                    m_sessionServedLeader = m_session != null;
                    if (m_options.ConfigureLeaderAsync != null && m_session != null)
                    {
                        await m_options.ConfigureLeaderAsync(m_session, fastActivated, ct)
                            .ConfigureAwait(false);
                    }
                    SubscribeToSessionConfigurationChanges();
                    if (!m_election.IsLeader)
                    {
                        return;
                    }
                    await PublishSecretsAsync(ct).ConfigureAwait(false);
                    return;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    attempt++;
                    TimeSpan delay = GetPromotionRetryDelay(attempt);
                    m_logger.ClientReplicaPromotionAttemptAttemptFailed(
                        ex,
                        attempt,
                        delay);
                    await Task.Delay(delay, ct).ConfigureAwait(false);
                }
            }
        }

        private async ValueTask<bool> EnsureLeaderSessionAsync(CancellationToken ct)
        {
            m_session ??= await m_options.CreateSessionAsync!(ct).ConfigureAwait(false);
            if (!m_options.EnableTokenReuse)
            {
                return false;
            }
            if (ReferenceEquals(m_session, m_adoptedSession))
            {
                // A retry after a failed configure/publish step: this session already
                // took over the leader's server session.
                return true;
            }

            try
            {
                (bool found, ByteString stored) = await m_store
                    .TryGetAsync(m_options.SessionRecordKey, ct).ConfigureAwait(false);
                if (!found ||
                    !SessionHandoffCodec.TryUnprotect(
                        stored,
                        m_protector,
                        RecordProtectionContext.Create("client-replica-session", m_options.SessionRecordKey),
                        m_telemetry,
                        out SessionHandoff? handoff))
                {
                    return false;
                }

                IUserIdentity? identity = null;
                if (handoff!.Session.IdentityToken is UserNameIdentityToken savedIdentity &&
                    m_session.Identity?.TokenHandler?.Token is UserNameIdentityToken liveIdentity &&
                    string.Equals(savedIdentity.UserName, liveIdentity.UserName, StringComparison.Ordinal))
                {
                    identity = m_session.Identity;
                }

                // The leader session is a new session that takes the server session over; the
                // standby session keeps its own server session until the takeover succeeded and
                // is then closed like any other session.
                ManagedSession standby = m_session;
                handoff = await SelectTakeoverEndpointAsync(handoff, standby, ct).ConfigureAwait(false);
                ManagedSession leader = await standby.CreateTakeoverSessionAsync(
                    handoff,
                    new SessionTakeoverOptions { Identity = identity },
                    ct).ConfigureAwait(false);
                m_session = leader;
                m_adoptedSession = leader;
                standby.SessionConfigurationChanged -= OnSessionConfigurationChanged;
                if (ReferenceEquals(m_publishedSession, standby))
                {
                    m_publishedSession = null;
                }
                await DisposeQuietlyAsync(standby).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The standby session is untouched and serves as the fresh leader session.
                m_logger.TokenReuseFastActivateFailedUsing(ex);
                return false;
            }
        }

        private async ValueTask<SessionHandoff> SelectTakeoverEndpointAsync(
            SessionHandoff handoff,
            ManagedSession standby,
            CancellationToken ct)
        {
            EndpointDescription savedEndpoint = handoff.Session.ConfiguredEndpoint?.Description
                ?? throw new ServiceResultException(
                    StatusCodes.BadDecodingError,
                    "The saved session handoff has no endpoint description.");
            EndpointDescription standbyEndpoint = standby.ConfiguredEndpoint.Description
                ?? throw new ServiceResultException(
                    StatusCodes.BadConfigurationError,
                    "The standby session has no endpoint description.");
            if (string.Equals(
                savedEndpoint.EndpointUrl,
                standbyEndpoint.EndpointUrl,
                StringComparison.Ordinal))
            {
                return handoff;
            }

            ServiceResultException? securityMismatch = ManagedSession.GetSecurityConfigurationMismatch(
                savedEndpoint,
                standbyEndpoint);
            if (securityMismatch != null)
            {
                throw securityMismatch;
            }

            Func<EndpointDescription, EndpointDescription, CancellationToken, ValueTask<bool>>? validator =
                m_options.ValidateMirroredEndpointAsync;
            if (validator == null ||
                !await validator(savedEndpoint, standbyEndpoint, ct).ConfigureAwait(false))
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed,
                    "The standby endpoint is not an authorized mirrored member of the saved endpoint.");
            }

            return handoff with
            {
                Session = handoff.Session with
                {
                    ConfiguredEndpoint = CoreUtils.Clone(standby.ConfiguredEndpoint)!
                }
            };
        }

        private async ValueTask DisposeQuietlyAsync(ManagedSession session)
        {
            try
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                m_logger.DisposingTokenReuseSessionThatFailed(ex);
            }
        }

        private static TimeSpan GetPromotionRetryDelay(int attempt)
        {
            int cappedAttempt = Math.Min(attempt - 1, 4);
            return TimeSpan.FromMilliseconds(250 * (1 << cappedAttempt));
        }

        private async ValueTask PublishSecretsAsync(CancellationToken ct)
        {
            if (m_session == null)
            {
                return;
            }
            ByteString protectedRecord = SessionHandoffCodec.Protect(
                m_session.CaptureHandoff(),
                m_protector,
                RecordProtectionContext.Create("client-replica-session", m_options.SessionRecordKey),
                m_session.MessageContext);
            await m_store.SetAsync(m_options.SessionRecordKey, protectedRecord, ct).ConfigureAwait(false);
        }

        private void SubscribeToSessionConfigurationChanges()
        {
            if (ReferenceEquals(m_publishedSession, m_session))
            {
                return;
            }

            m_publishedSession?.SessionConfigurationChanged -= OnSessionConfigurationChanged;

            m_publishedSession = m_session;
            m_publishedSession?.SessionConfigurationChanged += OnSessionConfigurationChanged;
        }

        private void OnSessionConfigurationChanged(object? sender, EventArgs e)
        {
            m_backgroundWork.Run(
                nameof(PublishSecretsAsync),
                async _ =>
                {
                    if (m_election.IsLeader && ReferenceEquals(sender, m_session))
                    {
                        await PublishSecretsAsync(m_cts.Token).ConfigureAwait(false);
                    }
                });
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            m_election.LeadershipChanged -= OnLeadershipChanged;
            m_cts.Cancel();

            // Before the session and the election go away: a role change already
            // in flight promotes or demotes through both of them.
            await m_backgroundWork.DisposeAsync().ConfigureAwait(false);

            await m_election.DisposeAsync().ConfigureAwait(false);
            if (m_session != null)
            {
                m_session.SessionConfigurationChanged -= OnSessionConfigurationChanged;
                await m_session.DisposeAsync().ConfigureAwait(false);
                m_session = null;
            }
            m_cts.Dispose();
        }

        private readonly ClientReplicaOptions m_options;
        private readonly ILeaderElection m_election;
        private readonly ISharedKeyValueStore m_store;
        private readonly IRecordProtector m_protector;
        private readonly ILogger m_logger;
        private readonly ITelemetryContext m_telemetry;
        private readonly CancellationTokenSource m_cts = new();
        private readonly BackgroundTaskScope m_backgroundWork;
        private ManagedSession? m_session;
        private ManagedSession? m_publishedSession;
        private bool m_sessionServedLeader;
        private ManagedSession? m_adoptedSession;
    }

    /// <summary>
    /// Source-generated log messages for <see cref="ClientReplicaCoordinator"/>.
    /// </summary>
    internal static partial class ClientReplicaCoordinatorLog
    {
        [LoggerMessage(EventId = ClientEventIds.ClientReplicaCoordinator + 0, Level = LogLevel.Error,
            Message = "Client replica role change to leader={IsLeader} failed.")]
        public static partial void ClientReplicaRoleChangeLeaderIsLeader(
            this ILogger logger,
            Exception? exception,
            bool isLeader);

        [LoggerMessage(EventId = ClientEventIds.ClientReplicaCoordinator + 1, Level = LogLevel.Warning,
            Message = "Client replica promotion attempt {Attempt} failed; retrying in {Delay}.")]
        public static partial void ClientReplicaPromotionAttemptAttemptFailed(
            this ILogger logger,
            Exception? exception,
            int attempt,
            TimeSpan delay);

        [LoggerMessage(EventId = ClientEventIds.ClientReplicaCoordinator + 2, Level = LogLevel.Information,
            Message = "Token-reuse fast-activate failed; using a fresh session.")]
        public static partial void TokenReuseFastActivateFailedUsing(this ILogger logger, Exception? exception);

        [LoggerMessage(EventId = ClientEventIds.ClientReplicaCoordinator + 3, Level = LogLevel.Information,
            Message = "Disposing the standby session replaced by the taken-over leader session threw; ignoring.")]
        public static partial void DisposingTokenReuseSessionThatFailed(this ILogger logger, Exception? exception);

        [LoggerMessage(EventId = ClientEventIds.ClientReplicaCoordinator + 5, Level = LogLevel.Warning,
            Message = "Tearing down the demoted leader session failed; ignoring.")]
        public static partial void DisposingDemotedLeaderSessionFailed(
            this ILogger logger,
            Exception? exception);

        [LoggerMessage(EventId = ClientEventIds.ClientReplicaCoordinator + 6, Level = LogLevel.Warning,
            Message = "Connecting the standby session after demotion failed; the next promotion connects.")]
        public static partial void ConnectingStandbySessionAfterDemotionFailed(
            this ILogger logger,
            Exception? exception);
    }
}
