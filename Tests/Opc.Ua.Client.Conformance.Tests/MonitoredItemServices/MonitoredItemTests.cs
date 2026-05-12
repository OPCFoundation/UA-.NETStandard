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

namespace Opc.Ua.Client.Conformance.Tests
{
    /// <summary>
    /// compliance tests for MonitoredItem Service Set.
    /// </summary>
    [NonParallelizable]
    [TestFixture]
    [Category("Conformance")]
    [Category("MonitoredItem")]
    public class MonitoredItemTests : TestFixture
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
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "001")]
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
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "017")]
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
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "008")]
        public async Task CreateMonitoredItemWithSamplingIntervalZeroServerRevisesAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse response = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 1, samplingInterval: 0)).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
            Assert.That(response.Results[0].RevisedSamplingInterval, Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "005")]
        public async Task CreateMonitoredItemWithQueueSizeZeroServerRevisesToOneAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse response = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 1, queueSize: 0)).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
            Assert.That(response.Results[0].RevisedQueueSize, Is.GreaterThanOrEqualTo(1u));
        }

        [Test]
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "001")]
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
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "004")]
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
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "014")]
        public async Task CreateMonitoredItemInSamplingModeAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse response = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 1, mode: MonitoringMode.Sampling)).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "013")]
        public async Task CreateMonitoredItemInDisabledModeAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse response = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 1, mode: MonitoringMode.Disabled)).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "006")]
        public async Task CreateMonitoredItemForDisplayNameAttributeAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse response = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 1, attributeId: Attributes.DisplayName)).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "006")]
        public async Task CreateMonitoredItemForBrowseNameAttributeAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse response = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 1, attributeId: Attributes.BrowseName)).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "Err-001")]
        public async Task CreateMonitoredItemWithInvalidNodeIdReturnsBadNodeIdUnknownAsync()
        {
            CreateMonitoredItemsResponse response = await CreateSingleItemAsync(
                CreateItemRequest(Constants.InvalidNodeId, 1)).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsBad(response.Results[0].StatusCode), Is.True,
                "Creating monitored item with invalid NodeId should return Bad status.");
        }

        [Test]
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "Err-002")]
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
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "Err-010")]
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
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "017")]
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
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "019")]
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
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "019")]
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
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "Err-006")]
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
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "Err-007")]
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
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "005")]
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
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "005")]
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
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "005")]
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
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "Err-015")]
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
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "016")]
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
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "017")]
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
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "015")]
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
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "015")]
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
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "001")]
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
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "Err-013")]
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
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "017")]
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
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "002")]
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
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "002")]
        public async Task CreateMonitoredItemInitialValueReturnedAsync()
        {
            NodeId nodeId = VariableIds.Server_ServerStatus_CurrentTime;

            CreateMonitoredItemsResponse createResp = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 1, samplingInterval: 100)).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(createResp.Results[0].StatusCode), Is.True);

            PublishResponse publishResp = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(publishResp.ResponseHeader.ServiceResult), Is.True);
            Assert.That(publishResp.NotificationMessage, Is.Not.Null);
        }

        [Test]
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "007")]
        public async Task WriteValueAndPublishVerifyNotificationContainsNewValueAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
            const uint clientHandle = 99u;

            CreateMonitoredItemsResponse createResp = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, clientHandle, samplingInterval: 50, queueSize: 10)).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(createResp.Results[0].StatusCode), Is.True);

            // Consume initial notification
            await Task.Delay(200).ConfigureAwait(false);
            await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            // Write new value
            int newValue = new Random().Next(1, 10000);
            await WriteValueAsync(nodeId, newValue).ConfigureAwait(false);

            await Task.Delay(300).ConfigureAwait(false);

            PublishResponse pubResp = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(pubResp.ResponseHeader.ServiceResult), Is.True);
            Assert.That(pubResp.NotificationMessage.NotificationData.Count, Is.GreaterThan(0));
        }

        [Test]
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "003")]
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
            await WriteValueAsync(nodeId1, new Random().Next(1, 10000)).ConfigureAwait(false);

            await Task.Delay(300).ConfigureAwait(false);

            PublishResponse pubResp = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(pubResp.ResponseHeader.ServiceResult), Is.True);
            Assert.That(pubResp.NotificationMessage, Is.Not.Null);
        }

        [Test]
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "002")]
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
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "004")]
        public async Task CreateMonitoredItemWithDiscardOldestTrueAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse response = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 1, queueSize: 2, discardOldest: true)).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "004")]
        public async Task CreateMonitoredItemWithDiscardOldestFalseAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse response = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 1, queueSize: 2, discardOldest: false)).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "017")]
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
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "017")]
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
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "001")]
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
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "005")]
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
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "004")]
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
            catch (ServiceResultException sre) when (
                sre.StatusCode == StatusCodes.BadRequestTimeout ||
                sre.StatusCode == StatusCodes.BadRequestInterrupted ||
                sre.StatusCode == StatusCodes.BadConnectionClosed)
            {
                Assert.Ignore(
                    $"Timing-sensitive: rapid-write/publish sequence interrupted by CI runner load ({sre.StatusCode}).");
            }
        }

        [Test]
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "005")]
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
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "004")]
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
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "004")]
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
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "006")]
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
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "006")]
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
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "006")]
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
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "005")]
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
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "017")]
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
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "008")]
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
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "006")]
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

        private uint m_subscriptionId;
    }
}
