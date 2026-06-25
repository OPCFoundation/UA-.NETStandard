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
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Security.Certificates
{
    /// <summary>
    /// Measures the throughput of concurrent certificate validations against a
    /// single shared <see cref="CertificateManager"/>, the way a server
    /// validates many client certificates in parallel. Exercises the
    /// lock-free validation path plus the cached trust-list store and CRL
    /// cache so that scaling with the degree of parallelism can be observed.
    /// </summary>
    [TestFixture]
    [Category("CertificateValidator")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [MemoryDiagnoser]
    [BenchmarkCategory("CertificateValidator")]
    public class CertificateValidationConcurrencyBenchmarks
    {
        /// <summary>
        /// Number of validations issued in parallel per invocation.
        /// [Params(1, 8, 32, 64)]
        /// </summary>
        public int Concurrency { get; set; } = 8;

        /// <summary>
        /// Validates the leaf certificate <see cref="Concurrency"/> times in
        /// parallel. With the shared validator lock removed, throughput should
        /// scale with the degree of parallelism instead of serializing.
        /// </summary>
        [Benchmark]
        [Test]
        public async Task ValidateChainConcurrentlyAsync()
        {
            int concurrency = Concurrency;
            var tasks = new Task[concurrency];
            for (int i = 0; i < concurrency; i++)
            {
                tasks[i] = ValidateOnceAsync();
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        private async Task ValidateOnceAsync()
        {
            CertificateValidationResult result = await m_manager
                .ValidateAsync(m_leaf, TrustListIdentifier.Peers)
                .ConfigureAwait(false);

            Assert.That(result.IsValid, Is.True);
        }

        [OneTimeSetUp]
        public Task OneTimeSetUpAsync()
        {
            return SetupAsync();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            Cleanup();
        }

        [GlobalSetup]
        public Task GlobalSetupAsync()
        {
            return SetupAsync();
        }

        [GlobalCleanup]
        public void GlobalCleanup()
        {
            Cleanup();
        }

        private async Task SetupAsync()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_trustedPath = Path.Combine(
                Path.GetTempPath(),
                "opcua-validator-bench-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(m_trustedPath);

            m_rootCa = CertificateBuilder
                .Create("CN=ValidatorBenchRoot, O=OPC Foundation")
                .SetCAConstraint()
                .SetRSAKeySize(2048)
                .CreateForRSA();
            m_leaf = CertificateBuilder
                .Create("CN=ValidatorBenchLeaf, O=OPC Foundation")
                .SetIssuer(m_rootCa)
                .SetRSAKeySize(2048)
                .CreateForRSA();

            m_manager = new CertificateManager(m_telemetry);
            m_manager.RegisterTrustList(TrustListIdentifier.Peers, m_trustedPath);

            using (ICertificateStore store = m_manager.OpenTrustedStore(TrustListIdentifier.Peers))
            {
                await store.AddAsync(m_rootCa).ConfigureAwait(false);

                // Add an (empty) CRL so the revocation-check path is exercised.
                var crl = new X509CRL(CrlBuilder
                    .Create(m_rootCa.SubjectName)
                    .CreateForRSA(m_rootCa));
                await store.AddCRLAsync(crl).ConfigureAwait(false);
            }
        }

        private void Cleanup()
        {
            m_manager?.Dispose();
            m_manager = null;
            m_leaf?.Dispose();
            m_leaf = null;
            m_rootCa?.Dispose();
            m_rootCa = null;
            (m_telemetry as IDisposable)?.Dispose();
            m_telemetry = null;

            try
            {
                if (m_trustedPath != null && Directory.Exists(m_trustedPath))
                {
                    Directory.Delete(m_trustedPath, true);
                }
            }
            catch (IOException)
            {
                // best effort cleanup
            }
        }

        private ITelemetryContext m_telemetry;
        private string m_trustedPath;
        private CertificateManager m_manager;
        private Certificate m_rootCa;
        private Certificate m_leaf;
    }
}
