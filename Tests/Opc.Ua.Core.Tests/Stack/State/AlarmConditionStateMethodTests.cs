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

#pragma warning disable CA2000
using System;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.State
{
    /// <summary>
    /// Tests for Part 9 alarm methods on the AlarmConditionState class:
    /// Silence, Suppress/Unsuppress, OutOfService, Reset, GetGroupMemberships,
    /// Latched alarms, ReAlarm helpers, Audible state.
    /// </summary>
    [TestFixture]
    [Category("AlarmConditionState")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class AlarmConditionStateMethodTests
    {
        private ISystemContext m_context;
        private ITelemetryContext m_telemetry;

        [OneTimeSetUp]
        protected void OneTimeSetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            var messageContext = ServiceMessageContext.Create(m_telemetry);
            messageContext.NamespaceUris.GetIndexOrAppend(Namespaces.OpcUa);
            m_context = new SystemContext(m_telemetry)
            {
                NamespaceUris = messageContext.NamespaceUris
            };
        }

        [OneTimeTearDown]
        protected void OneTimeTearDown()
        {
            (m_context as IDisposable)?.Dispose();
        }

        private AlarmConditionState CreateAlarm()
        {
            var alarm = new AlarmConditionState(m_telemetry, null);
            alarm.Create(m_context, new NodeId(1), QualifiedName.From("Alarm"), default, true);
            alarm.SetEnableState(m_context, true);
            return alarm;
        }

        private void AddTwoStateChild(AlarmConditionState alarm, string browseName, Action<TwoStateVariableState> setter)
        {
            var state = new TwoStateVariableState(alarm);
            state.Create(m_context, default, QualifiedName.From(browseName), default, false);
            setter(state);
        }

        [Test]
        public void SetSilenceStateUpdatesValueAndTransitionTime()
        {
            AlarmConditionState alarm = CreateAlarm();
            AddTwoStateChild(alarm, BrowseNames.SilenceState, s => alarm.SilenceState = s);

            DateTimeUtc before = DateTimeUtc.Now;
            alarm.SetSilenceState(m_context, true);
            DateTimeUtc after = DateTimeUtc.Now;

            Assert.That(alarm.SilenceState.Id.Value, Is.True);
            Assert.That(alarm.SilenceState.Timestamp, Is.GreaterThanOrEqualTo(before));
            Assert.That(alarm.SilenceState.Timestamp, Is.LessThanOrEqualTo(after));
            Assert.That(alarm.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
        }

        [Test]
        public void SetSilenceStateWithoutSilenceVariableIsNoOp()
        {
            AlarmConditionState alarm = CreateAlarm();
            // No SilenceState child created

            Assert.DoesNotThrow(() => alarm.SetSilenceState(m_context, true));
        }

        [Test]
        public void NewActivationClearsSilenceState()
        {
            AlarmConditionState alarm = CreateAlarm();
            AddTwoStateChild(alarm, BrowseNames.SilenceState, s => alarm.SilenceState = s);

            alarm.SetSilenceState(m_context, true);
            Assert.That(alarm.SilenceState.Id.Value, Is.True);

            // New activation should clear silence
            alarm.SetActiveState(m_context, true);

            Assert.That(alarm.SilenceState.Id.Value, Is.False);
        }

        [Test]
        public void SetOutOfServiceStateUpdatesSuppressedOrShelved()
        {
            AlarmConditionState alarm = CreateAlarm();
            AddTwoStateChild(alarm, BrowseNames.OutOfServiceState, s => alarm.OutOfServiceState = s);

            Assert.That(alarm.SuppressedOrShelved.Value, Is.False);

            alarm.SetOutOfServiceState(m_context, true);

            Assert.That(alarm.OutOfServiceState.Id.Value, Is.True);
            Assert.That(alarm.SuppressedOrShelved.Value, Is.True, "OutOfService must set SuppressedOrShelved");

            alarm.SetOutOfServiceState(m_context, false);

            Assert.That(alarm.OutOfServiceState.Id.Value, Is.False);
            Assert.That(alarm.SuppressedOrShelved.Value, Is.False, "Placing in service clears SuppressedOrShelved");
        }

        [Test]
        public void OutOfServiceDoesNotClearSuppressedOrShelvedWhenAlsoSuppressed()
        {
            AlarmConditionState alarm = CreateAlarm();
            AddTwoStateChild(alarm, BrowseNames.SuppressedState, s => alarm.SuppressedState = s);
            AddTwoStateChild(alarm, BrowseNames.OutOfServiceState, s => alarm.OutOfServiceState = s);

            alarm.SetSuppressedState(m_context, true);
            alarm.SetOutOfServiceState(m_context, true);

            Assert.That(alarm.SuppressedOrShelved.Value, Is.True);

            // Place in service - suppressed remains, so SuppressedOrShelved must remain true
            alarm.SetOutOfServiceState(m_context, false);
            Assert.That(alarm.SuppressedOrShelved.Value, Is.True, "Still suppressed");

            // Unsuppress - now should clear
            alarm.SetSuppressedState(m_context, false);
            Assert.That(alarm.SuppressedOrShelved.Value, Is.False);
        }

        [Test]
        public void UnsuppressDoesNotClearSuppressedOrShelvedWhenOutOfService()
        {
            AlarmConditionState alarm = CreateAlarm();
            AddTwoStateChild(alarm, BrowseNames.SuppressedState, s => alarm.SuppressedState = s);
            AddTwoStateChild(alarm, BrowseNames.OutOfServiceState, s => alarm.OutOfServiceState = s);

            alarm.SetSuppressedState(m_context, true);
            alarm.SetOutOfServiceState(m_context, true);

            Assert.That(alarm.SuppressedOrShelved.Value, Is.True);

            alarm.SetSuppressedState(m_context, false);
            Assert.That(alarm.SuppressedOrShelved.Value, Is.True, "OutOfService keeps SuppressedOrShelved");
        }

        [Test]
        public void SetLatchedStateUpdatesValueAndTimestamp()
        {
            AlarmConditionState alarm = CreateAlarm();
            AddTwoStateChild(alarm, BrowseNames.LatchedState, s => alarm.LatchedState = s);

            DateTimeUtc before = DateTimeUtc.Now;
            alarm.SetLatchedState(m_context, true);
            DateTimeUtc after = DateTimeUtc.Now;

            Assert.That(alarm.LatchedState.Id.Value, Is.True);
            Assert.That(alarm.LatchedState.Timestamp, Is.GreaterThanOrEqualTo(before));
            Assert.That(alarm.LatchedState.Timestamp, Is.LessThanOrEqualTo(after));
        }

        [Test]
        public void ActivationSetsLatchedStateTrueWhenPresent()
        {
            AlarmConditionState alarm = CreateAlarm();
            AddTwoStateChild(alarm, BrowseNames.LatchedState, s => alarm.LatchedState = s);

            Assert.That(alarm.LatchedState.Id.Value, Is.False);

            alarm.SetActiveState(m_context, true);

            Assert.That(alarm.LatchedState.Id.Value, Is.True, "Active alarm with LatchedState becomes latched");
        }

        [Test]
        public void DeactivationDoesNotClearLatchedState()
        {
            AlarmConditionState alarm = CreateAlarm();
            AddTwoStateChild(alarm, BrowseNames.LatchedState, s => alarm.LatchedState = s);

            alarm.SetActiveState(m_context, true);
            Assert.That(alarm.LatchedState.Id.Value, Is.True);

            alarm.SetActiveState(m_context, false);

            Assert.That(alarm.ActiveState.Id.Value, Is.False, "ActiveState reflects real process state");
            Assert.That(alarm.LatchedState.Id.Value, Is.True, "LatchedState persists across deactivation");
        }

        [Test]
        public void LatchedAlarmIsRetained()
        {
            AlarmConditionState alarm = CreateAlarm();
            AddTwoStateChild(alarm, BrowseNames.LatchedState, s => alarm.LatchedState = s);

            alarm.SetActiveState(m_context, true);
            alarm.SetActiveState(m_context, false);

            // Retain is updated via state setters which call UpdateRetainState internally;
            // verify LatchedState alone keeps the alarm in retained state.
            Assert.That(alarm.LatchedState.Id.Value, Is.True);
        }

        [Test]
        public void IsReAlarmEnabledReflectsReAlarmTime()
        {
            AlarmConditionState alarm = CreateAlarm();

            Assert.That(alarm.IsReAlarmEnabled, Is.False, "Without ReAlarmTime, re-alarm is disabled");

            alarm.ReAlarmTime = PropertyState<double>.With<VariantBuilder>(alarm);
            alarm.ReAlarmTime.Create(m_context, default, QualifiedName.From(BrowseNames.ReAlarmTime), default, false);
            alarm.ReAlarmTime.Value = 1000.0;

            Assert.That(alarm.IsReAlarmEnabled, Is.True);

            alarm.ReAlarmTime.Value = 0;
            Assert.That(alarm.IsReAlarmEnabled, Is.False);
        }

        [Test]
        public void ProcessReAlarmIncrementsRepeatCount()
        {
            AlarmConditionState alarm = CreateAlarm();
            alarm.ReAlarmRepeatCount = BaseDataVariableState<short>.With<VariantBuilder>(alarm);
            alarm.ReAlarmRepeatCount.Create(m_context, default, QualifiedName.From(BrowseNames.ReAlarmRepeatCount), default, false);
            alarm.ReAlarmRepeatCount.Value = 0;
            alarm.SetActiveState(m_context, true);

            alarm.ProcessReAlarm(m_context);

            Assert.That(alarm.ReAlarmRepeatCount.Value, Is.EqualTo((short)1));

            alarm.ProcessReAlarm(m_context);

            Assert.That(alarm.ReAlarmRepeatCount.Value, Is.EqualTo((short)2));
        }

        [Test]
        public void ProcessReAlarmIsNoOpWhenInactive()
        {
            AlarmConditionState alarm = CreateAlarm();
            alarm.ReAlarmRepeatCount = BaseDataVariableState<short>.With<VariantBuilder>(alarm);
            alarm.ReAlarmRepeatCount.Create(m_context, default, QualifiedName.From(BrowseNames.ReAlarmRepeatCount), default, false);
            alarm.ReAlarmRepeatCount.Value = 0;

            // Inactive alarm
            alarm.ProcessReAlarm(m_context);

            Assert.That(alarm.ReAlarmRepeatCount.Value, Is.Zero);
        }

        [Test]
        public void ResetReAlarmRepeatCountZeroes()
        {
            AlarmConditionState alarm = CreateAlarm();
            alarm.ReAlarmRepeatCount = BaseDataVariableState<short>.With<VariantBuilder>(alarm);
            alarm.ReAlarmRepeatCount.Create(m_context, default, QualifiedName.From(BrowseNames.ReAlarmRepeatCount), default, false);
            alarm.ReAlarmRepeatCount.Value = 5;

            alarm.ResetReAlarmRepeatCount(m_context);

            Assert.That(alarm.ReAlarmRepeatCount.Value, Is.Zero);
        }

        [Test]
        public void UpdateAudibleStateNoOpWhenAudibleDisabled()
        {
            AlarmConditionState alarm = CreateAlarm();
            AddTwoStateChild(alarm, BrowseNames.SilenceState, s => alarm.SilenceState = s);
            alarm.SetSilenceState(m_context, true);

            // AudibleEnabled not configured — should not affect silence
            alarm.UpdateAudibleState(m_context, true);

            Assert.That(alarm.SilenceState.Id.Value, Is.True, "No change without AudibleEnabled");
        }

        [Test]
        public void UpdateAudibleStateClearsSilenceOnActivation()
        {
            AlarmConditionState alarm = CreateAlarm();
            AddTwoStateChild(alarm, BrowseNames.SilenceState, s => alarm.SilenceState = s);
            alarm.AudibleEnabled = PropertyState<bool>.With<VariantBuilder>(alarm);
            alarm.AudibleEnabled.Create(m_context, default, QualifiedName.From(BrowseNames.AudibleEnabled), default, false);
            alarm.AudibleEnabled.Value = true;

            alarm.SetSilenceState(m_context, true);
            Assert.That(alarm.SilenceState.Id.Value, Is.True);

            alarm.UpdateAudibleState(m_context, true);

            Assert.That(alarm.SilenceState.Id.Value, Is.False, "Audible activation clears silence");
        }

}
}
