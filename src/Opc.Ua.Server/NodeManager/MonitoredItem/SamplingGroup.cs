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
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Server
{
    /// <summary>
    /// An object which periodically reads the items and updates the cache.
    /// </summary>
    public class SamplingGroup : IDisposable
    {
        /// <summary>
        /// Creates a new instance of a sampling group.
        /// </summary>
        public SamplingGroup(
            IServerInternal server,
            IAsyncNodeManager nodeManager,
            List<SamplingRateGroup> samplingRates,
            OperationContext context,
            double samplingInterval,
            IUserIdentity? savedOwnerIdentity = null)
            : this(
                server,
                nodeManager,
                samplingRates,
                context,
                samplingInterval,
                savedOwnerIdentity,
                timeProvider: null)
        {
        }

        /// <summary>
        /// Creates a new instance of a sampling group with an explicit
        /// <see cref="TimeProvider"/>.
        /// </summary>
        /// <param name="server">The server.</param>
        /// <param name="nodeManager">The owning node manager.</param>
        /// <param name="samplingRates">The supported sampling-rate groups.</param>
        /// <param name="context">The operation context.</param>
        /// <param name="samplingInterval">The requested sampling interval.</param>
        /// <param name="savedOwnerIdentity">Owner identity for sessionless groups.</param>
        /// <param name="timeProvider">
        /// Optional <see cref="TimeProvider"/> used for the sampling-loop pacing
        /// and for the wall-clock fallback timestamp emitted when a sampled
        /// read returns no value. When <c>null</c>, the time provider exposed
        /// by the server (via <see cref="ITimeProviderProvider"/>) is used,
        /// falling back to <see cref="TimeProvider.System"/>.
        /// </param>
        public SamplingGroup(
            IServerInternal server,
            IAsyncNodeManager nodeManager,
            List<SamplingRateGroup> samplingRates,
            OperationContext context,
            double samplingInterval,
            IUserIdentity? savedOwnerIdentity,
            TimeProvider? timeProvider)
        {
            m_server = server ?? throw new ArgumentNullException(nameof(server));
            m_timeProvider = timeProvider
                ?? (server as ITimeProviderProvider)?.TimeProvider
                ?? TimeProvider.System;
            m_logger = server.Telemetry.CreateLogger<SamplingGroup>();
            m_backgroundWork = new BackgroundTaskScope(nameof(SamplingGroup), server.Telemetry);
            m_nodeManager = nodeManager ?? throw new ArgumentNullException(nameof(nodeManager));
            m_samplingRates = samplingRates ??
                throw new ArgumentNullException(nameof(samplingRates));
            m_session = context.Session;
            if (m_session == null)
            {
                m_effectiveIdentity =
                    savedOwnerIdentity
                    ?? throw new ArgumentNullException(
                        nameof(savedOwnerIdentity),
                        "Either a context with a Session or an owner identity need to be provided");
            }
            m_diagnosticsMask = context.DiagnosticsMask & DiagnosticsMasks.OperationAll;
            m_samplingInterval = AdjustSamplingInterval(samplingInterval);

            m_itemsToAdd = [];
            m_itemsToRemove = [];
            m_items = [];
        }

        /// <summary>
        /// Frees any unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Signal only: Dispose is synchronous. An in-flight sample
                // stops at its next await once the token trips.
                m_backgroundWork.Dispose();

                // cancel outside the lock: cancellation runs the callbacks registered
                // on the token, which may belong to node-manager read hooks.
                CancellationTokenSource? samplingCts;
                Task? samplingTask;
                lock (m_lock)
                {
                    (samplingCts, samplingTask) = DetachSamplingLoop();
                    UnsubscribePermissionChanges();
                }

                CancelSamplingLoop(samplingCts, samplingTask);
            }
        }

        /// <summary>
        /// Starts the sampling loop which periodically reads the items in the group.
        /// </summary>
        /// <remarks>
        /// The loop waits asynchronously between samples, so an idle group does not
        /// hold a thread.
        /// </remarks>
        public void Startup()
        {
            lock (m_lock)
            {
                StopSamplingLoop();

                var samplingCts = new CancellationTokenSource();
                m_samplingCts = samplingCts;
                CancellationToken token = samplingCts.Token;
                double samplingInterval = m_samplingInterval;
                SubscribePermissionChanges();
                m_samplingTask = Task.Run(
                    () => SampleMonitoredItemsAsync(samplingInterval, samplingCts, token).AsTask(),
                    CancellationToken.None);
            }
        }

        /// <summary>
        /// Stops the sampling loop.
        /// </summary>
        public void Shutdown()
        {
            lock (m_lock)
            {
                StopSamplingLoop();
                UnsubscribePermissionChanges();
                m_items.Clear();
                m_itemSnapshot = null;
            }
        }

        /// <summary>
        /// Invalidates the cached role permission verdicts of the sampled items when the
        /// default permissions of a namespace change. Must be called while holding the lock.
        /// </summary>
        private void SubscribePermissionChanges()
        {
            if (m_configurationNodeManager != null)
            {
                return;
            }

            IConfigurationNodeManager? configurationNodeManager = m_server.ConfigurationNodeManager;
            if (configurationNodeManager != null)
            {
                configurationNodeManager.DefaultPermissionsChanged += OnDefaultPermissionsChanged;
                m_configurationNodeManager = configurationNodeManager;
            }
        }

        /// <summary>
        /// Stops observing namespace default permission changes. Must be called while holding the lock.
        /// </summary>
        private void UnsubscribePermissionChanges()
        {
            m_configurationNodeManager?.DefaultPermissionsChanged -= OnDefaultPermissionsChanged;
            m_configurationNodeManager = null;
        }

        private void OnDefaultPermissionsChanged(object? sender, EventArgs e)
        {
            Interlocked.Increment(ref m_permissionEpoch);
        }

        /// <summary>
        /// Changes whenever a cached role permission verdict of an item may be stale: the role
        /// permissions of a node or the default permissions of a namespace changed.
        /// </summary>
        private long PermissionEpoch => Interlocked.Read(ref m_permissionEpoch) + NodeState.RolePermissionsVersion;

        /// <summary>
        /// Stops the running sampling loop. Must be called while holding the lock.
        /// </summary>
        /// <remarks>
        /// The loop is detached at once, so it stops sampling at its next cycle, but its
        /// token is cancelled on the thread pool: cancellation runs the callbacks that
        /// node-manager read hooks registered on the token, which must not run under the
        /// group lock or the lock of a <see cref="SamplingGroupManager"/> applying changes.
        /// </remarks>
        private void StopSamplingLoop()
        {
            (CancellationTokenSource? samplingCts, Task? samplingTask) = DetachSamplingLoop();
            if (samplingCts != null)
            {
                _ = Task.Run(() => CancelSamplingLoop(samplingCts, samplingTask), CancellationToken.None);
            }
        }

        /// <summary>
        /// Detaches the running sampling loop from the group. Must be called while
        /// holding the lock.
        /// </summary>
        private (CancellationTokenSource?, Task?) DetachSamplingLoop()
        {
            CancellationTokenSource? samplingCts = m_samplingCts;
            Task? samplingTask = m_samplingTask;
            m_samplingCts = null;
            m_samplingTask = null;
            return (samplingCts, samplingTask);
        }

        /// <summary>
        /// Cancels a detached sampling loop and releases its token source once the
        /// loop has observed the cancellation.
        /// </summary>
        private void CancelSamplingLoop(CancellationTokenSource? samplingCts, Task? samplingTask)
        {
            if (samplingCts == null)
            {
                return;
            }

            try
            {
                samplingCts.Cancel();
            }
            catch (AggregateException ex)
            {
                // a callback registered on the token by a read hook threw. It must not
                // abort applying the changes of this or other groups, nor leak the source.
                m_logger.SamplingLoopCancellationFailed(ex);
            }

            if (samplingTask == null || samplingTask.IsCompleted)
            {
                samplingCts.Dispose();
                return;
            }

            _ = samplingTask.ContinueWith(
                (_, state) => ((CancellationTokenSource)state!).Dispose(),
                samplingCts,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        /// <summary>
        /// Checks if the monitored item can be handled by the group.
        /// </summary>
        /// <returns>
        /// True if the item was added to the group.
        /// </returns>
        /// <remarks>
        /// The ApplyChanges() method must be called to actually start sampling the item.
        /// </remarks>
        public bool StartMonitoring(
            OperationContext context,
            ISampledDataChangeMonitoredItem monitoredItem,
            IUserIdentity? savedOwnerIdentity = null)
        {
            return StartMonitoring(context, monitoredItem, savedOwnerIdentity, initialValueQueued: false);
        }

        /// <summary>
        /// Checks if the monitored item can be handled by the group.
        /// </summary>
        /// <param name="context">The operation context.</param>
        /// <param name="monitoredItem">The monitored item.</param>
        /// <param name="savedOwnerIdentity">Owner identity for sessionless groups.</param>
        /// <param name="initialValueQueued">
        /// True when the caller already queued the initial value of the item, so the
        /// immediate sample taken by <see cref="ApplyChanges"/> is skipped.
        /// </param>
        internal bool StartMonitoring(
            OperationContext context,
            ISampledDataChangeMonitoredItem monitoredItem,
            IUserIdentity? savedOwnerIdentity,
            bool initialValueQueued)
        {
            lock (m_lock)
            {
                if (MeetsGroupCriteria(context, monitoredItem, savedOwnerIdentity))
                {
                    // an item that is still sampled by this group but was marked for
                    // removal by an earlier modification simply stays in the group.
                    if (m_itemsToRemove.Remove(monitoredItem) &&
                        m_items.ContainsKey(monitoredItem.Id))
                    {
                        monitoredItem.SetSamplingInterval(m_samplingInterval);
                        return true;
                    }

                    if (!m_itemsToAdd.Contains(monitoredItem))
                    {
                        m_itemsToAdd.Add(monitoredItem);
                    }

                    if (initialValueQueued)
                    {
                        m_itemsWithInitialValue.Add(monitoredItem.Id);
                    }

                    monitoredItem.SetSamplingInterval(m_samplingInterval);
                    return true;
                }

                return false;
            }
        }

        /// <summary>
        /// Checks if the monitored item can still be handled by the group.
        /// </summary>
        /// <returns>
        /// False if the item has be marked for removal from the group.
        /// </returns>
        /// <remarks>
        /// The ApplyChanges() method must be called to actually stop sampling the item.
        /// </remarks>
        public bool ModifyMonitoring(
            OperationContext context,
            ISampledDataChangeMonitoredItem monitoredItem)
        {
            lock (m_lock)
            {
                // an item added by StartMonitoring but not yet applied is still owned by
                // this group; it must leave the pending additions when it moves to another
                // group, otherwise both groups end up sampling it.
                if (m_itemsToAdd.Contains(monitoredItem))
                {
                    if (MeetsGroupCriteria(context, monitoredItem))
                    {
                        monitoredItem.SetSamplingInterval(m_samplingInterval);
                        return true;
                    }

                    m_itemsToAdd.Remove(monitoredItem);
                    m_itemsWithInitialValue.Remove(monitoredItem.Id);
                    return false;
                }

                if (m_items.ContainsKey(monitoredItem.Id))
                {
                    if (MeetsGroupCriteria(context, monitoredItem))
                    {
                        m_itemsToRemove.Remove(monitoredItem);
                        monitoredItem.SetSamplingInterval(m_samplingInterval);
                        return true;
                    }

                    if (!m_itemsToRemove.Contains(monitoredItem))
                    {
                        m_itemsToRemove.Add(monitoredItem);
                    }
                }

                return false;
            }
        }

        /// <summary>
        /// Stops monitoring the item.
        /// </summary>
        /// <returns>
        /// Returns true if the items was marked for removal from the group.
        /// </returns>
        public bool StopMonitoring(ISampledDataChangeMonitoredItem monitoredItem)
        {
            lock (m_lock)
            {
                if (m_itemsToAdd.Remove(monitoredItem))
                {
                    m_itemsWithInitialValue.Remove(monitoredItem.Id);
                    return true;
                }

                if (m_items.ContainsKey(monitoredItem.Id))
                {
                    m_itemsToRemove.Add(monitoredItem);
                    return true;
                }

                return false;
            }
        }

        /// <summary>
        /// Updates the group by apply any pending changes.
        /// </summary>
        /// <returns>
        /// Returns true if the group has no more items and can be dropped.
        /// </returns>
        public bool ApplyChanges()
        {
            lock (m_lock)
            {
                // add items.
                var itemsToSample = new List<ISampledDataChangeMonitoredItem>();

                for (int ii = 0; ii < m_itemsToAdd.Count; ii++)
                {
                    ISampledDataChangeMonitoredItem monitoredItem = m_itemsToAdd[ii];

                    // an item whose initial value the caller already queued is not
                    // sampled again right away (Part 4 5.13.2.1: queued once).
                    if (m_items.TryAdd(monitoredItem.Id, monitoredItem) &&
                        monitoredItem.MonitoringMode != MonitoringMode.Disabled &&
                        !m_itemsWithInitialValue.Contains(monitoredItem.Id))
                    {
                        itemsToSample.Add(monitoredItem);
                    }
                }

                if (m_itemsToAdd.Count > 0 || m_itemsToRemove.Count > 0)
                {
                    m_itemSnapshot = null;
                }

                m_itemsToAdd.Clear();
                m_itemsWithInitialValue.Clear();

                // collect first sample.
                if (itemsToSample.Count > 0)
                {
                    m_backgroundWork.Run(
                        nameof(DoSampleAsync),
                        async ct => await DoSampleAsync(itemsToSample, loopCts: null, ct)
                            .ConfigureAwait(false));
                }

                // remove items.
                for (int ii = 0; ii < m_itemsToRemove.Count; ii++)
                {
                    m_items.Remove(m_itemsToRemove[ii].Id);
                }

                m_itemsToRemove.Clear();

                // start the group if it is not running.
                if (m_samplingTask == null && m_items.Count > 0)
                {
                    Startup();
                }
                // stop the group if it is running.
                else if (m_items.Count == 0)
                {
                    Shutdown();
                }

                // can be shutdown if no items left.
                return m_items.Count == 0;
            }
        }

        /// <summary>
        /// Checks if the item meets the group's criteria.
        /// </summary>
        private bool MeetsGroupCriteria(
            OperationContext context,
            ISampledDataChangeMonitoredItem monitoredItem,
            IUserIdentity? savedOwnerIdentity = null)
        {
            // can only sample variables.
            if ((monitoredItem.MonitoredItemType & MonitoredItemTypeMask.DataChange) == 0)
            {
                return false;
            }

            // can't sample disabled items.
            if (monitoredItem.MonitoringMode == MonitoringMode.Disabled)
            {
                return false;
            }

            // check sampling interval.
            if (AdjustSamplingInterval(monitoredItem.SamplingInterval) != m_samplingInterval)
            {
                return false;
            }

            if (m_session == null)
            {
                // fallback to compare user Identity if session is not set.
                if (!m_effectiveIdentity!.Equals(savedOwnerIdentity))
                {
                    return false;
                }
            }
            // compare session
            else if (context?.SessionId != m_session.Id)
            {
                return false;
            }

            // check the diagnostics marks.
            return context != null &&
                m_diagnosticsMask == (context.DiagnosticsMask & DiagnosticsMasks.OperationAll);
        }

        /// <summary>
        /// Ensures the requested sampling interval lines up with one of the supported sampling rates.
        /// </summary>
        private double AdjustSamplingInterval(double samplingInterval)
        {
            return AdjustSamplingInterval(m_samplingRates, samplingInterval);
        }

        /// <summary>
        /// Lines the requested sampling interval up with one of the supported sampling rates,
        /// which is the sampling interval an item gets in the group.
        /// </summary>
        internal static double AdjustSamplingInterval(
            List<SamplingRateGroup> samplingRates,
            double samplingInterval)
        {
            samplingInterval = SubscriptionManager.CalculateRevisedSamplingInterval(
                samplingInterval, 0, MinimumSamplingIntervals.Continuous, 0);
            foreach (SamplingRateGroup samplingRate in samplingRates)
            {
                if (double.IsNaN(samplingRate.Start) || double.IsInfinity(samplingRate.Start) || samplingRate.Start < 0)
                {
                    continue;
                }
                // groups are ordered by start rate.
                if (samplingInterval <= samplingRate.Start)
                {
                    return Math.Min(samplingRate.Start, int.MaxValue);
                }

                if (!(samplingRate.Increment > 0) ||
                    double.IsInfinity(samplingRate.Increment) ||
                    samplingRate.Count < 0)
                {
                    continue;
                }

                // a count of 0 means the group has no limit: every interval beyond the
                // start is rounded up to a multiple of the increment, so client chosen
                // intervals cannot create an unbounded number of groups.
                double maxSamplingRate = samplingRate.Count == 0
                    ? int.MaxValue
                    : samplingRate.Start + (samplingRate.Increment * samplingRate.Count);
                if (samplingInterval > maxSamplingRate)
                {
                    continue;
                }

                // find sampling rate within rate group.
                if (samplingInterval == maxSamplingRate)
                {
                    return Math.Min(maxSamplingRate, int.MaxValue);
                }

                double steps = Math.Ceiling((samplingInterval - samplingRate.Start) / samplingRate.Increment);
                double revised = samplingRate.Start + (steps * samplingRate.Increment);
                return Math.Min(Math.Max(samplingInterval, revised), int.MaxValue);
            }

            return samplingInterval;
        }

        /// <summary>
        /// Periodically checks if the sessions have timed out.
        /// </summary>
        private async ValueTask SampleMonitoredItemsAsync(
            double samplingInterval,
            CancellationTokenSource samplingCts,
            CancellationToken cancellationToken)
        {
            try
            {
                m_logger.ServerNameThreadStarted(Thread.CurrentThread.Name);

                int sleepCycle = Convert.ToInt32(samplingInterval, CultureInfo.InvariantCulture);
                var loop = new SamplingLoop(this, samplingCts);

                long frequency = m_timeProvider.TimestampFrequency;
                var schedule = SamplingSchedule.Create(samplingInterval, frequency, m_timeProvider.GetTimestamp());

                while (m_server.IsRunning && !cancellationToken.IsCancellationRequested)
                {
                    long startTimestamp = m_timeProvider.GetTimestamp();

                    // wait till next sample without holding a thread.
                    try
                    {
                        long remaining = schedule.GetWait(startTimestamp);
                        if (remaining > 0)
                        {
                            await Task.Delay(
                                SamplingSchedule.ToTimeSpan(remaining, frequency),
                                cancellationToken).ConfigureAwait(false);
                        }
                        else
                        {
                            // catching up: let other work run between back-to-back samples.
                            await Task.Yield();
                            cancellationToken.ThrowIfCancellationRequested();
                        }
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    // get current items to sample; the snapshot is rebuilt only when items
                    // were added or removed.
                    ISampledDataChangeMonitoredItem[] items;

                    lock (m_lock)
                    {
                        // a detached loop stops even before its token is cancelled.
                        if (!ReferenceEquals(m_samplingCts, samplingCts))
                        {
                            break;
                        }

                        items = m_itemSnapshot ??= [.. m_items.Values];
                    }

                    // sample the values.
                    await loop.SampleAsync(items, cancellationToken).ConfigureAwait(false);

                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    if (schedule.Advance(m_timeProvider.GetTimestamp()))
                    {
                        // the samples take longer than the interval, or the process stalled
                        int delay = (int)m_timeProvider.GetElapsedTime(startTimestamp).TotalMilliseconds;
                        m_logger.WARNINGSamplingGroupCannotSampleFastEnoughTimeToSample(delay, sleepCycle);
                    }
                }

                m_logger.ServerNameThreadExitedNormally(Thread.CurrentThread.Name);
            }
            catch (Exception e)
            {
                m_logger.ServerSampleMonitoredItemsThreadExitedUnexpectedly(e);
            }
        }

        /// <summary>
        /// Samples the values of the items.
        /// </summary>
        /// <param name="items">The items to sample.</param>
        /// <param name="loopCts">
        /// The token source of the sampling loop that takes the sample, or null for a sample
        /// outside the loop; a loop sample stops as soon as that loop is detached from the group.
        /// </param>
        /// <param name="cancellationToken">The cancellation token.</param>
        private async ValueTask DoSampleAsync(
            List<ISampledDataChangeMonitoredItem> items,
            CancellationTokenSource? loopCts,
            CancellationToken cancellationToken = default)
        {
            try
            {
                // read values for all enabled items.
                if (items != null && items.Count > 0)
                {
                    // if session of the Sampling group is not set yet, adopt the session of the first monitored item.
                    m_session ??= items[0].Session;

                    // TransferSubscriptions moves items to another session without regrouping them,
                    // so every item is read and permission-checked as its current owner.
                    foreach (IGrouping<object?, ISampledDataChangeMonitoredItem> owned in items
                        .GroupBy(item => (object?)item.Session ?? item.EffectiveIdentity))
                    {
                        List<ISampledDataChangeMonitoredItem> ownedItems = [.. owned];
                        await SampleItemsAsync(ownedItems, loopCts, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // the group was stopped while sampling.
            }
            catch (Exception e)
            {
                m_logger.ServerUnexpectedErrorSamplingValues(e);
            }
        }

        /// <summary>
        /// Reads and queues the values of items owned by the session of the context.
        /// </summary>
        private async ValueTask SampleItemsAsync(
            List<ISampledDataChangeMonitoredItem> items,
            CancellationTokenSource? loopCts,
            CancellationToken cancellationToken)
        {
            using OperationContext context = MonitoredReadAuthorization.CreateContext(items[0], m_diagnosticsMask);
            var itemsToRead = new List<ReadValueId>(items.Count);
            var values = new List<DataValue>(items.Count);
            var errors = new List<ServiceResult>(items.Count);

            // allocate space for results.
            for (int ii = 0; ii < items.Count; ii++)
            {
                ReadValueId readValueId = items[ii].GetReadValueId();
                readValueId.Processed = false;
                itemsToRead.Add(readValueId);

                values.Add(default);
                errors.Add(null!);
            }

            // read values.
            await m_nodeManager.ReadAsync(context, 0, itemsToRead, values, errors, cancellationToken).ConfigureAwait(false);

            // update monitored items, unless the group stopped while reading: its items
            // may already be deleted.
            for (int ii = 0; ii < items.Count; ii++)
            {
                if (IsSamplingStopped(loopCts, cancellationToken))
                {
                    return;
                }

                ServiceResult permissionResult = await m_nodeManager
                    .ValidateRolePermissionsAsync(
                        context,
                        itemsToRead[ii].NodeId,
                        PermissionType.Read,
                        cancellationToken)
                    .ConfigureAwait(false);

                permissionResult = await ValidateSampleReadAccessAsync(
                    items[ii], context, permissionResult, cancellationToken).ConfigureAwait(false);

                // the group may have stopped while the permission check was suspended.
                if (IsSamplingStopped(loopCts, cancellationToken))
                {
                    return;
                }

                QueueSample(items[ii], context, values[ii], errors[ii], permissionResult);
            }
        }

        /// <summary>
        /// Rechecks each NodeState-backed sample's current subscriber policy and endpoint before queueing its value.
        /// </summary>
        private async ValueTask<ServiceResult> ValidateSampleReadAccessAsync(
            ISampledDataChangeMonitoredItem item,
            OperationContext readContext,
            ServiceResult permission,
            CancellationToken cancellationToken)
        {
            if (ServiceResult.IsBad(permission))
            {
                return permission;
            }
            NodeState? node = item.ManagerHandle switch
            {
                NodeHandle handle => handle.Node,
                NodeState state => state,
                _ => (m_nodeManager as AsyncCustomNodeManager)?.Find(item.NodeId)
            };
            if (node == null)
            {
                // Provider-specific non-NodeState handles retain their existing Read and role validation contracts.
                return permission;
            }
            try
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!ReferenceEquals(readContext.Session, item.Session) ||
                        !ReferenceEquals(readContext.UserIdentity,
                            item.Session?.EffectiveIdentity ?? item.EffectiveIdentity))
                    {
                        return StatusCodes.BadUserAccessDenied;
                    }
                    EndpointDescription? endpoint = MonitoredReadAuthorization.GetEndpoint(item.Session);
                    MessageSecurityMode? securityMode = endpoint?.SecurityMode;
                    string? transport = endpoint?.TransportProfileUri;
                    AccessRestrictionType? restrictions = node.AccessRestrictions;
                    long epoch = PermissionEpoch;
                    using OperationContext current = MonitoredReadAuthorization.CreateContext(item, m_diagnosticsMask);
                    var systemContext = new ServerSystemContext(m_server, current);
                    ServiceResult result = await MonitoredReadAuthorization.ValidateAsync(
                        m_nodeManager, node, systemContext, item.AttributeId, cancellationToken).ConfigureAwait(false);
                    EndpointDescription? currentEndpoint = MonitoredReadAuthorization.GetEndpoint(item.Session);
                    if (ReferenceEquals(readContext.Session, item.Session) &&
                        ReferenceEquals(readContext.UserIdentity,
                            item.Session?.EffectiveIdentity ?? item.EffectiveIdentity) &&
                        ReferenceEquals(endpoint, currentEndpoint) &&
                        securityMode == currentEndpoint?.SecurityMode &&
                        transport == currentEndpoint?.TransportProfileUri &&
                        restrictions == node.AccessRestrictions &&
                        epoch == PermissionEpoch)
                    {
                        return result;
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (ObjectDisposedException)
            {
                return StatusCodes.BadShutdown;
            }
            catch (ServiceResultException error)
            {
                return error.Result;
            }
            catch (Exception error) when (
                error is not OutOfMemoryException and not StackOverflowException and not AccessViolationException)
            {
                return ServiceResult.Create(
                    error, StatusCodes.BadUnexpectedError, "A sampled Value read authorization failed.");
            }
        }

        /// <summary>
        /// Queues a sampled value, or the error of a failed permission check, on an item.
        /// </summary>
        private void QueueSample(
            ISampledDataChangeMonitoredItem item,
            OperationContext context,
            DataValue value,
            ServiceResult error,
            ServiceResult permissionResult)
        {
            if (ServiceResult.IsBad(permissionResult))
            {
                value = DataValue.FromStatusCode(
                    permissionResult.StatusCode,
                    m_timeProvider.GetUtcNow().UtcDateTime);
                error = permissionResult;
            }
            else if (value.IsNull)
            {
                value = DataValue.FromStatusCode(
                    StatusCodes.BadInternalError,
                    m_timeProvider.GetUtcNow().UtcDateTime);
            }

            if (item is MonitoredItem concreteItem)
            {
                concreteItem.QueueValueIfCurrent(context, value, error);
            }
            else
            {
                item.QueueValue(value, error);
            }
        }

        /// <summary>
        /// True when the sample must not be queued: its token is cancelled, or it belongs to a
        /// sampling loop that was detached from the group and whose cancellation is still
        /// pending on the thread pool.
        /// </summary>
        private bool IsSamplingStopped(CancellationTokenSource? loopCts, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return true;
            }

            // the loop is detached under the lock; a stale read only delays the stop by one item.
            return loopCts != null && !ReferenceEquals(Volatile.Read(ref m_samplingCts), loopCts);
        }

        /// <summary>
        /// The state that one sampling loop keeps between its cycles: the read requests and
        /// results of its items, and the role permission verdicts of their last validation.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Each loop owns its state, so a detached loop that still finishes a cycle cannot
        /// interfere with its successor.
        /// </para>
        /// <para>
        /// The Read permission of an item is validated when the item joins the loop and again
        /// whenever its Session, effective identity or node handle changed, the RolePermissions or
        /// UserRolePermissions of a node changed, or the default permissions of a namespace changed.
        /// Independently of these signals, every item is revalidated about once per
        /// <see cref="kPermissionRevalidationPeriod"/>, spread over the cycles, which bounds
        /// the delay for permissions that a node manager computes without a change signal.
        /// </para>
        /// </remarks>
        private sealed class SamplingLoop
        {
            public SamplingLoop(SamplingGroup group, CancellationTokenSource loopCts)
            {
                m_group = group;
                m_loopCts = loopCts;
                // round down, so that no verdict is used for longer than the period.
                double cycles = Math.Floor(kPermissionRevalidationPeriod / Math.Max(group.m_samplingInterval, 1));
                m_revalidationStride = (int)Math.Min(Math.Max(cycles, 1), int.MaxValue);
            }

            /// <summary>
            /// Reads and queues the values of the enabled items.
            /// </summary>
            public async ValueTask SampleAsync(
                ISampledDataChangeMonitoredItem[] items,
                CancellationToken cancellationToken)
            {
                try
                {
                    await SampleItemsAsync(items, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // the group was stopped while sampling.
                }
                catch (Exception e)
                {
                    m_group.m_logger.ServerUnexpectedErrorSamplingValues(e);
                }
            }

            private async ValueTask SampleItemsAsync(
                ISampledDataChangeMonitoredItem[] items,
                CancellationToken cancellationToken)
            {
                if (!ReferenceEquals(items, m_items))
                {
                    Reset(items);
                }

                // prepare the reads; disabled items are marked processed so no node manager reads them.
                ISampledDataChangeMonitoredItem? first = null;
                object? owner = null;
                bool singleOwner = true;
                for (int ii = 0; ii < items.Length; ii++)
                {
                    ISampledDataChangeMonitoredItem item = items[ii];
                    ReadValueId readValueId = m_readValueIds[ii];
                    m_values[ii] = default;
                    m_errors[ii] = null!;
                    m_enabled[ii] = item.MonitoringMode != MonitoringMode.Disabled;
                    readValueId.Processed = !m_enabled[ii];
                    if (!m_enabled[ii])
                    {
                        continue;
                    }

                    readValueId.Handle = item.ManagerHandle;
                    object? itemOwner = (object?)item.Session ?? item.EffectiveIdentity;
                    if (first == null)
                    {
                        first = item;
                        owner = itemOwner;
                    }
                    else if (!Equals(owner, itemOwner))
                    {
                        singleOwner = false;
                    }
                }

                if (first == null)
                {
                    return;
                }

                if (!singleOwner)
                {
                    // TransferSubscriptions moved items to another owner without regrouping them:
                    // read and validate each owner's items with its own context.
                    var enabled = new List<ISampledDataChangeMonitoredItem>(items.Length);
                    for (int ii = 0; ii < items.Length; ii++)
                    {
                        if (m_enabled[ii])
                        {
                            enabled.Add(items[ii]);
                        }
                    }

                    await m_group.DoSampleAsync(enabled, m_loopCts, cancellationToken).ConfigureAwait(false);
                    return;
                }

                // if session of the Sampling group is not set yet, adopt the session of the first monitored item.
                m_group.m_session ??= first.Session;

                using OperationContext context = MonitoredReadAuthorization.CreateContext(
                    first, m_group.m_diagnosticsMask);

                await m_group.m_nodeManager.ReadAsync(
                    context, 0, m_readValueIds, m_values, m_errors, cancellationToken).ConfigureAwait(false);

                long epoch = m_group.PermissionEpoch;
                int slice = (int)(m_cycle++ % m_revalidationStride);

                // update monitored items, unless the group stopped while reading: its items
                // may already be deleted.
                for (int ii = 0; ii < items.Length; ii++)
                {
                    if (!m_enabled[ii])
                    {
                        continue;
                    }

                    if (m_group.IsSamplingStopped(m_loopCts, cancellationToken))
                    {
                        return;
                    }

                    ReadValueId readValueId = m_readValueIds[ii];
                    PermissionVerdict verdict = m_verdicts[ii];
                    ServiceResult? permissionResult = verdict.Result;
                    if (permissionResult == null ||
                        ii % m_revalidationStride == slice ||
                        verdict.Epoch != epoch ||
                        !ReferenceEquals(verdict.Session, context.Session) ||
                        !ReferenceEquals(verdict.Identity, context.UserIdentity) ||
                        !ReferenceEquals(verdict.Handle, readValueId.Handle))
                    {
                        permissionResult = await m_group.m_nodeManager
                            .ValidateRolePermissionsAsync(
                                context,
                                readValueId.NodeId,
                                PermissionType.Read,
                                cancellationToken)
                            .ConfigureAwait(false);

                        // the group may have stopped while the permission check was suspended.
                        if (m_group.IsSamplingStopped(m_loopCts, cancellationToken))
                        {
                            return;
                        }

                        m_verdicts[ii] = new PermissionVerdict(
                            permissionResult, epoch, context.Session, context.UserIdentity, readValueId.Handle);
                    }

                    permissionResult = await m_group.ValidateSampleReadAccessAsync(
                        items[ii], context, permissionResult, cancellationToken).ConfigureAwait(false);
                    if (m_group.IsSamplingStopped(m_loopCts, cancellationToken))
                    {
                        return;
                    }
                    m_group.QueueSample(items[ii], context, m_values[ii], m_errors[ii], permissionResult);
                }
            }

            /// <summary>
            /// Rebuilds the per-item state after items were added to or removed from the group.
            /// </summary>
            private void Reset(ISampledDataChangeMonitoredItem[] items)
            {
                m_items = items;
                m_readValueIds = new ReadValueId[items.Length];
                for (int ii = 0; ii < items.Length; ii++)
                {
                    m_readValueIds[ii] = items[ii].GetReadValueId();
                }

                m_values = new DataValue[items.Length];
                m_errors = new ServiceResult[items.Length];
                m_enabled = new bool[items.Length];
                m_verdicts = new PermissionVerdict[items.Length];
            }

            private readonly SamplingGroup m_group;
            private readonly CancellationTokenSource m_loopCts;
            private readonly int m_revalidationStride;
            private long m_cycle;
            private ISampledDataChangeMonitoredItem[]? m_items;
            private ReadValueId[] m_readValueIds = [];
            private DataValue[] m_values = [];
            private ServiceResult[] m_errors = [];
            private bool[] m_enabled = [];
            private PermissionVerdict[] m_verdicts = [];
        }

        /// <summary>
        /// The cached result of a role permission validation and the inputs it is valid for.
        /// </summary>
        private readonly record struct PermissionVerdict(
            ServiceResult? Result,
            long Epoch,
            ISession? Session,
            IUserIdentity? Identity,
            object? Handle);

        /// <summary>
        /// The longest time a cached role permission verdict of a sampled item is used
        /// without revalidation, in milliseconds.
        /// </summary>
        private const double kPermissionRevalidationPeriod = 1000;

        private readonly Lock m_lock = new();
        private readonly ILogger m_logger;
        private readonly BackgroundTaskScope m_backgroundWork;
        private readonly IServerInternal m_server;
        private readonly TimeProvider m_timeProvider;
        private readonly IAsyncNodeManager m_nodeManager;
        private ISession? m_session;
        private readonly IUserIdentity? m_effectiveIdentity;
        private readonly DiagnosticsMasks m_diagnosticsMask;
        private readonly double m_samplingInterval;
        private readonly List<ISampledDataChangeMonitoredItem> m_itemsToAdd;
        private readonly List<ISampledDataChangeMonitoredItem> m_itemsToRemove;
        private readonly HashSet<uint> m_itemsWithInitialValue = [];
        private readonly Dictionary<uint, ISampledDataChangeMonitoredItem> m_items;
        private readonly List<SamplingRateGroup> m_samplingRates;
        private ISampledDataChangeMonitoredItem[]? m_itemSnapshot;
        private IConfigurationNodeManager? m_configurationNodeManager;
        private long m_permissionEpoch;
        private Task? m_samplingTask;
        // Dispose detaches the source and CancelSamplingLoop disposes it once the loop completes.
        // TODO: Remove the pragma when CA2213 recognizes deferred ownership transfer.
#pragma warning disable CA2213
        private CancellationTokenSource? m_samplingCts;
#pragma warning restore CA2213
    }

    /// <summary>
    /// Source-generated log messages for SamplingGroup.
    /// </summary>
    internal static partial class SamplingGroupLog
    {
        /// <summary>
        /// Logs the start of the named sampling worker.
        /// </summary>
        [LoggerMessage(EventId = ServerEventIds.SamplingGroup + 0, Level = LogLevel.Trace,
            Message = "Server: {Name} Thread Started.")]
        public static partial void ServerNameThreadStarted(this ILogger logger, string? name);

        /// <summary>
        /// Logs a sampling pass that cannot keep up with the configured sampling interval.
        /// </summary>
        [LoggerMessage(EventId = ServerEventIds.SamplingGroup + 1, Level = LogLevel.Warning,
            Message = "SamplingGroup cannot sample fast enough. TimeToSample={Delay}ms, " +
                "SamplingInterval={SleepCycle}ms")]
        public static partial void WARNINGSamplingGroupCannotSampleFastEnoughTimeToSample(
            this ILogger logger,
            double delay,
            double sleepCycle);

        /// <summary>
        /// Logs normal completion of the named sampling worker.
        /// </summary>
        [LoggerMessage(EventId = ServerEventIds.SamplingGroup + 2, Level = LogLevel.Trace,
            Message = "Server: {Name} Thread Exited Normally.")]
        public static partial void ServerNameThreadExitedNormally(this ILogger logger, string? name);

        /// <summary>
        /// Logs an exception that unexpectedly terminated the monitored-item sampling worker.
        /// </summary>
        [LoggerMessage(EventId = ServerEventIds.SamplingGroup + 3, Level = LogLevel.Error,
            Message = "Server: SampleMonitoredItems Thread Exited Unexpectedly.")]
        public static partial void ServerSampleMonitoredItemsThreadExitedUnexpectedly(
            this ILogger logger,
            Exception ex);

        /// <summary>
        /// Logs an unexpected failure while sampling monitored values.
        /// </summary>
        [LoggerMessage(EventId = ServerEventIds.SamplingGroup + 4, Level = LogLevel.Error,
            Message = "Server: Unexpected error sampling values.")]
        public static partial void ServerUnexpectedErrorSamplingValues(this ILogger logger, Exception ex);

        /// <summary>
        /// Logs a cancellation callback that threw while the sampling loop was stopped.
        /// </summary>
        [LoggerMessage(EventId = ServerEventIds.SamplingGroup + 5, Level = LogLevel.Warning,
            Message = "Server: A cancellation callback threw while stopping the sampling loop.")]
        public static partial void SamplingLoopCancellationFailed(this ILogger logger, Exception ex);
    }
}
