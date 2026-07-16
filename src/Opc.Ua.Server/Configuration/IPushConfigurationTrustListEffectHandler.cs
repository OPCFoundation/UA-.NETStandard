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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Classifies how a committed TrustList change must be applied after
    /// the <c>ApplyChanges</c> response boundary (OPC 10000-12 §7.10.9).
    /// </summary>
    public enum TrustListEffectKind
    {
        /// <summary>
        /// The TrustList belongs to an application or HTTPS certificate group
        /// and validates the peer (client application) certificates that
        /// inbound SecureChannels present. Channels whose peer certificate is
        /// no longer trusted must be forced to renegotiate, but only on the
        /// transport listeners that validate against the matching
        /// <see cref="TrustListChangeEffect.ValidationScope"/>:
        /// <see cref="TrustListIdentifier.Peers"/>
        /// for the <c>opc.tcp</c> transports, whereas an
        /// <see cref="TrustListIdentifier.Https"/> change has no long-lived
        /// per-peer SecureChannel to cut on the request-scoped HTTPS
        /// transport.
        /// </summary>
        SecureChannelTrust,

        /// <summary>
        /// The TrustList belongs to the user-token certificate group and
        /// validates X.509 user identity tokens. Active certificate-based
        /// user identities must be re-validated and Sessions (plus their
        /// Subscriptions) that are no longer valid must be closed.
        /// </summary>
        UserIdentityTrust
    }

    /// <summary>
    /// Describes a single committed TrustList change and how it must be
    /// applied to running SecureChannels or Sessions per OPC 10000-12
    /// §7.10.9.
    /// </summary>
    public sealed class TrustListChangeEffect
    {
        /// <summary>
        /// The TrustList NodeId that was committed.
        /// </summary>
        public NodeId TrustListId { get; init; } = NodeId.Null;

        /// <summary>
        /// The certificate group NodeId that owns the TrustList.
        /// </summary>
        public NodeId CertificateGroupId { get; init; } = NodeId.Null;

        /// <summary>
        /// How the change must be applied (channel renegotiation vs. user
        /// identity re-validation).
        /// </summary>
        public TrustListEffectKind Kind { get; init; }

        /// <summary>
        /// The trust-list scope re-validation must run against
        /// (<see cref="TrustListIdentifier.Peers"/>,
        /// <see cref="TrustListIdentifier.Https"/>, or
        /// <see cref="TrustListIdentifier.Users"/>).
        /// </summary>
        public TrustListIdentifier ValidationScope { get; init; } = TrustListIdentifier.Peers;
    }

    /// <summary>
    /// The collaborators and committed effects an
    /// <see cref="IPushConfigurationTrustListEffectHandler"/> needs to apply
    /// the post-<c>ApplyChanges</c> TrustList effects of OPC 10000-12
    /// §7.10.9.
    /// </summary>
    /// <remarks>
    /// The context is built by <see cref="ConfigurationNodeManager"/> from
    /// the running server after the <c>ApplyChanges</c> response has been
    /// flushed, so the handler stays free of any direct server dependency
    /// and remains trivially injectable and testable.
    /// </remarks>
    public sealed class PushConfigurationTrustListEffectContext
    {
        /// <summary>
        /// The committed TrustList changes to apply. Never <see langword="null"/>.
        /// </summary>
        public required IReadOnlyList<TrustListChangeEffect> Effects { get; init; }

        /// <summary>
        /// The transport listeners currently bound to the server. A listener
        /// that implements <see cref="ITransportListenerPeerCertificateRotation"/>
        /// participates in SecureChannel renegotiation, but only for the
        /// committed effects whose scope matches its
        /// <see cref="ITransportListenerPeerCertificateRotation.PeerCertificateTrustListScope"/>.
        /// Never <see langword="null"/>.
        /// </summary>
        public required IReadOnlyList<ITransportListener> TransportListeners { get; init; }

        /// <summary>
        /// The manager used to enumerate active Sessions for user-identity
        /// re-validation, or <see langword="null"/> when no session manager
        /// is available (user-identity effects are then skipped).
        /// </summary>
        public ISessionManager? SessionManager { get; init; }

        /// <summary>
        /// The validator used to re-validate peer and user certificates
        /// against the updated TrustLists, or <see langword="null"/> when no
        /// validator is available (all effects are then skipped).
        /// </summary>
        public ICertificateValidatorEx? CertificateValidator { get; init; }

        /// <summary>
        /// Closes the Session identified by the supplied NodeId, deleting
        /// its Subscriptions when the second argument is
        /// <see langword="true"/>. Invoked with
        /// <c>deleteSubscriptions: true</c> for certificate-based user
        /// identities that no longer validate against the updated user
        /// TrustList (§7.10.9). Never <see langword="null"/>.
        /// </summary>
        public required Func<NodeId, bool, CancellationToken, ValueTask> CloseSessionAsync { get; init; }
    }

    /// <summary>
    /// Applies the post-<c>ApplyChanges</c> effects that a committed
    /// TrustList change has on running SecureChannels and Sessions, per OPC
    /// 10000-12 §7.10.9.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="ConfigurationNodeManager"/> creates a private
    /// <see cref="PushConfigurationTrustListEffectHandler"/> by default;
    /// hosts that need to observe or customize the effect behavior can
    /// supply their own via dependency injection or direct construction.
    /// </para>
    /// <para>
    /// Implementations must use asynchronous APIs only and must never hold a
    /// synchronous lock across an <see langword="await"/>.
    /// </para>
    /// </remarks>
    public interface IPushConfigurationTrustListEffectHandler
    {
        /// <summary>
        /// Applies the committed TrustList effects: forces the SecureChannels
        /// affected by an application-group trust change to renegotiate — on
        /// each transport listener whose peer-certificate scope matches the
        /// change — and closes the Sessions (plus Subscriptions) whose
        /// certificate user identity no longer validates against a committed
        /// user-group TrustList. Unaffected channels and Sessions are left
        /// untouched.
        /// </summary>
        /// <param name="context">
        /// The committed effects and the collaborators required to apply
        /// them.
        /// </param>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask ApplyAsync(
            PushConfigurationTrustListEffectContext context,
            CancellationToken cancellationToken = default);
    }
}
