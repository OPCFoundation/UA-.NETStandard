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

namespace Opc.Ua.Server.Fluent
{
    internal sealed class MonitoredSourceRegistry : IDisposable
    {
        public MonitoredSourceRegistry(
            FluentNodeManagerBase owner,
            ILogger logger)
        {
            m_owner = owner ?? throw new ArgumentNullException(nameof(owner));
            m_logger = logger;
            m_timeProvider =
                (owner.Server as ITimeProviderProvider)?.TimeProvider ??
                TimeProvider.System;
            m_managerCts = new CancellationTokenSource();
        }

        public MonitoredSourceRegistration Register(NodeState node)
        {
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }

            lock (m_registrationLock)
            {
                ThrowIfDisposed();
                if (!m_exact.TryGetValue(
                    node.NodeId,
                    out MonitoredSourceRegistration? registration))
                {
                    registration = new MonitoredSourceRegistration(
                        node.NodeId,
                        m_timeProvider,
                        m_logger,
                        m_managerCts.Token);
                    m_exact.Add(node.NodeId, registration);
                }
                return registration;
            }
        }

        public MonitoredSourceRegistration Register(
            VirtualNodeRegistration virtualNodes)
        {
            if (virtualNodes == null)
            {
                throw new ArgumentNullException(nameof(virtualNodes));
            }

            lock (m_registrationLock)
            {
                ThrowIfDisposed();
                if (!m_virtualTemplates.TryGetValue(
                    virtualNodes,
                    out MonitoredSourceRegistration? registration))
                {
                    registration = new MonitoredSourceRegistration(
                        NodeId.Null,
                        m_timeProvider,
                        m_logger,
                        m_managerCts.Token);
                    m_virtualTemplates.Add(virtualNodes, registration);
                }
                return registration;
            }
        }

        public ValueTask OnCreatedAsync(
            ISystemContext context,
            NodeState source,
            ISampledDataChangeMonitoredItem monitoredItem)
        {
            MonitoredSourceRegistration? registration = Find(
                source.NodeId,
                out _);
            return registration?.OnCreatedAsync(context, source, monitoredItem) ?? default;
        }

        public ValueTask OnModifiedAsync(
            ISystemContext context,
            NodeState source,
            ISampledDataChangeMonitoredItem monitoredItem)
        {
            MonitoredSourceRegistration? registration = Find(
                source.NodeId,
                out _);
            return registration?.OnModifiedAsync(context, source, monitoredItem) ?? default;
        }

        public async ValueTask OnDeletedAsync(
            ISystemContext context,
            NodeState source,
            ISampledDataChangeMonitoredItem monitoredItem)
        {
            MonitoredSourceRegistration? registration = Find(
                source.NodeId,
                out VirtualNodeRegistration? virtualNodes);
            if (registration == null)
            {
                return;
            }

            bool empty = await registration.OnDeletedAsync(
                context,
                source,
                monitoredItem).ConfigureAwait(false);
            if (empty && virtualNodes != null)
            {
                await RemoveVirtualInstanceAsync(
                    virtualNodes,
                    source.NodeId,
                    registration).ConfigureAwait(false);
            }
        }

        public ValueTask OnMonitoringModeChangedAsync(
            ISystemContext context,
            NodeState source,
            ISampledDataChangeMonitoredItem monitoredItem,
            MonitoringMode monitoringMode)
        {
            MonitoredSourceRegistration? registration = Find(
                source.NodeId,
                out _);
            return registration?.OnMonitoringModeChangedAsync(
                context,
                source,
                monitoredItem,
                monitoringMode) ?? default;
        }

