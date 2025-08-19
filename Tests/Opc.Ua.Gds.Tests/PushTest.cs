/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Gds.Server;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Test;
using Assert = NUnit.Framework.Legacy.ClassicAssert;
using OpcUa = Opc.Ua;

namespace Opc.Ua.Gds.Tests
{
    [TestFixture]
    [Category("GDSPush")]
    [Category("GDS")]
    [TestFixtureSource(nameof(FixtureArgs))]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public class PushTest
    {
        /// <summary>
        /// CertificateTypes to run the Test with.
        /// For ECC types, the additional fourth element is the expected curve friendly name.
        /// </summary>
        public static readonly object[] FixtureArgs =
        [
            new object[]
            {
                nameof(OpcUa.ObjectTypeIds.RsaSha256ApplicationCertificateType),
                OpcUa.ObjectTypeIds.RsaSha256ApplicationCertificateType,
                SecurityPolicies.Aes256_Sha256_RsaPss,
                null
            },
#if ECC_SUPPORT
            new object[]
            {
                nameof(OpcUa.ObjectTypeIds.EccNistP256ApplicationCertificateType),
                OpcUa.ObjectTypeIds.EccNistP256ApplicationCertificateType,
                SecurityPolicies.ECC_nistP256,
                ECCurve.NamedCurves.nistP256
            },
            new object[]
            {
                nameof(OpcUa.ObjectTypeIds.EccNistP384ApplicationCertificateType),
                OpcUa.ObjectTypeIds.EccNistP384ApplicationCertificateType,
                SecurityPolicies.ECC_nistP384,
                ECCurve.NamedCurves.nistP384
            },
            new object[]
            {
                nameof(OpcUa.ObjectTypeIds.EccBrainpoolP256r1ApplicationCertificateType),
                OpcUa.ObjectTypeIds.EccBrainpoolP256r1ApplicationCertificateType,
                SecurityPolicies.ECC_brainpoolP256r1,
                ECCurve.NamedCurves.brainpoolP256r1
            },
            new object[]
            {
                nameof(OpcUa.ObjectTypeIds.EccBrainpoolP384r1ApplicationCertificateType),
                OpcUa.ObjectTypeIds.EccBrainpoolP384r1ApplicationCertificateType,
                SecurityPolicies.ECC_brainpoolP384r1,
                ECCurve.NamedCurves.brainpoolP384r1
            }
#endif
        ];

        public PushTest(string certificateTypeString, NodeId certificateType, string securityPolicyUri, ECCurve? curve)
        {
            if (!Utils.IsSupportedCertificateType(certificateType))
            {
                NUnit.Framework.Assert.Ignore(
                    $"Certificate type {certificateTypeString} is not supported on this platform.");
            }

            // If a curve name is provided, perform extra check if ecc is supported
            if (curve != null && !IsCurveSupported(curve.Value))
            {
                NUnit.Framework.Assert.Ignore("ECC curve is not supported on this platform.");
            }

            m_certificateType = certificateType;
            m_securityPolicyUri = securityPolicyUri;
        }

        private static bool IsCurveSupported(ECCurve curve)
        {
            ECDsa key = null;
            try
            {
                key = ECDsa.Create(curve);
            }
            catch
            {
                return false;
            }
            finally
            {
                Utils.SilentDispose(key);
            }
            return true;
        }

        /// <summary>
        /// Set up a Global Discovery Server and Client instance and connect the session
        /// </summary>
        [OneTimeSetUp]
        protected async Task OneTimeSetUpAsync()
        {
            // start GDS first clean, then restart server
            // to ensure the application cert is not 'fresh'
            m_server = await TestUtils.StartGDSAsync(true).ConfigureAwait(false);
            m_server.StopServer();
            await Task.Delay(1000).ConfigureAwait(false);
            m_server = await TestUtils.StartGDSAsync(false).ConfigureAwait(false);

            m_randomSource = new RandomSource(kRandomStart);

            // load clients
            m_gdsClient = new GlobalDiscoveryTestClient(true);
            await m_gdsClient.LoadClientConfigurationAsync(m_server.BasePort).ConfigureAwait(false);
            m_pushClient = new ServerConfigurationPushTestClient(true);
            await m_pushClient.LoadClientConfigurationAsync(m_server.BasePort)
                .ConfigureAwait(false);

            // connect once
            await m_gdsClient.GDSClient.ConnectAsync(m_gdsClient.GDSClient.EndpointUrl)
                .ConfigureAwait(false);

            await m_pushClient.ConnectAsync(m_securityPolicyUri)
                .ConfigureAwait(false);

            await ConnectGDSClientAsync(true).ConfigureAwait(false);
            await RegisterPushServerApplicationAsync(m_pushClient.PushClient.EndpointUrl).ConfigureAwait(false);

            m_selfSignedServerCert = X509CertificateLoader.LoadCertificate(
                m_pushClient.PushClient.Session.ConfiguredEndpoint.Description.ServerCertificate);
            m_domainNames = [.. X509Utils.GetDomainsFromCertificate(m_selfSignedServerCert)];

            await CreateCATestCertsAsync(m_pushClient.TempStorePath).ConfigureAwait(false);
        }

        /// <summary>
        /// Tear down the Global Discovery Server and disconnect the Client
        /// </summary>
        [OneTimeTearDown]
        protected async Task OneTimeTearDownAsync()
        {
            try
            {
                await ConnectGDSClientAsync(true).ConfigureAwait(false);
                await UnRegisterPushServerApplicationAsync().ConfigureAwait(false);
                await m_gdsClient.DisconnectClientAsync().ConfigureAwait(false);
                await m_pushClient.DisconnectClientAsync().ConfigureAwait(false);
                m_server.StopServer();
            }
            catch
            {
            }
            m_gdsClient = null;
            m_pushClient = null;
            m_server = null;
        }

        [SetUp]
        protected void SetUp()
        {
            m_server.ResetLogFile();
        }

        [TearDown]
        protected async Task TearDownAsync()
        {
            await DisconnectGDSClientAsync().ConfigureAwait(false);
            await DisconnectPushClientAsync().ConfigureAwait(false);
            try
            {
                TestContext.AddTestAttachment(
                    m_server.GetLogFilePath(),
                    "GDS Client and Server logs");
            }
            catch
            {
            }
        }

        [Test]
        [Order(100)]
        public async Task GetSupportedKeyFormatsAsync()
        {
            await ConnectPushClientAsync(true).ConfigureAwait(false);
            string[] keyFormats = await m_pushClient.PushClient.GetSupportedKeyFormatsAsync().ConfigureAwait(false);
            Assert.IsNotNull(keyFormats);
        }

        [Test]
        [Order(200)]
        public async Task ReadTrustListAsync()
        {
            await ConnectPushClientAsync(true).ConfigureAwait(false);
            TrustListDataType allTrustList = await m_pushClient.PushClient.ReadTrustListAsync().ConfigureAwait(false);
            Assert.IsNotNull(allTrustList);
            Assert.IsNotNull(allTrustList.IssuerCertificates);
            Assert.IsNotNull(allTrustList.IssuerCrls);
            Assert.IsNotNull(allTrustList.TrustedCertificates);
            Assert.IsNotNull(allTrustList.TrustedCrls);
            TrustListDataType noneTrustList = await m_pushClient.PushClient
                .ReadTrustListAsync(TrustListMasks.None).ConfigureAwait(false);
            Assert.IsNotNull(noneTrustList);
            Assert.IsNotNull(noneTrustList.IssuerCertificates);
            Assert.IsNotNull(noneTrustList.IssuerCrls);
            Assert.IsNotNull(noneTrustList.TrustedCertificates);
            Assert.IsNotNull(noneTrustList.TrustedCrls);
            Assert.IsTrue(noneTrustList.IssuerCertificates.Count == 0);
            Assert.IsTrue(noneTrustList.IssuerCrls.Count == 0);
            Assert.IsTrue(noneTrustList.TrustedCertificates.Count == 0);
            Assert.IsTrue(noneTrustList.TrustedCrls.Count == 0);
            TrustListDataType issuerTrustList = await m_pushClient.PushClient.ReadTrustListAsync(
                (TrustListMasks)((int)TrustListMasks.IssuerCertificates |
                    (int)TrustListMasks.IssuerCrls)).ConfigureAwait(false);
            Assert.IsNotNull(issuerTrustList);
            Assert.IsNotNull(issuerTrustList.IssuerCertificates);
            Assert.IsNotNull(issuerTrustList.IssuerCrls);
            Assert.IsNotNull(issuerTrustList.TrustedCertificates);
            Assert.IsNotNull(issuerTrustList.TrustedCrls);
            Assert.IsTrue(
                issuerTrustList.IssuerCertificates.Count == allTrustList.IssuerCertificates.Count);
            Assert.IsTrue(issuerTrustList.IssuerCrls.Count == allTrustList.IssuerCrls.Count);
            Assert.IsTrue(issuerTrustList.TrustedCertificates.Count == 0);
            Assert.IsTrue(issuerTrustList.TrustedCrls.Count == 0);
            TrustListDataType trustedTrustList = await m_pushClient.PushClient.ReadTrustListAsync(
                (TrustListMasks)((int)TrustListMasks.TrustedCertificates |
                    (int)TrustListMasks.TrustedCrls)).ConfigureAwait(false);
            Assert.IsNotNull(trustedTrustList);
            Assert.IsNotNull(trustedTrustList.IssuerCertificates);
            Assert.IsNotNull(trustedTrustList.IssuerCrls);
            Assert.IsNotNull(trustedTrustList.TrustedCertificates);
            Assert.IsNotNull(trustedTrustList.TrustedCrls);
            Assert.IsTrue(trustedTrustList.IssuerCertificates.Count == 0);
            Assert.IsTrue(trustedTrustList.IssuerCrls.Count == 0);
            Assert.IsTrue(
                trustedTrustList.TrustedCertificates.Count == allTrustList.TrustedCertificates
                    .Count);
            Assert.IsTrue(trustedTrustList.TrustedCrls.Count == allTrustList.TrustedCrls.Count);
        }

        [Test]
        [Order(300)]
        public async Task UpdateTrustListAsync()
        {
            await ConnectPushClientAsync(true).ConfigureAwait(false);
            TrustListDataType fullTrustList = await m_pushClient.PushClient.ReadTrustListAsync().ConfigureAwait(false);
            TrustListDataType emptyTrustList = await m_pushClient.PushClient
                .ReadTrustListAsync(TrustListMasks.None).ConfigureAwait(false);
            emptyTrustList.SpecifiedLists = (uint)TrustListMasks.All;
            bool requireReboot = await m_pushClient.PushClient.UpdateTrustListAsync(emptyTrustList).ConfigureAwait(false);
            Assert.False(requireReboot);
            TrustListDataType expectEmptyTrustList = await m_pushClient.PushClient.ReadTrustListAsync().ConfigureAwait(false);
            Assert.IsTrue(Utils.IsEqual(expectEmptyTrustList, emptyTrustList));
            requireReboot = await m_pushClient.PushClient.UpdateTrustListAsync(fullTrustList).ConfigureAwait(false);
            Assert.False(requireReboot);
            TrustListDataType expectFullTrustList = await m_pushClient.PushClient.ReadTrustListAsync().ConfigureAwait(false);
            Assert.IsTrue(Utils.IsEqual(expectFullTrustList, fullTrustList));
        }

        [Test]
        [Order(301)]
        public async Task AddRemoveCertAsync()
        {
            using X509Certificate2 trustedCert = CertificateFactory
                .CreateCertificate("uri:x:y:z", "TrustedCert", "CN=Push Server Test", null)
                .CreateForRSA();
            using X509Certificate2 issuerCert = CertificateFactory
                .CreateCertificate("uri:x:y:z", "IssuerCert", "CN=Push Server Test", null)
                .CreateForRSA();
            await ConnectPushClientAsync(true).ConfigureAwait(false);
            TrustListDataType beforeTrustList = await m_pushClient.PushClient.ReadTrustListAsync().ConfigureAwait(false);
            await m_pushClient.PushClient.AddCertificateAsync(trustedCert, true).ConfigureAwait(false);
            await m_pushClient.PushClient.AddCertificateAsync(issuerCert, false).ConfigureAwait(false);
            TrustListDataType afterAddTrustList = await m_pushClient.PushClient.ReadTrustListAsync().ConfigureAwait(false);
            Assert.Greater(
                afterAddTrustList.TrustedCertificates.Count,
                beforeTrustList.TrustedCertificates.Count);
            Assert.Greater(
                afterAddTrustList.IssuerCertificates.Count,
                beforeTrustList.IssuerCertificates.Count);
            Assert.IsFalse(Utils.IsEqual(beforeTrustList, afterAddTrustList));
            await m_pushClient.PushClient.RemoveCertificateAsync(trustedCert.Thumbprint, true).ConfigureAwait(false);
            await m_pushClient.PushClient.RemoveCertificateAsync(issuerCert.Thumbprint, false).ConfigureAwait(false);
            TrustListDataType afterRemoveTrustList = await m_pushClient.PushClient.ReadTrustListAsync().ConfigureAwait(false);
            Assert.IsTrue(Utils.IsEqual(beforeTrustList, afterRemoveTrustList));
        }

        [Test]
        [Order(302)]
        public async Task AddRemoveCATrustedCertAsync()
        {
            await ConnectPushClientAsync(true).ConfigureAwait(false);
            TrustListDataType beforeTrustList = await m_pushClient.PushClient.ReadTrustListAsync().ConfigureAwait(false);
            await m_pushClient.PushClient.AddCertificateAsync(m_caCert, true).ConfigureAwait(false);
            TrustListDataType afterAddTrustList = await m_pushClient.PushClient.ReadTrustListAsync().ConfigureAwait(false);
            Assert.Greater(
                afterAddTrustList.TrustedCertificates.Count,
                beforeTrustList.TrustedCertificates.Count);
            Assert.AreEqual(afterAddTrustList.TrustedCrls.Count, beforeTrustList.TrustedCrls.Count);
            Assert.IsFalse(Utils.IsEqual(beforeTrustList, afterAddTrustList));
            ServiceResultException serviceResultException = NUnit.Framework.Assert
                .ThrowsAsync<ServiceResultException>(() =>
                    m_pushClient.PushClient.RemoveCertificateAsync(m_caCert.Thumbprint, false));
            Assert.AreEqual(
                (StatusCode)StatusCodes.BadInvalidArgument,
                (StatusCode)serviceResultException.StatusCode,
                serviceResultException.Message);
            TrustListDataType afterRemoveTrustList = await m_pushClient.PushClient.ReadTrustListAsync().ConfigureAwait(false);
            Assert.IsFalse(Utils.IsEqual(beforeTrustList, afterRemoveTrustList));
            await m_pushClient.PushClient.RemoveCertificateAsync(m_caCert.Thumbprint, true).ConfigureAwait(false);
            afterRemoveTrustList = await m_pushClient.PushClient.ReadTrustListAsync().ConfigureAwait(false);
            Assert.IsTrue(Utils.IsEqual(beforeTrustList, afterRemoveTrustList));
        }

        [Test]
        [Order(303)]
        public async Task AddRemoveCAIssuerCertAsync()
        {
            await ConnectPushClientAsync(true).ConfigureAwait(false);
            TrustListDataType beforeTrustList = await m_pushClient.PushClient.ReadTrustListAsync().ConfigureAwait(false);
            await m_pushClient.PushClient.AddCertificateAsync(m_caCert, false).ConfigureAwait(false);
            TrustListDataType afterAddTrustList = await m_pushClient.PushClient.ReadTrustListAsync().ConfigureAwait(false);
            Assert.Greater(
                afterAddTrustList.IssuerCertificates.Count,
                beforeTrustList.IssuerCertificates.Count);
            Assert.AreEqual(afterAddTrustList.IssuerCrls.Count, beforeTrustList.IssuerCrls.Count);
            Assert.IsFalse(Utils.IsEqual(beforeTrustList, afterAddTrustList));
            await NUnit.Framework.Assert.ThatAsync(
                () => m_pushClient.PushClient.RemoveCertificateAsync(m_caCert.Thumbprint, true),
                Throws.Exception).ConfigureAwait(false);
            TrustListDataType afterRemoveTrustList = await m_pushClient.PushClient.ReadTrustListAsync().ConfigureAwait(false);
            Assert.IsFalse(Utils.IsEqual(beforeTrustList, afterRemoveTrustList));
            await m_pushClient.PushClient.RemoveCertificateAsync(m_caCert.Thumbprint, false).ConfigureAwait(false);
            afterRemoveTrustList = await m_pushClient.PushClient.ReadTrustListAsync().ConfigureAwait(false);
            Assert.IsTrue(Utils.IsEqual(beforeTrustList, afterRemoveTrustList));
        }

        [Test]
        [Order(400)]
        public async Task CreateSigningRequestBadParmsAsync()
        {
            await ConnectPushClientAsync(true).ConfigureAwait(false);
            var invalidCertGroup = new NodeId(333);
            var invalidCertType = new NodeId(Guid.NewGuid());
            await NUnit.Framework.Assert.ThatAsync(
                () => m_pushClient.PushClient
                    .CreateSigningRequestAsync(invalidCertGroup, null, null, false, null),
                Throws.Exception).ConfigureAwait(false);
            await NUnit.Framework.Assert.ThatAsync(
                () => m_pushClient.PushClient
                    .CreateSigningRequestAsync(null, invalidCertType, null, false, null),
                Throws.Exception).ConfigureAwait(false);
            await NUnit.Framework.Assert.ThatAsync(
                () => m_pushClient.PushClient.CreateSigningRequestAsync(null, null, null, false, null),
                Throws.Exception).ConfigureAwait(false);
            await NUnit.Framework.Assert.ThatAsync(
                () =>
                    m_pushClient.PushClient
                        .CreateSigningRequestAsync(invalidCertGroup, invalidCertType, null, false, null),
                Throws.Exception).ConfigureAwait(false);
        }

        [Test]
        [Order(401)]
        public async Task CreateSigningRequestNullParmsAsync()
        {
            await ConnectPushClientAsync(true).ConfigureAwait(false);
            byte[] csr = await m_pushClient.PushClient
                .CreateSigningRequestAsync(null, m_certificateType, null, false, null).ConfigureAwait(false);
            Assert.IsNotNull(csr);
        }

        [Test]
        [Order(402)]
        public async Task CreateSigningRequestRsaMinNullParmsAsync()
        {
#if NETSTANDARD2_1 || NET5_0_OR_GREATER
            NUnit.Framework.Assert
                .Ignore("SHA1 not supported on .NET Standard 2.1 and .NET 5.0 or greater");
#endif
            await ConnectPushClientAsync(true).ConfigureAwait(false);
            await NUnit.Framework.Assert.ThatAsync(
                () =>
                    m_pushClient.PushClient.CreateSigningRequestAsync(
                        null,
                        OpcUa.ObjectTypeIds.RsaMinApplicationCertificateType,
                        null,
                        false,
                        null
                    ),
                Throws.Exception).ConfigureAwait(false);
        }

        [Test]
        [Order(409)]
        public async Task CreateSigningRequestAllParmsAsync()
        {
            await ConnectPushClientAsync(true).ConfigureAwait(false);
            byte[] nonce = [];
            byte[] csr = await m_pushClient.PushClient.CreateSigningRequestAsync(
                m_pushClient.PushClient.DefaultApplicationGroup,
                m_certificateType,
                string.Empty,
                false,
                nonce).ConfigureAwait(false);
            Assert.IsNotNull(csr);
        }

        [Test]
        [Order(410)]
        public async Task CreateSigningRequestNullParmsWithNewPrivateKeyAsync()
        {
            await ConnectPushClientAsync(true).ConfigureAwait(false);
            byte[] csr = await m_pushClient.PushClient.CreateSigningRequestAsync(
                null,
                m_certificateType,
                null,
                true,
                Encoding.ASCII.GetBytes("OPCTest")).ConfigureAwait(false);
            Assert.IsNotNull(csr);
        }

        [Test]
        [Order(419)]
        public async Task CreateSigningRequestAllParmsWithNewPrivateKeyAsync()
        {
            await ConnectPushClientAsync(true).ConfigureAwait(false);
            byte[] nonce = new byte[32];
            m_randomSource.NextBytes(nonce, 0, nonce.Length);
            byte[] csr = await m_pushClient.PushClient.CreateSigningRequestAsync(
                m_pushClient.PushClient.DefaultApplicationGroup,
                m_certificateType,
                string.Empty,
                true,
                nonce).ConfigureAwait(false);
            Assert.IsNotNull(csr);
        }

        [Test]
        [Order(500)]
        public async Task UpdateCertificateSelfSignedNoPrivateKeyAssertsAsync()
        {
            await ConnectPushClientAsync(true).ConfigureAwait(false);
            using X509Certificate2 invalidCert = CertificateFactory
                .CreateCertificate("uri:x:y:z", "TestApp", "CN=Push Server Test", null)
                .CreateForRSA();
            using X509Certificate2 serverCert = X509CertificateLoader.LoadCertificate(
                m_pushClient.PushClient.Session.ConfiguredEndpoint.Description.ServerCertificate);
            if (!X509Utils.CompareDistinguishedName(serverCert.Subject, serverCert.Issuer))
            {
                NUnit.Framework.Assert.Ignore("Server has no self signed cert in use.");
            }
            byte[] invalidRawCert = [0xba, 0xd0, 0xbe, 0xef, 3];
            // negative test all parameter combinations
            var invalidCertGroup = new NodeId(333);
            var invalidCertType = new NodeId(Guid.NewGuid());
            await NUnit.Framework.Assert.ThatAsync(
                () => m_pushClient.PushClient.UpdateCertificateAsync(null, null, null, null, null, null),
                Throws.Exception).ConfigureAwait(false);
            await NUnit.Framework.Assert.ThatAsync(
                () =>
                    m_pushClient.PushClient.UpdateCertificateAsync(
                        invalidCertGroup,
                        null,
                        serverCert.RawData,
                        null,
                        null,
                        null
                    ),
                Throws.Exception).ConfigureAwait(false);
            await NUnit.Framework.Assert.ThatAsync(
                () =>
                    m_pushClient.PushClient.UpdateCertificateAsync(
                        null,
                        invalidCertType,
                        serverCert.RawData,
                        null,
                        null,
                        null
                    ),
                Throws.Exception).ConfigureAwait(false);
            await NUnit.Framework.Assert.ThatAsync(
                () =>
                    m_pushClient.PushClient.UpdateCertificateAsync(
                        invalidCertGroup,
                        invalidCertType,
                        serverCert.RawData,
                        null,
                        null,
                        null
                    ),
                Throws.Exception).ConfigureAwait(false);
            await NUnit.Framework.Assert.ThatAsync(
                () => m_pushClient.PushClient
                    .UpdateCertificateAsync(null, null, invalidRawCert, null, null, null),
                Throws.Exception).ConfigureAwait(false);
            await NUnit.Framework.Assert.ThatAsync(
                () => m_pushClient.PushClient
                    .UpdateCertificateAsync(null, null, invalidCert.RawData, null, null, null),
                Throws.Exception).ConfigureAwait(false);
            await NUnit.Framework.Assert.ThatAsync(
                () => m_pushClient.PushClient
                    .UpdateCertificateAsync(null, null, serverCert.RawData, "XYZ", null, null),
                Throws.Exception).ConfigureAwait(false);
            await NUnit.Framework.Assert.ThatAsync(
                () =>
                    m_pushClient.PushClient.UpdateCertificateAsync(
                        null,
                        null,
                        serverCert.RawData,
                        "XYZ",
                        invalidCert.RawData,
                        null
                    ),
                Throws.Exception).ConfigureAwait(false);
            await NUnit.Framework.Assert.ThatAsync(
                () =>
                    m_pushClient.PushClient.UpdateCertificateAsync(
                        null,
                        null,
                        invalidCert.RawData,
                        null,
                        null,
                        [serverCert.RawData, invalidCert.RawData]
                    ),
                Throws.Exception).ConfigureAwait(false);
            await NUnit.Framework.Assert.ThatAsync(
                () =>
                    m_pushClient.PushClient.UpdateCertificateAsync(
                        null,
                        null,
                        null,
                        null,
                        null,
                        [serverCert.RawData, invalidCert.RawData]
                    ),
                Throws.Exception).ConfigureAwait(false);
            await NUnit.Framework.Assert.ThatAsync(
                () =>
                    m_pushClient.PushClient.UpdateCertificateAsync(
                        null,
                        null,
                        invalidRawCert,
                        null,
                        null,
                        [serverCert.RawData, invalidCert.RawData]
                    ),
                Throws.Exception).ConfigureAwait(false);
            await NUnit.Framework.Assert.ThatAsync(
                () =>
                    m_pushClient.PushClient.UpdateCertificateAsync(
                        null,
                        null,
                        serverCert.RawData,
                        null,
                        null,
                        [serverCert.RawData, invalidRawCert]
                    ),
                Throws.Exception).ConfigureAwait(false);
            await NUnit.Framework.Assert.ThatAsync(
                () => m_pushClient.PushClient
                    .UpdateCertificateAsync(null, null, serverCert.RawData, null, null, null),
                Throws.Exception).ConfigureAwait(false);
        }

        [Test]
        [Order(501)]
        public async Task UpdateCertificateSelfSignedNoPrivateKeyAsync()
        {
            if (m_certificateType != OpcUa.ObjectTypeIds.RsaSha256ApplicationCertificateType)
            {
                NUnit.Framework.Assert.Ignore("Test only supported for RSA");
            }
            await ConnectPushClientAsync(true).ConfigureAwait(false);
            using X509Certificate2 serverCert = X509CertificateLoader.LoadCertificate(
                m_pushClient.PushClient.Session.ConfiguredEndpoint.Description.ServerCertificate);
            if (!X509Utils.CompareDistinguishedName(serverCert.Subject, serverCert.Issuer))
            {
                NUnit.Framework.Assert.Ignore("Server has no self signed cert in use.");
            }
            bool success = await m_pushClient.PushClient.UpdateCertificateAsync(
                null,
                m_certificateType,
                serverCert.RawData,
                null,
                null,
                null).ConfigureAwait(false);
            if (success)
            {
                await m_pushClient.PushClient.ApplyChangesAsync().ConfigureAwait(false);
            }
            await VerifyNewPushServerCertAsync(serverCert.RawData).ConfigureAwait(false);
        }

        [Test]
        [Order(509)]
        public async Task UpdateCertificateCASignedRegeneratePrivateKeyAsync()
        {
            await UpdateCertificateCASignedAsync(true).ConfigureAwait(false);
        }

        [Test]
        [Order(510)]
        public async Task UpdateCertificateCASignedAsync()
        {
            await UpdateCertificateCASignedAsync(false).ConfigureAwait(false);
        }

        public async Task UpdateCertificateCASignedAsync(bool regeneratePrivateKey)
        {
            await ConnectPushClientAsync(true).ConfigureAwait(false);
            await ConnectGDSClientAsync(true).ConfigureAwait(false);
            TestContext.Out.WriteLine("Create Signing Request");
            byte[] csr = await m_pushClient.PushClient.CreateSigningRequestAsync(
                null,
                m_certificateType,
                m_selfSignedServerCert.Subject + "2",
                regeneratePrivateKey,
                null).ConfigureAwait(false);
            Assert.IsNotNull(csr);
            TestContext.Out.WriteLine("Start Signing Request");
            NodeId requestId = await m_gdsClient.GDSClient.StartSigningRequestAsync(
                m_applicationRecord.ApplicationId,
                null,
                m_certificateType,
                csr).ConfigureAwait(false);
            Assert.NotNull(requestId);
            byte[] privateKey = null;
            byte[] certificate = null;
            byte[][] issuerCertificates = null;

            Thread.Sleep(1000);

            DateTime now = DateTime.UtcNow;
            do
            {
                try
                {
                    TestContext.Out.WriteLine("Finish Signing Request");
                    (certificate, privateKey, issuerCertificates) = await m_gdsClient.GDSClient.FinishRequestAsync(
                        m_applicationRecord.ApplicationId,
                        requestId).ConfigureAwait(false);
                }
                catch (ServiceResultException sre)
                {
                    // wait if GDS requires manual approval of cert request
                    if (sre.StatusCode == StatusCodes.BadNothingToDo &&
                        now.AddMinutes(5) > DateTime.UtcNow)
                    {
                        Thread.Sleep(10000);
                    }
                    else
                    {
                        throw;
                    }
                }
            } while (certificate == null);
            Assert.NotNull(issuerCertificates);
            Assert.IsNull(privateKey);
            await DisconnectGDSClientAsync().ConfigureAwait(false);
            TestContext.Out.WriteLine("Update Certificate");
            bool success = await m_pushClient.PushClient.UpdateCertificateAsync(
                null,
                m_certificateType,
                certificate,
                null,
                null,
                issuerCertificates).ConfigureAwait(false);
            if (success)
            {
                TestContext.Out.WriteLine("Apply Changes");
                await m_pushClient.PushClient.ApplyChangesAsync().ConfigureAwait(false);
            }
            TestContext.Out.WriteLine("Verify Cert Update");
            await VerifyNewPushServerCertAsync(certificate).ConfigureAwait(false);
        }

        [Test]
        [Order(520)]
        public async Task UpdateCertificateSelfSignedPFXAsync()
        {
            await UpdateCertificateSelfSignedAsync("PFX").ConfigureAwait(false);
        }

        [Test]
        [Order(530)]
        public async Task UpdateCertificateSelfSignedPEMAsync()
        {
            await UpdateCertificateSelfSignedAsync("PEM").ConfigureAwait(false);
        }

        public async Task UpdateCertificateSelfSignedAsync(string keyFormat)
        {
            await ConnectPushClientAsync(true).ConfigureAwait(false);
            string[] keyFormats = await m_pushClient.PushClient.GetSupportedKeyFormatsAsync().ConfigureAwait(false);
            if (!keyFormats.Contains(keyFormat))
            {
                NUnit.Framework.Assert
                    .Ignore($"Push server doesn't support {keyFormat} key update");
            }

            X509Certificate2 newCert;

#if ECC_SUPPORT
            ECCurve? curve = EccUtils.GetCurveFromCertificateTypeId(m_certificateType);

            if (curve != null)
            {
                newCert = CertificateFactory
                    .CreateCertificate(
                        m_applicationRecord.ApplicationUri,
                        m_applicationRecord.ApplicationNames[0].Text,
                        m_selfSignedServerCert.Subject + "1",
                        null)
                    .SetECCurve(curve.Value)
                    .CreateForECDsa();
            }
            // RSA Certificate
            else
            {
#endif
                newCert = CertificateFactory
                    .CreateCertificate(
                        m_applicationRecord.ApplicationUri,
                        m_applicationRecord.ApplicationNames[0].Text,
                        m_selfSignedServerCert.Subject + "1",
                        null)
                    .CreateForRSA();
#if ECC_SUPPORT
            }
#endif

            byte[] privateKey = null;
            if (keyFormat == "PFX")
            {
                Assert.IsTrue(newCert.HasPrivateKey);
                privateKey = newCert.Export(X509ContentType.Pfx);
            }
            else if (keyFormat == "PEM")
            {
                Assert.IsTrue(newCert.HasPrivateKey);
                privateKey = PEMWriter.ExportPrivateKeyAsPEM(newCert);
            }
            else
            {
                NUnit.Framework.Assert.Fail($"Testing unsupported key format {keyFormat}.");
            }

            bool success = await m_pushClient.PushClient.UpdateCertificateAsync(
                m_pushClient.PushClient.DefaultApplicationGroup,
                m_certificateType,
                newCert.RawData,
                keyFormat,
                privateKey,
                null).ConfigureAwait(false);

            if (success)
            {
                await m_pushClient.PushClient.ApplyChangesAsync().ConfigureAwait(false);
            }
            await VerifyNewPushServerCertAsync(newCert.RawData).ConfigureAwait(false);
        }

        [Test]
        [Order(540)]
        public async Task UpdateCertificateNewKeyPairPFXAsync()
        {
            await UpdateCertificateWithNewKeyPairAsync("PFX").ConfigureAwait(false);
        }

        [Test]
        [Order(550)]
        public async Task UpdateCertificateNewKeyPairPEMAsync()
        {
            await UpdateCertificateWithNewKeyPairAsync("PEM").ConfigureAwait(false);
        }

        public async Task UpdateCertificateWithNewKeyPairAsync(string keyFormat)
        {
            await ConnectPushClientAsync(true).ConfigureAwait(false);
            string[] keyFormats = await m_pushClient.PushClient.GetSupportedKeyFormatsAsync().ConfigureAwait(false);
            if (!keyFormats.Contains(keyFormat))
            {
                NUnit.Framework.Assert
                    .Ignore($"Push server doesn't support {keyFormat} key update");
            }

            NodeId requestId = await m_gdsClient.GDSClient.StartNewKeyPairRequestAsync(
                m_applicationRecord.ApplicationId,
                null,
                m_certificateType,
                m_selfSignedServerCert.Subject + "3",
                m_domainNames,
                keyFormat,
                null).ConfigureAwait(false);

            Assert.NotNull(requestId);
            byte[] privateKey = null;
            byte[] certificate = null;
            byte[][] issuerCertificates = null;
            DateTime now = DateTime.UtcNow;
            do
            {
                try
                {
                    Thread.Sleep(500);
                    (certificate, privateKey, issuerCertificates) = await m_gdsClient.GDSClient.FinishRequestAsync(
                        m_applicationRecord.ApplicationId,
                        requestId).ConfigureAwait(false);
                }
                catch (ServiceResultException sre)
                {
                    // wait if GDS requires manual approval of cert request
                    if (sre.StatusCode == StatusCodes.BadNothingToDo &&
                        now.AddMinutes(5) > DateTime.UtcNow)
                    {
                        Thread.Sleep(10000);
                    }
                    else
                    {
                        throw;
                    }
                }
            } while (certificate == null);
            Assert.NotNull(issuerCertificates);
            Assert.NotNull(privateKey);
            await DisconnectGDSClientAsync().ConfigureAwait(false);

            bool success = await m_pushClient.PushClient.UpdateCertificateAsync(
                m_pushClient.PushClient.DefaultApplicationGroup,
                m_certificateType,
                certificate,
                keyFormat,
                privateKey,
                issuerCertificates).ConfigureAwait(false);
            if (success)
            {
                await m_pushClient.PushClient.ApplyChangesAsync().ConfigureAwait(false);
            }
            await VerifyNewPushServerCertAsync(certificate).ConfigureAwait(false);
        }

        [Test]
        [Order(600)]
        public async Task GetRejectedListAsync()
        {
            await ConnectPushClientAsync(true).ConfigureAwait(false);
            X509Certificate2Collection collection = await m_pushClient.PushClient.GetRejectedListAsync().ConfigureAwait(false);
            Assert.NotNull(collection);
        }

        [Test]
        [Order(610)]
        public async Task GetCertificatesAsync()
        {
            await ConnectPushClientAsync(true).ConfigureAwait(false);

            await NUnit.Framework.Assert.ThatAsync(
                () => m_pushClient.PushClient.GetCertificatesAsync(null),
                Throws.Exception).ConfigureAwait(false);

            (NodeId[] certificateTypeIds, byte[][] certificates) = await m_pushClient.PushClient.GetCertificatesAsync(
                m_pushClient.PushClient.DefaultApplicationGroup).ConfigureAwait(false);

            NUnit.Framework.Assert.That(certificateTypeIds.Length == certificates.Length);
            Assert.NotNull(certificates[0]);
            using X509Certificate2 x509 = X509CertificateLoader.LoadCertificate(certificates[0]);
            Assert.NotNull(x509);
        }

        [Test]
        [Order(700)]
        public async Task ApplyChangesAsync()
        {
            await ConnectPushClientAsync(true).ConfigureAwait(false);
            await m_pushClient.PushClient.ApplyChangesAsync().ConfigureAwait(false);
        }

        [Test]
        [Order(800)]
        public async Task VerifyNoUserAccessAsync()
        {
            await ConnectPushClientAsync(false).ConfigureAwait(false);
            await NUnit.Framework.Assert.ThatAsync(() => m_pushClient.PushClient.ApplyChangesAsync(), Throws.Exception).ConfigureAwait(false);
            await NUnit.Framework.Assert.ThatAsync(() => m_pushClient.PushClient.GetRejectedListAsync(), Throws.Exception).ConfigureAwait(false);
            await NUnit.Framework.Assert.ThatAsync(
                () => m_pushClient.PushClient.GetCertificatesAsync(null),
                Throws.Exception).ConfigureAwait(false);
            await NUnit.Framework.Assert.ThatAsync(
                () =>
                    m_pushClient.PushClient.UpdateCertificateAsync(
                        null,
                        null,
                        m_selfSignedServerCert.RawData,
                        null,
                        null,
                        null
                    ),
                Throws.Exception).ConfigureAwait(false);
            await NUnit.Framework.Assert.ThatAsync(
                () => m_pushClient.PushClient.CreateSigningRequestAsync(null, null, null, false, null),
                Throws.Exception).ConfigureAwait(false);
            await NUnit.Framework.Assert
                .ThatAsync(() => m_pushClient.PushClient.ReadTrustListAsync(), Throws.Exception).ConfigureAwait(false);
        }

        private async Task ConnectPushClientAsync(
            bool sysAdmin,
            [System.Runtime.CompilerServices.CallerMemberName] string memberName = "")
        {
            m_pushClient.PushClient.AdminCredentials = sysAdmin
                ? m_pushClient.SysAdminUser
                : m_pushClient.AppUser;
            await m_pushClient.ConnectAsync(m_securityPolicyUri).ConfigureAwait(false);
            TestContext.Progress.WriteLine($"GDS Push({sysAdmin}) Connected -- {memberName}");
        }

        private async Task DisconnectPushClientAsync()
        {
            await m_pushClient.PushClient.DisconnectAsync().ConfigureAwait(false);
        }

        private async Task ConnectGDSClientAsync(
            bool admin,
            [System.Runtime.CompilerServices.CallerMemberName] string memberName = "")
        {
            m_gdsClient.GDSClient.AdminCredentials = admin
                ? m_gdsClient.AdminUser
                : m_gdsClient.AppUser;
            await m_gdsClient.GDSClient.ConnectAsync(m_gdsClient.GDSClient.EndpointUrl)
                .ConfigureAwait(false);
            TestContext.Progress.WriteLine($"GDS Client({admin}) connected -- {memberName}");
        }

        private async Task DisconnectGDSClientAsync()
        {
            await m_gdsClient.GDSClient.DisconnectAsync().ConfigureAwait(false);
        }

        private async Task RegisterPushServerApplicationAsync(string discoveryUrl)
        {
            if (m_applicationRecord == null && discoveryUrl != null)
            {
                EndpointDescription endpointDescription = CoreClientUtils.SelectEndpoint(
                    m_gdsClient.Configuration,
                    discoveryUrl,
                    true);
                ApplicationDescription description = endpointDescription.Server;
                m_applicationRecord = new ApplicationRecordDataType
                {
                    ApplicationNames = [description.ApplicationName],
                    ApplicationUri = description.ApplicationUri,
                    ApplicationType = description.ApplicationType,
                    ProductUri = description.ProductUri,
                    DiscoveryUrls = description.DiscoveryUrls,
                    ServerCapabilities = ["NA"]
                };
            }
            Assert.IsNotNull(m_applicationRecord);
            Assert.IsNull(m_applicationRecord.ApplicationId);
            NodeId id = await m_gdsClient.GDSClient.RegisterApplicationAsync(m_applicationRecord).ConfigureAwait(false);
            Assert.IsNotNull(id);
            m_applicationRecord.ApplicationId = id;

            // add issuer and trusted certs to client stores
            NodeId trustListId = await m_gdsClient.GDSClient.GetTrustListAsync(id, null).ConfigureAwait(false);
            TrustListDataType trustList = await m_gdsClient.GDSClient.ReadTrustListAsync(trustListId).ConfigureAwait(false);
            bool result = AddTrustListToStoreAsync(
                m_gdsClient.Configuration.SecurityConfiguration,
                trustList).Result;
            Assert.IsTrue(result);
            result = AddTrustListToStoreAsync(m_pushClient.Config.SecurityConfiguration, trustList)
                .Result;
            Assert.IsTrue(result);
        }

        private async Task UnRegisterPushServerApplicationAsync()
        {
            await m_gdsClient.GDSClient.UnregisterApplicationAsync(m_applicationRecord.ApplicationId).ConfigureAwait(false);
            m_applicationRecord.ApplicationId = null;
        }

        private async Task VerifyNewPushServerCertAsync(byte[] certificateBlob)
        {
            await DisconnectPushClientAsync().ConfigureAwait(false);

            const int maxWaitSeconds = 10;
            const int retryIntervalMs = 2000;
            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed.TotalSeconds < maxWaitSeconds)
            {
                await m_gdsClient.GDSClient.ConnectAsync(m_gdsClient.GDSClient.EndpointUrl).ConfigureAwait(false);
                await m_pushClient.ConnectAsync(m_securityPolicyUri).ConfigureAwait(false);

                X509Certificate2 serverCertificate = Utils.ParseCertificateBlob(
                    m_pushClient.PushClient.Session.ConfiguredEndpoint.Description.ServerCertificate);

                if (Utils.IsEqual(serverCertificate.RawData, certificateBlob))
                {
                    // Success, exit early
                    return;
                }

                await DisconnectPushClientAsync().ConfigureAwait(false);
                await Task.Delay(retryIntervalMs).ConfigureAwait(false);
            }

            Assert.Fail("Server certificate did not match with the Certificate pushed by " +
                "the GDS within the allowed time.");
        }

