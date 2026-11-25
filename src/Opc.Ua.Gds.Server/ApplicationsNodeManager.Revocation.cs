// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Security.Certificates;
using X509AuthorityKeyIdentifierExtension = Opc.Ua.Security.Certificates.X509AuthorityKeyIdentifierExtension;

namespace Opc.Ua.Gds.Server
{
    public partial class ApplicationsNodeManager
    {
        /// <summary>
        /// Gets every certificate the GDS issued to the application: the
        /// certificates stored in its record and the earlier ones in the
        /// application certificate store that carry its ApplicationUri.
        /// </summary>
        private async Task<List<ByteString>> GetIssuedCertificatesAsync(
            ApplicationRecordDataType application,
            CancellationToken ct)
        {
            var result = new List<ByteString>();
            var thumbprints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string certificateTypeId in m_certTypeMap.Values)
            {
                if (m_database.GetApplicationCertificate(
                        application.ApplicationId,
                        certificateTypeId,
                        out ByteString certificate) &&
                    !certificate.IsEmpty)
                {
                    using var x509 = Certificate.FromRawData(certificate);
                    if (thumbprints.Add(x509.Thumbprint))
                    {
                        result.Add(certificate);
                    }
                }
            }

            using ICertificateStore? store = OpenApplicationCertificatesStore();
            if (store == null || string.IsNullOrEmpty(application.ApplicationUri))
            {
                return result;
            }

            using CertificateCollection certificates = await store.EnumerateAsync(ct)
                .ConfigureAwait(false);
            foreach (Certificate certificate in certificates)
            {
                if (!thumbprints.Contains(certificate.Thumbprint) &&
                    HasApplicationUri(certificate, application.ApplicationUri!) &&
                    FindIssuingGroup(certificate) != null &&
                    thumbprints.Add(certificate.Thumbprint))
                {
                    result.Add(ByteString.From(certificate.RawData));
                }
            }

            return result;
        }

