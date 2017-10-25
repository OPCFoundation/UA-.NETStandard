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
    /// <summary>
    /// 
    /// </summary>
    /// 
    [TestFixture]
    public class ClientTest
    {
        const int goodApplicationsTestCount = 100;
        const int invalidApplicationsTestCount = 10;
        const int randomStart = 1;

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
            _goodApplications = ApplicationTestSet(goodApplicationsTestCount, false);
            _invalidApplications = ApplicationTestSet(invalidApplicationsTestCount, true);
            
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

        /// <summary>
        /// 
        /// </summary>
        /// 
        [Test, Order(100)]
        public void RegisterGoodApplications()
        {
            foreach (var application in _goodApplications)
            {
                NodeId id = _client.GDSClient.RegisterApplication(application);
                Assert.NotNull(id);
                Assert.IsFalse(id.IsNullNodeId);
                Assert.AreEqual(id.IdType, IdType.Guid);
                application.ApplicationId = id;
            }

        }

        [Test, Order(100)]
        public void RegisterInvalidApplications()
        {
            foreach (var application in _invalidApplications)
            {
                Assert.That(() => { NodeId id = _client.GDSClient.RegisterApplication(application); }, Throws.Exception);
            }
        }

        [Test, Order(200)]
        public void UpdateGoodApplications()
        {
            foreach (var application in _goodApplications)
            {
                var updatedApplication = (ApplicationRecordDataType)application.MemberwiseClone();
                updatedApplication.ApplicationUri += "update";
                _client.GDSClient.UpdateApplication(updatedApplication);
                var result = _client.GDSClient.FindApplication(updatedApplication.ApplicationUri);
                _client.GDSClient.UpdateApplication(application);
                Assert.NotNull(result);
                Assert.GreaterOrEqual(1, result.Length, "Couldn't find updated application");
            }
        }

        [Test, Order(200)]
        public void UpdateGoodApplicationsWithNewGuid()
        {
            foreach (var application in _goodApplications)
            {
                var testApplication = (ApplicationRecordDataType)application.MemberwiseClone();
                testApplication.ApplicationId = new NodeId(Guid.NewGuid());
                Assert.That(() => { _client.GDSClient.UpdateApplication(testApplication); }, Throws.Exception);
            }
        }

        [Test, Order(200)]
        public void UpdateInvalidApplications()
        {
            foreach (var application in _invalidApplications)
            {
                Assert.That(() => { _client.GDSClient.UpdateApplication(application); }, Throws.Exception);
            }
        }

        [Test, Order(400)]
        public void FindGoodApplications()
        {
            foreach (var application in _goodApplications)
            {
                var result = _client.GDSClient.FindApplication(application.ApplicationUri);
                Assert.NotNull(result);
                Assert.GreaterOrEqual(1, result.Length, "Couldn't find good application");
            }
        }

        [Test, Order(400)]
        public void FindInvalidApplications()
        {
            foreach (var application in _invalidApplications)
            {
                var result = _client.GDSClient.FindApplication(application.ApplicationUri);
                Assert.NotNull(result);
                Assert.AreEqual(0, result.Length, "Found invalid application on server");
            }
        }

        [Test, Order(400)]
        public void GetGoodApplications()
        {
            foreach (var application in _goodApplications)
            {
                var result = _client.GDSClient.GetApplication(application.ApplicationId);
                Assert.NotNull(result);
                Assert.IsTrue(Utils.IsEqual(application, result));
            }
        }

        [Test, Order(400)]
        public void GetInvalidApplications()
        {
            foreach (var application in _invalidApplications)
            {
                Assert.That(() => { var result = _client.GDSClient.GetApplication(application.ApplicationId); }, Throws.Exception);
            }
        }

        [Test, Order(900)]
        public void UnregisterInvalidApplications()
        {
            foreach (var application in _invalidApplications)
            {
                Assert.That(() => {_client.GDSClient.UnregisterApplication(application.ApplicationId); }, Throws.Exception);
            }
        }

        [Test, Order(900)]
        public void UnregisterGoodApplications()
        {
            foreach (var application in _goodApplications)
            {
                _client.GDSClient.UnregisterApplication(application.ApplicationId);
            }
        }

        [Test, Order(901)]
        public void UnregisterUnregisteredGoodApplications()
        {
            foreach (var application in _goodApplications)
            {
                Assert.That(() => { _client.GDSClient.UnregisterApplication(application.ApplicationId); }, Throws.Exception);
            }
        }

        #region Private Methods
        private IList<ApplicationRecordDataType> ApplicationTestSet(int count, bool invalidateSet)
        {
            var applications = new List<ApplicationRecordDataType>();
            for (int i = 0; i < count; i++)
            {
                var application = RandomApplication();
                if (invalidateSet)
                {
                    switch (i % 6)
                    {
                        case 0: application.ApplicationUri = _dataGenerator.GetRandomString(); break;
                        case 1: application.ApplicationType = (application.ApplicationType == ApplicationType.Client) ?
                                ApplicationType.Server : ApplicationType.Client; break;
                        case 2: application.ProductUri = _dataGenerator.GetRandomString(); break;
                        case 3: application.DiscoveryUrls = application.DiscoveryUrls == null ? 
                                new StringCollection { "opc.tcp://xxx:333" } : null; break;
                        case 4: application.ServerCapabilities = application.ServerCapabilities == null ?
                                RandomServerCapabilities() : null; break;
                        case 5: application.ApplicationId = new NodeId(100); break;
                    }
                }
                applications.Add(application);
            }
            return applications;
        }

        private ApplicationRecordDataType RandomApplication()
        {
            ApplicationType appType = (ApplicationType)_randomSource.NextInt32((int)ApplicationType.ClientAndServer);
            string pureAppName = _dataGenerator.GetRandomString("en");
            pureAppName = Regex.Replace(pureAppName, @"[^\w\d\s]", "");
            string pureAppUri = Regex.Replace(pureAppName, @"[^\w\d]", "");
            string appName = "UA " + pureAppName;
            string localhost = Regex.Replace(_dataGenerator.GetRandomSymbol("en").Trim().ToLower(), @"[^\w\d]", "");
            if (localhost.Length >= 12)
            {
                localhost = localhost.Substring(0, 12);
            }
            string appUri = ("urn:localhost:opcfoundation.org:" + pureAppUri.ToLower()).Replace("localhost", localhost);
            string prodUri = "http://opcfoundation.org/UA/" + pureAppUri;
            StringCollection discoveryUrls = null;
            StringCollection serverCapabilities = null;
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
                    discoveryUrls = new StringCollection { String.Format("opc.tcp://{0}:{1}/{2}", localhost, port.ToString(), pureAppUri) };
                    serverCapabilities = RandomServerCapabilities();
                    break;
            }
            ApplicationRecordDataType application = new ApplicationRecordDataType
            {
                ApplicationNames = new LocalizedTextCollection { new LocalizedText("en-us", appName) },
                ApplicationUri = appUri,
                ApplicationType = appType,
                ProductUri = prodUri,
                DiscoveryUrls = discoveryUrls,
                ServerCapabilities = serverCapabilities
            };
            return application;
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
        #endregion

        #region Private Fields
        private RandomSource _randomSource;
        private DataGenerator _dataGenerator;
        private GlobalDiscoveryTestServer _server;
        private GlobalDiscoveryTestClient _client;
        private IList<ApplicationRecordDataType> _goodApplications;
        private IList<ApplicationRecordDataType> _invalidApplications;
        private ServerCapabilities _serverCapabilities;
        #endregion
    }
}