        private static async Task<bool> AddTrustListToStoreAsync(
            SecurityConfiguration config,
            TrustListDataType trustList)
        {
            int masks = (int)trustList.SpecifiedLists;

            X509Certificate2Collection issuerCertificates = null;
            X509CRLCollection issuerCrls = null;
            X509Certificate2Collection trustedCertificates = null;
            X509CRLCollection trustedCrls = null;

            // test integrity of all CRLs
            if ((masks & (int)TrustListMasks.IssuerCertificates) != 0)
            {
                issuerCertificates = [];
                foreach (byte[] cert in trustList.IssuerCertificates)
                {
                    issuerCertificates.Add(X509CertificateLoader.LoadCertificate(cert));
                }
            }
            if ((masks & (int)TrustListMasks.IssuerCrls) != 0)
            {
                issuerCrls = [];
                foreach (byte[] crl in trustList.IssuerCrls)
                {
                    issuerCrls.Add(new X509CRL(crl));
                }
            }
            if ((masks & (int)TrustListMasks.TrustedCertificates) != 0)
            {
                trustedCertificates = [];
                foreach (byte[] cert in trustList.TrustedCertificates)
                {
                    trustedCertificates.Add(X509CertificateLoader.LoadCertificate(cert));
                }
            }
            if ((masks & (int)TrustListMasks.TrustedCrls) != 0)
            {
                trustedCrls = [];
                foreach (byte[] crl in trustList.TrustedCrls)
                {
                    trustedCrls.Add(new X509CRL(crl));
                }
            }

            // update store
            // test integrity of all CRLs
            int updateMasks = (int)TrustListMasks.None;
            if ((masks & (int)TrustListMasks.IssuerCertificates) != 0 &&
                await UpdateStoreCertificatesAsync(
                    config.TrustedIssuerCertificates,
                    issuerCertificates)
                    .ConfigureAwait(false))
            {
                updateMasks |= (int)TrustListMasks.IssuerCertificates;
            }
            if ((masks & (int)TrustListMasks.IssuerCrls) != 0 &&
                await UpdateStoreCrlsAsync(config.TrustedIssuerCertificates, issuerCrls)
                    .ConfigureAwait(false))
            {
                updateMasks |= (int)TrustListMasks.IssuerCrls;
            }
            if ((masks & (int)TrustListMasks.TrustedCertificates) != 0 &&
                await UpdateStoreCertificatesAsync(
                    config.TrustedPeerCertificates,
                    trustedCertificates)
                    .ConfigureAwait(false))
            {
                updateMasks |= (int)TrustListMasks.TrustedCertificates;
            }
            if ((masks & (int)TrustListMasks.TrustedCrls) != 0 &&
                await UpdateStoreCrlsAsync(config.TrustedPeerCertificates, trustedCrls)
                    .ConfigureAwait(false))
            {
                updateMasks |= (int)TrustListMasks.TrustedCrls;
            }

            return masks == updateMasks;
        }

