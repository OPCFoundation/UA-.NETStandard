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
using System.Globalization;
using System.Text;

namespace Opc.Ua
{
    public partial class DialogConditionState
    {
        /// <summary>
        /// Called after a node is created.
        /// </summary>
        protected override void OnAfterCreate(ISystemContext context, NodeState node)
        {
            base.OnAfterCreate(context, node);

            if (Respond != null)
            {
                Respond.OnCall = OnRespondCalled;
            }
        }

        /// <summary>
        /// Activates the dialog.
        /// </summary>
        /// <param name="context">The system context.</param>
        public void Activate(ISystemContext context)
        {
            var state = new TranslationInfo(
                "ConditionStateDialogActive",
                "en-US",
                ConditionStateNames.Active);

            DialogState.Value = new LocalizedText(state);
            DialogState.Id.Value = true;

            if (DialogState.TransitionTime != null)
            {
                DialogState.TransitionTime.Value = DateTime.UtcNow;
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
            LastResponse.Value = response;

            var state = new TranslationInfo(
                "ConditionStateDialogInactive",
                "en-US",
                ConditionStateNames.Inactive);

            DialogState.Value = new LocalizedText(state);
            DialogState.Id.Value = false;

            if (DialogState.TransitionTime != null)
            {
                DialogState.TransitionTime.Value = DateTime.UtcNow;
            }

            UpdateEffectiveState(context);
        }

        /// <summary>
        /// Raised when a dialog receives a Response.
        /// </summary>
        /// <remarks>
        /// Return code can be used to cancel the operation.
        /// </remarks>
        public DialogResponseEventHandler OnRespond;

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

            string locale = null;

            if (DialogState.Value != null)
            {
                locale = DialogState.Value.Locale;
                builder.Append(DialogState.Value);
            }

            var effectiveState = new LocalizedText(locale, builder.ToString());

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
                if (!EnabledState.Id.Value)
                {
                    return error = StatusCodes.BadConditionDisabled;
                }

                if (!DialogState.Id.Value)
                {
                    return error = StatusCodes.BadDialogNotActive;
                }

                if (selectedResponse < 0 || selectedResponse >= ResponseOptionSet.Value.Length)
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
                if (AreEventsMonitored)
                {
                    var e = new AuditConditionRespondEventState(null);

                    var info = new TranslationInfo(
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

                    e.SetChildValue(context, BrowseNames.SourceNode, NodeId, false);
                    e.SetChildValue(context, BrowseNames.SourceName, "Method/Respond", false);

                    e.SetChildValue(context, BrowseNames.MethodId, method.NodeId, false);
                    e.SetChildValue(context, BrowseNames.InputArguments, new object[] { selectedResponse }, false);

                    e.SetChildValue(context, BrowseNames.SelectedResponse, selectedResponse.ToString(CultureInfo.InvariantCulture), false);

                    ReportEvent(context, e);
                }
            }

            return error;
        }
    }

    /// <summary>
    /// Used to receive notifications when the dialog receives a response.
    /// </summary>
    public delegate ServiceResult DialogResponseEventHandler(
        ISystemContext context,
        DialogConditionState dialog,
        int selectedResponse);
}
