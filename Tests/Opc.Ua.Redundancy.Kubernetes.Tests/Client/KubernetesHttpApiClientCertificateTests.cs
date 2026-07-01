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

// CA2007: tests run without a SynchronizationContext; ConfigureAwait(false)
// adds noise without a behavioural benefit. Disabled file-level for the suite.
#pragma warning disable CA2007

using System;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using NUnit.Framework;

namespace Opc.Ua.Redundancy.Kubernetes.Tests
{
    /// <summary>
    /// Unit tests for Kubernetes API server certificate validation.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    [Parallelizable(ParallelScope.All)]
    public class KubernetesHttpApiClientCertificateTests
    {
        [Test]
        public void CertificateSignedByPinnedCaWithMatchingSanIsAccepted()
        {
            using X509Certificate2 root = CreateRootCertificate("Pinned Kubernetes CA");
            using X509Certificate2 server = CreateServerCertificate(root, "kubernetes.default.svc");
            using var chain = new X509Chain();

            bool accepted = KubernetesHttpApiClient.ValidateServerCertificate(
                root,
                "kubernetes.default.svc",
                server,
                chain,
                SslPolicyErrors.RemoteCertificateChainErrors);

            Assert.That(accepted, Is.True);
        }

        [Test]
        public void CertificateSignedByPinnedCaWithDifferentSanIsRejected()
        {
            using X509Certificate2 root = CreateRootCertificate("Pinned Kubernetes CA");
            using X509Certificate2 server = CreateServerCertificate(root, "attacker.default.svc");
            using var chain = new X509Chain();

            bool accepted = KubernetesHttpApiClient.ValidateServerCertificate(
                root,
                "kubernetes.default.svc",
                server,
                chain,
                SslPolicyErrors.RemoteCertificateChainErrors | SslPolicyErrors.RemoteCertificateNameMismatch);

            Assert.That(accepted, Is.False);
        }

        [Test]
        public void CertificateSignedByDifferentCaIsRejected()
        {
            using X509Certificate2 pinnedRoot = CreateRootCertificate("Pinned Kubernetes CA");
            using X509Certificate2 otherRoot = CreateRootCertificate("Other Kubernetes CA");
            using X509Certificate2 server = CreateServerCertificate(otherRoot, "kubernetes.default.svc");
            using var chain = new X509Chain();

            bool accepted = KubernetesHttpApiClient.ValidateServerCertificate(
                pinnedRoot,
                "kubernetes.default.svc",
                server,
                chain,
                SslPolicyErrors.RemoteCertificateChainErrors);

            Assert.That(accepted, Is.False);
        }

        private static X509Certificate2 CreateRootCertificate(string commonName)
        {
            using RSA key = RSA.Create(2048);
            var request = new CertificateRequest(
                $"CN={commonName}",
                key,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(
                new X509BasicConstraintsExtension(certificateAuthority: true, hasPathLengthConstraint: false, 0, true));
            request.CertificateExtensions.Add(
                new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
            request.CertificateExtensions.Add(
                new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

            DateTimeOffset notBefore = DateTimeOffset.UtcNow.AddDays(-1);
            DateTimeOffset notAfter = DateTimeOffset.UtcNow.AddDays(30);
            return request.CreateSelfSigned(notBefore, notAfter);
        }

        private static X509Certificate2 CreateServerCertificate(
            X509Certificate2 issuer,
            string dnsName)
        {
            using RSA key = RSA.Create(2048);
            var request = new CertificateRequest(
                $"CN={dnsName}",
                key,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
            var sanBuilder = new SubjectAlternativeNameBuilder();
            sanBuilder.AddDnsName(dnsName);
            request.CertificateExtensions.Add(sanBuilder.Build());
            request.CertificateExtensions.Add(
                new X509BasicConstraintsExtension(certificateAuthority: false, hasPathLengthConstraint: false, 0, true));
            request.CertificateExtensions.Add(
                new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
            request.CertificateExtensions.Add(
                new X509EnhancedKeyUsageExtension(
                    new OidCollection
                    {
                        new Oid("1.3.6.1.5.5.7.3.1")
                    },
                    false));

            byte[] serialNumber = RandomNumberGenerator.GetBytes(16);
            DateTimeOffset notBefore = DateTimeOffset.UtcNow.AddDays(-1);
            DateTimeOffset notAfter = issuer.NotAfter.AddSeconds(-1);
            return request.Create(issuer, notBefore, notAfter, serialNumber);
        }
    }
}