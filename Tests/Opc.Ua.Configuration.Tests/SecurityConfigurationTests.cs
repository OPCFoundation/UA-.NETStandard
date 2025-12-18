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
using System.Configuration;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
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
        public async Task LoadingConfigurationWithApplicationCertificateShouldMarkItDeprecated()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var file = Path.Combine(TestContext.CurrentContext.WorkDirectory, "testlegacyconfig.xml");

            var serializer = new DataContractSerializer(typeof(ApplicationConfiguration));
            using var stream = new FileStream(file, FileMode.Open);
            var reloadedConfiguration =
                (ApplicationConfiguration)serializer.ReadObject(stream);

            Assert.That(
                reloadedConfiguration.SecurityConfiguration.IsDeprecatedConfiguration,
                Is.True);
        }

        [Test]
        public async Task LoadingConfigurationWithApplicationCertificateAndApplicationCertificatesShouldNotMarkItDeprecated()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var file = Path.Combine(TestContext.CurrentContext.WorkDirectory, "testhybridconfig.xml");

            var serializer = new DataContractSerializer(typeof(ApplicationConfiguration));
            using var stream = new FileStream(file, FileMode.Open);
            var reloadedConfiguration =
                (ApplicationConfiguration)serializer.ReadObject(stream);

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
                ApplicationCertificates = new CertificateIdentifierCollection
                {
                    new CertificateIdentifier
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = "pki/own",
                        CertificateType = ObjectTypeIds.RsaSha256ApplicationCertificateType
                    }
                },
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

            var serializer = new DataContractSerializer(typeof(ApplicationConfiguration));
            using var stream = new MemoryStream();
            serializer.WriteObject(stream, configuration);
            stream.Position = 0;

            var reloadedConfiguration =
                (ApplicationConfiguration)serializer.ReadObject(stream);

            Assert.That(
                reloadedConfiguration.SecurityConfiguration.IsDeprecatedConfiguration,
                Is.False,
                "Deserializing a configuration that uses ApplicationCertificates should not mark it deprecated via the legacy ApplicationCertificate setter.");
        }

        [Test]
        public void DeprecatedConfigurationRoundTripsWithLegacyElement()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var serializer = new DataContractSerializer(typeof(ApplicationConfiguration));

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

            string xml;
            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, configuration);
                xml = Encoding.UTF8.GetString(stream.ToArray());
            }

            var document = XDocument.Parse(xml);
            var roundTripped = (ApplicationConfiguration)serializer.ReadObject(
                new MemoryStream(Encoding.UTF8.GetBytes(xml)));

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
            var serializer = new DataContractSerializer(typeof(ApplicationConfiguration));

            var configuration = new ApplicationConfiguration(telemetry)
            {
                ApplicationName = "ModernConfigurationTest",
                ApplicationUri = "urn:localhost:ModernConfigurationTest",
                ApplicationType = ApplicationType.Server,
                SecurityConfiguration = new SecurityConfiguration
                {
                    ApplicationCertificates = new CertificateIdentifierCollection
                    {
                        new CertificateIdentifier
                        {
                            StoreType = CertificateStoreType.Directory,
                            StorePath = "pki/own",
                            CertificateType = ObjectTypeIds.RsaSha256ApplicationCertificateType
                        }
                    },
                    TrustedPeerCertificates = new CertificateTrustList { StorePath = "Test" },
                    TrustedIssuerCertificates = new CertificateTrustList { StorePath = "Test" }
                }
            };

            string xml;
            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, configuration);
                xml = Encoding.UTF8.GetString(stream.ToArray());
            }

            var document = XDocument.Parse(xml);
            var roundTripped = (ApplicationConfiguration)serializer.ReadObject(
                new MemoryStream(Encoding.UTF8.GetBytes(xml)));

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
            var serializer = new DataContractSerializer(typeof(ApplicationConfiguration));

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

            string xml;
            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, configuration);
                xml = Encoding.UTF8.GetString(stream.ToArray());
            }

            var document = XDocument.Parse(xml);

            Assert.That(configuration.SecurityConfiguration.IsDeprecatedConfiguration, Is.True);
            Assert.That(
                document.Descendants(XName.Get("ApplicationCertificate", Namespaces.OpcUaConfig)).Any(),
                Is.True,
                "Legacy ApplicationCertificate element should be present for deprecated configurations.");
            Assert.That(
                document.Descendants(XName.Get("ApplicationCertificates", Namespaces.OpcUaConfig)).Any(),
                Is.False,
                "Deprecated configurations should not emit the ApplicationCertificates element.");
        }

        [Test]
        public void HybridConfigurationPrefersModernElementOnSave()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var serializer = new DataContractSerializer(typeof(ApplicationConfiguration));

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
            configuration.SecurityConfiguration.ApplicationCertificates =
                new CertificateIdentifierCollection { modernCert };

            string xml;
            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, configuration);
                xml = Encoding.UTF8.GetString(stream.ToArray());
            }

            var document = XDocument.Parse(xml);
            var roundTripped = (ApplicationConfiguration)serializer.ReadObject(
                new MemoryStream(Encoding.UTF8.GetBytes(xml)));

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
            Assert.That(roundTripped.SecurityConfiguration.ApplicationCertificates.Count, Is.EqualTo(1));
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
