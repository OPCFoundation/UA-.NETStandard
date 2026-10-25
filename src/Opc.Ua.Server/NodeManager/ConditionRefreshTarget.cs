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

using System.Collections.Generic;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Forwards a refresh callback's item operations while carrying its revocable source admission.
    /// </summary>
    /// <param name="item">The original subscription-owned item.</param>
    /// <param name="origin">The exact source admitted for this refresh callback.</param>
    /// <param name="session">The session that authorized the refresh.</param>
    /// <param name="authorization">The identity revision that authorized the refresh.</param>
    internal sealed class ConditionRefreshTarget(
        IEventMonitoredItem item,
        MasterNodeManager.SourceEmissionOrigin origin,
        ISession? session,
        NotificationAuthorization? authorization) : IEventMonitoredItem
    {
        /// <inheritdoc/>
        public IAsyncNodeManager NodeManager => item.NodeManager;

        /// <inheritdoc/>
        public ISession Session => item.Session;

        /// <inheritdoc/>
        public IUserIdentity EffectiveIdentity => item.EffectiveIdentity;

        /// <inheritdoc/>
        public uint Id => item.Id;

        /// <inheritdoc/>
        public uint SubscriptionId => item.SubscriptionId;

        /// <inheritdoc/>
        public bool IsDurable => item.IsDurable;

        /// <inheritdoc/>
        public uint ClientHandle => item.ClientHandle;

        /// <inheritdoc/>
        public ISubscription SubscriptionCallback
        {
            get => item.SubscriptionCallback;
            set => item.SubscriptionCallback = value;
        }

        /// <inheritdoc/>
        public object ManagerHandle => item.ManagerHandle;

        /// <inheritdoc/>
        public int MonitoredItemType => item.MonitoredItemType;

        /// <inheritdoc/>
        public bool IsReadyToPublish => item.IsReadyToPublish;

        /// <inheritdoc/>
        public bool IsReadyToTrigger
        {
            get => item.IsReadyToTrigger;
            set => item.IsReadyToTrigger = value;
        }

        /// <inheritdoc/>
        public bool IsResendData => item.IsResendData;

        /// <inheritdoc/>
        public NodeId NodeId => item.NodeId;

        /// <inheritdoc/>
        public MonitoringMode MonitoringMode => item.MonitoringMode;

        /// <inheritdoc/>
        public double SamplingInterval => item.SamplingInterval;

        /// <inheritdoc/>
        public bool MonitoringAllEvents => item.MonitoringAllEvents;

        /// <inheritdoc/>
        public EventFilter? EventFilter => item.EventFilter;

        /// <inheritdoc/>
        public void SetupResendDataTrigger()
        {
            item.SetupResendDataTrigger();
        }

        /// <inheritdoc/>
        public ServiceResult GetCreateResult(out MonitoredItemCreateResult result)
        {
            return item.GetCreateResult(out result);
        }

        /// <inheritdoc/>
        public ServiceResult GetModifyResult(out MonitoredItemModifyResult result)
        {
            return item.GetModifyResult(out result);
        }

        /// <inheritdoc/>
        public IStoredMonitoredItem ToStorableMonitoredItem()
        {
            return item.ToStorableMonitoredItem();
        }

        /// <inheritdoc/>
        public MonitoringMode SetMonitoringMode(MonitoringMode monitoringMode)
        {
            return item.SetMonitoringMode(monitoringMode);
        }

        /// <inheritdoc/>
        public void QueueEvent(IFilterTarget instance)
        {
            QueueEventCore(instance, null);
        }

        /// <inheritdoc/>
        public void QueueEvent(IFilterTarget instance, bool bypassFilter)
        {
            QueueEventCore(instance, bypassFilter);
        }

        /// <inheritdoc/>
        public bool Publish(OperationContext context, Queue<EventFieldList> notifications, uint maxNotificationsPerPublish)
        {
            return item.Publish(context, notifications, maxNotificationsPerPublish);
        }

        /// <inheritdoc/>
        public ServiceResult? ModifyAttributes(
            DiagnosticsMasks diagnosticsMasks,
            TimestampsToReturn timestampsToReturn,
            uint clientHandle,
            MonitoringFilter originalFilter,
            MonitoringFilter filterToUse,
            Range range,
            double samplingInterval,
            uint queueSize,
            bool discardOldest)
        {
            return item.ModifyAttributes(
                diagnosticsMasks, timestampsToReturn, clientHandle, originalFilter,
                filterToUse, range, samplingInterval, queueSize, discardOldest);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            item.Dispose();
        }

        /// <summary>
        /// Borrows the refresh source through the original virtual queue entry point.
        /// </summary>
        private void QueueEventCore(IFilterTarget instance, bool? bypassFilter)
        {
            if (!origin.TryCaptureForItem(item, out MasterNodeManager.SourceEmissionAdmission? emission))
            {
                return;
            }
            using var admission = emission;
            if (item is MonitoredItem concrete)
            {
                concrete.QueueEventIfCurrent(
                    session, authorization, instance, null, null, emission?.Generation, bypassFilter);
            }
            else if (bypassFilter is { } bypass)
            {
                item.QueueEvent(instance, bypass);
            }
            else
            {
                item.QueueEvent(instance);
            }
        }
    }
}
