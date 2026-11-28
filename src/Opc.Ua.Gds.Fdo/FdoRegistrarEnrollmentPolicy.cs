// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using global::Fdo.Enrollment;
using Opc.Ua.Gds.Server;
using Opc.Ua.Gds.Server.Onboarding;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Gds.Fdo
{
    /// <summary>
    /// Issues an initial DCA credential for one approved FDO device and gates admission on the committed receipt.
    /// The host supplies durable storage and reconciles interrupted runs before retrying.
    /// </summary>
    public sealed class FdoRegistrarEnrollmentPolicy :
        IFdoOwnerEnrollmentHandler, IDeviceRegistrarExternalAuthenticationPolicy
    {
        /// <summary>
        /// Binds the approved Device to a dedicated signing group and borrows the host's storage and admission policy.
        /// </summary>
        public FdoRegistrarEnrollmentPolicy(
            IFdoEnrollmentReceiptStore receipts,
            IDeviceRegistrarExternalAuthenticationPolicy admissionPolicy,
            string productInstanceUri,
            string applicationUri,
            string subjectName,
            ByteString deviceGuid,
            ByteString attestationSha256,
            ICertificateGroupWithAuthenticatedSigningRequest issuer)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(subjectName);
            ArgumentNullException.ThrowIfNull(receipts);
            ArgumentNullException.ThrowIfNull(admissionPolicy);
            ArgumentNullException.ThrowIfNull(issuer);
            if (!Uri.IsWellFormedUriString(productInstanceUri, UriKind.Absolute) ||
                !Uri.IsWellFormedUriString(applicationUri, UriKind.Absolute))
            {
                throw new ArgumentException("Approval requires absolute product and application URIs.");
            }
            if (deviceGuid.Length != DeviceGuidSize || attestationSha256.Length != SHA256.HashSizeInBytes)
            {
                throw new ArgumentException(
                    "Approval requires a wire-order Device GUID and an attestation SHA-256 pin.");
            }
            m_policy = admissionPolicy;
            m_receipts = receipts;
            m_productInstanceUri = productInstanceUri;
            m_applicationUri = applicationUri;
            m_subjectName = subjectName;
            m_deviceGuid = ByteString.From(deviceGuid.Span);
            m_attestationSha256 = ByteString.From(attestationSha256.Span);
            m_issuer = issuer;
        }

        /// <inheritdoc/>
        async ValueTask<ReadOnlyMemory<byte>> IFdoOwnerEnrollmentHandler.AuthorizeAndIssueAsync(
            FdoEnrollmentRequest request, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            RequireInitialEnrollment(request);
            ByteString certificate = await AuthorizeAndIssueAsync(
                request.RunId, ByteString.From(request.DeviceGuid.Span),
                ByteString.From(request.DeviceAttestationCertificate.Span),
                ByteString.From(request.CertificateSigningRequest.Span), cancellationToken).ConfigureAwait(false);
            return certificate.ToArray();
        }

        /// <inheritdoc/>
        ValueTask IFdoOwnerEnrollmentHandler.CommitAsync(
            FdoEnrollmentCommit enrollment, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(enrollment);
            FdoEnrollmentRequest request = enrollment.Request;
            RequireInitialEnrollment(request);
            return CommitAsync(
                request.RunId, ByteString.From(request.DeviceGuid.Span),
                ByteString.From(request.DeviceAttestationCertificate.Span),
                ByteString.From(request.CertificateSigningRequest.Span),
                ByteString.From(enrollment.Certificate.Span), cancellationToken);
        }

        /// <summary>
        /// Issues from the dedicated group only for the approved Device, then retains the pending receipt before
        /// returning the certificate. The FDO role must authenticate the evidence before handing it to this policy.
        /// </summary>
        internal async ValueTask<ByteString> AuthorizeAndIssueAsync(
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
            ByteString certificate = default;
            await m_receipts.CreateAsync(async token =>
            {
                using Certificate issued = await m_issuer.SigningRequestForAuthenticatedEvidenceAsync(
                    global::Opc.Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType, m_subjectName, m_applicationUri,
                    m_productInstanceUri, [], [], certificateSigningRequest, token).ConfigureAwait(false);
                certificate = ByteString.From(issued.RawData.AsSpan());
                return ByteString.From(
                    CreateReceipt(runId, deviceGuid, attestationCertificate, certificateSigningRequest, certificate));
            }, ct).ConfigureAwait(false);
            if (certificate.IsEmpty)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState, "No enrollment receipt was created.");
            }
            return certificate;
        }

        /// <summary>
        /// Makes only the previously issued receipt eligible for admission after the role authenticates Done20.
        /// A repeat of the same commit is idempotent and does not attest to DoneAck20 delivery.
        /// </summary>
        internal async ValueTask CommitAsync(
            Guid runId,
            ByteString deviceGuid,
            ByteString attestationCertificate,
            ByteString certificateSigningRequest,
            ByteString certificate,
            CancellationToken ct = default)
        {
            byte[] expected =
                CreateReceipt(runId, deviceGuid, attestationCertificate, certificateSigningRequest, certificate);
            while (true)
            {
                byte[]? current = await ReadReceiptAsync(ct).ConfigureAwait(false);
                if (current == null || !MatchesDeviceApproval(current) ||
                    !CryptographicOperations.FixedTimeEquals(current.AsSpan(2), expected.AsSpan(2)))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidState,
                        "Enrollment does not match the retained issuance. Reconcile the receipt before another run.");
                }
                expected[1] = Committed;
                if (current[1] == Committed || await m_receipts.TryReplaceAsync(
                        ByteString.From(current), ByteString.From(expected), ct).ConfigureAwait(false))
                {
                    return;
                }
            }
        }

        /// <inheritdoc/>
        public async ValueTask<DeviceRegistrarExternalAuthenticationResult> EvaluateAsync(
            ByteString certificate,
            ArrayOf<string> certificateUris,
            CancellationToken cancellationToken = default)
        {
            byte[]? receipt = await ReadReceiptAsync(cancellationToken).ConfigureAwait(false);
            if (receipt == null || receipt[1] != Committed || !MatchesDeviceApproval(receipt) ||
                !certificateUris.Contains(m_productInstanceUri) || !certificateUris.Contains(m_applicationUri) ||
                !CryptographicOperations.FixedTimeEquals(
                    receipt.AsSpan(CertificateDigestOffset), SHA256.HashData(certificate.Span)))
            {
                throw new ServiceResultException(
                    StatusCodes.BadUserAccessDenied, "The channel certificate has no matching committed enrollment.");
            }

            DeviceRegistrarExternalAuthenticationResult result = await m_policy
                .EvaluateAsync(certificate, certificateUris, cancellationToken).ConfigureAwait(false);
            if (result?.ProductInstanceUri != m_productInstanceUri ||
                result.Decision?.Application?.ApplicationUri != m_applicationUri)
            {
                throw new ServiceResultException(StatusCodes.BadUserAccessDenied, "The admission identity changed.");
            }
            return result;
        }

        private static void RequireInitialEnrollment(FdoEnrollmentRequest request)
        {
            if (request.IsReenrollment || request.IsServerKeyGeneration)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported, "Only initial client-key enrollment is supported.");
            }
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
            ct.ThrowIfCancellationRequested();
            ByteString stored = await m_receipts.ReadAsync(ct).ConfigureAwait(false);
            byte[]? receipt = stored.IsNull ? null : stored.Span.ToArray();
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

        /// <summary>The host's exclusive durable checkpoint storage.</summary>
        private readonly IFdoEnrollmentReceiptStore m_receipts;

        /// <summary>The ordinary product and DCA approval.</summary>
        private readonly IDeviceRegistrarExternalAuthenticationPolicy m_policy;

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
    }
}
