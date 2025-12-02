/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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

namespace Opc.Ua.Client
{
    /// <summary>
    /// The current status of monitored item.
    /// </summary>
    public sealed record class MonitoredItemStatus
    {
        /// <summary>
        /// The identifier assigned by the server.
        /// </summary>
        public uint Id { get; set; }

        /// <summary>
        /// Whether the item has been created on the server.
        /// </summary>
        public bool Created => Id != 0;

        /// <summary>
        /// Any error condition associated with the monitored item.
        /// </summary>
        public ServiceResult? Error { get; private set; }

        /// <summary>
        /// The node id being monitored.
        /// </summary>
        public NodeId NodeId { get; private set; } = NodeId.Null;

        /// <summary>
        /// The attribute being monitored.
        /// </summary>
        public uint AttributeId { get; private set; } = Attributes.Value;

        /// <summary>
        /// The range of array indexes to being monitored.
        /// </summary>
        public string? IndexRange { get; private set; }

        /// <summary>
        /// The encoding to use when returning notifications.
        /// </summary>
        public QualifiedName DataEncoding { get; private set; } = QualifiedName.Null;

        /// <summary>
        /// The monitoring mode.
        /// </summary>
        public MonitoringMode MonitoringMode { get; private set; } = MonitoringMode.Disabled;

        /// <summary>
        /// The identifier assigned by the client.
        /// </summary>
        public uint ClientHandle { get; private set; }

        /// <summary>
        /// The sampling interval.
        /// </summary>
        public double SamplingInterval { get; private set; }

        /// <summary>
        /// The filter to use to select values to return.
        /// </summary>
        public MonitoringFilter? Filter { get; private set; }

        /// <summary>
        /// The result of applying the filter
        /// </summary>
        public MonitoringFilterResult? FilterResult { get; private set; }

        /// <summary>
        /// The length of the queue used to buffer values.
        /// </summary>
        public uint QueueSize { get; private set; }

        /// <summary>
        /// Whether to discard the oldest entries in the queue when it is full.
        /// </summary>
        public bool DiscardOldest { get; private set; } = true;

        /// <summary>
        /// Updates the monitoring mode.
        /// </summary>
        public void SetMonitoringMode(MonitoringMode monitoringMode)
        {
            MonitoringMode = monitoringMode;
        }

        /// <summary>
        /// Updates the object with the results of a translate browse paths request.
        /// </summary>
        internal void SetResolvePathResult(ServiceResult? error)
        {
            Error = error;
        }

        /// <summary>
        /// Updates the object with the results of a create monitored item request.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="request"/> is <c>null</c>.</exception>
        internal void SetCreateResult(
            MonitoredItemCreateRequest request,
            MonitoredItemCreateResult result,
            ServiceResult? error)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            NodeId = request.ItemToMonitor.NodeId;
            AttributeId = request.ItemToMonitor.AttributeId;
            IndexRange = request.ItemToMonitor.IndexRange;
            DataEncoding = request.ItemToMonitor.DataEncoding;
            MonitoringMode = request.MonitoringMode;
            ClientHandle = request.RequestedParameters.ClientHandle;
            SamplingInterval = request.RequestedParameters.SamplingInterval;
            QueueSize = request.RequestedParameters.QueueSize;
            DiscardOldest = request.RequestedParameters.DiscardOldest;
            Filter = null;
            FilterResult = null;
            Error = error;

            if (request.RequestedParameters.Filter != null)
            {
                Filter = Utils.Clone(request.RequestedParameters.Filter.Body) as MonitoringFilter;
            }

            if (ServiceResult.IsGood(error))
            {
                Id = result.MonitoredItemId;
                SamplingInterval = result.RevisedSamplingInterval;
                QueueSize = result.RevisedQueueSize;

                if (result.FilterResult != null)
                {
                    FilterResult = Utils.Clone(result.FilterResult.Body) as MonitoringFilterResult;
                }
            }
        }

        /// <summary>
        /// Updates the object with the results of a transfer monitored item request.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="monitoredItem"/> is <c>null</c>.</exception>
        internal void SetTransferResult(MonitoredItem monitoredItem)
        {
            if (monitoredItem == null)
            {
                throw new ArgumentNullException(nameof(monitoredItem));
            }

            NodeId = monitoredItem.ResolvedNodeId;
            AttributeId = monitoredItem.AttributeId;
            IndexRange = monitoredItem.IndexRange;
            DataEncoding = monitoredItem.Encoding;
            MonitoringMode = monitoredItem.MonitoringMode;
            ClientHandle = monitoredItem.ClientHandle;
            SamplingInterval = monitoredItem.SamplingInterval;
            QueueSize = monitoredItem.QueueSize;
            DiscardOldest = monitoredItem.DiscardOldest;
            Filter = null;
            FilterResult = null;

            if (monitoredItem.Filter != null)
            {
                Filter = Utils.Clone(monitoredItem.Filter);
            }
        }

        /// <summary>
        /// Updates the object with the results of a modify monitored item request.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="request"/> is <c>null</c>.</exception>
        internal void SetModifyResult(
            MonitoredItemModifyRequest request,
            MonitoredItemModifyResult result,
            ServiceResult? error)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            Error = error;

            if (ServiceResult.IsGood(error))
            {
                ClientHandle = request.RequestedParameters.ClientHandle;
                SamplingInterval = request.RequestedParameters.SamplingInterval;
                QueueSize = request.RequestedParameters.QueueSize;
                DiscardOldest = request.RequestedParameters.DiscardOldest;
                Filter = null;
                FilterResult = null;

                if (request.RequestedParameters.Filter != null)
                {
                    Filter = Utils.Clone(
                        request.RequestedParameters.Filter.Body) as MonitoringFilter;
                }

                SamplingInterval = result.RevisedSamplingInterval;
                QueueSize = result.RevisedQueueSize;

                if (result.FilterResult != null)
                {
                    FilterResult = Utils.Clone(result.FilterResult.Body) as MonitoringFilterResult;
                }
            }
        }

        /// <summary>
        /// Updates the object with the results of a delete item request.
        /// </summary>
        internal void SetDeleteResult(ServiceResult? error)
        {
            Id = 0;
            Error = error;
        }

        /// <summary>
        /// Sets the error state for the monitored item status.
        /// </summary>
        internal void SetError(ServiceResult error)
        {
            Error = error;
        }

        /// <summary>
        /// Updates the error state based on a notification result.
        /// When a bad status code is received in a notification,
        /// the error is set. When a good status code is received,
        /// the error is cleared.
        /// </summary>
        internal void SetNotificationResult(ServiceResult? result)
        {
            if (result == null || ServiceResult.IsGood(result))
            {
                Error = null;
            }
            else
            {
                Error = result;
            }
        }
    }
}
