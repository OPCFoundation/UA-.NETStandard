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
using Quickstarts.ReferenceServer;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// CTT Base Info History Read Capabilities / 001: MaxHistoryContinuationPoints.
    /// </summary>
    /// <remarks>
    /// Step 1: a HistoryRead that consumes all continuation points returns exactly
    /// MaxHistoryContinuationPoints of them. Step 2: the same call with one more
    /// node returns Bad_NoContinuationPoints on one of the results.
    /// OPC 10000-4 7.9: "A Server shall process the operations until it uses the
    /// maximum number of continuation points in this response. Once that happens
    /// the Server shall return a Bad_NoContinuationPoints error for any remaining
    /// operations." and "A Server shall automatically free ContinuationPoints from
    /// prior requests from a Session if they are needed to process a new request
    /// from this Session."
    /// </remarks>
    [TestFixture]
    [Category("Server")]
    [Category("HistoryRead")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class HistoryContinuationPointLimitTests
    {
        private const string kReferenceNode = "Reference";
        private const string kTestDataNode = "TestData";
        private const string kMixedNodes = "Mixed";

        private ServerFixture<ReferenceServer> m_fixture;
        private ReferenceServer m_server;
        private RequestHeader m_requestHeader;
        private SecureChannelContext m_secureChannelContext;
        private int m_maxHistoryContinuationPoints;

        [OneTimeSetUp]
        public async Task OneTimeSetUpAsync()
        {
            m_fixture = new ServerFixture<ReferenceServer>
            {
                AllNodeManagers = true,
                OperationLimits = true,
                DurableSubscriptionsEnabled = false,
                UseSamplingGroupsInReferenceNodeManager = false
            };
            m_server = await m_fixture.StartAsync().ConfigureAwait(false);
            m_maxHistoryContinuationPoints =
                m_fixture.Config.ServerConfiguration.MaxHistoryContinuationPoints;
            Assert.Greater(m_maxHistoryContinuationPoints, 0);
        }

        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
            await m_fixture.StopAsync().ConfigureAwait(false);
        }

        [SetUp]
        public async Task SetUpAsync()
        {
            (m_requestHeader, m_secureChannelContext) = await m_server
                .CreateAndActivateSessionAsync(TestContext.CurrentContext.Test.Name)
                .ConfigureAwait(false);
        }

        [TearDown]
        public async Task TearDownAsync()
        {
            m_requestHeader.Timestamp = DateTime.UtcNow;
            await m_server.CloseSessionAsync(
                m_secureChannelContext,
                m_requestHeader,
                true,
                CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// Raw reads with NumValuesPerNode = 1 over a range with many values: each
        /// node needs a continuation point.
        /// </summary>
        [TestCase(kReferenceNode)]
        [TestCase(kTestDataNode)]
        [TestCase(kMixedNodes)]
        public async Task HistoryReadCapabilities001_RawReadBeyondLimitReturnsBadNoContinuationPointsAsync(
            string nodes)
        {
            var details = new ReadRawModifiedDetails
            {
                IsReadModified = false,
                ReturnBounds = false,
                StartTime = DateTime.UtcNow.AddDays(-1),
                EndTime = DateTime.UtcNow.AddDays(1),
                NumValuesPerNode = 1
            };

            await VerifyContinuationPointLimitAsync(new ExtensionObject(details), nodes)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Processed reads whose result exceeds one page (1000 values): each node
        /// needs a continuation point.
        /// </summary>
        [TestCase(kReferenceNode)]
        [TestCase(kTestDataNode)]
        public async Task HistoryReadCapabilities001_ProcessedReadBeyondLimitReturnsBadNoContinuationPointsAsync(
            string nodes)
        {
            DateTime end = DateTime.UtcNow;
            var details = new ReadProcessedDetails
            {
                StartTime = end.AddSeconds(-10000),
                EndTime = end,
                ProcessingInterval = 5000,
                AggregateType = [.. Enumerable.Repeat(
                    ObjectIds.AggregateFunction_Count,
                    m_maxHistoryContinuationPoints + 1)]
            };

            await VerifyContinuationPointLimitAsync(new ExtensionObject(details), nodes)
                .ConfigureAwait(false);
        }

        private async Task VerifyContinuationPointLimitAsync(ExtensionObject details, string nodes)
        {
            int max = m_maxHistoryContinuationPoints;
            if (details.Body is ReadProcessedDetails processed)
            {
                processed.AggregateType = [.. processed.AggregateType.Take(max)];
            }

            // Step 1: exactly MaxHistoryContinuationPoints continuation points.
            HistoryReadResponse step1 = await HistoryReadAsync(details, CreateNodes(nodes, max))
                .ConfigureAwait(false);
            Assert.AreEqual(max, step1.Results.Count);
            for (int ii = 0; ii < max; ii++)
            {
                Assert.IsTrue(StatusCode.IsGood(step1.Results[ii].StatusCode), $"Step 1 result {ii}: {step1.Results[ii].StatusCode}");
                Assert.IsNotEmpty(step1.Results[ii].ContinuationPoint ?? [], $"Step 1 result {ii} needs a ContinuationPoint.");
            }

            // Step 2: one node more. The points of step 1 belong to a prior request and
            // are freed; this request may use MaxHistoryContinuationPoints points and
            // the remaining operation gets Bad_NoContinuationPoints.
            if (details.Body is ReadProcessedDetails processed2)
            {
                processed2.AggregateType = [.. processed2.AggregateType, processed2.AggregateType[0]];
            }

            // The order in which the node managers process their operations is internal
            // to the server, so the refused operation can be any one of them.
            HistoryReadValueIdCollection step2Nodes = CreateNodes(nodes, max + 1);
            HistoryReadResponse step2 = await HistoryReadAsync(details, step2Nodes).ConfigureAwait(false);
            Assert.AreEqual(max + 1, step2.Results.Count);

            int[] withContinuation = [.. Enumerable.Range(0, step2.Results.Count).Where(ii =>
                StatusCode.IsGood(step2.Results[ii].StatusCode) &&
                step2.Results[ii].ContinuationPoint?.Length > 0)];
            int[] refusedIndexes = [.. Enumerable.Range(0, step2.Results.Count).Where(ii =>
                step2.Results[ii].StatusCode == StatusCodes.BadNoContinuationPoints)];
            Assert.AreEqual(max, withContinuation.Length, "Operations with a ContinuationPoint in step 2.");
            Assert.AreEqual(1, refusedIndexes.Length, "Operations with Bad_NoContinuationPoints in step 2.");

            HistoryReadResult refused = step2.Results[refusedIndexes[0]];
            Assert.IsTrue(refused.ContinuationPoint == null || refused.ContinuationPoint.Length == 0);
            Assert.AreEqual(0, (refused.HistoryData?.Body as HistoryData)?.DataValues?.Count ?? 0);

            // The continuation points of step 2 are valid, the oldest one too.
            int first = withContinuation[0];
            var continuation = new HistoryReadValueIdCollection
            {
                new HistoryReadValueId
                {
                    NodeId = step2Nodes[first].NodeId,
                    ContinuationPoint = step2.Results[first].ContinuationPoint
                }
            };
            if (details.Body is ReadProcessedDetails processed3)
            {
                processed3.AggregateType = [processed3.AggregateType[0]];
            }

            HistoryReadResponse resumed = await HistoryReadAsync(details, continuation).ConfigureAwait(false);
            Assert.IsTrue(
                StatusCode.IsGood(resumed.Results[0].StatusCode),
                $"The first ContinuationPoint of step 2 must still be valid: {resumed.Results[0].StatusCode}");
        }

        private HistoryReadValueIdCollection CreateNodes(string nodes, int count)
        {
            NamespaceTable namespaceUris = m_server.CurrentInstance.NamespaceUris;
            var reference = new NodeId(
                "Scalar_Static_Int32",
                (ushort)namespaceUris.GetIndex(Quickstarts.ReferenceServer.Namespaces.ReferenceServer));
            var testData = new NodeId(
                TestData.Variables.Data_Dynamic_Scalar_Int32Value,
                (ushort)namespaceUris.GetIndex(TestData.Namespaces.TestData));

            var collection = new HistoryReadValueIdCollection();
            for (int ii = 0; ii < count; ii++)
            {
                NodeId nodeId = nodes switch
                {
                    kReferenceNode => reference,
                    kTestDataNode => testData,
                    _ => ii % 2 == 0 ? reference : testData
                };
                collection.Add(new HistoryReadValueId { NodeId = nodeId });
            }

            return collection;
        }

        private Task<HistoryReadResponse> HistoryReadAsync(
            ExtensionObject details,
            HistoryReadValueIdCollection nodesToRead)
        {
            m_requestHeader.Timestamp = DateTime.UtcNow;
            return m_server.HistoryReadAsync(
                m_secureChannelContext,
                m_requestHeader,
                details,
                TimestampsToReturn.Source,
                false,
                nodesToRead,
                CancellationToken.None);
        }
    }
}
