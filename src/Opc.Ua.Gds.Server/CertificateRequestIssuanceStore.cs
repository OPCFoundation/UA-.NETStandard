/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a
 * copy of this software and associated documentation files (the "Software"),
 * to deal in the Software without restriction, including without limitation
 * the rights to use, copy, modify, merge, publish, distribute, sublicense,
 * and/or sell copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included
 * in all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
 * OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
 * THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR
 * OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE,
 * ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Gds.Server
{
    /// <summary>
    /// Retains issued material while a certificate request is still approved but its delivery has not committed.
    /// Implementations own serialization and publication across every writer sharing their storage.
    /// </summary>
    public interface ICertificateRequestIssuanceStore
    {
        /// <summary>
        /// Returns the retained issuance or exclusively invokes the factory and publishes its result before returning.
        /// Concurrent writers for the same application/request must not invoke competing factories.
        /// A published issuance remains immutable until explicitly removed. Claim handoff must fence prior writers.
        /// Publication errors propagate; no pending credential may be changed before this call succeeds.
        /// A crash before publication may repeat creation, but cannot expose uncommitted material to device lifecycle.
        /// Distributed implementations require a cross-writer transaction or claim, not separate reads and writes.
        /// </summary>
        ValueTask<CertificateRequestIssuance> GetOrCreateAsync(
            NodeId applicationId,
            NodeId requestId,
            Func<CancellationToken, ValueTask<CertificateRequestIssuance>> create,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Removes a retained receipt after the host has established that no completion retry needs it.
        /// This does not change request approval, acceptance, or device privilege.
        /// </summary>
        ValueTask<bool> RemoveAsync(
            NodeId applicationId,
            NodeId requestId,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// One certificate issuance, including secret output for new-key requests.
    /// Private key passwords are not retained, and this value does not establish installation or possession.
    /// </summary>
    public sealed class CertificateRequestIssuance
    {
        /// <summary>
        /// Creates a detached issuance receipt.
        /// </summary>
        public CertificateRequestIssuance(
            string certificateGroupId,
            string certificateTypeId,
            ByteString certificate,
            ByteString privateKey,
            ArrayOf<ByteString> issuerCertificates)
        {
            CertificateGroupId = certificateGroupId ?? throw new ArgumentNullException(nameof(certificateGroupId));
            CertificateTypeId = certificateTypeId ?? throw new ArgumentNullException(nameof(certificateTypeId));
            if (certificateGroupId.Length == 0)
            {
                throw new ArgumentException("A certificate group identifier is required.", nameof(certificateGroupId));
            }
            if (certificateTypeId.Length == 0)
            {
                throw new ArgumentException("A certificate type identifier is required.", nameof(certificateTypeId));
            }
            if (certificate.IsEmpty)
            {
                throw new ArgumentException("An issued certificate is required.", nameof(certificate));
            }
            Certificate = ByteString.From(certificate.Span);
            PrivateKey = ByteString.From(privateKey.Span);
            var issuers = new ByteString[issuerCertificates.Count];
            for (int i = 0; i < issuers.Length; i++)
            {
                if (issuerCertificates[i].IsEmpty)
                {
                    throw new ArgumentException("Issuer certificates must not be empty.", nameof(issuerCertificates));
                }
                issuers[i] = ByteString.From(issuerCertificates[i].Span);
            }
            IssuerCertificates = issuers.ToArrayOf();
        }

        /// <summary>
        /// Gets the configured certificate group identifier used when signing.
        /// </summary>
        public string CertificateGroupId { get; }

        /// <summary>
        /// Gets the configured certificate type identifier used when signing.
        /// </summary>
        public string CertificateTypeId { get; }

        /// <summary>
        /// Gets the issued public certificate.
        /// </summary>
        public ByteString Certificate { get; }

        /// <summary>
        /// Gets the encoded private key output, empty for signing requests.
        /// </summary>
        public ByteString PrivateKey { get; }

        /// <summary>
        /// Gets the issuer chain returned with this issuance.
        /// </summary>
        public ArrayOf<ByteString> IssuerCertificates { get; }
    }

    /// <summary>
    /// Stores request-correlated issuance through an explicitly supplied secret store.
    /// One adapter instance owns its secret-store scope; it serializes creation within that instance only.
    /// ISecretStore has no cross-writer transaction, so two adapter instances must never share a scope.
    /// Distributed hosts supply a transactional provider instead. Receipt retention and secret-store durability
    /// remain host responsibilities; completed SetAsync must publish a complete value before returning.
    /// </summary>
    public sealed class SecretStoreCertificateRequestIssuanceStore : ICertificateRequestIssuanceStore, IDisposable
    {
        /// <summary>
        /// Borrows a secret store and the server namespace table.
        /// The scope must uniquely identify the owning GDS database within that secret store.
        /// </summary>
        public SecretStoreCertificateRequestIssuanceStore(
            ISecretStore secretStore,
            NamespaceTable namespaceUris,
            string scope)
        {
            m_secretStore = secretStore ?? throw new ArgumentNullException(nameof(secretStore));
            m_namespaceUris = namespaceUris ?? throw new ArgumentNullException(nameof(namespaceUris));
            m_scope = scope ?? throw new ArgumentNullException(nameof(scope));
            if (scope.Length == 0)
            {
                throw new ArgumentException("A unique GDS database scope is required.", nameof(scope));
            }
        }

        /// <summary>
        /// Creates a hosted adapter that must receive the running GDS namespace table before accessing receipts.
        /// </summary>
        internal SecretStoreCertificateRequestIssuanceStore(ISecretStore secretStore, string scope)
        {
            m_secretStore = secretStore ?? throw new ArgumentNullException(nameof(secretStore));
            m_scope = scope ?? throw new ArgumentNullException(nameof(scope));
            if (scope.Length == 0)
            {
                throw new ArgumentException("A unique GDS database scope is required.", nameof(scope));
            }
        }

        /// <summary>
        /// Binds one host's namespace table without permitting a later host to reinterpret receipt identifiers.
        /// </summary>
        internal void BindNamespaceTable(NamespaceTable namespaceUris)
        {
            if (namespaceUris == null)
            {
                throw new ArgumentNullException(nameof(namespaceUris));
            }
            NamespaceTable? previous = Interlocked.CompareExchange(ref m_namespaceUris, namespaceUris, null);
            if (previous != null && !ReferenceEquals(previous, namespaceUris))
            {
                throw new InvalidOperationException("The issuance store is already bound to another GDS host.");
            }
        }

        /// <inheritdoc/>
        public async ValueTask<CertificateRequestIssuance> GetOrCreateAsync(
            NodeId applicationId,
            NodeId requestId,
            Func<CancellationToken, ValueTask<CertificateRequestIssuance>> create,
            CancellationToken cancellationToken = default)
        {
            if (create == null)
            {
                throw new ArgumentNullException(nameof(create));
            }
            cancellationToken.ThrowIfCancellationRequested();
            SecretIdentifier identifier = GetIdentifier(applicationId, requestId);
            await m_issuanceGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                using ISecret? existing = await m_secretStore.GetAsync(identifier, cancellationToken)
                    .ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (existing != null)
                {
                    return Decode(existing);
                }
                CertificateRequestIssuance issuance = await create(cancellationToken).ConfigureAwait(false);
                if (issuance == null)
                {
                    throw new InvalidOperationException("The issuance factory returned no receipt.");
                }
                byte[] encoded = Encode(issuance);
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await m_secretStore.SetAsync(identifier, encoded, cancellationToken).ConfigureAwait(false);
                    return issuance;
                }
                finally
                {
                    Array.Clear(encoded, 0, encoded.Length);
                }
            }
            finally
            {
                m_issuanceGate.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask<bool> RemoveAsync(
            NodeId applicationId,
            NodeId requestId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SecretIdentifier identifier = GetIdentifier(applicationId, requestId);
            await m_issuanceGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await m_secretStore.RemoveAsync(identifier, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                m_issuanceGate.Release();
            }
        }

        /// <summary>
        /// Releases the adapter after its owning host has drained completion requests.
        /// The supplied secret store and its persisted receipts are not disposed or removed.
        /// </summary>
        public void Dispose()
        {
            m_issuanceGate.Dispose();
        }

        /// <summary>
        /// Encodes the group, type, certificate, private key output, and original issuer chain without reflection.
        /// </summary>
        private static byte[] Encode(CertificateRequestIssuance issuance)
        {
            using var buffer = new MemoryStream();
            try
            {
                using (var writer = new Utf8JsonWriter(buffer))
                {
                    writer.WriteStartObject();
                    writer.WriteNumber("version", 1);
                    writer.WriteString("group", issuance.CertificateGroupId);
                    writer.WriteString("type", issuance.CertificateTypeId);
                    writer.WriteBase64String("certificate", issuance.Certificate.Span);
                    writer.WriteBase64String("privateKey", issuance.PrivateKey.Span);
                    writer.WriteStartArray("issuers");
                    foreach (ByteString issuer in issuance.IssuerCertificates)
                    {
                        writer.WriteBase64StringValue(issuer.Span);
                    }
                    writer.WriteEndArray();
                    writer.WriteEndObject();
                }
                if (buffer.Length > MaximumReceiptBytes)
                {
                    throw new InvalidDataException("The certificate issuance receipt exceeds its size limit.");
                }
                return buffer.ToArray();
            }
            finally
            {
                if (buffer.TryGetBuffer(out ArraySegment<byte> bytes))
                {
                    Array.Clear(bytes.Array!, bytes.Offset, bytes.Count);
                }
            }
        }

        /// <summary>
        /// Rejects corrupt receipts instead of treating them as permission to generate a replacement.
        /// </summary>
        private static CertificateRequestIssuance Decode(ISecret secret)
        {
            if (secret.Bytes.Length > MaximumReceiptBytes)
            {
                throw new InvalidDataException("The certificate issuance receipt exceeds its size limit.");
            }
            byte[] encoded = secret.Bytes.ToArray();
            try
            {
                using JsonDocument document = JsonDocument.Parse(encoded);
                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object || root.GetProperty("version").GetInt32() != 1)
                {
                    throw new InvalidDataException("The certificate issuance receipt version is invalid.");
                }
                int properties = 0;
                foreach (JsonProperty property in root.EnumerateObject())
                {
                    properties++;
                    if (property.Name is not ("version" or "group" or "type" or
                        "certificate" or "privateKey" or "issuers"))
                    {
                        throw new InvalidDataException("The certificate issuance receipt contains an unknown field.");
                    }
                }
                if (properties != 6)
                {
                    throw new InvalidDataException("The certificate issuance receipt fields are invalid.");
                }
                JsonElement issuerArray = root.GetProperty("issuers");
                var issuers = new ByteString[issuerArray.GetArrayLength()];
                for (int i = 0; i < issuers.Length; i++)
                {
                    issuers[i] = ByteString.From(issuerArray[i].GetBytesFromBase64().AsSpan());
                }
                byte[] privateKey = root.GetProperty("privateKey").GetBytesFromBase64();
                try
                {
                    return new CertificateRequestIssuance(
                        root.GetProperty("group").GetString()!,
                        root.GetProperty("type").GetString()!,
                        ByteString.From(root.GetProperty("certificate").GetBytesFromBase64()),
                        ByteString.From(privateKey),
                        issuers.ToArrayOf());
                }
                finally
                {
                    Array.Clear(privateKey, 0, privateKey.Length);
                }
            }
            catch (Exception error) when (error is JsonException or InvalidOperationException or
                KeyNotFoundException or FormatException or ArgumentException)
            {
                throw new InvalidDataException("The certificate issuance receipt is invalid.", error);
            }
            finally
            {
                Array.Clear(encoded, 0, encoded.Length);
            }
        }

        /// <summary>
        /// Uses namespace URIs and length-prefixed identifiers so reordering or delimiter content cannot alias keys.
        /// </summary>
        private SecretIdentifier GetIdentifier(NodeId applicationId, NodeId requestId)
        {
            string application = EncodeId(applicationId);
            string request = EncodeId(requestId);
            string key = m_scope.Length.ToString(CultureInfo.InvariantCulture) + ":" + m_scope +
                application.Length.ToString(CultureInfo.InvariantCulture) + ":" + application +
                request.Length.ToString(CultureInfo.InvariantCulture) + ":" + request;
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
            return new SecretIdentifier("GdsIssuance-" + Convert.ToBase64String(hash), m_secretStore.StoreType);
        }

        /// <summary>
        /// Requires a registered namespace and a local non-null request/application identifier.
        /// </summary>
        private string EncodeId(NodeId id)
        {
            NamespaceTable namespaces = m_namespaceUris ?? throw new ServiceResultException(
                StatusCodes.BadServerHalted, "The GDS host has not bound the issuance namespace table.");
            if (id.IsNull || string.IsNullOrEmpty(namespaces.GetString(id.NamespaceIndex)))
            {
                throw new ArgumentException(
                    "A non-null identifier with a registered namespace is required.", nameof(id));
            }
            return NodeId.ToExpandedNodeId(id, namespaces).ToString();
        }

        /// <summary>The maximum retained JSON document, including private-key output.</summary>
        private const int MaximumReceiptBytes = 16 * 1024 * 1024;

        /// <summary>The host-owned storage for secret issuance output.</summary>
        private readonly ISecretStore m_secretStore;

        /// <summary>The hosting server's namespace URI mapping.</summary>
        private NamespaceTable? m_namespaceUris;

        /// <summary>The unique database scope inside the secret store.</summary>
        private readonly string m_scope;

        /// <summary>The adapter's internal single-writer coordination.</summary>
        private readonly SemaphoreSlim m_issuanceGate = new(1, 1);
    }
}
