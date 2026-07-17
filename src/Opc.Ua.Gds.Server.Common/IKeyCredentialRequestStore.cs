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
using System.Threading.Tasks;

namespace Opc.Ua.Gds.Server
{
    /// <summary>
    /// Represents the state of a key-credential request.
    /// </summary>
    public enum KeyCredentialRequestState
    {
        /// <summary>
        /// Request is new / pending approval.
        /// </summary>
        New,

        /// <summary>
        /// Request has been approved.
        /// </summary>
        Approved,

        /// <summary>
        /// Request has been rejected.
        /// </summary>
        Rejected,

        /// <summary>
        /// Request has been completed.
        /// </summary>
        Completed
    }

    /// <summary>
    /// Represents a single key-credential request record.
    /// </summary>
    public sealed class KeyCredentialRequestRecord
    {
        /// <summary>
        /// The unique request identifier.
        /// </summary>
        public NodeId RequestId { get; set; }

        /// <summary>
        /// The ApplicationUri that requested the credential.
        /// </summary>
        public string? ApplicationUri { get; set; }

        /// <summary>
        /// The public key supplied by the requester.
        /// </summary>
        public ByteString PublicKey { get; set; }

        /// <summary>
        /// The security policy URI for the credential.
        /// </summary>
        public string? SecurityPolicyUri { get; set; }

        /// <summary>
        /// The roles requested by the caller.
        /// </summary>
        public ArrayOf<NodeId> RequestedRoles { get; set; }

        /// <summary>
        /// Current state of the request.
        /// </summary>
        public KeyCredentialRequestState State { get; set; }

        /// <summary>
        /// The credential identifier (populated after approval).
        /// </summary>
        public string? CredentialId { get; set; }

        /// <summary>
        /// The credential secret (populated after approval).
        /// </summary>
        public ByteString CredentialSecret { get; set; }

        /// <summary>
        /// Thumbprint of the certificate that protects the credential.
        /// </summary>
        public string? CertificateThumbprint { get; set; }

        /// <summary>
        /// The security policy URI actually assigned.
        /// </summary>
        public string? GrantedSecurityPolicyUri { get; set; }

        /// <summary>
        /// The roles actually granted.
        /// </summary>
        public ArrayOf<NodeId> GrantedRoles { get; set; }

        /// <summary>
        /// When the request was created.
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// SHA-256 fingerprint of the SecureChannel client certificate that
        /// initiated the request.
        /// </summary>
        public ByteString ClientCertificateFingerprint { get; set; }
    }

