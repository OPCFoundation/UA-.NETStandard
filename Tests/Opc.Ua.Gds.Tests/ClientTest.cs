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
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Gds.Server;
using Opc.Ua.Tests;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Opc.Ua.Gds.Tests
{
    /// <summary>
    /// Test GDS Registration and Client Pull.
    /// </summary>
    [TestFixture]
    [Category("GDSRegistrationAndPull")]
    [Category("GDS")]
    [TestFixtureSource(nameof(FixtureArgs))]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public class ClientTest
    {
        public class ConnectionProfile : IFormattable
        {
            public ConnectionProfile(
                string securityProfileUri,
                MessageSecurityMode messageSecurityMode)
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
        /// store types to run the tests with
        /// </summary>
        public static readonly object[] FixtureArgs =
        [
            new object[] { CertificateStoreType.Directory },
            new object[] { CertificateStoreType.X509Store }
        ];

        public ClientTest(string storeType = CertificateStoreType.Directory)
        {
            if (storeType == CertificateStoreType.X509Store &&
                !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                NUnit.Framework.Assert
                    .Ignore("X509 Store with crls is only supported on Windows, skipping test run");
            }
            m_storeType = storeType;
        }

        /// <summary>
        /// Set up a Global Discovery Server and Client instance and connect the session
        /// </summary>
        [OneTimeSetUp]
        protected async Task OneTimeSetUpAsync()
        {
            // start GDS
            m_telemetry = NUnitTelemetryContext.Create();
            m_server = await TestUtils.StartGDSAsync(true, m_telemetry, m_storeType).ConfigureAwait(false);

            // load client
            m_gdsClient = new GlobalDiscoveryTestClient(true, m_telemetry, m_storeType);
            await m_gdsClient.LoadClientConfigurationAsync(m_server.BasePort).ConfigureAwait(false);

            // good applications test set
            m_appTestDataGenerator = new ApplicationTestDataGenerator(1, m_telemetry);
            m_goodApplicationTestSet = m_appTestDataGenerator.ApplicationTestSet(
                kGoodApplicationsTestCount,
                false);
            m_invalidApplicationTestSet = m_appTestDataGenerator.ApplicationTestSet(
                kInvalidApplicationsTestCount,
                true);

            m_goodRegistrationOk = false;
            m_invalidRegistrationOk = false;
            m_goodNewKeyPairRequestOk = false;
            m_gdsRegisteredTestClient = false;

            //get supported CertificateTypes from GDS
            m_supportedCertificateTypes =
            [
                .. m_server
                    .Config.ParseExtension<GlobalDiscoveryServerConfiguration>()
                    .CertificateGroups.Where(cg => cg.Id == "Default")
                    .SelectMany(cg => cg.CertificateTypes)
                    .Select(s => typeof(Ua.ObjectTypeIds).GetField(s).GetValue(null) as NodeId)
                    .Where(n => n != null && Utils.IsSupportedCertificateType(n))
            ];
        }

        /// <summary>
        /// Tear down the Global Discovery Server and disconnect the Client
        /// </summary>
        [OneTimeTearDown]
        protected async Task OneTimeTearDownAsync()
        {
            await m_gdsClient.DisconnectClientAsync().ConfigureAwait(false);
            m_gdsClient = null;
            m_server.StopServer();
            m_server = null;
            Thread.Sleep(1000);
        }

        [SetUp]
        protected void SetUp()
        {
        }

        [TearDown]
        protected async Task TearDownAsync()
        {
            await DisconnectGDSAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Clean the app database from application Uri used during test.
        /// </summary>
        [Test]
        [Order(10)]
        public async Task CleanGoodApplicationsAsync()
        {
            await ConnectGDSAsync(true).ConfigureAwait(false);
            foreach (ApplicationTestData application in m_goodApplicationTestSet)
            {
                ApplicationRecordDataType[] applicationDataRecords = await m_gdsClient.GDSClient
                    .FindApplicationAsync(
                        application.ApplicationRecord.ApplicationUri).ConfigureAwait(false);
                if (applicationDataRecords != null)
                {
                    foreach (ApplicationRecordDataType applicationDataRecord in applicationDataRecords)
                    {
                        await m_gdsClient.GDSClient
                            .UnregisterApplicationAsync(applicationDataRecord.ApplicationId).ConfigureAwait(false);
                    }
                }
            }
        }

        /// <summary>
        /// Register the good applications in the database.
        /// </summary>
        [Test]
        [Order(100)]
        public async Task RegisterGoodApplicationsAsync()
        {
            await ConnectGDSAsync(true).ConfigureAwait(false);
            foreach (ApplicationTestData application in m_goodApplicationTestSet)
            {
                NodeId id = await m_gdsClient.GDSClient
                    .RegisterApplicationAsync(application.ApplicationRecord).ConfigureAwait(false);
                Assert.NotNull(id);
                Assert.IsFalse(id.IsNullNodeId);
                NUnit.Framework.Assert.That(id.IdType is IdType.Guid or IdType.String);
                application.ApplicationRecord.ApplicationId = id;
            }
            m_goodRegistrationOk = true;
        }

        [Test]
        [Order(105)]
        public async Task RegisterDuplicateGoodApplicationsAsync()
        {
            AssertIgnoreTestWithoutGoodRegistration();
            await ConnectGDSAsync(true).ConfigureAwait(false);
            foreach (ApplicationTestData application in m_goodApplicationTestSet)
            {
                var newRecord = (ApplicationRecordDataType)application.ApplicationRecord
                    .MemberwiseClone();
                newRecord.ApplicationId = null;
                NodeId id = await m_gdsClient.GDSClient.RegisterApplicationAsync(newRecord).ConfigureAwait(false);
                Assert.NotNull(id);
                Assert.IsFalse(id.IsNullNodeId);
                NUnit.Framework.Assert.That(id.IdType is IdType.Guid or IdType.String);
                newRecord.ApplicationId = id;
                ApplicationRecordDataType[] applicationDataRecords = await m_gdsClient.GDSClient
                    .FindApplicationAsync(
                        newRecord.ApplicationUri).ConfigureAwait(false);
                Assert.NotNull(applicationDataRecords);
                bool newIdFound = false;
                bool registeredIdFound = false;
                foreach (ApplicationRecordDataType applicationDataRecord in applicationDataRecords)
                {
                    if (applicationDataRecord.ApplicationId == newRecord.ApplicationId)
                    {
                        await m_gdsClient.GDSClient.UnregisterApplicationAsync(id).ConfigureAwait(false);
                        newIdFound = true;
                    }
                    else if (applicationDataRecord.ApplicationId == application.ApplicationRecord
                        .ApplicationId)
                    {
                        registeredIdFound = true;
                    }
                }
                Assert.IsTrue(newIdFound);
                Assert.IsTrue(registeredIdFound);
            }
        }

        [Test]
        [Order(110)]
        public async Task RegisterInvalidApplicationsAsync()
        {
            await ConnectGDSAsync(true).ConfigureAwait(false);
            foreach (ApplicationTestData application in m_invalidApplicationTestSet)
            {
                await NUnit.Framework.Assert.ThatAsync(
                    () => _ = m_gdsClient.GDSClient
                        .RegisterApplicationAsync(application.ApplicationRecord),
                    Throws.Exception).ConfigureAwait(false);
            }
            m_invalidRegistrationOk = true;
        }

        [Test]
        [Order(120)]
        public async Task RegisterApplicationAsUserAsync()
        {
            AssertIgnoreTestWithoutInvalidRegistration();
            await ConnectGDSAsync(false).ConfigureAwait(false);
            foreach (ApplicationTestData application in m_invalidApplicationTestSet)
            {
                await NUnit.Framework.Assert.ThatAsync(
                    () => _ = m_gdsClient.GDSClient
                        .RegisterApplicationAsync(application.ApplicationRecord),
                    Throws.Exception).ConfigureAwait(false);
            }
        }

        [Test]
        [Order(200)]
        public async Task UpdateGoodApplicationsAsync()
        {
            AssertIgnoreTestWithoutGoodRegistration();
            await ConnectGDSAsync(true).ConfigureAwait(false);
            foreach (ApplicationTestData application in m_goodApplicationTestSet)
            {
                var updatedApplicationRecord = (ApplicationRecordDataType)
                    application.ApplicationRecord.MemberwiseClone();
                updatedApplicationRecord.ApplicationUri += "update";
                await m_gdsClient.GDSClient.UpdateApplicationAsync(updatedApplicationRecord).ConfigureAwait(false);
                ApplicationRecordDataType[] result = await m_gdsClient.GDSClient.FindApplicationAsync(
                    updatedApplicationRecord.ApplicationUri).ConfigureAwait(false);
                await m_gdsClient.GDSClient.UpdateApplicationAsync(application.ApplicationRecord).ConfigureAwait(false);
                Assert.NotNull(result);
                Assert.GreaterOrEqual(1, result.Length, "Couldn't find updated application record");
            }
        }

        [Test]
        [Order(210)]
        public async Task UpdateGoodApplicationsWithNewGuidAsync()
        {
            AssertIgnoreTestWithoutGoodRegistration();
            await ConnectGDSAsync(true).ConfigureAwait(false);
            foreach (ApplicationTestData application in m_goodApplicationTestSet)
            {
                var testApplicationRecord = (ApplicationRecordDataType)application.ApplicationRecord
                    .MemberwiseClone();
                testApplicationRecord.ApplicationId = new NodeId(Guid.NewGuid());
                await NUnit.Framework.Assert.ThatAsync(
                    () => m_gdsClient.GDSClient.UpdateApplicationAsync(testApplicationRecord),
                    Throws.Exception).ConfigureAwait(false);
            }
        }

        [Test]
        [Order(210)]
        public async Task UpdateGoodApplicationsWithNewStringAsync()
        {
            AssertIgnoreTestWithoutGoodRegistration();
            await ConnectGDSAsync(true).ConfigureAwait(false);
            foreach (ApplicationTestData application in m_goodApplicationTestSet)
            {
                var testApplicationRecord = (ApplicationRecordDataType)application.ApplicationRecord
                    .MemberwiseClone();
                testApplicationRecord.ApplicationId = new NodeId(
                    "s=" + m_appTestDataGenerator.DataGenerator.GetRandomString("en"));
                await NUnit.Framework.Assert.ThatAsync(
                    () => m_gdsClient.GDSClient.UpdateApplicationAsync(testApplicationRecord),
                    Throws.Exception).ConfigureAwait(false);
            }
        }

        [Test]
        [Order(220)]
        public async Task UpdateInvalidApplicationsAsync()
        {
            AssertIgnoreTestWithoutInvalidRegistration();
            await ConnectGDSAsync(true).ConfigureAwait(false);
            foreach (ApplicationTestData application in m_invalidApplicationTestSet)
            {
                await NUnit.Framework.Assert.ThatAsync(
                    () => m_gdsClient.GDSClient.UpdateApplicationAsync(application.ApplicationRecord),
                    Throws.Exception).ConfigureAwait(false);
            }
        }

        [Test]
        [Order(400)]
        public async Task FindGoodApplicationsAsync()
        {
            AssertIgnoreTestWithoutGoodRegistration();
            await ConnectGDSAsync(false).ConfigureAwait(false);
            foreach (ApplicationTestData application in m_goodApplicationTestSet)
            {
                ApplicationRecordDataType[] result = await m_gdsClient.GDSClient.FindApplicationAsync(
                    application.ApplicationRecord.ApplicationUri).ConfigureAwait(false);
                Assert.NotNull(result);
                Assert.GreaterOrEqual(result.Length, 1, "Couldn't find good application");
            }
        }

        [Test]
        [Order(400)]
        public async Task FindInvalidApplicationsAsync()
        {
            AssertIgnoreTestWithoutInvalidRegistration();
            await ConnectGDSAsync(true).ConfigureAwait(false);
            foreach (ApplicationTestData application in m_invalidApplicationTestSet)
            {
                ApplicationRecordDataType[] result = await m_gdsClient.GDSClient.FindApplicationAsync(
                    application.ApplicationRecord.ApplicationUri).ConfigureAwait(false);
                if (result != null)
                {
                    Assert.NotNull(result);
                    Assert.AreEqual(0, result.Length, "Found invalid application on server");
                }
            }
        }

        [Test]
        [Order(400)]
        public async Task GetGoodApplicationsAsync()
        {
            AssertIgnoreTestWithoutGoodRegistration();
            await ConnectGDSAsync(false).ConfigureAwait(false);
            foreach (ApplicationTestData application in m_goodApplicationTestSet)
            {
                ApplicationRecordDataType result = await m_gdsClient.GDSClient.GetApplicationAsync(
                    application.ApplicationRecord.ApplicationId).ConfigureAwait(false);
                Assert.NotNull(result);
                result.ServerCapabilities.Sort();
                application.ApplicationRecord.ServerCapabilities.Sort();
                Assert.IsTrue(Utils.IsEqual(application.ApplicationRecord, result));
            }
        }

        [Test]
        [Order(401)]
        public async Task GetGoodApplicationsTestApplicationIdAsync()
        {
            AssertIgnoreTestWithoutGoodRegistration();
            await ConnectGDSAsync(false).ConfigureAwait(false);
            foreach (ApplicationTestData application in m_goodApplicationTestSet)
            {
                ApplicationRecordDataType result = await m_gdsClient.GDSClient.GetApplicationAsync(
                    application.ApplicationRecord.ApplicationId).ConfigureAwait(false);
                Assert.NotNull(result);
                Assert.IsTrue(
                    Utils.IsEqual(
                        application.ApplicationRecord.ApplicationId,
                        result.ApplicationId));
            }
        }

        [Test]
        [Order(401)]
        public async Task GetGoodApplicationsTestApplicationNamesAsync()
        {
            AssertIgnoreTestWithoutGoodRegistration();
            await ConnectGDSAsync(false).ConfigureAwait(false);
            foreach (ApplicationTestData application in m_goodApplicationTestSet)
            {
                ApplicationRecordDataType result = await m_gdsClient.GDSClient.GetApplicationAsync(
                    application.ApplicationRecord.ApplicationId).ConfigureAwait(false);
                Assert.NotNull(result);
                Assert.IsTrue(Utils.IsEqual(
                    application.ApplicationRecord.ApplicationNames,
                    result.ApplicationNames));
            }
        }

        [Test]
        [Order(401)]
        public async Task GetGoodApplicationsTestApplicationTypeAsync()
        {
            AssertIgnoreTestWithoutGoodRegistration();
            await ConnectGDSAsync(false).ConfigureAwait(false);
            foreach (ApplicationTestData application in m_goodApplicationTestSet)
            {
                ApplicationRecordDataType result = await m_gdsClient.GDSClient.GetApplicationAsync(
                    application.ApplicationRecord.ApplicationId).ConfigureAwait(false);
                Assert.NotNull(result);
                Assert.IsTrue(Utils.IsEqual(
                    application.ApplicationRecord.ApplicationType,
                    result.ApplicationType));
            }
        }

        [Test]
        [Order(401)]
        public async Task GetGoodApplicationsTestApplicationUriAsync()
        {
            AssertIgnoreTestWithoutGoodRegistration();
            await ConnectGDSAsync(false).ConfigureAwait(false);
            foreach (ApplicationTestData application in m_goodApplicationTestSet)
            {
                ApplicationRecordDataType result = await m_gdsClient.GDSClient.GetApplicationAsync(
                    application.ApplicationRecord.ApplicationId).ConfigureAwait(false);
                Assert.NotNull(result);
                Assert.IsTrue(Utils.IsEqual(
                    application.ApplicationRecord.ApplicationUri,
                    result.ApplicationUri));
            }
        }

        [Test]
        [Order(401)]
        public async Task GetGoodApplicationsTestDiscoveryUrlsAsync()
        {
            AssertIgnoreTestWithoutGoodRegistration();
            await ConnectGDSAsync(false).ConfigureAwait(false);
            foreach (ApplicationTestData application in m_goodApplicationTestSet)
            {
                ApplicationRecordDataType result = await m_gdsClient.GDSClient.GetApplicationAsync(
                    application.ApplicationRecord.ApplicationId).ConfigureAwait(false);
                Assert.NotNull(result);
                Assert.IsTrue(
                    Utils.IsEqual(
                        application.ApplicationRecord.DiscoveryUrls,
                        result.DiscoveryUrls));
            }
        }

        [Test]
        [Order(401)]
        public async Task GetGoodApplicationsTestProductUriAsync()
        {
            AssertIgnoreTestWithoutGoodRegistration();
            await ConnectGDSAsync(false).ConfigureAwait(false);
            foreach (ApplicationTestData application in m_goodApplicationTestSet)
            {
                ApplicationRecordDataType result = await m_gdsClient.GDSClient.GetApplicationAsync(
                    application.ApplicationRecord.ApplicationId).ConfigureAwait(false);
                Assert.NotNull(result);
                Assert.IsTrue(
                    Utils.IsEqual(application.ApplicationRecord.ProductUri, result.ProductUri));
            }
        }

        [Test]
        [Order(401)]
        public async Task GetGoodApplicationsTestServerCapabilitiesAsync()
        {
            AssertIgnoreTestWithoutGoodRegistration();
            await ConnectGDSAsync(false).ConfigureAwait(false);
            foreach (ApplicationTestData application in m_goodApplicationTestSet)
            {
                ApplicationRecordDataType result = await m_gdsClient.GDSClient.GetApplicationAsync(
                    application.ApplicationRecord.ApplicationId).ConfigureAwait(false);
                Assert.NotNull(result);
                result.ServerCapabilities.Sort();
                application.ApplicationRecord.ServerCapabilities.Sort();
                Assert.IsTrue(
                    Utils.IsEqual(
                        application.ApplicationRecord.ServerCapabilities,
                        result.ServerCapabilities));
            }
        }

        [Test]
        [Order(400)]
        public async Task GetInvalidApplicationsAsync()
        {
            AssertIgnoreTestWithoutInvalidRegistration();
            await ConnectGDSAsync(true).ConfigureAwait(false);
            foreach (ApplicationTestData application in m_invalidApplicationTestSet)
            {
                await NUnit.Framework.Assert.ThatAsync(
                    () => m_gdsClient.GDSClient.GetApplicationAsync(
                            application.ApplicationRecord.ApplicationId)
                    ,
                    Throws.Exception).ConfigureAwait(false);
            }
        }

        [Test]
        [Order(410)]
        public async Task QueryAllServersAsync()
        {
            AssertIgnoreTestWithoutGoodRegistration();
            await ConnectGDSAsync(false).ConfigureAwait(false);
            // get all servers
            IList<ServerOnNetwork> allServers = await m_gdsClient.GDSClient
                .QueryServersAsync(0, string.Empty, string.Empty, string.Empty, []).ConfigureAwait(false);
            int totalCount = 0;
            uint firstID = uint.MaxValue;
            uint lastID = 0;
            Assert.IsNotNull(allServers);
            foreach (ServerOnNetwork server in allServers)
            {
                IList<ServerOnNetwork> oneServers = (await m_gdsClient.GDSClient.QueryServersAsync(
                    server.RecordId,
                    1,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    []).ConfigureAwait(false)).servers;
                Assert.IsNotNull(oneServers);
                Assert.GreaterOrEqual(oneServers.Count, 1);
                foreach (ServerOnNetwork oneServer in oneServers)
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

        [Test]
        [Order(411)]
        public async Task QueryAllServersNullAsync()
        {
            AssertIgnoreTestWithoutGoodRegistration();
            await ConnectGDSAsync(false).ConfigureAwait(false);
            // get all servers
            IList<ServerOnNetwork> allServers = await m_gdsClient.GDSClient
                .QueryServersAsync(0, null, null, null, null).ConfigureAwait(false);
            int totalCount = 0;
            uint firstID = uint.MaxValue;
            uint lastID = 0;
            Assert.IsNotNull(allServers);
            foreach (ServerOnNetwork server in allServers)
            {
                IList<ServerOnNetwork> oneServers = (await m_gdsClient.GDSClient.QueryServersAsync(
                    server.RecordId,
                    1,
                    null,
                    null,
                    null,
                    null).ConfigureAwait(false)).servers;
                Assert.IsNotNull(oneServers);
                Assert.GreaterOrEqual(oneServers.Count, 1);
                foreach (ServerOnNetwork oneServer in oneServers)
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

        [Test]
        [Order(420)]
        public async Task QueryGoodServersBatchesAsync()
        {
            // repeating queries to get all servers
            uint nextID = 0;
            uint iterationCount = Math.Min(10, (uint)(kGoodApplicationsTestCount / 2));
            int serversOnNetwork = 0;
            int goodServersOnNetwork = GoodServersOnNetworkCount();
            while (true)
            {
                IList<ServerOnNetwork> iterServers = (await m_gdsClient.GDSClient.QueryServersAsync(
                    nextID,
                    iterationCount,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    null).ConfigureAwait(false)).servers;
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

        [Test]
        [Order(430)]
        public async Task QueryServersByNameAsync()
        {
            // search applications by name
            const int searchPatternLength = 5;
            foreach (ApplicationTestData application in m_goodApplicationTestSet)
            {
                IList<ServerOnNetwork> atLeastOneServer = await m_gdsClient.GDSClient.QueryServersAsync(
                    1,
                    application.ApplicationRecord.ApplicationNames[0].Text,
                    string.Empty,
                    string.Empty,
                    null).ConfigureAwait(false);
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
                    searchName = $"{searchName[..searchPatternLength]}%";
                }
                atLeastOneServer = await m_gdsClient.GDSClient
                    .QueryServersAsync(1, searchName, string.Empty, string.Empty, null).ConfigureAwait(false);
                Assert.IsNotNull(atLeastOneServer);
                if (application.ApplicationRecord.ApplicationType != ApplicationType.Client)
                {
                    Assert.GreaterOrEqual(atLeastOneServer.Count, 1);
                }
            }
        }

        [Test]
        [Order(440)]
        public async Task QueryServersByAppUriAsync()
        {
            // search applications by name
            const int searchPatternLength = 5;
            foreach (ApplicationTestData application in m_goodApplicationTestSet)
            {
                IList<ServerOnNetwork> atLeastOneServer = await m_gdsClient.GDSClient.QueryServersAsync(
                    1,
                    null,
                    application.ApplicationRecord.ApplicationUri,
                    null,
                    null).ConfigureAwait(false);
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
                    searchName = $"{searchName[..searchPatternLength]}%";
                }
                atLeastOneServer = await m_gdsClient.GDSClient
                    .QueryServersAsync(1, null, searchName, null, null).ConfigureAwait(false);
                Assert.IsNotNull(atLeastOneServer);
                if (application.ApplicationRecord.ApplicationType != ApplicationType.Client)
                {
                    Assert.GreaterOrEqual(atLeastOneServer.Count, 1);
                }
            }
        }

        [Test]
        [Order(450)]
        public async Task QueryServersByProductUriAsync()
        {
            // search applications by name
            const int searchPatternLength = 5;
            foreach (ApplicationTestData application in m_goodApplicationTestSet)
            {
                IList<ServerOnNetwork> atLeastOneServer = await m_gdsClient.GDSClient.QueryServersAsync(
                    1,
                    null,
                    null,
                    application.ApplicationRecord.ProductUri,
                    null).ConfigureAwait(false);
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
                    searchName = $"{searchName[..searchPatternLength]}%";
                }
                atLeastOneServer = await m_gdsClient.GDSClient
                    .QueryServersAsync(1, null, null, searchName, null).ConfigureAwait(false);
                Assert.IsNotNull(atLeastOneServer);
                if (application.ApplicationRecord.ApplicationType != ApplicationType.Client)
                {
                    Assert.GreaterOrEqual(atLeastOneServer.Count, 1);
                }
            }
        }

        [Test]
        [Order(480)]
        public async Task QueryAllApplicationsAsync()
        {
            AssertIgnoreTestWithoutGoodRegistration();
            await ConnectGDSAsync(false).ConfigureAwait(false);
            // get all applications
            uint nextRecordId;
            IList<ApplicationDescription> allApplications = (await m_gdsClient.GDSClient.QueryApplicationsAsync(
                0,
                0,
                string.Empty,
                string.Empty,
                0,
                string.Empty,
                []).ConfigureAwait(false)).applications;
            int totalCount = 0;
            Assert.IsNotNull(allApplications);
            nextRecordId = 0;
            foreach (ApplicationDescription application in allApplications)
            {
                (IList<ApplicationDescription> oneApplication, DateTime lastResetCounterTime, nextRecordId) = await m_gdsClient.GDSClient
                    .QueryApplicationsAsync(
                        nextRecordId,
                        1,
                        string.Empty,
                        string.Empty,
                        0,
                        string.Empty,
                        []).ConfigureAwait(false);
                Assert.IsNotNull(oneApplication);
                Assert.GreaterOrEqual(oneApplication.Count, 1);
                foreach (ApplicationDescription oneApp in oneApplication)
                {
                    //Assert.AreEqual(oneApp., server.RecordId);
                }
                totalCount++;
            }
            Assert.GreaterOrEqual(totalCount, kGoodApplicationsTestCount);
            Assert.AreEqual(totalCount, allApplications.Count);
        }

        [Test]
        [Order(500)]
        public async Task StartGoodNewKeyPairRequestsAsync()
        {
            AssertIgnoreTestWithoutGoodRegistration();
            await ConnectGDSAsync(true).ConfigureAwait(false);
            int certificateTypeIndex = 0;
            foreach (ApplicationTestData application in m_goodApplicationTestSet)
            {
                application.CertificateTypeId = m_supportedCertificateTypes[certificateTypeIndex];
                certificateTypeIndex = (certificateTypeIndex + 1) %
                    m_supportedCertificateTypes.Count;

                Assert.Null(application.CertificateRequestId);
                NodeId requestId = await m_gdsClient.GDSClient.StartNewKeyPairRequestAsync(
                    application.ApplicationRecord.ApplicationId,
                    application.CertificateGroupId,
                    application.CertificateTypeId,
                    application.Subject,
                    application.DomainNames,
                    application.PrivateKeyFormat,
                    application.PrivateKeyPassword).ConfigureAwait(false);
                Assert.NotNull(requestId);
                application.CertificateRequestId = requestId;
            }
            m_goodNewKeyPairRequestOk = true;
        }

        [Test]
        [Order(501)]
        public async Task StartInvalidNewKeyPairRequestsAsync()
        {
            AssertIgnoreTestWithoutInvalidRegistration();
            await ConnectGDSAsync(true).ConfigureAwait(false);
            foreach (ApplicationTestData application in m_invalidApplicationTestSet)
            {
                Assert.Null(application.CertificateRequestId);
                await NUnit.Framework.Assert.ThatAsync(
                    () => m_gdsClient.GDSClient.StartNewKeyPairRequestAsync(
                            application.ApplicationRecord.ApplicationId,
                            application.CertificateGroupId,
                            application.CertificateTypeId,
                            application.Subject,
                            application.DomainNames,
                            application.PrivateKeyFormat,
                            application.PrivateKeyPassword)
                    ,
                    Throws.Exception).ConfigureAwait(false);
            }
        }

        [Test]
        [Order(510)]
        public async Task FinishGoodNewKeyPairRequestsAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            AssertIgnoreTestWithoutGoodRegistration();
            AssertIgnoreTestWithoutGoodNewKeyPairRequest();
            await ConnectGDSAsync(true).ConfigureAwait(false);
            bool requestBusy;
            DateTime now = DateTime.UtcNow;
            do
            {
                requestBusy = false;
                foreach (ApplicationTestData application in m_goodApplicationTestSet)
                {
                    if (application.CertificateRequestId != null)
                    {
                        try
                        {
                            (byte[] certificate, byte[] privateKey, byte[][] issuerCertificates) = await m_gdsClient.GDSClient.FinishRequestAsync(
                                application.ApplicationRecord.ApplicationId,
                                application.CertificateRequestId).ConfigureAwait(false);

                            if (certificate != null)
                            {
                                application.CertificateRequestId = null;

                                Assert.NotNull(certificate);
                                Assert.NotNull(privateKey);
                                Assert.NotNull(issuerCertificates);
                                application.Certificate = certificate;
                                application.PrivateKey = privateKey;
                                application.IssuerCertificates = issuerCertificates;
                                X509TestUtils.VerifySignedApplicationCert(
                                    application,
                                    certificate,
                                    issuerCertificates);
                                X509TestUtils.VerifyApplicationCertIntegrity(
                                    certificate,
                                    privateKey,
                                    application.PrivateKeyPassword,
                                    application.PrivateKeyFormat,
                                    issuerCertificates,
                                    telemetry);
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

        [Test]
        [Order(512)]
        public async Task FinishInvalidNewKeyPairRequestsAsync()
        {
            AssertIgnoreTestWithoutInvalidRegistration();
            await ConnectGDSAsync(true).ConfigureAwait(false);
            foreach (ApplicationTestData application in m_invalidApplicationTestSet)
            {
                await NUnit.Framework.Assert.ThatAsync(
                    () => m_gdsClient.GDSClient.FinishRequestAsync(
                            application.ApplicationRecord.ApplicationId,
                            new NodeId(Guid.NewGuid())),
                    Throws.Exception).ConfigureAwait(false);
            }
        }

        [Test]
        [Order(520)]
        public async Task StartGoodSigningRequestsAsync()
        {
            AssertIgnoreTestWithoutGoodRegistration();
            AssertIgnoreTestWithoutGoodNewKeyPairRequest();
            await ConnectGDSAsync(true).ConfigureAwait(false);
            foreach (ApplicationTestData application in m_goodApplicationTestSet)
            {
                Assert.Null(application.CertificateRequestId);
                X509Certificate2 csrCertificate;
                if (application.PrivateKeyFormat == "PFX")
                {
                    csrCertificate = X509Utils.CreateCertificateFromPKCS12(
                        application.PrivateKey,
                        application.PrivateKeyPassword);
                }
                else
                {
                    csrCertificate = CertificateFactory.CreateCertificateWithPEMPrivateKey(
                        X509CertificateLoader.LoadCertificate(application.Certificate),
                        application.PrivateKey,
                        application.PrivateKeyPassword);
                }
                byte[] certificateRequest = CertificateFactory.CreateSigningRequest(
                    csrCertificate,
                    application.DomainNames);
                csrCertificate.Dispose();
                NodeId requestId = await m_gdsClient.GDSClient.StartSigningRequestAsync(
                    application.ApplicationRecord.ApplicationId,
                    application.CertificateGroupId,
                    application.CertificateTypeId,
                    certificateRequest).ConfigureAwait(false);
                Assert.NotNull(requestId);
                application.CertificateRequestId = requestId;
            }
        }

        [Test]
        [Order(530)]
        public async Task FinishGoodSigningRequestsAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            AssertIgnoreTestWithoutGoodRegistration();
            AssertIgnoreTestWithoutGoodNewKeyPairRequest();
            await ConnectGDSAsync(true).ConfigureAwait(false);
            bool requestBusy;
            DateTime now = DateTime.UtcNow;
            do
            {
                requestBusy = false;

                foreach (ApplicationTestData application in m_goodApplicationTestSet)
                {
                    if (application.CertificateRequestId != null)
                    {
                        try
                        {
                            (byte[] certificate, byte[] privateKey, byte[][] issuerCertificates) = await m_gdsClient.GDSClient.FinishRequestAsync(
                                application.ApplicationRecord.ApplicationId,
                                application.CertificateRequestId).ConfigureAwait(false);

                            if (certificate != null)
                            {
                                application.CertificateRequestId = null;

                                Assert.Null(privateKey);
                                Assert.NotNull(issuerCertificates);
                                application.Certificate = certificate;
                                application.IssuerCertificates = issuerCertificates;
                                X509TestUtils.VerifySignedApplicationCert(
                                    application,
                                    certificate,
                                    issuerCertificates);
                                X509TestUtils.VerifyApplicationCertIntegrity(
                                    certificate,
                                    application.PrivateKey,
                                    application.PrivateKeyPassword,
                                    application.PrivateKeyFormat,
                                    issuerCertificates,
                                    telemetry);
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

        [Test]
        [Order(540)]
        public async Task GetGoodCertificatesAsync()
        {
            AssertIgnoreTestWithoutGoodRegistration();
            AssertIgnoreTestWithoutGoodNewKeyPairRequest();
            await ConnectGDSAsync(true).ConfigureAwait(false);

            await NUnit.Framework.Assert.ThatAsync(
                () => m_gdsClient.GDSClient
                    .GetCertificatesAsync(null, null),
                Throws.Exception).ConfigureAwait(false);

            foreach (ApplicationTestData application in m_goodApplicationTestSet)
            {
                (NodeId[] certificateTypeIds, byte[][] certificates) = await m_gdsClient.GDSClient.GetCertificatesAsync(
                    application.ApplicationRecord.ApplicationId,
                    null).ConfigureAwait(false);
                NUnit.Framework.Assert.That(certificateTypeIds.Length == 1);
                Assert.NotNull(certificates[0]);
                Assert.AreEqual(certificates[0], application.Certificate);
                (NodeId[] certificateTypeIds2, byte[][] certificates2) = await m_gdsClient.GDSClient.GetCertificatesAsync(
                    application.ApplicationRecord.ApplicationId,
                    application.CertificateGroupId).ConfigureAwait(false);
                NUnit.Framework.Assert.That(certificateTypeIds2.Length == 1);
                Assert.NotNull(certificates2[0]);
                Assert.AreEqual(certificates2[0], application.Certificate);
            }
        }

        [Test]
        [Order(550)]
        public async Task StartGoodSigningRequestWithInvalidAppURIAsync()
        {
            AssertIgnoreTestWithoutGoodRegistration();
            AssertIgnoreTestWithoutGoodNewKeyPairRequest();
            await ConnectGDSAsync(true).ConfigureAwait(false);
            ApplicationTestData application = m_goodApplicationTestSet[0];
            Assert.Null(application.CertificateRequestId);
            // load csr with invalid app URI
            string testCSR = Utils.GetAbsoluteFilePath("test.csr", true, true, false);
            byte[] certificateRequest = File.ReadAllBytes(testCSR);
            await NUnit.Framework.Assert.ThatAsync(
                () => m_gdsClient.GDSClient.StartSigningRequestAsync(
                        application.ApplicationRecord.ApplicationId,
                        application.CertificateGroupId,
                        application.CertificateTypeId,
                        certificateRequest),
                Throws.Exception).ConfigureAwait(false);
        }

        [Test]
        [Order(600)]
        public async Task GetGoodCertificateGroupsNullTestsAsync()
        {
            AssertIgnoreTestWithoutGoodRegistration();
            await ConnectGDSAsync(true).ConfigureAwait(false);

            await NUnit.Framework.Assert
                .ThatAsync(() => m_gdsClient.GDSClient.GetCertificateGroupsAsync(null), Throws.Exception).ConfigureAwait(false);

            foreach (ApplicationTestData application in m_goodApplicationTestSet)
            {
                NodeId trustListId = await m_gdsClient.GDSClient.GetTrustListAsync(
                    application.ApplicationRecord.ApplicationId,
                    null).ConfigureAwait(false);
                TrustListDataType trustList = await m_gdsClient.GDSClient.ReadTrustListAsync(trustListId).ConfigureAwait(false);
                await NUnit.Framework.Assert
                    .ThatAsync(() => m_gdsClient.GDSClient.ReadTrustListAsync(null), Throws.Exception).ConfigureAwait(false);
                foreach (
                    NodeId certificateGroup in await m_gdsClient.GDSClient.GetCertificateGroupsAsync(
                        application.ApplicationRecord.ApplicationId).ConfigureAwait(false))
                {
                    await NUnit.Framework.Assert.ThatAsync(
                        () => m_gdsClient.GDSClient.GetTrustListAsync(null, certificateGroup),
                        Throws.Exception).ConfigureAwait(false);
                }
            }
        }

        [Test]
        [Order(601)]
        public async Task GetInvalidCertificateGroupsNullTestsAsync()
        {
            AssertIgnoreTestWithoutInvalidRegistration();
            await ConnectGDSAsync(true).ConfigureAwait(false);
            await NUnit.Framework.Assert
                .ThatAsync(() => m_gdsClient.GDSClient.GetCertificateGroupsAsync(null), Throws.Exception).ConfigureAwait(false);
            await NUnit.Framework.Assert.ThatAsync(
                () => m_gdsClient.GDSClient.GetCertificateGroupsAsync(new NodeId(Guid.NewGuid())),
                Throws.Exception).ConfigureAwait(false);

            foreach (ApplicationTestData application in m_invalidApplicationTestSet)
            {
                await NUnit.Framework.Assert.ThatAsync(
                    () => _ = m_gdsClient.GDSClient
                        .GetTrustListAsync(application.ApplicationRecord.ApplicationId, null),
                    Throws.Exception).ConfigureAwait(false);
                await NUnit.Framework.Assert.ThatAsync(
                    () =>
                        _ = m_gdsClient.GDSClient.GetTrustListAsync(
                            application.ApplicationRecord.ApplicationId,
                            new NodeId(Guid.NewGuid())
                        ),
                    Throws.Exception).ConfigureAwait(false);
                await NUnit.Framework.Assert.ThatAsync(
                    () => _ = m_gdsClient.GDSClient
                        .GetCertificateGroupsAsync(application.ApplicationRecord.ApplicationId),
                    Throws.Exception).ConfigureAwait(false);
            }
        }

        [Test]
        [Order(610)]
        public async Task GetGoodCertificateGroupsAndTrustListsAsync()
        {
            AssertIgnoreTestWithoutGoodRegistration();
            await ConnectGDSAsync(true).ConfigureAwait(false);

            foreach (ApplicationTestData application in m_goodApplicationTestSet)
            {
                foreach (
                    NodeId certificateGroup in await m_gdsClient.GDSClient.GetCertificateGroupsAsync(
                        application.ApplicationRecord.ApplicationId).ConfigureAwait(false))
                {
                    NodeId trustListId = await m_gdsClient.GDSClient.GetTrustListAsync(
                        application.ApplicationRecord.ApplicationId,
                        certificateGroup).ConfigureAwait(false);

                    Assert.NotNull(trustListId);

                    // Opc.Ua.TrustListDataType -> not possible, this needs ApplicationUserAccess
                    await m_gdsClient.GDSClient.ReadTrustListAsync(trustListId).ConfigureAwait(false);
                }
            }
        }

        [Test]
        [Order(620)]
        public async Task FailToGetGoodCertificateGroupsWithoutPriviledgesAsync()
        {
            AssertIgnoreTestWithoutGoodRegistration();
            AssertIgnoreTestWithoutGoodNewKeyPairRequest();

            //connect to GDS without Admin Privilege
            await ConnectGDSAsync(false).ConfigureAwait(false);

            foreach (ApplicationTestData application in m_goodApplicationTestSet)
            {
                if (application.Certificate != null)
                {
                    ServiceResultException sre = NUnit.Framework.Assert
                        .ThrowsAsync<ServiceResultException>(() =>
                            m_gdsClient.GDSClient
                            .GetCertificateGroupsAsync(application.ApplicationRecord.ApplicationId));
                    Assert.NotNull(sre);
                    Assert.AreEqual(
                        (StatusCode)StatusCodes.BadUserAccessDenied,
                        (StatusCode)sre.StatusCode,
                        sre.Result.ToString());
                }
            }
        }

        /// <summary>
        /// use self registered application and get the group / trust lists
        /// </summary>
        [Test]
        [Order(630)]
        public async Task GetGoodCertificateGroupsAsSelfAdminAsync()
        {
            AssertIgnoreTestWithoutGoodRegistration();
            AssertIgnoreTestWithoutGoodNewKeyPairRequest();

            // register at gds and get gds issued certificate
            bool success = await m_gdsClient.RegisterTestClientAtGdsAsync().ConfigureAwait(false);

            if (success)
            {
                m_gdsRegisteredTestClient = true;
            }
            else
            {
                NUnit.Framework.Assert.Fail("Registering test Client at GDS failed");
            }

            await ConnectGDSAsync(false, true).ConfigureAwait(false);

            // ensure access to other applications is denied
            foreach (ApplicationTestData testApplication in m_goodApplicationTestSet)
            {
                if (testApplication.Certificate != null)
                {
                    ServiceResultException sre = NUnit.Framework.Assert
                        .ThrowsAsync<ServiceResultException>(() =>
                            m_gdsClient.GDSClient
                            .GetCertificateGroupsAsync(testApplication.ApplicationRecord.ApplicationId));
                    Assert.NotNull(sre);
                    Assert.AreEqual(
                        (StatusCode)StatusCodes.BadUserAccessDenied,
                        (StatusCode)sre.StatusCode,
                        sre.Result.ToString());
                }
            }

            ApplicationTestData application = m_gdsClient.OwnApplicationTestData;

            // use self registered application and get the group / trust lists
            foreach (
                NodeId certificateGroup in await m_gdsClient.GDSClient.GetCertificateGroupsAsync(
                    application.ApplicationRecord.ApplicationId).ConfigureAwait(false))
            {
                NodeId trustListId = await m_gdsClient.GDSClient.GetTrustListAsync(
                    application.ApplicationRecord.ApplicationId,
                    certificateGroup).ConfigureAwait(false);
                // Opc.Ua.TrustListDataType
                TrustListDataType trustList = await m_gdsClient.GDSClient.ReadTrustListAsync(trustListId).ConfigureAwait(false); //ToDo make it possible to read the trust List with SelfAdminPrivilege
                Assert.NotNull(trustListId);
            }
            await DisconnectGDSAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// self issue a certificate and read it back
        /// </summary>
        [Test]
        [Order(631)]
        public async Task GoodSigningRequestAsSelfAdminAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            AssertIgnoreTestWithoutGdsRegisteredTestClient();
            AssertIgnoreTestWithoutGoodRegistration();
            AssertIgnoreTestWithoutGoodNewKeyPairRequest();

            ApplicationTestData application = m_gdsClient.OwnApplicationTestData;

            await ConnectGDSAsync(false, true).ConfigureAwait(false);
            Assert.Null(application.CertificateRequestId);
            X509Certificate2 csrCertificate;
            if (application.PrivateKeyFormat == "PFX")
            {
                csrCertificate = X509Utils.CreateCertificateFromPKCS12(
                    application.PrivateKey,
                    application.PrivateKeyPassword);
            }
            else
            {
                csrCertificate = CertificateFactory.CreateCertificateWithPEMPrivateKey(
                    X509CertificateLoader.LoadCertificate(application.Certificate),
                    application.PrivateKey,
                    application.PrivateKeyPassword);
            }
            byte[] certificateRequest = CertificateFactory.CreateSigningRequest(
                csrCertificate,
                application.DomainNames);
            csrCertificate.Dispose();

            // ensure access to other applications is denied
            foreach (ApplicationTestData testApplication in m_goodApplicationTestSet)
            {
                if (testApplication.CertificateRequestId == null)
                {
                    ServiceResultException sre = NUnit.Framework.Assert
                        .ThrowsAsync<ServiceResultException>(() =>
                            m_gdsClient.GDSClient.StartSigningRequestAsync(
                                testApplication.ApplicationRecord.ApplicationId,
                                testApplication.CertificateGroupId,
                                testApplication.CertificateTypeId,
                                certificateRequest));
                    Assert.NotNull(sre);
                    Assert.AreEqual(
                        (StatusCode)StatusCodes.BadUserAccessDenied,
                        (StatusCode)sre.StatusCode,
                        sre.Result.ToString());
                }
            }

            //own Application is allowed
            NodeId requestId = await m_gdsClient.GDSClient.StartSigningRequestAsync(
                application.ApplicationRecord.ApplicationId,
                application.CertificateGroupId,
                application.CertificateTypeId,
                certificateRequest).ConfigureAwait(false);
            Assert.NotNull(requestId);
            application.CertificateRequestId = requestId;
            bool requestBusy;
            DateTime now = DateTime.UtcNow;
            do
            {
                requestBusy = false;

                if (application.CertificateRequestId != null)
                {
                    try
                    {
                        (byte[] certificate, byte[] privateKey, byte[][] issuerCertificates) = await m_gdsClient.GDSClient.FinishRequestAsync(
                            application.ApplicationRecord.ApplicationId,
                            application.CertificateRequestId).ConfigureAwait(false);

                        if (certificate != null)
                        {
                            application.CertificateRequestId = null;

                            Assert.Null(privateKey);
                            Assert.NotNull(issuerCertificates);
                            application.Certificate = certificate;
                            application.IssuerCertificates = issuerCertificates;
                            X509TestUtils.VerifySignedApplicationCert(
                                application,
                                certificate,
                                issuerCertificates);
                            X509TestUtils.VerifyApplicationCertIntegrity(
                                certificate,
                                application.PrivateKey,
                                application.PrivateKeyPassword,
                                application.PrivateKeyFormat,
                                issuerCertificates,
                                telemetry);
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

                if (requestBusy)
                {
                    Thread.Sleep(5000);
                    Console.WriteLine("Waiting for certificate approval");
                }
            } while (requestBusy);

            await DisconnectGDSAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// self issue a public/private key pair and read it back
        /// </summary>
        [Test]
        [Order(632)]
        public async Task GoodKeyPairRequestAsSelfAdminAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            AssertIgnoreTestWithoutGdsRegisteredTestClient();
            AssertIgnoreTestWithoutGoodRegistration();
            AssertIgnoreTestWithoutGoodNewKeyPairRequest();

            ApplicationTestData application = m_gdsClient.OwnApplicationTestData;

            await ConnectGDSAsync(false, true).ConfigureAwait(false);

            // ensure access to other applications is denied
            foreach (ApplicationTestData testApplication in m_goodApplicationTestSet)
            {
                if (testApplication.CertificateRequestId == null)
                {
                    ServiceResultException sre = NUnit.Framework.Assert
                        .ThrowsAsync<ServiceResultException>(() =>
                            m_gdsClient.GDSClient.StartNewKeyPairRequestAsync(
                                testApplication.ApplicationRecord.ApplicationId,
                                testApplication.CertificateGroupId,
                                testApplication.CertificateTypeId,
                                testApplication.Subject,
                                testApplication.DomainNames,
                                testApplication.PrivateKeyFormat,
                                testApplication.PrivateKeyPassword));
                    Assert.NotNull(sre);
                    Assert.AreEqual(
                        (StatusCode)StatusCodes.BadUserAccessDenied,
                        (StatusCode)sre.StatusCode,
                        sre.Result.ToString());
                }
            }

            Assert.Null(application.CertificateRequestId);
            //Start KeyPairRequest
            NodeId requestId = await m_gdsClient.GDSClient.StartNewKeyPairRequestAsync(
                application.ApplicationRecord.ApplicationId,
                application.CertificateGroupId,
                application.CertificateTypeId,
                application.Subject,
                application.DomainNames,
                application.PrivateKeyFormat,
                application.PrivateKeyPassword).ConfigureAwait(false);

            Assert.NotNull(requestId);
            application.CertificateRequestId = requestId;

            //Finish KeyPairRequest
            bool requestBusy;
            DateTime now = DateTime.UtcNow;
            do
            {
                requestBusy = false;
                if (application.CertificateRequestId != null)
                {
                    try
                    {
                        (byte[] certificate, byte[] privateKey, byte[][] issuerCertificates) = await m_gdsClient.GDSClient.FinishRequestAsync(
                            application.ApplicationRecord.ApplicationId,
                            application.CertificateRequestId).ConfigureAwait(false);

                        if (certificate != null)
                        {
                            application.CertificateRequestId = null;

                            Assert.NotNull(certificate);
                            Assert.NotNull(privateKey);
                            Assert.NotNull(issuerCertificates);
                            X509TestUtils.VerifySignedApplicationCert(
                                application,
                                certificate,
                                issuerCertificates);
                            X509TestUtils.VerifyApplicationCertIntegrity(
                                certificate,
                                privateKey,
                                application.PrivateKeyPassword,
                                application.PrivateKeyFormat,
                                issuerCertificates,
                                telemetry);
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

                if (requestBusy)
                {
                    Thread.Sleep(5000);
                    Console.WriteLine("Waiting for certificate approval");
                }
            } while (requestBusy);

            await DisconnectGDSAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// unregister the Client at the GDS and try to read the trust List
        /// </summary>
        [Test]
        [Order(633)]
        public async Task FailToGetGoodCertificateGroupsWithoutSelfAdminPrivilegeAsync()
        {
            AssertIgnoreTestWithoutGoodRegistration();
            AssertIgnoreTestWithoutGoodNewKeyPairRequest();
            AssertIgnoreTestWithoutGdsRegisteredTestClient();

            await ConnectGDSAsync(true).ConfigureAwait(false);

            ApplicationTestData application = m_gdsClient.OwnApplicationTestData;

            //unregister GDS Client
            await m_gdsClient.GDSClient
                .UnregisterApplicationAsync(application.ApplicationRecord.ApplicationId).ConfigureAwait(false);

            m_gdsRegisteredTestClient = false;

            await DisconnectGDSAsync().ConfigureAwait(false);

            //connect as self admin with revoked cert

            await ConnectGDSAsync(false, true).ConfigureAwait(false);
            ServiceResultException sre = NUnit.Framework.Assert.ThrowsAsync<ServiceResultException>(() =>
                m_gdsClient.GDSClient
                    .GetCertificateGroupsAsync(application.ApplicationRecord.ApplicationId));
            Assert.NotNull(sre);
            Assert.AreEqual(
                (StatusCode)StatusCodes.BadUserAccessDenied,
                (StatusCode)sre.StatusCode,
                sre.Result.ToString());
        }

        [Test]
        [Order(690)]
        public async Task GetGoodCertificateStatusAsync()
        {
            AssertIgnoreTestWithoutGoodRegistration();
            await ConnectGDSAsync(true).ConfigureAwait(false);
            foreach (ApplicationTestData application in m_goodApplicationTestSet)
            {
                bool certificateStatus = await m_gdsClient.GDSClient.GetCertificateStatusAsync(
                    application.ApplicationRecord.ApplicationId,
                    null,
                    null).ConfigureAwait(false);
                Assert.False(certificateStatus);
            }
        }

        [Test]
        [Order(691)]
        public async Task GetInvalidCertificateStatusAsync()
        {
            AssertIgnoreTestWithoutInvalidRegistration();
            await ConnectGDSAsync(true).ConfigureAwait(false);
            foreach (ApplicationTestData application in m_invalidApplicationTestSet)
            {
                await NUnit.Framework.Assert.ThatAsync(
                    () =>
                        _ = m_gdsClient.GDSClient.GetCertificateStatusAsync(
                            application.ApplicationRecord.ApplicationId,
                            null,
                            null
                        ),
                    Throws.Exception).ConfigureAwait(false);
            }
        }

        [Test]
        [Order(700)]
        public async Task CheckGoodRevocationStatusAsync()
        {
            AssertIgnoreTestWithoutGoodRegistration();
            await ConnectGDSAsync(false).ConfigureAwait(false);
            foreach (ApplicationTestData application in m_goodApplicationTestSet)
            {
                (StatusCode certificateStatus, DateTime validityTime) = await m_gdsClient.GDSClient.CheckRevocationStatusAsync(
                    application.Certificate).ConfigureAwait(false);
                //Status code needs to be Bad as the method builds a custom chain that does not know about the custom cert stores.
                Assert.True(StatusCode.IsBad(certificateStatus.Code));
                Assert.NotNull(validityTime);
            }
        }

        [Test]
        [Order(895)]
        public async Task RevokeGoodCertificatesAsync()
        {
            AssertIgnoreTestWithoutInvalidRegistration();
            AssertIgnoreTestWithoutGoodNewKeyPairRequest();
            await ConnectGDSAsync(true).ConfigureAwait(false);
            foreach (ApplicationTestData application in m_goodApplicationTestSet)
            {
                await m_gdsClient.GDSClient.RevokeCertificateAsync(
                    application.ApplicationRecord.ApplicationId,
                    application.Certificate).ConfigureAwait(false);
            }
            foreach (ApplicationTestData application in m_invalidApplicationTestSet)
            {
                await NUnit.Framework.Assert.ThatAsync(
                    () =>
                        m_gdsClient.GDSClient.RevokeCertificateAsync(
                            application.ApplicationRecord.ApplicationId,
                            application.Certificate
                        ),
                    Throws.Exception).ConfigureAwait(false);
            }
        }

        [Test]
        [Order(900)]
        public async Task UnregisterGoodApplicationsAsync()
        {
            AssertIgnoreTestWithoutGoodRegistration();
            await ConnectGDSAsync(true).ConfigureAwait(false);
            foreach (ApplicationTestData application in m_goodApplicationTestSet)
            {
                await m_gdsClient.GDSClient
                    .UnregisterApplicationAsync(application.ApplicationRecord.ApplicationId).ConfigureAwait(false);
            }
        }

        [Test]
        [Order(910)]
        public async Task CheckRevocationStatusUnregisteredApplicationsAsync()
        {
            AssertIgnoreTestWithoutGoodRegistration();
            await ConnectGDSAsync(false).ConfigureAwait(false);
            foreach (ApplicationTestData application in m_goodApplicationTestSet)
            {
                (StatusCode certificateStatus, DateTime validityTime) = await m_gdsClient.GDSClient.CheckRevocationStatusAsync(
                    application.Certificate).ConfigureAwait(false);
                Assert.IsTrue(
                    ((StatusCode)certificateStatus.Code).ToString().StartsWith("BadCertificate"));
                Assert.NotNull(validityTime);
            }
        }

        [Test]
        [Order(910)]
        public async Task UnregisterInvalidApplicationsAsync()
        {
            await ConnectGDSAsync(true).ConfigureAwait(false);
            foreach (ApplicationTestData application in m_invalidApplicationTestSet)
            {
                await NUnit.Framework.Assert.ThatAsync(
                    () => m_gdsClient.GDSClient
                        .UnregisterApplicationAsync(application.ApplicationRecord.ApplicationId),
                    Throws.Exception).ConfigureAwait(false);
            }
        }

        [Test]
        [Order(915)]
        public async Task VerifyUnregisterGoodApplicationsAsync()
        {
            AssertIgnoreTestWithoutGoodRegistration();
            await ConnectGDSAsync(true).ConfigureAwait(false);
            foreach (ApplicationTestData application in m_goodApplicationTestSet)
            {
                ApplicationRecordDataType[] result = await m_gdsClient.GDSClient.FindApplicationAsync(
                    application.ApplicationRecord.ApplicationUri).ConfigureAwait(false);
                if (result != null)
                {
                    Assert.NotNull(result);
                    Assert.AreEqual(0, result.Length, "Found deleted application on server!");
                }
            }
        }

        [Test]
        [Order(920)]
        public async Task UnregisterUnregisteredGoodApplicationsAsync()
        {
            AssertIgnoreTestWithoutGoodRegistration();
            await ConnectGDSAsync(true).ConfigureAwait(false);
            foreach (ApplicationTestData application in m_goodApplicationTestSet)
            {
                await NUnit.Framework.Assert.ThatAsync(
                    () => m_gdsClient.GDSClient
                        .UnregisterApplicationAsync(application.ApplicationRecord.ApplicationId),
                    Throws.Exception).ConfigureAwait(false);
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

        private async Task ConnectGDSAsync(
            bool admin,
            bool anonymous = false,
            [System.Runtime.CompilerServices.CallerMemberName] string memberName = "")
        {
            if (anonymous)
            {
                m_gdsClient.GDSClient.AdminCredentials = m_gdsClient.Anonymous;
            }
            else
            {
                m_gdsClient.GDSClient.AdminCredentials = admin
                    ? m_gdsClient.AdminUser
                    : m_gdsClient.AppUser;
            }
            await m_gdsClient.GDSClient.ConnectAsync().ConfigureAwait(false);
            TestContext.Progress.WriteLine($"GDS Client({admin}) connected -- {memberName}");
        }

        private async Task DisconnectGDSAsync(
            [System.Runtime.CompilerServices.CallerMemberName] string memberName = "")
        {
            await m_gdsClient.GDSClient.DisconnectAsync().ConfigureAwait(false);
            TestContext.Progress.WriteLine($"GDS Client disconnected -- {memberName}");
        }

        private void AssertIgnoreTestWithoutGoodRegistration()
        {
            if (!m_goodRegistrationOk)
            {
                NUnit.Framework.Assert.Ignore("Test requires good application registrations.");
            }
        }

        private void AssertIgnoreTestWithoutGdsRegisteredTestClient()
        {
            if (!m_gdsRegisteredTestClient)
            {
                NUnit.Framework.Assert.Ignore(
                    "Test requires the test client to be registered at the GDS and use a GDS signed Certificate.");
            }
        }

        private void AssertIgnoreTestWithoutInvalidRegistration()
        {
            if (!m_invalidRegistrationOk)
            {
                NUnit.Framework.Assert.Ignore("Test requires invalid application registration.");
            }
        }

        private void AssertIgnoreTestWithoutGoodNewKeyPairRequest()
        {
            if (!m_goodNewKeyPairRequestOk)
            {
                NUnit.Framework.Assert.Ignore("Test requires good new key pair request.");
            }
        }

        private int GoodServersOnNetworkCount()
        {
            return m_goodApplicationTestSet.Sum(a => a.ApplicationRecord.DiscoveryUrls.Count);
        }

        private const int kGoodApplicationsTestCount = 10;
        private const int kInvalidApplicationsTestCount = 10;
        private ApplicationTestDataGenerator m_appTestDataGenerator;
        private ITelemetryContext m_telemetry;
        private GlobalDiscoveryTestServer m_server;
        private GlobalDiscoveryTestClient m_gdsClient;
        private IList<ApplicationTestData> m_goodApplicationTestSet;
        private IList<ApplicationTestData> m_invalidApplicationTestSet;
        private bool m_goodRegistrationOk;
        private bool m_invalidRegistrationOk;
        private bool m_gdsRegisteredTestClient;
        private List<NodeId> m_supportedCertificateTypes;
        private bool m_goodNewKeyPairRequestOk;
        private readonly string m_storeType;
    }
}
