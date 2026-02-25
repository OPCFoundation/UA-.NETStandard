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
            requestHeader.Timestamp = DateTime.UtcNow;
            ReadResponse readResponse = await m_server.ReadAsync(
                m_secureChannelContext,
                requestHeader,
                kMaxAge,
                TimestampsToReturn.Neither,
                readIdCollection,
                CancellationToken.None).ConfigureAwait(false);
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
            var nodeId = new NodeId("Scalar_Simulation_Int32", 2);
            var nodesToRead = ServerFixtureUtils.AttributesIds.Keys
                .Select(attributeId => new ReadValueId { NodeId = nodeId, AttributeId = attributeId })
                .ToArrayOf();
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
            var serverTestServices = new ServerTestServices(m_server, m_secureChannelContext);
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
                nodesToWrite, CancellationToken.None).ConfigureAwait(false);
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
            var nodesToRead = new ReadValueIdCollection
            {
                new ReadValueId { NodeId = nodeId, AttributeId = Attributes.Value }
            };

            // First read
            RequestHeader requestHeader = m_requestHeader;
            requestHeader.Timestamp = DateTime.UtcNow;
            DateTime timeBeforeFirstRead = DateTime.UtcNow;
            ReadResponse firstReadResponse = await m_server.ReadAsync(
                m_secureChannelContext,
                requestHeader,
                kMaxAge,
                TimestampsToReturn.Both,
                nodesToRead,
                CancellationToken.None).ConfigureAwait(false);

            Assert.IsNotNull(firstReadResponse);
            Assert.IsNotNull(firstReadResponse.Results);
            Assert.AreEqual(1, firstReadResponse.Results.Count);
            DataValue firstValue = firstReadResponse.Results[0];
            Assert.AreEqual(StatusCodes.Good, firstValue.StatusCode);
            Assert.IsNotNull(firstValue.SourceTimestamp);
            logger.LogInformation("First read - SourceTimestamp: {SourceTimestamp}, ServerTimestamp: {ServerTimestamp}",
                firstValue.SourceTimestamp, firstValue.ServerTimestamp);

            // Verify the timestamp is recent (not startup time)
            Assert.GreaterOrEqual(firstValue.SourceTimestamp, timeBeforeFirstRead.AddSeconds(-1),
                "SourceTimestamp should be close to the read time, not the server startup time");

            // Wait a bit to ensure time difference
            await Task.Delay(1500).ConfigureAwait(false);

            // Second read
            requestHeader.Timestamp = DateTime.UtcNow;
            DateTime timeBeforeSecondRead = DateTime.UtcNow;
            ReadResponse secondReadResponse = await m_server.ReadAsync(
                m_secureChannelContext,
                requestHeader,
                kMaxAge,
                TimestampsToReturn.Both,
                nodesToRead,
                CancellationToken.None).ConfigureAwait(false);

            Assert.IsNotNull(secondReadResponse);
            Assert.IsNotNull(secondReadResponse.Results);
            Assert.AreEqual(1, secondReadResponse.Results.Count);
            DataValue secondValue = secondReadResponse.Results[0];
            Assert.AreEqual(StatusCodes.Good, secondValue.StatusCode);
            Assert.IsNotNull(secondValue.SourceTimestamp);
            logger.LogInformation("Second read - SourceTimestamp: {SourceTimestamp}, ServerTimestamp: {ServerTimestamp}",
                secondValue.SourceTimestamp, secondValue.ServerTimestamp);

            // Verify the second timestamp is more recent than the first
            Assert.Greater(secondValue.SourceTimestamp, firstValue.SourceTimestamp,
                "SourceTimestamp should be updated on each read");

            // Verify the second timestamp is recent
            Assert.GreaterOrEqual(secondValue.SourceTimestamp, timeBeforeSecondRead.AddSeconds(-1),
                "SourceTimestamp should be close to the second read time");
        }

        /// <summary>
        /// Test that ReferenceNodeManager array variables update their SourceTimestamp on read.
        /// </summary>
        [Test]
        [NonParallelizable]
        public async Task ReferenceNodeManagerArrayVariablesUpdateTimestampOnReadAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ILogger logger = telemetry.CreateLogger<ReferenceServerTests>();

            // Read an array variable from the ReferenceNodeManager (namespace index 2)
            var nodeId = new NodeId("Scalar_Static_Arrays_Byte", 2);
            var nodesToRead = new ReadValueIdCollection
            {
                new ReadValueId { NodeId = nodeId, AttributeId = Attributes.Value }
            };

            // First read
            RequestHeader requestHeader = m_requestHeader;
            requestHeader.Timestamp = DateTime.UtcNow;
            DateTime timeBeforeFirstRead = DateTime.UtcNow;
            ReadResponse firstReadResponse = await m_server.ReadAsync(
                m_secureChannelContext,
                requestHeader,
                kMaxAge,
                TimestampsToReturn.Both,
                nodesToRead,
                CancellationToken.None).ConfigureAwait(false);

            Assert.IsNotNull(firstReadResponse);
            Assert.IsNotNull(firstReadResponse.Results);
            Assert.AreEqual(1, firstReadResponse.Results.Count);
            DataValue firstValue = firstReadResponse.Results[0];
            Assert.AreEqual(StatusCodes.Good, firstValue.StatusCode);
            Assert.IsNotNull(firstValue.SourceTimestamp);
            logger.LogInformation("Array First read - SourceTimestamp: {SourceTimestamp}, ServerTimestamp: {ServerTimestamp}",
                firstValue.SourceTimestamp, firstValue.ServerTimestamp);

            // Verify the timestamp is recent (not startup time)
            Assert.GreaterOrEqual(firstValue.SourceTimestamp, timeBeforeFirstRead.AddSeconds(-1),
                "Array SourceTimestamp should be close to the read time, not the server startup time");

            // Wait a bit to ensure time difference
            await Task.Delay(1500).ConfigureAwait(false);

            // Second read
            requestHeader.Timestamp = DateTime.UtcNow;
            DateTime timeBeforeSecondRead = DateTime.UtcNow;
            ReadResponse secondReadResponse = await m_server.ReadAsync(
                m_secureChannelContext,
                requestHeader,
                kMaxAge,
                TimestampsToReturn.Both,
                nodesToRead,
                CancellationToken.None).ConfigureAwait(false);

            Assert.IsNotNull(secondReadResponse);
            Assert.IsNotNull(secondReadResponse.Results);
            Assert.AreEqual(1, secondReadResponse.Results.Count);
            DataValue secondValue = secondReadResponse.Results[0];
            Assert.AreEqual(StatusCodes.Good, secondValue.StatusCode);
            Assert.IsNotNull(secondValue.SourceTimestamp);
            logger.LogInformation("Array Second read - SourceTimestamp: {SourceTimestamp}, ServerTimestamp: {ServerTimestamp}",
                secondValue.SourceTimestamp, secondValue.ServerTimestamp);

            // Verify the second timestamp is more recent than the first
            Assert.Greater(secondValue.SourceTimestamp, firstValue.SourceTimestamp,
                "Array SourceTimestamp should be updated on each read");

            // Verify the second timestamp is recent
            Assert.GreaterOrEqual(secondValue.SourceTimestamp, timeBeforeSecondRead.AddSeconds(-1),
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
            var serverTestServices = new ServerTestServices(m_server, m_secureChannelContext);
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
                    StatusCodes.BadNoSubscription,
                    sre.StatusCode);
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

            var serverTestServices = new ServerTestServices(m_server, m_secureChannelContext);

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
            var nodesToCall = await ResendDataCallAsync(
                StatusCodes.Good,
                subscriptionIds).ConfigureAwait(false);

            Thread.Sleep(1000);

            // Make sure publish queue becomes empty by consuming it
            Assert.AreEqual(1, subscriptionIds.Count);

            // Issue a Publish request
            m_requestHeader.Timestamp = DateTime.UtcNow;
            ArrayOf<SubscriptionAcknowledgement> acknowledgements = [];
            PublishResponse publishResponse = await serverTestServices.PublishAsync(
                m_requestHeader,
                acknowledgements).ConfigureAwait(false);

            Assert.AreEqual(StatusCodes.Good, publishResponse.ResponseHeader.ServiceResult);
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

                Assert.AreEqual(StatusCodes.Good, publishResponse.ResponseHeader.ServiceResult);
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

            Assert.AreEqual(StatusCodes.BadUserAccessDenied, callResponse.Results[0].StatusCode);
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

            Assert.AreEqual(StatusCodes.Good, publishResponse.ResponseHeader.ServiceResult);
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

            Assert.AreEqual(StatusCodes.Good, publishResponse.ResponseHeader.ServiceResult);
            ServerFixtureUtils.ValidateResponse(publishResponse.ResponseHeader);
            ServerFixtureUtils.ValidateDiagnosticInfos(
                publishResponse.DiagnosticInfos,
                acknowledgements,
                publishResponse.ResponseHeader.StringTable,
                serverTestServices.Logger);
            Assert.AreEqual(subscriptionIds[0], publishResponse.SubscriptionId);
            Assert.AreEqual(1, publishResponse.NotificationMessage.NotificationData.Count);
            ExtensionObject items = publishResponse.NotificationMessage.NotificationData.FirstOrDefault();
            Assert.IsTrue(items.TryGetEncodeable(out DataChangeNotification dataChangeNotification));
            MonitoredItemNotificationCollection monitoredItemsCollection = dataChangeNotification.MonitoredItems;
            Assert.AreEqual(testSet.Length, monitoredItemsCollection.Count,
                "One MonitoredItemNotification should be returned for each Node present in the TestSet");

            Thread.Sleep(1000);

            if (updateValues && queueSize > 1)
            {
                // remaining queue Data should be sent in this publish
                m_requestHeader.Timestamp = DateTime.UtcNow;
                publishResponse = await serverTestServices.PublishAsync(
                    m_requestHeader,
                    acknowledgements).ConfigureAwait(false);

                Assert.AreEqual(StatusCodes.Good, publishResponse.ResponseHeader.ServiceResult);
                ServerFixtureUtils.ValidateResponse(publishResponse.ResponseHeader);
                ServerFixtureUtils.ValidateDiagnosticInfos(
                    publishResponse.DiagnosticInfos,
                    acknowledgements,
                    publishResponse.ResponseHeader.StringTable,
                    serverTestServices.Logger);
                Assert.AreEqual(subscriptionIds[0], publishResponse.SubscriptionId);
                Assert.AreEqual(1, publishResponse.NotificationMessage.NotificationData.Count);
                items = publishResponse.NotificationMessage.NotificationData.FirstOrDefault();
                Assert.IsTrue(items.TryGetEncodeable(out dataChangeNotification));
                monitoredItemsCollection = dataChangeNotification.MonitoredItems;
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

            Assert.AreEqual(StatusCodes.Good, publishResponse.ResponseHeader.ServiceResult);
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
            m_requestHeader.Timestamp = DateTime.UtcNow;
            CallResponse callResponse = await m_server.CallAsync(
                m_secureChannelContext,
                m_requestHeader,
                nodesToCall, CancellationToken.None).ConfigureAwait(false);

            Assert.AreEqual(expectedStatus, callResponse.Results[0].StatusCode);
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
                TypeInfo typeInfo = dataValue.WrappedValue.TypeInfo;
                Assert.False(typeInfo.IsUnknown);
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
            ArrayOf<ReadValueId> readIdCollection =
            [
                new ReadValueId
                {
                    AttributeId = Attributes.EventNotifier,
                    NodeId = ObjectIds.Server
                }
            ];

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

            m_requestHeader.Timestamp = DateTime.UtcNow;
            ReadResponse readResponse = await m_server.ReadAsync(
                m_secureChannelContext,
                m_requestHeader,
                0,
                TimestampsToReturn.Both,
                nodesToRead,
                CancellationToken.None).ConfigureAwait(false);

            ServerFixtureUtils.ValidateResponse(readResponse.ResponseHeader, readResponse.Results, nodesToRead);
            Assert.AreEqual(3, readResponse.Results.Count);

            // Verify that SourceTimestamp and ServerTimestamp are equal for all ServerStatus children
            for (int i = 0; i < readResponse.Results.Count; i++)
            {
                DataValue result = readResponse.Results[i];
                logger.LogInformation(
                    "NodeId: {NodeId}, SourceTimestamp: {SourceTimestamp}, ServerTimestamp: {ServerTimestamp}",
                    nodesToRead[i].NodeId,
                    result.SourceTimestamp,
                    result.ServerTimestamp);

                Assert.AreEqual(result.SourceTimestamp, result.ServerTimestamp,
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

            m_requestHeader.Timestamp = DateTime.UtcNow;
            ReadResponse readResponse = await m_server.ReadAsync(
                m_secureChannelContext,
                m_requestHeader,
                kMaxAge,
                TimestampsToReturn.Neither,
                readIdCollection,
                CancellationToken.None).ConfigureAwait(false);

            ServerFixtureUtils.ValidateResponse(readResponse.ResponseHeader, readResponse.Results, readIdCollection);
            Assert.AreEqual(2, readResponse.Results.Count);

            bool historizing = (bool)readResponse.Results[0].Value;
            byte accessLevel = (byte)readResponse.Results[1].Value;

            logger.LogInformation("Historizing: {Historizing}, AccessLevel: {AccessLevel}", historizing, accessLevel);

            Assert.IsTrue(historizing, "Int32Value node should have Historizing=true");
            Assert.IsTrue((accessLevel & AccessLevels.HistoryRead) != 0,
                "Int32Value node should have HistoryRead access level");

            // Perform a history read operation
            var historyReadDetails = new ReadRawModifiedDetails
            {
                StartTime = DateTime.UtcNow.AddHours(-1),
                EndTime = DateTime.UtcNow,
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

            m_requestHeader.Timestamp = DateTime.UtcNow;
            HistoryReadResponse historyReadResponse = await m_server.HistoryReadAsync(
                m_secureChannelContext,
                m_requestHeader,
                new ExtensionObject(historyReadDetails),
                TimestampsToReturn.Both,
                false,
                nodesToRead,
                CancellationToken.None).ConfigureAwait(false);

            ServerFixtureUtils.ValidateResponse(historyReadResponse.ResponseHeader, historyReadResponse.Results, nodesToRead);
            Assert.AreEqual(1, historyReadResponse.Results.Count);

            HistoryReadResult result = historyReadResponse.Results[0];

            logger.LogInformation("History read StatusCode: {StatusCode}", result.StatusCode);

            // The result should be Good or GoodMoreData (if there are more values)
            Assert.IsTrue(StatusCode.IsGood(result.StatusCode),
                $"History read should succeed, but got: {result.StatusCode}");
            Assert.IsNotNull(result.HistoryData, "HistoryData should not be null");

            // Verify we got HistoryData back
            if (result.HistoryData.TryGetEncodeable(out HistoryData historyData))
            {
                logger.LogInformation("Retrieved {Count} history values", historyData.DataValues.Count);
                Assert.IsNotNull(historyData.DataValues, "DataValues should not be null");
                Assert.Greater(historyData.DataValues.Count, 0, "Should have at least one historical value");

                // Verify the data values have proper timestamps
                foreach (DataValue dataValue in historyData.DataValues)
                {
                    Assert.IsNotNull(dataValue, "DataValue should not be null");
                    Assert.IsTrue(dataValue.ServerTimestamp != DateTime.MinValue,
                        "DataValue should have a valid ServerTimestamp");
                }
            }
            else
            {
                NUnit.Framework.Assert.Fail("HistoryData body should be of type HistoryData");
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
