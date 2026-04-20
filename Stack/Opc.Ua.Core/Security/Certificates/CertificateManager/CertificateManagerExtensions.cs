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
using System.Collections.Generic;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua
{
    /// <summary>
    /// Options for configuring the <see cref="CertificateManager"/>.
    /// </summary>
    public sealed class CertificateManagerOptions
    {
        /// <summary>
        /// Gets or sets the maximum number of rejected certificates to keep.
        /// Default is 5.
        /// </summary>
        public int MaxRejectedCertificates { get; set; } = 5;

        /// <summary>
        /// Gets or sets the threshold before expiry to emit CertificateExpiring events.
        /// Default is 14 days.
        /// </summary>
        public TimeSpan ExpiryWarningThreshold { get; set; } = TimeSpan.FromDays(14);

        /// <summary>
        /// Gets the additional named trust-lists to register.
        /// </summary>
        internal List<(TrustListIdentifier Id, string TrustedPath, string? IssuerPath)> AdditionalTrustLists { get; } = [];

        /// <summary>
        /// Registers a custom named trust-list.
        /// </summary>
        /// <param name="name">The name of the trust list.</param>
        /// <param name="trustedStorePath">Path to the trusted certificate store.</param>
        /// <param name="issuerStorePath">Optional path to the issuer certificate store.</param>
        /// <returns>The options instance for fluent chaining.</returns>
        public CertificateManagerOptions AddTrustList(
            string name, string trustedStorePath, string? issuerStorePath = null)
        {
            AdditionalTrustLists.Add((new TrustListIdentifier(name), trustedStorePath, issuerStorePath));
            return this;
        }
    }

    /// <summary>
    /// Factory methods for creating a <see cref="CertificateManager"/>.
    /// </summary>
    public static class CertificateManagerFactory
    {
        /// <summary>
        /// Creates a <see cref="CertificateManager"/> configured from a
        /// <see cref="SecurityConfiguration"/>.
        /// </summary>
        /// <param name="securityConfiguration">
        /// The security configuration to map trust lists from.
        /// </param>
        /// <param name="telemetry">
        /// The telemetry context used for logging and diagnostics.
        /// </param>
        /// <param name="configure">
        /// Optional callback to further configure the certificate manager options.
        /// </param>
        /// <returns>A fully configured <see cref="CertificateManager"/> instance.</returns>
        public static CertificateManager Create(
            SecurityConfiguration securityConfiguration,
            ITelemetryContext telemetry,
            Action<CertificateManagerOptions>? configure = null)
        {
            if (securityConfiguration == null) throw new ArgumentNullException(nameof(securityConfiguration));
            if (telemetry == null) throw new ArgumentNullException(nameof(telemetry));

            var options = new CertificateManagerOptions();
            configure?.Invoke(options);

            var manager = new CertificateManager(
                telemetry,
                maxRejectedCertificates: options.MaxRejectedCertificates);

            manager.MapFromSecurityConfiguration(securityConfiguration);

            foreach (var (id, trustedPath, issuerPath) in options.AdditionalTrustLists)
            {
                manager.RegisterTrustList(id, trustedPath, issuerPath);
            }

            return manager;
        }
    }
}
