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
 *
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
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Security.Certificates
{
    /// <summary>
    /// Tests for <see cref="CertificateIdentifierResolver"/> covering the
    /// resolution paths (registry / inline RawData / store) and the
    /// post-rotation fallbacks of <c>LoadPrivateKeyAsync</c>.
    /// </summary>
    [TestFixture]
    [Category("CertificateIdentifierResolver")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class CertificateIdentifierResolverTests
    {
        private string m_storePath;
        private Certificate m_diskCert;
        private ITelemetryContext m_telemetry;

        [OneTimeSetUp]
        public async Task OneTimeSetUpAsync()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_storePath = Path.Combine(
                Path.GetTempPath(),
                "ResolverTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_storePath);

            m_diskCert = CertificateBuilder
                .Create("CN=ResolverTest, O=OPC Foundation")
                .SetRSAKeySize(2048)
                .CreateForRSA();

            await m_diskCert.AddToStoreAsync(
                CertificateStoreType.Directory,
                m_storePath,
                password: null,
                m_telemetry).ConfigureAwait(false);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            m_diskCert?.Dispose();
            try
            {
                if (m_storePath != null && Directory.Exists(m_storePath))
                {
                    Directory.Delete(m_storePath, recursive: true);
                }
            }
            catch
            {
                // best effort cleanup
            }
        }

        [Test]
        public async Task ResolveAsyncReturnsNullForNullIdentifier()
        {
            using Certificate result = await CertificateIdentifierResolver
                .ResolveAsync(
                    identifier: null,
                    registry: null,
                    needPrivateKey: false,
                    applicationUri: null,
                    m_telemetry)
                .ConfigureAwait(false);
            Assert.That(result, Is.Null);
        }

        [Test]
        public async Task ResolveAsyncFromInlineRawData()
        {
            var id = new CertificateIdentifier { RawData = m_diskCert.RawData };

            using Certificate result = await CertificateIdentifierResolver
                .ResolveAsync(
                    id,
                    registry: null,
                    needPrivateKey: false,
                    applicationUri: null,
                    m_telemetry)
                .ConfigureAwait(false);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Thumbprint, Is.EqualTo(m_diskCert.Thumbprint));
        }

        [Test]
        public async Task ResolveAsyncFromStoreByThumbprint()
        {
            var id = new CertificateIdentifier
            {
                StoreType = CertificateStoreType.Directory,
                StorePath = m_storePath,
                Thumbprint = m_diskCert.Thumbprint,
                SubjectName = m_diskCert.Subject,
                CertificateType = ObjectTypeIds.RsaSha256ApplicationCertificateType
            };

            using Certificate result = await CertificateIdentifierResolver
                .ResolveAsync(
                    id,
                    registry: null,
                    needPrivateKey: false,
                    applicationUri: null,
                    m_telemetry)
                .ConfigureAwait(false);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Thumbprint, Is.EqualTo(m_diskCert.Thumbprint));
        }

        [Test]
        public async Task ResolveAsyncReturnsNullWhenNothingMatches()
        {
            var id = new CertificateIdentifier
            {
                StoreType = CertificateStoreType.Directory,
                StorePath = m_storePath,
                Thumbprint = "FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF",
                CertificateType = ObjectTypeIds.RsaSha256ApplicationCertificateType
            };

            using Certificate result = await CertificateIdentifierResolver
                .ResolveAsync(
                    id,
                    registry: null,
                    needPrivateKey: false,
                    applicationUri: null,
                    m_telemetry)
                .ConfigureAwait(false);

            Assert.That(result, Is.Null);
        }

        [Test]
        public async Task LoadPrivateKeyAsyncReturnsNullForNullIdentifier()
        {
            using Certificate result = await CertificateIdentifierResolver
                .LoadPrivateKeyAsync(
                    identifier: null,
                    passwordProvider: null,
                    applicationUri: null,
                    m_telemetry)
                .ConfigureAwait(false);
            Assert.That(result, Is.Null);
        }

        [Test]
        public async Task LoadPrivateKeyAsyncFromDirectoryStore()
        {
            var id = new CertificateIdentifier
            {
                StoreType = CertificateStoreType.Directory,
                StorePath = m_storePath,
                Thumbprint = m_diskCert.Thumbprint,
                SubjectName = m_diskCert.Subject,
                CertificateType = ObjectTypeIds.RsaSha256ApplicationCertificateType
            };

            using Certificate result = await CertificateIdentifierResolver
                .LoadPrivateKeyAsync(
                    id,
                    passwordProvider: null,
                    applicationUri: null,
                    m_telemetry)
                .ConfigureAwait(false);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Thumbprint, Is.EqualTo(m_diskCert.Thumbprint));
            Assert.That(result.HasPrivateKey, Is.True);
        }

        [Test]
        public async Task LoadPrivateKeyAsyncReturnsNullForUnknownThumbprint()
        {
            var id = new CertificateIdentifier
            {
                StoreType = CertificateStoreType.Directory,
                StorePath = m_storePath,
                Thumbprint = "FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF",
                CertificateType = ObjectTypeIds.RsaSha256ApplicationCertificateType
            };

            using Certificate result = await CertificateIdentifierResolver
                .LoadPrivateKeyAsync(
                    id,
                    passwordProvider: null,
                    applicationUri: null,
                    m_telemetry)
                .ConfigureAwait(false);

            Assert.That(result, Is.Null);
        }

        [Test]
        public void OpenStoreReturnsNullForIdentifierWithoutStorePath()
        {
            var id = new CertificateIdentifier
            {
                Thumbprint = "ABCDEF"
            };

            using ICertificateStore store = CertificateIdentifierResolver
                .OpenStore(id, m_telemetry);

            Assert.That(store, Is.Null);
        }

        [Test]
        public void OpenStoreReturnsStoreForDirectoryIdentifier()
        {
            var id = new CertificateIdentifier
            {
                StoreType = CertificateStoreType.Directory,
                StorePath = m_storePath
            };

            using ICertificateStore store = CertificateIdentifierResolver
                .OpenStore(id, m_telemetry);

            Assert.That(store, Is.Not.Null);
        }
    }
}
