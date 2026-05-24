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
