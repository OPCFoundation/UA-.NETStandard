/* ========================================================================
 * Copyright (c) 2005-2024 The OPC Foundation, Inc. All rights reserved.
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
        private SecureChannelContext m_secureChannelContext;
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
        public async Task SetUpAsync()
        {
            (m_requestHeader, m_secureChannelContext) = await m_server.CreateAndActivateSessionAsync(
                TestContext.CurrentContext.Test.Name).ConfigureAwait(false);
            m_requestHeader.Timestamp = DateTime.UtcNow;
            m_requestHeader.TimeoutHint = kTimeoutHint;
            m_random = new RandomSource();
            m_generator = new DataGenerator(m_random, m_telemetry);
        }

        /// <summary>
        /// Tear down the test session.
        /// </summary>
        [TearDown]
        public async Task TearDownAsync()
        {
            if (!m_sessionClosed)
            {
                m_requestHeader.Timestamp = DateTime.UtcNow;
                await m_server.CloseSessionAsync(m_secureChannelContext, m_requestHeader, CancellationToken.None).ConfigureAwait(false);
                m_requestHeader = null;
            }
        }

        /// <summary>
        /// Set up a Reference Server a session
        /// </summary>
        [GlobalSetup]
        public async Task GlobalSetupAsync()
        {
            // start Ref server
            m_fixture = new ServerFixture<ReferenceServer> { AllNodeManagers = true };
            m_server = await m_fixture.StartAsync(null).ConfigureAwait(false);
            (m_requestHeader, m_secureChannelContext) = await m_server.CreateAndActivateSessionAsync("Bench").ConfigureAwait(false);
        }

        /// <summary>
        /// Tear down Server and the close the session.
        /// </summary>
        [GlobalCleanup]
        public async Task GlobalCleanupAsync()
        {
            await m_server.CloseSessionAsync(m_secureChannelContext, m_requestHeader, true, CancellationToken.None).ConfigureAwait(false);
            await m_fixture.StopAsync().ConfigureAwait(false);
            Thread.Sleep(1000);
        }

        /// <summary>
        /// Test for expected exceptions.
        /// </summary>
        [Test]
        public async Task NoInvalidTimestampExceptionAsync()
        {
            // test that the server accepts an invalid timestamp
            m_requestHeader.Timestamp = DateTime.UtcNow - TimeSpan.FromDays(30);
            await m_server.CloseSessionAsync(m_secureChannelContext, m_requestHeader, false, CancellationToken.None).ConfigureAwait(false);
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
        public async Task GetOperationLimitsAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ILogger logger = telemetry.CreateLogger<ReferenceServerTests>();

            var readIdCollection = new ReadValueIdCollection {
                new ReadValueId {
                    AttributeId = Attributes.Value,
                    NodeId = VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerRead
                },
                new ReadValueId {
                    AttributeId = Attributes.Value,
                    NodeId = VariableIds
                        .Server_ServerCapabilities_OperationLimits_MaxNodesPerHistoryReadData
                },
                new ReadValueId {
                    AttributeId = Attributes.Value,
                    NodeId = VariableIds
                        .Server_ServerCapabilities_OperationLimits_MaxNodesPerHistoryReadEvents
                },
                new ReadValueId {
                    AttributeId = Attributes.Value,
                    NodeId = VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerWrite
                },
                new ReadValueId {
                    AttributeId = Attributes.Value,
                    NodeId = VariableIds
                        .Server_ServerCapabilities_OperationLimits_MaxNodesPerHistoryUpdateData
                },
                new ReadValueId {
                    AttributeId = Attributes.Value,
                    NodeId = VariableIds
                        .Server_ServerCapabilities_OperationLimits_MaxNodesPerHistoryUpdateEvents
                },
                new ReadValueId {
                    AttributeId = Attributes.Value,
                    NodeId = VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerBrowse
                },
                new ReadValueId {
                    AttributeId = Attributes.Value,
                    NodeId = VariableIds
                        .Server_ServerCapabilities_OperationLimits_MaxMonitoredItemsPerCall
                },
                new ReadValueId {
                    AttributeId = Attributes.Value,
                    NodeId = VariableIds
                        .Server_ServerCapabilities_OperationLimits_MaxNodesPerNodeManagement
                },
                new ReadValueId {
                    AttributeId = Attributes.Value,
                    NodeId = VariableIds
                        .Server_ServerCapabilities_OperationLimits_MaxNodesPerRegisterNodes
                },
                new ReadValueId {
                    AttributeId = Attributes.Value,
                    NodeId =
                        VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerTranslateBrowsePathsToNodeIds
                },
                new ReadValueId {
                    AttributeId = Attributes.Value,
                    NodeId = VariableIds
                        .Server_ServerCapabilities_OperationLimits_MaxNodesPerMethodCall
                }
            };

            RequestHeader requestHeader = m_requestHeader;
            requestHeader.Timestamp = DateTime.UtcNow;
            ReadResponse readResponse = await m_server.ReadAsync(
                m_secureChannelContext,
                requestHeader,
                kMaxAge,
                TimestampsToReturn.Neither,
                readIdCollection, CancellationToken.None).ConfigureAwait(false);
            ServerFixtureUtils.ValidateResponse(readResponse.ResponseHeader, readResponse.Results, readIdCollection);
            ServerFixtureUtils.ValidateDiagnosticInfos(
                readResponse.DiagnosticInfos,
                readResponse.Results,
                readResponse.ResponseHeader.StringTable,
                logger);

            DataValueCollection results = readResponse.Results;
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
        public async Task ReadAsync()
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
            ReadResponse readResponse = await m_server.ReadAsync(
                m_secureChannelContext,
                requestHeader,
                kMaxAge,
                TimestampsToReturn.Neither,
                nodesToRead, CancellationToken.None).ConfigureAwait(false);
            ServerFixtureUtils.ValidateResponse(readResponse.ResponseHeader, readResponse.Results, nodesToRead);
            ServerFixtureUtils.ValidateDiagnosticInfos(
                readResponse.DiagnosticInfos,
                readResponse.Results,
                readResponse.ResponseHeader.StringTable,
                logger);
        }

        /// <summary>
        /// Read all nodes.
        /// </summary>
        [Test]
        public async Task ReadAllNodesAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var serverTestServices = new ServerTestServices(m_server, m_secureChannelContext, telemetry);
            if (m_operationLimits == null)
            {
                await GetOperationLimitsAsync().ConfigureAwait(false);
            }
            m_referenceDescriptions ??= await CommonTestWorkers.BrowseFullAddressSpaceWorkerAsync(
                serverTestServices,
                m_requestHeader,
                m_operationLimits).ConfigureAwait(false);

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
                ReadResponse readResponse = await m_server.ReadAsync(
                    m_secureChannelContext,
                    requestHeader,
                    kMaxAge,
                    TimestampsToReturn.Both,
                    nodesToRead, CancellationToken.None).ConfigureAwait(false);
                ServerFixtureUtils.ValidateResponse(readResponse.ResponseHeader, readResponse.Results, nodesToRead);
                ServerFixtureUtils.ValidateDiagnosticInfos(
                    readResponse.DiagnosticInfos,
                    readResponse.Results,
                    readResponse.ResponseHeader.StringTable,
                    serverTestServices.Logger);

                foreach (DataValue dataValue in readResponse.Results)
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
        public async Task WriteAsync()
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
            WriteResponse writeResponse = await m_server.WriteAsync(
                m_secureChannelContext,
                requestHeader,
                nodesToWrite, CancellationToken.None).ConfigureAwait(false);
            ServerFixtureUtils.ValidateResponse(writeResponse.ResponseHeader, writeResponse.Results, nodesToWrite);
            ServerFixtureUtils.ValidateDiagnosticInfos(
                writeResponse.DiagnosticInfos,
                writeResponse.Results,
                writeResponse.ResponseHeader.StringTable,
                logger);
        }

        /// <summary>
        /// Update static Nodes, read modify write.
        /// </summary>
        [Test]
        [Order(350)]
        public async Task ReadWriteUpdateNodesAsync()
        {
            // Nodes
            NamespaceTable namespaceUris = m_server.CurrentInstance.NamespaceUris;
            NodeId[] testSet =
            [
                .. CommonTestWorkers.NodeIdTestSetStatic
                    .Select(n => ExpandedNodeId.ToNodeId(n, namespaceUris))
            ];

            await UpdateValuesAsync(testSet).ConfigureAwait(false);
        }

        /// <summary>
        /// Browse full address space.
        /// </summary>
        [Test]
        [Order(400)]
        [Benchmark]
        public async Task BrowseFullAddressSpaceAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var serverTestServices = new ServerTestServices(m_server, m_secureChannelContext, telemetry);
            if (m_operationLimits == null)
            {
                await GetOperationLimitsAsync().ConfigureAwait(false);
            }
            m_referenceDescriptions = await CommonTestWorkers.BrowseFullAddressSpaceWorkerAsync(
                serverTestServices,
                m_requestHeader,
                m_operationLimits).ConfigureAwait(false);
        }

        /// <summary>
        /// Translate references.
        /// </summary>
        [Test]
        [Order(500)]
        [Benchmark]
        public async Task TranslateBrowsePathAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var serverTestServices = new ServerTestServices(m_server, m_secureChannelContext, telemetry);
            if (m_operationLimits == null)
            {
                await GetOperationLimitsAsync().ConfigureAwait(false);
            }
            m_referenceDescriptions ??= await CommonTestWorkers.BrowseFullAddressSpaceWorkerAsync(
                serverTestServices,
                m_requestHeader,
                m_operationLimits).ConfigureAwait(false);
            _ = await CommonTestWorkers.TranslateBrowsePathWorkerAsync(
                serverTestServices,
                m_referenceDescriptions,
                m_requestHeader,
                m_operationLimits).ConfigureAwait(false);
        }

        /// <summary>
        /// Create a subscription with a monitored item.
        /// Read a few notifications with Publish.
        /// Delete the monitored item and subscription.
        /// </summary>
        [Test]
        public async Task SubscriptionAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var serverTestServices = new ServerTestServices(m_server, m_secureChannelContext, telemetry);
            await CommonTestWorkers.SubscriptionTestAsync(serverTestServices, m_requestHeader).ConfigureAwait(false);
        }

        /// <summary>
        /// Create a secondary Session.
        /// Create a subscription with a monitored item.
        /// Close session, but do not delete subscriptions.
        /// Transfer subscription from closed session to the other.
        /// </summary>
        [Theory]
        public async Task TransferSubscriptionSessionClosedAsync(bool sendInitialData, bool useSecurity)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var serverTestServices = new ServerTestServices(m_server, m_secureChannelContext, telemetry);
            (RequestHeader transferRequestHeader, SecureChannelContext transferContext) = await m_server.CreateAndActivateSessionAsync(
                "ClosedSession",
                useSecurity).ConfigureAwait(false);
            NamespaceTable namespaceUris = m_server.CurrentInstance.NamespaceUris;
            NodeId[] testSet =
            [
                .. CommonTestWorkers.NodeIdTestSetStatic
                        .Select(n => ExpandedNodeId.ToNodeId(n, namespaceUris))
            ];
            transferRequestHeader.Timestamp = DateTime.UtcNow;
            serverTestServices.SecureChannelContext = transferContext;
            UInt32Collection subscriptionIds = await CommonTestWorkers.CreateSubscriptionForTransferAsync(
                serverTestServices,
                transferRequestHeader,
                testSet,
                kQueueSize,
                -1).ConfigureAwait(false);

            transferRequestHeader.Timestamp = DateTime.UtcNow;
            await m_server.CloseSessionAsync(transferContext, transferRequestHeader, false, CancellationToken.None).ConfigureAwait(false);

            //restore security context, transfer abandoned subscription
            serverTestServices.SecureChannelContext = m_secureChannelContext;
            await CommonTestWorkers.TransferSubscriptionTestAsync(
                serverTestServices,
                m_requestHeader,
                subscriptionIds,
                sendInitialData,
                !useSecurity).ConfigureAwait(false);

            if (useSecurity)
            {
                // subscription was deleted, expect 'BadNoSubscription'
                ServiceResultException sre = NUnit.Framework.Assert.ThrowsAsync<ServiceResultException>(async () =>
                {
                    m_requestHeader.Timestamp = DateTime.UtcNow;
                    await CommonTestWorkers.VerifySubscriptionTransferredAsync(
                        serverTestServices,
                        m_requestHeader,
                        subscriptionIds,
                        true).ConfigureAwait(false);
                });
                Assert.AreEqual(
                    (StatusCode)StatusCodes.BadNoSubscription,
                    (StatusCode)sre.StatusCode);
            }
        }

        /// <summary>
        /// Create a subscription with a monitored item.
        /// Create a secondary Session.
        /// Transfer subscription with a monitored item from one session to the other.
        /// </summary>
        [Theory]
        public async Task TransferSubscriptionAsync(bool sendInitialData, bool useSecurity)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var serverTestServices = new ServerTestServices(m_server, m_secureChannelContext, telemetry);

            NamespaceTable namespaceUris = m_server.CurrentInstance.NamespaceUris;
            NodeId[] testSet =
            [
                .. CommonTestWorkers.NodeIdTestSetStatic
                        .Select(n => ExpandedNodeId.ToNodeId(n, namespaceUris))
            ];
            UInt32Collection subscriptionIds = await CommonTestWorkers.CreateSubscriptionForTransferAsync(
                serverTestServices,
                m_requestHeader,
                testSet,
                kQueueSize,
                -1).ConfigureAwait(false);

            (RequestHeader transferRequestHeader, SecureChannelContext transferSecurityContext) = await m_server.CreateAndActivateSessionAsync(
                "TransferSession",
                useSecurity).ConfigureAwait(false);
            serverTestServices.SecureChannelContext = transferSecurityContext;
            await CommonTestWorkers.TransferSubscriptionTestAsync(
                serverTestServices,
                transferRequestHeader,
                subscriptionIds,
                sendInitialData,
                !useSecurity).ConfigureAwait(false);

            if (useSecurity)
            {
                //restore security context
                serverTestServices.SecureChannelContext = m_secureChannelContext;
                await CommonTestWorkers.VerifySubscriptionTransferredAsync(
                    serverTestServices,
                    m_requestHeader,
                    subscriptionIds,
                    true).ConfigureAwait(false);
            }

            transferRequestHeader.Timestamp = DateTime.UtcNow;
            await m_server.CloseSessionAsync(transferSecurityContext, transferRequestHeader, true, CancellationToken.None).ConfigureAwait(false);
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
        public async Task ResendDataAsync(bool updateValues, uint queueSize)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var serverTestServices = new ServerTestServices(m_server, m_secureChannelContext, telemetry);

            NamespaceTable namespaceUris = m_server.CurrentInstance.NamespaceUris;
            NodeIdCollection testSetCollection = CommonTestWorkers
                .NodeIdTestSetStatic.Select(n => ExpandedNodeId.ToNodeId(n, namespaceUris))
                .ToArray();
            testSetCollection.AddRange(
                CommonTestWorkers.NodeIdTestDataSetStatic
                    .Select(n => ExpandedNodeId.ToNodeId(n, namespaceUris)));
            NodeId[] testSet = [.. testSetCollection];

            //Re-use method CreateSubscriptionForTransfer to create a subscription
            UInt32Collection subscriptionIds = await CommonTestWorkers.CreateSubscriptionForTransferAsync(
                serverTestServices,
                m_requestHeader,
                testSet,
                queueSize,
                0).ConfigureAwait(false);

            (RequestHeader resendDataRequestHeader, SecureChannelContext resendDataSecurityContext) = await m_server.CreateAndActivateSessionAsync(
                "ResendData").ConfigureAwait(false);

            serverTestServices.SecureChannelContext = m_secureChannelContext;
            // After the ResendData call there will be data to publish again
            CallMethodRequestCollection nodesToCall = await ResendDataCallAsync(
                StatusCodes.Good,
                subscriptionIds).ConfigureAwait(false);

            Thread.Sleep(1000);

            // Make sure publish queue becomes empty by consuming it
            Assert.AreEqual(1, subscriptionIds.Count);

            // Issue a Publish request
            m_requestHeader.Timestamp = DateTime.UtcNow;
            var acknowledgements = new SubscriptionAcknowledgementCollection();
            PublishResponse publishResponse = await serverTestServices.PublishAsync(
                m_requestHeader,
                acknowledgements).ConfigureAwait(false);

            Assert.AreEqual((StatusCode)StatusCodes.Good, publishResponse.ResponseHeader.ServiceResult);
            ServerFixtureUtils.ValidateResponse(publishResponse.ResponseHeader);
            ServerFixtureUtils.ValidateDiagnosticInfos(
                publishResponse.DiagnosticInfos,
                acknowledgements,
                publishResponse.ResponseHeader.StringTable,
                serverTestServices.Logger);
            Assert.AreEqual(subscriptionIds[0], publishResponse.SubscriptionId);
            Assert.AreEqual(1, publishResponse.NotificationMessage.NotificationData.Count);

            // Validate nothing to publish a few times
            const int timesToCallPublish = 3;
            for (int i = 0; i < timesToCallPublish; i++)
            {
                m_requestHeader.Timestamp = DateTime.UtcNow;
                publishResponse = await serverTestServices.PublishAsync(
                    m_requestHeader,
                    acknowledgements).ConfigureAwait(false);

                Assert.AreEqual((StatusCode)StatusCodes.Good, publishResponse.ResponseHeader.ServiceResult);
                ServerFixtureUtils.ValidateResponse(publishResponse.ResponseHeader);
                ServerFixtureUtils.ValidateDiagnosticInfos(
                    publishResponse.DiagnosticInfos,
                    acknowledgements,
                    publishResponse.ResponseHeader.StringTable,
                    serverTestServices.Logger);
                Assert.AreEqual(subscriptionIds[0], publishResponse.SubscriptionId);
                Assert.AreEqual(0, publishResponse.NotificationMessage.NotificationData.Count);
            }

            // Validate ResendData method call returns error from different session contexts

            // call ResendData method from different session context
            resendDataRequestHeader.Timestamp = DateTime.UtcNow;
            CallResponse callResponse = await m_server.CallAsync(
                resendDataSecurityContext,
                resendDataRequestHeader,
                nodesToCall, CancellationToken.None).ConfigureAwait(false);

            serverTestServices.SecureChannelContext = m_secureChannelContext;

            Assert.AreEqual((StatusCode)StatusCodes.BadUserAccessDenied, callResponse.Results[0].StatusCode);
            ServerFixtureUtils.ValidateResponse(callResponse.ResponseHeader, callResponse.Results, nodesToCall);
            ServerFixtureUtils.ValidateDiagnosticInfos(
                callResponse.DiagnosticInfos,
                nodesToCall,
                callResponse.ResponseHeader.StringTable,
                serverTestServices.Logger);

            // Still nothing to publish since previous ResendData call did not execute
            m_requestHeader.Timestamp = DateTime.UtcNow;
            publishResponse = await serverTestServices.PublishAsync(
                m_requestHeader,
                acknowledgements).ConfigureAwait(false);

            Assert.AreEqual((StatusCode)StatusCodes.Good, publishResponse.ResponseHeader.ServiceResult);
            ServerFixtureUtils.ValidateResponse(publishResponse.ResponseHeader);
            ServerFixtureUtils.ValidateDiagnosticInfos(
                publishResponse.DiagnosticInfos,
                acknowledgements,
                publishResponse.ResponseHeader.StringTable,
                serverTestServices.Logger);
            Assert.AreEqual(subscriptionIds[0], publishResponse.SubscriptionId);
            Assert.AreEqual(0, publishResponse.NotificationMessage.NotificationData.Count);

            if (updateValues)
            {
                await UpdateValuesAsync(testSet).ConfigureAwait(false);

                // fill queues, but only a single value per resend publish shall be returned
                for (int i = 1; i < queueSize; i++)
                {
                    //If sampling groups are used, samplingInterval needs to be waited before values are queued
                    if (m_fixture.UseSamplingGroupsInReferenceNodeManager)
                    {
                        Thread.Sleep((int)(100.0 * 1.7));
                    }
                    await UpdateValuesAsync(testSet).ConfigureAwait(false);
                }

                // Wait a bit to ensure that the server has time to queue the values
                Thread.Sleep(1000);
            }

            // call ResendData method from the same session context
            await ResendDataCallAsync(StatusCodes.Good, subscriptionIds).ConfigureAwait(false);

            // Data should be available for publishing now
            m_requestHeader.Timestamp = DateTime.UtcNow;
            publishResponse = await serverTestServices.PublishAsync(
                m_requestHeader,
                acknowledgements).ConfigureAwait(false);

            Assert.AreEqual((StatusCode)StatusCodes.Good, publishResponse.ResponseHeader.ServiceResult);
            ServerFixtureUtils.ValidateResponse(publishResponse.ResponseHeader);
            ServerFixtureUtils.ValidateDiagnosticInfos(
                publishResponse.DiagnosticInfos,
                acknowledgements,
                publishResponse.ResponseHeader.StringTable,
                serverTestServices.Logger);
            Assert.AreEqual(subscriptionIds[0], publishResponse.SubscriptionId);
            Assert.AreEqual(1, publishResponse.NotificationMessage.NotificationData.Count);
            ExtensionObject items = publishResponse.NotificationMessage.NotificationData.FirstOrDefault();
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
                publishResponse = await serverTestServices.PublishAsync(
                    m_requestHeader,
                    acknowledgements).ConfigureAwait(false);

                Assert.AreEqual((StatusCode)StatusCodes.Good, publishResponse.ResponseHeader.ServiceResult);
                ServerFixtureUtils.ValidateResponse(publishResponse.ResponseHeader);
                ServerFixtureUtils.ValidateDiagnosticInfos(
                    publishResponse.DiagnosticInfos,
                    acknowledgements,
                    publishResponse.ResponseHeader.StringTable,
                    serverTestServices.Logger);
                Assert.AreEqual(subscriptionIds[0], publishResponse.SubscriptionId);
                Assert.AreEqual(1, publishResponse.NotificationMessage.NotificationData.Count);
                items = publishResponse.NotificationMessage.NotificationData.FirstOrDefault();
                Assert.IsTrue(items.Body is DataChangeNotification);
                monitoredItemsCollection = ((DataChangeNotification)items.Body).MonitoredItems;
                Assert.AreEqual(
                    testSet.Length * (queueSize - 1),
                    monitoredItemsCollection.Count,
                    testSet.Length);
            }

            // Call ResendData method with invalid subscription Id
            await ResendDataCallAsync(StatusCodes.BadSubscriptionIdInvalid, [subscriptionIds[^1] + 20]).ConfigureAwait(false);

            // Nothing to publish since previous ResendData call did not execute
            m_requestHeader.Timestamp = DateTime.UtcNow;
            publishResponse = await serverTestServices.PublishAsync(
                m_requestHeader,
                acknowledgements).ConfigureAwait(false);

            Assert.AreEqual((StatusCode)StatusCodes.Good, publishResponse.ResponseHeader.ServiceResult);
            ServerFixtureUtils.ValidateResponse(publishResponse.ResponseHeader);
            ServerFixtureUtils.ValidateDiagnosticInfos(
                publishResponse.DiagnosticInfos,
                acknowledgements,
                publishResponse.ResponseHeader.StringTable,
                serverTestServices.Logger);
            Assert.AreEqual(subscriptionIds[0], publishResponse.SubscriptionId);
            Assert.AreEqual(0, publishResponse.NotificationMessage.NotificationData.Count);

            resendDataRequestHeader.Timestamp = DateTime.UtcNow;
            await m_server.CloseSessionAsync(resendDataSecurityContext, resendDataRequestHeader, true, CancellationToken.None).ConfigureAwait(false);
        }

        private async Task<CallMethodRequestCollection> ResendDataCallAsync(
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
            CallResponse callResponse = await m_server.CallAsync(
                m_secureChannelContext,
                m_requestHeader,
                nodesToCall, CancellationToken.None).ConfigureAwait(false);

            Assert.AreEqual(expectedStatus, callResponse.Results[0].StatusCode.Code);
            ServerFixtureUtils.ValidateResponse(callResponse.ResponseHeader, callResponse.Results, nodesToCall);
            ServerFixtureUtils.ValidateDiagnosticInfos(
                callResponse.DiagnosticInfos,
                nodesToCall,
                callResponse.ResponseHeader.StringTable,
                logger);

            return nodesToCall;
        }

        /// <summary>
        /// Read Values of NodeIds, determine types, write back new random values.
        /// </summary>
        /// <param name="testSet">The nodeIds to modify.</param>
        private async Task UpdateValuesAsync(NodeId[] testSet)
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
            ReadResponse readResponse = await m_server.ReadAsync(
                m_secureChannelContext,
                requestHeader,
                kMaxAge,
                TimestampsToReturn.Neither,
                nodesToRead, CancellationToken.None).ConfigureAwait(false);

            ServerFixtureUtils.ValidateResponse(readResponse.ResponseHeader, readResponse.Results, nodesToRead);
            ServerFixtureUtils.ValidateDiagnosticInfos(
                readResponse.DiagnosticInfos,
                readResponse.Results,
                readResponse.ResponseHeader.StringTable,
                logger);
            Assert.AreEqual(testSet.Length, readResponse.Results.Count);

            var modifiedValues = new DataValueCollection();
            foreach (DataValue dataValue in readResponse.Results)
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
            WriteResponse writeResponse = await m_server.WriteAsync(
                m_secureChannelContext,
                requestHeader,
                nodesToWrite, CancellationToken.None).ConfigureAwait(false);
            ServerFixtureUtils.ValidateResponse(writeResponse.ResponseHeader, writeResponse.Results, nodesToWrite);
            ServerFixtureUtils.ValidateDiagnosticInfos(
                writeResponse.DiagnosticInfos,
                writeResponse.Results,
                writeResponse.ResponseHeader.StringTable,
                logger);
        }

        /// <summary>
        /// Test that Server object EventNotifier has HistoryRead bit set when history capabilities are enabled.
        /// </summary>
        [Test]
        public async Task ServerEventNotifierHistoryReadBitAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ILogger logger = telemetry.CreateLogger<ReferenceServerTests>();

            // Read Server object EventNotifier attribute
            var readIdCollection = new ReadValueIdCollection {
                new ReadValueId {
                    AttributeId = Attributes.EventNotifier,
                    NodeId = ObjectIds.Server
                }
            };

            m_requestHeader.Timestamp = DateTime.UtcNow;
            ReadResponse readResponse = await m_server.ReadAsync(
                m_secureChannelContext,
                m_requestHeader,
                0,
                TimestampsToReturn.Both,
                readIdCollection, CancellationToken.None).ConfigureAwait(false);

            ServerFixtureUtils.ValidateResponse(readResponse.ResponseHeader, readResponse.Results, readIdCollection);
            Assert.AreEqual(1, readResponse.Results.Count);
            Assert.NotNull(readResponse.Results[0].Value);

            byte eventNotifier = (byte)readResponse.Results[0].Value;

            // Read history capabilities
            var historyCapabilitiesReadIds = new ReadValueIdCollection {
                new ReadValueId {
                    AttributeId = Attributes.Value,
                    NodeId = VariableIds.HistoryServerCapabilities_AccessHistoryEventsCapability
                },
                new ReadValueId {
                    AttributeId = Attributes.Value,
                    NodeId = VariableIds.HistoryServerCapabilities_AccessHistoryDataCapability
                }
            };

            m_requestHeader.Timestamp = DateTime.UtcNow;
            readResponse = await m_server.ReadAsync(
                m_secureChannelContext,
                m_requestHeader,
                0,
                TimestampsToReturn.Both,
                historyCapabilitiesReadIds, CancellationToken.None).ConfigureAwait(false);

            ServerFixtureUtils.ValidateResponse(readResponse.ResponseHeader, readResponse.Results, historyCapabilitiesReadIds);
            Assert.AreEqual(2, readResponse.Results.Count);

            bool accessHistoryEventsCapability =
                readResponse.Results[0].Value != null &&
                (bool)readResponse.Results[0].Value;
            bool accessHistoryDataCapability =
                readResponse.Results[1].Value != null &&
                (bool)readResponse.Results[1].Value;

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

        /// <summary>
        /// Test provisioning mode - server should start with limited namespace.
        /// </summary>
        [Test]
        public async Task ProvisioningModeTestAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // start Ref server in provisioning mode
            var fixture = new ServerFixture<ReferenceServer>
            {
                AllNodeManagers = false,
                OperationLimits = false,
                DurableSubscriptionsEnabled = false,
                AutoAccept = true,
                ProvisioningMode = true
            };

            ReferenceServer server = await fixture.StartAsync().ConfigureAwait(false);

            // Verify provisioning mode is enabled
            Assert.IsTrue(server.ProvisioningMode, "Server should be in provisioning mode");

            // Get endpoints - in provisioning mode, anonymous authentication should not be allowed
            EndpointDescriptionCollection endpoints = server.GetEndpoints();
            Assert.IsNotNull(endpoints);
            Assert.IsTrue(endpoints.Count > 0, "Server should have endpoints");

            // Check that anonymous token policy is not present for at least one endpoint
            bool hasEndpointWithoutAnonymous = false;
            foreach (EndpointDescription endpoint in endpoints)
            {
                bool hasAnonymous = endpoint.UserIdentityTokens.Any(
                    policy => policy.TokenType == UserTokenType.Anonymous);
                if (!hasAnonymous)
                {
                    hasEndpointWithoutAnonymous = true;
                    break;
                }
            }
            Assert.IsTrue(hasEndpointWithoutAnonymous,
                "At least one endpoint should not allow anonymous authentication in provisioning mode");

            // Clean up
            await fixture.StopAsync().ConfigureAwait(false);
        }
    }
}
