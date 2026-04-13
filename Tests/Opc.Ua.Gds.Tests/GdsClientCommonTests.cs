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
using System.Security.Cryptography.X509Certificates;
using System.Text;
using NUnit.Framework;
using Opc.Ua.Gds.Client;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Gds.Tests
{
    [TestFixture]
    [Category("GDS")]
    [Parallelizable]
    public class ServerCapabilitiesTests
    {
        private static ServerCapabilities CreateTestCapabilities()
        {
            string csv = "DA,Live Data\nAC,Alarms and Conditions\nHD,Historical Data\n";
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
            var capabilities = new ServerCapabilities();
            capabilities.Load(stream);
            return capabilities;
        }

        [Test]
        public void ConstructorCreatesInstance()
        {
            var capabilities = new ServerCapabilities();
            Assert.That(capabilities, Is.Not.Null);
        }

        [Test]
        public void FindReturnsCapabilityById()
        {
            ServerCapabilities capabilities = CreateTestCapabilities();
            ServerCapability result = capabilities.Find("DA");
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Id, Is.EqualTo("DA"));
            Assert.That(result.Description, Is.EqualTo("Live Data"));
        }

        [Test]
        public void FindReturnsNullForUnknownId()
        {
            ServerCapabilities capabilities = CreateTestCapabilities();
            ServerCapability result = capabilities.Find("UNKNOWN_XYZ");
            Assert.That(result, Is.Null);
        }

        [Test]
        public void FindReturnsNullForNullId()
        {
            ServerCapabilities capabilities = CreateTestCapabilities();
            ServerCapability result = capabilities.Find(null);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void GetEnumeratorEnumeratesCapabilities()
        {
            ServerCapabilities capabilities = CreateTestCapabilities();
            var list = new List<ServerCapability>();

            foreach (ServerCapability cap in capabilities)
            {
                list.Add(cap);
            }

            Assert.That(list.Count, Is.EqualTo(3));
            Assert.That(list.All(c => !string.IsNullOrEmpty(c.Id)), Is.True);
        }

        [Test]
        public void LoadFromCustomStream()
        {
            string csv = "TEST,Test Capability\nFOO,Foo Capability\n";
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
            var capabilities = new ServerCapabilities();
            capabilities.Load(stream);

            Assert.That(capabilities.Count(), Is.EqualTo(2));
            ServerCapability test = capabilities.Find("TEST");
            Assert.That(test, Is.Not.Null);
            Assert.That(test.Description, Is.EqualTo("Test Capability"));
        }

        [Test]
        public void LoadFromEmptyStreamProducesEmptyList()
        {
            using var stream = new MemoryStream(Array.Empty<byte>());
            var capabilities = new ServerCapabilities();
            capabilities.Load(stream);

            Assert.That(capabilities.Count(), Is.EqualTo(0));
        }

        [Test]
        public void LoadSkipsLinesWithoutComma()
        {
            string csv = "GOOD,Good Description\nBADLINE\nALSO_GOOD,Also Good\n";
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
            var capabilities = new ServerCapabilities();
            capabilities.Load(stream);

            Assert.That(capabilities.Count(), Is.EqualTo(2));
            Assert.That(capabilities.Find("BADLINE"), Is.Null);
        }
    }

    [TestFixture]
    [Category("GDS")]
    [Parallelizable]
    public class ServerCapabilityTests
    {
        [Test]
        public void ToStringReturnsFormattedString()
        {
            var capability = new ServerCapability { Id = "DA", Description = "Live Data" };
            string result = capability.ToString();
            Assert.That(result, Is.EqualTo("[DA] Live Data"));
        }

        [Test]
        public void ToStringWithNullFormatReturnsFormattedString()
        {
            var capability = new ServerCapability { Id = "AC", Description = "Alarms" };
            string result = capability.ToString(null, null);
            Assert.That(result, Is.EqualTo("[AC] Alarms"));
        }

        [Test]
        public void ToStringWithNonNullFormatThrowsFormatException()
        {
            var capability = new ServerCapability { Id = "DA", Description = "Live Data" };
            Assert.Throws<FormatException>(() => capability.ToString("G", null));
        }

        [TestCase(ServerCapability.LiveData, "DA")]
        [TestCase(ServerCapability.NoInformation, "NA")]
        [TestCase(ServerCapability.AlarmsAndConditions, "AC")]
        [TestCase(ServerCapability.HistoricalData, "HD")]
        [TestCase(ServerCapability.HistoricalEvents, "HE")]
        [TestCase(ServerCapability.GlobalDiscoveryServer, "GDS")]
        [TestCase(ServerCapability.LocalDiscoveryServer, "LDS")]
        [TestCase(ServerCapability.DI, "DI")]
        public void ConstantsHaveExpectedValues(string actual, string expected)
        {
            Assert.That(actual, Is.EqualTo(expected));
        }
    }

    [TestFixture]
    [Category("GDS")]
    [Parallelizable]
    public class RegisteredApplicationTests
    {
        private static readonly string[] s_pfxPemFormats = ["PFX", "PEM"];

        [Test]
        public void GetHttpsDomainNameReturnsHostFromDiscoveryUrl()
        {
            var app = new RegisteredApplication {
                DiscoveryUrl = new[] { "opc.tcp://myserver.example.com:4840" }
            };
            string result = app.GetHttpsDomainName();
            Assert.That(result, Is.EqualTo("myserver.example.com"));
        }

        [Test]
        public void GetHttpsDomainNameReplacesLocalhostWithHostName()
        {
            var app = new RegisteredApplication {
                DiscoveryUrl = new[] { "opc.tcp://localhost:4840" }
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
            var app = new RegisteredApplication {
                DiscoveryUrl = new[] { "not a url", "opc.tcp://valid.example.com:4840" }
            };
            string result = app.GetHttpsDomainName();
            Assert.That(result, Is.EqualTo("valid.example.com"));
        }

        [Test]
        public void GetPrivateKeyFormatReturnsPfxByDefault()
        {
            var app = new RegisteredApplication {
                RegistrationType = RegistrationType.ClientPull
            };
            string result = app.GetPrivateKeyFormat();
            Assert.That(result, Is.EqualTo("PFX"));
        }

        [Test]
        public void GetPrivateKeyFormatReturnsPemWhenPathEndsPem()
        {
            var app = new RegisteredApplication {
                RegistrationType = RegistrationType.ClientPull,
                CertificatePrivateKeyPath = "/certs/key.PEM"
            };
            string result = app.GetPrivateKeyFormat();
            Assert.That(result, Is.EqualTo("PEM"));
        }

        [Test]
        public void GetPrivateKeyFormatServerPushReturnsPemWhenNoPfxSupport()
        {
            var app = new RegisteredApplication {
                RegistrationType = RegistrationType.ServerPush
            };
            string result = app.GetPrivateKeyFormat(null);
            Assert.That(result, Is.EqualTo("PEM"));
        }

        [Test]
        public void GetPrivateKeyFormatServerPushReturnsPfxWhenPfxSupported()
        {
            var app = new RegisteredApplication {
                RegistrationType = RegistrationType.ServerPush
            };
            string result = app.GetPrivateKeyFormat(s_pfxPemFormats);
            Assert.That(result, Is.EqualTo("PFX"));
        }

        [Test]
        public void GetDomainNamesReturnsParsedDomains()
        {
            var app = new RegisteredApplication {
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
            var app = new RegisteredApplication {
                DiscoveryUrl = new[] {
                    "opc.tcp://server1.example.com:4840",
                    "opc.tcp://server2.example.com:4840"
                }
            };
            List<string> result = app.GetDomainNames(null);
            Assert.That(result, Has.Count.EqualTo(2));
            Assert.That(result, Does.Contain("server1.example.com"));
            Assert.That(result, Does.Contain("server2.example.com"));
        }

        [Test]
        public void GetDomainNamesDeduplicatesUrls()
        {
            var app = new RegisteredApplication {
                DiscoveryUrl = new[] {
                    "opc.tcp://server.example.com:4840",
                    "opc.tcp://server.example.com:4841"
                }
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
            using X509Certificate2 cert = CertificateFactory.CreateCertificate(
                "urn:test:app",
                "TestApp",
                "CN=TestApp,DC=testdomain,DC=com",
                new ArrayOf<string>(s_testHostDomains))
                .CreateForRSA();

            var app = new RegisteredApplication();
            List<string> result = app.GetDomainNames(cert);
            Assert.That(result.Count, Is.GreaterThan(0));
        }

        [Test]
        public void GetDomainNamesEmptyDomainsStringFallsThrough()
        {
            var app = new RegisteredApplication {
                Domains = "  ,  ,  ",
                DiscoveryUrl = new[] { "opc.tcp://fallback.example.com:4840" }
            };
            List<string> result = app.GetDomainNames(null);
            Assert.That(result, Does.Contain("fallback.example.com"));
        }
    }

    [TestFixture]
    [Category("GDS")]
    [Parallelizable]
    public class CertificateWrapperTests
    {
        private static readonly string[] s_localhostDomains = ["localhost"];
        private X509Certificate2 m_testCertificate;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            m_testCertificate = CertificateFactory.CreateCertificate(
                "urn:test:wrapper",
                "TestWrapper",
                "CN=TestWrapper,O=OPCFoundation",
                new ArrayOf<string>(s_localhostDomains))
                .CreateForRSA();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            m_testCertificate?.Dispose();
        }

        [Test]
        public void PropertiesReturnNullWhenCertificateIsNull()
        {
            var wrapper = new CertificateWrapper();
            Assert.That(wrapper.SubjectName, Is.Null);
            Assert.That(wrapper.IssuerName, Is.Null);
            Assert.That(wrapper.SerialNumber, Is.Null);
            Assert.That(wrapper.Thumbprint, Is.Null);
            Assert.That(wrapper.SignatureAlgorithm, Is.Null);
            Assert.That(wrapper.PublicKeyAlgorithm, Is.Null);
            Assert.That(wrapper.PublicKey, Is.Null);
            Assert.That(wrapper.ApplicationUri, Is.Null);
            Assert.That(wrapper.Domains, Is.Null);
        }

        [Test]
        public void ValidFromReturnsMinValueWhenCertificateIsNull()
        {
            var wrapper = new CertificateWrapper();
            Assert.That(wrapper.ValidFrom, Is.EqualTo(DateTime.MinValue));
        }

        [Test]
        public void ValidToReturnsMinValueWhenCertificateIsNull()
        {
            var wrapper = new CertificateWrapper();
            Assert.That(wrapper.ValidTo, Is.EqualTo(DateTime.MinValue));
        }

        [Test]
        public void KeySizeReturnsZeroWhenCertificateIsNull()
        {
            var wrapper = new CertificateWrapper();
            Assert.That(wrapper.KeySize, Is.EqualTo(0));
        }

        [Test]
        public void SubjectNameReturnsCertificateSubject()
        {
            var wrapper = new CertificateWrapper { Certificate = m_testCertificate };
            Assert.That(wrapper.SubjectName, Is.Not.Null);
            Assert.That(wrapper.SubjectName, Does.Contain("TestWrapper"));
        }

        [Test]
        public void IssuerNameReturnsCertificateIssuer()
        {
            var wrapper = new CertificateWrapper { Certificate = m_testCertificate };
            Assert.That(wrapper.IssuerName, Is.Not.Null);
        }

        [Test]
        public void ValidFromReturnsCertificateNotBefore()
        {
            var wrapper = new CertificateWrapper { Certificate = m_testCertificate };
            Assert.That(wrapper.ValidFrom, Is.EqualTo(m_testCertificate.NotBefore));
        }

        [Test]
        public void ValidToReturnsCertificateNotAfter()
        {
            var wrapper = new CertificateWrapper { Certificate = m_testCertificate };
            Assert.That(wrapper.ValidTo, Is.EqualTo(m_testCertificate.NotAfter));
        }

        [Test]
        public void SerialNumberReturnsCertificateSerialNumber()
        {
            var wrapper = new CertificateWrapper { Certificate = m_testCertificate };
            Assert.That(wrapper.SerialNumber, Is.EqualTo(m_testCertificate.SerialNumber));
        }

        [Test]
        public void ThumbprintReturnsCertificateThumbprint()
        {
            var wrapper = new CertificateWrapper { Certificate = m_testCertificate };
            Assert.That(wrapper.Thumbprint, Is.EqualTo(m_testCertificate.Thumbprint));
        }

        [Test]
        public void SignatureAlgorithmReturnsFriendlyName()
        {
            var wrapper = new CertificateWrapper { Certificate = m_testCertificate };
            Assert.That(wrapper.SignatureAlgorithm, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void PublicKeyAlgorithmReturnsFriendlyName()
        {
            var wrapper = new CertificateWrapper { Certificate = m_testCertificate };
            Assert.That(wrapper.PublicKeyAlgorithm, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void PublicKeyReturnsRawData()
        {
            var wrapper = new CertificateWrapper { Certificate = m_testCertificate };
            Assert.That(wrapper.PublicKey, Is.Not.Null);
            Assert.That(wrapper.PublicKey.Length, Is.GreaterThan(0));
        }

        [Test]
        public void KeySizeReturnsPositiveValue()
        {
            var wrapper = new CertificateWrapper { Certificate = m_testCertificate };
            Assert.That(wrapper.KeySize, Is.GreaterThan(0));
        }

        [Test]
        public void ApplicationUriReturnsValueFromCertificate()
        {
            var wrapper = new CertificateWrapper { Certificate = m_testCertificate };
            Assert.That(wrapper.ApplicationUri, Is.Not.Null);
            Assert.That(wrapper.ApplicationUri, Does.Contain("urn:test:wrapper"));
        }

        [Test]
        public void DomainsReturnsListFromCertificate()
        {
            var wrapper = new CertificateWrapper { Certificate = m_testCertificate };
            Assert.That(wrapper.Domains, Is.Not.Null);
            Assert.That(wrapper.Domains.Count, Is.GreaterThan(0));
        }

        [Test]
        public void ToStringReturnsSubjectName()
        {
            var wrapper = new CertificateWrapper { Certificate = m_testCertificate };
            Assert.That(wrapper.ToString(), Is.EqualTo(wrapper.SubjectName));
        }

        [Test]
        public void ToStringNullCertificateReturnsNull()
        {
            var wrapper = new CertificateWrapper();
            Assert.That(wrapper.ToString(), Is.Null);
        }

        [Test]
        public void ToStringWithNonNullFormatThrowsFormatException()
        {
            var wrapper = new CertificateWrapper { Certificate = m_testCertificate };
            Assert.Throws<FormatException>(() => wrapper.ToString("G", null));
        }

        [Test]
        public void CloneCreatesCopyWithSameCertificate()
        {
            var wrapper = new CertificateWrapper { Certificate = m_testCertificate };
            var clone = (CertificateWrapper)wrapper.Clone();
            Assert.That(clone, Is.Not.SameAs(wrapper));
            Assert.That(clone.Certificate, Is.SameAs(wrapper.Certificate));
        }

        [Test]
        public void EncodeThrowsNotImplementedException()
        {
            var wrapper = new CertificateWrapper();
            Assert.Throws<NotImplementedException>(() => wrapper.Encode(null));
        }

        [Test]
        public void DecodeThrowsNotImplementedException()
        {
            var wrapper = new CertificateWrapper();
            Assert.Throws<NotImplementedException>(() => wrapper.Decode(null));
        }

        [Test]
        public void IsEqualThrowsNotImplementedException()
        {
            var wrapper = new CertificateWrapper();
            Assert.Throws<NotImplementedException>(() => wrapper.IsEqual(null));
        }

        [Test]
        public void TypeIdReturnsNodeIdNull()
        {
            var wrapper = new CertificateWrapper();
            Assert.That(wrapper.TypeId, Is.EqualTo(NodeId.Null));
        }

        [Test]
        public void BinaryEncodingIdReturnsNodeIdNull()
        {
            var wrapper = new CertificateWrapper();
            Assert.That(wrapper.BinaryEncodingId, Is.EqualTo(NodeId.Null));
        }

        [Test]
        public void XmlEncodingIdReturnsNodeIdNull()
        {
            var wrapper = new CertificateWrapper();
            Assert.That(wrapper.XmlEncodingId, Is.EqualTo(NodeId.Null));
        }
    }
}