        public void Dispose()
        {
            List<MonitoredSourceRegistration> registrations;
            lock (m_registrationLock)
            {
                if (m_disposed)
                {
                    return;
                }
                m_disposed = true;
                m_managerCts.Cancel();
                registrations = [.. m_exact.Values, .. m_virtualTemplates.Values];
                foreach (Dictionary<NodeId, MonitoredSourceRegistration> instances
                    in m_virtualInstances.Values)
                {
                    registrations.AddRange(instances.Values);
                }
                m_exact.Clear();
                m_virtualTemplates.Clear();
                m_virtualInstances.Clear();
            }

            foreach (MonitoredSourceRegistration registration in registrations)
            {
                registration.Dispose();
            }
            m_managerCts.Dispose();
        }

        private MonitoredSourceRegistration? Find(
            NodeId nodeId,
            out VirtualNodeRegistration? virtualNodes)
        {
            lock (m_registrationLock)
            {
                virtualNodes = null;
                if (m_disposed)
                {
                    return null;
                }
                if (m_exact.TryGetValue(
                    nodeId,
                    out MonitoredSourceRegistration? exact))
                {
                    return exact;
                }

                virtualNodes =
                    m_owner.FindVirtualNodeRegistration(nodeId);
                if (virtualNodes == null ||
                    !m_virtualTemplates.TryGetValue(
                        virtualNodes,
                        out MonitoredSourceRegistration? template))
                {
                    return null;
                }

                if (!m_virtualInstances.TryGetValue(
                    virtualNodes,
                    out Dictionary<NodeId, MonitoredSourceRegistration>? instances))
                {
                    instances = [];
                    m_virtualInstances.Add(virtualNodes, instances);
                }
                if (!instances.TryGetValue(
                    nodeId,
                    out MonitoredSourceRegistration? registration))
                {
                    registration = template.CreateForNode(nodeId);
                    instances.Add(nodeId, registration);
                }
                return registration;
            }
        }

        private async ValueTask RemoveVirtualInstanceAsync(
            VirtualNodeRegistration virtualNodes,
            NodeId nodeId,
            MonitoredSourceRegistration registration)
        {
            bool removed = false;
            lock (m_registrationLock)
            {
                if (m_virtualInstances.TryGetValue(
                        virtualNodes,
                        out Dictionary<NodeId, MonitoredSourceRegistration>? instances) &&
                    instances.TryGetValue(
                        nodeId,
                        out MonitoredSourceRegistration? current) &&
                    ReferenceEquals(current, registration))
                {
                    instances.Remove(nodeId);
                    if (instances.Count == 0)
                    {
                        m_virtualInstances.Remove(virtualNodes);
                    }
                    removed = true;
                }
            }

            if (removed)
            {
                await registration.DisposeAsync().ConfigureAwait(false);
            }
        }

        private void ThrowIfDisposed()
        {
            if (m_disposed)
            {
                throw new ObjectDisposedException(GetType().FullName);
            }
        }

