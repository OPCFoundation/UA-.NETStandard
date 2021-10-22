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
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Gds.Client;
using Opc.Ua.Gds.Server;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Test;
using OpcUa = Opc.Ua;

namespace Opc.Ua.Gds.Tests
{

    [TestFixture, Category("GDSPush"), Category("GDS")]
    [SetCulture("en-us"), SetUICulture("en-us")]
    [NonParallelizable]
    public class PushTest
    {
        #region Test Setup
        /// <summary>
        /// Set up a Global Discovery Server and Client instance and connect the session
        /// </summary>
        [OneTimeSetUp]
        protected async Task OneTimeSetUp()
        {
            // start GDS first clean, then restart server
            // to ensure the application cert is not 'fresh'
            m_server = await TestUtils.StartGDS(true).ConfigureAwait(false);
            m_server.StopServer();
            await Task.Delay(1000).ConfigureAwait(false);
            m_server = await TestUtils.StartGDS(false).ConfigureAwait(false);

            m_serverCapabilities = new ServerCapabilities();
            m_randomSource = new RandomSource(kRandomStart);
            m_dataGenerator = new DataGenerator(m_randomSource);

            // load clients
            m_gdsClient = new GlobalDiscoveryTestClient(true);
            await m_gdsClient.LoadClientConfiguration(m_server.BasePort).ConfigureAwait(false);
            m_pushClient = new ServerConfigurationPushTestClient(true);
            await m_pushClient.LoadClientConfiguration(m_server.BasePort).ConfigureAwait(false);

            // connect once
            await m_gdsClient.GDSClient.Connect(m_gdsClient.GDSClient.EndpointUrl).ConfigureAwait(false);
            await m_pushClient.PushClient.Connect(m_pushClient.PushClient.EndpointUrl).ConfigureAwait(false);

            ConnectGDSClient(true);
            RegisterPushServerApplication(m_pushClient.PushClient.EndpointUrl);

            m_selfSignedServerCert = new X509Certificate2(m_pushClient.PushClient.Session.ConfiguredEndpoint.Description.ServerCertificate);
            m_domainNames = X509Utils.GetDomainsFromCertficate(m_selfSignedServerCert).ToArray();

            await CreateCATestCerts(m_pushClient.TempStorePath).ConfigureAwait(false);
        }

