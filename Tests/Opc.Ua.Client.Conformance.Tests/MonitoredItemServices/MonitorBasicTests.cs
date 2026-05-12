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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Client.Conformance.Tests
{
    /// <summary>
    /// compliance tests for Monitor Basic conformance unit.
    /// Tests 002-039 covering CreateMonitoredItems, ModifyMonitoredItems,
    /// SetMonitoringMode, DataEncoding, and multi-dimensional arrays.
    /// </summary>
    [NonParallelizable]
    [TestFixture]
    [Category("Conformance")]
    [Category("MonitorBasic")]
    public class MonitorBasicTests : TestFixture
    {
        [SetUp]
        public async Task SetUp()
        {
            CreateSubscriptionResponse response = await Session.CreateSubscriptionAsync(
                null, 1000, 100, 10, 0, true, 0,
                CancellationToken.None).ConfigureAwait(false);
            m_subscriptionId = response.SubscriptionId;
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
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "002")]
        public async Task CreateMonitoredItemsDisabledModeServerTimestampAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse response = await Session.CreateMonitoredItemsAsync(
                null, m_subscriptionId, TimestampsToReturn.Server,
                new MonitoredItemCreateRequest[]
                {
                    CreateItemRequest(nodeId, 1, queueSize: 1,
                        mode: MonitoringMode.Disabled)
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(response.ResponseHeader.ServiceResult), Is.True);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "003")]
        public async Task ModifyMonitoredItemChangeClientHandleAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse createResp = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 1)).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(createResp.Results[0].StatusCode), Is.True);
            uint monitoredItemId = createResp.Results[0].MonitoredItemId;

            ModifyMonitoredItemsResponse modifyResp = await Session.ModifyMonitoredItemsAsync(
                null, m_subscriptionId, TimestampsToReturn.Both,
                new MonitoredItemModifyRequest[]
                {
                    new() {
                        MonitoredItemId = monitoredItemId,
                        RequestedParameters = new MonitoringParameters
                        {
                            ClientHandle = 0x1234,
                            SamplingInterval = 1000,
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
        [Property("Tag", "004")]
        public async Task ModifyMonitoredItemTimestampsToSourceAsync()
        {
            NodeId nodeId = VariableIds.Server_ServerStatus_CurrentTime;

            CreateMonitoredItemsResponse createResp = await Session.CreateMonitoredItemsAsync(
                null, m_subscriptionId, TimestampsToReturn.Server,
                new MonitoredItemCreateRequest[]
                {
                    CreateItemRequest(nodeId, 1, samplingInterval: 100)
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(createResp.Results[0].StatusCode), Is.True);
            uint monitoredItemId = createResp.Results[0].MonitoredItemId;

            ModifyMonitoredItemsResponse modifyResp = await Session.ModifyMonitoredItemsAsync(
                null, m_subscriptionId, TimestampsToReturn.Source,
                new MonitoredItemModifyRequest[]
                {
                    new() {
                        MonitoredItemId = monitoredItemId,
                        RequestedParameters = new MonitoringParameters
                        {
                            ClientHandle = 1,
                            SamplingInterval = 100,
                            QueueSize = 10,
                            DiscardOldest = true
                        }
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(modifyResp.Results[0].StatusCode), Is.True);
            await Task.Delay(500).ConfigureAwait(false);

            PublishResponse pubResp = await PublishAndAckAsync().ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(pubResp.ResponseHeader.ServiceResult), Is.True);
            Assert.That(pubResp.NotificationMessage.NotificationData.Count, Is.GreaterThan(0));
        }

        [Test]
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "005")]
        public async Task ModifyMonitoredItemTimestampsToServerAsync()
        {
            NodeId nodeId = VariableIds.Server_ServerStatus_CurrentTime;

            CreateMonitoredItemsResponse createResp = await Session.CreateMonitoredItemsAsync(
                null, m_subscriptionId, TimestampsToReturn.Source,
                new MonitoredItemCreateRequest[]
                {
                    CreateItemRequest(nodeId, 1, samplingInterval: 100)
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(createResp.Results[0].StatusCode), Is.True);
            uint monitoredItemId = createResp.Results[0].MonitoredItemId;

            ModifyMonitoredItemsResponse modifyResp = await Session.ModifyMonitoredItemsAsync(
                null, m_subscriptionId, TimestampsToReturn.Server,
                new MonitoredItemModifyRequest[]
                {
                    new() {
                        MonitoredItemId = monitoredItemId,
                        RequestedParameters = new MonitoringParameters
                        {
                            ClientHandle = 1,
                            SamplingInterval = 100,
                            QueueSize = 10,
                            DiscardOldest = true
                        }
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(modifyResp.Results[0].StatusCode), Is.True);
            await Task.Delay(500).ConfigureAwait(false);

            PublishResponse pubResp = await PublishAndAckAsync().ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(pubResp.ResponseHeader.ServiceResult), Is.True);
            Assert.That(pubResp.NotificationMessage.NotificationData.Count, Is.GreaterThan(0));
        }

        [Test]
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "006")]
        public async Task ModifyMonitoredItemTimestampsToNeitherAsync()
        {
            NodeId nodeId = VariableIds.Server_ServerStatus_CurrentTime;

            CreateMonitoredItemsResponse createResp = await Session.CreateMonitoredItemsAsync(
                null, m_subscriptionId, TimestampsToReturn.Server,
                new MonitoredItemCreateRequest[]
                {
                    CreateItemRequest(nodeId, 1, samplingInterval: 100)
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(createResp.Results[0].StatusCode), Is.True);
            uint monitoredItemId = createResp.Results[0].MonitoredItemId;

            ModifyMonitoredItemsResponse modifyResp = await Session.ModifyMonitoredItemsAsync(
                null, m_subscriptionId, TimestampsToReturn.Neither,
                new MonitoredItemModifyRequest[]
                {
                    new() {
                        MonitoredItemId = monitoredItemId,
                        RequestedParameters = new MonitoringParameters
                        {
                            ClientHandle = 1,
                            SamplingInterval = 100,
                            QueueSize = 10,
                            DiscardOldest = true
                        }
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(modifyResp.Results[0].StatusCode), Is.True);
            await Task.Delay(500).ConfigureAwait(false);

            PublishResponse pubResp = await PublishAndAckAsync().ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(pubResp.ResponseHeader.ServiceResult), Is.True);
            Assert.That(pubResp.NotificationMessage.NotificationData.Count, Is.GreaterThan(0));
        }

        [Test]
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "010")]
        public async Task ModifyMultipleItemsSamplingIntervalsAsync()
        {
            var items = new List<MonitoredItemCreateRequest>();
            int count = Math.Min(5, Constants.ScalarStaticNodes.Length);
            for (int i = 0; i < count; i++)
            {
                NodeId nodeId = ToNodeId(Constants.ScalarStaticNodes[i]);
                items.Add(CreateItemRequest(nodeId, (uint)(100 + i)));
            }

            CreateMonitoredItemsResponse createResp = await Session.CreateMonitoredItemsAsync(
                null, m_subscriptionId, TimestampsToReturn.Both,
                items.ToArray().ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(createResp.Results.Count, Is.EqualTo(count));

            var modItems = new List<MonitoredItemModifyRequest>();
            for (int i = 0; i < count; i++)
            {
                Assert.That(StatusCode.IsGood(createResp.Results[i].StatusCode), Is.True);
                modItems.Add(new MonitoredItemModifyRequest
                {
                    MonitoredItemId = createResp.Results[i].MonitoredItemId,
                    RequestedParameters = new MonitoringParameters
                    {
                        ClientHandle = (uint)(100 + i),
                        SamplingInterval = i % 2 == 0 ? 1000 : 3000,
                        QueueSize = 10,
                        DiscardOldest = true
                    }
                });
            }

            ModifyMonitoredItemsResponse modResp = await Session.ModifyMonitoredItemsAsync(
                null, m_subscriptionId, TimestampsToReturn.Both,
                modItems.ToArray().ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(modResp.Results.Count, Is.EqualTo(count));
            foreach (MonitoredItemModifyResult r in modResp.Results)
            {
                Assert.That(StatusCode.IsGood(r.StatusCode), Is.True);
                Assert.That(r.RevisedSamplingInterval, Is.GreaterThanOrEqualTo(0.0));
            }
        }

        [Test]
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "011")]
        public async Task ModifyMonitoredItemQueueSizeZeroAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse createResp = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 1)).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(createResp.Results[0].StatusCode), Is.True);
            uint monitoredItemId = createResp.Results[0].MonitoredItemId;

            ModifyMonitoredItemsResponse modResp = await Session.ModifyMonitoredItemsAsync(
                null, m_subscriptionId, TimestampsToReturn.Both,
                new MonitoredItemModifyRequest[]
                {
                    new() {
                        MonitoredItemId = monitoredItemId,
                        RequestedParameters = new MonitoringParameters
                        {
                            ClientHandle = 1,
                            SamplingInterval = 1000,
                            QueueSize = 0,
                            DiscardOldest = true
                        }
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(modResp.Results[0].StatusCode), Is.True);
            Assert.That(modResp.Results[0].RevisedQueueSize, Is.GreaterThan(0u),
                "Server should revise QueueSize=0 to at least 1");
        }

        [Test]
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "012")]
        public async Task ModifyMonitoredItemQueueSizeMaxUInt32Async()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse createResp = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 1)).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(createResp.Results[0].StatusCode), Is.True);
            uint monitoredItemId = createResp.Results[0].MonitoredItemId;

            ModifyMonitoredItemsResponse modResp = await Session.ModifyMonitoredItemsAsync(
                null, m_subscriptionId, TimestampsToReturn.Both,
                new MonitoredItemModifyRequest[]
                {
                    new() {
                        MonitoredItemId = monitoredItemId,
                        RequestedParameters = new MonitoringParameters
                        {
                            ClientHandle = 1,
                            SamplingInterval = 1000,
                            QueueSize = uint.MaxValue,
                            DiscardOldest = true
                        }
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(modResp.Results[0].StatusCode), Is.True);
            Assert.That(modResp.Results[0].RevisedQueueSize, Is.GreaterThan(0u));
        }

        [Test]
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "015")]
        public async Task SetMonitoringModeOnDeletedItemReturnsBadIdAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
            var items = new MonitoredItemCreateRequest[]
            {
                CreateItemRequest(nodeId, 1),
                CreateItemRequest(nodeId, 2)
            };

            CreateMonitoredItemsResponse createResp = await Session.CreateMonitoredItemsAsync(
                null, m_subscriptionId, TimestampsToReturn.Both,
                items.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            uint[] itemIds = [.. createResp.Results.ToArray().Select(r => r.MonitoredItemId)];

            await Session.DeleteMonitoredItemsAsync(
                null, m_subscriptionId, itemIds.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            SetMonitoringModeResponse smResp = await Session.SetMonitoringModeAsync(
                null, m_subscriptionId, MonitoringMode.Reporting,
                itemIds.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(smResp.ResponseHeader.ServiceResult), Is.True);
            foreach (StatusCode sc in smResp.Results)
            {
                Assert.That(sc.Code, Is.EqualTo(StatusCodes.BadMonitoredItemIdInvalid));
            }
        }

        [Test]
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "016")]
        public async Task SetMonitoringModeOnMixDeletedAndValidItemsAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
            var items = new MonitoredItemCreateRequest[]
            {
                CreateItemRequest(nodeId, 1),
                CreateItemRequest(nodeId, 2),
                CreateItemRequest(nodeId, 3),
                CreateItemRequest(nodeId, 4)
            };

            CreateMonitoredItemsResponse createResp = await Session.CreateMonitoredItemsAsync(
                null, m_subscriptionId, TimestampsToReturn.Both,
                items.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            uint[] allIds = [.. createResp.Results.ToArray().Select(r => r.MonitoredItemId)];

            uint[] toDelete = [allIds[0], allIds[1]];
            await Session.DeleteMonitoredItemsAsync(
                null, m_subscriptionId, toDelete.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            SetMonitoringModeResponse smResp = await Session.SetMonitoringModeAsync(
                null, m_subscriptionId, MonitoringMode.Reporting,
                allIds.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(smResp.Results.Count, Is.EqualTo(4));
            Assert.That(smResp.Results[0].Code, Is.EqualTo(StatusCodes.BadMonitoredItemIdInvalid));
            Assert.That(smResp.Results[1].Code, Is.EqualTo(StatusCodes.BadMonitoredItemIdInvalid));
            Assert.That(StatusCode.IsGood(smResp.Results[2]), Is.True);
            Assert.That(StatusCode.IsGood(smResp.Results[3]), Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "018")]
        public async Task CreateItemSamplingIntervalZeroReportingAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse resp = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 1, samplingInterval: 0,
                    mode: MonitoringMode.Reporting)).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(resp.Results[0].StatusCode), Is.True);
            Assert.That(resp.Results[0].RevisedSamplingInterval, Is.GreaterThanOrEqualTo(0.0));
        }

        [Test]
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "020")]
        public async Task SetMonitoringModeDisabledToDisabledAsync()
        {
            NodeId nodeId = VariableIds.Server_ServerStatus_CurrentTime;

            CreateMonitoredItemsResponse createResp = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 1, samplingInterval: 100,
                    mode: MonitoringMode.Disabled)).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(createResp.Results[0].StatusCode), Is.True);
            uint monitoredItemId = createResp.Results[0].MonitoredItemId;

            await Task.Delay(500).ConfigureAwait(false);
            PublishResponse pub1 = await PublishAndAckAsync().ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(pub1.ResponseHeader.ServiceResult), Is.True);

            SetMonitoringModeResponse smResp = await Session.SetMonitoringModeAsync(
                null, m_subscriptionId, MonitoringMode.Disabled,
                new uint[] { monitoredItemId }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(smResp.Results[0]), Is.True);

            await Task.Delay(500).ConfigureAwait(false);
            PublishResponse pub2 = await PublishAndAckAsync().ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(pub2.ResponseHeader.ServiceResult), Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "021")]
        public async Task SetMonitoringModeDisabledToSamplingAsync()
        {
            NodeId nodeId = VariableIds.Server_ServerStatus_CurrentTime;

            CreateMonitoredItemsResponse createResp = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 1, samplingInterval: 100,
                    mode: MonitoringMode.Disabled)).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(createResp.Results[0].StatusCode), Is.True);
            uint monitoredItemId = createResp.Results[0].MonitoredItemId;

            await Task.Delay(500).ConfigureAwait(false);
            await PublishAndAckAsync().ConfigureAwait(false);

            SetMonitoringModeResponse smResp = await Session.SetMonitoringModeAsync(
                null, m_subscriptionId, MonitoringMode.Sampling,
                new uint[] { monitoredItemId }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(smResp.Results[0]), Is.True);

            await Task.Delay(500).ConfigureAwait(false);
            PublishResponse pub2 = await PublishAndAckAsync().ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(pub2.ResponseHeader.ServiceResult), Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "022")]
        public async Task SetMonitoringModeDisabledToReportingAsync()
        {
            NodeId nodeId = VariableIds.Server_ServerStatus_CurrentTime;

            CreateMonitoredItemsResponse createResp = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 1, samplingInterval: 100,
                    mode: MonitoringMode.Disabled)).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(createResp.Results[0].StatusCode), Is.True);
            uint monitoredItemId = createResp.Results[0].MonitoredItemId;

            SetMonitoringModeResponse smResp = await Session.SetMonitoringModeAsync(
                null, m_subscriptionId, MonitoringMode.Reporting,
                new uint[] { monitoredItemId }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(smResp.Results[0]), Is.True);

            await Task.Delay(500).ConfigureAwait(false);
            PublishResponse pubResp = await PublishAndAckAsync().ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(pubResp.ResponseHeader.ServiceResult), Is.True);
            Assert.That(pubResp.NotificationMessage.NotificationData.Count,
                Is.GreaterThan(0), "Expected data after switching to Reporting");
        }

        [Test]
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "023")]
        public async Task SetMonitoringModeSamplingToDisabledAsync()
        {
            NodeId nodeId = VariableIds.Server_ServerStatus_CurrentTime;

            CreateMonitoredItemsResponse createResp = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 1, samplingInterval: 100,
                    mode: MonitoringMode.Sampling)).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(createResp.Results[0].StatusCode), Is.True);
            uint monitoredItemId = createResp.Results[0].MonitoredItemId;

            await Task.Delay(500).ConfigureAwait(false);
            await PublishAndAckAsync().ConfigureAwait(false);

            SetMonitoringModeResponse smResp = await Session.SetMonitoringModeAsync(
                null, m_subscriptionId, MonitoringMode.Disabled,
                new uint[] { monitoredItemId }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(smResp.Results[0]), Is.True);

            await Task.Delay(500).ConfigureAwait(false);
            PublishResponse pub2 = await PublishAndAckAsync().ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(pub2.ResponseHeader.ServiceResult), Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "024")]
        public async Task SetMonitoringModeSamplingToSamplingAsync()
        {
            NodeId nodeId = VariableIds.Server_ServerStatus_CurrentTime;

            CreateMonitoredItemsResponse createResp = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 1, samplingInterval: 100,
                    mode: MonitoringMode.Sampling)).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(createResp.Results[0].StatusCode), Is.True);
            uint monitoredItemId = createResp.Results[0].MonitoredItemId;

            await Task.Delay(500).ConfigureAwait(false);
            await PublishAndAckAsync().ConfigureAwait(false);

            SetMonitoringModeResponse smResp = await Session.SetMonitoringModeAsync(
                null, m_subscriptionId, MonitoringMode.Sampling,
                new uint[] { monitoredItemId }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(smResp.Results[0]), Is.True);

            await Task.Delay(500).ConfigureAwait(false);
            PublishResponse pub2 = await PublishAndAckAsync().ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(pub2.ResponseHeader.ServiceResult), Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "025")]
        public async Task SetMonitoringModeSamplingToReportingAsync()
        {
            NodeId nodeId = VariableIds.Server_ServerStatus_CurrentTime;

            CreateMonitoredItemsResponse createResp = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 1, samplingInterval: 100,
                    mode: MonitoringMode.Sampling)).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(createResp.Results[0].StatusCode), Is.True);
            uint monitoredItemId = createResp.Results[0].MonitoredItemId;

            await Task.Delay(500).ConfigureAwait(false);
            await PublishAndAckAsync().ConfigureAwait(false);

            SetMonitoringModeResponse smResp = await Session.SetMonitoringModeAsync(
                null, m_subscriptionId, MonitoringMode.Reporting,
                new uint[] { monitoredItemId }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(smResp.Results[0]), Is.True);

            await Task.Delay(500).ConfigureAwait(false);
            PublishResponse pub2 = await PublishAndAckAsync().ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(pub2.ResponseHeader.ServiceResult), Is.True);
            Assert.That(pub2.NotificationMessage.NotificationData.Count,
                Is.GreaterThan(0), "Expected data after switching to Reporting");
        }

        [Test]
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "026")]
        public async Task SetMonitoringModeReportingToDisabledAsync()
        {
            NodeId nodeId = VariableIds.Server_ServerStatus_CurrentTime;

            CreateMonitoredItemsResponse createResp = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 1, samplingInterval: 100,
                    mode: MonitoringMode.Reporting)).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(createResp.Results[0].StatusCode), Is.True);
            uint monitoredItemId = createResp.Results[0].MonitoredItemId;

            await Task.Delay(500).ConfigureAwait(false);
            PublishResponse pub1 = await PublishAndAckAsync().ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(pub1.ResponseHeader.ServiceResult), Is.True);
            Assert.That(pub1.NotificationMessage.NotificationData.Count, Is.GreaterThan(0));

            SetMonitoringModeResponse smResp = await Session.SetMonitoringModeAsync(
                null, m_subscriptionId, MonitoringMode.Disabled,
                new uint[] { monitoredItemId }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(smResp.Results[0]), Is.True);

            await Task.Delay(500).ConfigureAwait(false);
            PublishResponse pub2 = await PublishAndAckAsync().ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(pub2.ResponseHeader.ServiceResult), Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "027")]
        public async Task SetMonitoringModeReportingToSamplingAsync()
        {
            NodeId nodeId = VariableIds.Server_ServerStatus_CurrentTime;

            CreateMonitoredItemsResponse createResp = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 1, samplingInterval: 100,
                    mode: MonitoringMode.Reporting)).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(createResp.Results[0].StatusCode), Is.True);
            uint monitoredItemId = createResp.Results[0].MonitoredItemId;

            await Task.Delay(500).ConfigureAwait(false);
            PublishResponse pub1 = await PublishAndAckAsync().ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(pub1.ResponseHeader.ServiceResult), Is.True);
            Assert.That(pub1.NotificationMessage.NotificationData.Count, Is.GreaterThan(0));

            SetMonitoringModeResponse smResp = await Session.SetMonitoringModeAsync(
                null, m_subscriptionId, MonitoringMode.Sampling,
                new uint[] { monitoredItemId }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(smResp.Results[0]), Is.True);

            await Task.Delay(500).ConfigureAwait(false);
            PublishResponse pub2 = await PublishAndAckAsync().ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(pub2.ResponseHeader.ServiceResult), Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "028")]
        public async Task SetMonitoringModeReportingToReportingAsync()
        {
            NodeId nodeId = VariableIds.Server_ServerStatus_CurrentTime;

            CreateMonitoredItemsResponse createResp = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 1, samplingInterval: 100,
                    mode: MonitoringMode.Reporting)).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(createResp.Results[0].StatusCode), Is.True);
            uint monitoredItemId = createResp.Results[0].MonitoredItemId;

            await Task.Delay(500).ConfigureAwait(false);
            PublishResponse pub1 = await PublishAndAckAsync().ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(pub1.ResponseHeader.ServiceResult), Is.True);
            Assert.That(pub1.NotificationMessage.NotificationData.Count, Is.GreaterThan(0));

            SetMonitoringModeResponse smResp = await Session.SetMonitoringModeAsync(
                null, m_subscriptionId, MonitoringMode.Reporting,
                new uint[] { monitoredItemId }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(smResp.Results[0]), Is.True);

            await Task.Delay(500).ConfigureAwait(false);
            PublishResponse pub2 = await PublishAndAckAsync().ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(pub2.ResponseHeader.ServiceResult), Is.True);
            Assert.That(pub2.NotificationMessage.NotificationData.Count,
                Is.GreaterThan(0), "Expected data after re-setting Reporting mode");
        }

        [Test]
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "034")]
        public async Task CreateMonitoredItemsForAllAttributesAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            uint[] attributeIds =
            [
                Attributes.NodeId,
                Attributes.NodeClass,
                Attributes.BrowseName,
                Attributes.DisplayName,
                Attributes.Description,
                Attributes.WriteMask,
                Attributes.UserWriteMask,
                Attributes.Value,
                Attributes.DataType,
                Attributes.ValueRank,
                Attributes.AccessLevel,
                Attributes.UserAccessLevel,
                Attributes.Historizing
            ];

            var items = new List<MonitoredItemCreateRequest>();
            for (int i = 0; i < attributeIds.Length; i++)
            {
                items.Add(CreateItemRequest(nodeId, (uint)(200 + i),
                    attributeId: attributeIds[i]));
            }

            CreateMonitoredItemsResponse resp = await Session.CreateMonitoredItemsAsync(
                null, m_subscriptionId, TimestampsToReturn.Both,
                items.ToArray().ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(resp.Results.Count, Is.EqualTo(attributeIds.Length));
            int goodCount = resp.Results.ToArray()
                .Count(r => StatusCode.IsGood(r.StatusCode));
            Assert.That(goodCount, Is.GreaterThan(0),
                "At least some attribute monitors should succeed");

            await Task.Delay(500).ConfigureAwait(false);
            PublishResponse pubResp = await PublishAndAckAsync().ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(pubResp.ResponseHeader.ServiceResult), Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "036")]
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
                    Is.True, $"Encoding '{testCases[i].Name}': unexpected status {sc}");

                if (StatusCode.IsGood(sc))
                {
                    await DeleteMonitoredItemAsync(
                        resp.Results[0].MonitoredItemId).ConfigureAwait(false);
                }
            }
        }

        [Test]
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "037")]
        public async Task CreateMonitoredItemsDisabledModeServerTimestampDuplicateAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse response = await Session.CreateMonitoredItemsAsync(
                null, m_subscriptionId, TimestampsToReturn.Server,
                new MonitoredItemCreateRequest[]
                {
                    CreateItemRequest(nodeId, 1, queueSize: 1,
                        mode: MonitoringMode.Disabled)
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(response.ResponseHeader.ServiceResult), Is.True);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "038")]
        public async Task CreateItemSamplingIntervalZeroVerifyRevisedAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse resp = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 1, samplingInterval: 0,
                    mode: MonitoringMode.Reporting)).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(resp.Results[0].StatusCode), Is.True);
            Assert.That(resp.Results[0].RevisedSamplingInterval,
                Is.GreaterThanOrEqualTo(0.0),
                "Server should revise sampling interval of 0");
        }

        [Test]
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "039")]
        public async Task CreateMonitoredItemsMultiDimensionalArrayAsync()
        {
            var items = new List<MonitoredItemCreateRequest>();
            int count = Constants.ScalarStaticArrayNodes.Length;

            for (int i = 0; i < count; i++)
            {
                NodeId nodeId = ToNodeId(Constants.ScalarStaticArrayNodes[i]);
                items.Add(CreateItemRequest(nodeId, (uint)(300 + i),
                    samplingInterval: 100));
            }

            CreateMonitoredItemsResponse createResp =
                await Session.CreateMonitoredItemsAsync(
                    null, m_subscriptionId, TimestampsToReturn.Both,
                    items.ToArray().ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

            Assert.That(createResp.Results.Count, Is.EqualTo(count));
            int goodCount = createResp.Results.ToArray()
                .Count(r => StatusCode.IsGood(r.StatusCode));
            Assert.That(goodCount, Is.GreaterThan(0),
                "At least some array node monitors should succeed");

            await Task.Delay(500).ConfigureAwait(false);
            PublishResponse pubResp = await PublishAndAckAsync().ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(pubResp.ResponseHeader.ServiceResult), Is.True);

            if (pubResp.NotificationMessage.NotificationData.Count > 0)
            {
                var dcn = ExtensionObject.ToEncodeable(
                    pubResp.NotificationMessage.NotificationData[0]) as
                    DataChangeNotification;
                Assert.That(dcn, Is.Not.Null);
                Assert.That(dcn.MonitoredItems.Count, Is.GreaterThan(0),
                    "Should receive initial values for array monitored items");
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
            MonitoredItemCreateRequest item,
            TimestampsToReturn timestamps = TimestampsToReturn.Both)
        {
            return await Session.CreateMonitoredItemsAsync(
                null,
                m_subscriptionId,
                timestamps,
                new MonitoredItemCreateRequest[] { item }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
        }

        private async Task<PublishResponse> PublishAndAckAsync()
        {
            return await Session.PublishWithTimeoutAsync().ConfigureAwait(false);
        }

        private async Task DeleteMonitoredItemAsync(uint monitoredItemId)
        {
            await Session.DeleteMonitoredItemsAsync(
                null, m_subscriptionId,
                new uint[] { monitoredItemId }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
        }

        private uint m_subscriptionId;
    }
}
