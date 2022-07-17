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
using Opc.Ua;

namespace Opc.Ua
{
    public partial class AcknowledgeableConditionState
    {
        #region Initialization
        /// <summary>
        /// Called after a node is created.
        /// </summary>
        protected override void OnAfterCreate(ISystemContext context, NodeState node)
        {
            base.OnAfterCreate(context, node);

            if (this.Acknowledge != null)
            {
                this.Acknowledge.OnCall = OnAcknowledgeCalled;
            }

            if (this.Confirm != null)
            {
                this.Confirm.OnCall = OnConfirmCalled;
            }
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Sets the acknowledged state of the condition.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="acknowledged">if set to <c>true</c> the condition is acknowledged.</param>
        public virtual void SetAcknowledgedState(
            ISystemContext context,
            bool acknowledged)
        {
            if (acknowledged)
            {
                UpdateStateAfterAcknowledge(context);
            }
            else
            {
                UpdateStateAfterUnacknowledge(context);
            }
        }

        /// <summary>
        /// Sets the confirmed state of the condition.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="confirmed">if set to <c>true</c> the condition is confirmed.</param>
        public virtual void SetConfirmedState(
            ISystemContext context,
            bool confirmed)
        {
            if (confirmed)
            {
                UpdateStateAfterConfirm(context);
            }
            else
            {
                UpdateStateAfterUnconfirm(context);
            }
        }
        #endregion

        #region Event Handlers
        /// <summary>
        /// Raised when a condition is acknowledged.
        /// </summary>
        /// <remarks>
        /// Return code can be used to cancel the operation.
        /// </remarks>
        public ConditionAddCommentEventHandler OnAcknowledge;

        /// <summary>
        /// Raised when a condition is confirmed.
        /// </summary>
        /// <remarks>
        /// Return code can be used to cancel the operation.
        /// </remarks>
        public ConditionAddCommentEventHandler OnConfirm;
        #endregion

        #region Protected Methods
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

            if (SupportsConfirm())
            {
                if (!this.ConfirmedState.Id.Value)
                {
                    SetEffectiveSubState(context, this.ConfirmedState.Value, DateTime.MinValue);
                    return;
                }
            }

            if (this.AckedState != null)
            {
                SetEffectiveSubState(context, this.AckedState.Value, DateTime.MinValue);
            }
        }

        /// <summary>
        /// Called when the Acknowledge method is called.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="method">The method being called.</param>
        /// <param name="objectId">The id of the object.</param>
        /// <param name="eventId">The identifier for the event which is the target for the comment.</param>
        /// <param name="comment">The comment.</param>
        /// <returns>Any error.</returns>
        protected virtual ServiceResult OnAcknowledgeCalled(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            byte[] eventId,
            LocalizedText comment)
        {
            ServiceResult error = ProcessBeforeAcknowledge(context, eventId, comment);

            if (ServiceResult.IsGood(error))
            {
                AcknowledgeableConditionState branch = GetAcknowledgeableBranch(eventId);

                if ( branch != null )
                {
                    branch.OnAcknowledgeCalled(context, method, objectId, eventId, comment);

                    if (SupportsConfirm())
                    {
                        ReplaceBranchEvent(eventId, branch);
                    }
                    else
                    { 
                        RemoveBranchEvent(eventId);
                    }
                }
                else
                {
                    SetAcknowledgedState(context, true);

                    if (SupportsConfirm())
                    {
                        SetConfirmedState(context, false);
                    }
                }

                // If this is a branch, the comment goes to both the branch and the original event
                if (CanSetComment(comment))
                {
                    SetComment(context, comment, GetCurrentUserId(context));
                }

                UpdateRetainState();

            }

            if (EventsMonitored())
            {
                // report a state change event.
                if (ServiceResult.IsGood(error))
                {
                    ReportStateChange(context, false);
                }

                // raise the audit event.
                AuditConditionAcknowledgeEventState e = new AuditConditionAcknowledgeEventState(null);

                TranslationInfo info = new TranslationInfo(
                    "AuditConditionAcknowledge",
                    "en-US",
                    "The Acknowledge method was called.");

                e.Initialize(
                    context,
                    this,
                    EventSeverity.Low,
                    new LocalizedText(info),
                    ServiceResult.IsGood(error),
                    DateTime.UtcNow);

                e.SetChildValue(context, BrowseNames.SourceNode, NodeId, false);
                e.SetChildValue(context, BrowseNames.SourceName, "Method/Acknowledge", false);

                e.SetChildValue(context, BrowseNames.MethodId, method.NodeId, false);
                e.SetChildValue(context, BrowseNames.InputArguments, new object[] { eventId, comment }, false);

                e.SetChildValue(context, BrowseNames.ConditionEventId, eventId, false);
                e.SetChildValue(context, BrowseNames.Comment, comment, false);

                ReportEvent(context, e);
            }

            return error;
        }