        /// <summary>
        /// Tear down the Global Discovery Server and disconnect the Client
        /// </summary>
        [OneTimeTearDown]
        protected void OneTimeTearDown()
        {
            try
            {
                ConnectGDSClient(true);
                UnRegisterPushServerApplication();
                m_gdsClient.DisconnectClient();
                m_pushClient.DisconnectClient();
                m_server.StopServer();
            }
            catch { }
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
        protected void TearDown()
        {
            DisconnectGDSClient();
            DisconnectPushClient();
            try
            {
                TestContext.AddTestAttachment(m_server.GetLogFilePath(), "GDS Client and Server logs");
            }
            catch { }

        }
        #endregion

        #region Test Methods
        [Test, Order(100)]
        public void GetSupportedKeyFormats()
        {
            ConnectPushClient(true);
            var keyFormats = m_pushClient.PushClient.GetSupportedKeyFormats();
            Assert.IsNotNull(keyFormats);
        }

        [Test, Order(200)]
        public void ReadTrustList()
        {
            ConnectPushClient(true);
            TrustListDataType allTrustList = m_pushClient.PushClient.ReadTrustList();
            Assert.IsNotNull(allTrustList);
            Assert.IsNotNull(allTrustList.IssuerCertificates);
            Assert.IsNotNull(allTrustList.IssuerCrls);
            Assert.IsNotNull(allTrustList.TrustedCertificates);
            Assert.IsNotNull(allTrustList.TrustedCrls);
            TrustListDataType noneTrustList = m_pushClient.PushClient.ReadTrustList(TrustListMasks.None);
            Assert.IsNotNull(noneTrustList);
            Assert.IsNotNull(noneTrustList.IssuerCertificates);
            Assert.IsNotNull(noneTrustList.IssuerCrls);
            Assert.IsNotNull(noneTrustList.TrustedCertificates);
            Assert.IsNotNull(noneTrustList.TrustedCrls);
            Assert.IsTrue(noneTrustList.IssuerCertificates.Count == 0);
            Assert.IsTrue(noneTrustList.IssuerCrls.Count == 0);
            Assert.IsTrue(noneTrustList.TrustedCertificates.Count == 0);
            Assert.IsTrue(noneTrustList.TrustedCrls.Count == 0);
            TrustListDataType issuerTrustList = m_pushClient.PushClient.ReadTrustList(TrustListMasks.IssuerCertificates | TrustListMasks.IssuerCrls);
            Assert.IsNotNull(issuerTrustList);
            Assert.IsNotNull(issuerTrustList.IssuerCertificates);
            Assert.IsNotNull(issuerTrustList.IssuerCrls);
            Assert.IsNotNull(issuerTrustList.TrustedCertificates);
            Assert.IsNotNull(issuerTrustList.TrustedCrls);
            Assert.IsTrue(issuerTrustList.IssuerCertificates.Count == allTrustList.IssuerCertificates.Count);
            Assert.IsTrue(issuerTrustList.IssuerCrls.Count == allTrustList.IssuerCrls.Count);
            Assert.IsTrue(issuerTrustList.TrustedCertificates.Count == 0);
            Assert.IsTrue(issuerTrustList.TrustedCrls.Count == 0);
            TrustListDataType trustedTrustList = m_pushClient.PushClient.ReadTrustList(TrustListMasks.TrustedCertificates | TrustListMasks.TrustedCrls);
            Assert.IsNotNull(trustedTrustList);
            Assert.IsNotNull(trustedTrustList.IssuerCertificates);
            Assert.IsNotNull(trustedTrustList.IssuerCrls);
            Assert.IsNotNull(trustedTrustList.TrustedCertificates);
            Assert.IsNotNull(trustedTrustList.TrustedCrls);
            Assert.IsTrue(trustedTrustList.IssuerCertificates.Count == 0);
            Assert.IsTrue(trustedTrustList.IssuerCrls.Count == 0);
            Assert.IsTrue(trustedTrustList.TrustedCertificates.Count == allTrustList.TrustedCertificates.Count);
            Assert.IsTrue(trustedTrustList.TrustedCrls.Count == allTrustList.TrustedCrls.Count);
        }

        [Test, Order(300)]
        public void UpdateTrustList()
        {
            ConnectPushClient(true);
            TrustListDataType fullTrustList = m_pushClient.PushClient.ReadTrustList();
            TrustListDataType emptyTrustList = m_pushClient.PushClient.ReadTrustList(TrustListMasks.None);
            emptyTrustList.SpecifiedLists = (uint)TrustListMasks.All;
            bool requireReboot = m_pushClient.PushClient.UpdateTrustList(emptyTrustList);
            Assert.False(requireReboot);
            TrustListDataType expectEmptyTrustList = m_pushClient.PushClient.ReadTrustList();
            Assert.IsTrue(Utils.IsEqual(expectEmptyTrustList, emptyTrustList));
            requireReboot = m_pushClient.PushClient.UpdateTrustList(fullTrustList);
            Assert.False(requireReboot);
            TrustListDataType expectFullTrustList = m_pushClient.PushClient.ReadTrustList();
            Assert.IsTrue(Utils.IsEqual(expectFullTrustList, fullTrustList));
        }

        [Test, Order(301)]
        public void AddRemoveCert()
        {
            using (X509Certificate2 trustedCert = CertificateFactory.CreateCertificate("uri:x:y:z", "TrustedCert", "CN=Push Server Test", null).CreateForRSA())
            using (X509Certificate2 issuerCert = CertificateFactory.CreateCertificate("uri:x:y:z", "IssuerCert", "CN=Push Server Test", null).CreateForRSA())
            {
                ConnectPushClient(true);
                TrustListDataType beforeTrustList = m_pushClient.PushClient.ReadTrustList();
                m_pushClient.PushClient.AddCertificate(trustedCert, true);
                m_pushClient.PushClient.AddCertificate(issuerCert, false);
                TrustListDataType afterAddTrustList = m_pushClient.PushClient.ReadTrustList();
                Assert.Greater(afterAddTrustList.TrustedCertificates.Count, beforeTrustList.TrustedCertificates.Count);
                Assert.Greater(afterAddTrustList.IssuerCertificates.Count, beforeTrustList.IssuerCertificates.Count);
                Assert.IsFalse(Utils.IsEqual(beforeTrustList, afterAddTrustList));
                m_pushClient.PushClient.RemoveCertificate(trustedCert.Thumbprint, true);
                m_pushClient.PushClient.RemoveCertificate(issuerCert.Thumbprint, false);
                TrustListDataType afterRemoveTrustList = m_pushClient.PushClient.ReadTrustList();
                Assert.IsTrue(Utils.IsEqual(beforeTrustList, afterRemoveTrustList));
            }
        }

        [Test, Order(302)]
        public void AddRemoveCATrustedCert()
        {
            ConnectPushClient(true);
            TrustListDataType beforeTrustList = m_pushClient.PushClient.ReadTrustList();
            m_pushClient.PushClient.AddCertificate(m_caCert, true);
            TrustListDataType afterAddTrustList = m_pushClient.PushClient.ReadTrustList();
            Assert.Greater(afterAddTrustList.TrustedCertificates.Count, beforeTrustList.TrustedCertificates.Count);
            Assert.AreEqual(afterAddTrustList.TrustedCrls.Count, beforeTrustList.TrustedCrls.Count);
            Assert.IsFalse(Utils.IsEqual(beforeTrustList, afterAddTrustList));
            var serviceResultException = Assert.Throws<ServiceResultException>(() => { m_pushClient.PushClient.RemoveCertificate(m_caCert.Thumbprint, false); });
            Assert.AreEqual(StatusCodes.BadInvalidArgument, serviceResultException.StatusCode, serviceResultException.Message);
            TrustListDataType afterRemoveTrustList = m_pushClient.PushClient.ReadTrustList();
            Assert.IsFalse(Utils.IsEqual(beforeTrustList, afterRemoveTrustList));
            m_pushClient.PushClient.RemoveCertificate(m_caCert.Thumbprint, true);
            afterRemoveTrustList = m_pushClient.PushClient.ReadTrustList();
            Assert.IsTrue(Utils.IsEqual(beforeTrustList, afterRemoveTrustList));
        }

        [Test, Order(303)]
        public void AddRemoveCAIssuerCert()
        {
            ConnectPushClient(true);
            TrustListDataType beforeTrustList = m_pushClient.PushClient.ReadTrustList();
            m_pushClient.PushClient.AddCertificate(m_caCert, false);
            TrustListDataType afterAddTrustList = m_pushClient.PushClient.ReadTrustList();
            Assert.Greater(afterAddTrustList.IssuerCertificates.Count, beforeTrustList.IssuerCertificates.Count);
            Assert.AreEqual(afterAddTrustList.IssuerCrls.Count, beforeTrustList.IssuerCrls.Count);
            Assert.IsFalse(Utils.IsEqual(beforeTrustList, afterAddTrustList));
            Assert.That(() => { m_pushClient.PushClient.RemoveCertificate(m_caCert.Thumbprint, true); }, Throws.Exception);
            TrustListDataType afterRemoveTrustList = m_pushClient.PushClient.ReadTrustList();
            Assert.IsFalse(Utils.IsEqual(beforeTrustList, afterRemoveTrustList));
            m_pushClient.PushClient.RemoveCertificate(m_caCert.Thumbprint, false);
            afterRemoveTrustList = m_pushClient.PushClient.ReadTrustList();
            Assert.IsTrue(Utils.IsEqual(beforeTrustList, afterRemoveTrustList));
        }


        [Test, Order(400)]
        public void CreateSigningRequestBadParms()
        {
            ConnectPushClient(true);
            NodeId invalidCertGroup = new NodeId(333);
            NodeId invalidCertType = new NodeId(Guid.NewGuid());
            Assert.That(() => { m_pushClient.PushClient.CreateSigningRequest(invalidCertGroup, null, null, false, null); }, Throws.Exception);
            Assert.That(() => { m_pushClient.PushClient.CreateSigningRequest(null, invalidCertType, null, false, null); }, Throws.Exception);
            Assert.That(() => { m_pushClient.PushClient.CreateSigningRequest(null, null, null, false, null); }, Throws.Exception);
            Assert.That(() => { m_pushClient.PushClient.CreateSigningRequest(invalidCertGroup, invalidCertType, null, false, null); }, Throws.Exception);
        }

        [Test, Order(401)]
        public void CreateSigningRequestNullParms()
        {
            ConnectPushClient(true);
            byte[] csr = m_pushClient.PushClient.CreateSigningRequest(null, m_pushClient.PushClient.ApplicationCertificateType, null, false, null);
            Assert.IsNotNull(csr);
        }

        [Test, Order(402)]
        public void CreateSigningRequestRsaMinNullParms()
        {
#if NETSTANDARD2_1 || NET5_0
            Assert.Ignore("SHA1 not supported on .NET Standard 2.1.");
#endif
            ConnectPushClient(true);
            Assert.That(() => { m_pushClient.PushClient.CreateSigningRequest(null, OpcUa.ObjectTypeIds.RsaMinApplicationCertificateType, null, false, null); }, Throws.Exception);
        }

        [Test, Order(409)]
        public void CreateSigningRequestAllParms()
        {
            ConnectPushClient(true);
            byte[] nonce = Array.Empty<byte>();
            byte[] csr = m_pushClient.PushClient.CreateSigningRequest(
                m_pushClient.PushClient.DefaultApplicationGroup,
                m_pushClient.PushClient.ApplicationCertificateType,
                "",
                false,
                nonce);
            Assert.IsNotNull(csr);
        }

        [Test, Order(410)]
        public void CreateSigningRequestNullParmsWithNewPrivateKey()
        {
            ConnectPushClient(true);
            byte[] csr = m_pushClient.PushClient.CreateSigningRequest(null, m_pushClient.PushClient.ApplicationCertificateType, null, true, Encoding.ASCII.GetBytes("OPCTest"));
            Assert.IsNotNull(csr);
        }

        [Test, Order(419)]
        public void CreateSigningRequestAllParmsWithNewPrivateKey()
        {
            ConnectPushClient(true);
            byte[] nonce = new byte[32];
            m_randomSource.NextBytes(nonce, 0, nonce.Length);
            byte[] csr = m_pushClient.PushClient.CreateSigningRequest(
                m_pushClient.PushClient.DefaultApplicationGroup,
                m_pushClient.PushClient.ApplicationCertificateType,
                "",
                true,
                nonce);
            Assert.IsNotNull(csr);
        }

        [Test, Order(500)]
        public void UpdateCertificateSelfSignedNoPrivateKeyAsserts()
        {
            ConnectPushClient(true);
            using (X509Certificate2 invalidCert = CertificateFactory.CreateCertificate("uri:x:y:z", "TestApp", "CN=Push Server Test", null).CreateForRSA())
            using (X509Certificate2 serverCert = new X509Certificate2(m_pushClient.PushClient.Session.ConfiguredEndpoint.Description.ServerCertificate))
            {
                if (!X509Utils.CompareDistinguishedName(serverCert.Subject, serverCert.Issuer))
                {
                    Assert.Ignore("Server has no self signed cert in use.");
                }
                byte[] invalidRawCert = { 0xba, 0xd0, 0xbe, 0xef, 3 };
                // negative test all parameter combinations
                NodeId invalidCertGroup = new NodeId(333);
                NodeId invalidCertType = new NodeId(Guid.NewGuid());
                Assert.That(() => { m_pushClient.PushClient.UpdateCertificate(null, null, null, null, null, null); }, Throws.Exception);
                Assert.That(() => { m_pushClient.PushClient.UpdateCertificate(invalidCertGroup, null, serverCert.RawData, null, null, null); }, Throws.Exception);
                Assert.That(() => { m_pushClient.PushClient.UpdateCertificate(null, invalidCertType, serverCert.RawData, null, null, null); }, Throws.Exception);
                Assert.That(() => { m_pushClient.PushClient.UpdateCertificate(invalidCertGroup, invalidCertType, serverCert.RawData, null, null, null); }, Throws.Exception);
                Assert.That(() => { m_pushClient.PushClient.UpdateCertificate(null, null, invalidRawCert, null, null, null); }, Throws.Exception);
                Assert.That(() => { m_pushClient.PushClient.UpdateCertificate(null, null, invalidCert.RawData, null, null, null); }, Throws.Exception);
                Assert.That(() => { m_pushClient.PushClient.UpdateCertificate(null, null, serverCert.RawData, "XYZ", null, null); }, Throws.Exception);
                Assert.That(() => { m_pushClient.PushClient.UpdateCertificate(null, null, serverCert.RawData, "XYZ", invalidCert.RawData, null); }, Throws.Exception);
                Assert.That(() => { m_pushClient.PushClient.UpdateCertificate(null, null, invalidCert.RawData, null, null, new byte[][] { serverCert.RawData, invalidCert.RawData }); }, Throws.Exception);
                Assert.That(() => { m_pushClient.PushClient.UpdateCertificate(null, null, null, null, null, new byte[][] { serverCert.RawData, invalidCert.RawData }); }, Throws.Exception);
                Assert.That(() => { m_pushClient.PushClient.UpdateCertificate(null, null, invalidRawCert, null, null, new byte[][] { serverCert.RawData, invalidCert.RawData }); }, Throws.Exception);
                Assert.That(() => { m_pushClient.PushClient.UpdateCertificate(null, null, serverCert.RawData, null, null, new byte[][] { serverCert.RawData, invalidRawCert }); }, Throws.Exception);
                Assert.That(() => { m_pushClient.PushClient.UpdateCertificate(null, null, serverCert.RawData, null, null, null); }, Throws.Exception);
            }
        }

        [Test, Order(501)]
        public void UpdateCertificateSelfSignedNoPrivateKey()
        {
            ConnectPushClient(true);
            using (X509Certificate2 serverCert = new X509Certificate2(m_pushClient.PushClient.Session.ConfiguredEndpoint.Description.ServerCertificate))
            {
                if (!X509Utils.CompareDistinguishedName(serverCert.Subject, serverCert.Issuer))
                {
                    Assert.Ignore("Server has no self signed cert in use.");
                }
                var success = m_pushClient.PushClient.UpdateCertificate(
                    null,
                    m_pushClient.PushClient.ApplicationCertificateType,
                    serverCert.RawData,
                    null,
                    null,
                    null);
                if (success)
                {
                    m_pushClient.PushClient.ApplyChanges();
                }
                VerifyNewPushServerCert(serverCert.RawData);
            }
        }

        [Test, Order(510)]
        public void UpdateCertificateCASigned()
        {
#if NETCOREAPP3_1_OR_GREATER
            // this test fails on macOS, ignore
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Assert.Ignore("Update CA signed certificate fails on mac OS.");
            }
#endif
            ConnectPushClient(true);
            ConnectGDSClient(true);
            TestContext.Out.WriteLine("Create Signing Request");
            byte[] csr = m_pushClient.PushClient.CreateSigningRequest(
                null,
                m_pushClient.PushClient.ApplicationCertificateType,
                null,
                false,
                null);
            Assert.IsNotNull(csr);
            TestContext.Out.WriteLine("Start Signing Request");
            NodeId requestId = m_gdsClient.GDSClient.StartSigningRequest(
                _applicationRecord.ApplicationId,
                null,
                null,
                csr);
            Assert.NotNull(requestId);
            byte[] privateKey = null;
            byte[] certificate = null;
            byte[][] issuerCertificates = null;
            DateTime now = DateTime.UtcNow;
            do
            {
                try
                {
                    TestContext.Out.WriteLine("Finish Signing Request");
                    certificate = m_gdsClient.GDSClient.FinishRequest(
                        _applicationRecord.ApplicationId,
                        requestId,
                        out privateKey,
                        out issuerCertificates);
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
            DisconnectGDSClient();
            TestContext.Out.WriteLine("Update Certificate");
            bool success = m_pushClient.PushClient.UpdateCertificate(
                null,
                m_pushClient.PushClient.ApplicationCertificateType,
                certificate,
                null,
                null,
                issuerCertificates);
            if (success)
            {
                TestContext.Out.WriteLine("Apply Changes");
                m_pushClient.PushClient.ApplyChanges();
            }
            TestContext.Out.WriteLine("Verify Cert Update");
            VerifyNewPushServerCert(certificate);
        }


        [Test, Order(520)]
        public void UpdateCertificateSelfSignedPFX()
        {
            UpdateCertificateSelfSigned("PFX");
        }

        [Test, Order(530)]
        public void UpdateCertificateSelfSignedPEM()
        {
            UpdateCertificateSelfSigned("PEM");
        }

        public void UpdateCertificateSelfSigned(string keyFormat)
        {
            ConnectPushClient(true);
            var keyFormats = m_pushClient.PushClient.GetSupportedKeyFormats();
            if (!keyFormats.Contains(keyFormat))
            {
                Assert.Ignore("Push server doesn't support {0} key update", keyFormat);
            }

            X509Certificate2 newCert = CertificateFactory.CreateCertificate(
                _applicationRecord.ApplicationUri,
                _applicationRecord.ApplicationNames[0].Text,
                m_selfSignedServerCert.Subject,
                null).CreateForRSA();

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
                Assert.Fail("Testing unsupported key format {0}.", keyFormat);
            }

            var success = m_pushClient.PushClient.UpdateCertificate(
                m_pushClient.PushClient.DefaultApplicationGroup,
                m_pushClient.PushClient.ApplicationCertificateType,
                newCert.RawData,
                keyFormat,
                privateKey,
                null);

            if (success)
            {
                m_pushClient.PushClient.ApplyChanges();
            }
            VerifyNewPushServerCert(newCert.RawData);
        }

        [Test, Order(540)]
        public void UpdateCertificateNewKeyPairPFX()
        {
            UpdateCertificateWithNewKeyPair("PFX");
        }

        [Test, Order(550)]
        public void UpdateCertificateNewKeyPairPEM()
        {
            UpdateCertificateWithNewKeyPair("PEM");
        }

        public void UpdateCertificateWithNewKeyPair(string keyFormat)
        {
            ConnectPushClient(true);
            var keyFormats = m_pushClient.PushClient.GetSupportedKeyFormats();
            if (!keyFormats.Contains(keyFormat))
            {
                Assert.Ignore("Push server doesn't support {0} key update", keyFormat);
            }

            NodeId requestId = m_gdsClient.GDSClient.StartNewKeyPairRequest(
                _applicationRecord.ApplicationId,
                null,
                null,
                m_selfSignedServerCert.Subject,
                m_domainNames,
                keyFormat,
                null);

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
                    certificate = m_gdsClient.GDSClient.FinishRequest(
                        _applicationRecord.ApplicationId,
                        requestId,
                        out privateKey,
                        out issuerCertificates);
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
            DisconnectGDSClient();

            var success = m_pushClient.PushClient.UpdateCertificate(
                m_pushClient.PushClient.DefaultApplicationGroup,
                m_pushClient.PushClient.ApplicationCertificateType,
                certificate,
                keyFormat,
                privateKey,
                issuerCertificates);
            if (success)
            {
                m_pushClient.PushClient.ApplyChanges();
            }
            VerifyNewPushServerCert(certificate);
        }

        [Test, Order(600)]
        public void GetRejectedList()
        {
            ConnectPushClient(true);
            var collection = m_pushClient.PushClient.GetRejectedList();
            Assert.NotNull(collection);
        }

        [Test, Order(700)]
        public void ApplyChanges()
        {
            ConnectPushClient(true);
            m_pushClient.PushClient.ApplyChanges();
        }

        [Test, Order(800)]
        public void VerifyNoUserAccess()
        {
            ConnectPushClient(false);
            Assert.That(() => { m_pushClient.PushClient.ApplyChanges(); }, Throws.Exception);
            Assert.That(() => { m_pushClient.PushClient.GetRejectedList(); }, Throws.Exception);
            Assert.That(() => { m_pushClient.PushClient.UpdateCertificate(null, null, m_selfSignedServerCert.RawData, null, null, null); }, Throws.Exception);
            Assert.That(() => { m_pushClient.PushClient.CreateSigningRequest(null, null, null, false, null); }, Throws.Exception);
            Assert.That(() => { m_pushClient.PushClient.ReadTrustList(); }, Throws.Exception);
        }
        #endregion

        #region Private Methods
        private void ConnectPushClient(bool sysAdmin,
            [System.Runtime.CompilerServices.CallerMemberName] string memberName = ""
            )
        {
            m_pushClient.PushClient.AdminCredentials = sysAdmin ? m_pushClient.SysAdminUser : m_pushClient.AppUser;
            m_pushClient.PushClient.Connect(m_pushClient.PushClient.EndpointUrl).Wait();
            TestContext.Progress.WriteLine($"GDS Push({sysAdmin}) Connected -- {memberName}");
        }

        private void DisconnectPushClient()
        {
            m_pushClient.PushClient.Disconnect();
        }

        private void ConnectGDSClient(bool admin,
            [System.Runtime.CompilerServices.CallerMemberName] string memberName = ""
            )
        {
            m_gdsClient.GDSClient.AdminCredentials = admin ? m_gdsClient.AdminUser : m_gdsClient.AppUser;
            m_gdsClient.GDSClient.Connect(m_gdsClient.GDSClient.EndpointUrl).Wait();
            TestContext.Progress.WriteLine($"GDS Client({admin}) connected -- {memberName}");
        }

        private void DisconnectGDSClient()
        {
            m_gdsClient.GDSClient.Disconnect();
        }

        private X509Certificate2Collection CreateCertCollection(ByteStringCollection certList)
        {
            var result = new X509Certificate2Collection();
            foreach (var rawCert in certList)
            {
                result.Add(new X509Certificate2(rawCert));
            }
            return result;
        }

        private void RegisterPushServerApplication(string discoveryUrl)
        {
            if (_applicationRecord == null && discoveryUrl != null)
            {
                EndpointDescription endpointDescription = CoreClientUtils.SelectEndpoint(m_gdsClient.Configuration, discoveryUrl, true);
                ApplicationDescription description = endpointDescription.Server;
                _applicationRecord = new ApplicationRecordDataType {
                    ApplicationNames = new LocalizedTextCollection { description.ApplicationName },
                    ApplicationUri = description.ApplicationUri,
                    ApplicationType = description.ApplicationType,
                    ProductUri = description.ProductUri,
                    DiscoveryUrls = description.DiscoveryUrls,
                    ServerCapabilities = new StringCollection { "NA" },
                };
            }
            Assert.IsNotNull(_applicationRecord);
            Assert.IsNull(_applicationRecord.ApplicationId);
            NodeId id = m_gdsClient.GDSClient.RegisterApplication(_applicationRecord);
            Assert.IsNotNull(id);
            _applicationRecord.ApplicationId = id;

            // add issuer and trusted certs to client stores
            NodeId trustListId = m_gdsClient.GDSClient.GetTrustList(id, null);
            var trustList = m_gdsClient.GDSClient.ReadTrustList(trustListId);
            AddTrustListToStore(m_gdsClient.Configuration.SecurityConfiguration, trustList);
            AddTrustListToStore(m_pushClient.Config.SecurityConfiguration, trustList);
        }

        private void UnRegisterPushServerApplication()
        {
            m_gdsClient.GDSClient.UnregisterApplication(_applicationRecord.ApplicationId);
            _applicationRecord.ApplicationId = null;
        }

        private void VerifyNewPushServerCert(byte[] certificate)
        {
            DisconnectPushClient();
            Thread.Sleep(2000);
            m_gdsClient.GDSClient.Connect(m_gdsClient.GDSClient.EndpointUrl).Wait();
            m_pushClient.PushClient.Connect(m_pushClient.PushClient.EndpointUrl).Wait();
            Assert.AreEqual(
                certificate,
                m_pushClient.PushClient.Session.ConfiguredEndpoint.Description.ServerCertificate
                );
        }

        private bool AddTrustListToStore(SecurityConfiguration config, TrustListDataType trustList)
        {
            TrustListMasks masks = (TrustListMasks)trustList.SpecifiedLists;

            X509Certificate2Collection issuerCertificates = null;
            List<X509CRL> issuerCrls = null;
            X509Certificate2Collection trustedCertificates = null;
            List<X509CRL> trustedCrls = null;

            // test integrity of all CRLs
            if ((masks & TrustListMasks.IssuerCertificates) != 0)
            {
                issuerCertificates = new X509Certificate2Collection();
                foreach (var cert in trustList.IssuerCertificates)
                {
                    issuerCertificates.Add(new X509Certificate2(cert));
                }
            }
            if ((masks & TrustListMasks.IssuerCrls) != 0)
            {
                issuerCrls = new List<X509CRL>();
                foreach (var crl in trustList.IssuerCrls)
                {
                    issuerCrls.Add(new X509CRL(crl));
                }
            }
            if ((masks & TrustListMasks.TrustedCertificates) != 0)
            {
                trustedCertificates = new X509Certificate2Collection();
                foreach (var cert in trustList.TrustedCertificates)
                {
                    trustedCertificates.Add(new X509Certificate2(cert));
                }
            }
            if ((masks & TrustListMasks.TrustedCrls) != 0)
            {
                trustedCrls = new List<X509CRL>();
                foreach (var crl in trustList.TrustedCrls)
                {
                    trustedCrls.Add(new X509CRL(crl));
                }
            }

            // update store
            // test integrity of all CRLs
            TrustListMasks updateMasks = TrustListMasks.None;
            if ((masks & TrustListMasks.IssuerCertificates) != 0)
            {
                if (UpdateStoreCertificates(config.TrustedIssuerCertificates.StorePath, issuerCertificates))
                {
                    updateMasks |= TrustListMasks.IssuerCertificates;
                }
            }
            if ((masks & TrustListMasks.IssuerCrls) != 0)
            {
                if (UpdateStoreCrls(config.TrustedIssuerCertificates.StorePath, issuerCrls))
                {
                    updateMasks |= TrustListMasks.IssuerCrls;
                }
            }
            if ((masks & TrustListMasks.TrustedCertificates) != 0)
            {
                if (UpdateStoreCertificates(config.TrustedPeerCertificates.StorePath, trustedCertificates))
                {
                    updateMasks |= TrustListMasks.TrustedCertificates;
                }
            }
            if ((masks & TrustListMasks.TrustedCrls) != 0)
            {
                if (UpdateStoreCrls(config.TrustedPeerCertificates.StorePath, trustedCrls))
                {
                    updateMasks |= TrustListMasks.TrustedCrls;
                }
            }

            return masks == updateMasks;
        }

        private bool UpdateStoreCrls(
            string storePath,
            IList<X509CRL> updatedCrls)
        {
            bool result = true;
            try
            {
                using (ICertificateStore store = CertificateStoreIdentifier.OpenStore(storePath))
                {
                    var storeCrls = store.EnumerateCRLs();
                    foreach (var crl in storeCrls)
                    {
                        if (!updatedCrls.Contains(crl))
                        {
                            if (!store.DeleteCRL(crl))
                            {
                                result = false;
                            }
                        }
                        else
                        {
                            updatedCrls.Remove(crl);
                        }
                    }
                    foreach (var crl in updatedCrls)
                    {
                        store.AddCRL(crl);
                    }
                }
            }
            catch
            {
                result = false;
            }
            return result;
        }

        private bool UpdateStoreCertificates(
            string storePath,
            X509Certificate2Collection updatedCerts)
        {
            bool result = true;
            try
            {
                using (ICertificateStore store = CertificateStoreIdentifier.OpenStore(storePath))
                {
                    var storeCerts = store.Enumerate().Result;
                    foreach (var cert in storeCerts)
                    {
                        if (!updatedCerts.Contains(cert))
                        {
                            if (!store.Delete(cert.Thumbprint).Result)
                            {
                                result = false;
                            }
                        }
                        else
                        {
                            updatedCerts.Remove(cert);
                        }
                    }
                    foreach (var cert in updatedCerts)
                    {
                        store.Add(cert).Wait();
                    }
                }
            }
            catch
            {
                result = false;
            }
            return result;
        }

        /// <summary>
        /// Create CA test certificates.
        /// </summary>
        private async Task CreateCATestCerts(string tempStorePath)
        {
            Assert.IsTrue(EraseStore(tempStorePath));

            string subjectName = "CN=CA Test Cert, O=OPC Foundation";
            X509Certificate2 newCACert = CertificateFactory.CreateCertificate(
                null, null, subjectName, null)
                .SetCAConstraint()
                .CreateForRSA()
                .AddToStore(CertificateStoreType.Directory, tempStorePath);

            m_caCert = newCACert;

            // initialize cert revocation list (CRL)
            X509CRL newCACrl = await CertificateGroup.RevokeCertificateAsync(tempStorePath, newCACert).ConfigureAwait(false);

            m_caCrl = newCACrl;
        }

        private bool EraseStore(string storePath)
        {
            bool result = true;
            try
            {
                using (ICertificateStore store = CertificateStoreIdentifier.OpenStore(storePath))
                {
                    var storeCerts = store.Enumerate().Result;
                    foreach (var cert in storeCerts)
                    {
                        if (!store.Delete(cert.Thumbprint).Result)
                        {
                            result = false;
                        }
                    }
                    var storeCrls = store.EnumerateCRLs();
                    foreach (var crl in storeCrls)
                    {
                        if (!store.DeleteCRL(crl))
                        {
                            result = false;
                        }
                    }
                }
            }
            catch
            {
                result = false;
            }
            return result;
        }
        #endregion

        #region Private Fields
        private const int kRandomStart = 1;
        private RandomSource m_randomSource;
        private DataGenerator m_dataGenerator;
        private GlobalDiscoveryTestServer m_server;
        private GlobalDiscoveryTestClient m_gdsClient;
        private ServerConfigurationPushTestClient m_pushClient;
        private ServerCapabilities m_serverCapabilities;
        private ApplicationRecordDataType _applicationRecord;
        private X509Certificate2 m_selfSignedServerCert;
        private string[] m_domainNames;
        private X509Certificate2 m_caCert;
        private X509CRL m_caCrl;
        #endregion
    }
}
