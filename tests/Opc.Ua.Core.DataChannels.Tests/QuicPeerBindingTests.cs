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
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Core.DataChannels.Tests
{
    /// <summary>
    /// Checks the TLS to OPC UA peer binding of Part 6 errata 7.6.1.
    /// </summary>
    /// <remarks>
    /// DCQ-007 is the assertion these cover: a valid TLS certificate from
    /// an accepted CA that carries the server's ApplicationUri but a
    /// different key shall not be accepted. Equality of a URI
    /// subjectAltName proves only that some CA in the trust list issued
    /// the certificate, and CA and GDS implementations commonly populate
    /// that SAN from the requester's own CSR.
    /// </remarks>
    [TestFixture]
    [Category("DataChannels")]
    public class QuicPeerBindingTests
    {
        private const string ApplicationUri = "urn:localhost:UA:TestServer";

        private static readonly string[] s_domainNames = ["localhost"];

        [Test]
        public void DcQ007SameKeyInAllThreeCertificatesIsBound()
        {
            using X509Certificate2 certificate = CreateCertificate("CN=Peer");
            byte[] der = certificate.RawData;

            Assert.That(
                QuicPeerBinding.Verify(certificate, der, der),
                Is.EqualTo(QuicPeerBindingResult.Bound));
        }

        [Test]
        public void DcQ007ADifferentKeyAgainstTheEndpointIsRefused()
        {
            using X509Certificate2 tls = CreateCertificate("CN=Peer");
            using X509Certificate2 other = CreateCertificate("CN=Peer");

            Assert.Multiple(() =>
            {
                Assert.That(
                    QuicPeerBinding.Verify(tls, other.RawData, tls.RawData),
                    Is.EqualTo(QuicPeerBindingResult.EndpointKeyMismatch));
                Assert.That(
                    QuicPeerBinding.ToStatusCode(QuicPeerBindingResult.EndpointKeyMismatch),
                    Is.EqualTo((StatusCode)StatusCodes.BadCertificateInvalid));
            });
        }

        [Test]
        public void DcQ007ADifferentKeyAgainstOpenSecureChannelIsRefused()
        {
            using X509Certificate2 tls = CreateCertificate("CN=Peer");
            using X509Certificate2 other = CreateCertificate("CN=Peer");

            Assert.That(
                QuicPeerBinding.Verify(tls, tls.RawData, other.RawData),
                Is.EqualTo(QuicPeerBindingResult.SecureChannelKeyMismatch));
        }

        [Test]
        public void DcQ007MatchingApplicationUriIsNotSufficient()
        {
            // Both certificates assert the same ApplicationUri and both
            // would pass a name based check. Only the key comparison
            // separates them.
            using X509Certificate2 victim = CreateCertificate("CN=Victim", ApplicationUri);
            using X509Certificate2 attacker = CreateCertificate("CN=Victim", ApplicationUri);

            Assert.Multiple(() =>
            {
                Assert.That(
                    attacker.Subject,
                    Is.EqualTo(victim.Subject),
                    "the two certificates are indistinguishable by name");
                Assert.That(
                    QuicPeerBinding.Verify(attacker, victim.RawData, victim.RawData),
                    Is.EqualTo(QuicPeerBindingResult.EndpointKeyMismatch));
            });
        }

        [Test]
        public void AMissingCertificateIsRefusedRatherThanAssumedGood()
        {
            using X509Certificate2 certificate = CreateCertificate("CN=Peer");
            byte[] der = certificate.RawData;

            Assert.Multiple(() =>
            {
                Assert.That(
                    QuicPeerBinding.Verify(null, der, der),
                    Is.EqualTo(QuicPeerBindingResult.NoTlsCertificate));
                Assert.That(
                    QuicPeerBinding.Verify(certificate, [], der),
                    Is.EqualTo(QuicPeerBindingResult.NoEndpointCertificate));
                Assert.That(
                    QuicPeerBinding.Verify(certificate, der, []),
                    Is.EqualTo(QuicPeerBindingResult.NoSecureChannelCertificate));
            });
        }

        [Test]
        public void MutualBindingComparesTlsClientCertificateToOpenSecureChannelCertificate()
        {
            using X509Certificate2 tls = CreateCertificate("CN=ClientTls");
            using X509Certificate2 other = CreateCertificate("CN=ClientOpcUa");

            Assert.Multiple(() =>
            {
                Assert.That(
                    QuicPeerBinding.Verify(tls, tls.RawData),
                    Is.EqualTo(QuicPeerBindingResult.Bound));
                Assert.That(
                    QuicPeerBinding.Verify(tls, other.RawData),
                    Is.EqualTo(QuicPeerBindingResult.SecureChannelKeyMismatch));
                Assert.That(
                    QuicPeerBinding.Verify(null, tls.RawData),
                    Is.EqualTo(QuicPeerBindingResult.NoTlsCertificate));
            });
        }

        [Test]
        public void AMalformedCertificateIsRefused()
        {
            using X509Certificate2 certificate = CreateCertificate("CN=Peer");

            Assert.That(
                QuicPeerBinding.Verify(certificate, new byte[] { 1, 2, 3 }, certificate.RawData),
                Is.EqualTo(QuicPeerBindingResult.MalformedCertificate));
        }

        private static X509Certificate2 CreateCertificate(
            string subject,
            string? applicationUri = null)
        {
            ICertificateBuilder builder = CertificateBuilder.Create(subject);

            if (applicationUri != null)
            {
                builder = builder.AddExtension(
                    new X509SubjectAltNameExtension(applicationUri, s_domainNames));
            }

            using Certificate created = builder
                .SetNotBefore(DateTime.UtcNow.AddDays(-1))
                .SetNotAfter(DateTime.UtcNow.AddDays(1))
                .SetRSAKeySize(2048)
                .CreateForRSA();

            return created.AsX509Certificate2();
        }
    }
}
