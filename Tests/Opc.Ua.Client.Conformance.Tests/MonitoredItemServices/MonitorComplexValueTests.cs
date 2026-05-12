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

using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Client.Conformance.Tests
{
    /// <summary>
    /// compliance tests for Monitor Complex Value conformance unit.
    /// Tests monitoring of complex/structured data types.
    /// </summary>
    [NonParallelizable]
    [TestFixture]
    [Category("Conformance")]
    [Category("MonitorComplexValue")]
    public class MonitorComplexValueTests : TestFixture
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
        [Property("ConformanceUnit", "Monitor Complex Value")]
        [Property("Tag", "001")]
        public async Task MonitorComplexDataTypeValueAsync()
        {
            NodeId nodeId = VariableIds.Server_ServerStatus;

            CreateMonitoredItemsResponse createResp = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 1, samplingInterval: 0)).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(createResp.ResponseHeader.ServiceResult), Is.True);
            Assert.That(createResp.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(createResp.Results[0].StatusCode), Is.True);

            await Task.Delay(500).ConfigureAwait(false);

            PublishResponse pubResp = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(pubResp.ResponseHeader.ServiceResult), Is.True);
            Assert.That(pubResp.NotificationMessage.NotificationData.Count, Is.GreaterThan(0));

            var dcn = ExtensionObject.ToEncodeable(
                pubResp.NotificationMessage.NotificationData[0]) as DataChangeNotification;
            Assert.That(dcn, Is.Not.Null);
            Assert.That(dcn.MonitoredItems.Count, Is.GreaterThan(0));
            Assert.That(dcn.MonitoredItems[0].Value, Is.Not.Null,
                "ServerStatus should return a structured value");
        }

        [Test]
        [Property("ConformanceUnit", "Monitor Complex Value")]
        [Property("Tag", "002")]
        public async Task MonitorNestedComplexDataTypeValueAsync()
        {
            NodeId nodeId = VariableIds.Server_ServerStatus_BuildInfo;

            CreateMonitoredItemsResponse createResp = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 1, samplingInterval: 0)).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(createResp.ResponseHeader.ServiceResult), Is.True);
            Assert.That(createResp.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(createResp.Results[0].StatusCode), Is.True);

            await Task.Delay(500).ConfigureAwait(false);

            PublishResponse pubResp = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(pubResp.ResponseHeader.ServiceResult), Is.True);
            Assert.That(pubResp.NotificationMessage.NotificationData.Count, Is.GreaterThan(0));

            var dcn = ExtensionObject.ToEncodeable(
                pubResp.NotificationMessage.NotificationData[0]) as DataChangeNotification;
            Assert.That(dcn, Is.Not.Null);
            Assert.That(dcn.MonitoredItems.Count, Is.GreaterThan(0));
            Assert.That(dcn.MonitoredItems[0].Value, Is.Not.Null,
                "BuildInfo should return a nested structured value");
        }

        [Test]
        [Property("ConformanceUnit", "Monitor Complex Value")]
        [Property("Tag", "003")]
        public async Task MonitorComplexDataTypeDataEncodingVariationsAsync()
        {
            NodeId nodeId = VariableIds.Server_ServerStatus;

            var encodings = new (QualifiedName Encoding, string Name)[]
            {
                (default, "null"),
                (new QualifiedName(string.Empty), "empty"),
                (new QualifiedName("Default Binary", 0), "Default Binary"),
                (new QualifiedName("Default XML", 0), "Default XML"),
                (new QualifiedName("Default JSON", 0), "Default JSON"),
                (new QualifiedName("Modbus", 0), "unknown Modbus")
            };

            for (int i = 0; i < encodings.Length; i++)
            {
                var item = new MonitoredItemCreateRequest
                {
                    ItemToMonitor = new ReadValueId
                    {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        DataEncoding = encodings[i].Encoding
                    },
                    MonitoringMode = MonitoringMode.Reporting,
                    RequestedParameters = new MonitoringParameters
                    {
                        ClientHandle = (uint)(50 + i),
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
                    Is.True, $"Encoding '{encodings[i].Name}': unexpected {sc}");

                if (StatusCode.IsGood(sc))
                {
                    await Session.DeleteMonitoredItemsAsync(
                        null, m_subscriptionId,
                        new uint[] { resp.Results[0].MonitoredItemId }.ToArrayOf(),
                        CancellationToken.None).ConfigureAwait(false);
                }
            }
        }

        private static MonitoredItemCreateRequest CreateItemRequest(
            NodeId nodeId,
            uint clientHandle,
            double samplingInterval = 1000,
            uint queueSize = 10)
        {
            return new MonitoredItemCreateRequest
            {
                ItemToMonitor = new ReadValueId
                {
                    NodeId = nodeId,
                    AttributeId = Attributes.Value
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
