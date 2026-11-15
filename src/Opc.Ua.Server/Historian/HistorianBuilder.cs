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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server.Historian.InMemory;

namespace Opc.Ua.Server.Historian
{
    /// <summary>
    /// Fluent helper for wiring an <see cref="IHistorianProvider"/> into
    /// the server. Sample usage:
    /// </summary>
    /// <example>
    /// <code>
    /// server
    ///     .UseHistorian()
    ///     .UseInMemory()
    ///     .Historize(variableNodeId, AccessLevels.HistoryRead | AccessLevels.HistoryWrite)
    ///     .RegisterAsDefault();
    /// </code>
    /// </example>
    public sealed class HistorianBuilder : IAsyncDisposable
    {
        private static readonly ConditionalWeakTable<IHistorianProvider, ProviderCaptureState>
            s_providerCaptureStates = new();

        private static readonly Lock s_providerCaptureLock = new();

        /// <summary>
        /// Creates a new builder bound to the supplied server.
        /// </summary>
        public HistorianBuilder(IServerInternal server)
        {
            m_server = server ?? throw new ArgumentNullException(nameof(server));
            if (server is not IHistorianRegistryProvider)
            {
                throw new InvalidOperationException(
                    "The supplied IServerInternal does not implement IHistorianRegistryProvider; " +
                    "the fluent HistorianBuilder requires the standard ServerInternalData host.");
            }
        }

        /// <summary>
        /// The historian provider currently bound to the builder. Set by
        /// <see cref="UseInMemory"/> or <see cref="UseProvider(IHistorianProvider)"/>.
        /// </summary>
        public IHistorianProvider? Provider { get; private set; }

        /// <summary>
        /// Creates a weak reference to the capture-handler target registered for a variable.
        /// </summary>
        /// <param name="variable">The historized variable.</param>
        /// <returns>A weak reference to the target retained by the variable event subscription.</returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown when the variable does not have exactly one capture handler with a target.
        /// </exception>
        /// <exception cref="ArgumentNullException"><paramref name="variable"/> is null.</exception>
        internal WeakReference<object> CreateCaptureHandlerTargetReference(BaseVariableState variable)
        {
            if (variable == null)
            {
                throw new ArgumentNullException(nameof(variable));
            }
            WeakReference<object>? captureHandlerTarget = null;
            lock (m_captureSinkLock)
            {
                foreach (CaptureHandlerRegistration registration in m_captureHandlers)
                {
                    if (ReferenceEquals(registration.Variable, variable))
                    {
                        if (captureHandlerTarget != null)
                        {
                            throw new InvalidOperationException(
                                "The variable has multiple registered historian capture handlers.");
                        }
                        if (registration.Handler.Target is not object target)
                        {
                            throw new InvalidOperationException(
                                "The registered historian capture handler has no target.");
                        }
                        captureHandlerTarget = new WeakReference<object>(target);
                    }
                }
            }

            return captureHandlerTarget ??
                throw new InvalidOperationException(
                "The variable does not have a registered historian capture handler.");
        }

        /// <summary>
        /// Configures the builder to use the bundled
        /// <see cref="InMemoryHistorianProvider"/>. The returned provider
        /// is the same instance that becomes <see cref="Provider"/>; the
        /// caller may keep the reference to register more variables later.
        /// </summary>
        public InMemoryHistorianProvider UseInMemory(InMemoryHistorianOptions? options = null)
        {
            lock (m_captureSinkLock)
            {
                ThrowIfDisposing();
                var provider = new InMemoryHistorianProvider(
                    options ?? new InMemoryHistorianOptions(),
                    (m_server as ITimeProviderProvider)?.TimeProvider ?? TimeProvider.System);
                Provider = provider;
                m_provisionNodes = true;
                return provider;
            }
        }