        private static async Task<bool> UpdateStoreCrlsAsync(
            CertificateTrustList trustList,
            X509CRLCollection updatedCrls)
        {
            bool result = true;
            ICertificateStore store = null;
            try
            {
                store = trustList.OpenStore();
                X509CRLCollection storeCrls = await store.EnumerateCRLsAsync()
                    .ConfigureAwait(false);
                foreach (X509CRL crl in storeCrls)
                {
                    if (!updatedCrls.Remove(crl) &&
                        !await store.DeleteCRLAsync(crl).ConfigureAwait(false))
                    {
                        result = false;
                    }
                }
                foreach (X509CRL crl in updatedCrls)
                {
                    await store.AddCRLAsync(crl).ConfigureAwait(false);
                }
            }
            catch
            {
                result = false;
            }
            finally
            {
                store?.Close();
            }
            return result;
        }

        private static async Task<bool> UpdateStoreCertificatesAsync(
            CertificateTrustList trustList,
            X509Certificate2Collection updatedCerts)
        {
            bool result = true;
            ICertificateStore store = null;
            try
            {
                store = trustList.OpenStore();
                X509Certificate2Collection storeCerts = await store.EnumerateAsync()
                    .ConfigureAwait(false);
                foreach (X509Certificate2 cert in storeCerts)
                {
                    if (!updatedCerts.Contains(cert))
                    {
                        if (!store.DeleteAsync(cert.Thumbprint).Result)
                        {
                            result = false;
                        }
                    }
                    else
                    {
                        updatedCerts.Remove(cert);
                    }
                }
                foreach (X509Certificate2 cert in updatedCerts)
                {
                    await store.AddAsync(cert).ConfigureAwait(false);
                }
            }
            catch
            {
                result = false;
            }
            finally
            {
                store?.Close();
            }
            return result;
        }

