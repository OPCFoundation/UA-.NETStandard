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
 *
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