        /// <summary>
        /// Does any processing before adding a comment to a condition.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="eventId">The identifier for the event which is the target for the comment.</param>
        /// <param name="comment">The comment.</param>
        protected virtual ServiceResult ProcessBeforeAcknowledge(
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
            
            if (OnAcknowledge != null)
            {
                try
                {
                    return OnAcknowledge(context, this, eventId, comment);
                }
                catch (Exception e)
                {
                    return ServiceResult.Create(e, StatusCodes.BadUnexpectedError, "Unexpected error acknowledging a Condition.");
                }
            }

            return ServiceResult.Good;
        }

        /// <summary>
        /// Updates the condition state after enabling.
        /// </summary>
        /// <param name="context">The system context.</param>
        protected virtual void UpdateStateAfterAcknowledge(ISystemContext context)
        {
            TranslationInfo state = new TranslationInfo(
                "ConditionStateAcknowledged",
                "en-US",
                ConditionStateNames.Acknowledged);

            this.AckedState.Value = new LocalizedText(state);
            this.AckedState.Id.Value = true;

            if (this.AckedState.TransitionTime != null)
            {
                this.AckedState.TransitionTime.Value = DateTime.UtcNow;
            }

            UpdateEffectiveState(context);
        }

        /// <summary>
        /// Updates the condition state after disabling.
        /// </summary>
        /// <param name="context">The system context.</param>
        protected virtual void UpdateStateAfterUnacknowledge(ISystemContext context)
        {
            TranslationInfo state = new TranslationInfo(
                "ConditionStateUnacknowledged",
                "en-US",
                ConditionStateNames.Unacknowledged);

            this.AckedState.Value = new LocalizedText(state);
            this.AckedState.Id.Value = false;

            if (this.AckedState.TransitionTime != null)
            {
                this.AckedState.TransitionTime.Value = DateTime.UtcNow;
            }

            UpdateEffectiveState(context);
        }

        /// <summary>
        /// Called when the Confirm method is called.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="method">The method being called.</param>
        /// <param name="objectId">The id of the object.</param>
        /// <param name="eventId">The identifier for the event which is the target for the comment.</param>
        /// <param name="comment">The comment.</param>
        /// <returns>Any error.</returns>
        protected virtual ServiceResult OnConfirmCalled(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            byte[] eventId,
            LocalizedText comment)
        {
            ServiceResult error = ProcessBeforeConfirm(context, eventId, comment);

            if (ServiceResult.IsGood(error))
            {
                AcknowledgeableConditionState branch = GetAcknowledgeableBranch(eventId);

                if (branch != null)
                {
                    branch.OnConfirmCalled(context, method, objectId, eventId, comment);
                    RemoveBranchEvent(eventId);
                }
                else
                {
                    SetConfirmedState(context, true);
                }

                // If this is a branch, the comment goes to both the branch and the original event
                if (CanSetComment(comment))
                {
                    SetComment(context, comment, GetCurrentUserId(context));
                }

                UpdateRetainState();
            }

            if (EventsMonitored())
            {
                // report a state change event.
                if (ServiceResult.IsGood(error))
                {
                    ReportStateChange(context, false);
                }

                // raise the audit event.
                AuditConditionConfirmEventState e = new AuditConditionConfirmEventState(null);

                TranslationInfo info = new TranslationInfo(
                    "AuditConditionConfirm",
                    "en-US",
                    "The Confirm method was called.");

                e.Initialize(
                    context,
                    this,
                    EventSeverity.Low,
                    new LocalizedText(info),
                    ServiceResult.IsGood(error),
                    DateTime.UtcNow);

                e.SetChildValue(context, BrowseNames.SourceNode, NodeId, false);
                e.SetChildValue(context, BrowseNames.SourceName, "Method/Confirm", false);

                e.SetChildValue(context, BrowseNames.MethodId, method.NodeId, false);
                e.SetChildValue(context, BrowseNames.InputArguments, new object[] { eventId, comment }, false);

                e.SetChildValue(context, BrowseNames.ConditionEventId, eventId, false);
                e.SetChildValue(context, BrowseNames.Comment, comment, false);

                ReportEvent(context, e);
            }

            return error;
        }

