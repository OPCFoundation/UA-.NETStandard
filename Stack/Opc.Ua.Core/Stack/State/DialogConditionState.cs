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
    public partial class DialogConditionState
    {
        #region Initialization
        /// <summary>
        /// Called after a node is created.
        /// </summary>
        protected override void OnAfterCreate(ISystemContext context, NodeState node)
        {
            base.OnAfterCreate(context, node);

            if (this.Respond != null)
            {
                this.Respond.OnCall = OnRespondCalled;
            }
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Activates the dialog.
        /// </summary>
        /// <param name="context">The system context.</param>
        public void Activate(ISystemContext context)
        {
            TranslationInfo state = new TranslationInfo(
                "ConditionStateDialogActive",
                "en-US",
                ConditionStateNames.Active);

            this.DialogState.Value = new LocalizedText(state);
            this.DialogState.Id.Value = true;

            if (this.DialogState.TransitionTime != null)
            {
                this.DialogState.TransitionTime.Value = DateTime.UtcNow;
            }

            UpdateEffectiveState(context);
        }

        /// <summary>
        /// Sets the response to the dialog.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="response">The selected response.</param>
        public virtual void SetResponse(ISystemContext context, int response)
        {
            this.LastResponse.Value = response;

            TranslationInfo state = new TranslationInfo(
                "ConditionStateDialogInactive",
                "en-US",
                ConditionStateNames.Inactive);

            this.DialogState.Value = new LocalizedText(state);
            this.DialogState.Id.Value = false;

            if (this.DialogState.TransitionTime != null)
            {
                this.DialogState.TransitionTime.Value = DateTime.UtcNow;
            }

            UpdateEffectiveState(context);
        }
        #endregion

        #region Event Handlers
        /// <summary>
        /// Raised when a dialog receives a Response.
        /// </summary>
        /// <remarks>
        /// Return code can be used to cancel the operation.
        /// </remarks>
        public DialogResponseEventHandler OnRespond;
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

            StringBuilder builder = new StringBuilder();

            string locale = null;

            if (this.DialogState.Value != null)
            {
                locale = this.DialogState.Value.Locale;
                builder.Append(this.DialogState.Value);
            }

            LocalizedText effectiveState = new LocalizedText(locale, builder.ToString());

            SetEffectiveSubState(context, effectiveState, DateTime.MinValue);
        }

        /// <summary>
        /// Called when the Respond method is called.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="method">The method being called.</param>
        /// <param name="objectId">The id of the object.</param>
        /// <param name="selectedResponse">The selected response.</param>
        /// <returns>Any error.</returns>
        protected virtual ServiceResult OnRespondCalled(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            int selectedResponse)
        {
            ServiceResult error = null;

            try
            {
                if (!this.EnabledState.Id.Value)
                {
                    return error = StatusCodes.BadConditionDisabled;
                }

                if (!this.DialogState.Id.Value)
                {
                    return error = StatusCodes.BadDialogNotActive;
                }

                if (selectedResponse < 0 || selectedResponse >= this.ResponseOptionSet.Value.Length)
                {
                    return error = StatusCodes.BadDialogResponseInvalid;
                }

                if (OnRespond == null)
                {
                    return error = StatusCodes.BadNotSupported;
                }

                error = OnRespond(context, this, selectedResponse);

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
                    AuditConditionRespondEventState e = new AuditConditionRespondEventState(null);

                    TranslationInfo info = new TranslationInfo(
                        "AuditConditionDialogResponse",
                        "en-US",
                        "The Respond method was called.");

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
                    e.InputArguments.Value = new object[] { selectedResponse };

                    ReportEvent(context, e);
                }
            }

            return error;
        }
        #endregion
    }

    /// <summary>
    /// Used to receive notifications when the dialog receives a response.
    /// </summary>
    public delegate ServiceResult DialogResponseEventHandler(
        ISystemContext context,
        DialogConditionState dialog,
        int selectedResponse);
}
