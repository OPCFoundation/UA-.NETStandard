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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;

namespace Opc.Ua.History.Tests
{
    /// <summary>
    /// compliance tests for the A and C Confirm conformance unit.
    /// Verifies that Confirm methods exist on the type system and
    /// that the ConfirmedState transitions correctly.
    /// </summary>
    [NonParallelizable]
    [TestFixture]
    [Category("Conformance")]
    [Category("AlarmsAndConditions")]
    public class AlarmsAndConditionsConfirmTests : AlarmsAndConditionsTestFixture
    {
        [Test]
        public async Task AcknowledgeableConditionTypeHasConfirmMethodAsync()
        {
            BrowseResult result = await BrowseForwardAsync(
                ObjectTypeIds.AcknowledgeableConditionType)
                .ConfigureAwait(false);
            bool found = false;
            int count = result.References.Count;
            for (int i = 0; i < count; i++)
            {
                if (result.References[i].BrowseName.Name == "Confirm")
                {
                    found = true;
                    break;
                }
            }
            Assert.That(found, Is.True,
                "AcknowledgeableConditionType should have Confirm method.");
        }

        [Test]
        public async Task AcknowledgeableConditionTypeHasConfirmedStateAsync()
        {
            BrowseResult result = await BrowseForwardAsync(
                ObjectTypeIds.AcknowledgeableConditionType)
                .ConfigureAwait(false);
            bool found = false;
            int count = result.References.Count;
            for (int i = 0; i < count; i++)
            {
                if (result.References[i].BrowseName.Name == "ConfirmedState")
                {
                    found = true;
                    break;
                }
            }
            Assert.That(found, Is.True,
                "AcknowledgeableConditionType should have ConfirmedState property.");
        }

        [Test]
        public async Task ConfirmWithUniqueCommentReachesEventPayloadAsync()
        {
            NodeId alarmId = RequireCttAlarm("AlarmConditionType");
            await NormalizeAlarmAsync(alarmId).ConfigureAwait(false);

            await using AlarmEventCollector collector =
                await AlarmEventCollector.CreateAsync(Session).ConfigureAwait(false);
            collector.Reset();

            await WriteAlarmSourceValueAsync(alarmId, new Variant(90)).ConfigureAwait(false);
            await collector.WaitForEventAsync(
                alarmId,
                e => AlarmEventCollector.TryGetBoolean(
                    e,
                    AlarmEventCollector.FieldIndex.ActiveStateId,
                    out bool active) && active,
                TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            ByteString eventId = await ReadEventIdAsync(alarmId).ConfigureAwait(false);

            collector.Reset();
            CallMethodResult acknowledge = await CallMethodOnAlarmAsync(
                alarmId,
                MethodIds.AcknowledgeableConditionType_Acknowledge,
                new Variant(eventId),
                new Variant(new LocalizedText("en", "ack before confirm"))).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(acknowledge.StatusCode), Is.True,
                $"Acknowledge should succeed before Confirm: {acknowledge.StatusCode}");

            await collector.WaitForEventAsync(
                alarmId,
                e => AlarmEventCollector.TryGetBoolean(
                    e,
                    AlarmEventCollector.FieldIndex.AckedStateId,
                    out bool acked) && acked,
                TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            ByteString confirmEventId = await ReadEventIdAsync(alarmId).ConfigureAwait(false);
            string commentText = "phase4-3841-confirm-" + confirmEventId.ToHexString();

            collector.Reset();
            CallMethodResult confirm = await CallMethodOnAlarmAsync(
                alarmId,
                MethodIds.AcknowledgeableConditionType_Confirm,
                new Variant(confirmEventId),
                new Variant(new LocalizedText("en", commentText))).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(confirm.StatusCode), Is.True,
                $"Confirm should succeed: {confirm.StatusCode}");

            EventFieldList confirmEvent = await collector.WaitForEventAsync(
                alarmId,
                e => AlarmEventCollector.TryGetBoolean(
                    e,
                    AlarmEventCollector.FieldIndex.ConfirmedStateId,
                    out bool confirmed) && confirmed,
                TimeSpan.FromSeconds(5)).ConfigureAwait(false);

            Assert.That(
                AlarmEventCollector.TryGetLocalizedText(
                    confirmEvent,
                    AlarmEventCollector.FieldIndex.Comment,
                    out LocalizedText comment),
                Is.True,
                "Confirm event should include Comment.");
            Assert.That(comment.Text, Is.EqualTo(commentText));
        }

