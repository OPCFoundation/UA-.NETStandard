/* Copyright (c) 1996-2019 The OPC Foundation. All rights reserved.
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

                // report a state change event.
                if (this.AreEventsMonitored)
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
                SetComment(context, comment, GetCurrentUserId(context));
            }

            if (this.AreEventsMonitored)
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

                e.SourceName.Value = "Attribute/Call";

                e.MethodId = new PropertyState<NodeId>(e);
                e.MethodId.Value = method.NodeId;

                e.InputArguments = new PropertyState<object[]>(e);
                e.InputArguments.Value = new object[] { eventId, comment };

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
                AuditConditionCommentEventState e = new AuditConditionCommentEventState(null);

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

                e.SourceName.Value = "Attribute/Call";

                e.MethodId = new PropertyState<NodeId>(e);
                e.MethodId.Value = method.NodeId;

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

                e.SourceName.Value = "Attribute/Call";

                e.MethodId = new PropertyState<NodeId>(e);
                e.MethodId.Value = method.NodeId;

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
        #endregion
        
        #region Private Fields
        private bool m_autoReportStateChanges;
        #endregion
    }

    /// <summary>
    /// Used to recieve notifications when a condition is enabled or disabled.
    /// </summary>
    /// <param name="context">The current system context.</param>
    /// <param name="condition">The condition that raised the event.</param>
    /// <param name="enabling">True if the condition is moving/has moved to the Enabled state.</param>
    public delegate ServiceResult ConditionEnableEventHandler(
        ISystemContext context, 
        ConditionState condition,
        bool enabling);

    /// <summary>
    /// Used to recieve notifications when a comment is added.
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
