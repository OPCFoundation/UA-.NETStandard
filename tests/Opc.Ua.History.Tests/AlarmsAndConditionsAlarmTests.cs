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

namespace Opc.Ua.History.Tests
{
    /// <summary>
    /// compliance tests for the A and C Alarm conformance unit.
    /// Verifies that AlarmConditionType, AlarmGroupType, and
    /// AlarmSuppressionGroupType exist and have the correct hierarchy.
    /// </summary>
    [NonParallelizable]
    [TestFixture]
    [Category("Conformance")]
    [Category("AlarmsAndConditions")]
    public class AlarmsAndConditionsAlarmTests : AlarmsAndConditionsTestFixture
    {
        [Test]
        public async Task AlarmConditionTypeExistsAsync()
        {
            DataValue dv = await ReadBrowseNameAsync(
                ObjectTypeIds.AlarmConditionType).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(dv.StatusCode), Is.True,
                "AlarmConditionType should exist in the address space.");
        }

        [Test]
        public async Task AlarmConditionIsSubtypeOfAcknowledgeableAsync()
        {
            await VerifySubtypeOfAsync(
                ObjectTypeIds.AlarmConditionType,
                ObjectTypeIds.AcknowledgeableConditionType)
                .ConfigureAwait(false);
        }

        [Test]
        public async Task AlarmGroupTypeExistsAsync()
        {
            DataValue dv = await ReadBrowseNameAsync(
                ObjectTypeIds.AlarmGroupType).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(dv.StatusCode), Is.True,
                "AlarmGroupType should exist in the address space.");
        }

        [Test]
        public async Task AlarmGroupTypeIsSubtypeOfFolderTypeAsync()
        {
            await VerifySubtypeOfAsync(
                ObjectTypeIds.AlarmGroupType,
                ObjectTypeIds.FolderType).ConfigureAwait(false);
        }

        [Test]
        public async Task AlarmSuppressionGroupTypeExistsAsync()
        {
            DataValue dv = await ReadBrowseNameAsync(
                ObjectTypeIds.AlarmSuppressionGroupType)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(dv.StatusCode), Is.True,
                "AlarmSuppressionGroupType should exist.");
        }

        [Test]
        public async Task AlarmActiveNormalCycleWithAckAndConfirmAsync()
        {
            NodeId alarmId = RequireCttAlarm("AlarmConditionType");
            await NormalizeAlarmAsync(alarmId).ConfigureAwait(false);

            await using AlarmEventCollector collector =
                await AlarmEventCollector.CreateAsync(Session).ConfigureAwait(false);
            collector.Reset();

            await WriteAlarmSourceValueAsync(alarmId, new Variant(90)).ConfigureAwait(false);
            EventFieldList activeEvent = await collector.WaitForEventAsync(
                alarmId,
                e => AlarmEventCollector.TryGetBoolean(
                    e,
                    AlarmEventCollector.FieldIndex.ActiveStateId,
                    out bool active) &&
                    active,
                DefaultEventWaitTimeout).ConfigureAwait(false);
            Assert.That(
                AlarmEventCollector.TryGetBoolean(
                    activeEvent,
                    AlarmEventCollector.FieldIndex.AckedStateId,
                    out bool acked),
                Is.True,
                "Active transition event should include AckedState/Id.");
            Assert.That(acked, Is.False,
                "Active transition should require acknowledgement.");

            await WriteAlarmSourceValueAsync(alarmId, new Variant(50)).ConfigureAwait(false);
            EventFieldList normalEvent = await collector.WaitForEventAsync(
                alarmId,
                e => AlarmEventCollector.TryGetBoolean(
                    e,
                    AlarmEventCollector.FieldIndex.ActiveStateId,
                    out bool active) &&
                    !active,
                DefaultEventWaitTimeout).ConfigureAwait(false);
            ByteString normalEventId = GetEventIdOrInconclusive(normalEvent);

            collector.Reset();
            CallMethodResult acknowledge = await CallMethodOnAlarmAsync(
                alarmId,
                MethodIds.AcknowledgeableConditionType_Acknowledge,
                new Variant(normalEventId),
                new Variant(new LocalizedText("en", "alarm cycle ack"))).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(acknowledge.StatusCode), Is.True,
                $"Acknowledge should succeed: {acknowledge.StatusCode}");

            EventFieldList ackEvent = await collector.WaitForEventAsync(
                alarmId,
                e => AlarmEventCollector.TryGetBoolean(
                    e,
                    AlarmEventCollector.FieldIndex.AckedStateId,
                    out bool ackedState) &&
                    ackedState,
                DefaultEventWaitTimeout).ConfigureAwait(false);
            ByteString confirmEventId = GetEventIdOrInconclusive(ackEvent);
            Assert.That(
                AlarmEventCollector.TryGetBoolean(
                    ackEvent,
                    AlarmEventCollector.FieldIndex.Retain,
                    out bool retainAfterAcknowledge),
                Is.True,
                "Acknowledge event should include Retain.");
            Assert.That(
                retainAfterAcknowledge,
                Is.True,
                "Retain stays set while confirmation is still outstanding.");

            collector.Reset();
            CallMethodResult confirm = await CallMethodOnAlarmAsync(
                alarmId,
                MethodIds.AcknowledgeableConditionType_Confirm,
                new Variant(confirmEventId),
                new Variant(new LocalizedText("en", "alarm cycle confirm"))).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(confirm.StatusCode), Is.True,
                $"Confirm should succeed: {confirm.StatusCode}");

            EventFieldList confirmEvent = await collector.WaitForEventAsync(
                alarmId,
                e => AlarmEventCollector.TryGetBoolean(
                    e,
                    AlarmEventCollector.FieldIndex.ConfirmedStateId,
                    out bool confirmed) &&
                    confirmed,
                DefaultEventWaitTimeout).ConfigureAwait(false);

            Assert.That(
                AlarmEventCollector.TryGetBoolean(
                    confirmEvent,
                    AlarmEventCollector.FieldIndex.ActiveStateId,
                    out bool finalActive),
                Is.True,
                "Confirm event should include ActiveState/Id.");
            Assert.That(finalActive, Is.False,
                "Alarm should remain normal after Acknowledge and Confirm.");
            Assert.That(
                AlarmEventCollector.TryGetBoolean(
                    confirmEvent,
                    AlarmEventCollector.FieldIndex.Retain,
                    out bool retainAfterConfirm),
                Is.True,
                "Confirm event should include Retain.");
            Assert.That(
                retainAfterConfirm,
                Is.True,
                "The main condition remains retained while its prior active branch is outstanding.");
        }

        [Test]
        public async Task EveryAlarmInputNodeIsReadableAsync()
        {
            int alarmCount = 0;
            foreach (KeyValuePair<string, NodeId> alarm in AlarmInstances)
            {
                BrowseResult alarmChildren = await BrowseForwardAsync(alarm.Value)
                    .ConfigureAwait(false);
                ReferenceDescription? inputNodeReference = null;
                foreach (ReferenceDescription reference in alarmChildren.References)
                {
                    if (reference.BrowseName.Name == BrowseNames.InputNode)
                    {
                        inputNodeReference = reference;
                        break;
                    }
                }
                if (inputNodeReference == null)
                {
                    continue;
                }
                alarmCount++;

                DataValue inputNodeValue = await ReadAttributeAsync(
                    ToNodeId(inputNodeReference.NodeId),
                    Attributes.Value).ConfigureAwait(false);
                Assert.That(
                    StatusCode.IsGood(inputNodeValue.StatusCode),
                    Is.True,
                    $"Reading InputNode failed for {alarm.Key}.");
                Assert.That(
                    inputNodeValue.WrappedValue.TryGetValue(out NodeId inputNode),
                    Is.True,
                    $"InputNode was not a NodeId for {alarm.Key}.");
                Assert.That(
                    inputNode.IsNull,
                    Is.False,
                    $"InputNode was null for {alarm.Key}.");

                DataValue sourceValue = await ReadAttributeAsync(
                    inputNode,
                    Attributes.Value).ConfigureAwait(false);
                Assert.That(
                    StatusCode.IsGood(sourceValue.StatusCode),
                    Is.True,
                    $"Reading input node {inputNode} failed for {alarm.Key}: " +
                    sourceValue.StatusCode);
            }
            Assert.That(alarmCount, Is.GreaterThan(0));
        }

        private async Task<DataValue> ReadBrowseNameAsync(NodeId nodeId)
        {
            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.BrowseName
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            return response.Results[0];
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
            foreach (ReferenceDescription r in response.Results[0].References)
            {
                NodeId parentId = ToNodeId(r.NodeId);
                if (parentId == expectedParent)
                {
                    found = true;
                    break;
                }
            }

            Assert.That(found, Is.True,
                $"Type {typeId} should be a subtype of {expectedParent}.");
        }

        private static ByteString GetEventIdOrInconclusive(EventFieldList eventFields)
        {
            if (AlarmEventCollector.TryGetByteString(
                eventFields,
                AlarmEventCollector.FieldIndex.EventId,
                out ByteString eventId) &&
                !eventId.IsNull)
            {
                return eventId;
            }

            Assert.Inconclusive("The alarm event did not include EventId.");
            return default;
        }
    }
}
