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

using System.Collections.Generic;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Configuration.Tests
{
    /// <summary>
    /// Tests for the security configuration class.
    /// </summary>
    [TestFixture]
    [Category("SecurityConfiguration")]
    [SetCulture("en-us")]
    public class SecurityConfigurationTests
    {
        [Test]
        public void ValidConfgurationPasses()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var configuration = new SecurityConfiguration
            {
                ApplicationCertificate = new CertificateIdentifier { RawData = [] },
                TrustedPeerCertificates = new CertificateTrustList { StorePath = "Test" },
                TrustedIssuerCertificates = new CertificateTrustList { StorePath = "Test" }
            };

            configuration.Validate(telemetry);
        }

        [TestCaseSource(nameof(GetInvalidConfigurations))]
        public void InvalidConfigurationThrows(SecurityConfiguration configuration)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            Assert.Throws<ServiceResultException>(() => configuration.Validate(telemetry));
        }

        private static IEnumerable<TestCaseData> GetInvalidConfigurations()
        {
            yield return new TestCaseData(
                new SecurityConfiguration
                {
                    ApplicationCertificate = new CertificateIdentifier { RawData = [] }
                }
            ).SetName("NoStores");

            yield return new TestCaseData(
                new SecurityConfiguration
                {
                    ApplicationCertificate = new CertificateIdentifier { RawData = [] },
                    TrustedPeerCertificates = new CertificateTrustList { StorePath = string.Empty },
                    TrustedIssuerCertificates = new CertificateTrustList { StorePath = "Test" }
                }
            ).SetName("InvalidTrustedStore");

            yield return new TestCaseData(
                new SecurityConfiguration
                {
                    ApplicationCertificate = new CertificateIdentifier { RawData = [] },
                    TrustedPeerCertificates = new CertificateTrustList { StorePath = "Test" },
                    TrustedIssuerCertificates = new CertificateTrustList
                    {
                        StorePath = string.Empty
                    }
                }
            ).SetName("InvalidIssuerStore");

            yield return new TestCaseData(
                new SecurityConfiguration
                {
                    ApplicationCertificate = new CertificateIdentifier { RawData = [] },
                    TrustedPeerCertificates = new CertificateTrustList { StorePath = "Test" },
                    TrustedIssuerCertificates = new CertificateTrustList { StorePath = "Test" },
                    TrustedHttpsCertificates = new CertificateTrustList { StorePath = "Test" }
                }
            ).SetName("OnlyTrustedHttps");

            yield return new TestCaseData(
                new SecurityConfiguration
                {
                    ApplicationCertificate = new CertificateIdentifier { RawData = [] },
                    TrustedPeerCertificates = new CertificateTrustList { StorePath = "Test" },
                    TrustedIssuerCertificates = new CertificateTrustList { StorePath = "Test" },
                    HttpsIssuerCertificates = new CertificateTrustList { StorePath = "Test" }
                }
            ).SetName("OnlyIssuerHttps");

            yield return new TestCaseData(
                new SecurityConfiguration
                {
                    ApplicationCertificate = new CertificateIdentifier { RawData = [] },
                    TrustedPeerCertificates = new CertificateTrustList { StorePath = "Test" },
                    TrustedIssuerCertificates = new CertificateTrustList { StorePath = "Test" },
                    HttpsIssuerCertificates = new CertificateTrustList { StorePath = string.Empty },
                    TrustedHttpsCertificates = new CertificateTrustList { StorePath = "Test" }
                }
            ).SetName("InvalidHttpsIssuer");

            yield return new TestCaseData(
                new SecurityConfiguration
                {
                    ApplicationCertificate = new CertificateIdentifier { RawData = [] },
                    TrustedPeerCertificates = new CertificateTrustList { StorePath = "Test" },
                    TrustedIssuerCertificates = new CertificateTrustList { StorePath = "Test" },
                    HttpsIssuerCertificates = new CertificateTrustList { StorePath = "Test" },
                    TrustedHttpsCertificates = new CertificateTrustList { StorePath = string.Empty }
                }
            ).SetName("InvalidHttpsTrusted");

            yield return new TestCaseData(
                new SecurityConfiguration
                {
                    ApplicationCertificate = new CertificateIdentifier { RawData = [] },
                    TrustedPeerCertificates = new CertificateTrustList { StorePath = "Test" },
                    TrustedIssuerCertificates = new CertificateTrustList { StorePath = "Test" },
                    TrustedUserCertificates = new CertificateTrustList { StorePath = "Test" }
                }
            ).SetName("OnlyTrustedUser");

            yield return new TestCaseData(
                new SecurityConfiguration
                {
                    ApplicationCertificate = new CertificateIdentifier { RawData = [] },
                    TrustedPeerCertificates = new CertificateTrustList { StorePath = "Test" },
                    TrustedIssuerCertificates = new CertificateTrustList { StorePath = "Test" },
                    UserIssuerCertificates = new CertificateTrustList { StorePath = "Test" }
                }
            ).SetName("OnlyIssuerUser");

            yield return new TestCaseData(
                new SecurityConfiguration
                {
                    ApplicationCertificate = new CertificateIdentifier { RawData = [] },
                    TrustedPeerCertificates = new CertificateTrustList { StorePath = "Test" },
                    TrustedIssuerCertificates = new CertificateTrustList { StorePath = "Test" },
                    UserIssuerCertificates = new CertificateTrustList { StorePath = string.Empty },
                    TrustedUserCertificates = new CertificateTrustList { StorePath = "Test" }
                }
            ).SetName("InvalidUserIssuer");

            yield return new TestCaseData(
                new SecurityConfiguration
                {
                    ApplicationCertificate = new CertificateIdentifier { RawData = [] },
                    TrustedPeerCertificates = new CertificateTrustList { StorePath = "Test" },
                    TrustedIssuerCertificates = new CertificateTrustList { StorePath = "Test" },
                    UserIssuerCertificates = new CertificateTrustList { StorePath = "Test" },
                    TrustedUserCertificates = new CertificateTrustList { StorePath = string.Empty }
                }
            ).SetName("InvalidUserTrusted");
        }
    }
}
