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
using Opc.Ua.Gds.Server.Onboarding;

namespace OnboardingRegistrar
{
    /// <summary>
    /// Gates one sample DCA's external admission on its durably committed enrollment receipt.
    /// One policy instance owns the receipt directory; reconciling an interrupted run is a host operation.
    /// </summary>
    internal sealed class FdoRegistrarEnrollmentPolicy : IDeviceRegistrarExternalAuthenticationPolicy, IDisposable
    {
        /// <summary>
        /// Leases a dedicated checkpoint directory and applies the sample's product and application approval.
        /// </summary>
        public FdoRegistrarEnrollmentPolicy(
            string directory,
            string productInstanceUri,
            string applicationUri)
        {
            m_policy = new PullRegistrarPolicy(productInstanceUri, applicationUri);
            using var scope = new MemoryStream();
            using (var writer = new BinaryWriter(scope, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(productInstanceUri);
                writer.Write(applicationUri);
            }
            m_receiptKey = "FdoExternalEnrollment-" + Convert.ToHexString(SHA256.HashData(scope.ToArray()));
            m_files = new PushRegistrarCheckpointFiles(directory);
        }

        /// <summary>
        /// Records the issued certificate and exact authenticated request before returning the certificate to the Device.
        /// The caller must establish Device trust, grant issuance, and verify the CSR before recording it.
        /// </summary>
        public ValueTask RecordIssuanceAsync(
            Guid runId,
            ByteString deviceGuid,
            ByteString attestationCertificate,
            ByteString certificateSigningRequest,
            ByteString certificate,
            CancellationToken ct = default)
        {
            return PublishAsync(
                CreateReceipt(runId, deviceGuid, attestationCertificate, certificateSigningRequest, certificate),
                commit: false,
                ct);
        }

        /// <summary>
        /// Makes only the previously issued receipt eligible for admission after the role authenticates Done20.
        /// A repeat of the same commit is idempotent and does not attest to DoneAck20 delivery.
        /// </summary>
        public ValueTask CommitAsync(
            Guid runId,
            ByteString deviceGuid,
            ByteString attestationCertificate,
            ByteString certificateSigningRequest,
            ByteString certificate,
            CancellationToken ct = default)
        {
            return PublishAsync(
                CreateReceipt(runId, deviceGuid, attestationCertificate, certificateSigningRequest, certificate),
                commit: true,
                ct);
        }

        /// <inheritdoc/>
        public async ValueTask<DeviceRegistrarExternalAuthenticationResult> EvaluateAsync(
            ByteString certificate,
            ArrayOf<string> certificateUris,
            CancellationToken cancellationToken = default)
        {
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                byte[]? receipt = await ReadReceiptAsync(cancellationToken).ConfigureAwait(false);
                if (receipt == null || receipt[1] != Committed ||
                    !CryptographicOperations.FixedTimeEquals(
                        receipt.AsSpan(CertificateDigestOffset),
                        SHA256.HashData(certificate.Span)))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadUserAccessDenied,
                        "The channel certificate has no matching committed enrollment.");
                }

                return await m_policy.EvaluateAsync(certificate, certificateUris, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <summary>
        /// Releases the policy gate after its enrollment and admission callers have stopped.
        /// </summary>
        public void Dispose()
        {
            m_files.Dispose();
            m_gate.Dispose();
        }

        /// <summary>
        /// Publishes an initial issuance or commits the same receipt without replacing another run.
        /// </summary>
        private async ValueTask PublishAsync(byte[] expected, bool commit, CancellationToken ct)
        {
            await m_gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                byte[]? current = await ReadReceiptAsync(ct).ConfigureAwait(false);
                if (current == null && !commit)
                {
                    await m_files.WriteAsync(m_receiptKey, expected, ct).ConfigureAwait(false);
                    return;
                }
                if (current == null ||
                    !CryptographicOperations.FixedTimeEquals(current.AsSpan(2), expected.AsSpan(2)))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidState,
                        "Enrollment does not match the retained issuance. Reconcile the receipt before another run.");
                }
                if (commit && current[1] == Pending)
                {
                    expected[1] = Committed;
                    await m_files.WriteAsync(m_receiptKey, expected, ct).ConfigureAwait(false);
                }
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <summary>
        /// Rejects inconsistent storage instead of treating it as an absent receipt.
        /// </summary>
        private async ValueTask<byte[]?> ReadReceiptAsync(CancellationToken ct)
        {
            byte[]? receipt = await m_files.ReadAsync(m_receiptKey, ct).ConfigureAwait(false);
            if (receipt != null &&
                (receipt.Length != ReceiptSize || receipt[0] != Version || receipt[1] is not (Pending or Committed)))
            {
                throw new ServiceResultException(StatusCodes.BadDecodingError, "The enrollment receipt is invalid.");
            }
            return receipt;
        }

        /// <summary>
        /// Binds the run and Device GUID to hashes of the attestation leaf, exact request, and issued certificate.
        /// </summary>
        private static byte[] CreateReceipt(
            Guid runId,
            ByteString deviceGuid,
            ByteString attestationCertificate,
            ByteString certificateSigningRequest,
            ByteString certificate)
        {
            if (runId == Guid.Empty || deviceGuid.Length != 16 || attestationCertificate.IsEmpty ||
                certificateSigningRequest.IsEmpty || certificate.IsEmpty)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidArgument, "The enrollment evidence is incomplete.");
            }
            var receipt = new byte[ReceiptSize];
            receipt[0] = Version;
            receipt[1] = Pending;
            runId.ToByteArray().CopyTo(receipt, 2);
            deviceGuid.Span.CopyTo(receipt.AsSpan(18, 16));
            SHA256.HashData(attestationCertificate.Span, receipt.AsSpan(34, SHA256.HashSizeInBytes));
            SHA256.HashData(certificateSigningRequest.Span, receipt.AsSpan(66, SHA256.HashSizeInBytes));
            SHA256.HashData(certificate.Span, receipt.AsSpan(CertificateDigestOffset));
            return receipt;
        }

        /// <summary>The layout version.</summary>
        private const byte Version = 1;
        /// <summary>The issuance is retained but not eligible for admission.</summary>
        private const byte Pending = 0;
        /// <summary>The matching authenticated Done20 was committed.</summary>
        private const byte Committed = 1;
        /// <summary>The certificate SHA-256 digest follows the run, Device GUID and two other digests.</summary>
        private const int CertificateDigestOffset = 2 + 16 + 16 + (2 * SHA256.HashSizeInBytes);
        /// <summary>The exact size of the versioned receipt.</summary>
        private const int ReceiptSize = CertificateDigestOffset + SHA256.HashSizeInBytes;
        /// <summary>The sample's exclusive durable checkpoint storage.</summary>
        private readonly PushRegistrarCheckpointFiles m_files;
        /// <summary>The ordinary product and DCA approval.</summary>
        private readonly PullRegistrarPolicy m_policy;
        /// <summary>The receipt key scoped to the approved product and application pair.</summary>
        private readonly string m_receiptKey;
        /// <summary>Serializes receipt reads and changes for this single-instance sample policy.</summary>
        private readonly SemaphoreSlim m_gate = new(1, 1);
    }
}