        /// <summary>
        /// Does any processing before adding a comment to a condition.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="eventId">The identifier for the event which is the target for the comment.</param>
        /// <param name="comment">The comment.</param>
        protected virtual ServiceResult ProcessBeforeConfirm(
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

            if (OnConfirm != null)
            {
                try
                {
                    return OnConfirm(context, this, eventId, comment);
                }
                catch (Exception e)
                {
                    return ServiceResult.Create(e, StatusCodes.BadUnexpectedError, "Unexpected error confirming a Condition.");
                }
            }

            return ServiceResult.Good;
        }

        /// <summary>
        /// Updates the condition state after enabling.
        /// </summary>
        /// <param name="context">The system context.</param>
        protected virtual void UpdateStateAfterConfirm(ISystemContext context)
        {
            if (this.ConfirmedState != null)
            {
                TranslationInfo state = new TranslationInfo(
                    "ConditionStateConfirmed",
                    "en-US",
                    ConditionStateNames.Confirmed);

                this.ConfirmedState.Value = new LocalizedText(state);
                this.ConfirmedState.Id.Value = true;

                if (this.ConfirmedState.TransitionTime != null)
                {
                    this.ConfirmedState.TransitionTime.Value = DateTime.UtcNow;
                }

                UpdateEffectiveState(context);
            }
        }

        /// <summary>
        /// Updates the condition state after disabling.
        /// </summary>
        /// <param name="context">The system context.</param>
        protected virtual void UpdateStateAfterUnconfirm(ISystemContext context)
        {
            if (this.ConfirmedState != null)
            {
                TranslationInfo state = new TranslationInfo(
                    "ConditionStateUnconfirmed",
                    "en-US",
                    ConditionStateNames.Unconfirmed);

                this.ConfirmedState.Value = new LocalizedText(state);
                this.ConfirmedState.Id.Value = false;

                if (this.ConfirmedState.TransitionTime != null)
                {
                    this.ConfirmedState.TransitionTime.Value = DateTime.UtcNow;
                }

                UpdateEffectiveState(context);
            }
        }

        /// <summary>
        /// Determines if a comment should be added on Acknowledgment or Confirm.
        /// </summary>
        /// <param name="comment">The client provided comment.</param>
        /// <returns>Boolean stating whether the comment should be set</returns>
        /// <remarks>
        /// According to the specification for Alarms, the Acknowledgment states that
        /// "If the comment field is NULL (both locale and text are empty) it will be
        /// ignored and any existing comments will remain unchanged."
        /// This also applies to the Confirm method, although the spec needs updating
        /// (Mantis issue 6405)
        /// </remarks>
        private bool CanSetComment(LocalizedText comment)
        {
            bool canSetComment = false;

            if (comment != null)
            {
                canSetComment = true;

                bool emptyComment = comment.Text == null || comment.Text.Length == 0;
                bool emptyLocale = comment.Locale == null || comment.Locale.Length == 0;

                if (emptyComment && emptyLocale)
                {
                    canSetComment = false;
                }
            }

            return canSetComment;
        }

        /// <summary>
        /// Determines if this Event supports Confirm.
        /// </summary>
        /// <returns>
        /// Boolean stating whether the Confirm is supported
        /// </returns>
        public bool SupportsConfirm()
        {
            bool supportsConfirm = false;

            if (this.ConfirmedState != null && this.ConfirmedState.Value != null )
            {
                supportsConfirm = true;
            }

            return supportsConfirm;
        }

        /// <summary>
        /// Determines whether a specified branch exists, and returns it as AcknowledgeableConditionState
        /// </summary>
        /// <param name="eventId">Desired Event Id</param>
        /// <returns>
        /// AcknowledgeableConditionState branch if it exists
        /// </returns>
        private AcknowledgeableConditionState GetAcknowledgeableBranch(byte[] eventId)
        {
            AcknowledgeableConditionState acknowledgeableBranch = null;
            ConditionState branch = GetBranch(eventId);

            if (branch != null)
            {
                object acknowledgeable = branch as AcknowledgeableConditionState;
                if (acknowledgeable != null)
                {
                    acknowledgeableBranch = (AcknowledgeableConditionState)acknowledgeable;
                }
            }

            return acknowledgeableBranch;
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

                if (!this.AckedState.Id.Value)
                {
                    retainState = true;
                }
                else
                {
                    if ( SupportsConfirm() && !this.ConfirmedState.Id.Value)
                    {
                        retainState = true;
                    }
                }
            }

            return retainState;
        }



        #endregion

        #region Public Interface
        #endregion

        #region Private Fields
        #endregion
    }
}
