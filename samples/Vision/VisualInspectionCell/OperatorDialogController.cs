/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;

namespace Vision.VisualInspectionCell
{
    internal enum OperatorDisposition
    {
        AcceptAsOk,
        AcceptAsNotOk,
        Reinspect,
        Stop
    }

    internal sealed partial class OperatorDialogController
    {
        public OperatorDialogController(
            VisualInspectionFeedbackSink feedbackSink,
            ILogger<OperatorDialogController> logger)
        {
            m_feedbackSink = feedbackSink ?? throw new ArgumentNullException(nameof(feedbackSink));
            m_logger = logger ?? throw new ArgumentNullException(nameof(logger));
            m_feedbackSink.AttachOperatorDialog(this);
        }

        public void Attach(ISystemContext context, DialogConditionState dialog)
        {
            m_context = context ?? throw new ArgumentNullException(nameof(context));
            m_dialog = dialog ?? throw new ArgumentNullException(nameof(dialog));
            dialog.OnRespond = OnRespond;
        }

        public void RequestDisposition(PublishedInspectionResult result)
        {
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }
            DialogConditionState dialog = RequireDialog();
            ISystemContext context = RequireContext();
            bool activated = false;
            lock (m_lock)
            {
                if (m_pending != null)
                {
                    m_queued.Enqueue(result);
                }
                else
                {
                    ActivateLocked(context, dialog, result);
                    activated = true;
                }
            }
            if (activated)
            {
                m_logger.DialogActivated(result.ResultId);
            }
            else
            {
                m_logger.DialogQueued(result.ResultId);
            }
        }

        private ServiceResult OnRespond(
            ISystemContext context,
            DialogConditionState dialog,
            int selectedResponse)
        {
            if (!Enum.IsDefined((OperatorDisposition)selectedResponse))
            {
                return StatusCodes.BadDialogResponseInvalid;
            }
            PublishedInspectionResult? pending;
            var disposition = (OperatorDisposition)selectedResponse;
            lock (m_lock)
            {
                pending = m_pending;
                if (pending == null)
                {
                    return StatusCodes.BadInvalidState;
                }
                m_pending = null;
                dialog.SetResponse(context, selectedResponse);
                if (m_queued.Count > 0)
                {
                    ActivateLocked(context, dialog, m_queued.Dequeue());
                }
                else
                {
                    dialog.Retain!.Value = false;
                    dialog.ClearChangeMasks(context, includeChildren: true);
                }
            }
            _ = m_feedbackSink.HandleOperatorDispositionAsync(pending, disposition, CancellationToken.None)
                .AsTask()
                .ContinueWith(
                    task => m_logger.OperatorDispositionFailed(pending.ResultId, task.Exception!.GetBaseException()),
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted,
                    TaskScheduler.Default);
            return ServiceResult.Good;
        }

        private void ActivateLocked(
            ISystemContext context,
            DialogConditionState dialog,
            PublishedInspectionResult result)
        {
            m_pending = result;
            dialog.Message!.Value = LocalizedText.From(
                "Human disposition required for inspection result " + result.ResultId + ".");
            dialog.SetEnableState(context, enabled: true);
            dialog.Retain!.Value = true;
            dialog.Activate(context);
            dialog.ClearChangeMasks(context, includeChildren: true);
            dialog.ReportEvent(context, dialog);
        }

        private DialogConditionState RequireDialog()
        {
            return m_dialog ?? throw new InvalidOperationException("The operator dialog is not attached.");
        }

        private ISystemContext RequireContext()
        {
            return m_context ?? throw new InvalidOperationException("The operator dialog context is not attached.");
        }

        private readonly VisualInspectionFeedbackSink m_feedbackSink;
        private readonly ILogger<OperatorDialogController> m_logger;
        private readonly Lock m_lock = new();
        private readonly Queue<PublishedInspectionResult> m_queued = [];
        private DialogConditionState? m_dialog;
        private ISystemContext? m_context;
        private PublishedInspectionResult? m_pending;
    }

    internal static partial class OperatorDialogControllerLog
    {
        [LoggerMessage(EventId = VisualInspectionCellEventIds.Dialog + 1,
            Level = LogLevel.Information,
            Message = "Activated operator disposition dialog for result {ResultId}.")]
        public static partial void DialogActivated(
            this ILogger<OperatorDialogController> logger,
            string resultId);

        [LoggerMessage(EventId = VisualInspectionCellEventIds.Dialog + 2,
            Level = LogLevel.Error,
            Message = "Operator disposition processing failed for result {ResultId}.")]
        public static partial void OperatorDispositionFailed(
            this ILogger<OperatorDialogController> logger,
            string resultId,
            Exception exception);

        [LoggerMessage(EventId = VisualInspectionCellEventIds.Dialog + 3,
            Level = LogLevel.Information,
            Message = "Queued operator disposition dialog for result {ResultId}.")]
        public static partial void DialogQueued(
            this ILogger<OperatorDialogController> logger,
            string resultId);
    }
}
