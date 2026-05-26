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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Server.Fluent
{
    /// <summary>
    /// Coordinates the lifecycle of <c>Publish</c>-registered event sources
    /// for a single node manager. Activation is driven off
    /// <see cref="NodeState.AreEventsMonitored"/>: each call to the manager's
    /// <c>SubscribeToEvents</c> override triggers a reconcile pass that
    /// starts iterators for newly-monitored sources and cancels iterators
    /// for sources whose subscriber count dropped to zero. Disposing the
    /// registry cancels every running iterator and waits for them to drain
    /// (bounded by <see cref="EventPublishOptions.CancellationTimeout"/>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The registry runs a single background reconcile loop that consumes
    /// signals via a <see cref="SemaphoreSlim"/>. Signals coalesce — multiple
    /// rapid sub/unsub calls produce at most one extra reconcile pass.
    /// Each registered source runs in its own background <see cref="Task"/>
    /// so backpressure on one source does not block another.
    /// </para>
    /// <para>
    /// Threading: <see cref="Register"/> takes a short private lock to
    /// mutate the source dictionary; everything else runs lock-free off
    /// the reconcile worker.
    /// </para>
    /// </remarks>
    internal sealed class EventSourceRegistry : IDisposable
    {
        public EventSourceRegistry(
            FluentNodeManagerBase owner,
            ILogger logger)
        {
            m_owner = owner ?? throw new ArgumentNullException(nameof(owner));
            m_logger = logger;
            m_reconcileSignal = new SemaphoreSlim(0, 1);
            m_managerCts = new CancellationTokenSource();
            m_reconcileTask = Task.Run(() => RunReconcileLoopAsync(m_managerCts.Token));
        }

        /// <summary>
        /// Registers <paramref name="factory"/> as the event source for
        /// <paramref name="notifier"/>. Auto-promotes the notifier's
        /// <see cref="BaseObjectState.EventNotifier"/> flag and, when
        /// requested in <paramref name="options"/>, also registers the
        /// notifier as a root notifier with the owning manager. Throws if a
        /// source is already registered for the same notifier.
        /// </summary>
        /// <remarks>
        /// Designed to be called from the manager's <c>Configure</c>
        /// delegate, which runs single-threaded before clients connect.
        /// In that context the synchronous wait on
        /// <see cref="AsyncCustomNodeManager.AddRootNotifierAsync"/>
        /// (when <see cref="EventPublishOptions.RegisterAsRootNotifier"/>
        /// is set) cannot deadlock because no other thread is contending
        /// on the manager's monitored-item semaphore.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="notifier"/> is null.
        /// </exception>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="factory"/> is null.
        /// </exception>
        /// <exception cref="ServiceResultException">
        /// A source is already registered for <paramref name="notifier"/>.
        /// </exception>
        /// <exception cref="ServiceResultException">
        /// <see cref="EventPublishOptions.RegisterAsRootNotifier"/> is
        /// set and adding <paramref name="notifier"/> as a root notifier
        /// failed.
        /// </exception>
        public void Register(
            BaseObjectState notifier,
            Func<NodeState, ISystemContext, CancellationToken, IAsyncEnumerable<BaseEventState>> factory,
            EventPublishOptions? options)
        {
            if (notifier == null)
            {
                throw new ArgumentNullException(nameof(notifier));
            }
            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }

            options ??= new EventPublishOptions();

            ValidateOptions(options);

            lock (m_sourcesLock)
            {
                ThrowIfDisposed();
                if (m_sources.ContainsKey(notifier.NodeId))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadConfigurationError,
                        "Node '{0}' (id '{1}') already has a Publish event source registered.",
                        notifier.BrowseName,
                        notifier.NodeId);
                }

                // Auto-promote EventNotifier so clients can subscribe to events on
                // the node — Boiler-style models do not set this flag by default.
                if ((notifier.EventNotifier & EventNotifiers.SubscribeToEvents) == 0)
                {
                    notifier.EventNotifier |= EventNotifiers.SubscribeToEvents;
                    m_logger?.LogDebug(
                        "Publish: promoted EventNotifier of '{Browse}' (id '{NodeId}') to include SubscribeToEvents.",
                        notifier.BrowseName,
                        notifier.NodeId);
                }

                m_sources[notifier.NodeId] = new SourceEntry(notifier, factory, options);
            }

            // Root-notifier registration runs eagerly OUTSIDE m_sourcesLock so
            // existing Server-level event monitored items get attached
            // immediately. Lazy activation gated on AreEventsMonitored cannot
            // bootstrap a root notifier (the gate is on the wrong node).
            if (options.RegisterAsRootNotifier)
            {
                try
                {
                    m_owner.AddRootNotifierFromFluentAsync(notifier, CancellationToken.None)
                        .GetAwaiter()
                        .GetResult();
                    m_logger?.LogDebug(
                        "Publish: registered '{Browse}' (id '{NodeId}') as a root notifier (RegisterAsRootNotifier=true).",
                        notifier.BrowseName,
                        notifier.NodeId);
                }
                catch (Exception ex)
                {
                    lock (m_sourcesLock)
                    {
                        m_sources.Remove(notifier.NodeId);
                    }
                    throw ServiceResultException.Create(
                        StatusCodes.BadConfigurationError,
                        ex,
                        "Publish: failed to add '{0}' (id '{1}') as a root notifier.",
                        notifier.BrowseName,
                        notifier.NodeId);
                }
            }

            SignalReconcile();
        }

        private static void ValidateOptions(EventPublishOptions options)
        {
            TimeSpan timeout = options.CancellationTimeout;
            if (timeout != Timeout.InfiniteTimeSpan && timeout < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(options),
                    timeout,
                    "EventPublishOptions.CancellationTimeout must be non-negative or Timeout.InfiniteTimeSpan.");
            }
        }

        /// <summary>
        /// Signals the registry to walk every source and reconcile its
        /// activation state against <see cref="NodeState.AreEventsMonitored"/>.
        /// Called by <see cref="FluentNodeManagerBase"/> after the base
        /// <c>SubscribeToEvents</c> implementation has updated the ref-count
        /// recursively.
        /// </summary>
        public void SignalReconcile()
        {
            if (Volatile.Read(ref m_disposed) != 0)
            {
                return;
            }
            // Coalesce: only one reconcile pass is pending at a time.
            try
            {
                m_reconcileSignal.Release();
            }
            catch (SemaphoreFullException)
            {
                // Another signal is already pending — that's the whole point.
            }
            catch (ObjectDisposedException)
            {
                // Lost the race with Dispose — that's fine, the registry is
                // shutting down and no further reconciliation is needed.
            }
        }

        /// <summary>
        /// Cancels every running iterator, waits for them to drain (bounded
        /// by each source's <see cref="EventPublishOptions.CancellationTimeout"/>),
        /// and stops the reconcile loop. Idempotent.
        /// </summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref m_disposed, 1) != 0)
            {
                return;
            }

            // Stop the reconcile loop first so it cannot race with us.
            try
            {
                m_managerCts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            // Reconcile loop awaits the signal — releasing here unblocks
            // its WaitAsync so it can observe the cancellation token.
            try
            {
                m_reconcileSignal.Release();
            }
            catch (SemaphoreFullException)
            {
            }
            catch (ObjectDisposedException)
            {
            }

            // Wait for the reconcile task to actually exit. We must wait long
            // enough to cover the worst case where the loop is mid-pass when
            // the cancel fires: the in-flight ReconcileAll may DeactivateSource
            // each registered source, and each DeactivateSource blocks on its
            // worker task for up to entry.Options.CancellationTimeout. Use the
            // largest per-source timeout, plus a safety margin, instead of a
            // hardcoded 5s that could be exceeded by a single slow source.
            TimeSpan waitFor = ComputeReconcileWaitTimeout();
            try
            {
                m_reconcileTask.Wait(waitFor);
            }
            catch (AggregateException)
            {
            }

            // Deactivate every source so their iterators get cancelled.
            List<SourceEntry> snapshot;
            lock (m_sourcesLock)
            {
                snapshot = [.. m_sources.Values];
                m_sources.Clear();
            }

            foreach (SourceEntry entry in snapshot)
            {
                DeactivateSource(entry, force: true);
            }

            m_reconcileSignal.Dispose();
            m_managerCts.Dispose();
        }

        private TimeSpan ComputeReconcileWaitTimeout()
        {
            TimeSpan maxPerSource = TimeSpan.Zero;
            lock (m_sourcesLock)
            {
                foreach (SourceEntry entry in m_sources.Values)
                {
                    TimeSpan t = entry.Options.CancellationTimeout;
                    if (t == Timeout.InfiniteTimeSpan)
                    {
                        return Timeout.InfiniteTimeSpan;
                    }
                    if (t > maxPerSource)
                    {
                        maxPerSource = t;
                    }
                }
            }
            // The reconcile loop only needs the larger of its in-flight pass and
            // a small bookkeeping margin; per-source deactivation runs again on
            // the disposer thread below.
            return maxPerSource + TimeSpan.FromSeconds(5);
        }

        private async Task RunReconcileLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await m_reconcileSignal.WaitAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }

                if (ct.IsCancellationRequested)
                {
                    return;
                }

                ReconcileAll();
            }
        }

        private void ReconcileAll()
        {
            List<SourceEntry> snapshot;
            lock (m_sourcesLock)
            {
                snapshot = [.. m_sources.Values];
            }

            foreach (SourceEntry entry in snapshot)
            {
                try
                {
                    bool wantActive = entry.Options.AlwaysOn || entry.Notifier.AreEventsMonitored;
                    if (wantActive && entry.WorkerCts == null)
                    {
                        ActivateSource(entry);
                    }
                    else if (!wantActive && entry.WorkerCts != null)
                    {
                        DeactivateSource(entry, force: false);
                    }
                }
                catch (Exception ex)
                {
                    m_logger?.LogError(
                        ex,
                        "Publish: reconcile pass failed for '{Browse}' (id '{NodeId}').",
                        entry.Notifier.BrowseName,
                        entry.Notifier.NodeId);
                }
            }
        }

        private void ActivateSource(SourceEntry entry)
        {
            var cts = CancellationTokenSource.CreateLinkedTokenSource(m_managerCts.Token);
            entry.WorkerCts = cts;
            Volatile.Write(ref entry.LeakedFaulted, 0);
            entry.WorkerTask = Task.Run(() => RunSourceAsync(entry, cts.Token));
            m_logger?.LogDebug(
                "Publish: activated source for '{Browse}' (id '{NodeId}').",
                entry.Notifier.BrowseName,
                entry.Notifier.NodeId);
        }

        private void DeactivateSource(SourceEntry entry, bool force)
        {
            CancellationTokenSource? cts = entry.WorkerCts;
            Task? worker = entry.WorkerTask;
            entry.WorkerCts = null;
            entry.WorkerTask = null;

            if (cts == null)
            {
                return;
            }

            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            try
            {
                bool completed = worker?.Wait(entry.Options.CancellationTimeout) ?? true;
                if (!completed)
                {
                    Volatile.Write(ref entry.LeakedFaulted, 1);
                    m_logger?.LogWarning(
                        "Publish: source for '{Browse}' (id '{NodeId}') did not honor cancellation within {Timeout}; further yielded events will be discarded.",
                        entry.Notifier.BrowseName,
                        entry.Notifier.NodeId,
                        entry.Options.CancellationTimeout);
                }
            }
            catch (AggregateException ex)
            {
                ex.Handle(e => e is OperationCanceledException);
            }
            finally
            {
                cts.Dispose();
            }

            if (force)
            {
                m_logger?.LogDebug(
                    "Publish: tore down source for '{Browse}' (id '{NodeId}') on dispose.",
                    entry.Notifier.BrowseName,
                    entry.Notifier.NodeId);
            }
            else
            {
                m_logger?.LogDebug(
                    "Publish: deactivated source for '{Browse}' (id '{NodeId}').",
                    entry.Notifier.BrowseName,
                    entry.Notifier.NodeId);
            }
        }

        private async Task RunSourceAsync(SourceEntry entry, CancellationToken ct)
        {
            ISystemContext systemContext = m_owner.SystemContext;

            IAsyncEnumerable<BaseEventState> stream;
            try
            {
                stream = entry.Factory(entry.Notifier, systemContext, ct);
                if (stream == null)
                {
                    m_logger?.LogError(
                        "Publish: factory for '{Browse}' (id '{NodeId}') returned a null stream.",
                        entry.Notifier.BrowseName,
                        entry.Notifier.NodeId);
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                m_logger?.LogError(
                    ex,
                    "Publish: factory invocation for '{Browse}' (id '{NodeId}') threw.",
                    entry.Notifier.BrowseName,
                    entry.Notifier.NodeId);
                try
                {
                    entry.Options.OnError?.Invoke(ex);
                }
                catch
                {
                    // Swallow secondary errors from the user hook.
                }
                return;
            }

            try
            {
                await foreach (BaseEventState e in stream.WithCancellation(ct).ConfigureAwait(false))
                {
                    if (ct.IsCancellationRequested ||
                        Volatile.Read(ref entry.LeakedFaulted) != 0)
                    {
                        return;
                    }
                    DispatchEvent(entry, systemContext, e);
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown.
            }
            catch (Exception ex)
            {
                m_logger?.LogError(
                    ex,
                    "Publish: iterator for '{Browse}' (id '{NodeId}') threw — stopping that source only.",
                    entry.Notifier.BrowseName,
                    entry.Notifier.NodeId);
                try
                {
                    entry.Options.OnError?.Invoke(ex);
                }
                catch
                {
                }
            }
        }

        private void DispatchEvent(SourceEntry entry, ISystemContext context, BaseEventState e)
        {
            if (e == null)
            {
                return;
            }

            try
            {
                if (!entry.Options.SkipDefaultPopulation)
                {
                    PopulateDefaults(entry.Notifier, context, e);
                }

                entry.Notifier.ReportEvent(context, e);
            }
            catch (Exception ex)
            {
                m_logger?.LogError(
                    ex,
                    "Publish: ReportEvent for '{Browse}' (id '{NodeId}') threw — dropping this event and continuing iterator.",
                    entry.Notifier.BrowseName,
                    entry.Notifier.NodeId);
                try
                {
                    entry.Options.OnError?.Invoke(ex);
                }
                catch
                {
                }
            }
        }

        /// <summary>
        /// Mirrors <c>BaseEventState.Initialize</c> for fields the user
        /// did not populate themselves: <c>EventId</c> (random uuid),
        /// <c>EventType</c> / <c>TypeDefinitionId</c> (default for the
        /// CLR type), <c>SourceNode</c>, <c>SourceName</c>, <c>Time</c>,
        /// <c>ReceiveTime</c>, <c>Severity</c> (Medium when 0),
        /// <c>Message</c>.
        /// </summary>
        private static void PopulateDefaults(BaseObjectState notifier, ISystemContext context, BaseEventState e)
        {
            if (e.EventId == null || e.EventId.Value.IsNull)
            {
                e.EventId = PropertyState<ByteString>.With<VariantBuilder>(
                    e,
                    Uuid.NewUuid().ToByteString());
            }

            if (e.EventType == null || e.EventType.Value.IsNull)
            {
                NodeId defaultType = e.GetDefaultTypeDefinitionId(context);
                e.EventType = PropertyState<NodeId>.With<VariantBuilder>(e, defaultType);
                if (e.TypeDefinitionId.IsNull)
                {
                    e.TypeDefinitionId = defaultType;
                }
            }
            else if (e.TypeDefinitionId.IsNull)
            {
                e.TypeDefinitionId = e.EventType.Value;
            }

            if ((e.SourceNode == null || e.SourceNode.Value.IsNull) &&
                !notifier.NodeId.IsNull)
            {
                e.SourceNode = PropertyState<NodeId>.With<VariantBuilder>(e, notifier.NodeId);
            }

            if ((e.SourceName == null || e.SourceName.Value == null) &&
                !notifier.BrowseName.IsNull)
            {
                e.SourceName = PropertyState<string>.With<VariantBuilder>(
                    e,
                    notifier.BrowseName.Name ?? string.Empty);
            }

            if (e.Time == null || e.Time.Value.IsNull)
            {
                e.Time = PropertyState<DateTimeUtc>.With<VariantBuilder>(e, DateTimeUtc.Now);
            }

            if (e.ReceiveTime == null || e.ReceiveTime.Value.IsNull)
            {
                e.ReceiveTime = PropertyState<DateTimeUtc>.With<VariantBuilder>(
                    e,
                    DateTimeUtc.Now);
            }

            if (e.Severity == null || e.Severity.Value == 0)
            {
                e.Severity = PropertyState<ushort>.With<VariantBuilder>(
                    e,
                    (ushort)EventSeverity.Medium);
            }

            e.Message ??= PropertyState<LocalizedText>.With<VariantBuilder>(
                    e,
                    new LocalizedText(string.Empty));
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref m_disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(EventSourceRegistry));
            }
        }

        private sealed class SourceEntry
        {
            public SourceEntry(
                BaseObjectState notifier,
                Func<NodeState, ISystemContext, CancellationToken, IAsyncEnumerable<BaseEventState>> factory,
                EventPublishOptions options)
            {
                Notifier = notifier;
                Factory = factory;
                Options = options;
            }

            public BaseObjectState Notifier { get; }
            public Func<NodeState, ISystemContext, CancellationToken, IAsyncEnumerable<BaseEventState>> Factory { get; }
            public EventPublishOptions Options { get; }
            public CancellationTokenSource? WorkerCts;
            public Task? WorkerTask;
            public int LeakedFaulted;
        }

        private readonly FluentNodeManagerBase m_owner;
        private readonly ILogger m_logger;
        private readonly SemaphoreSlim m_reconcileSignal;
        private readonly CancellationTokenSource m_managerCts;
        private readonly Task m_reconcileTask;
        private readonly Lock m_sourcesLock = new();
        private readonly Dictionary<NodeId, SourceEntry> m_sources = [];
        private int m_disposed;
    }
}
