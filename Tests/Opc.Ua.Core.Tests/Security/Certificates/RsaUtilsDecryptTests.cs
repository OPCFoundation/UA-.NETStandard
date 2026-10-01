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
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Security.Certificates
{
    /// <summary>
    /// Legacy RSA user token decryption must fail the same way for a bad
    /// padding and for a conforming padding with an invalid length prefix,
    /// otherwise the error is a padding oracle on the server key.
    /// </summary>
    [TestFixture]
    [Category("Security")]
    [Parallelizable]
    public class RsaUtilsDecryptTests
    {
        private X509Certificate2 m_certificate;
        private ILogger m_logger;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            m_certificate = CertificateFactory
                .CreateCertificate(
                    "urn:localhost:RsaUtilsDecryptTests",
                    "RsaUtilsDecryptTests",
                    "CN=RsaUtilsDecryptTests",
                    null)
                .SetRSAKeySize(2048)
                .CreateForRSA();
            m_logger = NUnitTelemetryContext.Create().CreateLogger<RsaUtilsDecryptTests>();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            m_certificate?.Dispose();
        }

        [Test]
        public void RoundTripStillWorks()
        {
            byte[] secret = [1, 2, 3, 4, 5, 6, 7, 8];
            EncryptedData encrypted = SecurityPolicies.Encrypt(
                m_certificate,
                SecurityPolicies.Basic256Sha256,
                secret,
                m_logger);

            byte[] decrypted = SecurityPolicies.Decrypt(
                m_certificate,
                SecurityPolicies.Basic256Sha256,
                encrypted,
                m_logger);

            Assert.That(decrypted, Is.EqualTo(secret));
        }

        [TestCase(0x7FFFFFFF)]
        [TestCase(-1)]
        [TestCase(1000)]
        public void InvalidLengthFailsLikeInvalidPadding(int length)
        {
            // a conforming OAEP block whose length prefix does not fit the block.
            byte[] block = new byte[16];
            BitConverter.GetBytes(length).CopyTo(block, 0);
            using RSA rsa = m_certificate.GetRSAPublicKey();
            byte[] cipherText = rsa.Encrypt(block, RSAEncryptionPadding.OaepSHA1);

            Exception lengthError = Catch(cipherText);

            // the same size ciphertext with a broken padding.
            byte[] garbage = new byte[cipherText.Length];
            garbage[0] = 0x01;
            garbage[^1] = 0x01;
            Exception paddingError = Catch(garbage);

            Assert.That(lengthError, Is.InstanceOf<CryptographicException>());
            Assert.That(paddingError, Is.InstanceOf<CryptographicException>());
            Assert.That(lengthError, Is.Not.InstanceOf<ServiceResultException>());
        }

        private Exception Catch(byte[] cipherText)
        {
            return Assert.Catch(() => SecurityPolicies.Decrypt(
                m_certificate,
                SecurityPolicies.Basic256Sha256,
                new EncryptedData
                {
                    Algorithm = SecurityAlgorithms.RsaOaep,
                    Data = cipherText
                },
                m_logger));
        }
    }
}
