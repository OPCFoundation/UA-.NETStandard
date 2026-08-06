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

// CA2000: test code; many disposables are ownership-transferred to test fixtures or short-lived,
// making CA2000 noisy without a real leak risk. Disabled file-level for the suite.
#pragma warning disable CA2000
using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using NUnit.Framework;
using Opc.Ua.Core.TestFramework;

namespace Opc.Ua.Security.Certificates.Tests
{
    /// <summary>
    /// Tests for private keys that are held alongside a certificate rather than
    /// attached to it, which is how keys resident in a TPM, an HSM, a PKCS#11
    /// token or a remote key service must be represented.
    /// </summary>
    [TestFixture]
    [Category("Certificate")]
    [Category("DetachedPrivateKey")]
    [Parallelizable]
    [SetCulture("en-us")]
    public class CertificateDetachedPrivateKeyTests
    {
        /// <summary>
        /// A non exportable key cannot be attached with the platform API, which
        /// is the reason detached keys exist. This documents the constraint that
        /// motivates the whole feature.
        /// </summary>
        [Test]
        public void CopyWithPrivateKeyRejectsNonExportableKey()
        {
            using Certificate publicOnly = CreatePublicOnlyRsaCertificate(out RSA softwareKey);
            using (softwareKey)
            {
                var hardwareKey = new NonExportableRsa(softwareKey);

                Assert.That(
                    () => publicOnly.CopyWithPrivateKey(hardwareKey),
                    Throws.TypeOf<CryptographicException>(),
                    "Attaching a non exportable key is expected to fail; use CopyWithDetachedPrivateKey.");
            }
        }

        [Test]
        public void DetachedRsaKeyReportsPrivateKeyAndSigns()
        {
            using Certificate publicOnly = CreatePublicOnlyRsaCertificate(out RSA softwareKey);
            var hardwareKey = new NonExportableRsa(softwareKey, ownsKey: true);

            using Certificate withKey = publicOnly.CopyWithDetachedPrivateKey(hardwareKey);

            Assert.Multiple(() =>
            {
                Assert.That(withKey.HasPrivateKey, Is.True);
                Assert.That(withKey.HasDetachedPrivateKey, Is.True);
                Assert.That(publicOnly.HasPrivateKey, Is.False, "The source must stay public only.");
            });

            byte[] data = [1, 2, 3, 4];
            byte[] signature;

            using (RSA privateKey = withKey.GetRSAPrivateKey())
            {
                Assert.That(privateKey, Is.Not.Null);
                Assert.That(privateKey!.KeySize, Is.EqualTo(2048));
                signature = privateKey.SignData(
                    data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            }

            using RSA publicKey = withKey.GetRSAPublicKey();
            Assert.That(publicKey, Is.Not.Null);
            Assert.That(
                publicKey!.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1),
                Is.True);
        }

        /// <summary>
        /// Callers of GetRSAPrivateKey own the returned object and dispose it.
        /// The shared detached key must survive that, otherwise the first caller
        /// would destroy the hardware key for everyone.
        /// </summary>
        [Test]
        public void DisposingReturnedKeyDoesNotDestroyDetachedKey()
        {
            using Certificate publicOnly = CreatePublicOnlyRsaCertificate(out RSA softwareKey);
            var hardwareKey = new NonExportableRsa(softwareKey, ownsKey: true);
            using Certificate withKey = publicOnly.CopyWithDetachedPrivateKey(hardwareKey);

            byte[] data = [9, 8, 7];

            for (int ii = 0; ii < 3; ii++)
            {
                using RSA privateKey = withKey.GetRSAPrivateKey();
                Assert.That(
                    () => privateKey!.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1),
                    Throws.Nothing,
                    $"Signing must still work on iteration {ii} after previous handles were disposed.");
            }
        }

        [Test]
        public void DetachedKeySurvivesAddRefAndIsReleasedOnce()
        {
            using Certificate publicOnly = CreatePublicOnlyRsaCertificate(out RSA softwareKey);
            var hardwareKey = new NonExportableRsa(softwareKey, ownsKey: true);
            Certificate withKey = publicOnly.CopyWithDetachedPrivateKey(hardwareKey);

            Certificate second = withKey.AddRef();
            withKey.Dispose();

            using (second)
            {
                using RSA privateKey = second.GetRSAPrivateKey();
                Assert.That(privateKey, Is.Not.Null, "The detached key must outlive the first handle.");
                Assert.That(
                    () => privateKey!.SignData([1], HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1),
                    Throws.Nothing);
            }
        }

