/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua
{
    /// <summary>
    /// Selects the status code a server reports to a client whose application instance
    /// certificate failed validation, for OpenSecureChannel and CreateSession alike.
    /// </summary>
    /// <remarks>
    /// Part 4 §6.1.3 (Table 106) requires Bad_SecurityChecksFailed for the certificate
    /// structure, chain, signature, security policy, trust list and revocation checks,
    /// including an unavailable revocation list, so that a client cannot probe the server's
    /// trust configuration. Validity period, host name, URI and usage errors may be reported
    /// with their own code.
    /// </remarks>
    internal static class CertificateErrorReporting
    {
        /// <summary>
        /// Returns Bad_SecurityChecksFailed when <paramref name="result"/> or any of its inner
        /// results is an error that must not be reported to the client, otherwise the status
        /// code of <paramref name="result"/>.
        /// </summary>
        /// <remarks>
        /// The certificate validator nests every failed check below the last one, so a single
        /// hidden check hides the whole result.
        /// </remarks>
        /// <param name="result">The validation error.</param>
        /// <returns>The status code to report to the client.</returns>
        public static StatusCode GetClientStatusCode(ServiceResult result)
        {
            for (ServiceResult? current = result; current != null; current = current.InnerResult)
            {
                if (IsHiddenFromClient(current.StatusCode))
                {
                    return StatusCodes.BadSecurityChecksFailed;
                }
            }

            return result.StatusCode;
        }

        /// <summary>
        /// Returns whether a certificate validation error may be reported to the client with
        /// its own status code.
        /// </summary>
        /// <param name="statusCode">The status code.</param>
        /// <returns><c>true</c> for validity period, host name, URI and usage errors.</returns>
        public static bool IsReportedToClient(StatusCode statusCode)
        {
            return statusCode == StatusCodes.BadCertificateTimeInvalid ||
                statusCode == StatusCodes.BadCertificateIssuerTimeInvalid ||
                statusCode == StatusCodes.BadCertificateHostNameInvalid ||
                statusCode == StatusCodes.BadCertificateUriInvalid ||
                statusCode == StatusCodes.BadCertificateUseNotAllowed ||
                statusCode == StatusCodes.BadCertificateIssuerUseNotAllowed;
        }

        /// <summary>
        /// Returns whether a certificate validation error must be reported to the client as
        /// Bad_SecurityChecksFailed.
        /// </summary>
        /// <param name="statusCode">The status code.</param>
        /// <returns><c>true</c> for structure, chain, policy, trust list and revocation errors.</returns>
        public static bool IsHiddenFromClient(StatusCode statusCode)
        {
            return statusCode == StatusCodes.BadCertificateInvalid ||
                statusCode == StatusCodes.BadCertificateChainIncomplete ||
                statusCode == StatusCodes.BadCertificatePolicyCheckFailed ||
                statusCode == StatusCodes.BadCertificateUntrusted ||
                statusCode == StatusCodes.BadCertificateRevoked ||
                statusCode == StatusCodes.BadCertificateIssuerRevoked ||
                statusCode == StatusCodes.BadCertificateRevocationUnknown ||
                statusCode == StatusCodes.BadCertificateIssuerRevocationUnknown;
        }
    }
}