    /// <summary>
    /// Result of <see cref="IKeyCredentialRequestStore.FinishRequestAsync"/>.
    /// </summary>
    public readonly record struct FinishKeyCredentialRequestResult
    {
        /// <summary>
        /// The state of the request.
        /// </summary>
        public KeyCredentialRequestState State { get; init; }

        /// <summary>The issued credential id, or <c>null</c> when cancelled.</summary>
        public string? CredentialId { get; init; }

        /// <summary>The issued credential secret, or <c>default</c> when cancelled.</summary>
        public ByteString CredentialSecret { get; init; }

        /// <summary>
        /// The thumbprint of the protecting certificate.
        /// </summary>
        public string? CertificateThumbprint { get; init; }

        /// <summary>
        /// The assigned security policy URI.
        /// </summary>
        public string? SecurityPolicyUri { get; init; }

        /// <summary>
        /// The granted roles.
        /// </summary>
        public ArrayOf<NodeId> GrantedRoles { get; init; }
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
        ValueTask<NodeId> StartRequestAsync(
            string applicationUri,
            ByteString publicKey,
            string? securityPolicyUri,
            ArrayOf<NodeId> requestedRoles,
            CancellationToken cancellationToken = default);

#if NETSTANDARD2_1 || NET8_0_OR_GREATER
        /// <summary>
        /// Starts a request bound to the initiating SecureChannel client
        /// certificate.
        /// </summary>
        /// <remarks>
        /// The default implementation rejects the request so legacy stores
        /// cannot silently bypass certificate binding.
        /// </remarks>
        ValueTask<NodeId> StartBoundRequestAsync(
            string applicationUri,
            ByteString publicKey,
            string? securityPolicyUri,
            ArrayOf<NodeId> requestedRoles,
            ByteString clientCertificateFingerprint,
            CancellationToken cancellationToken = default)
        {
            throw new ServiceResultException(
                StatusCodes.BadSecurityChecksFailed,
                "The key-credential store does not support client certificate binding.");
        }
#endif

        /// <summary>
        /// Finish (approve or cancel) a key-credential request.
        /// </summary>
        /// <param name="requestId">The request to finish.</param>
        /// <param name="cancelRequest">
        /// When <c>true</c> the request is cancelled; when <c>false</c>
        /// the credential is issued.
        /// </param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The state of the request and any issued credential data.</returns>
        ValueTask<FinishKeyCredentialRequestResult> FinishRequestAsync(
            NodeId requestId,
            bool cancelRequest,
            CancellationToken cancellationToken = default);

#if NETSTANDARD2_1 || NET8_0_OR_GREATER
        /// <summary>
        /// Finishes a request only when it is bound to the supplied
        /// SecureChannel client certificate fingerprint.
        /// </summary>
        /// <remarks>
        /// The default implementation rejects the request so legacy stores
        /// cannot silently bypass certificate binding.
        /// </remarks>
        ValueTask<FinishKeyCredentialRequestResult> FinishBoundRequestAsync(
            NodeId requestId,
            bool cancelRequest,
            ByteString clientCertificateFingerprint,
            CancellationToken cancellationToken = default)
        {
            throw new ServiceResultException(
                StatusCodes.BadSecurityChecksFailed,
                "The key-credential store does not support client certificate binding.");
        }
#endif

        /// <summary>
        /// Revoke a previously issued credential.
        /// </summary>
        /// <param name="credentialId">The credential to revoke.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        ValueTask RevokeAsync(string credentialId, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Optional compatibility surface for stores that enforce SecureChannel
    /// client-certificate binding on TFMs without default interface methods.
    /// </summary>
    public interface IClientCertificateBoundKeyCredentialRequestStore : IKeyCredentialRequestStore
    {
        /// <summary>
        /// Starts a request bound to the supplied client-certificate fingerprint.
        /// </summary>
#if NETSTANDARD2_1 || NET8_0_OR_GREATER
        new ValueTask<NodeId> StartBoundRequestAsync(
#else
        ValueTask<NodeId> StartBoundRequestAsync(
#endif
            string applicationUri,
            ByteString publicKey,
            string? securityPolicyUri,
            ArrayOf<NodeId> requestedRoles,
            ByteString clientCertificateFingerprint,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Finishes a request bound to the supplied client-certificate fingerprint.
        /// </summary>
#if NETSTANDARD2_1 || NET8_0_OR_GREATER
        new ValueTask<FinishKeyCredentialRequestResult> FinishBoundRequestAsync(
#else
        ValueTask<FinishKeyCredentialRequestResult> FinishBoundRequestAsync(
#endif
            NodeId requestId,
            bool cancelRequest,
            ByteString clientCertificateFingerprint,
            CancellationToken cancellationToken = default);
    }

    internal static class KeyCredentialRequestStoreBinding
    {
        public static ValueTask<NodeId> StartBoundRequestCompatAsync(
            this IKeyCredentialRequestStore store,
            string applicationUri,
            ByteString publicKey,
            string? securityPolicyUri,
            ArrayOf<NodeId> requestedRoles,
            ByteString clientCertificateFingerprint,
            CancellationToken cancellationToken)
        {
#if NETSTANDARD2_1 || NET8_0_OR_GREATER
            return store.StartBoundRequestAsync(
                applicationUri,
                publicKey,
                securityPolicyUri,
                requestedRoles,
                clientCertificateFingerprint,
                cancellationToken);
#else
            if (store is IClientCertificateBoundKeyCredentialRequestStore boundStore)
            {
                return boundStore.StartBoundRequestAsync(
                    applicationUri,
                    publicKey,
                    securityPolicyUri,
                    requestedRoles,
                    clientCertificateFingerprint,
                    cancellationToken);
            }

            throw new ServiceResultException(
                StatusCodes.BadSecurityChecksFailed,
                "The key-credential store does not support client certificate binding.");
#endif
        }

        public static ValueTask<FinishKeyCredentialRequestResult> FinishBoundRequestCompatAsync(
            this IKeyCredentialRequestStore store,
            NodeId requestId,
            bool cancelRequest,
            ByteString clientCertificateFingerprint,
            CancellationToken cancellationToken)
        {
#if NETSTANDARD2_1 || NET8_0_OR_GREATER
            return store.FinishBoundRequestAsync(
                requestId,
                cancelRequest,
                clientCertificateFingerprint,
                cancellationToken);
#else
            if (store is IClientCertificateBoundKeyCredentialRequestStore boundStore)
            {
                return boundStore.FinishBoundRequestAsync(
                    requestId,
                    cancelRequest,
                    clientCertificateFingerprint,
                    cancellationToken);
            }

            throw new ServiceResultException(
                StatusCodes.BadSecurityChecksFailed,
                "The key-credential store does not support client certificate binding.");
#endif
        }
    }

    /// <summary>
    /// In-memory implementation of <see cref="IKeyCredentialRequestStore"/>.
    /// Suitable for testing and single-process GDS deployments.
    /// </summary>
    /// <remarks>
    /// When an <see cref="ISecretStore"/> is supplied, credential secrets
    /// are persisted through the store rather than held in-process. This
    /// allows a production deployment to plug in Key Vault, DPAPI,
    /// Kubernetes secrets, or any other backend that implements
    /// <see cref="ISecretStore"/> without changing the GDS code.
    /// </remarks>
    public sealed class InMemoryKeyCredentialRequestStore : IClientCertificateBoundKeyCredentialRequestStore
    {
        private const int Sha256FingerprintLength = 32;

        private int m_nextId;
        private readonly ConcurrentDictionary<NodeId, KeyCredentialRequestRecord> m_requests = new();
        private readonly ConcurrentDictionary<string, KeyCredentialRequestRecord> m_credentials = new(StringComparer.Ordinal);
        private readonly ISecretStore m_secretStore;

        /// <summary>
        /// Creates a store that holds credential secrets in-process.
        /// </summary>
        public InMemoryKeyCredentialRequestStore()
            : this(new InMemorySecretStore("KeyCredential"))
        {
        }

        /// <summary>
        /// Creates a store that delegates credential-secret persistence
        /// to the supplied <paramref name="secretStore"/>.
        /// </summary>
        /// <param name="secretStore">
        /// The secret store used to persist, materialise and purge
        /// credential secrets. Pass an <see cref="InMemorySecretStore"/>
        /// for testing or a Key Vault / DPAPI store for production.
        /// </param>
        public InMemoryKeyCredentialRequestStore(ISecretStore secretStore)
        {
            m_secretStore = secretStore ?? throw new ArgumentNullException(nameof(secretStore));
        }

        /// <inheritdoc/>
        public ValueTask<NodeId> StartRequestAsync(
            string applicationUri,
            ByteString publicKey,
            string? securityPolicyUri,
            ArrayOf<NodeId> requestedRoles,
            CancellationToken cancellationToken = default)
        {
            return StartRequestCoreAsync(
                applicationUri,
                publicKey,
                securityPolicyUri,
                requestedRoles,
                default,
                cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask<NodeId> StartBoundRequestAsync(
            string applicationUri,
            ByteString publicKey,
            string? securityPolicyUri,
            ArrayOf<NodeId> requestedRoles,
            ByteString clientCertificateFingerprint,
            CancellationToken cancellationToken = default)
        {
            ValidateClientCertificateFingerprint(clientCertificateFingerprint);
            return StartRequestCoreAsync(
                applicationUri,
                publicKey,
                securityPolicyUri,
                requestedRoles,
                ByteString.From(clientCertificateFingerprint.ToArray()),
                cancellationToken);
        }

        private async ValueTask<NodeId> StartRequestCoreAsync(
            string applicationUri,
            ByteString publicKey,
            string? securityPolicyUri,
            ArrayOf<NodeId> requestedRoles,
            ByteString clientCertificateFingerprint,
            CancellationToken cancellationToken)
        {
            int id = Interlocked.Increment(ref m_nextId);
            var requestId = new NodeId((uint)id);
            string credentialId = $"KC-{id}";

            // generate and persist the credential secret via ISecretStore
            byte[] secretBytes = GenerateRandomBytes(32);
            var secretId = new SecretIdentifier(credentialId, m_secretStore.StoreType);
            await m_secretStore.SetAsync(secretId, secretBytes, cancellationToken).ConfigureAwait(false);

            var record = new KeyCredentialRequestRecord
            {
                RequestId = requestId,
                ApplicationUri = applicationUri,
                PublicKey = publicKey,
                SecurityPolicyUri = securityPolicyUri,
                RequestedRoles = requestedRoles,
                State = KeyCredentialRequestState.Approved,
                CreatedAt = DateTime.UtcNow,
                CredentialId = credentialId,
                CredentialSecret = ByteString.From(secretBytes),
                CertificateThumbprint = null,
                GrantedSecurityPolicyUri = securityPolicyUri,
                GrantedRoles = requestedRoles,
                ClientCertificateFingerprint = clientCertificateFingerprint
            };

            m_requests[requestId] = record;
            m_credentials[record.CredentialId] = record;
            return requestId;
        }

        /// <inheritdoc/>
        public ValueTask<FinishKeyCredentialRequestResult> FinishRequestAsync(
            NodeId requestId,
            bool cancelRequest,
            CancellationToken cancellationToken = default)
        {
            KeyCredentialRequestRecord record = GetRequest(requestId);
            if (!record.ClientCertificateFingerprint.IsEmpty)
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed,
                    "A client certificate fingerprint is required to finish the bound request.");
            }
            return FinishRequestCoreAsync(record, cancelRequest, cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask<FinishKeyCredentialRequestResult> FinishBoundRequestAsync(
            NodeId requestId,
            bool cancelRequest,
            ByteString clientCertificateFingerprint,
            CancellationToken cancellationToken = default)
        {
            ValidateClientCertificateFingerprint(clientCertificateFingerprint);
            KeyCredentialRequestRecord record = GetRequest(requestId);
            ValidateRequestBinding(record, clientCertificateFingerprint);
            return FinishRequestCoreAsync(record, cancelRequest, cancellationToken);
        }

        private async ValueTask<FinishKeyCredentialRequestResult> FinishRequestCoreAsync(
            KeyCredentialRequestRecord record,
            bool cancelRequest,
            CancellationToken cancellationToken)
        {
            if (cancelRequest)
            {
                record.State = KeyCredentialRequestState.Rejected;
                // purge the secret on cancel
                if (record.CredentialId != null)
                {
                    var secretId = new SecretIdentifier(record.CredentialId, m_secretStore.StoreType);
                    await m_secretStore.RemoveAsync(secretId, cancellationToken).ConfigureAwait(false);
                }
                return new FinishKeyCredentialRequestResult { State = record.State };
            }

            if (record.State == KeyCredentialRequestState.Approved)
            {
                record.State = KeyCredentialRequestState.Completed;
            }

            // materialise the secret from the store
            string? credentialId = record.CredentialId;
            var sid = new SecretIdentifier(credentialId!, m_secretStore.StoreType);
            using ISecret? secret = m_secretStore.TryGet(sid);
            ByteString credentialSecret = secret != null
                ? ByteString.From(secret.Bytes.ToArray())
                : record.CredentialSecret;
            return new FinishKeyCredentialRequestResult
            {
                State = record.State,
                CredentialId = credentialId,
                CredentialSecret = credentialSecret,
                CertificateThumbprint = record.CertificateThumbprint,
                SecurityPolicyUri = record.GrantedSecurityPolicyUri,
                GrantedRoles = record.GrantedRoles
            };
        }

        /// <inheritdoc/>
        public async ValueTask RevokeAsync(string credentialId, CancellationToken cancellationToken = default)
        {
            if (!m_credentials.TryGetValue(credentialId, out KeyCredentialRequestRecord? record))
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotFound,
                    "The CredentialId is not known.");
            }

            record.State = KeyCredentialRequestState.Rejected;

            // purge the secret from the backing store
            var secretId = new SecretIdentifier(credentialId, m_secretStore.StoreType);
            await m_secretStore.RemoveAsync(secretId, cancellationToken).ConfigureAwait(false);
        }

        private static byte[] GenerateRandomBytes(int length)
        {
            byte[] buffer = new byte[length];
            using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
            rng.GetBytes(buffer);
            return buffer;
        }

        private KeyCredentialRequestRecord GetRequest(NodeId requestId)
        {
            if (m_requests.TryGetValue(requestId, out KeyCredentialRequestRecord? record))
            {
                return record;
            }

            throw new ServiceResultException(
                StatusCodes.BadNotFound,
                "The RequestId is not known.");
        }

        private static void ValidateClientCertificateFingerprint(ByteString clientCertificateFingerprint)
        {
            if (clientCertificateFingerprint.Length != Sha256FingerprintLength)
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed,
                    "A valid SHA-256 client certificate fingerprint is required.");
            }
        }

        private static void ValidateRequestBinding(
            KeyCredentialRequestRecord record,
            ByteString clientCertificateFingerprint)
        {
            if (record.ClientCertificateFingerprint.Length != Sha256FingerprintLength)
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed,
                    "The key-credential request has no client certificate binding.");
            }

            if (!CryptoUtils.FixedTimeEquals(
                    record.ClientCertificateFingerprint.Span,
                    clientCertificateFingerprint.Span))
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed,
                    "The key-credential request belongs to a different client certificate.");
            }
        }
    }
}