        private readonly FluentNodeManagerBase m_owner;
        private readonly ILogger m_logger;
        private readonly TimeProvider m_timeProvider;
        private readonly CancellationTokenSource m_managerCts;
        private readonly Lock m_registrationLock = new();
        private readonly Dictionary<NodeId, MonitoredSourceRegistration> m_exact = [];
        private readonly Dictionary<VirtualNodeRegistration, MonitoredSourceRegistration>
            m_virtualTemplates = [];
        private readonly Dictionary<
            VirtualNodeRegistration,
            Dictionary<NodeId, MonitoredSourceRegistration>> m_virtualInstances = [];
        private bool m_disposed;
    }

    internal sealed class MonitoredSourceRegistration :
        IDisposable,
        IAsyncDisposable
    {
        public MonitoredSourceRegistration(
            NodeId nodeId,
            TimeProvider timeProvider,
            ILogger logger,
            CancellationToken managerToken)
        {
            m_nodeId = nodeId;
            m_timeProvider = timeProvider;
            m_logger = logger;
            m_managerToken = managerToken;
            m_updateLock = new SemaphoreSlim(1, 1);
        }

        public void SetFirstSubscriber(MonitoredSourceLifecycleHandler handler)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }
            if (m_firstSubscriber != null)
            {
                throw CreateDuplicate("OnFirstSubscriber");
            }
            m_firstSubscriber = handler;
        }

        public void SetLastSubscriber(MonitoredSourceLifecycleHandler handler)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }
            if (m_lastSubscriber != null)
            {
                throw CreateDuplicate("OnLastSubscriber");
            }
            m_lastSubscriber = handler;
        }

        public void SetPoller(IMonitoredValuePoller poller, TimeSpan minimumPeriod)
        {
            if (poller == null)
            {
                throw new ArgumentNullException(nameof(poller));
            }
            if (minimumPeriod <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(minimumPeriod),
                    minimumPeriod,
                    "The minimum polling period must be positive.");
            }
            if (m_poller != null)
            {
                throw CreateDuplicate("PollWhileMonitored");
            }
            m_poller = poller;
            m_minimumPeriod = minimumPeriod;
        }

        public MonitoredSourceRegistration CreateForNode(NodeId nodeId)
        {
            var registration = new MonitoredSourceRegistration(
                nodeId,
                m_timeProvider,
                m_logger,
                m_managerToken)
            {
                m_firstSubscriber = m_firstSubscriber,
                m_lastSubscriber = m_lastSubscriber,
                m_minimumPeriod = m_minimumPeriod,
                m_poller = m_poller?.Clone()
            };
            return registration;
        }

        public async ValueTask OnCreatedAsync(
            ISystemContext context,
            NodeState source,
            ISampledDataChangeMonitoredItem monitoredItem)
        {
            _ = await UpdateAsync(
                context,
                source,
                monitoredItem,
                monitoredItem.MonitoringMode,
                remove: false).ConfigureAwait(false);
        }

        public async ValueTask OnModifiedAsync(
            ISystemContext context,
            NodeState source,
            ISampledDataChangeMonitoredItem monitoredItem)
        {
            _ = await UpdateAsync(
                context,
                source,
                monitoredItem,
                monitoredItem.MonitoringMode,
                remove: false).ConfigureAwait(false);
        }

        public ValueTask<bool> OnDeletedAsync(
            ISystemContext context,
            NodeState source,
            ISampledDataChangeMonitoredItem monitoredItem)
        {
            return UpdateAsync(
                context,
                source,
                monitoredItem,
                monitoredItem.MonitoringMode,
                remove: true);
        }

        public async ValueTask OnMonitoringModeChangedAsync(
            ISystemContext context,
            NodeState source,
            ISampledDataChangeMonitoredItem monitoredItem,
            MonitoringMode monitoringMode)
        {
            _ = await UpdateAsync(
                context,
                source,
                monitoredItem,
                monitoringMode,
                remove: false).ConfigureAwait(false);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref m_disposeStarted, 1) != 0)
            {
                return;
            }

            CancellationTokenSource? workerCts = m_workerCts;
            Task? worker = m_worker;
            workerCts?.Cancel();
            if (workerCts == null)
            {
                return;
            }

            if (worker == null || worker.IsCompleted)
            {
                workerCts.Dispose();
                return;
            }

            _ = worker.ContinueWith(
                _ => workerCts.Dispose(),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref m_disposeStarted, 1) != 0)
            {
                return;
            }

            CancellationTokenSource? workerCts;
            Task? worker;
            await m_updateLock.WaitAsync().ConfigureAwait(false);
            try
            {
                workerCts = m_workerCts;
                worker = m_worker;
                m_workerCts = null;
                m_worker = null;
                m_items.Clear();
            }
            finally
            {
                m_updateLock.Release();
            }

            workerCts?.Cancel();
            try
            {
                if (worker != null)
                {
                    await worker.ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                workerCts?.Dispose();
                m_updateLock.Dispose();
            }
        }

        private async ValueTask<bool> UpdateAsync(
            ISystemContext context,
            NodeState source,
            ISampledDataChangeMonitoredItem monitoredItem,
            MonitoringMode monitoringMode,
            bool remove)
        {
            ReconcileAction action;
            TimeSpan effectivePeriod;
            bool isEmpty;
            try
            {
                await m_updateLock.WaitAsync().ConfigureAwait(false);
                try
                {
                    if (Volatile.Read(ref m_disposeStarted) != 0)
                    {
                        return false;
                    }

                    bool wasActive = HasActiveItems();
                    TimeSpan previousPeriod = GetEffectivePeriod();
                    if (remove)
                    {
                        m_items.Remove(monitoredItem.Id);
                    }
                    else
                    {
                        m_items[monitoredItem.Id] = new TrackedItem(
                            monitoringMode,
                            monitoredItem.SamplingInterval);
                    }

                    bool isActive = HasActiveItems();
                    effectivePeriod = GetEffectivePeriod();
                    m_desiredSource = source;
                    if (!wasActive && isActive)
                    {
                        action = ReconcileAction.Activate;
                    }
                    else if (wasActive && !isActive)
                    {
                        action = ReconcileAction.Deactivate;
                    }
                    else if (isActive &&
                        m_poller != null &&
                        effectivePeriod != previousPeriod)
                    {
                        action = ReconcileAction.Restart;
                    }
                    else
                    {
                        action = ReconcileAction.None;
                    }

                    isEmpty = m_items.Count == 0;
                }
                finally
                {
                    m_updateLock.Release();
                }

                switch (action)
                {
                    case ReconcileAction.Activate:
                        await InvokeLifecycleAsync(
                            m_firstSubscriber,
                            context,
                            source,
                            "OnFirstSubscriber").ConfigureAwait(false);
                        await RestartWorkerAsync(
                            context,
                            source,
                            effectivePeriod).ConfigureAwait(false);
                        break;
                    case ReconcileAction.Deactivate:
                        _ = await StopWorkerAsync(
                            requireInactive: true,
                            source,
                            effectivePeriod).ConfigureAwait(false);
                        await InvokeLifecycleAsync(
                            m_lastSubscriber,
                            context,
                            source,
                            "OnLastSubscriber").ConfigureAwait(false);
                        break;
                    case ReconcileAction.Restart:
                        await RestartWorkerAsync(
                            context,
                            source,
                            effectivePeriod).ConfigureAwait(false);
                        break;
                }

                return isEmpty;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                m_logger.MonitoredSourceReconcileFailed(
                    exception,
                    FormatNodeId(source.NodeId));
                return false;
            }
        }

        private async ValueTask RestartWorkerAsync(
            ISystemContext context,
            NodeState source,
            TimeSpan period)
        {
            if (m_poller == null)
            {
                return;
            }

            bool isCurrent = await StopWorkerAsync(
                requireInactive: false,
                source,
                period).ConfigureAwait(false);
            if (!isCurrent)
            {
                return;
            }

            await SampleOnceAsync(
                context,
                source,
                m_managerToken).ConfigureAwait(false);

            CancellationTokenSource? workerCts = null;
            try
            {
                workerCts = CancellationTokenSource.CreateLinkedTokenSource(
                    m_managerToken);
                var startSignal = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                var workerStarted = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                Task worker = RunWorkerAsync(
                    context,
                    source,
                    period,
                    startSignal.Task,
                    workerStarted,
                    workerCts.Token);

                bool startWorker = false;
                await m_updateLock.WaitAsync().ConfigureAwait(false);
                try
                {
                    if (Volatile.Read(ref m_disposeStarted) == 0 &&
                        HasActiveItems() &&
                        GetEffectivePeriod() == period &&
                        ReferenceEquals(m_desiredSource, source) &&
                        m_workerCts == null)
                    {
                        m_workerCts = workerCts;
                        m_worker = worker;
                        workerCts = null; // ownership transferred
                        startWorker = true;
                    }
                }
                finally
                {
                    m_updateLock.Release();
                    startSignal.TrySetResult(true);
                }

                if (startWorker)
                {
                    await workerStarted.Task.ConfigureAwait(false);
                    return;
                }

                workerCts!.Cancel();
                await worker.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                workerCts?.Dispose();
            }
        }

        private async ValueTask<bool> StopWorkerAsync(
            bool requireInactive,
            NodeState source,
            TimeSpan period)
        {
            CancellationTokenSource? workerCts;
            Task? worker;
            await m_updateLock.WaitAsync().ConfigureAwait(false);
            try
            {
                bool stateMatches = requireInactive
                    ? !HasActiveItems()
                    : HasActiveItems() &&
                        GetEffectivePeriod() == period &&
                        ReferenceEquals(m_desiredSource, source);
                if (Volatile.Read(ref m_disposeStarted) != 0 || !stateMatches)
                {
                    return false;
                }

                workerCts = m_workerCts;
                worker = m_worker;
                m_workerCts = null;
                m_worker = null;
            }
            finally
            {
                m_updateLock.Release();
            }

            workerCts?.Cancel();
            try
            {
                if (worker != null)
                {
                    await worker.ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                workerCts?.Dispose();
            }
            return true;
        }

        private async Task RunWorkerAsync(
            ISystemContext context,
            NodeState source,
            TimeSpan period,
            Task startSignal,
            TaskCompletionSource<bool> workerStarted,
            CancellationToken cancellationToken)
        {
            await startSignal.ConfigureAwait(false);
            bool signalStartup = true;
            while (true)
            {
                try
                {
                    Task delay = m_timeProvider.Delay(
                        period,
                        cancellationToken);
                    if (signalStartup)
                    {
                        signalStartup = false;
                        workerStarted.TrySetResult(true);
                    }
                    await delay.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                await SampleOnceAsync(
                    context,
                    source,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        private async ValueTask SampleOnceAsync(
            ISystemContext context,
            NodeState source,
            CancellationToken cancellationToken)
        {
            try
            {
                await m_poller!.SampleAsync(
                    context,
                    source,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (
                cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                m_logger.MonitoredSourceSampleFailed(
                    exception,
                    FormatNodeId(source.NodeId));
            }
        }

        private async ValueTask InvokeLifecycleAsync(
            MonitoredSourceLifecycleHandler? handler,
            ISystemContext context,
            NodeState source,
            string callback)
        {
            if (handler == null)
            {
                return;
            }

            try
            {
                await handler(context, source, m_managerToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (m_managerToken.IsCancellationRequested)
            {
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                m_logger.MonitoredSourceLifecycleFailed(
                    exception,
                    callback,
                    FormatNodeId(source.NodeId));
            }
        }

        private bool HasActiveItems()
        {
            foreach (TrackedItem item in m_items.Values)
            {
                if (item.Mode != MonitoringMode.Disabled)
                {
                    return true;
                }
            }
            return false;
        }

        private TimeSpan GetEffectivePeriod()
        {
            double fastest = double.PositiveInfinity;
            foreach (TrackedItem item in m_items.Values)
            {
                if (item.Mode == MonitoringMode.Disabled)
                {
                    continue;
                }
                if (!double.IsNaN(item.SamplingInterval) &&
                    !double.IsInfinity(item.SamplingInterval) &&
                    item.SamplingInterval >= 0 &&
                    item.SamplingInterval < fastest)
                {
                    fastest = item.SamplingInterval;
                }
            }

            if (double.IsPositiveInfinity(fastest))
            {
                return m_minimumPeriod;
            }

            TimeSpan requested = TimeSpan.FromMilliseconds(fastest);
            return requested > m_minimumPeriod ? requested : m_minimumPeriod;
        }

        private ServiceResultException CreateDuplicate(string feature)
        {
            return ServiceResultException.Create(
                StatusCodes.BadConfigurationError,
                "Node '{0}' already has a {1} registration.",
                FormatNodeId(m_nodeId),
                feature);
        }

        private static string FormatNodeId(NodeId nodeId)
        {
            return nodeId.IsNull ? "(virtual family)" : nodeId.ToString();
        }

        private readonly record struct TrackedItem(
            MonitoringMode Mode,
            double SamplingInterval);

        private readonly NodeId m_nodeId;
        private readonly TimeProvider m_timeProvider;
        private readonly ILogger m_logger;
        private readonly CancellationToken m_managerToken;
        private readonly SemaphoreSlim m_updateLock;
        private readonly Dictionary<uint, TrackedItem> m_items = [];
        private MonitoredSourceLifecycleHandler? m_firstSubscriber;
        private MonitoredSourceLifecycleHandler? m_lastSubscriber;
        private IMonitoredValuePoller? m_poller;
        private TimeSpan m_minimumPeriod;
        private NodeState? m_desiredSource;
        private CancellationTokenSource? m_workerCts;
        private Task? m_worker;
        private int m_disposeStarted;

        private enum ReconcileAction
        {
            None,
            Activate,
            Deactivate,
            Restart
        }
    }

    internal interface IMonitoredValuePoller
    {
        IMonitoredValuePoller Clone();

        ValueTask SampleAsync(
            ISystemContext context,
            NodeState source,
            CancellationToken cancellationToken);
    }

    internal sealed class MonitoredValuePoller<TValue> : IMonitoredValuePoller
    {
        public MonitoredValuePoller(
            Func<ISystemContext, NodeState, CancellationToken, ValueTask<TValue>> sample)
        {
            m_sample = sample ?? throw new ArgumentNullException(nameof(sample));
        }

        public async ValueTask SampleAsync(
            ISystemContext context,
            NodeState source,
            CancellationToken cancellationToken)
        {
            if (source is not BaseVariableState variable)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadTypeMismatch,
                    "PollWhileMonitored source '{0}' is not a variable.",
                    source.NodeId);
            }

            TValue current = await m_sample(
                context,
                source,
                cancellationToken).ConfigureAwait(false);
            if (!m_hasValue ||
                !EqualityComparer<TValue>.Default.Equals(current, m_last))
            {
                m_hasValue = true;
                m_last = current;
                if (m_updater == null ||
                    !ReferenceEquals(m_variable, variable) ||
                    !ReferenceEquals(m_context, context))
                {
                    m_variable = variable;
                    m_context = context;
                    m_updater = new ValueUpdater<TValue>(variable, context);
                }
                m_updater.SetValue(current);
            }
        }

        public IMonitoredValuePoller Clone()
        {
            return new MonitoredValuePoller<TValue>(m_sample);
        }

        private readonly Func<
            ISystemContext,
            NodeState,
            CancellationToken,
            ValueTask<TValue>> m_sample;
        private BaseVariableState? m_variable;
        private ISystemContext? m_context;
        private ValueUpdater<TValue>? m_updater;
        private TValue? m_last;
        private bool m_hasValue;
    }

    internal static partial class MonitoredSourceRegistryLog
    {
        [LoggerMessage(
            EventId = ServerEventIds.MonitoredSourceRegistry + 0,
            Level = LogLevel.Error,
            Message = "Monitored source reconciliation failed for node {NodeId}.")]
        public static partial void MonitoredSourceReconcileFailed(
            this ILogger logger,
            Exception exception,
            string nodeId);

        [LoggerMessage(
            EventId = ServerEventIds.MonitoredSourceRegistry + 1,
            Level = LogLevel.Error,
            Message = "Monitored source sample failed for node {NodeId}.")]
        public static partial void MonitoredSourceSampleFailed(
            this ILogger logger,
            Exception exception,
            string nodeId);

        [LoggerMessage(
            EventId = ServerEventIds.MonitoredSourceRegistry + 2,
            Level = LogLevel.Error,
            Message = "Monitored source callback {Callback} failed for node {NodeId}.")]
        public static partial void MonitoredSourceLifecycleFailed(
            this ILogger logger,
            Exception exception,
            string callback,
            string nodeId);
    }
}
