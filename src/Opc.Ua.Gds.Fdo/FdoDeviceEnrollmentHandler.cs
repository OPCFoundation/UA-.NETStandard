// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;
using global::Fdo.Enrollment;

namespace Opc.Ua.Gds.Fdo
{
    /// <summary>
    /// Binds one FDO device session to the device's retained CSR and host-owned DCA credential handling.
    /// </summary>
    /// <remarks>
    /// Use one instance per session. Validation must check enrollment trust and the locally retained key
    /// without installing anything. Installation must persist the key pair before activating the credential.
    /// The host must reconcile uncertain installation before starting a new run.
    /// </remarks>
    public sealed class FdoDeviceEnrollmentHandler : IFdoDeviceEnrollmentHandler
    {
        /// <summary>
        /// Captures the expected wire-order device GUID and exact CSR for initial client-key enrollment.
        /// </summary>
        public FdoDeviceEnrollmentHandler(
            ByteString deviceGuid,
            ByteString csr,
            Func<ByteString, CancellationToken, ValueTask> validate,
            Func<ByteString, CancellationToken, ValueTask> install)
        {
            if (deviceGuid.Length != 16 || csr.IsEmpty)
            {
                throw new ArgumentException("The wire-order Device GUID and exact CSR are required.");
            }
            m_validate = validate ?? throw new ArgumentNullException(nameof(validate));
            m_install = install ?? throw new ArgumentNullException(nameof(install));
            m_deviceGuid = ByteString.From(deviceGuid.Span);
            m_csr = ByteString.From(csr.Span);
        }

        /// <inheritdoc/>
        async ValueTask IFdoDeviceEnrollmentHandler.ValidateCertificateAsync(
            FdoDeviceEnrollment enrollment, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(enrollment);
            cancellationToken.ThrowIfCancellationRequested();
            if (!MatchesRequest(enrollment) || Interlocked.CompareExchange(ref m_state, 1, 0) != 0)
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed, "Unexpected enrollment evidence.");
            }
            ByteString certificate = ByteString.From(enrollment.Certificate.Span);
            await m_validate(certificate, cancellationToken).ConfigureAwait(false);
            m_certificate = certificate;
            m_runId = enrollment.RunId;
            Volatile.Write(ref m_state, 2);
        }

        /// <inheritdoc/>
        ValueTask IFdoDeviceEnrollmentHandler.InstallAsync(
            FdoDeviceEnrollment enrollment, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(enrollment);
            cancellationToken.ThrowIfCancellationRequested();
            if (!MatchesRequest(enrollment) || Volatile.Read(ref m_state) != 2 || m_runId != enrollment.RunId ||
                !enrollment.Certificate.Span.SequenceEqual(m_certificate.Span) ||
                Interlocked.CompareExchange(ref m_state, 3, 2) != 2)
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed, "No matching validated enrollment.");
            }
            return m_install(ByteString.From(m_certificate.Span), cancellationToken);
        }

        private bool MatchesRequest(FdoDeviceEnrollment enrollment)
        {
            return enrollment.RunId != Guid.Empty && !enrollment.IsReenrollment && !enrollment.IsServerKeyGeneration &&
                !enrollment.Certificate.IsEmpty &&
                enrollment.DeviceGuid.Span.SequenceEqual(m_deviceGuid.Span) &&
                enrollment.CertificateSigningRequest.Span.SequenceEqual(m_csr.Span);
        }

        private readonly ByteString m_deviceGuid;
        private readonly ByteString m_csr;
        private readonly Func<ByteString, CancellationToken, ValueTask> m_validate;
        private readonly Func<ByteString, CancellationToken, ValueTask> m_install;
        private ByteString m_certificate;
        private Guid m_runId;
        private int m_state;
    }
}
