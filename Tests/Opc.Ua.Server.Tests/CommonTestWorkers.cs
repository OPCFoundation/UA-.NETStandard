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
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Test workers using test services.
    /// </summary>
    public static class CommonTestWorkers
    {
        public const int DefaultMonitoredItemsQueueSize = 0;
        public const int DefaultMonitoredItemsSamplingInterval = -1;

        public static readonly ExpandedNodeId[] NodeIdTestSetStatic =
        [
            new ExpandedNodeId(
                "Scalar_Static_SByte",
                Quickstarts.ReferenceServer.Namespaces.ReferenceServer),
            new ExpandedNodeId(
                "Scalar_Static_Int16",
                Quickstarts.ReferenceServer.Namespaces.ReferenceServer),
            new ExpandedNodeId(
                "Scalar_Static_Int32",
                Quickstarts.ReferenceServer.Namespaces.ReferenceServer),
            new ExpandedNodeId(
                "Scalar_Static_Byte",
                Quickstarts.ReferenceServer.Namespaces.ReferenceServer),
            new ExpandedNodeId(
                "Scalar_Static_UInt16",
                Quickstarts.ReferenceServer.Namespaces.ReferenceServer),
            new ExpandedNodeId(
                "Scalar_Static_UInt32",
                Quickstarts.ReferenceServer.Namespaces.ReferenceServer),
            new ExpandedNodeId(
                "Scalar_Static_NodeId",
                Quickstarts.ReferenceServer.Namespaces.ReferenceServer),
            new ExpandedNodeId(
                "Scalar_Static_LocalizedText",
                Quickstarts.ReferenceServer.Namespaces.ReferenceServer),
            new ExpandedNodeId(
                "Scalar_Static_QualifiedName",
                Quickstarts.ReferenceServer.Namespaces.ReferenceServer),
            new ExpandedNodeId(
                "Scalar_Static_Variant",
                Quickstarts.ReferenceServer.Namespaces.ReferenceServer)
        ];

        /// <summary>
        /// static variables from namespace TestData
        /// </summary>
        public static readonly ExpandedNodeId[] NodeIdTestDataSetStatic =
        [
            new ExpandedNodeId(
                TestData.Variables.Data_Static_Scalar_Int16Value,
                TestData.Namespaces.TestData),
            new ExpandedNodeId(
                TestData.Variables.Data_Static_Scalar_Int32Value,
                TestData.Namespaces.TestData),
            new ExpandedNodeId(
                TestData.Variables.Data_Static_Scalar_UInt16Value,
                TestData.Namespaces.TestData),
            new ExpandedNodeId(
                TestData.Variables.Data_Static_Scalar_UInt32Value,
                TestData.Namespaces.TestData)
        ];

        /// <summary>
        /// CTT simulation data
        /// </summary>
        public static readonly ExpandedNodeId[] NodeIdTestSetSimulation =
        [
            new ExpandedNodeId(
                "Scalar_Simulation_SByte",
                Quickstarts.ReferenceServer.Namespaces.ReferenceServer),
            new ExpandedNodeId(
                "Scalar_Simulation_Int16",
                Quickstarts.ReferenceServer.Namespaces.ReferenceServer),
            new ExpandedNodeId(
                "Scalar_Simulation_Int32",
                Quickstarts.ReferenceServer.Namespaces.ReferenceServer),
            new ExpandedNodeId(
                "Scalar_Simulation_Byte",
                Quickstarts.ReferenceServer.Namespaces.ReferenceServer),
            new ExpandedNodeId(
                "Scalar_Simulation_UInt16",
                Quickstarts.ReferenceServer.Namespaces.ReferenceServer),
            new ExpandedNodeId(
                "Scalar_Simulation_UInt32",
                Quickstarts.ReferenceServer.Namespaces.ReferenceServer),
            new ExpandedNodeId(
                "Scalar_Simulation_NodeId",
                Quickstarts.ReferenceServer.Namespaces.ReferenceServer),
            new ExpandedNodeId(
                "Scalar_Simulation_LocalizedText",
                Quickstarts.ReferenceServer.Namespaces.ReferenceServer
            ),
            new ExpandedNodeId(
                "Scalar_Simulation_QualifiedName",
                Quickstarts.ReferenceServer.Namespaces.ReferenceServer
            ),
            new ExpandedNodeId(
                "Scalar_Simulation_Variant",
                Quickstarts.ReferenceServer.Namespaces.ReferenceServer)
        ];

        /// <summary>
        /// Ref server test data node manager.
        /// </summary>
        public static readonly ExpandedNodeId[] NodeIdTestSetDataSimulation =
        [
            new ExpandedNodeId(
                TestData.Variables.Data_Dynamic_Scalar_Int16Value,
                TestData.Namespaces.TestData),
            new ExpandedNodeId(
                TestData.Variables.Data_Dynamic_Scalar_Int32Value,
                TestData.Namespaces.TestData),
            new ExpandedNodeId(
                TestData.Variables.Data_Dynamic_Scalar_UInt16Value,
                TestData.Namespaces.TestData),
            new ExpandedNodeId(
                TestData.Variables.Data_Dynamic_Scalar_UInt32Value,
                TestData.Namespaces.TestData),
            new ExpandedNodeId(
                TestData.Variables.AnalogScalarValueObjectType_UInt32Value,
                TestData.Namespaces.TestData
            ),
            new ExpandedNodeId(
                TestData.Variables.Data_Dynamic_AnalogArray_ByteValue,
                TestData.Namespaces.TestData),
            new ExpandedNodeId(
                TestData.Variables.Data_Dynamic_Scalar_VectorValue,
                TestData.Namespaces.TestData),
            new ExpandedNodeId(
                TestData.Variables.Data_Dynamic_Scalar_VectorValue_X,
                TestData.Namespaces.TestData),
            new ExpandedNodeId(
                TestData.Variables.Data_Dynamic_Structure_ScalarStructure,
                TestData.Namespaces.TestData)
        ];

        public static readonly ExpandedNodeId[] NodeIdTestDataHistory =
        [
            new ExpandedNodeId(
                TestData.Variables.Data_Dynamic_Scalar_Int32Value,
                TestData.Namespaces.TestData)
        ];

        public static readonly ExpandedNodeId[] NodeIdMemoryBufferSimulation =
        [
            // dynamic variables from namespace MemoryBuffer
            new ExpandedNodeId("UInt32[64]", MemoryBuffer.Namespaces.MemoryBuffer + "/Instance"),
            new ExpandedNodeId("Double[40]", MemoryBuffer.Namespaces.MemoryBuffer + "/Instance")
        ];

        /// <summary>
        /// Mass numeric static nodes for performance tests.
        /// </summary>
        public static readonly (Type Type, ExpandedNodeId[] NodeIds)[] NodeIdTestSetStaticMassNumeric =
        [
            (typeof(short), Enumerable.Range(0, 100).Select(i =>
                new ExpandedNodeId($"Scalar_Static_Mass_Int16_Int16_{i:D2}", Quickstarts.ReferenceServer.Namespaces.ReferenceServer)).ToArray()),
            (typeof(int), Enumerable.Range(0, 100).Select(i =>
                new ExpandedNodeId($"Scalar_Static_Mass_Int32_Int32_{i:D2}", Quickstarts.ReferenceServer.Namespaces.ReferenceServer)).ToArray()),
            (typeof(long), Enumerable.Range(0, 100).Select(i =>
                new ExpandedNodeId($"Scalar_Static_Mass_Int64_Int64_{i:D2}", Quickstarts.ReferenceServer.Namespaces.ReferenceServer)).ToArray()),
            (typeof(ushort), Enumerable.Range(0, 100).Select(i =>
                new ExpandedNodeId($"Scalar_Static_Mass_UInt16_UInt16_{i:D2}", Quickstarts.ReferenceServer.Namespaces.ReferenceServer)).ToArray()),
            (typeof(uint), Enumerable.Range(0, 100).Select(i =>
                new ExpandedNodeId($"Scalar_Static_Mass_UInt32_UInt32_{i:D2}", Quickstarts.ReferenceServer.Namespaces.ReferenceServer)).ToArray()),
            (typeof(ulong), Enumerable.Range(0, 100).Select(i =>
                new ExpandedNodeId($"Scalar_Static_Mass_UInt64_UInt64_{i:D2}", Quickstarts.ReferenceServer.Namespaces.ReferenceServer)).ToArray())
        ];

        /// <summary>
        /// Worker function to browse the full address space of a server.
        /// </summary>
        /// <param name="services">The service interface.</param>
        /// <param name="operationLimits">The operation limits.</param>
        public static async Task<ArrayOf<ReferenceDescription>> BrowseFullAddressSpaceWorkerAsync(
            IServerTestServices services,
            RequestHeader requestHeader,
            OperationLimits operationLimits = null,
            BrowseDescription browseDescription = null,
            bool outputResult = false)
        {
            operationLimits ??= new OperationLimits();
            requestHeader.Timestamp = DateTime.UtcNow;

            // Browse template
            const uint startingNode = Objects.RootFolder;
            BrowseDescription browseTemplate =
                browseDescription
                ?? new BrowseDescription
                {
                    NodeId = startingNode,
                    BrowseDirection = BrowseDirection.Forward,
                    ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                    IncludeSubtypes = true,
                    NodeClassMask = 0,
                    ResultMask = (uint)BrowseResultMask.All
                };
            ArrayOf<BrowseDescription> browseDescriptionCollection =
                ServerFixtureUtils.CreateBrowseDescriptionCollectionFromNodeId(
                    [.. new NodeId[] { Objects.RootFolder }],
                    browseTemplate);

            // Browse
            uint requestedMaxReferencesPerNode = operationLimits.MaxNodesPerBrowse;
            bool verifyMaxNodesPerBrowse = operationLimits.MaxNodesPerBrowse > 0;
            var referenceDescriptions = new ReferenceDescriptionCollection();

            // Test if server responds with BadNothingToDo
            {
                ServiceResultException sre = NUnit.Framework.Assert.ThrowsAsync<ServiceResultException>(async () =>
                    _ = await services.BrowseAsync(
                        requestHeader,
                        null,
                        0,
                        ArrayOf<BrowseDescription>.Empty).ConfigureAwait(false));
                Assert.AreEqual(StatusCodes.BadNothingToDo, sre.StatusCode);
            }

            while (browseDescriptionCollection.Count > 0)
            {
                var allResults = new BrowseResultCollection();
                if (verifyMaxNodesPerBrowse &&
                    browseDescriptionCollection.Count > operationLimits.MaxNodesPerBrowse)
                {
                    verifyMaxNodesPerBrowse = false;
                    // Test if server responds with BadTooManyOperations
                    ServiceResultException sre = NUnit.Framework.Assert.ThrowsAsync<ServiceResultException>(async () =>
                            _ = await services.BrowseAsync(
                                requestHeader,
                                null,
                                0,
                                browseDescriptionCollection).ConfigureAwait(false));
                    Assert.AreEqual(
                        StatusCodes.BadTooManyOperations,
                        sre.StatusCode);

                    // Test if server responds with BadTooManyOperations
                    ArrayOf<BrowseDescription> tempBrowsePath =
                        browseDescriptionCollection[..((int)operationLimits.MaxNodesPerBrowse + 1)];
                    sre = NUnit.Framework.Assert.ThrowsAsync<ServiceResultException>(async () =>
                        _ = await services.BrowseAsync(
                            requestHeader,
                            null,
                            0,
                            tempBrowsePath).ConfigureAwait(false));
                    Assert.AreEqual(
                        StatusCodes.BadTooManyOperations,
                        sre.StatusCode);
                }

                bool repeatBrowse;
                uint maxNodesPerBrowse = operationLimits.MaxNodesPerBrowse;
                BrowseResponse browseResponse = null;
                do
                {
                    if (maxNodesPerBrowse >= browseDescriptionCollection.Count)
                    {
                        maxNodesPerBrowse = 0; // Do not slice, take all
                    }

                    ArrayOf<BrowseDescription> browseCollection = maxNodesPerBrowse == 0
                        ? browseDescriptionCollection
                        : browseDescriptionCollection[..(int)maxNodesPerBrowse];
                    repeatBrowse = false;
                    try
                    {
                        requestHeader.Timestamp = DateTime.UtcNow;
                        browseResponse = await services.BrowseAsync(
                            requestHeader,
                            null,
                            requestedMaxReferencesPerNode,
                            browseCollection).ConfigureAwait(false);
                        ServerFixtureUtils.ValidateResponse(
                            browseResponse.ResponseHeader,
                            browseResponse.Results,
                            browseCollection);
                        ServerFixtureUtils.ValidateDiagnosticInfos(
                            browseResponse.DiagnosticInfos,
                            browseCollection,
                            browseResponse.ResponseHeader.StringTable,
                            services.Logger);

                        allResults.AddRange(browseResponse.Results);
                    }
                    catch (ServiceResultException sre) when (
                        sre.StatusCode == StatusCodes.BadEncodingLimitsExceeded ||
                        sre.StatusCode == StatusCodes.BadResponseTooLarge)
                    {
                        // try to address by overriding operation limit
                        maxNodesPerBrowse =
                            maxNodesPerBrowse == 0
                                ? (uint)browseCollection.Count / 2
                                : maxNodesPerBrowse / 2;
                        repeatBrowse = true;
                    }
                } while (repeatBrowse);

                browseDescriptionCollection = maxNodesPerBrowse == 0 ?
                    default :
                    browseDescriptionCollection[(int)maxNodesPerBrowse..];

                // Browse next
                ArrayOf<ByteString> continuationPoints = ServerFixtureUtils.PrepareBrowseNext(
                    browseResponse.Results);
                while (continuationPoints.Count > 0)
                {
                    requestHeader.Timestamp = DateTime.UtcNow;
                    BrowseNextResponse browseNextResponse = await services.BrowseNextAsync(
                        requestHeader,
                        false,
                        continuationPoints).ConfigureAwait(false);
                    ServerFixtureUtils.ValidateResponse(
                        browseNextResponse.ResponseHeader,
                        browseNextResponse.Results,
                        continuationPoints);
                    ServerFixtureUtils.ValidateDiagnosticInfos(
                        browseNextResponse.DiagnosticInfos,
                        continuationPoints,
                        browseNextResponse.ResponseHeader.StringTable,
                        services.Logger);
                    allResults.AddRange(browseNextResponse.Results);
                    continuationPoints = ServerFixtureUtils.PrepareBrowseNext(
                        browseNextResponse.Results);
                }

                // Build browse request for next level
                var browseTable = new NodeIdCollection();
                foreach (BrowseResult result in allResults)
                {
                    referenceDescriptions.AddRange(result.References);
                    foreach (ReferenceDescription reference in result.References)
                    {
                        browseTable.Add(ExpandedNodeId.ToNodeId(reference.NodeId, null));
                    }
                }
                browseDescriptionCollection = ServerFixtureUtils
                    .CreateBrowseDescriptionCollectionFromNodeId(
                        browseTable,
                        browseTemplate);
            }

            referenceDescriptions.Sort((x, y) => x.NodeId.CompareTo(y.NodeId));
            int diagnosticNs = services.MessageContext.NamespaceUris
                .GetIndex(Ua.Namespaces.OpcUa + "Diagnostics");
            // Remove diagnostic nodes since they change per session
            referenceDescriptions.RemoveAll(r => r.NodeId.NamespaceIndex == diagnosticNs);

            TestContext.Out
                .WriteLine("Found {0} references on server.", referenceDescriptions.Count);
            if (outputResult)
            {
                foreach (ReferenceDescription reference in referenceDescriptions)
                {
                    TestContext.Out.WriteLine(
                        "NodeId {0} {1} {2}",
                        reference.NodeId,
                        reference.NodeClass,
                        reference.BrowseName);
                }
            }
            return referenceDescriptions;
        }

        /// <summary>
        /// Worker method to translate the browse path.
        /// </summary>
        public static async Task<ArrayOf<BrowsePathResult>> TranslateBrowsePathWorkerAsync(
            IServerTestServices services,
            ArrayOf<ReferenceDescription> referenceDescriptions,
            RequestHeader requestHeader,
            OperationLimits operationLimits)
        {
            // Browse template
            const uint startingNode = Objects.RootFolder;
            requestHeader.Timestamp = DateTime.UtcNow;

            // TranslateBrowsePath
            bool verifyMaxNodesPerBrowse = operationLimits
                .MaxNodesPerTranslateBrowsePathsToNodeIds > 0;
            ArrayOf<BrowsePath> browsePaths = referenceDescriptions.ConvertAll(r => new BrowsePath
            {
                RelativePath = new RelativePath(r.BrowseName),
                StartingNode = startingNode
            });
            var allBrowsePaths = new BrowsePathResultCollection();
            while (browsePaths.Count > 0)
            {
                if (verifyMaxNodesPerBrowse &&
                    browsePaths.Count > operationLimits.MaxNodesPerTranslateBrowsePathsToNodeIds)
                {
                    verifyMaxNodesPerBrowse = false;
                    // Test if server responds with BadTooManyOperations
                    ServiceResultException sre = NUnit.Framework.Assert.ThrowsAsync<ServiceResultException>(async () =>
                            _ = await services.TranslateBrowsePathsToNodeIdsAsync(
                                requestHeader,
                                browsePaths).ConfigureAwait(false));
                    Assert.AreEqual(
                        StatusCodes.BadTooManyOperations,
                        sre.StatusCode);
                }
                ArrayOf<BrowsePath> browsePathSnippet =
                    operationLimits.MaxNodesPerTranslateBrowsePathsToNodeIds == 0 ||
                        browsePaths.Count <= operationLimits.MaxNodesPerTranslateBrowsePathsToNodeIds
                        ? browsePaths // take all
                        : browsePaths[..(int)operationLimits.MaxNodesPerTranslateBrowsePathsToNodeIds];
                TranslateBrowsePathsToNodeIdsResponse translateResponse = await services.TranslateBrowsePathsToNodeIdsAsync(
                    requestHeader,
                    browsePathSnippet).ConfigureAwait(false);
                ServerFixtureUtils.ValidateResponse(translateResponse.ResponseHeader, translateResponse.Results, browsePathSnippet);
                ServerFixtureUtils.ValidateDiagnosticInfos(
                    translateResponse.DiagnosticInfos,
                    browsePathSnippet,
                    translateResponse.ResponseHeader.StringTable,
                    services.Logger);
                allBrowsePaths.AddRange(translateResponse.Results);
                foreach (BrowsePathResult result in translateResponse.Results)
                {
                    if (result.Targets?.Count > 0)
                    {
                        TestContext.Out.WriteLine("BrowsePath {0}",
                            result.Targets[0].TargetId.ToString());
                    }
                }

                browsePaths =
                    operationLimits.MaxNodesPerTranslateBrowsePathsToNodeIds == 0 ||
                        browsePaths.Count <= operationLimits.MaxNodesPerTranslateBrowsePathsToNodeIds ?
                        default : // done
                        browsePaths[(int)operationLimits.MaxNodesPerTranslateBrowsePathsToNodeIds..];
            }
            return allBrowsePaths;
        }

        /// <summary>
        /// Worker method to test subscriptions of a server.
        /// </summary>
        public static async Task SubscriptionTestAsync(
            IServerTestServices services,
            RequestHeader requestHeader)
        {
            // start time
            requestHeader.Timestamp = DateTime.UtcNow;

            // create subscription
            const double publishingInterval = 1000.0;
            const uint lifetimeCount = 60;
            const uint maxKeepAliveCount = 2;
            const uint maxNotificationPerPublish = 0;
            const byte priority = 128;
            bool enabled = false;
            const uint queueSize = 5;

            CreateSubscriptionResponse createSubscriptionResponse = await services.CreateSubscriptionAsync(
                requestHeader,
                publishingInterval,
                lifetimeCount,
                maxKeepAliveCount,
                maxNotificationPerPublish,
                enabled,
                priority).ConfigureAwait(false);
            Assert.AreEqual(publishingInterval, createSubscriptionResponse.RevisedPublishingInterval);
            Assert.AreEqual(lifetimeCount, createSubscriptionResponse.RevisedLifetimeCount);
            Assert.AreEqual(maxKeepAliveCount, createSubscriptionResponse.RevisedMaxKeepAliveCount);
            ServerFixtureUtils.ValidateResponse(createSubscriptionResponse.ResponseHeader);
            uint id = createSubscriptionResponse.SubscriptionId;

            ArrayOf<MonitoredItemCreateRequest> itemsToCreate = default;
            // check badnothingtodo
            ServiceResultException sre = NUnit.Framework.Assert.ThrowsAsync<ServiceResultException>(async () =>
                await services.CreateMonitoredItemsAsync(
                    requestHeader,
                    id,
                    TimestampsToReturn.Neither,
                    itemsToCreate).ConfigureAwait(false));
            Assert.AreEqual(StatusCodes.BadNothingToDo, sre.StatusCode);

            // add item
            uint handleCounter = 1;
            itemsToCreate =
            [
                new MonitoredItemCreateRequest
                {
                    ItemToMonitor = new ReadValueId
                    {
                        AttributeId = Attributes.Value,
                        NodeId = VariableIds.Server_ServerStatus_CurrentTime
                    },
                    MonitoringMode = MonitoringMode.Reporting,
                    RequestedParameters = new MonitoringParameters
                    {
                        ClientHandle = ++handleCounter,
                        SamplingInterval = -1,
                        Filter = default,
                        DiscardOldest = true,
                        QueueSize = queueSize
                    }
                },
                //add event item
                CreateEventMonitoredItem(queueSize, ref handleCounter)
            ];

            CreateMonitoredItemsResponse createMonitoredItemsResponse = await services.CreateMonitoredItemsAsync(
                requestHeader,
                id,
                TimestampsToReturn.Neither,
                itemsToCreate).ConfigureAwait(false);
            ServerFixtureUtils.ValidateResponse(createMonitoredItemsResponse.ResponseHeader, createMonitoredItemsResponse.Results, itemsToCreate);
            ServerFixtureUtils.ValidateDiagnosticInfos(
                createMonitoredItemsResponse.DiagnosticInfos,
                itemsToCreate,
                createMonitoredItemsResponse.ResponseHeader.StringTable,
                services.Logger);

            // modify subscription
            ModifySubscriptionResponse modifySubscriptionResponse = await services.ModifySubscriptionAsync(
                requestHeader,
                id,
                publishingInterval,
                lifetimeCount,
                maxKeepAliveCount,
                maxNotificationPerPublish,
                priority).ConfigureAwait(false);
            Assert.AreEqual(publishingInterval, modifySubscriptionResponse.RevisedPublishingInterval);
            Assert.AreEqual(lifetimeCount, modifySubscriptionResponse.RevisedLifetimeCount);
            Assert.AreEqual(maxKeepAliveCount, modifySubscriptionResponse.RevisedMaxKeepAliveCount);
            ServerFixtureUtils.ValidateResponse(modifySubscriptionResponse.ResponseHeader);

            // modify monitored item, just timestamps to return
            ArrayOf<MonitoredItemModifyRequest> itemsToModify = createMonitoredItemsResponse.Results
                .ConvertAll(itemCreated => new MonitoredItemModifyRequest
                {
                    MonitoredItemId = itemCreated.MonitoredItemId
                });
            ModifyMonitoredItemsResponse modifyMonitoredItemsResponse = await services.ModifyMonitoredItemsAsync(
                requestHeader,
                id,
                TimestampsToReturn.Both,
                itemsToModify).ConfigureAwait(false);
            ServerFixtureUtils.ValidateResponse(modifyMonitoredItemsResponse.ResponseHeader, modifyMonitoredItemsResponse.Results, itemsToModify);
            ServerFixtureUtils.ValidateDiagnosticInfos(
                modifyMonitoredItemsResponse.DiagnosticInfos,
                itemsToModify,
                modifyMonitoredItemsResponse.ResponseHeader.StringTable,
                services.Logger);

            // publish request
            ArrayOf<SubscriptionAcknowledgement> acknowledgements = default;
            PublishResponse publishResponse = await services.PublishAsync(
                requestHeader,
                acknowledgements).ConfigureAwait(false);
            ServerFixtureUtils.ValidateResponse(publishResponse.ResponseHeader, publishResponse.Results, acknowledgements);
            ServerFixtureUtils.ValidateDiagnosticInfos(
                publishResponse.DiagnosticInfos,
                acknowledgements,
                publishResponse.ResponseHeader.StringTable,
                services.Logger);
            Assert.AreEqual(id, publishResponse.SubscriptionId);
            Assert.AreEqual(0, publishResponse.AvailableSequenceNumbers.Count);

            // enable publishing
            enabled = true;
            ArrayOf<uint> subscriptions = [id];
            SetPublishingModeResponse setPublishingModeResponse = await services.SetPublishingModeAsync(
                requestHeader,
                enabled,
                subscriptions).ConfigureAwait(false);
            ServerFixtureUtils.ValidateResponse(setPublishingModeResponse.ResponseHeader, setPublishingModeResponse.Results, subscriptions);
            ServerFixtureUtils.ValidateDiagnosticInfos(
                setPublishingModeResponse.DiagnosticInfos,
                subscriptions,
                setPublishingModeResponse.ResponseHeader.StringTable,
                services.Logger);

            // wait some time to fill queue
            int loopCounter = (int)queueSize;
            Thread.Sleep(loopCounter * 1000);

            acknowledgements = [];
            do
            {
                // get publish responses
                publishResponse = await services.PublishAsync(
                    requestHeader,
                    acknowledgements).ConfigureAwait(false);
                ServerFixtureUtils.ValidateResponse(publishResponse.ResponseHeader, publishResponse.Results, acknowledgements);
                ServerFixtureUtils.ValidateDiagnosticInfos(
                    publishResponse.DiagnosticInfos,
                    acknowledgements,
                    publishResponse.ResponseHeader.StringTable,
                    services.Logger);
                Assert.AreEqual(id, publishResponse.SubscriptionId);

                if (publishResponse.NotificationMessage.NotificationData.Count == 0)
                {
                    TestContext.Out.WriteLine("No notifications received in publish");
                }
                else
                {
                    var dataChangeNotification = publishResponse.NotificationMessage.NotificationData[0]
                        .TryGetEncodeable(out DataChangeNotification d) ? d : default;
                    var eventNotification = publishResponse.NotificationMessage.NotificationData[0]
                        .TryGetEncodeable(out EventNotificationList e) ? e : default;
                    TestContext.Out.WriteLine(
                        "Notification: {0} {1}",
                        publishResponse.NotificationMessage.SequenceNumber,
                        publishResponse.NotificationMessage.PublishTime);
                }

                acknowledgements =
                [
                    new SubscriptionAcknowledgement
                    {
                        SubscriptionId = id,
                        SequenceNumber = publishResponse.NotificationMessage.SequenceNumber
                    }
                ];
            } while (acknowledgements.Count > 0 && --loopCounter > 0);

            // republish
            RepublishResponse republishResponse = await services.RepublishAsync(
                requestHeader,
                publishResponse.SubscriptionId,
                publishResponse.NotificationMessage.SequenceNumber).ConfigureAwait(false);
            ServerFixtureUtils.ValidateResponse(republishResponse.ResponseHeader);

            // disable publishing
            enabled = false;
            setPublishingModeResponse = await services.SetPublishingModeAsync(
                requestHeader,
                enabled,
                subscriptions).ConfigureAwait(false);
            ServerFixtureUtils.ValidateResponse(setPublishingModeResponse.ResponseHeader, setPublishingModeResponse.Results, subscriptions);
            ServerFixtureUtils.ValidateDiagnosticInfos(
                setPublishingModeResponse.DiagnosticInfos,
                subscriptions,
                setPublishingModeResponse.ResponseHeader.StringTable,
                services.Logger);

            // disable monitoring
            ArrayOf<uint> monitoredItemIds =
                createMonitoredItemsResponse.Results.ConvertAll(r => r.MonitoredItemId);
            SetMonitoringModeResponse setMonitoringModeResponse = await services.SetMonitoringModeAsync(
                requestHeader,
                id,
                MonitoringMode.Disabled,
                monitoredItemIds).ConfigureAwait(false);
            ServerFixtureUtils.ValidateResponse(setMonitoringModeResponse.ResponseHeader, setMonitoringModeResponse.Results, monitoredItemIds);
            ServerFixtureUtils.ValidateDiagnosticInfos(
                setMonitoringModeResponse.DiagnosticInfos,
                monitoredItemIds,
                setMonitoringModeResponse.ResponseHeader.StringTable,
                services.Logger);

            // delete subscription
            DeleteSubscriptionsResponse deleteSubscriptionsResponse = await services.DeleteSubscriptionsAsync(
                requestHeader,
                subscriptions).ConfigureAwait(false);
            ServerFixtureUtils.ValidateResponse(deleteSubscriptionsResponse.ResponseHeader, deleteSubscriptionsResponse.Results, subscriptions);
            ServerFixtureUtils.ValidateDiagnosticInfos(
                deleteSubscriptionsResponse.DiagnosticInfos,
                subscriptions,
                deleteSubscriptionsResponse.ResponseHeader.StringTable,
                services.Logger);
        }

        /// <summary>
        /// Worker method to test TransferSubscriptions of a server.
        /// </summary>
        public static async Task<UInt32Collection> CreateSubscriptionForTransferAsync(
            IServerTestServices services,
            RequestHeader requestHeader,
            NodeId[] testNodes,
            uint queueSize = DefaultMonitoredItemsQueueSize,
            int samplingInterval = DefaultMonitoredItemsSamplingInterval)
        {
            // start time

            requestHeader.Timestamp = DateTime.UtcNow;
            uint subscriptionId = await CreateSubscriptionAsync(services, requestHeader).ConfigureAwait(false);
            uint clientHandle = 1;
            foreach (NodeId testNode in testNodes)
            {
                await CreateMonitoredItemAsync(
                    services,
                    requestHeader,
                    subscriptionId,
                    testNode,
                    clientHandle++,
                    queueSize,
                    samplingInterval).ConfigureAwait(false);
            }

            ArrayOf<uint> subscriptionIds = [subscriptionId];

            // enable publishing
            SetPublishingModeResponse setPublishingModeResponse = await services.SetPublishingModeAsync(
                requestHeader,
                true,
                subscriptionIds).ConfigureAwait(false);
            ServerFixtureUtils.ValidateResponse(setPublishingModeResponse.ResponseHeader, setPublishingModeResponse.Results, subscriptionIds);
            ServerFixtureUtils.ValidateDiagnosticInfos(
                setPublishingModeResponse.DiagnosticInfos,
                subscriptionIds,
                setPublishingModeResponse.ResponseHeader.StringTable,
                services.Logger);

            // wait some time to settle
            Thread.Sleep(1000);

            // publish request (use invalid sequence number for status)
            ArrayOf<SubscriptionAcknowledgement> acknowledgements =
            [
                new SubscriptionAcknowledgement
                {
                    SubscriptionId = subscriptionId,
                    SequenceNumber = 123
                }
            ];
            PublishResponse publishResponse = await services.PublishAsync(
                requestHeader,
                acknowledgements).ConfigureAwait(false);
            ServerFixtureUtils.ValidateResponse(publishResponse.ResponseHeader, publishResponse.Results, acknowledgements);
            ServerFixtureUtils.ValidateDiagnosticInfos(
                publishResponse.DiagnosticInfos,
                acknowledgements,
                publishResponse.ResponseHeader.StringTable,
                services.Logger);
            Assert.AreEqual(subscriptionId, publishResponse.SubscriptionId);

            // static node, do not acknowledge
            Assert.AreEqual(1, publishResponse.AvailableSequenceNumbers.Count);

            return subscriptionIds;
        }

        /// <summary>
        /// Worker method to test Transfer of subscriptions to new session.
        /// </summary>
        public static async Task TransferSubscriptionTestAsync(
            IServerTestServices services,
            RequestHeader requestHeader,
            ArrayOf<uint> subscriptionIds,
            bool sendInitialData,
            bool expectAccessDenied)
        {
            Assert.AreEqual(1, subscriptionIds.Count);

            requestHeader.Timestamp = DateTime.UtcNow;
            TransferSubscriptionsResponse transferResponse = await services.TransferSubscriptionsAsync(
                requestHeader,
                subscriptionIds,
                sendInitialData).ConfigureAwait(false);
            Assert.AreEqual(StatusCodes.Good, transferResponse.ResponseHeader.ServiceResult);
            Assert.AreEqual(subscriptionIds.Count, transferResponse.Results.Count);
            ServerFixtureUtils.ValidateResponse(transferResponse.ResponseHeader, transferResponse.Results, subscriptionIds);
            ServerFixtureUtils.ValidateDiagnosticInfos(
                transferResponse.DiagnosticInfos,
                subscriptionIds,
                transferResponse.ResponseHeader.StringTable,
                services.Logger);

            foreach (TransferResult transferResult in transferResponse.Results)
            {
                TestContext.Out.WriteLine("TransferResult: {0}", transferResult.StatusCode);
                if (expectAccessDenied)
                {
                    Assert.AreEqual(
                        StatusCodes.BadUserAccessDenied,
                        transferResult.StatusCode);
                }
                else
                {
                    Assert.IsTrue(StatusCode.IsGood(transferResult.StatusCode));
                    Assert.AreEqual(1, transferResult.AvailableSequenceNumbers.Count);
                }
            }

            if (expectAccessDenied)
            {
                return;
            }

            requestHeader.Timestamp = DateTime.UtcNow;
            ArrayOf<SubscriptionAcknowledgement> acknowledgements = default;
            PublishResponse publishResponse = await services.PublishAsync(
                requestHeader,
                acknowledgements).ConfigureAwait(false);
            Assert.AreEqual(StatusCodes.Good, publishResponse.ResponseHeader.ServiceResult);
            ServerFixtureUtils.ValidateResponse(publishResponse.ResponseHeader);
            ServerFixtureUtils.ValidateDiagnosticInfos(
                publishResponse.DiagnosticInfos,
                acknowledgements,
                publishResponse.ResponseHeader.StringTable,
                services.Logger);
            Assert.AreEqual(subscriptionIds[0], publishResponse.SubscriptionId);
            Assert.AreEqual(sendInitialData ? 1 : 0, publishResponse.NotificationMessage.NotificationData.Count);
            if (sendInitialData)
            {
                ExtensionObject items = publishResponse.NotificationMessage.NotificationData.FirstOrDefault();
                Assert.IsTrue(items.TryGetEncodeable(out DataChangeNotification dataChangeNotification));
                MonitoredItemNotificationCollection monitoredItemsCollection = dataChangeNotification.MonitoredItems;
                Assert.IsNotEmpty(monitoredItemsCollection);
            }
            //Assert.AreEqual(0, availableSequenceNumbers.Count);

            requestHeader.Timestamp = DateTime.UtcNow;
            DeleteSubscriptionsResponse deleteResponse = await services.DeleteSubscriptionsAsync(requestHeader, subscriptionIds).ConfigureAwait(false);
            Assert.AreEqual(StatusCodes.Good, deleteResponse.ResponseHeader.ServiceResult);
        }

        /// <summary>
        /// Worker method to verify the SubscriptionTransferred message of a server.
        /// </summary>
        public static async Task VerifySubscriptionTransferredAsync(
            IServerTestServices services,
            RequestHeader requestHeader,
            ArrayOf<uint> subscriptionIds,
            bool deleteSubscriptions)
        {
            // start time
            requestHeader.Timestamp = DateTime.UtcNow;

            // wait some time to settle
            Thread.Sleep(100);

            // publish request
            ArrayOf<SubscriptionAcknowledgement> acknowledgements = default;
            PublishResponse publishResponse = await services.PublishAsync(
                requestHeader,
                acknowledgements).ConfigureAwait(false);
            ServerFixtureUtils.ValidateResponse(publishResponse.ResponseHeader);
            ServerFixtureUtils.ValidateDiagnosticInfos(
                publishResponse.DiagnosticInfos,
                acknowledgements,
                publishResponse.ResponseHeader.StringTable,
                services.Logger);
            Assert.IsFalse(publishResponse.MoreNotifications);
            Assert.IsTrue(subscriptionIds.ToArray().Contains(publishResponse.SubscriptionId));
            Assert.AreEqual(1, publishResponse.NotificationMessage.NotificationData.Count);
            string statusMessage = publishResponse.NotificationMessage.NotificationData[0].ToString();
            // Should contain GoodSubscriptionTransferred status code
            Assert.AreEqual("{GoodSubscriptionTransferred [0x002D0000] | }", statusMessage);

            // static node, do not acknowledge
            Assert.AreEqual(0, publishResponse.AvailableSequenceNumbers.Count);

            if (deleteSubscriptions)
            {
                DeleteSubscriptionsResponse deleteResponse = await services.DeleteSubscriptionsAsync(
                    requestHeader,
                    subscriptionIds).ConfigureAwait(false);
                ServerFixtureUtils.ValidateResponse(deleteResponse.ResponseHeader, deleteResponse.Results, subscriptionIds);
                ServerFixtureUtils.ValidateDiagnosticInfos(
                    deleteResponse.DiagnosticInfos,
                    subscriptionIds,
                    deleteResponse.ResponseHeader.StringTable,
                    services.Logger);
            }
        }

        private static async Task<uint> CreateSubscriptionAsync(
            IServerTestServices services,
            RequestHeader requestHeader)
        {
            // start time
            requestHeader.Timestamp = DateTime.UtcNow;

            // create subscription
            const double publishingInterval = 1000.0;
            const uint lifetimeCount = 60;
            const uint maxKeepAliveCount = 2;
            const uint maxNotificationPerPublish = 0;
            const byte priority = 128;
            const bool enabled = false;
            CreateSubscriptionResponse response = await services.CreateSubscriptionAsync(
                requestHeader,
                publishingInterval,
                lifetimeCount,
                maxKeepAliveCount,
                maxNotificationPerPublish,
                enabled,
                priority).ConfigureAwait(false);
            ServerFixtureUtils.ValidateResponse(response.ResponseHeader);

            return response.SubscriptionId;
        }

        private static async Task CreateMonitoredItemAsync(
            IServerTestServices services,
            RequestHeader requestHeader,
            uint subscriptionId,
            NodeId nodeId,
            uint clientHandle,
            uint queueSize,
            int samplingInterval)
        {
            ArrayOf<MonitoredItemCreateRequest> itemsToCreate =
            [
                // add item
                new MonitoredItemCreateRequest
                {
                    ItemToMonitor = new ReadValueId
                    {
                        AttributeId = Attributes.Value,
                        NodeId = nodeId
                    },
                    MonitoringMode = MonitoringMode.Reporting,
                    RequestedParameters = new MonitoringParameters
                    {
                        ClientHandle = clientHandle,
                        SamplingInterval = samplingInterval,
                        Filter = default,
                        DiscardOldest = true,
                        QueueSize = queueSize
                    }
                }
            ];
            CreateMonitoredItemsResponse response = await services.CreateMonitoredItemsAsync(
                requestHeader,
                subscriptionId,
                TimestampsToReturn.Neither,
                itemsToCreate).ConfigureAwait(false);
            ServerFixtureUtils.ValidateResponse(response.ResponseHeader, response.Results, itemsToCreate);
            ServerFixtureUtils.ValidateDiagnosticInfos(
                response.DiagnosticInfos,
                itemsToCreate,
                response.ResponseHeader.StringTable,
                services.Logger);
        }

        private static MonitoredItemCreateRequest CreateEventMonitoredItem(
            uint queueSize,
            ref uint handleCounter)
        {
            var whereClause = new ContentFilter();

            whereClause.Push(
                FilterOperator.Equals,
                [
                    new SimpleAttributeOperand
                    {
                        AttributeId = Attributes.Value,
                        TypeDefinitionId = ObjectTypeIds.BaseEventType,
                        BrowsePath = [.. new QualifiedName[] { QualifiedName.From("EventType") }]
                    },
                    new LiteralOperand {
                        Value = new Variant(ObjectTypeIds.BaseEventType) }
                ]);

            return new MonitoredItemCreateRequest
            {
                ItemToMonitor = new ReadValueId
                {
                    AttributeId = Attributes.EventNotifier,
                    NodeId = ObjectIds.Server
                },
                MonitoringMode = MonitoringMode.Reporting,
                RequestedParameters = new MonitoringParameters
                {
                    ClientHandle = ++handleCounter,
                    SamplingInterval = -1,
                    Filter = new ExtensionObject(
                        new EventFilter
                        {
                            SelectClauses =
                            [
                                .. new SimpleAttributeOperand[]
                                {
                                    new()
                                    {
                                        AttributeId = Attributes.Value,
                                        TypeDefinitionId = ObjectTypeIds.BaseEventType,
                                        BrowsePath = [.. new QualifiedName[] {
                                            QualifiedName.From(BrowseNames.Message) }]
                                    }
                                }
                            ],
                            WhereClause = whereClause
                        }
                    ),
                    DiscardOldest = true,
                    QueueSize = queueSize
                }
            };
        }
    }
}