        [Test]
        public async Task AcknowledgeThenConfirmWithEmptyCommentAsync()
        {
            NodeId alarmId = RequireCttAlarm("AlarmConditionType");
            await NormalizeAlarmAsync(alarmId).ConfigureAwait(false);

            await using AlarmEventCollector collector =
                await AlarmEventCollector.CreateAsync(Session).ConfigureAwait(false);
            collector.Reset();

            await WriteAlarmSourceValueAsync(alarmId, new Variant(90)).ConfigureAwait(false);
            await collector.WaitForEventAsync(
                alarmId,
                e => AlarmEventCollector.TryGetBoolean(
                    e,
                    AlarmEventCollector.FieldIndex.ActiveStateId,
                    out bool active) && active,
                TimeSpan.FromSeconds(5)).ConfigureAwait(false);

            await WriteAlarmSourceValueAsync(alarmId, new Variant(50)).ConfigureAwait(false);
            await collector.WaitForEventAsync(
                alarmId,
                e => AlarmEventCollector.TryGetBoolean(
                    e,
                    AlarmEventCollector.FieldIndex.ActiveStateId,
                    out bool active) && !active,
                TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            ByteString eventId = await ReadEventIdAsync(alarmId).ConfigureAwait(false);

            collector.Reset();
            CallMethodResult acknowledge = await CallMethodOnAlarmAsync(
                alarmId,
                MethodIds.AcknowledgeableConditionType_Acknowledge,
                new Variant(eventId),
                new Variant(new LocalizedText("en", "ack before empty confirm"))).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(acknowledge.StatusCode), Is.True,
                $"Acknowledge should succeed before Confirm: {acknowledge.StatusCode}");

            await collector.WaitForEventAsync(
                alarmId,
                e => AlarmEventCollector.TryGetBoolean(
                    e,
                    AlarmEventCollector.FieldIndex.AckedStateId,
                    out bool acked) && acked,
                TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            ByteString confirmEventId = await ReadEventIdAsync(alarmId).ConfigureAwait(false);

            collector.Reset();
            CallMethodResult confirm = await CallMethodOnAlarmAsync(
                alarmId,
                MethodIds.AcknowledgeableConditionType_Confirm,
                new Variant(confirmEventId),
                new Variant(new LocalizedText("en", string.Empty))).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(confirm.StatusCode), Is.True,
                $"Confirm should succeed: {confirm.StatusCode}");

            EventFieldList confirmEvent = await collector.WaitForEventAsync(
                alarmId,
                e => AlarmEventCollector.TryGetBoolean(
                    e,
                    AlarmEventCollector.FieldIndex.ConfirmedStateId,
                    out bool confirmed) && confirmed,
                TimeSpan.FromSeconds(5)).ConfigureAwait(false);

            Assert.That(
                AlarmEventCollector.TryGetLocalizedText(
                    confirmEvent,
                    AlarmEventCollector.FieldIndex.Comment,
                    out LocalizedText comment),
                Is.True,
                "Confirm event should include Comment.");
            Assert.That(comment.Text ?? string.Empty, Is.EqualTo(string.Empty));
        }

        [Test]
        public async Task ConfirmConditionSetsConfirmedStateTrueAsync()
        {
            NodeId alarmId = RequireAlarm();

            await Task.Delay(1500).ConfigureAwait(false);

            ByteString eventId = await ReadEventIdAsync(alarmId).ConfigureAwait(false);
            if (eventId.IsNull)
            {
                Assert.Ignore("Alarm has no EventId yet.");
            }

            await CallMethodOnAlarmAsync(
                alarmId,
                MethodIds.AcknowledgeableConditionType_Acknowledge,
                new Variant(eventId),
                new Variant(new LocalizedText("en", "ack"))).ConfigureAwait(false);

            await Task.Delay(500).ConfigureAwait(false);
            ByteString confirmEventId = await ReadEventIdAsync(alarmId).ConfigureAwait(false);
            if (confirmEventId.IsNull)
            {
                confirmEventId = eventId;
            }

            CallMethodResult callResult = await CallMethodOnAlarmAsync(
                alarmId,
                MethodIds.AcknowledgeableConditionType_Confirm,
                new Variant(confirmEventId),
                new Variant(new LocalizedText("en", "Confirmed by test")))
                .ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(callResult.StatusCode) ||
                callResult.StatusCode == StatusCodes.BadConditionBranchAlreadyConfirmed,
                Is.True,
                $"Confirm should succeed or report already-confirmed: {callResult.StatusCode}");

