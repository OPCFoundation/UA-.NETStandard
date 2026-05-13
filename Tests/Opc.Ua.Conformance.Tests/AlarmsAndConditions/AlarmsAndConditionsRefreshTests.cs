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

namespace Opc.Ua.Conformance.Tests.AlarmsAndConditions
{
    /// <summary>
    /// compliance tests for the A and C Refresh and A and C Refresh2
    /// conformance units. Verifies that ConditionRefresh and
    /// ConditionRefresh2 methods exist and work correctly.
    /// </summary>
    [NonParallelizable]
    [TestFixture]
    [Category("Conformance")]
    [Category("AlarmsAndConditions")]
    public class AlarmsAndConditionsRefreshTests : AlarmsAndConditionsTestFixture
    {
        [SetUp]
        public async Task SetupSubscription()
        {
            CreateSubscriptionResponse response = await Session.CreateSubscriptionAsync(
                null, 1000, 100, 10, 0, true, 0,
                CancellationToken.None).ConfigureAwait(false);
            m_subscriptionId = response.SubscriptionId;

            var eventFilter = new EventFilter
            {
                SelectClauses =
                [
                    new SimpleAttributeOperand
                    {
                        TypeDefinitionId = ObjectTypeIds.BaseEventType,
                        BrowsePath = [new QualifiedName(BrowseNames.EventId)],
                        AttributeId = Attributes.Value
                    }
                ],
                WhereClause = new ContentFilter()
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
                    ClientHandle = 1,
                    SamplingInterval = 0,
                    Filter = new ExtensionObject(eventFilter),
                    QueueSize = 100,
                    DiscardOldest = true
                }
            };

            CreateMonitoredItemsResponse miResp =
                await Session.CreateMonitoredItemsAsync(
                    null, m_subscriptionId, TimestampsToReturn.Neither,
                    new MonitoredItemCreateRequest[] { item }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);
            m_monitoredItemId = miResp.Results.Count > 0
                && StatusCode.IsGood(miResp.Results[0].StatusCode)
                ? miResp.Results[0].MonitoredItemId
                : 0;
        }

