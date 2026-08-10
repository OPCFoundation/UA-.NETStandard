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
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Core.TestFramework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Security.Certificates
{
    /// <summary>
    /// Verifies that the certificate stores cope with private keys that cannot
    /// be exported, which is the case for every key held in a TPM, an HSM, a
    /// PKCS#11 token or a remote key service.
    /// </summary>
    [TestFixture]
    [Category("CertificateStore")]
    [Category("NonExportableKey")]
    [Parallelizable]
    [SetCulture("en-us")]
    public class NonExportableKeyStoreTests
    {
        [SetUp]
        public void SetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_storePath = Path.Combine(
                Path.GetTempPath(), "opcua-nonexportable-" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            if (m_storePath != null && Directory.Exists(m_storePath))
            {
                Directory.Delete(m_storePath, true);
            }
        }

        /// <summary>
        /// Adding a certificate whose key is not exportable must store the public
        /// certificate rather than fail, because the key is already safe where it
        /// resides and was never going to reach the disk.
        /// </summary>
        [Test]
        public async Task AddAsyncStoresPublicCertificateWhenKeyIsNotExportableAsync()
        {
            using Certificate withKey = CreateDetachedKeyCertificate();

            using var store = new DirectoryCertificateStore(false, m_telemetry);
            store.Open(m_storePath, false);

            Assert.That(
                async () => await store.AddAsync(withKey).ConfigureAwait(false),
                Throws.Nothing,
                "A non exportable key must not prevent the certificate being stored.");

            CertificateCollection found = await store
                .FindByThumbprintAsync(withKey.Thumbprint)
                .ConfigureAwait(false);

            Assert.That(found, Is.Not.Empty, "The public certificate should have been stored.");
            using Certificate stored = found[0];
            Assert.That(stored.Thumbprint, Is.EqualTo(withKey.Thumbprint));
        }

        private static Certificate CreateDetachedKeyCertificate()
        {
            RSA softwareKey = RSA.Create(2048);
            var request = new CertificateRequest(
                "CN=NonExportable", softwareKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using X509Certificate2 selfSigned = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));

            using Certificate publicOnly = Certificate.FromRawData(selfSigned.RawData);
            return publicOnly.CopyWithDetachedPrivateKey(
                new NonExportableRsa(softwareKey, ownsKey: true));
        }

        private string m_storePath;

        private ITelemetryContext m_telemetry;
    }
}
