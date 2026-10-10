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
    /// activation path still performs the full client-certificate signature
    /// check against the mirrored <c>serverNonce</c>, the nonce is consumed
    /// exactly once across the replica set (so a captured activation cannot be
    /// replayed), and the SecurityPolicy/Mode must match the original.
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
    /// the entry. A restore takes ownership with a conditional write after the restored
    /// activation passed validation, and the previous owner then drops its stale local
    /// copy without deleting the entry. A restored session keeps its SessionId. A
    /// periodic sweep removes the entries of timed-out sessions (for example those of a
    /// replica that crashed) and orphaned consumed-nonce markers.
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
        /// <param name="nonceRegistry">The cross-replica single-use nonce registry.</param>
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
            ISingleUseNonceRegistry nonceRegistry,
            Func<string, Certificate?> serverCertificateProvider,
            DistributedSessionOptions? options = null,
            TimeProvider? timeProvider = null)
            : base(server, configuration, timeProvider)
        {
            m_server = server ?? throw new ArgumentNullException(nameof(server));
            m_sessionStore = sessionStore ?? throw new ArgumentNullException(nameof(sessionStore));
            m_nonceRegistry = nonceRegistry ?? throw new ArgumentNullException(nameof(nonceRegistry));
            m_serverCertificateProvider = serverCertificateProvider
                ?? throw new ArgumentNullException(nameof(serverCertificateProvider));
            m_options = options ?? new DistributedSessionOptions();
            m_telemetry = server.Telemetry;
            m_logger = server.Telemetry.CreateLogger<DistributedSessionManager>();
            m_restoreTimeProvider = timeProvider
                ?? (server as ITimeProviderProvider)?.TimeProvider
                ?? TimeProvider.System;
            m_replicaId = Guid.NewGuid().ToString("N");
        }

        /// <inheritdoc/>
        protected override bool SupportsSessionRestore => m_options.EnableFastReconnect;

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
            if (m_options.EnableFastReconnect &&
                m_sessionsByToken.TryGetValue(authenticationToken, out NodeId localSessionId))
            {
                await DropLocalCopyIfOwnedElsewhereAsync(
                    authenticationToken,
                    localSessionId,
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
            // A restored session becomes this replica's once its first activation has
            // committed: nothing shared beyond the consumed nonce is touched before.
            SharedSessionEntry? restoredFrom = null;
            lock (m_pendingRestoresLock)
            {
                if (m_admittedRestores.TryGetValue(session, out restoredFrom))
                {
                    m_admittedRestores.Remove(session);
                }
            }
            if (restoredFrom != null)
            {
                await CompleteRestoreAsync(authenticationToken, session, restoredFrom, cancellationToken)
                    .ConfigureAwait(false);
            }
            else if (!m_tokensBySession.ContainsKey(session.Id))
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
            if (string.IsNullOrEmpty(securityPolicyUri))
            {
                securityPolicyUri = session.EndpointDescription.SecurityPolicyUri;
            }

            SecurityPolicyInfo? info = string.IsNullOrEmpty(securityPolicyUri)
                ? null
                : SecurityPolicies.Default.GetInfo(securityPolicyUri!);
            return info != null && info.EphemeralKeyAlgorithm != CertificateKeyAlgorithm.None;
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
        public override async ValueTask CloseSessionAsync(
            NodeId sessionId,
            CancellationToken cancellationToken = default)
        {
            if (m_tokensBySession.TryRemove(sessionId, out NodeId token))
            {
                m_sessionsByToken.TryRemove(token, out _);
                try
                {
                    // Only the owner ends the mirrored session. A copy left behind on a
                    // replica the client moved away from times out eventually, and must
                    // not delete the entry of the session that is live elsewhere.
                    SharedSessionEntry? entry = await m_sessionStore
                        .TryGetAsync(token, cancellationToken)
                        .ConfigureAwait(false);
                    if (entry != null && IsOwnedByThisReplica(entry))
                    {
                        await m_sessionStore.TryRemoveAsync(entry, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    m_logger.FailedToRemoveMirroredSessionFromSharedStore(ex);
                }
            }

            await base.CloseSessionAsync(sessionId, cancellationToken).ConfigureAwait(false);
            if (!token.IsNull)
            {
                m_mirrorLocks.TryRemove(token, out _);
                m_mirroredSequences.TryRemove(token, out _);
                m_liveness.TryRemove(token, out _);
            }
        }

        /// <inheritdoc/>
        protected override async ValueTask<ISession?> RestoreSessionAsync(
            NodeId authenticationToken,
            OperationContext context,
            CancellationToken cancellationToken = default)
        {
            if (!m_options.EnableFastReconnect)
            {
                return null;
            }

            SharedSessionEntry? entry = await m_sessionStore
                .TryGetAsync(authenticationToken, cancellationToken)
                .ConfigureAwait(false);
            if (entry == null)
            {
                return null;
            }

            EndpointDescription endpoint = context.ChannelContext!.EndpointDescription!;
            RestoreDecision decision = AuthorizeRestore(
                entry,
                endpoint.SecurityPolicyUri ?? string.Empty,
                endpoint.SecurityMode,
                EmptyIfNull(context.ChannelContext.ClientChannelCertificate));
            if (decision != RestoreDecision.Authorized)
            {
                m_logger.DistributedSessionRestoreRejected(TokenDigest(authenticationToken), decision);
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

                // The single-use nonce is consumed, and the session taken over, only
                // once its first activation has passed the signature and identity
                // checks (AdmitRestoredSessionAsync). A failed attempt therefore leaves
                // the mirrored session restorable here or on another replica.
                lock (m_pendingRestoresLock)
                {
                    m_pendingRestores.Remove(session);
                    m_pendingRestores.Add(session, entry);
                }
            }
            return session;
        }

        /// <inheritdoc/>
        protected override async ValueTask<bool> AdmitRestoredSessionAsync(
            NodeId authenticationToken,
            ISession session,
            OperationContext context,
            CancellationToken cancellationToken)
        {
            SharedSessionEntry? entry;
            lock (m_pendingRestoresLock)
            {
                if (!m_pendingRestores.TryGetValue(session, out entry))
                {
                    return false;
                }
                m_pendingRestores.Remove(session);
            }

            bool consumed = await m_nonceRegistry
                .TryConsumeAsync(entry.ServerNonce, cancellationToken)
                .ConfigureAwait(false);
            if (!consumed)
            {
                m_logger.DistributedSessionRestoreRejected(
                    TokenDigest(authenticationToken),
                    RestoreDecision.NonceReplayed);
                return false;
            }

            // Only the single-use nonce is consumed here, the one step that must precede
            // the commit to stay single-use across the replica set. Ownership, the
            // continuation points and the audit follow once the activation committed.
            lock (m_pendingRestoresLock)
            {
                m_admittedRestores.Remove(session);
                m_admittedRestores.Add(session, entry);
            }
            return true;
        }

        /// <summary>
        /// Completes a restore after its first activation committed: this replica takes
        /// over the entry (the previous owner then drops its stale copy), tracks the
        /// session locally, loads its mirrored continuation points and audits it.
        /// </summary>
        private async ValueTask CompleteRestoreAsync(
            NodeId authenticationToken,
            ISession session,
            SharedSessionEntry entry,
            CancellationToken cancellationToken)
        {
            TrackLocalSession(session.Id, authenticationToken, entry.SessionTimeout);

            // A failure to record the ownership is only logged, the activation is valid.
            try
            {
                await TakeOwnershipAsync(entry, session.Id, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                m_logger.FailedToMirrorActivatedSessionToSharedStore(ex);
            }

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
            /// <summary>The restore is authorized and the nonce was consumed.</summary>
            Authorized,

            /// <summary>The mirrored session timed out before the restore attempt.</summary>
            Expired,

            /// <summary>The mirrored record predates or omits required security state.</summary>
            SecurityStateMissing,

            /// <summary>The SecurityPolicy/Mode does not match the original.</summary>
            PolicyMismatch,

            /// <summary>The new SecureChannel uses a different client certificate.</summary>
            ClientCertificateMismatch,

            /// <summary>The mirrored nonce is malformed.</summary>
            NonceInvalid,

            /// <summary>The mirrored nonce is missing or was already consumed (replay).</summary>
            NonceReplayed,

            /// <summary>
            /// The user identity token is encrypted with an EphemeralKey whose private
            /// part only the replica that issued it holds.
            /// </summary>
            EphemeralKeyUnavailable
        }

        /// <summary>
        /// Enforces the cross-channel security checks and consumes the mirrored
        /// <c>serverNonce</c> exactly once. Factored out so the security policy
        /// is unit-testable without constructing a live session; a live restore
        /// runs the checks first and consumes the nonce only after the activation
        /// passed validation.
        /// </summary>
        internal async ValueTask<RestoreDecision> AuthorizeAndConsumeAsync(
            SharedSessionEntry entry,
            string securityPolicyUri,
            MessageSecurityMode securityMode,
            ByteString clientChannelCertificate,
            CancellationToken cancellationToken = default)
        {
            RestoreDecision decision = AuthorizeRestore(
                entry,
                securityPolicyUri,
                securityMode,
                clientChannelCertificate);
            if (decision != RestoreDecision.Authorized)
            {
                return decision;
            }

            bool consumed = await m_nonceRegistry
                .TryConsumeAsync(entry.ServerNonce, cancellationToken)
                .ConfigureAwait(false);
            return consumed ? RestoreDecision.Authorized : RestoreDecision.NonceReplayed;
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

            if (!string.Equals(entry.SecurityPolicyUri, securityPolicyUri, StringComparison.Ordinal) ||
                entry.SecurityMode != (int)securityMode)
            {
                return RestoreDecision.PolicyMismatch;
            }

            bool requiresClientCertificate =
                securityMode != MessageSecurityMode.None ||
                !string.Equals(securityPolicyUri, SecurityPolicies.None, StringComparison.Ordinal);
            if ((requiresClientCertificate &&
                    entry.OriginalClientChannelCertificate.IsEmpty) ||
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
            if (!HasValidClientUserIdState(entry))
            {
                return null;
            }

            bool requiresClientCertificate =
                entry.SecurityMode != (int)MessageSecurityMode.None ||
                !string.Equals(entry.SecurityPolicyUri, SecurityPolicies.None, StringComparison.Ordinal);
            if (requiresClientCertificate &&
                (entry.ClientCertificateChain.IsNull ||
                    entry.ClientCertificateChain.IsEmpty ||
                    entry.OriginalClientChannelCertificate.IsEmpty))
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

            using CertificateCollection parsed = entry.ClientCertificateChain.IsNull ||
                entry.ClientCertificateChain.IsEmpty
                    ? []
                    : Utils.ParseCertificateChainBlob(entry.ClientCertificateChain.ToArray(), m_telemetry);
            if (requiresClientCertificate && parsed.Count == 0)
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
                clientCertificate = parsed.Count == 0 ? null : parsed[0].AddRef();
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
            // The activation committed on this replica, so the client is here: the
            // write also (re)asserts ownership. It is conditional, so it never
            // silently overwrites a concurrent change of the entry.
            for (int attempt = 0; attempt < kMaxConditionalWriteAttempts; attempt++)
            {
                SharedSessionEntry? existing = await m_sessionStore
                    .TryGetAsync(authenticationToken, cancellationToken)
                    .ConfigureAwait(false);
                if (existing == null)
                {
                    return;
                }

                DateTimeUtc now = UtcNow();
                SharedSessionEntry updated = existing with
                {
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
                if (await m_sessionStore.TryReplaceAsync(existing, updated, cancellationToken).ConfigureAwait(false))
                {
                    MarkContactMirrored(authenticationToken, now.ToDateTime());
                    return;
                }
            }

            m_logger.MirroredSessionUpdateConflicted(TokenDigest(authenticationToken));
        }

        /// <summary>
        /// Records this replica as the owner of a restored session, with the SessionId
        /// it was restored under, after its first activation passed validation.
        /// </summary>
        private async ValueTask TakeOwnershipAsync(
            SharedSessionEntry restoredFrom,
            NodeId sessionId,
            CancellationToken cancellationToken)
        {
            SharedSessionEntry? current = restoredFrom;
            for (int attempt = 0; attempt < kMaxConditionalWriteAttempts; attempt++)
            {
                if (current == null)
                {
                    // Removed meanwhile (for example by the sweep): the session is live
                    // here, so the entry is written again.
                    SharedSessionEntry recreated = restoredFrom with
                    {
                        SessionId = sessionId,
                        OwnerId = m_replicaId,
                        LastContactAt = UtcNow(),
                        SecurityStateVersion = SharedSessionEntry.CurrentSecurityStateVersion
                    };
                    await m_sessionStore.PutAsync(recreated, cancellationToken).ConfigureAwait(false);
                    MarkContactMirrored(recreated.AuthenticationToken, recreated.LastContactAt.ToDateTime());
                    return;
                }

                SharedSessionEntry updated = current with
                {
                    // A SessionId that could not be kept (it was in use here) is
                    // replaced, so the next failover loads the continuation points
                    // that this replica mirrors under its own id.
                    SessionId = sessionId,
                    OwnerId = m_replicaId,
                    LastContactAt = UtcNow(),
                    SecurityStateVersion = SharedSessionEntry.CurrentSecurityStateVersion
                };
                if (await m_sessionStore.TryReplaceAsync(current, updated, cancellationToken).ConfigureAwait(false))
                {
                    MarkContactMirrored(updated.AuthenticationToken, updated.LastContactAt.ToDateTime());
                    return;
                }

                current = await m_sessionStore
                    .TryGetAsync(restoredFrom.AuthenticationToken, cancellationToken)
                    .ConfigureAwait(false);
            }

            m_logger.MirroredSessionUpdateConflicted(TokenDigest(restoredFrom.AuthenticationToken));
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
                m_liveness[authenticationToken] = state with { MirroredContactUtc = contactUtc };
            }
        }

        private async ValueTask TryRemoveEntryAsync(SharedSessionEntry entry, CancellationToken cancellationToken)
        {
            try
            {
                await m_sessionStore.TryRemoveAsync(entry, cancellationToken).ConfigureAwait(false);
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
            NodeId sessionId,
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
                await DropLocalCopyAsync(authenticationToken, sessionId).ConfigureAwait(false);
            }
        }

        private async ValueTask DropLocalCopyAsync(NodeId authenticationToken, NodeId sessionId)
        {
            // Forget the token first, so the close below leaves the entry alone.
            if (!m_tokensBySession.TryRemove(sessionId, out _))
            {
                return;
            }
            m_sessionsByToken.TryRemove(authenticationToken, out _);
            m_liveness.TryRemove(authenticationToken, out _);
            if (m_logger.IsEnabled(LogLevel.Information))
            {
                m_logger.DroppingSessionOwnedByAnotherReplica(TokenDigest(authenticationToken), sessionId);
            }

            // The subscriptions are kept, like on a timeout: the client may transfer them.
            await m_server.CloseSessionAsync(null!, sessionId, false, CancellationToken.None)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Mirrors the client activity of the sessions this replica serves, at most once
        /// per <see cref="kHeartbeatFraction"/>th of their timeout, so a standby can tell a
        /// session in use from one that timed out (OPC 10000-4 5.7.2.1).
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
                if ((lastContact - state.MirroredContactUtc).TotalMilliseconds <
                    state.SessionTimeout / kHeartbeatFraction)
                {
                    continue;
                }

                try
                {
                    await MirrorContactAsync(token, session.Id, lastContact, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    m_logger.FailedToMirrorActivatedSessionToSharedStore(ex);
                }
            }
        }

        private async ValueTask MirrorContactAsync(
            NodeId authenticationToken,
            NodeId sessionId,
            DateTime lastContactUtc,
            CancellationToken cancellationToken)
        {
            SemaphoreSlim mirrorLock = m_mirrorLocks.GetOrAdd(
                authenticationToken,
                static _ => new SemaphoreSlim(1, 1));
            await mirrorLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            bool ownedElsewhere = false;
            try
            {
                for (int attempt = 0; attempt < kMaxConditionalWriteAttempts; attempt++)
                {
                    SharedSessionEntry? existing = await m_sessionStore
                        .TryGetAsync(authenticationToken, cancellationToken)
                        .ConfigureAwait(false);
                    if (existing == null)
                    {
                        return;
                    }
                    if (!IsOwnedByThisReplica(existing))
                    {
                        ownedElsewhere = true;
                        break;
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
                    if (await m_sessionStore.TryReplaceAsync(existing, updated, cancellationToken).ConfigureAwait(false))
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

            if (ownedElsewhere)
            {
                await DropLocalCopyAsync(authenticationToken, sessionId).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Removes the entries of sessions that can no longer be restored (their owner
        /// crashed or shut down, so nobody closed them) and the consumed-nonce markers
        /// that no live entry references any more.
        /// </summary>
        internal async ValueTask<(int Entries, int NonceMarkers)> SweepAsync(CancellationToken cancellationToken)
        {
            int removed = 0;
            var liveNonces = new List<ByteString>();
            await foreach (SharedSessionEntry entry in m_sessionStore
                .EnumerateAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                if (IsRestoreExpired(entry) &&
                    !m_sessionsByToken.ContainsKey(entry.AuthenticationToken) &&
                    await m_sessionStore.TryRemoveAsync(entry, cancellationToken).ConfigureAwait(false))
                {
                    removed++;
                    continue;
                }
                liveNonces.Add(entry.ServerNonce);
            }

            int purged = await m_nonceRegistry
                .PurgeAsync(liveNonces, m_options.NonceMarkerRetention, cancellationToken)
                .ConfigureAwait(false);
            if (removed > 0 || purged > 0)
            {
                m_logger.SweptSharedSessionStore(removed, purged);
            }
            return (removed, purged);
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
        private readonly Lock m_pendingRestoresLock = new();
        private readonly Lock m_maintenanceLock = new();
        private CancellationTokenSource? m_maintenanceCts;
        private Task? m_maintenanceTask;
        private readonly IServerInternal m_server;
        private readonly ISharedSessionStore m_sessionStore;
        private readonly ISingleUseNonceRegistry m_nonceRegistry;
        private readonly Func<string, Certificate?> m_serverCertificateProvider;
        private readonly DistributedSessionOptions m_options;
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
            Message = "Session {SessionId} ({Token}) is served by another replica now; dropping the local copy.")]
        public static partial void DroppingSessionOwnedByAnotherReplica(
            this ILogger logger,
            string token,
            NodeId sessionId);

        [LoggerMessage(EventId = RedundancyServerEventIds.DistributedSessionManager + 9, Level = LogLevel.Information,
            Message = "Swept the shared session store: {Entries} expired session entries and {NonceMarkers} " +
                "consumed-nonce markers removed.")]
        public static partial void SweptSharedSessionStore(this ILogger logger, int entries, int nonceMarkers);

        [LoggerMessage(EventId = RedundancyServerEventIds.DistributedSessionManager + 10, Level = LogLevel.Warning,
            Message = "Distributed session maintenance failed.")]
        public static partial void SessionMaintenanceFailed(this ILogger logger, Exception exception);

        [LoggerMessage(EventId = RedundancyServerEventIds.DistributedSessionManager + 11, Level = LogLevel.Warning,
            Message = "Failed to load the mirrored continuation points of restored session {SessionId}.")]
        public static partial void FailedToLoadMirroredContinuationPoints(
            this ILogger logger,
            Exception exception,
            NodeId sessionId);
    }

}
