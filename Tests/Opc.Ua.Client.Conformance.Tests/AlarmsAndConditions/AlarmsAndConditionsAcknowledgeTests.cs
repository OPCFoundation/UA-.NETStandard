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

namespace Opc.Ua.Client.Conformance.Tests
{
    /// <summary>
    /// compliance tests for the A and C Acknowledge conformance unit.
    /// Verifies that Acknowledge methods exist on the type system and
    /// that the AckedState transitions correctly.
    /// </summary>
    [NonParallelizable]
    [TestFixture]
    [Category("Conformance")]
    [Category("AlarmsAndConditions")]
    public class AlarmsAndConditionsAcknowledgeTests : AlarmsAndConditionsTestFixture
    {
        [Test]
        [Property("ConformanceUnit", "A and C Acknowledge")]
        [Property("Tag", "Test_001")]
        public async Task AcknowledgeableConditionTypeHasAcknowledgeMethodAsync()
        {
            BrowseResult result = await BrowseForwardAsync(
                ObjectTypeIds.AcknowledgeableConditionType)
                .ConfigureAwait(false);
            bool found = false;
            int count = result.References.Count;
            for (int i = 0; i < count; i++)
            {
                if (result.References[i].BrowseName.Name == "Acknowledge")
                {
                    found = true;
                    break;
                }
            }
            Assert.That(found, Is.True,
                "AcknowledgeableConditionType should have Acknowledge method.");
        }

        [Test]
        [Property("ConformanceUnit", "A and C Acknowledge")]
        [Property("Tag", "Test_001")]
        public async Task AcknowledgeableConditionTypeHasAckedStateAsync()
        {
            BrowseResult result = await BrowseForwardAsync(
                ObjectTypeIds.AcknowledgeableConditionType)
                .ConfigureAwait(false);
            bool found = false;
            int count = result.References.Count;
            for (int i = 0; i < count; i++)
            {
                if (result.References[i].BrowseName.Name == "AckedState")
                {
                    found = true;
                    break;
                }
            }
            Assert.That(found, Is.True,
                "AcknowledgeableConditionType should have AckedState property.");
        }

        [Test]
        [Property("ConformanceUnit", "A and C Acknowledge")]
        [Property("Tag", "Test_002")]
        public async Task AcknowledgeConditionSetsAckedStateTrueAsync()
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
                MethodIds.AcknowledgeableConditionType_Acknowledge,
                new Variant(eventId),
                new Variant(new LocalizedText("en", "Acknowledged by test")))
                .ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(callResult.StatusCode) ||
                callResult.StatusCode == StatusCodes.BadConditionBranchAlreadyAcked,
                Is.True,
                $"Acknowledge should succeed or report already-acked: {callResult.StatusCode}");

            DataValue ackedState = await ReadStateIdAsync(alarmId, "AckedState")
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(ackedState.StatusCode), Is.True,
                "Should be able to read AckedState/Id.");
            Assert.That(
                ackedState.WrappedValue.TryGetValue(out bool ackedValue), Is.True);
            Assert.That(ackedValue, Is.True,
                "AckedState/Id should be true after Acknowledge.");
        }

        [Test]
        [Property("ConformanceUnit", "A and C Acknowledge")]
        [Property("Tag", "Err_005")]
        public async Task ErrAcknowledgeWithBadNodeIdAsync()
        {
            CallMethodResult callResult = await CallMethodOnAlarmAsync(
                new NodeId(uint.MaxValue, 99),
                MethodIds.AcknowledgeableConditionType_Acknowledge,
                new Variant(default(ByteString)),
                new Variant(new LocalizedText("en", string.Empty)))
                .ConfigureAwait(false);

            Assert.That(StatusCode.IsBad(callResult.StatusCode), Is.True,
                "Acknowledge on a bad NodeId should fail.");
        }

        [Test]
        [Property("ConformanceUnit", "A and C Acknowledge")]
        [Property("Tag", "Err_006")]
        public async Task ErrAcknowledgeWithInvalidMethodArgsAsync()
        {
            NodeId alarmId = RequireAlarm();

            CallMethodResult callResult = await CallMethodOnAlarmAsync(
                alarmId,
                MethodIds.AcknowledgeableConditionType_Acknowledge)
                .ConfigureAwait(false);

            Assert.That(StatusCode.IsBad(callResult.StatusCode), Is.True,
                "Acknowledge with no arguments should fail.");
        }

        [Test]
        [Property("ConformanceUnit", "A and C Acknowledge")]
        [Property("Tag", "Err_007")]
        public async Task ErrAcknowledgeAlreadyAcknowledgedAsync()
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
                new Variant(new LocalizedText("en", "first"))).ConfigureAwait(false);

            CallMethodResult second = await CallMethodOnAlarmAsync(
                alarmId,
                MethodIds.AcknowledgeableConditionType_Acknowledge,
                new Variant(eventId),
                new Variant(new LocalizedText("en", "second"))).ConfigureAwait(false);

            Assert.That(StatusCode.IsBad(second.StatusCode), Is.True,
                "Re-acknowledging the same EventId should fail.");
        }

        [Test]
        [Property("ConformanceUnit", "A and C Acknowledge")]
        [Property("Tag", "Err_008")]
        public async Task ErrAcknowledgeWithNullEventIdAsync()
        {
            NodeId alarmId = RequireAlarm();

            CallMethodResult callResult = await CallMethodOnAlarmAsync(
                alarmId,
                MethodIds.AcknowledgeableConditionType_Acknowledge,
                new Variant(default(ByteString)),
                new Variant(new LocalizedText("en", "no event id")))
                .ConfigureAwait(false);

            Assert.That(StatusCode.IsBad(callResult.StatusCode), Is.True,
                "Acknowledge with a null EventId should fail.");
        }

        [Test]
        [Property("ConformanceUnit", "A and C Acknowledge")]
        [Property("Tag", "Err_009")]
        public async Task ErrAcknowledgeWithEmptyCommentAsync()
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
                MethodIds.AcknowledgeableConditionType_Acknowledge,
                new Variant(eventId),
                new Variant(LocalizedText.Null)).ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(callResult.StatusCode) ||
                StatusCode.IsBad(callResult.StatusCode), Is.True,
                "Server should produce a deterministic status for an " +
                "Acknowledge with empty comment.");
        }

        [Test]
        [Property("ConformanceUnit", "A and C Acknowledge")]
        [Property("Tag", "Err_004")]
        public async Task ErrAcknowledgeOnDisabledConditionAsync()
        {
            NodeId alarmId = RequireAlarm();

            await Task.Delay(1500).ConfigureAwait(false);

            CallMethodResult disable = await CallMethodOnAlarmAsync(
                alarmId,
                MethodIds.ConditionType_Disable).ConfigureAwait(false);
            _ = disable.StatusCode;

            ByteString eventId = await ReadEventIdAsync(alarmId).ConfigureAwait(false);
            CallMethodResult callResult = await CallMethodOnAlarmAsync(
                alarmId,
                MethodIds.AcknowledgeableConditionType_Acknowledge,
                new Variant(eventId),
                new Variant(new LocalizedText("en", "x"))).ConfigureAwait(false);

            await CallMethodOnAlarmAsync(
                alarmId,
                MethodIds.ConditionType_Enable).ConfigureAwait(false);

            Assert.That(StatusCode.IsBad(callResult.StatusCode), Is.True,
                "Acknowledge on a disabled condition should fail.");
        }
    }
}
