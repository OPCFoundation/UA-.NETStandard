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
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Test;
using Opc.Ua.Tests;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Opc.Ua.Gds.Tests
{
    [TestFixture]
    [Category("GDSPush")]
    [Category("GDS")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public class TrustListValidationTest
    {
        private GlobalDiscoveryTestServer m_server;
        private ServerConfigurationPushTestClient m_pushClient;
        private ITelemetryContext m_telemetry;

        [OneTimeSetUp]
        public async Task OneTimeSetUpAsync()
        {
            // Start GDS server
            m_telemetry = NUnitTelemetryContext.Create();
            m_server = await TestUtils.StartGDSAsync(true, CertificateStoreType.Directory).ConfigureAwait(false);

            // Load client
            m_pushClient = new ServerConfigurationPushTestClient(true, m_telemetry);
            await m_pushClient.LoadClientConfigurationAsync(m_server.BasePort).ConfigureAwait(false);

            // Set admin credentials and connect
            m_pushClient.PushClient.AdminCredentials = m_pushClient.SysAdminUser;
            await m_pushClient.ConnectAsync(SecurityPolicies.Aes256_Sha256_RsaPss).ConfigureAwait(false);
        }

        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
            try
            {
                await m_pushClient.DisconnectClientAsync().ConfigureAwait(false);
                await m_server.StopServerAsync().ConfigureAwait(false);
            }
            catch
            {
            }
            finally
            {
                m_pushClient?.Dispose();
                m_pushClient = null;
                m_server = null;
            }
        }

        /// <summary>
        /// Test that normal-sized trust lists work correctly.
        /// </summary>
        [Test]
        [Order(100)]
        public async Task NormalSizeTrustListAsync()
        {
            // Create a normal-sized trust list
            var normalTrustList = new TrustListDataType
            {
                SpecifiedLists = (uint)TrustListMasks.All,
                TrustedCertificates = new ByteStringCollection(),
                TrustedCrls = new ByteStringCollection(),
                IssuerCertificates = new ByteStringCollection(),
                IssuerCrls = new ByteStringCollection()
            };

            // Add a reasonable number of certificates (10)
            for (int i = 0; i < 10; i++)
            {
                using X509Certificate2 cert = CertificateFactory
                    .CreateCertificate($"urn:test:cert{i}", $"NormalCert{i}", $"CN=NormalCert{i}, O=OPC Foundation", null)
                    .CreateForRSA();
                normalTrustList.TrustedCertificates.Add(cert.RawData);
            }

            // This should succeed
            bool requireReboot = await m_pushClient.PushClient.UpdateTrustListAsync(normalTrustList).ConfigureAwait(false);
            Assert.False(requireReboot);

            // Read it back to verify
            TrustListDataType readTrustList = await m_pushClient.PushClient.ReadTrustListAsync().ConfigureAwait(false);
            Assert.IsNotNull(readTrustList);
            Assert.AreEqual(normalTrustList.TrustedCertificates.Count, readTrustList.TrustedCertificates.Count);
        }

        /// <summary>
        /// Test that writing a trust list exceeding the size limit fails.
        /// </summary>
        [Test]
        [Order(200)]
        public async Task WriteTrustListExceedsSizeLimitAsync()
        {
            // Create a trust list that will definitely exceed 16MB when encoded
            var oversizedTrustList = new TrustListDataType
            {
                SpecifiedLists = (uint)TrustListMasks.All,
                TrustedCertificates = new ByteStringCollection(),
                TrustedCrls = new ByteStringCollection(),
                IssuerCertificates = new ByteStringCollection(),
                IssuerCrls = new ByteStringCollection()
            };

            // Generate a large number of certificates to exceed 16MB
            // Each 4096-bit RSA cert is roughly 2KB, so we need about 9000+ certs
            TestContext.Out.WriteLine("Generating large trust list...");
            for (int i = 0; i < 9000; i++)
            {
                using X509Certificate2 cert = CertificateFactory
                    .CreateCertificate($"urn:test:cert{i}", $"TestCert{i}", $"CN=TestCert{i}, O=OPC Foundation", null)
                    .SetRSAKeySize(4096)
                    .CreateForRSA();
                oversizedTrustList.TrustedCertificates.Add(cert.RawData);

                if (i % 1000 == 0)
                {
                    TestContext.Out.WriteLine($"Generated {i} certificates...");
                }
            }

            TestContext.Out.WriteLine("Attempting to write oversized trust list...");

            // This should throw ServiceResultException with BadEncodingLimitsExceeded
            var ex = Assert.ThrowsAsync<ServiceResultException>(async () =>
            {
                await m_pushClient.PushClient.UpdateTrustListAsync(oversizedTrustList).ConfigureAwait(false);
            });

            Assert.IsNotNull(ex);
            Assert.AreEqual(StatusCodes.BadEncodingLimitsExceeded, ex.StatusCode);
            TestContext.Out.WriteLine($"Expected exception caught: {ex.Message}");
        }

        /// <summary>
        /// Test boundary condition - trust list just under the limit.
        /// </summary>
        [Test]
        [Order(300)]
        public async Task TrustListJustUnderLimitAsync()
        {
            var boundaryTrustList = new TrustListDataType
            {
                SpecifiedLists = (uint)TrustListMasks.All,
                TrustedCertificates = new ByteStringCollection(),
                TrustedCrls = new ByteStringCollection(),
                IssuerCertificates = new ByteStringCollection(),
                IssuerCrls = new ByteStringCollection()
            };

            // Add enough certificates to get close to but under the limit
            // Estimate: 4096-bit cert ~2KB, so ~7500 certs = ~15MB
            TestContext.Out.WriteLine("Generating trust list just under limit...");
            for (int i = 0; i < 7500; i++)
            {
                using X509Certificate2 cert = CertificateFactory
                    .CreateCertificate($"urn:test:cert{i}", $"BoundaryCert{i}", $"CN=BoundaryCert{i}, O=OPC Foundation", null)
                    .SetRSAKeySize(4096)
                    .CreateForRSA();
                boundaryTrustList.TrustedCertificates.Add(cert.RawData);

                if (i % 1000 == 0)
                {
                    TestContext.Out.WriteLine($"Generated {i} certificates...");
                }
            }

            TestContext.Out.WriteLine("Writing trust list just under limit...");

            // This should succeed
            bool requireReboot = await m_pushClient.PushClient.UpdateTrustListAsync(boundaryTrustList).ConfigureAwait(false);
            Assert.False(requireReboot);

            // Read it back
            TrustListDataType readTrustList = await m_pushClient.PushClient.ReadTrustListAsync().ConfigureAwait(false);
            Assert.IsNotNull(readTrustList);
            Assert.AreEqual(boundaryTrustList.TrustedCertificates.Count, readTrustList.TrustedCertificates.Count);
        }
    }
}
