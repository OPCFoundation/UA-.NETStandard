/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Security.Certificates.Tests
{
    /// <summary>
    /// Certificates received from a peer are loaded as a single X.509 certificate
    /// only. Containers that the content-sniffing <see cref="X509Certificate2"/>
    /// constructor would also import (PKCS#12, PKCS#7) are rejected on every
    /// target framework, like the .NET 9 <c>X509CertificateLoader</c> does.
    /// </summary>
    [TestFixture]
    [Category("Certificate")]
    [Parallelizable]
    public class CertificateLoaderFormatTests
    {
        private Certificate m_certificate = null!;
        private byte[] m_pkcs12 = null!;
        private byte[] m_pkcs7 = null!;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            m_certificate = CertificateBuilder.Create("CN=LoaderFormat")
                .SetRSAKeySize(2048)
                .CreateForRSA();

            // public-only container content: no private key is imported anywhere.
            using X509Certificate2 publicOnly = X509CertificateLoader.LoadCertificate(m_certificate.RawData);
            Assert.That(publicOnly.HasPrivateKey, Is.False);
            m_pkcs12 = publicOnly.Export(X509ContentType.Pkcs12);
            m_pkcs7 = new X509Certificate2Collection(publicOnly).Export(X509ContentType.Pkcs7)!;
            Assert.That(X509Certificate2.GetCertContentType(m_pkcs12), Is.EqualTo(X509ContentType.Pkcs12));
            Assert.That(X509Certificate2.GetCertContentType(m_pkcs7), Is.EqualTo(X509ContentType.Pkcs7));
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            m_certificate?.Dispose();
        }

        [Test]
        public void LoadCertificateAcceptsDerCertificate()
        {
            using X509Certificate2 certificate = X509CertificateLoader.LoadCertificate(m_certificate.RawData);

            Assert.That(certificate.Thumbprint, Is.EqualTo(m_certificate.Thumbprint));
        }

        [Test]
        public void LoadCertificateAcceptsPemCertificate()
        {
            string pem = "-----BEGIN CERTIFICATE-----\n" +
                Convert.ToBase64String(m_certificate.RawData, Base64FormattingOptions.InsertLineBreaks) +
                "\n-----END CERTIFICATE-----\n";

            using X509Certificate2 certificate = X509CertificateLoader.LoadCertificate(
                Encoding.ASCII.GetBytes(pem));

            Assert.That(certificate.Thumbprint, Is.EqualTo(m_certificate.Thumbprint));
        }

        [Test]
        public void LoadCertificateRejectsPkcs12()
        {
            Assert.That(
                () => X509CertificateLoader.LoadCertificate(m_pkcs12),
                Throws.InstanceOf<CryptographicException>());
#if NET6_0_OR_GREATER
            Assert.That(
                () => X509CertificateLoader.LoadCertificate(new ReadOnlySpan<byte>(m_pkcs12)),
                Throws.InstanceOf<CryptographicException>());
#endif
            Assert.That(
                () => Certificate.FromRawData(m_pkcs12),
                Throws.InstanceOf<CryptographicException>());
        }

        [Test]
        public void LoadCertificateRejectsPkcs7()
        {
            Assert.That(
                () => X509CertificateLoader.LoadCertificate(m_pkcs7),
                Throws.InstanceOf<CryptographicException>());
#if NET6_0_OR_GREATER
            Assert.That(
                () => X509CertificateLoader.LoadCertificate(new ReadOnlySpan<byte>(m_pkcs7)),
                Throws.InstanceOf<CryptographicException>());
#endif
        }

        [Test]
        public void LoadCertificateRejectsBase64Pkcs12()
        {
            byte[] base64 = Encoding.ASCII.GetBytes(Convert.ToBase64String(m_pkcs12));

            Assert.That(
                () => X509CertificateLoader.LoadCertificate(base64),
                Throws.InstanceOf<CryptographicException>());
        }

        [Test]
        public void ParseCertificateChainBlobRejectsPkcs12Elements()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            byte[] chain = new byte[m_pkcs12.Length * 16];
            for (int i = 0; i < 16; i++)
            {
                Buffer.BlockCopy(m_pkcs12, 0, chain, i * m_pkcs12.Length, m_pkcs12.Length);
            }

            ServiceResultException? single = Assert.Throws<ServiceResultException>(
                () => Utils.ParseCertificateChainBlob(m_pkcs12, telemetry).Dispose());
            ServiceResultException? multiple = Assert.Throws<ServiceResultException>(
                () => Utils.ParseCertificateChainBlob(chain, telemetry).Dispose());

            Assert.That(single!.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadCertificateInvalid));
            Assert.That(multiple!.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadCertificateInvalid));
        }

        [Test]
        public void ParseCertificateChainBlobAcceptsDerChain()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            byte[] raw = m_certificate.RawData;
            byte[] chain = new byte[raw.Length * 2];
            Buffer.BlockCopy(raw, 0, chain, 0, raw.Length);
            Buffer.BlockCopy(raw, 0, chain, raw.Length, raw.Length);

            using CertificateCollection certificates = Utils.ParseCertificateChainBlob(chain, telemetry);

            Assert.That(certificates, Has.Count.EqualTo(2));
            Assert.That(certificates[0].Thumbprint, Is.EqualTo(m_certificate.Thumbprint));
        }
    }
}
