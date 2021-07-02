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
using Opc.Ua.Server;
using Opc.Ua.Server.Tests;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// Test Client Services.
    /// </summary>
    [TestFixture, Category("Client")]
    [SetCulture("en-us"), SetUICulture("en-us")]
    [NonParallelizable]
    [MemoryDiagnoser]
    [DisassemblyDiagnoser]
    public class ClientTest
    {
        const int MaxReferences = 100;
        ServerFixture<ReferenceServer> m_serverFixture;
        ClientFixture m_clientFixture;
        ReferenceServer m_server;
        EndpointDescriptionCollection m_endpoints;
        ReferenceDescriptionCollection m_referenceDescriptions;
        Session m_session;
        OperationLimits m_operationLimits;
        Uri m_url;

        #region DataPointSources
        [DatapointSource]
        public static string[] Policies = SecurityPolicies.GetDisplayNames()
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
            // start Ref server
            m_serverFixture = new ServerFixture<ReferenceServer>();
            m_clientFixture = new ClientFixture();
            m_serverFixture.AutoAccept = true;
            m_serverFixture.OperationLimits = true;
            if (writer != null)
            {
                m_serverFixture.TraceMasks = Utils.TraceMasks.Error;
            }
            m_server = await m_serverFixture.StartAsync(writer ?? TestContext.Out, true).ConfigureAwait(false);
            await m_clientFixture.LoadClientConfiguration();
            m_url = new Uri("opc.tcp://localhost:" + m_serverFixture.Port.ToString());
            m_session = await m_clientFixture.ConnectAsync(m_url, SecurityPolicies.Basic256Sha256);
        }

        /// <summary>
        /// Tear down the Server and the Client.
        /// </summary>
        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
            m_session.Close();
            m_session.Dispose();
            m_session = null;
            await m_serverFixture.StopAsync();
            await Task.Delay(1000);
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
            Console.WriteLine("GlobalCleanup: Disconnect");
            m_session.Close();
            m_session.Dispose();
            m_session = null;
            Console.WriteLine("GlobalCleanup: Stop Server");
            OneTimeTearDownAsync().GetAwaiter().GetResult();
            Console.WriteLine("GlobalCleanup: Done");
        }
        #endregion

        #region Test Methods
        [Test, Order(100)]
        [Benchmark]
        public async Task GetEndpoints()
        {
            var endpointConfiguration = EndpointConfiguration.Create();
            endpointConfiguration.OperationTimeout = 1000;

            using (var client = DiscoveryClient.Create(m_url, endpointConfiguration))
            {
                m_endpoints = await client.GetEndpointsAsync(null);
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
            var session = await m_clientFixture.ConnectAsync(m_url, securityPolicy, m_endpoints);
            Assert.NotNull(session);
            var result = session.Close();
            Assert.NotNull(result);
            session.Dispose();
        }

        [Test, Order(210)]
        public async Task ConnectAndReconnectAsync()
        {
            const int Timeout = 10000;
            var session = await m_clientFixture.ConnectAsync(m_url, SecurityPolicies.Basic256Sha256, m_endpoints).ConfigureAwait(false);
            Assert.NotNull(session);

            ManualResetEvent quitEvent = new ManualResetEvent(false);
            var reconnectHandler = new SessionReconnectHandler();
            reconnectHandler.BeginReconnect(session, Timeout/5,
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
                MaxNodesPerRead = (uint)m_session.ReadValue(VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerRead).Value,
                MaxNodesPerHistoryReadData = (uint)m_session.ReadValue(VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerHistoryReadData).Value,
                MaxNodesPerHistoryReadEvents = (uint)m_session.ReadValue(VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerHistoryReadEvents).Value,
                MaxNodesPerWrite = (uint)m_session.ReadValue(VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerWrite).Value,
                MaxNodesPerHistoryUpdateData = (uint)m_session.ReadValue(VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerHistoryUpdateData).Value,
                MaxNodesPerHistoryUpdateEvents = (uint)m_session.ReadValue(VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerHistoryUpdateEvents).Value,
                MaxNodesPerBrowse = (uint)m_session.ReadValue(VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerBrowse).Value,
                MaxMonitoredItemsPerCall = (uint)m_session.ReadValue(VariableIds.Server_ServerCapabilities_OperationLimits_MaxMonitoredItemsPerCall).Value,
                MaxNodesPerNodeManagement = (uint)m_session.ReadValue(VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerNodeManagement).Value,
                MaxNodesPerRegisterNodes = (uint)m_session.ReadValue(VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerRegisterNodes).Value,
                MaxNodesPerTranslateBrowsePathsToNodeIds = (uint)m_session.ReadValue(VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerTranslateBrowsePathsToNodeIds).Value,
                MaxNodesPerMethodCall = (uint)m_session.ReadValue(VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerMethodCall).Value
            };

            m_operationLimits = operationLimits;
        }

        [Theory, Order(400)]
        public async Task BrowseFullAddressSpace(string securityPolicy)
        {
            if (m_operationLimits == null) { OperationLimits(); }

            var requestHeader = new RequestHeader();
            requestHeader.Timestamp = DateTime.UtcNow;
            requestHeader.TimeoutHint = 10000;

            // Session
            Session session;
            if (securityPolicy != null)
            {
                session = await m_clientFixture.ConnectAsync(m_url, securityPolicy, m_endpoints);
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
        public void Subscription()
        {
            var requestHeader = new RequestHeader();
            requestHeader.Timestamp = DateTime.UtcNow;
            requestHeader.TimeoutHint = 10000;

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

            while (nodesToBrowse.Count > 0)
            {
                var nextNodesToBrowse = new ExpandedNodeIdCollection();
                foreach (var node in nodesToBrowse)
                {
                    try
                    {
                        var organizers = m_session.NodeCache.FindReferences(
                            node,
                            ReferenceTypeIds.Organizes,
                            false,
                            true);
                        var components = m_session.NodeCache.FindReferences(
                            node,
                            ReferenceTypeIds.HasComponent,
                            false,
                            false);
                        var properties = m_session.NodeCache.FindReferences(
                            node,
                            ReferenceTypeIds.HasProperty,
                            false,
                            false);
                        nextNodesToBrowse.AddRange(organizers
                            .Where(n => n is ObjectNode)
                            .Select(n => n.NodeId).ToList());
                        nextNodesToBrowse.AddRange(components
                            .Where(n => n is ObjectNode)
                            .Select(n => n.NodeId).ToList());
                        result.AddRange(organizers.Where(n => n is VariableNode));
                        result.AddRange(components.Where(n => n is VariableNode));
                        result.AddRange(properties.Where(n => n is VariableNode));
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
                await BrowseFullAddressSpace(null);
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
                await BrowseFullAddressSpace(null);
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
            var sre = Assert.ThrowsAsync<ServiceResultException>(async () => { var t = await m_session.LoadDataTypeSystem(ObjectIds.ObjectAttributes_Encoding_DefaultJson); });
            Assert.AreEqual(StatusCodes.BadNodeIdInvalid, sre.StatusCode);
            var typeSystem = await m_session.LoadDataTypeSystem();
            Assert.NotNull(typeSystem);
            typeSystem = await m_session.LoadDataTypeSystem(ObjectIds.OPCBinarySchema_TypeSystem);
            Assert.NotNull(typeSystem);
            typeSystem = await m_session.LoadDataTypeSystem(ObjectIds.XmlSchema_TypeSystem);
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
        #endregion
    }
}
