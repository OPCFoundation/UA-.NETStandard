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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Server.Tests;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// Test Client Reverse Connect Services.
    /// </summary>
    [TestFixture, Category("Client")]
    [SetCulture("en-us"), SetUICulture("en-us")]
    [NonParallelizable]
    public class ReverseConnectTest
    {
        const int MaxTimeout = 10000;
        ServerFixture<ReferenceServer> m_serverFixture;
        ClientFixture m_clientFixture;
        ReferenceServer m_server;
        EndpointDescriptionCollection m_endpoints;
        Uri m_endpointUrl;
        string m_pkiRoot;

        #region DataPointSources
        [DatapointSource]
        public static string[] Policies = SecurityPolicies.GetDisplayNames()
            .Select(displayName => SecurityPolicies.GetUri(displayName)).ToArray();
        #endregion

        #region Test Setup
        /// <summary>
        /// Setup a server and client fixture.
        /// </summary>
        [OneTimeSetUp]
        public async Task OneTimeSetUpAsync()
        {
            // pki directory root for test runs. 
            m_pkiRoot = Path.GetTempPath() + Path.GetRandomFileName();

            // start ref server
            m_serverFixture = new ServerFixture<ReferenceServer> {
                AutoAccept = true,
                SecurityNone = true,
                ReverseConnectTimeout = MaxTimeout,
                TraceMasks = Utils.TraceMasks.Error | Utils.TraceMasks.Security
            };
            m_server = await m_serverFixture.StartAsync(TestContext.Out, m_pkiRoot).ConfigureAwait(false);

            // create client
            m_clientFixture = new ClientFixture();
            await m_clientFixture.LoadClientConfiguration(m_pkiRoot).ConfigureAwait(false);
            await m_clientFixture.StartReverseConnectHost().ConfigureAwait(false);
            m_endpointUrl = new Uri(Utils.ReplaceLocalhost("opc.tcp://localhost:" + m_serverFixture.Port.ToString()));
            // start reverse connection
            m_server.AddReverseConnection(new Uri(m_clientFixture.ReverseConnectUri), MaxTimeout);
        }

        /// <summary>
        /// Tear down the Server and the Client.
        /// </summary>
        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
            await m_serverFixture.StopAsync().ConfigureAwait(false);
            await Task.Delay(1000).ConfigureAwait(false);
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
        /// <summary>
        /// Get endpoints using a reverse connection.
        /// </summary>
        [Test, Order(100)]
        public async Task GetEndpoints()
        {
            await RequireEndpoints().ConfigureAwait(false);
        }

        /// <summary>
        /// Internal get endpoints which is called with semaphore.
        /// </summary>
        public async Task GetEndpointsInternal()
        {
            var config = m_clientFixture.Config;
            ITransportWaitingConnection connection;
            using (var cancellationTokenSource = new CancellationTokenSource(MaxTimeout))
            {
                connection = await m_clientFixture.ReverseConnectManager.WaitForConnection(
                    m_endpointUrl, null, cancellationTokenSource.Token).ConfigureAwait(false);
                Assert.NotNull(connection, "Failed to get connection.");
            }
            var endpointConfiguration = EndpointConfiguration.Create();
            endpointConfiguration.OperationTimeout = MaxTimeout;
            using (DiscoveryClient client = DiscoveryClient.Create(config, connection, endpointConfiguration))
            {
                m_endpoints = client.GetEndpoints(null);
            }
        }

        [Test, Order(200)]
        public async Task SelectEndpoint()
        {
            var config = m_clientFixture.Config;
            ITransportWaitingConnection connection;
            using (var cancellationTokenSource = new CancellationTokenSource(MaxTimeout))
            {
                connection = await m_clientFixture.ReverseConnectManager.WaitForConnection(
                    m_endpointUrl, null, cancellationTokenSource.Token).ConfigureAwait(false);
                Assert.NotNull(connection, "Failed to get connection.");
            }
            var selectedEndpoint = CoreClientUtils.SelectEndpoint(config, connection, true, MaxTimeout);
            Assert.NotNull(selectedEndpoint);
        }

        [Theory, Order(300)]
        public async Task ReverseConnect(string securityPolicy)
        {
            // ensure endpoints are available
            await RequireEndpoints().ConfigureAwait(false);

            // get a connection
            var config = m_clientFixture.Config;
            ITransportWaitingConnection connection;
            using (var cancellationTokenSource = new CancellationTokenSource(MaxTimeout))
            {
                connection = await m_clientFixture.ReverseConnectManager.WaitForConnection(
                    m_endpointUrl, null, cancellationTokenSource.Token).ConfigureAwait(false);
                Assert.NotNull(connection, "Failed to get connection.");
            }

            // select the secure endpoint
            var endpointConfiguration = EndpointConfiguration.Create(config);
            var selectedEndpoint = ClientFixture.SelectEndpoint(config, m_endpoints, m_endpointUrl, securityPolicy);
            Assert.NotNull(selectedEndpoint);
            var endpoint = new ConfiguredEndpoint(null, selectedEndpoint, endpointConfiguration);
            Assert.NotNull(endpoint);

            // connect
            var session = await Session.Create(config, connection, endpoint, false, false, "Reverse Connect Client",
                MaxTimeout, new UserIdentity(new AnonymousIdentityToken()), null).ConfigureAwait(false);
            Assert.NotNull(session);

            // default request header
            var requestHeader = new RequestHeader();
            requestHeader.Timestamp = DateTime.UtcNow;
            requestHeader.TimeoutHint = MaxTimeout;

            // Browse
            var clientTestServices = new ClientTestServices(session);
            var referenceDescriptions = CommonTestWorkers.BrowseFullAddressSpaceWorker(clientTestServices, requestHeader);
            Assert.NotNull(referenceDescriptions);

            // close session
            var result = session.Close();
            Assert.NotNull(result);
            session.Dispose();
        }

        [Theory, Order(301)]
        public async Task ReverseConnect2(bool updateBeforeConnect, bool checkDomain)
        {
            string securityPolicy = SecurityPolicies.Basic256Sha256;

            // ensure endpoints are available
            await RequireEndpoints().ConfigureAwait(false);

            // get a connection
            var config = m_clientFixture.Config;

            // select the secure endpoint
            var endpointConfiguration = EndpointConfiguration.Create(config);
            var selectedEndpoint = ClientFixture.SelectEndpoint(config, m_endpoints, m_endpointUrl, securityPolicy);
            Assert.NotNull(selectedEndpoint);
            var endpoint = new ConfiguredEndpoint(null, selectedEndpoint, endpointConfiguration);
            Assert.NotNull(endpoint);

            // connect
            var session = await Session.Create(config, m_clientFixture.ReverseConnectManager, endpoint, updateBeforeConnect, checkDomain, "Reverse Connect Client",
                MaxTimeout, new UserIdentity(new AnonymousIdentityToken()), null).ConfigureAwait(false);
            Assert.NotNull(session);

            // header
            var requestHeader = new RequestHeader();
            requestHeader.Timestamp = DateTime.UtcNow;
            requestHeader.TimeoutHint = MaxTimeout;

            // Browse
            var clientTestServices = new ClientTestServices(session);
            var referenceDescriptions = CommonTestWorkers.BrowseFullAddressSpaceWorker(clientTestServices, requestHeader);
            Assert.NotNull(referenceDescriptions);

            // close session
            var result = session.Close();
            Assert.NotNull(result);
            session.Dispose();
        }
        #endregion

        #region Private Methods
        private async Task RequireEndpoints()
        {
            await m_requiredLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (m_endpoints == null)
                {
                    await GetEndpointsInternal().ConfigureAwait(false);
                }
            }
            finally
            {
                m_requiredLock.Release();
            }
        }
        #endregion

        private SemaphoreSlim m_requiredLock = new SemaphoreSlim(1);
    }
}
