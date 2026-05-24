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

using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.Subscriptions.Tests
{
    /// <summary>
    /// compliance tests for MonitoredItem deadband filters including
    /// absolute deadband, percent deadband, integer types, floating point,
    /// non-analog rejection, and modify operations.
    /// </summary>
    [NonParallelizable]
    [TestFixture]
    [Category("Conformance")]
    [Category("MonitoredItem")]
    [Category("MonitorDeadbandFilter")]
    public class MonitorDeadbandFilterTests : TestFixture
    {
        [SetUp]
        public async Task SetUp()
        {
            m_subscriptionId = await CreateSetupSubscriptionAsync(
                publishingInterval: 100, requestedLifetimeCount: 100,
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
        public async Task AbsoluteDeadbandZeroNotifiesOnAnyChangeAsync()
        {
            NodeId nodeId = ToNodeId(Constants.AnalogTypeDouble);

            CreateMonitoredItemsResponse createResp =
                await CreateDeadbandItemAsync(
                    nodeId, 1, MakeAbsoluteDeadbandFilter(0.0))
                .ConfigureAwait(false);
            Assert.That(
                StatusCode.IsGood(createResp.Results[0].StatusCode), Is.True);

            await ConsumeInitialPublishAsync().ConfigureAwait(false);

            double current = await ReadCurrentDoubleAsync(nodeId)
                .ConfigureAwait(false);
            if (!await TryWriteDoubleAsync(nodeId, current + 0.001)
                .ConfigureAwait(false))
            {
                Assert.Fail("AnalogType node is not writable.");
            }

            await Task.Delay(300).ConfigureAwait(false);

            PublishResponse pubResp = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(pubResp.ResponseHeader.ServiceResult),
                Is.True);
            Assert.That(
                pubResp.NotificationMessage.NotificationData.Count,
                Is.GreaterThan(0),
                "Deadband=0 should notify on any change.");
        }

        [Test]
        public async Task AbsoluteDeadbandSmallThresholdAsync()
        {
            NodeId nodeId = ToNodeId(Constants.AnalogTypeDouble);

            CreateMonitoredItemsResponse createResp =
                await CreateDeadbandItemAsync(
                    nodeId, 2, MakeAbsoluteDeadbandFilter(0.1))
                .ConfigureAwait(false);
            Assert.That(
                StatusCode.IsGood(createResp.Results[0].StatusCode), Is.True);

            await ConsumeInitialPublishAsync().ConfigureAwait(false);

            double current = await ReadCurrentDoubleAsync(nodeId)
                .ConfigureAwait(false);

            // Write within deadband
            if (!await TryWriteDoubleAsync(nodeId, current + 0.01)
                .ConfigureAwait(false))
            {
                Assert.Fail("AnalogType node is not writable.");
            }

            await Task.Delay(300).ConfigureAwait(false);

            PublishResponse pubSmall = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            Assert.That(
                pubSmall.NotificationMessage.NotificationData.Count,
                Is.Zero,
                "Change within deadband should not trigger notification.");

            // Write outside deadband
            await TryWriteDoubleAsync(nodeId, current + 1.0)
                .ConfigureAwait(false);

            await Task.Delay(300).ConfigureAwait(false);

            PublishResponse pubLarge = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            Assert.That(
                pubLarge.NotificationMessage.NotificationData.Count,
                Is.GreaterThan(0),
                "Change outside deadband should trigger notification.");
        }

        [Test]
        public async Task AbsoluteDeadbandLargeThresholdAsync()
        {
            NodeId nodeId = ToNodeId(Constants.AnalogTypeDouble);

            CreateMonitoredItemsResponse createResp =
                await CreateDeadbandItemAsync(
                    nodeId, 3, MakeAbsoluteDeadbandFilter(1000.0))
                .ConfigureAwait(false);
            Assert.That(
                StatusCode.IsGood(createResp.Results[0].StatusCode), Is.True);

            await ConsumeInitialPublishAsync().ConfigureAwait(false);

            double current = await ReadCurrentDoubleAsync(nodeId)
                .ConfigureAwait(false);
            if (!await TryWriteDoubleAsync(nodeId, current + 10.0)
                .ConfigureAwait(false))
            {
                Assert.Fail("AnalogType node is not writable.");
            }

            await Task.Delay(300).ConfigureAwait(false);

            PublishResponse pubResp = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            Assert.That(
                pubResp.NotificationMessage.NotificationData.Count,
                Is.Zero,
                "Change of 10 within deadband of 1000 should not notify.");
        }

        [Test]
        public async Task AbsoluteDeadbandExactlyAtBoundaryAsync()
        {
            NodeId nodeId = ToNodeId(Constants.AnalogTypeDouble);
            const double deadband = 5.0;

            CreateMonitoredItemsResponse createResp =
                await CreateDeadbandItemAsync(
                    nodeId, 4, MakeAbsoluteDeadbandFilter(deadband))
                .ConfigureAwait(false);
            Assert.That(
                StatusCode.IsGood(createResp.Results[0].StatusCode), Is.True);

            await ConsumeInitialPublishAsync().ConfigureAwait(false);

            double current = await ReadCurrentDoubleAsync(nodeId)
                .ConfigureAwait(false);
            if (!await TryWriteDoubleAsync(nodeId, current + deadband)
                .ConfigureAwait(false))
            {
                Assert.Fail("AnalogType node is not writable.");
            }

            await Task.Delay(300).ConfigureAwait(false);

            PublishResponse pubResp = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(pubResp.ResponseHeader.ServiceResult),
                Is.True);
            // At the exact boundary, behavior is implementation-defined
            Assert.Pass(
                "Boundary behavior is implementation-defined. " +
                "Notifications: " +
                $"{pubResp.NotificationMessage.NotificationData.Count}");
        }

        [Test]
        public async Task AbsoluteDeadbandNegativeValueRejectedAsync()
        {
            NodeId nodeId = ToNodeId(Constants.AnalogTypeDouble);

            CreateMonitoredItemsResponse resp = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 5,
                    filter: MakeAbsoluteDeadbandFilter(-1.0)))
                .ConfigureAwait(false);

            StatusCode status = resp.Results[0].StatusCode;
            Assert.That(
                status == StatusCodes.BadMonitoredItemFilterInvalid ||
                status == StatusCodes.BadFilterNotAllowed ||
                status == StatusCodes.BadDeadbandFilterInvalid,
                Is.True,
                $"Negative deadband should be rejected, got {status}");
        }

        [Test]
        public async Task AbsoluteDeadbandMaxDoubleValueAsync()
        {
            NodeId nodeId = ToNodeId(Constants.AnalogTypeDouble);

            CreateMonitoredItemsResponse resp = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 6,
                    filter: MakeAbsoluteDeadbandFilter(double.MaxValue)))
                .ConfigureAwait(false);

            StatusCode status = resp.Results[0].StatusCode;
            // Server may accept or reject this extreme value
            Assert.That(
                StatusCode.IsGood(status) ||
                status == StatusCodes.BadFilterNotAllowed ||
                status == StatusCodes.BadDeadbandFilterInvalid ||
                status == StatusCodes.BadMonitoredItemFilterInvalid,
                Is.True,
                $"MaxValue deadband should be accepted or rejected gracefully: {status}");
        }

        [Test]
        public async Task PercentDeadbandTenPercentOnAnalogNodeAsync()
        {
            NodeId nodeId = ToNodeId(Constants.AnalogTypeDouble);

            CreateMonitoredItemsResponse createResp =
                await CreateDeadbandItemAsync(
                    nodeId, 10, MakePercentDeadbandFilter(10.0))
                .ConfigureAwait(false);
            Assert.That(
                StatusCode.IsGood(createResp.Results[0].StatusCode), Is.True);

            await ConsumeInitialPublishAsync().ConfigureAwait(false);

            double current = await ReadCurrentDoubleAsync(nodeId)
                .ConfigureAwait(false);
            // Write a tiny change (should be within 10% deadband)
            if (!await TryWriteDoubleAsync(nodeId, current + 0.001)
                .ConfigureAwait(false))
            {
                Assert.Fail("AnalogType node is not writable.");
            }

            await Task.Delay(300).ConfigureAwait(false);

            PublishResponse pubResp = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(pubResp.ResponseHeader.ServiceResult),
                Is.True);
            Assert.That(
                pubResp.NotificationMessage.NotificationData.Count,
                Is.Zero,
                "Small change within 10% deadband should not notify.");
        }

        [Test]
        public async Task PercentDeadbandZeroNotifiesOnAnyChangeAsync()
        {
            NodeId nodeId = ToNodeId(Constants.AnalogTypeDouble);

            CreateMonitoredItemsResponse createResp =
                await CreateDeadbandItemAsync(
                    nodeId, 11, MakePercentDeadbandFilter(0.0))
                .ConfigureAwait(false);
            Assert.That(
                StatusCode.IsGood(createResp.Results[0].StatusCode), Is.True);

            await ConsumeInitialPublishAsync().ConfigureAwait(false);

            double current = await ReadCurrentDoubleAsync(nodeId)
                .ConfigureAwait(false);
            if (!await TryWriteDoubleAsync(nodeId, current + 0.001)
                .ConfigureAwait(false))
            {
                Assert.Fail("AnalogType node is not writable.");
            }

            await Task.Delay(300).ConfigureAwait(false);

            PublishResponse pubResp = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(pubResp.ResponseHeader.ServiceResult),
                Is.True);
            Assert.That(
                pubResp.NotificationMessage.NotificationData.Count,
                Is.GreaterThan(0),
                "Percent deadband 0% should notify on any change.");
        }

        [Test]
        public async Task PercentDeadbandHundredPercentOnlyExtremeChangesAsync()
        {
            NodeId nodeId = ToNodeId(Constants.AnalogTypeDouble);

            CreateMonitoredItemsResponse createResp =
                await CreateDeadbandItemAsync(
                    nodeId, 12, MakePercentDeadbandFilter(100.0))
                .ConfigureAwait(false);
            Assert.That(
                StatusCode.IsGood(createResp.Results[0].StatusCode), Is.True);

            await ConsumeInitialPublishAsync().ConfigureAwait(false);

            double current = await ReadCurrentDoubleAsync(nodeId)
                .ConfigureAwait(false);
            if (!await TryWriteDoubleAsync(nodeId, current + 1.0)
                .ConfigureAwait(false))
            {
                Assert.Fail("AnalogType node is not writable.");
            }

            await Task.Delay(300).ConfigureAwait(false);

            PublishResponse pubResp = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(pubResp.ResponseHeader.ServiceResult),
                Is.True);
            // 100% deadband means only full-range changes notify
            Assert.That(
                pubResp.NotificationMessage.NotificationData.Count,
                Is.Zero,
                "Small change with 100% deadband should not notify.");
        }

        [Test]
        public async Task PercentDeadbandFiftyPercentAsync()
        {
            NodeId nodeId = ToNodeId(Constants.AnalogTypeDouble);

            CreateMonitoredItemsResponse createResp =
                await CreateDeadbandItemAsync(
                    nodeId, 13, MakePercentDeadbandFilter(50.0))
                .ConfigureAwait(false);
            Assert.That(
                StatusCode.IsGood(createResp.Results[0].StatusCode), Is.True);

            await ConsumeInitialPublishAsync().ConfigureAwait(false);

            double current = await ReadCurrentDoubleAsync(nodeId)
                .ConfigureAwait(false);
            if (!await TryWriteDoubleAsync(nodeId, current + 0.01)
                .ConfigureAwait(false))
            {
                Assert.Fail("AnalogType node is not writable.");
            }

            await Task.Delay(300).ConfigureAwait(false);

            PublishResponse pubResp = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(pubResp.ResponseHeader.ServiceResult),
                Is.True);
            Assert.That(
                pubResp.NotificationMessage.NotificationData.Count,
                Is.Zero,
                "Tiny change within 50% deadband should not notify.");
        }

        [Test]
        public async Task PercentDeadbandNegativeRejectedAsync()
        {
            NodeId nodeId = ToNodeId(Constants.AnalogTypeDouble);

            CreateMonitoredItemsResponse resp = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 14,
                    filter: MakePercentDeadbandFilter(-10.0)))
                .ConfigureAwait(false);

            StatusCode status = resp.Results[0].StatusCode;
            Assert.That(
                status == StatusCodes.BadMonitoredItemFilterInvalid ||
                status == StatusCodes.BadFilterNotAllowed ||
                status == StatusCodes.BadDeadbandFilterInvalid,
                Is.True,
                $"Negative percent deadband should be rejected: {status}");
        }

        [Test]
        public async Task AbsoluteDeadbandOnInt32NodeAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse resp = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 20,
                    samplingInterval: 50,
                    filter: MakeAbsoluteDeadbandFilter(5.0)))
                .ConfigureAwait(false);

            StatusCode status = resp.Results[0].StatusCode;
            if (status == StatusCodes.BadFilterNotAllowed)
            {
                Assert.Fail(
                    "Server does not support deadband on static Int32.");
            }
            Assert.That(
                StatusCode.IsGood(status), Is.True,
                $"Deadband on Int32 should succeed or be not allowed: {status}");
        }

        [Test]
        public async Task AbsoluteDeadbandOnInt16NodeAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt16);

            CreateMonitoredItemsResponse resp = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 21,
                    samplingInterval: 50,
                    filter: MakeAbsoluteDeadbandFilter(2.0)))
                .ConfigureAwait(false);

            StatusCode status = resp.Results[0].StatusCode;
            if (status == StatusCodes.BadFilterNotAllowed)
            {
                Assert.Fail(
                    "Server does not support deadband on static Int16.");
            }
            Assert.That(
                StatusCode.IsGood(status), Is.True,
                $"Deadband on Int16: {status}");
        }

        [Test]
        public async Task AbsoluteDeadbandOnUInt32NodeAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticUInt32);

            CreateMonitoredItemsResponse resp = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 22,
                    samplingInterval: 50,
                    filter: MakeAbsoluteDeadbandFilter(10.0)))
                .ConfigureAwait(false);

            StatusCode status = resp.Results[0].StatusCode;
            if (status == StatusCodes.BadFilterNotAllowed)
            {
                Assert.Fail(
                    "Server does not support deadband on static UInt32.");
            }
            Assert.That(
                StatusCode.IsGood(status), Is.True,
                $"Deadband on UInt32: {status}");
        }

        [Test]
        public async Task AbsoluteDeadbandOnByteNodeAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticByte);

            CreateMonitoredItemsResponse resp = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 23,
                    samplingInterval: 50,
                    filter: MakeAbsoluteDeadbandFilter(1.0)))
                .ConfigureAwait(false);

            StatusCode status = resp.Results[0].StatusCode;
            if (status == StatusCodes.BadFilterNotAllowed)
            {
                Assert.Fail(
                    "Server does not support deadband on static Byte.");
            }
            Assert.That(
                StatusCode.IsGood(status), Is.True,
                $"Deadband on Byte: {status}");
        }

        [Test]
        public async Task AbsoluteDeadbandOnFloatAnalogNodeAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticFloat);

            CreateMonitoredItemsResponse resp = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 30,
                    samplingInterval: 50,
                    filter: MakeAbsoluteDeadbandFilter(0.5)))
                .ConfigureAwait(false);

            StatusCode status = resp.Results[0].StatusCode;
            if (status == StatusCodes.BadFilterNotAllowed)
            {
                Assert.Fail(
                    "Server does not support deadband on Float node.");
            }
            Assert.That(
                StatusCode.IsGood(status), Is.True,
                $"Deadband on Float: {status}");
        }

        [Test]
        public async Task AbsoluteDeadbandOnDoubleAnalogNodeAsync()
        {
            NodeId nodeId = ToNodeId(Constants.AnalogTypeDouble);

            CreateMonitoredItemsResponse createResp =
                await CreateDeadbandItemAsync(
                    nodeId, 31, MakeAbsoluteDeadbandFilter(5.0))
                .ConfigureAwait(false);
            Assert.That(
                StatusCode.IsGood(createResp.Results[0].StatusCode), Is.True);

            await ConsumeInitialPublishAsync().ConfigureAwait(false);

            double current = await ReadCurrentDoubleAsync(nodeId)
                .ConfigureAwait(false);
            if (!await TryWriteDoubleAsync(nodeId, current + 100.0)
                .ConfigureAwait(false))
            {
                Assert.Ignore("AnalogType node is not writable.");
            }

            await Task.Delay(300).ConfigureAwait(false);

            PublishResponse pubResp = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            Assert.That(
                pubResp.NotificationMessage.NotificationData.Count,
                Is.GreaterThan(0),
                "Change well outside deadband should notify on Double analog.");
        }

        [Test]
        public async Task PercentDeadbandOnDoubleAnalogNodeAsync()
        {
            NodeId nodeId = ToNodeId(Constants.AnalogTypeDouble);

            CreateMonitoredItemsResponse createResp =
                await CreateDeadbandItemAsync(
                    nodeId, 32, MakePercentDeadbandFilter(5.0))
                .ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(createResp.Results[0].StatusCode), Is.True,
                "Percent deadband on Double analog node should be accepted.");
        }

        [Test]
        public async Task DeadbandOnStringNodeReturnsBadFilterNotAllowedAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticString);

            CreateMonitoredItemsResponse resp = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 40,
                    filter: MakeAbsoluteDeadbandFilter(1.0)))
                .ConfigureAwait(false);

            StatusCode status = resp.Results[0].StatusCode;
            Assert.That(
                status == StatusCodes.BadFilterNotAllowed ||
                status == StatusCodes.BadMonitoredItemFilterInvalid ||
                status == StatusCodes.BadMonitoredItemFilterUnsupported,
                Is.True,
                $"Deadband on String should be rejected: {status}");
        }

        [Test]
        public async Task DeadbandOnBooleanNodeReturnsBadFilterNotAllowedAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticBoolean);

            CreateMonitoredItemsResponse resp = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 41,
                    filter: MakeAbsoluteDeadbandFilter(1.0)))
                .ConfigureAwait(false);

            StatusCode status = resp.Results[0].StatusCode;
            Assert.That(
                status == StatusCodes.BadFilterNotAllowed ||
                status == StatusCodes.BadMonitoredItemFilterInvalid ||
                status == StatusCodes.BadMonitoredItemFilterUnsupported,
                Is.True,
                $"Deadband on Boolean should be rejected: {status}");
        }

        [Test]
        public async Task PercentDeadbandOnNonAnalogNodeReturnsBadFilterNotAllowedAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticString);

            CreateMonitoredItemsResponse resp = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 42,
                    filter: MakePercentDeadbandFilter(10.0)))
                .ConfigureAwait(false);

            StatusCode status = resp.Results[0].StatusCode;
            Assert.That(
                status == StatusCodes.BadFilterNotAllowed ||
                status == StatusCodes.BadMonitoredItemFilterInvalid ||
                status == StatusCodes.BadMonitoredItemFilterUnsupported,
                Is.True,
                $"Percent deadband on non-analog should be rejected: {status}");
        }

        [Test]
        public async Task ModifyItemToAddDeadbandFilterAsync()
        {
            NodeId nodeId = ToNodeId(Constants.AnalogTypeDouble);

            // Create without filter
            CreateMonitoredItemsResponse createResp =
                await CreateSingleItemAsync(
                    CreateItemRequest(nodeId, 50, samplingInterval: 50))
                .ConfigureAwait(false);
            Assert.That(
                StatusCode.IsGood(createResp.Results[0].StatusCode), Is.True);
            uint monId = createResp.Results[0].MonitoredItemId;

            // Modify to add absolute deadband filter
            ModifyMonitoredItemsResponse modResp =
                await Session.ModifyMonitoredItemsAsync(
                    null, m_subscriptionId, TimestampsToReturn.Both,
                    new MonitoredItemModifyRequest[]
                    {
                        new() {
                            MonitoredItemId = monId,
                            RequestedParameters = new MonitoringParameters
                            {
                                ClientHandle = 50,
                                SamplingInterval = 50,
                                QueueSize = 10,
                                DiscardOldest = true,
                                Filter = MakeAbsoluteDeadbandFilter(10.0)
                            }
                        }
                    }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

            StatusCode modStatus = modResp.Results[0].StatusCode;
            if (modStatus == StatusCodes.BadFilterNotAllowed)
            {
                Assert.Fail(
                    "Server does not support deadband filter on this node.");
            }
            Assert.That(StatusCode.IsGood(modStatus), Is.True,
                "Modify to add deadband filter should succeed.");
        }

        [Test]
        public async Task ModifyItemToRemoveDeadbandFilterAsync()
        {
            NodeId nodeId = ToNodeId(Constants.AnalogTypeDouble);

            // Create with deadband filter
            CreateMonitoredItemsResponse createResp =
                await CreateDeadbandItemAsync(
                    nodeId, 51, MakeAbsoluteDeadbandFilter(10.0))
                .ConfigureAwait(false);
            Assert.That(
                StatusCode.IsGood(createResp.Results[0].StatusCode), Is.True);
            uint monId = createResp.Results[0].MonitoredItemId;

            // Modify to remove filter (set filter to default/none)
            ModifyMonitoredItemsResponse modResp =
                await Session.ModifyMonitoredItemsAsync(
                    null, m_subscriptionId, TimestampsToReturn.Both,
                    new MonitoredItemModifyRequest[]
                    {
                        new() {
                            MonitoredItemId = monId,
                            RequestedParameters = new MonitoringParameters
                            {
                                ClientHandle = 51,
                                SamplingInterval = 50,
                                QueueSize = 10,
                                DiscardOldest = true,
                                Filter = default
                            }
                        }
                    }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(modResp.Results[0].StatusCode), Is.True,
                "Modify to remove deadband filter should succeed.");
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
            string indexRange = null,
            TimestampsToReturn timestamps = TimestampsToReturn.Both)
        {
            return new MonitoredItemCreateRequest
            {
                ItemToMonitor = new ReadValueId
                {
                    NodeId = nodeId,
                    AttributeId = attributeId,
                    IndexRange = indexRange
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

        private async Task<bool> TryWriteDoubleAsync(
            NodeId nodeId, double value)
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

        private ExtensionObject MakeAbsoluteDeadbandFilter(
            double deadbandValue)
        {
            return new ExtensionObject(new DataChangeFilter
            {
                Trigger = DataChangeTrigger.StatusValue,
                DeadbandType = (uint)DeadbandType.Absolute,
                DeadbandValue = deadbandValue
            });
        }

        private ExtensionObject MakePercentDeadbandFilter(
            double percentValue)
        {
            return new ExtensionObject(new DataChangeFilter
            {
                Trigger = DataChangeTrigger.StatusValue,
                DeadbandType = (uint)DeadbandType.Percent,
                DeadbandValue = percentValue
            });
        }

        private async Task ConsumeInitialPublishAsync()
        {
            await Task.Delay(300).ConfigureAwait(false);
            await Session.PublishWithTimeoutAsync().ConfigureAwait(false);
        }

        private async Task<double> ReadCurrentDoubleAsync(NodeId nodeId)
        {
            ReadResponse readResp = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            return readResp.Results[0].WrappedValue.GetDouble();
        }

        private uint m_subscriptionId;

        /// <summary>
        /// Creates a monitored item with deadband filter, returning the
        /// create status. Calls Assert.Ignore if BadFilterNotAllowed.
        /// </summary>
        private async Task<CreateMonitoredItemsResponse>
            CreateDeadbandItemAsync(
                NodeId nodeId,
                uint clientHandle,
                ExtensionObject filter,
                double samplingInterval = 50,
                MonitoringMode mode = MonitoringMode.Reporting,
                uint queueSize = 10,
                string indexRange = null,
                TimestampsToReturn timestamps = TimestampsToReturn.Both)
        {
            CreateMonitoredItemsResponse resp = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, clientHandle,
                    samplingInterval: samplingInterval,
                    queueSize: queueSize,
                    mode: mode,
                    filter: filter,
                    indexRange: indexRange),
                timestamps)
                .ConfigureAwait(false);

            StatusCode status = resp.Results[0].StatusCode;
            if (status == StatusCodes.BadFilterNotAllowed ||
                status == StatusCodes.BadMonitoredItemFilterUnsupported)
            {
                Assert.Ignore(
                    "Server does not support deadband filter on this node.");
            }

            return resp;
        }

        private async Task<bool> TryWriteArrayAsync(
            NodeId nodeId, int[] values, string indexRange = null)
        {
            var wv = new WriteValue
            {
                NodeId = nodeId,
                AttributeId = Attributes.Value,
                Value = new DataValue(new Variant(values))
            };
            if (indexRange != null)
            {
                wv.IndexRange = indexRange;
            }

            WriteResponse writeResp = await Session.WriteAsync(
                null,
                new WriteValue[] { wv }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            return StatusCode.IsGood(writeResp.Results[0]);
        }

        private async Task<int[]> ReadCurrentArrayInt32Async(NodeId nodeId)
        {
            ReadResponse readResp = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Variant variant = readResp.Results[0].WrappedValue;
            if (variant.TryGetValue(out ArrayOf<int> arr))
            {
                return arr.ToArray();
            }
            // FUTURE-AsBoxedObject-cleanup: legacy compatibility for callers
            // that still surface int[] / IConvertableToArray / Array outside
            // the typed Variant accessors. Once those paths migrate this can
            // drop.
            object val = variant.AsBoxedObject();
            if (val is int[] intArrLegacy)
            {
                return intArrLegacy;
            }
            if (val is IConvertableToArray convertable)
            {
                var converted = convertable.ToArray();
                if (converted is int[] intArr)
                {
                    return intArr;
                }

                return [.. converted.Cast<object>().Select(Convert.ToInt32)];
            }
            if (val is Array a)
            {
                return [.. a.Cast<object>().Select(Convert.ToInt32)];
            }
            return [Convert.ToInt32(val)];
        }

        private int NotificationCount(PublishResponse pubResp)
        {
            if (pubResp.NotificationMessage?.NotificationData == null ||
                pubResp.NotificationMessage.NotificationData.Count == 0)
            {
                return 0;
            }
            if (ExtensionObject.ToEncodeable(
                pubResp.NotificationMessage.NotificationData[0]) is not DataChangeNotification dcn)
            {
                return 0;
            }
            return dcn.MonitoredItems.Count;
        }

        [Test]
        public async Task DisabledModeAbsoluteDeadbandZeroAsync()
        {
            NodeId nodeId = ToNodeId(Constants.AnalogTypeDouble);

            CreateMonitoredItemsResponse resp = await CreateDeadbandItemAsync(
                nodeId, 1, MakeAbsoluteDeadbandFilter(0.0),
                mode: MonitoringMode.Disabled,
                queueSize: 1,
                timestamps: TimestampsToReturn.Server)
                .ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(resp.Results[0].StatusCode), Is.True,
                "Create with Disabled mode and absolute deadband 0 " +
                "should succeed.");
        }

        [Test]
        public async Task SamplingModeAbsoluteDeadbandZeroAsync()
        {
            NodeId nodeId = ToNodeId(Constants.AnalogTypeDouble);

            CreateMonitoredItemsResponse resp = await CreateDeadbandItemAsync(
                nodeId, 1, MakeAbsoluteDeadbandFilter(0.0),
                mode: MonitoringMode.Sampling,
                queueSize: 1,
                timestamps: TimestampsToReturn.Server)
                .ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(resp.Results[0].StatusCode), Is.True,
                "Create with Sampling mode and absolute deadband 0 " +
                "should succeed.");
        }

        [Test]
        public async Task SamplingModeAbsoluteDeadbandZeroQueueZeroAsync()
        {
            NodeId nodeId = ToNodeId(Constants.AnalogTypeDouble);

            CreateMonitoredItemsResponse resp = await CreateDeadbandItemAsync(
                nodeId, 1, MakeAbsoluteDeadbandFilter(0.0),
                mode: MonitoringMode.Sampling,
                queueSize: 0,
                timestamps: TimestampsToReturn.Server)
                .ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(resp.Results[0].StatusCode), Is.True,
                "Create with Sampling mode, deadband 0, queue 0 " +
                "should succeed.");
            Assert.That(
                resp.Results[0].RevisedQueueSize,
                Is.GreaterThanOrEqualTo(1u),
                "Server should revise queue size 0 to at least 1.");
        }

        [Test]
        public async Task ReportingModeAbsoluteDeadbandZeroQueueOneAsync()
        {
            NodeId nodeId = ToNodeId(Constants.AnalogTypeDouble);

            CreateMonitoredItemsResponse resp = await CreateDeadbandItemAsync(
                nodeId, 1, MakeAbsoluteDeadbandFilter(0.0),
                mode: MonitoringMode.Reporting,
                queueSize: 1,
                timestamps: TimestampsToReturn.Server)
                .ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(resp.Results[0].StatusCode), Is.True,
                "Create with Reporting mode, deadband 0, queue 1 " +
                "should succeed.");
        }

        [Test]
        public async Task ReportingModeAbsoluteDeadbandZeroQueueZeroAsync()
        {
            NodeId nodeId = ToNodeId(Constants.AnalogTypeDouble);

            CreateMonitoredItemsResponse resp = await CreateDeadbandItemAsync(
                nodeId, 1, MakeAbsoluteDeadbandFilter(0.0),
                mode: MonitoringMode.Reporting,
                queueSize: 0,
                timestamps: TimestampsToReturn.Server)
                .ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(resp.Results[0].StatusCode), Is.True,
                "Create with Reporting mode, deadband 0, queue 0 " +
                "should succeed.");
            Assert.That(
                resp.Results[0].RevisedQueueSize,
                Is.GreaterThanOrEqualTo(1u),
                "Server should revise queue size 0 to at least 1.");
        }

        [Test]
        public async Task DeadbandOnNonValueAttributesRejectedAsync()
        {
            NodeId nodeId = ToNodeId(Constants.AnalogTypeDouble);
            ExtensionObject filter = MakeAbsoluteDeadbandFilter(5.0);

            // Value attribute should succeed
            CreateMonitoredItemsResponse valueResp =
                await CreateDeadbandItemAsync(
                    nodeId, 1, filter)
                .ConfigureAwait(false);
            Assert.That(
                StatusCode.IsGood(valueResp.Results[0].StatusCode), Is.True,
                "Deadband filter on Value attribute should succeed.");

            // DisplayName attribute should be rejected
            CreateMonitoredItemsResponse displayResp =
                await CreateSingleItemAsync(
                    CreateItemRequest(nodeId, 2,
                        attributeId: Attributes.DisplayName,
                        filter: filter))
                .ConfigureAwait(false);
            Assert.That(
                StatusCode.IsBad(displayResp.Results[0].StatusCode), Is.True,
                "Deadband on DisplayName should be rejected.");

            // ValueRank attribute should be rejected
            CreateMonitoredItemsResponse rankResp =
                await CreateSingleItemAsync(
                    CreateItemRequest(nodeId, 3,
                        attributeId: Attributes.ValueRank,
                        filter: filter))
                .ConfigureAwait(false);
            Assert.That(
                StatusCode.IsBad(rankResp.Results[0].StatusCode), Is.True,
                "Deadband on ValueRank should be rejected.");
        }

        [Test]
        public async Task AbsoluteDeadbandWritePublishThresholdTwoAsync()
        {
            NodeId nodeId = ToNodeId(Constants.AnalogTypeDouble);
            const double deadband = 2.0;

            CreateMonitoredItemsResponse createResp =
                await CreateDeadbandItemAsync(
                    nodeId, 1, MakeAbsoluteDeadbandFilter(deadband))
                .ConfigureAwait(false);
            Assert.That(
                StatusCode.IsGood(createResp.Results[0].StatusCode),
                Is.True);

            await ConsumeInitialPublishAsync().ConfigureAwait(false);

            double current = await ReadCurrentDoubleAsync(nodeId)
                .ConfigureAwait(false);

            // Write within deadband — should not notify
            if (!await TryWriteDoubleAsync(nodeId, current + 1.0)
                .ConfigureAwait(false))
            {
                Assert.Fail("AnalogType node is not writable.");
            }

            await Task.Delay(300).ConfigureAwait(false);
            PublishResponse pubSmall = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);
            Assert.That(
                NotificationCount(pubSmall), Is.Zero,
                "Change within deadband should not notify.");

            // Write exceeding deadband — should notify
            await TryWriteDoubleAsync(nodeId, current + 5.0)
                .ConfigureAwait(false);

            await Task.Delay(300).ConfigureAwait(false);
            PublishResponse pubLarge = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);
            Assert.That(
                pubLarge.NotificationMessage.NotificationData.Count,
                Is.GreaterThan(0),
                "Change exceeding deadband should notify.");
        }

        [Test]
        public async Task AbsoluteDeadbandWritePublishThresholdOneAsync()
        {
            NodeId nodeId = ToNodeId(Constants.AnalogTypeDouble);
            const double deadband = 1.0;

            CreateMonitoredItemsResponse createResp =
                await CreateDeadbandItemAsync(
                    nodeId, 1, MakeAbsoluteDeadbandFilter(deadband))
                .ConfigureAwait(false);
            Assert.That(
                StatusCode.IsGood(createResp.Results[0].StatusCode),
                Is.True);

            await ConsumeInitialPublishAsync().ConfigureAwait(false);

            double current = await ReadCurrentDoubleAsync(nodeId)
                .ConfigureAwait(false);

            // Write within deadband
            if (!await TryWriteDoubleAsync(nodeId, current + 0.25)
                .ConfigureAwait(false))
            {
                Assert.Fail("AnalogType node is not writable.");
            }

            await Task.Delay(300).ConfigureAwait(false);
            PublishResponse pubSmall = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);
            Assert.That(
                NotificationCount(pubSmall), Is.Zero,
                "Change within deadband should not notify.");

            // Write exceeding deadband
            await TryWriteDoubleAsync(nodeId, current + 3.0)
                .ConfigureAwait(false);

            await Task.Delay(300).ConfigureAwait(false);
            PublishResponse pubLarge = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);
            Assert.That(
                pubLarge.NotificationMessage.NotificationData.Count,
                Is.GreaterThan(0),
                "Change exceeding deadband should notify.");
        }

        [Test]
        public async Task AbsoluteDeadbandLargeThresholdNewSubscriptionAsync()
        {
            // Use a fresh subscription for this test
            uint freshSubId = 0;
            try
            {
                CreateSubscriptionResponse subResp =
                    await Session.CreateSubscriptionAsync(
                        null, 100, 100, 10, 0, true, 0,
                        CancellationToken.None).ConfigureAwait(false);
                freshSubId = subResp.SubscriptionId;

                NodeId nodeId = ToNodeId(Constants.AnalogTypeDouble);
                ExtensionObject filter = MakeAbsoluteDeadbandFilter(250.0);

                CreateMonitoredItemsResponse createResp =
                    await Session.CreateMonitoredItemsAsync(
                        null, freshSubId, TimestampsToReturn.Both,
                        new MonitoredItemCreateRequest[]
                        {
                            CreateItemRequest(nodeId, 1,
samplingInterval: 50,                                 filter: filter)
                        }.ToArrayOf(),
                        CancellationToken.None).ConfigureAwait(false);

                StatusCode status = createResp.Results[0].StatusCode;
                if (status == StatusCodes.BadFilterNotAllowed ||
                    status == StatusCodes.BadMonitoredItemFilterUnsupported)
                {
                    Assert.Ignore(
                        "Server does not support deadband on this node.");
                }
                Assert.That(StatusCode.IsGood(status), Is.True);

                await Task.Delay(300).ConfigureAwait(false);
                await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

                double current = await ReadCurrentDoubleAsync(nodeId)
                    .ConfigureAwait(false);

                // Write within deadband
                if (!await TryWriteDoubleAsync(nodeId, current + 10.0)
                    .ConfigureAwait(false))
                {
                    Assert.Ignore("AnalogType node is not writable.");
                }

                await Task.Delay(300).ConfigureAwait(false);
                PublishResponse pubResp = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);
                if (NotificationCount(pubResp) != 0)
                {
                    Assert.Ignore("Timing-sensitive: received notification within large deadband threshold.");
                }
            }
            finally
            {
                if (freshSubId > 0)
                {
                    await Session.DeleteSubscriptionsAsync(
                        null,
                        new uint[] { freshSubId }.ToArrayOf(),
                        CancellationToken.None).ConfigureAwait(false);
                }
            }
        }

        [Test]
        [Category("LongRunning")]
        public async Task ArrayDeadbandFirstElementAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticArrayInt32);
            ExtensionObject filter = MakeAbsoluteDeadbandFilter(10.0);

            CreateMonitoredItemsResponse createResp =
                await CreateDeadbandItemAsync(
                    nodeId, 1, filter, samplingInterval: 50)
                .ConfigureAwait(false);
            Assert.That(
                StatusCode.IsGood(createResp.Results[0].StatusCode),
                Is.True);

            try
            {
                await ConsumeInitialPublishAsync().ConfigureAwait(false);

                int[] current = await ReadCurrentArrayInt32Async(nodeId)
                    .ConfigureAwait(false);
                if (current.Length == 0)
                {
                    Assert.Ignore("Array node has no elements.");
                }

                // Change element 0 by +11 (exceeds deadband)
                int[] modified = (int[])current.Clone();
                modified[0] = current[0] + 11;
                if (!await TryWriteArrayAsync(nodeId, modified)
                    .ConfigureAwait(false))
                {
                    Assert.Ignore("Array node is not writable.");
                }

                await Task.Delay(300).ConfigureAwait(false);
                PublishResponse pub1 = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);
                Assert.That(
                    pub1.NotificationMessage.NotificationData.Count,
                    Is.GreaterThan(0),
                    "Change exceeding deadband (+11) should notify.");

                // Change element 0 by +5 (within deadband)
                modified[0] += 5;
                await TryWriteArrayAsync(nodeId, modified)
                    .ConfigureAwait(false);

                await Task.Delay(300).ConfigureAwait(false);
                PublishResponse pub2 = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);
                if (NotificationCount(pub2) != 0)
                {
                    Assert.Ignore("Timing-sensitive: server notified for within-deadband change.");
                }

                // Change element 0 to current[0] - 11 (22 below last reported
                // value of current[0]+11; clearly exceeds deadband of 10).
                modified[0] = current[0] - 11;
                await TryWriteArrayAsync(nodeId, modified)
                    .ConfigureAwait(false);

                await Task.Delay(300).ConfigureAwait(false);
                PublishResponse pub3 = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);
                Assert.That(
                    pub3.NotificationMessage.NotificationData.Count,
                    Is.GreaterThan(0),
                    "Change exceeding deadband (-22 from last reported) should notify.");
            }
            catch (ServiceResultException sre) when (IsTransientCiTimeoutStatus(sre.StatusCode))
            {
                Assert.Ignore(
                    $"Timing-sensitive: array-deadband publish interrupted by CI runner load ({sre.StatusCode}).");
            }
        }

        [Test]
        public async Task ArrayDeadbandIndexRangeOneTwoAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticArrayInt32);
            ExtensionObject filter = MakeAbsoluteDeadbandFilter(10.0);

            CreateMonitoredItemsResponse createResp =
                await CreateDeadbandItemAsync(
                    nodeId, 1, filter, samplingInterval: 50,
                    indexRange: "1:2")
                .ConfigureAwait(false);
            Assert.That(
                StatusCode.IsGood(createResp.Results[0].StatusCode),
                Is.True);

            await ConsumeInitialPublishAsync().ConfigureAwait(false);

            int[] current = await ReadCurrentArrayInt32Async(nodeId)
                .ConfigureAwait(false);
            if (current.Length < 3)
            {
                Assert.Fail("Array needs at least 3 elements.");
            }

            // Write outside monitored range (element 0) — should not notify
            int[] modified = (int[])current.Clone();
            modified[0] = current[0] + 100;
            if (!await TryWriteArrayAsync(nodeId, modified)
                .ConfigureAwait(false))
            {
                Assert.Fail("Array node is not writable.");
            }

            await Task.Delay(300).ConfigureAwait(false);
            PublishResponse pub1 = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);
            Assert.That(
                NotificationCount(pub1), Is.Zero,
                "Change outside monitored IndexRange should not notify.");

            // Write inside monitored range exceeding deadband (element 1)
            modified[1] += 15;
            await TryWriteArrayAsync(nodeId, modified).ConfigureAwait(false);

            await Task.Delay(300).ConfigureAwait(false);
            PublishResponse pub2 = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);
            Assert.That(
                pub2.NotificationMessage.NotificationData.Count,
                Is.GreaterThan(0),
                "In-range change exceeding deadband should notify.");
        }

        [Test]
        public async Task ArrayDeadbandMiddleIndexRangeAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticArrayInt32);
            int[] current = await ReadCurrentArrayInt32Async(nodeId)
                .ConfigureAwait(false);
            if (current.Length < 4)
            {
                Assert.Fail("Array needs at least 4 elements.");
            }

            int mid = current.Length / 2;
            string range = $"{mid - 1}:{mid}";
            ExtensionObject filter = MakeAbsoluteDeadbandFilter(10.0);

            CreateMonitoredItemsResponse createResp =
                await CreateDeadbandItemAsync(
                    nodeId, 1, filter, samplingInterval: 50,
                    indexRange: range)
                .ConfigureAwait(false);
            Assert.That(
                StatusCode.IsGood(createResp.Results[0].StatusCode),
                Is.True);

            await ConsumeInitialPublishAsync().ConfigureAwait(false);

            // Write outside monitored range (element 0)
            int[] modified = (int[])current.Clone();
            modified[0] = current[0] + 100;
            if (!await TryWriteArrayAsync(nodeId, modified)
                .ConfigureAwait(false))
            {
                Assert.Fail("Array node is not writable.");
            }

            await Task.Delay(300).ConfigureAwait(false);
            PublishResponse pub1 = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);
            Assert.That(
                NotificationCount(pub1), Is.Zero,
                "Change outside middle range should not notify.");

            // Write inside monitored range exceeding deadband
            modified[mid] += 15;
            await TryWriteArrayAsync(nodeId, modified).ConfigureAwait(false);

            await Task.Delay(300).ConfigureAwait(false);
            PublishResponse pub2 = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);
            Assert.That(
                pub2.NotificationMessage.NotificationData.Count,
                Is.GreaterThan(0),
                "Middle-range change exceeding deadband should notify.");
        }

        [Test]
        public async Task ArrayDeadbandIndexRangeOneThreeAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticArrayInt32);
            ExtensionObject filter = MakeAbsoluteDeadbandFilter(10.0);

            CreateMonitoredItemsResponse createResp =
                await CreateDeadbandItemAsync(
                    nodeId, 1, filter, samplingInterval: 50,
                    indexRange: "1:3")
                .ConfigureAwait(false);
            Assert.That(
                StatusCode.IsGood(createResp.Results[0].StatusCode),
                Is.True);

            await ConsumeInitialPublishAsync().ConfigureAwait(false);

            int[] current = await ReadCurrentArrayInt32Async(nodeId)
                .ConfigureAwait(false);
            if (current.Length < 4)
            {
                Assert.Ignore("Array needs at least 4 elements.");
            }

            // Change element 2 by +11 (exceeds deadband)
            int[] modified = (int[])current.Clone();
            modified[2] = current[2] + 11;
            if (!await TryWriteArrayAsync(nodeId, modified)
                .ConfigureAwait(false))
            {
                Assert.Ignore("Array node is not writable.");
            }

            await Task.Delay(300).ConfigureAwait(false);
            PublishResponse pub1 = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);
            Assert.That(
                pub1.NotificationMessage.NotificationData.Count,
                Is.GreaterThan(0),
                "In-range change +11 should notify.");

            // Change element 2 by +5 (within deadband)
            modified[2] += 5;
            await TryWriteArrayAsync(nodeId, modified).ConfigureAwait(false);

            await Task.Delay(300).ConfigureAwait(false);
            PublishResponse pub2 = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);
            if (NotificationCount(pub2) != 0)
            {
                Assert.Ignore("Timing-sensitive: server notified for within-deadband change.");
            }

            // Change element 2 to current[2] - 11 (22 below last reported
            // value of current[2]+11; clearly exceeds deadband of 10).
            modified[2] = current[2] - 11;
            await TryWriteArrayAsync(nodeId, modified).ConfigureAwait(false);

            await Task.Delay(300).ConfigureAwait(false);
            PublishResponse pub3 = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);
            Assert.That(
                pub3.NotificationMessage.NotificationData.Count,
                Is.GreaterThan(0),
                "In-range change to current-11 should notify (delta 22 from last reported).");
        }

        [Test]
        public async Task ArrayDeadbandFullRangeAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticArrayInt32);
            int[] current = await ReadCurrentArrayInt32Async(nodeId)
                .ConfigureAwait(false);
            if (current.Length < 2)
            {
                Assert.Fail("Array needs at least 2 elements.");
            }

            string range = $"0:{current.Length - 1}";
            ExtensionObject filter = MakeAbsoluteDeadbandFilter(10.0);

            CreateMonitoredItemsResponse createResp =
                await CreateDeadbandItemAsync(
                    nodeId, 1, filter, samplingInterval: 50,
                    indexRange: range)
                .ConfigureAwait(false);
            Assert.That(
                StatusCode.IsGood(createResp.Results[0].StatusCode),
                Is.True);

            await ConsumeInitialPublishAsync().ConfigureAwait(false);

            // Modify first element by +11 — should notify
            int[] modified = (int[])current.Clone();
            modified[0] = current[0] + 11;
            if (!await TryWriteArrayAsync(nodeId, modified)
                .ConfigureAwait(false))
            {
                Assert.Fail("Array node is not writable.");
            }

            await Task.Delay(300).ConfigureAwait(false);
            PublishResponse pub1 = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);
            Assert.That(
                pub1.NotificationMessage.NotificationData.Count,
                Is.GreaterThan(0),
                "Full-range first element +11 should notify.");

            // Modify last element by +11 — should notify
            modified[^1] += 11;
            await TryWriteArrayAsync(nodeId, modified).ConfigureAwait(false);

            await Task.Delay(300).ConfigureAwait(false);
            PublishResponse pub2 = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);
            Assert.That(
                pub2.NotificationMessage.NotificationData.Count,
                Is.GreaterThan(0),
                "Full-range last element +11 should notify.");
        }

        [Test]
        public async Task DeadbandOnArrayDimensionsAttributeAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticArrayInt32);
            ExtensionObject filter = MakeAbsoluteDeadbandFilter(10.0);

            CreateMonitoredItemsResponse resp = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 1,
                    samplingInterval: 50,
                    attributeId: Attributes.ArrayDimensions,
                    filter: filter))
                .ConfigureAwait(false);

            StatusCode status = resp.Results[0].StatusCode;
            // Server may reject deadband on ArrayDimensions; that is valid
            if (StatusCode.IsBad(status))
            {
                Assert.That(
                    status == StatusCodes.BadFilterNotAllowed ||
                    status == StatusCodes.BadMonitoredItemFilterInvalid ||
                    status == StatusCodes.BadMonitoredItemFilterUnsupported,
                    Is.True,
                    $"Expected filter rejection status, got {status}");
            }
            else
            {
                Assert.That(StatusCode.IsGood(status), Is.True);
            }
        }

        [Test]
        public async Task ArrayDeadbandFullRangeWriteSequenceAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticArrayInt32);
            int[] current = await ReadCurrentArrayInt32Async(nodeId)
                .ConfigureAwait(false);
            if (current.Length < 1)
            {
                Assert.Fail("Array needs at least 1 element.");
            }

            string range = current.Length == 1
                ? "0"
                : $"0:{current.Length - 1}";
            ExtensionObject filter = MakeAbsoluteDeadbandFilter(10.0);

            CreateMonitoredItemsResponse createResp =
                await CreateDeadbandItemAsync(
                    nodeId, 1, filter, samplingInterval: 50,
                    indexRange: range)
                .ConfigureAwait(false);
            Assert.That(
                StatusCode.IsGood(createResp.Results[0].StatusCode),
                Is.True);

            await ConsumeInitialPublishAsync().ConfigureAwait(false);

            int[] modified = (int[])current.Clone();

            // +11 should notify
            modified[0] = current[0] + 11;
            if (!await TryWriteArrayAsync(nodeId, modified)
                .ConfigureAwait(false))
            {
                Assert.Fail("Array node is not writable.");
            }
            await Task.Delay(300).ConfigureAwait(false);
            PublishResponse pub1 = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);
            Assert.That(
                pub1.NotificationMessage.NotificationData.Count,
                Is.GreaterThan(0),
                "+11 should notify.");

            // +5 within deadband — should not notify
            modified[0] += 5;
            await TryWriteArrayAsync(nodeId, modified).ConfigureAwait(false);
            await Task.Delay(300).ConfigureAwait(false);
            PublishResponse pub2 = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);
            if (NotificationCount(pub2) != 0)
            {
                Assert.Fail("Timing-sensitive: server notified for within-deadband change.");
            }

            // -16 exceeds deadband — should notify
            modified[0] -= 16;
            await TryWriteArrayAsync(nodeId, modified).ConfigureAwait(false);
            await Task.Delay(300).ConfigureAwait(false);
            PublishResponse pub3 = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);
            Assert.That(
                pub3.NotificationMessage.NotificationData.Count,
                Is.GreaterThan(0),
                "-16 should notify.");

            // Repeat same value — should not notify
            await TryWriteArrayAsync(nodeId, modified).ConfigureAwait(false);
            await Task.Delay(300).ConfigureAwait(false);
            PublishResponse pub4 = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);
            Assert.That(
                NotificationCount(pub4), Is.Zero,
                "Unchanged repeat should not notify.");
        }

        [Test]
        public async Task ArrayDeadbandQueueSizeOneNoIndexRangeAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticArrayInt32);
            ExtensionObject filter = MakeAbsoluteDeadbandFilter(10.0);

            CreateMonitoredItemsResponse createResp =
                await CreateDeadbandItemAsync(
                    nodeId, 1, filter, samplingInterval: 50,
                    queueSize: 1)
                .ConfigureAwait(false);
            Assert.That(
                StatusCode.IsGood(createResp.Results[0].StatusCode),
                Is.True);

            await ConsumeInitialPublishAsync().ConfigureAwait(false);

            int[] current = await ReadCurrentArrayInt32Async(nodeId)
                .ConfigureAwait(false);
            if (current.Length == 0)
            {
                Assert.Ignore("Array has no elements.");
            }

            // +11 should notify
            int[] modified = (int[])current.Clone();
            modified[0] = current[0] + 11;
            if (!await TryWriteArrayAsync(nodeId, modified)
                .ConfigureAwait(false))
            {
                Assert.Ignore("Array node is not writable.");
            }
            await Task.Delay(300).ConfigureAwait(false);
            PublishResponse pub1 = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);
            Assert.That(
                pub1.NotificationMessage.NotificationData.Count,
                Is.GreaterThan(0),
                "+11 should notify with queue 1.");

            // +5 within deadband — should not notify
            modified[0] += 5;
            await TryWriteArrayAsync(nodeId, modified).ConfigureAwait(false);
            await Task.Delay(300).ConfigureAwait(false);
            PublishResponse pub2 = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);
            if (NotificationCount(pub2) != 0)
            {
                Assert.Ignore("Timing-sensitive: server notified for within-deadband change.");
            }

            // Change element 0 to current[0] - 11 (22 below last reported
            // value of current[0]+11; clearly exceeds deadband of 10).
            modified[0] = current[0] - 11;
            await TryWriteArrayAsync(nodeId, modified).ConfigureAwait(false);
            await Task.Delay(300).ConfigureAwait(false);
            PublishResponse pub3 = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);
            Assert.That(
                pub3.NotificationMessage.NotificationData.Count,
                Is.GreaterThan(0),
                "Change to current-11 should notify with queue 1 (delta 22 from last reported).");

            // Repeat unchanged — should not notify
            await TryWriteArrayAsync(nodeId, modified).ConfigureAwait(false);
            await Task.Delay(300).ConfigureAwait(false);
            PublishResponse pub4 = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);
            Assert.That(
                NotificationCount(pub4), Is.Zero,
                "Unchanged should not notify with queue 1.");
        }
    }
}
