/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Server.Tests;
using Opc.Ua.Tests;
using Quickstarts.ReferenceServer;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// Test Client Reverse Connect Services.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public class ReverseConnectTest : ClientTestFramework
    {
        private Uri m_endpointUrl;

        [DatapointSource]
        public static readonly TelemetryParameterizable<ISessionFactory>[] SessionFactories =
        [
            TelemetryParameterizable.Create<ISessionFactory>(t => new TestableSessionFactory(t)),
            TelemetryParameterizable.Create<ISessionFactory>(t => new DefaultSessionFactory(t))
        ];

        /// <summary>
        /// Setup a server and client fixture.
        /// </summary>
        [OneTimeSetUp]
        public override async Task OneTimeSetUpAsync()
        {
            // this test fails on macOS, ignore (TODO)
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                NUnit.Framework.Assert.Ignore("Reverse connect fails on mac OS.");
            }

            // pki directory root for test runs.
            PkiRoot = Path.GetTempPath() + Path.GetRandomFileName();

            // start ref server with reverse connect
            ServerFixture = new ServerFixture<ReferenceServer>(t => new ReferenceServer(t))
            {
                AutoAccept = true,
                SecurityNone = true,
                ReverseConnectTimeout = MaxTimeout,
                TraceMasks = Utils.TraceMasks.Error | Utils.TraceMasks.Security
            };
            ReferenceServer = await ServerFixture.StartAsync(PkiRoot)
                .ConfigureAwait(false);

            // create client
            ClientFixture = new ClientFixture(telemetry: Telemetry);

            await ClientFixture.LoadClientConfigurationAsync(PkiRoot).ConfigureAwait(false);
            await ClientFixture.StartReverseConnectHostAsync().ConfigureAwait(false);
            m_endpointUrl = new Uri(
                Utils.ReplaceLocalhost(
                    "opc.tcp://localhost:" +
                    ServerFixture.Port.ToString(CultureInfo.InvariantCulture)));
            // start reverse connection
            ReferenceServer.AddReverseConnection(
                new Uri(ClientFixture.ReverseConnectUri),
                MaxTimeout);
        }

        /// <summary>
        /// Tear down the Server and the Client.
        /// </summary>
        [OneTimeTearDown]
        public override Task OneTimeTearDownAsync()
        {
            Utils.SilentDispose(ClientFixture);
            return base.OneTimeTearDownAsync();
        }

        /// <summary>
        /// Test setup.
        /// </summary>
        [SetUp]
        public override Task SetUpAsync()
        {
            return base.SetUpAsync();
        }

        /// <summary>
        /// Test teardown.
        /// </summary>
        [TearDown]
        public override Task TearDownAsync()
        {
            return base.TearDownAsync();
        }

        /// <summary>
        /// Get endpoints using a reverse connection.
        /// </summary>
        [Test]
        [Order(100)]
        public async Task GetEndpointsAsync()
        {
            await RequireEndpointsAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Internal get endpoints which is called with semaphore.
        /// </summary>
        public async Task GetEndpointsInternalAsync()
        {
            ApplicationConfiguration config = ClientFixture.Config;
            ITransportWaitingConnection connection;
            using (var cancellationTokenSource = new CancellationTokenSource(MaxTimeout))
            {
                connection = await ClientFixture
                    .ReverseConnectManager.WaitForConnectionAsync(
                        m_endpointUrl,
                        null,
                        cancellationTokenSource.Token)
                    .ConfigureAwait(false);
                Assert.NotNull(connection, "Failed to get connection.");
            }

            using (var cancellationTokenSource = new CancellationTokenSource(MaxTimeout))
            {
                var endpointConfiguration = EndpointConfiguration.Create();
                endpointConfiguration.OperationTimeout = MaxTimeout;
                using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                    config,
                    connection,
                    endpointConfiguration,
                    ct: cancellationTokenSource.Token).ConfigureAwait(false);
                Endpoints = await client.GetEndpointsAsync(default, cancellationTokenSource.Token)
                    .ConfigureAwait(false);
                await client.CloseAsync(cancellationTokenSource.Token).ConfigureAwait(false);
            }
        }

        [Test]
        [Order(200)]
        public async Task SelectEndpointAsync()
        {
            ApplicationConfiguration config = ClientFixture.Config;
            ITransportWaitingConnection connection;
            using (var cancellationTokenSource = new CancellationTokenSource(MaxTimeout))
            {
                connection = await ClientFixture
                    .ReverseConnectManager.WaitForConnectionAsync(
                        m_endpointUrl,
                        null,
                        cancellationTokenSource.Token)
                    .ConfigureAwait(false);
                Assert.NotNull(connection, "Failed to get connection.");
            }
            EndpointDescription selectedEndpoint = await CoreClientUtils.SelectEndpointAsync(
                config,
                connection,
                true,
                MaxTimeout,
                Telemetry).ConfigureAwait(false);
            Assert.NotNull(selectedEndpoint);
        }

        [Theory]
        [Order(300)]
        public async Task ReverseConnectAsync(string securityPolicy, TelemetryParameterizable<ISessionFactory> sessionFactory)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // ensure endpoints are available
            await RequireEndpointsAsync().ConfigureAwait(false);

            // get a connection
            ApplicationConfiguration config = ClientFixture.Config;
            ITransportWaitingConnection connection;
            using (var cancellationTokenSource = new CancellationTokenSource(MaxTimeout))
            {
                connection = await ClientFixture
                    .ReverseConnectManager.WaitForConnectionAsync(
                        m_endpointUrl,
                        null,
                        cancellationTokenSource.Token)
                    .ConfigureAwait(false);
                Assert.NotNull(connection, "Failed to get connection.");
            }

            // select the secure endpoint
            var endpointConfiguration = EndpointConfiguration.Create(config);
            EndpointDescription selectedEndpoint = ClientFixture.SelectEndpoint(
                config,
                Endpoints,
                m_endpointUrl,
                securityPolicy);
            Assert.NotNull(selectedEndpoint);
            var endpoint = new ConfiguredEndpoint(null, selectedEndpoint, endpointConfiguration);
            Assert.NotNull(endpoint);

            // connect
            ISession session = await sessionFactory.Create(telemetry)
                .CreateAsync(
                    config,
                    connection,
                    endpoint,
                    false,
                    false,
                    "Reverse Connect Client",
                    MaxTimeout,
                    new UserIdentity(),
                    default)
                .ConfigureAwait(false);
            Assert.NotNull(session);

            // default request header
            var requestHeader = new RequestHeader
            {
                Timestamp = DateTime.UtcNow,
                TimeoutHint = MaxTimeout
            };

            // Browse
            var clientTestServices = new ClientTestServices(session, telemetry);
            ReferenceDescriptionCollection referenceDescriptions = await CommonTestWorkers
                .BrowseFullAddressSpaceWorkerAsync(
                    clientTestServices,
                    requestHeader)
                .ConfigureAwait(false);
            Assert.NotNull(referenceDescriptions);

            // close session
            StatusCode result = await session.CloseAsync().ConfigureAwait(false);
            Assert.NotNull(result);
            session.Dispose();
        }

        [Theory]
        [Order(301)]
        public async Task ReverseConnect2Async(
            bool updateBeforeConnect,
            bool checkDomain,
            TelemetryParameterizable<ISessionFactory> sessionFactory)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            const string securityPolicy = SecurityPolicies.Basic256Sha256;

            // ensure endpoints are available
            await RequireEndpointsAsync().ConfigureAwait(false);

            // get a connection
            ApplicationConfiguration config = ClientFixture.Config;

            // select the secure endpoint
            var endpointConfiguration = EndpointConfiguration.Create(config);
            EndpointDescription selectedEndpoint = ClientFixture.SelectEndpoint(
                config,
                Endpoints,
                m_endpointUrl,
                securityPolicy);
            Assert.NotNull(selectedEndpoint);
            var endpoint = new ConfiguredEndpoint(null, selectedEndpoint, endpointConfiguration);
            Assert.NotNull(endpoint);

            // connect
            ISession session = await sessionFactory.Create(telemetry)
                .CreateAsync(
                    config,
                    ClientFixture.ReverseConnectManager,
                    endpoint,
                    updateBeforeConnect,
                    checkDomain,
                    "Reverse Connect Client",
                    MaxTimeout,
                    new UserIdentity(),
                    default)
                .ConfigureAwait(false);

            Assert.NotNull(session);

            // header
            var requestHeader = new RequestHeader
            {
                Timestamp = DateTime.UtcNow,
                TimeoutHint = MaxTimeout
            };

            // Browse
            var clientTestServices = new ClientTestServices(session, telemetry);
            ReferenceDescriptionCollection referenceDescriptions = await CommonTestWorkers
                .BrowseFullAddressSpaceWorkerAsync(
                    clientTestServices,
                    requestHeader)
                .ConfigureAwait(false);
            Assert.NotNull(referenceDescriptions);

            // close session
            StatusCode result = await session.CloseAsync().ConfigureAwait(false);
            Assert.NotNull(result);
            session.Dispose();
        }

        private async Task RequireEndpointsAsync()
        {
            await m_requiredLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (Endpoints == null)
                {
                    await GetEndpointsInternalAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                m_requiredLock.Release();
            }
        }

        private readonly SemaphoreSlim m_requiredLock = new(1);
    }
}
