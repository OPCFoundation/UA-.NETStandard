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
using Opc.Ua.Gds.Client;
using Opc.Ua.Security.Certificates;

#pragma warning disable CS0618 // Tests exercise obsolete methods intentionally

namespace Opc.Ua.Gds.Tests
{
    [TestFixture]
    [Category("GDS")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class RegisteredApplicationTests
    {
        private static readonly string[] s_pfxPemFormats = ["PFX", "PEM"];
        private static readonly string[] s_pemOnlyFormats = ["PEM"];

        [Test]
        public void GetHttpsDomainNameReturnsHostFromDiscoveryUrl()
        {
            var app = new RegisteredApplication
            {
                DiscoveryUrl = ["opc.tcp://myserver.example.com:4840"]
            };
            string result = app.GetHttpsDomainName();
            Assert.That(result, Is.EqualTo("myserver.example.com"));
        }

        [Test]
        public void GetHttpsDomainNameReplacesLocalhostWithHostName()
        {
            var app = new RegisteredApplication
            {
                DiscoveryUrl = ["opc.tcp://localhost:4840"]
            };
            string result = app.GetHttpsDomainName();
            Assert.That(result, Is.EqualTo(Utils.GetHostName()));
        }

        [Test]
        public void GetHttpsDomainNameReturnsNullWhenNoDiscoveryUrls()
        {
            var app = new RegisteredApplication();
            string result = app.GetHttpsDomainName();
            Assert.That(result, Is.Null);
        }

        [Test]
        public void GetHttpsDomainNameSkipsInvalidUrls()
        {
            var app = new RegisteredApplication
            {
                DiscoveryUrl = ["not a url", "opc.tcp://valid.example.com:4840"]
            };
            string result = app.GetHttpsDomainName();
            Assert.That(result, Is.EqualTo("valid.example.com"));
        }

        [Test]
        public void GetPrivateKeyFormatReturnsPfxByDefault()
        {
            var app = new RegisteredApplication
            {
                RegistrationType = RegistrationType.ClientPull
            };
            string result = app.GetPrivateKeyFormat();
            Assert.That(result, Is.EqualTo("PFX"));
        }

        [Test]
        public void GetPrivateKeyFormatReturnsPemWhenPathEndsPem()
        {
            var app = new RegisteredApplication
            {
                RegistrationType = RegistrationType.ClientPull,
                CertificatePrivateKeyPath = "/certs/key.PEM"
            };
            string result = app.GetPrivateKeyFormat();
            Assert.That(result, Is.EqualTo("PEM"));
        }

        [Test]
        public void GetPrivateKeyFormatServerPushReturnsPemWhenNoPfxSupport()
        {
            var app = new RegisteredApplication
            {
                RegistrationType = RegistrationType.ServerPush
            };
            string result = app.GetPrivateKeyFormat(null);
            Assert.That(result, Is.EqualTo("PEM"));
        }

        [Test]
        public void GetPrivateKeyFormatServerPushReturnsPfxWhenPfxSupported()
        {
            var app = new RegisteredApplication
            {
                RegistrationType = RegistrationType.ServerPush
            };
            string result = app.GetPrivateKeyFormat(s_pfxPemFormats);
            Assert.That(result, Is.EqualTo("PFX"));
        }

        [Test]
        public void GetDomainNamesReturnsParsedDomains()
        {
            var app = new RegisteredApplication
            {
                Domains = "host1.com, host2.com, host3.com"
            };
            List<string> result = app.GetDomainNames(null);
            Assert.That(result, Has.Count.EqualTo(3));
            Assert.That(result, Does.Contain("host1.com"));
            Assert.That(result, Does.Contain("host2.com"));
            Assert.That(result, Does.Contain("host3.com"));
        }

        [Test]
        public void GetDomainNamesFromDiscoveryUrls()
        {
            var app = new RegisteredApplication
            {
                DiscoveryUrl = [
                    "opc.tcp://server1.example.com:4840",
                    "opc.tcp://server2.example.com:4840"
                ]
            };
            List<string> result = app.GetDomainNames(null);
            Assert.That(result, Has.Count.EqualTo(2));
            Assert.That(result, Does.Contain("server1.example.com"));
            Assert.That(result, Does.Contain("server2.example.com"));
        }

        [Test]
        public void GetDomainNamesDeduplicatesUrls()
        {
            var app = new RegisteredApplication
            {
                DiscoveryUrl = [
                    "opc.tcp://server.example.com:4840",
                    "opc.tcp://server.example.com:4841"
                ]
            };
            List<string> result = app.GetDomainNames(null);
            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0], Is.EqualTo("server.example.com"));
        }