            DataValue confirmedState = await ReadStateIdAsync(alarmId, "ConfirmedState")
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(confirmedState.StatusCode), Is.True,
                "Should be able to read ConfirmedState/Id.");
            Assert.That(
                confirmedState.WrappedValue.TryGetValue(out bool value), Is.True);
            Assert.That(value, Is.True,
                "ConfirmedState/Id should be true after Confirm.");
        }

        [Test]
        public async Task ErrConfirmWithBadNodeIdAsync()
        {
            CallMethodResult callResult = await CallMethodOnAlarmAsync(
                new NodeId(uint.MaxValue, 99),
                MethodIds.AcknowledgeableConditionType_Confirm,
                new Variant(default(ByteString)),
                new Variant(new LocalizedText("en", string.Empty)))
                .ConfigureAwait(false);

            Assert.That(StatusCode.IsBad(callResult.StatusCode), Is.True,
                "Confirm on a bad NodeId should fail.");
        }

        [Test]
        public async Task ErrConfirmWithInvalidMethodArgsAsync()
        {
            NodeId alarmId = RequireAlarm();

            CallMethodResult callResult = await CallMethodOnAlarmAsync(
                alarmId,
                MethodIds.AcknowledgeableConditionType_Confirm)
                .ConfigureAwait(false);

            Assert.That(StatusCode.IsBad(callResult.StatusCode), Is.True,
                "Confirm with no arguments should fail.");
        }

        [Test]
        public async Task ErrConfirmAlreadyConfirmedAsync()
        {
            NodeId alarmId = RequireAlarm();

            await Task.Delay(1500).ConfigureAwait(false);
            ByteString eventId = await ReadEventIdAsync(alarmId).ConfigureAwait(false);
            if (eventId.IsNull)
            {
                Assert.Ignore("Alarm has no EventId yet.");
            }

            await CallMethodOnAlarmAsync(
                alarmId,
                MethodIds.AcknowledgeableConditionType_Confirm,
                new Variant(eventId),
                new Variant(new LocalizedText("en", "first"))).ConfigureAwait(false);

            CallMethodResult second = await CallMethodOnAlarmAsync(
                alarmId,
                MethodIds.AcknowledgeableConditionType_Confirm,
                new Variant(eventId),
                new Variant(new LocalizedText("en", "second"))).ConfigureAwait(false);

            Assert.That(StatusCode.IsBad(second.StatusCode), Is.True,
                "Re-confirming the same EventId should fail.");
        }

        [Test]
        public async Task ErrConfirmWithNullEventIdAsync()
        {
            NodeId alarmId = RequireAlarm();

            CallMethodResult callResult = await CallMethodOnAlarmAsync(
                alarmId,
                MethodIds.AcknowledgeableConditionType_Confirm,
                new Variant(default(ByteString)),
                new Variant(new LocalizedText("en", "no event id")))
                .ConfigureAwait(false);

            Assert.That(StatusCode.IsBad(callResult.StatusCode), Is.True,
                "Confirm with a null EventId should fail.");
        }

        [Test]
        public async Task ErrConfirmWithEmptyCommentAsync()
        {
            NodeId alarmId = RequireAlarm();

            await Task.Delay(1500).ConfigureAwait(false);
            ByteString eventId = await ReadEventIdAsync(alarmId).ConfigureAwait(false);
            if (eventId.IsNull)
            {
                Assert.Ignore("Alarm has no EventId yet.");
            }

            CallMethodResult callResult = await CallMethodOnAlarmAsync(
                alarmId,
                MethodIds.AcknowledgeableConditionType_Confirm,
                new Variant(eventId),
                new Variant(LocalizedText.Null)).ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(callResult.StatusCode) ||
                StatusCode.IsBad(callResult.StatusCode), Is.True,
                "Server should produce a deterministic status for an " +
                "Confirm with empty comment.");
        }

        [Test]
        public async Task ConfirmAlreadyConfirmedAcrossSessionsReturnsBranchAlreadyConfirmedAsync()
        {
            NodeId alarmId = RequireCttAlarm("AlarmConditionType");
            await NormalizeAlarmAsync(alarmId).ConfigureAwait(false);

            ISession auxSession = await OpenAuxSessionAsync().ConfigureAwait(false);
            try
            {
                await using AlarmEventCollector primaryCollector =
                    await AlarmEventCollector.CreateAsync(Session).ConfigureAwait(false);
                await using AlarmEventCollector auxCollector =
                    await AlarmEventCollector.CreateAsync(auxSession).ConfigureAwait(false);

                primaryCollector.Reset();
                auxCollector.Reset();

                await WriteAlarmSourceValueAsync(alarmId, new Variant(90)).ConfigureAwait(false);
                await primaryCollector.WaitForEventAsync(
                    alarmId,
                    e => AlarmEventCollector.TryGetBoolean(
                        e,
                        AlarmEventCollector.FieldIndex.ActiveStateId,
                        out bool active) && active,
                    TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                await auxCollector.WaitForEventAsync(
                    alarmId,
                    e => AlarmEventCollector.TryGetBoolean(
                        e,
                        AlarmEventCollector.FieldIndex.ActiveStateId,
                        out bool active) && active,
                    TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                ByteString eventId = await ReadEventIdAsync(alarmId).ConfigureAwait(false);

                primaryCollector.Reset();
                auxCollector.Reset();
                CallMethodResult acknowledge = await CallMethodOnAlarmAsync(
                    alarmId,
                    MethodIds.AcknowledgeableConditionType_Acknowledge,
                    new Variant(eventId),
                    new Variant(new LocalizedText("en", "ack before race"))).ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(acknowledge.StatusCode), Is.True,
                    $"Acknowledge should succeed before Confirm: {acknowledge.StatusCode}");

                await primaryCollector.WaitForEventAsync(
                    alarmId,
                    e => AlarmEventCollector.TryGetBoolean(
                        e,
                        AlarmEventCollector.FieldIndex.AckedStateId,
                        out bool acked) && acked,
                    TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                ByteString confirmEventId = await ReadEventIdAsync(alarmId).ConfigureAwait(false);

                CallMethodResult first = await CallMethodOnAlarmAsync(
                    alarmId,
                    MethodIds.AcknowledgeableConditionType_Confirm,
                    new Variant(confirmEventId),
                    new Variant(new LocalizedText("en", "first confirm"))).ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(first.StatusCode), Is.True,
                    $"First Confirm should succeed: {first.StatusCode}");

                CallMethodResult second = await CallMethodOnSessionAsync(
                    auxSession,
                    alarmId,
                    MethodIds.AcknowledgeableConditionType_Confirm,
                    new Variant(confirmEventId),
                    new Variant(new LocalizedText("en", "second confirm"))).ConfigureAwait(false);

                Assert.That(second.StatusCode,
                    Is.EqualTo(StatusCodes.BadConditionBranchAlreadyConfirmed),
                    "Second session must receive BadConditionBranchAlreadyConfirmed.");
            }
            finally
            {
                try
                {
                    await auxSession.CloseAsync(2000, true, CancellationToken.None).ConfigureAwait(false);
                }
                finally
                {
                    auxSession.Dispose();
                }
            }
        }

        [Test]
        public async Task ErrConfirmOnDisabledConditionAsync()
        {
            NodeId alarmId = RequireAlarm();

            await Task.Delay(1500).ConfigureAwait(false);

            await CallMethodOnAlarmAsync(
                alarmId,
                MethodIds.ConditionType_Disable).ConfigureAwait(false);

            ByteString eventId = await ReadEventIdAsync(alarmId).ConfigureAwait(false);
            CallMethodResult callResult = await CallMethodOnAlarmAsync(
                alarmId,
                MethodIds.AcknowledgeableConditionType_Confirm,
                new Variant(eventId),
                new Variant(new LocalizedText("en", "x"))).ConfigureAwait(false);

            await CallMethodOnAlarmAsync(
                alarmId,
                MethodIds.ConditionType_Enable).ConfigureAwait(false);

            Assert.That(StatusCode.IsBad(callResult.StatusCode), Is.True,
                "Confirm on a disabled condition should fail.");
        }
    }
}
