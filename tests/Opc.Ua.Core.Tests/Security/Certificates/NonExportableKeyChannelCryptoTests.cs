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
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Opc.Ua.Core.TestFramework;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Core.Tests.Security.Certificates
{
    /// <summary>
    /// Exercises the secure channel crypto entry points with a private key that
    /// can never be exported, which is the shape of every key held in a TPM, an
    /// HSM, a PKCS#11 token or a remote key service.
    /// </summary>
    /// <remarks>
    /// These are the operations the channel performs during OpenSecureChannel and
    /// ActivateSession: an asymmetric signature over the handshake, verification
    /// of the peer signature, and for RSA policies the unwrapping of the secret
    /// the peer encrypted to our public key. If they work here, the private key
    /// never needs to leave its device for the channel to be established.
    /// </remarks>
    [TestFixture]
    [Category("CertificateStore")]
    [Category("NonExportableKey")]
    [Parallelizable]
    [SetCulture("en-us")]
    public class NonExportableKeyChannelCryptoTests
    {
        [Test]
        [TestCase(SecurityPolicies.Basic256Sha256)]
        [TestCase(SecurityPolicies.Aes128_Sha256_RsaOaep)]
        public void RsaChannelSignatureWorksWithNonExportableKey(string securityPolicyUri)
        {
            using Certificate certificate = CreateDetachedRsaCertificate(out NonExportableRsa hardwareKey);

            byte[] dataToSign = CreateHandshakeData();
            byte[] signature = CryptoUtils.Sign(
                new ArraySegment<byte>(dataToSign), certificate, securityPolicyUri);

            Assert.That(signature, Is.Not.Null.And.Not.Empty);
            Assert.That(
                CryptoUtils.Verify(
                    new ArraySegment<byte>(dataToSign), signature, certificate, securityPolicyUri),
                Is.True,
                "The signature produced with the hardware key must verify against the certificate.");
            Assert.That(
                hardwareKey.PrivateKeyExportAttempts,
                Is.Zero,
                "Signing must never reach for the private key material itself.");
        }

        /// <summary>
        /// RSA policies unwrap the peer's secret with our private key. This is the
        /// only decryption the channel performs with the application key; ECC
        /// policies use an ephemeral key agreement instead.
        /// </summary>
        [Test]
        [TestCase(SecurityPolicies.Basic256Sha256)]
        [TestCase(SecurityPolicies.Aes128_Sha256_RsaOaep)]
        public void RsaChannelDecryptWorksWithNonExportableKey(string securityPolicyUri)
        {
            using Certificate certificate = CreateDetachedRsaCertificate(out NonExportableRsa hardwareKey);

            byte[] secret = [1, 2, 3, 4, 5, 6, 7, 8];
            EncryptedData encrypted = SecurityPolicyRegistry.Default.Encrypt(
                certificate, securityPolicyUri, secret);

            byte[] decrypted = SecurityPolicyRegistry.Default.Decrypt(
                certificate, securityPolicyUri, encrypted);

            Assert.That(decrypted, Is.EqualTo(secret));
            Assert.That(
                hardwareKey.PrivateKeyExportAttempts,
                Is.Zero,
                "Decryption must never reach for the private key material itself.");
        }

        [Test]
        [TestCase(SecurityPolicies.ECC_nistP256)]
        [TestCase(SecurityPolicies.ECC_nistP384)]
        public void EccChannelSignatureWorksWithNonExportableKey(string securityPolicyUri)
        {
            if (SecurityPolicyRegistry.Default.GetInfo(securityPolicyUri) == null)
            {
                Assert.Ignore($"{securityPolicyUri} is not supported on this platform.");
            }

            ECCurve curve = securityPolicyUri == SecurityPolicies.ECC_nistP256
                ? ECCurve.NamedCurves.nistP256
                : ECCurve.NamedCurves.nistP384;

            using Certificate certificate = CreateDetachedEcdsaCertificate(
                curve, out NonExportableECDsa hardwareKey);

            byte[] dataToSign = CreateHandshakeData();
            byte[] signature = CryptoUtils.Sign(
                new ArraySegment<byte>(dataToSign), certificate, securityPolicyUri);

            Assert.That(signature, Is.Not.Null.And.Not.Empty);
            Assert.That(
                CryptoUtils.Verify(
                    new ArraySegment<byte>(dataToSign), signature, certificate, securityPolicyUri),
                Is.True);
            Assert.That(hardwareKey.PrivateKeyExportAttempts, Is.Zero);
        }

        /// <summary>
        /// The channel sizes its buffers from the key before using it, so those
        /// helpers must not reach for private material either.
        /// </summary>
        [Test]
        public void KeySizeHelpersWorkWithNonExportableKey()
        {
            using Certificate certificate = CreateDetachedRsaCertificate(out NonExportableRsa hardwareKey);

            Assert.Multiple(() =>
            {
                Assert.That(CryptoUtils.GetRsaPublicKeySize(certificate), Is.EqualTo(2048));
                Assert.That(CryptoUtils.GetSignatureLength(certificate), Is.EqualTo(256));
                Assert.That(hardwareKey.PrivateKeyExportAttempts, Is.Zero);
            });
        }

        private static byte[] CreateHandshakeData()
        {
            byte[] data = new byte[256];
            for (int ii = 0; ii < data.Length; ii++)
            {
                data[ii] = (byte)ii;
            }
            return data;
        }

        private static Certificate CreateDetachedRsaCertificate(out NonExportableRsa hardwareKey)
        {
            RSA softwareKey = RSA.Create(2048);
            var request = new CertificateRequest(
                "CN=ChannelCryptoRsa", softwareKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using X509Certificate2 selfSigned = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));

            using Certificate publicOnly = Certificate.FromRawData(selfSigned.RawData);
            hardwareKey = new NonExportableRsa(softwareKey, ownsKey: true);
            return publicOnly.CopyWithDetachedPrivateKey(hardwareKey);
        }

        private static Certificate CreateDetachedEcdsaCertificate(
            ECCurve curve,
            out NonExportableECDsa hardwareKey)
        {
            ECDsa softwareKey = ECDsa.Create(curve);
            var request = new CertificateRequest(
                "CN=ChannelCryptoEcc", softwareKey, HashAlgorithmName.SHA256);
            using X509Certificate2 selfSigned = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));

            using Certificate publicOnly = Certificate.FromRawData(selfSigned.RawData);
            hardwareKey = new NonExportableECDsa(softwareKey, ownsKey: true);
            return publicOnly.CopyWithDetachedPrivateKey(hardwareKey);
        }
    }
}
