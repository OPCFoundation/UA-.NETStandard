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
using Opc.Ua.Gds.Server.Onboarding;
using Opc.Ua.Security.Certificates;

namespace OnboardingRegistrar
{
    /// <summary>
    /// Gates one sample DCA's external admission on its durably committed enrollment receipt.
    /// One policy instance owns the receipt directory; reconciling an interrupted run is a host operation.
    /// </summary>
    internal sealed class FdoRegistrarEnrollmentPolicy : IDeviceRegistrarExternalAuthenticationPolicy, IDisposable
    {
        /// <summary>
        /// Leases a checkpoint directory and binds the operator-approved Device to a dedicated signing group.
        /// </summary>
        public FdoRegistrarEnrollmentPolicy(
            string directory,
            string productInstanceUri,
            string applicationUri,
            string subjectName,
            ByteString deviceGuid,
            ByteString attestationSha256,
            ICertificateGroupWithAuthenticatedSigningRequest issuer)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(subjectName);
            ArgumentNullException.ThrowIfNull(issuer);
            if (deviceGuid.Length != DeviceGuidSize || attestationSha256.Length != SHA256.HashSizeInBytes)
            {
                throw new ArgumentException(
                    "Approval requires a wire-order Device GUID and an attestation SHA-256 pin.");
            }
            m_policy = new PullRegistrarPolicy(productInstanceUri, applicationUri);
            m_productInstanceUri = productInstanceUri;
            m_applicationUri = applicationUri;
            m_subjectName = subjectName;
            m_deviceGuid = ByteString.From(deviceGuid.Span);
            m_attestationSha256 = ByteString.From(attestationSha256.Span);
            m_issuer = issuer;
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
        /// Issues from the dedicated group only for the approved Device, then retains the pending receipt before
        /// returning the certificate. The FDO role must authenticate the evidence before handing it to this policy.
        /// </summary>
        public async ValueTask<ByteString> AuthorizeAndIssueAsync(
            Guid runId,
            ByteString deviceGuid,
            ByteString attestationCertificate,
            ByteString certificateSigningRequest,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            ValidateEvidence(runId, deviceGuid, attestationCertificate, certificateSigningRequest);
            deviceGuid = ByteString.From(deviceGuid.Span);
            attestationCertificate = ByteString.From(attestationCertificate.Span);
            certificateSigningRequest = ByteString.From(certificateSigningRequest.Span);
            if (!MatchesDeviceApproval(deviceGuid.Span, SHA256.HashData(attestationCertificate.Span)))
            {
                throw new ServiceResultException(StatusCodes.BadUserAccessDenied, "The Device is not approved.");
            }
            await m_gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (await ReadReceiptAsync(ct).ConfigureAwait(false) != null)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidState, "Reconcile the retained enrollment before requesting issuance.");
                }
                using Certificate issued = await m_issuer.SigningRequestForAuthenticatedEvidenceAsync(
                    ObjectTypeIds.RsaSha256ApplicationCertificateType, m_subjectName, m_applicationUri,
                    m_productInstanceUri, [], [], certificateSigningRequest, ct).ConfigureAwait(false);
                ByteString certificate = ByteString.From(issued.RawData.AsSpan());
                byte[] receipt =
                    CreateReceipt(runId, deviceGuid, attestationCertificate, certificateSigningRequest, certificate);
                await m_files.WriteAsync(m_receiptKey, receipt, ct).ConfigureAwait(false);
                return certificate;
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <summary>
        /// Makes only the previously issued receipt eligible for admission after the role authenticates Done20.
        /// A repeat of the same commit is idempotent and does not attest to DoneAck20 delivery.
        /// </summary>
        public async ValueTask CommitAsync(
            Guid runId,
            ByteString deviceGuid,
            ByteString attestationCertificate,
            ByteString certificateSigningRequest,
            ByteString certificate,
            CancellationToken ct = default)
        {
            byte[] expected =
                CreateReceipt(runId, deviceGuid, attestationCertificate, certificateSigningRequest, certificate);
            await m_gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                byte[]? current = await ReadReceiptAsync(ct).ConfigureAwait(false);
                if (current == null || !MatchesDeviceApproval(current) ||
                    !CryptographicOperations.FixedTimeEquals(current.AsSpan(2), expected.AsSpan(2)))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidState,
                        "Enrollment does not match the retained issuance. Reconcile the receipt before another run.");
                }
                if (current[1] == Pending)
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
                if (receipt == null || receipt[1] != Committed || !MatchesDeviceApproval(receipt) ||
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
        /// Checks that a durable receipt belongs to the Device still approved by the operator.
        /// </summary>
        private bool MatchesDeviceApproval(byte[] receipt)
        {
            return MatchesDeviceApproval(
                receipt.AsSpan(DeviceGuidOffset, DeviceGuidSize),
                receipt.AsSpan(AttestationDigestOffset, SHA256.HashSizeInBytes));
        }

        /// <summary>
        /// Matches both fixed-width values selected by the operator.
        /// </summary>
        private bool MatchesDeviceApproval(ReadOnlySpan<byte> deviceGuid, ReadOnlySpan<byte> attestationSha256)
        {
            return CryptographicOperations.FixedTimeEquals(deviceGuid, m_deviceGuid.Span) &&
                CryptographicOperations.FixedTimeEquals(attestationSha256, m_attestationSha256.Span);
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
            ValidateEvidence(runId, deviceGuid, attestationCertificate, certificateSigningRequest);
            if (certificate.IsEmpty)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidArgument, "The issued certificate is missing.");
            }
            var receipt = new byte[ReceiptSize];
            receipt[0] = Version;
            receipt[1] = Pending;
            runId.ToByteArray().CopyTo(receipt, 2);
            deviceGuid.Span.CopyTo(receipt.AsSpan(DeviceGuidOffset, DeviceGuidSize));
            SHA256.HashData(
                attestationCertificate.Span, receipt.AsSpan(AttestationDigestOffset, SHA256.HashSizeInBytes));
            SHA256.HashData(
                certificateSigningRequest.Span,
                receipt.AsSpan(AttestationDigestOffset + SHA256.HashSizeInBytes, SHA256.HashSizeInBytes));
            SHA256.HashData(certificate.Span, receipt.AsSpan(CertificateDigestOffset));
            return receipt;
        }

        /// <summary>
        /// Checks that the authenticated role supplied the run, Device, attestation leaf and exact CSR.
        /// </summary>
        private static void ValidateEvidence(
            Guid runId,
            ByteString deviceGuid,
            ByteString attestationCertificate,
            ByteString certificateSigningRequest)
        {
            if (runId == Guid.Empty || deviceGuid.Length != DeviceGuidSize || attestationCertificate.IsEmpty ||
                certificateSigningRequest.IsEmpty)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidArgument, "The enrollment evidence is incomplete.");
            }
        }

        /// <summary>The layout version.</summary>
        private const byte Version = 1;
        /// <summary>The issuance is retained but not eligible for admission.</summary>
        private const byte Pending = 0;
        /// <summary>The matching authenticated Done20 was committed.</summary>
        private const byte Committed = 1;
        /// <summary>The size of a wire-order Device GUID.</summary>
        private const int DeviceGuidSize = 16;
        /// <summary>The Device GUID follows the layout version, state and run identifier.</summary>
        private const int DeviceGuidOffset = 2 + 16;
        /// <summary>The attestation certificate SHA-256 digest follows the Device GUID.</summary>
        private const int AttestationDigestOffset = DeviceGuidOffset + DeviceGuidSize;
        /// <summary>The certificate SHA-256 digest follows the run, Device GUID and two other digests.</summary>
        private const int CertificateDigestOffset = AttestationDigestOffset + (2 * SHA256.HashSizeInBytes);
        /// <summary>The exact size of the versioned receipt.</summary>
        private const int ReceiptSize = CertificateDigestOffset + SHA256.HashSizeInBytes;
        /// <summary>The sample's exclusive durable checkpoint storage.</summary>
        private readonly PushRegistrarCheckpointFiles m_files;
        /// <summary>The ordinary product and DCA approval.</summary>
        private readonly PullRegistrarPolicy m_policy;
        /// <summary>The operator-approved product instance.</summary>
        private readonly string m_productInstanceUri;
        /// <summary>The operator-approved DCA application.</summary>
        private readonly string m_applicationUri;
        /// <summary>The operator-approved certificate subject.</summary>
        private readonly string m_subjectName;
        /// <summary>The approved wire-order FDO Device GUID.</summary>
        private readonly ByteString m_deviceGuid;
        /// <summary>The SHA-256 pin for the approved Device attestation leaf.</summary>
        private readonly ByteString m_attestationSha256;
        /// <summary>The dedicated, initialized issuance group, borrowed from the host.</summary>
        private readonly ICertificateGroupWithAuthenticatedSigningRequest m_issuer;
        /// <summary>The receipt key scoped to the approved product and application pair.</summary>
        private readonly string m_receiptKey;
        /// <summary>Serializes receipt reads and changes for this single-instance sample policy.</summary>
        private readonly SemaphoreSlim m_gate = new(1, 1);
    }
}
