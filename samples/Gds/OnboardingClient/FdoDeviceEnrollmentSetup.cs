// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Linq;
using Opc.Ua;
using Opc.Ua.Gds.Fdo;
using Opc.Ua.Security.Certificates;

namespace OnboardingClientSample
{
    /// <summary>
    /// Shares the samples' DCA trust validation and credential installation.
    /// </summary>
    internal static class FdoDeviceEnrollmentSetup
    {
        /// <summary>
        /// Binds the installed credential to its retained key and separate enrollment trust.
        /// The caller keeps the key and trust list alive through TO2.
        /// </summary>
        internal static FdoDeviceEnrollmentHandler CreateHandler(
            OnboardingDeviceCredentials credentials,
            Certificate key,
            TrustListData authority,
            ByteString deviceGuid,
            ByteString csr,
            Action? beforeInstall = null)
        {
            if (authority.TrustedCertificates.Count == 0)
            {
                throw new InvalidOperationException("Configure at least one trusted enrollment CA.");
            }
            var trust = new CertificateValidationScope(
                authority.TrustedCertificates.Select(cert => ByteString.From(cert.RawData.AsSpan())).ToArrayOf(),
                authority.IssuerCertificates.Select(cert => ByteString.From(cert.RawData.AsSpan())).ToArrayOf(),
                authority.TrustedCrls.Concat(authority.IssuerCrls)
                    .Select(crl => ByteString.From(crl.RawData.AsSpan())).ToArrayOf());
            return new FdoDeviceEnrollmentHandler(
                deviceGuid, csr,
                (leaf, ct) => credentials.ValidateExternalCredentialAsync(leaf, key, trust, ct),
                async (leaf, ct) =>
                {
                    beforeInstall?.Invoke();
                    using var issuers = new CertificateCollection();
                    foreach (Certificate issuer in authority.IssuerCertificates)
                    {
                        issuers.Add(issuer);
                    }
                    foreach (Certificate anchor in authority.TrustedCertificates)
                    {
                        issuers.Add(anchor);
                    }
                    await credentials.InstallCompletedExternalCredentialAsync(leaf, key, issuers, ct)
                        .ConfigureAwait(false);
                });
        }
    }
}
