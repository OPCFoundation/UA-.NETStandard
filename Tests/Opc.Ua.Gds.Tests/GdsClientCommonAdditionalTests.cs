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
using NUnit.Framework;
using Opc.Ua.Gds.Client;

namespace Opc.Ua.Gds.Tests
{
    [TestFixture]
    [Category("GDS")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class GdsClientCommonAdditionalAdminCredentialsTests
    {
        [Test]
        public void ConstructorCreatesInstance()
        {
            var args = new AdminCredentialsRequiredEventArgs();
            Assert.That(args, Is.Not.Null);
        }

        [Test]
        public void InheritsFromEventArgs()
        {
            var args = new AdminCredentialsRequiredEventArgs();
            Assert.That(args, Is.InstanceOf<EventArgs>());
        }

        [Test]
        public void CredentialsDefaultsToNull()
        {
            var args = new AdminCredentialsRequiredEventArgs();
            Assert.That(args.Credentials, Is.Null);
        }

        [Test]
        public void CredentialsRoundTrip()
        {
            var args = new AdminCredentialsRequiredEventArgs();
            using var identity = new UserIdentity();
            args.Credentials = identity;
            Assert.That(args.Credentials, Is.SameAs(identity));
        }

        [Test]
        public void CacheCredentialsDefaultsToFalse()
        {
            var args = new AdminCredentialsRequiredEventArgs();
            Assert.That(args.CacheCredentials, Is.False);
        }

        [Test]
        public void CacheCredentialsRoundTrip()
        {
            var args = new AdminCredentialsRequiredEventArgs();
            args.CacheCredentials = true;
            Assert.That(args.CacheCredentials, Is.True);
        }
    }

    [TestFixture]
    [Category("GDS")]
    [Parallelizable]
    public class GdsClientCommonAdditionalConfigurationTests
    {
        [Test]
        public void ConstructorCreatesInstance()
        {
            var config = new GlobalDiscoveryClientConfiguration();
            Assert.That(config, Is.Not.Null);
        }

        [Test]
        public void GlobalDiscoveryServerUrlDefaultsToNull()
        {
            var config = new GlobalDiscoveryClientConfiguration();
            Assert.That(config.GlobalDiscoveryServerUrl, Is.Null);
        }

        [Test]
        public void GlobalDiscoveryServerUrlRoundTrip()
        {
            var config = new GlobalDiscoveryClientConfiguration
            {
                GlobalDiscoveryServerUrl = "opc.tcp://gds.example.com:4840"
            };
            Assert.That(config.GlobalDiscoveryServerUrl, Is.EqualTo("opc.tcp://gds.example.com:4840"));
        }

        [Test]
        public void ExternalEditorDefaultsToNull()
        {
            var config = new GlobalDiscoveryClientConfiguration();
            Assert.That(config.ExternalEditor, Is.Null);
        }

        [Test]
        public void ExternalEditorRoundTrip()
        {
            var config = new GlobalDiscoveryClientConfiguration
            {
                ExternalEditor = "notepad.exe"
            };
            Assert.That(config.ExternalEditor, Is.EqualTo("notepad.exe"));
        }
    }

    [TestFixture]
    [Category("GDS")]
    [Parallelizable]
    public class GdsClientCommonAdditionalLocalDiscoveryTests
    {
        private static readonly string[] s_frenchGermanLocales = ["fr-FR", "de-DE"];
        [Test]
        public void ConstructorSetsApplicationConfiguration()
        {
            var appConfig = new ApplicationConfiguration();
            var client = new LocalDiscoveryServerClient(appConfig);
            Assert.That(client.ApplicationConfiguration, Is.SameAs(appConfig));
        }

        [Test]
        public void ConstructorSetsDefaultDiagnosticsMasksToNone()
        {
            var appConfig = new ApplicationConfiguration();
            var client = new LocalDiscoveryServerClient(appConfig);
            Assert.That(client.DiagnosticsMasks, Is.EqualTo(DiagnosticsMasks.None));
        }

        [Test]
        public void ConstructorWithCustomDiagnosticsMasks()
        {
            var appConfig = new ApplicationConfiguration();
            var client = new LocalDiscoveryServerClient(appConfig, DiagnosticsMasks.All);
            Assert.That(client.DiagnosticsMasks, Is.EqualTo(DiagnosticsMasks.All));
        }

        [Test]
        public void ConstructorCreatesMessageContext()
        {
            var appConfig = new ApplicationConfiguration();
            var client = new LocalDiscoveryServerClient(appConfig);
            Assert.That(client.MessageContext, Is.Not.Null);
        }

        [Test]
        public void ConstructorSetsPreferredLocalesIncludingEnUs()
        {
            var appConfig = new ApplicationConfiguration();
            var client = new LocalDiscoveryServerClient(appConfig);
            Assert.That(client.PreferredLocales.IsEmpty, Is.False);
            Assert.That(client.PreferredLocales.ToList(), Does.Contain("en-US"));
        }

        [Test]
        public void PreferredLocalesContainsCurrentCulture()
        {
            var appConfig = new ApplicationConfiguration();
            var client = new LocalDiscoveryServerClient(appConfig);
            string currentUiCulture = System.Globalization.CultureInfo.CurrentUICulture.Name;
            Assert.That(client.PreferredLocales.ToList(), Does.Contain(currentUiCulture));
        }

        [Test]
        public void DefaultOperationTimeoutDefaultsToZero()
        {
            var appConfig = new ApplicationConfiguration();
            var client = new LocalDiscoveryServerClient(appConfig);
            Assert.That(client.DefaultOperationTimeout, Is.EqualTo(0));
        }

        [Test]
        public void DefaultOperationTimeoutRoundTrip()
        {
            var appConfig = new ApplicationConfiguration();
            var client = new LocalDiscoveryServerClient(appConfig);
            client.DefaultOperationTimeout = 30000;
            Assert.That(client.DefaultOperationTimeout, Is.EqualTo(30000));
        }

        [Test]
        public void PreferredLocalesCanBeReplaced()
        {
            var appConfig = new ApplicationConfiguration();
            var client = new LocalDiscoveryServerClient(appConfig);
            var newLocales = new ArrayOf<string>(s_frenchGermanLocales);
            client.PreferredLocales = newLocales;
            Assert.That(client.PreferredLocales.ToList(), Does.Contain("fr-FR"));
            Assert.That(client.PreferredLocales.ToList(), Does.Contain("de-DE"));
        }
    }

    [TestFixture]
    [Category("GDS")]
    [Parallelizable]
    public class GdsClientCommonAdditionalRegisteredAppTests
    {
        private static readonly string[] s_pemOnlyFormats = ["PEM"];
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
                CertificatePrivateKeyPath = ""
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
                DiscoveryUrl = new[] { "opc.tcp://localhost:4840" }
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
                DiscoveryUrl = new[] { "not-a-url", "also not valid" }
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
                DiscoveryUrl = Array.Empty<string>()
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
                ServerCapability = new[] { "DA", "HD" },
                DiscoveryUrl = new[] { "opc.tcp://server:4840" }
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
                DiscoveryUrl = new[] {
                    "not-valid",
                    "opc.tcp://server1.example.com:4840",
                    "also-not-valid",
                    "opc.tcp://server2.example.com:4841"
                }
            };
            List<string> result = app.GetDomainNames(null);
            Assert.That(result, Has.Count.EqualTo(2));
            Assert.That(result, Does.Contain("server1.example.com"));
            Assert.That(result, Does.Contain("server2.example.com"));
        }
    }

    [TestFixture]
    [Category("GDS")]
    [Parallelizable]
    public class GdsClientCommonAdditionalCertificateWrapperTests
    {
        private static readonly string[] s_localhostDomains = ["localhost"];
        [Test]
        public void CertificatePropertyDefaultsToNull()
        {
            var wrapper = new CertificateWrapper();
            Assert.That(wrapper.Certificate, Is.Null);
        }

        [Test]
        public void CertificatePropertyRoundTrip()
        {
            using var cert = CertificateFactory.CreateCertificate(
                "urn:test:roundtrip",
                "RoundTrip",
                "CN=RoundTrip",
                new ArrayOf<string>(s_localhostDomains))
                .CreateForRSA();

            var wrapper = new CertificateWrapper { Certificate = cert };
            Assert.That(wrapper.Certificate, Is.SameAs(cert));
        }

        [Test]
        public void ToStringWithNullFormatReturnsSubjectName()
        {
            using var cert = CertificateFactory.CreateCertificate(
                "urn:test:tostring",
                "ToStringTest",
                "CN=ToStringTest",
                new ArrayOf<string>(s_localhostDomains))
                .CreateForRSA();

            var wrapper = new CertificateWrapper { Certificate = cert };
            string result = wrapper.ToString(null, null);
            Assert.That(result, Is.EqualTo(cert.Subject));
        }
    }
}
