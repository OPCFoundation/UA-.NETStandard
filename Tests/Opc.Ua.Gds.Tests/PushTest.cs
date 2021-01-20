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
            _server = await TestUtils.StartGDS(true);
            _server.StopServer();
            await Task.Delay(1000);
            _server = await TestUtils.StartGDS(false);

            _serverCapabilities = new ServerCapabilities();
            _randomSource = new RandomSource(randomStart);
            _dataGenerator = new DataGenerator(_randomSource);

            // load clients
            _gdsClient = new GlobalDiscoveryTestClient(true);
            await _gdsClient.LoadClientConfiguration(_server.BasePort);
            _pushClient = new ServerConfigurationPushTestClient(true);
            await _pushClient.LoadClientConfiguration(_server.BasePort);

            // connect once
            await _gdsClient.GDSClient.Connect(_gdsClient.GDSClient.EndpointUrl);
            await _pushClient.PushClient.Connect(_pushClient.PushClient.EndpointUrl);

            ConnectGDSClient(true);
            RegisterPushServerApplication(_pushClient.PushClient.EndpointUrl);

            _selfSignedServerCert = new X509Certificate2(_pushClient.PushClient.Session.ConfiguredEndpoint.Description.ServerCertificate);
            _domainNames = X509Utils.GetDomainsFromCertficate(_selfSignedServerCert).ToArray();

            await CreateCATestCerts(_pushClient.TempStorePath);
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
                _gdsClient.DisconnectClient();
                _pushClient.DisconnectClient();
                _server.StopServer();
            }
            catch { }
            _gdsClient = null;
            _pushClient = null;
            _server = null;
        }

        [SetUp]
        protected void SetUp()
        {
            _server.ResetLogFile();
        }

        [TearDown]
        protected void TearDown()
        {
            DisconnectGDSClient();
            DisconnectPushClient();
            try
            {
                TestContext.AddTestAttachment(_server.GetLogFilePath(), "GDS Client and Server logs");
            }
            catch { }

        }
        #endregion

        #region Test Methods
        [Test, Order(100)]
        public void GetSupportedKeyFormats()
        {
            ConnectPushClient(true);
            var keyFormats = _pushClient.PushClient.GetSupportedKeyFormats();
            Assert.IsNotNull(keyFormats);
        }

        [Test, Order(200)]
        public void ReadTrustList()
        {
            ConnectPushClient(true);
            TrustListDataType allTrustList = _pushClient.PushClient.ReadTrustList();
            Assert.IsNotNull(allTrustList);
            Assert.IsNotNull(allTrustList.IssuerCertificates);
            Assert.IsNotNull(allTrustList.IssuerCrls);
            Assert.IsNotNull(allTrustList.TrustedCertificates);
            Assert.IsNotNull(allTrustList.TrustedCrls);
            TrustListDataType noneTrustList = _pushClient.PushClient.ReadTrustList(TrustListMasks.None);
            Assert.IsNotNull(noneTrustList);
            Assert.IsNotNull(noneTrustList.IssuerCertificates);
            Assert.IsNotNull(noneTrustList.IssuerCrls);
            Assert.IsNotNull(noneTrustList.TrustedCertificates);
            Assert.IsNotNull(noneTrustList.TrustedCrls);
            Assert.IsTrue(noneTrustList.IssuerCertificates.Count == 0);
            Assert.IsTrue(noneTrustList.IssuerCrls.Count == 0);
            Assert.IsTrue(noneTrustList.TrustedCertificates.Count == 0);
            Assert.IsTrue(noneTrustList.TrustedCrls.Count == 0);
            TrustListDataType issuerTrustList = _pushClient.PushClient.ReadTrustList(TrustListMasks.IssuerCertificates | TrustListMasks.IssuerCrls);
            Assert.IsNotNull(issuerTrustList);
            Assert.IsNotNull(issuerTrustList.IssuerCertificates);
            Assert.IsNotNull(issuerTrustList.IssuerCrls);
            Assert.IsNotNull(issuerTrustList.TrustedCertificates);
            Assert.IsNotNull(issuerTrustList.TrustedCrls);
            Assert.IsTrue(issuerTrustList.IssuerCertificates.Count == allTrustList.IssuerCertificates.Count);
            Assert.IsTrue(issuerTrustList.IssuerCrls.Count == allTrustList.IssuerCrls.Count);
            Assert.IsTrue(issuerTrustList.TrustedCertificates.Count == 0);
            Assert.IsTrue(issuerTrustList.TrustedCrls.Count == 0);
            TrustListDataType trustedTrustList = _pushClient.PushClient.ReadTrustList(TrustListMasks.TrustedCertificates | TrustListMasks.TrustedCrls);
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
            TrustListDataType fullTrustList = _pushClient.PushClient.ReadTrustList();
            TrustListDataType emptyTrustList = _pushClient.PushClient.ReadTrustList(TrustListMasks.None);
            emptyTrustList.SpecifiedLists = (uint)TrustListMasks.All;
            bool requireReboot = _pushClient.PushClient.UpdateTrustList(emptyTrustList);
            Assert.False(requireReboot);
            TrustListDataType expectEmptyTrustList = _pushClient.PushClient.ReadTrustList();
            Assert.IsTrue(Utils.IsEqual(expectEmptyTrustList, emptyTrustList));
            requireReboot = _pushClient.PushClient.UpdateTrustList(fullTrustList);
            Assert.False(requireReboot);
            TrustListDataType expectFullTrustList = _pushClient.PushClient.ReadTrustList();
            Assert.IsTrue(Utils.IsEqual(expectFullTrustList, fullTrustList));
        }

        [Test, Order(301)]
        public void AddRemoveCert()
        {
            using (X509Certificate2 trustedCert = CertificateFactory.CreateCertificate("uri:x:y:z", "TrustedCert", "CN=Push Server Test", null).CreateForRSA())
            using (X509Certificate2 issuerCert = CertificateFactory.CreateCertificate("uri:x:y:z", "IssuerCert", "CN=Push Server Test", null).CreateForRSA())
            {
                ConnectPushClient(true);
                TrustListDataType beforeTrustList = _pushClient.PushClient.ReadTrustList();
                _pushClient.PushClient.AddCertificate(trustedCert, true);
                _pushClient.PushClient.AddCertificate(issuerCert, false);
                TrustListDataType afterAddTrustList = _pushClient.PushClient.ReadTrustList();
                Assert.Greater(afterAddTrustList.TrustedCertificates.Count, beforeTrustList.TrustedCertificates.Count);
                Assert.Greater(afterAddTrustList.IssuerCertificates.Count, beforeTrustList.IssuerCertificates.Count);
                Assert.IsFalse(Utils.IsEqual(beforeTrustList, afterAddTrustList));
                _pushClient.PushClient.RemoveCertificate(trustedCert.Thumbprint, true);
                _pushClient.PushClient.RemoveCertificate(issuerCert.Thumbprint, false);
                TrustListDataType afterRemoveTrustList = _pushClient.PushClient.ReadTrustList();
                Assert.IsTrue(Utils.IsEqual(beforeTrustList, afterRemoveTrustList));
            }
        }

        [Test, Order(302)]
        public void AddRemoveCATrustedCert()
        {
            ConnectPushClient(true);
            TrustListDataType beforeTrustList = _pushClient.PushClient.ReadTrustList();
            _pushClient.PushClient.AddCertificate(_caCert, true);
            _pushClient.PushClient.AddCrl(_caCrl, true);
            TrustListDataType afterAddTrustList = _pushClient.PushClient.ReadTrustList();
            Assert.Greater(afterAddTrustList.TrustedCertificates.Count, beforeTrustList.TrustedCertificates.Count);
            Assert.Greater(afterAddTrustList.TrustedCrls.Count, beforeTrustList.TrustedCrls.Count);
            Assert.IsFalse(Utils.IsEqual(beforeTrustList, afterAddTrustList));
            var serviceResultException = Assert.Throws<ServiceResultException>(() => { _pushClient.PushClient.RemoveCertificate(_caCert.Thumbprint, false); });
            Assert.AreEqual(StatusCodes.BadInvalidArgument, serviceResultException.StatusCode, serviceResultException.Message);
            TrustListDataType afterRemoveTrustList = _pushClient.PushClient.ReadTrustList();
            Assert.IsFalse(Utils.IsEqual(beforeTrustList, afterRemoveTrustList));
            _pushClient.PushClient.RemoveCertificate(_caCert.Thumbprint, true);
            afterRemoveTrustList = _pushClient.PushClient.ReadTrustList();
            Assert.IsTrue(Utils.IsEqual(beforeTrustList, afterRemoveTrustList));
        }

        [Test, Order(303)]
        public void AddRemoveCAIssuerCert()
        {
            ConnectPushClient(true);
            TrustListDataType beforeTrustList = _pushClient.PushClient.ReadTrustList();
            _pushClient.PushClient.AddCertificate(_caCert, false);
            _pushClient.PushClient.AddCrl(_caCrl, false);
            TrustListDataType afterAddTrustList = _pushClient.PushClient.ReadTrustList();
            Assert.Greater(afterAddTrustList.IssuerCertificates.Count, beforeTrustList.IssuerCertificates.Count);
            Assert.Greater(afterAddTrustList.IssuerCrls.Count, beforeTrustList.IssuerCrls.Count);
            Assert.IsFalse(Utils.IsEqual(beforeTrustList, afterAddTrustList));
            Assert.That(() => { _pushClient.PushClient.RemoveCertificate(_caCert.Thumbprint, true); }, Throws.Exception);
            TrustListDataType afterRemoveTrustList = _pushClient.PushClient.ReadTrustList();
            Assert.IsFalse(Utils.IsEqual(beforeTrustList, afterRemoveTrustList));
            _pushClient.PushClient.RemoveCertificate(_caCert.Thumbprint, false);
            afterRemoveTrustList = _pushClient.PushClient.ReadTrustList();
            Assert.IsTrue(Utils.IsEqual(beforeTrustList, afterRemoveTrustList));
        }


        [Test, Order(400)]
        public void CreateSigningRequestBadParms()
        {
            ConnectPushClient(true);
            NodeId invalidCertGroup = new NodeId(333);
            NodeId invalidCertType = new NodeId(Guid.NewGuid());
            Assert.That(() => { _pushClient.PushClient.CreateSigningRequest(invalidCertGroup, null, null, false, null); }, Throws.Exception);
            Assert.That(() => { _pushClient.PushClient.CreateSigningRequest(null, invalidCertType, null, false, null); }, Throws.Exception);
            Assert.That(() => { _pushClient.PushClient.CreateSigningRequest(null, null, null, false, null); }, Throws.Exception);
            Assert.That(() => { _pushClient.PushClient.CreateSigningRequest(invalidCertGroup, invalidCertType, null, false, null); }, Throws.Exception);
        }

        [Test, Order(401)]
        public void CreateSigningRequestNullParms()
        {
            ConnectPushClient(true);
            byte[] csr = _pushClient.PushClient.CreateSigningRequest(null, _pushClient.PushClient.ApplicationCertificateType, null, false, null);
            Assert.IsNotNull(csr);
        }

        [Test, Order(402)]
        public void CreateSigningRequestRsaMinNullParms()
        {
#if NETSTANDARD2_1
            Assert.Ignore("SHA1 not supported on .NET Standard 2.1.");
#endif
            ConnectPushClient(true);
            Assert.That(() => { _pushClient.PushClient.CreateSigningRequest(null, OpcUa.ObjectTypeIds.RsaMinApplicationCertificateType, null, false, null); }, Throws.Exception);
        }

        [Test, Order(409)]
        public void CreateSigningRequestAllParms()
        {
            ConnectPushClient(true);
            byte[] nonce = new byte[0];
            byte[] csr = _pushClient.PushClient.CreateSigningRequest(
                _pushClient.PushClient.DefaultApplicationGroup,
                _pushClient.PushClient.ApplicationCertificateType,
                "",
                false,
                nonce);
            Assert.IsNotNull(csr);
        }

        [Test, Order(410)]
        public void CreateSigningRequestNullParmsWithNewPrivateKey()
        {
            ConnectPushClient(true);
            byte[] csr = _pushClient.PushClient.CreateSigningRequest(null, _pushClient.PushClient.ApplicationCertificateType, null, true, Encoding.ASCII.GetBytes("OPCTest"));
            Assert.IsNotNull(csr);
        }

        [Test, Order(419)]
        public void CreateSigningRequestAllParmsWithNewPrivateKey()
        {
            ConnectPushClient(true);
            byte[] nonce = new byte[32];
            _randomSource.NextBytes(nonce, 0, nonce.Length);
            byte[] csr = _pushClient.PushClient.CreateSigningRequest(
                _pushClient.PushClient.DefaultApplicationGroup,
                _pushClient.PushClient.ApplicationCertificateType,
                "",
                true,
                nonce);
            Assert.IsNotNull(csr);
        }

        [Test, Order(500)]
        public void UpdateCertificateSelfSignedNoPrivateKey()
        {
            ConnectPushClient(true);
            using (X509Certificate2 invalidCert = CertificateFactory.CreateCertificate("uri:x:y:z", "TestApp", "CN=Push Server Test", null).CreateForRSA())
            using (X509Certificate2 serverCert = new X509Certificate2(_pushClient.PushClient.Session.ConfiguredEndpoint.Description.ServerCertificate))
            {
                if (!X509Utils.CompareDistinguishedName(serverCert.Subject, serverCert.Issuer))
                {
                    Assert.Ignore("Server has no self signed cert in use.");
                }
                byte[] invalidRawCert = { 0xba, 0xd0, 0xbe, 0xef, 3 };
                // negative test all parameter combinations
                NodeId invalidCertGroup = new NodeId(333);
                NodeId invalidCertType = new NodeId(Guid.NewGuid());
                Assert.That(() => { _pushClient.PushClient.UpdateCertificate(null, null, null, null, null, null); }, Throws.Exception);
                Assert.That(() => { _pushClient.PushClient.UpdateCertificate(invalidCertGroup, null, serverCert.RawData, null, null, null); }, Throws.Exception);
                Assert.That(() => { _pushClient.PushClient.UpdateCertificate(null, invalidCertType, serverCert.RawData, null, null, null); }, Throws.Exception);
                Assert.That(() => { _pushClient.PushClient.UpdateCertificate(invalidCertGroup, invalidCertType, serverCert.RawData, null, null, null); }, Throws.Exception);
                Assert.That(() => { _pushClient.PushClient.UpdateCertificate(null, null, invalidRawCert, null, null, null); }, Throws.Exception);
                Assert.That(() => { _pushClient.PushClient.UpdateCertificate(null, null, invalidCert.RawData, null, null, null); }, Throws.Exception);
                Assert.That(() => { _pushClient.PushClient.UpdateCertificate(null, null, serverCert.RawData, "XYZ", null, null); }, Throws.Exception);
                Assert.That(() => { _pushClient.PushClient.UpdateCertificate(null, null, serverCert.RawData, "XYZ", invalidCert.RawData, null); }, Throws.Exception);
                Assert.That(() => { _pushClient.PushClient.UpdateCertificate(null, null, invalidCert.RawData, null, null, new byte[][] { serverCert.RawData, invalidCert.RawData }); }, Throws.Exception);
                Assert.That(() => { _pushClient.PushClient.UpdateCertificate(null, null, null, null, null, new byte[][] { serverCert.RawData, invalidCert.RawData }); }, Throws.Exception);
                Assert.That(() => { _pushClient.PushClient.UpdateCertificate(null, null, invalidRawCert, null, null, new byte[][] { serverCert.RawData, invalidCert.RawData }); }, Throws.Exception);
                Assert.That(() => { _pushClient.PushClient.UpdateCertificate(null, null, serverCert.RawData, null, null, new byte[][] { serverCert.RawData, invalidRawCert }); }, Throws.Exception);
                Assert.That(() => { _pushClient.PushClient.UpdateCertificate(null, null, serverCert.RawData, null, null, null); }, Throws.Exception);
                var success = _pushClient.PushClient.UpdateCertificate(
                    null,
                    _pushClient.PushClient.ApplicationCertificateType,
                    serverCert.RawData,
                    null,
                    null,
                    null);
                if (success)
                {
                    _pushClient.PushClient.ApplyChanges();
                }
                VerifyNewPushServerCert(serverCert.RawData);
            }
        }

        [Test, Order(510)]
        public void UpdateCertificateCASigned()
        {
#if NETCOREAPP3_1
            // this test fails on macOS, ignore
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Assert.Ignore("Update CA signed certifcate fails on mac OS.");
            }
