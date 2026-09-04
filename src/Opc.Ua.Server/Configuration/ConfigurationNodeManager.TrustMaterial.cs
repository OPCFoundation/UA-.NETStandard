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
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Trust-material change propagation of <see cref="ConfigurationNodeManager"/>: building
    /// <see cref="TrustListChangeEffect"/>s for affected TrustLists, notifying
    /// <c>m_trustListEffectHandler</c>, and the certificate-change pump that re-enforces trust scopes
    /// on store changes. Code in this file owns <c>m_trustMaterialPump</c>, <c>m_trustMaterialPumpTask</c>
    /// and <c>m_selfTrustNotification</c>; it never mutates certificate slots or transaction state.
    /// </summary>
    public partial class ConfigurationNodeManager
    {
        /// <summary>
        /// Maps the TrustList NodeIds committed by a transaction to the
        /// §7.10.9 post-<c>ApplyChanges</c> effect that must be applied to
        /// running SecureChannels or Sessions, using the certificate group
        /// each TrustList belongs to. TrustLists that do not map to a known
        /// server certificate group are ignored.
        /// </summary>
        internal List<TrustListChangeEffect> BuildTrustListEffects(ArrayOf<NodeId> affectedTrustLists)
        {
            var effects = new List<TrustListChangeEffect>();
            if (affectedTrustLists.Count == 0)
            {
                return effects;
            }

            foreach (NodeId trustListId in affectedTrustLists)
            {
                if (trustListId.IsNull)
                {
                    continue;
                }

                foreach (ServerCertificateGroup certGroup in m_certificateGroups)
                {
                    if (certGroup.Node?.TrustList == null ||
                        !Utils.IsEqual(certGroup.Node.TrustList.NodeId, trustListId))
                    {
                        continue;
                    }

                    effects.Add(CreateTrustListEffect(certGroup));
                    break;
                }
            }

            return effects;
        }

        /// <summary>
        /// Maps trust-list scopes to the §7.10.9 effects on the server's
        /// certificate groups, mirroring <see cref="BuildTrustListEffects"/>
        /// (which maps committed TrustList NodeIds instead of scopes).
        /// </summary>
        internal List<TrustListChangeEffect> BuildTrustListEffectsForScopes(
            IEnumerable<TrustListIdentifier> scopes)
        {
            // A null scope in the input matches no group and simply drops out.
            var requested = new HashSet<TrustListIdentifier>(
                scopes.Where(static scope => scope != null));

            var effects = new List<TrustListChangeEffect>();
            foreach (ServerCertificateGroup certGroup in m_certificateGroups)
            {
                if (requested.Contains(GetGroupValidationScope(certGroup)))
                {
                    effects.Add(CreateTrustListEffect(certGroup));
                }
            }

            return effects;
        }

        /// <summary>
        /// The single source of truth for the certificate-group →
        /// trust-list-scope mapping used by both effect builders: the
        /// user-token group validates X.509 user identities
        /// (<see cref="TrustListIdentifier.Users"/>), the HTTPS group the
        /// HTTPS transport certificates
        /// (<see cref="TrustListIdentifier.Https"/>), every other group the
        /// peer application certificates
        /// (<see cref="TrustListIdentifier.Peers"/>).
        /// </summary>
        private static TrustListIdentifier GetGroupValidationScope(
            ServerCertificateGroup certGroup)
        {
            if (certGroup.BrowseName == BrowseNames.DefaultUserTokenGroup)
            {
                return TrustListIdentifier.Users;
            }

            return certGroup.BrowseName == BrowseNames.DefaultHttpsGroup
                ? TrustListIdentifier.Https
                : TrustListIdentifier.Peers;
        }

        /// <summary>
        /// Builds the §7.10.9 effect for a certificate group from the shared
        /// scope mapping.
        /// </summary>
        private static TrustListChangeEffect CreateTrustListEffect(
            ServerCertificateGroup certGroup)
        {
            TrustListIdentifier scope = GetGroupValidationScope(certGroup);
            return new TrustListChangeEffect
            {
                TrustListId = certGroup.Node?.TrustList?.NodeId ?? NodeId.Null,
                CertificateGroupId = certGroup.NodeId,
                Kind = scope == TrustListIdentifier.Users
                    ? TrustListEffectKind.UserIdentityTrust
                    : TrustListEffectKind.SecureChannelTrust,
                ValidationScope = scope
            };
        }

        /// <summary>
        /// Notifies the certificate manager that the stores behind the
        /// committed TrustList scopes changed, deduplicating scopes so each
        /// is notified once. Failures are logged and never block the
        /// subsequent channel/session effects.
        /// </summary>
        private void NotifyTrustMaterialChanged(List<TrustListChangeEffect> trustListEffects)
        {
            ICertificateManager? certificateManager = m_configuration.CertificateManager;
            if (certificateManager == null)
            {
                return;
            }

            // Observers are invoked synchronously on the notifying thread, so
            // the thread-local flag marks exactly the events this call raises
            // about its own committed changes - a concurrent out-of-band
            // notification raised on another thread is never suppressed. The
            // enforcement pump skips the self-raised events: the deferred
            // apply below already runs the precise committed-scope §7.10.9
            // sweep, and a second pump-driven sweep over the same scopes
            // would be redundant work.
            m_selfTrustNotification.Value = true;
            try
            {
                var notifiedScopes = new List<TrustListIdentifier>();
                foreach (TrustListChangeEffect effect in trustListEffects)
                {
                    TrustListIdentifier scope = effect.ValidationScope;
                    if (scope == null || notifiedScopes.Contains(scope))
                    {
                        continue;
                    }

                    notifiedScopes.Add(scope);
                    try
                    {
                        certificateManager.NotifyTrustListChanged(
                            scope,
                            trustChanged: true,
                            crlChanged: true);
                    }
                    catch (Exception ex)
                    {
                        m_logger.TrustListChangeNotificationFailed(ex, scope.ToString());
                    }
                }
            }
            finally
            {
                m_selfTrustNotification.Value = false;
            }
        }

        /// <summary>
        /// Builds the effect context from the running server and applies the
        /// committed §7.10.9 TrustList effects through the injected
        /// <see cref="IPushConfigurationTrustListEffectHandler"/>. Shared by
        /// the <c>ApplyChanges</c> deferred apply and the origin-independent
        /// trust-material enforcement pump.
        /// </summary>
        private ValueTask ApplyTrustListEffectsAsync(
            List<TrustListChangeEffect> trustListEffects,
            IReadOnlyList<ITransportListener> listeners,
            CancellationToken cancellationToken = default)
        {
            var context = new PushConfigurationTrustListEffectContext
            {
                Effects = trustListEffects,
                TransportListeners = listeners,
                SessionManager = Server.SessionManager,
                CertificateValidator = m_configuration.CertificateManager,
                // A server-initiated close carries no client OperationContext,
                // matching the SessionManager's own timeout-driven close path.
                CloseSessionAsync = (sessionId, deleteSubscriptions, ct) =>
                    Server.CloseSessionAsync(null!, sessionId, deleteSubscriptions, ct)
            };

            return m_trustListEffectHandler.ApplyAsync(context, cancellationToken);
        }

        /// <summary>
        /// Subscribes the trust-material enforcement pump to the certificate
        /// manager's change stream. Every
        /// <see cref="CertificateChangeKind.TrustListUpdated"/> or
        /// <see cref="CertificateChangeKind.CrlUpdated"/> for a well-known
        /// server scope — whatever wrote the stores — is coalesced into one
        /// §7.10.9 effect fan-out over the affected scopes. Sweeping is
        /// idempotent with the <c>ApplyChanges</c>-driven fan-out: a channel
        /// already faulted by one sweep is skipped by the other.
        /// </summary>
        private void SubscribeTrustMaterialEnforcement()
        {
            ICertificateManager? certificateManager = m_configuration.CertificateManager;
            if (certificateManager == null)
            {
                return;
            }

            m_trustMaterialPump ??= new CertificateChangePump<HashSet<TrustListIdentifier>>(
                evt => (evt.Kind
                    is CertificateChangeKind.TrustListUpdated
                    or CertificateChangeKind.CrlUpdated) &&
                    // Skip the notifications this manager raises about its
                    // own ApplyChanges commits (dispatched synchronously on
                    // the raising thread): the deferred apply already runs
                    // the precise committed-scope sweep.
                    !m_selfTrustNotification.Value,
                static (scopes, evt) =>
                {
                    // Union fold: a Users change must never be dropped
                    // because a Peers change arrived later in the burst.
                    scopes ??= [];
                    scopes.Add(evt.TrustList);
                    return scopes;
                },
                EnforceTrustMaterialScopesAsync,
                ex => m_logger.TrustMaterialEnforcementFailed(ex),
                onPumpStateChanged: task =>
                {
                    // Track the drain task so DeleteAddressSpaceAsync can
                    // await an in-flight enforcement pass during shutdown.
                    lock (m_pendingApplyChangesLock)
                    {
                        m_trustMaterialPumpTask = task ?? Task.CompletedTask;
                    }
                });

            m_trustMaterialPump.Subscribe(certificateManager.CertificateChanges);
        }

        /// <summary>
        /// The trust-material enforcement pump's processing pass: maps the
        /// coalesced scopes to §7.10.9 effects on the server's certificate
        /// groups and applies them to the running channels/Sessions. Scopes
        /// that do not correspond to a server certificate group (e.g.
        /// <see cref="TrustListIdentifier.Rejected"/> or custom trust lists)
        /// map to no effect and are ignored.
        /// </summary>
        private async ValueTask EnforceTrustMaterialScopesAsync(
            HashSet<TrustListIdentifier> scopes,
            CancellationToken cancellationToken)
        {
            CancellationToken shutdownToken;
            try
            {
                shutdownToken = m_shutdownCts.Token;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            if (shutdownToken.IsCancellationRequested)
            {
                // Server shutting down: never run effects against listeners
                // and managers that are being torn down.
                return;
            }

            List<TrustListChangeEffect> effects = BuildTrustListEffectsForScopes(scopes);
            if (effects.Count == 0)
            {
                return;
            }

            IReadOnlyList<ITransportListener> listeners
                = (Server as ITransportListenerRegistryProvider)?.TransportListeners
                    ?? [];

            // Propagate the shutdown token: a pass overlapping shutdown must
            // stop instead of sweeping listeners/sessions being torn down.
            await ApplyTrustListEffectsAsync(effects, listeners, shutdownToken)
                .ConfigureAwait(false);
        }
    }
}
