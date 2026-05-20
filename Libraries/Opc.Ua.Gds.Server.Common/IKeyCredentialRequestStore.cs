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
using System.Collections.Concurrent;
using System.Threading;

namespace Opc.Ua.Gds.Server
{
    /// <summary>
    /// Represents the state of a key-credential request.
    /// </summary>
    public enum KeyCredentialRequestState
    {
        /// <summary>Request is new / pending approval.</summary>
        New,

        /// <summary>Request has been approved.</summary>
        Approved,

        /// <summary>Request has been rejected.</summary>
        Rejected,

        /// <summary>Request has been completed.</summary>
        Completed
    }

    /// <summary>
    /// Represents a single key-credential request record.
    /// </summary>
    public sealed class KeyCredentialRequestRecord
    {
        /// <summary>The unique request identifier.</summary>
        public NodeId RequestId { get; set; }

        /// <summary>The ApplicationUri that requested the credential.</summary>
        public string? ApplicationUri { get; set; }

        /// <summary>The public key supplied by the requester.</summary>
        public ByteString PublicKey { get; set; }

        /// <summary>The security policy URI for the credential.</summary>
        public string? SecurityPolicyUri { get; set; }

        /// <summary>The roles requested by the caller.</summary>
        public ArrayOf<NodeId> RequestedRoles { get; set; }

        /// <summary>Current state of the request.</summary>
        public KeyCredentialRequestState State { get; set; }

        /// <summary>The credential identifier (populated after approval).</summary>
        public string? CredentialId { get; set; }

        /// <summary>The credential secret (populated after approval).</summary>
        public ByteString CredentialSecret { get; set; }

        /// <summary>Thumbprint of the certificate that protects the credential.</summary>
        public string? CertificateThumbprint { get; set; }

        /// <summary>The security policy URI actually assigned.</summary>
        public string? GrantedSecurityPolicyUri { get; set; }

        /// <summary>The roles actually granted.</summary>
        public ArrayOf<NodeId> GrantedRoles { get; set; }

        /// <summary>When the request was created.</summary>
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>
    /// An abstract interface to the key-credential request store.
    /// Mirrors <see cref="ICertificateRequest"/> but for OPC 10000-12
    /// §8 KeyCredentialService requests.
    /// </summary>
    public interface IKeyCredentialRequestStore
    {
        /// <summary>
        /// Start a new key-credential request.
        /// </summary>
        NodeId StartRequest(
            string applicationUri,
            ByteString publicKey,
            string? securityPolicyUri,
            ArrayOf<NodeId> requestedRoles);

        /// <summary>
        /// Finish (approve or cancel) a key-credential request.
        /// </summary>
        /// <param name="requestId">The request to finish.</param>
        /// <param name="cancelRequest">
        /// When <c>true</c> the request is cancelled; when <c>false</c>
        /// the credential is issued.
        /// </param>
        /// <param name="credentialId">The issued credential id.</param>
        /// <param name="credentialSecret">The issued secret.</param>
        /// <param name="certificateThumbprint">Thumbprint of the protecting cert.</param>
        /// <param name="securityPolicyUri">The assigned security policy.</param>
        /// <param name="grantedRoles">The roles granted.</param>
        /// <returns>The state of the request.</returns>
        KeyCredentialRequestState FinishRequest(
            NodeId requestId,
            bool cancelRequest,
            out string? credentialId,
            out ByteString credentialSecret,
            out string? certificateThumbprint,
            out string? securityPolicyUri,
            out ArrayOf<NodeId> grantedRoles);

        /// <summary>
        /// Revoke a previously issued credential.
        /// </summary>
        /// <param name="credentialId">The credential to revoke.</param>
        void Revoke(string credentialId);
    }

    /// <summary>
    /// In-memory implementation of <see cref="IKeyCredentialRequestStore"/>.
    /// Suitable for testing and single-process GDS deployments.
    /// </summary>
    public sealed class InMemoryKeyCredentialRequestStore : IKeyCredentialRequestStore
    {
        private int m_nextId;
        private readonly ConcurrentDictionary<NodeId, KeyCredentialRequestRecord> m_requests = new();
        private readonly ConcurrentDictionary<string, KeyCredentialRequestRecord> m_credentials = new(StringComparer.Ordinal);

        /// <inheritdoc/>
        public NodeId StartRequest(
            string applicationUri,
            ByteString publicKey,
            string? securityPolicyUri,
            ArrayOf<NodeId> requestedRoles)
        {
            int id = Interlocked.Increment(ref m_nextId);
            var requestId = new NodeId((uint)id);

            var record = new KeyCredentialRequestRecord
            {
                RequestId = requestId,
                ApplicationUri = applicationUri,
                PublicKey = publicKey,
                SecurityPolicyUri = securityPolicyUri,
                RequestedRoles = requestedRoles,
                State = KeyCredentialRequestState.Approved,
                CreatedAt = DateTime.UtcNow,
                CredentialId = $"KC-{id}",
                CredentialSecret = GenerateRandomSecret(32),
                CertificateThumbprint = null,
                GrantedSecurityPolicyUri = securityPolicyUri,
                GrantedRoles = requestedRoles
            };

            m_requests[requestId] = record;
            m_credentials[record.CredentialId] = record;
            return requestId;
        }

        /// <inheritdoc/>
        public KeyCredentialRequestState FinishRequest(
            NodeId requestId,
            bool cancelRequest,
            out string? credentialId,
            out ByteString credentialSecret,
            out string? certificateThumbprint,
            out string? securityPolicyUri,
            out ArrayOf<NodeId> grantedRoles)
        {
            if (!m_requests.TryGetValue(requestId, out KeyCredentialRequestRecord? record))
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotFound,
                    "The RequestId is not known.");
            }

            if (cancelRequest)
            {
                record.State = KeyCredentialRequestState.Rejected;
                credentialId = null;
                credentialSecret = default;
                certificateThumbprint = null;
                securityPolicyUri = null;
                grantedRoles = default;
                return record.State;
            }

            if (record.State == KeyCredentialRequestState.Approved)
            {
                record.State = KeyCredentialRequestState.Completed;
            }

            credentialId = record.CredentialId;
            credentialSecret = record.CredentialSecret;
            certificateThumbprint = record.CertificateThumbprint;
            securityPolicyUri = record.GrantedSecurityPolicyUri;
            grantedRoles = record.GrantedRoles;
            return record.State;
        }

        /// <inheritdoc/>
        public void Revoke(string credentialId)
        {
            if (!m_credentials.TryGetValue(credentialId, out KeyCredentialRequestRecord? record))
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotFound,
                    "The CredentialId is not known.");
            }

            record.State = KeyCredentialRequestState.Rejected;
        }

        private static ByteString GenerateRandomSecret(int length)
        {
            byte[] buffer = new byte[length];
            using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
            rng.GetBytes(buffer);
            return ByteString.From(buffer);
        }
    }
}
