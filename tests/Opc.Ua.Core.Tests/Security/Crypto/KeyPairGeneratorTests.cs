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
using NUnit.Framework;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Core.Tests.Security.Crypto
{
    /// <summary>
    /// Covers the seam that decides where the key behind a new application
    /// instance certificate comes from.
    /// </summary>
    [TestFixture]
    [Category("CryptoProvider")]
    [Parallelizable]
    [SetCulture("en-us")]
    public class KeyPairGeneratorTests
    {
        [Test]
        public void DefaultGeneratorProducesAnRsaCertificate()
        {
            ICertificateBuilder builder = CreateBuilder();

            using Certificate certificate = DefaultKeyPairGenerator.Instance.CreateCertificate(
                builder, ObjectTypeIds.RsaSha256ApplicationCertificateType, 2048);

            Assert.That(certificate.HasPrivateKey, Is.True);
            using RSA key = certificate.GetRSAPublicKey();
            Assert.That(key, Is.Not.Null);
            Assert.That(key.KeySize, Is.EqualTo(2048));
        }

        [Test]
        public void DefaultGeneratorProducesAnEccCertificate()
        {
            if (!Utils.IsSupportedCertificateType(
                ObjectTypeIds.EccNistP256ApplicationCertificateType))
            {
                Assert.Ignore("nistP256 is not supported on this platform.");
            }

            ICertificateBuilder builder = CreateBuilder();

            using Certificate certificate = DefaultKeyPairGenerator.Instance.CreateCertificate(
                builder, ObjectTypeIds.EccNistP256ApplicationCertificateType, 0);

            Assert.That(certificate.HasPrivateKey, Is.True);
            using ECDsa key = certificate.GetECDsaPublicKey();
            Assert.That(key, Is.Not.Null);
        }

        /// <summary>
        /// A key size of zero must fall back to the stack's default rather than
        /// producing an unusable certificate.
        /// </summary>
        [Test]
        public void DefaultGeneratorUsesTheDefaultKeySizeWhenUnspecified()
        {
            ICertificateBuilder builder = CreateBuilder();

            using Certificate certificate = DefaultKeyPairGenerator.Instance.CreateCertificate(
                builder, ObjectTypeIds.RsaSha256ApplicationCertificateType, 0);

            using RSA key = certificate.GetRSAPublicKey();
            Assert.That(key.KeySize, Is.EqualTo(CertificateFactory.DefaultKeySize));
        }

        [Test]
        public void UnknownCertificateTypeIsRejected()
        {
            ICertificateBuilder builder = CreateBuilder();

            Assert.That(
                () => DefaultKeyPairGenerator.Instance.CreateCertificate(
                    builder, new NodeId(999999), 0),
                Throws.TypeOf<ServiceResultException>());
        }

        [Test]
        public void GeneratorRejectsANullBuilder()
        {
            Assert.That(
                () => DefaultKeyPairGenerator.Instance.CreateCertificate(
                    null, ObjectTypeIds.RsaSha256ApplicationCertificateType, 2048),
                Throws.TypeOf<ArgumentNullException>());
        }

        /// <summary>
        /// A custom generator must be able to take over completely, which is how
        /// a device has its key created inside a TPM or an HSM.
        /// </summary>
        [Test]
        public void CustomGeneratorReplacesTheDefault()
        {
            var generator = new RecordingKeyPairGenerator();
            ICertificateBuilder builder = CreateBuilder();

            using Certificate certificate = generator.CreateCertificate(
                builder, ObjectTypeIds.RsaSha256ApplicationCertificateType, 2048);

            Assert.Multiple(() =>
            {
                Assert.That(generator.Invocations, Is.EqualTo(1));
                Assert.That(certificate.HasPrivateKey, Is.True);
            });
        }

        private static ICertificateBuilder CreateBuilder()
        {
            return DefaultCertificateFactory.Instance
                .CreateApplicationCertificate(
                    "urn:localhost:KeyPairGeneratorTests",
                    "KeyPairGeneratorTests",
                    "CN=KeyPairGeneratorTests",
                    ["localhost"])
                .SetLifeTime(12);
        }

        /// <summary>
        /// Stands in for a generator that would delegate to a device.
        /// </summary>
        private sealed class RecordingKeyPairGenerator : IKeyPairGenerator
        {
            public int Invocations { get; private set; }

            public Certificate CreateCertificate(
                ICertificateBuilder builder,
                NodeId certificateType,
                ushort keySizeInBits)
            {
                Invocations++;
                return DefaultKeyPairGenerator.Instance.CreateCertificate(
                    builder, certificateType, keySizeInBits);
            }
        }
    }
}
