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
using System.Linq;

namespace Opc.Ua
{
    public partial class ConditionState
    {
        /// <summary>
        /// Called after a node is created.
        /// </summary>
        protected override void OnAfterCreate(ISystemContext context, NodeState node)
        {
            base.OnAfterCreate(context, node);

            if (Enable != null)
            {
                Enable.OnCallMethod = OnEnableCalled;
            }

            if (Disable != null)
            {
                Disable.OnCallMethod = OnDisableCalled;
            }

            if (AddComment != null)
            {
                AddComment.OnCall = OnAddCommentCalled;
            }
        }

        /// <inheritdoc/>
        public PropertyState<bool> SupportsFilteredRetain
        {
            get => m_supportsFilteredRetain;
            set
            {
                if (!ReferenceEquals(m_supportsFilteredRetain, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_supportsFilteredRetain = value;
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether the condition will automatically report an event when a method call completes.
        /// </summary>
        /// <value>
        /// 	<c>true</c> if the condition automatically reports ecents; otherwise, <c>false</c>.
        /// </value>
        public bool AutoReportStateChanges { get; set; }

        /// <summary>
        /// Called when one or more sub-states change state.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="displayName">The display name for the effective state.</param>
        /// <param name="transitionTime">The transition time.</param>
        public virtual void SetEffectiveSubState(
            ISystemContext context,
            LocalizedText displayName,
            DateTime transitionTime)
        {
            if (EnabledState.EffectiveDisplayName != null)
            {
                EnabledState.EffectiveDisplayName.Value = displayName;
            }

            if (EnabledState.EffectiveTransitionTime != null)
            {
                if (transitionTime != DateTime.MinValue)
                {
                    EnabledState.EffectiveTransitionTime.Value = transitionTime;
                }
                else
                {
                    EnabledState.EffectiveTransitionTime.Value = DateTime.UtcNow;
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
            LastSeverity.Value = Severity.Value;
            Severity.Value = (ushort)severity;

            if (LastSeverity.SourceTimestamp != null)
            {
                LastSeverity.SourceTimestamp.Value = DateTime.UtcNow;
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
            if (Comment != null)
            {
                Comment.Value = comment;
                Comment.SourceTimestamp.Value = DateTime.UtcNow;

                if (ClientUserId != null)
                {
                    ClientUserId.Value = clientUserId;
                }
            }
        }

        /// <summary>
        /// Create a branch based off the original Event
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="branchId">The Desired Branch Id</param>
        /// <returns>ConditionState newly created branch</returns>
        public virtual ConditionState CreateBranch(ISystemContext context, NodeId branchId)
        {
            ConditionState state = null;

            Type alarmType = GetType();
            object branchedAlarm = Activator.CreateInstance(alarmType, this);
            if (branchedAlarm != null)
            {
                var branchedNodeState = (ConditionState)branchedAlarm;
                branchedNodeState.Initialize(context, this);
                branchedNodeState.BranchId.Value = branchId;
                branchedNodeState.AutoReportStateChanges = AutoReportStateChanges;
                branchedNodeState.ReportStateChange(context, false);

                string postEventId = Utils.ToHexString(branchedNodeState.EventId.Value);

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
        public Dictionary<string, ConditionState> GetBranches()
        {
            return m_branches ??= [];
        }

        /// <summary>
        /// Finds an event, whether it is the original event, or a branch
        /// </summary>
        /// <param name="eventId">Desired Event Id</param>
        /// <returns>ConditionState branch if it exists</returns>
        public virtual ConditionState GetEventByEventId(byte[] eventId)
        {
            if (EventId.Value.SequenceEqual(eventId))
            {
                return this;
            }

            return GetBranch(eventId);
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
        /// Replace the Event Id of a branch, usually due to an Acknowledgement
        /// </summary>
        /// <param name="originalEventId">Event Id prior to the Acknowledgement</param>
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

            if (Retain.Value != retainState)
            {
                Retain.Value = retainState;
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

            if (EnabledState.Id.Value)
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
        public bool EventsMonitored()
        {
            bool areEventsMonitored = AreEventsMonitored;

            if (IsBranch())
            {
                areEventsMonitored = Parent.AreEventsMonitored;
            }

            return areEventsMonitored;
        }

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

        /// <summary>
        /// Handles a condition refresh.
        /// </summary>
        public override void ConditionRefresh(
            ISystemContext context,
            List<IFilterTarget> events,
            bool includeChildren)
        {
            if (Retain.Value)
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
            if (!ignoreDisabledState && !EnabledState.Id.Value)
            {
                return;
            }

            if (AutoReportStateChanges)
            {
                // create a new event instance.
                EventId.Value = Guid.NewGuid().ToByteArray();
                Time.Value = DateTime.UtcNow;
                ReceiveTime.Value = Time.Value;

                ClearChangeMasks(context, includeChildren: true);

                // report a state change event.
                if (EventsMonitored())
                {
                    var snapshot = new InstanceStateSnapshot();
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
            SetEffectiveSubState(context, EnabledState.Value, DateTime.MinValue);
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
                branch?.OnAddCommentCalled(context, method, objectId, eventId, comment);

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
                var e = new AuditConditionCommentEventState(null);

                var info = new TranslationInfo(
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
                e.SetChildValue(
                    context,
                    BrowseNames.InputArguments,
                    new object[] { eventId, comment },
                    false);

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
            if (context is ISessionOperationContext operationContext &&
                operationContext.UserIdentity != null)
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

            if (!EnabledState.Id.Value)
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
                    return ServiceResult.Create(
                        e,
                        StatusCodes.BadUnexpectedError,
                        "Unexpected error adding a comment to a Condition.");
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
                foreach (ConditionState branch in branches.Values)
                {
                    branch.OnEnableCalled(context, method, inputArguments, outputArguments);
                }

                UpdateStateAfterEnable(context);
            }

            if (AreEventsMonitored)
            {
                // report a state change event.
                if (ServiceResult.IsGood(error))
                {
                    ReportStateChange(context, false);
                }

                // raise the audit event.
                var e = new AuditConditionEnableEventState(null);

                var info = new TranslationInfo(
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
            if (AreEventsMonitored)
            {
                // report a state change event.
                if (ServiceResult.IsGood(error))
                {
                    ReportStateChange(context, true);
                }

                // raise the audit event.
                var e = new AuditConditionEnableEventState(null);

                var info = new TranslationInfo(
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
        /// <param name="enabling">True if the condition is being enabled.</param>
        protected virtual ServiceResult ProcessBeforeEnableDisable(
            ISystemContext context,
            bool enabling)
        {
            if (enabling && EnabledState.Id.Value)
            {
                return StatusCodes.BadConditionAlreadyEnabled;
            }

            if (!enabling && !EnabledState.Id.Value)
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
                    return ServiceResult.Create(
                        e,
                        StatusCodes.BadUnexpectedError,
                        "Unexpected error enabling or disabling a Condition.");
                }
            }

            return ServiceResult.Good;
        }

        /// <summary>
        /// Evaluates and updates the Retain state when the condition is enabled.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <remarks>
        /// This method is called by UpdateStateAfterEnable to determine the Retain value.
        /// The default implementation calls UpdateRetainState() which uses GetRetainState().
        /// Derived classes can override this method to provide custom logic for determining
        /// the Retain value when the condition is enabled.
        /// </remarks>
        protected virtual void EvaluateRetainStateOnEnable(ISystemContext context)
        {
            UpdateRetainState();
        }

        /// <summary>
        /// Updates the condition state after enabling.
        /// </summary>
        /// <param name="context">The system context.</param>
        protected virtual void UpdateStateAfterEnable(ISystemContext context)
        {
            var state = new TranslationInfo(
                "ConditionStateEnabled",
                "en-US",
                ConditionStateNames.Enabled);

            EnabledState.Value = new LocalizedText(state);
            EnabledState.Id.Value = true;

            if (EnabledState.TransitionTime != null)
            {
                EnabledState.TransitionTime.Value = DateTime.UtcNow;
            }

            EvaluateRetainStateOnEnable(context);

            UpdateEffectiveState(context);
        }

        /// <summary>
        /// Updates the condition state after disabling.
        /// </summary>
        /// <param name="context">The system context.</param>
        protected virtual void UpdateStateAfterDisable(ISystemContext context)
        {
            var state = new TranslationInfo(
                "ConditionStateDisabled",
                "en-US",
                ConditionStateNames.Disabled);

            Retain.Value = false;
            EnabledState.Value = new LocalizedText(state);
            EnabledState.Id.Value = false;

            if (EnabledState.TransitionTime != null)
            {
                EnabledState.TransitionTime.Value = DateTime.UtcNow;
            }

            UpdateEffectiveState(context);
        }

        /// <summary>
        /// Determines if this event is a branch
        /// </summary>
        /// <returns>true if branch</returns>
        protected bool IsBranch()
        {
            return !BranchId.Value.IsNullNodeId;
        }

        /// <summary>
        /// Branches
        /// </summary>
        protected Dictionary<string, ConditionState> m_branches;
        private PropertyState<bool> m_supportsFilteredRetain;
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
