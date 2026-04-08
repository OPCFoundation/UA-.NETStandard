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
using Opc.Ua.Server.Tests;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// Client tests.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("SessionClient")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [TestFixtureSource(nameof(FixtureArgs))]
    [Parallelizable(ParallelScope.Fixtures)]
    [MemoryDiagnoser]
    [DisassemblyDiagnoser]
    public class ClientBatchTest : ClientTestFramework
    {
        private const uint kOperationLimit = 5;

        public ClientBatchTest(string uriScheme = Utils.UriSchemeOpcTcp)
            : base(uriScheme)
        {
        }

        /// <summary>
        /// Set up a Server and a Client instance.
        /// </summary>
        [OneTimeSetUp]
        public override async Task OneTimeSetUpAsync()
        {
            SupportsExternalServerUrl = true;
            await base.OneTimeSetUpAsync().ConfigureAwait(false);
            if (Session is Session session)
            {
                session.OperationLimits.MaxMonitoredItemsPerCall = kOperationLimit;
                session.OperationLimits.MaxNodesPerBrowse = kOperationLimit;
                session.OperationLimits.MaxNodesPerHistoryReadData = kOperationLimit;
                session.OperationLimits.MaxNodesPerHistoryReadEvents = kOperationLimit;
                session.OperationLimits.MaxNodesPerHistoryUpdateData = kOperationLimit;
                session.OperationLimits.MaxNodesPerHistoryUpdateEvents = kOperationLimit;
                session.OperationLimits.MaxNodesPerMethodCall = kOperationLimit;
                session.OperationLimits.MaxNodesPerNodeManagement = kOperationLimit;
                session.OperationLimits.MaxNodesPerRead = kOperationLimit;
                session.OperationLimits.MaxNodesPerRegisterNodes = kOperationLimit;
                session.OperationLimits.MaxNodesPerTranslateBrowsePathsToNodeIds = kOperationLimit;
                session.OperationLimits.MaxNodesPerWrite = kOperationLimit;
            }
        }

        /// <summary>
        /// Tear down the Server and the Client.
        /// </summary>
        [OneTimeTearDown]
        public override Task OneTimeTearDownAsync()
        {
            return base.OneTimeTearDownAsync();
        }

        /// <summary>
        /// Test setup.
        /// </summary>
        [SetUp]
        public override async Task SetUpAsync()
        {
            await base.SetUpAsync().ConfigureAwait(false);

            // test if the server accepts RequestHeader timestampes which
            // are up to +-5 days off.
            if (Session is TestableSession testableSession)
            {
                // set the time offset to a value from -5 to +5 days
                testableSession.TimestampOffset = TimeSpan.FromSeconds(
                    (UnsecureRandom.Shared.NextDouble() - 0.5) * 3600.0 * 24.0 * 10.0);
                TestContext.Out.WriteLine(
                    "The time offset for request headers has been set to {0} offset.",
                    testableSession.TimestampOffset.ToString());
            }
        }

        /// <summary>
        /// Test teardown.
        /// </summary>
        [TearDown]
        public override Task TearDownAsync()
        {
            return base.TearDownAsync();
        }

        [Test]
        public void AddNodesAsyncThrows()
        {
            var nodesToAdd = new List<AddNodesItem>();
            var addNodesItem = new AddNodesItem();
            for (int ii = 0; ii < kOperationLimit * 2; ii++)
            {
                nodesToAdd.Add(addNodesItem);
            }

            var requestHeader = new RequestHeader();
            ServiceResultException sre = Assert
                .ThrowsAsync<ServiceResultException>(async () =>
                    {
                        AddNodesResponse response = await Session
                            .AddNodesAsync(requestHeader, nodesToAdd, CancellationToken.None)
                            .ConfigureAwait(false);

                        Assert.That(response, Is.Not.Null);
                        ArrayOf<AddNodesResult> results = response.Results;
                        ArrayOf<DiagnosticInfo> diagnosticInfos = response.DiagnosticInfos;

                        Assert.That(results.Count, Is.EqualTo(nodesToAdd.Count));
                        Assert.That(diagnosticInfos.Count, Is.EqualTo(results.Count));
                    });

            Assert.That(
                sre.StatusCode,
                Is.EqualTo(StatusCodes.BadServiceUnsupported),
                sre.ToString());
        }

        [Test]
        public void AddReferencesAsyncThrows()
        {
            var referencesToAdd = new List<AddReferencesItem>();
            var addReferencesItem = new AddReferencesItem();
            for (int ii = 0; ii < kOperationLimit * 2; ii++)
            {
                referencesToAdd.Add(addReferencesItem);
            }

            var requestHeader = new RequestHeader();
            ServiceResultException sre = Assert
                .ThrowsAsync<ServiceResultException>(async () =>
                    {
                        AddReferencesResponse response = await Session
                            .AddReferencesAsync(
                                requestHeader,
                                referencesToAdd,
                                CancellationToken.None)
                            .ConfigureAwait(false);

                        Assert.That(response, Is.Not.Null);
                        ArrayOf<StatusCode> results = response.Results;
                        ArrayOf<DiagnosticInfo> diagnosticInfos = response.DiagnosticInfos;

                        Assert.That(results.Count, Is.EqualTo(referencesToAdd.Count));
                        Assert.That(diagnosticInfos.Count, Is.EqualTo(results.Count));
                    });

            Assert.That(
                sre.StatusCode,
                Is.EqualTo(StatusCodes.BadServiceUnsupported));
        }

        [Test]
        public void DeleteNodesAsyncThrows()
        {
            var nodesTDelete = new List<DeleteNodesItem>();
            var deleteNodesItem = new DeleteNodesItem();
            for (int ii = 0; ii < kOperationLimit * 2; ii++)
            {
                nodesTDelete.Add(deleteNodesItem);
            }

            var requestHeader = new RequestHeader();
            ServiceResultException sre = Assert
                .ThrowsAsync<ServiceResultException>(async () =>
                    {
                        DeleteNodesResponse response = await Session
                            .DeleteNodesAsync(requestHeader, nodesTDelete, CancellationToken.None)
                            .ConfigureAwait(false);

                        ArrayOf<StatusCode> results = response.Results;
                        ArrayOf<DiagnosticInfo> diagnosticInfos = response.DiagnosticInfos;

                        Assert.That(response.ResponseHeader, Is.Not.Null);
                        Assert.That(results.Count, Is.EqualTo(nodesTDelete.Count));
                        Assert.That(diagnosticInfos.Count, Is.EqualTo(results.Count));
                    });

            Assert.That(
                sre.StatusCode,
                Is.EqualTo(StatusCodes.BadServiceUnsupported));
        }

        [Test]
        public void DeleteReferencesAsyncThrows()
        {
            var referencesToDelete = new List<DeleteReferencesItem>();
            var deleteReferencesItem = new DeleteReferencesItem();
            for (int ii = 0; ii < kOperationLimit * 2; ii++)
            {
                referencesToDelete.Add(deleteReferencesItem);
            }

            var requestHeader = new RequestHeader();
            ServiceResultException sre = Assert
                .ThrowsAsync<ServiceResultException>(async () =>
                    {
                        DeleteReferencesResponse response = await Session
                            .DeleteReferencesAsync(
                        requestHeader,
                        referencesToDelete,
                        CancellationToken.None)
                            .ConfigureAwait(false);

                        ArrayOf<StatusCode> results = response.Results;
                        ArrayOf<DiagnosticInfo> diagnosticInfos = response.DiagnosticInfos;

                        Assert.That(response.ResponseHeader, Is.Not.Null);
                        Assert.That(results.Count, Is.EqualTo(referencesToDelete.Count));
                        Assert.That(diagnosticInfos.Count, Is.EqualTo(results.Count));
                    });

            Assert.That(
                sre.StatusCode,
                Is.EqualTo(StatusCodes.BadServiceUnsupported));
        }

        [Test]
        public async Task BrowseAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ILogger logger = telemetry.CreateLogger<ClientBatchTest>();

            // Browse template
            NodeId startingNode = ObjectIds.RootFolder;
            var browseTemplate = new BrowseDescription
            {
                NodeId = startingNode,
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                IncludeSubtypes = true,
                NodeClassMask = 0,
                ResultMask = (uint)BrowseResultMask.All
            };

            var requestHeader = new RequestHeader();
            var referenceDescriptions = new List<ReferenceDescription>();

            ArrayOf<BrowseDescription> browseDescriptionCollection =
                ServerFixtureUtils.CreateBrowseDescriptionCollectionFromNodeId(
                    [ObjectIds.RootFolder],
                    browseTemplate);
            while (browseDescriptionCollection.Count > 0)
            {
                TestContext.Out.WriteLine("Browse {0} Nodes...", browseDescriptionCollection.Count);
                var allResults = new List<BrowseResult>();
                BrowseResponse response = await Session
                    .BrowseAsync(
                        requestHeader,
                        null,
                        5,
                        browseDescriptionCollection,
                        CancellationToken.None)
                    .ConfigureAwait(false);

                ArrayOf<BrowseResult> results = response.Results;
                ArrayOf<DiagnosticInfo> diagnosticInfos = response.DiagnosticInfos;

                allResults.AddRange(results);

                ArrayOf<ByteString> continuationPoints = ServerFixtureUtils.PrepareBrowseNext(
                    results);
                while (continuationPoints.Count > 0)
                {
                    TestContext.Out.WriteLine("BrowseNext {0} Nodes...", continuationPoints.Count);
                    BrowseNextResponse nextResponse = await Session
                        .BrowseNextAsync(
                            requestHeader,
                            false,
                            continuationPoints,
                            CancellationToken.None)
                        .ConfigureAwait(false);
                    ArrayOf<BrowseResult> browseNextResultCollection = nextResponse.Results;
                    diagnosticInfos = nextResponse.DiagnosticInfos;
                    ServerFixtureUtils.ValidateResponse(
                        response.ResponseHeader,
                        nextResponse.Results,
                        continuationPoints);
                    ServerFixtureUtils.ValidateDiagnosticInfos(
                        diagnosticInfos,
                        continuationPoints,
                        nextResponse.ResponseHeader.StringTable,
                        logger);
                    allResults.AddRange(browseNextResultCollection);
                    continuationPoints = ServerFixtureUtils.PrepareBrowseNext(
                        browseNextResultCollection);
                }

                // Build browse request for next level
                var browseTable = new List<NodeId>();
                foreach (BrowseResult result in allResults)
                {
                    referenceDescriptions.AddRange(result.References);
                    foreach (ReferenceDescription reference in result.References)
                    {
                        browseTable.Add(
                            ExpandedNodeId.ToNodeId(reference.NodeId, Session.NamespaceUris));
                    }
                }
                browseDescriptionCollection = ServerFixtureUtils
                    .CreateBrowseDescriptionCollectionFromNodeId(
                        browseTable,
                        browseTemplate);
            }

            referenceDescriptions.Sort((x, y) => x.NodeId.CompareTo(y.NodeId));

            // read values
            var nodesToRead =
                referenceDescriptions.Select(r => new ReadValueId
                {
                    NodeId = ExpandedNodeId.ToNodeId(r.NodeId, Session.NamespaceUris),
                    AttributeId = Attributes.Value
                }).ToArrayOf();

            // test reads
            TestContext.Out.WriteLine("Test Read Nodes...");
            ReadResponse readResponse = await Session
                .ReadAsync(
                    requestHeader,
                    0,
                    TimestampsToReturn.Neither,
                    nodesToRead,
                    CancellationToken.None)
                .ConfigureAwait(false);

            // test register nodes
            TestContext.Out.WriteLine("Test Register Nodes...");
            var nodesToRegister = nodesToRead.ConvertAll(n => n.NodeId).ToList();
            RegisterNodesResponse registerResponse = await Session
                .RegisterNodesAsync(requestHeader, nodesToRegister, CancellationToken.None)
                .ConfigureAwait(false);
            UnregisterNodesResponse unregisterResponse = await Session
                .UnregisterNodesAsync(
                    requestHeader,
                    registerResponse.RegisteredNodeIds,
                    CancellationToken.None)
                .ConfigureAwait(false);

            // test writes
            var nodesToWrite = new List<WriteValue>();
            int ii = 0;
            foreach (DataValue result in readResponse.Results)
            {
                if (StatusCode.IsGood(result.StatusCode))
                {
                    var writeValue = new WriteValue
                    {
                        AttributeId = Attributes.Value,
                        NodeId = nodesToRead[ii].NodeId,
                        Value = new DataValue(result.WrappedValue)
                    };
                    nodesToWrite.Add(writeValue);
                }
                ii++;
            }

            TestContext.Out.WriteLine("Test Writes...");
            WriteResponse writeResponse = await Session
                .WriteAsync(requestHeader, nodesToWrite, CancellationToken.None)
                .ConfigureAwait(false);

            TestContext.Out
                .WriteLine("Found {0} references on server.", referenceDescriptions.Count);
            ii = 0;
            foreach (ReferenceDescription reference in referenceDescriptions)
            {
                TestContext.Out.WriteLine(
                    "NodeId {0} {1} {2} {3}",
                    reference.NodeId,
                    reference.NodeClass,
                    reference.BrowseName,
                    readResponse.Results[ii++].WrappedValue);
            }
        }

        [Test]
        public async Task TranslateBrowsePathsToNodeIdsAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ILogger logger = telemetry.CreateLogger<ClientBatchTest>();

            var browsePathList = new List<BrowsePath>();
            var browsePath = new BrowsePath
            {
                StartingNode = ObjectIds.RootFolder,
                RelativePath = new RelativePath(QualifiedName.From("Types"))
            };

            for (int ii = 0; ii < kOperationLimit * 2; ii++)
            {
                browsePathList.Add(browsePath);
            }
            var browsePaths = browsePathList.ToArrayOf();

            var requestHeader = new RequestHeader();
            TranslateBrowsePathsToNodeIdsResponse response = await Session
                .TranslateBrowsePathsToNodeIdsAsync(
                    requestHeader,
                    browsePaths,
                    CancellationToken.None)
                .ConfigureAwait(false);
            ArrayOf<BrowsePathResult> results = response.Results;
            ArrayOf<DiagnosticInfo> diagnosticInfos = response.DiagnosticInfos;

            ServerFixtureUtils.ValidateResponse(response.ResponseHeader, results, browsePaths);
            ServerFixtureUtils.ValidateDiagnosticInfos(
                diagnosticInfos,
                browsePaths,
                response.ResponseHeader.StringTable,
                logger);
            Assert.That(response.ResponseHeader, Is.Not.Null);
        }

        [Theory]
        public async Task HistoryReadAsync(bool eventDetails)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ILogger logger = telemetry.CreateLogger<ClientBatchTest>();

            // there are no historizing nodes, but create some real ones
            var nodesToRead = GetTestSetSimulation(
                Session.NamespaceUris)
                .Select(nodeId => new HistoryReadValueId { NodeId = nodeId })
            // add a some real history nodes
                .Concat(GetTestSetHistory(Session.NamespaceUris)
                    .Select(nodeId => new HistoryReadValueId { NodeId = nodeId }))
                .ToArrayOf();

            HistoryReadResponse response = await Session
                .HistoryReadAsync(
                    null,
                    eventDetails ? ReadEventDetails() : ReadRawModifiedDetails(),
                    TimestampsToReturn.Source,
                    false,
                    nodesToRead,
                    CancellationToken.None)
                .ConfigureAwait(false);

            ServerFixtureUtils.ValidateResponse(
                response.ResponseHeader,
                response.Results,
                nodesToRead);
            ServerFixtureUtils.ValidateDiagnosticInfos(
                response.DiagnosticInfos,
                nodesToRead,
                response.ResponseHeader.StringTable,
                logger);
        }

        [Theory]
        public async Task HistoryUpdateAsync(bool eventDetails)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ILogger logger = telemetry.CreateLogger<ClientBatchTest>();

            // there are no historizing nodes, instead use some real nodes to test
            IList<NodeId> testSet = GetTestSetSimulation(
                Session.NamespaceUris);

            // see https://reference.opcfoundation.org/v104/Core/docs/Part11/6.8.1/ as to why
            // history update of event, data or annotations should be called individually
            ArrayOf<ExtensionObject> historyUpdateDetails;
            if (eventDetails)
            {
                historyUpdateDetails = testSet
                    .Select(nodeId => new ExtensionObject(
                        new UpdateEventDetails
                        {
                            NodeId = nodeId,
                            PerformInsertReplace = PerformUpdateType.Insert
                        }
                    )).ToArrayOf();
            }
            else
            {
                historyUpdateDetails = testSet
                    .Select(nodeId => new ExtensionObject(
                        new UpdateDataDetails
                        {
                            NodeId = nodeId,
                            PerformInsertReplace = PerformUpdateType.Replace
                        }
                    )).ToArrayOf();
            }

            HistoryUpdateResponse response = await Session
                .HistoryUpdateAsync(null, historyUpdateDetails, CancellationToken.None)
                .ConfigureAwait(false);

            ServerFixtureUtils.ValidateResponse(
                response.ResponseHeader,
                response.Results,
                historyUpdateDetails);
            ServerFixtureUtils.ValidateDiagnosticInfos(
                response.DiagnosticInfos,
                historyUpdateDetails,
                response.ResponseHeader.StringTable,
                logger);
        }

        private static ExtensionObject ReadRawModifiedDetails()
        {
            var details = new ReadRawModifiedDetails
            {
                StartTime = DateTime.MinValue,
                EndTime = DateTime.UtcNow.AddDays(1),
                NumValuesPerNode = 1,
                IsReadModified = false,
                ReturnBounds = false
            };
            return new ExtensionObject(details);
        }

        private static ExtensionObject ReadEventDetails()
        {
            var details = new ReadEventDetails
            {
                NumValuesPerNode = 10,
                Filter = DefaultEventFilter(),
                StartTime = DateTime.UtcNow.AddSeconds(30),
                EndTime = DateTime.UtcNow.AddHours(-1)
            };
            return new ExtensionObject(details);
        }

        private static EventFilter DefaultEventFilter()
        {
            EventFilter filter = _ = new EventFilter();

            filter.AddSelectClause(ObjectTypeIds.BaseEventType, QualifiedName.From(BrowseNames.EventId));
            filter.AddSelectClause(ObjectTypeIds.BaseEventType, QualifiedName.From(BrowseNames.EventType));
            filter.AddSelectClause(ObjectTypeIds.BaseEventType, QualifiedName.From(BrowseNames.SourceNode));
            filter.AddSelectClause(ObjectTypeIds.BaseEventType, QualifiedName.From(BrowseNames.SourceName));
            filter.AddSelectClause(ObjectTypeIds.BaseEventType, QualifiedName.From(BrowseNames.Time));
            filter.AddSelectClause(ObjectTypeIds.BaseEventType, QualifiedName.From(BrowseNames.ReceiveTime));
            filter.AddSelectClause(ObjectTypeIds.BaseEventType, QualifiedName.From(BrowseNames.LocalTime));
            filter.AddSelectClause(ObjectTypeIds.BaseEventType, QualifiedName.From(BrowseNames.Message));
            filter.AddSelectClause(ObjectTypeIds.BaseEventType, QualifiedName.From(BrowseNames.Severity));

            return filter;
        }
    }
}
