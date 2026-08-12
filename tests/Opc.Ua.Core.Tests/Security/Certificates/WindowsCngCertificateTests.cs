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
using System.Runtime.InteropServices;
#if NET5_0_OR_GREATER
using System.Runtime.Versioning;
#endif
using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Opc.Ua.Core.TestFramework;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Core.Tests.Security.Certificates
{
    /// <summary>
    /// Exercises the channel crypto path with a key held by a Windows key
    /// storage provider and marked non extractable, which is how a TPM backed
    /// application instance certificate behaves.
    /// </summary>
    /// <remarks>
    /// The Platform Crypto Provider is used when a TPM is present. Otherwise the
    /// software key storage provider stands in: the key is still genuinely non
    /// extractable, so the code paths under test are the same, and CI agents
    /// without a TPM still get coverage.
    /// </remarks>
    [TestFixture]
    [Category("CertificateStore")]
    [Category("NonExportableKey")]
    [Category("WindowsCng")]
    [Platform("Win")]
    [NonParallelizable]
    [SetCulture("en-us")]
#if NET5_0_OR_GREATER
    [SupportedOSPlatform("windows")]
#endif
    public class WindowsCngCertificateTests
    {
        [SetUp]
        public void SetUp()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Assert.Ignore("CNG key storage providers are a Windows feature.");
            }

            m_keyName = "opcua-test-" + Guid.NewGuid().ToString("N");
        }

        [TearDown]
        public void TearDown()
        {
            if (m_keyName != null && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                WindowsCngCertificateFactory.DeleteKey(m_keyName);
            }
        }

        [Test]
        public void CngKeyIsGenuinelyNonExportable()
        {
            using Certificate certificate = WindowsCngCertificateFactory.CreateRsaCertificate(
                "CN=CngNonExportable", m_keyName);

            using RSA privateKey = certificate.GetRSAPrivateKey();
            Assert.That(privateKey, Is.Not.Null);

            Assert.Multiple(() =>
            {
                Assert.That(
                    () => privateKey.ExportParameters(true),
                    Throws.TypeOf<CryptographicException>(),
                    "A key created with CngExportPolicies.None must refuse to export.");
                Assert.That(
                    () => certificate.Export(System.Security.Cryptography.X509Certificates.X509ContentType.Pfx),
                    Throws.TypeOf<CryptographicException>(),
                    "Exporting a PKCS#12 must fail rather than silently omit the key.");
            });
        }

        [Test]
        [TestCase(SecurityPolicies.Basic256Sha256)]
        [TestCase(SecurityPolicies.Aes128_Sha256_RsaOaep)]
        public void CngCertificateCompletesChannelCrypto(string securityPolicyUri)
        {
            if (SecurityPolicies.Default.GetInfo(securityPolicyUri) == null)
            {
                Assert.Ignore($"{securityPolicyUri} is not supported on this platform.");
            }

            using Certificate certificate = WindowsCngCertificateFactory.CreateRsaCertificate(
                "CN=CngChannel", m_keyName);

            byte[] dataToSign = [2, 4, 6, 8, 10, 12];
            byte[] signature = CryptoUtils.Sign(
                new ArraySegment<byte>(dataToSign), certificate, securityPolicyUri);

            Assert.That(signature, Is.Not.Null.And.Not.Empty);
            Assert.That(
                CryptoUtils.Verify(
                    new ArraySegment<byte>(dataToSign), signature, certificate, securityPolicyUri),
                Is.True);

            byte[] secret = [42, 43, 44];
            EncryptedData encrypted = SecurityPolicies.Default.Encrypt(
                certificate, securityPolicyUri, secret);
            byte[] decrypted = SecurityPolicies.Default.Decrypt(
                certificate, securityPolicyUri, encrypted);

            Assert.That(decrypted, Is.EqualTo(secret));
        }

        /// <summary>
        /// Records whether the agent has a TPM, so a failure on a TPM equipped
        /// machine can be told apart from one on a machine without.
        /// </summary>
        [Test]
        public void ReportPlatformCryptoProviderAvailability()
        {
            TestContext.Out.WriteLine(
                $"Platform Crypto Provider (TPM) available: {WindowsCngCertificateFactory.IsTpmAvailable}");
            Assert.Pass();
        }

        private string m_keyName;
    }
}
