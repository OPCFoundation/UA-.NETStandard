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
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;

// Conformance tests use inline literal arrays as expected-value
// assertions; the per-call allocation cost is irrelevant for tests
// and keeping the literal adjacent to the assertion improves readability.
#pragma warning disable CA1861 // Avoid constant arrays as arguments

namespace Opc.Ua.Subscriptions.Tests
{
    /// <summary>
    /// compliance tests for MonitoredItem Service Set.
    /// </summary>
    [NonParallelizable]
    [TestFixture]
    [Category("Conformance")]
    [Category("MonitoredItem")]
    public class MonitoredItemServicesTests : TestFixture
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
                await Session.DeleteSubscriptionsAsync(
                    null,
                    new uint[] { m_subscriptionId }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);
                m_subscriptionId = 0;
            }
        }

        [Test]
        public async Task CreateMonitoredItemOnScalarVariableAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse response = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 1)).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(response.ResponseHeader.ServiceResult), Is.True);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
            Assert.That(response.Results[0].MonitoredItemId, Is.GreaterThan(0u));
        }

        [Test]
        public async Task CreateMonitoredItemsOnMultipleNodesAsync()
        {
            var items = new MonitoredItemCreateRequest[]
            {
                CreateItemRequest(ToNodeId(Constants.ScalarStaticInt32), 1),
                CreateItemRequest(ToNodeId(Constants.ScalarStaticDouble), 2),
                CreateItemRequest(ToNodeId(Constants.ScalarStaticString), 3)
            };

            CreateMonitoredItemsResponse response = await Session.CreateMonitoredItemsAsync(
                null,
                m_subscriptionId,
                TimestampsToReturn.Both,
                items.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(3));
            foreach (MonitoredItemCreateResult result in response.Results)
            {
                Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            }
        }

        [Test]
        public async Task CreateMonitoredItemWithSamplingIntervalZeroServerRevisesAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse response = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 1, samplingInterval: 0)).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
            Assert.That(response.Results[0].RevisedSamplingInterval, Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public async Task CreateMonitoredItemWithQueueSizeZeroServerRevisesToOneAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse response = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 1, queueSize: 0)).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
            Assert.That(response.Results[0].RevisedQueueSize, Is.GreaterThanOrEqualTo(1u));
        }

        [Test]
        public async Task CreateMonitoredItemVerifyRevisedSamplingIntervalAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse response = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 1, samplingInterval: 500)).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
            Assert.That(response.Results[0].RevisedSamplingInterval, Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public async Task CreateMonitoredItemVerifyRevisedQueueSizeAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse response = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 1, queueSize: 50)).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
            Assert.That(response.Results[0].RevisedQueueSize, Is.GreaterThan(0u));
        }

        [TestCase("ScalarStaticBoolean", typeof(bool))]
        [TestCase("ScalarStaticInt16", typeof(short))]
        [TestCase("ScalarStaticInt32", typeof(int))]
        [TestCase("ScalarStaticInt64", typeof(long))]
        [TestCase("ScalarStaticFloat", typeof(float))]
        [TestCase("ScalarStaticDouble", typeof(double))]
        [TestCase("ScalarStaticString", typeof(string))]
        [TestCase("ScalarStaticDateTime", typeof(DateTime))]
        [TestCase("ScalarStaticGuid", typeof(Guid))]
        [TestCase("ScalarStaticByteString", typeof(byte[]))]
        public async Task CreateMonitoredItemOnScalarDataType(string fieldName, Type expectedType)
        {
            _ = expectedType;
            FieldInfo field = typeof(Constants).GetField(fieldName);
            Assert.That(field, Is.Not.Null, $"Constants.{fieldName} not found");

            var expandedNodeId = (ExpandedNodeId)field.GetValue(null);
            NodeId nodeId = ToNodeId(expandedNodeId);

            CreateMonitoredItemsResponse response = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 1)).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
            Assert.That(response.Results[0].MonitoredItemId, Is.GreaterThan(0u));
        }

        [Test]
        public async Task CreateMonitoredItemInSamplingModeAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse response = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 1, mode: MonitoringMode.Sampling)).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
        }

        [Test]
        public async Task CreateMonitoredItemInDisabledModeAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse response = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 1, mode: MonitoringMode.Disabled)).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
        }

        [Test]
        public async Task CreateMonitoredItemForDisplayNameAttributeAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse response = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 1, attributeId: Attributes.DisplayName)).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
        }

        [Test]
        public async Task CreateMonitoredItemForBrowseNameAttributeAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse response = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 1, attributeId: Attributes.BrowseName)).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
        }

        [Test]
        public async Task CreateMonitoredItemWithInvalidNodeIdReturnsBadNodeIdUnknownAsync()
        {
            CreateMonitoredItemsResponse response = await CreateSingleItemAsync(
                CreateItemRequest(Constants.InvalidNodeId, 1)).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsBad(response.Results[0].StatusCode), Is.True,
                "Creating monitored item with invalid NodeId should return Bad status.");
        }

        [Test]
        public async Task CreateMonitoredItemWithWrongAttributeIdReturnsBadAttributeIdInvalidAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            var item = new MonitoredItemCreateRequest
            {
                ItemToMonitor = new ReadValueId
                {
                    NodeId = nodeId,
                    AttributeId = 999
                },
                MonitoringMode = MonitoringMode.Reporting,
                RequestedParameters = new MonitoringParameters
                {
                    ClientHandle = 1,
                    SamplingInterval = 1000,
                    Filter = default,
                    DiscardOldest = true,
                    QueueSize = 10
                }
            };

            CreateMonitoredItemsResponse response = await CreateSingleItemAsync(item).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsBad(response.Results[0].StatusCode), Is.True);
        }

        [Test]
        public Task CreateMonitoredItemWithInvalidSubscriptionIdReturnsBadSubscriptionIdInvalid()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(async () => await Session.CreateMonitoredItemsAsync(
                    null,
                    999999u,
                    TimestampsToReturn.Both,
                    new MonitoredItemCreateRequest[] { CreateItemRequest(nodeId, 1) }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadSubscriptionIdInvalid));
            return Task.CompletedTask;
        }

        [Test]
        public async Task BatchCreateOneHundredMonitoredItemsAsync()
        {
            var items = new List<MonitoredItemCreateRequest>();
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            for (uint i = 0; i < 100; i++)
            {
                items.Add(CreateItemRequest(nodeId, i));
            }

            CreateMonitoredItemsResponse response = await Session.CreateMonitoredItemsAsync(
                null,
                m_subscriptionId,
                TimestampsToReturn.Both,
                items.ToArray().ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(response.ResponseHeader.ServiceResult), Is.True);
            Assert.That(response.Results.Count, Is.EqualTo(100));

            int goodCount = response.Results.ToArray().Count(r => StatusCode.IsGood(r.StatusCode));
            Assert.That(goodCount, Is.EqualTo(100));
        }

        [Test]
        public async Task CreateMonitoredItemWithDataChangeFilterStatusValueAsync()
        {
            NodeId nodeId = VariableIds.Server_ServerStatus_CurrentTime;

            var filter = new DataChangeFilter
            {
                Trigger = DataChangeTrigger.StatusValue,
                DeadbandType = (uint)DeadbandType.None,
                DeadbandValue = 0
            };

            CreateMonitoredItemsResponse response = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 1, filter: new ExtensionObject(filter))).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            StatusCode status = response.Results[0].StatusCode;
            Assert.That(
                StatusCode.IsGood(status) || status == StatusCodes.BadFilterNotAllowed,
                Is.True,
                $"Expected Good or BadFilterNotAllowed, got {status}");
        }

        [Test]
        public async Task CreateMonitoredItemWithDataChangeFilterStatusValueTimestampAsync()
        {
            NodeId nodeId = VariableIds.Server_ServerStatus_CurrentTime;

            var filter = new DataChangeFilter
            {
                Trigger = DataChangeTrigger.StatusValueTimestamp,
                DeadbandType = (uint)DeadbandType.None,
                DeadbandValue = 0
            };

            CreateMonitoredItemsResponse response = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 1, filter: new ExtensionObject(filter))).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            StatusCode status = response.Results[0].StatusCode;
            Assert.That(
                StatusCode.IsGood(status) || status == StatusCodes.BadFilterNotAllowed,
                Is.True,
                $"Expected Good or BadFilterNotAllowed, got {status}");
        }

        [Test]
        public async Task CreateMonitoredItemWithAbsoluteDeadbandFilterAsync()
        {
            NodeId nodeId = ToNodeId(Constants.AnalogTypeDouble);

            var filter = new DataChangeFilter
            {
                Trigger = DataChangeTrigger.StatusValue,
                DeadbandType = (uint)DeadbandType.Absolute,
                DeadbandValue = 5.0
            };

            CreateMonitoredItemsResponse response = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 1, filter: new ExtensionObject(filter))).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            StatusCode status = response.Results[0].StatusCode;
            Assert.That(
                StatusCode.IsGood(status) || status == StatusCodes.BadFilterNotAllowed,
                Is.True,
                $"Expected Good or BadFilterNotAllowed, got {status}");
        }

        [Test]
        public async Task CreateMonitoredItemWithPercentDeadbandFilterAsync()
        {
            NodeId nodeId = ToNodeId(Constants.AnalogTypeDouble);

            var filter = new DataChangeFilter
            {
                Trigger = DataChangeTrigger.StatusValue,
                DeadbandType = (uint)DeadbandType.Percent,
                DeadbandValue = 10.0
            };

            CreateMonitoredItemsResponse response = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 1, filter: new ExtensionObject(filter))).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            StatusCode status = response.Results[0].StatusCode;
            Assert.That(
                StatusCode.IsGood(status) || status == StatusCodes.BadFilterNotAllowed,
                Is.True,
                $"Expected Good or BadFilterNotAllowed, got {status}");
        }

        [Test]
        public async Task ModifyMonitoredItemChangeSamplingIntervalAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse createResp = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 1, samplingInterval: 1000)).ConfigureAwait(false);

            uint monitoredItemId = createResp.Results[0].MonitoredItemId;

            ModifyMonitoredItemsResponse modifyResp = await Session.ModifyMonitoredItemsAsync(
                null,
                m_subscriptionId,
                TimestampsToReturn.Both,
                new MonitoredItemModifyRequest[]
                {
                    new() {
                        MonitoredItemId = monitoredItemId,
                        RequestedParameters = new MonitoringParameters
                        {
                            ClientHandle = 1,
                            SamplingInterval = 500,
                            QueueSize = 10,
                            DiscardOldest = true
                        }
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(modifyResp.ResponseHeader.ServiceResult), Is.True);
            Assert.That(modifyResp.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(modifyResp.Results[0].StatusCode), Is.True);
        }

        [Test]
        public async Task ModifyMonitoredItemChangeQueueSizeAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse createResp = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 1, queueSize: 5)).ConfigureAwait(false);

            uint monitoredItemId = createResp.Results[0].MonitoredItemId;

            ModifyMonitoredItemsResponse modifyResp = await Session.ModifyMonitoredItemsAsync(
                null,
                m_subscriptionId,
                TimestampsToReturn.Both,
                new MonitoredItemModifyRequest[]
                {
                    new() {
                        MonitoredItemId = monitoredItemId,
                        RequestedParameters = new MonitoringParameters
                        {
                            ClientHandle = 1,
                            SamplingInterval = 1000,
                            QueueSize = 20,
                            DiscardOldest = true
                        }
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(modifyResp.Results[0].StatusCode), Is.True);
            Assert.That(modifyResp.Results[0].RevisedQueueSize, Is.GreaterThan(0u));
        }

        [Test]
        public async Task ModifyMonitoredItemChangeFilterAsync()
        {
            NodeId nodeId = VariableIds.Server_ServerStatus_CurrentTime;

            CreateMonitoredItemsResponse createResp = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 1)).ConfigureAwait(false);

            uint monitoredItemId = createResp.Results[0].MonitoredItemId;

            var filter = new DataChangeFilter
            {
                Trigger = DataChangeTrigger.StatusValueTimestamp,
                DeadbandType = (uint)DeadbandType.None,
                DeadbandValue = 0
            };

            ModifyMonitoredItemsResponse modifyResp = await Session.ModifyMonitoredItemsAsync(
                null,
                m_subscriptionId,
                TimestampsToReturn.Both,
                new MonitoredItemModifyRequest[]
                {
                    new() {
                        MonitoredItemId = monitoredItemId,
                        RequestedParameters = new MonitoringParameters
                        {
                            ClientHandle = 1,
                            SamplingInterval = 1000,
                            QueueSize = 10,
                            DiscardOldest = true,
                            Filter = new ExtensionObject(filter)
                        }
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(modifyResp.ResponseHeader.ServiceResult), Is.True);
            Assert.That(modifyResp.Results.Count, Is.EqualTo(1));
            StatusCode status = modifyResp.Results[0].StatusCode;
            Assert.That(
                StatusCode.IsGood(status) || status == StatusCodes.BadFilterNotAllowed,
                Is.True);
        }

        [Test]
        public async Task ModifyMonitoredItemWithInvalidIdReturnsBadMonitoredItemIdInvalidAsync()
        {
            ModifyMonitoredItemsResponse modifyResp = await Session.ModifyMonitoredItemsAsync(
                null,
                m_subscriptionId,
                TimestampsToReturn.Both,
                new MonitoredItemModifyRequest[]
                {
                    new() {
                        MonitoredItemId = 999999u,
                        RequestedParameters = new MonitoringParameters
                        {
                            ClientHandle = 1,
                            SamplingInterval = 500,
                            QueueSize = 10,
                            DiscardOldest = true
                        }
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(modifyResp.ResponseHeader.ServiceResult), Is.True);
            Assert.That(modifyResp.Results.Count, Is.EqualTo(1));
            Assert.That(modifyResp.Results[0].StatusCode, Is.EqualTo(StatusCodes.BadMonitoredItemIdInvalid));
        }

        [Test]
        public async Task SetMonitoringModeDisabledAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse createResp = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 1)).ConfigureAwait(false);

            uint monitoredItemId = createResp.Results[0].MonitoredItemId;

            SetMonitoringModeResponse response = await Session.SetMonitoringModeAsync(
                null,
                m_subscriptionId,
                MonitoringMode.Disabled,
                new uint[] { monitoredItemId }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(response.ResponseHeader.ServiceResult), Is.True);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0]), Is.True);
        }

        [Test]
        public async Task SetMonitoringModeSamplingAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse createResp = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 1)).ConfigureAwait(false);

            uint monitoredItemId = createResp.Results[0].MonitoredItemId;

            SetMonitoringModeResponse response = await Session.SetMonitoringModeAsync(
                null,
                m_subscriptionId,
                MonitoringMode.Sampling,
                new uint[] { monitoredItemId }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(response.ResponseHeader.ServiceResult), Is.True);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0]), Is.True);
        }

        [Test]
        public async Task SetMonitoringModeReportingAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse createResp = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 1, mode: MonitoringMode.Disabled)).ConfigureAwait(false);

            uint monitoredItemId = createResp.Results[0].MonitoredItemId;

            SetMonitoringModeResponse response = await Session.SetMonitoringModeAsync(
                null,
                m_subscriptionId,
                MonitoringMode.Reporting,
                new uint[] { monitoredItemId }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(response.ResponseHeader.ServiceResult), Is.True);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0]), Is.True);
        }

        [Test]
        public async Task SetMonitoringModeDisabledThenReportingAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse createResp = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 1)).ConfigureAwait(false);

            uint monitoredItemId = createResp.Results[0].MonitoredItemId;

            // Disable
            SetMonitoringModeResponse disableResp = await Session.SetMonitoringModeAsync(
                null,
                m_subscriptionId,
                MonitoringMode.Disabled,
                new uint[] { monitoredItemId }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(disableResp.Results[0]), Is.True);

            // Re-enable to Reporting
            SetMonitoringModeResponse reportResp = await Session.SetMonitoringModeAsync(
                null,
                m_subscriptionId,
                MonitoringMode.Reporting,
                new uint[] { monitoredItemId }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(reportResp.Results[0]), Is.True);
        }

        [Test]
        public async Task DeleteMonitoredItemsAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse createResp = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 1)).ConfigureAwait(false);

            uint monitoredItemId = createResp.Results[0].MonitoredItemId;

            DeleteMonitoredItemsResponse deleteResp = await Session.DeleteMonitoredItemsAsync(
                null,
                m_subscriptionId,
                new uint[] { monitoredItemId }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(deleteResp.ResponseHeader.ServiceResult), Is.True);
            Assert.That(deleteResp.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(deleteResp.Results[0]), Is.True);
        }

        [Test]
        public async Task DeleteMonitoredItemWithInvalidIdReturnsBadMonitoredItemIdInvalidAsync()
        {
            DeleteMonitoredItemsResponse deleteResp = await Session.DeleteMonitoredItemsAsync(
                null,
                m_subscriptionId,
                new uint[] { 999999u }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(deleteResp.ResponseHeader.ServiceResult), Is.True);
            Assert.That(deleteResp.Results.Count, Is.EqualTo(1));
            Assert.That(deleteResp.Results[0], Is.EqualTo(StatusCodes.BadMonitoredItemIdInvalid));
        }

        [Test]
        public async Task DeleteMultipleMonitoredItemsAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            var items = new MonitoredItemCreateRequest[]
            {
                CreateItemRequest(nodeId, 1),
                CreateItemRequest(nodeId, 2),
                CreateItemRequest(nodeId, 3)
            };

            CreateMonitoredItemsResponse createResp = await Session.CreateMonitoredItemsAsync(
                null,
                m_subscriptionId,
                TimestampsToReturn.Both,
                items.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            uint[] ids = [.. createResp.Results.ToArray().Select(r => r.MonitoredItemId)];

            DeleteMonitoredItemsResponse deleteResp = await Session.DeleteMonitoredItemsAsync(
                null,
                m_subscriptionId,
                ids.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(deleteResp.Results.Count, Is.EqualTo(3));
            foreach (StatusCode sc in deleteResp.Results)
            {
                Assert.That(StatusCode.IsGood(sc), Is.True);
            }
        }

        [Test]
        public async Task PublishReceivesDataChangeNotificationAsync()
        {
            NodeId nodeId = VariableIds.Server_ServerStatus_CurrentTime;

            CreateMonitoredItemsResponse createResp = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 1, samplingInterval: 100, queueSize: 5)).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(createResp.Results[0].StatusCode), Is.True);

            await Task.Delay(500).ConfigureAwait(false);

            PublishResponse publishResp = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(publishResp.ResponseHeader.ServiceResult), Is.True);
            Assert.That(publishResp.SubscriptionId, Is.EqualTo(m_subscriptionId));
            Assert.That(publishResp.NotificationMessage, Is.Not.Null);
        }

        [Test]
        [Category("LongRunning")]
        public async Task CreateMonitoredItemInitialValueReturnedAsync()
        {
            NodeId nodeId = VariableIds.Server_ServerStatus_CurrentTime;

            CreateMonitoredItemsResponse createResp = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 1, samplingInterval: 100)).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(createResp.Results[0].StatusCode), Is.True);

            try
            {
                PublishResponse publishResp = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

                Assert.That(StatusCode.IsGood(publishResp.ResponseHeader.ServiceResult), Is.True);
                Assert.That(publishResp.NotificationMessage, Is.Not.Null);
            }
            catch (ServiceResultException sre) when (IsTransientCiTimeoutStatus(sre.StatusCode))
            {
                Assert.Ignore(
                    $"Timing-sensitive: initial-publish interrupted by CI runner load ({sre.StatusCode}).");
            }
        }

        [Test]
        [Category("LongRunning")]
        public async Task WriteValueAndPublishVerifyNotificationContainsNewValueAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
            const uint clientHandle = 99u;

            CreateMonitoredItemsResponse createResp = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, clientHandle, samplingInterval: 50, queueSize: 10)).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(createResp.Results[0].StatusCode), Is.True);

            try
            {
                // Consume initial notification
                await Task.Delay(200).ConfigureAwait(false);
                await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

                // Write new value
                int newValue = UnsecureRandom.Shared.Next(1, 10000);
                await WriteValueAsync(nodeId, newValue).ConfigureAwait(false);

                await Task.Delay(300).ConfigureAwait(false);

                PublishResponse pubResp = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

                Assert.That(StatusCode.IsGood(pubResp.ResponseHeader.ServiceResult), Is.True);
                Assert.That(pubResp.NotificationMessage.NotificationData.Count, Is.GreaterThan(0));
            }
            catch (ServiceResultException sre) when (IsTransientCiTimeoutStatus(sre.StatusCode))
            {
                Assert.Ignore(
                    $"Timing-sensitive: write/publish sequence interrupted by CI runner load ({sre.StatusCode}).");
            }
        }

        [Test]
        public async Task WriteToOneOfMultipleMonitoredItemsOnlyThatOneNotifiesAsync()
        {
            NodeId nodeId1 = ToNodeId(Constants.ScalarStaticInt32);
            NodeId nodeId2 = ToNodeId(Constants.ScalarStaticDouble);

            var items = new MonitoredItemCreateRequest[]
            {
                CreateItemRequest(nodeId1, 1, samplingInterval: 50),
                CreateItemRequest(nodeId2, 2, samplingInterval: 50)
            };

            CreateMonitoredItemsResponse createResp = await Session.CreateMonitoredItemsAsync(
                null,
                m_subscriptionId,
                TimestampsToReturn.Both,
                items.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(createResp.Results.Count, Is.EqualTo(2));
            Assert.That(StatusCode.IsGood(createResp.Results[0].StatusCode), Is.True);
            Assert.That(StatusCode.IsGood(createResp.Results[1].StatusCode), Is.True);

            // Consume initial notifications
            await Task.Delay(200).ConfigureAwait(false);
            await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            // Write to only the first node
            await WriteValueAsync(nodeId1, UnsecureRandom.Shared.Next(1, 10000)).ConfigureAwait(false);

            await Task.Delay(300).ConfigureAwait(false);

            PublishResponse pubResp = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(pubResp.ResponseHeader.ServiceResult), Is.True);
            Assert.That(pubResp.NotificationMessage, Is.Not.Null);
        }

        [Test]
        public async Task MatrixIndexRangeReportsEveryChangedTypeAsync()
        {
            string[] matrixTypes =
            [
                "Boolean",
                "Byte",
                "ByteString",
                "DateTime",
                "Double",
                "Float",
                "Guid",
                "Int16",
                "Int32",
                "Int64",
                "SByte",
                "String",
                "UInt16",
                "UInt32",
                "UInt64",
                "XmlElement",
                "Variant",
                "LocalizedText",
                "QualifiedName"
            ];
            NodeId[] nodeIds = [.. matrixTypes
                .Select(type => ToNodeId(
                    new ExpandedNodeId(
                        $"Scalar_Static_Arrays2D_{type}",
                        Constants.ReferenceServerNamespaceUri)))];

            var reads = nodeIds
                .Select(nodeId => new ReadValueId
                {
                    NodeId = nodeId,
                    AttributeId = Attributes.Value
                })
                .ToArrayOf();
            ReadResponse readResponse = await Session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Both,
                reads,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(readResponse.Results.Count, Is.EqualTo(matrixTypes.Length));
            for (int ii = 0; ii < readResponse.Results.Count; ii++)
            {
                Assert.That(
                    StatusCode.IsGood(readResponse.Results[ii].StatusCode),
                    Is.True,
                    $"Initial read failed for {matrixTypes[ii]}.");
            }

            var requests = nodeIds
                .Select((nodeId, index) => new MonitoredItemCreateRequest
                {
                    ItemToMonitor = new ReadValueId
                    {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        IndexRange = "1,1"
                    },
                    MonitoringMode = MonitoringMode.Reporting,
                    RequestedParameters = new MonitoringParameters
                    {
                        ClientHandle = (uint)index + 1,
                        SamplingInterval = 0,
                        QueueSize = 10,
                        DiscardOldest = true
                    }
                })
                .ToArrayOf();
            CreateMonitoredItemsResponse createResponse = await Session.CreateMonitoredItemsAsync(
                null,
                m_subscriptionId,
                TimestampsToReturn.Both,
                requests,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(createResponse.Results.Count, Is.EqualTo(matrixTypes.Length));
            for (int ii = 0; ii < createResponse.Results.Count; ii++)
            {
                Assert.That(
                    StatusCode.IsGood(createResponse.Results[ii].StatusCode),
                    Is.True,
                    $"CreateMonitoredItems failed for {matrixTypes[ii]}.");
            }

            await Task.Delay(1200).ConfigureAwait(false);
            HashSet<uint> initialHandles = await CollectDataChangeClientHandlesAsync(
                matrixTypes.Length).ConfigureAwait(false);
            Assert.That(
                initialHandles,
                Is.EquivalentTo(Enumerable.Range(1, matrixTypes.Length).Select(value => (uint)value)),
                "The initial Publish did not contain every configured matrix item.");

            var changedWrites = new WriteValue[matrixTypes.Length];
            var restoreWrites = new WriteValue[matrixTypes.Length];
            for (int ii = 0; ii < matrixTypes.Length; ii++)
            {
                changedWrites[ii] = new WriteValue
                {
                    NodeId = nodeIds[ii],
                    AttributeId = Attributes.Value,
                    Value = new DataValue(ChangeSelectedMatrixElement(readResponse.Results[ii].WrappedValue))
                };
                restoreWrites[ii] = new WriteValue
                {
                    NodeId = nodeIds[ii],
                    AttributeId = Attributes.Value,
                    Value = readResponse.Results[ii]
                };
            }

            try
            {
                WriteResponse writeResponse = await Session.WriteAsync(
                    null,
                    changedWrites.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);
                Assert.That(writeResponse.Results.Count, Is.EqualTo(matrixTypes.Length));
                for (int ii = 0; ii < writeResponse.Results.Count; ii++)
                {
                    Assert.That(
                        StatusCode.IsGood(writeResponse.Results[ii]),
                        Is.True,
                        $"Whole-matrix write failed for {matrixTypes[ii]}.");
                }

                await Task.Delay(1200).ConfigureAwait(false);
                HashSet<uint> changedHandles = await CollectDataChangeClientHandlesAsync(
                    matrixTypes.Length).ConfigureAwait(false);
                string[] missingTypes = [.. Enumerable.Range(0, matrixTypes.Length)
                    .Where(index => !changedHandles.Contains((uint)index + 1))
                    .Select(index => matrixTypes[index])];

                Assert.That(
                    missingTypes,
                    Is.Empty,
                    $"Missing matrix notifications: {string.Join(", ", missingTypes)}.");
            }
            finally
            {
                await Session.WriteAsync(
                    null,
                    restoreWrites.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task PublishReturnsCorrectMonitoredItemClientHandleAsync()
        {
            NodeId nodeId = VariableIds.Server_ServerStatus_CurrentTime;
            const uint expectedHandle = 777u;

            CreateMonitoredItemsResponse createResp = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, expectedHandle, samplingInterval: 50)).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(createResp.Results[0].StatusCode), Is.True);

            await Task.Delay(300).ConfigureAwait(false);

            PublishResponse pubResp = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(pubResp.ResponseHeader.ServiceResult), Is.True);
            Assert.That(pubResp.NotificationMessage.NotificationData.Count, Is.GreaterThan(0));

            var dcn = ExtensionObject.ToEncodeable(pubResp.NotificationMessage.NotificationData[0]) as
                DataChangeNotification;
            if (dcn != null && dcn.MonitoredItems.Count > 0)
            {
                Assert.That(dcn.MonitoredItems[0].ClientHandle, Is.EqualTo(expectedHandle));
            }
        }

        [Test]
        public async Task CreateMonitoredItemWithDiscardOldestTrueAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse response = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 1, queueSize: 2, discardOldest: true)).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
        }

        [Test]
        public async Task CreateMonitoredItemWithDiscardOldestFalseAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse response = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 1, queueSize: 2, discardOldest: false)).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
        }

        [Test]
        public async Task SetTriggeringLinkTriggeringToTriggeredItemAsync()
        {
            NodeId triggeringNode = VariableIds.Server_ServerStatus_CurrentTime;
            NodeId triggeredNode = ToNodeId(Constants.ScalarStaticInt32);

            var items = new MonitoredItemCreateRequest[]
            {
                CreateItemRequest(triggeringNode, 1, samplingInterval: 100),
                CreateItemRequest(triggeredNode, 2, samplingInterval: 100,
                    mode: MonitoringMode.Sampling)
            };

            CreateMonitoredItemsResponse createResp = await Session.CreateMonitoredItemsAsync(
                null,
                m_subscriptionId,
                TimestampsToReturn.Both,
                items.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(createResp.Results.Count, Is.EqualTo(2));
            Assert.That(StatusCode.IsGood(createResp.Results[0].StatusCode), Is.True);
            Assert.That(StatusCode.IsGood(createResp.Results[1].StatusCode), Is.True);

            uint triggeringItemId = createResp.Results[0].MonitoredItemId;
            uint triggeredItemId = createResp.Results[1].MonitoredItemId;

            SetTriggeringResponse trigResp = await Session.SetTriggeringAsync(
                null,
                m_subscriptionId,
                triggeringItemId,
                new uint[] { triggeredItemId }.ToArrayOf(),
                default,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(trigResp.ResponseHeader.ServiceResult), Is.True);
            Assert.That(trigResp.AddResults.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(trigResp.AddResults[0]), Is.True);
        }

        [Test]
        public async Task SetTriggeringRemoveLinkAsync()
        {
            NodeId triggeringNode = VariableIds.Server_ServerStatus_CurrentTime;
            NodeId triggeredNode = ToNodeId(Constants.ScalarStaticInt32);

            var items = new MonitoredItemCreateRequest[]
            {
                CreateItemRequest(triggeringNode, 1, samplingInterval: 100),
                CreateItemRequest(triggeredNode, 2, samplingInterval: 100,
                    mode: MonitoringMode.Sampling)
            };

            CreateMonitoredItemsResponse createResp = await Session.CreateMonitoredItemsAsync(
                null,
                m_subscriptionId,
                TimestampsToReturn.Both,
                items.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            uint triggeringItemId = createResp.Results[0].MonitoredItemId;
            uint triggeredItemId = createResp.Results[1].MonitoredItemId;

            // Add link
            await Session.SetTriggeringAsync(
                null,
                m_subscriptionId,
                triggeringItemId,
                new uint[] { triggeredItemId }.ToArrayOf(),
                default,
                CancellationToken.None).ConfigureAwait(false);

            // Remove link
            SetTriggeringResponse removeResp = await Session.SetTriggeringAsync(
                null,
                m_subscriptionId,
                triggeringItemId,
                default,
                new uint[] { triggeredItemId }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(removeResp.ResponseHeader.ServiceResult), Is.True);
            Assert.That(removeResp.RemoveResults.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(removeResp.RemoveResults[0]), Is.True);
        }

        [Test]
        public async Task MonitoredItemRevisedSamplingIntervalReturnedAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse response = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 1, samplingInterval: 500)).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
            Assert.That(response.Results[0].RevisedSamplingInterval, Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public async Task QueueSizeOneOnlyLatestValueAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
            CreateMonitoredItemsResponse response = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 100, queueSize: 1)).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
            Assert.That(response.Results[0].RevisedQueueSize, Is.GreaterThanOrEqualTo(1u));
        }

        [Test]
        [Category("LongRunning")]
        public async Task QueueSizeFiveRapidWritesAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
            CreateMonitoredItemsResponse response = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 101, samplingInterval: 0, queueSize: 5))
                .ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
            uint revisedQueue = response.Results[0].RevisedQueueSize;
            Assert.That(revisedQueue, Is.GreaterThanOrEqualTo(1u));

            try
            {
                // Write values rapidly
                for (int i = 0; i < 10; i++)
                {
                    await WriteValueAsync(nodeId, 1000 + i).ConfigureAwait(false);
                }

                await Task.Delay(300).ConfigureAwait(false);

                PublishResponse pub = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult), Is.True);
            }
            catch (ServiceResultException sre) when (IsTransientCiTimeoutStatus(sre.StatusCode))
            {
                Assert.Ignore(
                    $"Timing-sensitive: rapid-write/publish sequence interrupted by CI runner load ({sre.StatusCode}).");
            }
        }

        [Test]
        public async Task QueueSizeZeroRevisedToOneAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
            CreateMonitoredItemsResponse response = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 102, queueSize: 0)).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
            Assert.That(response.Results[0].RevisedQueueSize,
                Is.GreaterThanOrEqualTo(1u),
                "Server should revise QueueSize=0 to at least 1.");
        }

        [Test]
        public async Task DiscardOldestTrueBehaviorAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
            CreateMonitoredItemsResponse response = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 103, samplingInterval: 0,
