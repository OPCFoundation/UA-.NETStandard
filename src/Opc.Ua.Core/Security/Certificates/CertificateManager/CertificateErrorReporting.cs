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
