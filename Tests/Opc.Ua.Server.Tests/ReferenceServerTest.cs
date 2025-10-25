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
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.Test;
using Opc.Ua.Tests;
using Quickstarts.ReferenceServer;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Test Reference Server.
    /// </summary>
    [TestFixture]
    [Category("Server")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    [MemoryDiagnoser]
    [DisassemblyDiagnoser]
    public class ReferenceServerTests
    {
        private const double kMaxAge = 10000;
        private const uint kTimeoutHint = 10000;
        private const uint kQueueSize = 5;
        private ITelemetryContext m_telemetry;
        private ServerFixture<ReferenceServer> m_fixture;
        private ReferenceServer m_server;
        private RequestHeader m_requestHeader;
        private OperationLimits m_operationLimits;
        private ReferenceDescriptionCollection m_referenceDescriptions;
        private RandomSource m_random;
        private DataGenerator m_generator;
        private bool m_sessionClosed;

        /// <summary>
        /// Set up a Server fixture.
        /// </summary>
        [OneTimeSetUp]
        public async Task OneTimeSetUpAsync()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            // start Ref server
            m_fixture = new ServerFixture<ReferenceServer>
            {
                AllNodeManagers = true,
                OperationLimits = true,
                DurableSubscriptionsEnabled = false,
                UseSamplingGroupsInReferenceNodeManager = false
            };
            m_server = await m_fixture.StartAsync().ConfigureAwait(false);
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
            m_requestHeader = m_server.CreateAndActivateSession(
                TestContext.CurrentContext.Test.Name);
            m_requestHeader.Timestamp = DateTime.UtcNow;
            m_requestHeader.TimeoutHint = kTimeoutHint;
            m_random = new RandomSource(999);
            m_generator = new DataGenerator(m_random, m_telemetry);
        }

        /// <summary>
        /// Tear down the test session.
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            if (!m_sessionClosed)
            {
                m_requestHeader.Timestamp = DateTime.UtcNow;
                m_server.CloseSession(m_requestHeader);
                m_requestHeader = null;
            }
        }

        /// <summary>
        /// Set up a Reference Server a session
        /// </summary>
        [GlobalSetup]
        public void GlobalSetup()
        {
            // start Ref server
            m_fixture = new ServerFixture<ReferenceServer> { AllNodeManagers = true };
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

        /// <summary>
        /// Test for expected exceptions.
        /// </summary>
        [Test]
        public void NoInvalidTimestampException()
        {
            // test that the server accepts an invalid timestamp
            m_requestHeader.Timestamp = DateTime.UtcNow - TimeSpan.FromDays(30);
            m_server.CloseSession(m_requestHeader, false);
            m_sessionClosed = true;
        }

        /// <summary>
        /// Get Endpoints.
        /// </summary>
        [Test]
        public void GetEndpoints()
        {
            EndpointDescriptionCollection endpoints = m_server.GetEndpoints();
            Assert.NotNull(endpoints);
        }

        /// <summary>
        /// Get Operation limits.
        /// </summary>
        [Test]
        [Order(100)]
        public void GetOperationLimits()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ILogger logger = telemetry.CreateLogger<ReferenceServerTests>();

            var readIdCollection = new ReadValueIdCollection
            {
                new ReadValueId
                {
                    AttributeId = Attributes.Value,
                    NodeId = VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerRead
                },
                new ReadValueId
                {
                    AttributeId = Attributes.Value,
                    NodeId = VariableIds
                        .Server_ServerCapabilities_OperationLimits_MaxNodesPerHistoryReadData
                },
                new ReadValueId
                {
                    AttributeId = Attributes.Value,
                    NodeId = VariableIds
                        .Server_ServerCapabilities_OperationLimits_MaxNodesPerHistoryReadEvents
                },
                new ReadValueId
                {
                    AttributeId = Attributes.Value,
                    NodeId = VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerWrite
                },
                new ReadValueId
                {
                    AttributeId = Attributes.Value,
                    NodeId = VariableIds
                        .Server_ServerCapabilities_OperationLimits_MaxNodesPerHistoryUpdateData
                },
                new ReadValueId
                {
                    AttributeId = Attributes.Value,
                    NodeId = VariableIds
                        .Server_ServerCapabilities_OperationLimits_MaxNodesPerHistoryUpdateEvents
                },
                new ReadValueId
                {
                    AttributeId = Attributes.Value,
                    NodeId = VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerBrowse
                },
                new ReadValueId
                {
                    AttributeId = Attributes.Value,
                    NodeId = VariableIds
                        .Server_ServerCapabilities_OperationLimits_MaxMonitoredItemsPerCall
                },
                new ReadValueId
                {
                    AttributeId = Attributes.Value,
                    NodeId = VariableIds
                        .Server_ServerCapabilities_OperationLimits_MaxNodesPerNodeManagement
                },
                new ReadValueId
                {
                    AttributeId = Attributes.Value,
                    NodeId = VariableIds
                        .Server_ServerCapabilities_OperationLimits_MaxNodesPerRegisterNodes
                },
                new ReadValueId
                {
                    AttributeId = Attributes.Value,
                    NodeId =
                        VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerTranslateBrowsePathsToNodeIds
                },
                new ReadValueId
                {
                    AttributeId = Attributes.Value,
                    NodeId = VariableIds
                        .Server_ServerCapabilities_OperationLimits_MaxNodesPerMethodCall
                }
            };

            RequestHeader requestHeader = m_requestHeader;
            requestHeader.Timestamp = DateTime.UtcNow;
            ResponseHeader response = m_server.Read(
                requestHeader,
                kMaxAge,
                TimestampsToReturn.Neither,
                readIdCollection,
                out DataValueCollection results,
                out DiagnosticInfoCollection diagnosticInfos);
            ServerFixtureUtils.ValidateResponse(response, results, readIdCollection);
            ServerFixtureUtils.ValidateDiagnosticInfos(
                diagnosticInfos,
                results,
                response.StringTable,
                logger);

            Assert.NotNull(results);
            Assert.AreEqual(readIdCollection.Count, results.Count);

            m_operationLimits = new OperationLimits
            {
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
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ILogger logger = telemetry.CreateLogger<ReferenceServerTests>();

            // Read
            RequestHeader requestHeader = m_requestHeader;
            requestHeader.Timestamp = DateTime.UtcNow;
            var nodesToRead = new ReadValueIdCollection();
            var nodeId = new NodeId("Scalar_Simulation_Int32", 2);
            foreach (uint attributeId in ServerFixtureUtils.AttributesIds.Keys)
            {
                nodesToRead.Add(new ReadValueId { NodeId = nodeId, AttributeId = attributeId });
            }
            ResponseHeader response = m_server.Read(
                requestHeader,
                kMaxAge,
                TimestampsToReturn.Neither,
                nodesToRead,
                out DataValueCollection dataValues,
                out DiagnosticInfoCollection diagnosticInfos);
            ServerFixtureUtils.ValidateResponse(response, dataValues, nodesToRead);
            ServerFixtureUtils.ValidateDiagnosticInfos(
                diagnosticInfos,
                dataValues,
                response.StringTable,
                logger);
        }

        /// <summary>
        /// Read all nodes.
        /// </summary>
        [Test]
        public void ReadAllNodes()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var serverTestServices = new ServerTestServices(m_server, telemetry);
            if (m_operationLimits == null)
            {
                GetOperationLimits();
            }
            m_referenceDescriptions ??= CommonTestWorkers.BrowseFullAddressSpaceWorker(
                serverTestServices,
                m_requestHeader,
                m_operationLimits);

            // Read all variables
            RequestHeader requestHeader = m_requestHeader;
            foreach (ReferenceDescription reference in m_referenceDescriptions)
            {
                requestHeader.Timestamp = DateTime.UtcNow;
                var nodesToRead = new ReadValueIdCollection();
                var nodeId = ExpandedNodeId.ToNodeId(
                    reference.NodeId,
                    m_server.CurrentInstance.NamespaceUris);
                foreach (uint attributeId in ServerFixtureUtils.AttributesIds.Keys)
                {
                    nodesToRead.Add(new ReadValueId { NodeId = nodeId, AttributeId = attributeId });
                }
                TestContext.Out.WriteLine("NodeId {0} {1}", reference.NodeId, reference.BrowseName);
                ResponseHeader response = m_server.Read(
                    requestHeader,
                    kMaxAge,
                    TimestampsToReturn.Both,
                    nodesToRead,
                    out DataValueCollection dataValues,
                    out DiagnosticInfoCollection diagnosticInfos);
                ServerFixtureUtils.ValidateResponse(response, dataValues, nodesToRead);
                ServerFixtureUtils.ValidateDiagnosticInfos(
                    diagnosticInfos,
                    dataValues,
                    response.StringTable,
                    serverTestServices.Logger);

                foreach (DataValue dataValue in dataValues)
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
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ILogger logger = telemetry.CreateLogger<ReferenceServerTests>();

            // Write
            RequestHeader requestHeader = m_requestHeader;
            requestHeader.Timestamp = DateTime.UtcNow;
            var nodesToWrite = new WriteValueCollection();
            var nodeId = new NodeId("Scalar_Simulation_Int32", 2);
            nodesToWrite.Add(
                new WriteValue
                {
                    NodeId = nodeId,
                    AttributeId = Attributes.Value,
                    Value = new DataValue(1234)
                });
            ResponseHeader response = m_server.Write(
                requestHeader,
                nodesToWrite,
                out StatusCodeCollection dataValues,
                out DiagnosticInfoCollection diagnosticInfos);
            ServerFixtureUtils.ValidateResponse(response, dataValues, nodesToWrite);
            ServerFixtureUtils.ValidateDiagnosticInfos(
                diagnosticInfos,
                dataValues,
                response.StringTable,
                logger);
        }

        /// <summary>
        /// Update static Nodes, read modify write.
        /// </summary>
        [Test]
        [Order(350)]
        public void ReadWriteUpdateNodes()
        {
            // Nodes
            NamespaceTable namespaceUris = m_server.CurrentInstance.NamespaceUris;
            NodeId[] testSet =
            [
                .. CommonTestWorkers.NodeIdTestSetStatic
                    .Select(n => ExpandedNodeId.ToNodeId(n, namespaceUris))
            ];

            UpdateValues(testSet);
        }

        /// <summary>
        /// Browse full address space.
        /// </summary>
        [Test]
        [Order(400)]
        [Benchmark]
        public void BrowseFullAddressSpace()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var serverTestServices = new ServerTestServices(m_server, telemetry);
            if (m_operationLimits == null)
            {
                GetOperationLimits();
            }
            m_referenceDescriptions = CommonTestWorkers.BrowseFullAddressSpaceWorker(
                serverTestServices,
                m_requestHeader,
                m_operationLimits);
        }

        /// <summary>
        /// Translate references.
        /// </summary>
        [Test]
        [Order(500)]
        [Benchmark]
        public void TranslateBrowsePath()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var serverTestServices = new ServerTestServices(m_server, telemetry);
            if (m_operationLimits == null)
            {
                GetOperationLimits();
            }
            m_referenceDescriptions ??= CommonTestWorkers.BrowseFullAddressSpaceWorker(
                serverTestServices,
                m_requestHeader,
                m_operationLimits);
            _ = CommonTestWorkers.TranslateBrowsePathWorker(
                serverTestServices,
                m_referenceDescriptions,
                m_requestHeader,
                m_operationLimits);
        }

        /// <summary>
        /// Create a subscription with a monitored item.
        /// Read a few notifications with Publish.
        /// Delete the monitored item and subscription.
        /// </summary>
        [Test]
        public void Subscription()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var serverTestServices = new ServerTestServices(m_server, telemetry);
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
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var serverTestServices = new ServerTestServices(m_server, telemetry);
            // save old security context, test fixture can only work with one session
            SecureChannelContext securityContext = SecureChannelContext.Current;
            try
            {
                RequestHeader transferRequestHeader = m_server.CreateAndActivateSession(
                    "ClosedSession",
                    useSecurity);
                SecureChannelContext transferSecurityContext = SecureChannelContext.Current;
                NamespaceTable namespaceUris = m_server.CurrentInstance.NamespaceUris;
                NodeId[] testSet =
                [
                    .. CommonTestWorkers.NodeIdTestSetStatic
                        .Select(n => ExpandedNodeId.ToNodeId(n, namespaceUris))
                ];
                transferRequestHeader.Timestamp = DateTime.UtcNow;
                UInt32Collection subscriptionIds = CommonTestWorkers.CreateSubscriptionForTransfer(
                    serverTestServices,
                    transferRequestHeader,
                    testSet,
                    kQueueSize,
                    -1);

                transferRequestHeader.Timestamp = DateTime.UtcNow;
                m_server.CloseSession(transferRequestHeader, false);

                //restore security context, transfer abandoned subscription
                SecureChannelContext.Current = securityContext;
                CommonTestWorkers.TransferSubscriptionTest(
                    serverTestServices,
                    m_requestHeader,
                    subscriptionIds,
                    sendInitialData,
                    !useSecurity);

                if (useSecurity)
                {
                    // subscription was deleted, expect 'BadNoSubscription'
                    ServiceResultException sre = NUnit.Framework.Assert
                        .Throws<ServiceResultException>(() =>
                            {
                                m_requestHeader.Timestamp = DateTime.UtcNow;
                                CommonTestWorkers.VerifySubscriptionTransferred(
                                    serverTestServices,
                                    m_requestHeader,
                                    subscriptionIds,
                                    true);
                            });
                    Assert.AreEqual(
                        (StatusCode)StatusCodes.BadNoSubscription,
                        (StatusCode)sre.StatusCode);
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
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var serverTestServices = new ServerTestServices(m_server, telemetry);
            // save old security context, test fixture can only work with one session
            SecureChannelContext securityContext = SecureChannelContext.Current;
            try
            {
                NamespaceTable namespaceUris = m_server.CurrentInstance.NamespaceUris;
                NodeId[] testSet =
                [
                    .. CommonTestWorkers.NodeIdTestSetStatic
                        .Select(n => ExpandedNodeId.ToNodeId(n, namespaceUris))
                ];
                UInt32Collection subscriptionIds = CommonTestWorkers.CreateSubscriptionForTransfer(
                    serverTestServices,
                    m_requestHeader,
                    testSet,
                    kQueueSize,
                    -1);

                RequestHeader transferRequestHeader = m_server.CreateAndActivateSession(
                    "TransferSession",
                    useSecurity);
                SecureChannelContext transferSecurityContext = SecureChannelContext.Current;
                CommonTestWorkers.TransferSubscriptionTest(
                    serverTestServices,
                    transferRequestHeader,
                    subscriptionIds,
                    sendInitialData,
                    !useSecurity);

                if (useSecurity)
                {
                    //restore security context
                    SecureChannelContext.Current = securityContext;
                    CommonTestWorkers.VerifySubscriptionTransferred(
                        serverTestServices,
                        m_requestHeader,
                        subscriptionIds,
                        true);
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
        [Test]
        [NonParallelizable]
        [TestCase(true, kQueueSize)]
        [TestCase(false, kQueueSize)]
        [TestCase(true, 0U)]
        [TestCase(false, 0U)]
        public void ResendData(bool updateValues, uint queueSize)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var serverTestServices = new ServerTestServices(m_server, telemetry);
            // save old security context, test fixture can only work with one session
            SecureChannelContext securityContext = SecureChannelContext.Current;
            try
            {
                NamespaceTable namespaceUris = m_server.CurrentInstance.NamespaceUris;
                NodeIdCollection testSetCollection = CommonTestWorkers
                    .NodeIdTestSetStatic.Select(n => ExpandedNodeId.ToNodeId(n, namespaceUris))
                    .ToArray();
                testSetCollection.AddRange(
                    CommonTestWorkers.NodeIdTestDataSetStatic
                        .Select(n => ExpandedNodeId.ToNodeId(n, namespaceUris)));
                NodeId[] testSet = [.. testSetCollection];

                //Re-use method CreateSubscriptionForTransfer to create a subscription
                UInt32Collection subscriptionIds = CommonTestWorkers.CreateSubscriptionForTransfer(
                    serverTestServices,
                    m_requestHeader,
                    testSet,
                    queueSize,
                    0);

                RequestHeader resendDataRequestHeader = m_server.CreateAndActivateSession(
                    "ResendData");
                SecureChannelContext resendDataSecurityContext = SecureChannelContext.Current;

                SecureChannelContext.Current = securityContext;
                // After the ResendData call there will be data to publish again
                CallMethodRequestCollection nodesToCall = ResendDataCall(
                    StatusCodes.Good,
                    subscriptionIds);

                Thread.Sleep(1000);

                // Make sure publish queue becomes empty by consuming it
                Assert.AreEqual(1, subscriptionIds.Count);

                // Issue a Publish request
                m_requestHeader.Timestamp = DateTime.UtcNow;
                var acknowledgements = new SubscriptionAcknowledgementCollection();
                ResponseHeader response = serverTestServices.Publish(
                    m_requestHeader,
                    acknowledgements,
                    out uint publishedId,
                    out UInt32Collection availableSequenceNumbers,
                    out bool moreNotifications,
                    out NotificationMessage notificationMessage,
                    out StatusCodeCollection _,
                    out DiagnosticInfoCollection diagnosticInfos);

                Assert.AreEqual((StatusCode)StatusCodes.Good, response.ServiceResult);
                ServerFixtureUtils.ValidateResponse(response);
                ServerFixtureUtils.ValidateDiagnosticInfos(
                    diagnosticInfos,
                    acknowledgements,
                    response.StringTable,
                    serverTestServices.Logger);
                Assert.AreEqual(subscriptionIds[0], publishedId);
                Assert.AreEqual(1, notificationMessage.NotificationData.Count);

                // Validate nothing to publish a few times
                const int timesToCallPublish = 3;
                for (int i = 0; i < timesToCallPublish; i++)
                {
                    m_requestHeader.Timestamp = DateTime.UtcNow;
                    response = serverTestServices.Publish(
                        m_requestHeader,
                        acknowledgements,
                        out publishedId,
                        out availableSequenceNumbers,
                        out moreNotifications,
                        out notificationMessage,
                        out StatusCodeCollection _,
                        out diagnosticInfos);

                    Assert.AreEqual((StatusCode)StatusCodes.Good, response.ServiceResult);
                    ServerFixtureUtils.ValidateResponse(response);
                    ServerFixtureUtils.ValidateDiagnosticInfos(
                        diagnosticInfos,
                        acknowledgements,
                        response.StringTable,
                        serverTestServices.Logger);
                    Assert.AreEqual(subscriptionIds[0], publishedId);
                    Assert.AreEqual(0, notificationMessage.NotificationData.Count);
                }

                // Validate ResendData method call returns error from different session contexts

                // call ResendData method from different session context
                SecureChannelContext.Current = resendDataSecurityContext;
                resendDataRequestHeader.Timestamp = DateTime.UtcNow;
                response = m_server.Call(
                    resendDataRequestHeader,
                    nodesToCall,
                    out CallMethodResultCollection results,
                    out diagnosticInfos);

                SecureChannelContext.Current = securityContext;

                Assert.AreEqual((StatusCode)StatusCodes.BadUserAccessDenied, results[0].StatusCode);
                ServerFixtureUtils.ValidateResponse(response, results, nodesToCall);
                ServerFixtureUtils.ValidateDiagnosticInfos(
                    diagnosticInfos,
                    nodesToCall,
                    response.StringTable,
                    serverTestServices.Logger);

                // Still nothing to publish since previous ResendData call did not execute
                m_requestHeader.Timestamp = DateTime.UtcNow;
                response = serverTestServices.Publish(
                    m_requestHeader,
                    acknowledgements,
                    out publishedId,
                    out availableSequenceNumbers,
                    out moreNotifications,
                    out notificationMessage,
                    out StatusCodeCollection _,
                    out diagnosticInfos);

                Assert.AreEqual((StatusCode)StatusCodes.Good, response.ServiceResult);
                ServerFixtureUtils.ValidateResponse(response);
                ServerFixtureUtils.ValidateDiagnosticInfos(
                    diagnosticInfos,
                    acknowledgements,
                    response.StringTable,
                    serverTestServices.Logger);
                Assert.AreEqual(subscriptionIds[0], publishedId);
                Assert.AreEqual(0, notificationMessage.NotificationData.Count);

                if (updateValues)
                {
                    UpdateValues(testSet);

                    // fill queues, but only a single value per resend publish shall be returned
                    for (int i = 1; i < queueSize; i++)
                    {
                        //If sampling groups are used, samplingInterval needs to be waited before values are queued
                        if (m_fixture.UseSamplingGroupsInReferenceNodeManager)
                        {
                            Thread.Sleep((int)(100.0 * 1.7));
                        }
                        UpdateValues(testSet);
                    }

                    // Wait a bit to ensure that the server has time to queue the values
                    Thread.Sleep(1000);
                }

                // call ResendData method from the same session context
                ResendDataCall(StatusCodes.Good, subscriptionIds);

                // Data should be available for publishing now
                m_requestHeader.Timestamp = DateTime.UtcNow;
                response = serverTestServices.Publish(
                    m_requestHeader,
                    acknowledgements,
                    out publishedId,
                    out availableSequenceNumbers,
                    out moreNotifications,
                    out notificationMessage,
                    out StatusCodeCollection _,
                    out diagnosticInfos);

                Assert.AreEqual((StatusCode)StatusCodes.Good, response.ServiceResult);
                ServerFixtureUtils.ValidateResponse(response);
                ServerFixtureUtils.ValidateDiagnosticInfos(
                    diagnosticInfos,
                    acknowledgements,
                    response.StringTable,
                    serverTestServices.Logger);
                Assert.AreEqual(subscriptionIds[0], publishedId);
                Assert.AreEqual(1, notificationMessage.NotificationData.Count);
                ExtensionObject items = notificationMessage.NotificationData.FirstOrDefault();
                Assert.IsTrue(items.Body is DataChangeNotification);
                MonitoredItemNotificationCollection monitoredItemsCollection = (
                    (DataChangeNotification)items.Body
                ).MonitoredItems;
                Assert.AreEqual(testSet.Length, monitoredItemsCollection.Count);

                Thread.Sleep(1000);

                if (updateValues && queueSize > 1)
                {
                    // remaining queue Data should be sent in this publish
                    m_requestHeader.Timestamp = DateTime.UtcNow;
                    response = serverTestServices.Publish(
                        m_requestHeader,
                        acknowledgements,
                        out publishedId,
                        out availableSequenceNumbers,
                        out moreNotifications,
                        out notificationMessage,
                        out StatusCodeCollection _,
                        out diagnosticInfos);

                    Assert.AreEqual((StatusCode)StatusCodes.Good, response.ServiceResult);
                    ServerFixtureUtils.ValidateResponse(response);
                    ServerFixtureUtils.ValidateDiagnosticInfos(
                        diagnosticInfos,
                        acknowledgements,
                        response.StringTable,
                        serverTestServices.Logger);
                    Assert.AreEqual(subscriptionIds[0], publishedId);
                    Assert.AreEqual(1, notificationMessage.NotificationData.Count);
                    items = notificationMessage.NotificationData.FirstOrDefault();
                    Assert.IsTrue(items.Body is DataChangeNotification);
                    monitoredItemsCollection = ((DataChangeNotification)items.Body).MonitoredItems;
                    Assert.AreEqual(
                        testSet.Length * (queueSize - 1),
                        monitoredItemsCollection.Count,
                        testSet.Length);
                }

                // Call ResendData method with invalid subscription Id
                ResendDataCall(StatusCodes.BadSubscriptionIdInvalid, [subscriptionIds[^1] + 20]);

                // Nothing to publish since previous ResendData call did not execute
                m_requestHeader.Timestamp = DateTime.UtcNow;
                response = serverTestServices.Publish(
                    m_requestHeader,
                    acknowledgements,
                    out publishedId,
                    out availableSequenceNumbers,
                    out moreNotifications,
                    out notificationMessage,
                    out StatusCodeCollection _,
                    out diagnosticInfos);

                Assert.AreEqual((StatusCode)StatusCodes.Good, response.ServiceResult);
                ServerFixtureUtils.ValidateResponse(response);
                ServerFixtureUtils.ValidateDiagnosticInfos(
                    diagnosticInfos,
                    acknowledgements,
                    response.StringTable,
                    serverTestServices.Logger);
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

        private CallMethodRequestCollection ResendDataCall(
            StatusCode expectedStatus,
            UInt32Collection subscriptionIds)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ILogger logger = telemetry.CreateLogger<ReferenceServerTests>();

            // Find the ResendData method
            var nodesToCall = new CallMethodRequestCollection();
            foreach (uint subscriptionId in subscriptionIds)
            {
                nodesToCall.Add(
                    new CallMethodRequest
                    {
                        ObjectId = ObjectIds.Server,
                        MethodId = MethodIds.Server_ResendData,
                        InputArguments = [new Variant(subscriptionId)]
                    });
            }

            //call ResendData method with subscription ids
            m_requestHeader.Timestamp = DateTime.UtcNow;
            ResponseHeader response = m_server.Call(
                m_requestHeader,
                nodesToCall,
                out CallMethodResultCollection results,
                out DiagnosticInfoCollection diagnosticInfos);

            Assert.AreEqual(expectedStatus, results[0].StatusCode.Code);
            ServerFixtureUtils.ValidateResponse(response, results, nodesToCall);
            ServerFixtureUtils.ValidateDiagnosticInfos(
                diagnosticInfos,
                nodesToCall,
                response.StringTable,
                logger);

            return nodesToCall;
        }

        /// <summary>
        /// Read Values of NodeIds, determine types, write back new random values.
        /// </summary>
        /// <param name="testSet">The nodeIds to modify.</param>
        private void UpdateValues(NodeId[] testSet)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ILogger logger = telemetry.CreateLogger<ReferenceServerTests>();

            // Read values
            RequestHeader requestHeader = m_requestHeader;
            var nodesToRead = new ReadValueIdCollection();
            foreach (NodeId nodeId in testSet)
            {
                nodesToRead.Add(
                    new ReadValueId { NodeId = nodeId, AttributeId = Attributes.Value });
            }
            ResponseHeader response = m_server.Read(
                requestHeader,
                kMaxAge,
                TimestampsToReturn.Neither,
                nodesToRead,
                out DataValueCollection readDataValues,
                out DiagnosticInfoCollection diagnosticInfos);

            ServerFixtureUtils.ValidateResponse(response, readDataValues, nodesToRead);
            ServerFixtureUtils.ValidateDiagnosticInfos(
                diagnosticInfos,
                readDataValues,
                response.StringTable,
                logger);
            Assert.AreEqual(testSet.Length, readDataValues.Count);

            var modifiedValues = new DataValueCollection();
            foreach (DataValue dataValue in readDataValues)
            {
                var typeInfo = TypeInfo.Construct(dataValue.Value);
                Assert.IsNotNull(typeInfo);
                object value = m_generator.GetRandom(typeInfo.BuiltInType);
                modifiedValues.Add(new DataValue { WrappedValue = new Variant(value) });
            }

            int ii = 0;
            var nodesToWrite = new WriteValueCollection();
            foreach (NodeId nodeId in testSet)
            {
                nodesToWrite.Add(
                    new WriteValue
                    {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        Value = modifiedValues[ii]
                    });
                ii++;
            }

            // Write Nodes
            requestHeader.Timestamp = DateTime.UtcNow;
            response = m_server.Write(
                requestHeader,
                nodesToWrite,
                out StatusCodeCollection writeDataValues,
                out diagnosticInfos);
            ServerFixtureUtils.ValidateResponse(response, writeDataValues, nodesToWrite);
            ServerFixtureUtils.ValidateDiagnosticInfos(
                diagnosticInfos,
                writeDataValues,
                response.StringTable,
                logger);
        }

        /// <summary>
        /// Test that Server object EventNotifier has HistoryRead bit set when history capabilities are enabled.
        /// </summary>
        [Test]
        public void ServerEventNotifierHistoryReadBit()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ILogger logger = telemetry.CreateLogger<ReferenceServerTests>();

            // Read Server object EventNotifier attribute
            var readIdCollection = new ReadValueIdCollection
            {
                new ReadValueId
                {
                    AttributeId = Attributes.EventNotifier,
                    NodeId = ObjectIds.Server
                }
            };

            m_requestHeader.Timestamp = DateTime.UtcNow;
            ResponseHeader response = m_server.Read(
                m_requestHeader,
                0,
                TimestampsToReturn.Both,
                readIdCollection,
                out DataValueCollection serverEventNotifierValues,
                out DiagnosticInfoCollection _);

            ServerFixtureUtils.ValidateResponse(response, serverEventNotifierValues, readIdCollection);
            Assert.AreEqual(1, serverEventNotifierValues.Count);
            Assert.NotNull(serverEventNotifierValues[0].Value);

            byte eventNotifier = (byte)serverEventNotifierValues[0].Value;

            // Read history capabilities
            var historyCapabilitiesReadIds = new ReadValueIdCollection
            {
                new ReadValueId
                {
                    AttributeId = Attributes.Value,
                    NodeId = VariableIds.HistoryServerCapabilities_AccessHistoryEventsCapability
                },
                new ReadValueId
                {
                    AttributeId = Attributes.Value,
                    NodeId = VariableIds.HistoryServerCapabilities_AccessHistoryDataCapability
                }
            };

            m_requestHeader.Timestamp = DateTime.UtcNow;
            response = m_server.Read(
                m_requestHeader,
                0,
                TimestampsToReturn.Both,
                historyCapabilitiesReadIds,
                out DataValueCollection historyCapabilitiesValues,
                out DiagnosticInfoCollection _);

            ServerFixtureUtils.ValidateResponse(response, historyCapabilitiesValues, historyCapabilitiesReadIds);
            Assert.AreEqual(2, historyCapabilitiesValues.Count);

            bool accessHistoryEventsCapability =
                historyCapabilitiesValues[0].Value != null &&
                (bool)historyCapabilitiesValues[0].Value;
            bool accessHistoryDataCapability =
                historyCapabilitiesValues[1].Value != null &&
                (bool)historyCapabilitiesValues[1].Value;

            logger.LogInformation("Server EventNotifier: {EventNotifier}", eventNotifier);
            logger.LogInformation("AccessHistoryEventsCapability: {AccessHistoryEventsCapability}", accessHistoryEventsCapability);
            logger.LogInformation("AccessHistoryDataCapability: {AccessHistoryDataCapability}", accessHistoryDataCapability);

            // If either history capability is enabled, the HistoryRead bit should be set
            if (accessHistoryEventsCapability || accessHistoryDataCapability)
            {
                Assert.IsTrue((eventNotifier & EventNotifiers.HistoryRead) != 0,
                    "Server EventNotifier should have HistoryRead bit set when history capabilities are enabled");
            }

            // Verify SubscribeToEvents bit is set (Server object should always support events)
            Assert.IsTrue((eventNotifier & EventNotifiers.SubscribeToEvents) != 0,
                "Server EventNotifier should have SubscribeToEvents bit set");
        }
    }
}