        /// <summary>
        /// Create CA test certificates.
        /// </summary>
        private async Task CreateCATestCertsAsync(string tempStorePath)
        {
            var certificateStoreIdentifier = new CertificateStoreIdentifier(tempStorePath, false);
            Assert.IsTrue(EraseStore(certificateStoreIdentifier));
            const string subjectName = "CN=CA Test Cert, O=OPC Foundation";
#if ECC_SUPPORT
            ECCurve? curve = EccUtils.GetCurveFromCertificateTypeId(m_certificateType);

            if (curve != null)
            {
                m_caCert = await CertificateFactory
                    .CreateCertificate(null, null, subjectName, null)
                    .SetCAConstraint()
                    .SetECCurve(curve.Value)
                    .CreateForECDsa()
                    .AddToStoreAsync(certificateStoreIdentifier)
                    .ConfigureAwait(false);
            }
            // RSA Certificate
            else
            {
#endif
                m_caCert = await CertificateFactory
                    .CreateCertificate(null, null, subjectName, null)
                    .SetCAConstraint()
                    .CreateForRSA()
                    .AddToStoreAsync(certificateStoreIdentifier)
                    .ConfigureAwait(false);
#if ECC_SUPPORT
            }
#endif

            // initialize cert revocation list (CRL)
            X509CRL caCrl = await CertificateGroup
                .RevokeCertificateAsync(certificateStoreIdentifier, m_caCert)
                .ConfigureAwait(false);
        }

        private static bool EraseStore(CertificateStoreIdentifier storeIdentifier)
        {
            bool result = true;
            try
            {
                using ICertificateStore store = storeIdentifier.OpenStore();
                foreach (X509Certificate2 cert in store.EnumerateAsync().Result)
                {
                    if (!store.DeleteAsync(cert.Thumbprint).Result)
                    {
                        result = false;
                    }
                }
                foreach (X509CRL crl in store.EnumerateCRLsAsync().Result)
                {
                    if (!store.DeleteCRLAsync(crl).Result)
                    {
                        result = false;
                    }
                }
            }
            catch
            {
                result = false;
            }
            return result;
        }

        private const int kRandomStart = 1;
        private RandomSource m_randomSource;
        private GlobalDiscoveryTestServer m_server;
        private GlobalDiscoveryTestClient m_gdsClient;
        private ServerConfigurationPushTestClient m_pushClient;
        private ApplicationRecordDataType m_applicationRecord;
        private X509Certificate2 m_selfSignedServerCert;
        private string[] m_domainNames;
        private X509Certificate2 m_caCert;
        private readonly NodeId m_certificateType;
        private readonly string m_securityPolicyUri;
    }
}
