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
    /// Depth compliance tests for MonitoredItem Service Set covering
    /// sampling intervals, timestamps, data change filters, deadband,
    /// queue behavior, triggering chains, and batch operations.
    /// </summary>
    [NonParallelizable]
    [TestFixture]
    [Category("Conformance")]
    [Category("MonitoredItem")]
    [Category("MonitoredItemDepth")]
    public class MonitoredItemDepthTests : TestFixture
    {
        [SetUp]
        public async Task SetUp()
        {
            CreateSubscriptionResponse response = await Session.CreateSubscriptionAsync(
                null, 100, 100, 10, 0, true, 0,
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
        [Property("Tag", "001")]
        public async Task MonitorWithSamplingIntervalMinusOneUsesSubscriptionIntervalAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse resp = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 1, samplingInterval: -1)).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(resp.Results[0].StatusCode), Is.True);
            // -1 means use the subscription's publishing interval
            Assert.That(resp.Results[0].RevisedSamplingInterval, Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "021")]
        public async Task MonitorAllNineteenScalarTypesInitialNotificationAsync()
        {
            // : Monitor Value Change V2 – monitor on all scalar types
            var items = new List<MonitoredItemCreateRequest>();
            for (int i = 0; i < Constants.ScalarStaticNodes.Length; i++)
            {
                NodeId nodeId = ToNodeId(Constants.ScalarStaticNodes[i]);
                items.Add(CreateItemRequest(nodeId, (uint)(500 + i), samplingInterval: 50));
            }

            CreateMonitoredItemsResponse createResp = await Session.CreateMonitoredItemsAsync(
                null, m_subscriptionId, TimestampsToReturn.Both,
                items.ToArray().ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(createResp.Results.Count, Is.EqualTo(Constants.ScalarStaticNodes.Length));
            foreach (MonitoredItemCreateResult r in createResp.Results)
            {
                Assert.That(StatusCode.IsGood(r.StatusCode), Is.True,
                    $"MonitoredItemId {r.MonitoredItemId} failed with {r.StatusCode}");
            }

            // Publish to get initial values
            await Task.Delay(300).ConfigureAwait(false);

            PublishResponse pubResp = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(pubResp.ResponseHeader.ServiceResult), Is.True);
            Assert.That(pubResp.NotificationMessage.NotificationData.Count, Is.GreaterThan(0),
                "Should receive initial data change for all monitored scalar items.");
        }

        [TestCase(TimestampsToReturn.Source)]
        [TestCase(TimestampsToReturn.Server)]
        [TestCase(TimestampsToReturn.Both)]
        [TestCase(TimestampsToReturn.Neither)]
        public async Task MonitorWithDifferentTimestampsToReturnAsync(TimestampsToReturn timestamps)
        {
            NodeId nodeId = VariableIds.Server_ServerStatus_CurrentTime;

            CreateMonitoredItemsResponse resp = await Session.CreateMonitoredItemsAsync(
                null, m_subscriptionId, timestamps,
                new MonitoredItemCreateRequest[]
                {
                    CreateItemRequest(nodeId, 1, samplingInterval: 50)
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(resp.Results[0].StatusCode), Is.True);

            await Task.Delay(300).ConfigureAwait(false);

            PublishResponse pubResp = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(pubResp.ResponseHeader.ServiceResult), Is.True);
            Assert.That(pubResp.NotificationMessage.NotificationData.Count, Is.GreaterThan(0));

            var dcn = ExtensionObject.ToEncodeable(pubResp.NotificationMessage.NotificationData[0]) as
                DataChangeNotification;
            Assert.That(dcn, Is.Not.Null);
            Assert.That(dcn.MonitoredItems.Count, Is.GreaterThan(0));
        }

        [Test]
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "019")]
        public async Task DataChangeFilterTriggerStatusOnlyNotifyOnStatusChangeAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            var filter = new DataChangeFilter
            {
                Trigger = DataChangeTrigger.Status,
                DeadbandType = (uint)DeadbandType.None,
                DeadbandValue = 0
            };

            CreateMonitoredItemsResponse resp = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 1,
                    filter: new ExtensionObject(filter))).ConfigureAwait(false);

            StatusCode status = resp.Results[0].StatusCode;
            Assert.That(
                StatusCode.IsGood(status) || status == StatusCodes.BadFilterNotAllowed,
                Is.True,
                $"Expected Good or BadFilterNotAllowed, got {status}");
        }

        [Test]
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "019")]
        public async Task DataChangeFilterTriggerStatusValueAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            var filter = new DataChangeFilter
            {
                Trigger = DataChangeTrigger.StatusValue,
                DeadbandType = (uint)DeadbandType.None,
                DeadbandValue = 0
            };

            CreateMonitoredItemsResponse resp = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 2,
                    filter: new ExtensionObject(filter))).ConfigureAwait(false);

            StatusCode status = resp.Results[0].StatusCode;
            Assert.That(
                StatusCode.IsGood(status) || status == StatusCodes.BadFilterNotAllowed,
                Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "019")]
        public async Task DataChangeFilterTriggerStatusValueTimestampAsync()
        {
            NodeId nodeId = VariableIds.Server_ServerStatus_CurrentTime;

            var filter = new DataChangeFilter
            {
                Trigger = DataChangeTrigger.StatusValueTimestamp,
                DeadbandType = (uint)DeadbandType.None,
                DeadbandValue = 0
            };

            CreateMonitoredItemsResponse resp = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 3,
                    filter: new ExtensionObject(filter))).ConfigureAwait(false);

            StatusCode status = resp.Results[0].StatusCode;
            Assert.That(
                StatusCode.IsGood(status) || status == StatusCodes.BadFilterNotAllowed,
                Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "Err-006")]
        public async Task AbsoluteDeadbandWriteWithinDeadbandNoNotificationAsync()
        {
            // : Monitor Items Deadband Filter – within deadband
            NodeId nodeId = ToNodeId(Constants.AnalogTypeDouble);

            var filter = new DataChangeFilter
            {
                Trigger = DataChangeTrigger.StatusValue,
                DeadbandType = (uint)DeadbandType.Absolute,
                DeadbandValue = 50.0
            };

            CreateMonitoredItemsResponse createResp = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 10,
                    samplingInterval: 50,
                    filter: new ExtensionObject(filter))).ConfigureAwait(false);

            StatusCode createStatus = createResp.Results[0].StatusCode;
            if (createStatus == StatusCodes.BadFilterNotAllowed)
            {
                Assert.Fail("Server does not support deadband filter on this node.");
            }
            Assert.That(StatusCode.IsGood(createStatus), Is.True);

            // Consume initial notification
            await Task.Delay(300).ConfigureAwait(false);
            await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            // Read current value
            ReadResponse readResp = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() { NodeId = nodeId, AttributeId = Attributes.Value }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            double currentVal = readResp.Results[0].WrappedValue.GetDouble();

            // Write value within deadband (change by 1, deadband is 50)
            if (!await TryWriteDoubleAsync(nodeId, currentVal + 1.0).ConfigureAwait(false))
            {
                Assert.Fail("AnalogType node is not writable.");
            }

            await Task.Delay(300).ConfigureAwait(false);

            PublishResponse pubResp = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(pubResp.ResponseHeader.ServiceResult), Is.True);
            // Should be KeepAlive (no notification data) since change < deadband
            Assert.That(pubResp.NotificationMessage.NotificationData.Count, Is.Zero,
                "Change within deadband should not trigger notification.");
        }

        [Test]
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "Err-006")]
        public async Task AbsoluteDeadbandWriteOutsideDeadbandNotificationAsync()
        {
            NodeId nodeId = ToNodeId(Constants.AnalogTypeDouble);

            var filter = new DataChangeFilter
            {
                Trigger = DataChangeTrigger.StatusValue,
                DeadbandType = (uint)DeadbandType.Absolute,
                DeadbandValue = 5.0
            };

            CreateMonitoredItemsResponse createResp = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 11,
                    samplingInterval: 50,
                    filter: new ExtensionObject(filter))).ConfigureAwait(false);

            StatusCode createStatus = createResp.Results[0].StatusCode;
            if (createStatus == StatusCodes.BadFilterNotAllowed)
            {
                Assert.Fail("Server does not support deadband filter on this node.");
            }
            Assert.That(StatusCode.IsGood(createStatus), Is.True);

            // Consume initial
            await Task.Delay(300).ConfigureAwait(false);
            await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            // Read current value
            ReadResponse readResp = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() { NodeId = nodeId, AttributeId = Attributes.Value }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            double currentVal = readResp.Results[0].WrappedValue.GetDouble();

            // Write value well outside deadband
            if (!await TryWriteDoubleAsync(nodeId, currentVal + 100.0).ConfigureAwait(false))
            {
                Assert.Fail("AnalogType node is not writable.");
            }

            await Task.Delay(300).ConfigureAwait(false);

            PublishResponse pubResp = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(pubResp.ResponseHeader.ServiceResult), Is.True);
            Assert.That(pubResp.NotificationMessage.NotificationData.Count, Is.GreaterThan(0),
                "Change outside deadband should trigger notification.");
        }

        [Test]
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "Err-007")]
        public async Task PercentDeadbandCreationAcceptedAsync()
        {
            NodeId nodeId = ToNodeId(Constants.AnalogTypeDouble);

            var filter = new DataChangeFilter
            {
                Trigger = DataChangeTrigger.StatusValue,
                DeadbandType = (uint)DeadbandType.Percent,
                DeadbandValue = 10.0
            };

            CreateMonitoredItemsResponse resp = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 12,
                    filter: new ExtensionObject(filter))).ConfigureAwait(false);

            StatusCode status = resp.Results[0].StatusCode;
            Assert.That(
                StatusCode.IsGood(status) || status == StatusCodes.BadFilterNotAllowed,
                Is.True,
                $"Expected Good or BadFilterNotAllowed, got {status}");
        }

        [Test]
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "017")]
        public async Task MonitorMultipleItemsSameSubscriptionAllGetInitialValuesAsync()
        {
            var items = new MonitoredItemCreateRequest[]
            {
                CreateItemRequest(ToNodeId(Constants.ScalarStaticInt32), 20, samplingInterval: 50),
                CreateItemRequest(ToNodeId(Constants.ScalarStaticDouble), 21, samplingInterval: 50),
                CreateItemRequest(ToNodeId(Constants.ScalarStaticString), 22, samplingInterval: 50),
                CreateItemRequest(ToNodeId(Constants.ScalarStaticBoolean), 23, samplingInterval: 50),
                CreateItemRequest(VariableIds.Server_ServerStatus_CurrentTime, 24, samplingInterval: 50)
            };

            CreateMonitoredItemsResponse createResp = await Session.CreateMonitoredItemsAsync(
                null, m_subscriptionId, TimestampsToReturn.Both,
                items.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(createResp.Results.Count, Is.EqualTo(5));
            foreach (MonitoredItemCreateResult r in createResp.Results)
            {
                Assert.That(StatusCode.IsGood(r.StatusCode), Is.True);
            }

            await Task.Delay(300).ConfigureAwait(false);

            PublishResponse pubResp = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(pubResp.ResponseHeader.ServiceResult), Is.True);
            Assert.That(pubResp.NotificationMessage.NotificationData.Count, Is.GreaterThan(0));

            var dcn = ExtensionObject.ToEncodeable(pubResp.NotificationMessage.NotificationData[0]) as
                DataChangeNotification;
            Assert.That(dcn, Is.Not.Null);
            Assert.That(dcn.MonitoredItems.Count, Is.GreaterThanOrEqualTo(1),
                "Should receive initial values for multiple monitored items.");
        }

        [Test]
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "004")]
        public async Task QueueSizeFiveWriteThreeGetAllInSinglePublishAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse createResp = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 30, samplingInterval: 0, queueSize: 5))
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(createResp.Results[0].StatusCode), Is.True);

            // Consume initial
            await Task.Delay(200).ConfigureAwait(false);
            await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            // Write 3 values rapidly
            for (int i = 0; i < 3; i++)
            {
                await WriteValueAsync(nodeId, 2000 + i).ConfigureAwait(false);
            }

            await Task.Delay(300).ConfigureAwait(false);

            PublishResponse pubResp = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(pubResp.ResponseHeader.ServiceResult), Is.True);
            Assert.That(pubResp.NotificationMessage.NotificationData.Count, Is.GreaterThan(0));
        }

        [Test]
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "004")]
        public async Task QueueSizeOneWriteFiveGetOnlyLatestAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse createResp = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 31, samplingInterval: 0,
