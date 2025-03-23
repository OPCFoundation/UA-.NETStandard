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
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Configuration.Tests
{
    /// <summary>
    /// Tests for the security configuration class.
    /// </summary>
    [TestFixture, Category("SecurityConfiguration")]
    [SetCulture("en-us")]
    public class SecurityConfigurationTests
    {
        [Test]
        public void ValidConfgurationPasses()
        {
            var configuration = new SecurityConfiguration {
                ApplicationCertificate = new CertificateIdentifier { RawData = Array.Empty<byte>() },
                TrustedPeerCertificates = new CertificateTrustList { StorePath = "Test" },
                TrustedIssuerCertificates = new CertificateTrustList { StorePath = "Test" },
            };

            configuration.Validate();
        }

        [TestCaseSource(nameof(GetInvalidConfigurations))]
        public void InvalidConfigurationThrows(SecurityConfiguration configuration)
        {
            Assert.Throws<ServiceResultException>(() => configuration.Validate());
        }

        private static IEnumerable<TestCaseData> GetInvalidConfigurations()
        {
            yield return new TestCaseData(new SecurityConfiguration {
                ApplicationCertificate = new CertificateIdentifier { RawData = Array.Empty<byte>() },
            }).SetName("NoStores");

            yield return new TestCaseData(new SecurityConfiguration {
                ApplicationCertificate = new CertificateIdentifier { RawData = Array.Empty<byte>() },
                TrustedPeerCertificates = new CertificateTrustList { StorePath = "" },
                TrustedIssuerCertificates = new CertificateTrustList { StorePath = "Test" },
            }).SetName("InvalidTrustedStore");

            yield return new TestCaseData(new SecurityConfiguration {
                ApplicationCertificate = new CertificateIdentifier { RawData = Array.Empty<byte>() },
                TrustedPeerCertificates = new CertificateTrustList { StorePath = "Test" },
                TrustedIssuerCertificates = new CertificateTrustList { StorePath = "" },
            }).SetName("InvalidIssuerStore");

            yield return new TestCaseData(new SecurityConfiguration {
                ApplicationCertificate = new CertificateIdentifier { RawData = Array.Empty<byte>() },
                TrustedPeerCertificates = new CertificateTrustList { StorePath = "Test" },
                TrustedIssuerCertificates = new CertificateTrustList { StorePath = "Test" },
                TrustedHttpsCertificates = new CertificateTrustList { StorePath = "Test" },
            }).SetName("OnlyTrustedHttps");

            yield return new TestCaseData(new SecurityConfiguration {
                ApplicationCertificate = new CertificateIdentifier { RawData = Array.Empty<byte>() },
                TrustedPeerCertificates = new CertificateTrustList { StorePath = "Test" },
                TrustedIssuerCertificates = new CertificateTrustList { StorePath = "Test" },
                HttpsIssuerCertificates = new CertificateTrustList { StorePath = "Test" },
            }).SetName("OnlyIssuerHttps");

            yield return new TestCaseData(new SecurityConfiguration {
                ApplicationCertificate = new CertificateIdentifier { RawData = Array.Empty<byte>() },
                TrustedPeerCertificates = new CertificateTrustList { StorePath = "Test" },
                TrustedIssuerCertificates = new CertificateTrustList { StorePath = "Test" },
                HttpsIssuerCertificates = new CertificateTrustList { StorePath = "" },
                TrustedHttpsCertificates = new CertificateTrustList { StorePath = "Test" },
            }).SetName("InvalidHttpsIssuer");

            yield return new TestCaseData(new SecurityConfiguration {
                ApplicationCertificate = new CertificateIdentifier { RawData = Array.Empty<byte>() },
                TrustedPeerCertificates = new CertificateTrustList { StorePath = "Test" },
                TrustedIssuerCertificates = new CertificateTrustList { StorePath = "Test" },
                HttpsIssuerCertificates = new CertificateTrustList { StorePath = "Test" },
                TrustedHttpsCertificates = new CertificateTrustList { StorePath = "" },
            }).SetName("InvalidHttpsTrusted");

            yield return new TestCaseData(new SecurityConfiguration {
                ApplicationCertificate = new CertificateIdentifier { RawData = Array.Empty<byte>() },
                TrustedPeerCertificates = new CertificateTrustList { StorePath = "Test" },
                TrustedIssuerCertificates = new CertificateTrustList { StorePath = "Test" },
                TrustedUserCertificates = new CertificateTrustList { StorePath = "Test" },
            }).SetName("OnlyTrustedUser");

            yield return new TestCaseData(new SecurityConfiguration {
                ApplicationCertificate = new CertificateIdentifier { RawData = Array.Empty<byte>() },
                TrustedPeerCertificates = new CertificateTrustList { StorePath = "Test" },
                TrustedIssuerCertificates = new CertificateTrustList { StorePath = "Test" },
                UserIssuerCertificates = new CertificateTrustList { StorePath = "Test" },
            }).SetName("OnlyIssuerUser");

            yield return new TestCaseData(new SecurityConfiguration {
                ApplicationCertificate = new CertificateIdentifier { RawData = Array.Empty<byte>() },
                TrustedPeerCertificates = new CertificateTrustList { StorePath = "Test" },
                TrustedIssuerCertificates = new CertificateTrustList { StorePath = "Test" },
                UserIssuerCertificates = new CertificateTrustList { StorePath = "" },
                TrustedUserCertificates = new CertificateTrustList { StorePath = "Test" },
            }).SetName("InvalidUserIssuer");

            yield return new TestCaseData(new SecurityConfiguration {
                ApplicationCertificate = new CertificateIdentifier { RawData = Array.Empty<byte>() },
                TrustedPeerCertificates = new CertificateTrustList { StorePath = "Test" },
                TrustedIssuerCertificates = new CertificateTrustList { StorePath = "Test" },
                UserIssuerCertificates = new CertificateTrustList { StorePath = "Test" },
                TrustedUserCertificates = new CertificateTrustList { StorePath = "" },
            }).SetName("InvalidUserTrusted");
        }
    }
}
