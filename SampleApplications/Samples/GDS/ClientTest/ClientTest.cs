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
using Opc.Ua.Gds.Test;
using Opc.Ua.Test;
using System;
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

        /// <summary>
        /// 
        /// </summary>
        [SetUp]
        protected void SetUp()
        {
            m_randomSource = new RandomSource(1);
            m_dataGenerator = new DataGenerator(m_randomSource);
            m_server = new GlobalDiscoveryTestServer(true);
            m_server.StartServer().Wait();
            m_client = new GlobalDiscoveryTestClient(true);
            m_client.ConnectClient().Wait();
            m_client.GDSClient.AdminCredentials = new UserIdentity("appadmin", "demo");
            m_client.GDSClient.Connect(m_client.GDSClient.EndpointUrl);
        }

        /// <summary>
        /// 
        /// </summary>
        [TearDown]
        protected void TearDown()
        {
            m_client.DisconnectClient();
            m_server.StopServer();
        }

        /// <summary>
        /// 
        /// </summary>
        /// 
        [Test]
        public void RegisterApplication()
        {
            for (int i = 0; i < 100; i++)
            {
                ApplicationRecordDataType application = RandomApplication();
                NodeId id = m_client.GDSClient.RegisterApplication(application);
            }
        }


        #region Private Methods
        private ApplicationRecordDataType RandomApplication()
        {
            ApplicationType appType = (ApplicationType)m_randomSource.NextInt32((int)ApplicationType.ClientAndServer);
            string pureAppName = m_dataGenerator.GetRandomString("en");
            pureAppName = Regex.Replace(pureAppName, @"[^\w\d\s]", "");
            string pureAppUri = Regex.Replace(pureAppName, @"[^\w\d]", "");
            string appName = "UA " + pureAppName;
            string localhost = Regex.Replace(m_dataGenerator.GetRandomSymbol("en").Trim().ToLower(), @"[^\w\d]", "");
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
                    int port = (m_dataGenerator.GetRandomInt16() & 0x1fff) + 50000;
                    discoveryUrls = new StringCollection { String.Format("opc.tcp://{0}:{1}/{2}", localhost, port.ToString(), pureAppUri) };
                    serverCapabilities = new StringCollection { "DA", "HA" };
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
        #endregion

        #region Private Fields
        private RandomSource m_randomSource;
        private DataGenerator m_dataGenerator;
        private GlobalDiscoveryTestServer m_server;
        private GlobalDiscoveryTestClient m_client;
        #endregion
    }
}