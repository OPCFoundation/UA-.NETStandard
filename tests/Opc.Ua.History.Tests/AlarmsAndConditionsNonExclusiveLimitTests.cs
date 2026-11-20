/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.History.Tests
{
    /// <summary>
    /// Event-driven parity tests for the A and C Non-Exclusive Limit
    /// conformance unit.
    /// </summary>
    [NonParallelizable]
    [TestFixture]
    [Category("Conformance")]
    [Category("AlarmsAndConditions")]
    public class AlarmsAndConditionsNonExclusiveLimitTests : AlarmsAndConditionsTestFixture
    {
        [Test]
        public async Task NonExclusiveLimitAlarmActiveNormalCycleAsync()
        {
            NodeId alarmId = RequireCttAlarm("NonExclusiveLimitAlarmType");
            await NormalizeAlarmAsync(alarmId).ConfigureAwait(false);

            await using AlarmEventCollector collector =
                await AlarmEventCollector.CreateAsync(Session).ConfigureAwait(false);
            collector.Reset();

            await WriteAlarmSourceValueAsync(alarmId, new Variant(90)).ConfigureAwait(false);
            await collector.ConditionRefreshAsync().ConfigureAwait(false);
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
            await collector.ConditionRefreshAsync().ConfigureAwait(false);
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
                new Variant(new LocalizedText("en", "non-exclusive ack"))).ConfigureAwait(false);
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

            collector.Reset();
            CallMethodResult confirm = await CallMethodOnAlarmAsync(
                alarmId,
                MethodIds.AcknowledgeableConditionType_Confirm,
                new Variant(confirmEventId),
                new Variant(new LocalizedText("en", "non-exclusive confirm"))).ConfigureAwait(false);
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
                "NonExclusiveLimitAlarm should remain normal after Acknowledge and Confirm.");
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
