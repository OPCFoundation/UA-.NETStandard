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
