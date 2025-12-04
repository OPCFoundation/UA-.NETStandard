/* ========================================================================
 * Copyright (c) 2005-2022 The OPC Foundation, Inc. All rights reserved.
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
using Opc.Ua.Server.Tests;
using Opc.Ua.Tests;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

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
            var nodesToAdd = new AddNodesItemCollection();
            var addNodesItem = new AddNodesItem();
            for (int ii = 0; ii < kOperationLimit * 2; ii++)
            {
                nodesToAdd.Add(addNodesItem);
            }

            var requestHeader = new RequestHeader();
            ServiceResultException sre = NUnit.Framework.Assert
                .ThrowsAsync<ServiceResultException>(async () =>
                    {
                        AddNodesResponse response = await Session
                            .AddNodesAsync(requestHeader, nodesToAdd, CancellationToken.None)
                            .ConfigureAwait(false);

                        Assert.NotNull(response);
                        AddNodesResultCollection results = response.Results;
                        DiagnosticInfoCollection diagnosticInfos = response.DiagnosticInfos;

                        Assert.AreEqual(nodesToAdd.Count, results.Count);
                        Assert.AreEqual(diagnosticInfos.Count, diagnosticInfos.Count);
                    });

            Assert.AreEqual(
                (StatusCode)StatusCodes.BadServiceUnsupported,
                (StatusCode)sre.StatusCode,
                sre.ToString());
        }

        [Test]
        public void AddReferencesAsyncThrows()
        {
            var referencesToAdd = new AddReferencesItemCollection();
            var addReferencesItem = new AddReferencesItem();
            for (int ii = 0; ii < kOperationLimit * 2; ii++)
            {
                referencesToAdd.Add(addReferencesItem);
            }

            var requestHeader = new RequestHeader();
            ServiceResultException sre = NUnit.Framework.Assert
                .ThrowsAsync<ServiceResultException>(async () =>
                    {
                        AddReferencesResponse response = await Session
                            .AddReferencesAsync(
                                requestHeader,
                                referencesToAdd,
                                CancellationToken.None)
                            .ConfigureAwait(false);

                        Assert.NotNull(response);
                        StatusCodeCollection results = response.Results;
                        DiagnosticInfoCollection diagnosticInfos = response.DiagnosticInfos;

                        Assert.AreEqual(referencesToAdd.Count, results.Count);
                        Assert.AreEqual(diagnosticInfos.Count, diagnosticInfos.Count);
                    });

            Assert.AreEqual(
                (StatusCode)StatusCodes.BadServiceUnsupported,
                (StatusCode)sre.StatusCode);
        }

        [Test]
        public void DeleteNodesAsyncThrows()
        {
            var nodesTDelete = new DeleteNodesItemCollection();
            var deleteNodesItem = new DeleteNodesItem();
            for (int ii = 0; ii < kOperationLimit * 2; ii++)
            {
                nodesTDelete.Add(deleteNodesItem);
            }

            var requestHeader = new RequestHeader();
            ServiceResultException sre = NUnit.Framework.Assert
                .ThrowsAsync<ServiceResultException>(async () =>
                    {
                        DeleteNodesResponse response = await Session
                            .DeleteNodesAsync(requestHeader, nodesTDelete, CancellationToken.None)
                            .ConfigureAwait(false);

                        StatusCodeCollection results = response.Results;
                        DiagnosticInfoCollection diagnosticInfos = response.DiagnosticInfos;

                        Assert.NotNull(response.ResponseHeader);
                        Assert.AreEqual(nodesTDelete.Count, results.Count);
                        Assert.AreEqual(diagnosticInfos.Count, diagnosticInfos.Count);
                    });

            Assert.AreEqual(
                (StatusCode)StatusCodes.BadServiceUnsupported,
                (StatusCode)sre.StatusCode);
        }

        [Test]
        public void DeleteReferencesAsyncThrows()
        {
            var referencesToDelete = new DeleteReferencesItemCollection();
            var deleteReferencesItem = new DeleteReferencesItem();
            for (int ii = 0; ii < kOperationLimit * 2; ii++)
            {
                referencesToDelete.Add(deleteReferencesItem);
            }

            var requestHeader = new RequestHeader();
            ServiceResultException sre = NUnit.Framework.Assert
                .ThrowsAsync<ServiceResultException>(async () =>
                    {
                        DeleteReferencesResponse response = await Session
                            .DeleteReferencesAsync(
                        requestHeader,
                        referencesToDelete,
                        CancellationToken.None)
                            .ConfigureAwait(false);

                        StatusCodeCollection results = response.Results;
                        DiagnosticInfoCollection diagnosticInfos = response.DiagnosticInfos;

                        Assert.NotNull(response.ResponseHeader);
                        Assert.AreEqual(referencesToDelete.Count, results.Count);
                        Assert.AreEqual(diagnosticInfos.Count, diagnosticInfos.Count);
                    });

            Assert.AreEqual(
                (StatusCode)StatusCodes.BadServiceUnsupported,
                (StatusCode)sre.StatusCode);
        }

        [Test]
        public async Task BrowseAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ILogger logger = telemetry.CreateLogger<ClientBatchTest>();

            // Browse template
            const uint startingNode = Objects.RootFolder;
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
            var referenceDescriptions = new ReferenceDescriptionCollection();

            BrowseDescriptionCollection browseDescriptionCollection =
                ServerFixtureUtils.CreateBrowseDescriptionCollectionFromNodeId(
                    [.. new NodeId[] { Objects.RootFolder }],
                    browseTemplate);
            while (browseDescriptionCollection.Count > 0)
            {
                TestContext.Out.WriteLine("Browse {0} Nodes...", browseDescriptionCollection.Count);
                var allResults = new BrowseResultCollection();
                BrowseResponse response = await Session
                    .BrowseAsync(
                        requestHeader,
                        null,
                        5,
                        browseDescriptionCollection,
                        CancellationToken.None)
                    .ConfigureAwait(false);

                BrowseResultCollection results = response.Results;
                DiagnosticInfoCollection diagnosticInfos = response.DiagnosticInfos;

                allResults.AddRange(results);

                ByteStringCollection continuationPoints = ServerFixtureUtils.PrepareBrowseNext(
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
                    BrowseResultCollection browseNextResultCollection = nextResponse.Results;
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
                var browseTable = new NodeIdCollection();
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
            var nodesToRead = new ReadValueIdCollection(
                referenceDescriptions.Select(r => new ReadValueId
                {
                    NodeId = ExpandedNodeId.ToNodeId(r.NodeId, Session.NamespaceUris),
                    AttributeId = Attributes.Value
                }));

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
            var nodesToRegister = new NodeIdCollection(nodesToRead.Select(n => n.NodeId));
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
            var nodesToWrite = new WriteValueCollection();
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

            var browsePaths = new BrowsePathCollection();
            var browsePath = new BrowsePath
            {
                StartingNode = ObjectIds.RootFolder,
                RelativePath = new RelativePath("Types")
            };

            for (int ii = 0; ii < kOperationLimit * 2; ii++)
            {
                browsePaths.Add(browsePath);
            }

            var requestHeader = new RequestHeader();
            TranslateBrowsePathsToNodeIdsResponse response = await Session
                .TranslateBrowsePathsToNodeIdsAsync(
                    requestHeader,
                    browsePaths,
                    CancellationToken.None)
                .ConfigureAwait(false);
            BrowsePathResultCollection results = response.Results;
            DiagnosticInfoCollection diagnosticInfos = response.DiagnosticInfos;

            ServerFixtureUtils.ValidateResponse(response.ResponseHeader, results, browsePaths);
            ServerFixtureUtils.ValidateDiagnosticInfos(
                diagnosticInfos,
                browsePaths,
                response.ResponseHeader.StringTable,
                logger);
            Assert.NotNull(response.ResponseHeader);
        }

        [Theory]
        public async Task HistoryReadAsync(bool eventDetails)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ILogger logger = telemetry.CreateLogger<ClientBatchTest>();

            // there are no historizing nodes, but create some real ones
            System.Collections.Generic.IList<NodeId> testSet = GetTestSetSimulation(
                Session.NamespaceUris);
            var nodesToRead = new HistoryReadValueIdCollection(
                testSet.Select(nodeId => new HistoryReadValueId { NodeId = nodeId }));

            // add a some real history nodes
            testSet = GetTestSetHistory(Session.NamespaceUris);
            nodesToRead.AddRange(
                testSet.Select(nodeId => new HistoryReadValueId { NodeId = nodeId }));

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
            System.Collections.Generic.IList<NodeId> testSet = GetTestSetSimulation(
                Session.NamespaceUris);

            // see https://reference.opcfoundation.org/v104/Core/docs/Part11/6.8.1/ as to why
            // history update of event, data or annotations should be called individually
            ExtensionObjectCollection historyUpdateDetails;
            if (eventDetails)
            {
                historyUpdateDetails =
                [
                    .. testSet.Select(nodeId => new ExtensionObject(
                        new UpdateEventDetails {
                            NodeId = nodeId,
                            PerformInsertReplace = PerformUpdateType.Insert }
                    ))
                ];
            }
            else
            {
                historyUpdateDetails =
                [
                    .. testSet.Select(nodeId => new ExtensionObject(
                        new UpdateDataDetails {
                            NodeId = nodeId,
                            PerformInsertReplace = PerformUpdateType.Replace }
                    ))
                ];
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

            filter.AddSelectClause(ObjectTypes.BaseEventType, BrowseNames.EventId);
            filter.AddSelectClause(ObjectTypes.BaseEventType, BrowseNames.EventType);
            filter.AddSelectClause(ObjectTypes.BaseEventType, BrowseNames.SourceNode);
            filter.AddSelectClause(ObjectTypes.BaseEventType, BrowseNames.SourceName);
            filter.AddSelectClause(ObjectTypes.BaseEventType, BrowseNames.Time);
            filter.AddSelectClause(ObjectTypes.BaseEventType, BrowseNames.ReceiveTime);
            filter.AddSelectClause(ObjectTypes.BaseEventType, BrowseNames.LocalTime);
            filter.AddSelectClause(ObjectTypes.BaseEventType, BrowseNames.Message);
            filter.AddSelectClause(ObjectTypes.BaseEventType, BrowseNames.Severity);

            return filter;
        }
    }
}
