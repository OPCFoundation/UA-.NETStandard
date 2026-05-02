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
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Security.Certificates
{
    /// <summary>
    /// Tests for the <see cref="CertificateValidatorAdapter"/> class.
    /// </summary>
    [TestFixture]
    [Category("CertificateValidatorAdapter")]
    [Parallelizable]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class CertificateValidatorAdapterTests
    {
        private ITelemetryContext m_telemetry;
        private readonly List<string> m_tempDirs = [];

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            (m_telemetry as IDisposable)?.Dispose();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (string dir in m_tempDirs)
            {
                try
                {
                    if (Directory.Exists(dir))
                    {
                        Directory.Delete(dir, true);
                    }
                }
                catch (IOException)
                {
                    // best effort cleanup
                }
            }

            m_tempDirs.Clear();
        }

        [Test]
        public async Task ValidateAsyncSuccessDoesNotThrow()
        {
            string trustedPath = CreateTempDir();
            using var manager = new CertificateManager(m_telemetry);
            manager.RegisterTrustList(TrustListIdentifier.Peers, trustedPath);

            using Certificate cert = CertificateBuilder
                .Create("CN=TrustedCert")
                .SetRSAKeySize(2048)
                .CreateForRSA();

            // Add the certificate to the trusted store so validation succeeds.
            using (ICertificateStore store = manager.OpenTrustedStore(TrustListIdentifier.Peers))
            {
                await store.AddAsync(cert).ConfigureAwait(false);
            }

            var adapter = new CertificateValidatorAdapter(manager);

            Assert.DoesNotThrowAsync(
                async () => await adapter.ValidateAsync(cert, CancellationToken.None)
                    .ConfigureAwait(false));
        }

        [Test]
        public void ValidateAsyncFailureThrowsServiceResultException()
        {
            string trustedPath = CreateTempDir();
            using var manager = new CertificateManager(m_telemetry);
            manager.RegisterTrustList(TrustListIdentifier.Peers, trustedPath);

            using Certificate cert = CertificateBuilder
                .Create("CN=UntrustedCert")
                .SetRSAKeySize(2048)
                .CreateForRSA();

            var adapter = new CertificateValidatorAdapter(manager);

            Assert.ThrowsAsync<ServiceResultException>(
                async () => await adapter.ValidateAsync(cert, CancellationToken.None)
                    .ConfigureAwait(false));
        }

        [Test]
        public async Task ValidateChainAsyncSuccessDoesNotThrow()
        {
            string trustedPath = CreateTempDir();
            using var manager = new CertificateManager(m_telemetry);
            manager.RegisterTrustList(TrustListIdentifier.Peers, trustedPath);

            using Certificate cert = CertificateBuilder
                .Create("CN=TrustedChainCert")
                .SetRSAKeySize(2048)
                .CreateForRSA();

            using (ICertificateStore store = manager.OpenTrustedStore(TrustListIdentifier.Peers))
            {
                await store.AddAsync(cert).ConfigureAwait(false);
            }

            var adapter = new CertificateValidatorAdapter(manager);
            using var chain = new CertificateCollection { cert };

            Assert.DoesNotThrowAsync(
                async () => await adapter.ValidateAsync(chain, CancellationToken.None)
                    .ConfigureAwait(false));
        }

        [Test]
        public void ValidateChainAsyncFailureThrowsServiceResultException()
        {
            string trustedPath = CreateTempDir();
            using var manager = new CertificateManager(m_telemetry);
            manager.RegisterTrustList(TrustListIdentifier.Peers, trustedPath);

            using Certificate cert = CertificateBuilder
                .Create("CN=UntrustedChainCert")
                .SetRSAKeySize(2048)
                .CreateForRSA();

            var adapter = new CertificateValidatorAdapter(manager);
            using var chain = new CertificateCollection { cert };

            Assert.ThrowsAsync<ServiceResultException>(
                async () => await adapter.ValidateAsync(chain, CancellationToken.None)
                    .ConfigureAwait(false));
        }

        #region Helpers

        private string CreateTempDir()
        {
            string dir = Path.Combine(
                Path.GetTempPath(),
                "opcua-cva-test-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(dir);
            m_tempDirs.Add(dir);
            return dir;
        }

        #endregion
    }
}
