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
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using NUnit.Framework;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Test Reference Server.
    /// </summary>
    [TestFixture, Category("Server")]
    [SetCulture("en-us"), SetUICulture("en-us")]
    [Parallelizable]
    [MemoryDiagnoser]
    [DisassemblyDiagnoser]
    public class ReferenceServerTests
    {
        const double MaxAge = 10000;
        const uint TimeoutHint = 10000;
        ServerFixture<ReferenceServer> m_fixture;
        ReferenceServer m_server;
        RequestHeader m_requestHeader;
        OperationLimits m_operationLimits;
        ReferenceDescriptionCollection m_referenceDescriptions;

        #region Test Setup
        /// <summary>
        /// Set up a Server fixture.
        /// </summary>
        [OneTimeSetUp]
        public async Task OneTimeSetUp()
        {
            // start Ref server
            m_fixture = new ServerFixture<ReferenceServer>();
            m_fixture.OperationLimits = true;
            m_server = await m_fixture.StartAsync(TestContext.Out).ConfigureAwait(false);
        }

        /// <summary>
        /// Tear down the server fixture.
        /// </summary>
        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
            await m_fixture.StopAsync();
            Thread.Sleep(1000);
        }

        /// <summary>
        /// Create a session for a test.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            m_fixture.SetTraceOutput(TestContext.Out);
            m_requestHeader = m_server.CreateAndActivateSession(TestContext.CurrentContext.Test.Name);
            m_requestHeader.Timestamp = DateTime.UtcNow;
            m_requestHeader.TimeoutHint = TimeoutHint;
        }

        /// <summary>
        /// Tear down the test session.
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            m_requestHeader.Timestamp = DateTime.UtcNow;
            m_server.CloseSession(m_requestHeader);
            m_requestHeader = null;
        }
        #endregion

        #region Benchmark Setup
        /// <summary>
        /// Set up a Reference Server a session
        /// </summary>
        [GlobalSetup]
        public void GlobalSetup()
        {
            // start Ref server
            m_fixture = new ServerFixture<ReferenceServer>();
            m_server = m_fixture.StartAsync(null).GetAwaiter().GetResult();
            m_requestHeader = m_server.CreateAndActivateSession("Bench");
        }

        /// <summary>
        /// Tear down Server and the close the session.
        /// </summary>
        [GlobalCleanup]
        public void GlobalCleanup()
        {
            m_server.CloseSession(m_requestHeader);
            m_fixture.StopAsync().GetAwaiter().GetResult();
            Thread.Sleep(1000);
        }
        #endregion

        #region Test Methods
        /// <summary>
        /// Test for expected exceptions.
        /// </summary>
        [Test]
        public void ServiceResultException()
        {
            // test invalid timestamp
            m_requestHeader.Timestamp = DateTime.UtcNow - TimeSpan.FromDays(30);
            var sre = Assert.Throws<ServiceResultException>(() => m_server.CloseSession(m_requestHeader, false));
            Assert.AreEqual(StatusCodes.BadInvalidTimestamp, sre.StatusCode);
        }

        /// <summary>
        /// Get Endpoints.
        /// </summary>
        [Test]
        public void GetEndpoints()
        {
            var endpoints = m_server.GetEndpoints();
            Assert.NotNull(endpoints);
        }

        /// <summary>
        /// Get Operation limits.
        /// </summary>
        [Test, Order(100)]
        public void GetOperationLimits()
        {
            var readIdCollection = new ReadValueIdCollection() {
                new ReadValueId(){ AttributeId = Attributes.Value, NodeId = VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerRead },
                new ReadValueId(){ AttributeId = Attributes.Value, NodeId = VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerHistoryReadData },
                new ReadValueId(){ AttributeId = Attributes.Value, NodeId = VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerHistoryReadEvents },
                new ReadValueId(){ AttributeId = Attributes.Value, NodeId = VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerWrite },
                new ReadValueId(){ AttributeId = Attributes.Value, NodeId = VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerHistoryUpdateData },
                new ReadValueId(){ AttributeId = Attributes.Value, NodeId = VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerHistoryUpdateEvents },
                new ReadValueId(){ AttributeId = Attributes.Value, NodeId = VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerBrowse },
                new ReadValueId(){ AttributeId = Attributes.Value, NodeId = VariableIds.Server_ServerCapabilities_OperationLimits_MaxMonitoredItemsPerCall },
                new ReadValueId(){ AttributeId = Attributes.Value, NodeId = VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerNodeManagement },
                new ReadValueId(){ AttributeId = Attributes.Value, NodeId = VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerRegisterNodes },
                new ReadValueId(){ AttributeId = Attributes.Value, NodeId = VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerTranslateBrowsePathsToNodeIds },
                new ReadValueId(){ AttributeId = Attributes.Value, NodeId = VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerMethodCall }
            };

            var requestHeader = m_requestHeader;
            requestHeader.Timestamp = DateTime.UtcNow;
            var response = m_server.Read(requestHeader, MaxAge, TimestampsToReturn.Neither, readIdCollection, out var results, out var diagnosticInfos);
            ServerFixtureUtils.ValidateResponse(response);
            ServerFixtureUtils.ValidateDiagnosticInfos(diagnosticInfos, results);

            Assert.NotNull(results);
            Assert.AreEqual(readIdCollection.Count, results.Count);

            m_operationLimits = new OperationLimits() {
                MaxNodesPerRead = (uint)results[0].Value,
                MaxNodesPerHistoryReadData = (uint)results[1].Value,
                MaxNodesPerHistoryReadEvents = (uint)results[2].Value,
                MaxNodesPerWrite = (uint)results[3].Value,
                MaxNodesPerHistoryUpdateData = (uint)results[4].Value,
                MaxNodesPerHistoryUpdateEvents = (uint)results[5].Value,
                MaxNodesPerBrowse = (uint)results[6].Value,
                MaxMonitoredItemsPerCall = (uint)results[7].Value,
                MaxNodesPerNodeManagement = (uint)results[8].Value,
                MaxNodesPerRegisterNodes = (uint)results[9].Value,
                MaxNodesPerTranslateBrowsePathsToNodeIds = (uint)results[10].Value,
                MaxNodesPerMethodCall = (uint)results[11].Value
            };
        }

        /// <summary>
        /// Read node.
        /// </summary>
        [Test]
        [Benchmark]
        public void Read()
        {
            // Read
            var requestHeader = m_requestHeader;
            requestHeader.Timestamp = DateTime.UtcNow;
            var nodesToRead = new ReadValueIdCollection();
            var nodeId = new NodeId("Scalar_Simulation_Int32", 2);
            foreach (var attributeId in ServerFixtureUtils.AttributesIds.Keys)
            {
                nodesToRead.Add(new ReadValueId() { NodeId = nodeId, AttributeId = attributeId });
            }
            var response = m_server.Read(requestHeader, MaxAge, TimestampsToReturn.Neither, nodesToRead,
                out var dataValues, out var diagnosticInfos);
            ServerFixtureUtils.ValidateResponse(response);
            ServerFixtureUtils.ValidateDiagnosticInfos(diagnosticInfos, dataValues);
        }

        /// <summary>
        /// Read all nodes.
        /// </summary>
        [Test]
        public void ReadAllNodes()
        {
            var serverTestServices = new ServerTestServices(m_server);
            if (m_operationLimits == null)
            {
                GetOperationLimits();
            }
            if (m_referenceDescriptions == null)
            {
                m_referenceDescriptions = CommonTestWorkers.BrowseFullAddressSpaceWorker(serverTestServices, m_requestHeader, m_operationLimits);
            }

            // Read all variables
            var requestHeader = m_requestHeader;
            foreach (var reference in m_referenceDescriptions)
            {
                requestHeader.Timestamp = DateTime.UtcNow;
                var nodesToRead = new ReadValueIdCollection();
                var nodeId = ExpandedNodeId.ToNodeId(reference.NodeId, m_server.CurrentInstance.NamespaceUris);
                foreach (var attributeId in ServerFixtureUtils.AttributesIds.Keys)
                {
                    nodesToRead.Add(new ReadValueId() { NodeId = nodeId, AttributeId = attributeId });
                }
                TestContext.Out.WriteLine("NodeId {0} {1}", reference.NodeId, reference.BrowseName);
                var response = m_server.Read(requestHeader, MaxAge, TimestampsToReturn.Both, nodesToRead,
                    out var dataValues, out var diagnosticInfos);
                ServerFixtureUtils.ValidateResponse(response);
                ServerFixtureUtils.ValidateDiagnosticInfos(diagnosticInfos, dataValues);

                foreach (var dataValue in dataValues)
                {
                    TestContext.Out.WriteLine(" {0}", dataValue.ToString());
                }
            }
        }

        /// <summary>
        /// Write Node.
        /// </summary>
        [Test]
        [Benchmark]
        public void Write()
        {
            // Write
            var requestHeader = m_requestHeader;
            requestHeader.Timestamp = DateTime.UtcNow;
            var nodesToWrite = new WriteValueCollection();
            var nodeId = new NodeId("Scalar_Simulation_Int32", 2);
            nodesToWrite.Add(new WriteValue() { NodeId = nodeId, AttributeId = Attributes.Value, Value = new DataValue(1234) });
            var response = m_server.Write(requestHeader, nodesToWrite,
                out var dataValues, out var diagnosticInfos);
            ServerFixtureUtils.ValidateResponse(response);
            ServerFixtureUtils.ValidateDiagnosticInfos(diagnosticInfos, dataValues);
        }

        /// <summary>
        /// Browse full address space.
        /// </summary>
        [Test, Order(400)]
        [Benchmark]
        public void BrowseFullAddressSpace()
        {
            var serverTestServices = new ServerTestServices(m_server);
            if (m_operationLimits == null)
            {
                GetOperationLimits();
            }
            m_referenceDescriptions = CommonTestWorkers.BrowseFullAddressSpaceWorker(serverTestServices, m_requestHeader, m_operationLimits);
        }

        /// <summary>
        /// Translate references.
        /// </summary>
        [Test, Order(500)]
        [Benchmark]
        public void TranslateBrowsePath()
        {
            var serverTestServices = new ServerTestServices(m_server);
            if (m_operationLimits == null)
            {
                GetOperationLimits();
            }
            if (m_referenceDescriptions == null)
            {
                m_referenceDescriptions = CommonTestWorkers.BrowseFullAddressSpaceWorker(serverTestServices, m_requestHeader, m_operationLimits);
            }
            _ = CommonTestWorkers.TranslateBrowsePathWorker(serverTestServices, m_referenceDescriptions, m_requestHeader, m_operationLimits);
        }

        /// <summary>
        /// Create a subscription with a monitored item.
        /// Read a few notifications with Publish.
        /// Delete the monitored item and subscription.
        /// </summary>
        [Test]
        public void Subscription()
        {
            var serverTestServices = new ServerTestServices(m_server);
            CommonTestWorkers.SubscriptionTest(serverTestServices, m_requestHeader);

        }
        #endregion
    }
}
