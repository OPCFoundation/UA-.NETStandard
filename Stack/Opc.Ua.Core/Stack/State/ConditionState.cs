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
using System.Diagnostics;
using System.Linq;
using System.Xml;
using System.Text;
using System.IO;
using System.Reflection;
using Opc.Ua;

namespace Opc.Ua
{
    public partial class ConditionState
    {
        #region Initialization
        /// <summary>
        /// Called after a node is created.
        /// </summary>
        protected override void OnAfterCreate(ISystemContext context, NodeState node)
        {
            base.OnAfterCreate(context, node);

            if (this.Enable != null)
            {
                this.Enable.OnCallMethod = OnEnableCalled;
            }

            if (this.Disable != null)
            {
                this.Disable.OnCallMethod = OnDisableCalled;
            }

            if (this.AddComment != null)
            {
                this.AddComment.OnCall = OnAddCommentCalled;
            }
        }
        #endregion
        
        #region Public Methods
        /// <summary>
        /// Gets or sets a value indicating whether the condition will automatically report an event when a method call completes.
        /// </summary>
        /// <value>
        /// 	<c>true</c> if the condition automatically reports ecents; otherwise, <c>false</c>.
        /// </value>
        public bool AutoReportStateChanges
        {
            get { return m_autoReportStateChanges; }
            set { m_autoReportStateChanges = value; }
        }

        /// <summary>
        /// Called when one or more sub-states change state.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="displayName">The display name for the effective state.</param>
        /// <param name="transitionTime">The transition time.</param>
        public virtual void SetEffectiveSubState(ISystemContext context, LocalizedText displayName, DateTime transitionTime)
        {
            if (this.EnabledState.EffectiveDisplayName != null)
            {
                this.EnabledState.EffectiveDisplayName.Value = displayName;
            }

            if (this.EnabledState.EffectiveTransitionTime != null)
            {
                if (transitionTime != DateTime.MinValue)
                {
                    this.EnabledState.EffectiveTransitionTime.Value = transitionTime;
                }
                else
                {
                    this.EnabledState.EffectiveTransitionTime.Value = DateTime.UtcNow;
                }
            }
        }

        /// <summary>
        /// Sets the enable state for the condition without raising events.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="enabled">If true the condition is put into the Enabled state.</param>
        /// <remarks>This method ensures all related variables are set correctly.</remarks>
        public virtual void SetEnableState(ISystemContext context, bool enabled)
        {
            if (enabled)
            {
                UpdateStateAfterEnable(context);
            }
            else
            {
                UpdateStateAfterDisable(context);
            }
        }

        /// <summary>
        /// Sets the severity for the condition without raising events.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="severity">The event severity.</param>
        /// <remarks>This method ensures all related variables are set correctly.</remarks>
        public virtual void SetSeverity(ISystemContext context, EventSeverity severity)
        {
            this.LastSeverity.Value = this.Severity.Value;
            this.Severity.Value = (ushort)severity;

            if (this.LastSeverity.SourceTimestamp != null)
            {
                this.LastSeverity.SourceTimestamp.Value = DateTime.UtcNow;
            }
        }

        /// <summary>
        /// Updates the condition after adding a comment.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="comment">The comment.</param>
        /// <param name="clientUserId">The user that added the comment.</param>
        public virtual void SetComment(
            ISystemContext context,
            LocalizedText comment,
            string clientUserId)
        {
            if (this.Comment != null)
            {
                this.Comment.Value = comment;
                this.Comment.SourceTimestamp.Value = DateTime.UtcNow;

                if (this.ClientUserId != null)
                {
                    this.ClientUserId.Value = clientUserId;
                }
            }
        }

        /// <summary>
        /// Create a branch based off the original Event
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="branchId">The Desired Branch Id</param>
        /// <returns>ConditionState newly created branch</returns>
        public virtual ConditionState CreateBranch( ISystemContext context, NodeId branchId )
        {
            ConditionState state = null;

            Type alarmType = this.GetType();
            object branchedAlarm = Activator.CreateInstance(alarmType, this);
            if ( branchedAlarm != null )
            {
                ConditionState branchedNodeState = (ConditionState)branchedAlarm;
                branchedNodeState.Initialize(context, this);
                branchedNodeState.BranchId.Value = branchId;
                branchedNodeState.AutoReportStateChanges = AutoReportStateChanges;
                branchedNodeState.ReportStateChange(context, false );

                string postEventId = Utils.ToHexString(branchedNodeState.EventId.Value as byte[]);

                Dictionary<string, ConditionState> branches = GetBranches();

                branches.Add(postEventId, branchedNodeState);

                state = branchedNodeState;
            }

            return state;
        }

