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

using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Gds.Client;
using Opc.Ua.Gds.Test;
using Opc.Ua.Test;
using System;
using System.Security.Cryptography.X509Certificates;
using System.Threading;

namespace NUnit.Opc.Ua.Gds.Test
{

    [TestFixture]
    public class PushTest
    {
        #region Test Setup
        /// <summary>
        /// Set up a Global Discovery Server and Client instance and connect the session
        /// </summary>
        [OneTimeSetUp]
        protected void OneTimeSetUp()
        {
            _serverCapabilities = new ServerCapabilities();
            _randomSource = new RandomSource(randomStart);
            _dataGenerator = new DataGenerator(_randomSource);
            _server = new GlobalDiscoveryTestServer(true);
            _server.StartServer().Wait();
            Thread.Sleep(1000);
            _gdsClient = new GlobalDiscoveryTestClient(true);
            _gdsClient.LoadClientConfiguration().Wait();
            _pushClient = new ServerConfigurationPushTestClient(true);
            _pushClient.LoadClientConfiguration().Wait();
        }

        /// <summary>
        /// Tear down the Global Discovery Server and disconnect the Client
        /// </summary>
        [OneTimeTearDown]
        protected void OneTimeTearDown()
        {
            _gdsClient.DisconnectClient();
            _pushClient.DisconnectClient();
            _server.StopServer();
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
            NodeId invalidCertGroup = new NodeId(333);
            NodeId invalidCertType = new NodeId(Guid.NewGuid());
            Assert.That(() => { _pushClient.PushClient.CreateCertificateRequest(invalidCertGroup, null, null, false, null); }, Throws.Exception);
            Assert.That(() => { _pushClient.PushClient.CreateCertificateRequest(null, invalidCertType, null, false, null); }, Throws.Exception);
            Assert.That(() => { _pushClient.PushClient.CreateCertificateRequest(invalidCertGroup, invalidCertType, null, false, null); }, Throws.Exception);
            byte[] csr = _pushClient.PushClient.CreateCertificateRequest(null, null, null, false, null);
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
        }

        [Test, Order(510)]
        public void UpdateCertificateCASigned()
        {
            ConnectPushClient(true);
        }

        [Test, Order(520)]
        public void UpdateCertificateSelfSignedWithPFXKey()
        {
            ConnectPushClient(true);
        }

        [Test, Order(530)]
        public void UpdateCertificateSelfSignedWithPEMKey()
        {
            ConnectPushClient(true);
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
            X509Certificate2 serverCert = new X509Certificate2(_pushClient.PushClient.Session.ConfiguredEndpoint.Description.ServerCertificate);
            Assert.That(() => { _pushClient.PushClient.UpdateCertificate(null, null, serverCert.RawData, null, null, null); }, Throws.Exception);
            Assert.That(() => { _pushClient.PushClient.CreateCertificateRequest(null, null, null, false, null); }, Throws.Exception);
            Assert.That(() => { _pushClient.PushClient.ReadTrustList(); }, Throws.Exception);
        }
        #endregion
        #region Private Methods
        private void ConnectPushClient(bool sysAdmin)
        {
            _pushClient.PushClient.AdminCredentials = new UserIdentity(sysAdmin ? "sysadmin" : "appuser", "demo");
            _pushClient.PushClient.Connect(_pushClient.PushClient.EndpointUrl);
        }

        private void DisconnectPushClient()
        {
            _pushClient.PushClient.Session?.Close();
        }

        private void ConnectGDSClient(bool admin)
        {
            _gdsClient.GDSClient.AdminCredentials = new UserIdentity(admin ? "appadmin" : "appuser", "demo");
            _gdsClient.GDSClient.Connect(_gdsClient.GDSClient.EndpointUrl);
        }

        private void DisconnectGDSClient()
        {
            _gdsClient.GDSClient.Session?.Close();
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
        #endregion

        #region Private Fields
        private const int randomStart = 1;
        private RandomSource _randomSource;
        private DataGenerator _dataGenerator;
        private GlobalDiscoveryTestServer _server;
        private GlobalDiscoveryTestClient _gdsClient;
        private ServerConfigurationPushTestClient _pushClient;
        private ServerCapabilities _serverCapabilities;
        #endregion
    }
}