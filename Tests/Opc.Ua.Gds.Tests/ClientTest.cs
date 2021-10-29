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
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Gds.Tests
{
    /// <summary>
    /// Test GDS Registration and Client Pull.
    /// </summary>
    [TestFixture, Category("GDSRegistrationAndPull"), Category("GDS")]
    [SetCulture("en-us"), SetUICulture("en-us")]
    [NonParallelizable]
    public class ClientTest
    {
        #region Test Setup
        public class ConnectionProfile : IFormattable
        {
            public ConnectionProfile(string securityProfileUri, MessageSecurityMode messageSecurityMode)
            {
                SecurityProfileUri = securityProfileUri;
                MessageSecurityMode = messageSecurityMode;
            }

            public string SecurityProfileUri { get; set; }
            public MessageSecurityMode MessageSecurityMode { get; set; }

            public string ToString(string format, IFormatProvider formatProvider)
            {
                return $"{SecurityProfileUri.Split('#').Last()}:{MessageSecurityMode}";
            }
        }

        /// <summary>
        /// Set up a Global Discovery Server and Client instance and connect the session
        /// </summary>
        [OneTimeSetUp]
        protected async Task OneTimeSetUp()
        {
            // start GDS
            m_server = await TestUtils.StartGDS(true).ConfigureAwait(false);

            // load client
            m_gdsClient = new GlobalDiscoveryTestClient(true);
            await m_gdsClient.LoadClientConfiguration(m_server.BasePort).ConfigureAwait(false);

            // good applications test set
            m_appTestDataGenerator = new ApplicationTestDataGenerator(1);
            m_goodApplicationTestSet = m_appTestDataGenerator.ApplicationTestSet(kGoodApplicationsTestCount, false);
            m_invalidApplicationTestSet = m_appTestDataGenerator.ApplicationTestSet(kInvalidApplicationsTestCount, true);

            m_goodRegistrationOk = false;
            m_invalidRegistrationOk = false;
            m_goodNewKeyPairRequestOk = false;
        }

        /// <summary>
        /// Tear down the Global Discovery Server and disconnect the Client
        /// </summary>
        [OneTimeTearDown]
        protected void OneTimeTearDown()
        {
            m_gdsClient.DisconnectClient();
            m_gdsClient = null;
            m_server.StopServer();
            m_server = null;
            Thread.Sleep(1000);
        }

        [SetUp]
        protected void SetUp()
        {
            m_server.ResetLogFile();
        }

        [TearDown]
        protected void TearDown()
        {
            DisconnectGDS();
            try
            {
                TestContext.AddTestAttachment(m_server.GetLogFilePath(), "GDS Client and Server logs");
            }
            catch { }
        }
        #endregion

        #region Test Methods
        /// <summary>
        /// Clean the app database from application Uri used during test.
        /// </summary>
        [Test, Order(10)]
        public void CleanGoodApplications()
        {
            ConnectGDS(true);
            foreach (var application in m_goodApplicationTestSet)
            {
                var applicationDataRecords = m_gdsClient.GDSClient.FindApplication(application.ApplicationRecord.ApplicationUri);
                if (applicationDataRecords != null)
                {
                    foreach (var applicationDataRecord in applicationDataRecords)
                    {
                        m_gdsClient.GDSClient.UnregisterApplication(applicationDataRecord.ApplicationId);
                    }
                }
            }
        }

        /// <summary>
        /// Register the good applications in the database.
        /// </summary>
        [Test, Order(100)]
        public void RegisterGoodApplications()
        {
            ConnectGDS(true);
            foreach (var application in m_goodApplicationTestSet)
            {
                NodeId id = m_gdsClient.GDSClient.RegisterApplication(application.ApplicationRecord);
                Assert.NotNull(id);
                Assert.IsFalse(id.IsNullNodeId);
                Assert.That(id.IdType == IdType.Guid || id.IdType == IdType.String);
                application.ApplicationRecord.ApplicationId = id;
            }
            m_goodRegistrationOk = true;
        }

        [Test, Order(105)]
        public void RegisterDuplicateGoodApplications()
        {
            AssertIgnoreTestWithoutGoodRegistration();
            ConnectGDS(true);
            foreach (var application in m_goodApplicationTestSet)
            {
                ApplicationRecordDataType newRecord = (ApplicationRecordDataType)application.ApplicationRecord.MemberwiseClone();
                newRecord.ApplicationId = null;
                NodeId id = m_gdsClient.GDSClient.RegisterApplication(newRecord);
                Assert.NotNull(id);
                Assert.IsFalse(id.IsNullNodeId);
                Assert.That(id.IdType == IdType.Guid || id.IdType == IdType.String);
                newRecord.ApplicationId = id;
                var applicationDataRecords = m_gdsClient.GDSClient.FindApplication(newRecord.ApplicationUri);
                Assert.NotNull(applicationDataRecords);
                bool newIdFound = false;
                bool registeredIdFound = false;
                foreach (var applicationDataRecord in applicationDataRecords)
                {
                    if (applicationDataRecord.ApplicationId == newRecord.ApplicationId)
                    {
                        m_gdsClient.GDSClient.UnregisterApplication(id);
                        newIdFound = true;
                    }
                    else if (applicationDataRecord.ApplicationId == application.ApplicationRecord.ApplicationId)
                    {
                        registeredIdFound = true;
                    }
                }
                Assert.IsTrue(newIdFound);
                Assert.IsTrue(registeredIdFound);
            }
        }

        [Test, Order(110)]
        public void RegisterInvalidApplications()
        {
            ConnectGDS(true);
            foreach (var application in m_invalidApplicationTestSet)
            {
                Assert.That(() => { _ = m_gdsClient.GDSClient.RegisterApplication(application.ApplicationRecord); }, Throws.Exception);
            }
            m_invalidRegistrationOk = true;
        }

        [Test, Order(120)]
        public void RegisterApplicationAsUser()
        {
            AssertIgnoreTestWithoutInvalidRegistration();
            ConnectGDS(false);
            foreach (var application in m_invalidApplicationTestSet)
            {
                Assert.That(() => { _ = m_gdsClient.GDSClient.RegisterApplication(application.ApplicationRecord); }, Throws.Exception);
            }
        }

        [Test, Order(200)]
        public void UpdateGoodApplications()
        {
            AssertIgnoreTestWithoutGoodRegistration();
            ConnectGDS(true);
            foreach (var application in m_goodApplicationTestSet)
            {
                var updatedApplicationRecord = (ApplicationRecordDataType)application.ApplicationRecord.MemberwiseClone();
                updatedApplicationRecord.ApplicationUri += "update";
                m_gdsClient.GDSClient.UpdateApplication(updatedApplicationRecord);
                var result = m_gdsClient.GDSClient.FindApplication(updatedApplicationRecord.ApplicationUri);
                m_gdsClient.GDSClient.UpdateApplication(application.ApplicationRecord);
                Assert.NotNull(result);
                Assert.GreaterOrEqual(1, result.Length, "Couldn't find updated application record");
            }
        }

        [Test, Order(210)]
        public void UpdateGoodApplicationsWithNewGuid()
        {
            AssertIgnoreTestWithoutGoodRegistration();
            ConnectGDS(true);
            foreach (var application in m_goodApplicationTestSet)
            {
                var testApplicationRecord = (ApplicationRecordDataType)application.ApplicationRecord.MemberwiseClone();
                testApplicationRecord.ApplicationId = new NodeId(Guid.NewGuid());
                Assert.That(() => { m_gdsClient.GDSClient.UpdateApplication(testApplicationRecord); }, Throws.Exception);
            }
        }

        [Test, Order(210)]
        public void UpdateGoodApplicationsWithNewString()
        {
            AssertIgnoreTestWithoutGoodRegistration();
            ConnectGDS(true);
            foreach (var application in m_goodApplicationTestSet)
            {
                var testApplicationRecord = (ApplicationRecordDataType)application.ApplicationRecord.MemberwiseClone();
                testApplicationRecord.ApplicationId = new NodeId(m_appTestDataGenerator.DataGenerator.GetRandomString("en"));
                Assert.That(() => { m_gdsClient.GDSClient.UpdateApplication(testApplicationRecord); }, Throws.Exception);
            }
        }

        [Test, Order(220)]
        public void UpdateInvalidApplications()
        {
            AssertIgnoreTestWithoutInvalidRegistration();
            ConnectGDS(true);
            foreach (var application in m_invalidApplicationTestSet)
            {
                Assert.That(() => { m_gdsClient.GDSClient.UpdateApplication(application.ApplicationRecord); }, Throws.Exception);
            }
        }

        [Test, Order(400)]
        public void FindGoodApplications()
        {
            AssertIgnoreTestWithoutGoodRegistration();
            ConnectGDS(false);
            foreach (var application in m_goodApplicationTestSet)
            {
                var result = m_gdsClient.GDSClient.FindApplication(application.ApplicationRecord.ApplicationUri);
                Assert.NotNull(result);
                Assert.GreaterOrEqual(result.Length, 1, "Couldn't find good application");
            }
        }

        [Test, Order(400)]
        public void FindInvalidApplications()
        {
            AssertIgnoreTestWithoutInvalidRegistration();
            ConnectGDS(true);
            foreach (var application in m_invalidApplicationTestSet)
            {
                var result = m_gdsClient.GDSClient.FindApplication(application.ApplicationRecord.ApplicationUri);
                if (result != null)
                {
                    Assert.NotNull(result);
                    Assert.AreEqual(0, result.Length, "Found invalid application on server");
                }
            }
        }

        [Test, Order(400)]
        public void GetGoodApplications()
        {
            AssertIgnoreTestWithoutGoodRegistration();
            ConnectGDS(false);
            foreach (var application in m_goodApplicationTestSet)
            {
                var result = m_gdsClient.GDSClient.GetApplication(application.ApplicationRecord.ApplicationId);
                Assert.NotNull(result);
                result.ServerCapabilities.Sort();
                application.ApplicationRecord.ServerCapabilities.Sort();
                Assert.IsTrue(Utils.IsEqual(application.ApplicationRecord, result));
            }
        }

        [Test, Order(401)]
        public void GetGoodApplicationsTestApplicationId()
        {
            AssertIgnoreTestWithoutGoodRegistration();
            ConnectGDS(false);
            foreach (var application in m_goodApplicationTestSet)
            {
                var result = m_gdsClient.GDSClient.GetApplication(application.ApplicationRecord.ApplicationId);
                Assert.NotNull(result);
                Assert.IsTrue(Utils.IsEqual(application.ApplicationRecord.ApplicationId, result.ApplicationId));
            }
        }

        [Test, Order(401)]
        public void GetGoodApplicationsTestApplicationNames()
        {
            AssertIgnoreTestWithoutGoodRegistration();
            ConnectGDS(false);
            foreach (var application in m_goodApplicationTestSet)
            {
                var result = m_gdsClient.GDSClient.GetApplication(application.ApplicationRecord.ApplicationId);
                Assert.NotNull(result);
                Assert.IsTrue(Utils.IsEqual(application.ApplicationRecord.ApplicationNames, result.ApplicationNames));
            }
        }

        [Test, Order(401)]
        public void GetGoodApplicationsTestApplicationType()
        {
            AssertIgnoreTestWithoutGoodRegistration();
            ConnectGDS(false);
            foreach (var application in m_goodApplicationTestSet)
            {
                var result = m_gdsClient.GDSClient.GetApplication(application.ApplicationRecord.ApplicationId);
                Assert.NotNull(result);
                Assert.IsTrue(Utils.IsEqual(application.ApplicationRecord.ApplicationType, result.ApplicationType));
            }
        }

        [Test, Order(401)]
        public void GetGoodApplicationsTestApplicationUri()
        {
            AssertIgnoreTestWithoutGoodRegistration();
            ConnectGDS(false);
            foreach (var application in m_goodApplicationTestSet)
            {
                var result = m_gdsClient.GDSClient.GetApplication(application.ApplicationRecord.ApplicationId);
                Assert.NotNull(result);
                Assert.IsTrue(Utils.IsEqual(application.ApplicationRecord.ApplicationUri, result.ApplicationUri));
            }
        }

        [Test, Order(401)]
        public void GetGoodApplicationsTestDiscoveryUrls()
        {
            AssertIgnoreTestWithoutGoodRegistration();
            ConnectGDS(false);
            foreach (var application in m_goodApplicationTestSet)
            {
                var result = m_gdsClient.GDSClient.GetApplication(application.ApplicationRecord.ApplicationId);
                Assert.NotNull(result);
                Assert.IsTrue(Utils.IsEqual(application.ApplicationRecord.DiscoveryUrls, result.DiscoveryUrls));
            }
        }

        [Test, Order(401)]
        public void GetGoodApplicationsTestProductUri()
        {
            AssertIgnoreTestWithoutGoodRegistration();
            ConnectGDS(false);
            foreach (var application in m_goodApplicationTestSet)
            {
                var result = m_gdsClient.GDSClient.GetApplication(application.ApplicationRecord.ApplicationId);
                Assert.NotNull(result);
                Assert.IsTrue(Utils.IsEqual(application.ApplicationRecord.ProductUri, result.ProductUri));
            }
        }

        [Test, Order(401)]
        public void GetGoodApplicationsTestServerCapabilities()
        {
            AssertIgnoreTestWithoutGoodRegistration();
            ConnectGDS(false);
            foreach (var application in m_goodApplicationTestSet)
            {
                var result = m_gdsClient.GDSClient.GetApplication(application.ApplicationRecord.ApplicationId);
                Assert.NotNull(result);
                result.ServerCapabilities.Sort();
                application.ApplicationRecord.ServerCapabilities.Sort();
                Assert.IsTrue(Utils.IsEqual(application.ApplicationRecord.ServerCapabilities, result.ServerCapabilities));
            }
        }

        [Test, Order(400)]
        public void GetInvalidApplications()
        {
            AssertIgnoreTestWithoutInvalidRegistration();
            ConnectGDS(true);
            foreach (var application in m_invalidApplicationTestSet)
            {
                Assert.That(() => { var result = m_gdsClient.GDSClient.GetApplication(application.ApplicationRecord.ApplicationId); }, Throws.Exception);
            }
        }

        [Test, Order(410)]
        public void QueryAllServers()
        {
            AssertIgnoreTestWithoutGoodRegistration();
            ConnectGDS(false);
            // get all servers
            var allServers = m_gdsClient.GDSClient.QueryServers(0, "", "", "", new List<string>());
            int totalCount = 0;
            uint firstID = uint.MaxValue, lastID = 0;
            Assert.IsNotNull(allServers);
            foreach (var server in allServers)
            {
                var oneServers = m_gdsClient.GDSClient.QueryServers(server.RecordId, 1, "", "", "", new List<string>());
                Assert.IsNotNull(oneServers);
                Assert.GreaterOrEqual(oneServers.Count, 1);
                foreach (var oneServer in oneServers)
                {
                    Assert.AreEqual(oneServer.RecordId, server.RecordId);
                }
                firstID = Math.Min(firstID, server.RecordId);
                lastID = Math.Max(lastID, server.RecordId);
                totalCount++;
            }
            Assert.GreaterOrEqual(totalCount, kGoodApplicationsTestCount);
            Assert.AreEqual(totalCount, allServers.Count);
            Assert.GreaterOrEqual(lastID, firstID);
            Assert.GreaterOrEqual(lastID, 1);
            Assert.GreaterOrEqual(firstID, 1);
        }

        [Test, Order(411)]
        public void QueryAllServersNull()
        {
            AssertIgnoreTestWithoutGoodRegistration();
            ConnectGDS(false);
            // get all servers
            var allServers = m_gdsClient.GDSClient.QueryServers(0, null, null, null, null);
            int totalCount = 0;
            uint firstID = uint.MaxValue, lastID = 0;
            Assert.IsNotNull(allServers);
            foreach (var server in allServers)
            {
                var oneServers = m_gdsClient.GDSClient.QueryServers(server.RecordId, 1, null, null, null, null);
                Assert.IsNotNull(oneServers);
                Assert.GreaterOrEqual(oneServers.Count, 1);
                foreach (var oneServer in oneServers)
                {
                    Assert.AreEqual(oneServer.RecordId, server.RecordId);
                }
                firstID = Math.Min(firstID, server.RecordId);
                lastID = Math.Max(lastID, server.RecordId);
                totalCount++;
            }
            Assert.GreaterOrEqual(totalCount, kGoodApplicationsTestCount);
            Assert.AreEqual(totalCount, allServers.Count);
            Assert.GreaterOrEqual(lastID, firstID);
            Assert.GreaterOrEqual(lastID, 1);
            Assert.GreaterOrEqual(firstID, 1);
        }

        [Test, Order(420)]
        public void QueryGoodServersBatches()
        {
            // repeating queries to get all servers
            uint nextID = 0;
            uint iterationCount = Math.Min(10, (uint)(kGoodApplicationsTestCount / 2));
            int serversOnNetwork = 0;
            int goodServersOnNetwork = GoodServersOnNetworkCount();
            while (true)
            {
                var iterServers = m_gdsClient.GDSClient.QueryServers(nextID, iterationCount, "", "", "", null);
                Assert.IsNotNull(iterServers);
                serversOnNetwork += iterServers.Count;
                if (iterServers.Count == 0)
                {
                    break;
                }
                uint previousID = nextID;
                nextID = iterServers[iterServers.Count - 1].RecordId + 1;
                Assert.Greater(nextID, previousID);
            }
            Assert.GreaterOrEqual(serversOnNetwork, goodServersOnNetwork);
        }

        [Test, Order(430)]
        public void QueryServersByName()
        {
            // search aplications by name
            const int searchPatternLength = 5;
            foreach (var application in m_goodApplicationTestSet)
            {
                var atLeastOneServer = m_gdsClient.GDSClient.QueryServers(1, application.ApplicationRecord.ApplicationNames[0].Text, "", "", null);
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
                atLeastOneServer = m_gdsClient.GDSClient.QueryServers(1, searchName, "", "", null);
                Assert.IsNotNull(atLeastOneServer);
                if (application.ApplicationRecord.ApplicationType != ApplicationType.Client)
                {
                    Assert.GreaterOrEqual(atLeastOneServer.Count, 1);
                }
            }
        }

        [Test, Order(440)]
        public void QueryServersByAppUri()
        {
            // search aplications by name
            const int searchPatternLength = 5;
            foreach (var application in m_goodApplicationTestSet)
            {
                var atLeastOneServer = m_gdsClient.GDSClient.QueryServers(1, null, application.ApplicationRecord.ApplicationUri, null, null);
                Assert.IsNotNull(atLeastOneServer);
                if (application.ApplicationRecord.ApplicationType != ApplicationType.Client)
                {
                    Assert.GreaterOrEqual(atLeastOneServer.Count, 1);
                }
                else
                {
                    Assert.AreEqual(atLeastOneServer.Count, 0);
                }

                string searchName = application.ApplicationRecord.ApplicationUri;
                if (searchName.Length > searchPatternLength)
                {
                    searchName = searchName.Substring(0, searchPatternLength) + "%";
                }
                atLeastOneServer = m_gdsClient.GDSClient.QueryServers(1, null, searchName, null, null);
                Assert.IsNotNull(atLeastOneServer);
                if (application.ApplicationRecord.ApplicationType != ApplicationType.Client)
                {
                    Assert.GreaterOrEqual(atLeastOneServer.Count, 1);
                }
            }
        }

        [Test, Order(450)]
        public void QueryServersByProductUri()
        {
            // search aplications by name
            const int searchPatternLength = 5;
            foreach (var application in m_goodApplicationTestSet)
            {
                var atLeastOneServer = m_gdsClient.GDSClient.QueryServers(1, null, null, application.ApplicationRecord.ProductUri, null);
                Assert.IsNotNull(atLeastOneServer);
                if (application.ApplicationRecord.ApplicationType != ApplicationType.Client)
                {
                    Assert.GreaterOrEqual(atLeastOneServer.Count, 1);
                }
                else
                {
                    Assert.AreEqual(atLeastOneServer.Count, 0);
                }

                string searchName = application.ApplicationRecord.ProductUri;
                if (searchName.Length > searchPatternLength)
                {
                    searchName = searchName.Substring(0, searchPatternLength) + "%";
                }
                atLeastOneServer = m_gdsClient.GDSClient.QueryServers(1, null, null, searchName, null);
                Assert.IsNotNull(atLeastOneServer);
                if (application.ApplicationRecord.ApplicationType != ApplicationType.Client)
                {
                    Assert.GreaterOrEqual(atLeastOneServer.Count, 1);
                }
            }
        }

        [Test, Order(480)]
        public void QueryAllApplications()
        {
            AssertIgnoreTestWithoutGoodRegistration();
            ConnectGDS(false);
            // get all applications
            DateTime lastResetCounterTime;
            uint nextRecordId;
            var allApplications = m_gdsClient.GDSClient.QueryApplications(0, 0, "", "", 0, "", new List<string>(), out lastResetCounterTime, out nextRecordId);
            int totalCount = 0;
            Assert.IsNotNull(allApplications);
            nextRecordId = 0;
            foreach (var application in allApplications)
            {
                var oneApplication = m_gdsClient.GDSClient.QueryApplications(nextRecordId, 1, "", "", 0, "", new List<string>(), out lastResetCounterTime, out nextRecordId);
                Assert.IsNotNull(oneApplication);
                Assert.GreaterOrEqual(oneApplication.Count, 1);
                foreach (var oneApp in oneApplication)
                {
                    //Assert.AreEqual(oneApp., server.RecordId);
                }
                totalCount++;
            }
            Assert.GreaterOrEqual(totalCount, kGoodApplicationsTestCount);
            Assert.AreEqual(totalCount, allApplications.Count);
        }


        [Test, Order(500)]
        public void StartGoodNewKeyPairRequests()
        {
            AssertIgnoreTestWithoutGoodRegistration();
            ConnectGDS(true);
            foreach (var application in m_goodApplicationTestSet)
            {
                Assert.Null(application.CertificateRequestId);
                NodeId requestId = m_gdsClient.GDSClient.StartNewKeyPairRequest(
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
            m_goodNewKeyPairRequestOk = true;
        }

        [Test, Order(501)]
        public void StartInvalidNewKeyPairRequests()
        {
            AssertIgnoreTestWithoutInvalidRegistration();
            ConnectGDS(true);
            foreach (var application in m_invalidApplicationTestSet)
            {
                Assert.Null(application.CertificateRequestId);
                Assert.That(() => {
                    _ = m_gdsClient.GDSClient.StartNewKeyPairRequest(
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
            AssertIgnoreTestWithoutGoodNewKeyPairRequest();
            ConnectGDS(true);
            bool requestBusy;
            DateTime now = DateTime.UtcNow;
            do
            {
                requestBusy = false;
                foreach (var application in m_goodApplicationTestSet)
                {
                    if (application.CertificateRequestId != null)
                    {
                        try
                        {
                            byte[] certificate = m_gdsClient.GDSClient.FinishRequest(
                                application.ApplicationRecord.ApplicationId,
                                application.CertificateRequestId,
                                out byte[] privateKey,
                                out byte[][] issuerCertificates
                                );

                            if (certificate != null)
                            {
                                application.CertificateRequestId = null;

                                Assert.NotNull(certificate);
                                Assert.NotNull(privateKey);
                                Assert.NotNull(issuerCertificates);
                                application.Certificate = certificate;
                                application.PrivateKey = privateKey;
                                application.IssuerCertificates = issuerCertificates;
                                X509TestUtils.VerifySignedApplicationCert(application, certificate, issuerCertificates);
                                X509TestUtils.VerifyApplicationCertIntegrity(certificate, privateKey, application.PrivateKeyPassword, application.PrivateKeyFormat, issuerCertificates);
                            }
                            else
                            {
                                requestBusy = true;
                            }
                        }
                        catch (ServiceResultException sre)
                        {
                            if (sre.StatusCode == StatusCodes.BadNothingToDo &&
                                now.AddMinutes(5) > DateTime.UtcNow)
                            {
                                requestBusy = true;
                                Thread.Sleep(1000);
                            }
                            else
                            {
                                throw;
                            }
                        }

                    }
                }

                if (requestBusy)
                {
                    Thread.Sleep(5000);
                    Console.WriteLine("Waiting for certificate approval");
                }

            } while (requestBusy);
        }

        [Test, Order(511)]
        public void FinishInvalidNewKeyPairRequests()
        {
            AssertIgnoreTestWithoutInvalidRegistration();
            ConnectGDS(true);
            foreach (var application in m_invalidApplicationTestSet)
            {
                Assert.That(() => {
                    _ = m_gdsClient.GDSClient.FinishRequest(
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
            AssertIgnoreTestWithoutGoodNewKeyPairRequest();
            ConnectGDS(true);
            foreach (var application in m_goodApplicationTestSet)
            {
                Assert.Null(application.CertificateRequestId);
                X509Certificate2 csrCertificate;
                if (application.PrivateKeyFormat == "PFX")
                {
                    csrCertificate = X509Utils.CreateCertificateFromPKCS12(application.PrivateKey, application.PrivateKeyPassword);
                }
                else
                {
                    csrCertificate = CertificateFactory.CreateCertificateWithPEMPrivateKey(new X509Certificate2(application.Certificate), application.PrivateKey, application.PrivateKeyPassword);
                }
                byte[] certificateRequest = CertificateFactory.CreateSigningRequest(csrCertificate, application.DomainNames);
                csrCertificate.Dispose();
                NodeId requestId = m_gdsClient.GDSClient.StartSigningRequest(
                    application.ApplicationRecord.ApplicationId,
                    application.CertificateGroupId,
                    application.CertificateTypeId,
                    certificateRequest);
                Assert.NotNull(requestId);
                application.CertificateRequestId = requestId;
            }
        }

        [Test, Order(530)]
        public void FinishGoodSigningRequests()
        {
            AssertIgnoreTestWithoutGoodRegistration();
            AssertIgnoreTestWithoutGoodNewKeyPairRequest();
            ConnectGDS(true);
            bool requestBusy;
            DateTime now = DateTime.UtcNow;
            do
            {
                requestBusy = false;

                foreach (var application in m_goodApplicationTestSet)
                {
                    if (application.CertificateRequestId != null)
                    {
                        try
                        {
                            var certificate = m_gdsClient.GDSClient.FinishRequest(
                                application.ApplicationRecord.ApplicationId,
                                application.CertificateRequestId,
                                out byte[] privateKey,
                                out byte[][] issuerCertificates
                                );

                            if (certificate != null)
                            {
                                application.CertificateRequestId = null;

                                Assert.Null(privateKey);
                                Assert.NotNull(issuerCertificates);
                                application.Certificate = certificate;
                                application.IssuerCertificates = issuerCertificates;
                                X509TestUtils.VerifySignedApplicationCert(application, certificate, issuerCertificates);
                                X509TestUtils.VerifyApplicationCertIntegrity(certificate, application.PrivateKey, application.PrivateKeyPassword, application.PrivateKeyFormat, issuerCertificates);
                            }
                            else
                            {
                                requestBusy = true;
                            }
                        }
                        catch (ServiceResultException sre)
                        {
                            if (sre.StatusCode == StatusCodes.BadNothingToDo &&
                                now.AddMinutes(5) > DateTime.UtcNow)
                            {
                                requestBusy = true;
                                Thread.Sleep(1000);
                            }
                            else
                            {
                                throw;
                            }
                        }

                    }
                }

                if (requestBusy)
                {
                    Thread.Sleep(5000);
                    Console.WriteLine("Waiting for certificate approval");
                }
            } while (requestBusy);

        }

        [Test, Order(550)]
        public void StartGoodSigningRequestWithInvalidAppURI()
        {
            AssertIgnoreTestWithoutGoodRegistration();
            AssertIgnoreTestWithoutGoodNewKeyPairRequest();
            ConnectGDS(true);
            var application = m_goodApplicationTestSet[0];
            Assert.Null(application.CertificateRequestId);
            // load csr with invalid app URI
            var testCSR = Utils.GetAbsoluteFilePath("test.csr", true, true, false);
            byte[] certificateRequest = File.ReadAllBytes(testCSR);
            Assert.That(() => {
                _ = m_gdsClient.GDSClient.StartSigningRequest(
                application.ApplicationRecord.ApplicationId,
                application.CertificateGroupId,
                application.CertificateTypeId,
                certificateRequest);
            }, Throws.Exception);
        }


        [Test, Order(600)]
        public void GetGoodCertificateGroupsNullTests()
        {
            AssertIgnoreTestWithoutGoodRegistration();
            ConnectGDS(true);

            Assert.That(() => {
                m_gdsClient.GDSClient.GetCertificateGroups(null);
            }, Throws.Exception);

            foreach (var application in m_goodApplicationTestSet)
            {
                var trustListId = m_gdsClient.GDSClient.GetTrustList(application.ApplicationRecord.ApplicationId, null);
                var trustList = m_gdsClient.GDSClient.ReadTrustList(trustListId);
                Assert.That(() => {
                    m_gdsClient.GDSClient.ReadTrustList(null);
                }, Throws.Exception);
                var certificateGroups = m_gdsClient.GDSClient.GetCertificateGroups(application.ApplicationRecord.ApplicationId);
                foreach (var certificateGroup in certificateGroups)
                {
                    Assert.That(() => {
                        m_gdsClient.GDSClient.GetTrustList(null, certificateGroup);
                    }, Throws.Exception);
                }
            }
        }

        [Test, Order(601)]
        public void GetInvalidCertificateGroupsNullTests()
        {
            AssertIgnoreTestWithoutInvalidRegistration();
            ConnectGDS(true);
            Assert.That(() => {
                m_gdsClient.GDSClient.GetCertificateGroups(null);
            }, Throws.Exception);
            Assert.That(() => {
                m_gdsClient.GDSClient.GetCertificateGroups(new NodeId(Guid.NewGuid()));
            }, Throws.Exception);

            foreach (var application in m_invalidApplicationTestSet)
            {
                Assert.That(() => {
                    _ = m_gdsClient.GDSClient.GetTrustList(application.ApplicationRecord.ApplicationId, null);
                }, Throws.Exception);
                Assert.That(() => {
                    _ = m_gdsClient.GDSClient.GetTrustList(application.ApplicationRecord.ApplicationId, new NodeId(Guid.NewGuid()));
                }, Throws.Exception);
                Assert.That(() => {
                    _ = m_gdsClient.GDSClient.GetCertificateGroups(application.ApplicationRecord.ApplicationId);
                }, Throws.Exception);
            }
        }

        [Test, Order(610)]
        public void GetGoodCertificateGroupsAndTrustLists()
        {
            AssertIgnoreTestWithoutGoodRegistration();
            ConnectGDS(true);

            foreach (var application in m_goodApplicationTestSet)
            {
                var certificateGroups = m_gdsClient.GDSClient.GetCertificateGroups(application.ApplicationRecord.ApplicationId);
                foreach (var certificateGroup in certificateGroups)
                {
                    var trustListId = m_gdsClient.GDSClient.GetTrustList(application.ApplicationRecord.ApplicationId, certificateGroup);
                    // Opc.Ua.TrustListDataType
                    var trustList = m_gdsClient.GDSClient.ReadTrustList(trustListId);
                    Assert.NotNull(trustList);
                }
            }
        }

        [Test, Order(690)]
        public void GetGoodCertificateStatus()
        {
            AssertIgnoreTestWithoutGoodRegistration();
            ConnectGDS(true);
            foreach (var application in m_goodApplicationTestSet)
            {
                bool certificateStatus = m_gdsClient.GDSClient.GetCertificateStatus(application.ApplicationRecord.ApplicationId, null, null);
                Assert.False(certificateStatus);
            }
        }

        [Test, Order(691)]
        public void GetInvalidCertificateStatus()
        {
            AssertIgnoreTestWithoutInvalidRegistration();
            ConnectGDS(true);
            foreach (var application in m_invalidApplicationTestSet)
            {
                Assert.That(() => {
                    _ = m_gdsClient.GDSClient.GetCertificateStatus(application.ApplicationRecord.ApplicationId, null, null);
                }, Throws.Exception);
            }
        }

        [Test, Order(900)]
        public void UnregisterGoodApplications()
        {
            AssertIgnoreTestWithoutGoodRegistration();
            ConnectGDS(true);
            foreach (var application in m_goodApplicationTestSet)
            {
                m_gdsClient.GDSClient.UnregisterApplication(application.ApplicationRecord.ApplicationId);
            }
        }

        [Test, Order(910)]
        public void UnregisterInvalidApplications()
        {
            ConnectGDS(true);
            foreach (var application in m_invalidApplicationTestSet)
            {
                Assert.That(() => { m_gdsClient.GDSClient.UnregisterApplication(application.ApplicationRecord.ApplicationId); }, Throws.Exception);
            }
        }

        [Test, Order(915)]
        public void VerifyUnregisterGoodApplications()
        {
            AssertIgnoreTestWithoutGoodRegistration();
            ConnectGDS(true);
            foreach (var application in m_goodApplicationTestSet)
            {
                var result = m_gdsClient.GDSClient.FindApplication(application.ApplicationRecord.ApplicationUri);
                if (result != null)
                {
                    Assert.NotNull(result);
                    Assert.AreEqual(0, result.Length, "Found deleted application on server!");
                }
            }
        }


        [Test, Order(920)]
        public void UnregisterUnregisteredGoodApplications()
        {
            ConnectGDS(true);
            foreach (var application in m_goodApplicationTestSet)
            {
                Assert.That(() => { m_gdsClient.GDSClient.UnregisterApplication(application.ApplicationRecord.ApplicationId); }, Throws.Exception);
            }
        }

#if DEVOPS_LOG
        [Test, Order(9998)]
        public void ClientLogResult()
        {
            var log = _gdsClient.ReadLogFile();
            TestContext.Progress.WriteLine(log);
        }

        [Test, Order(9999)]
        public void ServerLogResult()
        {
            var log = _server.ReadLogFile();
            TestContext.Progress.WriteLine(log);
        }
#endif
        #endregion

        #region Private Methods
        private void ConnectGDS(bool admin,
            [System.Runtime.CompilerServices.CallerMemberName] string memberName = ""
            )
        {
            m_gdsClient.GDSClient.AdminCredentials = admin ? m_gdsClient.AdminUser : m_gdsClient.AppUser;
            m_gdsClient.GDSClient.Connect(m_gdsClient.GDSClient.EndpointUrl).Wait();
            TestContext.Progress.WriteLine($"GDS Client({admin}) connected -- {memberName}");
        }

        private void DisconnectGDS(
            [System.Runtime.CompilerServices.CallerMemberName] string memberName = ""
            )
        {
            m_gdsClient.GDSClient.Disconnect();
            TestContext.Progress.WriteLine($"GDS Client disconnected -- {memberName}");
        }

        private void AssertIgnoreTestWithoutGoodRegistration()
        {
            if (!m_goodRegistrationOk)
            {
                Assert.Ignore("Test requires good application registrations.");
            }
        }

        private void AssertIgnoreTestWithoutInvalidRegistration()
        {
            if (!m_invalidRegistrationOk)
            {
                Assert.Ignore("Test requires invalid application registration.");
            }
        }

        private void AssertIgnoreTestWithoutGoodNewKeyPairRequest()
        {
            if (!m_goodNewKeyPairRequestOk)
            {
                Assert.Ignore("Test requires good new key pair request.");
            }
        }

        private int GoodServersOnNetworkCount()
        {
            return m_goodApplicationTestSet.Sum(a => a.ApplicationRecord.DiscoveryUrls.Count);
        }

        #endregion

        #region Private Fields
        private const int kGoodApplicationsTestCount = 10;
        private const int kInvalidApplicationsTestCount = 10;
        private const int kRandomStart = 1;
        private ApplicationTestDataGenerator m_appTestDataGenerator;
        private GlobalDiscoveryTestServer m_server;
        private GlobalDiscoveryTestClient m_gdsClient;
        private IList<ApplicationTestData> m_goodApplicationTestSet;
        private IList<ApplicationTestData> m_invalidApplicationTestSet;
        private bool m_goodRegistrationOk;
        private bool m_invalidRegistrationOk;
        private bool m_goodNewKeyPairRequestOk;
        #endregion
    }

}
