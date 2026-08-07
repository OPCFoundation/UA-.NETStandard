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
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Security.Certificates.Tests
{
    /// <summary>
    /// Covers the non-owning views a detached key is handed out through.
    /// </summary>
    /// <remarks>
    /// Callers write <c>using RSA key = certificate.GetRSAPrivateKey()</c>, so
    /// every use disposes what it was given. A device key is shared by every
    /// handle on the certificate, so if a view disposed it the second caller
    /// would get a dead key - and on a real token, an operation that fails only
    /// after the first one succeeded. These tests pin that behaviour down.
    /// </remarks>
    [TestFixture]
    [Category("NonExportableKey")]
    [Parallelizable(ParallelScope.All)]
    [SetCulture("en-us")]
    public class NonOwningKeyViewTests
    {
        [Test]
        public void DisposingAnRsaViewLeavesTheSharedKeyUsable()
        {
            using Certificate certificate = CreateDetachedRsa(out RSA deviceKey);

            byte[] hash = Hash([1, 2, 3]);

            // Mirrors a caller's using block.
            using (RSA first = certificate.GetRSAPrivateKey())
            {
                Assert.That(first, Is.Not.Null);
                Assert.That(
                    first.SignHash(hash, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1),
                    Is.Not.Empty);
            }

            using RSA second = certificate.GetRSAPrivateKey();

            Assert.That(
                second.SignHash(hash, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1),
                Is.Not.Empty,
                "disposing one view must not take the shared device key down with it");

            // And the underlying key itself is still alive.
            Assert.That(
                deviceKey.SignHash(hash, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1),
                Is.Not.Empty);
        }

        [Test]
        public void DisposingAnEcdsaViewLeavesTheSharedKeyUsable()
        {
            using Certificate certificate = CreateDetachedEcdsa(out ECDsa deviceKey);

            byte[] hash = Hash([4, 5, 6]);

            using (ECDsa first = certificate.GetECDsaPrivateKey())
            {
                Assert.That(first.SignHash(hash), Is.Not.Empty);
            }

            using ECDsa second = certificate.GetECDsaPrivateKey();

            Assert.That(second.SignHash(hash), Is.Not.Empty);
            Assert.That(deviceKey.SignHash(hash), Is.Not.Empty);
        }

        [Test]
        public void RsaViewSignsAndVerifiesThroughTheSharedKey()
        {
            using Certificate certificate = CreateDetachedRsa(out _);
            using RSA view = certificate.GetRSAPrivateKey();

            byte[] hash = Hash([7, 7, 7]);
            byte[] signature = view.SignHash(hash, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);

            Assert.Multiple(() =>
            {
                Assert.That(
                    view.VerifyHash(hash, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss),
                    Is.True);
                Assert.That(view.KeySize, Is.EqualTo(2048));
                Assert.That(view.SignatureAlgorithm, Is.Not.Null);
                Assert.That(view.KeyExchangeAlgorithm, Is.Not.Null);
            });
        }

        [Test]
        public void RsaViewRoundTripsEncryption()
        {
            using Certificate certificate = CreateDetachedRsa(out _);
            using RSA view = certificate.GetRSAPrivateKey();

            byte[] plaintext = [1, 1, 2, 3, 5];
            byte[] encrypted = view.Encrypt(plaintext, RSAEncryptionPadding.OaepSHA256);

            Assert.That(
                view.Decrypt(encrypted, RSAEncryptionPadding.OaepSHA256),
                Is.EqualTo(plaintext));
        }

        [Test]
        public void RsaViewSignsData()
        {
            using Certificate certificate = CreateDetachedRsa(out _);
            using RSA view = certificate.GetRSAPrivateKey();

            byte[] data = [9, 9, 9, 9];

            // Exercises the HashData overrides the base class routes through.
            byte[] fromBuffer = view.SignData(
                data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

            using var stream = new MemoryStream(data);
            byte[] fromStream = view.SignData(
                stream, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

            Assert.Multiple(() =>
            {
                Assert.That(fromBuffer, Is.Not.Empty);
                Assert.That(
                    fromStream,
                    Is.EqualTo(fromBuffer),
                    "hashing a buffer and the same bytes as a stream must agree");
            });
        }

        [Test]
        public void EcdsaViewSignsDataAndExposesThePublicParameters()
        {
            using Certificate certificate = CreateDetachedEcdsa(out _);
            using ECDsa view = certificate.GetECDsaPrivateKey();

            byte[] data = [3, 1, 4, 1, 5];
            byte[] signature = view.SignData(data, HashAlgorithmName.SHA256);

            Assert.Multiple(() =>
            {
                Assert.That(view.VerifyData(data, signature, HashAlgorithmName.SHA256), Is.True);
                Assert.That(view.ExportParameters(false).Q.X, Is.Not.Null);
                Assert.That(view.KeySize, Is.EqualTo(256));
                Assert.That(view.SignatureAlgorithm, Is.Not.Null);
            });
        }

        [Test]
        public void EcdsaViewSignsAStream()
        {
            using Certificate certificate = CreateDetachedEcdsa(out _);
            using ECDsa view = certificate.GetECDsaPrivateKey();

            byte[] data = [2, 7, 1, 8];

            using var stream = new MemoryStream(data);
            byte[] signature = view.SignData(stream, HashAlgorithmName.SHA256);

            Assert.That(view.VerifyData(data, signature, HashAlgorithmName.SHA256), Is.True);
        }

        /// <summary>
        /// A view is transparent: it adds no policy of its own, so the key
        /// underneath is what refuses. Over a non-extractable key that means the
        /// private parameters stay unreachable through the view too, while the
        /// public ones remain readable.
        /// </summary>
        [Test]
        public void ViewOverANonExtractableKeyStillRefusesPrivateExport()
        {
            using RSA softwareKey = RSA.Create(2048);
            var request = new CertificateRequest(
                "CN=NonOwningHardware",
                softwareKey,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
            using X509Certificate2 selfSigned = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));

            var hardwareKey = new Opc.Ua.Core.TestFramework.NonExportableRsa(
                softwareKey, ownsKey: false);

            using Certificate publicOnly = Certificate.FromRawData(selfSigned.RawData);
            using Certificate certificate = publicOnly.CopyWithDetachedPrivateKey(hardwareKey);

            using RSA view = certificate.GetRSAPrivateKey();

            Assert.Multiple(() =>
            {
                Assert.Throws<CryptographicException>(
                    () => view.ExportParameters(true),
                    "the view must not become a way around a non-extractable key");
                Assert.DoesNotThrow(
                    () => view.ExportParameters(false),
                    "the public key must still be readable through the view");
            });

            // Signing still works, and never reached for the key material.
            byte[] hash = Hash([1, 2, 3]);

            Assert.Multiple(() =>
            {
                Assert.That(
                    view.SignHash(hash, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1),
                    Is.Not.Empty);
                Assert.That(hardwareKey.PrivateKeyExportAttempts, Is.EqualTo(1));
            });
        }

        private static byte[] Hash(byte[] data)
        {
#if NET6_0_OR_GREATER
            return SHA256.HashData(data);
#else
            using SHA256 sha256 = SHA256.Create();
            return sha256.ComputeHash(data);
#endif
        }

        private static Certificate CreateDetachedRsa(out RSA deviceKey)
        {
            deviceKey = RSA.Create(2048);
            var request = new CertificateRequest(
                "CN=NonOwningRsa", deviceKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using X509Certificate2 selfSigned = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));

            using Certificate publicOnly = Certificate.FromRawData(selfSigned.RawData);

            // ownsPrivateKey: false keeps the caller's handle valid for the test.
            return publicOnly.CopyWithDetachedPrivateKey(deviceKey, ownsPrivateKey: false);
        }

        private static Certificate CreateDetachedEcdsa(out ECDsa deviceKey)
        {
            deviceKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var request = new CertificateRequest(
                "CN=NonOwningEcc", deviceKey, HashAlgorithmName.SHA256);
            using X509Certificate2 selfSigned = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));

            using Certificate publicOnly = Certificate.FromRawData(selfSigned.RawData);

            return publicOnly.CopyWithDetachedPrivateKey(deviceKey, ownsPrivateKey: false);
        }
    }
}
