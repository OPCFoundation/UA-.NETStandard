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
using NUnit.Framework;
using Opc.Ua.Server.Tests;

namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// Client tests.
    /// </summary>
    [TestFixture, Category("Client"), Category("SessionClient")]
    [SetCulture("en-us"), SetUICulture("en-us")]
    [TestFixtureSource(nameof(FixtureArgs))]
    [MemoryDiagnoser]
    [DisassemblyDiagnoser]
    public class SessionClientBatchTest : ClientTestFramework
    {
        public const uint kOperationLimit = 5;
        public SessionClientBatchTest(string uriScheme = Utils.UriSchemeOpcTcp) :
            base(uriScheme)
        {
        }

        #region Test Setup
        /// <summary>
        /// Set up a Server and a Client instance.
        /// </summary>
        [OneTimeSetUp]
        public new async Task OneTimeSetUp()
        {
            SupportsExternalServerUrl = true;
            await base.OneTimeSetUp();
            Session.OperationLimits = null;
            Session.OperationLimits = new OperationLimits() {
                MaxMonitoredItemsPerCall = kOperationLimit,
                MaxNodesPerBrowse = kOperationLimit,
                MaxNodesPerHistoryReadData = kOperationLimit,
                MaxNodesPerHistoryReadEvents = kOperationLimit,
                MaxNodesPerHistoryUpdateData = kOperationLimit,
                MaxNodesPerHistoryUpdateEvents = kOperationLimit,
                MaxNodesPerMethodCall = kOperationLimit,
                MaxNodesPerNodeManagement = kOperationLimit,
                MaxNodesPerRead = kOperationLimit,
                MaxNodesPerRegisterNodes = kOperationLimit,
                MaxNodesPerTranslateBrowsePathsToNodeIds = kOperationLimit,
                MaxNodesPerWrite = kOperationLimit
            };
        }

        /// <summary>
        /// Tear down the Server and the Client.
        /// </summary>
        [OneTimeTearDown]
        public new Task OneTimeTearDownAsync()
        {
            return base.OneTimeTearDownAsync();
        }

        /// <summary>
        /// Test setup.
        /// </summary>
        [SetUp]
        public new async Task SetUp()
        {
            await base.SetUp().ConfigureAwait(false);
        }

        /// <summary>
        /// Test teardown.
        /// </summary>
        [TearDown]
        public new Task TearDown()
        {
            return base.TearDown();
        }
        #endregion

        #region Test Methods
        [Test]
        public void AddNodes()
        {
            var nodesToAdd = new AddNodesItemCollection();
            var addNodesItem = new AddNodesItem() { };
            for (int ii = 0; ii < kOperationLimit * 2; ii++)
            {
                nodesToAdd.Add(addNodesItem);
            }

            var requestHeader = new RequestHeader();
            var sre = Assert.Throws<ServiceResultException>(() => {
                var responseHeader = Session.AddNodes(requestHeader,
                    nodesToAdd,
                    out AddNodesResultCollection results,
                    out DiagnosticInfoCollection diagnosticInfos);

                Assert.NotNull(responseHeader);
                Assert.AreEqual(nodesToAdd.Count, results.Count);
                Assert.AreEqual(diagnosticInfos.Count, diagnosticInfos.Count);
            });

            Assert.AreEqual(StatusCodes.BadServiceUnsupported, sre.StatusCode);
        }

#if (CLIENT_ASYNC)
        [Test]
        public void AddNodesAsync()
        {
            var nodesToAdd = new AddNodesItemCollection();
            var addNodesItem = new AddNodesItem() { };
            for (int ii = 0; ii < kOperationLimit * 2; ii++)
            {
                nodesToAdd.Add(addNodesItem);
            }

            var requestHeader = new RequestHeader();
            var sre = Assert.ThrowsAsync<ServiceResultException>(async () => {
                var response = await Session.AddNodesAsync(requestHeader,
                    nodesToAdd, CancellationToken.None).ConfigureAwait(false); ;

                Assert.NotNull(response);
                AddNodesResultCollection results = response.Results;
                DiagnosticInfoCollection diagnosticInfos = response.DiagnosticInfos;

                Assert.AreEqual(nodesToAdd.Count, results.Count);
                Assert.AreEqual(diagnosticInfos.Count, diagnosticInfos.Count);
            });

            Assert.AreEqual(StatusCodes.BadServiceUnsupported, sre.StatusCode);
        }
#endif

        [Test]
        public void AddReferences()
        {
            var referencesToAdd = new AddReferencesItemCollection();
            var addReferencesItem = new AddReferencesItem() { };
            for (int ii = 0; ii < kOperationLimit * 2; ii++)
            {
                referencesToAdd.Add(addReferencesItem);
            }

            var requestHeader = new RequestHeader();
            var sre = Assert.Throws<ServiceResultException>(() => {
                var responseHeader = Session.AddReferences(requestHeader,
                    referencesToAdd,
                    out StatusCodeCollection results,
                    out DiagnosticInfoCollection diagnosticInfos);

                Assert.NotNull(responseHeader);
                Assert.AreEqual(referencesToAdd.Count, results.Count);
                Assert.AreEqual(diagnosticInfos.Count, diagnosticInfos.Count);
            });

            Assert.AreEqual(StatusCodes.BadServiceUnsupported, sre.StatusCode);
        }

#if (CLIENT_ASYNC)
        [Test]
        public void AddReferencesAsync()
        {
            var referencesToAdd = new AddReferencesItemCollection();
            var addReferencesItem = new AddReferencesItem() { };
            for (int ii = 0; ii < kOperationLimit * 2; ii++)
            {
                referencesToAdd.Add(addReferencesItem);
            }

            var requestHeader = new RequestHeader();
            var sre = Assert.ThrowsAsync<ServiceResultException>(async () => {
                var response = await Session.AddReferencesAsync(requestHeader,
                    referencesToAdd, CancellationToken.None).ConfigureAwait(false); ;

                Assert.NotNull(response);
                StatusCodeCollection results = response.Results;
                DiagnosticInfoCollection diagnosticInfos = response.DiagnosticInfos;

                Assert.AreEqual(referencesToAdd.Count, results.Count);
                Assert.AreEqual(diagnosticInfos.Count, diagnosticInfos.Count);
            });

            Assert.AreEqual(StatusCodes.BadServiceUnsupported, sre.StatusCode);
        }
#endif

        [Test]
        public void DeleteNodes()
        {
            var nodesTDelete = new DeleteNodesItemCollection();
            var deleteNodesItem = new DeleteNodesItem() { };
            for (int ii = 0; ii < kOperationLimit * 2; ii++)
            {
                nodesTDelete.Add(deleteNodesItem);
            }

            var requestHeader = new RequestHeader();
            var sre = Assert.Throws<ServiceResultException>(() => {
                var responseHeader = Session.DeleteNodes(requestHeader,
                    nodesTDelete,
                    out StatusCodeCollection results,
                    out DiagnosticInfoCollection diagnosticInfos);

                Assert.NotNull(responseHeader);
                Assert.AreEqual(nodesTDelete.Count, results.Count);
                Assert.AreEqual(diagnosticInfos.Count, diagnosticInfos.Count);
            });

            Assert.AreEqual(StatusCodes.BadServiceUnsupported, sre.StatusCode);
        }

#if (CLIENT_ASYNC)
        [Test]
        public void DeleteNodesAsync()
        {
            var nodesTDelete = new DeleteNodesItemCollection();
            var deleteNodesItem = new DeleteNodesItem() { };
            for (int ii = 0; ii < kOperationLimit * 2; ii++)
            {
                nodesTDelete.Add(deleteNodesItem);
            }

            var requestHeader = new RequestHeader();
            var sre = Assert.ThrowsAsync<ServiceResultException>(async () => {
                var response = await Session.DeleteNodesAsync(requestHeader,
                    nodesTDelete, CancellationToken.None).ConfigureAwait(false);

                StatusCodeCollection results = response.Results;
                DiagnosticInfoCollection diagnosticInfos = response.DiagnosticInfos;

                Assert.NotNull(response.ResponseHeader);
                Assert.AreEqual(nodesTDelete.Count, results.Count);
                Assert.AreEqual(diagnosticInfos.Count, diagnosticInfos.Count);
            });

            Assert.AreEqual(StatusCodes.BadServiceUnsupported, sre.StatusCode);
        }
#endif

        [Test]
        public void DeleteReferences()
        {
            var referencesToDelete = new DeleteReferencesItemCollection();
            var deleteReferencesItem = new DeleteReferencesItem() { };
            for (int ii = 0; ii < kOperationLimit * 2; ii++)
            {
                referencesToDelete.Add(deleteReferencesItem);
            }

            var requestHeader = new RequestHeader();
            var sre = Assert.Throws<ServiceResultException>(() => {
                var responseHeader = Session.DeleteReferences(requestHeader,
                    referencesToDelete,
                    out StatusCodeCollection results,
                    out DiagnosticInfoCollection diagnosticInfos);

                Assert.NotNull(responseHeader);
                Assert.AreEqual(referencesToDelete.Count, results.Count);
                Assert.AreEqual(diagnosticInfos.Count, diagnosticInfos.Count);
            });

            Assert.AreEqual(StatusCodes.BadServiceUnsupported, sre.StatusCode);
        }

#if (CLIENT_ASYNC)
        [Test]
        public void DeleteReferencesAsync()
        {
            var referencesToDelete = new DeleteReferencesItemCollection();
            var deleteReferencesItem = new DeleteReferencesItem() { };
            for (int ii = 0; ii < kOperationLimit * 2; ii++)
            {
                referencesToDelete.Add(deleteReferencesItem);
            }

            var requestHeader = new RequestHeader();
            var sre = Assert.ThrowsAsync<ServiceResultException>(async () => {
                var response = await Session.DeleteReferencesAsync(requestHeader,
                    referencesToDelete, CancellationToken.None).ConfigureAwait(false);

                StatusCodeCollection results = response.Results;
                DiagnosticInfoCollection diagnosticInfos = response.DiagnosticInfos;

                Assert.NotNull(response.ResponseHeader);
                Assert.AreEqual(referencesToDelete.Count, results.Count);
                Assert.AreEqual(diagnosticInfos.Count, diagnosticInfos.Count);
            });

            Assert.AreEqual(StatusCodes.BadServiceUnsupported, sre.StatusCode);
        }
#endif

        [Test]
        public void Browse()
        {
            // Browse template
            var startingNode = Objects.RootFolder;
            var browseTemplate = new BrowseDescription {
                NodeId = startingNode,
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                IncludeSubtypes = true,
                NodeClassMask = 0,
                ResultMask = (uint)BrowseResultMask.All
            };

            var browseDescriptionCollection = ServerFixtureUtils.CreateBrowseDescriptionCollectionFromNodeId(
                new NodeIdCollection(new NodeId[] { Objects.RootFolder }),
                browseTemplate);

            ResponseHeader response;
            var requestHeader = new RequestHeader();
            var referenceDescriptions = new ReferenceDescriptionCollection();

            while (browseDescriptionCollection.Any())
            {
                TestContext.Out.WriteLine("Browse {0} Nodes...", browseDescriptionCollection.Count);
                BrowseResultCollection allResults = new BrowseResultCollection();
                var responseHeader = Session.Browse(
                    requestHeader, null, 5,
                    browseDescriptionCollection,
                    out BrowseResultCollection results,
                    out DiagnosticInfoCollection diagnosticInfos);

                ServerFixtureUtils.ValidateResponse(responseHeader);
                ServerFixtureUtils.ValidateDiagnosticInfos(diagnosticInfos, browseDescriptionCollection);

                allResults.AddRange(results);

                var continuationPoints = ServerFixtureUtils.PrepareBrowseNext(results);
                while (continuationPoints.Any())
                {
                    TestContext.Out.WriteLine("BrowseNext {0} Nodes...", continuationPoints.Count);
                    responseHeader = Session.BrowseNext(requestHeader, false, continuationPoints,
                        out var browseNextResultCollection, out diagnosticInfos);
                    ServerFixtureUtils.ValidateResponse(responseHeader);
                    ServerFixtureUtils.ValidateDiagnosticInfos(diagnosticInfos, continuationPoints);
                    allResults.AddRange(browseNextResultCollection);
                    continuationPoints = ServerFixtureUtils.PrepareBrowseNext(browseNextResultCollection);
                }

                // Build browse request for next level
                var browseTable = new NodeIdCollection();
                foreach (var result in allResults)
                {
                    referenceDescriptions.AddRange(result.References);
                    foreach (var reference in result.References)
                    {
                        browseTable.Add(ExpandedNodeId.ToNodeId(reference.NodeId, Session.NamespaceUris));
                    }
                }
                browseDescriptionCollection = ServerFixtureUtils.CreateBrowseDescriptionCollectionFromNodeId(browseTable, browseTemplate);
            }

            referenceDescriptions.Sort((x, y) => (x.NodeId.CompareTo(y.NodeId)));

            // read values
            var nodesToRead = new ReadValueIdCollection(referenceDescriptions.Select(r =>
                new ReadValueId() {
                    NodeId = ExpandedNodeId.ToNodeId(r.NodeId, Session.NamespaceUris),
                    AttributeId = Attributes.Value
                }));

            TestContext.Out.WriteLine("Test Read Nodes...");
            var readResponse = Session.Read(requestHeader, 0, TimestampsToReturn.Neither, nodesToRead, out var valueResults, out _);

            // test register
            TestContext.Out.WriteLine("Test Register Nodes...");
            var nodesToRegister = new NodeIdCollection(nodesToRead.Select(n => n.NodeId));
            response = Session.RegisterNodes(requestHeader, nodesToRegister, out var registeredNodeIds);
            response = Session.UnregisterNodes(requestHeader, registeredNodeIds);

            // write values
            TestContext.Out.WriteLine("Test Writes...");
            var nodesToWrite = new WriteValueCollection();
            int ii = 0;
            foreach (var result in valueResults)
            {
                if (StatusCode.IsGood(result.StatusCode))
                {
                    var writeValue = new WriteValue() {
                        AttributeId = Attributes.Value,
                        NodeId = nodesToRead[ii].NodeId,
                        Value = new DataValue(result.WrappedValue)
                    };
                    nodesToWrite.Add(writeValue);
                }
                ii++;
            }
            var writeResponse = Session.Write(requestHeader, nodesToWrite, out var writeResults, out var writeDiagnostics);

            TestContext.Out.WriteLine("Found {0} references on server.", referenceDescriptions.Count);
            ii = 0;
            foreach (var reference in referenceDescriptions)
            {
                TestContext.Out.WriteLine("NodeId {0} {1} {2} {3}", reference.NodeId, reference.NodeClass, reference.BrowseName, valueResults[ii++].WrappedValue);
            }
        }

#if (CLIENT_ASYNC)
        [Test]
        public async Task BrowseAsync()
        {
            // Browse template
            var startingNode = Objects.RootFolder;
            var browseTemplate = new BrowseDescription {
                NodeId = startingNode,
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                IncludeSubtypes = true,
                NodeClassMask = 0,
                ResultMask = (uint)BrowseResultMask.All
            };

            var requestHeader = new RequestHeader();
            var referenceDescriptions = new ReferenceDescriptionCollection();

            var browseDescriptionCollection = ServerFixtureUtils.CreateBrowseDescriptionCollectionFromNodeId(
                new NodeIdCollection(new NodeId[] { Objects.RootFolder }),
                browseTemplate);
            while (browseDescriptionCollection.Any())
            {
                TestContext.Out.WriteLine("Browse {0} Nodes...", browseDescriptionCollection.Count);
                BrowseResultCollection allResults = new BrowseResultCollection();
                var response = await Session.BrowseAsync(
                    requestHeader, null, 5,
                    browseDescriptionCollection,
                    CancellationToken.None).ConfigureAwait(false);

                BrowseResultCollection results = response.Results;
                DiagnosticInfoCollection diagnosticInfos = response.DiagnosticInfos;

                allResults.AddRange(results);

                var continuationPoints = ServerFixtureUtils.PrepareBrowseNext(results);
                while (continuationPoints.Any())
                {
                    TestContext.Out.WriteLine("BrowseNext {0} Nodes...", continuationPoints.Count);
                    var nextResponse = await Session.BrowseNextAsync(requestHeader, false, continuationPoints, CancellationToken.None);
                    BrowseResultCollection browseNextResultCollection = nextResponse.Results;
                    diagnosticInfos = nextResponse.DiagnosticInfos;
                    ServerFixtureUtils.ValidateResponse(response.ResponseHeader);
                    ServerFixtureUtils.ValidateDiagnosticInfos(diagnosticInfos, continuationPoints);
                    allResults.AddRange(browseNextResultCollection);
                    continuationPoints = ServerFixtureUtils.PrepareBrowseNext(browseNextResultCollection);
                }

                // Build browse request for next level
                var browseTable = new NodeIdCollection();
                foreach (var result in allResults)
                {
                    referenceDescriptions.AddRange(result.References);
                    foreach (var reference in result.References)
                    {
                        browseTable.Add(ExpandedNodeId.ToNodeId(reference.NodeId, Session.NamespaceUris));
                    }
                }
                browseDescriptionCollection = ServerFixtureUtils.CreateBrowseDescriptionCollectionFromNodeId(browseTable, browseTemplate);
            }

            referenceDescriptions.Sort((x, y) => (x.NodeId.CompareTo(y.NodeId)));

            // read values
            var nodesToRead = new ReadValueIdCollection(referenceDescriptions.Select(r =>
                new ReadValueId() {
                    NodeId = ExpandedNodeId.ToNodeId(r.NodeId, Session.NamespaceUris),
                    AttributeId = Attributes.Value
                }));

            // test reads
            TestContext.Out.WriteLine("Test Read Nodes...");
            var readResponse = await Session.ReadAsync(requestHeader, 0, TimestampsToReturn.Neither, nodesToRead, CancellationToken.None).ConfigureAwait(false);

            // test register nodes
            TestContext.Out.WriteLine("Test Register Nodes...");
            var nodesToRegister = new NodeIdCollection(nodesToRead.Select(n => n.NodeId));
            var registerResponse = await Session.RegisterNodesAsync(requestHeader, nodesToRegister, CancellationToken.None).ConfigureAwait(false);
            var unregisterResponse = await Session.UnregisterNodesAsync(requestHeader, registerResponse.RegisteredNodeIds, CancellationToken.None).ConfigureAwait(false);

            // test writes
            var nodesToWrite = new WriteValueCollection();
            int ii = 0;
            foreach (var result in readResponse.Results)
            {
                if (StatusCode.IsGood(result.StatusCode))
                {
                    var writeValue = new WriteValue() {
                        AttributeId = Attributes.Value,
                        NodeId = nodesToRead[ii].NodeId,
                        Value = new DataValue(result.WrappedValue)
                    };
                    nodesToWrite.Add(writeValue);
                }
                ii++;
            }

            TestContext.Out.WriteLine("Test Writes...");
            var writeResponse = await Session.WriteAsync(requestHeader, nodesToWrite, CancellationToken.None).ConfigureAwait(false);

            TestContext.Out.WriteLine("Found {0} references on server.", referenceDescriptions.Count);
            ii = 0;
            foreach (var reference in referenceDescriptions)
            {
                TestContext.Out.WriteLine("NodeId {0} {1} {2} {3}", reference.NodeId, reference.NodeClass, reference.BrowseName, readResponse.Results[ii++].WrappedValue);
            }
        }
#endif

        [Test]
        public void TranslateBrowsePathsToNodeIds()
        {
            var browsePaths = new BrowsePathCollection();
            var browsePath = new BrowsePath() {
                StartingNode = ObjectIds.RootFolder,
                RelativePath = new RelativePath("Objects")
            };

            for (int ii = 0; ii < kOperationLimit * 2; ii++)
            {
                browsePaths.Add(browsePath);
            }

            var requestHeader = new RequestHeader();
            var responseHeader = Session.TranslateBrowsePathsToNodeIds(requestHeader,
                    browsePaths,
                    out BrowsePathResultCollection results,
                    out DiagnosticInfoCollection diagnosticInfos);

            ClientBase.ValidateResponse(results, browsePaths);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, browsePaths);
            Assert.NotNull(responseHeader);
        }

