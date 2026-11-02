/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
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
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Gds.Client
{
    /// <summary>
    /// Retains received public signing output using a host-selected secret store.
    /// One adapter instance must exclusively own its scope; ISecretStore does not coordinate competing writers.
    /// Restart durability depends on the selected store. This is not a distributed or HA recovery provider.
    /// </summary>
    public sealed class SecretStorePullDcaCertificateReceiptStore : IPullDcaCertificateReceiptStore, IDisposable
    {
        /// <summary>
        /// Borrows the secret store and session namespace table. The host drains operations before disposal.
        /// The scope identifies one device and must not be shared by multiple live adapter instances.
        /// </summary>
        public SecretStorePullDcaCertificateReceiptStore(
            ISecretStore secretStore,
            NamespaceTable namespaceUris,
            string scope,
            ITelemetryContext telemetry)
        {
            m_secretStore = secretStore ?? throw new ArgumentNullException(nameof(secretStore));
            m_namespaceUris = namespaceUris ?? throw new ArgumentNullException(nameof(namespaceUris));
            if (string.IsNullOrWhiteSpace(scope))
            {
                throw new ArgumentException("An exclusive device receipt scope is required.", nameof(scope));
            }
            if (telemetry == null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            m_scope = scope;
            m_context = ServiceMessageContext.Create(telemetry);
        }

        /// <inheritdoc/>
        public async ValueTask<PullDcaCertificateReceipt> GetOrCreateAsync(
            PullDcaRegistrar registrar,
            NodeId applicationId,
            NodeId requestId,
            PullDcaCertificateProfile profile,
            Func<CancellationToken, ValueTask<PullDcaCertificateReceipt>> receive,
            CancellationToken cancellationToken = default)
        {
            if (receive == null)
            {
                throw new ArgumentNullException(nameof(receive));
            }
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }
            ThrowIfDisposed();
            SecretIdentifier identifier = GetIdentifier(registrar, applicationId, requestId);
            string group = EncodeId(profile.CertificateGroupId);
            string type = EncodeId(profile.CertificateTypeId);
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                using ISecret? existing = await m_secretStore.GetAsync(identifier, cancellationToken)
                    .ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (existing != null)
                {
                    return Decode(existing.Bytes, group, type);
                }
                PullDcaCertificateReceipt receipt = await receive(cancellationToken).ConfigureAwait(false);
                byte[] encoded = Encode(receipt, group, type);
                PullDcaCertificateReceipt detached = Decode(encoded, group, type);
                cancellationToken.ThrowIfCancellationRequested();
                await m_secretStore.SetAsync(identifier, encoded, cancellationToken).ConfigureAwait(false);
                return detached;
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <summary>
        /// Explicitly retires a receipt only after the host has made installation recoverable without it.
        /// Removing a receipt does not reset device trust, change installed credentials, or restore a server request.
        /// </summary>
        public async ValueTask<bool> RemoveAsync(
            PullDcaRegistrar registrar,
            NodeId applicationId,
            NodeId requestId,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            SecretIdentifier identifier = GetIdentifier(registrar, applicationId, requestId);
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await m_secretStore.RemoveAsync(identifier, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <summary>
        /// Releases the local gate without deleting receipts or disposing the borrowed store.
        /// </summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref m_disposed, 1) == 0)
            {
                m_gate.Dispose();
            }
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Encodes the bound profile and detached public signing output with standard codec quotas.
        /// </summary>
        private byte[] Encode(PullDcaCertificateReceipt receipt, string group, string type)
        {
            if (receipt == null || receipt.Certificate.IsEmpty)
            {
                throw new InvalidDataException("A received signing certificate is required.");
            }
            foreach (ByteString issuer in receipt.IssuerCertificates)
            {
                if (issuer.IsEmpty)
                {
                    throw new InvalidDataException("An issuer certificate must not be empty.");
                }
            }
            using var buffer = new MemoryStream();
            using (var encoder = new BinaryEncoder(buffer, m_context, leaveOpen: true))
            {
                encoder.WriteUInt32(null, 1);
                encoder.WriteString(null, group);
                encoder.WriteString(null, type);
                encoder.WriteByteString(null, receipt.Certificate);
                encoder.WriteByteStringArray(null, receipt.IssuerCertificates);
            }
            if (buffer.Length > kMaximumReceiptBytes)
            {
                throw new InvalidDataException("The signing receipt exceeds its size limit.");
            }
            return buffer.ToArray();
        }

        /// <summary>
        /// Rejects corruption and profile changes without permitting another FinishRequest call.
        /// </summary>
        private PullDcaCertificateReceipt Decode(ReadOnlySpan<byte> bytes, string group, string type)
        {
            if (bytes.Length > kMaximumReceiptBytes)
            {
                throw new InvalidDataException("The signing receipt exceeds its size limit.");
            }
            try
            {
                using var buffer = new MemoryStream(bytes.ToArray(), writable: false);
                using var decoder = new BinaryDecoder(buffer, m_context);
                if (decoder.ReadUInt32(null) != 1)
                {
                    throw new InvalidDataException("The signing receipt version is not supported.");
                }
                if (decoder.ReadString(null) != group || decoder.ReadString(null) != type)
                {
                    throw new InvalidDataException("The signing receipt belongs to a different certificate profile.");
                }
                ByteString certificate = decoder.ReadByteString(null);
                ArrayOf<ByteString> issuers = decoder.ReadByteStringArray(null);
                if (certificate.IsEmpty || decoder.Position != buffer.Length)
                {
                    throw new InvalidDataException("The signing receipt has invalid or trailing content.");
                }
                foreach (ByteString issuer in issuers)
                {
                    if (issuer.IsEmpty)
                    {
                        throw new InvalidDataException("The signing receipt contains an empty issuer.");
                    }
                }
                return new PullDcaCertificateReceipt(certificate, issuers);
            }
            catch (Exception error) when (error is ServiceResultException or EndOfStreamException or ArgumentException)
            {
                throw new InvalidDataException("The signing receipt is invalid.", error);
            }
        }

        /// <summary>
        /// Binds the endpoint and namespace-stable model/application/request identifiers to one exclusive scope.
        /// Length prefixes prevent delimiter-bearing identifiers from aliasing another request.
        /// </summary>
        private SecretIdentifier GetIdentifier(PullDcaRegistrar registrar, NodeId applicationId, NodeId requestId)
        {
            if (registrar == null)
            {
                throw new ArgumentNullException(nameof(registrar));
            }
            if (registrar.EndpointUrl == null || !registrar.EndpointUrl.IsAbsoluteUri)
            {
                throw new ArgumentException("An absolute registrar endpoint is required.", nameof(registrar));
            }
            string[] parts =
            [
                m_scope, registrar.EndpointUrl.AbsoluteUri, EncodeId(registrar.RegistrarId),
                EncodeId(registrar.CertificateDirectoryId), EncodeId(applicationId), EncodeId(requestId)
            ];
            var key = new StringBuilder();
            foreach (string part in parts)
            {
                key.Append(part.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(part);
            }
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(key.ToString()));
            return new SecretIdentifier("PullDcaReceipt-" + Convert.ToBase64String(hash), m_secretStore.StoreType);
        }

        /// <summary>
        /// Converts an explicitly registered local identifier to its namespace URI representation.
        /// </summary>
        private string EncodeId(NodeId id)
        {
            if (id.IsNull || string.IsNullOrEmpty(m_namespaceUris.GetString(id.NamespaceIndex)))
            {
                throw new ArgumentException(
                    "A non-null identifier with a registered namespace is required.", nameof(id));
            }
            return NodeId.ToExpandedNodeId(id, m_namespaceUris).ToString();
        }

        /// <summary>
        /// Rejects use after disposal.
        /// </summary>
        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref m_disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(SecretStorePullDcaCertificateReceiptStore));
            }
        }

        /// <summary>The maximum encoded public receipt size.</summary>
        private const int kMaximumReceiptBytes = 16 * 1024 * 1024;

        /// <summary>The borrowed secret store.</summary>
        private readonly ISecretStore m_secretStore;

        /// <summary>The host's stable namespace mapping for the selected registrar.</summary>
        private readonly NamespaceTable m_namespaceUris;

        /// <summary>The exclusive device scope.</summary>
        private readonly string m_scope;

        /// <summary>The bounded codec context.</summary>
        private readonly ServiceMessageContext m_context;

        /// <summary>The instance-local gate, not a distributed writer claim.</summary>
        private readonly SemaphoreSlim m_gate = new(1, 1);

        /// <summary>Whether this adapter has been disposed.</summary>
        private int m_disposed;
    }
}
