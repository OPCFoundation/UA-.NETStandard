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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Sealed default implementation of
    /// <see cref="IPushConfigurationTrustListEffectHandler"/> that applies
    /// the post-<c>ApplyChanges</c> TrustList effects required by OPC
    /// 10000-12 §7.10.9.
    /// </summary>
    /// <remarks>
    /// The handler is stateless and free of any direct server dependency:
    /// every collaborator it needs (transport listeners, session manager,
    /// certificate validator, session-close delegate) is supplied through
    /// the <see cref="PushConfigurationTrustListEffectContext"/> built by
    /// <see cref="ConfigurationNodeManager"/>. It can therefore be a shared
    /// singleton and is trivially injectable and testable.
    /// </remarks>
    public sealed class PushConfigurationTrustListEffectHandler : IPushConfigurationTrustListEffectHandler
    {
        /// <summary>
        /// Initializes a new handler.
        /// </summary>
        /// <param name="telemetry">Telemetry context used to create a logger.</param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="telemetry"/> is <see langword="null"/>.
        /// </exception>
        public PushConfigurationTrustListEffectHandler(ITelemetryContext telemetry)
        {
            if (telemetry == null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }

            m_logger = telemetry.CreateLogger<PushConfigurationTrustListEffectHandler>();
        }

        /// <inheritdoc/>
        public async ValueTask ApplyAsync(
            PushConfigurationTrustListEffectContext context,
            CancellationToken cancellationToken = default)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            ICertificateValidatorEx? validator = context.CertificateValidator;
            if (validator == null || context.Effects.Count == 0)
            {
                // Without a validator no committed trust decision can be
                // re-evaluated, so no channel/session is affected.
                return;
            }

            // Split the committed effects into the distinct channel-trust
            // scopes (application/HTTPS groups) and whether any user-token
            // group changed. Only the affected scopes are re-validated, so
            // unaffected channels and Sessions are never disturbed.
            var channelScopes = new List<TrustListIdentifier>();
            bool hasUserIdentityEffect = false;
            foreach (TrustListChangeEffect effect in context.Effects)
            {
                if (effect.Kind == TrustListEffectKind.SecureChannelTrust)
                {
                    if (!channelScopes.Contains(effect.ValidationScope))
                    {
                        channelScopes.Add(effect.ValidationScope);
                    }
                }
                else if (effect.Kind == TrustListEffectKind.UserIdentityTrust)
                {
                    hasUserIdentityEffect = true;
                }
            }

            if (channelScopes.Count > 0)
            {
                await ForceUntrustedPeerChannelsToRenegotiateAsync(
                        context,
                        validator,
                        channelScopes,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            if (hasUserIdentityEffect)
            {
                await CloseSessionsWithUntrustedUserCertificatesAsync(
                        context,
                        validator,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Forces the SecureChannels whose negotiated peer certificate no
        /// longer validates against an affected TrustList to renegotiate,
        /// routing each committed channel-trust scope only to the listeners
        /// that validate their peer certificates against that same scope.
        /// Channels whose peer certificate still validates (or that have
        /// none), and listeners whose scope did not change, are left open.
        /// </summary>
        private async ValueTask ForceUntrustedPeerChannelsToRenegotiateAsync(
            PushConfigurationTrustListEffectContext context,
            ICertificateValidatorEx validator,
            IReadOnlyList<TrustListIdentifier> channelScopes,
            CancellationToken cancellationToken)
        {
            int totalCut = 0;
            foreach (ITransportListener listener in context.TransportListeners)
            {
                if (listener is not ITransportListenerPeerCertificateRotation rotator)
                {
                    continue;
                }

                // A listener validates its peer certificates against exactly
                // one TrustList scope. Route a committed change to it only
                // when the change targets that scope, so an opc.tcp listener
                // (scope Peers) is never re-validated or closed against the
                // HTTPS store and an HTTPS listener never against the Peers
                // store. A listener whose scope did not change is skipped
                // entirely, leaving its channels untouched.
                TrustListIdentifier listenerScope = rotator.PeerCertificateTrustListScope;
                if (listenerScope == null || !channelScopes.Contains(listenerScope))
                {
                    continue;
                }

                try
                {
                    IReadOnlyList<string> closed = await rotator
                        .CloseChannelsForUntrustedPeersAsync(
                            (peerCertificate, ct) =>
                                IsPeerTrustedInScopeAsync(validator, listenerScope, peerCertificate, ct),
                            cancellationToken)
                        .ConfigureAwait(false);
                    totalCut += closed.Count;
                }
                catch (Exception ex)
                {
                    m_logger.LogWarning(
                        ex,
                        "Listener {Listener} failed to renegotiate channels after a peer-trust change.",
                        listener.ListenerId);
                }
            }

            m_logger.LogInformation(
                Utils.TraceMasks.Security,
                "TrustList change forced {Count} SecureChannel(s) with untrusted peer certificates to renegotiate.",
                totalCut);
        }

        /// <summary>
        /// Re-validates a single peer certificate against one TrustList scope
        /// and reports whether it is still trusted.
        /// </summary>
        private static async ValueTask<bool> IsPeerTrustedInScopeAsync(
            ICertificateValidatorEx validator,
            TrustListIdentifier scope,
            Certificate peerCertificate,
            CancellationToken ct)
        {
            CertificateValidationResult result = await validator
                .ValidateAsync(peerCertificate, scope, ct)
                .ConfigureAwait(false);
            return result.IsValid;
        }

        /// <summary>
        /// Re-validates every active certificate-based user identity against
        /// the updated user TrustList and closes the Sessions (plus their
        /// Subscriptions) whose user certificate is no longer trusted.
        /// Sessions with a non-certificate identity are left untouched.
        /// </summary>
        private async ValueTask CloseSessionsWithUntrustedUserCertificatesAsync(
            PushConfigurationTrustListEffectContext context,
            ICertificateValidatorEx validator,
            CancellationToken cancellationToken)
        {
            ISessionManager? sessionManager = context.SessionManager;
            if (sessionManager == null)
            {
                return;
            }

            IList<ISession> sessions;
            try
            {
                sessions = sessionManager.GetSessions();
            }
            catch (Exception ex)
            {
                m_logger.LogWarning(ex, "Failed to enumerate Sessions for user-identity re-validation.");
                return;
            }

            if (sessions == null || sessions.Count == 0)
            {
                return;
            }

            int closedCount = 0;
            foreach (ISession session in sessions)
            {
                if (session == null)
                {
                    continue;
                }

                using Certificate? userCertificate = TryGetUserCertificate(session);
                if (userCertificate == null)
                {
                    // Not a certificate-based user identity — unaffected by a
                    // user TrustList change.
                    continue;
                }

                bool valid;
                try
                {
                    CertificateValidationResult result = await validator
                        .ValidateAsync(userCertificate, TrustListIdentifier.Users, cancellationToken)
                        .ConfigureAwait(false);
                    valid = result.IsValid;
                }
                catch (Exception ex)
                {
                    // Best-effort: never close a Session whose identity cannot
                    // be re-validated.
                    m_logger.LogWarning(
                        ex,
                        "Failed to re-validate the user certificate for Session {SessionId}; leaving it open.",
                        session.Id);
                    continue;
                }

                if (valid)
                {
                    continue;
                }

                try
                {
                    // deleteSubscriptions: true — §7.10.9 requires the invalid
                    // Session's Subscriptions are discarded together with it.
                    await context.CloseSessionAsync(session.Id, true, cancellationToken)
                        .ConfigureAwait(false);
                    closedCount++;
                }
                catch (Exception ex)
                {
                    m_logger.LogWarning(
                        ex,
                        "Failed to close Session {SessionId} whose user certificate is no longer trusted.",
                        session.Id);
                }
            }

            m_logger.LogInformation(
                Utils.TraceMasks.Security,
                "TrustList change closed {Count} Session(s) with untrusted certificate user identities.",
                closedCount);
        }

        /// <summary>
        /// Materialises the public-key user certificate from a Session's
        /// stored X.509 identity token, or <see langword="null"/> when the
        /// Session did not authenticate with a certificate user identity.
        /// The returned reference is owned by the caller.
        /// </summary>
        private static Certificate? TryGetUserCertificate(ISession session)
        {
            IUserIdentityTokenHandler? handler = session.IdentityToken;
            if (handler == null || handler.TokenType != UserTokenType.Certificate)
            {
                return null;
            }

            if (handler.Token is not X509IdentityToken x509Token)
            {
                return null;
            }

            ByteString certificateData = x509Token.CertificateData;
            if (certificateData.IsEmpty)
            {
                return null;
            }

            return Certificate.FromRawData(certificateData);
        }

        private readonly ILogger m_logger;
    }
}
