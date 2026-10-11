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
        /// reconstruct the application certificate retained by the Session.
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
        /// Version of the security state appended to the persisted entry.
        /// </summary>
        public uint SecurityStateVersion { get; init; }

        /// <summary>
        /// The client certificate used to establish the original SecureChannel.
        /// </summary>
        public ByteString OriginalClientChannelCertificate { get; init; }

        /// <summary>
        /// The identity continuity key of the activated Session owner, used to
        /// verify that a restored Session keeps the same ClientUserId
        /// (OPC 10000-4 5.7.3.1). This is <c>null</c> for an anonymous identity.
        /// </summary>
        /// <remarks>
        /// This is the key produced by the Server's ClientUserId resolver, not the
        /// human readable ClientUserId reported through SessionSecurityDiagnostics.
        /// </remarks>
        public string? ClientUserId { get; init; }

        /// <summary>
        /// The UserIdentityToken type used to derive <see cref="ClientUserId"/>.
        /// </summary>
        public UserTokenType ClientUserTokenType { get; init; }

        /// <summary>
        /// Whether the Session has completed activation and the ClientUserId state is valid.
        /// </summary>
        public bool HasActivatedUserIdentity { get; init; }

        /// <summary>
        /// Whether the client application certificate passed the server's validation
        /// when the Session was created. <c>false</c> when an
        /// <c>OnApplicationCertificateError</c> override accepted a validation error:
        /// the restored Session may still sign with the certificate, but it is not a
        /// trusted application identity for role assignment (OPC 10000-3 4.9,
        /// OPC 10000-18 4.4.4), exactly like the original Session.
        /// </summary>
        /// <remarks>
        /// An entry written before this state was mirrored (security state version 3)
        /// is decoded with <c>false</c>: the provenance of its certificate is unknown, so
        /// the restored Session fails closed and is granted no application-based roles.
        /// </remarks>
        public bool ClientCertificateValidated { get; init; }

        /// <summary>
        /// When the replica that owns the Session last mirrored client activity on it
        /// (UTC). A Session is kept alive by every Service request, not only by
        /// activations (OPC 10000-4 5.7.2.1), so the owner refreshes this liveness
        /// heartbeat while the client uses the Session; a mirrored Session expires
        /// <see cref="SessionTimeout"/> after the later of this and
        /// <see cref="LastActivatedAt"/>.
        /// </summary>
        public DateTimeUtc LastContactAt { get; init; }

        /// <summary>
        /// The identity of the replica that currently serves the Session. Only the owner
        /// mirrors the Session or deletes its entry; a restore on another replica takes
        /// ownership with a conditional write, so a stale copy left on the previous
        /// replica can neither overwrite nor delete the entry of the live Session.
        /// <c>null</c> for an entry written before ownership was mirrored.
        /// </summary>
        public string? OwnerId { get; init; }

        /// <summary>
        /// The DER encoded <c>ApplicationInstanceCertificate</c> of the server that
        /// created the Session, returned to the client in <c>CreateSession</c>. The
        /// client signature may cover it instead of the restoring replica's own
        /// certificate, because the replicas of a non-transparent redundant server set
        /// have their own ApplicationUri and certificate (OPC 10000-4 6.6.2.4.1).
        /// </summary>
        public ByteString ServerCertificate { get; init; }

        /// <summary>
        /// Whether the user identity token of the Session is encrypted with an
        /// EphemeralKey (the EphemeralKeyType of ECC and RSA-DH user token policies). The
        /// private part of that key never leaves the replica that issued it, so such a
        /// Session cannot be restored on another replica; the client re-creates it.
        /// </summary>
        public bool UserTokenRequiresEphemeralKey { get; init; }

        /// <summary>
        /// Optional opaque, caller-encrypted secret material. May be a null
        /// <see cref="ByteString"/>.
        /// </summary>
        public ByteString SecretMaterial { get; init; }

        /// <summary>
        /// The current persisted Session security state version.
        /// </summary>
        /// <remarks>
        /// Version 3 stores the identity continuity key in <see cref="ClientUserId"/>.
        /// Entries written by an earlier version carry a differently derived value,
        /// so they are treated as missing security state and fail closed rather
        /// than being compared against a key computed by this version. Version 4 adds
        /// <see cref="ClientCertificateValidated"/>; a version 3 entry is still
        /// restorable, with the certificate treated as not validated. Version 5 adds
        /// <see cref="LastContactAt"/>, <see cref="OwnerId"/>,
        /// <see cref="ServerCertificate"/> and <see cref="UserTokenRequiresEphemeralKey"/>;
        /// version 3 and 4 entries are still restorable, without an owner.
        /// </remarks>
        public const uint CurrentSecurityStateVersion = 5;

        /// <summary>
        /// The oldest persisted Session security state version that can be restored.
        /// </summary>
        internal const uint MinimumRestorableSecurityStateVersion = 3;

        /// <summary>
        /// The time from which <see cref="SessionTimeout"/> runs: the later of the last
        /// activation and the last mirrored client contact, or <c>null</c> when neither
        /// was recorded.
        /// </summary>
        internal DateTime? GetLastActivityUtc()
        {
            DateTime? activated = LastActivatedAt.IsNull ? null : LastActivatedAt.ToDateTime();
            DateTime? contact = LastContactAt.IsNull ? null : LastContactAt.ToDateTime();
            if (activated == null)
            {
                return contact;
            }
            if (contact == null)
            {
                return activated;
            }
            return contact.Value > activated.Value ? contact : activated;
        }

        /// <summary>
        /// Whether the mirrored Session has timed out at <paramref name="utcNow"/>.
        /// An entry without any recorded activity never expires by itself.
        /// </summary>
        internal bool IsExpired(DateTime utcNow)
        {
            DateTime? lastActivity = GetLastActivityUtc();
            return lastActivity != null &&
                utcNow >= lastActivity.Value.AddMilliseconds(Math.Max(SessionTimeout, 0));
        }
    }
}
