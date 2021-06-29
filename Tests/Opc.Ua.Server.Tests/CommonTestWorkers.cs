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
        /// <param name="browser">The service interface.</param>
        /// <param name="operationLimits">The operation limits.</param>
        public static ReferenceDescriptionCollection BrowseFullAddressSpaceWorker(
            IServerTestServices browser,
            RequestHeader requestHeader,
            OperationLimits operationLimits = null)
        {
            operationLimits = operationLimits ?? new OperationLimits();
            requestHeader.Timestamp = DateTime.UtcNow;

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

            // Browse
            ResponseHeader response = null;
            uint requestedMaxReferencesPerNode = 0;
            bool verifyMaxNodesPerBrowse = operationLimits.MaxNodesPerBrowse > 0;
            var referenceDescriptions = new ReferenceDescriptionCollection();
            while (browseDescriptionCollection.Any())
            {
                BrowseResultCollection allResults = new BrowseResultCollection();

                if (verifyMaxNodesPerBrowse &&
                    browseDescriptionCollection.Count > operationLimits.MaxNodesPerBrowse)
                {
                    verifyMaxNodesPerBrowse = false;
                    // Test if server responds with BadTooManyOperations
                    var sre = Assert.Throws<ServiceResultException>(() =>
                        _ = browser.Browse(requestHeader, null,
                            0, browseDescriptionCollection,
                            out var results, out var infos));
                    Assert.AreEqual(StatusCodes.BadTooManyOperations, sre.StatusCode);

                    // Test if server responds with BadTooManyOperations
                    var tempBrowsePath = browseDescriptionCollection.Take((int)operationLimits.MaxNodesPerBrowse + 1).ToArray();
                    sre = Assert.Throws<ServiceResultException>(() =>
                        _ = browser.Browse(requestHeader, null,
                            0, tempBrowsePath,
                            out var results, out var infos));
                    Assert.AreEqual(StatusCodes.BadTooManyOperations, sre.StatusCode);
                }

                var browseCollection = (operationLimits.MaxNodesPerBrowse == 0) ?
                    browseDescriptionCollection :
                    browseDescriptionCollection.Take((int)operationLimits.MaxNodesPerBrowse).ToArray();

                requestHeader.Timestamp = DateTime.UtcNow;
                response = browser.Browse(requestHeader, null,
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
                    response = browser.BrowseNext(requestHeader, false, continuationPoints,
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
            IServerTestServices browser,
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
                        _ = browser.TranslateBrowsePathsToNodeIds(requestHeader, browsePaths, out var results, out var infos));
                    Assert.AreEqual(StatusCodes.BadTooManyOperations, sre.StatusCode);
                }
                var browsePathSnippet = (operationLimits.MaxNodesPerTranslateBrowsePathsToNodeIds > 0) ?
                    browsePaths.Take((int)operationLimits.MaxNodesPerTranslateBrowsePathsToNodeIds).ToArray() :
                    browsePaths;
                ResponseHeader response = browser.TranslateBrowsePathsToNodeIds(requestHeader, browsePathSnippet, out var browsePathResults, out var diagnosticInfos);
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
        #endregion
    }
}