        [Test]
        public void GetDomainNamesFallsBackToHostName()
        {
            var app = new RegisteredApplication();
            List<string> result = app.GetDomainNames(null);
            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0], Is.EqualTo(Utils.GetHostName()));
        }

        private static readonly string[] s_testHostDomains = ["testhost.example.com"];

        [Test]
        public void GetDomainNamesFromCertificate()
        {
            using Certificate cert = CertificateFactory.CreateCertificate(
                "urn:test:app",
                "TestApp",
                "CN=TestApp,DC=testdomain,DC=com",
                new ArrayOf<string>(s_testHostDomains))
                .CreateForRSA();

            var app = new RegisteredApplication();
            List<string> result = app.GetDomainNames(cert);
            Assert.That(result, Is.Not.Empty);
        }

        [Test]
        public void GetDomainNamesEmptyDomainsStringFallsThrough()
        {
            var app = new RegisteredApplication
            {
                Domains = "  ,  ,  ",
                DiscoveryUrl = ["opc.tcp://fallback.example.com:4840"]
            };
            List<string> result = app.GetDomainNames(null);
            Assert.That(result, Does.Contain("fallback.example.com"));
        }

        [Test]
        public void ApplicationIdDefaultsToNull()
        {
            var app = new RegisteredApplication();
            Assert.That(app.ApplicationId, Is.Null);
        }

        [Test]
        public void ApplicationIdRoundTrip()
        {
            var app = new RegisteredApplication
            {
                ApplicationId = "test-app-id-123"
            };
            Assert.That(app.ApplicationId, Is.EqualTo("test-app-id-123"));
        }

        [Test]
        public void GetPrivateKeyFormatServerPushReturnsPemWhenFormatsExcludePfx()
        {
            var app = new RegisteredApplication
            {
                RegistrationType = RegistrationType.ServerPush
            };
            string result = app.GetPrivateKeyFormat(s_pemOnlyFormats);
            Assert.That(result, Is.EqualTo("PEM"));
        }

        [Test]
        public void GetPrivateKeyFormatClientPullWithEmptyPathReturnsPfx()
        {
            var app = new RegisteredApplication
            {
                RegistrationType = RegistrationType.ClientPull,
                CertificatePrivateKeyPath = string.Empty
            };
            string result = app.GetPrivateKeyFormat();
            Assert.That(result, Is.EqualTo("PFX"));
        }

        [Test]
        public void GetPrivateKeyFormatClientPullWithNonPemPathReturnsPfx()
        {
            var app = new RegisteredApplication
            {
                RegistrationType = RegistrationType.ClientPull,
                CertificatePrivateKeyPath = "/certs/key.pfx"
            };
            string result = app.GetPrivateKeyFormat();
            Assert.That(result, Is.EqualTo("PFX"));
        }

        [Test]
        public void GetDomainNamesReplacesLocalhostInDiscoveryUrl()
        {
            var app = new RegisteredApplication
            {
                DiscoveryUrl = ["opc.tcp://localhost:4840"]
            };
            List<string> result = app.GetDomainNames(null);
            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0], Is.EqualTo(Utils.GetHostName()));
        }

        [Test]
        public void GetDomainNamesSkipsInvalidDiscoveryUrls()
        {
            var app = new RegisteredApplication
            {
                DiscoveryUrl = ["not-a-url", "also not valid"]
            };
            List<string> result = app.GetDomainNames(null);
            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0], Is.EqualTo(Utils.GetHostName()));
        }

        [Test]
        public void GetHttpsDomainNameReturnsNullForEmptyDiscoveryUrlArray()
        {
            var app = new RegisteredApplication
            {
                DiscoveryUrl = []
            };
            string result = app.GetHttpsDomainName();
            Assert.That(result, Is.Null);
        }

        [Test]
        public void RegisteredApplicationPropertiesRoundTrip()
        {
            var app = new RegisteredApplication
            {
                ApplicationUri = "urn:test:app",
                ApplicationName = "TestApp",
                ProductUri = "urn:test:product",
                ConfigurationFile = "/path/to/config.xml",
                ServerUrl = "opc.tcp://server:4840",
                CertificateStorePath = "/certs/store",
                CertificateSubjectName = "CN=Test",
                CertificatePublicKeyPath = "/certs/public.der",
                CertificatePrivateKeyPath = "/certs/private.pfx",
                TrustListStorePath = "/trust",
                IssuerListStorePath = "/issuers",
                HttpsCertificatePublicKeyPath = "/https/public.der",
                HttpsCertificatePrivateKeyPath = "/https/private.pfx",
                HttpsTrustListStorePath = "/https/trust",
                HttpsIssuerListStorePath = "/https/issuers",
                CertificateRequestId = "req-123",
                Domains = "host1.com,host2.com",
                RegistrationType = RegistrationType.ServerPull,
                ServerCapability = ["DA", "HD"],
                DiscoveryUrl = ["opc.tcp://server:4840"]
            };

            Assert.That(app.ApplicationUri, Is.EqualTo("urn:test:app"));
            Assert.That(app.ApplicationName, Is.EqualTo("TestApp"));
            Assert.That(app.ProductUri, Is.EqualTo("urn:test:product"));
            Assert.That(app.ConfigurationFile, Is.EqualTo("/path/to/config.xml"));
            Assert.That(app.ServerUrl, Is.EqualTo("opc.tcp://server:4840"));
            Assert.That(app.CertificateStorePath, Is.EqualTo("/certs/store"));
            Assert.That(app.CertificateSubjectName, Is.EqualTo("CN=Test"));
            Assert.That(app.CertificatePublicKeyPath, Is.EqualTo("/certs/public.der"));
            Assert.That(app.CertificatePrivateKeyPath, Is.EqualTo("/certs/private.pfx"));
            Assert.That(app.TrustListStorePath, Is.EqualTo("/trust"));
            Assert.That(app.IssuerListStorePath, Is.EqualTo("/issuers"));
            Assert.That(app.HttpsCertificatePublicKeyPath, Is.EqualTo("/https/public.der"));
            Assert.That(app.HttpsCertificatePrivateKeyPath, Is.EqualTo("/https/private.pfx"));
            Assert.That(app.HttpsTrustListStorePath, Is.EqualTo("/https/trust"));
            Assert.That(app.HttpsIssuerListStorePath, Is.EqualTo("/https/issuers"));
            Assert.That(app.CertificateRequestId, Is.EqualTo("req-123"));
            Assert.That(app.Domains, Is.EqualTo("host1.com,host2.com"));
            Assert.That(app.RegistrationType, Is.EqualTo(RegistrationType.ServerPull));
            Assert.That(app.ServerCapability, Has.Length.EqualTo(2));
            Assert.That(app.DiscoveryUrl, Has.Length.EqualTo(1));
        }

        [Test]
        public void GetDomainNamesMixOfValidAndInvalidDiscoveryUrls()
        {
            var app = new RegisteredApplication
            {
                DiscoveryUrl = [
                    "not-valid",
                    "opc.tcp://server1.example.com:4840",
                    "also-not-valid",
                    "opc.tcp://server2.example.com:4841"
                ]
            };
            List<string> result = app.GetDomainNames(null);
            Assert.That(result, Has.Count.EqualTo(2));
            Assert.That(result, Does.Contain("server1.example.com"));
            Assert.That(result, Does.Contain("server2.example.com"));
        }
    }
}
