/* Copyright (c) 1996-2022 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation Corporate Members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;
using System.Collections.Generic;
using System.Xml;
using System.Text;
using System.IO;
using System.Reflection;
using System.Threading;
using Opc.Ua;

namespace Opc.Ua
{
    public partial class AlarmConditionState
    {
        #region Initialization
        /// <summary>
        /// Called after a node is created.
        /// </summary>
        protected override void OnAfterCreate(ISystemContext context, NodeState node)
        {
            base.OnAfterCreate(context, node);

            if (this.ShelvingState != null)
            {
                if (this.ShelvingState.UnshelveTime != null)
                {
                    this.ShelvingState.UnshelveTime.OnSimpleReadValue = OnReadUnshelveTime;
                    this.ShelvingState.UnshelveTime.MinimumSamplingInterval = 1000;
                }

                this.ShelvingState.OneShotShelve.OnCallMethod = OnOneShotShelve;
                this.ShelvingState.OneShotShelve.OnReadExecutable = IsOneShotShelveExecutable;
                this.ShelvingState.OneShotShelve.OnReadUserExecutable = IsOneShotShelveExecutable;

                this.ShelvingState.TimedShelve.OnCall = OnTimedShelve;
                this.ShelvingState.TimedShelve.OnReadExecutable = IsTimedShelveExecutable;
                this.ShelvingState.TimedShelve.OnReadUserExecutable = IsTimedShelveExecutable;

                this.ShelvingState.Unshelve.OnCallMethod = OnUnshelve;
                this.ShelvingState.Unshelve.OnReadExecutable = IsUnshelveExecutable;
                this.ShelvingState.Unshelve.OnReadUserExecutable = IsUnshelveExecutable;
            }
        }
        #endregion

        #region IDisposable Members
        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (m_unshelveTimer != null)
                {
                    m_unshelveTimer.Dispose();
                    m_unshelveTimer = null;
                }
                if (m_updateUnshelveTimer != null)
                {
                    m_updateUnshelveTimer.Dispose();
                    m_updateUnshelveTimer = null;
                }
            }

            base.Dispose(disposing);
        }
        #endregion

        #region Public Properties - Operational

        /// <summary>
        /// Defines how often to update the UnshelveTime when Shelving State is TimedShelve or OneShotShelved.
        /// Defaults to 1000 ms
        /// </summary>
        public int UnshelveTimeUpdateRate
        {
            get
            {
                return m_unshelveTimeUpdateRate;
            }

            set
            {
                m_unshelveTimeUpdateRate = value;
            }
        }

        #endregion

        #region Public Methods
        /// <summary>
        /// Called when one or more sub-states change state.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="displayName">The display name for the effective state.</param>
        /// <param name="transitionTime">The transition time.</param>
        public virtual void SetActiveEffectiveSubState(ISystemContext context, LocalizedText displayName, DateTime transitionTime)
        {
            if (this.ActiveState.EffectiveDisplayName != null)
            {
                this.ActiveState.EffectiveDisplayName.Value = displayName;
            }

            if (this.ActiveState.EffectiveTransitionTime != null)
            {
                if (transitionTime != DateTime.MinValue)
                {
                    this.ActiveState.EffectiveTransitionTime.Value = transitionTime;
                }
                else
                {
                    this.ActiveState.EffectiveTransitionTime.Value = DateTime.UtcNow;
                }
            }
        }

        /// <summary>
        /// Gets when the alarm is scheduled to be unshelved.
        /// </summary>
        /// <value>The unshelve time.</value>
        public DateTime UnshelveTime
        {
            get { return m_unshelveTime; }
        }

        /// <summary>
        /// Sets the active state of the condition.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="active">if set to <c>true</c> the condition is active.</param>
        public virtual void SetActiveState(
            ISystemContext context,
            bool active)
        {
            TranslationInfo state = null;

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
                if (this.ShelvingState != null)
                {
                    if (m_oneShot)
                    {
                        SetShelvingState(context, false, false, 0);
                    }
                }

                state = new TranslationInfo(
                     "ConditionStateInactive",
                     "en-US",
                     ConditionStateNames.Inactive);
            }

            this.ActiveState.Value = new LocalizedText(state);
            this.ActiveState.Id.Value = active;

            if (this.ActiveState.TransitionTime != null)
            {
                this.ActiveState.TransitionTime.Value = DateTime.UtcNow;
            }

            UpdateEffectiveState(context);
        }

        /// <summary>
        /// Sets the suppressed state of the condition.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="suppressed">if set to <c>true</c> the condition is suppressed.</param>
        public virtual void SetSuppressedState(
            ISystemContext context,
            bool suppressed)
        {
            if (this.SuppressedState == null)
            {
                return;
            }

            TranslationInfo state = null;

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
                if (this.ShelvingState == null || this.ShelvingState.CurrentState.Id.Value == ObjectIds.ShelvedStateMachineType_Unshelved)
                {
                    SuppressedOrShelved.Value = false;
                }

                state = new TranslationInfo(
                     "ConditionStateUnsuppressed",
                     "en-US",
                     ConditionStateNames.Unsuppressed);
            }

            this.SuppressedState.Value = new LocalizedText(state);
            this.SuppressedState.Id.Value = suppressed;

            if (this.SuppressedState.TransitionTime != null)
            {
                this.SuppressedState.TransitionTime.Value = DateTime.UtcNow;
            }

            UpdateEffectiveState(context);
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
            if (this.ShelvingState == null)
            {
                return;
            }

            if (m_unshelveTimer != null)
            {
                m_unshelveTimer.Dispose();
                m_unshelveTimer = null;
            }

            if (m_updateUnshelveTimer != null)
            {
                m_updateUnshelveTimer.Dispose();
                m_updateUnshelveTimer = null;
            }

            m_unshelveTime = DateTime.MinValue;

            if (!shelved)
            {
                if (this.SuppressedState == null || !this.SuppressedState.Id.Value)
                {
                    SuppressedOrShelved.Value = false;
                }

                this.ShelvingState.UnshelveTime.Value = 0.0;

                this.ShelvingState.CauseProcessingCompleted(context, Methods.ShelvedStateMachineType_Unshelve);
            }
            else
            {
                SuppressedOrShelved.Value = true;
                m_oneShot = oneShot;

                // Unshelve time is still valid even for OneShotShelved -  See Mantis 6462

                double maxTimeShelved = double.MaxValue;
                if (this.MaxTimeShelved != null && this.MaxTimeShelved.Value > 0)
                {
                    maxTimeShelved = this.MaxTimeShelved.Value;
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

                this.ShelvingState.UnshelveTime.Value = shelveTime;
                m_unshelveTime = DateTime.UtcNow.AddMilliseconds((int)shelveTime);

                m_updateUnshelveTimer = new Timer(OnUnshelveTimeUpdate, context, m_unshelveTimeUpdateRate, m_unshelveTimeUpdateRate);

                m_unshelveTimer = new Timer(OnTimerExpired, context, (int)shelveTime, Timeout.Infinite);
                this.ShelvingState.CauseProcessingCompleted(context, state);
            }

            UpdateEffectiveState(context);
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

            if (this.EnabledState.Id.Value)
            {
                retainState = base.GetRetainState();

                if (!IsBranch())
                {
                    if (this.ActiveState.Id.Value)
                    {
                        retainState = true;
                    }
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

            if (method == null)
            {
                if (this.ShelvingState != null)
                {
                    method = this.ShelvingState.FindMethod(context, methodId);
                }
            }

            return method;
        }

        #endregion

        #region Event Handlers
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

        #endregion

        #region Protected Method
        /// <summary>
        /// Updates the effective state for the condition.
        /// </summary>
        /// <param name="context">The context.</param>
        protected override void UpdateEffectiveState(ISystemContext context)
        {
            if (!this.EnabledState.Id.Value)
            {
                base.UpdateEffectiveState(context);
                return;
            }

            StringBuilder builder = new StringBuilder();

            string locale = null;

            if (this.ActiveState.Value != null)
            {
                locale = this.ActiveState.Value.Locale;

                if (this.ActiveState.Id.Value)
                {
                    if (this.ActiveState.EffectiveDisplayName != null && !LocalizedText.IsNullOrEmpty(this.ActiveState.EffectiveDisplayName.Value))
                    {
                        builder.Append(this.ActiveState.EffectiveDisplayName.Value);
                    }
                    else
                    {
                        builder.Append(this.ActiveState.Value);
                    }
                }
                else
                {
                    builder.Append(this.ActiveState.Value);
                }
            }

            LocalizedText suppressedState = null;

            if (this.SuppressedState != null)
            {
                if (this.SuppressedState.Id.Value)
                {
                    suppressedState = this.SuppressedState.Value;
                }
            }

            if (this.ShelvingState != null)
            {
                if (this.ShelvingState.CurrentState.Id.Value != ObjectIds.ShelvedStateMachineType_Unshelved)
                {
                    suppressedState = this.ShelvingState.CurrentState.Value;
                }
            }

            if (suppressedState != null)
            {
                builder.Append(" | ");
                builder.Append(suppressedState);
            }

            LocalizedText ackState = null;

            if (ConfirmedState != null)
            {
                if (!this.ConfirmedState.Id.Value)
                {
                    ackState = this.ConfirmedState.Value;
                }
            }

            if (AckedState != null)
            {
                if (!this.AckedState.Id.Value)
                {
                    ackState = this.AckedState.Value;
                }
            }

            if (ackState != null)
            {
                builder.Append(" | ");
                builder.Append(ackState);
            }

            LocalizedText effectiveState = new LocalizedText(locale, builder.ToString());

            SetEffectiveSubState(context, effectiveState, DateTime.MinValue);
        }

        /// <summary>
        /// Checks whether the OneShotShelve method is executable.
        /// </summary>
        protected ServiceResult OnReadUnshelveTime(
            ISystemContext context,
            NodeState node,
            ref object value)
        {
            double delta = 0;

            if (m_unshelveTime != DateTime.MinValue)
            {
                delta = (m_unshelveTime - DateTime.UtcNow).TotalMilliseconds;

                if (delta < 0)
                {
                    m_unshelveTime = DateTime.MinValue;
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
            value = this.ShelvingState.IsCausePermitted(context, Methods.ShelvedStateMachineType_OneShotShelve, false);
            return ServiceResult.Good;
        }

        /// <summary>
        /// Handles the OneShotShelve method.
        /// </summary>
        protected virtual ServiceResult OnOneShotShelve(
            ISystemContext context,
            MethodState method,
            IList<object> inputArguments,
            IList<object> outputArguments)
        {
            ServiceResult error = null;

            try
            {
                if (!this.EnabledState.Id.Value)
                {
                    return error = StatusCodes.BadConditionDisabled;
                }

                if (!this.ShelvingState.IsCausePermitted(context, Methods.ShelvedStateMachineType_OneShotShelve, false))
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
                if (this.AreEventsMonitored)
                {
                    AuditConditionShelvingEventState e = new AuditConditionShelvingEventState(null);

                    TranslationInfo info = new TranslationInfo(
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
                    e.SetChildValue(context, BrowseNames.ShelvingTime, null, false);

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
            value = this.ShelvingState.IsCausePermitted(context, Methods.ShelvedStateMachineType_TimedShelve, false);
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
                if (!this.EnabledState.Id.Value)
                {
                    return error = StatusCodes.BadConditionDisabled;
                }

                if (shelvingTime <= 0 || (this.MaxTimeShelved != null && shelvingTime > this.MaxTimeShelved.Value))
                {
                    return error = StatusCodes.BadShelvingTimeOutOfRange;
                }

                if (!this.ShelvingState.IsCausePermitted(context, Methods.ShelvedStateMachineType_TimedShelve, false))
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
                if (this.AreEventsMonitored)
                {
                    AuditConditionShelvingEventState e = new AuditConditionShelvingEventState(null);

                    TranslationInfo info = new TranslationInfo(
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
                    e.SetChildValue(context, BrowseNames.InputArguments, new object[] { shelvingTime }, false);

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
            value = this.ShelvingState.IsCausePermitted(context, Methods.ShelvedStateMachineType_Unshelve, false);
            return ServiceResult.Good;
        }

        /// <summary>
        /// Handles the Unshelve method.
        /// </summary>
        protected virtual ServiceResult OnUnshelve(
            ISystemContext context,
            MethodState method,
            IList<object> inputArguments,
            IList<object> outputArguments)
        {
            ServiceResult error = null;

            try
            {
                if (!this.EnabledState.Id.Value)
                {
                    return error = StatusCodes.BadConditionDisabled;
                }

                if (!this.ShelvingState.IsCausePermitted(context, Methods.ShelvedStateMachineType_Unshelve, false))
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
                if (this.AreEventsMonitored)
                {
                    AuditConditionShelvingEventState e = new AuditConditionShelvingEventState(null);

                    TranslationInfo info = new TranslationInfo(
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
                    e.SetChildValue(context, BrowseNames.ShelvingTime, null, false);

                    ReportEvent(context, e);
                }
            }

            return error;
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Called when timed shelve period expires.
        /// </summary>
        private void OnTimerExpired(object state)
        {
            try
            {
                if (OnTimedUnshelve != null)
                {
                    OnTimedUnshelve((ISystemContext)state, this);
                }
                this.OnUnshelveTimeUpdate(state);
            }
            catch (Exception e)
            {
                Utils.LogError(e, "Unexpected error unshelving alarm.");
            }
        }

        /// <summary>
        /// Called when shelved state is not Unshelved to update the UnshelveTime value.
        /// </summary>
        private void OnUnshelveTimeUpdate(object state)
        {
            try
            {
                ISystemContext context = (ISystemContext)state;
                object unshelveTimeObject = new object();
                OnReadUnshelveTime(context, null, ref unshelveTimeObject);
                double unshelveTime = (double)unshelveTimeObject;
                if (unshelveTime != this.ShelvingState.UnshelveTime.Value)
                {
                    this.ShelvingState.UnshelveTime.Value = unshelveTime;
                    this.ClearChangeMasks(context, true);
                }
            }
            catch (Exception e)
            {
                Utils.LogError(e, "Unexpected error updating UnshelveTime.");
            }
        }

        #endregion

        #region Private Fields
        private DateTime m_unshelveTime;
        private bool m_oneShot;
        private Timer m_unshelveTimer;
        private Timer m_updateUnshelveTimer;
        private int m_unshelveTimeUpdateRate = 1000;
        #endregion
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
