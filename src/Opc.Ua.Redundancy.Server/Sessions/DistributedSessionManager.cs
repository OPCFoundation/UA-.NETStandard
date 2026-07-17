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
        }

        /// <inheritdoc/>
        protected override bool SupportsSessionRestore => m_options.EnableFastReconnect;

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
                m_tokensBySession[result.SessionId] = result.AuthenticationToken;
                SharedSessionEntry entry = BuildEntry(
                    context,
                    result,
                    sessionName,
                    clientNonce,
                    clientDescription,
                    endpointUrl,
                    clientCertificate);
                await m_sessionStore.PutAsync(entry, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Mirroring is best-effort; never fail an otherwise-valid session
                // because the shared store is unavailable.
                m_logger.FailedToMirrorCreatedSessionToSharedStore(ex);
            }

            return result;
        }

        internal override async ValueTask OnSessionActivatedAsync(
            NodeId authenticationToken,
            ISession session,
            ByteString serverNonce,
            string clientUserId,
            CancellationToken cancellationToken)
        {
            // Mirror the freshly issued serverNonce so a standby validates the
            // client's next activation against it (and consumes it single-use).
            try
            {
                await MirrorActivationAsync(
                    authenticationToken,
                    serverNonce,
                    clientUserId,
                    cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                m_logger.FailedToMirrorActivatedSessionToSharedStore(ex);
            }
        }

        /// <inheritdoc/>
        public override async ValueTask CloseSessionAsync(
            NodeId sessionId,
            CancellationToken cancellationToken = default)
        {
            if (m_tokensBySession.TryRemove(sessionId, out NodeId token))
            {
                try
                {
                    await m_sessionStore.RemoveAsync(token, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    m_logger.FailedToRemoveMirroredSessionFromSharedStore(ex);
                }
            }

            await base.CloseSessionAsync(sessionId, cancellationToken).ConfigureAwait(false);
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

            if (IsRestoreExpired(entry))
            {
                m_logger.DistributedSessionRestoreRejected(TokenDigest(authenticationToken), RestoreDecision.Expired);
                return null;
            }

            EndpointDescription endpoint = context.ChannelContext!.EndpointDescription!;
            RestoreDecision decision = await AuthorizeAndConsumeAsync(
                entry,
                endpoint.SecurityPolicyUri ?? string.Empty,
                endpoint.SecurityMode,
                context.ChannelContext.ClientChannelCertificate.ToByteString(),
                cancellationToken).ConfigureAwait(false);
            if (decision != RestoreDecision.Authorized)
            {
                m_logger.DistributedSessionRestoreRejected(TokenDigest(authenticationToken), decision);
                return null;
            }

            ISession? session = ReconstructSession(entry, authenticationToken, context);
            if (session != null)
            {
                try
                {
                    await session.InitializeAsync(context, cancellationToken).ConfigureAwait(false);
                    if (session is Session restoredSession)
                    {
                        await restoredSession
                            .LoadMirroredContinuationPointsAsync(entry.SessionId, cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
                catch
                {
                    session.Dispose();
                    throw;
                }
                if (m_logger.IsEnabled(LogLevel.Information))
                {
                    m_logger.DistributedSessionRestoredFromSharedStore(TokenDigest(authenticationToken), session.Id);
                }
                m_tokensBySession[session.Id] = authenticationToken;

                // Security-relevant provenance: a session materialized on this
                // replica from shared state (audit, not just a log).
                m_server.ReportAuditSessionRestoredEvent(
                    TokenDigest(authenticationToken), session, m_logger);
            }
            return session;
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
            NonceReplayed
        }

        /// <summary>
        /// Enforces the cross-channel security checks and consumes the mirrored
        /// <c>serverNonce</c> exactly once. Factored out so the security policy
        /// is unit-testable without constructing a live session.
        /// </summary>
        internal async ValueTask<RestoreDecision> AuthorizeAndConsumeAsync(
            SharedSessionEntry entry,
            string securityPolicyUri,
            MessageSecurityMode securityMode,
            ByteString clientChannelCertificate,
            CancellationToken cancellationToken = default)
        {
            if (IsRestoreExpired(entry))
            {
                return RestoreDecision.Expired;
            }

            if (entry.SecurityStateVersion != SharedSessionEntry.CurrentSecurityStateVersion ||
                string.IsNullOrEmpty(entry.ClientUserId))
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

            bool consumed = await m_nonceRegistry
                .TryConsumeAsync(entry.ServerNonce, cancellationToken)
                .ConfigureAwait(false);
            return consumed ? RestoreDecision.Authorized : RestoreDecision.NonceReplayed;
        }

        private ISession? ReconstructSession(
            SharedSessionEntry entry,
            NodeId authenticationToken,
            OperationContext context)
        {
            if (entry.SecurityStateVersion != SharedSessionEntry.CurrentSecurityStateVersion ||
                string.IsNullOrEmpty(entry.ClientUserId))
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
                context.ChannelContext!.ClientChannelCertificate.ToByteString())
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
                SetRestoredSessionSecurityState(
                    session,
                    entry.OriginalClientChannelCertificate,
                    entry.ClientUserId);

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

            return new SharedSessionEntry
            {
                SessionId = result.SessionId,
                AuthenticationToken = result.AuthenticationToken,
                SessionName = sessionName ?? string.Empty,
                CreatedAt = UtcNow(),
                LastActivatedAt = UtcNow(),
                ServerNonce = result.ServerNonce,
                ClientNonce = clientNonce,
                ClientCertificateChain = clientCertBlob,
                SecurityStateVersion = SharedSessionEntry.CurrentSecurityStateVersion,
                OriginalClientChannelCertificate =
                    context.ChannelContext!.ClientChannelCertificate.ToByteString(),
                SecurityPolicyUri = endpoint.SecurityPolicyUri ?? string.Empty,
                SecurityMode = (int)endpoint.SecurityMode,
                EndpointUrl = endpointUrl ?? string.Empty,
                SessionTimeout = result.RevisedSessionTimeout,
                ClientDescription = clientDescription ?? new ApplicationDescription()
            };
        }

        private async ValueTask MirrorActivationAsync(
            NodeId authenticationToken,
            ByteString serverNonce,
            string clientUserId,
            CancellationToken cancellationToken)
        {
            SharedSessionEntry? existing = await m_sessionStore
                .TryGetAsync(authenticationToken, cancellationToken)
                .ConfigureAwait(false);
            if (existing == null)
            {
                return;
            }

            SharedSessionEntry updated = existing with
            {
                ServerNonce = serverNonce,
                LastActivatedAt = UtcNow(),
                SecurityStateVersion = SharedSessionEntry.CurrentSecurityStateVersion,
                ClientUserId = clientUserId
            };
            await m_sessionStore.PutAsync(updated, cancellationToken).ConfigureAwait(false);
        }

        private bool IsRestoreExpired(SharedSessionEntry entry)
        {
            if (entry.LastActivatedAt.IsNull)
            {
                return false;
            }

            DateTime expiry = entry.LastActivatedAt
                .ToDateTime()
                .AddMilliseconds(Math.Max(entry.SessionTimeout, 0));
            return m_restoreTimeProvider.GetUtcNow().UtcDateTime >= expiry;
        }

        private static bool IsValidSessionNonce(ByteString nonce)
        {
            return !nonce.IsNull &&
                nonce.Length is >= 32 and <= 128 &&
                Nonce.ValidateNonce(nonce.ToArray(), MessageSecurityMode.Sign, 32);
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

        private readonly IServerInternal m_server;
        private readonly ISharedSessionStore m_sessionStore;
        private readonly ISingleUseNonceRegistry m_nonceRegistry;
        private readonly Func<string, Certificate?> m_serverCertificateProvider;
        private readonly DistributedSessionOptions m_options;
        private readonly ITelemetryContext m_telemetry;
        private readonly ILogger m_logger;
        private readonly TimeProvider m_restoreTimeProvider;
        private readonly ConcurrentDictionary<NodeId, NodeId> m_tokensBySession = new();
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
    }

}