        /// <summary>
        /// Configures the builder to use the bundled
        /// <see cref="InMemoryHistorianProvider"/> and returns the builder so
        /// that historian configuration reads as a single fluent chain. The
        /// created provider is owned by the builder and remains reachable
        /// through <see cref="Provider"/> for callers that need to register
        /// more variables later.
        /// </summary>
        public HistorianBuilder UseInMemoryProvider(InMemoryHistorianOptions? options = null)
        {
            UseInMemory(options);
            return this;
        }

        /// <summary>
        /// Configures the builder to use the supplied provider.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="provider"/> is <c>null</c>.</exception>
        public HistorianBuilder UseProvider(IHistorianProvider provider)
        {
            return UseProvider(provider, provisionNodes: true);
        }

        /// <summary>
        /// Configures the provider and whether capture setup provisions its in-memory nodes.
        /// </summary>
        /// <param name="provider">The provider to use for capture and configuration.</param>
        /// <param name="provisionNodes">
        /// Whether historization registers in-memory nodes and applies the supplied capabilities.
        /// When false, the caller must provision nodes beforehand; capture setup leaves their
        /// provider capabilities unchanged.
        /// </param>
        /// <returns>The current builder.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="provider"/> is null.</exception>
        public HistorianBuilder UseProvider(IHistorianProvider provider, bool provisionNodes)
        {
            if (provider is null)
            {
                throw new ArgumentNullException(nameof(provider));
            }
            lock (m_captureSinkLock)
            {
                ThrowIfDisposing();
                Provider = provider;
                m_provisionNodes = provisionNodes;
            }
            return this;
        }

        /// <summary>
        /// Marks <paramref name="variable"/> as historizing and registers
        /// it with the in-memory provider when one is bound. Optionally
        /// sets the <c>HistoryRead</c> / <c>HistoryWrite</c> access level
        /// bits and the <see cref="BaseVariableState.Historizing"/> flag.
        /// <paramref name="historyAccessLevel"/> defaults to
        /// <c>HistoryRead</c> only; HistoryUpdate needs an explicit
        /// <c>HistoryRead | HistoryWrite</c>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>Automatic value capture (opt-out).</strong> When
        /// <paramref name="autoCapture"/> is <c>true</c> (the default),
        /// the builder attaches a handler to
        /// <see cref="NodeState.StateChanged"/> that forwards every
        /// value-mask change on the variable to a per-builder
        /// <see cref="HistorianCaptureSink"/>. The sink batches samples
        /// across all historized variables that share this builder and
        /// flushes them to <see cref="Provider"/> via
        /// <see cref="IHistorianBulkInsertProvider"/> (when supported)
        /// or per-node <see cref="IHistorianDataProvider.InsertAsync"/>.
        /// Pass <paramref name="autoCapture"/> = <c>false</c> to disable
        /// — only explicit HistoryUpdate Insert calls will populate the
        /// archive in that case.
        /// </para>
        /// <para>
        /// Auto-capture requires <paramref name="systemContext"/>
        /// because the sink stores it once at construction. When
        /// <paramref name="autoCapture"/> is <c>true</c> and
        /// <paramref name="systemContext"/> is <c>null</c>, the call
        /// throws.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="variable"/> is <c>null</c>.</exception>
        public HistorianBuilder Historize(
            BaseVariableState variable,
            byte historyAccessLevel = AccessLevels.HistoryRead,
            bool setHistorizing = true,
            ISystemContext? systemContext = null,
            HistorianNodeCapabilities? capabilities = null,
            bool autoCapture = true,
            HistorianCaptureOptions? captureOptions = null)
        {
            if (variable == null)
            {
                throw new ArgumentNullException(nameof(variable));
            }
            lock (m_captureSinkLock)
            {
                ThrowIfDisposing();
                if (autoCapture && Provider is { } provider)
                {
                    DemandProviderCaptureAdmission(provider);
                }
            }

            if (setHistorizing)
            {
                variable.Historizing = true;
            }
            if (historyAccessLevel != 0)
            {
                variable.AccessLevel = (byte)(variable.AccessLevel | historyAccessLevel);
                variable.UserAccessLevel = (byte)(variable.UserAccessLevel | historyAccessLevel);
            }

            if (m_provisionNodes && Provider is InMemoryHistorianProvider inMemory)
            {
                inMemory.Register(variable.NodeId, capabilities);
            }

            if (systemContext != null)
            {
                EnsureAnnotationsProperty(systemContext, variable, capabilities, historyAccessLevel);
            }

            if (autoCapture)
            {
                AttachAutoCapture(variable, systemContext, captureOptions);
            }
            return this;
        }