        [Test]
        public void DetachedEcdsaKeyReportsPrivateKeyAndSigns()
        {
            // The certificate is self signed with the software key, then the very
            // same key is wrapped as a hardware key so the pair still matches.
            // CertificateRequest.CreateSelfSigned cannot be used with a non
            // exportable key because it calls CopyWithPrivateKey internally.
            ECDsa softwareKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var request = new CertificateRequest(
                "CN=DetachedEcc", softwareKey, HashAlgorithmName.SHA256);
            using X509Certificate2 selfSigned = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));

            using Certificate publicOnly = Certificate.FromRawData(selfSigned.RawData);
            var hardwareKey = new NonExportableECDsa(softwareKey, ownsKey: true);
            using Certificate withKey = publicOnly.CopyWithDetachedPrivateKey(hardwareKey);

            Assert.Multiple(() =>
            {
                Assert.That(withKey.HasPrivateKey, Is.True);
                Assert.That(withKey.HasDetachedPrivateKey, Is.True);
            });

            byte[] data = [5, 6, 7];
            byte[] signature;

            using (ECDsa privateKey = withKey.GetECDsaPrivateKey())
            {
                Assert.That(privateKey, Is.Not.Null);
                signature = privateKey!.SignData(data, HashAlgorithmName.SHA256);
            }

            using ECDsa publicKey = withKey.GetECDsaPublicKey();
            Assert.That(publicKey, Is.Not.Null);
            Assert.That(publicKey!.VerifyData(data, signature, HashAlgorithmName.SHA256), Is.True);
        }

        /// <summary>
        /// The point of a detached key is that private material never leaves the
        /// device, so exporting it must fail rather than silently succeed.
        /// </summary>
        [Test]
        public void ExportingDetachedPrivateKeyFails()
        {
            using Certificate publicOnly = CreatePublicOnlyRsaCertificate(out RSA softwareKey);
            var hardwareKey = new NonExportableRsa(softwareKey, ownsKey: true);
            using Certificate withKey = publicOnly.CopyWithDetachedPrivateKey(hardwareKey);

            using RSA privateKey = withKey.GetRSAPrivateKey();

            Assert.Multiple(() =>
            {
                Assert.That(
                    () => privateKey!.ExportParameters(true),
                    Throws.TypeOf<CryptographicException>());
                Assert.That(
                    () => privateKey!.ExportParameters(false),
                    Throws.Nothing,
                    "The public parameters must remain available.");
            });
        }

        [Test]
        public void ExportingDetachedKeyAsPkcs12Fails()
        {
            using Certificate publicOnly = CreatePublicOnlyRsaCertificate(out RSA softwareKey);
            var hardwareKey = new NonExportableRsa(softwareKey, ownsKey: true);
            using Certificate withKey = publicOnly.CopyWithDetachedPrivateKey(hardwareKey);

            Assert.Multiple(() =>
            {
                Assert.That(
                    () => withKey.Export(X509ContentType.Pfx),
                    Throws.TypeOf<CryptographicException>(),
                    "A PKCS#12 export must fail loudly rather than silently omit the key.");
                Assert.That(
                    () => withKey.Export(X509ContentType.Cert),
                    Throws.Nothing,
                    "Exporting the public certificate must still work.");
            });
        }

        [Test]
        public void CopyWithDetachedPrivateKeyRejectsNullKey()
        {
            using Certificate publicOnly = CreatePublicOnlyRsaCertificate(out RSA softwareKey);
            using (softwareKey)
            {
                Assert.That(
                    () => publicOnly.CopyWithDetachedPrivateKey((RSA)null!),
                    Throws.TypeOf<ArgumentNullException>());
            }
        }

        private static Certificate CreatePublicOnlyRsaCertificate(out RSA key)
        {
            key = RSA.Create(2048);
            var request = new CertificateRequest(
                "CN=DetachedRsa", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using X509Certificate2 selfSigned = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
            return Certificate.FromRawData(selfSigned.RawData);
        }
    }
}
