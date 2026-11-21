// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
