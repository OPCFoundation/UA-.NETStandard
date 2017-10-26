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
using Opc.Ua.Gds;
using Opc.Ua.Gds.Client;
using Opc.Ua.Gds.Test;
using Opc.Ua.Test;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace NUnit.Opc.Ua.Gds.Test
{
    public class ApplicationTestData
    {
        public ApplicationTestData()
        {
            Initialize();
        }

        private void Initialize()
        {
            ApplicationRecord = new ApplicationRecordDataType();
            CertificateGroupId = null;
            CertificateTypeId = null;
            CertificateRequestId = null;
            DomainNames = new StringCollection();
            Subject = null;
            PrivateKeyFormat = "PFX";
            PrivateKeyPassword = "";
            Certificate = null;
            PrivateKey = null;
            IssuerCertificates = null;
        }

        public ApplicationRecordDataType ApplicationRecord;
        public NodeId CertificateGroupId;
        public NodeId CertificateTypeId;
        public NodeId CertificateRequestId;
        public StringCollection DomainNames;
        public String Subject;
        public String PrivateKeyFormat;
        public String PrivateKeyPassword;
        public byte[] Certificate;
        public byte[] PrivateKey;
        public byte[][] IssuerCertificates;
    }

    /// <summary>
    /// 
    /// </summary>
    /// 
    [TestFixture]
    public class ClientTest
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
            _client.ConnectClient().Wait();
            _client.GDSClient.AdminCredentials = new UserIdentity("appadmin", "demo");
            _client.GDSClient.Connect(_client.GDSClient.EndpointUrl);

            // good applications test set
            _goodApplicationTestSet = ApplicationTestSet(goodApplicationsTestCount, false);
            _invalidApplicationTestSet = ApplicationTestSet(invalidApplicationsTestCount, true);

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
        /// <summary>
        /// 
        /// </summary>
        /// 
        [Test, Order(100)]
        public void RegisterGoodApplications()
        {
            foreach (var application in _goodApplicationTestSet)
            {
                NodeId id = _client.GDSClient.RegisterApplication(application.ApplicationRecord);
                Assert.NotNull(id);
                Assert.IsFalse(id.IsNullNodeId);
                Assert.AreEqual(id.IdType, IdType.Guid);
                application.ApplicationRecord.ApplicationId = id;
            }

        }

        [Test, Order(101)]
        public void RegisterInvalidApplications()
        {
            foreach (var application in _invalidApplicationTestSet)
            {
                Assert.That(() => { NodeId id = _client.GDSClient.RegisterApplication(application.ApplicationRecord); }, Throws.Exception);
            }
        }

        [Test, Order(200)]
        public void UpdateGoodApplications()
        {
            foreach (var application in _goodApplicationTestSet)
            {
                var updatedApplicationRecord = (ApplicationRecordDataType)application.ApplicationRecord.MemberwiseClone();
                updatedApplicationRecord.ApplicationUri += "update";
                _client.GDSClient.UpdateApplication(updatedApplicationRecord);
                var result = _client.GDSClient.FindApplication(updatedApplicationRecord.ApplicationUri);
                _client.GDSClient.UpdateApplication(application.ApplicationRecord);
                Assert.NotNull(result);
                Assert.GreaterOrEqual(1, result.Length, "Couldn't find updated application record");
            }
        }

        [Test, Order(201)]
        public void UpdateGoodApplicationsWithNewGuid()
        {
            foreach (var application in _goodApplicationTestSet)
            {
                var testApplicationRecord = (ApplicationRecordDataType)application.ApplicationRecord.MemberwiseClone();
                testApplicationRecord.ApplicationId = new NodeId(Guid.NewGuid());
                Assert.That(() => { _client.GDSClient.UpdateApplication(testApplicationRecord); }, Throws.Exception);
            }
        }

        [Test, Order(202)]
        public void UpdateInvalidApplications()
        {
            foreach (var application in _invalidApplicationTestSet)
            {
                Assert.That(() => { _client.GDSClient.UpdateApplication(application.ApplicationRecord); }, Throws.Exception);
            }
        }

        [Test, Order(400)]
        public void FindGoodApplications()
        {
            foreach (var application in _goodApplicationTestSet)
            {
                var result = _client.GDSClient.FindApplication(application.ApplicationRecord.ApplicationUri);
                Assert.NotNull(result);
                Assert.GreaterOrEqual(1, result.Length, "Couldn't find good application");
            }
        }

        [Test, Order(400)]
        public void FindInvalidApplications()
        {
            foreach (var application in _invalidApplicationTestSet)
            {
                var result = _client.GDSClient.FindApplication(application.ApplicationRecord.ApplicationUri);
                Assert.NotNull(result);
                Assert.AreEqual(0, result.Length, "Found invalid application on server");
            }
        }

        [Test, Order(400)]
        public void GetGoodApplications()
        {
            foreach (var application in _goodApplicationTestSet)
            {
                var result = _client.GDSClient.GetApplication(application.ApplicationRecord.ApplicationId);
                Assert.NotNull(result);
                Assert.IsTrue(Utils.IsEqual(application.ApplicationRecord, result));
            }
        }

        [Test, Order(400)]
        public void GetInvalidApplications()
        {
            foreach (var application in _invalidApplicationTestSet)
            {
                Assert.That(() => { var result = _client.GDSClient.GetApplication(application.ApplicationRecord.ApplicationId); }, Throws.Exception);
            }
        }

        [Test, Order(500)]
        public void StartGoodNewKeyPairRequest()
        {
            foreach (var application in _goodApplicationTestSet)
            {
                NodeId requestId = _client.GDSClient.StartNewKeyPairRequest(
                    application.ApplicationRecord.ApplicationId,
                    application.CertificateGroupId,
                    application.CertificateTypeId,
                    application.Subject,
                    application.DomainNames,
                    application.PrivateKeyFormat,
                    application.PrivateKeyPassword);
                Assert.NotNull(requestId);
                application.CertificateRequestId = requestId;
            }
        }

        [Test, Order(501)]
        public void StartInvalidNewKeyPairRequest()
        {
            foreach (var application in _invalidApplicationTestSet)
            {
                Assert.That(() => { 
                    NodeId requestId = _client.GDSClient.StartNewKeyPairRequest(
                        application.ApplicationRecord.ApplicationId,
                        application.CertificateGroupId,
                        application.CertificateTypeId,
                        application.Subject,
                        application.DomainNames,
                        application.PrivateKeyFormat,
                        application.PrivateKeyPassword);
                }, Throws.Exception);
            }
        }

        [Test, Order(900)]
        public void UnregisterInvalidApplications()
        {
            foreach (var application in _invalidApplicationTestSet)
            {
                Assert.That(() => { _client.GDSClient.UnregisterApplication(application.ApplicationRecord.ApplicationId); }, Throws.Exception);
            }
        }

        [Test, Order(900)]
        public void UnregisterGoodApplications()
        {
            foreach (var application in _goodApplicationTestSet)
            {
                _client.GDSClient.UnregisterApplication(application.ApplicationRecord.ApplicationId);
            }
        }

        [Test, Order(901)]
        public void UnregisterUnregisteredGoodApplications()
        {
            foreach (var application in _goodApplicationTestSet)
            {
                Assert.That(() => { _client.GDSClient.UnregisterApplication(application.ApplicationRecord.ApplicationId); }, Throws.Exception);
            }
        }
        #endregion
        #region Private Methods
        private IList<ApplicationTestData> ApplicationTestSet(int count, bool invalidateSet)
        {
            var testDataSet = new List<ApplicationTestData>();
            for (int i = 0; i < count; i++)
            {
                var testData = RandomApplicationTestData();
                if (invalidateSet)
                {
                    ApplicationRecordDataType appRecord = testData.ApplicationRecord;
                    appRecord.ApplicationId = new NodeId(Guid.NewGuid());
                    switch (i % 4)
                    {
                        case 0:
                            appRecord.ApplicationUri = _dataGenerator.GetRandomString();
                            break;
                        case 1:
                            appRecord.ApplicationType = (ApplicationType)_randomSource.NextInt32(100) + 8;
                            break;
                        case 2:
                            appRecord.ProductUri = _dataGenerator.GetRandomString();
                            break;
                        case 3:
                            appRecord.DiscoveryUrls = appRecord.ApplicationType == ApplicationType.Client ?
                                RandomDiscoveryUrl(new StringCollection { "xxxyyyzzz" }, _randomSource.NextInt32(0x7fff), "TestClient") : null;
                            break;
                        case 4:
                            appRecord.ServerCapabilities = appRecord.ApplicationType == ApplicationType.Client ?
                                RandomServerCapabilities() : null;
                            break;
                        case 5:
                            appRecord.ApplicationId = new NodeId(100);
                            break;
                    }
                }
                testDataSet.Add(testData);
            }
            return testDataSet;
        }

        private ApplicationTestData RandomApplicationTestData()
        {
            ApplicationType appType = (ApplicationType)_randomSource.NextInt32((int)ApplicationType.ClientAndServer);
            string pureAppName = _dataGenerator.GetRandomString("en");
            pureAppName = Regex.Replace(pureAppName, @"[^\w\d\s]", "");
            string pureAppUri = Regex.Replace(pureAppName, @"[^\w\d]", "");
            string appName = "UA " + pureAppName;
            StringCollection domainNames = RandomDomainNames();
            string localhost = domainNames[0];
            string appUri = ("urn:localhost:opcfoundation.org:" + pureAppUri.ToLower()).Replace("localhost", localhost);
            string prodUri = "http://opcfoundation.org/UA/" + pureAppUri;
            StringCollection discoveryUrls = new StringCollection();
            StringCollection serverCapabilities = new StringCollection();
            switch (appType)
            {
                case ApplicationType.Client:
                    appName += " Client";
                    break;
                case ApplicationType.ClientAndServer:
                    appName += " Client and";
                    goto case ApplicationType.Server;
                case ApplicationType.Server:
                    appName += " Server";
                    int port = (_dataGenerator.GetRandomInt16() & 0x1fff) + 50000;
                    discoveryUrls = RandomDiscoveryUrl(domainNames, port, pureAppUri);
                    serverCapabilities = RandomServerCapabilities();
                    break;
            }
            ApplicationTestData testData = new ApplicationTestData
            {
                ApplicationRecord = new ApplicationRecordDataType
                {
                    ApplicationNames = new LocalizedTextCollection { new LocalizedText("en-us", appName) },
                    ApplicationUri = appUri,
                    ApplicationType = appType,
                    ProductUri = prodUri,
                    DiscoveryUrls = discoveryUrls,
                    ServerCapabilities = serverCapabilities
                },
                DomainNames = domainNames,
                Subject = String.Format("CN={0},DC={1},O=OPC Foundation", appName, localhost)
            };
            return testData;
        }

        private StringCollection RandomServerCapabilities()
        {
            var serverCapabilities = new StringCollection();
            int capabilities = _randomSource.NextInt32(8);
            foreach (var cap in _serverCapabilities)
            {
                if (_randomSource.NextInt32(100) > 50)
                {
                    serverCapabilities.Add(cap.Id);
                    if (capabilities-- == 0)
                    {
                        break;
                    }
                }
            }
            return serverCapabilities;
        }

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
        private const int goodApplicationsTestCount = 10;
        private const int invalidApplicationsTestCount = 10;
        private const int randomStart = 1;
        private RandomSource _randomSource;
        private DataGenerator _dataGenerator;
        private GlobalDiscoveryTestServer _server;
        private GlobalDiscoveryTestClient _client;
        private IList<ApplicationTestData> _goodApplicationTestSet;
        private IList<ApplicationTestData> _invalidApplicationTestSet;
        private ServerCapabilities _serverCapabilities;
        #endregion
    }
}