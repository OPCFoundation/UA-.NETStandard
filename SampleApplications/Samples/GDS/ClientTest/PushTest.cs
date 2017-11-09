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

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Gds;
using Opc.Ua.Gds.Client;
using Opc.Ua.Gds.Test;
using Opc.Ua.Test;
using System.Security.Cryptography.X509Certificates;

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
            _client = new GlobalDiscoveryTestClient(true);
            _client.ConnectClient(false).Wait();
            _client.PushClient.AdminCredentials = new UserIdentity("appadmin", "demo");
            _client.PushClient.Connect();
        }

        /// <summary>
        /// Tear down the Global Discovery Server and disconnect the Client
        /// </summary>
        [OneTimeTearDown]
        protected void OneTimeTearDown()
        {
            _client.DisconnectClient();
            _server.StopServer();
        }
        #endregion
        #region Test Methods
        [Test, Order(100)]
        public void GetSupportedKeyFormats()
        {
            _client.PushClient.GetSupportedKeyFormats();
        }

        [Test, Order(200)]
        public void ReadTrustList()
        {
            TrustListDataType trustList = _client.PushClient.ReadTrustList();
            Assert.IsNotNull(trustList);
            Assert.IsNotNull(trustList.IssuerCertificates);
            Assert.IsNotNull(trustList.IssuerCrls);
            Assert.IsNotNull(trustList.TrustedCertificates);
            Assert.IsNotNull(trustList.TrustedCrls);
        }

        [Test, Order(300)]
        public void UpdateTrustList()
        {
            TrustListDataType trustList = _client.PushClient.ReadTrustList();
            Assert.IsTrue(_client.PushClient.UpdateTrustList(trustList));
        }



        [Test, Order(400)]
        public void CreateCertificateRequest()
        {
            byte [] csr = _client.PushClient.CreateCertificateRequest(null,null,null,false,null);
            Assert.IsNotNull(csr);
        }

        [Test, Order(500)]
        public void UpdateCertificate()
        {
            X509Certificate2 cert = CertificateFactory.CreateCertificate(null, null, null, "uri:x:y:z", "TestApp", "CN=Push Server Test", null, 2048, DateTime.UtcNow, 1, 256);
            var success = _client.PushClient.UpdateCertificate(null, null, cert.RawData, null, null, null);
        }

        [Test, Order(500)]
        public void GetRejectedList()
        {
            var collection = _client.PushClient.GetRejectedList();
        }


        [Test, Order(600)]
        public void ApplyChanges()
        {
            _client.PushClient.ApplyChanges();
        }

        #endregion
        #region Private Methods

        private string RandomLocalHost()
        {
            string localhost = Regex.Replace(_dataGenerator.GetRandomSymbol("en").Trim().ToLower(), @"[^\w\d]", "");
            if (localhost.Length >= 12)
            {
                localhost = localhost.Substring(0, 12);
            }
            return localhost;
        }

        private string[] RandomDomainNames()
        {
            int count = _randomSource.NextInt32(8) + 1;
            var result = new string[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = RandomLocalHost();
            }
            return result;
        }

        private StringCollection RandomDiscoveryUrl(StringCollection domainNames, int port, string appUri)
        {
            var result = new StringCollection();
            foreach (var name in domainNames)
            {
                int random = _randomSource.NextInt32(7);
                if ((result.Count == 0) || (random & 1) == 0)
                {
                    result.Add(String.Format("opc.tcp://{0}:{1}/{2}", name, (port++).ToString(), appUri));
                }
                if ((random & 2) == 0)
                {
                    result.Add(String.Format("http://{0}:{1}/{2}", name, (port++).ToString(), appUri));
                }
                if ((random & 4) == 0)
                {
                    result.Add(String.Format("https://{0}:{1}/{2}", name, (port++).ToString(), appUri));
                }
            }
            return result;
        }
        #endregion

        #region Private Fields
        private const int randomStart = 1;
        private RandomSource _randomSource;
        private DataGenerator _dataGenerator;
        private GlobalDiscoveryTestServer _server;
        private GlobalDiscoveryTestClient _client;
        private ServerCapabilities _serverCapabilities;
        #endregion
    }
}