        /// <summary>
        /// Retrieves the branches for the current ConditionState.
        /// Creates the branches dictionary if required
        /// </summary>
        /// <remarks>
        /// Function exists because constructor is in auto generated code.
        /// </remarks>
        /// <returns></returns>
        public Dictionary< string, ConditionState >GetBranches()
        {
            if (m_branches == null)
            {
                m_branches = new Dictionary<string, ConditionState>();
            }

            return m_branches;
        }


        /// <summary>
        /// Finds an event, whether it is the original event, or a branch
        /// </summary>
        /// <param name="eventId">Desired Event Id</param>
        /// <returns>ConditionState branch if it exists</returns>
        public virtual ConditionState GetEventByEventId(byte[] eventId)
        {
            ConditionState alarm = null;

            if (this.EventId.Value.SequenceEqual(eventId))
            {
                alarm = this;
            }
            else
            {
                alarm = GetBranch(eventId);
            }

            return alarm;
        }

        /// <summary>
        /// Determines whether a specified branch exists, and returns it as ConditionState
        /// </summary>
        /// <param name="eventId">Desired Event Id</param>
        /// <returns>ConditionState branch if it exists</returns>
        public ConditionState GetBranch(byte[] eventId)
        {
            ConditionState alarm = null;

            Dictionary<string, ConditionState> branches = GetBranches();

            foreach (ConditionState branchEvent in branches.Values)
            {
                if (branchEvent.EventId.Value.SequenceEqual(eventId))
                {
                    alarm = branchEvent;
                    break;
                }
            }

            return alarm;
        }

        /// <summary>
        /// Replace the Event Id of a branch, usually due to an Acknowledgment
        /// </summary>
        /// <param name="originalEventId">Event Id prior to the Acknowledgment</param>
        /// <param name="alarm">Branch, containing the updated EventId to be stored</param>
        protected void ReplaceBranchEvent(byte[] originalEventId, ConditionState alarm)
        {
            string originalKey = Utils.ToHexString(originalEventId);
            string newKey = Utils.ToHexString(alarm.EventId.Value);

            Dictionary<string, ConditionState> branches = GetBranches();

            branches.Remove(originalKey);
            branches.Add(newKey, alarm);
        }

        /// <summary>
        /// Remove a specific branch
        /// </summary>
        /// <param name="eventId">The desired event to remove</param>
        protected void RemoveBranchEvent(byte[] eventId)
        {
            string key = Utils.ToHexString(eventId);

            Dictionary<string, ConditionState> branches = GetBranches();

            branches.Remove(key);
        }

        /// <summary>
        /// Clear all branches for this event
        /// </summary>
        public void ClearBranches()
        {
            Dictionary<string, ConditionState> branches = GetBranches();
            branches.Clear();
        }


        /// <summary>
        /// Updates the value of Retain based off all effective alarm properties
        /// ActiveState, AckedState, ConfirmedState, Branches, Enabled
        /// </summary>
        protected virtual void UpdateRetainState()
        {
            bool retainState = GetRetainState();

            if ( this.Retain.Value != retainState )
            {
                this.Retain.Value = retainState;
            }
        }

        /// <summary>
        /// Determines the desired Retain state based off Enabled state, and whether there are any branches
        /// </summary>
        /// <remarks>
        /// All implementations of this method should check the enabled state
        /// </remarks>
        protected virtual bool GetRetainState()
        {
            bool retainState = false;

            if (this.EnabledState.Id.Value)
            {
                Dictionary<string, ConditionState> branches = GetBranches();

                foreach (ConditionState branch in branches.Values)
                {
                    branch.UpdateRetainState();
                    if (branch.Retain.Value)
                    {
                        retainState = true;
                    }
                }
            }

            return retainState;
        }

        /// <summary>
        /// Get the Number of Branches currently utilized by this event
        /// </summary>
        /// <returns>
        /// Int contain the number of Branches
        /// </returns>
        public virtual int GetBranchCount()
        {
            Dictionary<string, ConditionState> branches = GetBranches();

            return branches.Count;
        }