        /// <summary>
        /// Marks a variable as historizing and installs its historical data
        /// configuration without blocking on asynchronous provider work.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="systemContext"/> is <c>null</c>.</exception>
        public async ValueTask<HistorianBuilder> HistorizeAsync(
            BaseVariableState variable,
            ISystemContext systemContext,
            byte historyAccessLevel = AccessLevels.HistoryRead,
            bool setHistorizing = true,
            HistorianNodeCapabilities? capabilities = null,
            bool autoCapture = true,
            HistorianCaptureOptions? captureOptions = null,
            CancellationToken cancellationToken = default)
        {
            if (systemContext == null)
            {
                throw new ArgumentNullException(nameof(systemContext));
            }
            Historize(
                variable,
                historyAccessLevel,
                setHistorizing,
                systemContext: systemContext,
                capabilities: capabilities,
                autoCapture: autoCapture,
                captureOptions: captureOptions);
            EnsureProvider();
            await HistoricalDataConfigurationInstaller.EnsureInstalledAsync(
                systemContext,
                variable,
                Provider!,
                cancellationToken).ConfigureAwait(false);
            return this;
        }

        /// <summary>
        /// Marks an Object as a historical event notifier and optionally installs
        /// its <c>HistoricalEventConfigurationType</c> companion. By default,
        /// events reported through <see cref="NodeState.ReportEvent"/> or
        /// <see cref="NodeState.ReportEventAsync"/> are snapshotted after live
        /// forwarding and queued for asynchronous archival.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="notifier"/> is <c>null</c>.</exception>
        public async ValueTask<HistoricalEventConfigurationState?> HistorizeEventsAsync(
            BaseObjectState notifier,
            ISystemContext systemContext,
            byte? eventNotifier = null,
            bool installConfiguration = true,
            HistorianNodeCapabilities? capabilities = null,
            bool autoCapture = true,
            HistorianCaptureOptions? captureOptions = null,
            CancellationToken cancellationToken = default)
        {
            if (notifier == null)
            {
                throw new ArgumentNullException(nameof(notifier));
            }
            if (systemContext == null)
            {
                throw new ArgumentNullException(nameof(systemContext));
            }
            EnsureProvider();
            if (autoCapture)
            {
                DemandProviderCaptureAdmission(Provider!);
            }
            if (m_provisionNodes && Provider is InMemoryHistorianProvider inMemory)
            {
                inMemory.Register(
                    notifier.NodeId,
                    capabilities ??
                    HistorianNodeCapabilities.EventReadWrite);
            }
            HistorianNodeCapabilities effectiveCapabilities = capabilities ??
                await Provider!.GetCapabilitiesAsync(
                    notifier.NodeId,
                    cancellationToken).ConfigureAwait(false);
            byte historyBits = eventNotifier ??
                (byte)(
                    (effectiveCapabilities.ReadEventHistory
                        ? EventNotifiers.HistoryRead
                        : 0) |
                    (effectiveCapabilities.SupportsAnyEventUpdate
                        ? EventNotifiers.HistoryWrite
                        : 0));
            lock (m_captureSinkLock)
            {
                ThrowIfDisposing();
                if (autoCapture)
                {
                    AttachEventCapture(
                        notifier,
                        effectiveCapabilities,
                        captureOptions);
                }
                notifier.EventNotifier = (byte)(
                    notifier.EventNotifier |
                    historyBits);
            }
            if (!installConfiguration)
            {
                return null;
            }
            return await HistoricalEventConfigurationInstaller
                .EnsureInstalledAsync(
                    systemContext,
                    notifier,
                    Provider!,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        private void AttachEventCapture(
            BaseObjectState notifier,
            HistorianNodeCapabilities capabilities,
            HistorianCaptureOptions? captureOptions)
        {
            lock (m_captureSinkLock)
            {
                ThrowIfDisposing();
                DemandProviderCaptureAdmission(Provider!);
                for (int i = 0; i < m_eventCaptureHandlers.Count; i++)
                {
                    if (ReferenceEquals(
                        m_eventCaptureHandlers[i].Notifier,
                        notifier))
                    {
                        return;
                    }
                }
                EnsureRegisteredWithLifecycle();
                ThrowIfDisposing();
                m_captureConstructionCount++;
                try
                {
                    // Ownership transfers to m_eventCaptureHandlers and is released
                    // by DisposeAsync.
#pragma warning disable CA2000
                    var capture = new HistorianEventCapture(
                        m_server,
                        Provider!,
                        capabilities,
                        captureOptions,
                        cleanupStarted: CreateProviderCleanupStartedHandler(Provider!));
#pragma warning restore CA2000
                    void handler(
                        ISystemContext context,
                        NodeState source,
                        IFilterTarget eventInstance)
                    {
                        capture.Enqueue(
                            context,
                            source,
                            eventInstance);
                    }

                    m_eventCaptureHandlers.Add(
                        new EventCaptureHandlerRegistration(
                            notifier,
                            handler,
                            capture));
                    ThrowIfDisposing();
                    notifier.EventReported += handler;
                }
                finally
                {
                    CompleteCaptureConstruction();
                }
            }
        }

        /// <summary>
        /// Attaches a <see cref="NodeState.StateChanged"/> handler that
        /// pushes every value-mask change into the per-builder
        /// <see cref="HistorianCaptureSink"/>.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Thrown when no historian provider has been bound, or when <paramref name="systemContext"/>
        /// is not a <see cref="ServerSystemContext"/>.
        /// </exception>
        private void AttachAutoCapture(
            BaseVariableState variable,
            ISystemContext? systemContext,
            HistorianCaptureOptions? captureOptions)
        {
            if (Provider == null)
            {
                throw new InvalidOperationException(
                    "Cannot enable auto-capture: no historian provider has been bound. " +
                    "Call UseInMemory() or UseProvider() before Historize(autoCapture: true).");
            }
            if (systemContext is not ServerSystemContext serverContext)
            {
                throw new InvalidOperationException(
                    "Cannot enable auto-capture: a ServerSystemContext must be supplied via " +
                    "the systemContext argument. Pass the manager's SystemContext or call " +
                    "Historize(autoCapture: false) to disable.");
            }

            lock (m_captureSinkLock)
            {
                ThrowIfDisposing();
                DemandProviderCaptureAdmission(Provider!);
                EnsureRegisteredWithLifecycle();
                ThrowIfDisposing();
                m_captureConstructionCount++;
                try
                {
                    HistorianCaptureSink sink = GetOrCreateCaptureSink(serverContext, captureOptions);
                    ThrowIfDisposing();
                    // The handler is a per-instance closure so the same NodeId
                    // routes to its sink across multiple captures; detach when
                    // the builder is disposed.
                    void handler(ISystemContext ctx, NodeState node, NodeStateChangeMasks masks)
                    {
                        if ((masks & NodeStateChangeMasks.Value) == 0)
                        {
                            return;
                        }
                        if (node is not BaseVariableState v)
                        {
                            return;
                        }
                        sink.Enqueue(v.NodeId, new DataValue(
                            v.WrappedValue,
                            v.StatusCode,
                            sourceTimestamp: v.Timestamp,
                            serverTimestamp: DateTime.UtcNow));
                    }
                    variable.StateChanged += handler;
                    m_captureHandlers.Add(new CaptureHandlerRegistration(variable, handler));
                }
                finally
                {
                    CompleteCaptureConstruction();
                }
            }
        }

        private void CompleteCaptureConstruction()
        {
            if (--m_captureConstructionCount == 0)
            {
                m_captureConstructionFinished?.TrySetResult(true);
            }
        }

        private HistorianCaptureSink GetOrCreateCaptureSink(
            ServerSystemContext serverContext,
            HistorianCaptureOptions? options)
        {
            lock (m_captureSinkLock)
            {
                ThrowIfDisposing();
                if (m_captureSink is null)
                {
                    var sink = new HistorianCaptureSink(
                        Provider!,
                        serverContext,
                        options,
                        cleanupStarted: CreateProviderCleanupStartedHandler(Provider!));
                    m_captureSinks.Add(sink);
                    m_captureSink ??= sink;
                }
                return m_captureSink;
            }
        }

        private Action<Task> CreateProviderCleanupStartedHandler(IHistorianProvider provider)
        {
            return consumer =>
            {
                lock (m_captureSinkLock)
                {
                    m_pendingProviderCleanupCount++;
                    lock (s_providerCaptureLock)
                    {
                        ProviderCaptureState state = s_providerCaptureStates.GetValue(
                            provider, static _ => new ProviderCaptureState());
                        if (state.PendingCleanupCount == 0)
                        {
                            state.CleanupDrained = new TaskCompletionSource<bool>(
                                TaskCreationOptions.RunContinuationsAsynchronously);
                        }
                        state.PendingCleanupCount++;
                    }
                }
                _ = consumer.ContinueWith(
                    static (completed, state) =>
                    {
                        _ = completed.Exception;
                        var (owner, pendingProvider) = ((HistorianBuilder, IHistorianProvider))state!;
                        owner.CompleteProviderCleanup(pendingProvider);
                    },
                    (this, provider),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            };
        }

        /// <summary>
        /// Waits until all timed-out capture consumers for the provider have completed.
        /// </summary>
        /// <param name="provider">The provider whose capture cleanup is being observed.</param>
        /// <returns>A task that completes when no timed-out consumer remains.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="provider"/> is null.</exception>
        internal static Task WaitForProviderCaptureCleanupAsync(IHistorianProvider provider)
        {
            if (provider is null)
            {
                throw new ArgumentNullException(nameof(provider));
            }
            lock (s_providerCaptureLock)
            {
                return s_providerCaptureStates.TryGetValue(provider, out ProviderCaptureState? state) &&
                    state.PendingCleanupCount != 0
                    ? state.CleanupDrained!.Task
                    : Task.CompletedTask;
            }
        }

        private static void DemandProviderCaptureAdmission(IHistorianProvider provider)
        {
            lock (s_providerCaptureLock)
            {
                if (s_providerCaptureStates.TryGetValue(provider, out ProviderCaptureState? state) &&
                    state.PendingCleanupCount != 0)
                {
                    throw new InvalidOperationException(
                        "A historian capture pipeline is still cleaning up for this provider.");
                }
            }
        }

        private void CompleteProviderCleanup(IHistorianProvider provider)
        {
            bool unregister = false;
            TaskCompletionSource<bool>? drained = null;
            lock (m_captureSinkLock)
            {
                if (m_pendingProviderCleanupCount != 0)
                {
                    m_pendingProviderCleanupCount--;
                }
                lock (s_providerCaptureLock)
                {
                    if (s_providerCaptureStates.TryGetValue(provider, out ProviderCaptureState? state) &&
                        state.PendingCleanupCount != 0 &&
                        --state.PendingCleanupCount == 0)
                    {
                        drained = state.CleanupDrained;
                        state.CleanupDrained = null;
                    }
                }
                unregister = TakeLifecycleRegistrationIfDrained();
            }
            drained?.TrySetResult(true);
            if (unregister)
            {
                (m_server as IHistorianBuilderRegistry)?.UnregisterHistorianBuilder(this);
            }
        }

        private void MarkDisposalOutcomePublished()
        {
            bool unregister;
            lock (m_captureSinkLock)
            {
                m_disposalOutcomePublished = true;
                unregister = TakeLifecycleRegistrationIfDrained();
            }
            if (unregister)
            {
                (m_server as IHistorianBuilderRegistry)?.UnregisterHistorianBuilder(this);
            }
        }

        private bool TakeLifecycleRegistrationIfDrained()
        {
            if (m_pendingProviderCleanupCount != 0 ||
                !m_disposalOutcomePublished ||
                !m_registeredWithLifecycle)
            {
                return false;
            }
            m_registeredWithLifecycle = false;
            return true;
        }

        private void EnsureRegisteredWithLifecycle()
        {
            lock (m_captureSinkLock)
            {
                ThrowIfDisposing();
                if (m_registeredWithLifecycle)
                {
                    return;
                }
                if (m_server is IHistorianBuilderRegistry registry)
                {
                    registry.RegisterHistorianBuilder(this);
                    m_registeredWithLifecycle = true;
                }
            }
        }

        /// <summary>
        /// Rejects registration after terminal capture disposal starts.
        /// </summary>
        private void ThrowIfDisposing()
        {
            if (m_disposalFinished is not null)
            {
                throw new ObjectDisposedException(nameof(HistorianBuilder));
            }
        }

        /// <summary>
        /// Flushes pending auto-captured samples and tears down the
        /// per-builder <see cref="HistorianCaptureSink"/>, detaching the
        /// <see cref="NodeState.StateChanged"/> handlers it installed.
        /// </summary>
        /// <remarks>
        /// Disposal is terminal. Concurrent and later callers observe the same cleanup outcome.
        /// </remarks>
        /// <exception cref="AggregateException"></exception>
        public async ValueTask DisposeAsync()
        {
            CaptureHandlerRegistration[] handlers = [];
            EventCaptureHandlerRegistration[] eventHandlers = [];
            HistorianCaptureSink[] sinks = [];
            CaptureHandlerRegistration[] pendingHandlers = [];
            EventCaptureHandlerRegistration[] pendingEventHandlers = [];
            HistorianCaptureSink[] pendingSinks = [];
            Task? pendingConstruction = null;
            TaskCompletionSource<bool> completion;
            bool ownsDisposal = false;
            lock (m_captureSinkLock)
            {
                if (m_disposalFinished is { } active)
                {
                    completion = active;
                }
                else
                {
                    completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    m_disposalFinished = completion;
                    ownsDisposal = true;
                    handlers = [.. m_captureHandlers];
                    m_captureHandlers.Clear();
                    eventHandlers = [.. m_eventCaptureHandlers];
                    m_eventCaptureHandlers.Clear();
                    sinks = [.. m_captureSinks];
                    m_captureSinks.Clear();
                    m_captureSink = null;
                    if (m_captureConstructionCount != 0)
                    {
                        // A dependency callback can start disposal before its capture is recorded.
                        m_captureConstructionFinished = new TaskCompletionSource<bool>(
                            TaskCreationOptions.RunContinuationsAsynchronously);
                        pendingConstruction = m_captureConstructionFinished.Task;
                    }
                }
            }
            if (!ownsDisposal)
            {
                await completion.Task.ConfigureAwait(false);
                return;
            }
            try
            {
                // Detach handlers first so no new samples enqueue while we drain.
                foreach (CaptureHandlerRegistration reg in handlers)
                {
                    reg.Variable.StateChanged -= reg.Handler;
                }
                foreach (EventCaptureHandlerRegistration reg in eventHandlers)
                {
                    reg.Notifier.EventReported -= reg.Handler;
                }
                if (pendingConstruction is not null)
                {
                    await pendingConstruction.ConfigureAwait(false);
                    lock (m_captureSinkLock)
                    {
                        pendingHandlers = [.. m_captureHandlers];
                        m_captureHandlers.Clear();
                        pendingEventHandlers = [.. m_eventCaptureHandlers];
                        m_eventCaptureHandlers.Clear();
                        pendingSinks = [.. m_captureSinks];
                        m_captureSinks.Clear();
                        m_captureSink = null;
                        m_captureConstructionFinished = null;
                    }
                    foreach (CaptureHandlerRegistration reg in pendingHandlers)
                    {
                        reg.Variable.StateChanged -= reg.Handler;
                    }
                    foreach (EventCaptureHandlerRegistration reg in pendingEventHandlers)
                    {
                        reg.Notifier.EventReported -= reg.Handler;
                    }
                }
                List<Exception>? errors = null;
                foreach (EventCaptureHandlerRegistration reg in eventHandlers)
                {
                    try
                    {
                        await reg.Capture.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        errors ??= [];
                        errors.Add(exception);
                    }
                }
                foreach (EventCaptureHandlerRegistration reg in pendingEventHandlers)
                {
                    try
                    {
                        await reg.Capture.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        errors ??= [];
                        errors.Add(exception);
                    }
                }
                foreach (HistorianCaptureSink sink in sinks)
                {
                    try
                    {
                        await sink.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        errors ??= [];
                        errors.Add(exception);
                    }
                }
                foreach (HistorianCaptureSink sink in pendingSinks)
                {
                    try
                    {
                        await sink.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        errors ??= [];
                        errors.Add(exception);
                    }
                }
                if (errors != null)
                {
                    throw new AggregateException(
                        "One or more historian capture pipelines failed during shutdown.",
                        errors);
                }
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                completion.TrySetException(failure);
                _ = completion.Task.Exception;
                MarkDisposalOutcomePublished();
                throw;
            }
            completion.TrySetResult(true);
            MarkDisposalOutcomePublished();
        }

        /// <summary>
        /// Detaches and cancels raw capture when synchronous fluent setup fails before publishing its binding.
        /// </summary>
        internal void AbortRawCapture()
        {
            CaptureHandlerRegistration[] handlers;
            HistorianCaptureSink? sink;
            lock (m_captureSinkLock)
            {
                handlers = [.. m_captureHandlers];
                m_captureHandlers.Clear();
                sink = m_captureSink;
                m_captureSink = null;
            }
            foreach (CaptureHandlerRegistration registration in handlers)
            {
                registration.Variable.StateChanged -= registration.Handler;
            }
            sink?.Abort();
        }

        private readonly record struct CaptureHandlerRegistration(
            BaseVariableState Variable,
            NodeStateChangedHandler Handler);

        private readonly record struct EventCaptureHandlerRegistration(
            BaseObjectState Notifier,
            NodeStateReportEventHandler Handler,
            HistorianEventCapture Capture);

        private sealed class ProviderCaptureState
        {
            public int PendingCleanupCount { get; set; }

            public TaskCompletionSource<bool>? CleanupDrained { get; set; }
        }

        private readonly List<CaptureHandlerRegistration> m_captureHandlers = [];
        private readonly List<EventCaptureHandlerRegistration> m_eventCaptureHandlers = [];
        private readonly List<HistorianCaptureSink> m_captureSinks = [];
        private HistorianCaptureSink? m_captureSink;
        private readonly Lock m_captureSinkLock = new();
        private int m_captureConstructionCount;
        private TaskCompletionSource<bool>? m_captureConstructionFinished;
        private bool m_registeredWithLifecycle;
        private bool m_disposalOutcomePublished;

        /// <summary>The active capture-disposal attempt shared by concurrent callers.</summary>
        private TaskCompletionSource<bool>? m_disposalFinished;
        private int m_pendingProviderCleanupCount;

        /// <summary>
        /// Ensures that <paramref name="variable"/> has an <c>Annotations</c>
        /// property when the provider's per-node capabilities advertise
        /// annotation support. The property is created if missing and its
        /// access-level bits are set to the same value as the variable's
        /// <c>HistoryRead</c> / <c>HistoryWrite</c> access.
        /// </summary>
        private static void EnsureAnnotationsProperty(
            ISystemContext systemContext,
            BaseVariableState variable,
            HistorianNodeCapabilities? capabilities,
            byte historyAccessLevel)
        {
            var browseName = new QualifiedName(BrowseNames.Annotations);
            BaseInstanceState? existing = variable.FindChild(systemContext, browseName);

            if (existing == null)
            {
                // Only auto-create when the caller explicitly wants annotation support.
                if (capabilities == null || !capabilities.InsertAnnotation)
                {
                    return;
                }

                var annotations = new PropertyState(variable)
                {
                    SymbolicName = BrowseNames.Annotations,
                    ReferenceTypeId = ReferenceTypeIds.HasProperty,
                    TypeDefinitionId = VariableTypeIds.PropertyType,
                    DataType = DataTypeIds.Annotation,
                    ValueRank = ValueRanks.OneDimension,
                    BrowseName = browseName,
                    DisplayName = LocalizedText.From(BrowseNames.Annotations),
                    AccessLevel = historyAccessLevel,
                    UserAccessLevel = historyAccessLevel
                };
                annotations.NodeId = annotations.NodeId.IsNull
                    ? new NodeId(variable.NodeId + "/Annotations", variable.NodeId.NamespaceIndex)
                    : annotations.NodeId;
                variable.AddChild(annotations);
                variable.AddReference(ReferenceTypeIds.HasProperty, false, annotations.NodeId);
                annotations.AddReference(ReferenceTypeIds.HasProperty, true, variable.NodeId);
                return;
            }

            if (existing is BaseVariableState annotationsVar && historyAccessLevel != 0)
            {
                annotationsVar.AccessLevel = (byte)(annotationsVar.AccessLevel | historyAccessLevel);
                annotationsVar.UserAccessLevel = (byte)(annotationsVar.UserAccessLevel | historyAccessLevel);
            }
        }

        /// <summary>
        /// Registers <see cref="Provider"/> as the server's default
        /// historian fallback (used for any historizing node that is not
        /// covered by a NodeId-scoped or namespace-scoped binding).
        /// </summary>
        public HistorianBuilder RegisterAsDefault()
        {
            EnsureProvider();
            EnsureRegisteredWithLifecycle();
            Registry.RegisterDefault(Provider!);
            return this;
        }

        /// <summary>
        /// Registers <see cref="Provider"/> for every historizing node
        /// whose NodeId namespace matches <paramref name="namespaceUri"/>.
        /// </summary>
        public HistorianBuilder RegisterForNamespace(string namespaceUri)
        {
            EnsureProvider();
            EnsureRegisteredWithLifecycle();
            Registry.RegisterForNamespace(namespaceUri, Provider!);
            return this;
        }

        /// <summary>
        /// Registers <see cref="Provider"/> specifically for
        /// <paramref name="nodeId"/>.
        /// </summary>
        public HistorianBuilder RegisterForNode(NodeId nodeId)
        {
            EnsureProvider();
            EnsureRegisteredWithLifecycle();
            Registry.RegisterForNode(nodeId, Provider!);
            return this;
        }

        private void EnsureProvider()
        {
            lock (m_captureSinkLock)
            {
                ThrowIfDisposing();
            }
            if (Provider == null)
            {
                throw new InvalidOperationException(
                    "No historian provider has been bound. Call UseInMemory() or UseProvider() first.");
            }
        }

        private IHistorianProviderRegistry Registry
            => ((IHistorianRegistryProvider)m_server).HistorianRegistry;

        private readonly IServerInternal m_server;

        /// <summary>Whether capture setup provisions and reconfigures in-memory provider nodes.</summary>
        private bool m_provisionNodes = true;
    }

    /// <summary>
    /// Extension helpers that root a <see cref="HistorianBuilder"/> on
    /// an <see cref="IServerInternal"/>.
    /// </summary>
    public static class HistorianBuilderExtensions
    {
        /// <summary>
        /// Starts a fluent registration chain for the historian.
        /// </summary>
        public static HistorianBuilder UseHistorian(this IServerInternal server)
        {
            return new HistorianBuilder(server);
        }
    }
}