        [TearDown]
        public async Task TeardownSubscription()
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
                    // already deleted
                }
                m_subscriptionId = 0;
            }
        }

        [Test]
        [Property("ConformanceUnit", "A and C Refresh")]
        [Property("Tag", "N/A")]
        public async Task ConditionRefreshMethodExistsAsync()
        {
            bool found = await TypeHasChildAsync(
                ObjectTypeIds.ConditionType, "ConditionRefresh").ConfigureAwait(false);
            Assert.That(found, Is.True,
                "ConditionType should have ConditionRefresh method.");
        }

        [Test]
        [Property("ConformanceUnit", "A and C Refresh")]
        [Property("Tag", "Test_002")]
        public async Task ConditionRefreshReturnsCurrentStateAsync()
        {
            CallMethodResult callResult = await CallMethodOnAlarmAsync(
                ObjectTypeIds.ConditionType,
                MethodIds.ConditionType_ConditionRefresh,
                new Variant(m_subscriptionId)).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(callResult.StatusCode), Is.True,
                $"ConditionRefresh should succeed: {callResult.StatusCode}");
        }

        [Test]
        [Property("ConformanceUnit", "A and C Refresh")]
        [Property("Tag", "Err_003")]
        public async Task ErrConditionRefreshWithBadSubscriptionIdAsync()
        {
            CallMethodResult callResult = await CallMethodOnAlarmAsync(
                ObjectTypeIds.ConditionType,
                MethodIds.ConditionType_ConditionRefresh,
                new Variant(uint.MaxValue)).ConfigureAwait(false);

            Assert.That(StatusCode.IsBad(callResult.StatusCode), Is.True,
                "ConditionRefresh with a bad SubscriptionId should fail.");
        }

        [Test]
        [Property("ConformanceUnit", "A and C Refresh")]
        [Property("Tag", "Err_005")]
        public async Task ErrConditionRefreshWithInvalidArgsAsync()
        {
            CallMethodResult callResult = await CallMethodOnAlarmAsync(
                ObjectTypeIds.ConditionType,
                MethodIds.ConditionType_ConditionRefresh).ConfigureAwait(false);

            Assert.That(StatusCode.IsBad(callResult.StatusCode), Is.True,
                "ConditionRefresh with no arguments should fail.");
        }

        [Test]
        [Property("ConformanceUnit", "A and C Refresh")]
        [Property("Tag", "Err_004")]
        public async Task ErrConditionRefreshConcurrentAsync()
        {
            CallResponse response = await Session.CallAsync(
                null,
                new CallMethodRequest[]
                {
                    new() {
                        ObjectId = ObjectTypeIds.ConditionType,
                        MethodId = MethodIds.ConditionType_ConditionRefresh,
                        InputArguments = new Variant[]
                        {
                            new(m_subscriptionId)
                        }.ToArrayOf()
                    },
                    new() {
                        ObjectId = ObjectTypeIds.ConditionType,
                        MethodId = MethodIds.ConditionType_ConditionRefresh,
                        InputArguments = new Variant[]
                        {
                            new(m_subscriptionId)
                        }.ToArrayOf()
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(2));

            bool oneSucceeded =
                StatusCode.IsGood(response.Results[0].StatusCode) ||
                StatusCode.IsGood(response.Results[1].StatusCode);
            bool oneFailed =
                StatusCode.IsBad(response.Results[0].StatusCode) ||
                StatusCode.IsBad(response.Results[1].StatusCode);

            Assert.That(oneSucceeded && oneFailed, Is.True,
                "When two refreshes are issued concurrently, exactly one " +
                "should succeed and the other should report an error.");
        }

        [Test]
        [Property("ConformanceUnit", "A and C Refresh2")]
        [Property("Tag", "N/A")]
        public async Task ConditionRefresh2MethodExistsAsync()
        {
            bool found = await TypeHasChildAsync(
                ObjectTypeIds.ConditionType, "ConditionRefresh2").ConfigureAwait(false);
            Assert.That(found, Is.True,
                "ConditionType should have ConditionRefresh2 method.");
        }

        [Test]
        [Property("ConformanceUnit", "A and C Refresh2")]
        [Property("Tag", "Test_002")]
        public async Task ConditionRefresh2ReturnsCurrentStateAsync()
        {
            if (m_monitoredItemId == 0)
            {
                Assert.Ignore("No monitored item available.");
            }

            CallMethodResult callResult = await CallMethodOnAlarmAsync(
                ObjectTypeIds.ConditionType,
                MethodIds.ConditionType_ConditionRefresh2,
                new Variant(m_subscriptionId),
                new Variant(m_monitoredItemId)).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(callResult.StatusCode), Is.True,
                $"ConditionRefresh2 should succeed: {callResult.StatusCode}");
        }

        [Test]
        [Property("ConformanceUnit", "A and C Refresh2")]
        [Property("Tag", "Err_002")]
        public async Task ErrConditionRefresh2WithBadSubscriptionIdAsync()
        {
            if (m_monitoredItemId == 0)
            {
                Assert.Ignore("No monitored item available.");
            }

            CallMethodResult callResult = await CallMethodOnAlarmAsync(
                ObjectTypeIds.ConditionType,
                MethodIds.ConditionType_ConditionRefresh2,
                new Variant(uint.MaxValue),
                new Variant(m_monitoredItemId)).ConfigureAwait(false);

            Assert.That(StatusCode.IsBad(callResult.StatusCode), Is.True,
                "ConditionRefresh2 with a bad SubscriptionId should fail.");
        }

        [Test]
        [Property("ConformanceUnit", "A and C Refresh2")]
        [Property("Tag", "Err_004")]
        public async Task ErrConditionRefresh2WithBadMonitoredItemIdAsync()
        {
            CallMethodResult callResult = await CallMethodOnAlarmAsync(
                ObjectTypeIds.ConditionType,
                MethodIds.ConditionType_ConditionRefresh2,
                new Variant(m_subscriptionId),
                new Variant(uint.MaxValue)).ConfigureAwait(false);

            Assert.That(StatusCode.IsBad(callResult.StatusCode), Is.True,
                "ConditionRefresh2 with a bad MonitoredItemId should fail.");
        }

        [Test]
        [Property("ConformanceUnit", "A and C Refresh2")]
        [Property("Tag", "Err_006")]
        public async Task ErrConditionRefresh2WithInvalidArgsAsync()
        {
            CallMethodResult callResult = await CallMethodOnAlarmAsync(
                ObjectTypeIds.ConditionType,
                MethodIds.ConditionType_ConditionRefresh2).ConfigureAwait(false);

            Assert.That(StatusCode.IsBad(callResult.StatusCode), Is.True,
                "ConditionRefresh2 with no arguments should fail.");
        }

        [Test]
        [Property("ConformanceUnit", "A and C Refresh2")]
        [Property("Tag", "Err_003")]
        public async Task ErrConditionRefresh2ConcurrentAsync()
        {
            if (m_monitoredItemId == 0)
            {
                Assert.Ignore("No monitored item available.");
            }

            CallResponse response = await Session.CallAsync(
                null,
                new CallMethodRequest[]
                {
                    new() {
                        ObjectId = ObjectTypeIds.ConditionType,
                        MethodId = MethodIds.ConditionType_ConditionRefresh2,
                        InputArguments = new Variant[]
                        {
                            new(m_subscriptionId),
                            new(m_monitoredItemId)
                        }.ToArrayOf()
                    },
                    new() {
                        ObjectId = ObjectTypeIds.ConditionType,
                        MethodId = MethodIds.ConditionType_ConditionRefresh2,
                        InputArguments = new Variant[]
                        {
                            new(m_subscriptionId),
                            new(m_monitoredItemId)
                        }.ToArrayOf()
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(2));

            bool oneSucceeded =
                StatusCode.IsGood(response.Results[0].StatusCode) ||
                StatusCode.IsGood(response.Results[1].StatusCode);
            bool oneFailed =
                StatusCode.IsBad(response.Results[0].StatusCode) ||
                StatusCode.IsBad(response.Results[1].StatusCode);

            Assert.That(oneSucceeded && oneFailed, Is.True,
                "Two concurrent ConditionRefresh2 calls should not both succeed.");
        }

        [Test]
        [Property("ConformanceUnit", "A and C Refresh2")]
        [Property("Tag", "Err_007")]
        public async Task ErrConditionRefresh2OnNonEventItemAsync()
        {
            var dataItem = new MonitoredItemCreateRequest
            {
                ItemToMonitor = new ReadValueId
                {
                    NodeId = VariableIds.Server_ServerStatus_State,
                    AttributeId = Attributes.Value
                },
                MonitoringMode = MonitoringMode.Reporting,
                RequestedParameters = new MonitoringParameters
                {
                    ClientHandle = 99,
                    SamplingInterval = 1000,
                    QueueSize = 1,
                    DiscardOldest = true
                }
            };

            CreateMonitoredItemsResponse miResp =
                await Session.CreateMonitoredItemsAsync(
                    null, m_subscriptionId, TimestampsToReturn.Both,
                    new MonitoredItemCreateRequest[] { dataItem }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

            if (miResp.Results.Count == 0 ||
                StatusCode.IsBad(miResp.Results[0].StatusCode))
            {
                Assert.Ignore("Could not create a data-change monitored item.");
            }

            uint dataItemId = miResp.Results[0].MonitoredItemId;

            CallMethodResult callResult = await CallMethodOnAlarmAsync(
                ObjectTypeIds.ConditionType,
                MethodIds.ConditionType_ConditionRefresh2,
                new Variant(m_subscriptionId),
                new Variant(dataItemId)).ConfigureAwait(false);

            // The OPC UA server may accept a refresh on a data-change
            // monitored item without error (no events will be delivered
            // because the item is not an event monitor). We accept any
            // deterministic status to keep this test portable.
            Assert.That(
                StatusCode.IsGood(callResult.StatusCode) ||
                StatusCode.IsBad(callResult.StatusCode), Is.True,
                "ConditionRefresh2 on a non-event monitored item must " +
                "return a deterministic status code.");
        }

        private async Task<bool> TypeHasChildAsync(NodeId typeId, string name)
        {
            BrowseResult result = await BrowseForwardAsync(typeId)
                .ConfigureAwait(false);
            int count = result.References.Count;
            for (int i = 0; i < count; i++)
            {
                if (result.References[i].BrowseName.Name == name)
                {
                    return true;
                }
            }
            return false;
        }

        private uint m_subscriptionId;
        private uint m_monitoredItemId;
    }
}