        /// <summary>
        /// Determines if Events are monitored for this event.  If this is a branch, then the original event is checked
        /// </summary>
        /// <returns>
        /// Boolean determining if this event is monitored, and should be reported</returns>
        public bool EventsMonitored( )
        {
            bool areEventsMonitored = this.AreEventsMonitored;

            if ( IsBranch() )
            {
                areEventsMonitored = Parent.AreEventsMonitored;
            }

            return areEventsMonitored;
        }


        #endregion

        #region Event Handlers
        /// <summary>
        /// Raised when the condition is enabled or disabled.
        /// </summary>
        /// <remarks>
        /// Return code can be used to cancel the operation.
        /// </remarks>
        public ConditionEnableEventHandler OnEnableDisable;

        /// <summary>
        /// Raised when a comment is added to the condition.
        /// </summary>
        /// <remarks>
        /// Return code can be used to cancel the operation.
        /// </remarks>
        public ConditionAddCommentEventHandler OnAddComment;
        #endregion

        #region Protected Methods
        /// <summary>
        /// Handles a condition refresh.
        /// </summary>
        public override void ConditionRefresh(ISystemContext context, List<IFilterTarget> events, bool includeChildren)
        {
            if (this.Retain.Value)
            {
                Dictionary<string, ConditionState> branches = GetBranches();

                foreach (ConditionState branch in branches.Values)
                {
                    branch.ConditionRefresh(context, events, includeChildren);
                }
                events.Add(this);
            }
        }

        /// <summary>
        /// Reports the state change for the condition.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="ignoreDisabledState">if set to <c>true</c> the event is reported event if the condition is in the disabled state.</param>
        protected void ReportStateChange(ISystemContext context, bool ignoreDisabledState)
        {
            // check the disabled state.
            if (!ignoreDisabledState && !this.EnabledState.Id.Value)
            {
                return;
            }
                   
            if (AutoReportStateChanges)
            {
                // create a new event instance.
                this.EventId.Value = Guid.NewGuid().ToByteArray();
                this.Time.Value = DateTime.UtcNow;
                this.ReceiveTime.Value = this.Time.Value;

                ClearChangeMasks(context, includeChildren: true);

                // report a state change event.
                if (EventsMonitored())
                {
                    InstanceStateSnapshot snapshot = new InstanceStateSnapshot();
                    snapshot.Initialize(context, this);
                    ReportEvent(context, snapshot);
                }
            }
        }

        /// <summary>
        /// Updates the effective state for the condition.
        /// </summary>
        /// <param name="context">The context.</param>
        protected virtual void UpdateEffectiveState(ISystemContext context)
        {
            SetEffectiveSubState(context, this.EnabledState.Value, DateTime.MinValue); 
        }

        /// <summary>
        /// Called when the add comment method is called.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="method">The method being called.</param>
        /// <param name="objectId">The id of the object.</param>
        /// <param name="eventId">The identifier for the event which is the target for the comment.</param>
        /// <param name="comment">The comment.</param>
        /// <returns>Any error.</returns>
        protected virtual ServiceResult OnAddCommentCalled(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            byte[] eventId,
            LocalizedText comment)
        {
            ServiceResult error = ProcessBeforeAddComment(context, eventId, comment);

            if (ServiceResult.IsGood(error))
            {
                string currentUserId = GetCurrentUserId(context);
                ConditionState branch = GetBranch(eventId);
                if ( branch != null )
                {
                    branch.OnAddCommentCalled(context, method, objectId, eventId, comment);
                }

                SetComment(context, comment, currentUserId);
            }

            if (EventsMonitored())
            {
                // report a state change event.
                if (ServiceResult.IsGood(error))
                {
                    ReportStateChange(context, false);
                }

                // raise the audit event.
                AuditConditionCommentEventState e = new AuditConditionCommentEventState(null);

                TranslationInfo info = new TranslationInfo(
                    "AuditConditionComment",
                    "en-US",
                    "The AddComment method was called.");

                e.Initialize(
                    context,
                    this,
                    EventSeverity.Low,
                    new LocalizedText(info),
                    ServiceResult.IsGood(error),
                    DateTime.UtcNow);

                e.SetChildValue(context, BrowseNames.SourceNode, NodeId, false);
                e.SetChildValue(context, BrowseNames.SourceName, "Method/AddComment", false);

                e.SetChildValue(context, BrowseNames.MethodId, method.NodeId, false);
                e.SetChildValue(context, BrowseNames.InputArguments, new object[] { eventId, comment }, false);

                e.SetChildValue(context, BrowseNames.ConditionEventId, eventId, false);
                e.SetChildValue(context, BrowseNames.Comment, comment, false);

                ReportEvent(context, e);
            }

            return error;
        }

