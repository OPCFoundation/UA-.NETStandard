// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using NUnit.Framework;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Core.Tests.Security.Certificates
{
    /// <summary>
    /// Tests for the <see cref="CertificateIssuerReference"/> public record
    /// used as the chain-element carrier returned by
    /// <see cref="ICertificateRegistry.GetIssuersAsync"/>.
    /// </summary>
    [TestFixture]
    [Category("CertificateIssuerReference")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class CertificateIssuerReferenceTests
    {
        private Certificate m_cert;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            m_cert = CertificateBuilder
                .Create("CN=IssuerRefTest")
                .SetRSAKeySize(2048)
                .CreateForRSA();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            m_cert?.Dispose();
        }

        [Test]
        public void RecordExposesCertificateAndOptions()
        {
            var reference = new CertificateIssuerReference(
                m_cert,
                CertificateValidationOptions.SuppressRevocationStatusUnknown);

            Assert.That(reference.Certificate, Is.SameAs(m_cert));
            Assert.That(
                reference.Options,
                Is.EqualTo(CertificateValidationOptions.SuppressRevocationStatusUnknown));
        }

        [Test]
        public void RecordEqualityComparesValues()
        {
            var a = new CertificateIssuerReference(
                m_cert,
                CertificateValidationOptions.Default);
            var b = new CertificateIssuerReference(
                m_cert,
                CertificateValidationOptions.Default);
            var c = new CertificateIssuerReference(
                m_cert,
                CertificateValidationOptions.SuppressRevocationStatusUnknown);

            Assert.That(a, Is.EqualTo(b));
            Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
            Assert.That(a, Is.Not.EqualTo(c));
        }

        [Test]
        public void RecordWithExpressionPreservesOtherFields()
        {
            var original = new CertificateIssuerReference(
                m_cert,
                CertificateValidationOptions.Default);
            CertificateIssuerReference modified = original with
            {
                Options = CertificateValidationOptions.SuppressCertificateExpired
            };

            Assert.That(modified.Certificate, Is.SameAs(m_cert));
            Assert.That(
                modified.Options,
                Is.EqualTo(CertificateValidationOptions.SuppressCertificateExpired));
            Assert.That(original.Options, Is.EqualTo(CertificateValidationOptions.Default));
        }
    }
}
