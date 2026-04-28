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
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.Test;
using Opc.Ua.Tests;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Test Reference Server.
    /// </summary>
    [TestFixture]
    [Category("Server")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
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
        private ArrayOf<ReferenceDescription> m_referenceDescriptions;
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
            m_fixture = new ServerFixture<ReferenceServer>(t => new ReferenceServer(t))
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
            m_requestHeader.Timestamp = DateTimeUtc.Now;
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
                m_requestHeader.Timestamp = DateTimeUtc.Now;
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
            m_fixture = new ServerFixture<ReferenceServer>(t => new ReferenceServer(t))
            {
                AllNodeManagers = true
            };
            m_server = await m_fixture.StartAsync(null).ConfigureAwait(false);
            (m_requestHeader, m_secureChannelContext) = await m_server.CreateAndActivateSessionAsync("Bench").ConfigureAwait(false);
        }

        /// <summary>
        /// Tear down Server and the close the session.
        /// </summary>
        [GlobalCleanup]
        public async Task GlobalCleanupAsync()
        {
            await m_server.CloseSessionAsync(m_secureChannelContext, m_requestHeader, true, RequestLifetime.None).ConfigureAwait(false);
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
            m_requestHeader.Timestamp = DateTimeUtc.Now - TimeSpan.FromDays(30);
            await m_server.CloseSessionAsync(m_secureChannelContext, m_requestHeader, false, RequestLifetime.None).ConfigureAwait(false);
            m_sessionClosed = true;
        }

        /// <summary>
        /// Get Endpoints.
        /// </summary>
        [Test]
        public void GetEndpoints()
        {
            ArrayOf<EndpointDescription> endpoints = m_server.GetEndpoints();
            Assert.That(endpoints.IsNull, Is.False);
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

            ArrayOf<ReadValueId> readIdCollection =
            [
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
            ];

            RequestHeader requestHeader = m_requestHeader;
            requestHeader.Timestamp = DateTimeUtc.Now;
            ReadResponse readResponse = await m_server.ReadAsync(
                m_secureChannelContext,
                requestHeader,
                kMaxAge,
                TimestampsToReturn.Neither,
                readIdCollection,
                RequestLifetime.None).ConfigureAwait(false);
            ServerFixtureUtils.ValidateResponse(readResponse.ResponseHeader, readResponse.Results, readIdCollection);
            ServerFixtureUtils.ValidateDiagnosticInfos(
                readResponse.DiagnosticInfos,
                readResponse.Results,
                readResponse.ResponseHeader.StringTable,
                logger);

            ArrayOf<DataValue> results = readResponse.Results;
            Assert.That(results.IsNull, Is.False);
            Assert.That(results.Count, Is.EqualTo(readIdCollection.Count));

            m_operationLimits = new OperationLimits
            {
                MaxNodesPerRead = (uint)results[0].WrappedValue,
                MaxNodesPerHistoryReadData = (uint)results[1].WrappedValue,
                MaxNodesPerHistoryReadEvents = (uint)results[2].WrappedValue,
                MaxNodesPerWrite = (uint)results[3].WrappedValue,
                MaxNodesPerHistoryUpdateData = (uint)results[4].WrappedValue,
                MaxNodesPerHistoryUpdateEvents = (uint)results[5].WrappedValue,
                MaxNodesPerBrowse = (uint)results[6].WrappedValue,
                MaxMonitoredItemsPerCall = (uint)results[7].WrappedValue,
                MaxNodesPerNodeManagement = (uint)results[8].WrappedValue,
                MaxNodesPerRegisterNodes = (uint)results[9].WrappedValue,
                MaxNodesPerTranslateBrowsePathsToNodeIds = (uint)results[10].WrappedValue,
                MaxNodesPerMethodCall = (uint)results[11].WrappedValue
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
            requestHeader.Timestamp = DateTimeUtc.Now;
            var nodeId = new NodeId("Scalar_Simulation_Int32", 2);
            var nodesToRead = ServerFixtureUtils.AttributesIds.Keys
                .Select(attributeId => new ReadValueId { NodeId = nodeId, AttributeId = attributeId })
                .ToArrayOf();
            ReadResponse readResponse = await m_server.ReadAsync(
                m_secureChannelContext,
                requestHeader,
                kMaxAge,
                TimestampsToReturn.Neither,
                nodesToRead, RequestLifetime.None).ConfigureAwait(false);
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
            var serverTestServices = new ServerTestServices(m_server, m_secureChannelContext);
            if (m_operationLimits == null)
            {
                await GetOperationLimitsAsync().ConfigureAwait(false);
            }
            if (m_referenceDescriptions.IsNull)
            {
                m_referenceDescriptions = await CommonTestWorkers.BrowseFullAddressSpaceWorkerAsync(
                    serverTestServices,
                    m_requestHeader,
                    m_operationLimits).ConfigureAwait(false);
            }
            // Read all variables
            RequestHeader requestHeader = m_requestHeader;
            foreach (ReferenceDescription reference in m_referenceDescriptions.ToList())
            {
                requestHeader.Timestamp = DateTimeUtc.Now;
                var nodeId = ExpandedNodeId.ToNodeId(
                    reference.NodeId,
                    m_server.CurrentInstance.NamespaceUris);
                var nodesToRead = ServerFixtureUtils.AttributesIds.Keys
                    .Select(attributeId => new ReadValueId { NodeId = nodeId, AttributeId = attributeId })
                    .ToArrayOf();
                TestContext.Out.WriteLine("NodeId {0} {1}", reference.NodeId, reference.BrowseName);
                ReadResponse readResponse = await m_server.ReadAsync(
                    m_secureChannelContext,
                    requestHeader,
                    kMaxAge,
                    TimestampsToReturn.Both,
                    nodesToRead, RequestLifetime.None).ConfigureAwait(false);
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
            requestHeader.Timestamp = DateTimeUtc.Now;
            var nodeId = new NodeId("Scalar_Simulation_Int32", 2);
            var nodesToWrite = ServerFixtureUtils.AttributesIds.Keys
                .Select(attributeId => new WriteValue
                {
                    NodeId = nodeId,
                    AttributeId = Attributes.Value,
                    Value = new DataValue(1234)
                })
                .ToArrayOf();
            WriteResponse writeResponse = await m_server.WriteAsync(
                m_secureChannelContext,
                requestHeader,
                nodesToWrite, RequestLifetime.None).ConfigureAwait(false);
            ServerFixtureUtils.ValidateResponse(writeResponse.ResponseHeader, writeResponse.Results, nodesToWrite);
            ServerFixtureUtils.ValidateDiagnosticInfos(
                writeResponse.DiagnosticInfos,
                writeResponse.Results,
                writeResponse.ResponseHeader.StringTable,
                logger);
        }

        /// <summary>
        /// Test that ReferenceNodeManager variables update their SourceTimestamp on read.
        /// </summary>
        [Test]
        [Order(340)]
        public async Task ReferenceNodeManagerVariablesUpdateTimestampOnReadAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ILogger logger = telemetry.CreateLogger<ReferenceServerTests>();

            // Read a variable from the ReferenceNodeManager (namespace index 2)
            var nodeId = new NodeId("Scalar_Static_Byte", 2);
            ArrayOf<ReadValueId> nodesToRead =
            [
                new ReadValueId
                {
                    NodeId = nodeId,
                    AttributeId = Attributes.Value
                }
            ];

            // First read
            RequestHeader requestHeader = m_requestHeader;
            requestHeader.Timestamp = DateTimeUtc.Now;
            DateTimeUtc timeBeforeFirstRead = DateTimeUtc.Now;
            ReadResponse firstReadResponse = await m_server.ReadAsync(
                m_secureChannelContext,
                requestHeader,
                kMaxAge,
                TimestampsToReturn.Both,
                nodesToRead,
                RequestLifetime.None).ConfigureAwait(false);

            Assert.That(firstReadResponse, Is.Not.Null);
            Assert.That(firstReadResponse.Results.IsNull, Is.False);
            Assert.That(firstReadResponse.Results.Count, Is.EqualTo(1));
            DataValue firstValue = firstReadResponse.Results[0];
            Assert.That(firstValue.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(firstValue.SourceTimestamp.IsNull, Is.False);
            logger.LogInformation("First read - SourceTimestamp: {SourceTimestamp}, ServerTimestamp: {ServerTimestamp}",
                firstValue.SourceTimestamp, firstValue.ServerTimestamp);

            // Verify the timestamp is recent (not startup time)
            Assert.That((long)firstValue.SourceTimestamp, Is.GreaterThanOrEqualTo((long)timeBeforeFirstRead.SubtractMilliseconds(1000)),
                "SourceTimestamp should be close to the read time, not the server startup time");

            // Wait a bit to ensure time difference
            await Task.Delay(1500).ConfigureAwait(false);

            // Second read
            requestHeader.Timestamp = DateTimeUtc.Now;
            DateTimeUtc timeBeforeSecondRead = DateTimeUtc.Now;
            ReadResponse secondReadResponse = await m_server.ReadAsync(
                m_secureChannelContext,
                requestHeader,
                kMaxAge,
                TimestampsToReturn.Both,
                nodesToRead,
                RequestLifetime.None).ConfigureAwait(false);

            Assert.That(secondReadResponse, Is.Not.Null);
            Assert.That(secondReadResponse.Results.IsNull, Is.False);
            Assert.That(secondReadResponse.Results.Count, Is.EqualTo(1));
            DataValue secondValue = secondReadResponse.Results[0];
            Assert.That(secondValue.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(secondValue.SourceTimestamp.IsNull, Is.False);
            logger.LogInformation("Second read - SourceTimestamp: {SourceTimestamp}, ServerTimestamp: {ServerTimestamp}",
                secondValue.SourceTimestamp, secondValue.ServerTimestamp);

            // Verify the second timestamp is more recent than the first
            Assert.That((long)secondValue.SourceTimestamp, Is.GreaterThan((long)firstValue.SourceTimestamp),
                "SourceTimestamp should be updated on each read");

            // Verify the second timestamp is recent
            Assert.That((long)secondValue.SourceTimestamp, Is.GreaterThanOrEqualTo((long)timeBeforeSecondRead.SubtractMilliseconds(1000)),
                "SourceTimestamp should be close to the second read time");
        }

        /// <summary>
        /// Test that ReferenceNodeManager array variables update their SourceTimestamp on read.
        /// </summary>
        [Test]
        public async Task ReferenceNodeManagerArrayVariablesUpdateTimestampOnReadAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ILogger logger = telemetry.CreateLogger<ReferenceServerTests>();

            // Read an array variable from the ReferenceNodeManager (namespace index 2)
            var nodeId = new NodeId("Scalar_Static_Arrays_Byte", 2);
            ArrayOf<ReadValueId> nodesToRead =
            [
                new ReadValueId
                {
                    NodeId = nodeId,
                    AttributeId = Attributes.Value
                }
            ];

            // First read
            RequestHeader requestHeader = m_requestHeader;
            requestHeader.Timestamp = DateTimeUtc.Now;
            DateTimeUtc timeBeforeFirstRead = DateTimeUtc.Now;
            ReadResponse firstReadResponse = await m_server.ReadAsync(
                m_secureChannelContext,
                requestHeader,
                kMaxAge,
                TimestampsToReturn.Both,
                nodesToRead,
                RequestLifetime.None).ConfigureAwait(false);

            Assert.That(firstReadResponse, Is.Not.Null);
            Assert.That(firstReadResponse.Results.IsNull, Is.False);
            Assert.That(firstReadResponse.Results.Count, Is.EqualTo(1));
            DataValue firstValue = firstReadResponse.Results[0];
            Assert.That(firstValue.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(firstValue.SourceTimestamp.IsNull, Is.False);
            logger.LogInformation("Array First read - SourceTimestamp: {SourceTimestamp}, ServerTimestamp: {ServerTimestamp}",
                firstValue.SourceTimestamp, firstValue.ServerTimestamp);

            // Verify the timestamp is recent (not startup time)
            Assert.That((long)firstValue.SourceTimestamp, Is.GreaterThanOrEqualTo((long)timeBeforeFirstRead.SubtractMilliseconds(1000)),
                "Array SourceTimestamp should be close to the read time, not the server startup time");

            // Wait a bit to ensure time difference
            await Task.Delay(1500).ConfigureAwait(false);

            // Second read
            requestHeader.Timestamp = DateTimeUtc.Now;
            DateTimeUtc timeBeforeSecondRead = DateTimeUtc.Now;
            ReadResponse secondReadResponse = await m_server.ReadAsync(
                m_secureChannelContext,
                requestHeader,
                kMaxAge,
                TimestampsToReturn.Both,
                nodesToRead,
                RequestLifetime.None).ConfigureAwait(false);

            Assert.That(secondReadResponse, Is.Not.Null);
            Assert.That(secondReadResponse.Results.IsNull, Is.False);
            Assert.That(secondReadResponse.Results.Count, Is.EqualTo(1));
            DataValue secondValue = secondReadResponse.Results[0];
            Assert.That(secondValue.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(secondValue.SourceTimestamp.IsNull, Is.False);
            logger.LogInformation("Array Second read - SourceTimestamp: {SourceTimestamp}, ServerTimestamp: {ServerTimestamp}",
                secondValue.SourceTimestamp, secondValue.ServerTimestamp);

            // Verify the second timestamp is more recent than the first
            Assert.That((long)secondValue.SourceTimestamp, Is.GreaterThan((long)firstValue.SourceTimestamp),
                "Array SourceTimestamp should be updated on each read");

            // Verify the second timestamp is recent
            Assert.That((long)secondValue.SourceTimestamp, Is.GreaterThanOrEqualTo((long)timeBeforeSecondRead.SubtractMilliseconds(1000)),
                "Array SourceTimestamp should be close to the second read time");
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
            var serverTestServices = new ServerTestServices(m_server, m_secureChannelContext);
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
            var serverTestServices = new ServerTestServices(m_server, m_secureChannelContext);
            if (m_operationLimits == null)
            {
                await GetOperationLimitsAsync().ConfigureAwait(false);
            }
            if (m_referenceDescriptions.IsNull)
            {
                m_referenceDescriptions = await CommonTestWorkers.BrowseFullAddressSpaceWorkerAsync(
                    serverTestServices,
                    m_requestHeader,
                    m_operationLimits).ConfigureAwait(false);
            }
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
            var serverTestServices = new ServerTestServices(m_server, m_secureChannelContext);
            await CommonTestWorkers.SubscriptionTestAsync(serverTestServices, m_requestHeader).ConfigureAwait(false);
        }

        /// <summary>
        /// Create a single session + subscription and raise a single event on the server node.
        /// Validates all the event fields are properly received.
        /// </summary>
        [Test]
        public async Task ServerEventSubscribeTestAsync()
        {
            var services = new ServerTestServices(m_server, m_secureChannelContext);
            RequestHeader requestHeader = m_requestHeader;
            requestHeader.Timestamp = DateTime.UtcNow;

            CreateSubscriptionResponse createSubscriptionResponse = await services.CreateSubscriptionAsync(
                requestHeader,
                100,
                100,
                10,
                0,
                true,
                0).ConfigureAwait(false);

            uint subscriptionId = createSubscriptionResponse.SubscriptionId;

            // Build an event filter for the base event type.
            var eventFilter = new EventFilter();
            eventFilter.AddSelectClause(
                ObjectTypeIds.BaseEventType,
                QualifiedName.From(BrowseNames.EventId));
            eventFilter.AddSelectClause(
                ObjectTypeIds.BaseEventType,
                QualifiedName.From(BrowseNames.EventType));
            eventFilter.AddSelectClause(
                ObjectTypeIds.BaseEventType,
                QualifiedName.From(BrowseNames.SourceNode));
            eventFilter.AddSelectClause(
                ObjectTypeIds.BaseEventType,
                QualifiedName.From(BrowseNames.SourceName));
            eventFilter.AddSelectClause(
                ObjectTypeIds.BaseEventType,
                QualifiedName.From(BrowseNames.Time));
            eventFilter.AddSelectClause(
                ObjectTypeIds.BaseEventType,
                QualifiedName.From(BrowseNames.Message));
            eventFilter.AddSelectClause(
                ObjectTypeIds.BaseEventType,
                QualifiedName.From(BrowseNames.Severity));

            ArrayOf<MonitoredItemCreateRequest> monitoredItems = [
                new MonitoredItemCreateRequest
                {
                    ItemToMonitor = new ReadValueId { NodeId = ObjectIds.Server, AttributeId = Attributes.EventNotifier },
                    MonitoringMode = MonitoringMode.Reporting,
                    RequestedParameters = new MonitoringParameters
                    {
                        ClientHandle = 1,
                        SamplingInterval = 0,
                        QueueSize = 100,
                        DiscardOldest = true,
                        Filter = new ExtensionObject(eventFilter)
                    }
                }
            ];

            CreateMonitoredItemsResponse createItemsResponse = await services.CreateMonitoredItemsAsync(
                requestHeader,
                subscriptionId,
                TimestampsToReturn.Both,
                monitoredItems).ConfigureAwait(false);

            ServerFixtureUtils.ValidateResponse(createItemsResponse.ResponseHeader, createItemsResponse.Results, monitoredItems);
            Assert.AreEqual(1, createItemsResponse.Results.Count);
            Assert.IsTrue(StatusCode.IsGood(createItemsResponse.Results[0].StatusCode));

            // Generate event directly on the server
            IServerInternal serverInternal = m_server.CurrentInstance;
            ISystemContext serverContext = serverInternal.DefaultSystemContext;
            using var e = new BaseEventState(null);
            const string eventMessage = "Integration Test Event";
            e.Initialize(
                serverContext,
                serverInternal.ServerObject,
                EventSeverity.Medium,
                new LocalizedText(eventMessage));
            serverInternal.ReportEvent(serverContext, e);

            // Wait for subscription to deliver the event
            await Task.Delay(200).ConfigureAwait(false);

            // Publish request to get the event notification
            var acknowledgements = new ArrayOf<SubscriptionAcknowledgement>();
            PublishResponse publishResponse = await services.PublishAsync(
                requestHeader,
                acknowledgements).ConfigureAwait(false);

            Assert.IsNotNull(publishResponse.NotificationMessage);
            Assert.IsNotNull(publishResponse.NotificationMessage.NotificationData);
            Assert.IsTrue(publishResponse.NotificationMessage.NotificationData.Count > 0);

            publishResponse.NotificationMessage.NotificationData[0].TryGetEncodeable(out EventNotificationList eventNotification);
            Assert.IsNotNull(eventNotification);
            Assert.IsTrue(eventNotification.Events.Count > 0);

            EventFieldList targetEvent = eventNotification.Events.ToList().FirstOrDefault(
                x => x.EventFields[5].TryGet(out LocalizedText lt) && lt.Text == eventMessage);
            Assert.IsNotNull(targetEvent, "Did not receive the target event.");

            ArrayOf<Variant> eventFields = targetEvent.EventFields;
            Assert.AreEqual(7, eventFields.Count); // we requested 7 fields in select clauses

            Assert.IsFalse(eventFields[0].IsNull); // EventId
            Assert.IsFalse(eventFields[1].IsNull); // EventType
            Assert.IsFalse(eventFields[2].IsNull); // SourceNode
            Assert.IsFalse(eventFields[3].IsNull); // SourceName
            Assert.IsFalse(eventFields[4].IsNull); // Time
            Assert.That(eventFields[5].TryGet(out LocalizedText receivedMessage), Is.True); // Message
            Assert.IsFalse(receivedMessage.IsNull);
            Assert.AreEqual(eventMessage, receivedMessage.Text);
            Assert.That(eventFields[6].TryGet(out ushort receiveSeverity), Is.True);
            Assert.AreEqual((ushort)EventSeverity.Medium, receiveSeverity); // Severity

            // Delete subscription
            await services.DeleteSubscriptionsAsync(
                requestHeader,
                [subscriptionId]).ConfigureAwait(false);
        }

        /// <summary>
        /// Create multiple sessions, each with a subscription.
        /// Close all sessions without deleting subscriptions (abandoning them).
        /// Concurrently delete the abandoned subscriptions from the main session.
        /// Verifies that the concurrent dictionary backing abandoned subscriptions
        /// handles parallel access correctly (fix for issue #3612).
        /// </summary>
        [Test]
        public async Task DeleteAbandonedSubscriptionsConcurrentlyAsync()
        {
            const int sessionCount = 5;
            var subscriptionIds = new List<uint>();

            NamespaceTable namespaceUris = m_server.CurrentInstance.NamespaceUris;
            NodeId[] testSet =
            [
                .. CommonTestWorkers.NodeIdTestSetStatic
                        .Select(n => ExpandedNodeId.ToNodeId(n, namespaceUris))
            ];

            // Create multiple sessions, each with a subscription, then close
            // the session without deleting subscriptions so they become abandoned.
            for (int i = 0; i < sessionCount; i++)
            {
                (RequestHeader header, SecureChannelContext context) =
                    await m_server.CreateAndActivateSessionAsync($"AbandonSession_{i}")
                        .ConfigureAwait(false);

                var services = new ServerTestServices(m_server, context);
                header.Timestamp = DateTimeUtc.Now;
                ArrayOf<uint> ids = await CommonTestWorkers.CreateSubscriptionForTransferAsync(
                    services, header, testSet, kQueueSize, -1).ConfigureAwait(false);
                subscriptionIds.AddRange(ids.ToList());

                // Close session without deleting subscriptions - makes them abandoned
                header.Timestamp = DateTimeUtc.Now;
                await m_server.CloseSessionAsync(context, header, false, RequestLifetime.None)
                    .ConfigureAwait(false);
            }

            // Concurrently delete all abandoned subscriptions from the main session.
            var mainServices = new ServerTestServices(m_server, m_secureChannelContext);
            var deleteTasks = new List<Task<DeleteSubscriptionsResponse>>();
            foreach (uint id in subscriptionIds)
            {
                ArrayOf<uint> singleId = [id];
                m_requestHeader.Timestamp = DateTimeUtc.Now;
                deleteTasks.Add(
                    mainServices.DeleteSubscriptionsAsync(m_requestHeader, singleId)
                        .AsTask());
            }

            DeleteSubscriptionsResponse[] responses = await Task.WhenAll(deleteTasks)
                .ConfigureAwait(false);

            // All deletions should succeed.
            foreach (DeleteSubscriptionsResponse response in responses)
            {
                Assert.AreEqual(StatusCodes.Good, response.ResponseHeader.ServiceResult);
                Assert.AreEqual(1, response.Results.Count);
                Assert.AreEqual(StatusCodes.Good, (uint)response.Results[0]);
            }
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

            var serverTestServices = new ServerTestServices(m_server, m_secureChannelContext);
            (RequestHeader transferRequestHeader, SecureChannelContext transferContext) = await m_server.CreateAndActivateSessionAsync(
                "ClosedSession",
                useSecurity).ConfigureAwait(false);
            NamespaceTable namespaceUris = m_server.CurrentInstance.NamespaceUris;
            NodeId[] testSet =
            [
                .. CommonTestWorkers.NodeIdTestSetStatic
                        .Select(n => ExpandedNodeId.ToNodeId(n, namespaceUris))
            ];
            transferRequestHeader.Timestamp = DateTimeUtc.Now;
            serverTestServices.SecureChannelContext = transferContext;
            ArrayOf<uint> subscriptionIds = await CommonTestWorkers.CreateSubscriptionForTransferAsync(
                serverTestServices,
                transferRequestHeader,
                testSet,
                kQueueSize,
                -1).ConfigureAwait(false);

            transferRequestHeader.Timestamp = DateTimeUtc.Now;
            await m_server.CloseSessionAsync(transferContext, transferRequestHeader, false, RequestLifetime.None).ConfigureAwait(false);

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
                ServiceResultException sre = Assert.ThrowsAsync<ServiceResultException>(async () =>
                {
                    m_requestHeader.Timestamp = DateTimeUtc.Now;
                    await CommonTestWorkers.VerifySubscriptionTransferredAsync(
                        serverTestServices,
                        m_requestHeader,
                        subscriptionIds,
                        true).ConfigureAwait(false);
                });
                Assert.That(
                    sre.StatusCode,
                    Is.EqualTo(StatusCodes.BadNoSubscription));
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

            var serverTestServices = new ServerTestServices(m_server, m_secureChannelContext);

            NamespaceTable namespaceUris = m_server.CurrentInstance.NamespaceUris;
            NodeId[] testSet =
            [
                .. CommonTestWorkers.NodeIdTestSetStatic
                        .Select(n => ExpandedNodeId.ToNodeId(n, namespaceUris))
            ];
            ArrayOf<uint> subscriptionIds = await CommonTestWorkers.CreateSubscriptionForTransferAsync(
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

            transferRequestHeader.Timestamp = DateTimeUtc.Now;
            await m_server.CloseSessionAsync(transferSecurityContext, transferRequestHeader, true, RequestLifetime.None).ConfigureAwait(false);
        }

        /// <summary>
        /// Create a subscription with a monitored item.
        /// Call ResendData.
        /// Ensure only a single value per monitored item is returned after ResendData was called.
        /// </summary>
        [Test]
        [TestCase(true, kQueueSize)]
        [TestCase(false, kQueueSize)]
        [TestCase(true, 0U)]
        [TestCase(false, 0U)]
        public async Task ResendDataAsync(bool updateValues, uint queueSize)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var serverTestServices = new ServerTestServices(m_server, m_secureChannelContext);

            NamespaceTable namespaceUris = m_server.CurrentInstance.NamespaceUris;
            var testSetCollection = CommonTestWorkers
                .NodeIdTestSetStatic.Select(n => ExpandedNodeId.ToNodeId(n, namespaceUris))
                .ToList();
            testSetCollection.AddRange(
                CommonTestWorkers.NodeIdTestDataSetStatic
                    .Select(n => ExpandedNodeId.ToNodeId(n, namespaceUris)));
            NodeId[] testSet = [.. testSetCollection];

            //Re-use method CreateSubscriptionForTransfer to create a subscription
            ArrayOf<uint> subscriptionIds = await CommonTestWorkers.CreateSubscriptionForTransferAsync(
                serverTestServices,
                m_requestHeader,
                testSet,
                queueSize,
                0).ConfigureAwait(false);

            (RequestHeader resendDataRequestHeader, SecureChannelContext resendDataSecurityContext) =
                await m_server.CreateAndActivateSessionAsync("ResendData").ConfigureAwait(false);

            serverTestServices.SecureChannelContext = m_secureChannelContext;
            // After the ResendData call there will be data to publish again
            ArrayOf<CallMethodRequest> nodesToCall = await ResendDataCallAsync(
                StatusCodes.Good,
                subscriptionIds).ConfigureAwait(false);

            Thread.Sleep(1000);

            // Make sure publish queue becomes empty by consuming it
            Assert.That(subscriptionIds.Count, Is.EqualTo(1));

            // Issue a Publish request
            m_requestHeader.Timestamp = DateTimeUtc.Now;
            ArrayOf<SubscriptionAcknowledgement> acknowledgements = [];
            PublishResponse publishResponse = await serverTestServices.PublishAsync(
                m_requestHeader,
                acknowledgements).ConfigureAwait(false);

            Assert.That(publishResponse.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
            ServerFixtureUtils.ValidateResponse(publishResponse.ResponseHeader);
            ServerFixtureUtils.ValidateDiagnosticInfos(
                publishResponse.DiagnosticInfos,
                acknowledgements,
                publishResponse.ResponseHeader.StringTable,
                serverTestServices.Logger);
            Assert.That(publishResponse.SubscriptionId, Is.EqualTo(subscriptionIds[0]));
            Assert.That(publishResponse.NotificationMessage.NotificationData.Count, Is.EqualTo(1));

            // Validate nothing to publish a few times
            const int timesToCallPublish = 3;
            for (int i = 0; i < timesToCallPublish; i++)
            {
                m_requestHeader.Timestamp = DateTimeUtc.Now;
                publishResponse = await serverTestServices.PublishAsync(
                    m_requestHeader,
                    acknowledgements).ConfigureAwait(false);

                Assert.That(publishResponse.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
                ServerFixtureUtils.ValidateResponse(publishResponse.ResponseHeader);
                ServerFixtureUtils.ValidateDiagnosticInfos(
                    publishResponse.DiagnosticInfos,
                    acknowledgements,
                    publishResponse.ResponseHeader.StringTable,
                    serverTestServices.Logger);
                Assert.That(publishResponse.SubscriptionId, Is.EqualTo(subscriptionIds[0]));
                Assert.That(publishResponse.NotificationMessage.NotificationData.Count, Is.EqualTo(0));
            }

            // Validate ResendData method call returns error from different session contexts

            // call ResendData method from different session context
            resendDataRequestHeader.Timestamp = DateTimeUtc.Now;
            CallResponse callResponse = await m_server.CallAsync(
                resendDataSecurityContext,
                resendDataRequestHeader,
                nodesToCall, RequestLifetime.None).ConfigureAwait(false);

            serverTestServices.SecureChannelContext = m_secureChannelContext;

            Assert.That(callResponse.Results[0].StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            ServerFixtureUtils.ValidateResponse(callResponse.ResponseHeader, callResponse.Results, nodesToCall);
            ServerFixtureUtils.ValidateDiagnosticInfos(
                callResponse.DiagnosticInfos,
                nodesToCall,
                callResponse.ResponseHeader.StringTable,
                serverTestServices.Logger);

            // Still nothing to publish since previous ResendData call did not execute
            m_requestHeader.Timestamp = DateTimeUtc.Now;
            publishResponse = await serverTestServices.PublishAsync(
                m_requestHeader,
                acknowledgements).ConfigureAwait(false);

            Assert.That(publishResponse.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
            ServerFixtureUtils.ValidateResponse(publishResponse.ResponseHeader);
            ServerFixtureUtils.ValidateDiagnosticInfos(
                publishResponse.DiagnosticInfos,
                acknowledgements,
                publishResponse.ResponseHeader.StringTable,
                serverTestServices.Logger);
            Assert.That(publishResponse.SubscriptionId, Is.EqualTo(subscriptionIds[0]));
            Assert.That(publishResponse.NotificationMessage.NotificationData.Count, Is.EqualTo(0));

            if (updateValues)
            {
                await UpdateValuesAsync(testSet).ConfigureAwait(false);

                // fill queues, but only a single value per resend publish shall be returned
                for (int i = 1; i < queueSize; i++)
                {
                    //If sampling groups are used, samplingInterval needs to be waited before values are queued
                    if (m_fixture.UseSamplingGroupsInReferenceNodeManager)
                    {
                        await Task.Delay((int)(100.0 * 1.7)).ConfigureAwait(false);
                    }
                    await UpdateValuesAsync(testSet).ConfigureAwait(false);
                }

                // Wait a bit to ensure that the server has time to queue the values
                await Task.Delay(1000).ConfigureAwait(false);
            }

            // call ResendData method from the same session context
            await ResendDataCallAsync(StatusCodes.Good, subscriptionIds).ConfigureAwait(false);

            // Data should be available for publishing now
            int totalNotifications = await CollectNotificationsAsync(
                serverTestServices,
                m_requestHeader,
                acknowledgements,
                subscriptionIds[0],
                testSet.Length).ConfigureAwait(false);

            Assert.That(totalNotifications, Is.EqualTo(testSet.Length),
                "One MonitoredItemNotification should be returned for each Node present in the TestSet");

            await Task.Delay(1000).ConfigureAwait(false);

            if (updateValues && queueSize > 1)
            {
                // remaining queue Data should be sent in this publish
                int expectedCount = testSet.Length * ((int)queueSize - 1);
                totalNotifications = await CollectNotificationsAsync(
                    serverTestServices,
                    m_requestHeader,
                    acknowledgements,
                    subscriptionIds[0],
                    expectedCount).ConfigureAwait(false);

                Assert.That(
                    totalNotifications,
                    Is.EqualTo(expectedCount).Within(testSet.Length));
            }

            // Call ResendData method with invalid subscription Id
            await ResendDataCallAsync(StatusCodes.BadSubscriptionIdInvalid, [subscriptionIds[^1] + 20]).ConfigureAwait(false);

            // Nothing to publish since previous ResendData call did not execute
            m_requestHeader.Timestamp = DateTimeUtc.Now;
            publishResponse = await serverTestServices.PublishAsync(
                m_requestHeader,
                acknowledgements).ConfigureAwait(false);

            Assert.That(publishResponse.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
            ServerFixtureUtils.ValidateResponse(publishResponse.ResponseHeader);
            ServerFixtureUtils.ValidateDiagnosticInfos(
                publishResponse.DiagnosticInfos,
                acknowledgements,
                publishResponse.ResponseHeader.StringTable,
                serverTestServices.Logger);
            Assert.That(publishResponse.SubscriptionId, Is.EqualTo(subscriptionIds[0]));
            Assert.That(publishResponse.NotificationMessage.NotificationData.Count, Is.EqualTo(0));

            resendDataRequestHeader.Timestamp = DateTimeUtc.Now;
            await m_server.CloseSessionAsync(resendDataSecurityContext, resendDataRequestHeader, true, RequestLifetime.None).ConfigureAwait(false);
        }

        private static async Task<int> CollectNotificationsAsync(
            ServerTestServices serverTestServices,
            RequestHeader requestHeader,
            ArrayOf<SubscriptionAcknowledgement> acknowledgements,
            uint subscriptionId,
            int expectedCount)
        {
            int totalNotifications = 0;
            int retries = 50;
            while (totalNotifications < expectedCount && retries-- > 0)
            {
                requestHeader.Timestamp = DateTime.UtcNow;
                PublishResponse publishResponse = await serverTestServices.PublishAsync(
                    requestHeader,
                    acknowledgements).ConfigureAwait(false);

                Assert.That(publishResponse.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
                ServerFixtureUtils.ValidateResponse(publishResponse.ResponseHeader);
                Assert.That(publishResponse.SubscriptionId, Is.EqualTo(subscriptionId));

                if (publishResponse.NotificationMessage.NotificationData.Count > 0)
                {
                    // acknowledge the notification
                    acknowledgements =
                    [
                        new SubscriptionAcknowledgement
                        {
                            SubscriptionId = subscriptionId,
                            SequenceNumber = publishResponse.NotificationMessage.SequenceNumber
                        }
                    ];

                    foreach (ExtensionObject item in publishResponse.NotificationMessage.NotificationData)
                    {
                        if (item.TryGetEncodeable(out DataChangeNotification dcn))
                        {
                            totalNotifications += dcn.MonitoredItems.Count;
                        }
                    }
                }

                if (totalNotifications >= expectedCount)
                {
                    break;
                }

                if (!publishResponse.MoreNotifications)
                {
                    // Wait for potential background processing
                    await Task.Delay(100).ConfigureAwait(false);
                }
            }
            return totalNotifications;
        }

        private async Task<ArrayOf<CallMethodRequest>> ResendDataCallAsync(
            StatusCode expectedStatus,
            ArrayOf<uint> subscriptionIds)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ILogger logger = telemetry.CreateLogger<ReferenceServerTests>();

            // Find the ResendData method
            ArrayOf<CallMethodRequest> nodesToCall = subscriptionIds
                .ConvertAll(subscriptionId => new CallMethodRequest
                {
                    ObjectId = ObjectIds.Server,
                    MethodId = MethodIds.Server_ResendData,
                    InputArguments = [new Variant(subscriptionId)]
                });

            //call ResendData method with subscription ids
            m_requestHeader.Timestamp = DateTimeUtc.Now;
            CallResponse callResponse = await m_server.CallAsync(
                m_secureChannelContext,
                m_requestHeader,
                nodesToCall, RequestLifetime.None).ConfigureAwait(false);

            Assert.That(callResponse.Results[0].StatusCode, Is.EqualTo(expectedStatus));
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
            var nodesToRead = testSet
                .Select(nodeId => new ReadValueId
                {
                    NodeId = nodeId,
                    AttributeId = Attributes.Value
                })
                .ToArrayOf();

            ReadResponse readResponse = await m_server.ReadAsync(
                m_secureChannelContext,
                requestHeader,
                kMaxAge,
                TimestampsToReturn.Neither,
                nodesToRead, RequestLifetime.None).ConfigureAwait(false);

            ServerFixtureUtils.ValidateResponse(readResponse.ResponseHeader, readResponse.Results, nodesToRead);
            ServerFixtureUtils.ValidateDiagnosticInfos(
                readResponse.DiagnosticInfos,
                readResponse.Results,
                readResponse.ResponseHeader.StringTable,
                logger);
            Assert.That(readResponse.Results.Count, Is.EqualTo(testSet.Length));

            var modifiedValues = new List<DataValue>();
            foreach (DataValue dataValue in readResponse.Results)
            {
                TypeInfo typeInfo = dataValue.WrappedValue.TypeInfo;
                Assert.That(typeInfo.IsUnknown, Is.False);
                Variant value = m_generator.GetRandomScalar(typeInfo.BuiltInType);
                modifiedValues.Add(new DataValue { WrappedValue = value });
            }

            int ii = 0;
            var nodesToWrite = testSet
                .Select(nodeId => new WriteValue
                {
                    NodeId = nodeId,
                    AttributeId = Attributes.Value,
                    Value = modifiedValues[ii++]
                })
                .ToArrayOf();

            // Write Nodes
            requestHeader.Timestamp = DateTimeUtc.Now;
            WriteResponse writeResponse = await m_server.WriteAsync(
                m_secureChannelContext,
                requestHeader,
                nodesToWrite, RequestLifetime.None).ConfigureAwait(false);
            ServerFixtureUtils.ValidateResponse(writeResponse.ResponseHeader, writeResponse.Results, nodesToWrite);
            ServerFixtureUtils.ValidateDiagnosticInfos(
                writeResponse.DiagnosticInfos,
                writeResponse.Results,
                writeResponse.ResponseHeader.StringTable,
                logger);
        }

        [Test]
        public async Task SemanticChangeNotificationTestAsync()
        {
            var services = new ServerTestServices(m_server, m_secureChannelContext);
            RequestHeader requestHeader = m_requestHeader;
            requestHeader.Timestamp = DateTime.UtcNow;

            // Step 1: Create a subscription
            CreateSubscriptionResponse createSubscriptionResponse = await services.CreateSubscriptionAsync(
                requestHeader,
                100,
                100,
                10,
                0,
                true,
                0).ConfigureAwait(false);

            uint subscriptionId = createSubscriptionResponse.SubscriptionId;

            // We monitor the server EventNotifier for SemanticChangeEventType
            var serverObject = new ReadValueId { NodeId = ObjectIds.Server, AttributeId = Attributes.EventNotifier };
            var eventFilter = new EventFilter();
            eventFilter.AddSelectClause(
                ObjectTypeIds.SemanticChangeEventType,
                QualifiedName.From(BrowseNames.EventId));
            eventFilter.AddSelectClause(
                ObjectTypeIds.SemanticChangeEventType,
                QualifiedName.From(BrowseNames.Changes));

            // Monitor a variable from ReferenceNodeManager (AnalogItem with EngineeringUnits)
            var nodeId = new NodeId("DataAccess_AnalogType_Double",
                (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(Quickstarts.ReferenceServer.Namespaces.ReferenceServer));

            ArrayOf<MonitoredItemCreateRequest> monitoredItems =
            [
                new MonitoredItemCreateRequest
                {
                    ItemToMonitor = serverObject,
                    MonitoringMode = MonitoringMode.Reporting,
                    RequestedParameters = new MonitoringParameters
                    {
                        ClientHandle = 1,
                        SamplingInterval = 0,
                        QueueSize = 100,
                        DiscardOldest = true,
                        Filter = new ExtensionObject(eventFilter)
                    }
                },
                new MonitoredItemCreateRequest
                {
                    ItemToMonitor = new ReadValueId { NodeId = nodeId, AttributeId = Attributes.Value },
                    MonitoringMode = MonitoringMode.Reporting,
                    RequestedParameters = new MonitoringParameters
                    {
                        ClientHandle = 2,
                        SamplingInterval = 0,
                        QueueSize = 10,
                        DiscardOldest = true
                    }
                }
            ];

            CreateMonitoredItemsResponse createItemsResponse = await services.CreateMonitoredItemsAsync(
                requestHeader,
                subscriptionId,
                TimestampsToReturn.Both,
                monitoredItems).ConfigureAwait(false);

            ServerFixtureUtils.ValidateResponse(createItemsResponse.ResponseHeader, createItemsResponse.Results, monitoredItems);
            Assert.AreEqual(2, createItemsResponse.Results.Count);
            Assert.IsTrue(StatusCode.IsGood(createItemsResponse.Results[0].StatusCode));
            Assert.IsTrue(StatusCode.IsGood(createItemsResponse.Results[1].StatusCode));

            // Initial publish to clear any initial data change notifications
            var acknowledgements = new ArrayOf<SubscriptionAcknowledgement>();
            PublishResponse publishResponse = await services.PublishAsync(requestHeader, acknowledgements).ConfigureAwait(false);

            if (publishResponse.NotificationMessage.NotificationData.Count > 0)
            {
                acknowledgements =
                [
                    new SubscriptionAcknowledgement
                   {
                       SubscriptionId = subscriptionId,
                       SequenceNumber = publishResponse.NotificationMessage.SequenceNumber
                   }
                ];
            }

            // Step 2: Write a new value to a semantic property (EngineeringUnits)
            var euNodeId = new NodeId("DataAccess_AnalogType_DataAccess_AnalogType_Double_EngineeringUnits",
                (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(Quickstarts.ReferenceServer.Namespaces.ReferenceServer));
            var engUnits = new EUInformation("V", "volt", "http://www.opcfoundation.org/UA/units/un/cefact")
            {
                UnitId = 4274026 // "V"
            };

            var writeValues = new WriteValue[]
            {
                new WriteValue
                {
                    NodeId = euNodeId,
                    AttributeId = Attributes.Value,
                    Value = new DataValue(new ExtensionObject(engUnits))
                }
            }.ToArrayOf();

            requestHeader.Timestamp = DateTime.UtcNow;
            WriteResponse writeResponse = await m_server.WriteAsync(
                m_secureChannelContext,
                requestHeader,
                writeValues, RequestLifetime.None).ConfigureAwait(false);

            ServerFixtureUtils.ValidateResponse(writeResponse.ResponseHeader, writeResponse.Results, writeValues);
            Assert.IsTrue(StatusCode.IsGood(writeResponse.Results[0]));

            // Wait for subscription to deliver the event and data change notification
            await Task.Delay(200).ConfigureAwait(false);

            // Step 3: Issue publish to receive DataChangeNotification and SemanticChangeEvent
            publishResponse = await services.PublishAsync(
                requestHeader,
                acknowledgements).ConfigureAwait(false);

            Assert.IsNotNull(publishResponse.NotificationMessage);
            Assert.IsNotNull(publishResponse.NotificationMessage.NotificationData);
            Assert.IsTrue(publishResponse.NotificationMessage.NotificationData.Count > 0);

            bool dataChangeReceived = false;
            bool semanticsChangedBitSet = false;
            bool eventReceived = false;

            foreach (ExtensionObject data in publishResponse.NotificationMessage.NotificationData)
            {
                if (data.TryGetEncodeable(out DataChangeNotification dcn))
                {
                    foreach (MonitoredItemNotification item in dcn.MonitoredItems)
                    {
                        if (item.ClientHandle == 2)
                        {
                            dataChangeReceived = true;
                            if (item.Value.StatusCode.SemanticsChanged)
                            {
                                semanticsChangedBitSet = true;
                            }
                        }
                    }
                }
                else if (data.TryGetEncodeable(out EventNotificationList enl))
                {
                    foreach (EventFieldList e in enl.Events)
                    {
                        if (e.ClientHandle == 1 && e.EventFields.Count >= 2)
                        {
                            var success = e.EventFields[1]
                                .TryGetStructure<SemanticChangeStructureDataType>(out ArrayOf<SemanticChangeStructureDataType> semanticChangeEvent);

                            if (success && !semanticChangeEvent.IsNull && semanticChangeEvent.Count > 0)
                            {
                                foreach (SemanticChangeStructureDataType change in semanticChangeEvent)
                                {
                                    if (change.Affected == nodeId)
                                    {
                                        eventReceived = true;
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            Assert.IsTrue(dataChangeReceived, "Did not receive DataChangeNotification.");
            Assert.IsTrue(semanticsChangedBitSet, "SemanticsChanged bit is not set.");
            Assert.IsTrue(eventReceived, "Did not receive SemanticChangeEvent.");

            // Delete subscription
            await services.DeleteSubscriptionsAsync(
                requestHeader,
                [subscriptionId]).ConfigureAwait(false);
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
            ArrayOf<ReadValueId> readIdCollection =
            [
                new ReadValueId
                {
                    AttributeId = Attributes.EventNotifier,
                    NodeId = ObjectIds.Server
                }
            ];

            m_requestHeader.Timestamp = DateTimeUtc.Now;
            ReadResponse readResponse = await m_server.ReadAsync(
                m_secureChannelContext,
                m_requestHeader,
                0,
                TimestampsToReturn.Both,
                readIdCollection, RequestLifetime.None).ConfigureAwait(false);

            ServerFixtureUtils.ValidateResponse(readResponse.ResponseHeader, readResponse.Results, readIdCollection);
            Assert.That(readResponse.Results.Count, Is.EqualTo(1));
            Assert.That(readResponse.Results[0].WrappedValue.IsNull, Is.False);

            byte eventNotifier = (byte)readResponse.Results[0].WrappedValue;

            // Read history capabilities
            ArrayOf<ReadValueId> historyCapabilitiesReadIds =
            [
                new ReadValueId {
                    AttributeId = Attributes.Value,
                    NodeId = VariableIds.HistoryServerCapabilities_AccessHistoryEventsCapability
                },
                new ReadValueId {
                    AttributeId = Attributes.Value,
                    NodeId = VariableIds.HistoryServerCapabilities_AccessHistoryDataCapability
                }
            ];

            m_requestHeader.Timestamp = DateTimeUtc.Now;
            readResponse = await m_server.ReadAsync(
                m_secureChannelContext,
                m_requestHeader,
                0,
                TimestampsToReturn.Both,
                historyCapabilitiesReadIds, RequestLifetime.None).ConfigureAwait(false);

            ServerFixtureUtils.ValidateResponse(readResponse.ResponseHeader, readResponse.Results, historyCapabilitiesReadIds);
            Assert.That(readResponse.Results.Count, Is.EqualTo(2));

            bool accessHistoryEventsCapability =
                !readResponse.Results[0].WrappedValue.IsNull &&
                (bool)readResponse.Results[0].WrappedValue;
            bool accessHistoryDataCapability =
                !readResponse.Results[1].WrappedValue.IsNull &&
                (bool)readResponse.Results[1].WrappedValue;

            logger.LogInformation("Server EventNotifier: {EventNotifier}", eventNotifier);
            logger.LogInformation("AccessHistoryEventsCapability: {AccessHistoryEventsCapability}", accessHistoryEventsCapability);
            logger.LogInformation("AccessHistoryDataCapability: {AccessHistoryDataCapability}", accessHistoryDataCapability);

            // If either history capability is enabled, the HistoryRead bit should be set
            if (accessHistoryEventsCapability || accessHistoryDataCapability)
            {
                Assert.That(eventNotifier & EventNotifiers.HistoryRead,
                    Is.Not.EqualTo(0),
                    "Server EventNotifier should have HistoryRead bit set when history capabilities are enabled");
            }

            // Verify SubscribeToEvents bit is set (Server object should always support events)
            Assert.That(eventNotifier & EventNotifiers.SubscribeToEvents,
                Is.Not.EqualTo(0),
                "Server EventNotifier should have SubscribeToEvents bit set");
        }

        /// <summary>
        /// Verify that ServerStatus children have matching SourceTimestamp and ServerTimestamp.
        /// </summary>
        [Test]
        public async Task ServerStatusTimestampsMatchAsync()
        {
            ILogger<ReferenceServerTests> logger = m_telemetry.CreateLogger<ReferenceServerTests>();

            // Read ServerStatus children (CurrentTime, StartTime, State, etc.)
            ArrayOf<ReadValueId> nodesToRead =
            [
                new ReadValueId { NodeId = VariableIds.Server_ServerStatus_CurrentTime, AttributeId = Attributes.Value },
                new ReadValueId { NodeId = VariableIds.Server_ServerStatus_StartTime, AttributeId = Attributes.Value },
                new ReadValueId { NodeId = VariableIds.Server_ServerStatus_State, AttributeId = Attributes.Value }
            ];

            m_requestHeader.Timestamp = DateTimeUtc.Now;
            ReadResponse readResponse = await m_server.ReadAsync(
                m_secureChannelContext,
                m_requestHeader,
                0,
                TimestampsToReturn.Both,
                nodesToRead,
                RequestLifetime.None).ConfigureAwait(false);

            ServerFixtureUtils.ValidateResponse(readResponse.ResponseHeader, readResponse.Results, nodesToRead);
            Assert.That(readResponse.Results.Count, Is.EqualTo(3));

            // Verify that SourceTimestamp and ServerTimestamp are equal for all ServerStatus children
            for (int i = 0; i < readResponse.Results.Count; i++)
            {
                DataValue result = readResponse.Results[i];
                logger.LogInformation(
                    "NodeId: {NodeId}, SourceTimestamp: {SourceTimestamp}, ServerTimestamp: {ServerTimestamp}",
                    nodesToRead[i].NodeId,
                    result.SourceTimestamp,
                    result.ServerTimestamp);

                Assert.That(result.ServerTimestamp, Is.EqualTo(result.SourceTimestamp),
                    $"SourceTimestamp and ServerTimestamp should be equal for {nodesToRead[i].NodeId}");
            }
        }

        /// <summary>
        /// Test that the Int32Value node (ns=3;i=2808) allows historical data access.
        /// Verifies the fix for issue #2520 where the node was marked as historizing
        /// but history read operations returned BadHistoryOperationUnsupported.
        /// </summary>
        [Test]
        public async Task HistoryReadInt32ValueNodeAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ILogger logger = telemetry.CreateLogger<ReferenceServerTests>();

            // Get the NodeId for Data_Dynamic_Scalar_Int32Value
            var int32ValueNodeId = new NodeId(
                TestData.Variables.Data_Dynamic_Scalar_Int32Value,
                (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(TestData.Namespaces.TestData));

            logger.LogInformation("Testing history read for Int32Value node: {NodeId}", int32ValueNodeId);

            // Verify the node has Historizing attribute set to true
            ArrayOf<ReadValueId> readIdCollection =
            [
                new ReadValueId
                {
                    AttributeId = Attributes.Historizing,
                    NodeId = int32ValueNodeId
                },
                new ReadValueId
                {
                    AttributeId = Attributes.AccessLevel,
                    NodeId = int32ValueNodeId
                }
            ];

            m_requestHeader.Timestamp = DateTimeUtc.Now;
            ReadResponse readResponse = await m_server.ReadAsync(
                m_secureChannelContext,
                m_requestHeader,
                kMaxAge,
                TimestampsToReturn.Neither,
                readIdCollection,
                RequestLifetime.None).ConfigureAwait(false);

            ServerFixtureUtils.ValidateResponse(readResponse.ResponseHeader, readResponse.Results, readIdCollection);
            Assert.That(readResponse.Results.Count, Is.EqualTo(2));

            bool historizing = (bool)readResponse.Results[0].WrappedValue;
            byte accessLevel = (byte)readResponse.Results[1].WrappedValue;

            logger.LogInformation("Historizing: {Historizing}, AccessLevel: {AccessLevel}", historizing, accessLevel);

            Assert.That(historizing, Is.True, "Int32Value node should have Historizing=true");
            Assert.That(accessLevel & AccessLevels.HistoryRead,
                Is.Not.EqualTo(0),
                "Int32Value node should have HistoryRead access level");

            // Perform a history read operation
            var historyReadDetails = new ReadRawModifiedDetails
            {
                StartTime = DateTimeUtc.Now.SubtractMilliseconds(60 * 60 * 1000),
                EndTime = DateTimeUtc.Now,
                NumValuesPerNode = 10,
                IsReadModified = false,
                ReturnBounds = false
            };

            ArrayOf<HistoryReadValueId> nodesToRead =
            [
                new HistoryReadValueId
                {
                    NodeId = int32ValueNodeId
                }
            ];

            m_requestHeader.Timestamp = DateTimeUtc.Now;
            HistoryReadResponse historyReadResponse = await m_server.HistoryReadAsync(
                m_secureChannelContext,
                m_requestHeader,
                new ExtensionObject(historyReadDetails),
                TimestampsToReturn.Both,
                false,
                nodesToRead,
                RequestLifetime.None).ConfigureAwait(false);

            ServerFixtureUtils.ValidateResponse(historyReadResponse.ResponseHeader, historyReadResponse.Results, nodesToRead);
            Assert.That(historyReadResponse.Results.Count, Is.EqualTo(1));

            HistoryReadResult result = historyReadResponse.Results[0];

            logger.LogInformation("History read StatusCode: {StatusCode}", result.StatusCode);

            // The result should be Good or GoodMoreData (if there are more values)
            Assert.That(StatusCode.IsGood(result.StatusCode),
                Is.True,
                $"History read should succeed, but got: {result.StatusCode}");
            Assert.That(result.HistoryData.IsNull, Is.False, "HistoryData should not be null");

            // Verify we got HistoryData back
            if (result.HistoryData.TryGetEncodeable(out HistoryData historyData))
            {
                logger.LogInformation("Retrieved {Count} history values", historyData.DataValues.Count);
                Assert.That(historyData.DataValues.IsNull, Is.False, "DataValues should not be null");
                Assert.That(historyData.DataValues.Count, Is.GreaterThan(0), "Should have at least one historical value");

                // Verify the data values have proper timestamps
                foreach (DataValue dataValue in historyData.DataValues)
                {
                    Assert.That(dataValue, Is.Not.Null, "DataValue should not be null");
                    Assert.That(dataValue.ServerTimestamp,
                        Is.Not.EqualTo(DateTimeUtc.MinValue),
                        "DataValue should have a valid ServerTimestamp");
                }
            }
            else
            {
                Assert.Fail("HistoryData body should be of type HistoryData");
            }
        }

        /// <summary>
        /// Test provisioning mode - server should start with limited namespace.
        /// </summary>
        [Test]
        public async Task ProvisioningModeTestAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // start Ref server in provisioning mode
            var fixture = new ServerFixture<ReferenceServer>(t => new ReferenceServer(t))
            {
                AllNodeManagers = false,
                OperationLimits = false,
                DurableSubscriptionsEnabled = false,
                AutoAccept = true,
                ProvisioningMode = true
            };

            ReferenceServer server = await fixture.StartAsync().ConfigureAwait(false);

            // Verify provisioning mode is enabled
            Assert.That(server.ProvisioningMode, Is.True, "Server should be in provisioning mode");

            // Get endpoints - in provisioning mode, anonymous authentication should not be allowed
            ArrayOf<EndpointDescription> endpoints = server.GetEndpoints();
            Assert.That(endpoints.IsNull, Is.False);
            Assert.That(endpoints.Count, Is.GreaterThan(0), "Server should have endpoints");

            // Check that anonymous token policy is not present for at least one endpoint
            bool hasEndpointWithoutAnonymous = false;
            foreach (EndpointDescription endpoint in endpoints)
            {
                bool hasAnonymous = endpoint.UserIdentityTokens.Contains(
                    policy => policy.TokenType == UserTokenType.Anonymous);
                if (!hasAnonymous)
                {
                    hasEndpointWithoutAnonymous = true;
                    break;
                }
            }
            Assert.That(hasEndpointWithoutAnonymous,
                Is.True,
                "At least one endpoint should not allow anonymous authentication in provisioning mode");

            // Clean up
            await fixture.StopAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Reads the TypeDefinitionId of a node and checks it matches the expected value.
        /// </summary>
        private async Task ReadAndVerifyTypeDefinitionAsync(NodeId nodeId, NodeId expectedTypeDefinitionId)
        {
            ArrayOf<ReadValueId> nodesToRead =
            [
                new ReadValueId { NodeId = nodeId, AttributeId = Attributes.Value }
            ];
            m_requestHeader.Timestamp = DateTimeUtc.Now;
            ReadResponse readResponse = await m_server.ReadAsync(
                m_secureChannelContext,
                m_requestHeader,
                kMaxAge,
                TimestampsToReturn.Neither,
                nodesToRead,
                RequestLifetime.None).ConfigureAwait(false);
            ServerFixtureUtils.ValidateResponse(readResponse.ResponseHeader, readResponse.Results, nodesToRead);
            Assert.That(readResponse.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good),
                $"Expected Good status reading {nodeId}");
        }

        /// <summary>
        /// Test that ArrayItemType sub-type nodes are accessible and readable.
        /// </summary>
        [Test]
        public async Task ArrayItemTypeNodesExistAndReadableAsync()
        {
            var nodeIds = new Dictionary<string, NodeId>
            {
                { "YArray", new NodeId("DataAccess_ArrayItemType_YArray", 2) },
                { "XYArray", new NodeId("DataAccess_ArrayItemType_XYArray", 2) },
                { "Image", new NodeId("DataAccess_ArrayItemType_Image", 2) },
                { "Cube", new NodeId("DataAccess_ArrayItemType_Cube", 2) },
                { "NDimension", new NodeId("DataAccess_ArrayItemType_NDimension", 2) }
            };

            foreach (var (name, nodeId) in nodeIds)
            {
                m_requestHeader.Timestamp = DateTimeUtc.Now;
                ArrayOf<ReadValueId> nodesToRead =
                [
                    new ReadValueId { NodeId = nodeId, AttributeId = Attributes.Value },
                    new ReadValueId { NodeId = nodeId, AttributeId = Attributes.DataType },
                    new ReadValueId { NodeId = nodeId, AttributeId = Attributes.ValueRank }
                ];
                ReadResponse readResponse = await m_server.ReadAsync(
                    m_secureChannelContext,
                    m_requestHeader,
                    kMaxAge,
                    TimestampsToReturn.Neither,
                    nodesToRead,
                    RequestLifetime.None).ConfigureAwait(false);

                ServerFixtureUtils.ValidateResponse(readResponse.ResponseHeader, readResponse.Results, nodesToRead);
                Assert.That(readResponse.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good),
                    $"Value read of {name} should succeed");
                Assert.That(readResponse.Results[1].StatusCode, Is.EqualTo(StatusCodes.Good),
                    $"DataType read of {name} should succeed");
                Assert.That(readResponse.Results[2].StatusCode, Is.EqualTo(StatusCodes.Good),
                    $"ValueRank read of {name} should succeed");

                TestContext.Out.WriteLine("{0}: DataType={1}, ValueRank={2}",
                    name,
                    readResponse.Results[1].WrappedValue,
                    readResponse.Results[2].WrappedValue);
            }
        }

        /// <summary>
        /// Test that TriggerNode01 and TriggerNode02 exist, are readable and writable,
        /// and that writing to them fires a BaseEvent.
        /// </summary>
        [Test]
        public async Task TriggerNodesFiringEventsOnWriteAsync()
        {
            var triggerNode01 = new NodeId("NodeIds_Events_TriggerNode01", 2);
            var triggerNode02 = new NodeId("NodeIds_Events_TriggerNode02", 2);

            // Verify both nodes exist and are readable
            foreach (NodeId nodeId in new[] { triggerNode01, triggerNode02 })
            {
                m_requestHeader.Timestamp = DateTimeUtc.Now;
                ArrayOf<ReadValueId> nodesToRead =
                [
                    new ReadValueId { NodeId = nodeId, AttributeId = Attributes.Value }
                ];
                ReadResponse readResponse = await m_server.ReadAsync(
                    m_secureChannelContext,
                    m_requestHeader,
                    kMaxAge,
                    TimestampsToReturn.Neither,
                    nodesToRead,
                    RequestLifetime.None).ConfigureAwait(false);

                ServerFixtureUtils.ValidateResponse(readResponse.ResponseHeader, readResponse.Results, nodesToRead);
                Assert.That(readResponse.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good),
                    $"Read of trigger node {nodeId} should succeed");
            }

            // Verify both nodes are writable
            foreach (NodeId nodeId in new[] { triggerNode01, triggerNode02 })
            {
                m_requestHeader.Timestamp = DateTimeUtc.Now;
                ArrayOf<WriteValue> nodesToWrite =
                [
                    new WriteValue
                    {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(42)
                    }
                ];
                WriteResponse writeResponse = await m_server.WriteAsync(
                    m_secureChannelContext,
                    m_requestHeader,
                    nodesToWrite,
                    RequestLifetime.None).ConfigureAwait(false);

                ServerFixtureUtils.ValidateResponse(writeResponse.ResponseHeader, writeResponse.Results, nodesToWrite);
                Assert.That(writeResponse.Results[0], Is.EqualTo(StatusCodes.Good),
                    $"Write to trigger node {nodeId} should succeed and fire an event");
            }
        }

        /// <summary>
        /// Test that the Variant arrays (1D and 2D) are accessible with proper NodeIds.
        /// </summary>
        [Test]
        public async Task VariantArrayNodesExistAndReadableAsync()
        {
            var nodeIds = new Dictionary<string, NodeId>
            {
                { "Scalar_Static_Arrays_Variant", new NodeId("Scalar_Static_Arrays_Variant", 2) },
                { "Scalar_Static_Arrays2D_Variant", new NodeId("Scalar_Static_Arrays2D_Variant", 2) }
            };

            foreach (var (name, nodeId) in nodeIds)
            {
                m_requestHeader.Timestamp = DateTimeUtc.Now;
                ArrayOf<ReadValueId> nodesToRead =
                [
                    new ReadValueId { NodeId = nodeId, AttributeId = Attributes.Value }
                ];
                ReadResponse readResponse = await m_server.ReadAsync(
                    m_secureChannelContext,
                    m_requestHeader,
                    kMaxAge,
                    TimestampsToReturn.Neither,
                    nodesToRead,
                    RequestLifetime.None).ConfigureAwait(false);

                ServerFixtureUtils.ValidateResponse(readResponse.ResponseHeader, readResponse.Results, nodesToRead);
                Assert.That(readResponse.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good),
                    $"Read of {name} should succeed");
            }
        }
    }
}
