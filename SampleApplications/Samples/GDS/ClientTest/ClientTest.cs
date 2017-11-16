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
using System.Security.Cryptography.X509Certificates;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Gds;
using Opc.Ua.Gds.Client;
using Opc.Ua.Gds.Test;
using Opc.Ua.Test;

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
    [TestFixture, Category("GDSRegistrationAndPull")]
    public class ClientTest
    {
        #region Test Setup
        /// <summary>
        /// Set up a Global Discovery Server and Client instance and connect the session
        /// </summary>
        [OneTimeSetUp]
        protected void OneTimeSetUp()
        {
#if DEBUG
            // work around travis issue by selecting different ports on every run
            const int testPort = 60000;
#else
            const int testPort = 60010;
#endif
            _serverCapabilities = new ServerCapabilities();
            _randomSource = new RandomSource(randomStart);
            _dataGenerator = new DataGenerator(_randomSource);
            _server = new GlobalDiscoveryTestServer(true);
            _server.StartServer(true, testPort).Wait();

            // load client
            _gdsClient = new GlobalDiscoveryTestClient(true);
            _gdsClient.LoadClientConfiguration(testPort).Wait();

            // good applications test set
            _goodApplicationTestSet = ApplicationTestSet(goodApplicationsTestCount, false);
            _invalidApplicationTestSet = ApplicationTestSet(invalidApplicationsTestCount, true);

            _goodRegistrationOk = false;
            _invalidRegistrationOk = false;
        }

        /// <summary>
        /// Tear down the Global Discovery Server and disconnect the Client
        /// </summary>
        [OneTimeTearDown]
        protected void OneTimeTearDown()
        {
            _gdsClient.DisconnectClient();
            _gdsClient = null;
            _server.StopServer();
            _server = null;
            Thread.Sleep(1000);
        }

        [TearDown]
        protected void TearDown()
        {
            DisconnectGDS();
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
            ConnectGDS(true);
            foreach (var application in _goodApplicationTestSet)
            {
                NodeId id = _gdsClient.GDSClient.RegisterApplication(application.ApplicationRecord);
                Assert.NotNull(id);
                Assert.IsFalse(id.IsNullNodeId);
                Assert.AreEqual(id.IdType, IdType.Guid);
                application.ApplicationRecord.ApplicationId = id;
            }
            _goodRegistrationOk = true;
        }

        [Test, Order(101)]
        public void RegisterGoodApplicationsAuditEvents()
        {
            AssertIgnoreTestWithoutGoodRegistration();
            foreach (var application in _goodApplicationTestSet)
            {
                // TODO
            }
        }

        [Test, Order(110)]
        public void RegisterInvalidApplications()
        {
            ConnectGDS(true);
            foreach (var application in _invalidApplicationTestSet)
            {
                Assert.That(() => { NodeId id = _gdsClient.GDSClient.RegisterApplication(application.ApplicationRecord); }, Throws.Exception);
            }
            _invalidRegistrationOk = true;
        }

        [Test, Order(111)]
        public void RegisterInvalidApplicationsAuditEvents()
        {
            AssertIgnoreTestWithoutInvalidRegistration();
            ConnectGDS(true);
            foreach (var application in _invalidApplicationTestSet)
            {
                // TODO
            }
        }

        [Test, Order(120)]
        public void RegisterApplicationAsUser()
        {
            AssertIgnoreTestWithoutInvalidRegistration();
            ConnectGDS(false);
            foreach (var application in _invalidApplicationTestSet)
            {
                Assert.That(() => { NodeId id = _gdsClient.GDSClient.RegisterApplication(application.ApplicationRecord); }, Throws.Exception);
            }
        }

        [Test, Order(200)]
        public void UpdateGoodApplications()
        {
            AssertIgnoreTestWithoutGoodRegistration();
            ConnectGDS(true);
            foreach (var application in _goodApplicationTestSet)
            {
                var updatedApplicationRecord = (ApplicationRecordDataType)application.ApplicationRecord.MemberwiseClone();
                updatedApplicationRecord.ApplicationUri += "update";
                _gdsClient.GDSClient.UpdateApplication(updatedApplicationRecord);
                var result = _gdsClient.GDSClient.FindApplication(updatedApplicationRecord.ApplicationUri);
                _gdsClient.GDSClient.UpdateApplication(application.ApplicationRecord);
                Assert.NotNull(result);
                Assert.GreaterOrEqual(1, result.Length, "Couldn't find updated application record");
            }
        }

        [Test, Order(201)]
        public void UpdateGoodApplicationsAuditEvents()
        {
            AssertIgnoreTestWithoutGoodRegistration();
            foreach (var application in _goodApplicationTestSet)
            {
                // TODO
            }
        }

        [Test, Order(210)]
        public void UpdateGoodApplicationsWithNewGuid()
        {
            AssertIgnoreTestWithoutGoodRegistration();
            ConnectGDS(true);
            foreach (var application in _goodApplicationTestSet)
            {
                var testApplicationRecord = (ApplicationRecordDataType)application.ApplicationRecord.MemberwiseClone();
                testApplicationRecord.ApplicationId = new NodeId(Guid.NewGuid());
                Assert.That(() => { _gdsClient.GDSClient.UpdateApplication(testApplicationRecord); }, Throws.Exception);
            }
        }

        [Test, Order(211)]
        public void UpdateGoodApplicationsWithNewGuidAuditEvents()
        {
            AssertIgnoreTestWithoutGoodRegistration();
            ConnectGDS(true);
            foreach (var application in _goodApplicationTestSet)
            {
                // TODO
            }
        }

        [Test, Order(220)]
        public void UpdateInvalidApplications()
        {
            AssertIgnoreTestWithoutInvalidRegistration();
            ConnectGDS(true);
            foreach (var application in _invalidApplicationTestSet)
            {
                Assert.That(() => { _gdsClient.GDSClient.UpdateApplication(application.ApplicationRecord); }, Throws.Exception);
            }
        }

        [Test, Order(221)]
        public void UpdateInvalidApplicationsAuditEvents()
        {
            AssertIgnoreTestWithoutInvalidRegistration();
            ConnectGDS(true);
            foreach (var application in _invalidApplicationTestSet)
            {
                // TODO
            }
        }

        [Test, Order(400)]
        public void FindGoodApplications()
        {
            AssertIgnoreTestWithoutGoodRegistration();
            ConnectGDS(false);
            foreach (var application in _goodApplicationTestSet)
            {
                var result = _gdsClient.GDSClient.FindApplication(application.ApplicationRecord.ApplicationUri);
                Assert.NotNull(result);
                Assert.GreaterOrEqual(1, result.Length, "Couldn't find good application");
            }
        }

        [Test, Order(400)]
        public void FindInvalidApplications()
        {
            AssertIgnoreTestWithoutInvalidRegistration();
            ConnectGDS(true);
            foreach (var application in _invalidApplicationTestSet)
            {
                var result = _gdsClient.GDSClient.FindApplication(application.ApplicationRecord.ApplicationUri);
                Assert.NotNull(result);
                Assert.AreEqual(0, result.Length, "Found invalid application on server");
            }
        }

        [Test, Order(400)]
        public void GetGoodApplications()
        {
            AssertIgnoreTestWithoutGoodRegistration();
            ConnectGDS(false);
            foreach (var application in _goodApplicationTestSet)
            {
                var result = _gdsClient.GDSClient.GetApplication(application.ApplicationRecord.ApplicationId);
                Assert.NotNull(result);
                Assert.IsTrue(Utils.IsEqual(application.ApplicationRecord, result));
            }
        }

        [Test, Order(400)]
        public void GetInvalidApplications()
        {
            AssertIgnoreTestWithoutInvalidRegistration();
            ConnectGDS(true);
            foreach (var application in _invalidApplicationTestSet)
            {
                Assert.That(() => { var result = _gdsClient.GDSClient.GetApplication(application.ApplicationRecord.ApplicationId); }, Throws.Exception);
            }
        }

        [Test, Order(410)]
        public void QueryGoodServers()
        {
            AssertIgnoreTestWithoutGoodRegistration();
            ConnectGDS(false);
            // get all servers
            var allServers = _gdsClient.GDSClient.QueryServers(0, "", "", "", null);
            int totalCount = 0;
            uint firstID = uint.MaxValue, lastID = 0;
            Assert.IsNotNull(allServers);
            foreach (var server in allServers)
            {
                var oneServers = _gdsClient.GDSClient.QueryServers(server.RecordId, 1, "", "", "", null);
                firstID = Math.Min(firstID, server.RecordId);
                lastID = Math.Max(lastID, server.RecordId);
                totalCount++;
            }

            // repeating queries to get all servers
            uint nextID = 0;
            const uint iterationCount = 10;
            int serversQueried = 0;
            while (true)
            {
                var tenServers = _gdsClient.GDSClient.QueryServers(nextID, iterationCount, "", "", "", null);
                Assert.IsNotNull(tenServers);
                serversQueried += tenServers.Count;
                if (tenServers.Count == 0)
                {
                    break;
                }
                Assert.LessOrEqual(tenServers.Count, iterationCount);
                uint previousID = nextID;
                nextID = tenServers[tenServers.Count - 1].RecordId + 1;
                Assert.Greater(nextID, previousID);
            }
            Assert.AreEqual(serversQueried, totalCount);

            // search aplications by name
            const int searchPatternLength = 5;
            foreach (var application in _goodApplicationTestSet)
            {
                var atLeastOneServer = _gdsClient.GDSClient.QueryServers(1, application.ApplicationRecord.ApplicationNames[0].Text, "", "", null);
                Assert.IsNotNull(atLeastOneServer);
                if (application.ApplicationRecord.ApplicationType != ApplicationType.Client)
                {
                    Assert.GreaterOrEqual(atLeastOneServer.Count, 1);
                }
                else
                {
                    Assert.AreEqual(atLeastOneServer.Count, 0);
                }

                string searchName = application.ApplicationRecord.ApplicationNames[0].Text.Trim();
                if (searchName.Length > searchPatternLength)
                {
                    searchName = searchName.Substring(0, searchPatternLength) + "%";
                }
                atLeastOneServer = _gdsClient.GDSClient.QueryServers(1, searchName, "", "", null);
                Assert.IsNotNull(atLeastOneServer);
                if (application.ApplicationRecord.ApplicationType != ApplicationType.Client)
                {
                    Assert.GreaterOrEqual(atLeastOneServer.Count, 1);
                }
            }
        }

        [Test, Order(500)]
        public void StartGoodNewKeyPairRequests()
        {
            AssertIgnoreTestWithoutGoodRegistration();
            ConnectGDS(true);
            foreach (var application in _goodApplicationTestSet)
            {
                Assert.Null(application.CertificateRequestId);
                NodeId requestId = _gdsClient.GDSClient.StartNewKeyPairRequest(
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
        public void StartInvalidNewKeyPairRequests()
        {
            AssertIgnoreTestWithoutInvalidRegistration();
            ConnectGDS(true);
            foreach (var application in _invalidApplicationTestSet)
            {
                Assert.Null(application.CertificateRequestId);
                Assert.That(() =>
                {
                    NodeId requestId = _gdsClient.GDSClient.StartNewKeyPairRequest(
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

        [Test, Order(510)]
        public void FinishGoodNewKeyPairRequests()
        {
            AssertIgnoreTestWithoutGoodRegistration();
            ConnectGDS(true);
            bool requestBusy;
            do
            {
                requestBusy = false;
                foreach (var application in _goodApplicationTestSet)
                {
                    if (application.CertificateRequestId != null)
                    {
                        byte[] certificate = _gdsClient.GDSClient.FinishRequest(
                            application.ApplicationRecord.ApplicationId,
                            application.CertificateRequestId,
                            out byte[] privateKey,
                            out byte[][] issuerCertificates
                            );

                        if (certificate != null)
                        {
                            Assert.NotNull(certificate);
                            Assert.NotNull(privateKey);
                            Assert.NotNull(issuerCertificates);
                            application.Certificate = certificate;
                            application.PrivateKey = privateKey;
                            application.IssuerCertificates = issuerCertificates;
                            application.CertificateRequestId = null;
                            // TODO: verify cert subject and extensions
                        }
                        else
                        {
                            requestBusy = true;
                        }
                    }
                }

                if (requestBusy)
                {
                    Thread.Sleep(500);
                }
            } while (requestBusy);
        }

        [Test, Order(511)]
        public void FinishInvalidNewKeyPairRequests()
        {
            AssertIgnoreTestWithoutInvalidRegistration();
            ConnectGDS(true);
            foreach (var application in _invalidApplicationTestSet)
            {
                Assert.That(() =>
                {
                    byte[] certificate = _gdsClient.GDSClient.FinishRequest(
                        application.ApplicationRecord.ApplicationId,
                        new NodeId(Guid.NewGuid()),
                        out byte[] privateKey,
                        out byte[][] issuerCertificates
                    );
                }, Throws.Exception);
            }
        }

        [Test, Order(520)]
        public void StartGoodSigningRequests()
        {
            AssertIgnoreTestWithoutGoodRegistration();
            ConnectGDS(true);
            foreach (var application in _goodApplicationTestSet)
            {
                Assert.Null(application.CertificateRequestId);
                X509Certificate2 csrCertificate;
                if (application.PrivateKeyFormat == "PFX")
                {
                    csrCertificate = CertificateFactory.CreateCertificateFromPKCS12(application.PrivateKey, application.PrivateKeyPassword);
                }
                else
                {
                    csrCertificate = CertificateFactory.CreateCertificateWithPEMPrivateKey(new X509Certificate2(application.Certificate), application.PrivateKey, application.PrivateKeyPassword);
                }
                byte[] certificateRequest = CertificateFactory.CreateSigningRequest(csrCertificate, application.DomainNames);
                NodeId requestId = _gdsClient.GDSClient.StartSigningRequest(
                    application.ApplicationRecord.ApplicationId,
                    application.CertificateGroupId,
                    application.CertificateTypeId,
                    certificateRequest);
                Assert.NotNull(requestId);
                application.CertificateRequestId = requestId;
            }
        }

        [Test, Order(521)]
        public void FinishGoodSigningRequests()
        {
            AssertIgnoreTestWithoutGoodRegistration();
            ConnectGDS(true);
            bool requestBusy;
            do
            {
                requestBusy = false;

                foreach (var application in _goodApplicationTestSet)
                {
                    if (application.CertificateRequestId != null)
                    {
                        var certificate = _gdsClient.GDSClient.FinishRequest(
                            application.ApplicationRecord.ApplicationId,
                            application.CertificateRequestId,
                            out byte[] privateKey,
                            out byte[][] issuerCertificates
                            );

                        if (certificate != null)
                        {
                            Assert.Null(privateKey);
                            Assert.NotNull(issuerCertificates);
                            application.Certificate = certificate;
                            application.IssuerCertificates = issuerCertificates;
                            application.CertificateRequestId = null;
                            // TODO: verify cert subject and extensions
                        }
                        else
                        {
                            requestBusy = true;
                        }
                    }
                }

                if (requestBusy)
                {
                    Thread.Sleep(500);
                }
            } while (requestBusy);

        }

        [Test, Order(600)]
        public void GetGoodCertificateGroupsNullTests()
        {
            AssertIgnoreTestWithoutGoodRegistration();
            ConnectGDS(true);

            Assert.That(() =>
            {
                _gdsClient.GDSClient.GetCertificateGroups(null);
            }, Throws.Exception);

            foreach (var application in _goodApplicationTestSet)
            {
                var trustListId = _gdsClient.GDSClient.GetTrustList(application.ApplicationRecord.ApplicationId, null);
                var trustList = _gdsClient.GDSClient.ReadTrustList(trustListId);
                Assert.That(() =>
                {
                    _gdsClient.GDSClient.ReadTrustList(null);
                }, Throws.Exception);
                var certificateGroups = _gdsClient.GDSClient.GetCertificateGroups(application.ApplicationRecord.ApplicationId);
                foreach (var certificateGroup in certificateGroups)
                {
                    Assert.That(() =>
                    {
                        _gdsClient.GDSClient.GetTrustList(null, certificateGroup);
                    }, Throws.Exception);
                }
            }
        }

        [Test, Order(601)]
        public void GetInvalidCertificateGroupsNullTests()
        {
            AssertIgnoreTestWithoutInvalidRegistration();
            ConnectGDS(true);
            Assert.That(() =>
            {
                _gdsClient.GDSClient.GetCertificateGroups(null);
            }, Throws.Exception);
            Assert.That(() =>
            {
                _gdsClient.GDSClient.GetCertificateGroups(new NodeId(Guid.NewGuid()));
            }, Throws.Exception);

            foreach (var application in _invalidApplicationTestSet)
            {
                Assert.That(() =>
                {
                    var trustListId = _gdsClient.GDSClient.GetTrustList(application.ApplicationRecord.ApplicationId, null);
                }, Throws.Exception);
                Assert.That(() =>
                {
                    var trustListId = _gdsClient.GDSClient.GetTrustList(application.ApplicationRecord.ApplicationId, new NodeId(Guid.NewGuid()));
                }, Throws.Exception);
                Assert.That(() =>
                {
                    var certificateGroups = _gdsClient.GDSClient.GetCertificateGroups(application.ApplicationRecord.ApplicationId);
                }, Throws.Exception);
            }
        }

        [Test, Order(610)]
        public void GetGoodCertificateGroupsAndTrustLists()
        {
            AssertIgnoreTestWithoutGoodRegistration();
            ConnectGDS(true);

            foreach (var application in _goodApplicationTestSet)
            {
                var certificateGroups = _gdsClient.GDSClient.GetCertificateGroups(application.ApplicationRecord.ApplicationId);
                foreach (var certificateGroup in certificateGroups)
                {
                    var trustListId = _gdsClient.GDSClient.GetTrustList(application.ApplicationRecord.ApplicationId, certificateGroup);
                    // Opc.Ua.TrustListDataType
                    var trustList = _gdsClient.GDSClient.ReadTrustList(trustListId);
                }
            }
        }

        [Test, Order(690)]
        public void GetGoodCertificateStatus()
        {
            AssertIgnoreTestWithoutGoodRegistration();
            ConnectGDS(true);
            foreach (var application in _goodApplicationTestSet)
            {
                var certificateStatus = _gdsClient.GDSClient.GetCertificateStatus(application.ApplicationRecord.ApplicationId, null, null);
            }
        }

        [Test, Order(691)]
        public void GetInvalidCertificateStatus()
        {
            AssertIgnoreTestWithoutInvalidRegistration();
            ConnectGDS(true);
            foreach (var application in _invalidApplicationTestSet)
            {
                Assert.That(() =>
                {
                    var certificateStatus = _gdsClient.GDSClient.GetCertificateStatus(application.ApplicationRecord.ApplicationId, null, null);
                }, Throws.Exception);
            }
        }

        [Test, Order(900)]
        public void UnregisterGoodApplications()
        {
            ConnectGDS(true);
            foreach (var application in _goodApplicationTestSet)
            {
                _gdsClient.GDSClient.UnregisterApplication(application.ApplicationRecord.ApplicationId);
            }
        }

        [Test, Order(901)]
        public void UnregisterGoodApplicationsAuditEvents()
        {
            ConnectGDS(true);
            foreach (var application in _goodApplicationTestSet)
            {
                // TODO
            }
        }

        [Test, Order(910)]
        public void UnregisterInvalidApplications()
        {
            ConnectGDS(true);
            foreach (var application in _invalidApplicationTestSet)
            {
                Assert.That(() => { _gdsClient.GDSClient.UnregisterApplication(application.ApplicationRecord.ApplicationId); }, Throws.Exception);
            }
        }

        [Test, Order(911)]
        public void UnregisterInvalidApplicationsAuditEvents()
        {
            ConnectGDS(true);
            foreach (var application in _invalidApplicationTestSet)
            {
                // TODO
            }
        }

        [Test, Order(920)]
        public void UnregisterUnregisteredGoodApplications()
        {
            ConnectGDS(true);
            foreach (var application in _goodApplicationTestSet)
            {
                Assert.That(() => { _gdsClient.GDSClient.UnregisterApplication(application.ApplicationRecord.ApplicationId); }, Throws.Exception);
            }
        }

        [Test, Order(921)]
        public void UnregisterUnregisteredGoodApplicationsAuditEvents()
        {
            ConnectGDS(true);
            foreach (var application in _goodApplicationTestSet)
            {
                // TODO
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
            string privateKeyFormat = _randomSource.NextInt32(1) == 0 ? "PEM" : "PFX";
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
                Subject = String.Format("CN={0},DC={1},O=OPC Foundation", appName, localhost),
                PrivateKeyFormat = privateKeyFormat
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

        private void ConnectGDS(bool admin)
        {
            _gdsClient.GDSClient.AdminCredentials = new UserIdentity(admin ? "appadmin" : "appuser", "demo");
            _gdsClient.GDSClient.Connect(_gdsClient.GDSClient.EndpointUrl).Wait();
        }

        private void DisconnectGDS()
        {
            _gdsClient.GDSClient.Disconnect();
        }

        private void AssertIgnoreTestWithoutGoodRegistration()
        {
            if (!_goodRegistrationOk)
            {
                Assert.Ignore("Test requires good application registrations.");
            }
        }

        private void AssertIgnoreTestWithoutInvalidRegistration()
        {
            if (!_invalidRegistrationOk)
            {
                Assert.Ignore("Test requires invalid application registration.");
            }
        }
        #endregion
        #region Private Fields
        private const int goodApplicationsTestCount = 10;
        private const int invalidApplicationsTestCount = 10;
        private const int randomStart = 1;
        private RandomSource _randomSource;
        private DataGenerator _dataGenerator;
        private GlobalDiscoveryTestServer _server;
        private GlobalDiscoveryTestClient _gdsClient;
        private IList<ApplicationTestData> _goodApplicationTestSet;
        private IList<ApplicationTestData> _invalidApplicationTestSet;
        private ServerCapabilities _serverCapabilities;
        private bool _goodRegistrationOk;
        private bool _invalidRegistrationOk;
        #endregion
    }

}