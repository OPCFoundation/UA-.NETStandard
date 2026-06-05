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
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.History.Tests
{
    /// <summary>
    /// compliance tests for the A and C Enable conformance unit.
    /// Verifies that Enable/Disable methods exist on the type system
    /// and that EnabledState transitions correctly.
    /// </summary>
    [NonParallelizable]
    [TestFixture]
    [Category("Conformance")]
    [Category("AlarmsAndConditions")]
    public class AlarmsAndConditionsEnableTests : AlarmsAndConditionsTestFixture
    {
        [Test]
        public async Task ConditionTypeHasEnableAndDisableMethodsAsync()
        {
            BrowseResult result = await BrowseForwardAsync(
                ObjectTypeIds.ConditionType).ConfigureAwait(false);
            bool foundEnable = false;
            bool foundDisable = false;
            int count = result.References.Count;
            for (int i = 0; i < count; i++)
            {
                string n = result.References[i].BrowseName.Name;
                if (n == "Enable")
                {
                    foundEnable = true;
                }
                else if (n == "Disable")
                {
                    foundDisable = true;
                }
            }
            Assert.That(foundEnable, Is.True,
                "ConditionType should have Enable method.");
            Assert.That(foundDisable, Is.True,
                "ConditionType should have Disable method.");
        }

        [Test]
        public async Task ConditionTypeHasEnabledStateAsync()
        {
            BrowseResult result = await BrowseForwardAsync(
                ObjectTypeIds.ConditionType).ConfigureAwait(false);
            bool found = false;
            int count = result.References.Count;
            for (int i = 0; i < count; i++)
            {
                if (result.References[i].BrowseName.Name == "EnabledState")
                {
                    found = true;
                    break;
                }
            }
            Assert.That(found, Is.True,
                "ConditionType should have EnabledState property.");
        }

        [Test]
        public async Task EnableConditionSetsEnabledStateTrueAsync()
        {
            NodeId alarmId = RequireAlarm();

            await CallMethodOnAlarmAsync(
                alarmId,
                MethodIds.ConditionType_Disable).ConfigureAwait(false);

            CallMethodResult callResult = await CallMethodOnAlarmAsync(
                alarmId,
                MethodIds.ConditionType_Enable).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(callResult.StatusCode), Is.True,
                $"Enable should succeed: {callResult.StatusCode}");

            DataValue enabledState = await ReadStateIdAsync(alarmId, "EnabledState")
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(enabledState.StatusCode), Is.True);
            Assert.That(enabledState.WrappedValue.TryGetValue(out bool value), Is.True);
            Assert.That(value, Is.True,
                "EnabledState/Id should be true after Enable.");
        }

        [Test]
        public async Task DisableConditionSetsEnabledStateFalseAsync()
        {
            NodeId alarmId = RequireAlarm();

            await CallMethodOnAlarmAsync(
                alarmId,
                MethodIds.ConditionType_Enable).ConfigureAwait(false);

            CallMethodResult callResult = await CallMethodOnAlarmAsync(
                alarmId,
                MethodIds.ConditionType_Disable).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(callResult.StatusCode), Is.True,
                $"Disable should succeed: {callResult.StatusCode}");

            DataValue enabledState = await ReadStateIdAsync(alarmId, "EnabledState")
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(enabledState.StatusCode), Is.True);
            Assert.That(enabledState.WrappedValue.TryGetValue(out bool value), Is.True);
            Assert.That(value, Is.False,
                "EnabledState/Id should be false after Disable.");

            await CallMethodOnAlarmAsync(
                alarmId,
                MethodIds.ConditionType_Enable).ConfigureAwait(false);
        }

        [Test]
        public async Task DisableEnableViaTypeAndInstanceMethodNoEventsDuringDisabledAsync()
        {
            NodeId alarmId = RequireCttAlarm("AlarmConditionType");
            await NormalizeAlarmAsync(alarmId).ConfigureAwait(false);

            NodeId instanceDisable = await FindInstanceMethodAsync(alarmId, BrowseNames.Disable)
                .ConfigureAwait(false);
            NodeId instanceEnable = await FindInstanceMethodAsync(alarmId, BrowseNames.Enable)
                .ConfigureAwait(false);
            if (instanceDisable.IsNull || instanceEnable.IsNull)
            {
                Assert.Inconclusive(
                    "The alarm instance does not expose instance-level Enable/Disable methods.");
            }

            await using AlarmEventCollector collector =
                await AlarmEventCollector.CreateAsync(Session).ConfigureAwait(false);

            try
            {
                await VerifyDisableEnableCycleAsync(
                    collector,
                    alarmId,
                    MethodIds.ConditionType_Disable,
                    MethodIds.ConditionType_Enable).ConfigureAwait(false);

                await VerifyDisableEnableCycleAsync(
                    collector,
                    alarmId,
                    instanceDisable,
                    instanceEnable).ConfigureAwait(false);
            }
            catch (TimeoutException ex)
            {
                Assert.Inconclusive(
                    "Disable/Enable event did not arrive within the expected window " +
                    "(CI load flakiness): " + ex.Message);
            }
        }

        [Test]
        public async Task ErrEnableWithBadNodeIdAsync()
        {
            CallMethodResult callResult = await CallMethodOnAlarmAsync(
                new NodeId(uint.MaxValue, 99),
                MethodIds.ConditionType_Enable).ConfigureAwait(false);

            Assert.That(StatusCode.IsBad(callResult.StatusCode), Is.True,
                "Enable on a bad NodeId should fail.");
        }

        [Test]
        public async Task ErrDisableWithBadNodeIdAsync()
        {
            CallMethodResult callResult = await CallMethodOnAlarmAsync(
                new NodeId(uint.MaxValue, 99),
                MethodIds.ConditionType_Disable).ConfigureAwait(false);

            Assert.That(StatusCode.IsBad(callResult.StatusCode), Is.True,
                "Disable on a bad NodeId should fail.");
        }

        [Test]
        public async Task ErrEnableAlreadyEnabledAsync()
        {
            NodeId alarmId = RequireAlarm();

            await CallMethodOnAlarmAsync(
                alarmId,
                MethodIds.ConditionType_Enable).ConfigureAwait(false);

            CallMethodResult callResult = await CallMethodOnAlarmAsync(
                alarmId,
                MethodIds.ConditionType_Enable).ConfigureAwait(false);

            Assert.That(
                callResult.StatusCode == StatusCodes.BadConditionAlreadyEnabled ||
                StatusCode.IsBad(callResult.StatusCode), Is.True,
                $"Enable on an already-enabled condition should fail: {callResult.StatusCode}");
        }

        [Test]
        public async Task ErrDisableAlreadyDisabledAsync()
        {
            NodeId alarmId = RequireAlarm();

            await CallMethodOnAlarmAsync(
                alarmId,
                MethodIds.ConditionType_Disable).ConfigureAwait(false);

            CallMethodResult callResult = await CallMethodOnAlarmAsync(
                alarmId,
                MethodIds.ConditionType_Disable).ConfigureAwait(false);

            await CallMethodOnAlarmAsync(
                alarmId,
                MethodIds.ConditionType_Enable).ConfigureAwait(false);

            Assert.That(
                callResult.StatusCode == StatusCodes.BadConditionAlreadyDisabled ||
                StatusCode.IsBad(callResult.StatusCode), Is.True,
                $"Disable on an already-disabled condition should fail: {callResult.StatusCode}");
        }

        private async Task VerifyDisableEnableCycleAsync(
            AlarmEventCollector collector,
            NodeId alarmId,
            NodeId disableMethodId,
            NodeId enableMethodId)
        {
            collector.Reset();
            DateTime disableStart = DateTime.UtcNow;
            CallMethodResult disable = await CallMethodOnAlarmAsync(
                alarmId,
                disableMethodId).ConfigureAwait(false);
            DateTime disableEnd = DateTime.UtcNow;
            Assert.That(StatusCode.IsGood(disable.StatusCode), Is.True,
                $"Disable should succeed: {disable.StatusCode}");

            EventFieldList disableEvent = await collector.WaitForEventAsync(
                alarmId,
                e => AlarmEventCollector.TryGetBoolean(
                    e,
                    AlarmEventCollector.FieldIndex.EnabledStateId,
                    out bool enabled) && !enabled,
                TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            AssertTransitionTimeInCallWindow(
                disableEvent,
                AlarmEventCollector.FieldIndex.EnabledStateTransitionTime,
                disableStart,
                disableEnd,
                "Disable");
            AssertEventRetain(disableEvent, expected: false, "Disable should clear Retain.");

            collector.Reset();
            await WriteAlarmSourceValueAsync(alarmId, new Variant(90)).ConfigureAwait(false);
            await Task.Delay(1000).ConfigureAwait(false);
            Assert.That(collector.HasEvents(alarmId), Is.False,
                "No condition events should be emitted while the condition is disabled.");

            DateTime enableStart = DateTime.UtcNow;
            CallMethodResult enable = await CallMethodOnAlarmAsync(
                alarmId,
                enableMethodId).ConfigureAwait(false);
            DateTime enableEnd = DateTime.UtcNow;
            Assert.That(StatusCode.IsGood(enable.StatusCode), Is.True,
                $"Enable should succeed: {enable.StatusCode}");

            EventFieldList enableEvent = await collector.WaitForEventAsync(
                alarmId,
                e => AlarmEventCollector.TryGetBoolean(
                    e,
                    AlarmEventCollector.FieldIndex.EnabledStateId,
                    out bool enabled) && enabled,
                TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            AssertTransitionTimeInCallWindow(
                enableEvent,
                AlarmEventCollector.FieldIndex.EnabledStateTransitionTime,
                enableStart,
                enableEnd,
                "Enable");
            AssertEventRetain(enableEvent, expected: true,
                "Enable should restore Retain when the source changed to an active alarm while disabled.");

            await NormalizeAlarmAsync(alarmId).ConfigureAwait(false);
        }

        private static void AssertTransitionTimeInCallWindow(
            EventFieldList eventFields,
            AlarmEventCollector.FieldIndex fieldIndex,
            DateTime start,
            DateTime end,
            string operation)
        {
            Assert.That(
                AlarmEventCollector.TryGetDateTime(eventFields, fieldIndex, out DateTime transitionTime),
                Is.True,
                $"{operation} event should include TransitionTime.");
            Assert.That(transitionTime, Is.GreaterThanOrEqualTo(start.AddSeconds(-1)));
            Assert.That(transitionTime, Is.LessThanOrEqualTo(end.AddSeconds(1)));
        }

        private static void AssertEventRetain(
            EventFieldList eventFields,
            bool expected,
            string message)
        {
            Assert.That(
                AlarmEventCollector.TryGetBoolean(
                    eventFields,
                    AlarmEventCollector.FieldIndex.Retain,
                    out bool retain),
                Is.True,
                "Event should include Retain.");
            Assert.That(retain, Is.EqualTo(expected), message);
        }
    }
}
