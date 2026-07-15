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

namespace Opc.Ua
{
    /// <summary>
    /// Optional capability interface for <see cref="ITransportListener"/>
    /// implementations that support targeted force-renegotiation of
    /// SecureChannels after a peer (client) certificate trust decision has
    /// changed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// OPC UA Part 12 §7.10.9 (ApplyChanges) requires that once the
    /// <c>ApplyChanges</c> method response has been delivered, the server
    /// <i>shall</i> force existing SecureChannels affected by a committed
    /// TrustList change to renegotiate. Removing a trusted client
    /// application certificate (or one of its issuers) from the
    /// application-group TrustList invalidates the trust decision that a
    /// currently-connected peer's SecureChannel was established on; that
    /// channel must be cut so the peer re-negotiates and is re-validated
    /// against the updated TrustList.
    /// </para>
    /// <para>
    /// This capability complements
    /// <see cref="ITransportListenerCertificateRotation"/>, which cuts
    /// channels affected by a change to the <i>server's own</i> application
    /// certificate. Here the deciding factor is the <i>peer's</i>
    /// certificate: only channels whose negotiated client certificate is no
    /// longer trusted are cut. Channels without a client certificate (for
    /// example <see cref="SecurityPolicies.None"/> channels) and channels
    /// whose client certificate is still trusted are left untouched, so
    /// unaffected peers keep their Sessions and Subscriptions. The listener
    /// socket remains bound so peers can immediately reconnect.
    /// </para>
    /// <para>
    /// A listener validates its peer certificates against exactly one
    /// TrustList scope, advertised through
    /// <see cref="PeerCertificateTrustListScope"/>. A committed TrustList
    /// change is routed to a listener <i>only</i> when the change targets
    /// that same scope, so a listener is never re-validated or closed against
    /// an unrelated store. The <c>opc.tcp</c> transports validate the client
    /// <i>application</i> certificate presented during
    /// <c>OpenSecureChannel</c> against
    /// <see cref="TrustListIdentifier.Peers"/> and therefore implement this
    /// capability with that scope.
    /// </para>
    /// <para>
    /// The UA-HTTPS transport deliberately does <i>not</i> implement this
    /// capability. Its binding is request-scoped over HTTP, so there is no
    /// long-lived per-peer SecureChannel keyed by the client application
    /// certificate to cut; any client TLS certificate (mutual TLS) is
    /// validated at the TLS handshake by the shared certificate validator,
    /// which reads the directory-backed trust stores directly, so subsequent
    /// handshakes automatically honour the updated
    /// <see cref="TrustListIdentifier.Https"/> TrustList. An
    /// <see cref="TrustListIdentifier.Https"/>-group TrustList change
    /// therefore forces no channel renegotiation rather than a meaningless
    /// teardown.
    /// </para>
    /// </remarks>
    public interface ITransportListenerPeerCertificateRotation
    {
        /// <summary>
        /// The single TrustList scope this listener validates its channels'
        /// negotiated peer (client) certificates against. A committed
        /// TrustList change forces this listener's channels to renegotiate
        /// only when the change targets this scope; changes to any other
        /// scope leave the listener's channels untouched. The <c>opc.tcp</c>
        /// transports return <see cref="TrustListIdentifier.Peers"/>.
        /// </summary>
        TrustListIdentifier PeerCertificateTrustListScope { get; }

        /// <summary>
        /// Forces SecureChannels whose negotiated client (peer) certificate
        /// is no longer trusted to renegotiate. Each channel's client
        /// certificate is passed to <paramref name="isPeerTrustedAsync"/>,
        /// which validates it against this listener's
        /// <see cref="PeerCertificateTrustListScope"/>; the channel is cut
        /// when the callback returns <see langword="false"/>. Channels
        /// without a client certificate, channels whose certificate is still
        /// trusted, and channels for which the callback throws are left
        /// untouched.
        /// </summary>
        /// <param name="isPeerTrustedAsync">
        /// A callback that re-validates a channel's client certificate
        /// against the updated TrustList for this listener's
        /// <see cref="PeerCertificateTrustListScope"/> and returns
        /// <see langword="true"/> when the certificate is still trusted
        /// (leave the channel open), or <see langword="false"/> when it is no
        /// longer trusted (cut the channel). Must not be
        /// <see langword="null"/>.
        /// </param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// The global channel ids of the SecureChannels that were closed by
        /// this call, useful for diagnostics and tests. Returns an empty
        /// list when no channels matched.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="isPeerTrustedAsync"/> is
        /// <see langword="null"/>.
        /// </exception>
        ValueTask<IReadOnlyList<string>> CloseChannelsForUntrustedPeersAsync(
            Func<Certificate, CancellationToken, ValueTask<bool>> isPeerTrustedAsync,
            CancellationToken ct = default);
    }
}