#endif
            ConnectPushClient(true);
            ConnectGDSClient(true);
            TestContext.Out.WriteLine("Create Signing Request");
            byte[] csr = _pushClient.PushClient.CreateSigningRequest(
                null,
                _pushClient.PushClient.ApplicationCertificateType,
                null,
                false,
                null);
            Assert.IsNotNull(csr);
            TestContext.Out.WriteLine("Start Signing Request");
            NodeId requestId = _gdsClient.GDSClient.StartSigningRequest(
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
                    certificate = _gdsClient.GDSClient.FinishRequest(
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
            bool success = _pushClient.PushClient.UpdateCertificate(
                null,
                _pushClient.PushClient.ApplicationCertificateType,
                certificate,
                null,
                null,
                issuerCertificates);
            if (success)
            {
                TestContext.Out.WriteLine("Apply Changes");
                _pushClient.PushClient.ApplyChanges();
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
            var keyFormats = _pushClient.PushClient.GetSupportedKeyFormats();
            if (!keyFormats.Contains(keyFormat))
            {
                Assert.Ignore("Push server doesn't support {0} key update", keyFormat);
            }

            X509Certificate2 newCert = CertificateFactory.CreateCertificate(
                _applicationRecord.ApplicationUri,
                _applicationRecord.ApplicationNames[0].Text,
                _selfSignedServerCert.Subject,
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

            var success = _pushClient.PushClient.UpdateCertificate(
                _pushClient.PushClient.DefaultApplicationGroup,
                _pushClient.PushClient.ApplicationCertificateType,
                newCert.RawData,
                keyFormat,
                privateKey,
                null);

            if (success)
            {
                _pushClient.PushClient.ApplyChanges();
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
            var keyFormats = _pushClient.PushClient.GetSupportedKeyFormats();
            if (!keyFormats.Contains(keyFormat))
            {
                Assert.Ignore("Push server doesn't support {0} key update", keyFormat);
            }

            NodeId requestId = _gdsClient.GDSClient.StartNewKeyPairRequest(
                _applicationRecord.ApplicationId,
                null,
                null,
                _selfSignedServerCert.Subject,
                _domainNames,
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
                    certificate = _gdsClient.GDSClient.FinishRequest(
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

            var success = _pushClient.PushClient.UpdateCertificate(
                _pushClient.PushClient.DefaultApplicationGroup,
                _pushClient.PushClient.ApplicationCertificateType,
                certificate,
                keyFormat,
                privateKey,
                issuerCertificates);
            if (success)
            {
                _pushClient.PushClient.ApplyChanges();
            }
            VerifyNewPushServerCert(certificate);
        }

        [Test, Order(600)]
        public void GetRejectedList()
        {
            ConnectPushClient(true);
            var collection = _pushClient.PushClient.GetRejectedList();
            Assert.NotNull(collection);
        }

        [Test, Order(700)]
        public void ApplyChanges()
        {
            ConnectPushClient(true);
            _pushClient.PushClient.ApplyChanges();
        }

        [Test, Order(800)]
        public void VerifyNoUserAccess()
        {
            ConnectPushClient(false);
            Assert.That(() => { _pushClient.PushClient.ApplyChanges(); }, Throws.Exception);
            Assert.That(() => { _pushClient.PushClient.GetRejectedList(); }, Throws.Exception);
            Assert.That(() => { _pushClient.PushClient.UpdateCertificate(null, null, _selfSignedServerCert.RawData, null, null, null); }, Throws.Exception);
            Assert.That(() => { _pushClient.PushClient.CreateSigningRequest(null, null, null, false, null); }, Throws.Exception);
            Assert.That(() => { _pushClient.PushClient.ReadTrustList(); }, Throws.Exception);
        }
        #endregion

        #region Private Methods
        private void ConnectPushClient(bool sysAdmin,
            [System.Runtime.CompilerServices.CallerMemberName] string memberName = ""
            )
        {
            _pushClient.PushClient.AdminCredentials = sysAdmin ? _pushClient.SysAdminUser : _pushClient.AppUser;
            _pushClient.PushClient.Connect(_pushClient.PushClient.EndpointUrl).Wait();
            TestContext.Progress.WriteLine($"GDS Push({sysAdmin}) Connected -- {memberName}");
        }

        private void DisconnectPushClient()
        {
            _pushClient.PushClient.Disconnect();
        }

        private void ConnectGDSClient(bool admin,
            [System.Runtime.CompilerServices.CallerMemberName] string memberName = ""
            )
        {
            _gdsClient.GDSClient.AdminCredentials = admin ? _gdsClient.AdminUser : _gdsClient.AppUser;
            _gdsClient.GDSClient.Connect(_gdsClient.GDSClient.EndpointUrl).Wait();
            TestContext.Progress.WriteLine($"GDS Client({admin}) connected -- {memberName}");
        }

        private void DisconnectGDSClient()
        {
            _gdsClient.GDSClient.Disconnect();
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
                EndpointDescription endpointDescription = CoreClientUtils.SelectEndpoint(discoveryUrl, true);
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
            NodeId id = _gdsClient.GDSClient.RegisterApplication(_applicationRecord);
            Assert.IsNotNull(id);
            _applicationRecord.ApplicationId = id;

            // add issuer and trusted certs to client stores
            NodeId trustListId = _gdsClient.GDSClient.GetTrustList(id, null);
            var trustList = _gdsClient.GDSClient.ReadTrustList(trustListId);
            AddTrustListToStore(_gdsClient.Config.SecurityConfiguration, trustList);
            AddTrustListToStore(_pushClient.Config.SecurityConfiguration, trustList);
        }

        private void UnRegisterPushServerApplication()
        {
            _gdsClient.GDSClient.UnregisterApplication(_applicationRecord.ApplicationId);
            _applicationRecord.ApplicationId = null;
        }

        private void VerifyNewPushServerCert(byte[] certificate)
        {
            DisconnectPushClient();
            Thread.Sleep(2000);
            _gdsClient.GDSClient.Connect(_gdsClient.GDSClient.EndpointUrl).Wait();
            _pushClient.PushClient.Connect(_pushClient.PushClient.EndpointUrl).Wait();
            Assert.AreEqual(
                certificate,
                _pushClient.PushClient.Session.ConfiguredEndpoint.Description.ServerCertificate
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

            _caCert = newCACert;

            // initialize cert revocation list (CRL)
            X509CRL newCACrl = await CertificateGroup.RevokeCertificateAsync(tempStorePath, newCACert);

            _caCrl = newCACrl;
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
        private const int randomStart = 1;
        private RandomSource _randomSource;
        private DataGenerator _dataGenerator;
        private GlobalDiscoveryTestServer _server;
        private GlobalDiscoveryTestClient _gdsClient;
        private ServerConfigurationPushTestClient _pushClient;
        private ServerCapabilities _serverCapabilities;
        private ApplicationRecordDataType _applicationRecord;
        private X509Certificate2 _selfSignedServerCert;
        private string[] _domainNames;
        private X509Certificate2 _caCert;
        private X509CRL _caCrl;
        #endregion
    }
}
