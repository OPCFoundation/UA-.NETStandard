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
using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.History.Tests
{
    /// <summary>
    /// compliance tests for the A and C Base conformance unit.
    /// Covers Limit, Refresh, and Discrete sub-conformance units verifying
    /// that alarm and condition types and their properties exist in the
    /// address space.
    /// </summary>
    [NonParallelizable]
    [TestFixture]
    [Category("Conformance")]
    [Category("AlarmsAndConditions")]
    public class AlarmsAndConditionsBaseTests : AlarmsAndConditionsTestFixture
    {
        [Test]
        public async Task LimitAlarmTypeExistsAsync()
        {
            DataValue dv = await ReadAttributeAsync(
                ObjectTypeIds.LimitAlarmType,
                Attributes.BrowseName).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(dv.StatusCode), Is.True,
                "LimitAlarmType should exist in the address space.");
        }

        [Test]
        public async Task LimitAlarmTypeIsSubtypeOfAlarmConditionTypeAsync()
        {
            await VerifySubtypeOfAsync(
                ObjectTypeIds.LimitAlarmType,
                ObjectTypeIds.AlarmConditionType).ConfigureAwait(false);
        }

        [Test]
        public async Task LimitAlarmTypeHasHighHighLimitAsync()
        {
            bool found = await TypeHasChildAsync(
                ObjectTypeIds.LimitAlarmType, "HighHighLimit").ConfigureAwait(false);
            Assert.That(found, Is.True,
                "LimitAlarmType should have HighHighLimit property.");
        }

        [Test]
        public async Task LimitAlarmTypeHasHighLimitAsync()
        {
            bool found = await TypeHasChildAsync(
                ObjectTypeIds.LimitAlarmType, "HighLimit").ConfigureAwait(false);
            Assert.That(found, Is.True,
                "LimitAlarmType should have HighLimit property.");
        }

        [Test]
        public async Task LimitAlarmTypeHasLowLimitAsync()
        {
            bool found = await TypeHasChildAsync(
                ObjectTypeIds.LimitAlarmType, "LowLimit").ConfigureAwait(false);
            Assert.That(found, Is.True,
                "LimitAlarmType should have LowLimit property.");
        }

        [Test]
        public async Task LimitAlarmTypeHasLowLowLimitAsync()
        {
            bool found = await TypeHasChildAsync(
                ObjectTypeIds.LimitAlarmType, "LowLowLimit").ConfigureAwait(false);
            Assert.That(found, Is.True,
                "LimitAlarmType should have LowLowLimit property.");
        }

        [Test]
        public async Task ExclusiveAndNonExclusiveLimitAlarmTypesExistAsync()
        {
            DataValue dvExcl = await ReadAttributeAsync(
                ObjectTypeIds.ExclusiveLimitAlarmType,
                Attributes.BrowseName).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(dvExcl.StatusCode), Is.True,
                "ExclusiveLimitAlarmType should exist.");

            await VerifySubtypeOfAsync(
                ObjectTypeIds.ExclusiveLimitAlarmType,
                ObjectTypeIds.LimitAlarmType).ConfigureAwait(false);

            DataValue dvNonExcl = await ReadAttributeAsync(
                ObjectTypeIds.NonExclusiveLimitAlarmType,
                Attributes.BrowseName).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(dvNonExcl.StatusCode), Is.True,
                "NonExclusiveLimitAlarmType should exist.");

            await VerifySubtypeOfAsync(
                ObjectTypeIds.NonExclusiveLimitAlarmType,
                ObjectTypeIds.LimitAlarmType).ConfigureAwait(false);
        }

        [Test]
        public async Task ConditionTypeExistsAsync()
        {
            DataValue dv = await ReadAttributeAsync(
                ObjectTypeIds.ConditionType,
                Attributes.BrowseName).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(dv.StatusCode), Is.True,
                "ConditionType should exist in the address space.");
        }

        [Test]
        public async Task ConditionTypeHasConditionRefreshMethodAsync()
        {
            bool found = await TypeHasChildAsync(
                ObjectTypeIds.ConditionType, "ConditionRefresh")
                .ConfigureAwait(false);
            Assert.That(found, Is.True,
                "ConditionType should have ConditionRefresh method.");
        }

        [Test]
        public async Task ConditionTypeHasConditionRefresh2MethodAsync()
        {
            bool found = await TypeHasChildAsync(
                ObjectTypeIds.ConditionType, "ConditionRefresh2")
                .ConfigureAwait(false);
            Assert.That(found, Is.True,
                "ConditionType should have ConditionRefresh2 method.");
        }

        [Test]
        public async Task ConditionTypeHasConditionNameAsync()
        {
            bool found = await TypeHasChildAsync(
                ObjectTypeIds.ConditionType, "ConditionName")
                .ConfigureAwait(false);
            Assert.That(found, Is.True,
                "ConditionType should have ConditionName property.");
        }

        [Test]
        public async Task ConditionTypeHasBranchIdAsync()
        {
            bool found = await TypeHasChildAsync(
                ObjectTypeIds.ConditionType, "BranchId")
                .ConfigureAwait(false);
            Assert.That(found, Is.True,
                "ConditionType should have BranchId property.");
        }

        [Test]
        public async Task ConditionTypeHasEnabledStateAsync()
        {
            bool found = await TypeHasChildAsync(
                ObjectTypeIds.ConditionType, "EnabledState")
                .ConfigureAwait(false);
            Assert.That(found, Is.True,
                "ConditionType should have EnabledState property.");
        }

        [Test]
        public async Task ConditionRefreshSubscriptionEventTestAsync()
        {
            uint subscriptionId = await CreateEventSubscriptionAsync()
                .ConfigureAwait(false);
            try
            {
                CallMethodResult callResult = await CallMethodOnAlarmAsync(
                    ObjectTypeIds.ConditionType,
                    MethodIds.ConditionType_ConditionRefresh,
                    new Variant(subscriptionId)).ConfigureAwait(false);

                Assert.That(StatusCode.IsGood(callResult.StatusCode), Is.True,
                    $"ConditionRefresh on a valid subscription should succeed: {callResult.StatusCode}");
            }
            finally
            {
                await DeleteSubscriptionAsync(subscriptionId).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task ConditionRefreshReturnsEventsAsync()
        {
            uint subscriptionId = await CreateEventSubscriptionAsync()
                .ConfigureAwait(false);
            try
            {
                CallMethodResult callResult = await CallMethodOnAlarmAsync(
                    ObjectTypeIds.ConditionType,
                    MethodIds.ConditionType_ConditionRefresh,
                    new Variant(subscriptionId)).ConfigureAwait(false);

                Assert.That(StatusCode.IsGood(callResult.StatusCode), Is.True,
                    $"ConditionRefresh should return Good: {callResult.StatusCode}");

                PublishResponse pubResp = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(pubResp.ResponseHeader.ServiceResult),
                    Is.True);
            }
            finally
            {
                await DeleteSubscriptionAsync(subscriptionId).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task DiscreteAlarmTypeExistsAsync()
        {
            DataValue dv = await ReadAttributeAsync(
                ObjectTypeIds.DiscreteAlarmType,
                Attributes.BrowseName).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(dv.StatusCode), Is.True,
                "DiscreteAlarmType should exist in the address space.");
        }

        [Test]
        public async Task DiscreteAlarmTypeIsSubtypeOfAlarmConditionAsync()
        {
            await VerifySubtypeOfAsync(
                ObjectTypeIds.DiscreteAlarmType,
                ObjectTypeIds.AlarmConditionType).ConfigureAwait(false);
        }

        [Test]
        public async Task OffNormalAlarmTypeExistsAsync()
        {
            DataValue dv = await ReadAttributeAsync(
                ObjectTypeIds.OffNormalAlarmType,
                Attributes.BrowseName).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(dv.StatusCode), Is.True,
                "OffNormalAlarmType should exist in the address space.");

            await VerifySubtypeOfAsync(
                ObjectTypeIds.OffNormalAlarmType,
                ObjectTypeIds.DiscreteAlarmType).ConfigureAwait(false);
        }

        private async Task<uint> CreateEventSubscriptionAsync()
        {
            CreateSubscriptionResponse subResp =
                await Session.CreateSubscriptionAsync(
                    null, 1000, 100, 10, 0, true, 0,
                    CancellationToken.None).ConfigureAwait(false);

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
                    QueueSize = 10,
                    DiscardOldest = true
                }
            };

            await Session.CreateMonitoredItemsAsync(
                null, subResp.SubscriptionId, TimestampsToReturn.Both,
                new MonitoredItemCreateRequest[] { item }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            return subResp.SubscriptionId;
        }

        private async Task DeleteSubscriptionAsync(uint subscriptionId)
        {
            try
            {
                await Session.DeleteSubscriptionsAsync(
                    null,
                    new uint[] { subscriptionId }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (ServiceResultException)
            {
                // Already deleted
            }
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

        private async Task VerifySubtypeOfAsync(
            NodeId typeId, NodeId expectedParent)
        {
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = typeId,
                        BrowseDirection = BrowseDirection.Inverse,
                        ReferenceTypeId = ReferenceTypeIds.HasSubtype,
                        IncludeSubtypes = false,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            bool found = false;
            int count = response.Results[0].References.Count;
            for (int i = 0; i < count; i++)
            {
                NodeId parentId = ToNodeId(response.Results[0].References[i].NodeId);
                if (parentId == expectedParent)
                {
                    found = true;
                    break;
                }
            }

            Assert.That(found, Is.True,
                $"Type {typeId} should be a subtype of {expectedParent}.");
        }
    }
}
