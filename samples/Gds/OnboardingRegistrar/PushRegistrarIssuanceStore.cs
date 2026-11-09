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
 * THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
 * IN THE SOFTWARE.
 * ======================================================================*/

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Gds.Server;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;

namespace OnboardingRegistrar
{
    /// <summary>
    /// Retains only public signing receipts from the existing GDS CA, never an alternate signer or private keys.
    /// This push-only sample rejects new-key issuance instead of writing secret output to plaintext files.
    /// One exclusive directory lease and one factory gate cover all writers in this sample process.
    /// </summary>
    internal sealed class PushRegistrarIssuanceStore :
        ICertificateRequestIssuanceStore,
        IServerPreStartupTask,
        IDisposable
    {
        /// <summary>
        /// Borrows the public checkpoint writer shared with the enrollment journal.
        /// </summary>
        public PushRegistrarIssuanceStore(PushRegistrarCheckpointFiles files)
        {
            m_files = files ?? throw new ArgumentNullException(nameof(files));
        }

        /// <inheritdoc/>
        public ValueTask OnServerStartingAsync(
            IServerContext server,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(server);
            cancellationToken.ThrowIfCancellationRequested();
            NamespaceTable namespaces = server.MessageContext.NamespaceUris;
            NamespaceTable? previous = Interlocked.CompareExchange(ref m_namespaces, namespaces, null);
            if (previous != null && !ReferenceEquals(previous, namespaces))
            {
                throw new InvalidOperationException("The sample receipt store is already bound to another GDS.");
            }
            return default;
        }

        /// <inheritdoc/>
        public async ValueTask<CertificateRequestIssuance> GetOrCreateAsync(
            NodeId applicationId,
            NodeId requestId,
            Func<CancellationToken, ValueTask<CertificateRequestIssuance>> create,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(create);
            string application = EncodeId(applicationId);
            string request = EncodeId(requestId);
            string key = GetKey(application, request);
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                byte[]? retained = await m_files.ReadAsync(key, cancellationToken).ConfigureAwait(false);
                if (retained != null)
                {
                    return Decode(retained, application, request);
                }
                CertificateRequestIssuance issuance = await create(cancellationToken).ConfigureAwait(false);
                if (!issuance.PrivateKey.IsEmpty)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadNotSupported,
                        "This sample persists public CSR signing receipts only; new-key requests are unsupported.");
                }
                if (issuance.IssuerCertificates.Count > 64)
                {
                    throw new InvalidDataException("The public receipt issuer count exceeds its size limit.");
                }
                using var buffer = new MemoryStream();
                using (var writer = new BinaryWriter(buffer, Encoding.UTF8, leaveOpen: true))
                {
                    writer.Write(1);
                    writer.Write(application);
                    writer.Write(request);
                    writer.Write(issuance.CertificateGroupId);
                    writer.Write(issuance.CertificateTypeId);
                    WriteCertificate(writer, issuance.Certificate);
                    writer.Write(issuance.IssuerCertificates.Count);
                    foreach (ByteString issuer in issuance.IssuerCertificates)
                    {
                        WriteCertificate(writer, issuer);
                    }
                }
                await m_files.WriteAsync(key, buffer.ToArray(), cancellationToken).ConfigureAwait(false);
                return issuance;
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask<bool> RemoveAsync(
            NodeId applicationId,
            NodeId requestId,
            CancellationToken cancellationToken = default)
        {
            string key = GetKey(EncodeId(applicationId), EncodeId(requestId));
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await m_files.RemoveAsync(key, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <summary>
        /// Releases factory coordination after the host stops incoming requests and its push worker.
        /// </summary>
        public void Dispose()
        {
            m_gate.Dispose();
        }

        /// <summary>
        /// Uses the live host's namespace URIs, never process-specific namespace indexes, for durable IDs.
        /// </summary>
        private string EncodeId(NodeId id)
        {
            NamespaceTable namespaces = m_namespaces ??
                throw new InvalidOperationException("The sample receipt store has not been bound to its GDS.");
            if (id.IsNull || string.IsNullOrEmpty(namespaces.GetString(id.NamespaceIndex)))
            {
                throw new ServiceResultException(StatusCodes.BadNodeIdInvalid);
            }
            return NodeId.ToExpandedNodeId(id, namespaces).ToString();
        }

        /// <summary>
        /// Names a public receipt independently of the secret-store enrollment journal identifiers.
        /// </summary>
        private static string GetKey(string application, string request)
        {
            using var buffer = new MemoryStream();
            using (var writer = new BinaryWriter(buffer, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(application);
                writer.Write(request);
            }
            return "PushPublicIssuance-" + Convert.ToHexString(SHA256.HashData(buffer.ToArray()));
        }

        /// <summary>
        /// Checks stored correlation and bounded certificate lengths before returning an immutable receipt.
        /// The checkpoint format has no private-key field.
        /// </summary>
        private static CertificateRequestIssuance Decode(byte[] bytes, string application, string request)
        {
            using var buffer = new MemoryStream(bytes, writable: false);
            using var reader = new BinaryReader(buffer, Encoding.UTF8);
            if (reader.ReadInt32() != 1 || reader.ReadString() != application || reader.ReadString() != request)
            {
                throw new InvalidDataException("The public receipt version or request correlation is invalid.");
            }
            string group = reader.ReadString();
            string type = reader.ReadString();
            ByteString certificate = ReadCertificate(reader);
            int count = reader.ReadInt32();
            if (count < 0 || count > 64)
            {
                throw new InvalidDataException("The public receipt issuer count is invalid.");
            }
            var issuers = new ByteString[count];
            for (int i = 0; i < count; i++)
            {
                issuers[i] = ReadCertificate(reader);
            }
            if (buffer.Position != buffer.Length)
            {
                throw new InvalidDataException("Unexpected data follows the public signing receipt.");
            }
            return new CertificateRequestIssuance(
                group, type, certificate, ByteString.Empty, issuers.ToArrayOf());
        }

        /// <summary>
        /// Writes a bounded DER public certificate, not an arbitrary issuance byte buffer.
        /// </summary>
        private static void WriteCertificate(BinaryWriter writer, ByteString bytes)
        {
            using Certificate certificate = Certificate.FromRawData(bytes);
            if (!certificate.RawData.AsSpan().SequenceEqual(bytes.Span))
            {
                throw new InvalidDataException("A receipt must contain only DER public certificates.");
            }
            writer.Write(bytes.Length);
            writer.Write(bytes.Span);
        }

        /// <summary>
        /// Reads a bounded DER public certificate without allowing a truncated or oversized payload.
        /// </summary>
        private static ByteString ReadCertificate(BinaryReader reader)
        {
            int length = reader.ReadInt32();
            if (length <= 0 || length > PushRegistrarCheckpointFiles.MaximumBytes ||
                length > reader.BaseStream.Length - reader.BaseStream.Position)
            {
                throw new InvalidDataException("The public receipt certificate length is invalid.");
            }
            ByteString bytes = reader.ReadBytes(length).ToByteString();
            using Certificate certificate = Certificate.FromRawData(bytes);
            if (!certificate.RawData.AsSpan().SequenceEqual(bytes.Span))
            {
                throw new InvalidDataException("A receipt must contain only DER public certificates.");
            }
            return bytes;
        }

        /// <summary>The exclusive public checkpoint writer; this store does not own it.</summary>
        private readonly PushRegistrarCheckpointFiles m_files;

        /// <summary>Serializes receipt factory execution, publication and removal.</summary>
        private readonly SemaphoreSlim m_gate = new(1, 1);

        /// <summary>The actual hosting GDS's namespace table.</summary>
        private NamespaceTable? m_namespaces;
    }
}
