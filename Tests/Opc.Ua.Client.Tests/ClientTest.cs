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
using BenchmarkDotNet.Attributes;
using NUnit.Framework;
using Opc.Ua.Configuration;
using Opc.Ua.Server.Tests;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// Client tests.
    /// </summary>
    [TestFixture, Category("Client")]
    [SetCulture("en-us"), SetUICulture("en-us")]
    [TestFixtureSource(nameof(FixtureArgs))]
    [NonParallelizable]
    [MemoryDiagnoser]
    [DisassemblyDiagnoser]
    public class ClientTest
    {
        static object[] FixtureArgs = {
            new object [] { Utils.UriSchemeOpcTcp},
            new object [] { Utils.UriSchemeHttps}
        };

        const int MaxReferences = 100;
        const int MaxTimeout = 10000;
        ServerFixture<ReferenceServer> m_serverFixture;
        ClientFixture m_clientFixture;
        ReferenceServer m_server;
        EndpointDescriptionCollection m_endpoints;
        ReferenceDescriptionCollection m_referenceDescriptions;
        Session m_session;
        OperationLimits m_operationLimits;
        string m_uriScheme;
        string m_pkiRoot;
        Uri m_url;

        public ClientTest(string uriScheme = Utils.UriSchemeOpcTcp)
        {
            m_uriScheme = uriScheme;
        }

        #region DataPointSources
        [DatapointSource]
        public static readonly string[] Policies = SecurityPolicies.GetDisplayNames()
            .Select(displayName => SecurityPolicies.GetUri(displayName)).ToArray();
        #endregion

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
                OperationLimits = true
            };
            if (writer != null)
            {
                m_serverFixture.TraceMasks = Utils.TraceMasks.Error;
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

        #region Benchmark Setup
        /// <summary>
        /// Global Setup for benchmarks.
        /// </summary>
        [GlobalSetup]
        public void GlobalSetup()
        {
            Console.WriteLine("GlobalSetup: Start Server");
            OneTimeSetUpAsync(Console.Out).GetAwaiter().GetResult();
            Console.WriteLine("GlobalSetup: Connecting");
            m_session = m_clientFixture.ConnectAsync(m_url, SecurityPolicy).GetAwaiter().GetResult();
            Console.WriteLine("GlobalSetup: Ready");
        }

        /// <summary>
        /// Global cleanup for benchmarks.
        /// </summary>
        [GlobalCleanup]
        public void GlobalCleanup()
        {
            Console.WriteLine("GlobalCleanup: Disconnect and Stop Server");
            OneTimeTearDownAsync().GetAwaiter().GetResult();
            Console.WriteLine("GlobalCleanup: Done");
        }
        #endregion

        #region Test Methods
        [Test, Order(100)]
        public async Task GetEndpoints()
        {
            var endpointConfiguration = EndpointConfiguration.Create();
            endpointConfiguration.OperationTimeout = 10000;

            using (var client = DiscoveryClient.Create(m_url, endpointConfiguration))
            {
                m_endpoints = await client.GetEndpointsAsync(null).ConfigureAwait(false);
            }
        }

        [Test, Order(110)]
        public async Task InvalidConfiguration()
        {
            var applicationInstance = new ApplicationInstance() {
                ApplicationName = m_clientFixture.Config.ApplicationName
            };
            Assert.NotNull(applicationInstance);
            ApplicationConfiguration config = await applicationInstance.Build(m_clientFixture.Config.ApplicationUri, m_clientFixture.Config.ProductUri)
                .AsClient()
                .AddSecurityConfiguration(m_clientFixture.Config.SecurityConfiguration.ApplicationCertificate.SubjectName)
                .Create().ConfigureAwait(false);
        }

        [Theory, Order(200)]
        public async Task Connect(string securityPolicy)
        {
            var session = await m_clientFixture.ConnectAsync(m_url, securityPolicy, m_endpoints).ConfigureAwait(false);
            Assert.NotNull(session);
            var result = session.Close();
            Assert.NotNull(result);
            session.Dispose();
        }

        [Test, Order(210)]
        public async Task ConnectAndReconnectAsync()
        {
            const int Timeout = MaxTimeout;
            var session = await m_clientFixture.ConnectAsync(m_url, SecurityPolicies.Basic256Sha256, m_endpoints).ConfigureAwait(false);
            Assert.NotNull(session);

            ManualResetEvent quitEvent = new ManualResetEvent(false);
            var reconnectHandler = new SessionReconnectHandler();
            reconnectHandler.BeginReconnect(session, Timeout / 5,
                (object sender, EventArgs e) => {
                    // ignore callbacks from discarded objects.
                    if (!Object.ReferenceEquals(sender, reconnectHandler))
                    {
                        return;
                    }

                    session = reconnectHandler.Session;
                    reconnectHandler.Dispose();
                    quitEvent.Set();
                });

            var timeout = quitEvent.WaitOne(Timeout);
            Assert.True(timeout);

            var result = session.Close();
            Assert.NotNull(result);
            session.Dispose();
        }

        [Test, Order(300)]
        public void OperationLimits()
        {
            var operationLimits = new OperationLimits() {
                MaxNodesPerRead = GetOperationLimitValue(VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerRead),
                MaxNodesPerHistoryReadData = GetOperationLimitValue(VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerHistoryReadData),
                MaxNodesPerHistoryReadEvents = GetOperationLimitValue(VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerHistoryReadEvents),
                MaxNodesPerWrite = GetOperationLimitValue(VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerWrite),
                MaxNodesPerHistoryUpdateData = GetOperationLimitValue(VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerHistoryUpdateData),
                MaxNodesPerHistoryUpdateEvents = GetOperationLimitValue(VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerHistoryUpdateEvents),
                MaxNodesPerBrowse = GetOperationLimitValue(VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerBrowse),
                MaxMonitoredItemsPerCall = GetOperationLimitValue(VariableIds.Server_ServerCapabilities_OperationLimits_MaxMonitoredItemsPerCall),
                MaxNodesPerNodeManagement = GetOperationLimitValue(VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerNodeManagement),
                MaxNodesPerRegisterNodes = GetOperationLimitValue(VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerRegisterNodes),
                MaxNodesPerTranslateBrowsePathsToNodeIds = GetOperationLimitValue(VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerTranslateBrowsePathsToNodeIds),
                MaxNodesPerMethodCall = GetOperationLimitValue(VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerMethodCall)
            };
            m_operationLimits = operationLimits;
        }

        [Test]
        public void ReadPublicProperties()
        {
            TestContext.Out.WriteLine("Identity         : {0}", m_session.Identity);
            TestContext.Out.WriteLine("IdentityHistory  : {0}", m_session.IdentityHistory);
            TestContext.Out.WriteLine("NamespaceUris    : {0}", m_session.NamespaceUris);
            TestContext.Out.WriteLine("ServerUris       : {0}", m_session.ServerUris);
            TestContext.Out.WriteLine("SystemContext    : {0}", m_session.SystemContext);
            TestContext.Out.WriteLine("Factory          : {0}", m_session.Factory);
            TestContext.Out.WriteLine("TypeTree         : {0}", m_session.TypeTree);
            TestContext.Out.WriteLine("FilterContext    : {0}", m_session.FilterContext);
            TestContext.Out.WriteLine("PreferredLocales : {0}", m_session.PreferredLocales);
            TestContext.Out.WriteLine("DataTypeSystem   : {0}", m_session.DataTypeSystem);
            TestContext.Out.WriteLine("Subscriptions    : {0}", m_session.Subscriptions);
            TestContext.Out.WriteLine("SubscriptionCount: {0}", m_session.SubscriptionCount);
            TestContext.Out.WriteLine("DefaultSubscription: {0}", m_session.DefaultSubscription);
            TestContext.Out.WriteLine("LastKeepAliveTime: {0}", m_session.LastKeepAliveTime);
            TestContext.Out.WriteLine("KeepAliveInterval: {0}", m_session.KeepAliveInterval);
            m_session.KeepAliveInterval += 1000;
            TestContext.Out.WriteLine("KeepAliveInterval: {0}", m_session.KeepAliveInterval);
            m_session.KeepAliveInterval -= 1000;
            TestContext.Out.WriteLine("KeepAliveInterval: {0}", m_session.KeepAliveInterval);
            TestContext.Out.WriteLine("KeepAliveStopped : {0}", m_session.KeepAliveStopped);
            TestContext.Out.WriteLine("OutstandingRequestCount : {0}", m_session.OutstandingRequestCount);
            TestContext.Out.WriteLine("DefunctRequestCount     : {0}", m_session.DefunctRequestCount);
            TestContext.Out.WriteLine("GoodPublishRequestCount : {0}", m_session.GoodPublishRequestCount);
        }

        [Test]
        public void ReadValues()
        {
            // Test ReadValue
            _ = m_session.ReadValue(VariableIds.Server_ServerRedundancy_RedundancySupport, typeof(Int32));
            _ = m_session.ReadValue(VariableIds.Server_ServerStatus, typeof(ServerStatusDataType));
            var sre = Assert.Throws<ServiceResultException>(() => m_session.ReadValue(VariableIds.Server_ServerStatus, typeof(ServiceHost)));
            Assert.AreEqual(StatusCodes.BadTypeMismatch, sre.StatusCode);

            // change locale
            var locale = new StringCollection() { "de-de", "en-us" };
            m_session.ChangePreferredLocales(locale);
        }

        [Test]
        public void ReadDataTypeDefinition()
        {
            // Test Read a DataType Node
            var node = m_session.ReadNode(DataTypeIds.ProgramDiagnosticDataType);
            Assert.NotNull(node);
            var dataTypeNode = (DataTypeNode)node;
            Assert.NotNull(dataTypeNode);
            var dataTypeDefinition = dataTypeNode.DataTypeDefinition;
            Assert.NotNull(dataTypeDefinition);
            Assert.True(dataTypeDefinition is ExtensionObject);
            Assert.NotNull(dataTypeDefinition.Body);
            Assert.True(dataTypeDefinition.Body is StructureDefinition);
            StructureDefinition structureDefinition = dataTypeDefinition.Body as StructureDefinition;
            Assert.AreEqual(ObjectIds.ProgramDiagnosticDataType_Encoding_DefaultBinary, structureDefinition.DefaultEncodingId);
        }

        [Theory, Order(400)]
        public async Task BrowseFullAddressSpace(string securityPolicy)
        {
            if (m_operationLimits == null) { OperationLimits(); }

            var requestHeader = new RequestHeader();
            requestHeader.Timestamp = DateTime.UtcNow;
            requestHeader.TimeoutHint = MaxTimeout;

            // Session
            Session session;
            if (securityPolicy != null)
            {
                session = await m_clientFixture.ConnectAsync(m_url, securityPolicy, m_endpoints).ConfigureAwait(false);
            }
            else
            {
                session = m_session;
            }

            var clientTestServices = new ClientTestServices(session);
            m_referenceDescriptions = CommonTestWorkers.BrowseFullAddressSpaceWorker(clientTestServices, requestHeader, m_operationLimits);

            if (securityPolicy != null)
            {
                session.Close();
                session.Dispose();
            }
        }

        [Test, Order(410)]
        public async Task ReadDisplayNames()
        {
            if (m_referenceDescriptions == null) { await BrowseFullAddressSpace(null).ConfigureAwait(false); }
            var nodeIds = m_referenceDescriptions.Select(n => ExpandedNodeId.ToNodeId(n.NodeId, m_session.NamespaceUris)).ToList();
            if (m_operationLimits.MaxNodesPerRead > 0 &&
                nodeIds.Count > m_operationLimits.MaxNodesPerRead)
            {
                var sre = Assert.Throws<ServiceResultException>(() => m_session.ReadDisplayName(nodeIds, out var displayNames, out var errors));
                Assert.AreEqual(StatusCodes.BadTooManyOperations, sre.StatusCode);
                while (nodeIds.Count > 0)
                {
                    m_session.ReadDisplayName(nodeIds.Take((int)m_operationLimits.MaxNodesPerRead).ToArray(), out var displayNames, out var errors);
                    foreach (var name in displayNames)
                    {
                        TestContext.Out.WriteLine("{0}", name);
                    }
                    nodeIds = nodeIds.Skip((int)m_operationLimits.MaxNodesPerRead).ToList();
                }
            }
            else
            {
                m_session.ReadDisplayName(nodeIds, out var displayNames, out var errors);
                foreach (var name in displayNames)
                {
                    TestContext.Out.WriteLine("{0}", name);
                }
            }
        }

        [Test, Order(480)]
        public void Subscription()
        {
            var requestHeader = new RequestHeader();
            requestHeader.Timestamp = DateTime.UtcNow;
            requestHeader.TimeoutHint = MaxTimeout;

            var clientTestServices = new ClientTestServices(m_session);
            CommonTestWorkers.SubscriptionTest(clientTestServices, requestHeader);
        }

        /// <summary>
        /// Browse all variables in the objects folder.
        /// </summary>
        [Test, Order(500)]
        public void NodeCache_BrowseAllVariables()
        {
            var result = new List<INode>();
            var nodesToBrowse = new ExpandedNodeIdCollection {
                ObjectIds.ObjectsFolder
            };
            m_session.NodeCache.LoadUaDefinedTypes(m_session.SystemContext);
            while (nodesToBrowse.Count > 0)
            {
                var nextNodesToBrowse = new ExpandedNodeIdCollection();
                foreach (var node in nodesToBrowse)
                {
                    try
                    {
                        var organizers = m_session.NodeCache.FindReferences(
                            node,
                            ReferenceTypeIds.HierarchicalReferences,
                            false,
                            true);
                        var objectNodes = organizers.Where(n => n is ObjectNode);
                        nextNodesToBrowse.AddRange(objectNodes.Select(n => n.NodeId));
                        var variableNodes = organizers.Where(n => n is VariableNode);
                        nextNodesToBrowse.AddRange(variableNodes.Select(n => n.NodeId).ToList());
                        result.AddRange(variableNodes);
                    }
                    catch (ServiceResultException sre)
                    {
                        if (sre.StatusCode == StatusCodes.BadUserAccessDenied)
                        {
                            TestContext.Out.WriteLine($"Access denied: Skip node {node}.");
                        }
                    }
                }
                nodesToBrowse = nextNodesToBrowse;

                if (result.Count > MaxReferences)
                {
                    break;
                }
            }

            TestContext.Out.WriteLine("Browsed {0} variables", result.Count);
        }

        [Test, Order(500)]
        public async Task Read()
        {
            if (m_referenceDescriptions == null)
            {
                await BrowseFullAddressSpace(null).ConfigureAwait(false);
            }

            foreach (var reference in m_referenceDescriptions.Take(MaxReferences))
            {
                var nodeId = ExpandedNodeId.ToNodeId(reference.NodeId, m_session.NamespaceUris);
                var node = m_session.ReadNode(nodeId);
                Assert.NotNull(node);
                TestContext.Out.WriteLine("NodeId: {0} Node: {1}", nodeId, node);
                if (node is VariableNode)
                {
                    try
                    {
                        var value = m_session.ReadValue(nodeId);
                        Assert.NotNull(value);
                        TestContext.Out.WriteLine("-- Value {0} ", value);
                    }
                    catch (ServiceResultException sre)
                    {
                        TestContext.Out.WriteLine("-- Read Value {0} ", sre.Message);
                    }
                }
            }
        }

        [Test, Order(600)]
        public async Task NodeCache_Read()
        {
            if (m_referenceDescriptions == null)
            {
                await BrowseFullAddressSpace(null).ConfigureAwait(false);
            }

            foreach (var reference in m_referenceDescriptions.Take(MaxReferences))
            {
                var nodeId = ExpandedNodeId.ToNodeId(reference.NodeId, m_session.NamespaceUris);
                var node = m_session.NodeCache.Find(reference.NodeId);
                TestContext.Out.WriteLine("NodeId: {0} Node: {1}", nodeId, node);
            }
        }

        [Test, Order(610)]
        public void FetchTypeTree()
        {
            m_session.FetchTypeTree(NodeId.ToExpandedNodeId(DataTypeIds.BaseDataType, m_session.NamespaceUris));
        }

        [Test, Order(620)]
        public void ReadAvailableEncodings()
        {
            var sre = Assert.Throws<ServiceResultException>(() => m_session.ReadAvailableEncodings(DataTypeIds.BaseDataType));
            Assert.AreEqual(StatusCodes.BadNodeIdInvalid, sre.StatusCode);
            var encoding = m_session.ReadAvailableEncodings(VariableIds.Server_ServerStatus_CurrentTime);
            Assert.NotNull(encoding);
            Assert.AreEqual(0, encoding.Count);
        }

        [Test, Order(700)]
        public async Task LoadDataTypeSystem()
        {
            var sre = Assert.ThrowsAsync<ServiceResultException>(async () => {
                var t = await m_session.LoadDataTypeSystem(ObjectIds.ObjectAttributes_Encoding_DefaultJson).ConfigureAwait(false);
            });
            Assert.AreEqual(StatusCodes.BadNodeIdInvalid, sre.StatusCode);
            var typeSystem = await m_session.LoadDataTypeSystem().ConfigureAwait(false);
            Assert.NotNull(typeSystem);
            typeSystem = await m_session.LoadDataTypeSystem(ObjectIds.OPCBinarySchema_TypeSystem).ConfigureAwait(false);
            Assert.NotNull(typeSystem);
            typeSystem = await m_session.LoadDataTypeSystem(ObjectIds.XmlSchema_TypeSystem).ConfigureAwait(false);
            Assert.NotNull(typeSystem);
        }
        #endregion

        #region Benchmarks
        /// <summary>
        /// Enumerator for security policies.
        /// </summary>
        public IEnumerable<string> BenchPolicies() { return Policies; }

        /// <summary>
        /// Helper variable for benchmark.
        /// </summary>
        [ParamsSource(nameof(BenchPolicies))]
        public string SecurityPolicy = SecurityPolicies.None;

        /// <summary>
        /// Benchmark wrapper for browse tests.
        /// </summary>
        [Benchmark]
        public async Task BrowseFullAddressSpaceBenchmark()
        {
            await BrowseFullAddressSpace(null).ConfigureAwait(false);
        }
        #endregion

        #region Private Methods

        private uint GetOperationLimitValue(NodeId nodeId)
        {
            try
            {
                return (uint)m_session.ReadValue(nodeId).Value;
            }
            catch (ServiceResultException sre)
            {
                if (sre.StatusCode == StatusCodes.BadNodeIdUnknown)
                {
                    return 0;
                }
                throw;
            }
        }
        #endregion
    }
}
