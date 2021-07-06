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
using NUnit.Framework;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Test workers using test services.
    /// </summary>
    public static class CommonTestWorkers
    {
        #region Public Workers
        /// <summary>
        /// Worker function to browse the full address space of a server.
        /// </summary>
        /// <param name="services">The service interface.</param>
        /// <param name="operationLimits">The operation limits.</param>
        public static ReferenceDescriptionCollection BrowseFullAddressSpaceWorker(
            IServerTestServices services,
            RequestHeader requestHeader,
            OperationLimits operationLimits = null,
            BrowseDescription browseDescription = null)
        {
            operationLimits = operationLimits ?? new OperationLimits();
            requestHeader.Timestamp = DateTime.UtcNow;

            // Browse template
            var startingNode = Objects.RootFolder;
            var browseTemplate = browseDescription ?? new BrowseDescription {
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

            // Browse
            ResponseHeader response = null;
            uint requestedMaxReferencesPerNode = operationLimits.MaxNodesPerBrowse;
            bool verifyMaxNodesPerBrowse = operationLimits.MaxNodesPerBrowse > 0;
            var referenceDescriptions = new ReferenceDescriptionCollection();

            // Test if server responds with BadTooManyOperations
            var sre = Assert.Throws<ServiceResultException>(() =>
                _ = services.Browse(requestHeader, null,
                    0, browseDescriptionCollection.Take(0).ToArray(),
                    out var results, out var infos));
            Assert.AreEqual(StatusCodes.BadNothingToDo, sre.StatusCode);

            while (browseDescriptionCollection.Any())
            {
                BrowseResultCollection allResults = new BrowseResultCollection();
#if mist
                if (verifyMaxNodesPerBrowse &&
                    browseDescriptionCollection.Count > operationLimits.MaxNodesPerBrowse)
                {
                    verifyMaxNodesPerBrowse = false;
                    // Test if server responds with BadTooManyOperations
                    sre = Assert.Throws<ServiceResultException>(() =>
                        _ = services.Browse(requestHeader, null,
                            0, browseDescriptionCollection,
                            out var results, out var infos));
                    Assert.AreEqual(StatusCodes.BadTooManyOperations, sre.StatusCode);

                    // Test if server responds with BadTooManyOperations
                    var tempBrowsePath = browseDescriptionCollection.Take((int)operationLimits.MaxNodesPerBrowse + 1).ToArray();
                    sre = Assert.Throws<ServiceResultException>(() =>
                        _ = services.Browse(requestHeader, null,
                            0, tempBrowsePath,
                            out var results, out var infos));
                    Assert.AreEqual(StatusCodes.BadTooManyOperations, sre.StatusCode);
                }
#endif
                var browseCollection = (operationLimits.MaxNodesPerBrowse == 0) ?
                    browseDescriptionCollection :
                    browseDescriptionCollection.Take((int)operationLimits.MaxNodesPerBrowse).ToArray();

                requestHeader.Timestamp = DateTime.UtcNow;
                response = services.Browse(requestHeader, null,
                    requestedMaxReferencesPerNode, browseCollection,
                    out var browseResultCollection, out var diagnosticsInfoCollection);
                ServerFixtureUtils.ValidateResponse(response);
                ServerFixtureUtils.ValidateDiagnosticInfos(diagnosticsInfoCollection, browseCollection);

                allResults.AddRange(browseResultCollection);
                if (operationLimits.MaxNodesPerBrowse == 0)
                {
                    browseDescriptionCollection.Clear();
                }
                else
                {
                    browseDescriptionCollection = browseDescriptionCollection.Skip((int)operationLimits.MaxNodesPerBrowse).ToArray();
                }

                // Browse next
                var continuationPoints = ServerFixtureUtils.PrepareBrowseNext(browseResultCollection);
                while (continuationPoints.Any())
                {
                    requestHeader.Timestamp = DateTime.UtcNow;
                    response = services.BrowseNext(requestHeader, false, continuationPoints,
                        out var browseNextResultCollection, out diagnosticsInfoCollection);
                    ServerFixtureUtils.ValidateResponse(response);
                    ServerFixtureUtils.ValidateDiagnosticInfos(diagnosticsInfoCollection, continuationPoints);
                    allResults.AddRange(browseNextResultCollection);
                    continuationPoints = ServerFixtureUtils.PrepareBrowseNext(browseNextResultCollection);
                }

                // build browse request for next level
                var browseTable = new NodeIdCollection();
                foreach (var result in allResults)
                {
                    referenceDescriptions.AddRange(result.References);
                    foreach (var reference in result.References)
                    {
                        browseTable.Add(ExpandedNodeId.ToNodeId(reference.NodeId, null));
                    }
                }
                browseDescriptionCollection = ServerFixtureUtils.CreateBrowseDescriptionCollectionFromNodeId(browseTable, browseTemplate);
            }

            TestContext.Out.WriteLine("Found {0} references on server.", referenceDescriptions.Count);
            foreach (var reference in referenceDescriptions)
            {
                TestContext.Out.WriteLine("NodeId {0} {1} {2}", reference.NodeId, reference.NodeClass, reference.BrowseName);
            }
            return referenceDescriptions;
        }

        /// <summary>
        /// Worker method to translate the browse path.
        /// </summary>
        public static BrowsePathResultCollection TranslateBrowsePathWorker(
            IServerTestServices services,
            ReferenceDescriptionCollection referenceDescriptions,
            RequestHeader requestHeader,
            OperationLimits operationLimits)
        {
            // Browse template
            var startingNode = Objects.RootFolder;
            requestHeader.Timestamp = DateTime.UtcNow;

            // TranslateBrowsePath
            bool verifyMaxNodesPerBrowse = operationLimits.MaxNodesPerTranslateBrowsePathsToNodeIds > 0;
            var browsePaths = new BrowsePathCollection(
                referenceDescriptions.Select(r => new BrowsePath() { RelativePath = new RelativePath(r.BrowseName), StartingNode = startingNode })
                );
            BrowsePathResultCollection allBrowsePaths = new BrowsePathResultCollection();
            while (browsePaths.Any())
            {
                if (verifyMaxNodesPerBrowse &&
                    browsePaths.Count > operationLimits.MaxNodesPerTranslateBrowsePathsToNodeIds)
                {
                    verifyMaxNodesPerBrowse = false;
                    // Test if server responds with BadTooManyOperations
                    var sre = Assert.Throws<ServiceResultException>(() =>
                        _ = services.TranslateBrowsePathsToNodeIds(requestHeader, browsePaths, out var results, out var infos));
                    Assert.AreEqual(StatusCodes.BadTooManyOperations, sre.StatusCode);
                }
                var browsePathSnippet = (operationLimits.MaxNodesPerTranslateBrowsePathsToNodeIds > 0) ?
                    browsePaths.Take((int)operationLimits.MaxNodesPerTranslateBrowsePathsToNodeIds).ToArray() :
                    browsePaths;
                ResponseHeader response = services.TranslateBrowsePathsToNodeIds(requestHeader, browsePathSnippet, out var browsePathResults, out var diagnosticInfos);
                ServerFixtureUtils.ValidateResponse(response);
                ServerFixtureUtils.ValidateDiagnosticInfos(diagnosticInfos, browsePathSnippet);
                allBrowsePaths.AddRange(browsePathResults);
                foreach (var result in browsePathResults)
                {
                    if (result.Targets?.Count > 0)
                    {
                        TestContext.Out.WriteLine("BrowsePath {0}", result.Targets[0].ToString());
                    }
                }

                if (operationLimits.MaxNodesPerTranslateBrowsePathsToNodeIds == 0)
                {
                    browsePaths.Clear();
                }
                else
                {
                    browsePaths = browsePaths.Skip((int)operationLimits.MaxNodesPerTranslateBrowsePathsToNodeIds).ToArray();
                }
            }
            return allBrowsePaths;
        }

        /// <summary>
        /// Worker method to test subscriptions of a server.
        /// </summary>
        /// <param name="services"></param>
        /// <param name="requestHeader"></param>
        public static void SubscriptionTest(
            IServerTestServices services,
            RequestHeader requestHeader)
        {
            // start time
            requestHeader.Timestamp = DateTime.UtcNow;

            // create subscription
            double publishingInterval = 1000.0;
            uint lifetimeCount = 60;
            uint maxKeepAliveCount = 2;
            uint maxNotificationPerPublish = 0;
            byte priority = 128;
            bool enabled = false;
            uint queueSize = 5;

            var response = services.CreateSubscription(requestHeader,
                publishingInterval, lifetimeCount, maxKeepAliveCount,
                maxNotificationPerPublish, enabled, priority,
                out uint id, out double revisedPublishingInterval, out uint revisedLifetimeCount, out uint revisedMaxKeepAliveCount);
            Assert.AreEqual(publishingInterval, revisedPublishingInterval);
            Assert.AreEqual(lifetimeCount, revisedLifetimeCount);
            Assert.AreEqual(maxKeepAliveCount, revisedMaxKeepAliveCount);
            ServerFixtureUtils.ValidateResponse(response);

            MonitoredItemCreateRequestCollection itemsToCreate = new MonitoredItemCreateRequestCollection();
            // check badnothingtodo
            var sre = Assert.Throws<ServiceResultException>(() =>
                services.CreateMonitoredItems(requestHeader, id, TimestampsToReturn.Neither, itemsToCreate,
                    out MonitoredItemCreateResultCollection mockResults, out DiagnosticInfoCollection mockInfos));
            Assert.AreEqual(StatusCodes.BadNothingToDo, sre.StatusCode);

            // add item
            uint handleCounter = 1;
            itemsToCreate.Add(new MonitoredItemCreateRequest() {
                ItemToMonitor = new ReadValueId() {
                    AttributeId = Attributes.Value,
                    NodeId = VariableIds.Server_ServerStatus_CurrentTime
                },
                MonitoringMode = MonitoringMode.Reporting,
                RequestedParameters = new MonitoringParameters() {
                    ClientHandle = ++handleCounter,
                    SamplingInterval = -1,
                    Filter = null,
                    DiscardOldest = true,
                    QueueSize = queueSize
                }
            });
            response = services.CreateMonitoredItems(requestHeader, id, TimestampsToReturn.Neither, itemsToCreate,
                out MonitoredItemCreateResultCollection itemCreateResults, out DiagnosticInfoCollection diagnosticInfos);
            ServerFixtureUtils.ValidateResponse(response);
            ServerFixtureUtils.ValidateDiagnosticInfos(diagnosticInfos, itemsToCreate);

            // modify subscription
            response = services.ModifySubscription(requestHeader, id,
                publishingInterval, lifetimeCount, maxKeepAliveCount,
                maxNotificationPerPublish, priority,
                out revisedPublishingInterval, out revisedLifetimeCount, out revisedMaxKeepAliveCount);
            Assert.AreEqual(publishingInterval, revisedPublishingInterval);
            Assert.AreEqual(lifetimeCount, revisedLifetimeCount);
            Assert.AreEqual(maxKeepAliveCount, revisedMaxKeepAliveCount);
            ServerFixtureUtils.ValidateResponse(response);

            // modify monitored item, just timestamps to return
            var itemsToModify = new MonitoredItemModifyRequestCollection();
            foreach (var itemCreated in itemCreateResults)
            {
                itemsToModify.Add(
                    new MonitoredItemModifyRequest() {
                        MonitoredItemId = itemCreated.MonitoredItemId
                    });
            };
            response = services.ModifyMonitoredItems(requestHeader, id, TimestampsToReturn.Both, itemsToModify,
                        out MonitoredItemModifyResultCollection modifyResults, out diagnosticInfos);
            ServerFixtureUtils.ValidateResponse(response);
            ServerFixtureUtils.ValidateDiagnosticInfos(diagnosticInfos, itemsToModify);

            // publish request
            var acknoledgements = new SubscriptionAcknowledgementCollection();
            response = services.Publish(requestHeader, acknoledgements,
                        out uint subscriptionId, out UInt32Collection availableSequenceNumbers,
                        out bool moreNotifications, out NotificationMessage notificationMessage,
                        out StatusCodeCollection statuses, out diagnosticInfos);
            ServerFixtureUtils.ValidateResponse(response);
            ServerFixtureUtils.ValidateDiagnosticInfos(diagnosticInfos, acknoledgements);
            Assert.AreEqual(id, subscriptionId);
            Assert.AreEqual(0, availableSequenceNumbers.Count);

            // enable publishing
            enabled = true;
            var subscriptions = new UInt32Collection() { id };
            response = services.SetPublishingMode(requestHeader, enabled, subscriptions,
                        out statuses, out diagnosticInfos);
            ServerFixtureUtils.ValidateResponse(response);
            ServerFixtureUtils.ValidateDiagnosticInfos(diagnosticInfos, subscriptions);

            // wait some time to fill queue
            int loopCounter = (int)queueSize;
            Thread.Sleep(loopCounter * 1000);

            acknoledgements = new SubscriptionAcknowledgementCollection();
            do
            {
                // get publish responses
                response = services.Publish(requestHeader, acknoledgements,
                    out subscriptionId, out availableSequenceNumbers,
                    out moreNotifications, out notificationMessage,
                    out statuses, out diagnosticInfos);
                ServerFixtureUtils.ValidateResponse(response);
                ServerFixtureUtils.ValidateDiagnosticInfos(diagnosticInfos, acknoledgements);
                Assert.AreEqual(id, subscriptionId);

                var dataChangeNotification = notificationMessage.NotificationData[0].Body as DataChangeNotification;
                TestContext.Out.WriteLine("Notification: {0} {1} {2}",
                                notificationMessage.SequenceNumber,
                                dataChangeNotification?.MonitoredItems[0].Value.ToString(),
                                notificationMessage.PublishTime);

                acknoledgements.Clear();
                acknoledgements.Add(new SubscriptionAcknowledgement() {
                    SubscriptionId = id,
                    SequenceNumber = notificationMessage.SequenceNumber
                });

            } while (acknoledgements.Count > 0 && --loopCounter > 0);

            // republish
            response = services.Republish(requestHeader, subscriptionId, notificationMessage.SequenceNumber, out notificationMessage);
            ServerFixtureUtils.ValidateResponse(response);

            // disable publishing
            enabled = false;
            response = services.SetPublishingMode(requestHeader, enabled, subscriptions,
                out statuses, out diagnosticInfos);
            ServerFixtureUtils.ValidateResponse(response);
            ServerFixtureUtils.ValidateDiagnosticInfos(diagnosticInfos, subscriptions);

            // delete subscription
            response = services.DeleteSubscriptions(requestHeader, subscriptions, out statuses, out diagnosticInfos);
            ServerFixtureUtils.ValidateResponse(response);
            ServerFixtureUtils.ValidateDiagnosticInfos(diagnosticInfos, subscriptions);
        }
#endregion
    }
}