        /// <summary>
        /// Gets the current user id from the system context.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <returns>The display name for the current user.</returns>
        protected string GetCurrentUserId(ISystemContext context)
        {
            IOperationContext operationContext = context as IOperationContext;

            if (operationContext != null && operationContext.UserIdentity != null)
            {
                return operationContext.UserIdentity.DisplayName;
            }

            return null;
        }

        /// <summary>
        /// Does any processing before adding a comment to a condition.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="eventId">The identifier for the event which is the target for the comment.</param>
        /// <param name="comment">The comment.</param>
        protected virtual ServiceResult ProcessBeforeAddComment(
            ISystemContext context, 
            byte[] eventId,
            LocalizedText comment)
        {
            if (eventId == null)
            {
                return StatusCodes.BadEventIdUnknown;
            }

            if (!this.EnabledState.Id.Value)
            {
                return StatusCodes.BadConditionDisabled;
            }

            if (OnAddComment != null)
            {
                try
                {
                    return OnAddComment(context, this, eventId, comment);
                }
                catch (Exception e)
                {
                    return ServiceResult.Create(e, StatusCodes.BadUnexpectedError, "Unexpected error adding a comment to a Condition.");
                }
            }

            return ServiceResult.Good;
        }

        /// <summary>
        /// Handles the Enable method.
        /// </summary>
        protected virtual ServiceResult OnEnableCalled(
            ISystemContext context,
            MethodState method,
            IList<object> inputArguments,
            IList<object> outputArguments)
        {
            ServiceResult error = ProcessBeforeEnableDisable(context, true);

            if (ServiceResult.IsGood(error))
            {
                Dictionary<string, ConditionState> branches = GetBranches();

                // Enable all branches
                foreach ( ConditionState branch in branches.Values )
                {
                    branch.OnEnableCalled(context, method, inputArguments, outputArguments);
                }

                UpdateStateAfterEnable(context);
            }

            if (this.AreEventsMonitored)
            {
                // report a state change event.
                if (ServiceResult.IsGood(error))
                {
                    ReportStateChange(context, false);
                }

                // raise the audit event.
                AuditConditionEnableEventState e = new AuditConditionEnableEventState(null);

                TranslationInfo info = new TranslationInfo(
                    "AuditConditionEnable",
                    "en-US",
                    "The Enable method was called.");

                e.Initialize(
                    context,
                    this,
                    EventSeverity.Low,
                    new LocalizedText(info),
                    ServiceResult.IsGood(error),
                    DateTime.UtcNow);
                
                e.SetChildValue(context, BrowseNames.SourceNode, NodeId, false);
                e.SetChildValue(context, BrowseNames.SourceName, "Method/Enable", false);
                e.SetChildValue(context, BrowseNames.MethodId, method.NodeId, false);

                ReportEvent(context, e);
            }

            return error;
        }

        /// <summary>
        /// Handles the Disable method.
        /// </summary>
        protected virtual ServiceResult OnDisableCalled(
            ISystemContext context,
            MethodState method,
            IList<object> inputArguments,
            IList<object> outputArguments)
        {
            // check that method can be called.
            ServiceResult error = ProcessBeforeEnableDisable(context, false);

            if (ServiceResult.IsGood(error))
            {
                Dictionary<string, ConditionState> branches = GetBranches();

                foreach (ConditionState branch in branches.Values)
                {
                    branch.OnDisableCalled(context, method, inputArguments, outputArguments);
                }

                UpdateStateAfterDisable(context);
            }

            // raise the audit event.
            if (this.AreEventsMonitored)
            {
                // report a state change event.
                if (ServiceResult.IsGood(error))
                {
                    ReportStateChange(context, true);
                }

                // raise the audit event.
                AuditConditionEnableEventState e = new AuditConditionEnableEventState(null);

                TranslationInfo info = new TranslationInfo(
                    "AuditConditionEnable",
                    "en-US",
                    "The Disable method was called.");

                e.Initialize(
                    context,
                    this,
                    EventSeverity.Low,
                    new LocalizedText(info),
                    ServiceResult.IsGood(error),
                    DateTime.UtcNow);

                e.SetChildValue(context, BrowseNames.SourceNode, NodeId, false);
                e.SetChildValue(context, BrowseNames.SourceName, "Method/Disable", false);
                e.SetChildValue(context, BrowseNames.MethodId, method.NodeId, false);

                ReportEvent(context, e);
            }

            return error;
        }
        
