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

#nullable enable

using System;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua
{
    /// <summary>
    /// Endpoint-aware validation extensions on
    /// <see cref="ICertificateValidatorEx"/>.
    /// </summary>
    /// <remarks>
    /// These extensions provide the
    /// <c>ValidateApplicationUri</c> / <c>ValidateDomains</c> capabilities
    /// previously available only on the legacy
    /// <see cref="CertificateValidator"/> class. When the receiver is a
    /// <see cref="CertificateValidator"/> instance, the extension delegates
    /// to the legacy method so the
    /// <see cref="CertificateValidator.CertificateValidation"/> event
    /// (and the rejected-certificate writer) continue to fire. For other
    /// implementations of <see cref="ICertificateValidatorEx"/>, the
    /// extension performs a pure (cache-free, event-free) check that
    /// throws on validation failure.
    /// </remarks>
    public static class CertificateValidationExtensions
    {
        /// <summary>
        /// Validates the application URI in the server certificate against
        /// the URI in the endpoint description.
        /// </summary>
        /// <param name="validator">The validator (receiver).</param>
        /// <param name="serverCertificate">The server certificate that
        /// contains the application URI.</param>
        /// <param name="endpoint">The endpoint used to connect.</param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="validator"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="ServiceResultException">
        /// Thrown with <see cref="StatusCodes.BadCertificateUriInvalid"/>
        /// when the application URI can not be found in the certificate.
        /// </exception>
        public static void ValidateApplicationUri(
            this ICertificateValidatorEx validator,
            Certificate serverCertificate,
            ConfiguredEndpoint endpoint)
        {
            if (validator == null)
            {
                throw new ArgumentNullException(nameof(validator));
            }

            // Prefer the legacy validator's instance method when available
            // so its CertificateValidation event and rejected-store writer
            // continue to fire (preserves existing behaviour).
#pragma warning disable CS0618 // Type or member is obsolete
            if (validator is CertificateValidator legacy)
            {
                legacy.ValidateApplicationUri(serverCertificate, endpoint);
                return;
            }

            ServiceResult serviceResult = CertificateValidator.ValidateServerCertificateApplicationUri(
                serverCertificate,
                endpoint);
#pragma warning restore CS0618
            if (ServiceResult.IsBad(serviceResult))
            {
                throw new ServiceResultException(serviceResult);
            }
        }

        /// <summary>
        /// Validates that the endpoint URL host appears in the server
        /// certificate's domain list.
        /// </summary>
        /// <param name="validator">The validator (receiver).</param>
        /// <param name="serverCertificate">The server certificate.</param>
        /// <param name="endpoint">The endpoint used to connect.</param>
        /// <param name="serverValidation">
        /// Whether this is a server-side validation (changes how the
        /// failure is logged on the legacy validator).
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="validator"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="ServiceResultException">
        /// Thrown with <see cref="StatusCodes.BadCertificateHostNameInvalid"/>
        /// when the endpoint URL host is not listed in the certificate.
        /// </exception>
        public static void ValidateDomains(
            this ICertificateValidatorEx validator,
            Certificate serverCertificate,
            ConfiguredEndpoint endpoint,
            bool serverValidation = false)
        {
            if (validator == null)
            {
                throw new ArgumentNullException(nameof(validator));
            }

#pragma warning disable CS0618 // Type or member is obsolete
            if (validator is CertificateValidator legacy)
            {
                legacy.ValidateDomains(serverCertificate, endpoint, serverValidation);
                return;
            }

            Uri? endpointUrl = endpoint?.EndpointUrl;
            if (endpointUrl != null &&
                !CertificateValidator.FindDomain(serverCertificate, endpointUrl))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadCertificateHostNameInvalid,
                    "The domain '{0}' is not listed in the server certificate.",
                    endpointUrl.IdnHost);
            }
#pragma warning restore CS0618
        }
    }
}
