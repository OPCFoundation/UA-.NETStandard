/* Copyright (c) 1996-2020 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
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
                this.ShelvingState.Unshelve.OnReadExecutable = IsTimedShelveExecutable;
                this.ShelvingState.Unshelve.OnReadUserExecutable = IsTimedShelveExecutable;
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
            }

            base.Dispose(disposing);
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

            if (!shelved)
            {
                if (this.SuppressedState == null || !this.SuppressedState.Id.Value)
                {
                    SuppressedOrShelved.Value = false;
                }

                this.ShelvingState.CauseProcessingCompleted(context, Methods.ShelvedStateMachineType_Unshelve);
            }
            else
            {
                SuppressedOrShelved.Value = true;
                m_oneShot = oneShot;
                m_unshelveTime = DateTime.MinValue;

                if (oneShot)
                {                    
                    this.ShelvingState.CauseProcessingCompleted(context, Methods.ShelvedStateMachineType_OneShotShelve);
                }
                else
                {
                    if (shelvingTime > 0)
                    {
                        m_unshelveTime = DateTime.UtcNow.AddMilliseconds(shelvingTime);
                        m_unshelveTimer = new Timer(OnTimerExpired, context, (int)shelvingTime, Timeout.Infinite);
                    }

                    this.ShelvingState.CauseProcessingCompleted(context, Methods.ShelvedStateMachineType_TimedShelve);
                }
            }

            UpdateEffectiveState(context);
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

                    e.SourceName.Value = "Attribute/Call";

                    e.MethodId = new PropertyState<NodeId>(e);
                    e.MethodId.Value = method.NodeId;

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

                    e.SourceName.Value = "Attribute/Call";

                    e.MethodId = new PropertyState<NodeId>(e);
                    e.MethodId.Value = method.NodeId;

                    e.InputArguments = new PropertyState<object[]>(e);
                    e.InputArguments.Value = new object[] { shelvingTime };

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

                    e.SourceName.Value = "Attribute/Call";

                    e.MethodId = new PropertyState<NodeId>(e);
                    e.MethodId.Value = method.NodeId;

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
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error unshelving alarm.");
            }
        }
        #endregion

        #region Private Fields
        private DateTime m_unshelveTime;
        private bool m_oneShot;
        private Timer m_unshelveTimer;
        #endregion
    }

    /// <summary>
    /// Used to recieve notifications when a alarm is shelved or unshelved.
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
    /// Used to recieve notifications when the timed shelve period elapses for an alarm.
    /// </summary>
    /// <param name="context">The current system context.</param>
    /// <param name="alarm">The alarm that raised the event.</param>
    public delegate ServiceResult AlarmConditionTimedUnshelveEventHandler(
        ISystemContext context,
        AlarmConditionState alarm);
}