        /// <summary>
        /// Does any processing before a condition is enabled or disabled.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="enabling">True is the condition is being enabled.</param>
        protected virtual ServiceResult ProcessBeforeEnableDisable(ISystemContext context, bool enabling)
        {
            if (enabling && this.EnabledState.Id.Value)
            {
                return StatusCodes.BadConditionAlreadyEnabled;
            }

            if (!enabling && !this.EnabledState.Id.Value)
            {
                return StatusCodes.BadConditionAlreadyDisabled;
            }

            if (OnEnableDisable != null)
            {
                try
                {
                    return OnEnableDisable(context, this, enabling);
                }
                catch (Exception e)
                {
                    return ServiceResult.Create(e, StatusCodes.BadUnexpectedError, "Unexpected error enabling or disabling a Condition.");
                }
            }

            return ServiceResult.Good;
        }

        /// <summary>
        /// Updates the condition state after enabling.
        /// </summary>
        /// <param name="context">The system context.</param>
        protected virtual void UpdateStateAfterEnable(ISystemContext context)
        {
            TranslationInfo state = new TranslationInfo(
                "ConditionStateEnabled",
                "en-US",
                ConditionStateNames.Enabled);

            this.Retain.Value = true;
            this.EnabledState.Value = new LocalizedText(state);
            this.EnabledState.Id.Value = true;

            if (this.EnabledState.TransitionTime != null)
            {
                this.EnabledState.TransitionTime.Value = DateTime.UtcNow;
            }

            UpdateEffectiveState(context);
        }

        /// <summary>
        /// Updates the condition state after disabling.
        /// </summary>
        /// <param name="context">The system context.</param>
        protected virtual void UpdateStateAfterDisable(ISystemContext context)
        {
            TranslationInfo state = new TranslationInfo(
                "ConditionStateDisabled",
                "en-US",
                ConditionStateNames.Disabled);

            this.Retain.Value = false;
            this.EnabledState.Value = new LocalizedText(state);
            this.EnabledState.Id.Value = false;

            if (this.EnabledState.TransitionTime != null)
            {
                this.EnabledState.TransitionTime.Value = DateTime.UtcNow;
            }

            UpdateEffectiveState(context);
        }

        /// <summary>
        /// Determines if this event is a branch
        /// </summary>
        /// <returns>true if branch</returns>
        protected bool IsBranch()
        {
            return !(this.BranchId.Value.IsNullNodeId);
        }
        #endregion

        #region Private Fields
        private bool m_autoReportStateChanges;
        /// <summary>
        /// 
        /// </summary>
        protected Dictionary<string, ConditionState> m_branches = null;

        #endregion
    }

    /// <summary>
    /// Used to receive notifications when a condition is enabled or disabled.
    /// </summary>
    /// <param name="context">The current system context.</param>
    /// <param name="condition">The condition that raised the event.</param>
    /// <param name="enabling">True if the condition is moving/has moved to the Enabled state.</param>
    public delegate ServiceResult ConditionEnableEventHandler(
        ISystemContext context, 
        ConditionState condition,
        bool enabling);

    /// <summary>
    /// Used to receive notifications when a comment is added.
    /// </summary>
    /// <param name="context">The current system context.</param>
    /// <param name="condition">The condition that raised the event.</param>
    /// <param name="eventId">The identifier for the event which is the target for the comment.</param>
    /// <param name="comment">The comment.</param>
    public delegate ServiceResult ConditionAddCommentEventHandler(
        ISystemContext context,
        ConditionState condition,
        byte[] eventId,
        LocalizedText comment);
}
