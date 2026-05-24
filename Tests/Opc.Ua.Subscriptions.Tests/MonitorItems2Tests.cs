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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.Subscriptions.Tests
{
    /// <summary>
    /// compliance tests for Monitor Items 2 conformance unit.
    /// Tests DataEncoding variations and batch modify with varying parameters.
    /// </summary>
    [NonParallelizable]
    [TestFixture]
    [Category("Conformance")]
    [Category("MonitorItems2")]
    public class MonitorItems2Tests : TestFixture
    {
        [SetUp]
        public async Task SetUp()
        {
            m_subscriptionId = await CreateSetupSubscriptionAsync(
                publishingInterval: 1000, requestedLifetimeCount: 100,
                requestedMaxKeepAliveCount: 10).ConfigureAwait(false);
        }

        [TearDown]
        public async Task TearDown()
        {
            if (m_subscriptionId > 0)
            {
                try
                {
                    await Session.DeleteSubscriptionsAsync(
                        null,
                        new uint[] { m_subscriptionId }.ToArrayOf(),
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch (ServiceResultException)
                {
                    // Subscription may already be deleted
                }
                m_subscriptionId = 0;
            }
        }

        [Test]
        public async Task CreateMonitoredItemDataEncodingVariationsAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            var testCases = new (QualifiedName Encoding, string Name, uint Attribute)[]
            {
                (default, "null", Attributes.Value),
                (new QualifiedName(string.Empty), "empty", Attributes.Value),
                (new QualifiedName("Default Binary", 0), "Default Binary", Attributes.Value),
                (new QualifiedName("Default XML", 0), "Default XML", Attributes.Value),
                (new QualifiedName("Default JSON", 0), "Default JSON", Attributes.Value),
                (new QualifiedName("Modbus", 0), "unknown Modbus", Attributes.Value),
                (new QualifiedName("Default Binary", 999), "invalid namespace", Attributes.Value),
                (new QualifiedName("Default Binary", 0), "BrowseName with encoding",
                    Attributes.BrowseName)
            };

            for (int i = 0; i < testCases.Length; i++)
            {
                var item = new MonitoredItemCreateRequest
                {
                    ItemToMonitor = new ReadValueId
                    {
                        NodeId = nodeId,
                        AttributeId = testCases[i].Attribute,
                        DataEncoding = testCases[i].Encoding
                    },
                    MonitoringMode = MonitoringMode.Reporting,
                    RequestedParameters = new MonitoringParameters
                    {
                        ClientHandle = (uint)(100 + i),
                        SamplingInterval = 1000,
                        Filter = default,
                        DiscardOldest = true,
                        QueueSize = 10
                    }
                };

                CreateMonitoredItemsResponse resp =
                    await CreateSingleItemAsync(item).ConfigureAwait(false);

                StatusCode sc = resp.Results[0].StatusCode;
                Assert.That(
                    StatusCode.IsGood(sc) ||
                    sc == StatusCodes.BadDataEncodingUnsupported ||
                    sc == StatusCodes.BadDataEncodingInvalid,
                    Is.True, $"Encoding '{testCases[i].Name}': unexpected {sc}");

                if (StatusCode.IsGood(sc))
                {
                    await Session.DeleteMonitoredItemsAsync(
                        null, m_subscriptionId,
                        new uint[] { resp.Results[0].MonitoredItemId }.ToArrayOf(),
                        CancellationToken.None).ConfigureAwait(false);
                }
            }
        }

        [Test]
        public async Task ModifyMultipleItemsVaryingParametersAsync()
        {
            int count = System.Math.Min(10, Constants.ScalarStaticNodes.Length);
            var items = new List<MonitoredItemCreateRequest>();
            for (int i = 0; i < count; i++)
            {
                ExpandedNodeId eni = Constants.ScalarStaticNodes[
                    i % Constants.ScalarStaticNodes.Length];
                items.Add(CreateItemRequest(ToNodeId(eni), (uint)(600 + i)));
            }

            CreateMonitoredItemsResponse createResp =
                await Session.CreateMonitoredItemsAsync(
                    null, m_subscriptionId, TimestampsToReturn.Both,
                    items.ToArray().ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);
            Assert.That(createResp.Results.Count, Is.EqualTo(count));

            var modItems = new List<MonitoredItemModifyRequest>();
            for (int i = 0; i < count; i++)
            {
                Assert.That(StatusCode.IsGood(createResp.Results[i].StatusCode), Is.True);
                uint queueSize = (uint)((i + 1) * 2);

                modItems.Add(new MonitoredItemModifyRequest
                {
                    MonitoredItemId = createResp.Results[i].MonitoredItemId,
                    RequestedParameters = new MonitoringParameters
                    {
                        ClientHandle = (uint)(600 + i),
                        SamplingInterval = i % 2 == 0 ? 500 : 5000,
                        QueueSize = queueSize,
                        DiscardOldest = i % 2 == 0
                    }
                });
            }

            ModifyMonitoredItemsResponse modResp =
                await Session.ModifyMonitoredItemsAsync(
                    null, m_subscriptionId, TimestampsToReturn.Both,
                    modItems.ToArray().ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

            Assert.That(modResp.Results.Count, Is.EqualTo(count));
            foreach (MonitoredItemModifyResult r in modResp.Results)
            {
                Assert.That(StatusCode.IsGood(r.StatusCode), Is.True);
                Assert.That(r.RevisedSamplingInterval, Is.GreaterThanOrEqualTo(0.0));
                Assert.That(r.RevisedQueueSize, Is.GreaterThan(0u));
            }
        }

        private static MonitoredItemCreateRequest CreateItemRequest(
            NodeId nodeId,
            uint clientHandle,
            double samplingInterval = 1000,
            uint queueSize = 10,
            MonitoringMode mode = MonitoringMode.Reporting,
            uint attributeId = Attributes.Value,
            bool discardOldest = true,
            ExtensionObject filter = default)
        {
            return new MonitoredItemCreateRequest
            {
                ItemToMonitor = new ReadValueId
                {
                    NodeId = nodeId,
                    AttributeId = attributeId
                },
                MonitoringMode = mode,
                RequestedParameters = new MonitoringParameters
                {
                    ClientHandle = clientHandle,
                    SamplingInterval = samplingInterval,
                    Filter = filter,
                    DiscardOldest = discardOldest,
                    QueueSize = queueSize
                }
            };
        }

        private async Task<CreateMonitoredItemsResponse> CreateSingleItemAsync(
            MonitoredItemCreateRequest item)
        {
            return await Session.CreateMonitoredItemsAsync(
                null, m_subscriptionId, TimestampsToReturn.Both,
                new MonitoredItemCreateRequest[] { item }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
        }

        private uint m_subscriptionId;
    }
}
