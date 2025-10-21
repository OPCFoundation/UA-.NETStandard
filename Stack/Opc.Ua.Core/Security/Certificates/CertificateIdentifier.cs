/* Copyright (c) 1996-2022 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation Corporate Members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua
{
    /// <summary>
    /// The identifier for an X509 certificate.
    /// </summary>
    public partial class CertificateIdentifier : IOpenStore, IFormattable
    {
        /// <summary>
        /// Formats the value of the current instance using the specified format.
        /// </summary>
        /// <param name="format">The <see cref="string"/> specifying the format to use.
        /// -or-
        /// null to use the default format defined for the type of the <see cref="IFormattable"/> implementation.</param>
        /// <param name="formatProvider">The <see cref="IFormatProvider"/> to use to format the value.
        /// -or-
        /// null to obtain the numeric format information from the current locale setting of the operating system.</param>
        /// <returns>
        /// A <see cref="string"/> containing the value of the current instance in the specified format.
        /// </returns>
        /// <exception cref="FormatException"></exception>
        public string ToString(string format, IFormatProvider formatProvider)
        {
            if (format != null)
            {
                throw new FormatException(Utils.Format("Invalid format string: '{0}'.", format));
            }

            return ToString();
        }

        /// <summary>
        /// Returns a <see cref="string"/> that represents the current <see cref="object"/>.
        /// </summary>
        /// <returns>
        /// A <see cref="string"/> that represents the current <see cref="object"/>.
        /// </returns>
        public override string ToString()
        {
            if (m_certificate != null)
            {
                return GetDisplayName(m_certificate);
            }

            return m_subjectName ?? m_thumbprint;
        }

        /// <summary>
        /// Returns true if the objects are equal.
        /// </summary>
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(this, obj))
            {
                return true;
            }

            if (obj is not CertificateIdentifier id)
            {
                return false;
            }

            if (m_certificate != null && id.m_certificate != null)
            {
                return m_certificate.Thumbprint == id.m_certificate.Thumbprint;
            }

            if (Thumbprint == id.Thumbprint)
            {
                return true;
            }

            if (SubjectName != id.SubjectName)
            {
                return false;
            }

            if (CertificateType != id.CertificateType)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Returns a suitable hash code.
        /// </summary>
        public override int GetHashCode()
        {
            return HashCode.Combine(
                Thumbprint,
                m_storePath,
                StoreType,
                SubjectName,
                CertificateType);
        }

        /// <summary>
        /// Gets or sets the validation options.
        /// </summary>
        /// <value>
        /// The validation options that can be used to suppress certificate validation errors.
        /// </value>
        public CertificateValidationOptions ValidationOptions { get; set; }

        /// <summary>
        /// Gets or sets the actual certificate.
        /// </summary>
        /// <value>The X509 certificate used by this instance.</value>
        public X509Certificate2 Certificate
        {
            get => m_certificate;
            set
            {
                m_certificate = value;
                if (m_certificate != null)
                {
                    CertificateType = GetCertificateType(m_certificate);
                }
            }
        }

        /// <summary>
        /// Finds a certificate in a store.
        /// </summary>
        public Task<X509Certificate2> FindAsync(
            string applicationUri = null,
            ITelemetryContext telemetry = null,
            CancellationToken ct = default)
        {
            return FindAsync(false, applicationUri, telemetry, ct);
        }

        /// <summary>
        /// Loads the private key for the certificate with an optional password.
        /// </summary>
        public Task<X509Certificate2> LoadPrivateKeyAsync(
            char[] password,
            string applicationUri = null,
            ITelemetryContext telemetry = null,
            CancellationToken ct = default)
        {
            return LoadPrivateKeyExAsync(
                password != null && password.Length != 0 ?
                    new CertificatePasswordProvider(password) :
                    null,
                applicationUri,
                telemetry,
                ct);
        }

        /// <summary>
        /// Loads the private key for the certificate with an optional password provider.
        /// </summary>
        public async Task<X509Certificate2> LoadPrivateKeyExAsync(
            ICertificatePasswordProvider passwordProvider,
            string applicationUri = null,
            ITelemetryContext telemetry = null,
            CancellationToken ct = default)
        {
            if (StoreType != CertificateStoreType.X509Store)
            {
                var certificateStoreIdentifier = new CertificateStoreIdentifier(
                    StorePath,
                    StoreType,
                    false);
                using ICertificateStore store = certificateStoreIdentifier.OpenStore(telemetry);
                if (store?.SupportsLoadPrivateKey == true)
                {
                    char[] password = passwordProvider?.GetPassword(this);
                    m_certificate = await store
                        .LoadPrivateKeyAsync(
                            Thumbprint,
                            SubjectName,
                            applicationUri: null,
                            CertificateType,
                            password,
                            ct)
                        .ConfigureAwait(false);

                    //find certificate by applicationUri instead of subjectName, as the subjectName could have changed after a certificate update
                    if (m_certificate == null && !string.IsNullOrEmpty(applicationUri))
                    {
                        m_certificate = await store
                            .LoadPrivateKeyAsync(
                                Thumbprint,
                                subjectName: null,
                                applicationUri,
                                CertificateType,
                                password,
                                ct)
                            .ConfigureAwait(false);
                    }

                    return m_certificate;
                }
                return null;
            }
            return await FindAsync(true, telemetry: telemetry, ct: ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Finds a certificate in a store.
        /// </summary>
        /// <remarks>The certificate type is used to match the signature and public key type.</remarks>
        /// <param name="needPrivateKey">if set to <c>true</c> the returned certificate must contain the private key.</param>
        /// <param name="applicationUri">the application uri in the extensions of the certificate.</param>
        /// <returns>An instance of the <see cref="X509Certificate2"/> that is embedded by this instance or find it in
        /// the selected store pointed out by the <see cref="StorePath"/> using selected <see cref="SubjectName"/> or if specified applicationUri.</returns>
        [Obsolete("Use FindAsync instead")]
        public Task<X509Certificate2> Find(bool needPrivateKey, string applicationUri = null)
        {
            return FindAsync(needPrivateKey, applicationUri);
        }

        /// <summary>
        /// Finds a certificate in a store.
        /// </summary>
        /// <remarks>The certificate type is used to match the signature and public key type.</remarks>
        /// <param name="needPrivateKey">if set to <c>true</c> the returned certificate must contain the private key.</param>
        /// <param name="applicationUri">the application uri in the extensions of the certificate.</param>
        /// <param name="telemetry">Telemetry context to use</param>
        /// <param name="ct">Cancellation token to cancel action</param>
        /// <returns>An instance of the <see cref="X509Certificate2"/> that is embedded by this instance or find it in
        /// the selected store pointed out by the <see cref="StorePath"/> using selected <see cref="SubjectName"/> or if specified applicationUri.</returns>
        public async Task<X509Certificate2> FindAsync(
            bool needPrivateKey,
            string applicationUri = null,
            ITelemetryContext telemetry = null,
            CancellationToken ct = default)
        {
            X509Certificate2 certificate = null;

            // check if the entire certificate has been specified.
            if (m_certificate != null && (!needPrivateKey || m_certificate.HasPrivateKey))
            {
                certificate = m_certificate;
            }
            else
            {
                // open store.
                var certificateStoreIdentifier = new CertificateStoreIdentifier(StorePath, false);
                using ICertificateStore store = certificateStoreIdentifier.OpenStore(telemetry);
                if (store == null)
                {
                    return null;
                }

                X509Certificate2Collection collection = await store.EnumerateAsync(ct)
                    .ConfigureAwait(false);

                certificate = Find(
                    collection,
                    m_thumbprint,
                    m_subjectName,
                    applicationUri,
                    CertificateType,
                    needPrivateKey);

                if (certificate != null)
                {
                    if (needPrivateKey && store.SupportsLoadPrivateKey)
                    {
                        ILogger logger = telemetry.CreateLogger<CertificateIdentifier>();
                        logger.LogWarning(
                            "Loaded a certificate with private key from store {StoreType}. " +
                            "Ensure to call LoadPrivateKeyEx with password provider before calling Find(true).",
                            StoreType);
                    }

                    m_certificate = certificate;
                }
            }

            return certificate;
        }

        /// <summary>
        /// Returns a display name for a certificate.
        /// </summary>
        /// <param name="certificate">The certificate.</param>
        /// <returns>
        /// A string containg FriendlyName of the <see cref="X509Certificate2"/> or created using Subject of
        /// the <see cref="X509Certificate2"/>.
        /// </returns>
        private static string GetDisplayName(X509Certificate2 certificate)
        {
            if (!string.IsNullOrEmpty(certificate.FriendlyName))
            {
                return certificate.FriendlyName;
            }

            string name = certificate.Subject;

            // find the common name delimiter.
            int index = name.IndexOf("CN", StringComparison.Ordinal);

            if (index == -1)
            {
                return name;
            }

            var buffer = new StringBuilder(name.Length);

            // skip characters until finding the '=' character
            for (int ii = index + 2; ii < name.Length; ii++)
            {
                if (name[ii] == '=')
                {
                    index = ii + 1;
                    break;
                }
            }

            // skip whitespace.
            for (int ii = index; ii < name.Length; ii++)
            {
                if (!char.IsWhiteSpace(name[ii]))
                {
                    index = ii;
                    break;
                }
            }

            // read the common until finding a ','.
            for (int ii = index; ii < name.Length; ii++)
            {
                if (name[ii] == ',')
                {
                    break;
                }

                buffer.Append(name[ii]);
            }

            return buffer.ToString();
        }

        /// <summary>
        /// Picks the best certificate from the collection.
        /// Does not ignore expired certificates nor not-yet-valid certificates.
        /// Selection criteria in order of priority:
        /// 1. Valid certificates preferred over expired certificates and over not-yet-valid certificates.
        /// 2. CA-signed certificates preferred over self-signed (within same validity status).
        /// 3. Longest remaining validity if there are any valid certificates.
        /// 4. Least expired if all expired.
        /// 5. The most soon-to-become-valid if all not-yet-valid.
        /// 6. The most soon-to-become-valid if only expired and not-yet-valid certificates exist.
        /// </summary>
        /// <param name="collection">
        /// The collection of certificates to evaluate. The "best" certificate is determined by the following criteria, in order:
        /// (1) Validity (currently valid over expired or not-yet-valid), (2) CA-signed over self-signed within the same validity status,
        /// (3) longest remaining validity if valid, (4) least expired if all expired, (5) soonest to become valid if all not-yet-valid,
        /// (6) soonest to become valid if only expired and not-yet-valid certificates exist.
        /// </param>
        /// <returns>
        /// The best matching certificate according to the selection criteria, or <c>null</c> if the collection is empty or no suitable certificate is found.
        /// </returns>
        private static X509Certificate2 PickBestCertificate(X509Certificate2Collection collection)
        {
            if (collection == null || collection.Count == 0)
            {
                return null;
            }

            X509Certificate2 bestValid = null;
            TimeSpan bestValidRemaining = TimeSpan.MinValue;
            bool bestValidIsCASigned = false;

            X509Certificate2 bestExpired = null;
            TimeSpan bestExpiredTime = TimeSpan.MaxValue; // Most recently expired (closest to now)
            bool bestExpiredIsCASigned = false;

            X509Certificate2 bestNotYetValid = null;
            TimeSpan bestNotYetValidTime = TimeSpan.MaxValue; // Soonest to become valid
            bool bestNotYetValidIsCASigned = false;

            DateTime now = DateTime.UtcNow;

            foreach (X509Certificate2 certificate in collection)
            {
                bool isCASigned = !X509Utils.IsSelfSigned(certificate);

                // Normalize certificate times to UTC for consistent comparison
                // X509Certificate2 NotBefore/NotAfter return local time
                DateTime notBefore = certificate.NotBefore.ToUniversalTime();
                DateTime notAfter = certificate.NotAfter.ToUniversalTime();

                if (notBefore <= now && notAfter >= now)
                {
                    // Valid certificate
                    TimeSpan remainingValidity = notAfter - now;
                    bool isValidBetter = bestValid == null ||
                        (isCASigned && !bestValidIsCASigned) ||
                        (isCASigned == bestValidIsCASigned && remainingValidity > bestValidRemaining);

                    if (isValidBetter)
                    {
                        bestValid = certificate;
                        bestValidRemaining = remainingValidity;
                        bestValidIsCASigned = isCASigned;
                    }
                }
                else if (notAfter < now)
                {
                    // Expired certificate
                    TimeSpan expiredTime = now - notAfter; // How long expired
                    bool isExpiredBetter = bestExpired == null ||
                        (isCASigned && !bestExpiredIsCASigned) ||
                        (isCASigned == bestExpiredIsCASigned && expiredTime < bestExpiredTime);

                    if (isExpiredBetter)
                    {
                        bestExpired = certificate;
                        bestExpiredTime = expiredTime;
                        bestExpiredIsCASigned = isCASigned;
                    }
                }
                else // notBefore > now
                {
                    // Not yet valid certificate
                    TimeSpan notYetValidTime = notBefore - now; // How long until valid
                    bool isNotYetValidBetter = bestNotYetValid == null ||
                        (isCASigned && !bestNotYetValidIsCASigned) ||
                        (isCASigned == bestNotYetValidIsCASigned && notYetValidTime < bestNotYetValidTime);

                    if (isNotYetValidBetter)
                    {
                        bestNotYetValid = certificate;
                        bestNotYetValidTime = notYetValidTime;
                        bestNotYetValidIsCASigned = isCASigned;
                    }
                }
            }

            // Return in priority order: valid > expired > not-yet-valid
            if (bestValid != null)
            {
                return bestValid;
            }

            if (bestExpired != null && bestNotYetValid != null)
            {
                // Both expired and not-yet-valid exist valid certificates do not 
                // Prioritize CA-signed over self-signed
                if (bestNotYetValidIsCASigned && !bestExpiredIsCASigned)
                {
                    return bestNotYetValid;
                }
                if (bestExpiredIsCASigned && !bestNotYetValidIsCASigned)
                {
                    return bestExpired;
                }
                // If both have same CA-signed status, pick the soonest to become valid
                return bestNotYetValidTime < bestExpiredTime ? bestNotYetValid : bestExpired;
            }

            if (bestExpired != null)
            {
                return bestExpired;
            }

            return bestNotYetValid;
        }

        /// <summary>
        /// <para>
        /// Finds a certificate in the specified collection.
        /// The order of search is:
        /// 1. By thumbprint.
        /// 2. By subject name, with exact match on CN= if specified and fuzzy match if not
        /// 3. By application uri.
        /// </para>
        /// <para>
        /// Excepting the thumbprint criteria, multiple matches are possible due to leftover certificates in the certificate store.
        /// If such multiple matches are found, the best matching certificate is selected using the following criteria, in order of priority:
        /// </para>
        /// <para>
        /// 1. Valid certificates preferred over expired certificates and over not-yet-valid certificates.
        /// 2. CA-signed certificates preferred over self-signed (within same validity status).
        /// 3. Longest remaining validity if there are any valid certificates.
        /// 4. Least expired if all expired.
        /// 5. The most soon-to-become-valid if all not-yet-valid.
        /// 6. The most soon-to-become-valid if only expired and soon-to-become-valid certificates exist.
        /// </para>
        /// </summary>
        /// <param name="collection">The collection.</param>
        /// <param name="thumbprint">The thumbprint of the certificate.</param>
        /// <param name="subjectName">Subject name of the certificate.</param>
        /// <param name="applicationUri">ApplicationUri in the SubjectAltNameExtension of the certificate.</param>
        /// <param name="certificateType">The certificate type.</param>
        /// <param name="needPrivateKey">if set to <c>true</c> [need private key].</param>
        public static X509Certificate2 Find(
            X509Certificate2Collection collection,
            string thumbprint,
            string subjectName,
            string applicationUri,
            NodeId certificateType,
            bool needPrivateKey)
        {
            // find by thumbprint.
            if (!string.IsNullOrEmpty(thumbprint))
            {
                collection = collection.Find(X509FindType.FindByThumbprint, thumbprint, false);

                foreach (X509Certificate2 certificate in collection)
                {
                    if (!needPrivateKey || certificate.HasPrivateKey)
                    {
                        if (string.IsNullOrEmpty(subjectName))
                        {
                            return certificate;
                        }

                        List<string> subjectName2 = X509Utils.ParseDistinguishedName(subjectName);

                        if (X509Utils.CompareDistinguishedName(certificate, subjectName2))
                        {
                            return certificate;
                        }
                    }
                }

                return null;
            }

            X509Certificate2Collection matchesOnCriteria = null;

            // find by subject name.
            if (!string.IsNullOrEmpty(subjectName))
            {
                List<string> parsedSubjectName = X509Utils.ParseDistinguishedName(subjectName);

                foreach (X509Certificate2 certificate in collection)
                {
                    if (ValidateCertificateType(certificate, certificateType) &&
                        X509Utils.CompareDistinguishedName(certificate, parsedSubjectName))
                    {
                        if (!needPrivateKey || certificate.HasPrivateKey)
                        {
                            (matchesOnCriteria ??= new X509Certificate2Collection()).Add(certificate);
                        }
                    }
                }
                if (matchesOnCriteria?.Count > 0)
                {
                    return PickBestCertificate(matchesOnCriteria);
                }

                bool hasCommonName = subjectName.IndexOf("CN=", StringComparison.OrdinalIgnoreCase) >= 0;

                // If parsedSubjectName did not match the certificate distinguished name
                // If "CN=" exists in the subject name than an exact match on CN is required
                if (hasCommonName)
                {
                    string commonNameEntry = parsedSubjectName
                        .FirstOrDefault(s => s.StartsWith("CN=", StringComparison.OrdinalIgnoreCase));
                    string commonName = commonNameEntry?.Length > 3
                        ? commonNameEntry.Substring(3).Trim()
                        : null;

                    if (!string.IsNullOrEmpty(commonName))
                    {
                        foreach (X509Certificate2 certificate in collection)
                        {
                            if (ValidateCertificateType(certificate, certificateType) &&
                                (!needPrivateKey || certificate.HasPrivateKey) &&
                                string.Equals(
                                    certificate.GetNameInfo(X509NameType.SimpleName, false),
                                    commonName,
                                    StringComparison.Ordinal))
                            {
                                (matchesOnCriteria ??= new X509Certificate2Collection()).Add(certificate);
                            }
                        }
                        if (matchesOnCriteria?.Count > 0)
                        {
                            return PickBestCertificate(matchesOnCriteria);
                        }
                    }
                }
                // If no "CN=" specified than a fuzzy match is allowed
                else
                {
                    X509Certificate2Collection fuzzyMatches = collection.Find(
                        X509FindType.FindBySubjectName,
                        subjectName,
                        false);
                    foreach (X509Certificate2 certificate in fuzzyMatches)
                    {
                        if (ValidateCertificateType(certificate, certificateType) &&
                            (!needPrivateKey || certificate.HasPrivateKey))
                        {
                            (matchesOnCriteria ??= new X509Certificate2Collection()).Add(certificate);
                        }
                    }
                    if (matchesOnCriteria?.Count > 0)
                    {
                        return PickBestCertificate(matchesOnCriteria);
                    }
                }
            }

            //find by application uri
            if (!string.IsNullOrEmpty(applicationUri))
            {
                foreach (X509Certificate2 certificate in collection)
                {
                    if (applicationUri == X509Utils.GetApplicationUriFromCertificate(certificate) &&
                        ValidateCertificateType(certificate, certificateType) &&
                        (!needPrivateKey || certificate.HasPrivateKey))
                    {
                        (matchesOnCriteria ??= new X509Certificate2Collection()).Add(certificate);
                    }
                }
                if (matchesOnCriteria?.Count > 0)
                {
                    return PickBestCertificate(matchesOnCriteria);
                }
            }

            // certificate not found.
            return null;
        }

        /// <summary>
        /// Obsoleted open call
        /// </summary>
        [Obsolete("Use OpenStore(ITelemetryContext) instead")]
        public ICertificateStore OpenStore()
        {
            return OpenStore(null);
        }

        /// <summary>
        /// Returns an object to access the store containing the certificate.
        /// </summary>
        /// <remarks>
        /// Opens a store which contains public and private keys.
        /// </remarks>
        /// <returns>A disposable instance of the <see cref="ICertificateStore"/>.</returns>
        public ICertificateStore OpenStore(ITelemetryContext telemetry)
        {
            ICertificateStore store = CertificateStoreIdentifier.CreateStore(StoreType, telemetry);
            store.Open(StorePath, false);
            return store;
        }

        /// <summary>
        /// Retrieves the minimum accepted key size given the security configuration
        /// </summary>
        public ushort GetMinKeySize(SecurityConfiguration securityConfiguration)
        {
            if (CertificateType == ObjectTypeIds.RsaMinApplicationCertificateType ||
                CertificateType == ObjectTypeIds.RsaSha256ApplicationCertificateType ||
                securityConfiguration.IsDeprecatedConfiguration
            ) // Deprecated configurations are implicitly RSA
            {
                return securityConfiguration.MinimumCertificateKeySize;
            }
            // non RSA
            return 0;
        }

        /// <summary>
        /// Get the OPC UA CertificateType.
        /// </summary>
        /// <param name="certificate">The certificate with a signature.</param>
        public static NodeId GetCertificateType(X509Certificate2 certificate)
        {
            switch (certificate.SignatureAlgorithm.Value)
            {
                case Oids.ECDsaWithSha1:
                case Oids.ECDsaWithSha384:
                case Oids.ECDsaWithSha256:
                case Oids.ECDsaWithSha512:
                    return EccUtils.GetEccCertificateTypeId(certificate);
                case Oids.RsaPkcs1Sha256:
                case Oids.RsaPkcs1Sha384:
                case Oids.RsaPkcs1Sha512:
                    return ObjectTypeIds.RsaSha256ApplicationCertificateType;
                case Oids.RsaPkcs1Sha1:
                    return ObjectTypeIds.RsaMinApplicationCertificateType;
                default:
                    return NodeId.Null;
            }
        }

        /// <summary>
        /// Validate if the certificate matches the CertificateType.
        /// </summary>
        /// <param name="certificate">The certificate with a signature.</param>
        /// <param name="certificateType">The NodeId of the certificate type.</param>
        public static bool ValidateCertificateType(
            X509Certificate2 certificate,
            NodeId certificateType)
        {
            if (certificateType == null)
            {
                return true;
            }
            switch (certificate.SignatureAlgorithm.Value)
            {
                case Oids.ECDsaWithSha1:
                case Oids.ECDsaWithSha384:
                case Oids.ECDsaWithSha256:
                case Oids.ECDsaWithSha512:
                    NodeId certType = EccUtils.GetEccCertificateTypeId(certificate);
                    if (certType.IsNullNodeId)
                    {
                        return false;
                    }
                    else if (certType == certificateType)
                    {
                        return true;
                    }

                    // not needed: An end entity Certificate shall use P-256.
                    // http://opcfoundation.org/UA/SecurityPolicy#ECC_nistP256
                    //if (certType == ObjectTypeIds.EccNistP384ApplicationCertificateType &&
                    //    certificateType == ObjectTypeIds.EccNistP256ApplicationCertificateType)
                    //{
                    //    return true;
                    //}

                    // not needed: An end entity Certificate shall use P256r1.
                    // http://opcfoundation.org/UA/SecurityPolicy#ECC_brainpoolP256r1
                    //if (certType == ObjectTypeIds.EccBrainpoolP384r1ApplicationCertificateType &&
                    //    certificateType == ObjectTypeIds.EccBrainpoolP256r1ApplicationCertificateType)
                    //{
                    //    return true;
                    //}

                    break;
                default:
                    // TODO: check SHA1/key size
                    if (certificateType == null ||
                        certificateType == ObjectTypeIds.RsaSha256ApplicationCertificateType ||
                        certificateType == ObjectTypeIds.RsaMinApplicationCertificateType ||
                        certificateType == ObjectTypeIds.ApplicationCertificateType)
                    {
                        return true;
                    }
                    break;
            }
            return false;
        }

        /// <summary>
        /// Map a security policy to a list of supported certificate types.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public static IList<NodeId> MapSecurityPolicyToCertificateTypes(string securityPolicy)
        {
            var result = new List<NodeId>();
            switch (securityPolicy)
            {
                case SecurityPolicies.Basic128Rsa15:
                case SecurityPolicies.Basic256:
                    result.Add(ObjectTypeIds.RsaMinApplicationCertificateType);
                    goto case SecurityPolicies.Basic256Sha256;
                case SecurityPolicies.Basic256Sha256:
                case SecurityPolicies.Aes128_Sha256_RsaOaep:
                case SecurityPolicies.Aes256_Sha256_RsaPss:
                    result.Add(ObjectTypeIds.RsaSha256ApplicationCertificateType);
                    goto default;
                case SecurityPolicies.ECC_nistP256:
                    result.Add(ObjectTypeIds.EccNistP256ApplicationCertificateType);
                    goto case SecurityPolicies.ECC_nistP384;
                case SecurityPolicies.ECC_nistP384:
                    result.Add(ObjectTypeIds.EccNistP384ApplicationCertificateType);
                    goto default;
                case SecurityPolicies.ECC_brainpoolP256r1:
                    result.Add(ObjectTypeIds.EccBrainpoolP256r1ApplicationCertificateType);
                    goto case SecurityPolicies.ECC_brainpoolP384r1;
                case SecurityPolicies.ECC_brainpoolP384r1:
                    result.Add(ObjectTypeIds.EccBrainpoolP384r1ApplicationCertificateType);
                    goto default;
                case SecurityPolicies.ECC_curve25519:
                    result.Add(ObjectTypeIds.EccCurve25519ApplicationCertificateType);
                    goto default;
                case SecurityPolicies.ECC_curve448:
                    result.Add(ObjectTypeIds.EccCurve448ApplicationCertificateType);
                    goto default;
                case SecurityPolicies.Https:
                    result.Add(ObjectTypeIds.HttpsCertificateType);
                    goto default;
                default:
                    return result;
            }
        }

        /// <summary>
        /// Disposes and deletes the reference to the certificate.
        /// </summary>
        public void DisposeCertificate()
        {
            X509Certificate2 certificate = m_certificate;
            m_certificate = null;
            Utils.SilentDispose(certificate);
        }

        /// <summary>
        /// The tags of the supported certificate types.
        /// </summary>
        private static readonly Dictionary<uint, string> s_supportedCertificateTypes = new()
        {
            { ObjectTypes.EccNistP256ApplicationCertificateType, "NistP256" },
            { ObjectTypes.EccNistP384ApplicationCertificateType, "NistP384" },
            { ObjectTypes.EccBrainpoolP256r1ApplicationCertificateType, "BrainpoolP256r1" },
            { ObjectTypes.EccBrainpoolP384r1ApplicationCertificateType, "BrainpoolP384r1" },
            { ObjectTypes.EccCurve25519ApplicationCertificateType, "Curve25519" },
            { ObjectTypes.EccCurve448ApplicationCertificateType, "Curve448" },
            { ObjectTypes.RsaSha256ApplicationCertificateType, "RsaSha256" },
            { ObjectTypes.RsaMinApplicationCertificateType, "RsaMin" },
            { ObjectTypes.ApplicationCertificateType, "Rsa" }
        };

#if UNUSED
        /// <summary>
        /// Checks if the certificate data represents a valid X509v3 certificate header.
        /// </summary>
        /// <param name="rawData">The raw data of a <see cref="X509Certificate2"/> object.</param>
        /// <returns>
        /// 	<c>true</c> if <paramref name="rawData"/> is a valid certificate BLOB; otherwise, <c>false</c>.
        /// </returns>
        private static bool IsValidCertificateBlob(byte[] rawData)
        {
            // check for header.
            if (rawData == null || rawData.Length < 4)
            {
                return false;
            }

            // check for ASN.1 header.
            if (rawData[0] != 0x30)
            {
                return false;
            }

            // extract length.
            int length;
            byte octet = rawData[1];

            // check for short for encoding.
            if ((octet & 0x80) == 0)
            {
                length = octet & 0x7F;

                return 2 + length >= rawData.Length;
            }

            // extract number of bytes for the length.
            int lengthBytes = octet & 0x7F;

            if (rawData.Length <= 2 + lengthBytes)
            {
                return false;
            }

            // check for unexpected negative number.
            if ((rawData[2] & 0x80) != 0)
            {
                return false;
            }

            // extract length.
            length = rawData[2];

            for (int ii = 0; ii < lengthBytes - 1; ii++)
            {
                length <<= 8;
                length |= rawData[ii + 3];
            }

            if (2 + lengthBytes + length > rawData.Length)
            {
                return false;
            }

            // potentially valid.
            return true;
        }
#endif

        /// <summary>
        /// The tags of the supported certificate types used to encode the NodeId coressponding to existing value.
        /// </summary>
        // TODO: remove if not used
        private static string EncodeCertificateType(NodeId certificateType)
        {
            if (certificateType == null)
            {
                return null;
            }

            foreach (KeyValuePair<uint, string> supportedCertificateType in s_supportedCertificateTypes)
            {
                if (supportedCertificateType.Key == (uint)certificateType.Identifier)
                {
                    return supportedCertificateType.Value;
                }
            }

            return null;
        }

        /// <summary>
        /// The tags of the supported certificate types used to decode the NodeId coressponding to existing value.
        /// </summary>
        // TODO: remove if not used
        private static NodeId DecodeCertificateType(string certificateType)
        {
            if (certificateType == null)
            {
                return null;
            }

            foreach (KeyValuePair<uint, string> supportedCertificateType in s_supportedCertificateTypes)
            {
                if (supportedCertificateType.Value == certificateType)
                {
                    return new NodeId(supportedCertificateType.Key);
                }
            }

            return null;
        }
    }

    /// <summary>
    /// A collection of CertificateIdentifier objects.
    /// </summary>
    public partial class CertificateIdentifierCollection : ICloneable
    {
        /// <inheritdoc/>
        public virtual object Clone()
        {
            return MemberwiseClone();
        }

        /// <summary>
        /// Creates a new object that is a copy of the current instance.
        /// </summary>
        /// <returns>
        /// A new object that is a copy of this instance.
        /// </returns>
        public new object MemberwiseClone()
        {
            var collection = new CertificateIdentifierCollection();

            for (int ii = 0; ii < Count; ii++)
            {
                collection.Add(Utils.Clone(this[ii]));
            }

            return collection;
        }
    }

    /// <summary>
    /// Wraps a collection of certificate identifiers and exposes it as a certificate store.
    /// </summary>
    public class CertificateIdentifierCollectionStore : ICertificateStore
    {
        /// <summary>
        /// Create an empty collection store.
        /// </summary>
        /// <param name="telemetry">The telemetry context to use to create obvservability instruments</param>
        public CertificateIdentifierCollectionStore(ITelemetryContext telemetry)
        {
            m_certificates = [];
            m_telemetry = telemetry;
        }

        /// <summary>
        /// Create a collection store from an existing collection.
        /// </summary>
        /// <param name="certificates"></param>
        /// <param name="telemetry">The telemetry context to use to create obvservability instruments</param>
        public CertificateIdentifierCollectionStore(
            CertificateIdentifierCollection certificates,
            ITelemetryContext telemetry)
        {
            m_certificates = certificates;
            m_telemetry = telemetry;
        }

        /// <summary>
        /// Frees any unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                // nothing to do.
            }
        }

        /// <inheritdoc/>
        /// <remarks>
        /// The certificate identifier store ignores the location.
        /// </remarks>
        public void Open(string location, bool noPrivateKeys)
        {
            // nothing to do.
        }

        /// <inheritdoc/>
        public void Close()
        {
            // nothing to do.
        }

        /// <inheritdoc/>
        public string StoreType => string.Empty;

        /// <inheritdoc/>
        public string StorePath => string.Empty;

        /// <inheritdoc/>
        public bool NoPrivateKeys => true;

        /// <inheritdoc/>
        public async Task<X509Certificate2Collection> EnumerateAsync(CancellationToken ct = default)
        {
            var collection = new X509Certificate2Collection();

            for (int ii = 0; ii < m_certificates.Count; ii++)
            {
                X509Certificate2 certificate = await m_certificates[ii].FindAsync(
                    false,
                    applicationUri: null,
                    m_telemetry,
                    ct: ct)
                    .ConfigureAwait(false);

                if (certificate != null)
                {
                    collection.Add(certificate);
                }
            }

            return collection;
        }

        /// <inheritdoc/>
        public async Task AddAsync(
            X509Certificate2 certificate,
            char[] password = null,
            CancellationToken ct = default)
        {
            if (certificate == null)
            {
                throw new ArgumentNullException(nameof(certificate));
            }

            for (int ii = 0; ii < m_certificates.Count; ii++)
            {
                X509Certificate2 current = await m_certificates[ii].FindAsync(
                    false,
                    applicationUri: null,
                    m_telemetry,
                    ct: ct)
                    .ConfigureAwait(false);

                if (current != null && current.Thumbprint == certificate.Thumbprint)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadEntryExists,
                        "A certificate with the specified thumbprint already exists. Subject={0}, Thumbprint={1}",
                        certificate.SubjectName,
                        certificate.Thumbprint);
                }
            }

            m_certificates.Add(new CertificateIdentifier(certificate));
        }

        /// <inheritdoc/>
        public async Task<bool> DeleteAsync(string thumbprint, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(thumbprint))
            {
                return false;
            }

            for (int ii = 0; ii < m_certificates.Count; ii++)
            {
                X509Certificate2 certificate = await m_certificates[ii].FindAsync(
                    false,
                    applicationUri: null,
                    m_telemetry,
                    ct)
                    .ConfigureAwait(false);

                if (certificate != null && certificate.Thumbprint == thumbprint)
                {
                    m_certificates.RemoveAt(ii);
                    return true;
                }
            }

            return false;
        }

        /// <inheritdoc/>
        public async Task<X509Certificate2Collection> FindByThumbprintAsync(
            string thumbprint,
            CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(thumbprint))
            {
                return null;
            }

            for (int ii = 0; ii < m_certificates.Count; ii++)
            {
                X509Certificate2 certificate = await m_certificates[ii].FindAsync(
                    false,
                    applicationUri: null,
                    m_telemetry,
                    ct)
                    .ConfigureAwait(false);

                if (certificate != null && certificate.Thumbprint == thumbprint)
                {
                    return [certificate];
                }
            }

            return [];
        }

        /// <inheritdoc/>
        public bool SupportsLoadPrivateKey => false;

        /// <inheritdoc/>
        public Task<X509Certificate2> LoadPrivateKeyAsync(
            string thumbprint,
            string subjectName,
            string applicationUri,
            NodeId certificateType,
            char[] password,
            CancellationToken ct = default)
        {
            return Task.FromResult<X509Certificate2>(null);
        }

        /// <inheritdoc/>
        public bool SupportsCRLs => false;

        /// <inheritdoc/>
        public Task<StatusCode> IsRevokedAsync(
            X509Certificate2 issuer,
            X509Certificate2 certificate,
            CancellationToken ct = default)
        {
            return Task.FromResult((StatusCode)StatusCodes.BadNotSupported);
        }

        /// <inheritdoc/>
        public Task<X509CRLCollection> EnumerateCRLsAsync(CancellationToken ct = default)
        {
            return Task.FromResult(new X509CRLCollection());
        }

        /// <inheritdoc/>
        public Task<X509CRLCollection> EnumerateCRLsAsync(
            X509Certificate2 issuer,
            bool validateUpdateTime = true,
            CancellationToken ct = default)
        {
            return Task.FromResult(new X509CRLCollection());
        }

        /// <inheritdoc/>
        public Task AddCRLAsync(X509CRL crl, CancellationToken ct = default)
        {
            throw new ServiceResultException(StatusCodes.BadNotSupported);
        }

        /// <inheritdoc/>
        public Task<bool> DeleteCRLAsync(X509CRL crl, CancellationToken ct = default)
        {
            throw new ServiceResultException(StatusCodes.BadNotSupported);
        }

        /// <inheritdoc/>
        public Task AddRejectedAsync(
            X509Certificate2Collection certificates,
            int maxCertificates,
            CancellationToken ct = default)
        {
            return Task.CompletedTask;
        }

        private readonly CertificateIdentifierCollection m_certificates;
        private readonly ITelemetryContext m_telemetry;
    }

    /// <summary>
    /// Options that can be used to suppress certificate validation errors.
    /// </summary>
    [Flags]
    public enum CertificateValidationOptions
    {
        /// <summary>
        /// Use the default options.
        /// </summary>
        Default = 0x0,

        /// <summary>
        /// Ignore expired certificates.
        /// </summary>
        SuppressCertificateExpired = 0x1,

        /// <summary>
        /// Ignore mismatches between the URL and the DNS names in the certificate.
        /// </summary>
        SuppressHostNameInvalid = 0x2,

        /// <summary>
        /// Ignore errors when it is not possible to check the revocation status for a certificate.
        /// </summary>
        SuppressRevocationStatusUnknown = 0x8,

        /// <summary>
        /// Attempt to check the revocation status online.
        /// </summary>
        CheckRevocationStatusOnline = 0x10,

        /// <summary>
        /// Attempt to check the revocation status offline.
        /// </summary>
        CheckRevocationStatusOffine = 0x20,

        /// <summary>
        /// Never trust the certificate.
        /// </summary>
        TreatAsInvalid = 0x40
    }
}
