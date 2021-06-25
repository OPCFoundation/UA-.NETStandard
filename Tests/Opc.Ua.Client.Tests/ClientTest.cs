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
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using NUnit.Framework;
using Opc.Ua.Server.Tests;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// Test Client Services.
    /// </summary>
    [TestFixture, Category("Client")]
    [SetCulture("en-us"), SetUICulture("en-us")]
    [Parallelizable]
    [MemoryDiagnoser]
    [DisassemblyDiagnoser]
    public class ClientTest
    {
        ServerFixture<ReferenceServer> m_serverFixture;
        ClientFixture m_clientFixture;
        ReferenceServer m_server;
        EndpointDescriptionCollection m_endpoints;
        ReferenceDescriptionCollection m_referenceDescriptions;
        Session m_session;
        OperationLimits m_operationLimits;
        string m_url;

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
            m_url = "opc.tcp://localhost:" + m_serverFixture.Port.ToString();
        }

        /// <summary>
        /// Tear down the Server and the Client.
        /// </summary>
        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
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
            m_session = m_clientFixture.ConnectAsync(GetEndpointAsync(m_url, SecurityPolicy).GetAwaiter().GetResult()).GetAwaiter().GetResult();
            Console.WriteLine("GlobalSetup: Ready");
        }

        /// <summary>
        /// Global cleanup for benchmarks.
        /// </summary>
        [GlobalCleanup]
        public void GlobalCleanup()
        {
            Console.WriteLine("GlobalCleanup: Disconnect");
            m_clientFixture.Disconnect();
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

            using (var client = DiscoveryClient.Create(new Uri(m_url), endpointConfiguration))
            {
                m_endpoints = await client.GetEndpointsAsync(null);
            }
        }

        [Theory, Order(200)]
        public async Task Connect(string securityPolicy)
        {
            await m_clientFixture.ConnectAsync(await GetEndpointAsync(m_url, securityPolicy));
            m_clientFixture.Disconnect();
        }

        [Test, Order(300)]
        public async Task OperationLimits()
        {
            m_session = await m_clientFixture.ConnectAsync(await GetEndpointAsync(m_url, SecurityPolicies.Basic256Sha256));

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

            m_clientFixture.Disconnect();
        }

        [Theory, Order(400)]
        public async Task BrowseFullAddressSpace(string securityPolicy)
        {
            var requestHeader = new RequestHeader();
            requestHeader.Timestamp = DateTime.UtcNow;
            requestHeader.TimeoutHint = 10000;

            // Session
            Session session;
            if (securityPolicy != null)
            {
                session = await m_clientFixture.ConnectAsync(await GetEndpointAsync(m_url, securityPolicy));
            }
            else
            {
                session = m_session;
            }

            var clientTestServices = new ClientTestServices(session);
            m_referenceDescriptions = CommonTestWorkers.BrowseFullAddressSpaceWorker(clientTestServices, requestHeader, m_operationLimits);

            if (securityPolicy != null)
            {
                m_clientFixture.Disconnect();
            }
        }

        /// <summary>
        /// Browse all variables in the objects folder.
        /// </summary>
        [Test, Order(500)]
        public void NodeCache_BrowseAllVariables()
        {
            m_session = m_clientFixture.ConnectAsync(GetEndpointAsync(m_url, SecurityPolicies.Basic256Sha256).GetAwaiter().GetResult()).GetAwaiter().GetResult();

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
                            ReferenceTypeIds.HierarchicalReferences,
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
            }

            TestContext.Out.WriteLine("Browsed {0} variables");

            m_clientFixture.Disconnect();
            m_session = null;
        }

        [Test, Order(500)]
        public async Task Read()
        {
            m_session = m_clientFixture.ConnectAsync(GetEndpointAsync(m_url, SecurityPolicies.Basic256Sha256).GetAwaiter().GetResult()).GetAwaiter().GetResult();

            if (m_referenceDescriptions == null)
            {
                await BrowseFullAddressSpace(null);
            }

            foreach (var reference in m_referenceDescriptions)
            {
                var nodeId = ExpandedNodeId.ToNodeId(reference.NodeId, m_session.NamespaceUris);
                var node = m_session.ReadNode(nodeId);
                TestContext.Out.WriteLine("NodeId: {0} Node: {1}", nodeId, node);
                if (reference.NodeClass == NodeClass.Variable)
                {
                    //var value = m_session.ReadValue(nodeId);
                }
            }
            m_clientFixture.Disconnect();
            m_session = null;
        }

        [Test, Order(600)]
        public async Task NodeCache_Read()
        {
            m_session = m_clientFixture.ConnectAsync(GetEndpointAsync(m_url, SecurityPolicies.Basic256Sha256).GetAwaiter().GetResult()).GetAwaiter().GetResult();

            if (m_referenceDescriptions == null)
            {
                await BrowseFullAddressSpace(null);
            }

            foreach (var reference in m_referenceDescriptions)
            {
                var nodeId = ExpandedNodeId.ToNodeId(reference.NodeId, m_session.NamespaceUris);
                var node = m_session.NodeCache.Find(reference.NodeId);
                TestContext.Out.WriteLine("NodeId: {0} Node: {1}", nodeId, node);
            }

            m_clientFixture.Disconnect();
            m_session = null;
        }

        [Test, Order(700)]
        public async Task LoadDataTypeSystem()
        {
            m_session = m_clientFixture.ConnectAsync(GetEndpointAsync(m_url, SecurityPolicy).GetAwaiter().GetResult()).GetAwaiter().GetResult();
            var typeSystem = await m_session.LoadDataTypeSystem();
            m_clientFixture.Disconnect();
            m_session = null;
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
        private async Task<ConfiguredEndpoint> GetEndpointAsync(string url, string securityPolicy)
        {
            if (m_endpoints == null)
            {
                await GetEndpoints().ConfigureAwait(false);
            }
            var endpointDescription = SelectEndpoint(new Uri(url), securityPolicy);
            EndpointConfiguration endpointConfiguration = EndpointConfiguration.Create(m_clientFixture.Config);
            return new ConfiguredEndpoint(null, endpointDescription, endpointConfiguration);
        }

        private EndpointDescription SelectEndpoint(Uri url, string securityPolicy)
        {
            EndpointDescription selectedEndpoint = null;

            // select the best endpoint to use based on the selected URL and the UseSecurity checkbox. 
            foreach (var endpoint in m_endpoints)
            {
                // check for a match on the URL scheme.
                if (endpoint.EndpointUrl.StartsWith(url.Scheme))
                {
                    // skip unsupported security policies
                    if (SecurityPolicies.GetDisplayName(endpoint.SecurityPolicyUri) == null)
                    {
                        continue;
                    }

                    // pick the first available endpoint by default.
                    if (selectedEndpoint == null &&
                        securityPolicy.Equals(endpoint.SecurityPolicyUri))
                    {
                        selectedEndpoint = endpoint;
                        continue;
                    }

                    if (selectedEndpoint?.SecurityMode < endpoint.SecurityMode &&
                        securityPolicy.Equals(endpoint.SecurityPolicyUri))
                    {
                        selectedEndpoint = endpoint;
                    }
                }
            }
            // return the selected endpoint.
            return selectedEndpoint;
        }
        #endregion
    }
}
