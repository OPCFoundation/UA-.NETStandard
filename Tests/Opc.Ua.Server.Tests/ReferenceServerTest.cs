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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using NUnit.Framework;
using Opc.Ua.Test;
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
        const double kMaxAge = 10000;
        const uint kTimeoutHint = 10000;
        ServerFixture<ReferenceServer> m_fixture;
        ReferenceServer m_server;
        RequestHeader m_requestHeader;
        OperationLimits m_operationLimits;
        ReferenceDescriptionCollection m_referenceDescriptions;
        RandomSource m_random;
        DataGenerator m_generator;


        #region Test Setup
        /// <summary>
        /// Set up a Server fixture.
        /// </summary>
        [OneTimeSetUp]
        public async Task OneTimeSetUp()
        {
            // start Ref server
            m_fixture = new ServerFixture<ReferenceServer>() {
                AllNodeManagers = true,
                OperationLimits = true
            };
            m_server = await m_fixture.StartAsync(TestContext.Out).ConfigureAwait(false);
        }

        /// <summary>
        /// Tear down the server fixture.
        /// </summary>
        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
            await m_fixture.StopAsync().ConfigureAwait(false);
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
            m_requestHeader.TimeoutHint = kTimeoutHint;
            m_random = new RandomSource(999);
            m_generator = new DataGenerator(m_random);
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
            m_fixture = new ServerFixture<ReferenceServer>() { AllNodeManagers = true };
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
            var response = m_server.Read(requestHeader, kMaxAge, TimestampsToReturn.Neither, readIdCollection, out var results, out var diagnosticInfos);
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
            var response = m_server.Read(requestHeader, kMaxAge, TimestampsToReturn.Neither, nodesToRead,
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
                var response = m_server.Read(requestHeader, kMaxAge, TimestampsToReturn.Both, nodesToRead,
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
        /// Update static Nodes, read modify write.
        /// </summary>
        [Test, Order(350)]
        public void ReadWriteUpdateNodes()
        {
            // Nodes
            var namespaceUris = m_server.CurrentInstance.NamespaceUris;
            NodeId[] testSet = CommonTestWorkers.NodeIdTestSetStatic.Select(n => ExpandedNodeId.ToNodeId(n, namespaceUris)).ToArray();

            UpdateValues(testSet);
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

        /// <summary>
        /// Create a secondary Session.
        /// Create a subscription with a monitored item.
        /// Close session, but do not delete subscriptions.
        /// Transfer subscription from closed session to the other.
        /// </summary>
        [Theory]
        public void TransferSubscriptionSessionClosed(bool sendInitialData, bool useSecurity)
        {
            var serverTestServices = new ServerTestServices(m_server);
            // save old security context, test fixture can only work with one session
            var securityContext = SecureChannelContext.Current;
            try
            {
                RequestHeader transferRequestHeader = m_server.CreateAndActivateSession("ClosedSession", useSecurity);
                var transferSecurityContext = SecureChannelContext.Current;
                var namespaceUris = m_server.CurrentInstance.NamespaceUris;
                NodeId[] testSet = CommonTestWorkers.NodeIdTestSetStatic.Select(n => ExpandedNodeId.ToNodeId(n, namespaceUris)).ToArray();
                transferRequestHeader.Timestamp = DateTime.UtcNow;
                CommonTestWorkers.CreateSubscriptionForTransfer(serverTestServices, transferRequestHeader,
                    testSet, out var subscriptionIds);

                transferRequestHeader.Timestamp = DateTime.UtcNow;
                m_server.CloseSession(transferRequestHeader, false);

                //restore security context, transfer abandoned subscription
                SecureChannelContext.Current = securityContext;
                CommonTestWorkers.TransferSubscriptionTest(serverTestServices, m_requestHeader, subscriptionIds, sendInitialData, !useSecurity);

                if (useSecurity)
                {
                    // subscription was deleted, expect 'BadNoSubscription'
                    var sre = Assert.Throws<ServiceResultException>(() => {
                        m_requestHeader.Timestamp = DateTime.UtcNow;
                        CommonTestWorkers.VerifySubscriptionTransferred(serverTestServices, m_requestHeader, subscriptionIds, true);
                    });
                    Assert.AreEqual(StatusCodes.BadNoSubscription, sre.StatusCode);
                }
            }
            finally
            {
                //restore security context, that close connection can work
                SecureChannelContext.Current = securityContext;
            }
        }

        /// <summary>
        /// Create a subscription with a monitored item.
        /// Create a secondary Session.
        /// Transfer subscription with a monitored item from one session to the other.
        /// </summary>
        [Theory]
        public void TransferSubscription(bool sendInitialData, bool useSecurity)
        {
            var serverTestServices = new ServerTestServices(m_server);
            // save old security context, test fixture can only work with one session
            var securityContext = SecureChannelContext.Current;
            try
            {
                var namespaceUris = m_server.CurrentInstance.NamespaceUris;
                NodeId[] testSet = CommonTestWorkers.NodeIdTestSetStatic.Select(n => ExpandedNodeId.ToNodeId(n, namespaceUris)).ToArray();
                CommonTestWorkers.CreateSubscriptionForTransfer(serverTestServices, m_requestHeader,
                    testSet, out var subscriptionIds);

                RequestHeader transferRequestHeader = m_server.CreateAndActivateSession("TransferSession", useSecurity);
                var transferSecurityContext = SecureChannelContext.Current;
                CommonTestWorkers.TransferSubscriptionTest(serverTestServices, transferRequestHeader, subscriptionIds, sendInitialData, !useSecurity);

                if (useSecurity)
                {
                    //restore security context
                    SecureChannelContext.Current = securityContext;
                    CommonTestWorkers.VerifySubscriptionTransferred(serverTestServices, m_requestHeader, subscriptionIds, true);
                }

                transferRequestHeader.Timestamp = DateTime.UtcNow;
                SecureChannelContext.Current = transferSecurityContext;
                m_server.CloseSession(transferRequestHeader);
            }
            finally
            {
                //restore security context, that close connection can work
                SecureChannelContext.Current = securityContext;
            }
        }


        /// <summary>
        /// Create a subscription with a monitored item.
        /// Call ResendData.
        /// Ensure only a single value per monitored item is returned after ResendData was called.
        /// </summary>
        [Theory]
        [NonParallelizable]
        public void ResendData(bool updateValues)
        {
            var serverTestServices = new ServerTestServices(m_server);
            // save old security context, test fixture can only work with one session
            var securityContext = SecureChannelContext.Current;
            try
            {
                var namespaceUris = m_server.CurrentInstance.NamespaceUris;
                NodeId[] testSet = CommonTestWorkers.NodeIdTestSetStatic.Select(n => ExpandedNodeId.ToNodeId(n, namespaceUris)).ToArray();

                //Re-use method CreateSubscriptionForTransfer to create a subscription
                CommonTestWorkers.CreateSubscriptionForTransfer(serverTestServices, m_requestHeader,
                    testSet, out var subscriptionIds);

                RequestHeader resendDataRequestHeader = m_server.CreateAndActivateSession("ResendData");
                var resendDataSecurityContext = SecureChannelContext.Current;

                SecureChannelContext.Current = securityContext;
                // After the ResendData call there will be data to publish again
                var nodesToCall = ResendDataCall(StatusCodes.Good, subscriptionIds);

                Thread.Sleep(1000);

                // Make sure publish queue becomes empty by consuming it 
                Assert.AreEqual(1, subscriptionIds.Count);

                // Issue a Publish request
                m_requestHeader.Timestamp = DateTime.UtcNow;
                var acknoledgements = new SubscriptionAcknowledgementCollection();
                var response = serverTestServices.Publish(m_requestHeader, acknoledgements,
                    out uint publishedId, out UInt32Collection availableSequenceNumbers,
                    out bool moreNotifications, out NotificationMessage notificationMessage,
                    out StatusCodeCollection _, out DiagnosticInfoCollection diagnosticInfos);

                Assert.AreEqual(StatusCodes.Good, response.ServiceResult.Code);
                ServerFixtureUtils.ValidateResponse(response);
                ServerFixtureUtils.ValidateDiagnosticInfos(diagnosticInfos, acknoledgements);
                Assert.AreEqual(subscriptionIds[0], publishedId);
                Assert.AreEqual(1, notificationMessage.NotificationData.Count);

                // Validate nothing to publish a few times
                const int timesToCallPublish = 3;
                for (int i = 0; i < timesToCallPublish; i++)
                {
                    m_requestHeader.Timestamp = DateTime.UtcNow;
                    response = serverTestServices.Publish(m_requestHeader, acknoledgements,
                        out publishedId, out availableSequenceNumbers,
                        out moreNotifications, out notificationMessage,
                        out StatusCodeCollection _, out diagnosticInfos);

                    Assert.AreEqual(StatusCodes.Good, response.ServiceResult.Code);
                    ServerFixtureUtils.ValidateResponse(response);
                    ServerFixtureUtils.ValidateDiagnosticInfos(diagnosticInfos, acknoledgements);
                    Assert.AreEqual(subscriptionIds[0], publishedId);
                    Assert.AreEqual(0, notificationMessage.NotificationData.Count);
                }

                // Validate ResendData method call returns error from different session contexts

                // call ResendData method from different session context
                SecureChannelContext.Current = resendDataSecurityContext;
                resendDataRequestHeader.Timestamp = DateTime.UtcNow;
                response = m_server.Call(resendDataRequestHeader,
                    nodesToCall,
                    out var results,
                    out diagnosticInfos);

                SecureChannelContext.Current = securityContext;

                Assert.AreEqual(StatusCodes.BadUserAccessDenied, results[0].StatusCode.Code);
                ServerFixtureUtils.ValidateResponse(response);

                // Still nothing to publish since previous ResendData call did not execute
                m_requestHeader.Timestamp = DateTime.UtcNow;
                response = serverTestServices.Publish(m_requestHeader, acknoledgements,
                    out publishedId, out availableSequenceNumbers,
                    out moreNotifications, out notificationMessage,
                    out StatusCodeCollection _, out diagnosticInfos);

                Assert.AreEqual(StatusCodes.Good, response.ServiceResult.Code);
                ServerFixtureUtils.ValidateResponse(response);
                ServerFixtureUtils.ValidateDiagnosticInfos(diagnosticInfos, acknoledgements);
                Assert.AreEqual(subscriptionIds[0], publishedId);
                Assert.AreEqual(0, notificationMessage.NotificationData.Count);

                if (updateValues)
                {
                    // fill queues, but only a single value per resend publish shall be returned
                    for (int i = 0; i < CommonTestWorkers.MonitoredItemsQueueSize; i++)
                    {
                        UpdateValues(testSet);
                    }
                }

                // call ResendData method from the same session context
                ResendDataCall(StatusCodes.Good, subscriptionIds);

                // Data should be available for publishing now
                m_requestHeader.Timestamp = DateTime.UtcNow;
                response = serverTestServices.Publish(m_requestHeader, acknoledgements,
                    out publishedId, out availableSequenceNumbers,
                    out moreNotifications, out notificationMessage,
                    out StatusCodeCollection _, out diagnosticInfos);

                Assert.AreEqual(StatusCodes.Good, response.ServiceResult.Code);
                ServerFixtureUtils.ValidateResponse(response);
                ServerFixtureUtils.ValidateDiagnosticInfos(diagnosticInfos, acknoledgements);
                Assert.AreEqual(subscriptionIds[0], publishedId);
                Assert.AreEqual(1, notificationMessage.NotificationData.Count);
                var items = notificationMessage.NotificationData.FirstOrDefault();
                Assert.IsTrue(items.Body is Opc.Ua.DataChangeNotification);
                var monitoredItemsCollection = ((Opc.Ua.DataChangeNotification)items.Body).MonitoredItems;
                Assert.AreEqual(testSet.Length, monitoredItemsCollection.Count);

                Thread.Sleep(1000);

                if (updateValues)
                {
                    // remaining queue Data should be sent in this publish
                    m_requestHeader.Timestamp = DateTime.UtcNow;
                    response = serverTestServices.Publish(m_requestHeader, acknoledgements,
                        out publishedId, out availableSequenceNumbers,
                        out moreNotifications, out notificationMessage,
                        out StatusCodeCollection _, out diagnosticInfos);

                    Assert.AreEqual(StatusCodes.Good, response.ServiceResult.Code);
                    ServerFixtureUtils.ValidateResponse(response);
                    ServerFixtureUtils.ValidateDiagnosticInfos(diagnosticInfos, acknoledgements);
                    Assert.AreEqual(subscriptionIds[0], publishedId);
                    Assert.AreEqual(1, notificationMessage.NotificationData.Count);
                    items = notificationMessage.NotificationData.FirstOrDefault();
                    Assert.IsTrue(items.Body is Opc.Ua.DataChangeNotification);
                    monitoredItemsCollection = ((Opc.Ua.DataChangeNotification)items.Body).MonitoredItems;
                    Assert.AreEqual(testSet.Length * (CommonTestWorkers.MonitoredItemsQueueSize - 1), monitoredItemsCollection.Count, testSet.Length);
                }

                // Call ResendData method with invalid subscription Id
                ResendDataCall(StatusCodes.BadSubscriptionIdInvalid, new UInt32Collection() { subscriptionIds.Last() + 20 });

                // Nothing to publish since previous ResendData call did not execute
                m_requestHeader.Timestamp = DateTime.UtcNow;
                response = serverTestServices.Publish(m_requestHeader, acknoledgements,
                    out publishedId, out availableSequenceNumbers,
                    out moreNotifications, out notificationMessage,
                    out StatusCodeCollection _, out diagnosticInfos);

                Assert.AreEqual(StatusCodes.Good, response.ServiceResult.Code);
                ServerFixtureUtils.ValidateResponse(response);
                ServerFixtureUtils.ValidateDiagnosticInfos(diagnosticInfos, acknoledgements);
                Assert.AreEqual(subscriptionIds[0], publishedId);
                Assert.AreEqual(0, notificationMessage.NotificationData.Count);

                resendDataRequestHeader.Timestamp = DateTime.UtcNow;
                SecureChannelContext.Current = resendDataSecurityContext;
                m_server.CloseSession(resendDataRequestHeader);
            }
            finally
            {
                //restore security context, that close connection can work
                SecureChannelContext.Current = securityContext;
            }
        }
        #endregion

        #region Private Methods
        private CallMethodRequestCollection ResendDataCall(StatusCode expectedStatus, UInt32Collection subscriptionIds)
        {
            // Find the ResendData method
            MethodState methodStateInstance = (MethodState)m_server.CurrentInstance.
               DiagnosticsNodeManager.FindPredefinedNode(MethodIds.Server_ResendData, typeof(MethodState));
            var nodesToCall = new CallMethodRequestCollection();
            foreach (var subscriptionId in subscriptionIds)
            {
                nodesToCall.Add(new CallMethodRequest() {
                    ObjectId = ObjectIds.Server,
                    MethodId = MethodIds.Server_ResendData,
                    InputArguments = new VariantCollection() { new Variant(subscriptionId) }
                });
            }

            //call ResendData method with subscription ids
            m_requestHeader.Timestamp = DateTime.UtcNow;
            var response = m_server.Call(m_requestHeader,
                nodesToCall,
                out var results,
                out var diagnosticInfos);

            Assert.AreEqual(expectedStatus, results[0].StatusCode.Code);
            ServerFixtureUtils.ValidateResponse(response);

            return nodesToCall;
        }

        /// <summary>
        /// Read Values of NodeIds, determine types, write back new random values.
        /// </summary>
        /// <param name="testSet">The nodeIds to modify.</param>
        private void UpdateValues(NodeId[] testSet)
        {
            // Read values
            var requestHeader = m_requestHeader;
            var nodesToRead = new ReadValueIdCollection();
            foreach (NodeId nodeId in testSet)
            {
                nodesToRead.Add(new ReadValueId() { NodeId = nodeId, AttributeId = Attributes.Value });
            }
            var response = m_server.Read(requestHeader, kMaxAge, TimestampsToReturn.Neither, nodesToRead,
                out var readDataValues, out var diagnosticInfos);

            ServerFixtureUtils.ValidateResponse(response);
            ServerFixtureUtils.ValidateDiagnosticInfos(diagnosticInfos, readDataValues);
            Assert.AreEqual(testSet.Length, readDataValues.Count);

            var modifiedValues = new DataValueCollection();
            foreach (var dataValue in readDataValues)
            {
                var typeInfo = TypeInfo.Construct(dataValue.Value);
                Assert.IsNotNull(typeInfo);
                var value = m_generator.GetRandom(typeInfo.BuiltInType);
                modifiedValues.Add(new DataValue() { WrappedValue = new Variant(value) });
            }

            int ii = 0;
            var nodesToWrite = new WriteValueCollection();
            foreach (NodeId nodeId in testSet)
            {
                nodesToWrite.Add(new WriteValue() { NodeId = nodeId, AttributeId = Attributes.Value, Value = modifiedValues[ii] });
                ii++;
            }

            // Write Nodes
            requestHeader.Timestamp = DateTime.UtcNow;
            response = m_server.Write(requestHeader, nodesToWrite,
                out var writeDataValues, out diagnosticInfos);
            ServerFixtureUtils.ValidateResponse(response);
            ServerFixtureUtils.ValidateDiagnosticInfos(diagnosticInfos, writeDataValues);
        }
        #endregion
    }
}
