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

namespace Opc.Ua.Features.Tests
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

        // -----------------------------------------------------------------
        // On*Called audit handler coverage
        // -----------------------------------------------------------------

        /// <summary>
        /// Test-only subclass that exposes the protected On*Called handlers
        /// so the handler logic can be exercised directly.
        /// </summary>
        private sealed class TestableAlarm : AlarmConditionState
        {
            public TestableAlarm(ITelemetryContext telemetry)
                : base(telemetry, null)
            {
            }

            public ServiceResult CallSilence(ISystemContext c)
            {
                return OnSilenceCalled(c, null!, default, []);
            }

            public ServiceResult CallSuppress(ISystemContext c)
            {
                return OnSuppressCalled(c, null!, default, []);
            }

            public ServiceResult CallSuppress2(ISystemContext c, LocalizedText comment)
            {
                return OnSuppress2Called(c, null!, NodeId.Null, comment);
            }

            public ServiceResult CallUnsuppress(ISystemContext c)
            {
                return OnUnsuppressCalled(c, null!, default, []);
            }

            public ServiceResult CallUnsuppress2(ISystemContext c, LocalizedText comment)
            {
                return OnUnsuppress2Called(c, null!, NodeId.Null, comment);
            }

            public ServiceResult CallRemoveFromService(ISystemContext c)
            {
                return OnRemoveFromServiceCalled(c, null!, default, []);
            }

            public ServiceResult CallRemoveFromService2(ISystemContext c, LocalizedText comment)
            {
                return OnRemoveFromService2Called(c, null!, NodeId.Null, comment);
            }

            public ServiceResult CallPlaceInService(ISystemContext c)
            {
                return OnPlaceInServiceCalled(c, null!, default, []);
            }

            public ServiceResult CallPlaceInService2(ISystemContext c, LocalizedText comment)
            {
                return OnPlaceInService2Called(c, null!, NodeId.Null, comment);
            }

            public ServiceResult CallReset(ISystemContext c)
            {
                return OnResetCalled(c, null!, default, []);
            }

            public ServiceResult CallReset2(ISystemContext c, LocalizedText comment)
            {
                return OnReset2Called(c, null!, NodeId.Null, comment);
            }

            public ServiceResult CallGetGroupMemberships(ISystemContext c, ref ArrayOf<NodeId> groups)
            {
                return OnGetGroupMembershipsCalled(c, null!, NodeId.Null, ref groups);
            }
        }

        private TestableAlarm CreateTestableAlarm()
        {
            var alarm = new TestableAlarm(m_telemetry);
            alarm.Create(m_context, new NodeId(1), QualifiedName.From("Alarm"), default, true);
            alarm.SetEnableState(m_context, true);
            return alarm;
        }

        private void EnsureComment(AlarmConditionState alarm)
        {
            if (alarm.Comment == null)
            {
                alarm.Comment = ConditionVariableState<LocalizedText>.With<VariantBuilder>(alarm);
                alarm.Comment.Create(m_context, default, QualifiedName.From(BrowseNames.Comment), default, false);
            }
        }

        // ---------- Silence ----------

        [Test]
        public void OnSilenceCalledReturnsBadNotSupportedWhenStateMissing()
        {
            TestableAlarm alarm = CreateTestableAlarm();
            // SilenceState intentionally not added.

            ServiceResult result = alarm.CallSilence(m_context);

            Assert.That(result.StatusCode.Code, Is.EqualTo(StatusCodes.BadNotSupported));
        }

        [Test]
        public void OnSilenceCalledReturnsBadNothingToDoWhenAlreadySilenced()
        {
            TestableAlarm alarm = CreateTestableAlarm();
            AddTwoStateChild(alarm, BrowseNames.SilenceState, s => alarm.SilenceState = s);
            alarm.SetSilenceState(m_context, true);

            ServiceResult result = alarm.CallSilence(m_context);

            Assert.That(result.StatusCode.Code, Is.EqualTo(StatusCodes.BadNothingToDo));
        }

        [Test]
        public void OnSilenceCalledInvokesRequestedDelegateAndTransitions()
        {
            TestableAlarm alarm = CreateTestableAlarm();
            AddTwoStateChild(alarm, BrowseNames.SilenceState, s => alarm.SilenceState = s);
            int callCount = 0;
            alarm.OnSilenceRequested = (c, a) =>
            {
                callCount++;
                return ServiceResult.Good;
            };

            ServiceResult result = alarm.CallSilence(m_context);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(callCount, Is.EqualTo(1));
            Assert.That(alarm.SilenceState.Id.Value, Is.True);
        }

        [Test]
        public void OnSilenceCalledVetoedByRequestedDelegateDoesNotTransition()
        {
            TestableAlarm alarm = CreateTestableAlarm();
            AddTwoStateChild(alarm, BrowseNames.SilenceState, s => alarm.SilenceState = s);
            alarm.OnSilenceRequested = (c, a) => StatusCodes.BadUserAccessDenied;

            ServiceResult result = alarm.CallSilence(m_context);

            Assert.That(result.StatusCode.Code, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(alarm.SilenceState.Id.Value, Is.False, "Veto must not transition");
        }

        // ---------- Suppress / Suppress2 ----------

        [Test]
        [TestCase("v1")]
        [TestCase("v2")]
        public void OnSuppressCalledReturnsBadNotSupportedWhenStateMissing(string variant)
        {
            TestableAlarm alarm = CreateTestableAlarm();
            // SuppressedState intentionally not added.

            ServiceResult result = variant == "v1"
                ? alarm.CallSuppress(m_context)
                : alarm.CallSuppress2(m_context, new LocalizedText("en", "x"));

            Assert.That(result.StatusCode.Code, Is.EqualTo(StatusCodes.BadNotSupported));
        }

        [Test]
        [TestCase("v1")]
        [TestCase("v2")]
        public void OnSuppressCalledReturnsBadNothingToDoWhenAlreadySuppressed(string variant)
        {
            TestableAlarm alarm = CreateTestableAlarm();
            AddTwoStateChild(alarm, BrowseNames.SuppressedState, s => alarm.SuppressedState = s);
            alarm.SetSuppressedState(m_context, true);

            ServiceResult result = variant == "v1"
                ? alarm.CallSuppress(m_context)
                : alarm.CallSuppress2(m_context, new LocalizedText("en", "x"));

            Assert.That(result.StatusCode.Code, Is.EqualTo(StatusCodes.BadNothingToDo));
        }

        [Test]
        [TestCase("v1")]
        [TestCase("v2")]
        public void OnSuppressCalledInvokesRequestedDelegateAndTransitions(string variant)
        {
            TestableAlarm alarm = CreateTestableAlarm();
            AddTwoStateChild(alarm, BrowseNames.SuppressedState, s => alarm.SuppressedState = s);
            int callCount = 0;
            bool? suppressingArg = null;
            alarm.OnSuppressRequested = (c, a, suppressing) =>
            {
                callCount++;
                suppressingArg = suppressing;
                return ServiceResult.Good;
            };

            ServiceResult result = variant == "v1"
                ? alarm.CallSuppress(m_context)
                : alarm.CallSuppress2(m_context, new LocalizedText("en", "x"));

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(callCount, Is.EqualTo(1));
            Assert.That(suppressingArg, Is.True);
            Assert.That(alarm.SuppressedState.Id.Value, Is.True);
        }

        [Test]
        [TestCase("v1")]
        [TestCase("v2")]
        public void OnSuppressCalledVetoedByRequestedDelegateDoesNotTransition(string variant)
        {
            TestableAlarm alarm = CreateTestableAlarm();
            AddTwoStateChild(alarm, BrowseNames.SuppressedState, s => alarm.SuppressedState = s);
            alarm.OnSuppressRequested = (c, a, suppressing) => StatusCodes.BadUserAccessDenied;

            ServiceResult result = variant == "v1"
                ? alarm.CallSuppress(m_context)
                : alarm.CallSuppress2(m_context, new LocalizedText("en", "x"));

            Assert.That(result.StatusCode.Code, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(alarm.SuppressedState.Id.Value, Is.False);
        }

        [Test]
        public void OnSuppress2CalledAppliesComment()
        {
            TestableAlarm alarm = CreateTestableAlarm();
            AddTwoStateChild(alarm, BrowseNames.SuppressedState, s => alarm.SuppressedState = s);
            EnsureComment(alarm);
            var comment = new LocalizedText("en", "operator suppressed");

            ServiceResult result = alarm.CallSuppress2(m_context, comment);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(alarm.Comment.Value, Is.EqualTo(comment));
        }

        // ---------- Unsuppress / Unsuppress2 ----------

        [Test]
        [TestCase("v1")]
        [TestCase("v2")]
        public void OnUnsuppressCalledReturnsBadNotSupportedWhenStateMissing(string variant)
        {
            TestableAlarm alarm = CreateTestableAlarm();

            ServiceResult result = variant == "v1"
                ? alarm.CallUnsuppress(m_context)
                : alarm.CallUnsuppress2(m_context, new LocalizedText("en", "x"));

            Assert.That(result.StatusCode.Code, Is.EqualTo(StatusCodes.BadNotSupported));
        }

        [Test]
        [TestCase("v1")]
        [TestCase("v2")]
        public void OnUnsuppressCalledReturnsBadNothingToDoWhenNotSuppressed(string variant)
        {
            TestableAlarm alarm = CreateTestableAlarm();
            AddTwoStateChild(alarm, BrowseNames.SuppressedState, s => alarm.SuppressedState = s);
            // Not suppressed.

            ServiceResult result = variant == "v1"
                ? alarm.CallUnsuppress(m_context)
                : alarm.CallUnsuppress2(m_context, new LocalizedText("en", "x"));

            Assert.That(result.StatusCode.Code, Is.EqualTo(StatusCodes.BadNothingToDo));
        }

        [Test]
        [TestCase("v1")]
        [TestCase("v2")]
        public void OnUnsuppressCalledInvokesRequestedDelegateAndTransitions(string variant)
        {
            TestableAlarm alarm = CreateTestableAlarm();
            AddTwoStateChild(alarm, BrowseNames.SuppressedState, s => alarm.SuppressedState = s);
            alarm.SetSuppressedState(m_context, true);

            int callCount = 0;
            bool? suppressingArg = null;
            alarm.OnSuppressRequested = (c, a, suppressing) =>
            {
                callCount++;
                suppressingArg = suppressing;
                return ServiceResult.Good;
            };

            ServiceResult result = variant == "v1"
                ? alarm.CallUnsuppress(m_context)
                : alarm.CallUnsuppress2(m_context, new LocalizedText("en", "x"));

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(callCount, Is.EqualTo(1));
            Assert.That(suppressingArg, Is.False);
            Assert.That(alarm.SuppressedState.Id.Value, Is.False);
        }

        [Test]
        [TestCase("v1")]
        [TestCase("v2")]
        public void OnUnsuppressCalledVetoedByRequestedDelegateDoesNotTransition(string variant)
        {
            TestableAlarm alarm = CreateTestableAlarm();
            AddTwoStateChild(alarm, BrowseNames.SuppressedState, s => alarm.SuppressedState = s);
            alarm.SetSuppressedState(m_context, true);
            alarm.OnSuppressRequested = (c, a, suppressing) => StatusCodes.BadUserAccessDenied;

            ServiceResult result = variant == "v1"
                ? alarm.CallUnsuppress(m_context)
                : alarm.CallUnsuppress2(m_context, new LocalizedText("en", "x"));

            Assert.That(result.StatusCode.Code, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(alarm.SuppressedState.Id.Value, Is.True, "Veto preserves suppressed state");
        }

        [Test]
        public void OnUnsuppress2CalledAppliesComment()
        {
            TestableAlarm alarm = CreateTestableAlarm();
            AddTwoStateChild(alarm, BrowseNames.SuppressedState, s => alarm.SuppressedState = s);
            alarm.SetSuppressedState(m_context, true);
            EnsureComment(alarm);
            var comment = new LocalizedText("en", "operator unsuppressed");

            ServiceResult result = alarm.CallUnsuppress2(m_context, comment);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(alarm.Comment.Value, Is.EqualTo(comment));
        }

        // ---------- RemoveFromService / RemoveFromService2 ----------

        [Test]
        [TestCase("v1")]
        [TestCase("v2")]
        public void OnRemoveFromServiceCalledReturnsBadNotSupportedWhenStateMissing(string variant)
        {
            TestableAlarm alarm = CreateTestableAlarm();

            ServiceResult result = variant == "v1"
                ? alarm.CallRemoveFromService(m_context)
                : alarm.CallRemoveFromService2(m_context, new LocalizedText("en", "x"));

            Assert.That(result.StatusCode.Code, Is.EqualTo(StatusCodes.BadNotSupported));
        }

        [Test]
        [TestCase("v1")]
        [TestCase("v2")]
        public void OnRemoveFromServiceCalledReturnsBadNothingToDoWhenAlreadyOutOfService(string variant)
        {
            TestableAlarm alarm = CreateTestableAlarm();
            AddTwoStateChild(alarm, BrowseNames.OutOfServiceState, s => alarm.OutOfServiceState = s);
            alarm.SetOutOfServiceState(m_context, true);

            ServiceResult result = variant == "v1"
                ? alarm.CallRemoveFromService(m_context)
                : alarm.CallRemoveFromService2(m_context, new LocalizedText("en", "x"));

            Assert.That(result.StatusCode.Code, Is.EqualTo(StatusCodes.BadNothingToDo));
        }

        [Test]
        [TestCase("v1")]
        [TestCase("v2")]
        public void OnRemoveFromServiceCalledInvokesRequestedDelegateAndTransitions(string variant)
        {
            TestableAlarm alarm = CreateTestableAlarm();
            AddTwoStateChild(alarm, BrowseNames.OutOfServiceState, s => alarm.OutOfServiceState = s);
            int callCount = 0;
            bool? outOfServiceArg = null;
            alarm.OnOutOfServiceRequested = (c, a, oos) =>
            {
                callCount++;
                outOfServiceArg = oos;
                return ServiceResult.Good;
            };

            ServiceResult result = variant == "v1"
                ? alarm.CallRemoveFromService(m_context)
                : alarm.CallRemoveFromService2(m_context, new LocalizedText("en", "x"));

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(callCount, Is.EqualTo(1));
            Assert.That(outOfServiceArg, Is.True);
            Assert.That(alarm.OutOfServiceState.Id.Value, Is.True);
        }

        [Test]
        [TestCase("v1")]
        [TestCase("v2")]
        public void OnRemoveFromServiceCalledVetoedByRequestedDelegateDoesNotTransition(string variant)
        {
            TestableAlarm alarm = CreateTestableAlarm();
            AddTwoStateChild(alarm, BrowseNames.OutOfServiceState, s => alarm.OutOfServiceState = s);
            alarm.OnOutOfServiceRequested = (c, a, oos) => StatusCodes.BadUserAccessDenied;

            ServiceResult result = variant == "v1"
                ? alarm.CallRemoveFromService(m_context)
                : alarm.CallRemoveFromService2(m_context, new LocalizedText("en", "x"));

            Assert.That(result.StatusCode.Code, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(alarm.OutOfServiceState.Id.Value, Is.False);
        }

        [Test]
        public void OnRemoveFromService2CalledAppliesComment()
        {
            TestableAlarm alarm = CreateTestableAlarm();
            AddTwoStateChild(alarm, BrowseNames.OutOfServiceState, s => alarm.OutOfServiceState = s);
            EnsureComment(alarm);
            var comment = new LocalizedText("en", "down for maintenance");

            ServiceResult result = alarm.CallRemoveFromService2(m_context, comment);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(alarm.Comment.Value, Is.EqualTo(comment));
        }

        // ---------- PlaceInService / PlaceInService2 ----------

        [Test]
        [TestCase("v1")]
        [TestCase("v2")]
        public void OnPlaceInServiceCalledReturnsBadNotSupportedWhenStateMissing(string variant)
        {
            TestableAlarm alarm = CreateTestableAlarm();

            ServiceResult result = variant == "v1"
                ? alarm.CallPlaceInService(m_context)
                : alarm.CallPlaceInService2(m_context, new LocalizedText("en", "x"));

            Assert.That(result.StatusCode.Code, Is.EqualTo(StatusCodes.BadNotSupported));
        }

        [Test]
        [TestCase("v1")]
        [TestCase("v2")]
        public void OnPlaceInServiceCalledReturnsBadNothingToDoWhenAlreadyInService(string variant)
        {
            TestableAlarm alarm = CreateTestableAlarm();
            AddTwoStateChild(alarm, BrowseNames.OutOfServiceState, s => alarm.OutOfServiceState = s);
            // Default OutOfService=false (in service).

            ServiceResult result = variant == "v1"
                ? alarm.CallPlaceInService(m_context)
                : alarm.CallPlaceInService2(m_context, new LocalizedText("en", "x"));

            Assert.That(result.StatusCode.Code, Is.EqualTo(StatusCodes.BadNothingToDo));
        }

        [Test]
        [TestCase("v1")]
        [TestCase("v2")]
        public void OnPlaceInServiceCalledInvokesRequestedDelegateAndTransitions(string variant)
        {
            TestableAlarm alarm = CreateTestableAlarm();
            AddTwoStateChild(alarm, BrowseNames.OutOfServiceState, s => alarm.OutOfServiceState = s);
            alarm.SetOutOfServiceState(m_context, true);

            int callCount = 0;
            bool? outOfServiceArg = null;
            alarm.OnOutOfServiceRequested = (c, a, oos) =>
            {
                callCount++;
                outOfServiceArg = oos;
                return ServiceResult.Good;
            };

            ServiceResult result = variant == "v1"
                ? alarm.CallPlaceInService(m_context)
                : alarm.CallPlaceInService2(m_context, new LocalizedText("en", "x"));

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(callCount, Is.EqualTo(1));
            Assert.That(outOfServiceArg, Is.False);
            Assert.That(alarm.OutOfServiceState.Id.Value, Is.False);
        }

        [Test]
        [TestCase("v1")]
        [TestCase("v2")]
        public void OnPlaceInServiceCalledVetoedByRequestedDelegateDoesNotTransition(string variant)
        {
            TestableAlarm alarm = CreateTestableAlarm();
            AddTwoStateChild(alarm, BrowseNames.OutOfServiceState, s => alarm.OutOfServiceState = s);
            alarm.SetOutOfServiceState(m_context, true);
            alarm.OnOutOfServiceRequested = (c, a, oos) => StatusCodes.BadUserAccessDenied;

            ServiceResult result = variant == "v1"
                ? alarm.CallPlaceInService(m_context)
                : alarm.CallPlaceInService2(m_context, new LocalizedText("en", "x"));

            Assert.That(result.StatusCode.Code, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(alarm.OutOfServiceState.Id.Value, Is.True);
        }

        [Test]
        public void OnPlaceInService2CalledAppliesComment()
        {
            TestableAlarm alarm = CreateTestableAlarm();
            AddTwoStateChild(alarm, BrowseNames.OutOfServiceState, s => alarm.OutOfServiceState = s);
            alarm.SetOutOfServiceState(m_context, true);
            EnsureComment(alarm);
            var comment = new LocalizedText("en", "back in service");

            ServiceResult result = alarm.CallPlaceInService2(m_context, comment);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(alarm.Comment.Value, Is.EqualTo(comment));
        }

        // ---------- Reset / Reset2 ----------

        [Test]
        [TestCase("v1")]
        [TestCase("v2")]
        public void OnResetCalledReturnsBadNotSupportedWhenLatchedStateMissing(string variant)
        {
            TestableAlarm alarm = CreateTestableAlarm();

            ServiceResult result = variant == "v1"
                ? alarm.CallReset(m_context)
                : alarm.CallReset2(m_context, new LocalizedText("en", "x"));

            Assert.That(result.StatusCode.Code, Is.EqualTo(StatusCodes.BadNotSupported));
        }

        private TestableAlarm CreateResettableAlarm()
        {
            TestableAlarm alarm = CreateTestableAlarm();
            AddTwoStateChild(alarm, BrowseNames.LatchedState, s => alarm.LatchedState = s);
            // Activate then deactivate so the alarm is latched but not active.
            alarm.SetActiveState(m_context, true);
            // Acknowledge / confirm so Reset prerequisites pass.
            if (alarm.AckedState is { } acked)
            {
                acked.Id!.Value = true;
            }
            if (alarm.ConfirmedState is { } confirmed)
            {
                confirmed.Id!.Value = true;
            }
            alarm.SetActiveState(m_context, false);
            return alarm;
        }

        [Test]
        [TestCase("v1")]
        [TestCase("v2")]
        public void OnResetCalledInvokesRequestedDelegateAndClearsLatch(string variant)
        {
            TestableAlarm alarm = CreateResettableAlarm();
            Assert.That(alarm.LatchedState.Id.Value, Is.True, "precondition: alarm is latched");

            int callCount = 0;
            alarm.OnResetRequested = (c, a) =>
            {
                callCount++;
                return ServiceResult.Good;
            };

            ServiceResult result = variant == "v1"
                ? alarm.CallReset(m_context)
                : alarm.CallReset2(m_context, new LocalizedText("en", "x"));

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(callCount, Is.EqualTo(1));
            Assert.That(alarm.LatchedState.Id.Value, Is.False);
        }

        [Test]
        [TestCase("v1")]
        [TestCase("v2")]
        public void OnResetCalledVetoedByRequestedDelegateDoesNotClearLatch(string variant)
        {
            TestableAlarm alarm = CreateResettableAlarm();
            alarm.OnResetRequested = (c, a) => StatusCodes.BadUserAccessDenied;

            ServiceResult result = variant == "v1"
                ? alarm.CallReset(m_context)
                : alarm.CallReset2(m_context, new LocalizedText("en", "x"));

            Assert.That(result.StatusCode.Code, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(alarm.LatchedState.Id.Value, Is.True);
        }

        [Test]
        public void OnReset2CalledAppliesComment()
        {
            TestableAlarm alarm = CreateResettableAlarm();
            EnsureComment(alarm);
            var comment = new LocalizedText("en", "reset by operator");

            ServiceResult result = alarm.CallReset2(m_context, comment);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(alarm.Comment.Value, Is.EqualTo(comment));
        }

        // ---------- GetGroupMemberships ----------

        [Test]
        public void OnGetGroupMembershipsCalledReturnsEmptyArrayWhenNoGroups()
        {
            TestableAlarm alarm = CreateTestableAlarm();
            ArrayOf<NodeId> groups = default;

            ServiceResult result = alarm.CallGetGroupMemberships(m_context, ref groups);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(groups.Count, Is.Zero);
        }

        [Test]
        public void OnGetGroupMembershipsCalledReturnsBadConditionDisabledWhenDisabled()
        {
            TestableAlarm alarm = CreateTestableAlarm();
            alarm.SetEnableState(m_context, false);

            ArrayOf<NodeId> groups = default;
            ServiceResult result = alarm.CallGetGroupMemberships(m_context, ref groups);

            Assert.That(result.StatusCode.Code, Is.EqualTo(StatusCodes.BadConditionDisabled));
        }

        [Test]
        public void OnGetGroupMembershipsCalledReturnsConfiguredGroups()
        {
            TestableAlarm alarm = CreateTestableAlarm();
            var groupNodeId = new NodeId(4242);
            alarm.AddReference(ReferenceTypeIds.AlarmGroupMember, isInverse: true, groupNodeId);

            ArrayOf<NodeId> groups = default;
            ServiceResult result = alarm.CallGetGroupMemberships(m_context, ref groups);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(groups.Count, Is.EqualTo(1));
            Assert.That(groups[0], Is.EqualTo(groupNodeId));
        }
    }
}
