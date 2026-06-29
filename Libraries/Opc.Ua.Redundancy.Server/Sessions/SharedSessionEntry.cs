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

using Opc.Ua.Redundancy;
using Opc.Ua.Server;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// The session context shared across replicas so a standby can
    /// re-activate a session after a failover. The token is a lookup key
    /// only; admission still requires a full <c>ActivateSession</c> signature
    /// check against the (single-use) <see cref="ServerNonce"/> on the standby.
    /// </summary>
    /// <remarks>
    /// The whole entry is encrypted and integrity-protected at rest by the
    /// configured <see cref="IRecordProtector"/> before it reaches the shared
    /// store, so the secret-bearing fields (<see cref="ServerNonce"/>,
    /// <see cref="ClientNonce"/>) are never written in cleartext. Certificate
    /// stores are assumed to be shared independently; the shared
    /// <c>ApplicationInstanceCertificate</c> is supplied by the
    /// server, not this entry.
    /// </remarks>
    public sealed record SharedSessionEntry
    {
        /// <summary>
        /// The server-assigned session identifier.
        /// </summary>
        public NodeId SessionId { get; init; } = NodeId.Null;

        /// <summary>
        /// The authentication token used as the lookup key on reconnect.
        /// </summary>
        public NodeId AuthenticationToken { get; init; } = NodeId.Null;

        /// <summary>
        /// The session name.
        /// </summary>
        public string SessionName { get; init; } = string.Empty;

        /// <summary>
        /// When the session was created (UTC).
        /// </summary>
        public DateTimeUtc CreatedAt { get; init; }

        /// <summary>
        /// When the session was last activated (UTC).
        /// </summary>
        public DateTimeUtc LastActivatedAt { get; init; }

        /// <summary>
        /// The last <c>serverNonce</c> issued for the session — the value the
        /// client signs on its next <c>ActivateSession</c>. Single-use: a
        /// standby must invalidate it (via an
        /// <see cref="ISingleUseNonceRegistry"/>) when it consumes it on
        /// restore so a captured activation cannot be replayed.
        /// </summary>
        public ByteString ServerNonce { get; init; }

        /// <summary>
        /// The client nonce associated with the session.
        /// </summary>
        public ByteString ClientNonce { get; init; }

        /// <summary>
        /// The client certificate chain (leaf first, then issuers) as a single
        /// blob (see <see cref="Utils.CreateCertificateChainBlob"/>). Used to
        /// reconstruct the session and to enforce that a failover reconnect
        /// presents the same client certificate.
        /// </summary>
        public ByteString ClientCertificateChain { get; init; }

        /// <summary>
        /// The security policy URI in force for the session — needed to rebuild
        /// the typed <c>serverNonce</c> and to enforce that a failover reconnect
        /// uses the same SecurityPolicy.
        /// </summary>
        public string SecurityPolicyUri { get; init; } = string.Empty;

        /// <summary>
        /// The message security mode (cast of <see cref="MessageSecurityMode"/>)
        /// in force for the session.
        /// </summary>
        public int SecurityMode { get; init; }

        /// <summary>
        /// The endpoint URL the session was created against.
        /// </summary>
        public string EndpointUrl { get; init; } = string.Empty;

        /// <summary>
        /// The revised session timeout, in milliseconds.
        /// </summary>
        public double SessionTimeout { get; init; }

        /// <summary>
        /// The client application description supplied at session creation.
        /// </summary>
        public ApplicationDescription ClientDescription { get; init; } = new();

        /// <summary>
        /// Optional opaque, caller-encrypted secret material. May be a null
        /// <see cref="ByteString"/>.
        /// </summary>
        public ByteString SecretMaterial { get; init; }
    }
}