        /// <summary>
        /// Checks that the GDS issued the certificate to the application: it is
        /// one of the certificates in the record, or a certificate in the
        /// application certificate store that carries the ApplicationUri and was
        /// signed by a CA of a certificate group.
        /// </summary>
        private async Task<bool> IsIssuedToApplicationAsync(
            ApplicationRecordDataType application,
            ByteString certificate,
            CancellationToken ct)
        {
            if (IsCurrentApplicationCertificate(application.ApplicationId, certificate))
            {
                return true;
            }

            if (string.IsNullOrEmpty(application.ApplicationUri))
            {
                return false;
            }

            Certificate x509;
            try
            {
                x509 = Certificate.FromRawData(certificate);
            }
            catch (CryptographicException)
            {
                return false;
            }

            using (x509)
            {
                if (!HasApplicationUri(x509, application.ApplicationUri!) ||
                    FindIssuingGroup(x509) == null)
                {
                    return false;
                }

                using ICertificateStore? store = OpenApplicationCertificatesStore();
                if (store == null)
                {
                    return false;
                }

                using CertificateCollection matches = await store
                    .FindByThumbprintAsync(x509.Thumbprint, ct)
                    .ConfigureAwait(false);
                foreach (Certificate match in matches)
                {
                    if (Utils.IsEqual(certificate, match.RawData))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Checks whether the certificate is one of the certificates stored in
        /// the application record.
        /// </summary>
        private bool IsCurrentApplicationCertificate(NodeId applicationId, ByteString certificate)
        {
            foreach (string certificateTypeId in m_certTypeMap.Values)
            {
                if (m_database.GetApplicationCertificate(
                        applicationId,
                        certificateTypeId,
                        out ByteString stored) &&
                    !stored.IsEmpty &&
                    Utils.IsEqual(stored, certificate))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Adds the certificate to the CRL of the CA that issued it and tells
        /// the certificate manager, so that open SecureChannels that use the
        /// certificate are closed.
        /// </summary>
        /// <returns><c>false</c> when no certificate group issued the
        /// certificate.</returns>
        /// <exception cref="ServiceResultException">The CRL could not be
        /// updated.</exception>
        private async Task<bool> RevokeIssuedCertificateAsync(
            ByteString certificate,
            CancellationToken ct)
        {
            if (certificate.IsEmpty)
            {
                return false;
            }

            using var x509 = Certificate.FromRawData(certificate);
            ICertificateGroup? certificateGroup = FindIssuingGroup(x509);
            if (certificateGroup == null)
            {
                return false;
            }

            try
            {
                await certificateGroup.RevokeCertificateAsync(x509, ct).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                m_logger.UnexpectedErrorRevokingCertificate(e, x509.Subject, certificateGroup.Id);
                if (e is ServiceResultException)
                {
                    throw;
                }
                throw new ServiceResultException(
                    StatusCodes.BadInternalError,
                    "The certificate revocation list could not be updated.",
                    e);
            }

            // The new CRL is in the server's issuer store. Notify the
            // certificate manager so that it revalidates the open
            // SecureChannels and closes those that use the revoked
            // certificate.
            try
            {
                m_configuration.CertificateManager?.NotifyTrustListChanged(
                    TrustListIdentifier.Peers,
                    trustChanged: false,
                    crlChanged: true);
            }
            catch (Exception ex)
            {
                m_logger.RevocationNotificationFailed(ex, certificateGroup.Id);
            }

            return true;
        }

        /// <summary>
        /// Finds the certificate group whose CA certificate issued the
        /// certificate.
        /// </summary>
        private ICertificateGroup? FindIssuingGroup(Certificate certificate)
        {
            return FindIssuingGroup(m_certificateGroups.Values, certificate);
        }

        /// <summary>
        /// Finds the certificate group whose CA certificate issued the
        /// certificate.
        /// </summary>
        internal static ICertificateGroup? FindIssuingGroup(
            IEnumerable<ICertificateGroup> certificateGroups,
            Certificate certificate)
        {
            // Prefer the group whose CA certificate is the actual issuer: the
            // authority key identifier names its key or its serial number.
            // Groups may share a CA subject name.
            X509AuthorityKeyIdentifierExtension? authority =
                certificate.FindExtension<X509AuthorityKeyIdentifierExtension>();
            ICertificateGroup? subjectMatch = null;
            foreach (ICertificateGroup certificateGroup in certificateGroups)
            {
                foreach (Certificate? caCertificate in certificateGroup.Certificates.Values)
                {
                    if (caCertificate == null ||
                        !X509Utils.CompareDistinguishedName(caCertificate.Subject, certificate.Issuer))
                    {
                        continue;
                    }
                    if (authority != null && IsAuthority(authority, caCertificate))
                    {
                        return certificateGroup;
                    }
                    subjectMatch ??= certificateGroup;
                }
            }

            // A CA certificate that was replaced is no longer the group's
            // current one; its certificates are found by the subject name.
            return subjectMatch;
        }

        private static bool IsAuthority(
            X509AuthorityKeyIdentifierExtension authority,
            Certificate caCertificate)
        {
            if (!string.IsNullOrEmpty(authority.SerialNumber) &&
                string.Equals(authority.SerialNumber, caCertificate.SerialNumber, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            X509SubjectKeyIdentifierExtension? subjectKey =
                caCertificate.FindExtension<X509SubjectKeyIdentifierExtension>();
            return subjectKey != null &&
                !string.IsNullOrEmpty(authority.KeyIdentifier) &&
                string.Equals(subjectKey.SubjectKeyIdentifier, authority.KeyIdentifier, StringComparison.OrdinalIgnoreCase);
        }

        private ICertificateStore? OpenApplicationCertificatesStore()
        {
            string? storePath = m_globalDiscoveryServerConfiguration.ApplicationCertificatesStorePath;
            if (string.IsNullOrEmpty(storePath))
            {
                return null;
            }
            return new CertificateStoreIdentifier(storePath).OpenStore(Server.Telemetry);
        }

        private static bool HasApplicationUri(Certificate certificate, string applicationUri)
        {
            foreach (string uri in X509Utils.GetApplicationUrisFromCertificate(certificate))
            {
                if (string.Equals(uri, applicationUri, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
