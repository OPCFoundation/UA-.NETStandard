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
using System.Security.Cryptography.X509Certificates;
using NUnit.Framework;
using Opc.Ua.Gds.Client;

namespace Opc.Ua.Gds.Tests
{
    [TestFixture]
    [Category("GDS")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
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
            using X509Certificate2 cert = CertificateFactory.CreateCertificate(
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
            using X509Certificate2 cert = CertificateFactory.CreateCertificate(
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