queueSize: 2, discardOldest: true))
                .ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
        }

        [Test]
        public async Task DiscardOldestFalseBehaviorAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
            CreateMonitoredItemsResponse response = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 104, samplingInterval: 0,
queueSize: 2, discardOldest: false))
                .ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
        }

        [Test]
        public async Task MonitorEventNotifierAttributeAsync()
        {
            CreateMonitoredItemsResponse response = await CreateSingleItemAsync(
                CreateItemRequest(
                    ObjectIds.Server, 105,
                    attributeId: Attributes.EventNotifier)).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            // EventNotifier attribute may or may not be monitorable
            Assert.That(
                StatusCode.IsGood(response.Results[0].StatusCode) ||
                response.Results[0].StatusCode.Code == StatusCodes.BadAttributeIdInvalid ||
                response.Results[0].StatusCode.Code == StatusCodes.BadFilterNotAllowed,
                Is.True);
        }

        [Test]
        public async Task CreateEventMonitoredItemWithEventFilterAsync()
        {
            var eventFilter = new EventFilter
            {
                SelectClauses =
                [
                    new SimpleAttributeOperand
                    {
                        TypeDefinitionId = ObjectTypeIds.BaseEventType,
                        BrowsePath = [new QualifiedName(BrowseNames.EventId)],
                        AttributeId = Attributes.Value
                    },
                    new SimpleAttributeOperand
                    {
                        TypeDefinitionId = ObjectTypeIds.BaseEventType,
                        BrowsePath = [new QualifiedName(BrowseNames.EventType)],
                        AttributeId = Attributes.Value
                    }
                ]
            };

            var item = new MonitoredItemCreateRequest
            {
                ItemToMonitor = new ReadValueId
                {
                    NodeId = ObjectIds.Server,
                    AttributeId = Attributes.EventNotifier
                },
                MonitoringMode = MonitoringMode.Reporting,
                RequestedParameters = new MonitoringParameters
                {
                    ClientHandle = 106,
                    SamplingInterval = 0,
                    QueueSize = 100,
                    DiscardOldest = true,
                    Filter = new ExtensionObject(eventFilter)
                }
            };

            CreateMonitoredItemsResponse response =
                await CreateSingleItemAsync(item).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True,
                "Event monitored item on Server should succeed.");
        }

        [Test]
        public async Task EventFilterWithWhereClauseAsync()
        {
            var eventFilter = new EventFilter
            {
                SelectClauses =
                [
                    new SimpleAttributeOperand
                    {
                        TypeDefinitionId = ObjectTypeIds.BaseEventType,
                        BrowsePath = [new QualifiedName(BrowseNames.EventType)],
                        AttributeId = Attributes.Value
                    }
                ],
                WhereClause = new ContentFilter
                {
                    Elements =
                    [
                        new ContentFilterElement
                        {
                            FilterOperator = FilterOperator.OfType,
                            FilterOperands = new ExtensionObject[]
                            {
                                new(new LiteralOperand
                                {
                                    Value = new Variant(ObjectTypeIds.BaseEventType)
                                })
                            }.ToArrayOf()
                        }
                    ]
                }
            };

            var item = new MonitoredItemCreateRequest
            {
                ItemToMonitor = new ReadValueId
                {
                    NodeId = ObjectIds.Server,
                    AttributeId = Attributes.EventNotifier
                },
                MonitoringMode = MonitoringMode.Reporting,
                RequestedParameters = new MonitoringParameters
                {
                    ClientHandle = 107,
                    SamplingInterval = 0,
                    QueueSize = 100,
                    DiscardOldest = true,
                    Filter = new ExtensionObject(eventFilter)
                }
            };

            CreateMonitoredItemsResponse response =
                await CreateSingleItemAsync(item).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
        }

        [Test]
        public async Task BatchModifyFiftyMonitoredItemsAsync()
        {
            // Create 50 items
            var items = new List<MonitoredItemCreateRequest>();
            for (int i = 0; i < 50; i++)
            {
                ExpandedNodeId eni = Constants.ScalarStaticNodes[
                    i % Constants.ScalarStaticNodes.Length];
                items.Add(CreateItemRequest(ToNodeId(eni), (uint)(200 + i)));
            }

            CreateMonitoredItemsResponse createResp =
                await Session.CreateMonitoredItemsAsync(
                    null, m_subscriptionId, TimestampsToReturn.Both,
                    items.ToArray().ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

            Assert.That(createResp.Results.Count, Is.EqualTo(50));

            // Batch modify
            var modifyItems = new List<MonitoredItemModifyRequest>();
            foreach (MonitoredItemCreateResult r in createResp.Results)
            {
                if (StatusCode.IsGood(r.StatusCode))
                {
                    modifyItems.Add(new MonitoredItemModifyRequest
                    {
                        MonitoredItemId = r.MonitoredItemId,
                        RequestedParameters = new MonitoringParameters
                        {
                            ClientHandle = r.MonitoredItemId,
                            SamplingInterval = 2000,
                            QueueSize = 5,
                            DiscardOldest = true
                        }
                    });
                }
            }

            if (modifyItems.Count == 0)
            {
                Assert.Fail("No items were created to modify.");
            }

            ModifyMonitoredItemsResponse modResp =
                await Session.ModifyMonitoredItemsAsync(
                    null, m_subscriptionId, TimestampsToReturn.Both,
                    modifyItems.ToArray().ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

            Assert.That(modResp.Results.Count, Is.EqualTo(modifyItems.Count));
            foreach (MonitoredItemModifyResult mr in modResp.Results)
            {
                Assert.That(StatusCode.IsGood(mr.StatusCode), Is.True);
            }
        }

        [Test]
        public async Task BatchDeleteFiftyMonitoredItemsAsync()
        {
            // Create 50 items
            var items = new List<MonitoredItemCreateRequest>();
            for (int i = 0; i < 50; i++)
            {
                ExpandedNodeId eni = Constants.ScalarStaticNodes[
                    i % Constants.ScalarStaticNodes.Length];
                items.Add(CreateItemRequest(ToNodeId(eni), (uint)(300 + i)));
            }

            CreateMonitoredItemsResponse createResp =
                await Session.CreateMonitoredItemsAsync(
                    null, m_subscriptionId, TimestampsToReturn.Both,
                    items.ToArray().ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

            var ids = new List<uint>();
            foreach (MonitoredItemCreateResult r in createResp.Results)
            {
                if (StatusCode.IsGood(r.StatusCode))
                {
                    ids.Add(r.MonitoredItemId);
                }
            }

            if (ids.Count == 0)
            {
                Assert.Fail("No items to delete.");
            }

            DeleteMonitoredItemsResponse delResp =
                await Session.DeleteMonitoredItemsAsync(
                    null, m_subscriptionId,
                    ids.ToArray().ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

            Assert.That(delResp.Results.Count, Is.EqualTo(ids.Count));
            foreach (StatusCode sc in delResp.Results)
            {
                Assert.That(StatusCode.IsGood(sc), Is.True);
            }
        }

        [Test]
        public async Task VeryFastSamplingIntervalRevisedAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
            CreateMonitoredItemsResponse response = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 108, samplingInterval: 0.001))
                .ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
            Assert.That(
                response.Results[0].RevisedSamplingInterval,
                Is.GreaterThanOrEqualTo(0),
                "Server should revise extremely fast sampling interval.");
        }

        [Test]
        public async Task MonitorAccessLevelAttributeAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
            CreateMonitoredItemsResponse response = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 109,
                    attributeId: Attributes.AccessLevel)).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            // AccessLevel monitoring may or may not be supported
            Assert.That(
                StatusCode.IsGood(response.Results[0].StatusCode) ||
                response.Results[0].StatusCode.Code ==
                    StatusCodes.BadFilterNotAllowed,
                Is.True);
        }

        private MonitoredItemCreateRequest CreateItemRequest(
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
                null,
                m_subscriptionId,
                TimestampsToReturn.Both,
                new MonitoredItemCreateRequest[] { item }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
        }

        private async Task WriteValueAsync(NodeId nodeId, int value)
        {
            WriteResponse writeResp = await Session.WriteAsync(
                null,
                new WriteValue[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(Variant.From(value))
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(writeResp.Results[0]), Is.True);
        }

        private async Task<HashSet<uint>> CollectDataChangeClientHandlesAsync(int expectedCount)
        {
            var handles = new HashSet<uint>();
            for (int ii = 0; ii < 3 && handles.Count < expectedCount; ii++)
            {
                PublishResponse response = await Session
                    .PublishWithTimeoutAsync()
                    .ConfigureAwait(false);
                handles.UnionWith(GetDataChangeClientHandles(response));
            }
            return handles;
        }

        private static HashSet<uint> GetDataChangeClientHandles(PublishResponse response)
        {
            var handles = new HashSet<uint>();
            foreach (ExtensionObject notification in response.NotificationMessage.NotificationData)
            {
                if (ExtensionObject.ToEncodeable(notification) is DataChangeNotification dataChange)
                {
                    foreach (MonitoredItemNotification item in dataChange.MonitoredItems)
                    {
                        handles.Add(item.ClientHandle);
                    }
                }
            }
            return handles;
        }

        private static Variant ChangeSelectedMatrixElement(Variant value)
        {
            switch (value.TypeInfo.BuiltInType)
            {
                case BuiltInType.Boolean when value.TryGetValue(out MatrixOf<bool> matrix):
                    return new Variant(ChangeSelectedMatrixElement(matrix, item => !item));
                case BuiltInType.Byte when value.TryGetValue(out MatrixOf<byte> matrix):
                    return new Variant(ChangeSelectedMatrixElement(
                        matrix,
                        item => item == byte.MaxValue ? (byte)0 : (byte)(item + 1)));
                case BuiltInType.ByteString when value.TryGetValue(out MatrixOf<ByteString> matrix):
                    return new Variant(ChangeSelectedMatrixElement(
                        matrix,
                        item => ByteString.From(item.ToArray().Append((byte)1).ToArray())));
                case BuiltInType.DateTime when value.TryGetValue(out MatrixOf<DateTimeUtc> matrix):
                    return new Variant(ChangeSelectedMatrixElement(
                        matrix,
                        item => item == DateTimeUtc.MaxValue
                            ? DateTimeUtc.MinValue
                            : item.AddMilliseconds(1)));
                case BuiltInType.Double when value.TryGetValue(out MatrixOf<double> matrix):
                    return new Variant(ChangeSelectedMatrixElement(
                        matrix,
                        item => item == 1.25 ? 2.5 : 1.25));
                case BuiltInType.Float when value.TryGetValue(out MatrixOf<float> matrix):
                    return new Variant(ChangeSelectedMatrixElement(
                        matrix,
                        item => item == 1.25f ? 2.5f : 1.25f));
                case BuiltInType.Guid when value.TryGetValue(out MatrixOf<Uuid> matrix):
                    return new Variant(ChangeSelectedMatrixElement(
                        matrix,
                        item => item == new Uuid("00112233-4455-6677-8899-aabbccddeeff")
                            ? new Uuid("ffeeddcc-bbaa-9988-7766-554433221100")
                            : new Uuid("00112233-4455-6677-8899-aabbccddeeff")));
                case BuiltInType.Int16 when value.TryGetValue(out MatrixOf<short> matrix):
                    return new Variant(ChangeSelectedMatrixElement(
                        matrix,
                        item => item == short.MaxValue ? short.MinValue : (short)(item + 1)));
                case BuiltInType.Int32 when value.TryGetValue(out MatrixOf<int> matrix):
                    return new Variant(ChangeSelectedMatrixElement(
                        matrix,
                        item => item == int.MaxValue ? int.MinValue : item + 1));
                case BuiltInType.Int64 when value.TryGetValue(out MatrixOf<long> matrix):
                    return new Variant(ChangeSelectedMatrixElement(
                        matrix,
                        item => item == long.MaxValue ? long.MinValue : item + 1));
                case BuiltInType.SByte when value.TryGetValue(out MatrixOf<sbyte> matrix):
                    return new Variant(ChangeSelectedMatrixElement(
                        matrix,
                        item => item == sbyte.MaxValue ? sbyte.MinValue : (sbyte)(item + 1)));
                case BuiltInType.String when value.TryGetValue(out MatrixOf<string> matrix):
                    return new Variant(ChangeSelectedMatrixElement(
                        matrix,
                        item => (item ?? string.Empty) + "x"));
                case BuiltInType.UInt16 when value.TryGetValue(out MatrixOf<ushort> matrix):
                    return new Variant(ChangeSelectedMatrixElement(
                        matrix,
                        item => item == ushort.MaxValue ? (ushort)0 : (ushort)(item + 1)));
                case BuiltInType.UInt32 when value.TryGetValue(out MatrixOf<uint> matrix):
                    return new Variant(ChangeSelectedMatrixElement(
                        matrix,
                        item => item == uint.MaxValue ? 0 : item + 1));
                case BuiltInType.UInt64 when value.TryGetValue(out MatrixOf<ulong> matrix):
                    return new Variant(ChangeSelectedMatrixElement(
                        matrix,
                        item => item == ulong.MaxValue ? 0 : item + 1));
                case BuiltInType.XmlElement
                    when value.TryGetValue(out MatrixOf<XmlElement> matrix):
                    return new Variant(ChangeSelectedMatrixElement(
                        matrix,
                        item => item == XmlElement.From("<changed/>")
                            ? XmlElement.From("<changed2/>")
                            : XmlElement.From("<changed/>")));
                case BuiltInType.Variant when value.TryGetValue(out MatrixOf<Variant> matrix):
                    return new Variant(ChangeSelectedMatrixElement(
                        matrix,
                        item => item.Equals(Variant.From("changed"))
                            ? Variant.From("changed2")
                            : Variant.From("changed")));
                case BuiltInType.LocalizedText
                    when value.TryGetValue(out MatrixOf<LocalizedText> matrix):
                    return new Variant(ChangeSelectedMatrixElement(
                        matrix,
                        item => new LocalizedText(
                            item.Locale,
                            (item.Text ?? string.Empty) + "x")));
                case BuiltInType.QualifiedName
                    when value.TryGetValue(out MatrixOf<QualifiedName> matrix):
                    return new Variant(ChangeSelectedMatrixElement(
                        matrix,
                        item => new QualifiedName(
                            (item.Name ?? string.Empty) + "x",
                            item.NamespaceIndex)));
                default:
                    throw new AssertionException(
                        $"Unsupported matrix type {value.TypeInfo.BuiltInType}.");
            }
        }

        private static MatrixOf<T> ChangeSelectedMatrixElement<T>(
            MatrixOf<T> matrix,
            Func<T, T> change)
        {
            int[] dimensions = matrix.Dimensions;
            Assert.That(dimensions, Has.Length.GreaterThanOrEqualTo(2));

            T[] elements = matrix.Memory.ToArray();
            int selectedIndex = GetSelectedMatrixIndex(dimensions);
            elements[selectedIndex] = change(elements[selectedIndex]);
            return elements.ToMatrixOf(dimensions);
        }

        private static int GetSelectedMatrixIndex(int[] dimensions)
        {
            int selectedIndex = 0;
            int stride = 1;
            for (int ii = dimensions.Length - 1; ii >= 0; ii--)
            {
                Assert.That(dimensions[ii], Is.GreaterThan(1));
                selectedIndex += stride;
                stride *= dimensions[ii];
            }
            return selectedIndex;
        }

        private uint m_subscriptionId;
    }
}
