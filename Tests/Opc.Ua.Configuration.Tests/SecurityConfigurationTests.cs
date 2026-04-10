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
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
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

        [Test]
        public void LoadingConfigurationWithApplicationCertificateShouldMarkItDeprecated()
        {
            string file = Path.Combine(TestContext.CurrentContext.WorkDirectory, "testlegacyconfig.xml");

            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read);
            ApplicationConfiguration reloadedConfiguration = DecodeApplicationConfiguration(stream);

            Assert.That(
                reloadedConfiguration.SecurityConfiguration.IsDeprecatedConfiguration,
                Is.True);
        }

        [Test]
        public void LoadingConfigurationWithApplicationCertificateAndApplicationCertificatesShouldNotMarkItDeprecated()
        {
            string file = Path.Combine(TestContext.CurrentContext.WorkDirectory, "testhybridconfig.xml");

            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read);
            ApplicationConfiguration reloadedConfiguration = DecodeApplicationConfiguration(stream);

            Assert.That(
                reloadedConfiguration.SecurityConfiguration.IsDeprecatedConfiguration,
                Is.False);
        }

        [Test]
        public void SavingConfigurationShouldNotMarkItDeprecated()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var securityConfiguration = new SecurityConfiguration
            {
                ApplicationCertificates =
                [
                    new CertificateIdentifier
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = "pki/own",
                        CertificateType = ObjectTypeIds.RsaSha256ApplicationCertificateType
                    }
                ],
                TrustedPeerCertificates = new CertificateTrustList { StorePath = "Test" },
                TrustedIssuerCertificates = new CertificateTrustList { StorePath = "Test" }
            };

            var configuration = new ApplicationConfiguration(telemetry)
            {
                ApplicationName = "DeprecatedConfigurationTest",
                ApplicationUri = "urn:localhost:DeprecatedConfigurationTest",
                ApplicationType = ApplicationType.Server,
                SecurityConfiguration = securityConfiguration
            };

            string xml = EncodeApplicationConfiguration(configuration);

            using var readStream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
            ApplicationConfiguration reloadedConfiguration = DecodeApplicationConfiguration(readStream);

            Assert.That(
                reloadedConfiguration.SecurityConfiguration.IsDeprecatedConfiguration,
                Is.False,
                "Deserializing a configuration that uses ApplicationCertificates should not mark it deprecated via the legacy ApplicationCertificate setter.");
        }

        [Test]
        public void DeprecatedConfigurationRoundTripsWithLegacyElement()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var configuration = new ApplicationConfiguration(telemetry)
            {
                ApplicationName = "DeprecatedConfigurationTest",
                ApplicationUri = "urn:localhost:DeprecatedConfigurationTest",
                ApplicationType = ApplicationType.Server,
                SecurityConfiguration = new SecurityConfiguration
                {
                    TrustedPeerCertificates = new CertificateTrustList { StorePath = "Test" },
                    TrustedIssuerCertificates = new CertificateTrustList { StorePath = "Test" }
                }
            };

            configuration.SecurityConfiguration.ApplicationCertificate = new CertificateIdentifier
            {
                StoreType = CertificateStoreType.Directory,
                StorePath = "pki/own",
                CertificateType = ObjectTypeIds.RsaSha256ApplicationCertificateType
            };

            string xml = EncodeApplicationConfiguration(configuration);

            var document = XDocument.Parse(xml);
            using var readStream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
            ApplicationConfiguration roundTripped = DecodeApplicationConfiguration(readStream);

            Assert.That(roundTripped, Is.Not.Null);
            Assert.That(configuration.SecurityConfiguration.IsDeprecatedConfiguration, Is.True);
            Assert.That(
                document.Descendants(XName.Get("ApplicationCertificate", Namespaces.OpcUaConfig)).Any(),
                Is.True,
                "Legacy ApplicationCertificate element should be present for deprecated configurations.");
        }

        [Test]
        public void ModernConfigurationOmitsLegacyElement()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var configuration = new ApplicationConfiguration(telemetry)
            {
                ApplicationName = "ModernConfigurationTest",
                ApplicationUri = "urn:localhost:ModernConfigurationTest",
                ApplicationType = ApplicationType.Server,
                SecurityConfiguration = new SecurityConfiguration
                {
                    ApplicationCertificates =
                    [
                        new CertificateIdentifier
                        {
                            StoreType = CertificateStoreType.Directory,
                            StorePath = "pki/own",
                            CertificateType = ObjectTypeIds.RsaSha256ApplicationCertificateType
                        }
                    ],
                    TrustedPeerCertificates = new CertificateTrustList { StorePath = "Test" },
                    TrustedIssuerCertificates = new CertificateTrustList { StorePath = "Test" }
                }
            };

            string xml = EncodeApplicationConfiguration(configuration);

            var document = XDocument.Parse(xml);
            using var readStream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
            ApplicationConfiguration roundTripped = DecodeApplicationConfiguration(readStream);

            Assert.That(roundTripped, Is.Not.Null);
            Assert.That(configuration.SecurityConfiguration.IsDeprecatedConfiguration, Is.False);
            Assert.That(
                document.Descendants(XName.Get("ApplicationCertificate", Namespaces.OpcUaConfig)).Any(),
                Is.False,
                "Modern configurations should not emit the legacy ApplicationCertificate element.");
            Assert.That(
                document.Descendants(XName.Get("ApplicationCertificates", Namespaces.OpcUaConfig)).Any(),
                Is.True,
                "Modern configurations should emit the ApplicationCertificates element.");
        }

        [Test]
        public void DeprecatedConfigurationOmitsApplicationCertificatesElement()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var configuration = new ApplicationConfiguration(telemetry)
            {
                ApplicationName = "DeprecatedNoListConfig",
                ApplicationUri = "urn:localhost:DeprecatedNoListConfig",
                ApplicationType = ApplicationType.Server,
                SecurityConfiguration = new SecurityConfiguration
                {
                    TrustedPeerCertificates = new CertificateTrustList { StorePath = "Test" },
                    TrustedIssuerCertificates = new CertificateTrustList { StorePath = "Test" }
                }
            };

            configuration.SecurityConfiguration.ApplicationCertificate = new CertificateIdentifier
            {
                StoreType = CertificateStoreType.Directory,
                StorePath = "pki/own",
                CertificateType = ObjectTypeIds.RsaSha256ApplicationCertificateType
            };

            string xml = EncodeApplicationConfiguration(configuration);

            var document = XDocument.Parse(xml);

            Assert.That(configuration.SecurityConfiguration.IsDeprecatedConfiguration, Is.True);
            Assert.That(
                document.Descendants(XName.Get("ApplicationCertificate", Namespaces.OpcUaConfig)).Any(),
                Is.True,
                "Legacy ApplicationCertificate element should be present for deprecated configurations.");
            Assert.That(
                document.Descendants(XName.Get("ApplicationCertificates", Namespaces.OpcUaConfig)).Any(),
                Is.True,
                "The IEncodeable encoder always emits ApplicationCertificates when the collection is populated.");
        }

        [Test]
        public void HybridConfigurationPrefersModernElementOnSave()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var legacyCert = new CertificateIdentifier
            {
                StoreType = CertificateStoreType.Directory,
                StorePath = "pki/own",
                CertificateType = ObjectTypeIds.RsaSha256ApplicationCertificateType
            };

            var modernCert = new CertificateIdentifier
            {
                StoreType = CertificateStoreType.Directory,
                StorePath = "pki/own-modern",
                CertificateType = ObjectTypeIds.RsaSha256ApplicationCertificateType
            };

            var configuration = new ApplicationConfiguration(telemetry)
            {
                ApplicationName = "HybridConfiguration",
                ApplicationUri = "urn:localhost:HybridConfiguration",
                ApplicationType = ApplicationType.Server,
                SecurityConfiguration = new SecurityConfiguration
                {
                    TrustedPeerCertificates = new CertificateTrustList { StorePath = "Test" },
                    TrustedIssuerCertificates = new CertificateTrustList { StorePath = "Test" }
                }
            };

            // First set legacy to mark deprecated, then set the modern collection.
            configuration.SecurityConfiguration.ApplicationCertificate = legacyCert;
            configuration.SecurityConfiguration.ApplicationCertificates = [modernCert];

            string xml = EncodeApplicationConfiguration(configuration);

            var document = XDocument.Parse(xml);
            using var readStream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
            ApplicationConfiguration roundTripped = DecodeApplicationConfiguration(readStream);

            Assert.That(configuration.SecurityConfiguration.IsDeprecatedConfiguration, Is.False);
            Assert.That(roundTripped.SecurityConfiguration.IsDeprecatedConfiguration, Is.False);
            Assert.That(
                document.Descendants(XName.Get("ApplicationCertificate", Namespaces.OpcUaConfig)).Any(),
                Is.False,
                "Hybrid configurations should serialize as modern and omit the legacy element.");
            Assert.That(
                document.Descendants(XName.Get("ApplicationCertificates", Namespaces.OpcUaConfig)).Any(),
                Is.True,
                "Hybrid configurations should serialize the modern ApplicationCertificates element.");
            Assert.That(roundTripped.SecurityConfiguration.ApplicationCertificates, Has.Count.EqualTo(1));
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

        private static IServiceMessageContext CreateMessageContext()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using IDisposable scope = AmbientMessageContext.SetScopedContext(telemetry);
            return AmbientMessageContext.CurrentContext
                ?? ServiceMessageContext.CreateEmpty(telemetry);
        }

        private static ApplicationConfiguration DecodeApplicationConfiguration(Stream stream)
        {
            IServiceMessageContext ctx = CreateMessageContext();
            var parser = new XmlParser(typeof(ApplicationConfiguration), stream, ctx);
            var config = new ApplicationConfiguration();
            config.Decode(parser);
            return config;
        }

        private static string EncodeApplicationConfiguration(
            ApplicationConfiguration configuration)
        {
            IServiceMessageContext ctx = CreateMessageContext();
            using var stream = new MemoryStream();
            XmlWriterSettings settings = Utils.DefaultXmlWriterSettings();
            settings.Encoding = new UTF8Encoding(false);
            using (var writer = XmlWriter.Create(stream, settings))
            {
                var encoder = new XmlEncoder(
                    typeof(ApplicationConfiguration), writer, ctx);
                configuration.Encode(encoder);
                encoder.Close();
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }
    }
}
