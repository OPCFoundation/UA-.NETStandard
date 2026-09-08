/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Stateless validation helpers of <see cref="ConfigurationNodeManager"/> for the ServerConfiguration
    /// Push methods (OPC 10000-12 §7.10): certificate/issuer-chain and trust-list checks, subject and
    /// key-size rules, endpoint-reference checks and slot selection. Everything in this file is static
    /// and touches no instance state; members are internal for test access.
    /// </summary>
    public partial class ConfigurationNodeManager
    {
        /// <summary>
        /// Resolves the exact certificate each <see cref="EndpointDescription"/>
        /// currently presents and reports whether any matches
        /// <paramref name="thumbprint"/>.
        /// </summary>
        /// <remarks>
        /// When <paramref name="registry"/> is supplied the presented
        /// certificate is resolved live from the certificate registry using the
        /// endpoint's (immutable) <see cref="EndpointDescription.SecurityPolicyUri"/>,
        /// so a certificate that was rotated after the endpoints were created is
        /// still matched even though the endpoint's cached
        /// <see cref="EndpointDescription.ServerCertificate"/> blob is stale;
        /// endpoints that do not require encryption present no channel
        /// certificate and are skipped. When no <paramref name="registry"/> is
        /// available the endpoint's <see cref="EndpointDescription.ServerCertificate"/>
        /// blob is used as a fallback (external/mocked servers).
        /// </remarks>
        internal static bool IsCertificateReferencedByEndpoint(
            string thumbprint,
            ArrayOf<EndpointDescription> endpoints,
            ICertificateRegistry? registry,
            ITelemetryContext? telemetry)
        {
            if (endpoints.IsNull)
            {
                return false;
            }

            foreach (EndpointDescription endpoint in endpoints)
            {
                if (endpoint == null)
                {
                    continue;
                }

                if (registry != null)
                {
                    // Authoritative path: an endpoint that requires encryption
                    // presents the certificate the registry currently maps its
                    // SecurityPolicyUri to. Endpoints without encryption present
                    // no channel certificate to protect.
                    if (!ServerBase.RequireEncryption(endpoint))
                    {
                        continue;
                    }

                    using CertificateEntry? entry = registry
                        .AcquireApplicationCertificateBySecurityPolicy(endpoint.SecurityPolicyUri!);
                    if (entry?.Certificate is { } current &&
                        string.Equals(current.Thumbprint, thumbprint, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }

                    // The registry resolved the exact certificate this endpoint
                    // presents; do not also consult the potentially stale blob.
                    continue;
                }

                ByteString serverCertificate = endpoint.ServerCertificate;
                if (serverCertificate.IsNull || serverCertificate.Length == 0)
                {
                    continue;
                }

                try
                {
                    using Certificate leaf = Utils.ParseCertificateBlob(serverCertificate, telemetry);
                    if (string.Equals(leaf.Thumbprint, thumbprint, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                catch (ServiceResultException)
                {
                    // A malformed endpoint certificate cannot be matched;
                    // skip it rather than blocking every DeleteCertificate.
                }
            }

            return false;
        }

        internal static async Task ValidatePushCertificateAndIssuerChainAsync(
            Certificate newCertificate,
            CertificateCollection issuerCertificates,
            SecurityConfiguration securityConfiguration,
            ITelemetryContext telemetry,
            CancellationToken ct)
        {
            if (newCertificate == null)
            {
                throw new ArgumentNullException(nameof(newCertificate));
            }

            if (issuerCertificates == null)
            {
                throw new ArgumentNullException(nameof(issuerCertificates));
            }

            if (securityConfiguration == null)
            {
                throw new ArgumentNullException(nameof(securityConfiguration));
            }

            if (telemetry == null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }

            using CertificateCollection validationChain = issuerCertificates.AddRef();
            validationChain.Insert(0, newCertificate);

            using CertificateManager validator = CertificateManagerFactory.Create(securityConfiguration, telemetry);
            var options = new Security.Certificates.CertificateValidationOptions
            {
                AllowCertificateDownload = false,
                UrlRetrievalTimeout = TimeSpan.FromMilliseconds(1),
                AcceptError = static (_, serviceResult) =>
                    serviceResult.StatusCode == StatusCodes.BadCertificateUntrusted
            };

            CertificateValidationResult validationResult = await validator.ValidateAsync(
                validationChain,
                trustList: null,
                options: options,
                ct).ConfigureAwait(false);

            validationResult.ThrowIfInvalid();
        }

        /// <summary>
        /// Validates <paramref name="newCertificate"/> against the TrustList
        /// (<paramref name="trustedStore"/>/<paramref name="issuerStore"/>)
        /// associated with a certificate group whose Purpose is
        /// <c>ApplicationCertificateType</c>, per OPC 10000-12 §7.10.5: "the
        /// Server shall verify the Certificate using the validation process
        /// defined in OPC 10000-4. All suppressible errors shall be
        /// ignored; however, they may be logged as warnings. If the
        /// validation fails, the appropriate StatusCode defined in
        /// OPC 10000-4 shall be reported. The validation process requires
        /// that the TrustList associated with the CertificateGroup already
        /// contains the IssuerCertificates."
        /// </summary>
        /// <remarks>
        /// Delegates entirely to the shared certificate validator's own
        /// suppressible-status-code classification (accepting every error
        /// it reports as suppressible) rather than maintaining a second
        /// hard-coded status list here: anything the validator does not
        /// classify as suppressible (key size, certificate type, signature
        /// integrity, URI/hostname requirements, and so on) still fails
        /// before this method's <c>AcceptError</c> callback is ever
        /// consulted.
        /// </remarks>
        /// <exception cref="ServiceResultException">
        /// Thrown when validation fails with a non-suppressible error.
        /// </exception>
        internal static async Task ValidateCertificateAgainstGroupTrustListAsync(
            CertificateStoreIdentifier trustedStore,
            CertificateStoreIdentifier? issuerStore,
            string trustListName,
            Certificate newCertificate,
            SecurityConfiguration securityConfiguration,
            ITelemetryContext telemetry,
            CancellationToken ct)
        {
            if (trustedStore == null)
            {
                throw new ArgumentNullException(nameof(trustedStore));
            }

            if (string.IsNullOrEmpty(trustListName))
            {
                throw new ArgumentException(
                    "Trust list name must not be null or empty.",
                    nameof(trustListName));
            }

            if (newCertificate == null)
            {
                throw new ArgumentNullException(nameof(newCertificate));
            }

            if (securityConfiguration == null)
            {
                throw new ArgumentNullException(nameof(securityConfiguration));
            }

            if (telemetry == null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }

            var trustList = new TrustListIdentifier(trustListName);
            using CertificateManager validator = CertificateManagerFactory.Create(
                securityConfiguration,
                telemetry,
                managerOptions => managerOptions.AddTrustList(
                    trustList.Name,
                    trustedStore.StorePath!,
                    issuerStore?.StorePath));

            using var validationChain = new CertificateCollection { newCertificate };

            var options = new Security.Certificates.CertificateValidationOptions
            {
                AllowCertificateDownload = false,
                UrlRetrievalTimeout = TimeSpan.FromMilliseconds(1),
                // OPC 10000-12 §7.10.5: "All suppressible errors shall be
                // ignored."
                AcceptError = static (_, _) => true
            };

            CertificateValidationResult validationResult = await validator.ValidateAsync(
                validationChain,
                trustList: trustList,
                options: options,
                ct).ConfigureAwait(false);

            validationResult.ThrowIfInvalid();
        }

        /// <summary>
        /// Builds a suitable default SubjectName for an ApplicationCertificateType
        /// slot when the caller omits one, per OPC 10000-12 §7.10.6/§7.10.21:
        /// a subject derived from the Server's ApplicationIdentity (here,
        /// its configured application name).
        /// </summary>
        internal static string CreateDefaultApplicationCertificateSubjectName(string? applicationName)
        {
            if (string.IsNullOrEmpty(applicationName))
            {
                applicationName = "UA Server";
            }

            // Distinguished-name field separators/control characters are not
            // valid inside a single RDN value.
            var sanitized = new StringBuilder(applicationName!.Length);
            foreach (char ch in applicationName)
            {
                sanitized.Append(char.IsControl(ch) || ch is '/' or ',' or ';' ? '+' : ch);
            }

            return Utils.Format("CN={0}, O=OPC Foundation", sanitized);
        }

        /// <summary>
        /// Determines whether <paramref name="subjectName"/>'s common name
        /// (the <c>CN=</c> field) equals one of <paramref name="domainNames"/>,
        /// per OPC 10000-12 §7.10.6: "For HttpsCertificateTypes the
        /// SubjectName shall be specified and have the dnsName or IP
        /// Address as the common name."
        /// </summary>
        internal static bool SubjectCommonNameMatchesDomain(
            string subjectName,
            IEnumerable<string> domainNames)
        {
            string? commonName = null;
            foreach (string field in X509Utils.ParseDistinguishedName(subjectName))
            {
                if (field.StartsWith("CN=", StringComparison.Ordinal))
                {
                    commonName = field[3..].Trim();
                    break;
                }
            }

            if (string.IsNullOrEmpty(commonName))
            {
                return false;
            }

            foreach (string domainName in domainNames)
            {
                if (string.Equals(domainName, commonName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Validates <paramref name="keySizeInBits"/> against the set of
        /// key sizes permitted for <paramref name="certificateTypeId"/> per
        /// OPC 10000-12 §7.10.6: "The CertificateTypeId limits the values
        /// that may be set." A value of 0 (use a suitable default) is
        /// always permitted.
        /// </summary>
        /// <exception cref="ServiceResultException">
        /// Thrown with <see cref="StatusCodes.BadOutOfRange"/> when
        /// <paramref name="keySizeInBits"/> is not supported for the
        /// specified certificate type.
        /// </exception>
        internal static void ValidateKeySizeForCertificateType(
            NodeId certificateTypeId,
            bool isRsaCertificateType,
            ushort keySizeInBits)
        {
            if (keySizeInBits == 0)
            {
                return;
            }

            bool supported;
            if (isRsaCertificateType)
            {
                supported = certificateTypeId == ObjectTypeIds.RsaMinApplicationCertificateType
                    ? keySizeInBits is 1024 or 2048
                    : certificateTypeId == ObjectTypeIds.RsaSha256ApplicationCertificateType
                        ? keySizeInBits is 2048 or 3072 or 4096
                        : keySizeInBits is 1024 or 2048 or 3072 or 4096;
            }
            else if (certificateTypeId == ObjectTypeIds.EccNistP256ApplicationCertificateType ||
                certificateTypeId == ObjectTypeIds.EccApplicationCertificateType ||
                certificateTypeId == ObjectTypeIds.EccBrainpoolP256r1ApplicationCertificateType)
            {
                supported = keySizeInBits == 256;
            }
            else if (certificateTypeId == ObjectTypeIds.EccNistP384ApplicationCertificateType ||
                certificateTypeId == ObjectTypeIds.EccBrainpoolP384r1ApplicationCertificateType)
            {
                supported = keySizeInBits == 384;
            }
            else
            {
                // An unrecognized ECC certificate type; CryptoUtils.GetCurveFromCertificateTypeId
                // reports Bad_NotSupported once certificate construction is attempted.
                return;
            }

            if (!supported)
            {
                throw new ServiceResultException(
                    StatusCodes.BadOutOfRange,
                    Utils.Format(
                        "The keySizeInBits value {0} is not supported for the specified certificate type.",
                        keySizeInBits));
            }
        }

        /// <summary>
        /// Builds the aligned (CertificateTypeIds, Certificates) pair
        /// returned by <c>GetCertificates</c> from only the currently
        /// occupied slots in <paramref name="applicationCertificates"/>,
        /// preserving configured order. A configured placeholder slot
        /// whose <paramref name="resolveActiveCertificate"/> resolves to
        /// <see langword="null"/> (no active certificate) is omitted
        /// rather than reported with an empty <see cref="ByteString"/>.
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="resolveActiveCertificate"/> is
        /// <see langword="null"/>.
        /// </exception>
        internal static (ArrayOf<NodeId> CertificateTypeIds, ArrayOf<ByteString> Certificates)
            SelectOccupiedCertificateSlots(
                ArrayOf<CertificateIdentifier> applicationCertificates,
                Func<NodeId, CertificateEntry?> resolveActiveCertificate)
        {
            if (resolveActiveCertificate == null)
            {
                throw new ArgumentNullException(nameof(resolveActiveCertificate));
            }

            var occupiedTypes = new List<NodeId>();
            var occupiedCerts = new List<ByteString>();

            foreach (CertificateIdentifier appId in applicationCertificates)
            {
                using CertificateEntry? entry = resolveActiveCertificate(appId.CertificateType);
                if (entry?.Certificate == null)
                {
                    continue;
                }

                occupiedTypes.Add(appId.CertificateType);
                occupiedCerts.Add(entry.Certificate.RawData.ToByteString());
            }

            return (occupiedTypes.ToArrayOf(), occupiedCerts.ToArrayOf());
        }
    }
}
