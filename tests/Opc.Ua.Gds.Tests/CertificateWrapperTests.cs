/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using NUnit.Framework;
using Opc.Ua.Gds.Client;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Gds.Tests
{
    [TestFixture]
    [Category("GDS")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class CertificateWrapperTests
    {
        private static readonly ICertificateFactory s_factory = DefaultCertificateFactory.Instance;
        private static readonly string[] s_localhostDomains = ["localhost"];
        private Certificate m_testCertificate;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            m_testCertificate = s_factory.CreateApplicationCertificate(
                "urn:test:wrapper",
                "TestWrapper",
                "CN=TestWrapper,O=OPCFoundation",
                s_localhostDomains)
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
            Assert.That(wrapper.KeySize, Is.Zero);
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
            Assert.That(wrapper.PublicKey, Is.Not.Empty);
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
            Assert.That(wrapper.Domains, Is.Not.Empty);
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
            CertificateWrapper clone = wrapper.Clone();
            Assert.That(clone, Is.Not.SameAs(wrapper));
            Assert.That(clone.Certificate, Is.SameAs(wrapper.Certificate));
        }

        [Test]
        public void CertificatePropertyDefaultsToNull()
        {
            var wrapper = new CertificateWrapper();
            Assert.That(wrapper.Certificate, Is.Null);
        }

        [Test]
        public void CertificatePropertyRoundTrip()
        {
            using Certificate cert = s_factory.CreateApplicationCertificate(
                "urn:test:roundtrip",
                "RoundTrip",
                "CN=RoundTrip",
                s_localhostDomains)
                .CreateForRSA();

            var wrapper = new CertificateWrapper { Certificate = cert };
            Assert.That(wrapper.Certificate, Is.SameAs(cert));
        }

        [Test]
        public void ToStringWithNullFormatReturnsSubjectName()
        {
            using Certificate cert = s_factory.CreateApplicationCertificate(
                "urn:test:tostring",
                "ToStringTest",
                "CN=ToStringTest",
                s_localhostDomains)
                .CreateForRSA();

            var wrapper = new CertificateWrapper { Certificate = cert };
            string result = wrapper.ToString(null, null);
            Assert.That(result, Is.EqualTo(cert.Subject));
        }
    }
}
