// Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;
using Fdo.Enrollment;
using OnboardingClientSample;
using OnboardingRegistrar;
using Opc.Ua;
using Opc.Ua.Security.Certificates;

namespace FdoOnboarding
{
    /// <summary>
    /// Binds the Owner's real enrollment callbacks to the sample registrar's issuer and durable receipt.
    /// </summary>
    internal sealed class OwnerEnrollmentCallbacks(FdoRegistrarEnrollmentPolicy policy) : IFdoOwnerEnrollmentHandler
    {
        /// <inheritdoc/>
        public async ValueTask<ReadOnlyMemory<byte>> AuthorizeAndIssueAsync(
            FdoEnrollmentRequest request, CancellationToken cancellationToken)
        {
            if (request.IsReenrollment || request.IsServerKeyGeneration)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported, "The demo supports initial enrollment only.");
            }
            ByteString der = await policy.AuthorizeAndIssueAsync(
                request.RunId, ByteString.From(request.DeviceGuid.Span),
                ByteString.From(request.DeviceAttestationCertificate.Span),
                ByteString.From(request.CertificateSigningRequest.Span), cancellationToken).ConfigureAwait(false);
            return der.ToArray();
        }

        /// <inheritdoc/>
        public ValueTask CommitAsync(FdoEnrollmentCommit enrollment, CancellationToken cancellationToken)
        {
            FdoEnrollmentRequest request = enrollment.Request;
            return policy.CommitAsync(
                request.RunId, ByteString.From(request.DeviceGuid.Span),
                ByteString.From(request.DeviceAttestationCertificate.Span),
                ByteString.From(request.CertificateSigningRequest.Span),
                ByteString.From(enrollment.Certificate.Span), cancellationToken);
        }
    }

    /// <summary>
    /// Validates the Device's local CSR binding before Done20, then installs only the same leaf after DoneAck20.
    /// </summary>
    internal sealed class DeviceEnrollmentCallbacks(
        OnboardingDeviceCredentials credentials,
        Certificate key,
        CertificateValidationScope trust,
        CertificateCollection issuers,
        ByteString deviceGuid,
        ByteString csr) : IFdoDeviceEnrollmentHandler
    {
        /// <inheritdoc/>
        public async ValueTask ValidateCertificateAsync(
            FdoDeviceEnrollment enrollment, CancellationToken cancellationToken)
        {
            if (m_validated != null || enrollment.IsReenrollment || enrollment.IsServerKeyGeneration ||
                !enrollment.DeviceGuid.Span.SequenceEqual(deviceGuid.Span) ||
                !enrollment.CertificateSigningRequest.Span.SequenceEqual(csr.Span))
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed, "Unexpected enrollment evidence.");
            }
            await credentials.ValidateExternalCredentialAsync(
                ByteString.From(enrollment.Certificate.Span), key, trust, cancellationToken).ConfigureAwait(false);
            m_validated = enrollment;
        }

        /// <inheritdoc/>
        public ValueTask InstallAsync(FdoDeviceEnrollment enrollment, CancellationToken cancellationToken)
        {
            if (m_validated is not { } validated || m_installationStarted || validated.RunId != enrollment.RunId ||
                !validated.Certificate.Span.SequenceEqual(enrollment.Certificate.Span))
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed, "No matching validated enrollment.");
            }
            m_installationStarted = true;
            return credentials.InstallCompletedExternalCredentialAsync(
                ByteString.From(enrollment.Certificate.Span), key, issuers, cancellationToken);
        }

        /// <summary>The exact enrollment that passed preflight in this run.</summary>
        private FdoDeviceEnrollment? m_validated;

        /// <summary>Prevents replaying installation after a successful or uncertain result.</summary>
        private bool m_installationStarted;
    }
}
