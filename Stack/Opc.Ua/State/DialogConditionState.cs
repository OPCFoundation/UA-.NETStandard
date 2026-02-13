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

            Respond?.OnCall = OnRespondCalled;
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

            DialogState.TransitionTime?.Value = DateTime.UtcNow;

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

            DialogState.TransitionTime?.Value = DateTime.UtcNow;

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

            if (!DialogState.Value.IsNullOrEmpty)
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
                    e.SetChildValue(
                        context,
                        BrowseNames.InputArguments,
                        new object[] { selectedResponse },
                        false);

                    e.SetChildValue(
                        context,
                        BrowseNames.SelectedResponse,
                        selectedResponse.ToString(CultureInfo.InvariantCulture),
                        false);

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
