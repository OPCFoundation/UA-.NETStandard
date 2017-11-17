/* ========================================================================
 * Copyright (c) 2005-2017 The OPC Foundation, Inc. All rights reserved.
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

using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Gds;
using Opc.Ua.Gds.Client;
using Opc.Ua.Gds.Test;
using Opc.Ua.Test;
using NUnit.Framework;
using System;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

namespace NUnit.Opc.Ua.Gds.Test
{

    [TestFixture, Category("GDSPush")]
    public class PushTest
    {
        #region Test Setup
        /// <summary>
        /// Set up a Global Discovery Server and Client instance and connect the session
        /// </summary>
        [OneTimeSetUp]
        protected async Task OneTimeSetUp()
        {
#if DEBUG
            // make sure all servers started in travis use a different port, or test will fail
            const int testPort = 58820;
#else
            const int testPort = 58830;
#endif
            _serverCapabilities = new ServerCapabilities();
            _randomSource = new RandomSource(randomStart);
            _dataGenerator = new DataGenerator(_randomSource);
            _server = new GlobalDiscoveryTestServer(true);
            await _server.StartServer(true, testPort);
            await Task.Delay(1000);

            // load clients
            _gdsClient = new GlobalDiscoveryTestClient(true);
            await _gdsClient.LoadClientConfiguration(testPort);
            _pushClient = new ServerConfigurationPushTestClient(true);
            await _pushClient.LoadClientConfiguration(testPort);

            // connect once
            await _gdsClient.GDSClient.Connect(_gdsClient.GDSClient.EndpointUrl);
            await _pushClient.PushClient.Connect(_pushClient.PushClient.EndpointUrl);

            ConnectGDSClient(true);
            RegisterPushServerApplication(_pushClient.PushClient.EndpointUrl);

            _selfSignedServerCert = new X509Certificate2(_pushClient.PushClient.Session.ConfiguredEndpoint.Description.ServerCertificate);
            _domainNames = Utils.GetDomainsFromCertficate(_selfSignedServerCert).ToArray();

        }

        /// <summary>
        /// Tear down the Global Discovery Server and disconnect the Client
        /// </summary>
        [OneTimeTearDown]
        protected void OneTimeTearDown()
        {
            ConnectGDSClient(true);
            UnRegisterPushServerApplication();
            _gdsClient.DisconnectClient();
            _gdsClient = null;
            _pushClient.DisconnectClient();
            _pushClient = null;
            _server.StopServer();
            _server = null;
            Thread.Sleep(1000);
        }

        [TearDown]
        protected void TearDown()
        {
            DisconnectGDSClient();
            DisconnectPushClient();
        }

        #endregion
        #region Test Methods
        [Test, Order(100)]
        public void GetSupportedKeyFormats()
        {
            ConnectPushClient(true);
            var keyFormats = _pushClient.PushClient.GetSupportedKeyFormats();
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
            TrustListDataType expectEmptyTrustList = _pushClient.PushClient.ReadTrustList();
            Assert.IsTrue(Utils.IsEqual(expectEmptyTrustList, emptyTrustList));
            requireReboot = _pushClient.PushClient.UpdateTrustList(fullTrustList);
            TrustListDataType expectFullTrustList = _pushClient.PushClient.ReadTrustList();
            Assert.IsTrue(Utils.IsEqual(expectFullTrustList, fullTrustList));
        }


        [Test, Order(400)]
        public void CreateCertificateRequest()
        {
            ConnectPushClient(true);
            ConnectGDSClient(true);
            NodeId invalidCertGroup = new NodeId(333);
            NodeId invalidCertType = new NodeId(Guid.NewGuid());
            Assert.That(() => { _pushClient.PushClient.CreateCertificateRequest(invalidCertGroup, null, null, false, null); }, Throws.Exception);
            Assert.That(() => { _pushClient.PushClient.CreateCertificateRequest(null, invalidCertType, null, false, null); }, Throws.Exception);
            Assert.That(() => { _pushClient.PushClient.CreateCertificateRequest(invalidCertGroup, invalidCertType, null, false, null); }, Throws.Exception);
            byte[] csr = _pushClient.PushClient.CreateCertificateRequest(null, null, null, false, null);
            Assert.IsNotNull(csr);
        }

        [Test, Order(400)]
        public void CreateCertificateRequestWithNewPrivateKey()
        {
            ConnectPushClient(true);
            ConnectGDSClient(true);
            byte[] csr = _pushClient.PushClient.CreateCertificateRequest(null, null, null, true, Encoding.ASCII.GetBytes("OPCTest"));
            Assert.IsNotNull(csr);
        }

        [Test, Order(500)]
        public void UpdateCertificateSelfSignedNoPrivateKey()
        {
            ConnectPushClient(true);
            X509Certificate2 serverCert = new X509Certificate2(_pushClient.PushClient.Session.ConfiguredEndpoint.Description.ServerCertificate);
            X509Certificate2 invalidCert = CertificateFactory.CreateCertificate(null, null, null, "uri:x:y:z", "TestApp", "CN=Push Server Test", null, 2048, DateTime.UtcNow, 1, 256);
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
            Assert.That(() => { _pushClient.PushClient.UpdateCertificate(null, null, serverCert.RawData, null, invalidCert.RawData, null); }, Throws.Exception);
            Assert.That(() => { _pushClient.PushClient.UpdateCertificate(null, null, invalidCert.RawData, null, null, new byte[][] { serverCert.RawData, invalidCert.RawData }); }, Throws.Exception);
            Assert.That(() => { _pushClient.PushClient.UpdateCertificate(null, null, null, null, null, new byte[][] { serverCert.RawData, invalidCert.RawData }); }, Throws.Exception);
            Assert.That(() => { _pushClient.PushClient.UpdateCertificate(null, null, invalidRawCert, null, null, new byte[][] { serverCert.RawData, invalidCert.RawData }); }, Throws.Exception);
            Assert.That(() => { _pushClient.PushClient.UpdateCertificate(null, null, serverCert.RawData, null, null, new byte[][] { serverCert.RawData, invalidRawCert }); }, Throws.Exception);
            // positive test, update server with its own cert...
            var success = _pushClient.PushClient.UpdateCertificate(null, null, serverCert.RawData, null, null, null);
            if (success)
            {
                _pushClient.PushClient.ApplyChanges();
            }
            VerifyNewPushServerCert(serverCert.RawData);
        }

        [Test, Order(510)]
        public void UpdateCertificateCASigned()
        {
            ConnectPushClient(true);
            ConnectGDSClient(true);
            byte[] csr = _pushClient.PushClient.CreateCertificateRequest(null, null, null, false, null);
            Assert.IsNotNull(csr);
            NodeId requestId = _gdsClient.GDSClient.StartSigningRequest(
                _applicationRecord.ApplicationId,
                null,
                null,
                csr);
            Assert.NotNull(requestId);
            byte[] privateKey;
            byte[] certificate;
            byte[][] issuerCertificates;
            int i = 0;
            do
            {
                Thread.Sleep(500);
                certificate = _gdsClient.GDSClient.FinishRequest(
                    _applicationRecord.ApplicationId,
                    requestId,
                    out privateKey,
                    out issuerCertificates);
                Assert.LessOrEqual(i++, 5);
            } while (certificate == null);
            Assert.NotNull(issuerCertificates);
            Assert.IsNull(privateKey);

            var success = _pushClient.PushClient.UpdateCertificate(null, null, certificate, null, null, issuerCertificates);
            if (success)
            {
                _pushClient.PushClient.ApplyChanges();
            }
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
                null,
                null,
                null,
                _applicationRecord.ApplicationUri,
                _applicationRecord.ApplicationNames[0].Text,
                _selfSignedServerCert.Subject,
                null,
                CertificateFactory.defaultKeySize,
                DateTime.UtcNow,
                CertificateFactory.defaultLifeTime,
                CertificateFactory.defaultHashSize);

            byte[] privateKey = null;
            if (keyFormat == "PFX")
            {
                Assert.IsTrue(newCert.HasPrivateKey);
                privateKey = newCert.Export(X509ContentType.Pfx);
            }
            else if (keyFormat == "PEM")
            {
                Assert.IsTrue(newCert.HasPrivateKey);
                privateKey = CertificateFactory.ExportPrivateKeyAsPEM(newCert);
            }
            else
            {
                Assert.Fail("Testing unsupported key format {0}.", keyFormat);
            }

            var success = _pushClient.PushClient.UpdateCertificate(
                null,
                null,
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
            byte[] privateKey;
            byte[] certificate;
            byte[][] issuerCertificates;
            int i = 0;
            do
            {
                Thread.Sleep(500);
                certificate = _gdsClient.GDSClient.FinishRequest(
                    _applicationRecord.ApplicationId,
                    requestId,
                    out privateKey,
                    out issuerCertificates);
                Assert.LessOrEqual(i++, 5);
            } while (certificate == null);
            Assert.NotNull(issuerCertificates);
            Assert.NotNull(privateKey);

            var success = _pushClient.PushClient.UpdateCertificate(
                null,
                null,
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
            Assert.That(() => { _pushClient.PushClient.CreateCertificateRequest(null, null, null, false, null); }, Throws.Exception);
            Assert.That(() => { _pushClient.PushClient.ReadTrustList(); }, Throws.Exception);
        }
        #endregion
        #region Private Methods
        private void ConnectPushClient(bool sysAdmin)
        {
            _pushClient.PushClient.AdminCredentials = sysAdmin ? _pushClient.SysAdminUser : _pushClient.AppUser;
            _pushClient.PushClient.Connect();
        }

        private void DisconnectPushClient()
        {
            _pushClient.PushClient.Disconnect();
        }

        private void ConnectGDSClient(bool admin)
        {
            _gdsClient.GDSClient.AdminCredentials = admin ? _gdsClient.AdminUser : _gdsClient.AppUser;
            _gdsClient.GDSClient.Connect();
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
                _applicationRecord = new ApplicationRecordDataType
                {
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
        }

        private void UnRegisterPushServerApplication()
        {
            _gdsClient.GDSClient.UnregisterApplication(_applicationRecord.ApplicationId);
            _applicationRecord.ApplicationId = null;
        }

        private void VerifyNewPushServerCert(byte[] certificate)
        {
            DisconnectPushClient();
            Thread.Sleep(500);
            _gdsClient.GDSClient.AdminCredentials = _gdsClient.AdminUser;
            _pushClient.PushClient.Connect(_pushClient.PushClient.EndpointUrl).Wait();
#if TODO
            Assert.AreEqual(
                certificate,
                _pushClient.PushClient.Session.ConfiguredEndpoint.Description.ServerCertificate
                );
#endif
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
        #endregion
    }
}