#if (CLIENT_ASYNC)
        [Test]
        public async Task TranslateBrowsePathsToNodeIdsAsync()
        {
            var browsePaths = new BrowsePathCollection();
            var browsePath = new BrowsePath() {
                StartingNode = ObjectIds.RootFolder,
                RelativePath = new RelativePath("Types")
            };

            for (int ii = 0; ii < kOperationLimit * 2; ii++)
            {
                browsePaths.Add(browsePath);
            }

            var requestHeader = new RequestHeader();
            var response = await Session.TranslateBrowsePathsToNodeIdsAsync(requestHeader,
                    browsePaths, CancellationToken.None).ConfigureAwait(false);
            BrowsePathResultCollection results = response.Results;
            DiagnosticInfoCollection diagnosticInfos = response.DiagnosticInfos;

            ClientBase.ValidateResponse(results, browsePaths);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, browsePaths);
            Assert.NotNull(response.ResponseHeader);
        }
#endif

        [Theory]
        public void HistoryRead(bool eventDetails)
        {
            HistoryReadResultCollection results = null;
            DiagnosticInfoCollection diagnosticInfos = null;

            // there are no historizing nodes, but create some real ones
            var testSet = GetTestSetSimulation(Session.NamespaceUris);
            HistoryReadValueIdCollection nodesToRead = new HistoryReadValueIdCollection(
                testSet.Select(nodeId => new HistoryReadValueId {
                    NodeId = nodeId
                }));

            var responseHeader = Session.HistoryRead(
                null,
                eventDetails ? ReadEventDetails() : ReadRawModifiedDetails(),
                TimestampsToReturn.Source,
                false,
                nodesToRead,
                out results,
                out diagnosticInfos);

            Session.ValidateResponse(results, nodesToRead);
            Session.ValidateDiagnosticInfos(diagnosticInfos, nodesToRead);
        }

        [Theory]
        public async Task HistoryReadAsync(bool eventDetails)
        {
            // there are no historizing nodes, but create some real ones
            var testSet = GetTestSetSimulation(Session.NamespaceUris);
            HistoryReadValueIdCollection nodesToRead = new HistoryReadValueIdCollection(
                testSet.Select(nodeId => new HistoryReadValueId {
                    NodeId = nodeId
                }));

            var response = await Session.HistoryReadAsync(
                null,
                eventDetails ? ReadEventDetails() : ReadRawModifiedDetails(),
                TimestampsToReturn.Source,
                false,
                nodesToRead, CancellationToken.None).ConfigureAwait(false);

            Session.ValidateResponse(response.Results, nodesToRead);
            Session.ValidateDiagnosticInfos(response.DiagnosticInfos, nodesToRead);
        }

        [Theory]
        public void HistoryUpdate(bool eventDetails)
        {
            HistoryUpdateResultCollection results = null;
            DiagnosticInfoCollection diagnosticInfos = null;

            // there are no historizing nodes, instead use some real nodes to test
            var testSet = GetTestSetSimulation(Session.NamespaceUris);

            // see https://reference.opcfoundation.org/v104/Core/docs/Part11/6.8.1/ as to why
            // history update of event, data or annotations should be called individually
            ExtensionObjectCollection historyUpdateDetails;
            if (eventDetails)
            {
                historyUpdateDetails = new ExtensionObjectCollection(
                    testSet.Select(nodeId => new ExtensionObject(new UpdateEventDetails() {
                        NodeId = nodeId,
                        PerformInsertReplace = PerformUpdateType.Insert
                    })));
            }
            else
            {
                historyUpdateDetails = new ExtensionObjectCollection(
                    testSet.Select(nodeId => new ExtensionObject(
                        new UpdateDataDetails() {
                            NodeId = nodeId,
                            PerformInsertReplace = PerformUpdateType.Replace
                        })));
            }

            var responseHeader = Session.HistoryUpdate(
                null,
                historyUpdateDetails,
                out results,
                out diagnosticInfos);

            Session.ValidateResponse(results, historyUpdateDetails);
            Session.ValidateDiagnosticInfos(diagnosticInfos, historyUpdateDetails);
        }

        [Theory]
        public async Task HistoryUpdateAsync(bool eventDetails)
        {
            // there are no historizing nodes, instead use some real nodes to test
            var testSet = GetTestSetSimulation(Session.NamespaceUris);

            // see https://reference.opcfoundation.org/v104/Core/docs/Part11/6.8.1/ as to why
            // history update of event, data or annotations should be called individually
            ExtensionObjectCollection historyUpdateDetails;
            if (eventDetails)
            {
                historyUpdateDetails = new ExtensionObjectCollection(
                    testSet.Select(nodeId => new ExtensionObject(new UpdateEventDetails() {
                        NodeId = nodeId,
                        PerformInsertReplace = PerformUpdateType.Insert
                    })));
            }
            else
            {
                historyUpdateDetails = new ExtensionObjectCollection(
                    testSet.Select(nodeId => new ExtensionObject(
                        new UpdateDataDetails() {
                            NodeId = nodeId,
                            PerformInsertReplace = PerformUpdateType.Replace
                        })));
            }

            var response = await Session.HistoryUpdateAsync(
                null,
                historyUpdateDetails,
                CancellationToken.None).ConfigureAwait(false);

            Session.ValidateResponse(response.Results, historyUpdateDetails);
            Session.ValidateDiagnosticInfos(response.DiagnosticInfos, historyUpdateDetails);
        }
        #endregion

        #region Benchmarks
        #endregion

        #region Private Methods
        private ExtensionObject ReadRawModifiedDetails()
        {
            ReadRawModifiedDetails details = new ReadRawModifiedDetails {
                StartTime = DateTime.MinValue,
                EndTime = DateTime.UtcNow.AddDays(1),
                NumValuesPerNode = 1,
                IsReadModified = false,
                ReturnBounds = false
            };
            return new ExtensionObject(details);
        }
        private ExtensionObject ReadEventDetails()
        {
            ReadEventDetails details = new ReadEventDetails {
                NumValuesPerNode = 10,
                StartTime = DateTime.UtcNow.AddSeconds(30),
                EndTime = DateTime.UtcNow.AddHours(-1),
            };
            return new ExtensionObject(details);
        }
    }
    #endregion
}

