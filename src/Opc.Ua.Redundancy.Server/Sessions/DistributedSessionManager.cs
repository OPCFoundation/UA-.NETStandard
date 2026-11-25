// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// A <see cref="SessionManager"/> that mirrors session state to a shared
    /// store so a client can fail over to a standby replica and reconnect by
    /// re-running <c>ActivateSession</c> on a new SecureChannel — the OPC UA
    /// HotAndMirrored fast reconnect (Part 4 §6.6).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Security model (see <c>docs/HighAvailability.md</c>): the
    /// <c>AuthenticationToken</c> is only a lookup key. On restore the standard
    /// activation path requires a signed SecureChannel and checks the full
    /// client-certificate signature against the mirrored <c>serverNonce</c>.
    /// One conditional commit replaces that nonce and transfers ownership,
    /// and the SecurityPolicy/Mode must match the original.
    /// The mirrored record is encrypted and integrity-protected at
    /// rest by the <see cref="ISharedSessionStore"/>'s record protector.
    /// </para>
    /// <para>
    /// The safe default is re-authentication on failover
    /// (<see cref="DistributedSessionOptions.EnableFastReconnect"/> = <c>false</c>):
    /// the manager mirrors metadata for visibility but does not admit a session
    /// from the shared store.
    /// </para>
    /// <para>
    /// Lifecycle: the replica that serves a session owns its entry. It refreshes a
    /// liveness heartbeat while the client uses the session, so a session in normal use
    /// stays restorable, and it is the only replica that mirrors activations or deletes
    /// the entry. Activation and close compete on that same committed record, so a
    /// close that wins cannot be undone by an in-flight activation. A restore takes
    /// ownership after validation. New session service requests are admitted only while
    /// the committed activation belongs to this replica; this is a check at admission,
    /// not a fence on operations already dispatched. The previous owner drops its stale
    /// local copy without deleting the entry. A restored session keeps its SessionId. A
    /// periodic sweep removes the entries of timed-out sessions (for example those of a
    /// replica that crashed).
    /// </para>
    /// <para>
    /// A session whose user identity token is encrypted with an EphemeralKey (ECC or
    /// RSA-DH user token policies) cannot be restored: the private part of the key never
    /// leaves the replica that issued it. Such a reconnect is rejected before anything
    /// is consumed, and the client re-creates the session.
    /// </para>
    /// </remarks>
    public sealed class DistributedSessionManager : SessionManager
    {
        /// <summary>
        /// Creates a distributed session manager.
        /// </summary>
        /// <param name="server">The hosting server.</param>
        /// <param name="configuration">The application configuration.</param>
        /// <param name="sessionStore">The shared session store (encrypted).</param>
        /// <param name="serverCertificateProvider">
        /// Resolves the shared server <c>ApplicationInstanceCertificate</c> for a
        /// security policy URI. Returns a new caller-owned certificate handle that
        /// the session manager disposes once the restored session has taken its own
        /// reference; returns <c>null</c> when no certificate is configured for the
        /// policy.
        /// </param>
        /// <param name="options">The distributed session options.</param>
        /// <param name="timeProvider">An optional time provider.</param>
        public DistributedSessionManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            ISharedSessionStore sessionStore,
            Func<string, Certificate?> serverCertificateProvider,
            DistributedSessionOptions? options = null,
            TimeProvider? timeProvider = null)
            : base(server, configuration, timeProvider)
        {
            m_server = server ?? throw new ArgumentNullException(nameof(server));
            m_sessionStore = sessionStore ?? throw new ArgumentNullException(nameof(sessionStore));
            m_serverCertificateProvider = serverCertificateProvider
                ?? throw new ArgumentNullException(nameof(serverCertificateProvider));
            m_options = options ?? new DistributedSessionOptions();
            m_fastReconnectEnabled = m_options.EnableFastReconnect;
            m_telemetry = server.Telemetry;
            m_logger = server.Telemetry.CreateLogger<DistributedSessionManager>();
            m_restoreTimeProvider = timeProvider
                ?? (server as ITimeProviderProvider)?.TimeProvider
                ?? TimeProvider.System;
            m_replicaId = Guid.NewGuid().ToString("N");
        }

        /// <inheritdoc/>
        protected override bool SupportsSessionRestore => m_fastReconnectEnabled;

        /// <inheritdoc/>
        protected override bool RequiresSessionRequestAdmission(ISession session)
        {
            return UsesAuthoritativeActivation(session.EndpointDescription);
        }

        /// <inheritdoc/>
        protected override async ValueTask ValidateSessionRequestAdmissionAsync(
            NodeId authenticationToken,
            ISession session,
            CancellationToken cancellationToken)
        {
            m_committedActivations.TryGetValue(session, out SharedSessionEntry? localActivation);
            ByteString localNonce = session is Session concrete ? concrete.GetServerNonceData() : default;
            SharedSessionEntry? current;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                current = await m_sessionStore.TryGetAsync(authenticationToken, cancellationToken)
                    .ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (Exception ex) when (
                ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                m_logger.FailedToVerifySessionRequestAdmission(ex);
                StatusCode status = ex is ServiceResultException { StatusCode: var code } &&
                    code == StatusCodes.BadSecurityChecksFailed
                        ? StatusCodes.BadSecurityChecksFailed
                        : StatusCodes.BadResourceUnavailable;
                throw new ServiceResultException(status, "The committed session state could not be verified.");
            }

            if (current == null || !MatchesLocalOwner(current, session) ||
                localNonce.IsNull || current.ServerNonce != localNonce ||
                (localActivation == null
                    ? session.Activated || current.HasActivatedUserIdentity
                    : !IsSameActivation(localActivation, current)))
            {
                throw new ServiceResultException(StatusCodes.BadSessionIdInvalid);
            }
        }

        /// <summary>
        /// The identity this manager records as the owner of the entries of the
        /// sessions it serves.
        /// </summary>
        internal string ReplicaId => m_replicaId;

        /// <inheritdoc/>
        public override async ValueTask StartupAsync(CancellationToken cancellationToken = default)
        {
            await base.StartupAsync(cancellationToken).ConfigureAwait(false);

            bool liveness = m_options.LivenessInterval > TimeSpan.Zero;
            bool sweep = m_options.SweepInterval > TimeSpan.Zero;
            if (!liveness && !sweep)
            {
                return;
            }

            lock (m_maintenanceLock)
            {
                if (m_maintenanceCts == null)
                {
                    m_maintenanceCts = new CancellationTokenSource();
                    CancellationToken token = m_maintenanceCts.Token;
                    m_maintenanceTask = Task.Run(() => RunMaintenanceAsync(token), token);
                }
            }
        }

        /// <inheritdoc/>
        public override async ValueTask ShutdownAsync(CancellationToken cancellationToken = default)
        {
            await StopMaintenanceAsync().ConfigureAwait(false);
            await base.ShutdownAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                CancellationTokenSource? cts;
                lock (m_maintenanceLock)
                {
                    cts = m_maintenanceCts;
                    m_maintenanceCts = null;
                    m_maintenanceTask = null;
                }
                if (cts != null)
                {
                    cts.Cancel();
                    cts.Dispose();
                }
            }
            base.Dispose(disposing);
        }

        /// <inheritdoc/>
        protected override ISession CreateSession(
            OperationContext context,
            IServerInternal server,
            Certificate serverCertificate,
            NodeId sessionCookie,
            ByteString clientNonce,
            Nonce serverNonce,
            string sessionName,
            ApplicationDescription clientDescription,
            string endpointUrl,
            Certificate clientCertificate,
            CertificateCollection clientCertificateChain,
            double sessionTimeout,
            uint maxResponseMessageSize,
            int maxRequestAge,
            int maxContinuationPoints)
        {
            ISession session = base.CreateSession(
                context,
                server,
                serverCertificate,
                sessionCookie,
                clientNonce,
                serverNonce,
                sessionName,
                clientDescription,
                endpointUrl,
                clientCertificate,
                clientCertificateChain,
                sessionTimeout,
                maxResponseMessageSize,
                maxRequestAge,
                maxContinuationPoints);

            // A per-process counter would hand two replicas the same SessionId, and a
            // restored session keeps the id it was created with, so the replica set
            // mints SessionIds that are unique across it. A restore replaces it with the
            // mirrored id.
            if (session is Session created)
            {
                int namespaceIndex = m_server.NamespaceUris.GetIndex(kDiagnosticsNamespaceUri);
                if (namespaceIndex > 0)
                {
                    created.RequestedId = new NodeId(Guid.NewGuid(), (ushort)namespaceIndex);
                }
            }
            return session;
        }

        /// <inheritdoc/>
        public override async ValueTask<(bool IdentityContextChanged, ByteString ServerNonce, ServiceResult ActivationStatus)> ActivateSessionAsync(
            OperationContext context,
            NodeId authenticationToken,
            SignatureData? clientSignature,
            ExtensionObject userIdentityToken,
            SignatureData? userTokenSignature,
            ArrayOf<string> localeIds,
            CancellationToken cancellationToken = default)
        {
            // A client that failed over to another replica and now comes back would
            // otherwise reach the stale local copy, which validates against a nonce the
            // other replica has since replaced. Once another replica owns the session,
            // the local copy is dropped and the session is restored from the store.
            if (UsesAuthoritativeActivation(context.ChannelContext?.EndpointDescription) &&
                m_sessionsByToken.ContainsKey(authenticationToken) &&
                GetSession(authenticationToken) is ISession localSession)
            {
                await DropLocalCopyIfOwnedElsewhereAsync(
                    authenticationToken,
                    localSession,
                    cancellationToken).ConfigureAwait(false);
            }

            return await base.ActivateSessionAsync(
                context,
                authenticationToken,
                clientSignature,
                userIdentityToken,
                userTokenSignature,
                localeIds,
                cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public override async ValueTask<CreateSessionResult> CreateSessionAsync(
            OperationContext context,
            Certificate serverCertificate,
            string? sessionName,
            ByteString clientNonce,
            ApplicationDescription? clientDescription,
            string? endpointUrl,
            Certificate? clientCertificate,
            CertificateCollection? clientCertificateChain,
            double requestedSessionTimeout,
            uint maxResponseMessageSize,
            CancellationToken cancellationToken = default)
        {
            CreateSessionResult result = await base.CreateSessionAsync(
                context,
                serverCertificate,
                sessionName,
                clientNonce,
                clientDescription,
                endpointUrl,
                clientCertificate,
                clientCertificateChain,
                requestedSessionTimeout,
                maxResponseMessageSize,
                cancellationToken).ConfigureAwait(false);

            try
            {
                CreationPublication publication = m_creationPublications.GetValue(result.Session, static _ => new());
                await publication.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    if (!publication.Closed && ReferenceEquals(GetSession(result.AuthenticationToken), result.Session))
                    {
                        TrackLocalSession(result.SessionId, result.AuthenticationToken, result.RevisedSessionTimeout);
                        SharedSessionEntry entry = BuildEntry(
                            context,
                            result,
                            serverCertificate,
                            sessionName,
                            clientNonce,
                            clientDescription,
                            endpointUrl,
                            clientCertificate);
                        await m_sessionStore.PutAsync(entry, cancellationToken).ConfigureAwait(false);
                        MarkContactMirrored(result.AuthenticationToken, entry.LastContactAt.ToDateTime());
                    }
                }
                finally
                {
                    publication.Gate.Release();
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Mirroring is best-effort; never fail an otherwise-valid session
                // because the shared store is unavailable.
                m_logger.FailedToMirrorCreatedSessionToSharedStore(ex);
            }

            return result;
        }

        /// <inheritdoc/>
        protected override async ValueTask OnSessionActivatedAsync(
            NodeId authenticationToken,
            ISession session,
            ByteString serverNonce,
            UserTokenType clientUserTokenType,
            string? clientUserId,
            long activationSequence,
            CancellationToken cancellationToken)
        {
            if (m_admittedRestores.TryGetValue(session, out SharedSessionEntry? restoredFrom) &&
                m_admittedRestores.Remove(session))
            {
                await CompleteRestoreAsync(authenticationToken, session, restoredFrom, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (UsesAuthoritativeActivation(session.EndpointDescription))
            {
                return;
            }
            if (!m_tokensBySession.ContainsKey(session.Id))
            {
                TrackLocalSession(
                    session.Id,
                    authenticationToken,
                    session.ReadDiagnostics(d => d.ActualSessionTimeout));
            }

            await MirrorActivationIfCurrentAsync(
                authenticationToken,
                serverNonce,
                clientUserTokenType,
                clientUserId,
                activationSequence,
                UserTokenRequiresEphemeralKey(session),
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Whether the next activation of the session must decrypt its user identity
        /// token with an EphemeralKey issued by this replica: the token policy of the
        /// active identity encrypts with an ECC or RSA-DH ephemeral key.
        /// </summary>
        internal static bool UserTokenRequiresEphemeralKey(ISession session)
        {
            if (session.IdentityToken is not IUserIdentityTokenHandler handler ||
                handler.TokenType is not (UserTokenType.UserName or UserTokenType.IssuedToken))
            {
                return false;
            }

            string? policyId = handler.Token?.PolicyId;
            string? securityPolicyUri = null;
            foreach (UserTokenPolicy policy in session.EndpointDescription.UserIdentityTokens)
            {
                if (policy != null && string.Equals(policy.PolicyId, policyId, StringComparison.Ordinal))
                {
                    securityPolicyUri = policy.SecurityPolicyUri;
                    break;
                }
            }
            return UserTokenRequiresEphemeralKey(
                handler.TokenType, securityPolicyUri, session.EndpointDescription.SecurityPolicyUri);
        }

        /// <summary>
        /// Mirrors an activation unless a newer activation of the same Session has
        /// already been mirrored.
        /// </summary>
        internal async ValueTask MirrorActivationIfCurrentAsync(
            NodeId authenticationToken,
            ByteString serverNonce,
            UserTokenType clientUserTokenType,
            string? clientUserId,
            long activationSequence,
            bool userTokenRequiresEphemeralKey,
            CancellationToken cancellationToken)
        {
            SemaphoreSlim mirrorLock = m_mirrorLocks.GetOrAdd(
                authenticationToken,
                static _ => new SemaphoreSlim(1, 1));
            await mirrorLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // Activations release the per-Session gate before this callback
                // runs, so two overlapping activations can arrive out of order.
                // Mirroring the older nonce last would leave a standby validating
                // the client's next signature against a superseded nonce, so the
                // stale write is dropped instead.
                if (m_mirroredSequences.TryGetValue(authenticationToken, out long mirrored) &&
                    mirrored >= activationSequence)
                {
                    return;
                }

                // Mirror the freshly issued serverNonce so a standby validates the
                // client's next activation against it (and consumes it single-use).
                await MirrorActivationAsync(
                    authenticationToken,
                    serverNonce,
                    clientUserTokenType,
                    clientUserId,
                    userTokenRequiresEphemeralKey,
                    cancellationToken)
                    .ConfigureAwait(false);
                m_mirroredSequences[authenticationToken] = activationSequence;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                m_logger.FailedToMirrorActivatedSessionToSharedStore(ex);
            }
            finally
            {
                mirrorLock.Release();
            }
        }

        /// <inheritdoc/>
        protected override async ValueTask OnSessionClosingAsync(
            NodeId authenticationToken,
            ISession session,
            CancellationToken cancellationToken)
        {
            // A create that has not published yet must not write after this close.
            CreationPublication publication = m_creationPublications.GetValue(session, static _ => new());
            await publication.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                publication.Closed = true;
                if (!m_tokensBySession.TryRemove(session.Id, out _))
                {
                    return;
                }

                m_sessionsByToken.TryRemove(authenticationToken, out _);
                m_liveness.TryRemove(authenticationToken, out _);
                bool authoritative = UsesAuthoritativeActivation(session.EndpointDescription);
                ByteString committedNonce = m_committedActivations.TryGetValue(session, out SharedSessionEntry? current)
                    ? current.ServerNonce
                    : session is Session localSession ? localSession.GetServerNonceData() : default;
                try
                {
                    await CloseMirroredSessionAsync(
                        authenticationToken, session.Id, committedNonce, authoritative, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    m_logger.FailedToRemoveMirroredSessionFromSharedStore(ex);
                    if (authoritative)
                    {
                        throw;
                    }
                }
                finally
                {
                    m_mirrorLocks.TryRemove(authenticationToken, out _);
                    m_mirroredSequences.TryRemove(authenticationToken, out _);
                }
            }
            finally
            {
                publication.Gate.Release();
            }
        }

        /// <inheritdoc/>
        protected override async ValueTask<ISession?> RestoreSessionAsync(
            NodeId authenticationToken,
            OperationContext context,
            CancellationToken cancellationToken = default)
        {
            if (!m_fastReconnectEnabled)
            {
                return null;
            }

            SecureChannelContext channel = context.ChannelContext!;
            EndpointDescription endpoint = channel.EndpointDescription!;
            if (!IsSignedChannel(endpoint.SecurityPolicyUri, endpoint.SecurityMode))
            {
                LogRestoreRejected(authenticationToken, RestoreDecision.UnsignedChannel);
                return null;
            }

            SharedSessionEntry? entry = await m_sessionStore
                .TryGetAsync(authenticationToken, cancellationToken)
                .ConfigureAwait(false);
            if (entry == null)
            {
                return null;
            }

            RestoreDecision decision = AuthorizeRestore(
                entry,
                endpoint.SecurityPolicyUri ?? string.Empty,
                endpoint.SecurityMode,
                EmptyIfNull(channel.ClientChannelCertificate));
            if (decision != RestoreDecision.Authorized)
            {
                LogRestoreRejected(authenticationToken, decision);
                if (decision == RestoreDecision.Expired)
                {
                    // Nobody can restore it any more, and its owner may be gone.
                    await TryRemoveEntryAsync(entry, cancellationToken).ConfigureAwait(false);
                }
                return null;
            }

            ISession? session = ReconstructSession(entry, authenticationToken, context);
            if (session != null)
            {
                try
                {
                    await session.InitializeAsync(context, cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    session.Dispose();
                    throw;
                }

                m_pendingRestores.Add(session, entry);
            }
            return session;
        }

        /// <inheritdoc/>
        protected override async ValueTask<bool> AdmitActivationAsync(
            NodeId authenticationToken,
            ISession session,
            OperationContext context,
            bool isRestoredSession,
            ByteString serverNonce,
            UserTokenType clientUserTokenType,
            string? clientUserId,
            UserTokenPolicy? userTokenPolicy,
            CancellationToken cancellationToken)
        {
            EndpointDescription endpoint = context.ChannelContext!.EndpointDescription!;
            if (!UsesAuthoritativeActivation(endpoint))
            {
                return !isRestoredSession;
            }
            if (session is not Session localSession)
            {
                return false;
            }

            ByteString previousNonce = localSession.GetServerNonceData();
            if (!IsValidSessionNonce(previousNonce))
            {
                return false;
            }

            SharedSessionEntry? current;
            if (isRestoredSession)
            {
                m_pendingRestores.TryGetValue(session, out current);
            }
            else
            {
                current = await m_sessionStore
                    .TryGetAsync(authenticationToken, cancellationToken).ConfigureAwait(false);
                if (current != null &&
                    (current.SessionId != session.Id ||
                        !string.Equals(current.OwnerId, m_replicaId, StringComparison.Ordinal)))
                {
                    current = null;
                }
            }

            if (current == null || current.ServerNonce != previousNonce)
            {
                LogRestoreRejected(authenticationToken, RestoreDecision.ActivationChanged);
                return false;
            }

            bool requiresEphemeralKey = UserTokenRequiresEphemeralKey(
                clientUserTokenType, userTokenPolicy?.SecurityPolicyUri, endpoint.SecurityPolicyUri);
            SharedSessionEntry original = current;
            for (int attempt = 0; attempt < kMaxConditionalWriteAttempts; attempt++)
            {
                if (isRestoredSession && IsRestoreExpired(current))
                {
                    LogRestoreRejected(authenticationToken, RestoreDecision.Expired);
                    return false;
                }

                SharedSessionEntry replacement = WithActivation(
                    current, session.Id, serverNonce, clientUserTokenType, clientUserId, requiresEphemeralKey);
                bool committed;
                try
                {
                    committed = await TryCommitActivationAsync(current, replacement, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    m_logger.FailedToMirrorActivatedSessionToSharedStore(ex);
                    throw;
                }
                if (committed)
                {
                    m_committedActivations.Remove(session);
                    m_committedActivations.Add(session, replacement);
                    if (isRestoredSession)
                    {
                        m_pendingRestores.Remove(session);
                        m_admittedRestores.Add(session, original);
                        TrackLocalSession(session.Id, authenticationToken, replacement.SessionTimeout);
                    }
                    MarkContactMirrored(authenticationToken, replacement.LastContactAt.ToDateTime());
                    return true;
                }

                SharedSessionEntry? refreshed = await m_sessionStore
                    .TryGetAsync(authenticationToken, cancellationToken).ConfigureAwait(false);
                // Heartbeats can advance the record without changing its activation
                // generation. Never retry against an advanced activation or a deletion.
                if (refreshed == null || !IsSameActivation(original, refreshed))
                {
                    break;
                }
                current = refreshed;
            }

            LogRestoreRejected(authenticationToken, RestoreDecision.ActivationChanged);
            return false;
        }

        /// <summary>
        /// Loads the continuation points and audits a committed restore.
        /// </summary>
        private async ValueTask CompleteRestoreAsync(
            NodeId authenticationToken,
            ISession session,
            SharedSessionEntry entry,
            CancellationToken cancellationToken)
        {
            if (session is Session restoredSession)
            {
                try
                {
                    // The continuation points of every earlier owner are mirrored under
                    // the SessionId the session keeps across failovers.
                    await restoredSession
                        .LoadMirroredContinuationPointsAsync(entry.SessionId, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    m_logger.FailedToLoadMirroredContinuationPoints(ex, session.Id);
                }
            }

            if (m_logger.IsEnabled(LogLevel.Information))
            {
                m_logger.DistributedSessionRestoredFromSharedStore(TokenDigest(authenticationToken), session.Id);
            }

            // Security-relevant provenance: a session materialized on this
            // replica from shared state (audit, not just a log).
            m_server.ReportAuditSessionRestoredEvent(
                TokenDigest(authenticationToken), session, m_logger);
        }

        /// <summary>
        /// The result of authorizing a session restore.
        /// </summary>
        internal enum RestoreDecision
        {
            /// <summary>The mirrored state is eligible for restore.</summary>
            Authorized,

            /// <summary>The mirrored session timed out before the restore attempt.</summary>
            Expired,

            /// <summary>The mirrored record predates or omits required security state.</summary>
            SecurityStateMissing,

            /// <summary>Cross-replica activation requires a signed SecureChannel.</summary>
            UnsignedChannel,

            /// <summary>The SecurityPolicy/Mode does not match the original.</summary>
            PolicyMismatch,

            /// <summary>The new SecureChannel uses a different client certificate.</summary>
            ClientCertificateMismatch,

            /// <summary>The mirrored nonce is malformed.</summary>
            NonceInvalid,

            /// <summary>The committed entry was closed or moved to another activation.</summary>
            ActivationChanged,

            /// <summary>
            /// The user identity token is encrypted with an EphemeralKey whose private
            /// part only the replica that issued it holds.
            /// </summary>
            EphemeralKeyUnavailable
        }

        /// <summary>
        /// Enforces the cross-channel security checks of a restore without consuming
        /// anything.
        /// </summary>
        internal RestoreDecision AuthorizeRestore(
            SharedSessionEntry entry,
            string securityPolicyUri,
            MessageSecurityMode securityMode,
            ByteString clientChannelCertificate)
        {
            if (IsRestoreExpired(entry))
            {
                return RestoreDecision.Expired;
            }

            if (!HasValidClientUserIdState(entry))
            {
                return RestoreDecision.SecurityStateMissing;
            }

            if (!IsSignedChannel(securityPolicyUri, securityMode))
            {
                return RestoreDecision.UnsignedChannel;
            }

            if (!string.Equals(entry.SecurityPolicyUri, securityPolicyUri, StringComparison.Ordinal) ||
                entry.SecurityMode != (int)securityMode)
            {
                return RestoreDecision.PolicyMismatch;
            }

            if (entry.OriginalClientChannelCertificate.IsEmpty ||
                entry.OriginalClientChannelCertificate != clientChannelCertificate)
            {
                return RestoreDecision.ClientCertificateMismatch;
            }

            if (!IsValidSessionNonce(entry.ServerNonce))
            {
                return RestoreDecision.NonceInvalid;
            }

            // Rejected before anything is consumed or counted as a failed
            // authentication: the token could not be decrypted on this replica.
            if (entry.UserTokenRequiresEphemeralKey)
            {
                return RestoreDecision.EphemeralKeyUnavailable;
            }

            return RestoreDecision.Authorized;
        }

        private ISession? ReconstructSession(
            SharedSessionEntry entry,
            NodeId authenticationToken,
            OperationContext context)
        {
            if (!HasValidClientUserIdState(entry) ||
                !IsSignedChannel(entry.SecurityPolicyUri, (MessageSecurityMode)entry.SecurityMode))
            {
                return null;
            }

            if (entry.ClientCertificateChain.IsEmpty || entry.OriginalClientChannelCertificate.IsEmpty)
            {
                m_logger.MirroredSessionHasNoClientCertificate();
                return null;
            }

            if (entry.OriginalClientChannelCertificate !=
                context.ChannelContext!.ClientChannelCertificate)
            {
                return null;
            }

            // Caller-owned handle; disposed at method scope exit after the created
            // session has taken its own ref-counted handle (the Session constructor
            // AddRefs the server certificate).
            using Certificate? serverCertificate = m_serverCertificateProvider(entry.SecurityPolicyUri);
            if (serverCertificate == null)
            {
                m_logger.NoServerCertificateAvailableForPolicy(entry.SecurityPolicyUri);
                return null;
            }

            using CertificateCollection parsed =
                Utils.ParseCertificateChainBlob(entry.ClientCertificateChain.ToArray(), m_telemetry);
            if (parsed.Count == 0)
            {
                return null;
            }

            // The session owns and disposes the client certificate + issuers and
            // the server nonce once created, so hand it independent (ref-counted)
            // handles and dispose them only if creation fails (assign null after
            // ownership transfer, per the CA2000 pattern). Trust of the chain was
            // already established when the session was created on the active.
            Certificate? clientCertificate = null;
            CertificateCollection? issuers = null;
            Nonce? serverNonce = null;
            try
            {
                clientCertificate = parsed[0].AddRef();
                issuers = [];
                serverNonce = Nonce.CreateNonce(SecurityPolicies.None, entry.ServerNonce.ToArray());
                ISession session = CreateSession(
                    context,
                    m_server,
                    serverCertificate,
                    authenticationToken,
                    entry.ClientNonce,
                    serverNonce,
                    entry.SessionName,
                    entry.ClientDescription,
                    entry.EndpointUrl,
                    clientCertificate!,
                    issuers,
                    entry.SessionTimeout,
                    0,
                    0,
                    0);
                SetRestoredSessionTransferSecurityState(
                    session,
                    entry.OriginalClientChannelCertificate,
                    entry.SecurityPolicyUri,
                    (MessageSecurityMode)entry.SecurityMode,
                    entry.ClientUserTokenType,
                    entry.ClientUserId);

                // The restored session carries the original certificate provenance: a
                // certificate whose validation error was accepted on the active replica
                // (or of an entry that predates the mirrored provenance) does not become
                // a trusted application identity after failover (OPC 10000-3 4.9).
                ClientCertificateProvenance.SetValidated(session, entry.ClientCertificateValidated);

                if (session is Session restored)
                {
                    // The session keeps the SessionId the client got from CreateSession
                    // (OPC 10000-4 5.7.2.2), so its diagnostics, audit events and mirrored
                    // continuation points stay addressable after the failover.
                    restored.RequestedId = entry.SessionId;

                    // A client may keep signing the certificate of the server that
                    // created the session; the replicas of a non-transparent set have
                    // their own certificates (OPC 10000-4 6.6.2.4.1).
                    restored.OriginalServerCertificate = entry.ServerCertificate;
                }

                // Ownership transferred to the session; prevent the finally below
                // from disposing handles the session now manages.
                clientCertificate = null;
                issuers = null;
                serverNonce = null;
                return session;
            }
            finally
            {
                clientCertificate?.Dispose();
                issuers?.Dispose();
                serverNonce?.Dispose();
            }
        }

        private SharedSessionEntry BuildEntry(
            OperationContext context,
            CreateSessionResult result,
            Certificate serverCertificate,
            string? sessionName,
            ByteString clientNonce,
            ApplicationDescription? clientDescription,
            string? endpointUrl,
            Certificate? clientCertificate)
        {
            EndpointDescription endpoint = context.ChannelContext!.EndpointDescription!;
            ByteString clientCertBlob = default;
            if (clientCertificate != null)
            {
                using var leaf = new CertificateCollection { clientCertificate };
                clientCertBlob = ByteString.From(Utils.CreateCertificateChainBlob(leaf));
            }

            DateTimeUtc now = UtcNow();
            return new SharedSessionEntry
            {
                SessionId = result.SessionId,
                AuthenticationToken = result.AuthenticationToken,
                SessionName = sessionName ?? string.Empty,
                CreatedAt = now,
                LastActivatedAt = now,
                LastContactAt = now,
                OwnerId = m_replicaId,
                ServerCertificate = GetCreateSessionServerCertificate(endpoint, serverCertificate),
                ServerNonce = result.ServerNonce,
                ClientNonce = clientNonce,
                ClientCertificateChain = clientCertBlob,
                SecurityStateVersion = SharedSessionEntry.CurrentSecurityStateVersion,
                OriginalClientChannelCertificate =
                    EmptyIfNull(context.ChannelContext!.ClientChannelCertificate),
                ClientUserId = null,
                ClientUserTokenType = UserTokenType.Anonymous,
                HasActivatedUserIdentity = false,
                // The base CreateSessionAsync records the provenance before it
                // publishes the session, so it is final here.
                ClientCertificateValidated = ClientCertificateProvenance.IsValidated(result.Session),
                SecurityPolicyUri = endpoint.SecurityPolicyUri ?? string.Empty,
                SecurityMode = (int)endpoint.SecurityMode,
                EndpointUrl = endpointUrl ?? string.Empty,
                SessionTimeout = result.RevisedSessionTimeout,
                ClientDescription = clientDescription ?? new ApplicationDescription()
            };
        }

        /// <summary>
        /// The server certificate blob the client received in <c>CreateSession</c>: the
        /// endpoint's encoded chain when the server sends the complete chain, otherwise the
        /// leaf. A client may sign either, so the restoring replica verifies both.
        /// </summary>
        private ByteString GetCreateSessionServerCertificate(
            EndpointDescription endpoint,
            Certificate? serverCertificate)
        {
            if (serverCertificate == null)
            {
                return default;
            }

            ByteString leaf = serverCertificate.RawData.ToByteString();
            ByteString advertised = endpoint.ServerCertificate;
            if (advertised.IsEmpty || advertised == leaf)
            {
                return leaf;
            }

            try
            {
                using CertificateCollection chain = Utils.ParseCertificateChainBlob(advertised, m_telemetry);
                if (chain.Count > 1 && chain[0].RawData.ToByteString() == leaf)
                {
                    return advertised;
                }
            }
            catch (ServiceResultException)
            {
                // not a chain blob; the leaf is what the client signs.
            }
            return leaf;
        }

        private async ValueTask MirrorActivationAsync(
            NodeId authenticationToken,
            ByteString serverNonce,
            UserTokenType clientUserTokenType,
            string? clientUserId,
            bool userTokenRequiresEphemeralKey,
            CancellationToken cancellationToken)
        {
            for (int attempt = 0; attempt < kMaxConditionalWriteAttempts; attempt++)
            {
                SharedSessionEntry? existing = await m_sessionStore
                    .TryGetAsync(authenticationToken, cancellationToken)
                    .ConfigureAwait(false);
                if (existing == null || !IsOwnedByThisReplica(existing))
                {
                    return;
                }

                SharedSessionEntry updated = WithActivation(
                    existing, existing.SessionId, serverNonce, clientUserTokenType,
                    clientUserId, userTokenRequiresEphemeralKey);
                if (await TryReplaceEntryAsync(existing, updated, cancellationToken).ConfigureAwait(false))
                {
                    MarkContactMirrored(authenticationToken, updated.LastContactAt.ToDateTime());
                    return;
                }
            }

            m_logger.MirroredSessionUpdateConflicted(TokenDigest(authenticationToken));
        }

        private SharedSessionEntry WithActivation(
            SharedSessionEntry current,
            NodeId sessionId,
            ByteString serverNonce,
            UserTokenType clientUserTokenType,
            string? clientUserId,
            bool userTokenRequiresEphemeralKey)
        {
            DateTimeUtc now = UtcNow();
            return current with
            {
                SessionId = sessionId,
                ServerNonce = serverNonce,
                LastActivatedAt = now,
                LastContactAt = now,
                OwnerId = m_replicaId,
                SecurityStateVersion = SharedSessionEntry.CurrentSecurityStateVersion,
                ClientUserId = clientUserId,
                ClientUserTokenType = clientUserTokenType,
                HasActivatedUserIdentity = true,
                UserTokenRequiresEphemeralKey = userTokenRequiresEphemeralKey
            };
        }

        private async ValueTask<bool> TryCommitActivationAsync(
            SharedSessionEntry current,
            SharedSessionEntry replacement,
            CancellationToken cancellationToken)
        {
            try
            {
                return await m_sessionStore.TryReplaceAsync(current, replacement, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or TimeoutException)
            {
                // A lost write acknowledgement is not proof of failure. Only this
                // attempt could have committed its freshly generated nonce.
                SharedSessionEntry? committed = await m_sessionStore
                    .TryGetAsync(current.AuthenticationToken, cancellationToken).ConfigureAwait(false);
                if (committed != null && IsSameActivation(replacement, committed))
                {
                    return true;
                }
                throw;
            }
        }

        private async ValueTask CloseMirroredSessionAsync(
            NodeId authenticationToken,
            NodeId sessionId,
            ByteString committedNonce,
            bool authoritative,
            CancellationToken cancellationToken)
        {
            for (int attempt = 0; attempt < kMaxConditionalWriteAttempts; attempt++)
            {
                SharedSessionEntry? current = await m_sessionStore.TryGetAsync(authenticationToken, cancellationToken)
                    .ConfigureAwait(false);
                if (current == null || current.SessionId != sessionId || !IsOwnedByThisReplica(current) ||
                    (authoritative && (current.OwnerId == null || current.ServerNonce != committedNonce)))
                {
                    return;
                }
                bool removed = authoritative
                    ? await m_sessionStore.TryRemoveAsync(current, cancellationToken).ConfigureAwait(false)
                    : await TryRemoveEntryVersionAsync(current, cancellationToken).ConfigureAwait(false);
                if (removed)
                {
                    return;
                }
            }

            m_logger.MirroredSessionUpdateConflicted(TokenDigest(authenticationToken));
            if (authoritative)
            {
                throw new ServiceResultException(StatusCodes.BadResourceUnavailable,
                    "The shared session close could not be committed.");
            }
        }

        private ValueTask<bool> TryReplaceEntryAsync(
            SharedSessionEntry current,
            SharedSessionEntry replacement,
            CancellationToken cancellationToken)
        {
            return !UsesAuthoritativeActivation(current) && m_sessionStore is SharedKeyValueSessionStore metadata
                ? metadata.TryReplaceMetadataAsync(current, replacement, cancellationToken)
                : m_sessionStore.TryReplaceAsync(current, replacement, cancellationToken);
        }

        private ValueTask<bool> TryRemoveEntryVersionAsync(
            SharedSessionEntry current,
            CancellationToken cancellationToken)
        {
            return !UsesAuthoritativeActivation(current) && m_sessionStore is SharedKeyValueSessionStore metadata
                ? metadata.TryRemoveMetadataAsync(current, cancellationToken)
                : m_sessionStore.TryRemoveAsync(current, cancellationToken);
        }

        private void LogRestoreRejected(NodeId authenticationToken, RestoreDecision decision)
        {
            m_logger.DistributedSessionRestoreRejected(TokenDigest(authenticationToken), decision);
        }

        private static bool IsSameActivation(SharedSessionEntry original, SharedSessionEntry current)
        {
            return (original with
                {
                    LastContactAt = current.LastContactAt,
                    ClientDescription = current.ClientDescription
                }) == current &&
                original.ClientDescription.IsEqual(current.ClientDescription);
        }

        private bool UsesAuthoritativeActivation(SharedSessionEntry entry)
        {
            return m_fastReconnectEnabled &&
                IsSignedChannel(entry.SecurityPolicyUri, (MessageSecurityMode)entry.SecurityMode);
        }

        private bool UsesAuthoritativeActivation(EndpointDescription? endpoint)
        {
            return m_fastReconnectEnabled && endpoint != null &&
                IsSignedChannel(endpoint.SecurityPolicyUri, endpoint.SecurityMode);
        }

        private static bool IsSignedChannel(string? policy, MessageSecurityMode mode)
        {
            return (mode is MessageSecurityMode.Sign or MessageSecurityMode.SignAndEncrypt) &&
                !string.IsNullOrEmpty(policy) &&
                !string.Equals(policy, SecurityPolicies.None, StringComparison.Ordinal);
        }

        private static bool UserTokenRequiresEphemeralKey(
            UserTokenType tokenType,
            string? tokenPolicy,
            string? endpointPolicy)
        {
            if (tokenType is not (UserTokenType.UserName or UserTokenType.IssuedToken))
            {
                return false;
            }

            string? policy = string.IsNullOrEmpty(tokenPolicy) ? endpointPolicy : tokenPolicy;
            SecurityPolicyInfo? info = string.IsNullOrEmpty(policy) ? null : SecurityPolicies.Default.GetInfo(policy!);
            return info != null && info.EphemeralKeyAlgorithm != CertificateKeyAlgorithm.None;
        }

        /// <summary>
        /// Whether a mirrored session can no longer be restored. The timeout runs from
        /// the later of the last activation and the last mirrored client contact; the
        /// heartbeat is throttled, so an entry that carries one is granted the
        /// heartbeat's maximum lag on top.
        /// </summary>
        private bool IsRestoreExpired(SharedSessionEntry entry)
        {
            DateTime? lastActivity = entry.GetLastActivityUtc();
            if (lastActivity == null)
            {
                return false;
            }

            double timeout = Math.Max(entry.SessionTimeout, 0);
            DateTime expiry = lastActivity.Value.AddMilliseconds(timeout);
            if (!entry.LastContactAt.IsNull && m_options.LivenessInterval > TimeSpan.Zero)
            {
                expiry = expiry
                    .AddMilliseconds(timeout / kHeartbeatFraction)
                    .Add(m_options.LivenessInterval);
            }
            return m_restoreTimeProvider.GetUtcNow().UtcDateTime >= expiry;
        }

        private static bool IsOwnedByThisReplica(SharedSessionEntry entry, string replicaId)
        {
            // An entry written before ownership was mirrored belongs to whoever serves it.
            return entry.OwnerId == null ||
                string.Equals(entry.OwnerId, replicaId, StringComparison.Ordinal);
        }

        private bool IsOwnedByThisReplica(SharedSessionEntry entry)
        {
            return IsOwnedByThisReplica(entry, m_replicaId);
        }

        private void TrackLocalSession(NodeId sessionId, NodeId authenticationToken, double sessionTimeout)
        {
            m_tokensBySession[sessionId] = authenticationToken;
            m_sessionsByToken[authenticationToken] = sessionId;
            m_liveness[authenticationToken] = new LivenessState(sessionTimeout, DateTime.MinValue);
        }

        private void MarkContactMirrored(NodeId authenticationToken, DateTime contactUtc)
        {
            if (m_liveness.TryGetValue(authenticationToken, out LivenessState? state))
            {
                m_liveness.TryUpdate(authenticationToken, state with { MirroredContactUtc = contactUtc }, state);
            }
        }

        private async ValueTask TryRemoveEntryAsync(SharedSessionEntry entry, CancellationToken cancellationToken)
        {
            try
            {
                await TryRemoveEntryVersionAsync(entry, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                m_logger.FailedToRemoveMirroredSessionFromSharedStore(ex);
            }
        }

        /// <summary>
        /// Drops the local copy of a session whose entry another replica has taken over,
        /// without deleting the entry. The client moved to the other replica; the copy
        /// here would otherwise validate against a superseded nonce and, on timeout,
        /// look like the end of the session.
        /// </summary>
        private async ValueTask DropLocalCopyIfOwnedElsewhereAsync(
            NodeId authenticationToken,
            ISession session,
            CancellationToken cancellationToken)
        {
            SharedSessionEntry? entry;
            try
            {
                entry = await m_sessionStore
                    .TryGetAsync(authenticationToken, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                m_logger.FailedToMirrorActivatedSessionToSharedStore(ex);
                return;
            }

            if (entry != null && !IsOwnedByThisReplica(entry))
            {
                await DropLocalCopyAsync(authenticationToken, session).ConfigureAwait(false);
            }
        }

        private async ValueTask DropLocalCopyAsync(NodeId authenticationToken, ISession session)
        {
            if (!ReferenceEquals(GetSession(authenticationToken), session) ||
                !SessionTermination.TryClaimClose(session))
            {
                return;
            }
            if (m_logger.IsEnabled(LogLevel.Information))
            {
                m_logger.DroppingUnownedSession(TokenDigest(authenticationToken), session.Id);
            }

            // The subscriptions are kept, like on a timeout: the client may transfer them.
            await m_server.CloseClaimedSessionAsync(session, false, CancellationToken.None)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Checks whether mirrored fast sessions still belong to this replica, and
        /// mirrors client activity at most once per <see cref="kHeartbeatFraction"/>th
        /// of their timeout (OPC 10000-4 5.7.2.1).
        /// </summary>
        internal async ValueTask RefreshLivenessAsync(CancellationToken cancellationToken)
        {
            DateTime now = m_restoreTimeProvider.GetUtcNow().UtcDateTime;
            long nowTicks = m_restoreTimeProvider.GetTimestampMilliseconds();
            foreach (ISession session in GetSessions())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!session.Activated ||
                    !m_tokensBySession.TryGetValue(session.Id, out NodeId token) ||
                    !m_liveness.TryGetValue(token, out LivenessState? state))
                {
                    continue;
                }

                DateTime lastContact = now.AddMilliseconds(-Math.Max(0, nowTicks - session.LastContactTickCount));
                bool writeHeartbeat = (lastContact - state.MirroredContactUtc).TotalMilliseconds >=
                    state.SessionTimeout / kHeartbeatFraction;
                bool authoritative = UsesAuthoritativeActivation(session.EndpointDescription);
                if (!authoritative && !writeHeartbeat)
                {
                    continue;
                }

                try
                {
                    await MirrorContactAsync(token, session, authoritative, writeHeartbeat, lastContact,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    m_logger.FailedToMirrorActivatedSessionToSharedStore(ex);
                }
            }
        }

        private async ValueTask MirrorContactAsync(
            NodeId authenticationToken,
            ISession session,
            bool authoritative,
            bool writeHeartbeat,
            DateTime lastContactUtc,
            CancellationToken cancellationToken)
        {
            SemaphoreSlim mirrorLock = m_mirrorLocks.GetOrAdd(
                authenticationToken,
                static _ => new SemaphoreSlim(1, 1));
            await mirrorLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            bool discardLocalCopy = false;
            try
            {
                for (int attempt = 0; attempt < kMaxConditionalWriteAttempts; attempt++)
                {
                    SharedSessionEntry? existing = await m_sessionStore
                        .TryGetAsync(authenticationToken, cancellationToken)
                        .ConfigureAwait(false);
                    if (existing == null)
                    {
                        discardLocalCopy = authoritative;
                        break;
                    }
                    if (authoritative ? !MatchesLocalOwner(existing, session) : !IsOwnedByThisReplica(existing))
                    {
                        discardLocalCopy = true;
                        break;
                    }
                    if (!writeHeartbeat)
                    {
                        return;
                    }

                    DateTime? mirrored = existing.LastContactAt.IsNull ? null : existing.LastContactAt.ToDateTime();
                    if (mirrored != null && mirrored.Value >= lastContactUtc)
                    {
                        MarkContactMirrored(authenticationToken, mirrored.Value);
                        return;
                    }

                    SharedSessionEntry updated = existing with
                    {
                        LastContactAt = DateTimeUtc.From(lastContactUtc),
                        OwnerId = m_replicaId
                    };
                    if (await TryReplaceEntryAsync(existing, updated, cancellationToken).ConfigureAwait(false))
                    {
                        MarkContactMirrored(authenticationToken, lastContactUtc);
                        return;
                    }
                }
            }
            finally
            {
                mirrorLock.Release();
            }

            if (discardLocalCopy)
            {
                await DropLocalCopyAsync(authenticationToken, session).ConfigureAwait(false);
            }
        }

        private bool MatchesLocalOwner(SharedSessionEntry entry, ISession session)
        {
            return entry.SessionId == session.Id &&
                string.Equals(entry.OwnerId, m_replicaId, StringComparison.Ordinal);
        }

        /// <summary>
        /// Removes the entries of sessions that can no longer be restored (their owner
        /// crashed or shut down, so nobody closed them).
        /// </summary>
        internal async ValueTask<int> SweepAsync(CancellationToken cancellationToken)
        {
            int removed = 0;
            await foreach (SharedSessionEntry entry in m_sessionStore
                .EnumerateAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                if (IsRestoreExpired(entry) &&
                    !m_sessionsByToken.ContainsKey(entry.AuthenticationToken) &&
                    await TryRemoveEntryVersionAsync(entry, cancellationToken).ConfigureAwait(false))
                {
                    removed++;
                }
            }

            if (removed > 0)
            {
                m_logger.SweptSharedSessionStore(removed);
            }
            return removed;
        }

        private async Task RunMaintenanceAsync(CancellationToken cancellationToken)
        {
            bool liveness = m_options.LivenessInterval > TimeSpan.Zero;
            bool sweep = m_options.SweepInterval > TimeSpan.Zero;

            // Each interval is an upper bound on its job's cadence, so the loop wakes up
            // at the shorter one and runs whichever job is due.
            TimeSpan period = !liveness ? m_options.SweepInterval
                : !sweep ? m_options.LivenessInterval
                : m_options.LivenessInterval < m_options.SweepInterval
                    ? m_options.LivenessInterval
                    : m_options.SweepInterval;
            DateTime start = m_restoreTimeProvider.GetUtcNow().UtcDateTime;
            DateTime nextLiveness = start + m_options.LivenessInterval;
            DateTime nextSweep = start + m_options.SweepInterval;
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    await m_restoreTimeProvider.Delay(period, cancellationToken).ConfigureAwait(false);
                    if (liveness && m_restoreTimeProvider.GetUtcNow().UtcDateTime >= nextLiveness)
                    {
                        nextLiveness = m_restoreTimeProvider.GetUtcNow().UtcDateTime + m_options.LivenessInterval;
                        try
                        {
                            await RefreshLivenessAsync(cancellationToken).ConfigureAwait(false);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            m_logger.SessionMaintenanceFailed(ex);
                        }
                    }

                    DateTime now = m_restoreTimeProvider.GetUtcNow().UtcDateTime;
                    if (sweep && now >= nextSweep)
                    {
                        nextSweep = now + m_options.SweepInterval;
                        try
                        {
                            await SweepAsync(cancellationToken).ConfigureAwait(false);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            m_logger.SessionMaintenanceFailed(ex);
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // stopped
            }
        }

        private async ValueTask StopMaintenanceAsync()
        {
            CancellationTokenSource? cts;
            Task? task;
            lock (m_maintenanceLock)
            {
                cts = m_maintenanceCts;
                task = m_maintenanceTask;
                m_maintenanceCts = null;
                m_maintenanceTask = null;
            }
            if (cts == null)
            {
                return;
            }

            cts.Cancel();
            try
            {
                if (task != null)
                {
                    await task.ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // stopped
            }
            finally
            {
                cts.Dispose();
            }
        }

        private static bool IsValidSessionNonce(ByteString nonce)
        {
            return !nonce.IsNull &&
                nonce.Length is >= 32 and <= 128 &&
                Nonce.ValidateNonce(nonce.ToArray(), MessageSecurityMode.Sign, 32);
        }

        private static bool HasValidClientUserIdState(SharedSessionEntry entry)
        {
            if (entry.SecurityStateVersion is < SharedSessionEntry.MinimumRestorableSecurityStateVersion or
                    > SharedSessionEntry.CurrentSecurityStateVersion ||
                !entry.HasActivatedUserIdentity)
            {
                return false;
            }

            return entry.ClientUserTokenType switch
            {
                UserTokenType.Anonymous => entry.ClientUserId == null,
                UserTokenType.UserName or
                UserTokenType.Certificate or
                UserTokenType.IssuedToken => entry.ClientUserId != null,
                _ => false
            };
        }

        private DateTimeUtc UtcNow()
        {
            return DateTimeUtc.From(m_restoreTimeProvider.GetUtcNow().UtcDateTime);
        }

        private static string TokenDigest(NodeId authenticationToken)
        {
            byte[] data = Encoding.UTF8.GetBytes(authenticationToken.ToString());
#if NET8_0_OR_GREATER
            byte[] hash = SHA256.HashData(data);
#else
            byte[] hash;
            using (var sha = SHA256.Create())
            {
                hash = sha.ComputeHash(data);
            }
#endif
            return Convert.ToBase64String(hash, 0, 8);
        }

        /// <summary>
        /// The mirrored liveness of a session this replica serves.
        /// </summary>
        private sealed record LivenessState(double SessionTimeout, DateTime MirroredContactUtc);

        /// <summary>
        /// Orders the initial shared publication with the close of the same local session.
        /// </summary>
        private sealed class CreationPublication
        {
            /// <summary>
            /// Serializes initial publication with explicit close.
            /// </summary>
            public SemaphoreSlim Gate { get; } = new(1, 1);

            /// <summary>
            /// Whether close prevents a future initial publication.
            /// </summary>
            public bool Closed { get; set; }
        }

        /// <summary>
        /// The heartbeat is written at most once per this fraction of the session timeout.
        /// </summary>
        private const int kHeartbeatFraction = 4;
        private const int kMaxConditionalWriteAttempts = 3;
        private const string kDiagnosticsNamespaceUri = Ua.Namespaces.OpcUa + "Diagnostics";
        private readonly string m_replicaId;
        private readonly ConcurrentDictionary<NodeId, NodeId> m_sessionsByToken = new();
        private readonly ConcurrentDictionary<NodeId, LivenessState> m_liveness = new();
        private readonly ConditionalWeakTable<ISession, SharedSessionEntry> m_pendingRestores = new();
        private readonly ConditionalWeakTable<ISession, SharedSessionEntry> m_admittedRestores = new();
        private readonly ConditionalWeakTable<ISession, SharedSessionEntry> m_committedActivations = new();
        private readonly ConditionalWeakTable<ISession, CreationPublication> m_creationPublications = new();
        private readonly Lock m_maintenanceLock = new();
        private CancellationTokenSource? m_maintenanceCts;
        private Task? m_maintenanceTask;
        private readonly IServerInternal m_server;
        private readonly ISharedSessionStore m_sessionStore;
        private readonly Func<string, Certificate?> m_serverCertificateProvider;
        private readonly DistributedSessionOptions m_options;
        private readonly bool m_fastReconnectEnabled;
        private readonly ITelemetryContext m_telemetry;
        private readonly ILogger m_logger;
        private readonly TimeProvider m_restoreTimeProvider;
        private readonly ConcurrentDictionary<NodeId, NodeId> m_tokensBySession = new();
        private readonly ConcurrentDictionary<NodeId, SemaphoreSlim> m_mirrorLocks = new();
        private readonly ConcurrentDictionary<NodeId, long> m_mirroredSequences = new();

        /// <summary>
        /// The channel certificate as the mirrored session state records it: empty, not
        /// null, for a channel without a client certificate.
        /// </summary>
        private static ByteString EmptyIfNull(ByteString certificate)
        {
            return certificate.IsNull ? ByteString.Empty : certificate;
        }
    }

    /// <summary>
    /// Source-generated log messages for <see cref="DistributedSessionManager"/>.
    /// </summary>
    internal static partial class DistributedSessionManagerLog
    {
        [LoggerMessage(EventId = RedundancyServerEventIds.DistributedSessionManager + 0, Level = LogLevel.Warning,
            Message = "Failed to mirror created session to the shared store.")]
        public static partial void FailedToMirrorCreatedSessionToSharedStore(
            this ILogger logger,
            Exception exception);

        [LoggerMessage(EventId = RedundancyServerEventIds.DistributedSessionManager + 1, Level = LogLevel.Warning,
            Message = "Failed to mirror activated session to the shared store.")]
        public static partial void FailedToMirrorActivatedSessionToSharedStore(
            this ILogger logger,
            Exception exception);

        [LoggerMessage(EventId = RedundancyServerEventIds.DistributedSessionManager + 2, Level = LogLevel.Warning,
            Message = "Failed to remove mirrored session from the shared store.")]
        public static partial void FailedToRemoveMirroredSessionFromSharedStore(
            this ILogger logger,
            Exception exception);

        [LoggerMessage(EventId = RedundancyServerEventIds.DistributedSessionManager + 3, Level = LogLevel.Warning,
            Message = "Distributed session restore for {Token} rejected: {Reason}.")]
        public static partial void DistributedSessionRestoreRejected(
            this ILogger logger,
            string token,
            DistributedSessionManager.RestoreDecision reason);

        [LoggerMessage(EventId = RedundancyServerEventIds.DistributedSessionManager + 4, Level = LogLevel.Information,
            Message = "Distributed session restored from shared store for {Token} (session {SessionId}).")]
        public static partial void DistributedSessionRestoredFromSharedStore(
            this ILogger logger,
            string token,
            NodeId sessionId);

        [LoggerMessage(EventId = RedundancyServerEventIds.DistributedSessionManager + 5, Level = LogLevel.Warning,
            Message = "Mirrored session has no client certificate; cannot restore.")]
        public static partial void MirroredSessionHasNoClientCertificate(this ILogger logger);

        [LoggerMessage(EventId = RedundancyServerEventIds.DistributedSessionManager + 6, Level = LogLevel.Warning,
            Message = "No server certificate available for policy {Policy}; cannot restore session.")]
        public static partial void NoServerCertificateAvailableForPolicy(this ILogger logger, string policy);

        [LoggerMessage(EventId = RedundancyServerEventIds.DistributedSessionManager + 7, Level = LogLevel.Warning,
            Message = "The mirrored session {Token} changed concurrently; the update was not written.")]
        public static partial void MirroredSessionUpdateConflicted(this ILogger logger, string token);

        [LoggerMessage(EventId = RedundancyServerEventIds.DistributedSessionManager + 8, Level = LogLevel.Information,
            Message = "Session {SessionId} ({Token}) is no longer owned here; dropping the local copy.")]
        public static partial void DroppingUnownedSession(
            this ILogger logger,
            string token,
            NodeId sessionId);

        [LoggerMessage(EventId = RedundancyServerEventIds.DistributedSessionManager + 9, Level = LogLevel.Information,
            Message = "Swept the shared session store: {Entries} expired session entries removed.")]
        public static partial void SweptSharedSessionStore(this ILogger logger, int entries);

        [LoggerMessage(EventId = RedundancyServerEventIds.DistributedSessionManager + 10, Level = LogLevel.Warning,
            Message = "Distributed session maintenance failed.")]
        public static partial void SessionMaintenanceFailed(this ILogger logger, Exception exception);

        [LoggerMessage(EventId = RedundancyServerEventIds.DistributedSessionManager + 11, Level = LogLevel.Warning,
            Message = "Failed to load the mirrored continuation points of restored session {SessionId}.")]
        public static partial void FailedToLoadMirroredContinuationPoints(
            this ILogger logger,
            Exception exception,
            NodeId sessionId);

        [LoggerMessage(EventId = RedundancyServerEventIds.DistributedSessionManager + 12, Level = LogLevel.Warning,
            Message = "Failed to verify committed session request admission.")]
        public static partial void FailedToVerifySessionRequestAdmission(this ILogger logger, Exception exception);
    }

}