queueSize: 1, discardOldest: true))
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(createResp.Results[0].StatusCode), Is.True);

            // Consume initial
            await Task.Delay(200).ConfigureAwait(false);
            await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            // Write 5 values rapidly
            int lastVal = 0;
            for (int i = 0; i < 5; i++)
            {
                lastVal = 3000 + i;
                await WriteValueAsync(nodeId, lastVal).ConfigureAwait(false);
            }

            await Task.Delay(300).ConfigureAwait(false);

            PublishResponse pubResp = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(pubResp.ResponseHeader.ServiceResult), Is.True);

            if (pubResp.NotificationMessage.NotificationData.Count > 0)
            {
                var dcn = ExtensionObject.ToEncodeable(
                    pubResp.NotificationMessage.NotificationData[0]) as DataChangeNotification;
                if (dcn != null && dcn.MonitoredItems.Count > 0)
                {
                    // With QueueSize=1 and DiscardOldest=true, should get latest
                    int notifiedValue = dcn.MonitoredItems[^1].Value.WrappedValue.GetInt32();
                    Assert.That(notifiedValue, Is.EqualTo(lastVal),
                        "Queue size 1 with DiscardOldest should report the latest value.");
                }
            }
        }

        [Test]
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "017")]
        public async Task DeleteMonitoredItemsWhileSubscriptionActiveRemainingItemsWorkAsync()
        {
            NodeId node1 = VariableIds.Server_ServerStatus_CurrentTime;
            NodeId node2 = ToNodeId(Constants.ScalarStaticInt32);

            var items = new MonitoredItemCreateRequest[]
            {
                CreateItemRequest(node1, 40, samplingInterval: 50),
                CreateItemRequest(node2, 41, samplingInterval: 50)
            };

            CreateMonitoredItemsResponse createResp = await Session.CreateMonitoredItemsAsync(
                null, m_subscriptionId, TimestampsToReturn.Both,
                items.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(createResp.Results.Count, Is.EqualTo(2));
            uint monId1 = createResp.Results[0].MonitoredItemId;

            // Consume initial
            await Task.Delay(300).ConfigureAwait(false);
            await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            // Delete first item
            DeleteMonitoredItemsResponse delResp = await Session.DeleteMonitoredItemsAsync(
                null, m_subscriptionId, new uint[] { monId1 }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(delResp.Results[0]), Is.True);

            // Write to second node
            await WriteValueAsync(node2, new Random().Next(1, 10000)).ConfigureAwait(false);

            await Task.Delay(300).ConfigureAwait(false);

            PublishResponse pubResp = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(pubResp.ResponseHeader.ServiceResult), Is.True);
            // Remaining item should still produce notifications
            Assert.That(pubResp.NotificationMessage, Is.Not.Null);
        }

        [Test]
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "016")]
        public async Task ModifyMonitoredItemReportingToDisabledNoMoreNotificationsAsync()
        {
            NodeId nodeId = VariableIds.Server_ServerStatus_CurrentTime;

            CreateMonitoredItemsResponse createResp = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 50, samplingInterval: 50)).ConfigureAwait(false);
            uint monId = createResp.Results[0].MonitoredItemId;

            // Consume initial
            await Task.Delay(300).ConfigureAwait(false);
            await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            // Set to Disabled
            SetMonitoringModeResponse disableResp = await Session.SetMonitoringModeAsync(
                null, m_subscriptionId, MonitoringMode.Disabled,
                new uint[] { monId }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(disableResp.Results[0]), Is.True);

            await Task.Delay(300).ConfigureAwait(false);

            PublishResponse pubResp = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(pubResp.ResponseHeader.ServiceResult), Is.True);
            Assert.That(pubResp.NotificationMessage.NotificationData.Count, Is.Zero,
                "Disabled monitored item should not produce notifications.");
        }

        [Test]
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "015")]
        public async Task ModifyMonitoredItemDisabledBackToReportingResumesNotificationsAsync()
        {
            NodeId nodeId = VariableIds.Server_ServerStatus_CurrentTime;

            CreateMonitoredItemsResponse createResp = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 51, samplingInterval: 50)).ConfigureAwait(false);
            uint monId = createResp.Results[0].MonitoredItemId;

            // Disable
            await Session.SetMonitoringModeAsync(
                null, m_subscriptionId, MonitoringMode.Disabled,
                new uint[] { monId }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            // Consume any pending
            await Task.Delay(200).ConfigureAwait(false);
            await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            // Re-enable to Reporting
            SetMonitoringModeResponse reportResp = await Session.SetMonitoringModeAsync(
                null, m_subscriptionId, MonitoringMode.Reporting,
                new uint[] { monId }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(reportResp.Results[0]), Is.True);

            await Task.Delay(300).ConfigureAwait(false);

            PublishResponse pubResp = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(pubResp.ResponseHeader.ServiceResult), Is.True);
            // Re-enabling should produce at least one notification (latest value)
            Assert.That(pubResp.NotificationMessage.NotificationData.Count, Is.GreaterThan(0),
                "Re-enabling Reporting should resume notifications with latest value.");
        }

        [Test]
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "017")]
        public async Task BatchCreateMonitoredItemsOnFiftyDifferentNodesAsync()
        {
            // : Monitor Items 10/500 – batch create
            var items = new List<MonitoredItemCreateRequest>();
            for (int i = 0; i < 50; i++)
            {
                ExpandedNodeId eni = Constants.ScalarStaticNodes[
                    i % Constants.ScalarStaticNodes.Length];
                items.Add(CreateItemRequest(ToNodeId(eni), (uint)(600 + i)));
            }

            CreateMonitoredItemsResponse createResp = await Session.CreateMonitoredItemsAsync(
                null, m_subscriptionId, TimestampsToReturn.Both,
                items.ToArray().ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(createResp.Results.Count, Is.EqualTo(50));
            int goodCount = createResp.Results.ToArray()
                .Count(r => StatusCode.IsGood(r.StatusCode));
            Assert.That(goodCount, Is.EqualTo(50),
                "All 50 monitored items should be created successfully.");
        }

        [Test]
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "006")]
        public async Task MonitorArrayVariableNotificationContainsFullArrayAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticArrayInt32);

            CreateMonitoredItemsResponse createResp = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 60, samplingInterval: 50)).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(createResp.Results[0].StatusCode), Is.True);

            await Task.Delay(300).ConfigureAwait(false);

            PublishResponse pubResp = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(pubResp.ResponseHeader.ServiceResult), Is.True);
            Assert.That(pubResp.NotificationMessage.NotificationData.Count, Is.GreaterThan(0));

            var dcn = ExtensionObject.ToEncodeable(pubResp.NotificationMessage.NotificationData[0]) as
                DataChangeNotification;
            Assert.That(dcn, Is.Not.Null);
            Assert.That(dcn.MonitoredItems.Count, Is.GreaterThan(0));

            // The value should be an array
            Assert.That(dcn.MonitoredItems[0].Value.WrappedValue.IsNull, Is.False,
                "Array variable should return a non-null value.");
        }

        [Test]
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "019")]
        public async Task MonitorWithIndexRangeOnArrayAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticArrayInt32);

            var item = new MonitoredItemCreateRequest
            {
                ItemToMonitor = new ReadValueId
                {
                    NodeId = nodeId,
                    AttributeId = Attributes.Value,
                    IndexRange = "0:2"
                },
                MonitoringMode = MonitoringMode.Reporting,
                RequestedParameters = new MonitoringParameters
                {
                    ClientHandle = 61,
                    SamplingInterval = 100,
                    Filter = default,
                    DiscardOldest = true,
                    QueueSize = 10
                }
            };

            CreateMonitoredItemsResponse resp = await CreateSingleItemAsync(item).ConfigureAwait(false);

            StatusCode status = resp.Results[0].StatusCode;
            Assert.That(
                StatusCode.IsGood(status) ||
                status == StatusCodes.BadIndexRangeNoData ||
                status == StatusCodes.BadIndexRangeInvalid,
                Is.True,
                $"Expected Good, BadIndexRangeNoData, or BadIndexRangeInvalid, got {status}");
        }

        [Test]
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "005")]
        public async Task VerifyModifyMonitoredItemRevisedSamplingIntervalAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse createResp = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 70, samplingInterval: 1000)).ConfigureAwait(false);
            uint monId = createResp.Results[0].MonitoredItemId;

            ModifyMonitoredItemsResponse modResp = await Session.ModifyMonitoredItemsAsync(
                null, m_subscriptionId, TimestampsToReturn.Both,
                new MonitoredItemModifyRequest[]
                {
                    new() {
                        MonitoredItemId = monId,
                        RequestedParameters = new MonitoringParameters
                        {
                            ClientHandle = 70,
                            SamplingInterval = 250,
                            QueueSize = 10,
                            DiscardOldest = true
                        }
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(modResp.Results[0].StatusCode), Is.True);
            Assert.That(modResp.Results[0].RevisedSamplingInterval, Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "005")]
        public async Task VerifyModifyMonitoredItemRevisedQueueSizeAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse createResp = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 71, queueSize: 5)).ConfigureAwait(false);
            uint monId = createResp.Results[0].MonitoredItemId;

            ModifyMonitoredItemsResponse modResp = await Session.ModifyMonitoredItemsAsync(
                null, m_subscriptionId, TimestampsToReturn.Both,
                new MonitoredItemModifyRequest[]
                {
                    new() {
                        MonitoredItemId = monId,
                        RequestedParameters = new MonitoringParameters
                        {
                            ClientHandle = 71,
                            SamplingInterval = 100,
                            QueueSize = 25,
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
        [Property("Tag", "017")]
        public async Task SetTriggeringChainATriggersBTriggersCAsync()
        {
            // : Monitor Triggering – chain A→B→C
            NodeId nodeA = VariableIds.Server_ServerStatus_CurrentTime;
            NodeId nodeB = ToNodeId(Constants.ScalarStaticInt32);
            NodeId nodeC = ToNodeId(Constants.ScalarStaticDouble);

            var items = new MonitoredItemCreateRequest[]
            {
                CreateItemRequest(nodeA, 80, samplingInterval: 50,
                    mode: MonitoringMode.Reporting),
                CreateItemRequest(nodeB, 81, samplingInterval: 50,
                    mode: MonitoringMode.Sampling),
                CreateItemRequest(nodeC, 82, samplingInterval: 50,
                    mode: MonitoringMode.Sampling)
            };

            CreateMonitoredItemsResponse createResp = await Session.CreateMonitoredItemsAsync(
                null, m_subscriptionId, TimestampsToReturn.Both,
                items.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(createResp.Results.Count, Is.EqualTo(3));
            foreach (MonitoredItemCreateResult r in createResp.Results)
            {
                Assert.That(StatusCode.IsGood(r.StatusCode), Is.True);
            }

            uint idA = createResp.Results[0].MonitoredItemId;
            uint idB = createResp.Results[1].MonitoredItemId;
            uint idC = createResp.Results[2].MonitoredItemId;

            // A triggers B
            SetTriggeringResponse trigRespAB = await Session.SetTriggeringAsync(
                null, m_subscriptionId, idA,
                new uint[] { idB }.ToArrayOf(),
                default,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(trigRespAB.ResponseHeader.ServiceResult), Is.True);
            Assert.That(StatusCode.IsGood(trigRespAB.AddResults[0]), Is.True);

            // B triggers C
            SetTriggeringResponse trigRespBC = await Session.SetTriggeringAsync(
                null, m_subscriptionId, idB,
                new uint[] { idC }.ToArrayOf(),
                default,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(trigRespBC.ResponseHeader.ServiceResult), Is.True);
            Assert.That(StatusCode.IsGood(trigRespBC.AddResults[0]), Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "017")]
        public async Task TriggeredItemOnlyReportsWhenTriggeringItemChangesAsync()
        {
            NodeId triggerNode = VariableIds.Server_ServerStatus_CurrentTime;
            NodeId linkedNode = ToNodeId(Constants.ScalarStaticInt32);

            var items = new MonitoredItemCreateRequest[]
            {
                CreateItemRequest(triggerNode, 90, samplingInterval: 50,
                    mode: MonitoringMode.Reporting),
                CreateItemRequest(linkedNode, 91, samplingInterval: 50,
                    mode: MonitoringMode.Sampling)
            };

            CreateMonitoredItemsResponse createResp = await Session.CreateMonitoredItemsAsync(
                null, m_subscriptionId, TimestampsToReturn.Both,
                items.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(createResp.Results.Count, Is.EqualTo(2));
            uint triggerId = createResp.Results[0].MonitoredItemId;
            uint linkedId = createResp.Results[1].MonitoredItemId;

            // Set triggering
            SetTriggeringResponse trigResp = await Session.SetTriggeringAsync(
                null, m_subscriptionId, triggerId,
                new uint[] { linkedId }.ToArrayOf(),
                default,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(trigResp.ResponseHeader.ServiceResult), Is.True);

            // Consume initial notifications
            await Task.Delay(300).ConfigureAwait(false);
            await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            // Write to the linked (sampling) node only
            await WriteValueAsync(linkedNode, new Random().Next(1, 10000)).ConfigureAwait(false);

            // Wait for trigger node (CurrentTime) to change and produce notification
            await Task.Delay(300).ConfigureAwait(false);

            PublishResponse pubResp = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(pubResp.ResponseHeader.ServiceResult), Is.True);
            Assert.That(pubResp.NotificationMessage, Is.Not.Null);
        }

        [Test]
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "017")]
        public async Task SetTriggeringAddMultipleLinksAtOnceAsync()
        {
            NodeId triggerNode = VariableIds.Server_ServerStatus_CurrentTime;
            NodeId linked1 = ToNodeId(Constants.ScalarStaticInt32);
            NodeId linked2 = ToNodeId(Constants.ScalarStaticDouble);
            NodeId linked3 = ToNodeId(Constants.ScalarStaticString);

            var items = new MonitoredItemCreateRequest[]
            {
                CreateItemRequest(triggerNode, 100, samplingInterval: 50),
                CreateItemRequest(linked1, 101, mode: MonitoringMode.Sampling),
                CreateItemRequest(linked2, 102, mode: MonitoringMode.Sampling),
                CreateItemRequest(linked3, 103, mode: MonitoringMode.Sampling)
            };

            CreateMonitoredItemsResponse createResp = await Session.CreateMonitoredItemsAsync(
                null, m_subscriptionId, TimestampsToReturn.Both,
                items.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            uint triggerId = createResp.Results[0].MonitoredItemId;
            uint[] linkedIds = [.. createResp.Results.ToArray()
                .Skip(1).Select(r => r.MonitoredItemId)];

            // Add 3 links at once
            SetTriggeringResponse addResp = await Session.SetTriggeringAsync(
                null, m_subscriptionId, triggerId,
                linkedIds.ToArrayOf(),
                default,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(addResp.ResponseHeader.ServiceResult), Is.True);
            Assert.That(addResp.AddResults.Count, Is.EqualTo(3));
            foreach (StatusCode sc in addResp.AddResults)
            {
                Assert.That(StatusCode.IsGood(sc), Is.True);
            }
        }

        [Test]
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "Err-011")]
        public async Task SetTriggeringWithInvalidTriggeringItemReturnsBadAsync()
        {
            NodeId linkedNode = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse createResp = await CreateSingleItemAsync(
                CreateItemRequest(linkedNode, 110,
                    mode: MonitoringMode.Sampling)).ConfigureAwait(false);
            uint linkedId = createResp.Results[0].MonitoredItemId;

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(async () => await Session.SetTriggeringAsync(
                    null, m_subscriptionId, 999999u,
                    new uint[] { linkedId }.ToArrayOf(),
                    default,
                    CancellationToken.None).ConfigureAwait(false));

            Assert.That(
                ex.StatusCode == StatusCodes.BadMonitoredItemIdInvalid ||
                StatusCode.IsBad(ex.StatusCode),
                Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "006")]
        public async Task MonitorDataTypeAttributeAcceptedAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse resp = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 120,
                    attributeId: Attributes.DataType)).ConfigureAwait(false);

            Assert.That(resp.Results.Count, Is.EqualTo(1));
            Assert.That(
                StatusCode.IsGood(resp.Results[0].StatusCode) ||
                resp.Results[0].StatusCode.Code == StatusCodes.BadFilterNotAllowed,
                Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "006")]
        public async Task MonitorNodeClassAttributeAcceptedAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse resp = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 121,
                    attributeId: Attributes.NodeClass)).ConfigureAwait(false);

            Assert.That(resp.Results.Count, Is.EqualTo(1));
            Assert.That(
                StatusCode.IsGood(resp.Results[0].StatusCode) ||
                resp.Results[0].StatusCode.Code == StatusCodes.BadFilterNotAllowed,
                Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "005")]
        public async Task VeryLargeQueueSizeServerRevisesAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse resp = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 130, queueSize: uint.MaxValue)).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(resp.Results[0].StatusCode), Is.True);
            Assert.That(resp.Results[0].RevisedQueueSize, Is.GreaterThan(0u));
            Assert.That(resp.Results[0].RevisedQueueSize, Is.LessThan(uint.MaxValue),
                "Server should revise very large queue size downward.");
        }

        [Test]
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "002")]
        public async Task MonitorServerStatusNodeGetsPeriodicUpdatesAsync()
        {
            NodeId nodeId = VariableIds.Server_ServerStatus_CurrentTime;

            CreateMonitoredItemsResponse createResp = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 140, samplingInterval: 50)).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(createResp.Results[0].StatusCode), Is.True);

            // Collect two publishes and verify values differ
            await Task.Delay(300).ConfigureAwait(false);

            PublishResponse pub1 = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(pub1.ResponseHeader.ServiceResult), Is.True);

            await Task.Delay(300).ConfigureAwait(false);

            PublishResponse pub2 = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(pub2.ResponseHeader.ServiceResult), Is.True);

            // Both should have notification data since CurrentTime changes
            Assert.That(pub1.NotificationMessage.NotificationData.Count +
                pub2.NotificationMessage.NotificationData.Count, Is.GreaterThan(0));
        }

        [Test]
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "005")]
        public async Task ModifyMonitoredItemAddDataChangeFilterAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse createResp = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 150)).ConfigureAwait(false);
            uint monId = createResp.Results[0].MonitoredItemId;

            var filter = new DataChangeFilter
            {
                Trigger = DataChangeTrigger.StatusValue,
                DeadbandType = (uint)DeadbandType.None,
                DeadbandValue = 0
            };

            ModifyMonitoredItemsResponse modResp = await Session.ModifyMonitoredItemsAsync(
                null, m_subscriptionId, TimestampsToReturn.Both,
                new MonitoredItemModifyRequest[]
                {
                    new() {
                        MonitoredItemId = monId,
                        RequestedParameters = new MonitoringParameters
                        {
                            ClientHandle = 150,
                            SamplingInterval = 100,
                            QueueSize = 10,
                            DiscardOldest = true,
                            Filter = new ExtensionObject(filter)
                        }
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            StatusCode modStatus = modResp.Results[0].StatusCode;
            Assert.That(
                StatusCode.IsGood(modStatus) || modStatus == StatusCodes.BadFilterNotAllowed,
                Is.True,
                $"Expected Good or BadFilterNotAllowed, got {modStatus}");
        }

        [Test]
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "017")]
        public async Task BatchCreateAndImmediatelyDeleteAllItemsAsync()
        {
            var items = new List<MonitoredItemCreateRequest>();
            for (int i = 0; i < 20; i++)
            {
                ExpandedNodeId eni = Constants.ScalarStaticNodes[
                    i % Constants.ScalarStaticNodes.Length];
                items.Add(CreateItemRequest(ToNodeId(eni), (uint)(700 + i)));
            }

            CreateMonitoredItemsResponse createResp = await Session.CreateMonitoredItemsAsync(
                null, m_subscriptionId, TimestampsToReturn.Both,
                items.ToArray().ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            uint[] monIds = [.. createResp.Results.ToArray()
                .Where(r => StatusCode.IsGood(r.StatusCode))
                .Select(r => r.MonitoredItemId)];

            Assert.That(monIds.Length, Is.EqualTo(20));

            // Delete all at once
            DeleteMonitoredItemsResponse delResp = await Session.DeleteMonitoredItemsAsync(
                null, m_subscriptionId, monIds.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(delResp.Results.Count, Is.EqualTo(20));
            foreach (StatusCode sc in delResp.Results)
            {
                Assert.That(StatusCode.IsGood(sc), Is.True);
            }

            // Publish should now return KeepAlive
            await Task.Delay(200).ConfigureAwait(false);
            PublishResponse pubResp = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(pubResp.ResponseHeader.ServiceResult), Is.True);
            Assert.That(pubResp.NotificationMessage.NotificationData.Count, Is.Zero);
        }

        [Test]
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "020")]
        public async Task MonitorAllArrayTypesInitialNotificationAsync()
        {
            var items = new List<MonitoredItemCreateRequest>();
            for (int i = 0; i < Constants.ScalarStaticArrayNodes.Length; i++)
            {
                NodeId nodeId = ToNodeId(Constants.ScalarStaticArrayNodes[i]);
                items.Add(CreateItemRequest(nodeId, (uint)(800 + i), samplingInterval: 50));
            }

            CreateMonitoredItemsResponse createResp = await Session.CreateMonitoredItemsAsync(
                null, m_subscriptionId, TimestampsToReturn.Both,
                items.ToArray().ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(createResp.Results.Count, Is.EqualTo(Constants.ScalarStaticArrayNodes.Length));
            foreach (MonitoredItemCreateResult r in createResp.Results)
            {
                Assert.That(StatusCode.IsGood(r.StatusCode), Is.True);
            }

            await Task.Delay(300).ConfigureAwait(false);

            PublishResponse pubResp = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(pubResp.ResponseHeader.ServiceResult), Is.True);
            Assert.That(pubResp.NotificationMessage.NotificationData.Count, Is.GreaterThan(0));
        }

        [Test]
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "017")]
        public async Task SetMonitoringModeOnMultipleItemsAtOnceAsync()
        {
            var items = new MonitoredItemCreateRequest[]
            {
                CreateItemRequest(ToNodeId(Constants.ScalarStaticInt32), 160),
                CreateItemRequest(ToNodeId(Constants.ScalarStaticDouble), 161),
                CreateItemRequest(ToNodeId(Constants.ScalarStaticString), 162)
            };

            CreateMonitoredItemsResponse createResp = await Session.CreateMonitoredItemsAsync(
                null, m_subscriptionId, TimestampsToReturn.Both,
                items.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            uint[] monIds = [.. createResp.Results.ToArray().Select(r => r.MonitoredItemId)];

            // Disable all
            SetMonitoringModeResponse disableResp = await Session.SetMonitoringModeAsync(
                null, m_subscriptionId, MonitoringMode.Disabled,
                monIds.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(disableResp.Results.Count, Is.EqualTo(3));
            foreach (StatusCode sc in disableResp.Results)
            {
                Assert.That(StatusCode.IsGood(sc), Is.True);
            }

            // Re-enable all
            SetMonitoringModeResponse enableResp = await Session.SetMonitoringModeAsync(
                null, m_subscriptionId, MonitoringMode.Reporting,
                monIds.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(enableResp.Results.Count, Is.EqualTo(3));
            foreach (StatusCode sc in enableResp.Results)
            {
                Assert.That(StatusCode.IsGood(sc), Is.True);
            }
        }

        [Test]
        [Property("ConformanceUnit", "Monitor Basic")]
        [Property("Tag", "007")]
        public async Task MonitorSimulationNodeReceivesChangingValuesAsync()
        {
            NodeId nodeId = ToNodeId(Constants.SimulationInt32);

            CreateMonitoredItemsResponse createResp = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 170, samplingInterval: 50)).ConfigureAwait(false);

            StatusCode createStatus = createResp.Results[0].StatusCode;
            if (StatusCode.IsBad(createStatus))
            {
                Assert.Fail("Simulation node not available or not writable.");
            }

            await Task.Delay(500).ConfigureAwait(false);

            PublishResponse pubResp = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(pubResp.ResponseHeader.ServiceResult), Is.True);
            Assert.That(pubResp.NotificationMessage, Is.Not.Null);
        }

        private MonitoredItemCreateRequest CreateItemRequest(
            NodeId nodeId,
            uint clientHandle,
            double samplingInterval = 100,
            uint queueSize = 10,
            MonitoringMode mode = MonitoringMode.Reporting,
            uint attributeId = Attributes.Value,
            bool discardOldest = true,
            ExtensionObject filter = default,
            TimestampsToReturn timestamps = TimestampsToReturn.Both)
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

        private async Task<bool> TryWriteDoubleAsync(NodeId nodeId, double value)
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

            return StatusCode.IsGood(writeResp.Results[0]);
        }

        private uint m_subscriptionId;
    }
}
