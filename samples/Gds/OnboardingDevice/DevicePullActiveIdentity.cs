// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Gds.Client;
using Opc.Ua.Security.Certificates;

namespace OnboardingDevice
{
    /// <summary>
    /// Accepts only the identity already selected by the device security owner.
    /// </summary>
    internal sealed class DevicePullActiveIdentity : IPullDcaIdentityProvider
    {
        /// <summary>
        /// Borrows the certificate manager that owns the active device identity.
        /// </summary>
        public DevicePullActiveIdentity(ICertificateManager certificates)
        {
            m_certificates = certificates ?? throw new ArgumentNullException(nameof(certificates));
        }

        /// <inheritdoc/>
        public ValueTask ActivateAsync(ByteString selectedIdentity, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            VerifyActiveIdentity(selectedIdentity);
            return default;
        }

        private void VerifyActiveIdentity(ByteString selectedIdentity)
        {
            using CertificateEntry? active = m_certificates.AcquireApplicationCertificateByType(
                ObjectTypeIds.RsaSha256ApplicationCertificateType);
            if (selectedIdentity.IsEmpty || active == null || !active.Certificate.HasPrivateKey ||
                !active.Certificate.RawData.AsSpan().SequenceEqual(selectedIdentity.Span))
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed, "The selected identity is not the active device identity.");
            }
        }

        /// <summary>The device's certificate manager.</summary>
        private readonly ICertificateManager m_certificates;
    }
}
