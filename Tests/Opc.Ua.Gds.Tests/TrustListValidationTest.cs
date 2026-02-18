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

using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using NUnit.Framework;
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
            await m_pushClient.ConnectAsync(SecurityPolicies.Aes256_Sha256_RsaPss).ConfigureAwait(false);
            m_pushClient.PushClient.AdminCredentials = m_pushClient.SysAdminUser;
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
                SpecifiedLists = (uint)TrustListMasks.TrustedCertificates,
                TrustedCertificates = [],
                TrustedCrls = [],
                IssuerCertificates = [],
                IssuerCrls = []
            };

            // Add a reasonable number of certificates (10)
            for (int i = 0; i < 10; i++)
            {
                using X509Certificate2 cert = CertificateFactory
                    .CreateCertificate($"urn:test:cert{i}", $"NormalCert{i}", $"CN=NormalCert{i}, O=OPC Foundation", null)
                    .CreateForRSA();
                normalTrustList.TrustedCertificates.Add(cert.RawData.ToByteString());
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
        public void WriteTrustListExceedsSizeLimit()
        {
            // Create a trust list with a few certificates
            var oversizedTrustList = new TrustListDataType
            {
                SpecifiedLists = (uint)TrustListMasks.TrustedCertificates,
                TrustedCertificates = [],
                TrustedCrls = [],
                IssuerCertificates = [],
                IssuerCrls = []
            };

            for (int i = 0; i < 20; i++)
            {
                using X509Certificate2 cert = CertificateFactory
                    .CreateCertificate($"urn:test:cert{i}", $"TestCert{i}", $"CN=TestCert{i}, O=OPC Foundation", null)
                    .SetRSAKeySize(2048)
                    .CreateForRSA();
                oversizedTrustList.TrustedCertificates.Add(cert.RawData.ToByteString());
            }

            // Calculate the encoded size
            long encodedSize = GetEncodedSize(oversizedTrustList);
            TestContext.Out.WriteLine($"Generated trust list with encoded size: {encodedSize} bytes.");

            // Set the client's max trust list size to be smaller than the actual size
            uint maxTrustListSize = (uint)encodedSize - 1;
            TestContext.Out.WriteLine($"Client MaxTrustListSize set to: {maxTrustListSize}");

            // This should throw ServiceResultException with BadEncodingLimitsExceeded
            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await m_pushClient.PushClient.UpdateTrustListAsync(oversizedTrustList, maxTrustListSize).ConfigureAwait(false));

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
                SpecifiedLists = (uint)TrustListMasks.TrustedCertificates,
                TrustedCertificates = [],
                TrustedCrls = [],
                IssuerCertificates = [],
                IssuerCrls = []
            };

            for (int i = 0; i < 20; i++)
            {
                using X509Certificate2 cert = CertificateFactory
                    .CreateCertificate($"urn:test:cert{i}", $"BoundaryCert{i}", $"CN=BoundaryCert{i}, O=OPC Foundation", null)
                    .SetRSAKeySize(2048)
                    .CreateForRSA();
                boundaryTrustList.TrustedCertificates.Add(cert.RawData.ToByteString());
            }

            // Calculate the encoded size
            long encodedSize = GetEncodedSize(boundaryTrustList);
            TestContext.Out.WriteLine($"Generated trust list with encoded size: {encodedSize} bytes.");

            // Set the client's max trust list size to be exactly the encoded size (should pass)
            uint maxTrustListSize = (uint)encodedSize;
            TestContext.Out.WriteLine($"Client MaxTrustListSize set to: {maxTrustListSize}");

            // This should succeed
            bool requireReboot = await m_pushClient.PushClient.UpdateTrustListAsync(boundaryTrustList, maxTrustListSize).ConfigureAwait(false);
            Assert.False(requireReboot);

            // Read it back
            TrustListDataType readTrustList = await m_pushClient.PushClient.ReadTrustListAsync().ConfigureAwait(false);
            Assert.IsNotNull(readTrustList);
            Assert.AreEqual(boundaryTrustList.TrustedCertificates.Count, readTrustList.TrustedCertificates.Count);
        }

        /// <summary>
        /// Test reading and writing with a custom MaxTrustListSize set in the ServerConfiguration.
        /// </summary>
        [Test]
        [Order(400)]
        public async Task ReadWriteWithCustomServerMaxTrustListSizeAsync()
        {
            // Define a custom size limit for the server
            const int customMaxTrustListSize = 8192; // 8 KB

            // Update server configuration
            await m_server.StopServerAsync().ConfigureAwait(false);
            m_server = await TestUtils.StartGDSAsync(false, CertificateStoreType.Directory, customMaxTrustListSize).ConfigureAwait(false);
            await m_pushClient.LoadClientConfigurationAsync(m_server.BasePort).ConfigureAwait(false);
            await m_pushClient.ConnectAsync(SecurityPolicies.Aes256_Sha256_RsaPss).ConfigureAwait(false);
            m_pushClient.PushClient.AdminCredentials = m_pushClient.SysAdminUser;

            TestContext.Out.WriteLine($"Server MaxTrustListSize set to: {customMaxTrustListSize}");

            try
            {
                // 1. Test writing a trust list that exceeds the server's limit
                var oversizedTrustList = new TrustListDataType
                {
                    SpecifiedLists = (uint)TrustListMasks.TrustedCertificates,
                    TrustedCertificates = []
                };

                long currentSize = 0;
                int certCount = 0;
                while (currentSize <= customMaxTrustListSize)
                {
                    using X509Certificate2 cert =
                        CertificateFactory.CreateCertificate($"urn:test:oversized{certCount}", "Oversized", "CN=Oversized", null).CreateForRSA();
                    oversizedTrustList.TrustedCertificates.Add(cert.RawData.ToByteString());
                    currentSize = GetEncodedSize(oversizedTrustList);
                    certCount++;
                }
                TestContext.Out.WriteLine($"Oversized trust list created with {certCount} certs and size {currentSize}");

                ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await m_pushClient.PushClient.UpdateTrustListAsync(oversizedTrustList).ConfigureAwait(false));
                Assert.AreEqual(StatusCodes.BadEncodingLimitsExceeded, ex.StatusCode);
                TestContext.Out.WriteLine("Successfully caught exception for writing oversized trust list to server.");

                // 2. Test writing a valid trust list (under the server's limit)
                var validTrustList = new TrustListDataType
                {
                    SpecifiedLists = (uint)TrustListMasks.TrustedCertificates,
                    TrustedCertificates = []
                };
                for (int i = 0; i < 2; i++)
                {
                    using X509Certificate2 cert = CertificateFactory.CreateCertificate($"urn:test:valid{i}", "Valid", "CN=Valid", null).CreateForRSA();
                    validTrustList.TrustedCertificates.Add(cert.RawData.ToByteString());
                }
                long validSize = GetEncodedSize(validTrustList);
                Assert.True(validSize < customMaxTrustListSize);
                TestContext.Out.WriteLine($"Valid trust list created with size {validSize}");

                bool reboot = await m_pushClient.PushClient.UpdateTrustListAsync(validTrustList).ConfigureAwait(false);
                Assert.False(reboot);
                TestContext.Out.WriteLine("Successfully wrote valid trust list to server.");

                // 3. Test reading the trust list with a client limit that is too small
                ServiceResultException exRead = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await m_pushClient.PushClient.ReadTrustListAsync(TrustListMasks.TrustedCertificates, (uint)validSize - 1).ConfigureAwait(false));
                Assert.AreEqual(StatusCodes.BadEncodingLimitsExceeded, exRead.StatusCode);
                TestContext.Out.WriteLine("Successfully caught exception for reading trust list with small client limit.");

                // 4. Test reading with a sufficient client limit
                TrustListDataType readTrustList = await m_pushClient.PushClient
                    .ReadTrustListAsync(TrustListMasks.TrustedCertificates, (uint)validSize).ConfigureAwait(false);
                Assert.IsNotNull(readTrustList);
                Assert.AreEqual(validTrustList.TrustedCertificates.Count, readTrustList.TrustedCertificates.Count);
                TestContext.Out.WriteLine("Successfully read trust list with sufficient client limit.");
            }
            finally
            {
                // Restore original server configuration
                await m_server.StopServerAsync().ConfigureAwait(false);
                m_server = await TestUtils.StartGDSAsync(false, CertificateStoreType.Directory, 0).ConfigureAwait(false);
                await m_pushClient.LoadClientConfigurationAsync(m_server.BasePort).ConfigureAwait(false);
                await m_pushClient.ConnectAsync(SecurityPolicies.Aes256_Sha256_RsaPss).ConfigureAwait(false);
                m_pushClient.PushClient.AdminCredentials = m_pushClient.SysAdminUser;

                TestContext.Out.WriteLine("Restored original server configuration.");
            }
        }

        private long GetEncodedSize(TrustListDataType trustList)
        {
            using var stream = new System.IO.MemoryStream();
            using var encoder = new BinaryEncoder(stream, m_pushClient.PushClient.Session.MessageContext, false);
            encoder.WriteEncodeable(null, trustList, trustList.GetType());
            return stream.Length;
        }
    }
}
