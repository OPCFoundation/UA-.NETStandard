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
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Opc.Ua
{
    public partial class AlarmConditionState
    {
        /// <summary>
        /// Create alarm condition
        /// </summary>
        /// <param name="telemetry">The telemetry context to use to create obvservability instruments</param>
        /// <param name="parent"></param>
        public AlarmConditionState(ITelemetryContext telemetry, NodeState parent)
            : this(parent)
        {
            m_logger = telemetry.CreateLogger<AlarmConditionState>();
        }

        /// <summary>
        /// Called after a node is created.
        /// </summary>
        protected override void OnAfterCreate(ISystemContext context, NodeState node)
        {
            base.OnAfterCreate(context, node);

            if (ShelvingState != null)
            {
                if (ShelvingState.UnshelveTime != null)
                {
                    ShelvingState.UnshelveTime.OnSimpleReadValue = OnReadUnshelveTime;
                    ShelvingState.UnshelveTime.MinimumSamplingInterval = 1000;
                }

                ShelvingState.OneShotShelve.OnCallMethod = OnOneShotShelve;
                ShelvingState.OneShotShelve.OnReadExecutable = IsOneShotShelveExecutable;
                ShelvingState.OneShotShelve.OnReadUserExecutable = IsOneShotShelveExecutable;

                ShelvingState.TimedShelve.OnCall = OnTimedShelve;
                ShelvingState.TimedShelve.OnReadExecutable = IsTimedShelveExecutable;
                ShelvingState.TimedShelve.OnReadUserExecutable = IsTimedShelveExecutable;

                ShelvingState.Unshelve.OnCallMethod = OnUnshelve;
                ShelvingState.Unshelve.OnReadExecutable = IsUnshelveExecutable;
                ShelvingState.Unshelve.OnReadUserExecutable = IsUnshelveExecutable;
            }
        }

        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                m_unshelveTimer?.Dispose();
                m_unshelveTimer = null;
                m_updateUnshelveTimer?.Dispose();
                m_updateUnshelveTimer = null;
            }

            base.Dispose(disposing);
        }

        /// <summary>
        /// Defines how often to update the UnshelveTime when Shelving State is TimedShelve or OneShotShelved.
        /// Defaults to 1000 ms
        /// </summary>
        public int UnshelveTimeUpdateRate { get; set; } = 1000;

        /// <summary>
        /// Called when one or more sub-states change state.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="displayName">The display name for the effective state.</param>
        /// <param name="transitionTime">The transition time.</param>
        public virtual void SetActiveEffectiveSubState(
            ISystemContext context,
            LocalizedText displayName,
            DateTime transitionTime)
        {
            ActiveState.EffectiveDisplayName?.Value = displayName;

            if (ActiveState.EffectiveTransitionTime != null)
            {
                if (transitionTime != DateTime.MinValue)
                {
                    ActiveState.EffectiveTransitionTime.Value = transitionTime;
                }
                else
                {
                    ActiveState.EffectiveTransitionTime.Value = DateTime.UtcNow;
                }
            }
        }

        /// <summary>
        /// Gets when the alarm is scheduled to be unshelved.
        /// </summary>
        /// <value>The unshelve time.</value>
        public DateTime UnshelveTime { get; private set; }

        /// <summary>
        /// Sets the active state of the condition.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="active">if set to <c>true</c> the condition is active.</param>
        public virtual void SetActiveState(ISystemContext context, bool active)
        {
            TranslationInfo state;
            if (active)
            {
                state = new TranslationInfo(
                    "ConditionStateActive",
                    "en-US",
                    ConditionStateNames.Active);
            }
            else
            {
                // update shelving state if one shot mode.
                if (ShelvingState != null && m_oneShot)
                {
                    SetShelvingState(context, false, false, 0);
                }

                state = new TranslationInfo(
                    "ConditionStateInactive",
                    "en-US",
                    ConditionStateNames.Inactive);
            }

            ActiveState.Value = new LocalizedText(state);
            ActiveState.Id.Value = active;

            ActiveState.TransitionTime?.Value = DateTime.UtcNow;

            ActiveState.Timestamp = DateTime.UtcNow;
            UpdateEffectiveState(context);
            ClearChangeMasks(context, includeChildren: true);
        }

        /// <summary>
        /// Sets the suppressed state of the condition.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="suppressed">if set to <c>true</c> the condition is suppressed.</param>
        public virtual void SetSuppressedState(ISystemContext context, bool suppressed)
        {
            if (SuppressedState == null)
            {
                return;
            }

            TranslationInfo state;
            if (suppressed)
            {
                SuppressedOrShelved.Value = true;

                state = new TranslationInfo(
                    "ConditionStateSuppressed",
                    "en-US",
                    ConditionStateNames.Suppressed);
            }
            else
            {
                if (ShelvingState == null ||
                    ShelvingState.CurrentState.Id.Value == ObjectIds
                        .ShelvedStateMachineType_Unshelved)
                {
                    SuppressedOrShelved.Value = false;
                }

                state = new TranslationInfo(
                    "ConditionStateUnsuppressed",
                    "en-US",
                    ConditionStateNames.Unsuppressed);
            }

            SuppressedState.Value = new LocalizedText(state);
            SuppressedState.Id.Value = suppressed;

            SuppressedState.TransitionTime?.Value = DateTime.UtcNow;

            SuppressedState.Timestamp = DateTime.UtcNow;
            UpdateEffectiveState(context);
            ClearChangeMasks(context, includeChildren: true);
        }

        /// <summary>
        /// Sets the shelving state of the condition.
        /// </summary>
        /// <param name="context">The shelving context.</param>
        /// <param name="shelved">if set to <c>true</c> shelved.</param>
        /// <param name="oneShot">if set to <c>true</c> for a one shot shelve..</param>
        /// <param name="shelvingTime">The duration of a timed shelve.</param>
        public virtual void SetShelvingState(
            ISystemContext context,
            bool shelved,
            bool oneShot,
            double shelvingTime)
        {
            if (ShelvingState == null)
            {
                return;
            }

            m_unshelveTimer?.Dispose();
            m_unshelveTimer = null;

            m_updateUnshelveTimer?.Dispose();
            m_updateUnshelveTimer = null;

            UnshelveTime = DateTime.MinValue;

            if (!shelved)
            {
                if (SuppressedState == null || !SuppressedState.Id.Value)
                {
                    SuppressedOrShelved.Value = false;
                }

                ShelvingState.UnshelveTime.Value = 0.0;

                ShelvingState.CauseProcessingCompleted(
                    context,
                    Methods.ShelvedStateMachineType_Unshelve);
            }
            else
            {
                SuppressedOrShelved.Value = true;
                m_oneShot = oneShot;

                // Unshelve time is still valid even for OneShotShelved -  See Mantis 6462

                double maxTimeShelved = double.MaxValue;
                if (MaxTimeShelved != null && MaxTimeShelved.Value > 0)
                {
                    maxTimeShelved = MaxTimeShelved.Value;
                }

                double shelveTime = maxTimeShelved;

                uint state = Methods.ShelvedStateMachineType_OneShotShelve;
                if (!oneShot)
                {
                    if (shelvingTime > 0 && shelvingTime < shelveTime)
                    {
                        shelveTime = shelvingTime;
                    }
                    state = Methods.ShelvedStateMachineType_TimedShelve;
                }

                ShelvingState.UnshelveTime.Value = shelveTime;
                UnshelveTime = DateTime.UtcNow.AddMilliseconds((int)shelveTime);

                m_updateUnshelveTimer = new Timer(
                    OnUnshelveTimeUpdate,
                    context,
                    UnshelveTimeUpdateRate,
                    UnshelveTimeUpdateRate);

                m_unshelveTimer = new Timer(
                    OnTimerExpired,
                    context,
                    (int)shelveTime,
                    Timeout.Infinite);
                ShelvingState.CauseProcessingCompleted(context, state);
            }

            ShelvingState.UnshelveTime?.Timestamp = DateTime.UtcNow;

            UpdateEffectiveState(context);
            ClearChangeMasks(context, includeChildren: true);
        }

        /// <summary>
        /// Determines the desired Retain state based off of the values of AckedState and
        /// ConfirmedState if ConfirmedState is supported
        /// </summary>
        /// <remarks>
        /// All implementations of this method should check the enabled state
        /// </remarks>
        protected override bool GetRetainState()
        {
            bool retainState = false;

            if (EnabledState.Id.Value)
            {
                retainState = base.GetRetainState();

                if (!IsBranch() && ActiveState.Id.Value)
                {
                    retainState = true;
                }
            }

            return retainState;
        }

        /// <summary>
        /// Returns the method with the specified NodeId or MethodDeclarationId.  Looks specifically for
        /// Shelving State Methods
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="methodId">The identifier for the method to find.</param>
        /// <returns>Returns the method. Null if no method found.</returns>
        /// <remarks>
        /// It is possible to call ShelvingState Methods by using only the ConditionId (1.04 Part 9 5.8.10.4).
        /// Look to the Shelving State object for the method if it cannot be found by the normal mechanism.
        /// </remarks>
        public override MethodState FindMethod(ISystemContext context, NodeId methodId)
        {
            MethodState method = base.FindMethod(context, methodId);

            if (method == null && ShelvingState != null)
            {
                method = ShelvingState.FindMethod(context, methodId);
            }

            return method;
        }

        /// <summary>
        /// Raised when the alarm is shelved.
        /// </summary>
        /// <remarks>
        /// Return code can be used to cancel the operation.
        /// </remarks>
        public AlarmConditionShelveEventHandler OnShelve;

        /// <summary>
        /// Raised when the timed shelving period expires.
        /// </summary>
        public AlarmConditionTimedUnshelveEventHandler OnTimedUnshelve;

        /// <summary>
        /// Raised periodically when the shelving state is not Unshelved to update the UnshelveTimeValue.
        /// </summary>
        public AlarmConditionUnshelveTimeValueEventHandler OnUpdateUnshelveTime;

        /// <summary>
        /// Updates the effective state for the condition.
        /// </summary>
        /// <param name="context">The context.</param>
        protected override void UpdateEffectiveState(ISystemContext context)
        {
            if (!EnabledState.Id.Value)
            {
                base.UpdateEffectiveState(context);
                return;
            }

            var builder = new StringBuilder();

            string locale = ActiveState.Value.Locale;

            if (ActiveState.Id.Value)
            {
                if (ActiveState.EffectiveDisplayName != null &&
                    !ActiveState.EffectiveDisplayName.Value.IsNullOrEmpty)
                {
                    builder.Append(ActiveState.EffectiveDisplayName.Value);
                }
                else
                {
                    builder.Append(ActiveState.Value);
                }
            }
            else
            {
                builder.Append(ActiveState.Value);
            }

            LocalizedText suppressedState = default;

            if (SuppressedState != null && SuppressedState.Id.Value)
            {
                suppressedState = SuppressedState.Value;
            }

            if (ShelvingState != null &&
                ShelvingState.CurrentState.Id.Value != ObjectIds.ShelvedStateMachineType_Unshelved)
            {
                suppressedState = ShelvingState.CurrentState.Value;
            }

            if (!suppressedState.IsNullOrEmpty)
            {
                builder.Append(" | ")
                    .Append(suppressedState);
            }

            LocalizedText ackState = default;

            if (ConfirmedState != null && !ConfirmedState.Id.Value)
            {
                ackState = ConfirmedState.Value;
            }

            if (AckedState != null && !AckedState.Id.Value)
            {
                ackState = AckedState.Value;
            }

            if (!ackState.IsNullOrEmpty)
            {
                builder.Append(" | ")
                    .Append(ackState);
            }

            var effectiveState = new LocalizedText(locale, builder.ToString());

            SetEffectiveSubState(context, effectiveState, DateTime.MinValue);
        }

        /// <summary>
        /// Checks whether the OneShotShelve method is executable.
        /// </summary>
        protected ServiceResult OnReadUnshelveTime(
            ISystemContext context,
            NodeState node,
            ref Variant value)
        {
            double delta = 0;

            if (UnshelveTime != DateTime.MinValue)
            {
                delta = (UnshelveTime - DateTime.UtcNow).TotalMilliseconds;

                if (delta < 0)
                {
                    UnshelveTime = DateTime.MinValue;
                }
            }

            value = delta;

            return ServiceResult.Good;
        }

        /// <summary>
        /// Checks whether the OneShotShelve method is executable.
        /// </summary>
        protected ServiceResult IsOneShotShelveExecutable(
            ISystemContext context,
            NodeState node,
            ref bool value)
        {
            value = ShelvingState.IsCausePermitted(
                context,
                Methods.ShelvedStateMachineType_OneShotShelve,
                false);
            return ServiceResult.Good;
        }

        /// <summary>
        /// Handles the OneShotShelve method.
        /// </summary>
        protected virtual ServiceResult OnOneShotShelve(
            ISystemContext context,
            MethodState method,
            VariantCollection inputArguments,
            VariantCollection outputArguments)
        {
            ServiceResult error = null;

            try
            {
                if (!EnabledState.Id.Value)
                {
                    return error = StatusCodes.BadConditionDisabled;
                }

                if (!ShelvingState.IsCausePermitted(
                    context,
                    Methods.ShelvedStateMachineType_OneShotShelve,
                    false))
                {
                    return error = StatusCodes.BadConditionAlreadyShelved;
                }

                if (OnShelve == null)
                {
                    return error = StatusCodes.BadNotSupported;
                }

                error = OnShelve(context, this, true, true, 0);

                // report a state change event.
                if (ServiceResult.IsGood(error))
                {
                    ReportStateChange(context, false);
                }
            }
            finally
            {
                if (AreEventsMonitored)
                {
                    var e = new AuditConditionShelvingEventState(null);

                    var info = new TranslationInfo(
                        "AuditConditionOneShotShelve",
                        "en-US",
                        "The OneShotShelve method was called.");

                    e.Initialize(
                        context,
                        this,
                        EventSeverity.Low,
                        new LocalizedText(info),
                        ServiceResult.IsGood(error),
                        DateTime.UtcNow);

                    e.SetChildValue(context, BrowseNames.SourceNode, NodeId, false);
                    e.SetChildValue(context, BrowseNames.SourceName, "Method/OneShotShelve", false);

                    e.SetChildValue(context, BrowseNames.MethodId, method.NodeId, false);
                    e.SetChildValue(context, BrowseNames.ShelvingTime, Variant.Null, false);

                    ReportEvent(context, e);
                }
            }

            return error;
        }

        /// <summary>
        /// Checks whether the TimedShelve method is executable.
        /// </summary>
        protected ServiceResult IsTimedShelveExecutable(
            ISystemContext context,
            NodeState node,
            ref bool value)
        {
            value = ShelvingState.IsCausePermitted(
                context,
                Methods.ShelvedStateMachineType_TimedShelve,
                false);
            return ServiceResult.Good;
        }

        /// <summary>
        /// Handles the TimedShelve method.
        /// </summary>
        protected virtual ServiceResult OnTimedShelve(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            double shelvingTime)
        {
            ServiceResult error = null;

            try
            {
                if (!EnabledState.Id.Value)
                {
                    return error = StatusCodes.BadConditionDisabled;
                }

                if (shelvingTime <= 0 ||
                    (MaxTimeShelved != null && shelvingTime > MaxTimeShelved.Value))
                {
                    return error = StatusCodes.BadShelvingTimeOutOfRange;
                }

                if (!ShelvingState.IsCausePermitted(
                    context,
                    Methods.ShelvedStateMachineType_TimedShelve,
                    false))
                {
                    return error = StatusCodes.BadConditionAlreadyShelved;
                }

                if (OnShelve == null)
                {
                    return error = StatusCodes.BadNotSupported;
                }

                error = OnShelve(context, this, true, false, shelvingTime);

                // report a state change event.
                if (ServiceResult.IsGood(error))
                {
                    ReportStateChange(context, false);
                }
            }
            finally
            {
                if (AreEventsMonitored)
                {
                    var e = new AuditConditionShelvingEventState(null);

                    var info = new TranslationInfo(
                        "AuditConditionTimedShelve",
                        "en-US",
                        "The TimedShelve method was called.");

                    e.Initialize(
                        context,
                        this,
                        EventSeverity.Low,
                        new LocalizedText(info),
                        ServiceResult.IsGood(error),
                        DateTime.UtcNow);

                    e.SetChildValue(context, BrowseNames.SourceNode, NodeId, false);
                    e.SetChildValue(context, BrowseNames.SourceName, "Method/TimedShelve", false);

                    e.SetChildValue(context, BrowseNames.MethodId, method.NodeId, false);
                    e.SetChildValue(
                        context,
                        BrowseNames.InputArguments,
                        new Variant[] { shelvingTime },
                        false);

                    e.SetChildValue(context, BrowseNames.ShelvingTime, shelvingTime, false);

                    ReportEvent(context, e);
                }
            }

            return error;
        }

        /// <summary>
        /// Checks whether the Unshelve method is executable.
        /// </summary>
        protected ServiceResult IsUnshelveExecutable(
            ISystemContext context,
            NodeState node,
            ref bool value)
        {
            value = ShelvingState.IsCausePermitted(
                context,
                Methods.ShelvedStateMachineType_Unshelve,
                false);
            return ServiceResult.Good;
        }

        /// <summary>
        /// Handles the Unshelve method.
        /// </summary>
        protected virtual ServiceResult OnUnshelve(
            ISystemContext context,
            MethodState method,
            VariantCollection inputArguments,
            VariantCollection outputArguments)
        {
            ServiceResult error = null;

            try
            {
                if (!EnabledState.Id.Value)
                {
                    return error = StatusCodes.BadConditionDisabled;
                }

                if (!ShelvingState.IsCausePermitted(
                    context,
                    Methods.ShelvedStateMachineType_Unshelve,
                    false))
                {
                    return error = StatusCodes.BadConditionNotShelved;
                }

                if (OnShelve == null)
                {
                    return error = StatusCodes.BadNotSupported;
                }

                error = OnShelve(context, this, false, false, 0);

                // report a state change event.
                if (ServiceResult.IsGood(error))
                {
                    ReportStateChange(context, false);
                }
            }
            finally
            {
                // raise the audit event.
                if (AreEventsMonitored)
                {
                    var e = new AuditConditionShelvingEventState(null);

                    var info = new TranslationInfo(
                        "AuditConditionUnshelve",
                        "en-US",
                        "The Unshelve method was called.");

                    e.Initialize(
                        context,
                        this,
                        EventSeverity.Low,
                        new LocalizedText(info),
                        ServiceResult.IsGood(error),
                        DateTime.UtcNow);

                    e.SetChildValue(context, BrowseNames.SourceNode, NodeId, false);
                    e.SetChildValue(context, BrowseNames.SourceName, "Method/UnShelve", false);

                    e.SetChildValue(context, BrowseNames.MethodId, method.NodeId, false);
                    e.SetChildValue(context, BrowseNames.ShelvingTime, Variant.Null, false);

                    ReportEvent(context, e);
                }
            }

            return error;
        }

        /// <summary>
        /// Called when timed shelve period expires.
        /// </summary>
        private void OnTimerExpired(object state)
        {
            try
            {
                OnTimedUnshelve?.Invoke((ISystemContext)state, this);
                OnUnshelveTimeUpdate(state);
            }
            catch (Exception e)
            {
                m_logger.LogError(e, "Unexpected error unshelving alarm.");
            }
        }

        /// <summary>
        /// Called when shelved state is not Unshelved to update the UnshelveTime value.
        /// </summary>
        private void OnUnshelveTimeUpdate(object state)
        {
            try
            {
                var context = (ISystemContext)state;
                Variant unshelveTimeObject = default;
                OnReadUnshelveTime(context, null, ref unshelveTimeObject);
                double unshelveTime = (double)unshelveTimeObject;
                if (unshelveTime != ShelvingState.UnshelveTime.Value)
                {
                    ShelvingState.UnshelveTime.Value = unshelveTime;
                    ClearChangeMasks(context, true);
                }
            }
            catch (Exception e)
            {
                m_logger.LogError(e, "Unexpected error updating UnshelveTime.");
            }
        }

        private readonly ILogger m_logger;
        private bool m_oneShot;
        private Timer m_unshelveTimer;
        private Timer m_updateUnshelveTimer;
    }

    /// <summary>
    /// Used to receive notifications when a alarm is shelved or unshelved.
    /// </summary>
    /// <param name="context">The current system context.</param>
    /// <param name="alarm">The alarm that raised the event.</param>
    /// <param name="shelving">True if the condition is being shelved.</param>
    /// <param name="oneShot">True if the condition is being until it goes inactive (i.e. OneShotShelve).</param>
    /// <param name="shelvingTime">How long to shelve the condition.</param>
    public delegate ServiceResult AlarmConditionShelveEventHandler(
        ISystemContext context,
        AlarmConditionState alarm,
        bool shelving,
        bool oneShot,
        double shelvingTime);

    /// <summary>
    /// Used to receive notifications when the timed shelve period elapses for an alarm.
    /// </summary>
    /// <param name="context">The current system context.</param>
    /// <param name="alarm">The alarm that raised the event.</param>
    public delegate ServiceResult AlarmConditionTimedUnshelveEventHandler(
        ISystemContext context,
        AlarmConditionState alarm);

    /// <summary>
    /// Used to receive notifications when the shelving state is either OneShotShelved or TimedShelved.
    /// Updates the value of the UnshelveTime
    /// </summary>
    /// <param name="context">The current system context.</param>
    /// <param name="alarm">The alarm that raised the event.</param>
    public delegate ServiceResult AlarmConditionUnshelveTimeValueEventHandler(
        ISystemContext context,
        AlarmConditionState alarm);
}
