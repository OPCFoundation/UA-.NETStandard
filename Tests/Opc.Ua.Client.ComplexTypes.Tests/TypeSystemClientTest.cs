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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.Tests;
using Opc.Ua.Configuration;
using Opc.Ua.Server.Tests;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Client.ComplexTypes.Tests
{
    /// <summary>
    /// Load Type System tests.
    /// </summary>
    [TestFixture, Category("Client")]
    [SetCulture("en-us"), SetUICulture("en-us")]
    [NonParallelizable]
    public class TypeSystemClientTest
    {
        const int kMaxReferences = 100;
        const int kMaxTimeout = 10000;
        ServerFixture<ReferenceServer> m_serverFixture;
        ClientFixture m_clientFixture;
        ReferenceServer m_server;
        Session m_session;
        string m_uriScheme;
        string m_pkiRoot;
        Uri m_url;

        public TypeSystemClientTest()
        {
            m_uriScheme = Utils.UriSchemeOpcTcp;
        }

        public TypeSystemClientTest(string uriScheme)
        {
            m_uriScheme = uriScheme;
        }

        #region Test Setup
        /// <summary>
        /// Set up a Server and a Client instance.
        /// </summary>
        [OneTimeSetUp]
        public Task OneTimeSetUp()
        {
            return OneTimeSetUpAsync(null);
        }

        /// <summary>
        /// Setup a server and client fixture.
        /// </summary>
        /// <param name="writer">The test output writer.</param>
        public async Task OneTimeSetUpAsync(TextWriter writer = null)
        {
            // pki directory root for test runs. 
            m_pkiRoot = Path.GetTempPath() + Path.GetRandomFileName();

            // start Ref server
            m_serverFixture = new ServerFixture<ReferenceServer> {
                UriScheme = m_uriScheme,
                SecurityNone = true,
                AutoAccept = true,
                AllNodeManagers = true,
            };
            if (writer != null)
            {
                m_serverFixture.TraceMasks = Utils.TraceMasks.Error | Utils.TraceMasks.StackTrace | Utils.TraceMasks.Security | Utils.TraceMasks.Information;
            }
            m_server = await m_serverFixture.StartAsync(writer ?? TestContext.Out, m_pkiRoot).ConfigureAwait(false);

            m_clientFixture = new ClientFixture();
            await m_clientFixture.LoadClientConfiguration(m_pkiRoot).ConfigureAwait(false);
            m_clientFixture.Config.TransportQuotas.MaxMessageSize =
            m_clientFixture.Config.TransportQuotas.MaxBufferSize = 4 * 1024 * 1024;
            m_url = new Uri(m_uriScheme + "://localhost:" + m_serverFixture.Port.ToString());
            try
            {
                m_session = await m_clientFixture.ConnectAsync(m_url, SecurityPolicies.Basic256Sha256).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Assert.Ignore("OneTimeSetup failed to create session, tests skipped. Error: {0}", e.Message);
            }
        }

        /// <summary>
        /// Tear down the Server and the Client.
        /// </summary>
        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
            if (m_session != null)
            {
                m_session.Close();
                m_session.Dispose();
                m_session = null;
            }
            await m_serverFixture.StopAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Test setup.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            m_serverFixture.SetTraceOutput(TestContext.Out);
        }
        #endregion

        #region Test Methods
        [Test, Order(100)]
        [TestCase(false, false, false)]
        [TestCase(true, false, false)]
        [TestCase(false, true, false)]
        [TestCase(false, false, true)]
        public async Task LoadTypeSystem(bool onlyEnumTypes, bool disableDataTypeDefinition, bool disableDataTypeDictionary)
        {
            var typeSystem = new ComplexTypeSystem(m_session);
            Assert.NotNull(typeSystem);
            typeSystem.DisableDataTypeDefinition = disableDataTypeDefinition;
            typeSystem.DisableDataTypeDictionary = disableDataTypeDictionary;

            bool success = await typeSystem.Load(onlyEnumTypes, true).ConfigureAwait(false);
            Assert.IsTrue(success);

            var types = typeSystem.GetDefinedTypes();
            TestContext.Out.WriteLine("Types loaded: {0} ", types.Length);
            foreach (var type in types)
            {
                TestContext.Out.WriteLine("Type: {0} ", type.FullName);
            }
        }
        #endregion
    }
}
