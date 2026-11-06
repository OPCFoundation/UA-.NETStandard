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

namespace Opc.Ua.Server
{
    public partial class MonitoredItem
    {
        /// <inheritdoc/>
        public ValueTask WaitForValuePublicationAsync(
            ISession session,
            in DataValue value,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }
            if (value.IsNull)
            {
                throw new ArgumentException("The final sample must be a DataValue.", nameof(value));
            }
            MonitoredValuePublication.ValidateTimeout(timeout);
            return new ValueTask(WaitForValuePublicationCoreAsync(session, value.Copy(), timeout, cancellationToken));
        }

        private async Task WaitForValuePublicationCoreAsync(
            ISession session,
            DataValue value,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            MonitoredValuePublication? publication = null;
            try
            {
                lock (m_lock)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (m_isDisposed || MonitoringMode != MonitoringMode.Reporting || session.IsClosing)
                    {
                        return;
                    }
                    if (AttributeId != Attributes.Value ||
                        (MonitoredItemType & MonitoredItemTypeMask.DataChange) == 0 ||
                        AggregateFilter != null ||
                        m_subscription is not Subscription subscription)
                    {
                        throw new NotSupportedException(
                            "Final-value observation requires a non-aggregate Value item " +
                            "and the standard Publish pipeline.");
                    }
                    if (m_isDeleted || Generation is null || m_isTerminal)
                    {
                        throw new ServiceResultException(StatusCodes.BadNodeIdUnknown);
                    }
                    if (m_valuePublication != null || m_dataChangeQueueHandler?.HasRequiredValues == true)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadInvalidState, "A required observation is already pending for this item.");
                    }
                    publication = new MonitoredValuePublication(subscription);
                    if (!subscription.RegisterValuePublication(publication, session))
                    {
                        return;
                    }
                    m_valuePublication = publication;
                    m_finalValuePending = true;
                    AddRequiredValueToQueue(value, ServiceResult.Good, replaceExisting: false);
                }

                bool published = await publication.Completion.WaitAsync(timeout, m_timeProvider, cancellationToken)
                    .ConfigureAwait(false);
                if (published)
                {
                    lock (m_lock)
                    {
                        m_finalValuePublished = true;
                    }
                }
            }
            finally
            {
                lock (m_lock)
                {
                    if (publication != null && ReferenceEquals(m_valuePublication, publication))
                    {
                        m_dataChangeQueueHandler?.ReleaseRequiredValue(value);
                        m_valuePublication = null;
                        m_finalValuePending = false;
                    }
                    publication?.Complete(published: false);
                    publication?.Unregister();
                }
            }
        }

        private void PublishObservedValue(
            OperationContext context,
            Queue<MonitoredItemNotification> notifications,
            Queue<DiagnosticInfo> diagnostics,
            in DataValue value,
            ServiceResult error,
            bool required)
        {
            m_publishingFinalValue = required && m_finalValuePending && m_valuePublication != null;
            try
            {
                Publish(context, notifications, diagnostics, value, error);
                if (m_publishingFinalValue)
                {
                    m_finalValuePending = false;
                }
            }
            finally
            {
                m_publishingFinalValue = false;
            }
        }

        /// <summary>The single pending final observation, also protecting a queue-size-one value.</summary>
        private MonitoredValuePublication? m_valuePublication;

        /// <summary>Whether the final sample is still in the monitored-item queue.</summary>
        private bool m_finalValuePending;

        /// <summary>Whether the per-value Publish call is producing the final notification.</summary>
        private bool m_publishingFinalValue;

        /// <summary>Protects deletion after terminal publication from late samples, until an explicit rebind.</summary>
        private bool m_finalValuePublished;
    }
}
