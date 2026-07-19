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
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Bindings;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Client
{
    /// <summary>
    /// The implementation of a reverse connect client manager.
    /// </summary>
    /// <remarks>
    /// This reverse connect manager allows to register for reverse connections
    /// with various strategies:
    /// i) take any connection.
    /// ii) filter for a specific application Uri and Url scheme.
    /// iii) filter for the Url.
    /// Second, any filter can be combined with the Once or Always flag.
    /// The lifecycle (start, reload, stop, dispose) is fully asynchronous
    /// (TAP). The synchronous <see cref="StartService(ApplicationConfiguration)"/>
    /// overloads and <see cref="Dispose()"/> are retained only as obsolete
    /// compatibility wrappers around the async lifecycle.
    /// </remarks>
    public class ReverseConnectManager : IDisposable, IAsyncDisposable
    {
        /// <summary>
        /// A default value for reverse hello configurations, if undefined.
        /// </summary>
        /// <remarks>
        /// This value is used as wait timeout if the value is undefined by a caller.
        /// </remarks>
        public const int DefaultWaitTimeout = 20000;

        /// <summary>
        /// Transactional lifecycle state of the reverse connect manager.
        /// </summary>
        private enum ReverseConnectManagerState
        {
            New = 0,
            Preparing = 1,
            Starting = 2,
            Started = 3,
            Reloading = 4,
            Stopping = 5,
            Stopped = 6,
            Faulted = 7,
            Disposing = 8,
            Disposed = 9
        }

        /// <summary>
        /// Internal state of the reverse connect host.
        /// </summary>
        private enum ReverseConnectHostState
        {
            New = 0,
            Closed = 1,
            Open = 2,
            Errored = 3
        }

        /// <summary>
        /// Specify the strategy for the reverse connect registration.
        /// </summary>
        [Flags]
        public enum ReverseConnectStrategy
        {
            /// <summary>
            /// Undefined strategy, defaults to Once.
            /// </summary>
            Undefined = 0,

            /// <summary>
            /// Remove entry after reverse connect callback.
            /// </summary>
            Once = 1,

            /// <summary>
            /// Always callback on matching url or uri.
            /// </summary>
            Always = 2,

            /// <summary>
            /// Flag for masking any connection.
            /// </summary>
            Any = 0x80,

            /// <summary>
            /// Respond to any incoming reverse connection,
            /// remove entry after reverse connect callback.
            /// </summary>
            AnyOnce = Any | Once,

            /// <summary>
            /// Respond to any incoming reverse connection,
            /// always callback.
            /// </summary>
            AnyAlways = Any | Always
        }

        /// <summary>
        /// Entry for a client reverse connect registration.
        /// </summary>
        private sealed class ReverseConnectInfo
        {
            public ReverseConnectInfo(
                Uri endpointUrl,
                ReverseConnectHost reverseConnectHost,
                bool configEntry,
                ApplicationConfiguration? manualConfiguration)
            {
                EndpointUrl = endpointUrl;
                ReverseConnectHost = reverseConnectHost;
                State = ReverseConnectHostState.New;
                ConfigEntry = configEntry;
                ManualConfiguration = manualConfiguration;
            }

            public readonly Uri EndpointUrl;
            public ReverseConnectHost ReverseConnectHost;
            public ReverseConnectHostState State;
            public bool ConfigEntry;

            /// <summary>
            /// The application configuration (TLS context) originally supplied
            /// to <see cref="AddEndpoint(Uri, ApplicationConfiguration?)"/> for
            /// this manual endpoint, or <c>null</c> for configured endpoints.
            /// It is reused verbatim when a destroyed manual host is recreated
            /// so a WSS listener keeps its original certificate registry and
            /// validator regardless of the later start configuration.
            /// </summary>
            public readonly ApplicationConfiguration? ManualConfiguration;

            public Exception? Error;
        }

        /// <summary>
        /// A prepared, not-yet-committed reverse-connect configuration. It
        /// owns the unbound <see cref="ReverseConnectHost"/> objects that this
        /// operation created (see <see cref="OwnedHosts"/>) until the async
        /// lifecycle commits or discards it. All application-configuration,
        /// certificate and type metadata stay operation-local here and are
        /// only promoted to instance state on a successful activation.
        /// </summary>
        private sealed class PreparedConfiguration
        {
            public PreparedConfiguration(
                ReverseConnectClientConfiguration effectiveConfiguration,
                ApplicationConfiguration? applicationConfiguration,
                ApplicationType applicationType,
                [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
                Type? configType,
                Dictionary<Uri, ReverseConnectInfo> hosts,
                List<ReverseConnectInfo> ownedHosts,
                string? sourceFilePath,
                DateTime? sourceFileLastWriteUtc)
            {
                EffectiveConfiguration = effectiveConfiguration;
                ApplicationConfiguration = applicationConfiguration;
                ApplicationType = applicationType;
                ConfigType = configType;
                Hosts = hosts;
                OwnedHosts = ownedHosts;
                SourceFilePath = sourceFilePath;
                SourceFileLastWriteUtc = sourceFileLastWriteUtc;
            }

            public ReverseConnectClientConfiguration EffectiveConfiguration { get; }
            public ApplicationConfiguration? ApplicationConfiguration { get; }
            public ApplicationType ApplicationType { get; }

            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
            public Type? ConfigType { get; }

            /// <summary>
            /// The full candidate host set (manual endpoints reused by object
            /// identity plus freshly created configured endpoints).
            /// </summary>
            public Dictionary<Uri, ReverseConnectInfo> Hosts { get; }

            /// <summary>
            /// Only the hosts this operation created. Rollback/cleanup close
            /// these by object identity so reused manual hosts are never torn
            /// down and colliding URIs never leak.
            /// </summary>
            public List<ReverseConnectInfo> OwnedHosts { get; }

            public string? SourceFilePath { get; }
            public DateTime? SourceFileLastWriteUtc { get; }
        }

        /// <summary>
        /// Operation-local, AsyncLocal capture context used by the legacy
        /// <see cref="OnUpdateConfiguration(ReverseConnectClientConfiguration)"/>
        /// adapter. The context is stacked so nested app/rcc callbacks and
        /// concurrent lifecycle operations never observe each other's
        /// candidate. A subclass override that calls <c>base</c> captures the
        /// candidate; an override that omits <c>base</c> falls back to the
        /// seed candidate (which the override may have mutated in place) so
        /// endpoint replacement/customization keeps working. Suppression is
        /// only possible explicitly through the configuration provider.
        /// </summary>
        private sealed class LegacyCaptureContext
        {
            public LegacyCaptureContext(ReverseConnectClientConfiguration seed)
            {
                Seed = seed;
            }

            public ReverseConnectClientConfiguration Seed { get; }

            public bool Captured { get; private set; }

            public ReverseConnectClientConfiguration? Configuration { get; private set; }

            public void Capture(ReverseConnectClientConfiguration configuration)
            {
                Captured = true;
                Configuration = configuration;
            }
        }

        /// <summary>
        /// Manager-owned marker for an in-flight activation transaction.
        /// Published before any awaited close/open in
        /// <see cref="ActivateAsync"/> so a concurrent
        /// <c>StopServiceAsync</c>/<c>DisposeAsync</c> can observe and abort it
        /// BEFORE waiting on the lifecycle gate. The abort also distinguishes a
        /// shutdown supersession (which must not restore/reopen the previous
        /// listeners) from a caller/provider cancellation (which does).
        /// </summary>
        private sealed class ActiveTransaction
        {
            public ActiveTransaction(CancellationTokenSource cts)
            {
                m_cts = cts;
            }

            /// <summary>
            /// The manager-owned operation token, linked to the caller token,
            /// under which the candidate listeners are opened.
            /// </summary>
            public CancellationToken Token => m_cts.Token;

            /// <summary>
            /// True once a Stop/Dispose aborted this transaction to shut the
            /// manager down, as opposed to a caller/provider cancellation.
            /// </summary>
            public bool AbortedByShutdown => Volatile.Read(ref m_shutdownAbort) != 0;

            /// <summary>
            /// Records that a shutdown superseded this transaction and cancels
            /// the operation token so a cooperative listener open unblocks.
            /// </summary>
            public void AbortForShutdown()
            {
                Volatile.Write(ref m_shutdownAbort, 1);
                try
                {
                    m_cts.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }
            }

            /// <summary>
            /// Disposes the linked cancellation source.
            /// </summary>
            public void Dispose()
            {
                m_cts.Dispose();
            }

            private readonly CancellationTokenSource m_cts;
            private int m_shutdownAbort;
        }

        /// <summary>
        /// Per-operation re-entrancy scope published on an
        /// <see cref="AsyncLocal{T}"/> owner marker for the duration of a
        /// re-entrancy-sensitive operation (an in-flight startup pipeline or a
        /// user connection-waiting callback). A child task spawned inside the
        /// guarded region captures the SAME scope reference through the flowing
        /// execution context, but the originating async flow clears
        /// <see cref="Active"/> in its finally. Guards therefore reject only
        /// while <see cref="Active"/> is still <c>true</c> (a genuine synchronous
        /// re-entry on the owning flow), while a deferred child task that runs
        /// after the originating flow has returned observes
        /// <see cref="Active"/> as <c>false</c> and is not falsely rejected.
        /// Nested scopes restore the parent scope and independently deactivate.
        /// </summary>
        private sealed class OperationScope
        {
            public OperationScope(long generation)
            {
                Generation = generation;
            }

            /// <summary>
            /// Monotonic identifier of this scope, unique per manager instance.
            /// Distinguishes nested scopes for diagnostics and ensures a child
            /// task can be reasoned about against the exact scope it captured.
            /// </summary>
            public long Generation { get; }

            /// <summary>
            /// <c>true</c> while the originating async flow is still inside the
            /// guarded operation. Cleared by that flow's finally so a captured
            /// child task deferred past the flow's completion is not rejected.
            /// </summary>
            public bool Active { get; set; } = true;
        }

        /// <summary>
        /// Record to store information on a client
        /// registration for a reverse connect event.
        /// </summary>
        private sealed class Registration
        {
            public Registration(
                string? serverUri,
                Uri endpointUrl,
                EventHandler<ConnectionWaitingEventArgs> onConnectionWaiting)
                : this(endpointUrl, onConnectionWaiting)
            {
                ServerUri = Utils.ReplaceLocalhost(serverUri);
            }

            /// <summary>
            /// Register with the server certificate to extract the application Uri.
            /// </summary>
            /// <remarks>
            /// The first Uri in the subject alternate name field is considered the application Uri.
            /// </remarks>
            /// <param name="serverCertificate">The server certificate with the application Uri.</param>
            /// <param name="endpointUrl">The endpoint Url of the server.</param>
            /// <param name="onConnectionWaiting">The connection to use.</param>
            public Registration(
                Certificate serverCertificate,
                Uri endpointUrl,
                EventHandler<ConnectionWaitingEventArgs> onConnectionWaiting)
                : this(endpointUrl, onConnectionWaiting)
            {
                IReadOnlyList<string> serverUris =
                    X509Utils.GetApplicationUrisFromCertificate(serverCertificate);
                ServerUri = serverUris.Count != 0 ? serverUris[0] : null;
            }

            private Registration(
                Uri endpointUrl,
                EventHandler<ConnectionWaitingEventArgs> onConnectionWaiting)
            {
                EndpointUrl = new Uri(Utils.ReplaceLocalhost(endpointUrl.ToString()));
                OnConnectionWaiting = onConnectionWaiting;
                ReverseConnectStrategy = ReverseConnectStrategy.Once;
            }

            public readonly string? ServerUri;
            public readonly Uri EndpointUrl;
            public readonly EventHandler<ConnectionWaitingEventArgs> OnConnectionWaiting;
            public ReverseConnectStrategy ReverseConnectStrategy;

            /// <summary>
            /// The tracked completion source for an internal
            /// <see cref="WaitForConnectionAsync"/> registration, or <c>null</c>
            /// for an external callback registration. When set, a matcher claims
            /// the waiting connection by completing this source UNDER
            /// <see cref="m_registrationsLock"/> (atomically with a committed
            /// Stop/Dispose that may fault the same source), so the transport is
            /// only accepted when the claim wins the race.
            /// </summary>
            public TaskCompletionSource<ITransportWaitingConnection>? Wait { get; init; }
        }

        /// <summary>
        /// A queued configuration reload request. Overlapping reloads are
        /// serialized through a single latest-wins loop so the newest queued
        /// candidate is always applied last.
        /// </summary>
        private sealed class ReloadRequest
        {
            public ReloadRequest(
                ApplicationConfiguration configuration,
                int watcherGeneration,
                long lifecycleVersion,
                long teardownEpoch,
                CancellationToken cancellationToken)
            {
                Configuration = configuration;
                WatcherGeneration = watcherGeneration;
                LifecycleVersion = lifecycleVersion;
                TeardownEpoch = teardownEpoch;
                CancellationToken = cancellationToken;
            }

            public ApplicationConfiguration Configuration { get; }

            /// <summary>
            /// The watcher generation observed when this request was queued.
            /// </summary>
            public int WatcherGeneration { get; }

            /// <summary>
            /// The lifecycle version observed when this request was queued.
            /// </summary>
            public long LifecycleVersion { get; }

            /// <summary>
            /// The teardown epoch observed when this request was queued. The
            /// drain rejects the request without applying if a committed
            /// Stop/Dispose advanced this epoch after the request was queued.
            /// </summary>
            public long TeardownEpoch { get; }

            public CancellationToken CancellationToken { get; }

            public TaskCompletionSource<bool> Completion { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        /// <summary>
        /// Obsolete default constructor
        /// </summary>
        [Obsolete("Use ReverseConnectManager(ITelemetryContext) instead.")]
        public ReverseConnectManager()
            // Forwards null into a non-nullable telemetry parameter on the modern ctor;
            // preserves the pre-nullable parameterless behavior of this obsolete API.
            : this(null!)
        {
        }

        /// <summary>
        /// Optional <see cref="ITransportBindingRegistry"/> threaded into
        /// every <see cref="ReverseConnectHost"/> created by this manager.
        /// When <c>null</c>, the host falls back to a private
        /// <see cref="DefaultTransportBindingRegistry"/> seeded with the
        /// raw-socket TCP listener. Set this BEFORE calling
        /// <see cref="AddEndpoint(Uri)"/> /
        /// <see cref="AddEndpoint(Uri, ApplicationConfiguration)"/>
        /// so the listener picks the right binding for the URI scheme.
        /// </summary>
        public ITransportBindingRegistry? TransportBindings { get; set; }

        /// <summary>
        /// Optional provider that asynchronously validates and/or transforms
        /// the effective <see cref="ReverseConnectClientConfiguration"/>
        /// before it is activated. When <c>null</c> the pass-through
        /// <see cref="DefaultReverseConnectConfigurationProvider"/> is used,
        /// preserving direct-constructor behavior.
        /// </summary>
        public IReverseConnectConfigurationProvider? ConfigurationProvider { get; set; }

        /// <summary>
        /// Initializes the object with default values.
        /// </summary>
        public ReverseConnectManager(ITelemetryContext telemetry)
            : this(telemetry, timeProvider: null)
        {
        }

        /// <summary>
        /// Initializes the object with default values.
        /// </summary>
        /// <param name="telemetry">The telemetry context to use to create obvservability instruments.</param>
        /// <param name="timeProvider">Optional time provider used for elapsed-time calculations and
        /// async timeouts. Defaults to <see cref="TimeProvider.System"/>.</param>
        public ReverseConnectManager(ITelemetryContext telemetry, TimeProvider? timeProvider)
        {
            m_timeProvider = timeProvider ?? TimeProvider.System;
            m_telemetry = telemetry;
            m_logger = telemetry.CreateLogger<ReverseConnectManager>();
            m_state = ReverseConnectManagerState.New;
            m_registrations = [];
            m_endpointUrls = [];
            m_manualEndpoints = [];
            m_cts = new CancellationTokenSource();
        }

        /// <summary>
        /// Synchronous dispose compatibility wrapper.
        /// </summary>
        /// <remarks>
        /// This is a sync-over-async bridge retained for
        /// <c>using</c>-based callers. Prefer
        /// <see cref="DisposeAsync"/> which is the normal async path.
        /// </remarks>
        [Obsolete("Use DisposeAsync instead.")]
        [SuppressMessage("Design", "CA1063:Implement IDisposable Correctly",
            Justification = "Dispose() is an intentional sync-over-async bridge to the shared " +
            "async teardown (GetOrStartDisposeTask), which invokes the protected Dispose(bool) " +
            "override exactly once at the end. Calling Dispose(true) here would run the legacy " +
            "hook before teardown and risk double-invocation.")]
        public void Dispose()
        {
            // A dispose re-entered directly by this manager's own connection
            // callback must fail fast: the teardown drains that very callback,
            // so a synchronous wait on it here would deadlock. Fail fast and
            // leave the manager coherent for an external dispose.
            ThrowIfInvokedFromConnectionCallback(disposing: true);

            // A synchronous dispose re-entered from within this manager's own
            // in-flight startup flow (a configuration provider/factory or legacy
            // update hook) must never block on the shared teardown: the teardown
            // drains this very start task, and blocking the start on it would
            // deadlock. Request/schedule the dispose without waiting for it here
            // (it completes once the start unwinds) and fail fast instead. Only
            // an active scope (a genuine synchronous re-entry on this startup
            // flow) rejects; a deferred child task whose originating startup
            // flow already completed observes an inactive scope and proceeds.
            if (m_activeStartupOwner.Value?.Active == true)
            {
                _ = GetOrStartDisposeTask(out _);
                throw new InvalidOperationException(
                    "The reverse connect manager cannot be disposed from within " +
                    "its own startup flow (configuration provider/factory or " +
                    "update hook).");
            }

            // Isolated sync-over-async bridge for the obsolete IDisposable
            // boundary. Task.Run moves the shared disposal off any captured
            // synchronization context so a provider/derived-cleanup await that
            // posts back to the caller's context cannot deadlock. The shared
            // teardown invokes the protected Dispose(bool) override exactly
            // once at the end, so this wrapper must never call it directly.
            Task.Run(() => GetOrStartDisposeTask(out _)).GetAwaiter().GetResult();
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Overridable synchronous cleanup hook retained for subclasses that
        /// predate <see cref="DisposeAsyncCore"/> and follow the classic
        /// <see cref="IDisposable"/> pattern. The shared async teardown
        /// invokes this exactly once (with <paramref name="disposing"/> set to
        /// <c>true</c>) after every listener has been closed, regardless of
        /// whether disposal was triggered through <see cref="Dispose()"/> or
        /// <see cref="DisposeAsync"/>. The base implementation is intentionally
        /// empty; the manager's own teardown runs in the shared async path and
        /// must not be duplicated here to avoid re-entering the obsolete
        /// <see cref="Dispose()"/> wrapper or running teardown twice.
        /// </summary>
        /// <param name="disposing"><c>true</c> to release both managed and unmanaged resources; <c>false</c> to release only unmanaged resources.</param>
        protected virtual void Dispose(bool disposing)
        {
        }

        /// <summary>
        /// Asynchronously disposes the reverse connect manager. Concurrent
        /// callers observe the same shared disposal task and each await the
        /// full teardown before returning.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            // A dispose re-entered directly by this manager's own connection
            // callback must fail fast before the teardown (which drains that
            // very callback) is scheduled/awaited, otherwise the drain never
            // reaches zero. The manager stays coherent for an external dispose.
            ThrowIfInvokedFromConnectionCallback(disposing: true);

            Task task = GetOrStartDisposeTask(out bool reentrantFromStartup);
            if (reentrantFromStartup)
            {
                // A DisposeAsync re-entered from within this manager's own
                // in-flight startup flow (a configuration provider/factory or
                // legacy update hook) must not await the shared teardown: the
                // teardown drains this very start task, so awaiting it here
                // would deadlock. The dispose has already been requested and
                // scheduled above (state is Disposing, the shutdown latch is
                // set, the pending start is cancelled); it completes on its own
                // once the current start unwinds. Fail fast rather than block.
                throw new InvalidOperationException(
                    "The reverse connect manager cannot be disposed from within " +
                    "its own startup flow (configuration provider/factory or " +
                    "update hook).");
            }
            await task.ConfigureAwait(false);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Overridable asynchronous disposal core. Derived types override this
        /// (calling <c>base</c>) to participate in teardown after the manager
        /// has closed all listeners. Invoked exactly once.
        /// </summary>
        protected virtual ValueTask DisposeAsyncCore()
        {
            return default;
        }

        /// <summary>
        /// Raised when the configuration changes.
        /// </summary>
        /// <remarks>
        /// This is a caught/logged <c>async void</c> adapter that funnels a
        /// synchronous <see cref="ConfigurationWatcher.Changed"/> event into
        /// the serialized async reload path. Stale callbacks from a
        /// superseded watcher generation are rejected before and after the
        /// configuration is loaded. The configured overlay decorator (the DI
        /// <see cref="ClientReverseConnectOptions"/> reverse-connect endpoints)
        /// is reapplied to the freshly loaded configuration before the reload is
        /// enqueued so option-only endpoints survive a file change.
        /// </remarks>
        /// <param name="sender">The sender.</param>
        /// <param name="args">The <see cref="ConfigurationWatcherEventArgs"/> instance containing the event data.</param>
        protected virtual async void OnConfigurationChangedAsync(
            object? sender,
            ConfigurationWatcherEventArgs args)
        {
            int generation;
            ApplicationType applicationType;
            Type? configType;
            Func<ApplicationConfiguration, ApplicationConfiguration>? decorator;
            lock (m_lock)
            {
                if (!ReferenceEquals(sender, m_configurationWatcher))
                {
                    return;
                }
                generation = m_watcherGeneration;
                applicationType = m_applicationType;
                configType = m_configType;
                decorator = m_initialConfigurationDecorator;
            }

            try
            {
                Func<string, ApplicationType, Type?, CancellationToken,
                    Task<ApplicationConfiguration>>? loader = ConfigurationFileLoaderForTest;
                ApplicationConfiguration configuration = loader != null
                    ? await loader(args.FilePath, applicationType, configType, CancellationToken.None)
                        .ConfigureAwait(false)
                    : await ApplicationConfiguration
                        .LoadAsync(
                            new FileInfo(args.FilePath),
                            applicationType,
                            configType,
                            m_telemetry)
                        .ConfigureAwait(false);

                // Reapply the DI reverse-connect option overlay (the configured
                // ClientReverseConnectOptions reverse-connect endpoints) onto the
                // freshly loaded file base so a watcher-triggered reload keeps the
                // option-only endpoints that live only in the in-memory overlay
                // instead of losing them to the plain file load. Without this an
                // endpoint contributed solely by the DI options would disappear on
                // the next file change.
                if (decorator != null)
                {
                    configuration = decorator(configuration);
                }

                await ReloadConfigurationAsync(configuration, generation, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception e)
            {
                m_logger.CouldNotLoadUpdatedConfigurationFile(
                    e,
                    args.FilePath);
            }
        }

        /// <summary>
        /// Legacy hook invoked outside the async lifecycle gate to adapt an
        /// application-configuration candidate.
        /// </summary>
        /// <remarks>
        /// Retained for backward compatibility. The base implementation
        /// forwards the reverse-connect section to the
        /// <see cref="ReverseConnectClientConfiguration"/> hook as the
        /// candidate the async lifecycle activates. This hook is intentionally
        /// side-effect free: candidate application-configuration, certificate
        /// and type metadata stay operation-local until a successful
        /// activation, so an override must never rely on instance state being
        /// mutated here.
        /// </remarks>
        /// <param name="configuration">The configuration.</param>
        [Obsolete("Reverse connect configuration is applied through the async lifecycle. " +
            "Override to mutate/validate/reject the candidate; call base to accept it.")]
        protected virtual void OnUpdateConfiguration(ApplicationConfiguration configuration)
        {
#pragma warning disable CS0618 // internal call into the obsolete legacy adapter
            OnUpdateConfiguration(
                configuration.ClientConfiguration?.ReverseConnect
                ?? new ReverseConnectClientConfiguration());
#pragma warning restore CS0618
        }

        /// <summary>
        /// Legacy hook invoked outside the async lifecycle gate to adapt a
        /// reverse-connect candidate configuration.
        /// </summary>
        /// <remarks>
        /// Retained for backward compatibility. The base implementation
        /// captures <paramref name="configuration"/> as the candidate. A
        /// subclass override may mutate or replace the candidate (in place or
        /// by passing a new instance to <c>base</c>), or validate it and throw
        /// to reject the update. Omitting the <c>base</c> call no longer
        /// suppresses the update: the (possibly mutated) candidate is still
        /// activated so endpoint replacement/customization keeps working.
        /// Explicit suppression is only available through the
        /// <see cref="ConfigurationProvider"/>, which may return an empty
        /// configuration.
        /// </remarks>
        /// <param name="configuration">The client endpoint configuration.</param>
        [Obsolete("Reverse connect configuration is applied through the async lifecycle. " +
            "Override to mutate/replace/validate the candidate; call base to accept it. " +
            "Omitting base no longer suppresses startup; suppress via the ConfigurationProvider.")]
        protected virtual void OnUpdateConfiguration(
            ReverseConnectClientConfiguration configuration)
        {
            m_legacyCapture.Value?.Capture(
                configuration ?? new ReverseConnectClientConfiguration());
        }

        /// <summary>
        /// Add endpoint for reverse connection.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="endpointUrl"/> is <c>null</c>.</exception>
        /// <exception cref="ServiceResultException"></exception>
        public void AddEndpoint(Uri endpointUrl)
        {
            AddEndpoint(endpointUrl, null);
        }

        /// <summary>
        /// Add endpoint for reverse connection. The optional
        /// <paramref name="configuration"/> overload lets callers provide
        /// the application configuration up front so that transports
        /// terminating TLS at the listener (e.g. <c>opc.wss</c>) can pull
        /// the server certificate and validator from
        /// <see cref="ApplicationConfiguration.CertificateManager"/> before
        /// the host is created. Without it the cert is only available after
        /// <see cref="StartServiceAsync(ApplicationConfiguration, CancellationToken)"/>
        /// runs - too late for WSS listeners that need TLS state at bind time.
        /// </summary>
        /// <param name="endpointUrl">The endpoint url for reverse connections.</param>
        /// <param name="configuration">Optional configuration whose
        /// <see cref="ApplicationConfiguration.CertificateManager"/> is used
        /// for TLS termination on WSS listeners.</param>
        /// <exception cref="ArgumentNullException"><paramref name="endpointUrl"/> is <c>null</c>.</exception>
        /// <exception cref="ServiceResultException"></exception>
        public void AddEndpoint(Uri endpointUrl, ApplicationConfiguration? configuration)
        {
            if (endpointUrl == null)
            {
                throw new ArgumentNullException(nameof(endpointUrl));
            }

            ThrowIfDisposed();

            lock (m_lock)
            {
                if (!CanMutateEndpoints(m_state))
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidState);
                }

                // Manual endpoint hosts are created from the operation-local
                // application configuration supplied here (or a previously
                // activated one) so CreateEndpointInfo can plumb the
                // CertificateManager into the listener when the endpoint URL
                // needs TLS termination (WSS). Instance state is not mutated.
                m_manualEndpoints[endpointUrl] = CreateEndpointInfo(
                    endpointUrl,
                    false,
                    configuration ?? m_appConfig);
            }
        }

        /// <summary>
        /// Starts the reverse connect manager using an application configuration.
        /// </summary>
        /// <param name="configuration">The configuration.</param>
        /// <param name="ct">A cancellation token.</param>
        /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <c>null</c>.</exception>
        /// <exception cref="ServiceResultException">
        /// The manager is already started, or a configured listener endpoint is invalid or could not be opened.
        /// Listener startup is atomic; bind and listener-open failures use
        /// <see cref="StatusCodes.BadNoCommunication"/>.
        /// </exception>
        public async Task StartServiceAsync(
            ApplicationConfiguration configuration,
            CancellationToken ct = default)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            ThrowIfDisposed();

            // Reserve an operation-specific lifecycle version and mark the
            // manager Preparing BEFORE any async preparation. This makes the
            // pending start visible to Stop/Dispose (which supersede it) and
            // makes already-started/already-starting calls reject immediately.
            // The reserved start is published as the shared current-start task
            // so a concurrent EnsureStartedAsync awaits it rather than
            // returning before the manager is actually Started.
            await StartServiceExplicitAsync(configuration, null, ct)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Starts the reverse connect manager using a bare reverse-connect
        /// client configuration.
        /// </summary>
        /// <param name="configuration">The configuration.</param>
        /// <param name="ct">A cancellation token.</param>
        /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <c>null</c>.</exception>
        /// <exception cref="ServiceResultException">
        /// The manager is already started, or a configured listener endpoint is invalid or could not be opened.
        /// Listener startup is atomic; bind and listener-open failures use
        /// <see cref="StatusCodes.BadNoCommunication"/>.
        /// </exception>
        public async Task StartServiceAsync(
            ReverseConnectClientConfiguration configuration,
            CancellationToken ct = default)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            ThrowIfDisposed();

            await StartServiceExplicitAsync(null, configuration, ct)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Runs an explicit <c>StartServiceAsync</c> overload. Reserves the
        /// lifecycle version synchronously (rejecting a concurrent start with
        /// <see cref="StatusCodes.BadInvalidState"/>) and publishes the start
        /// as the shared current-start task so a concurrent
        /// <see cref="EnsureStartedAsync"/> awaits the same operation instead
        /// of returning before the manager is Started. The explicit caller
        /// awaits its own start task directly (never the shared field) so it
        /// cannot self-await.
        /// </summary>
        private async Task StartServiceExplicitAsync(
            ApplicationConfiguration? appConfig,
            ReverseConnectClientConfiguration? rccConfig,
            CancellationToken ct)
        {
            Task startTask;
            // Run the explicit start under a manager-owned token linked to the
            // caller token so hosted cancellation (CancelPendingStart, invoked
            // when a joining IHostedService.StartAsync token is cancelled) can
            // abort this start - not merely the joining awaiter. The source is
            // published under the state lock together with the reservation so a
            // CancelPendingStart racing this publication either observes it (and
            // cancels it) or latches nothing because the source is already
            // visible.
            var startCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            try
            {
                lock (m_lock)
                {
                    long myVersion = ReserveStartLocked();
                    // Consume a hosted cancellation latched before this explicit
                    // start published its token so the start observes it
                    // deterministically and never binds a listener. The token
                    // has no registrations yet, so cancelling under the lock is
                    // safe.
                    if (ConsumeStartCancelLatchLocked())
                    {
                        startCts.Cancel();
                    }
                    m_activeStartCts = startCts;
                    // RunStartAsync yields before any work so publishing the task
                    // under the lock never runs the prepare/activate prefix while
                    // the lock is held.
                    startTask = RunStartAsync(appConfig, rccConfig, myVersion, startCts.Token);
                    m_currentStartTask = startTask;
                }

                try
                {
                    await startTask.ConfigureAwait(false);
                }
                finally
                {
                    lock (m_lock)
                    {
                        if (ReferenceEquals(m_currentStartTask, startTask))
                        {
                            m_currentStartTask = null;
                        }
                        if (ReferenceEquals(m_activeStartCts, startCts))
                        {
                            m_activeStartCts = null;
                        }
                    }
                }
            }
            finally
            {
                // The start has completed (or its reservation threw), so no
                // provider/activation is still using the token; dispose it.
                startCts.Dispose();
            }
        }

        /// <summary>
        /// Yields first so the synchronous prefix of the prepare/activate
        /// pipeline never runs while the publisher of the shared current-start
        /// task still holds the state lock, then runs the reserved start.
        /// </summary>
        private async Task RunStartAsync(
            ApplicationConfiguration? appConfig,
            ReverseConnectClientConfiguration? rccConfig,
            long myVersion,
            CancellationToken ct)
        {
            await Task.Yield();
            await StartServiceCoreAsync(appConfig, rccConfig, myVersion, ct)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Runs the prepare/activate pipeline for a start operation whose
        /// lifecycle version has already been reserved (via
        /// <see cref="ReserveStartLocked"/>). Shared by the public
        /// <c>StartServiceAsync</c> overloads and the lazy shared startup so
        /// the version is always reserved synchronously before any async work.
        /// </summary>
        private async Task StartServiceCoreAsync(
            ApplicationConfiguration? appConfig,
            ReverseConnectClientConfiguration? rccConfig,
            long myVersion,
            CancellationToken ct)
        {
            PreparedConfiguration? prepared = null;
            // Mark this async flow as owning an in-flight startup so a provider
            // or legacy configuration hook that re-enters
            // EnsureStartedAsync/RegisterWaitingConnectionAsync during
            // preparation fails fast instead of awaiting this start's own shared
            // task. The marker is restored in the finally so nested/subsequent
            // operations on the same flow are unaffected. The scope's Active
            // flag is cleared in that same finally so a child task deferred
            // past this flow (e.g. a provider-spawned Task.Run) that captured
            // the scope reference is not falsely rejected as a re-entry.
            OperationScope? previousStartupOwner = m_activeStartupOwner.Value;
            var startupScope = new OperationScope(
                Interlocked.Increment(ref m_operationScopeGeneration));
            m_activeStartupOwner.Value = startupScope;
            try
            {
                // Honor an already-signalled abort (e.g. a hosted StartAsync
                // token cancelled before the shared start was published) so
                // preparation never runs the provider or binds a listener.
                ct.ThrowIfCancellationRequested();

                prepared = await PrepareAsync(appConfig, rccConfig, ct).ConfigureAwait(false);

                // Recheck the caller token immediately after provider/host
                // preparation. A non-cooperative provider may ignore the token
                // and return normally even though cancellation was requested;
                // rechecking here surfaces the cancellation while this start
                // still owns the reserved Preparing state so the catch below
                // finalizes it, instead of leaving it for ActivateAsync's gate
                // entry to observe (which would strand the manager in
                // Preparing).
                ct.ThrowIfCancellationRequested();

                await ActivateAsync(prepared, myVersion, isReload: false, ct)
                    .ConfigureAwait(false);
                prepared = null;
            }
            catch (Exception e)
            {
                if (prepared != null)
                {
                    await CleanupCandidateAsync(prepared).ConfigureAwait(false);
                }
                // Finalize an owned (non-committed) pre-activation exit so a
                // start that reserved Preparing but never committed cannot
                // strand the lifecycle in Preparing. FaultIfOwned is a no-op
                // once ActivateAsync has taken ownership of the transition (it
                // bumps the lifecycle version on commit, on restore-after-
                // failure and on supersession, and disposal marks the manager
                // Disposing), so a genuinely activated or superseded start is
                // unaffected; only a pre-activation failure or a cancellation
                // observed before commit (including one thrown by ActivateAsync's
                // gate entry) is finalized to a retryable state.
                FaultIfOwned(myVersion);
                await InvokeStartFailureFinalizationHookForTestAsync().ConfigureAwait(false);
                throw MapStartException(e);
            }
            finally
            {
                startupScope.Active = false;
                m_activeStartupOwner.Value = previousStartupOwner;
            }
        }

        /// <summary>
        /// Ensures the manager is started. Idempotent lazy first-use entry
        /// point used by the hosted service (eager) and
        /// <see cref="WaitForConnectionAsync"/> (lazy fallback).
        /// </summary>
        /// <param name="ct">A cancellation token.</param>
        public async Task EnsureStartedAsync(CancellationToken ct = default)
        {
            ThrowIfDisposed();

            // Reject a re-entrant start triggered from within this manager's own
            // in-flight startup pipeline (a configuration provider or legacy
            // OnUpdateConfiguration hook that calls back into
            // EnsureStartedAsync/RegisterWaitingConnectionAsync during
            // preparation). The operation-owner marker is set only around this
            // manager's preparation/provider callbacks, so a nested unrelated
            // manager instance (whose own AsyncLocal marker is unset) still
            // starts normally. Failing fast here avoids awaiting this start's
            // own shared m_startTask, which would deadlock. Only an active scope
            // (a genuine synchronous re-entry on the owning flow) rejects; a
            // deferred child task whose originating startup flow already
            // completed observes an inactive scope and starts normally.
            if (m_activeStartupOwner.Value?.Active == true)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidState,
                    "The reverse connect manager startup cannot be re-entered " +
                    "from a configuration provider or update hook.");
            }

            // Retry the lazy first-use decision after awaiting an in-flight
            // reload so a waiter that arrived during a reload's transitional
            // window re-evaluates (returning success once the reload restores
            // Started, or reserving/awaiting a fresh start once it settles)
            // rather than returning premature success.
            while (true)
            {
                Task? inflight;
                Task? activeReload;
                lock (m_lock)
                {
                    if (m_state == ReverseConnectManagerState.Started)
                    {
                        return;
                    }
                    // A stop actively transitioning the manager down is terminal
                    // for a lazy start: rejecting deterministically prevents a
                    // caller (WaitForConnectionAsync/RegisterWaitingConnectionAsync)
                    // from registering an inert waiter against a listener that is
                    // being closed. A completed stop (state Stopped) is restartable
                    // and falls through to reserve a fresh start below.
                    RejectIfStopInProgressLocked();
                    inflight = InFlightStartTaskLocked();
                    activeReload = inflight == null ? ActiveReloadTaskLocked() : null;
                }

                if (inflight != null)
                {
                    await AwaitTrackedStartAsync(inflight, ct).ConfigureAwait(false);
                    return;
                }

                if (activeReload != null)
                {
                    // A reload owns the in-flight transition (Reloading/Stopping/
                    // Starting) with no tracked start task. It restores a coherent
                    // Started state on its own, so await its completion and
                    // re-evaluate rather than returning premature success or
                    // superseding it with a new start.
                    await activeReload.WaitAsync(ct).ConfigureAwait(false);
                    continue;
                }

                // A single shared startup task deduplicates concurrent first-use
                // callers (hosted eager start + lazy WaitForConnectionAsync) so the
                // outcome is deterministic: everyone awaits the same operation and
                // observes the same result.
                Task? startTask;
                lock (m_lock)
                {
                    if (m_state == ReverseConnectManagerState.Started)
                    {
                        return;
                    }
                    RejectIfStopInProgressLocked();
                    // A start became in-flight between the checks above (explicit
                    // or lazy): await it rather than reserving a new one.
                    Task? nowInflight = InFlightStartTaskLocked();
                    if (nowInflight != null)
                    {
                        startTask = nowInflight;
                    }
                    else if (ActiveReloadTaskLocked() is { } reloadNow)
                    {
                        // A reload became in-flight between the checks above: await
                        // it (outside the lock) and re-evaluate.
                        activeReload = reloadNow;
                        startTask = null;
                    }
                    else if (m_state is ReverseConnectManagerState.Preparing
                        or ReverseConnectManagerState.Starting
                        or ReverseConnectManagerState.Reloading
                        or ReverseConnectManagerState.Stopping)
                    {
                        // A lifecycle transition owns the state with no task we
                        // can await (should not normally occur now that starts,
                        // reloads and stops are tracked/rejected above). Reject
                        // deterministically rather than returning premature success.
                        throw new ServiceResultException(
                            StatusCodes.BadInvalidState,
                            "The reverse connect manager lifecycle is in transition.");
                    }
                    else
                    {
                        if (m_initialConfigurationMissing &&
                            m_initialConfigurationFactory == null)
                        {
                            throw new InvalidOperationException(
                                "OpcUaClientOptions.Configuration must be set before starting " +
                                "the reverse connect manager.");
                        }

                        ApplicationConfiguration? initial = m_initialConfiguration;
                        Func<CancellationToken, Task<ApplicationConfiguration>>? initialFactory =
                            m_initialConfigurationFactory;
                        if (!m_initialStartRequested ||
                            (initial == null && initialFactory == null))
                        {
                            // Nothing to auto-start (direct construction or manual start).
                            return;
                        }

                        if (m_startTask != null && m_startTask.IsCompleted)
                        {
                            // The previous shared attempt finished without reaching
                            // Started (failed/superseded). Allow a fresh attempt.
                            // The task is completed, so its token is no longer in
                            // use and can be disposed here safely.
                            m_startTask = null;
                            m_currentStartTask = null;
                            m_startCts?.Dispose();
                            m_startCts = null;
                        }

                        // Reserve the lifecycle version synchronously BEFORE
                        // publishing the shared start task so a concurrent
                        // StopServiceAsync/DisposeAsync (which bumps
                        // m_lifecycleVersion) supersedes this start even before it
                        // reaches provider preparation. The start runs under a
                        // manager-owned token so an individual waiter's cancellation
                        // only cancels its wait, while hosted cancellation
                        // (CancelPendingStart) aborts the whole start.
                        long reservedVersion = ReserveStartLocked();
                        var startCts = new CancellationTokenSource();
                        m_startCts = startCts;
                        // Consume a cancellation latched before the start was
                        // published (hosted StartAsync token cancelled early) and
                        // apply it to the freshly created token BEFORE the shared
                        // task is created, so RunInitialStartAsync observes the
                        // cancellation deterministically and the provider/listener
                        // never binds. The token has no registrations yet, so
                        // cancelling under the lock is safe.
                        if (ConsumeStartCancelLatchLocked())
                        {
                            startCts.Cancel();
                        }
                        m_startTask = RunInitialStartAsync(
                            initial,
                            initialFactory,
                            reservedVersion,
                            startCts.Token);
                        m_currentStartTask = m_startTask;
                        startTask = m_startTask;
                    }
                }

                if (startTask == null)
                {
                    // A reload became in-flight while deciding: await it and
                    // re-evaluate rather than reserving a superseding start.
                    await activeReload!.WaitAsync(ct).ConfigureAwait(false);
                    continue;
                }

                await AwaitTrackedStartAsync(startTask, ct).ConfigureAwait(false);
                return;
            }
        }

        /// <summary>
        /// Rejects a lazy start with <see cref="StatusCodes.BadInvalidState"/>
        /// while a <c>StopServiceAsync</c> is actively transitioning the manager
        /// down, so a caller never registers an inert waiter against a listener
        /// that is being closed. Must be called while holding <see cref="m_lock"/>.
        /// </summary>
        private void RejectIfStopInProgressLocked()
        {
            if (m_stopInProgress)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidState,
                    "The reverse connect manager is stopping.");
            }
        }

        /// <summary>
        /// Returns the in-flight reload task while a configuration reload is
        /// genuinely running, or <c>null</c> otherwise. Must be called while
        /// holding <see cref="m_lock"/>. A completed task is treated as absent.
        /// </summary>
        private Task? ActiveReloadTaskLocked()
        {
            Task? task = m_activeReloadTask;
            if (task == null || task.IsCompleted)
            {
                return null;
            }
            // A stale reload task from a superseded teardown epoch must never be
            // awaited by a lazy EnsureStartedAsync: a committed Stop/Dispose
            // detaches it, so a restart proceeds immediately rather than
            // blocking on a reload that can no longer reach Started.
            if (m_activeReloadEpoch != m_teardownEpoch)
            {
                return null;
            }
            return task;
        }

        /// <summary>
        /// Detaches the in-flight reload drain loop when a committed Stop/Dispose
        /// advances the teardown epoch, so a fresh reload starts a new loop and a
        /// lazy <see cref="EnsureStartedAsync"/> never awaits the superseded one.
        /// Returns the manager-owned reload cancellation source (if any) so the
        /// caller can cancel it OUTSIDE <see cref="m_lock"/>; a cooperative
        /// reload provider/activation then observes cancellation and exits, while
        /// a noncooperative reload's eventual completion is discarded by the
        /// stale loop's epoch guard. Must be called while holding
        /// <see cref="m_lock"/> AFTER the teardown epoch has been advanced.
        /// </summary>
        private CancellationTokenSource? DetachActiveReloadLocked()
        {
            CancellationTokenSource? cts = m_activeReloadCts;
            m_activeReloadCts = null;
            m_activeReloadTask = null;
            m_inFlightReload = null;
            m_reloadLoopActive = false;
            return cts;
        }

        /// <summary>
        /// Cancels a detached reload cancellation source outside
        /// <see cref="m_lock"/> so a cooperative reload provider/activation
        /// exits. The stale reload loop owns disposal, so this only cancels
        /// (guarding against a concurrent dispose in that loop).
        /// </summary>
        private static void CancelDetachedReload(CancellationTokenSource? cts)
        {
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
        }

        /// <summary>
        /// Returns the in-flight start task (explicit or shared lazy) while it
        /// has not yet completed, or <c>null</c> otherwise. Must be called while
        /// holding <see cref="m_lock"/>. A completed tracked task is treated as
        /// absent so a stale (finished) start is never awaited. The task is
        /// returned regardless of the current lifecycle state (including
        /// <see cref="ReverseConnectManagerState.Faulted"/>): a failing start
        /// may set Faulted (via <see cref="FaultIfOwned"/>) before its task
        /// actually completes and publishes its exception, so a concurrent
        /// waiter must still await THIS task rather than return prematurely or
        /// start an overlapping retry. Only a shutdown that explicitly detaches
        /// the tracked task (clearing <see cref="m_currentStartTask"/>) makes it
        /// absent here.
        /// </summary>
        private Task? InFlightStartTaskLocked()
        {
            Task? task = m_currentStartTask;
            if (task == null || task.IsCompleted)
            {
                return null;
            }
            return task;
        }

        /// <summary>
        /// Awaits a tracked start task on behalf of a waiter. The wait honors
        /// the waiter's <paramref name="ct"/> (cancelling only this wait, not
        /// the shared start) and treats a <see cref="StatusCodes.BadInvalidState"/>
        /// failure as success only when the manager actually reached Started (a
        /// benign concurrent-start race); every other failure propagates.
        /// </summary>
        private async Task AwaitTrackedStartAsync(Task startTask, CancellationToken ct)
        {
            try
            {
                await startTask.WaitAsync(ct).ConfigureAwait(false);
            }
            catch (ServiceResultException sre)
                when (sre.StatusCode == StatusCodes.BadInvalidState)
            {
                bool started;
                lock (m_lock)
                {
                    started = m_state == ReverseConnectManagerState.Started;
                }
                if (!started)
                {
                    throw;
                }
            }
        }

        /// <summary>
        /// Disposes a superseded start's manager-owned cancellation token
        /// source, deferring the disposal until the associated start task has
        /// completed so a provider or activation that is still using (or
        /// registering on) the token never observes a disposed source. The
        /// token must already have been cancelled by the caller.
        /// </summary>
        private static void DisposeStartCtsWhenComplete(
            CancellationTokenSource? cts,
            Task? startTask)
        {
            if (cts == null)
            {
                return;
            }
            if (startTask == null || startTask.IsCompleted)
            {
                cts.Dispose();
                return;
            }
            _ = AwaitThenDisposeAsync(cts, startTask);

            static async Task AwaitThenDisposeAsync(
                CancellationTokenSource cts,
                Task startTask)
            {
                try
                {
                    await startTask.ConfigureAwait(false);
                }
                catch
                {
                    // The start's failure is surfaced to its own awaiters; here
                    // we only need it to have stopped using the token before it
                    // is disposed.
                }
                cts.Dispose();
            }
        }

        /// <summary>
        /// Cancels a pending shared lazy start (if any). Used by the hosted
        /// service so a cancelled <c>IHostedService.StartAsync</c> aborts the
        /// underlying reverse-connect startup rather than only the awaiter.
        /// </summary>
        internal void CancelPendingStart()
        {
            CancellationTokenSource? lazyStart;
            CancellationTokenSource? explicitStart;
            lock (m_lock)
            {
                lazyStart = m_startCts;
                explicitStart = m_activeStartCts;
                if (lazyStart == null && explicitStart == null &&
                    m_activeHostedStartGeneration != 0 &&
                    m_state is not ReverseConnectManagerState.Started
                    and not ReverseConnectManagerState.Disposed
                    and not ReverseConnectManagerState.Disposing
                    and not ReverseConnectManagerState.Stopped)
                {
                    // Neither a shared lazy start nor an explicit start has
                    // published its token yet, but a hosted startup generation is
                    // genuinely in flight. Latch the cancellation against THAT
                    // generation so the shared lazy start applies it atomically
                    // when it creates the manager-owned start token
                    // (EnsureStartedAsync) or an explicit start consumes it at
                    // reservation (StartServiceExplicitAsync). An in-flight
                    // explicit start whose token is already visible is cancelled
                    // directly below, so the latch is NOT set. A cancellation
                    // observed with no active hosted startup
                    // (m_activeHostedStartGeneration == 0) never latches, so a
                    // late/stale cancellation after a completed start can never
                    // poison a later start.
                    m_startCancelLatchGeneration = m_activeHostedStartGeneration;
                }
            }
            try
            {
                lazyStart?.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
            try
            {
                explicitStart?.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        /// <summary>
        /// Opens a hosted startup scope and returns its generation. The hosted
        /// service brackets its <c>IHostedService.StartAsync</c> in this scope
        /// so a <see cref="CancelPendingStart"/> that fires before the start
        /// token is published latches against a genuinely active hosted startup
        /// generation rather than an unrelated later start.
        /// </summary>
        internal long BeginHostedStartup()
        {
            lock (m_lock)
            {
                m_activeHostedStartGeneration = ++m_hostedStartGeneration;
                return m_activeHostedStartGeneration;
            }
        }

        /// <summary>
        /// Closes the hosted startup scope identified by
        /// <paramref name="generation"/>. Clears the active generation and any
        /// still-pending cancellation latch tied to it so a latch set during the
        /// scope but never consumed (e.g. because the manager was already
        /// started) does not linger and poison a later start.
        /// </summary>
        internal void EndHostedStartup(long generation)
        {
            lock (m_lock)
            {
                if (m_activeHostedStartGeneration == generation)
                {
                    m_activeHostedStartGeneration = 0;
                }
                if (m_startCancelLatchGeneration == generation)
                {
                    m_startCancelLatchGeneration = 0;
                }
            }
        }

        /// <summary>
        /// Consumes a hosted-start cancellation latch when it targets the
        /// active hosted startup generation. Must be called while holding
        /// <see cref="m_lock"/> at the point a start reserves and publishes its
        /// token. Returns <c>true</c> (and clears the latch) only when a latch
        /// tied to the currently active hosted startup generation is pending, so
        /// a non-hosted lazy start or a stale latch never cancels the start.
        /// </summary>
        private bool ConsumeStartCancelLatchLocked()
        {
            if (m_startCancelLatchGeneration != 0 &&
                m_startCancelLatchGeneration == m_activeHostedStartGeneration)
            {
                m_startCancelLatchGeneration = 0;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Runs the shared initial startup. Yields first so the synchronous
        /// prefix of the prepare/activate pipeline never runs while the caller
        /// of <see cref="EnsureStartedAsync"/> still holds the state lock. The
        /// lifecycle version is reserved synchronously by
        /// <see cref="EnsureStartedAsync"/> before this task is published, so
        /// the yield introduces no supersede race: a Stop/Dispose that runs
        /// between publication and this body observes the already-reserved
        /// version and invalidates it.
        /// </summary>
        private async Task RunInitialStartAsync(
            ApplicationConfiguration? configuration,
            Func<CancellationToken, Task<ApplicationConfiguration>>? configurationFactory,
            long reservedVersion,
            CancellationToken ct)
        {
            await Task.Yield();
            if (configuration == null)
            {
                // Mark this async flow as owning an in-flight startup so a
                // configuration factory (provider) that re-enters
                // EnsureStartedAsync/RegisterWaitingConnectionAsync during the
                // asynchronous initial ApplicationConfiguration factory
                // invocation fails fast instead of self-awaiting this start's
                // own shared task. The marker is set only around the factory
                // call and restored in the finally, so a nested unrelated
                // manager (whose own AsyncLocal marker is unset) still starts
                // and StartServiceCoreAsync (which sets its own marker for the
                // prepare/activate pipeline) is unaffected. The scope's Active
                // flag is cleared in the finally so a child task deferred past
                // this factory invocation (e.g. a provider-spawned Task.Run)
                // that captured the scope reference is not falsely rejected.
                OperationScope? previousStartupOwner = m_activeStartupOwner.Value;
                var startupScope = new OperationScope(
                    Interlocked.Increment(ref m_operationScopeGeneration));
                m_activeStartupOwner.Value = startupScope;
                try
                {
                    // Honor an already-signalled abort (e.g. a hosted StartAsync
                    // or lazy first-use token cancelled before this shared start
                    // ran) BEFORE invoking the factory, so an already-cancelled
                    // startup never runs configuration validation or certificate
                    // creation. The catch below finalizes the reserved Preparing
                    // state so the lifecycle is not stranded.
                    ct.ThrowIfCancellationRequested();

                    configuration = configurationFactory == null
                        ? throw new InvalidOperationException(
                            "No application configuration is available for reverse connect startup.")
                        : await configurationFactory(ct).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    // The factory ran after ReserveStartLocked reserved the
                    // owned Preparing state. A factory throw or cancellation
                    // here must finalize that reservation via the same
                    // FaultIfOwned/MapStartException path used by
                    // StartServiceCoreAsync, otherwise the lifecycle would be
                    // stranded in Preparing and no later start could retry.
                    FaultIfOwned(reservedVersion);
                    await InvokeStartFailureFinalizationHookForTestAsync().ConfigureAwait(false);
                    throw MapStartException(e);
                }
                finally
                {
                    startupScope.Active = false;
                    m_activeStartupOwner.Value = previousStartupOwner;
                }
                // The factory result is intentionally NOT cached into
                // m_initialConfiguration here. Caching it before activation
                // commits would, on an activation failure, leave the (bad)
                // factory-loaded configuration as the cached seed and clear the
                // path back to the factory, so a retry would reuse the failed
                // configuration instead of re-invoking the factory. Keeping the
                // factory active until the commit block below promotes the
                // successfully activated configuration lets a retry reload the
                // current file/config and observe edits that fix the failure.
            }
            else if (string.IsNullOrEmpty(configuration.SourceFilePath))
            {
                // Memory-backed lazy start/restart/retry: the cached seed is
                // handed to the injected provider during preparation, which may
                // mutate its reverse-connect section (endpoints/hold and wait
                // timeouts) in place. Reapply the configured overlay decorator
                // before every start so such an in-place mutation from a
                // previous start never persists across a Stop/restart or a
                // failed-start retry: the decorator rebuilds a fresh
                // ReverseConnectClientConfiguration (fresh endpoints plus the
                // configured hold/wait timeouts) on each attempt, so every start
                // observes clean option endpoints/timeouts rather than the
                // provider-mutated cache. A file-backed seed is not reapplied
                // here: its Stop converts the seed into a reloading factory
                // (RestartFromInitialSourceFileAsync) that already reapplies the
                // decorator to the freshly loaded file, so this path is scoped to
                // the memory-only seed (no SourceFilePath).
                Func<ApplicationConfiguration, ApplicationConfiguration>? decorator;
                lock (m_lock)
                {
                    decorator = m_initialConfigurationDecorator;
                }
                if (decorator != null)
                {
                    configuration = decorator(configuration);
                }
            }
            // The shared start runs under the manager-owned token, not any one
            // caller's: individual callers cancel their wait via WaitAsync(ct)
            // instead, while hosted cancellation aborts this whole start.
            await StartServiceCoreAsync(configuration, null, reservedVersion, ct)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Cancels an in-flight activation's blocked listener open before a
        /// stop/dispose waits on the lifecycle gate. Cancelling the
        /// manager-owned operation token unblocks a listener <c>OpenAsync</c>
        /// that honors cancellation so the gate can be acquired; the pending
        /// shared lazy start (if any) is cancelled too. The lifecycle version
        /// is intentionally NOT bumped here: supersession is only committed once
        /// the stop/dispose actually proceeds (a stop cancelled on the gate wait
        /// must leave an in-flight start untouched). The tokens are only
        /// cancelled here; their owners dispose them.
        /// </summary>
        private void AbortActiveStartupBeforeGate()
        {
            ActiveTransaction? activeTransaction;
            CancellationTokenSource? pendingStart;
            CancellationTokenSource? explicitStart;
            lock (m_lock)
            {
                if (m_state is ReverseConnectManagerState.Disposed
                    or ReverseConnectManagerState.Disposing
                    or ReverseConnectManagerState.Stopped
                    or ReverseConnectManagerState.New)
                {
                    return;
                }
                activeTransaction = m_activeTransaction;
                pendingStart = m_startCts;
                explicitStart = m_activeStartCts;
            }

            activeTransaction?.AbortForShutdown();
            try
            {
                pendingStart?.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
            // Also cancel an in-flight explicit StartServiceAsync token so a
            // cooperative provider blocked in preparation (before it publishes
            // an ActiveTransaction) observes the cancellation and exits, letting
            // the non-cancellable stop proceed. The explicit start owns and
            // disposes this source in its own finally, so it is only cancelled
            // (never disposed) here.
            try
            {
                explicitStart?.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        /// <summary>
        /// Asynchronously stops the reverse connect manager, closing all
        /// active listeners. Usable by the hosting shutdown path. Supersedes
        /// any pending start so it can never bind later.
        /// </summary>
        /// <param name="ct">A cancellation token.</param>
        public async Task StopServiceAsync(CancellationToken ct = default)
        {
            // A stop re-entered directly by this manager's own connection
            // callback must fail fast: the stop drains in-flight callbacks, so
            // it would wait forever for the very callback that called it. A
            // normal external stop from a different async flow never observes
            // the per-flow marker and still drains callbacks.
            ThrowIfInvokedFromConnectionCallback(disposing: false);

            if (Volatile.Read(ref m_disposed) != 0)
            {
                return;
            }

            // Honor an already-signalled cancellation BEFORE aborting any
            // pending startup. A pre-cancelled stop must not cancel the
            // in-flight/pending start (via AbortActiveStartupBeforeGate) and
            // then abandon the stop at the gate wait: that would strand the
            // aborted start without ever completing the stop. Throwing here
            // leaves the shared start untouched so it can continue.
            ct.ThrowIfCancellationRequested();

            // Acquire the lifecycle gate with coherent ownership of any active
            // startup. Two cases:
            //  * A cancellable stop must NOT cancel an in-flight activation
            //    before a cancellable gate wait: a caller cancellation while
            //    queued would abort a start this stop then never completes,
            //    stranding it. So it waits cancellably WITHOUT aborting; on
            //    cancellation the OCE propagates and the in-flight start is left
            //    intact.
            //  * A non-cancellable stop cannot be abandoned, so it will always
            //    complete: it may safely unblock an in-flight cooperative
            //    listener open up front (which holds the gate) so the gate can
            //    be acquired, then waits non-cancellably.
            bool ownsShutdownLatch = false;
            if (ct.CanBeCanceled)
            {
                await m_gate.WaitAsync(ct).ConfigureAwait(false);
            }
            else
            {
                // Set the shutdown latch BEFORE the transaction lookup/gate wait
                // so an activation that already acquired the gate (its
                // ActiveTransaction not yet published) aborts without opening
                // when it checks the latch after gate acquisition. A cancellable
                // stop deliberately does NOT latch: it may be abandoned at its
                // cancellable gate wait and must leave an in-flight activation
                // intact.
                ownsShutdownLatch = TryAcquireShutdownLatch();
                AbortActiveStartupBeforeGate();
                await m_gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            }
            try
            {
                ReloadRequest? supersededReload = null;
                CancellationTokenSource? supersededReloadCts = null;
                CancellationTokenSource? pendingStart;
                Task? pendingStartTask;
                CancellationTokenSource? explicitStart;
                lock (m_lock)
                {
                    if (m_state is ReverseConnectManagerState.Disposed
                        or ReverseConnectManagerState.Disposing
                        or ReverseConnectManagerState.Stopped
                        or ReverseConnectManagerState.New)
                    {
                        return;
                    }
                    m_state = ReverseConnectManagerState.Stopping;
                    // Mark the stop as actively in progress so a lazy
                    // EnsureStartedAsync racing this stop rejects deterministically
                    // (never registering an inert waiter) instead of returning
                    // success. Distinct from the transient Stopping window a
                    // reload passes through, which sets no stop-in-progress flag.
                    m_stopInProgress = true;
                    // Supersede any pending start reserved before the gate so
                    // its ActivateAsync observes the version change and refuses
                    // to bind.
                    m_lifecycleVersion++;
                    // Drop and cancel the shared startup so a start that has not
                    // yet reached provider preparation is invalidated too. Keep
                    // a reference to its task so the token source is disposed
                    // only once the start (which may still register on the
                    // token) has completed.
                    pendingStart = m_startCts;
                    pendingStartTask = m_startTask;
                    m_startCts = null;
                    m_startTask = null;
                    // Capture an in-flight explicit StartServiceAsync so its
                    // token is cancelled below: a cooperative provider then
                    // observes the cancellation and exits, and the start unwinds
                    // rather than committing. The explicit start owns and
                    // disposes its own token source in its finally, so it is only
                    // cancelled - never disposed - here.
                    explicitStart = m_activeStartCts;
                    m_currentStartTask = null;
                }

                // Cancel outside the state lock so cancellation callbacks never
                // run under it, and defer disposal of the token source until the
                // associated start task completes so a provider/start that is
                // still registering on the token never sees a disposed source.
                try
                {
                    pendingStart?.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }
                DisposeStartCtsWhenComplete(pendingStart, pendingStartTask);

                // Cancel an in-flight explicit start (a cooperative provider then
                // observes cancellation and exits). Its token source is not
                // disposed here; the explicit start disposes it once it unwinds.
                try
                {
                    explicitStart?.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }

                List<ReverseConnectInfo> live;
                lock (m_lock)
                {
                    live = [.. m_endpointUrls.Values];
                }

                // Cancel any held OnConnectionWaitingAsync callbacks and wait for
                // every in-flight callback to finish (releasing or handing off its
                // transport) BEFORE the terminal listener close tears the channels
                // down, so a callback can never resolve transport ownership against
                // a listener that is already gone. The waiting-connection
                // registrations are deliberately preserved here: a held callback
                // unblocked by this drain still re-matches them, and only a
                // committed close clears them below. The drain is ended (and the
                // registration token renewed) in the finally so a rolled-back or
                // restarted manager accepts callbacks again. The drain honors the
                // caller token (e.g. a hosted StopAsync deadline): if a still-held
                // external callback that ignores the cancelled hold token blocks
                // the drain past the deadline, restore a coherent Started state
                // (the listeners were never touched and the registrations stay
                // intact) and surface the cancellation rather than tearing the
                // listeners down half way.
                try
                {
                    await DrainConnectionCallbacksAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    RestoreStartedAfterCanceledDrain();
                    throw;
                }

                // Propagate the caller token into the listener close (e.g. a
                // hosted StopAsync deadline). If the close is cancelled, do not
                // silently discard the token and leave the manager half-closed:
                // reopen the running listeners non-cancellably and restore a
                // coherent, retryable Started state so a subsequent stop can
                // complete cleanly, then surface the cancellation. The waiting
                // connection registrations are deliberately NOT cleared before
                // the close commits: a cancelled/rolled-back stop restores
                // Started and must leave the registrations intact so waiters
                // survive; only a committed close clears them below.
                try
                {
                    await CloseHostsHonoringCancellationAsync(live, ct)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    await RestoreStartedAfterCanceledStopAsync(live)
                        .ConfigureAwait(false);
                    throw;
                }

                // The close committed: now clear the waiting connection
                // registrations (waking any waiters) as part of the successful
                // stop.
                ClearWaitingConnections();

                DisposeConfigurationWatcher();

                // The configured listeners are gone for good, so dispose them
                // now that the close has committed (a cancelled/rolled-back stop
                // took the catch above and reopened them instead, so this only
                // runs once the stop is irreversible). Reused manual endpoint
                // hosts are only closed here; they survive for a later restart
                // and are disposed with the manager.
                await CloseAndDisposeOwnedHostsAsync(live, CancellationToken.None)
                    .ConfigureAwait(false);

                lock (m_lock)
                {
                    m_endpointUrls = [];
                    m_configuration = null;
                    // If a file-backed initial configuration seeds lazy restarts,
                    // replace the cached instance with a loader that re-reads its
                    // SourceFilePath at the next EnsureStartedAsync, so a
                    // configuration change made while the manager is stopped is
                    // applied on restart rather than the stale cached
                    // configuration/timestamp. A memory-backed configuration (no
                    // SourceFilePath) keeps its cached restart seed.
                    string? sourceFilePath = m_initialConfiguration?.SourceFilePath;
                    if (m_initialStartRequested && !m_initialConfigurationMissing &&
                        !string.IsNullOrEmpty(sourceFilePath))
                    {
                        m_initialConfigurationSourceFilePath = sourceFilePath;
                        m_initialConfiguration = null;
                        m_initialConfigurationFactory = RestartFromInitialSourceFileAsync;
                    }
                    m_state = ReverseConnectManagerState.Stopped;
                    m_lifecycleVersion++;
                    // A committed stop supersedes every queued reload: advance
                    // the teardown epoch so the drain rejects any reload queued
                    // before this stop, and detach the newest pending request so
                    // it is completed deterministically below (its awaiter never
                    // hangs) instead of reopening a listener after the stop.
                    m_teardownEpoch++;
                    supersededReload = m_pendingReload;
                    m_pendingReload = null;
                    // Detach the in-flight reload drain loop from the prior
                    // epoch: cancel its manager-owned source (below, outside the
                    // lock) so a cooperative reload provider/activation exits,
                    // and reset the reload fields so a fresh reload starts a new
                    // loop and a lazy EnsureStartedAsync never awaits the stale
                    // one. A noncooperative stale reload's eventual completion is
                    // discarded by the loop's epoch guard.
                    supersededReloadCts = DetachActiveReloadLocked();
                }

                CancelDetachedReload(supersededReloadCts);

                if (supersededReload != null)
                {
                    RejectSupersededReload(supersededReload);
                }
            }
            finally
            {
                // Clear the stop-in-progress flag once this stop has committed,
                // rolled back, or bailed out, so a subsequent lazy start (of a
                // Stopped/rolled-back-Started manager) is no longer rejected.
                lock (m_lock)
                {
                    m_stopInProgress = false;
                }
                // End the callback drain and renew the (drain-cancelled)
                // registration token so a rolled-back or restarted manager
                // accepts and holds callbacks again. Safe on the bail-out paths
                // that never started a drain (no signal set, token uncancelled).
                EndCallbackDrain();
                // Clear the latch only if this stop still owns it (a concurrent
                // Dispose may have taken it over permanently) and only after the
                // stop's own state transition committed, so a fresh start queued
                // behind this stop on the gate never observes a stale latch.
                if (ownsShutdownLatch)
                {
                    ReleaseShutdownLatch();
                }
                m_gate.Release();
            }
        }

        /// <summary>
        /// Starts the server application.
        /// </summary>
        /// <param name="configuration">The configuration.</param>
        /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <c>null</c>.</exception>
        /// <exception cref="ServiceResultException">
        /// The manager is already started, or a configured listener endpoint is invalid or could not be opened.
        /// </exception>
        [Obsolete("Use StartServiceAsync(ApplicationConfiguration, CancellationToken) instead.")]
        public void StartService(ApplicationConfiguration configuration)
        {
            // Isolated sync-over-async compatibility bridge. Task.Run moves the
            // whole async operation off any captured synchronization context so
            // a provider that posts continuations back to the caller's context
            // cannot deadlock this blocking wait.
            Task.Run(() => StartServiceAsync(configuration)).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Starts the server application.
        /// </summary>
        /// <param name="configuration">The configuration.</param>
        /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <c>null</c>.</exception>
        /// <exception cref="ServiceResultException">
        /// The manager is already started, or a configured listener endpoint is invalid or could not be opened.
        /// </exception>
        [Obsolete("Use StartServiceAsync(ReverseConnectClientConfiguration, CancellationToken) instead.")]
        public void StartService(ReverseConnectClientConfiguration configuration)
        {
            // Isolated sync-over-async compatibility bridge off any captured
            // synchronization context (see the ApplicationConfiguration overload).
            Task.Run(() => StartServiceAsync(configuration)).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Clears all waiting reverse connection handlers.
        /// </summary>
        public void ClearWaitingConnections()
        {
            lock (m_registrationsLock)
            {
                m_registrations.Clear();
                // Fault every pending WaitForConnectionAsync so a committed stop
                // (or an explicit clear) wakes its waiters promptly rather than
                // leaving them to time out against listeners that are gone.
                AbortActiveWaitsLocked(new ServiceResultException(
                    StatusCodes.BadInvalidState,
                    "The reverse connect manager stopped serving waiting connections."));
                CancelAndRenewTokenSource();
            }
        }

        /// <summary>
        /// Faults every tracked <see cref="WaitForConnectionAsync"/> completion
        /// source with <paramref name="error"/> and clears the tracking list.
        /// Must be called while holding <see cref="m_registrationsLock"/>.
        /// </summary>
        private void AbortActiveWaitsLocked(Exception error)
        {
            if (m_activeWaits.Count == 0)
            {
                return;
            }
            foreach (TaskCompletionSource<ITransportWaitingConnection> wait in m_activeWaits)
            {
                wait.TrySetException(error);
            }
            m_activeWaits.Clear();
        }

        /// <summary>
        /// Helper to wait for a reverse connection.
        /// </summary>
        [Obsolete("Use WaitForConnectionAsync instead.")]
        public Task<ITransportWaitingConnection> WaitForConnection(
            Uri endpointUrl,
            string serverUri,
            CancellationToken ct = default)
        {
            return WaitForConnectionAsync(endpointUrl, serverUri, ct);
        }

        /// <summary>
        /// Helper to wait for a reverse connection.
        /// </summary>
        /// <param name="endpointUrl">The endpoint Url of the reverse connection.</param>
        /// <param name="serverUri">Optional. The server application Uri of the reverse connection.</param>
        /// <param name="ct">A cancellation token.</param>
        /// <exception cref="ArgumentNullException"><paramref name="endpointUrl"/> is <c>null</c>.</exception>
        /// <exception cref="ServiceResultException"></exception>
        public async Task<ITransportWaitingConnection> WaitForConnectionAsync(
            Uri endpointUrl,
            string? serverUri,
            CancellationToken ct = default)
        {
            // Validate the endpoint BEFORE any lazy startup side effects so a
            // null argument never triggers the configuration provider or binds
            // a listener.
            if (endpointUrl == null)
            {
                throw new ArgumentNullException(nameof(endpointUrl));
            }

            // Lazy first-use startup fallback for hosting-less scenarios.
            await EnsureStartedAsync(ct).ConfigureAwait(false);

            var tcs = new TaskCompletionSource<ITransportWaitingConnection>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            // Verify the serving state and insert the registration + track the
            // pending wait atomically under the registrations lock. A concurrent
            // StopServiceAsync/DisposeAsync runs its registration clear/abort
            // under the same lock, so this wait is either inserted before the
            // committed clear (and then faulted by it) or observes the
            // stopping/stopped/disposed state here and is rejected - it can never
            // be stranded as an inert registration behind a committed stop.
            int hashCode = RegisterWaitConnection(endpointUrl, serverUri, tcs);
            try
            {
                await Task.WhenAny([tcs.Task, ListenForCancelAsync(ct)]).ConfigureAwait(false);

                if (!tcs.Task.IsCompleted || tcs.Task.IsCanceled)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadTimeout,
                        "Waiting for the reverse connection timed out.");
                }

                // A matched connection sets the result; a committed
                // Stop/Dispose faults this wait with BadInvalidState. Either is
                // surfaced to the caller here.
                return await tcs.Task.ConfigureAwait(false);
            }
            finally
            {
                UnregisterWait(hashCode, tcs);
            }

            async Task ListenForCancelAsync(CancellationToken ct)
            {
                if (ct == default)
                {
                    int waitTimeout = m_configuration?.WaitTimeout ?? 20000;
                    if (waitTimeout <= 0)
                    {
                        waitTimeout = DefaultWaitTimeout;
                    }
                    await Task.Delay(waitTimeout, ct).ConfigureAwait(false);
                }
                else
                {
                    await Task.Delay(-1, ct).ContinueWith(
                        _ => { },
                        ct,
                        TaskContinuationOptions.None,
                        TaskScheduler.Default).ConfigureAwait(false);
                }
                tcs.TrySetCanceled(ct);
            }
        }

        /// <summary>
        /// Atomically verifies the manager is still serving, inserts a
        /// <see cref="WaitForConnectionAsync"/> registration, and tracks its
        /// completion source, all under <see cref="m_registrationsLock"/> so a
        /// committed stop/dispose (whose clear/abort runs under the same lock)
        /// can never leave an inert waiter behind.
        /// </summary>
        private int RegisterWaitConnection(
            Uri endpointUrl,
            string? serverUri,
            TaskCompletionSource<ITransportWaitingConnection> tcs)
        {
            var registration = new Registration(
                serverUri, endpointUrl, (sender, e) => tcs.TrySetResult(e))
            {
                ReverseConnectStrategy = ReverseConnectStrategy.Once,
                // Tracked wait: the matcher claims the transport by completing
                // this source under m_registrationsLock, atomically with a
                // committed Stop/Dispose that may fault it first.
                Wait = tcs
            };
            lock (m_registrationsLock)
            {
                if (Volatile.Read(ref m_disposed) != 0)
                {
                    throw new ObjectDisposedException(nameof(ReverseConnectManager));
                }
                RejectIfNotServingLocked();
                m_registrations.Add(registration);
                m_activeWaits.Add(tcs);
                CancelAndRenewTokenSource();
            }
            return registration.GetHashCode();
        }

        /// <summary>
        /// Removes a <see cref="WaitForConnectionAsync"/> registration and its
        /// tracked completion source. Idempotent - a registration already
        /// removed (matched or cleared) and an already-untracked wait are both
        /// no-ops.
        /// </summary>
        private void UnregisterWait(
            int hashCode,
            TaskCompletionSource<ITransportWaitingConnection> tcs)
        {
            lock (m_registrationsLock)
            {
                m_activeWaits.Remove(tcs);
                Registration? toRemove = null;
                foreach (Registration registration in m_registrations)
                {
                    if (registration.GetHashCode() == hashCode)
                    {
                        toRemove = registration;
                        break;
                    }
                }
                if (toRemove != null)
                {
                    m_registrations.Remove(toRemove);
                    CancelAndRenewTokenSource();
                }
            }
        }

        /// <summary>
        /// Rejects a waiting-connection registration with
        /// <see cref="StatusCodes.BadInvalidState"/> when the manager has
        /// entered a stop/dispose transition, so no waiter is inserted after a
        /// committed stop. A never-started (New) manager stays a valid
        /// registration store. Must be called while holding
        /// <see cref="m_registrationsLock"/>; reads the lifecycle state under
        /// <see cref="m_lock"/> so the check is coherent with the committed-stop
        /// clear (which runs under the registrations lock).
        /// </summary>
        private void RejectIfNotServingLocked()
        {
            lock (m_lock)
            {
                if (m_state is ReverseConnectManagerState.Stopping
                    or ReverseConnectManagerState.Stopped
                    or ReverseConnectManagerState.Disposing
                    or ReverseConnectManagerState.Disposed)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidState,
                        "The reverse connect manager is stopping or stopped.");
                }
            }
        }

        /// <summary>
        /// Register for a waiting reverse connection.
        /// </summary>
        /// <remarks>
        /// Retained for backward compatibility. When an initial startup is
        /// configured (the DI-lazy scenario) this synchronous overload blocks
        /// on an off-context bridge to <see cref="EnsureStartedAsync"/> so the
        /// configured listeners are bound before the registration is added.
        /// Directly constructed, manually started, or unconfigured managers
        /// remain registration-only. Prefer
        /// <see cref="RegisterWaitingConnectionAsync"/> which starts the
        /// manager without blocking.
        /// </remarks>
        /// <param name="endpointUrl">The endpoint Url of the reverse connection.</param>
        /// <param name="serverUri">Optional. The server application Uri of the reverse connection.</param>
        /// <param name="onConnectionWaiting">The callback</param>
        /// <param name="reverseConnectStrategy">The reverse connect callback strategy.</param>
        /// <exception cref="ArgumentNullException"><paramref name="endpointUrl"/> is <c>null</c>.</exception>
        [Obsolete("Use RegisterWaitingConnectionAsync instead. The synchronous overload " +
            "blocks to start the manager in DI-lazy scenarios and is retained for compatibility.")]
        public int RegisterWaitingConnection(
            Uri endpointUrl,
            string? serverUri,
            EventHandler<ConnectionWaitingEventArgs> onConnectionWaiting,
            ReverseConnectStrategy reverseConnectStrategy)
        {
            if (endpointUrl == null)
            {
                throw new ArgumentNullException(nameof(endpointUrl));
            }

            // DI-lazy compatibility: a synchronous registration must start the
            // configured manager before registering so its listeners bind. The
            // start runs off the caller's synchronization context to avoid a
            // deadlock if that context is captured (see StartService/Dispose).
            bool verifyServing;
            if (RequiresLazyInitialStart(out verifyServing))
            {
                Task.Run(() => EnsureStartedAsync()).GetAwaiter().GetResult();
            }

            Func<Task>? beforeRegister = BeforeRegisterWaitForTest;
            if (beforeRegister != null)
            {
                Task.Run(beforeRegister).GetAwaiter().GetResult();
            }

            return RegisterWaitingConnectionCore(
                endpointUrl,
                serverUri,
                onConnectionWaiting,
                reverseConnectStrategy,
                verifyServing);
        }

        /// <summary>
        /// Adds a waiting reverse connection registration without starting the
        /// manager. Shared by the public registration entry points.
        /// </summary>
        private int RegisterWaitingConnectionCore(
            Uri endpointUrl,
            string? serverUri,
            EventHandler<ConnectionWaitingEventArgs> onConnectionWaiting,
            ReverseConnectStrategy reverseConnectStrategy,
            bool verifyServing = false)
        {
            var registration = new Registration(serverUri, endpointUrl, onConnectionWaiting)
            {
                ReverseConnectStrategy = reverseConnectStrategy
            };
            lock (m_registrationsLock)
            {
                // Atomically reject once disposal has begun so no callback or
                // renewed CTS can be installed after teardown. The disposed
                // flag is set synchronously when disposal begins and the CTS
                // teardown runs under this same lock, so a registration either
                // observes the not-disposed state and mutates safely (the
                // teardown disposes whatever CTS it later finds) or observes
                // disposal and throws WITHOUT mutating registrations or the CTS.
                if (Volatile.Read(ref m_disposed) != 0)
                {
                    throw new ObjectDisposedException(nameof(ReverseConnectManager));
                }
                if (verifyServing)
                {
                    // Reject a registration that raced a committed stop between
                    // the caller's EnsureStartedAsync and this insertion. The
                    // committed stop's registration clear runs under this same
                    // lock, so the check is coherent and never leaves an inert
                    // waiter behind.
                    RejectIfNotServingLocked();
                }
                m_registrations.Add(registration);
                CancelAndRenewTokenSource();
            }
            return registration.GetHashCode();
        }

        /// <summary>
        /// Whether a synchronous registration must lazily start the manager:
        /// an initial startup is configured (present and not missing) and the
        /// manager is not already started.
        /// </summary>
        private bool RequiresLazyInitialStart(out bool verifyServing)
        {
            lock (m_lock)
            {
                verifyServing = m_initialStartRequested &&
                    !(m_initialConfigurationMissing &&
                        m_initialConfigurationFactory == null) &&
                    (m_initialConfiguration != null ||
                        m_initialConfigurationFactory != null);
                return verifyServing &&
                    m_state != ReverseConnectManagerState.Started;
            }
        }

        /// <summary>
        /// Registers for a waiting reverse connection, ensuring the manager
        /// is started first (lazy fallback).
        /// </summary>
        /// <param name="endpointUrl">The endpoint Url of the reverse connection.</param>
        /// <param name="serverUri">Optional. The server application Uri of the reverse connection.</param>
        /// <param name="onConnectionWaiting">The callback</param>
        /// <param name="reverseConnectStrategy">The reverse connect callback strategy.</param>
        /// <param name="ct">A cancellation token.</param>
        /// <exception cref="ArgumentNullException"><paramref name="endpointUrl"/> is <c>null</c>.</exception>
        public async Task<int> RegisterWaitingConnectionAsync(
            Uri endpointUrl,
            string? serverUri,
            EventHandler<ConnectionWaitingEventArgs> onConnectionWaiting,
            ReverseConnectStrategy reverseConnectStrategy,
            CancellationToken ct = default)
        {
            if (endpointUrl == null)
            {
                throw new ArgumentNullException(nameof(endpointUrl));
            }

            await EnsureStartedAsync(ct).ConfigureAwait(false);

            // Deterministic test seam: lets a test commit a concurrent
            // stop/dispose in the window between the start above and the
            // insertion below to prove the serving verification rejects it.
            Func<Task>? beforeRegister = BeforeRegisterWaitForTest;
            if (beforeRegister != null)
            {
                await beforeRegister().ConfigureAwait(false);
            }

            // Verify the manager is still serving and insert the registration
            // atomically under m_registrationsLock (with the same
            // m_registrationsLock + m_lock ordering as RegisterWaitConnection).
            // A StopServiceAsync/DisposeAsync that commits between the start
            // above and this insertion runs its registration clear/abort under
            // the same lock, so this registration is either inserted while
            // still serving or rejected here - it can never be stranded as an
            // inert waiter behind a committed stop.
            return RegisterWaitingConnectionCore(
                endpointUrl,
                serverUri,
                onConnectionWaiting,
                reverseConnectStrategy,
                verifyServing: true);
        }

        /// <summary>
        /// Unregister reverse connection callback.
        /// </summary>
        /// <param name="hashCode">The hashcode returned by the registration.</param>
        public void UnregisterWaitingConnection(int hashCode)
        {
            lock (m_registrationsLock)
            {
                Registration? toRemove = null;
                foreach (Registration registration in m_registrations)
                {
                    if (registration.GetHashCode() == hashCode)
                    {
                        toRemove = registration;
                        break;
                    }
                }
                if (toRemove != null)
                {
                    m_registrations.Remove(toRemove);
                    CancelAndRenewTokenSource();
                }
            }
        }

        /// <summary>
        /// Configures the initial startup configuration applied by
        /// <see cref="EnsureStartedAsync"/>. Used by the DI activator to
        /// defer the blocking start out of the factory.
        /// </summary>
        /// <param name="configuration">The application configuration to start with.</param>
        internal void ConfigureInitialStartup(ApplicationConfiguration configuration)
        {
            ConfigureInitialStartup(configuration, reloadDecorator: null);
        }

        /// <summary>
        /// Configures the initial startup configuration applied by
        /// <see cref="EnsureStartedAsync"/>, together with a decorator that
        /// reapplies the configured overlay (the DI
        /// <see cref="ClientReverseConnectOptions"/> reverse-connect endpoints)
        /// onto a configuration reloaded from <c>SourceFilePath</c> after a
        /// stop. The decorator is retained so a file-backed lazy restart
        /// preserves the overlay instead of losing it to a plain file load.
        /// </summary>
        /// <param name="configuration">The application configuration to start with.</param>
        /// <param name="reloadDecorator">
        /// An optional overlay decorator applied to a configuration reloaded
        /// from <c>SourceFilePath</c> before activation. May be <c>null</c>
        /// when no overlay is configured.
        /// </param>
        internal void ConfigureInitialStartup(
            ApplicationConfiguration configuration,
            Func<ApplicationConfiguration, ApplicationConfiguration>? reloadDecorator)
        {
            m_initialConfiguration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            m_initialConfigurationFactory = null;
            m_initialConfigurationDecorator = reloadDecorator;
            m_initialStartRequested = true;
            m_initialConfigurationMissing = false;
        }

        /// <summary>
        /// Configures asynchronous initial startup from an application
        /// configuration provider.
        /// </summary>
        internal void ConfigureInitialStartup(
            Func<CancellationToken, Task<ApplicationConfiguration>> configurationFactory,
            Func<ApplicationConfiguration, ApplicationConfiguration>? reloadDecorator = null)
        {
            m_initialConfigurationFactory =
                configurationFactory ?? throw new ArgumentNullException(nameof(configurationFactory));
            m_initialConfiguration = null;
            m_initialConfigurationDecorator = reloadDecorator;
            m_initialStartRequested = true;
            m_initialConfigurationMissing = false;
        }

        /// <summary>
        /// Marks that an initial startup was requested but no application
        /// configuration is available. The missing configuration is surfaced
        /// during <see cref="EnsureStartedAsync"/> rather than at resolution.
        /// </summary>
        internal void MarkInitialConfigurationMissing()
        {
            m_initialStartRequested = true;
            m_initialConfigurationMissing = true;
        }

        /// <summary>
        /// Reloads a file-backed initial configuration from its captured
        /// <c>SourceFilePath</c> for a lazy restart after a stop, so any change
        /// made to the file while the manager was stopped is applied rather than
        /// a stale cached configuration. Uses the injectable loader seam when set
        /// (tests) and otherwise <see cref="ApplicationConfiguration.LoadAsync(FileInfo, ApplicationType, Type, ITelemetryContext, CancellationToken)"/>.
        /// The configured overlay decorator (the DI
        /// <see cref="ClientReverseConnectOptions"/> reverse-connect endpoints)
        /// is reapplied to the freshly loaded configuration before it is
        /// returned, so a file-backed restart keeps the DI reverse-connect
        /// option overlay instead of losing it to a plain file load. The
        /// injected provider preparation still runs during activation
        /// (<see cref="PrepareAsync"/>).
        /// </summary>
        private async Task<ApplicationConfiguration> RestartFromInitialSourceFileAsync(
            CancellationToken ct)
        {
            string? sourceFilePath;
            ApplicationType applicationType;
            Func<ApplicationConfiguration, ApplicationConfiguration>? decorator;
            lock (m_lock)
            {
                sourceFilePath = m_initialConfigurationSourceFilePath;
                applicationType = m_applicationType;
                decorator = m_initialConfigurationDecorator;
            }
            if (string.IsNullOrEmpty(sourceFilePath))
            {
                throw new InvalidOperationException(
                    "No configuration source file is available for reverse connect restart.");
            }

            Func<string, ApplicationType, Type?, CancellationToken,
                Task<ApplicationConfiguration>>? loader = ConfigurationFileLoaderForTest;
            ApplicationConfiguration loaded = loader != null
                ? await loader(sourceFilePath!, applicationType, m_configType, ct)
                    .ConfigureAwait(false)
                : await ApplicationConfiguration.LoadAsync(
                    new FileInfo(sourceFilePath!),
                    applicationType,
                    m_configType,
                    m_telemetry,
                    ct).ConfigureAwait(false);

            // Reapply the DI reverse-connect option overlay onto the freshly
            // loaded file base so the restart uses the updated file contents
            // plus the configured overlay endpoints. Without this the overlay
            // (which lives only in memory) would be lost across a stop/restart.
            return decorator != null ? decorator(loaded) : loaded;
        }

        /// <summary>
        /// The current configuration-watcher generation. Exposed internally
        /// so tests can drive <see cref="ReloadConfigurationAsync"/> directly.
        /// </summary>
        internal int CurrentWatcherGeneration
        {
            get
            {
                lock (m_lock)
                {
                    return m_watcherGeneration;
                }
            }
        }

        /// <summary>
        /// The number of active waiting-connection registrations. Exposed
        /// internally so tests can assert that a cancelled/rolled-back stop
        /// leaves registrations intact while a committed stop clears them.
        /// </summary>
        internal int WaitingConnectionCountForTest
        {
            get
            {
                lock (m_registrationsLock)
                {
                    return m_registrations.Count;
                }
            }
        }

        /// <summary>
        /// Inserts a tracked <see cref="WaitForConnectionAsync"/> registration
        /// and returns its completion source so a test can drive the
        /// match-vs-stop claim race deterministically (completing/faulting the
        /// source directly) without a real listener. Mirrors the registration
        /// that <see cref="WaitForConnectionAsync"/> installs.
        /// </summary>
        internal TaskCompletionSource<ITransportWaitingConnection> RegisterWaitForTest(
            Uri endpointUrl,
            string? serverUri)
        {
            var tcs = new TaskCompletionSource<ITransportWaitingConnection>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            RegisterWaitConnection(endpointUrl, serverUri, tcs);
            return tcs;
        }

        /// <summary>
        /// Runs the internal waiting-connection matcher against a supplied event
        /// and returns whether a registration claimed it. Exposed internally so
        /// a test can prove the claim (<c>TrySetResult</c> under the
        /// registrations lock) is atomic with a committed Stop/Dispose and never
        /// leaves an accepted orphan.
        /// </summary>
        internal bool MatchWaitingConnectionForTest(ConnectionWaitingEventArgs e)
        {
            return MatchRegistration(this, e);
        }

        /// <summary>
        /// Invokes the internal <see cref="OnConnectionWaitingAsync"/> callback
        /// against a supplied waiting-connection event, exactly as a listener
        /// would. Exposed internally so a test can drive a held callback (one
        /// with no matching registration, parked on its hold delay) and prove a
        /// concurrent Stop/Dispose cancels and drains it before the terminal
        /// listener close/dispose.
        /// </summary>
        internal Task InvokeConnectionWaitingForTest(ConnectionWaitingEventArgs e)
        {
            return OnConnectionWaitingAsync(this, e);
        }

        /// <summary>
        /// Enters the connection-callback tracking region exactly as
        /// <see cref="OnConnectionWaitingAsync"/> does, then blocks on the
        /// supplied gate while ignoring the cancelled hold token. Exposed
        /// internally so a test can simulate an external callback that holds a
        /// transport and does not observe the drain cancellation, proving a
        /// cancellable Stop cancels at its deadline while such a callback is
        /// still held - leaving the manager Started with its listeners open and
        /// the callback coherently tracked - and that the callback resolves and a
        /// subsequent non-cancellable Stop then completes. Returns immediately
        /// (without entering) if a shutdown drain is already rejecting new
        /// callbacks.
        /// </summary>
        internal async Task InvokeBlockingConnectionCallbackForTest(Task gate)
        {
            if (!TryEnterConnectionCallback())
            {
                return;
            }
            OperationScope? previousCallbackOwner = m_activeCallbackOwner.Value;
            var callbackScope = new OperationScope(
                Interlocked.Increment(ref m_operationScopeGeneration));
            m_activeCallbackOwner.Value = callbackScope;
            try
            {
                await gate.ConfigureAwait(false);
            }
            finally
            {
                callbackScope.Active = false;
                m_activeCallbackOwner.Value = previousCallbackOwner;
                ExitConnectionCallback();
            }
        }

        /// <summary>
        /// The number of in-flight <see cref="OnConnectionWaitingAsync"/>
        /// callbacks. Exposed internally so a test can assert that a callback is
        /// being tracked (holding a transport) and that a Stop/Dispose drains it
        /// to zero before tearing the listeners down.
        /// </summary>
        internal int ActiveConnectionCallbackCountForTest
        {
            get
            {
                lock (m_callbackLock)
                {
                    return m_activeCallbacks;
                }
            }
        }

        /// <summary>
        /// The currently active (committed) application configuration. Exposed
        /// internally so tests can assert that a candidate application
        /// configuration is only promoted on a successful activation.
        /// </summary>
        internal ApplicationConfiguration? ActiveApplicationConfigurationForTest
        {
            get
            {
                lock (m_lock)
                {
                    return m_appConfig;
                }
            }
        }

        /// <summary>
        /// The current lifecycle state name. Exposed internally so tests can
        /// assert lifecycle transitions without reflection.
        /// </summary>
        internal string CurrentStateForTest
        {
            get
            {
                lock (m_lock)
                {
                    return m_state.ToString();
                }
            }
        }

        /// <summary>
        /// Reloads the manager from a changed application configuration.
        /// </summary>
        /// <remarks>
        /// Invoked by the configuration watcher adapter. Failures are logged
        /// and swallowed. A failed pre-stop reload leaves the previously
        /// started service intact; a failure after stopping restores the
        /// previous listeners.
        /// </remarks>
        /// <param name="configuration">The reloaded configuration.</param>
        /// <param name="watcherGeneration">The watcher generation that raised the reload.</param>
        /// <param name="ct">A cancellation token.</param>
        internal Task ReloadConfigurationAsync(
            ApplicationConfiguration configuration,
            int watcherGeneration,
            CancellationToken ct = default)
        {
            if (Volatile.Read(ref m_disposed) != 0)
            {
                return Task.CompletedTask;
            }

            ReloadRequest request;
            ReloadRequest? superseded;
            lock (m_lock)
            {
                // Reject a callback from a superseded (stale) watcher generation
                // at intake. Overlapping reloads that arrive before a sibling
                // reload commits still share the current generation here and are
                // therefore accepted; the queue/loop below then applies the
                // newest one even though a sibling's commit later advances the
                // generation. Moving the check here (rather than re-checking the
                // moving generation inside the loop) is what lets an older
                // commit no longer invalidate/drop a newer queued reload.
                if (watcherGeneration != m_watcherGeneration)
                {
                    return Task.CompletedTask;
                }

                // Capture the lifecycle version, teardown epoch and watcher
                // generation at enqueue time. The serialized drain rejects this
                // request without applying if a committed Stop/Dispose advanced
                // the teardown epoch after the request was queued, so a reload
                // queued before a shutdown never reopens a listener. A sibling
                // reload commit only advances the lifecycle version (not the
                // teardown epoch), so an overlapping newer reload queued in the
                // same epoch is still applied (latest-wins) rather than dropped.
                request = new ReloadRequest(
                    configuration,
                    watcherGeneration,
                    m_lifecycleVersion,
                    m_teardownEpoch,
                    ct);

                // Publish this request as the newest pending candidate. Any
                // not-yet-started pending request is coalesced away (latest
                // wins) and completed once this newest request completes. A
                // single serialized loop drains the queue outside the lifecycle
                // gate so overlapping reloads never run concurrently and the
                // newest candidate is always applied last.
                superseded = m_pendingReload;
                m_pendingReload = request;
                if (!m_reloadLoopActive)
                {
                    m_reloadLoopActive = true;
                    // RunReloadLoopAsync yields before touching state so
                    // publishing the loop task under the lock never runs the
                    // loop body while the lock is held. The loop task is
                    // exposed as the active-reload task so a lazy
                    // EnsureStartedAsync that races a reload's transitional
                    // window awaits it rather than returning premature success.
                    // The loop runs under a manager-owned cancellation source
                    // tagged with the current teardown epoch: a committed
                    // Stop/Dispose cancels it (so a cooperative reload provider
                    // exits) and advances the epoch (so a stale/noncooperative
                    // reload completion is discarded and never restarts).
                    var reloadCts = new CancellationTokenSource();
                    m_activeReloadCts = reloadCts;
                    m_activeReloadEpoch = m_teardownEpoch;
                    m_activeReloadTask = RunReloadLoopAsync(reloadCts, m_teardownEpoch);
                }
            }

            if (superseded != null)
            {
                LinkSupersededReload(superseded, request);
            }

            return request.Completion.Task;
        }

        /// <summary>
        /// Completes a coalesced (superseded, never-run) reload request once the
        /// newest request that replaced it completes, so its awaiter observes
        /// the latest-wins outcome.
        /// </summary>
        private static void LinkSupersededReload(ReloadRequest superseded, ReloadRequest winner)
        {
            _ = CompleteSupersededReloadAsync(superseded, winner);

            static async Task CompleteSupersededReloadAsync(
                ReloadRequest superseded,
                ReloadRequest winner)
            {
                try
                {
                    await winner.Completion.Task.ConfigureAwait(false);
                }
                catch
                {
                    // The winner's failures are logged/swallowed by the core; the
                    // superseded awaiter only needs to observe completion.
                }
                superseded.Completion.TrySetResult(true);
            }
        }

        /// <summary>
        /// Drains queued reload requests one at a time (latest-wins). Yields
        /// first so the synchronous prefix never runs while the caller of
        /// <see cref="ReloadConfigurationAsync"/> still holds the state lock.
        /// Runs outside the lifecycle gate; each request enters the gate through
        /// <see cref="ActivateAsync"/>, so overlapping reloads are serialized
        /// and the newest queued candidate is applied last.
        /// </summary>
        /// <param name="reloadCts">
        /// The manager-owned cancellation source for this loop, cancelled by a
        /// committed Stop/Dispose so a cooperative reload provider/activation
        /// exits. Owned here: disposed once the loop ends.
        /// </param>
        /// <param name="epoch">
        /// The teardown epoch captured when this loop was created. A committed
        /// Stop/Dispose advances the epoch and detaches the loop, so a stale
        /// loop observing the mismatch exits without touching reload state a
        /// newer loop now owns and without restarting.
        /// </param>
        private async Task RunReloadLoopAsync(CancellationTokenSource reloadCts, long epoch)
        {
            await Task.Yield();
            try
            {
                while (true)
                {
                    ReloadRequest request;
                    bool superseded;
                    lock (m_lock)
                    {
                        // A committed Stop/Dispose advanced the teardown epoch
                        // and detached this loop (cancelling its source and
                        // reassigning the reload fields to a fresh loop or
                        // clearing them). Exit WITHOUT clearing those fields so a
                        // newer loop is never clobbered; any stale reload result
                        // this loop just produced is discarded and never
                        // restarts the drain.
                        if (epoch != m_teardownEpoch)
                        {
                            return;
                        }
                        if (m_pendingReload == null)
                        {
                            m_reloadLoopActive = false;
                            m_activeReloadTask = null;
                            m_activeReloadCts = null;
                            m_inFlightReload = null;
                            return;
                        }
                        request = m_pendingReload;
                        m_pendingReload = null;
                        // Publish the dequeued request as the in-flight reload so a
                        // cancelled stop that rolls back to Started can re-queue this
                        // candidate if its lifecycle version is superseded.
                        m_inFlightReload = request;
                        // Reject a request whose captured teardown epoch no longer
                        // matches the current one: a committed Stop/Dispose
                        // superseded it after it was queued, so it must never
                        // (re)open a listener. A sibling reload commit does not
                        // advance the teardown epoch, so an overlapping newer reload
                        // queued in the same epoch is still applied here
                        // (latest-wins).
                        superseded = request.TeardownEpoch != m_teardownEpoch ||
                            Volatile.Read(ref m_disposed) != 0;
                    }

                    if (superseded)
                    {
                        RejectSupersededReload(request);
                        continue;
                    }

                    try
                    {
                        await ReloadConfigurationCoreAsync(
                            request.Configuration,
                            reloadCts.Token,
                            request.CancellationToken).ConfigureAwait(false);
                    }
                    finally
                    {
                        request.Completion.TrySetResult(true);
                    }
                }
            }
            finally
            {
                // Single owner of the manager-owned reload source: dispose it
                // once the loop ends (normal drain or supersession). A committed
                // Stop/Dispose only cancels it, guarding against the race here.
                reloadCts.Dispose();
            }
        }

        /// <summary>
        /// Completes a queued reload request that a superseding Stop/Dispose (or
        /// a disposed manager) rejected without applying, so its awaiter never
        /// hangs. A request whose own cancellation token fired completes as
        /// canceled; otherwise it faults with <see cref="StatusCodes.BadInvalidState"/>
        /// to mirror the supersession surfaced by the activation path.
        /// </summary>
        private void RejectSupersededReload(ReloadRequest request)
        {
            m_logger.ReverseConnectConfigurationReloadSuperseded(
                request.WatcherGeneration,
                request.LifecycleVersion);

            if (request.CancellationToken.IsCancellationRequested)
            {
                request.Completion.TrySetCanceled(request.CancellationToken);
            }
            else
            {
                request.Completion.TrySetException(new ServiceResultException(
                    StatusCodes.BadInvalidState,
                    "Reverse connect configuration reload superseded by a shutdown."));
            }
        }

        /// <summary>
        /// Runs the reload prepare/activate pipeline for a single queued
        /// request. Failures are logged and swallowed. Lifecycle supersession
        /// (a concurrent stop/dispose) is enforced by the lifecycle version and
        /// shutdown latch inside <see cref="ActivateAsync"/>, not by the watcher
        /// generation, so a newer queued reload is never dropped merely because
        /// an older sibling reload committed and advanced the generation.
        /// </summary>
        /// <param name="configuration">The reloaded configuration to apply.</param>
        /// <param name="managerToken">
        /// The manager-owned reload token, cancelled by a committed Stop/Dispose
        /// so a cooperative provider/activation running under this reload exits.
        /// </param>
        /// <param name="requestToken">The token supplied by the reload request caller.</param>
        private async Task ReloadConfigurationCoreAsync(
            ApplicationConfiguration configuration,
            CancellationToken managerToken,
            CancellationToken requestToken)
        {
            if (Volatile.Read(ref m_disposed) != 0)
            {
                return;
            }

            long myVersion;
            lock (m_lock)
            {
                myVersion = m_lifecycleVersion;
            }

            // Link the caller-supplied token with the manager-owned reload token
            // so a committed Stop/Dispose that cancels the reload source unblocks
            // a cooperative provider/listener open just like the caller token.
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                managerToken,
                requestToken);
            CancellationToken ct = linkedCts.Token;

            PreparedConfiguration? prepared = null;
            try
            {
                prepared = await PrepareAsync(configuration, null, ct).ConfigureAwait(false);

                await ActivateAsync(prepared, myVersion, isReload: true, ct)
                    .ConfigureAwait(false);
                prepared = null;
            }
            catch (Exception e)
            {
                if (prepared != null)
                {
                    await CleanupCandidateAsync(prepared).ConfigureAwait(false);
                }
                m_logger.ReverseConnectConfigurationReloadFailed(
                    e,
                    configuration.SourceFilePath ?? "<memory>");
            }
        }

        /// <summary>
        /// Prepares a candidate configuration outside the lifecycle gate.
        /// Runs the legacy adaptation hooks, then the async provider, then
        /// creates the (unbound) candidate hosts. All application-config,
        /// certificate and type metadata is kept operation-local in the
        /// returned <see cref="PreparedConfiguration"/> and is never promoted
        /// to instance state here.
        /// </summary>
        [UnconditionalSuppressMessage("Trimming", "IL2072",
            Justification = "The configuration type was loaded with PublicParameterlessConstructor, so " +
            "GetType() is safe to store into the PublicConstructors-annotated ConfigType.")]
        private async Task<PreparedConfiguration> PrepareAsync(
            ApplicationConfiguration? appConfig,
            ReverseConnectClientConfiguration? rccConfig,
            CancellationToken ct)
        {
            // Seed candidate: for an application configuration this is its
            // reverse-connect section; for a bare configuration it is the
            // supplied instance. The legacy hook may mutate/replace it.
            ReverseConnectClientConfiguration seed = appConfig != null
                ? (appConfig.ClientConfiguration?.ReverseConnect
                    ?? new ReverseConnectClientConfiguration())
                : (rccConfig ?? new ReverseConnectClientConfiguration());

            // Validate the source file path up front without starting a polling
            // watcher and capture its last-write time so a modification between
            // now and commit can be detected and re-applied.
            string? sourceFilePath = appConfig?.SourceFilePath;
            DateTime? sourceFileLastWriteUtc = null;
            if (!string.IsNullOrEmpty(sourceFilePath))
            {
                var fileInfo = new FileInfo(sourceFilePath!);
                if (!fileInfo.Exists)
                {
                    throw new FileNotFoundException(
                        "Could not load configuration file",
                        sourceFilePath);
                }
                sourceFileLastWriteUtc = fileInfo.LastWriteTimeUtc;
            }

            var context = new LegacyCaptureContext(seed);
            LegacyCaptureContext? previous = m_legacyCapture.Value;
            m_legacyCapture.Value = context;
            try
            {
#pragma warning disable CS0618 // legacy adaptation hooks are intentionally invoked
                if (appConfig != null)
                {
                    OnUpdateConfiguration(appConfig);
                }
                else
                {
                    OnUpdateConfiguration(rccConfig ?? new ReverseConnectClientConfiguration());
                }
#pragma warning restore CS0618
            }
            finally
            {
                m_legacyCapture.Value = previous;
            }

            // Omitting the base call no longer suppresses the update: fall back
            // to the seed (which an override may have mutated in place) so
            // endpoint replacement/customization keeps working.
            ReverseConnectClientConfiguration adapted = context.Captured
                ? (context.Configuration ?? seed)
                : seed;

            IReverseConnectConfigurationProvider provider =
                ConfigurationProvider ?? DefaultReverseConnectConfigurationProvider.Instance;
            ReverseConnectClientConfiguration effective =
                await provider.ConfigureAsync(appConfig, adapted, ct).ConfigureAwait(false)
                ?? new ReverseConnectClientConfiguration();

            // Operation-local application/type metadata snapshot.
            ApplicationType applicationType = appConfig?.ApplicationType ?? default;
            Type? configType = appConfig?.GetType();

            (Dictionary<Uri, ReverseConnectInfo> hosts, List<ReverseConnectInfo> owned) =
                await BuildCandidateHostsAsync(effective, appConfig).ConfigureAwait(false);
            return new PreparedConfiguration(
                effective,
                appConfig,
                applicationType,
                configType,
                hosts,
                owned,
                sourceFilePath,
                sourceFileLastWriteUtc);
        }

        /// <summary>
        /// Validates configured endpoint URLs and creates the candidate host
        /// set. Manual endpoints are reused by object identity; configured
        /// endpoints are created fresh and tracked as owned. A configured URL
        /// that collides with a manual endpoint is resolved deterministically
        /// by reusing the manual host (no double bind, no leak).
        /// </summary>
        private async Task<(Dictionary<Uri, ReverseConnectInfo> Hosts, List<ReverseConnectInfo> Owned)>
            BuildCandidateHostsAsync(
                ReverseConnectClientConfiguration effective,
                ApplicationConfiguration? appConfig)
        {
            var configuredEndpointUrls = new List<Uri>();
            var uniqueEndpointUrls = new HashSet<Uri>();
            if (!effective.ClientEndpoints.IsNull)
            {
                foreach (ReverseConnectClientEndpoint endpoint in effective.ClientEndpoints)
                {
                    string? endpointUrl = endpoint.EndpointUrl;
                    Uri? uri = Utils.ParseUri(endpointUrl);
                    if (uri == null)
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadTcpEndpointUrlInvalid,
                            "Invalid reverse connect listener endpoint URL: {0}.",
                            endpointUrl ?? "<null>");
                    }
                    ValidateEndpointUrl(uri);
                    if (uniqueEndpointUrls.Add(uri))
                    {
                        configuredEndpointUrls.Add(uri);
                    }
                }
            }

            var manualSnapshot = new List<KeyValuePair<Uri, ReverseConnectInfo>>();
            lock (m_lock)
            {
                foreach (KeyValuePair<Uri, ReverseConnectInfo> manual in m_manualEndpoints)
                {
                    manualSnapshot.Add(manual);
                }
            }

            var candidate = new Dictionary<Uri, ReverseConnectInfo>();
            foreach (KeyValuePair<Uri, ReverseConnectInfo> manual in manualSnapshot)
            {
                // A persistent manual host whose listener was destroyed by a
                // failed destructive close is unusable (OpenAsync would reject
                // it), so reuse it by identity only while it is usable and
                // otherwise recreate it before this activation reopens it.
                candidate[manual.Key] = await ReuseOrRecreateManualHostAsync(
                    manual.Key,
                    manual.Value).ConfigureAwait(false);
            }

            var owned = new List<ReverseConnectInfo>();
            try
            {
                foreach (Uri endpointUrl in configuredEndpointUrls)
                {
                    if (candidate.ContainsKey(endpointUrl))
                    {
                        // Collides with a reused manual endpoint: keep the
                        // manual host, do not create a second listener for the
                        // same URI.
                        continue;
                    }
                    ReverseConnectInfo info = CreateEndpointInfo(endpointUrl, true, appConfig);
                    owned.Add(info);
                    candidate[endpointUrl] = info;
                }
            }
            catch
            {
                // Every entry in owned is a freshly created configured candidate
                // (ConfigEntry == true) that never opened - including ones
                // created successfully before a later CreateEndpointInfo threw,
                // which hold an unopened listener and would otherwise leak.
                // Disposing each host closes and releases its listener exactly
                // once (its DisposeAsync closes before disposing), so no explicit
                // pre-close is needed; reused manual hosts are never in owned, so
                // their ownership is preserved.
                await CloseAndDisposeOwnedHostsAsync(owned, CancellationToken.None)
                    .ConfigureAwait(false);
                throw;
            }

            return (candidate, owned);
        }

        /// <summary>
        /// Serializes the stop/open/commit/restore critical section under the
        /// async lifecycle gate. The configuration watcher is created and
        /// swapped only at commit so no polling starts before activation.
        /// </summary>
        [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
            Justification = "The commit watcher is disposed on the failure path and its ownership " +
            "is transferred to SwapWatcher on successful activation.")]
        private async Task ActivateAsync(
            PreparedConfiguration prepared,
            long myVersion,
            bool isReload,
            CancellationToken ct)
        {
            await m_gate.WaitAsync(ct).ConfigureAwait(false);
            ConfigurationWatcher? committedWatcher = null;
            bool watcherOwned = true;
            bool committed = false;
            bool callbackDrainStarted = false;
            ActiveTransaction? transaction = null;
            try
            {
                // Deterministic test seam: allow a test to pause here, right
                // after the gate is acquired and BEFORE any ActiveTransaction is
                // published, so it can invoke a Stop/Dispose that sets the
                // shutdown latch while this activation still holds the gate.
                Func<Task>? gateAcquiredHook = GateAcquiredForTest;
                if (gateAcquiredHook != null)
                {
                    await gateAcquiredHook().ConfigureAwait(false);
                }

                // Recheck the shutdown/supersession latch, the disposed state
                // and the lifecycle version AND publish the manager-owned active
                // transaction in a SINGLE critical section, immediately after
                // acquiring the gate and BEFORE any awaited close/open. Combining
                // the recheck with the publication closes the window in which a
                // non-cancellable Stop or a Dispose could latch (and look up
                // m_activeTransaction) after the recheck but before publication:
                // such a shutdown now either latches BEFORE this section (its
                // latch is observed here and the activation aborts WITHOUT
                // opening any listener) or AFTER it (it finds the already
                // published transaction to abort). The queued shutdown then
                // acquires the gate and finalizes the lifecycle. Aborting the
                // published transaction cancels the operation token (unblocking a
                // cooperative listener open so the gate can be acquired) and
                // records that a shutdown, not a caller/provider cancellation,
                // superseded this activation.
                ApplicationConfiguration? previousAppConfig;
                bool previousWasStarted;
                List<ReverseConnectInfo> previousLive;
                List<(Uri Url, bool ConfigEntry)> previousDescriptors;
                CancellationToken operationToken;
                lock (m_lock)
                {
                    if (m_shutdownLatchOwner != null)
                    {
                        // A disposal latch also marks the manager disposed, so
                        // preserve the historical ObjectDisposedException shape
                        // for a start superseded by a dispose. A non-cancellable
                        // stop queued behind this gate leaves the manager
                        // undisposed, so it surfaces as a supersession.
                        ThrowIfDisposedLocked();
                        throw new ServiceResultException(
                            StatusCodes.BadInvalidState,
                            "Reverse connect lifecycle superseded by a pending shutdown.");
                    }
                    ThrowIfDisposedLocked();
                    if (m_lifecycleVersion != myVersion)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadInvalidState,
                            "Reverse connect lifecycle superseded by a concurrent operation.");
                    }
                    if (!isReload && m_state == ReverseConnectManagerState.Started)
                    {
                        throw new ServiceResultException(StatusCodes.BadInvalidState);
                    }
                    // Snapshot the committed application configuration so a
                    // rollback recreates the previous hosts from the same
                    // configuration that produced them. Capture the previous
                    // lifecycle state independently from the descriptor count so
                    // a previously Started but empty configuration is restored
                    // to Started (not Faulted) after a failed reload.
                    previousAppConfig = m_appConfig;
                    previousWasStarted = m_state == ReverseConnectManagerState.Started;
                    m_state = isReload
                        ? ReverseConnectManagerState.Reloading
                        : ReverseConnectManagerState.Starting;
                    previousLive = [.. m_endpointUrls.Values];
                    previousDescriptors =
                        [.. previousLive.Select(info => (info.EndpointUrl, info.ConfigEntry))];

                    // Publish the transaction atomically with the recheck above
                    // so there is no separate check/publication gap.
                    transaction = new ActiveTransaction(
                        CancellationTokenSource.CreateLinkedTokenSource(ct));
                    operationToken = transaction.Token;
                    m_activeTransaction = transaction;
                }

                // Cancel any held OnConnectionWaitingAsync callbacks and wait for
                // every in-flight callback to finish (releasing or handing off its
                // transport) BEFORE the previous listeners are closed/discarded,
                // exactly like a Stop/Dispose, so a held callback can never resolve
                // transport ownership against a listener this activation is about
                // to tear down. While the drain signal is set, callbacks raised by
                // the freshly opened candidate listeners are rejected too (see
                // TryEnterConnectionCallback), so no candidate callback can outlive
                // an uncommitted activation and leak a transport when a rollback
                // discards the candidates. The waiting-connection registrations are
                // deliberately preserved across the drain: only a committed Stop
                // clears them. The drain is ended (and the registration token
                // renewed) in the finally so a committed or rolled-back manager
                // accepts and holds callbacks again. This transactional drain is
                // intentionally non-cancellable: the candidate listeners must not
                // outlive an in-flight callback, so it never forwards the caller
                // token.
                await DrainConnectionCallbacksAsync(CancellationToken.None).ConfigureAwait(false);
                callbackDrainStarted = true;

                if (previousLive.Count > 0)
                {
                    lock (m_lock)
                    {
                        m_state = ReverseConnectManagerState.Stopping;
                    }
                    // The previous configured listeners are being replaced by
                    // this activation's candidates and are never reused, so
                    // dispose them for good. Reused manual endpoint hosts are
                    // only closed here and reopened from the candidate set. The
                    // close runs under the manager-owned operation token (linked
                    // to the caller token) so a non-cancellable Stop/Dispose that
                    // aborts the transaction unblocks a listener CloseAsync that
                    // honors cancellation and can then acquire the gate. The
                    // close swallows the resulting cancellation per host, so the
                    // shutdown/caller-cancel recheck below (which distinguishes a
                    // shutdown supersession that must NOT restore from a caller
                    // cancellation that does) governs the outcome.
                    await CloseAndDisposeOwnedHostsAsync(previousLive, operationToken)
                        .ConfigureAwait(false);
                }

                lock (m_lock)
                {
                    m_state = ReverseConnectManagerState.Starting;
                }

                var candidateList = new List<ReverseConnectInfo>(prepared.Hosts.Values);
                try
                {
                    // Recheck shutdown/version before opening the candidate
                    // listeners: a Stop/Dispose that superseded this activation
                    // while the previous listeners were being closed must never
                    // open a new listener. A supersession routes through the
                    // catch below, which cleans the candidate WITHOUT restoring.
                    lock (m_lock)
                    {
                        if (IsShutdownSupersededLocked(transaction, myVersion))
                        {
                            throw new ServiceResultException(
                                StatusCodes.BadInvalidState,
                                "Reverse connect lifecycle superseded by a concurrent operation.");
                        }
                    }

                    await OpenHostsAsync(candidateList, operationToken).ConfigureAwait(false);

                    // Create the polling watcher INSIDE the transactional scope
                    // so a source file that disappears between preparation/
                    // validation and watcher construction rolls back exactly
                    // like a listener-open failure: the candidate listeners are
                    // closed and the previous listeners, configuration, state
                    // and (untouched) watcher are restored.
                    if (prepared.SourceFilePath != null &&
                        prepared.ApplicationConfiguration != null)
                    {
                        committedWatcher = new ConfigurationWatcher(
                            prepared.ApplicationConfiguration,
                            m_telemetry);
                    }
                }
                catch (Exception activationError)
                {
                    // Close any candidate listeners opened above so no reused or
                    // owned host is left open outside committed ownership.
                    // OpenHostsAsync already closes them on its own failure;
                    // CloseAsync/DisposeAsync are idempotent, so this also covers
                    // a watcher-construction failure after a successful open. The
                    // failed configured candidates are discarded for good (any
                    // restore recreates fresh hosts), so dispose them; reused
                    // manual endpoint hosts are only closed.
                    await CloseAndDisposeOwnedHostsAsync(candidateList, CancellationToken.None)
                        .ConfigureAwait(false);

                    bool abortedByShutdown;
                    lock (m_lock)
                    {
                        abortedByShutdown = IsShutdownSupersededLocked(transaction, myVersion);
                    }
                    if (abortedByShutdown)
                    {
                        // A Stop/Dispose superseded this activation. The previous
                        // listeners were already closed above; do NOT restore or
                        // non-cancellably reopen them. Let the shutdown proceed
                        // (it owns the lifecycle and will finalize the state) and
                        // surface the failure/cancellation.
                        throw;
                    }

                    // A caller/provider cancellation or a genuine open failure:
                    // this operation still owns the lifecycle, so restore the
                    // previously running listeners through the transactional
                    // restore path.
                    await RestoreAfterFailureAsync(
                        previousDescriptors,
                        previousAppConfig,
                        previousWasStarted,
                        activationError).ConfigureAwait(false);
                    throw;
                }

                // Recheck lifecycle version, disposed/dispose-requested state,
                // the shutdown abort marker and the caller token immediately
                // before committing. A Stop/Dispose that superseded this
                // activation (Dispose bumps the version and marks disposing
                // synchronously; a non-cancellable stop sets the abort marker)
                // must never bind: roll the freshly opened candidates back and
                // surface the supersession WITHOUT restoring the previous
                // listeners. The caller token covers a start cancelled through
                // its own token, which DOES restore the previous service.
                bool lifecycleLost;
                bool canceledByCaller;
                lock (m_lock)
                {
                    lifecycleLost = IsShutdownSupersededLocked(transaction, myVersion);
                    // A caller cancellation observed here (the listener open did
                    // not itself honor the token) is a failed transaction, not a
                    // supersession: this operation still owns the lifecycle, so
                    // it must be rolled back through the same restore path as an
                    // open failure rather than surfaced as a supersession.
                    canceledByCaller = !lifecycleLost && ct.IsCancellationRequested;
                    if (!lifecycleLost && !canceledByCaller)
                    {
                        m_endpointUrls = prepared.Hosts;
                        m_configuration = prepared.EffectiveConfiguration;
                        if (prepared.ApplicationConfiguration != null)
                        {
                            m_appConfig = prepared.ApplicationConfiguration;
                            m_applicationType = prepared.ApplicationType;
                            m_configType = prepared.ConfigType;
                            // Promote the committed application configuration to
                            // the lazy restart seed so a later StopServiceAsync
                            // followed by EnsureStartedAsync reopens the latest
                            // reloaded endpoints rather than the original startup
                            // configuration. Only done when a lazy startup seed
                            // was configured (the DI/hosted scenario); a manager
                            // started purely through the explicit StartServiceAsync
                            // overloads keeps no restart seed. If the promoted
                            // configuration carries a SourceFilePath the restart
                            // still re-validates that file (see PrepareAsync), so
                            // file-backed reload semantics are preserved. A
                            // factory-seeded lazy startup (m_initialConfiguration
                            // still null, factory set) is promoted here too: the
                            // factory result is only cached once activation has
                            // committed, so a factory-loaded start that fails keeps
                            // the factory active and a retry re-invokes it.
                            if (m_initialStartRequested && !m_initialConfigurationMissing &&
                                (m_initialConfiguration != null ||
                                    m_initialConfigurationFactory != null))
                            {
                                m_initialConfiguration = prepared.ApplicationConfiguration;
                                m_initialConfigurationFactory = null;
                            }
                        }
                        m_state = ReverseConnectManagerState.Started;
                        m_lifecycleVersion++;
                    }
                }

                if (canceledByCaller)
                {
                    // Close the freshly opened candidates and restore the
                    // previously running listeners/configuration/state before
                    // propagating the cancellation, exactly like an open
                    // failure. The previous service was already stopped above,
                    // so a commit-time cancellation must not leave the manager
                    // without its prior listeners.
                    var canceled = new OperationCanceledException(ct);
                    await CloseAndDisposeOwnedHostsAsync(candidateList, CancellationToken.None)
                        .ConfigureAwait(false);
                    await RestoreAfterFailureAsync(
                        previousDescriptors,
                        previousAppConfig,
                        previousWasStarted,
                        canceled).ConfigureAwait(false);
                    throw canceled;
                }

                if (lifecycleLost)
                {
                    // Superseded by a shutdown: close the freshly opened
                    // candidates and surface the supersession. Do NOT restore or
                    // non-cancellably reopen the previous listeners; the
                    // shutdown owns the lifecycle and proceeds to finalize it.
                    await CloseAndDisposeOwnedHostsAsync(candidateList, CancellationToken.None)
                        .ConfigureAwait(false);
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidState,
                        "Reverse connect lifecycle superseded by a concurrent operation.");
                }

                // Swap/dispose watcher only after successful activation.
                SwapWatcher(committedWatcher);
                watcherOwned = false;
                committed = true;
            }
            finally
            {
                if (transaction != null)
                {
                    lock (m_lock)
                    {
                        if (ReferenceEquals(m_activeTransaction, transaction))
                        {
                            m_activeTransaction = null;
                        }
                    }
                    transaction.Dispose();
                }
                if (watcherOwned)
                {
                    committedWatcher?.Dispose();
                }
                // End the callback drain and renew the (drain-cancelled)
                // registration token so a committed or rolled-back manager accepts
                // and holds callbacks again on its (new or restored) listeners.
                // Only ends a drain this activation actually started.
                if (callbackDrainStarted)
                {
                    EndCallbackDrain();
                }
                m_gate.Release();
            }

            // A file modification between preparation and commit would be
            // missed by the freshly created watcher (which captured the new
            // write time). Detect it explicitly and re-run a reload through the
            // existing watcher-changed seam so the change is not lost. This runs
            // OUTSIDE the lifecycle gate so the virtual seam never executes
            // while the gate is held.
            if (committed &&
                prepared.SourceFilePath != null &&
                prepared.SourceFileLastWriteUtc != null &&
                prepared.ApplicationConfiguration != null)
            {
                ScheduleReloadIfFileChanged(
                    prepared.SourceFilePath,
                    prepared.SourceFileLastWriteUtc.Value,
                    prepared.ApplicationConfiguration);
            }
        }

        /// <summary>
        /// Detects a configuration-file change that occurred between
        /// preparation and commit and re-runs a reload through the
        /// <see cref="OnConfigurationChangedAsync"/> seam (using the freshly
        /// swapped watcher as the sender) so the change is not lost.
        /// </summary>
        private void ScheduleReloadIfFileChanged(
            string sourceFilePath,
            DateTime preparedWriteUtc,
            ApplicationConfiguration applicationConfiguration)
        {
            DateTime currentWriteUtc;
            try
            {
                var fileInfo = new FileInfo(sourceFilePath);
                if (!fileInfo.Exists)
                {
                    return;
                }
                currentWriteUtc = fileInfo.LastWriteTimeUtc;
            }
            catch (IOException)
            {
                return;
            }
            catch (UnauthorizedAccessException)
            {
                return;
            }

            if (currentWriteUtc <= preparedWriteUtc)
            {
                return;
            }

            ConfigurationWatcher? watcher;
            lock (m_lock)
            {
                watcher = m_configurationWatcher;
            }
            if (watcher == null)
            {
                return;
            }

            OnConfigurationChangedAsync(
                watcher,
                new ConfigurationWatcherEventArgs(applicationConfiguration, sourceFilePath));
        }

        /// <summary>
        /// Restores the previously running listeners after a failed activation
        /// that already stopped the old service.
        /// </summary>
        private async Task RestoreAfterFailureAsync(
            List<(Uri Url, bool ConfigEntry)> previousDescriptors,
            ApplicationConfiguration? previousAppConfig,
            bool previousWasStarted,
            Exception openError)
        {
            if (previousDescriptors.Count == 0)
            {
                lock (m_lock)
                {
                    // A shutdown (Dispose) that latched while this activation was
                    // unwinding owns the lifecycle; never write Started/Faulted
                    // over it. Leave the disposal to finalize the state.
                    if (ShutdownOwnsLifecycleLocked())
                    {
                        return;
                    }
                    m_endpointUrls = [];
                    // A previously Started but empty configuration is restored
                    // to Started with zero listeners; only a failure with no
                    // prior running service faults.
                    m_state = previousWasStarted
                        ? ReverseConnectManagerState.Started
                        : ReverseConnectManagerState.Faulted;
                    m_lifecycleVersion++;
                }
                if (previousWasStarted)
                {
                    m_logger.ReverseConnectServiceReloadFailedRestored(openError);
                }
                return;
            }

            // Publish a shutdown-only transaction so a concurrent Stop/Dispose
            // aborts the restore reopen (cancelling the open) and rechecks below
            // observe it. The reopen runs under this token - never the caller
            // token - so a caller/provider cancellation that routed here still
            // restores the previous listeners, while a shutdown reliably aborts
            // the rollback.
            var transaction = new ActiveTransaction(
                CancellationTokenSource.CreateLinkedTokenSource(CancellationToken.None));
            Dictionary<Uri, ReverseConnectInfo>? restored = null;
            try
            {
                lock (m_lock)
                {
                    // Atomically recheck the stop/dispose shutdown latch and
                    // publish the rollback transaction: a non-cancellable Stop
                    // (or a Dispose) that latched before this point owns the
                    // lifecycle, so the rollback must NOT reopen the previous
                    // listeners. Leaving the transaction unpublished lets the
                    // queued shutdown acquire the gate and finalize the state
                    // once this activation releases it.
                    if (ShutdownOwnsLifecycleLocked() || transaction.AbortedByShutdown)
                    {
                        return;
                    }
                    m_activeTransaction = transaction;
                }

                // Deterministic test seam: let a test start a Stop/Dispose after
                // the shutdown-only transaction is published and BEFORE the
                // pre-open recheck, proving the recheck aborts the rollback
                // without reopening any listener.
                Func<Task>? reopenHook = RestoreReopenHookForTest;
                if (reopenHook != null)
                {
                    await reopenHook().ConfigureAwait(false);
                }

                // Recheck the shutdown latch/version/dispose immediately before
                // recreating and opening: a Stop/Dispose that latched after the
                // caller-cancel/open-failure recheck (its teardown queued behind
                // the gate this operation still holds) must abort the rollback
                // without reopening any listener, leaving the shutdown to
                // finalize the lifecycle.
                lock (m_lock)
                {
                    if (ShutdownOwnsLifecycleLocked() || transaction.AbortedByShutdown)
                    {
                        return;
                    }
                }

                restored = await RecreateHostsAsync(previousDescriptors, previousAppConfig)
                    .ConfigureAwait(false);
                await OpenHostsAsync([.. restored.Values], transaction.Token)
                    .ConfigureAwait(false);
            }
            catch (Exception restoreError)
            {
                bool abortedByShutdown;
                lock (m_lock)
                {
                    abortedByShutdown = ShutdownOwnsLifecycleLocked() ||
                        transaction.AbortedByShutdown;
                }
                if (restored != null)
                {
                    foreach (ReverseConnectInfo info in restored.Values)
                    {
                        info.State = ReverseConnectHostState.Errored;
                    }
                    // Discard the failed/aborted restore set: dispose the
                    // configured (ConfigEntry) hosts outright so no recreated
                    // listener leaks, while reused manual endpoint hosts are only
                    // closed so their persistent ownership survives for a later
                    // restart.
                    await CloseAndDisposeOwnedHostsAsync(
                        [.. restored.Values],
                        CancellationToken.None).ConfigureAwait(false);
                }
                if (abortedByShutdown)
                {
                    // A Dispose aborted the reopen: do not fault the manager.
                    // Leave the disposal to finalize the lifecycle.
                    return;
                }
                lock (m_lock)
                {
                    m_endpointUrls = [];
                    m_state = ReverseConnectManagerState.Faulted;
                    m_lifecycleVersion++;
                }
                m_logger.ReverseConnectServiceRestoreFailed(
                    new AggregateException(openError, restoreError));
                throw new AggregateException(openError, restoreError);
            }
            finally
            {
                lock (m_lock)
                {
                    if (ReferenceEquals(m_activeTransaction, transaction))
                    {
                        m_activeTransaction = null;
                    }
                }
                transaction.Dispose();
            }

            bool committedShutdown;
            lock (m_lock)
            {
                // Recheck the shutdown latch/version/dispose immediately before
                // committing Started: a Dispose that latched while the reopen ran
                // must win, so the freshly reopened listeners are discarded and
                // the lifecycle is left to the disposal.
                committedShutdown = ShutdownOwnsLifecycleLocked() ||
                    transaction.AbortedByShutdown;
                if (!committedShutdown)
                {
                    m_endpointUrls = restored;
                    m_state = ReverseConnectManagerState.Started;
                    m_lifecycleVersion++;
                }
            }
            if (committedShutdown)
            {
                // The disposal owns the lifecycle: the reopened hosts were never
                // published into m_endpointUrls, so dispose the configured ones
                // here (reused manual endpoint hosts are only closed) so no
                // recreated listener leaks, and leave the disposal to finalize.
                await CloseAndDisposeOwnedHostsAsync(
                    [.. restored.Values],
                    CancellationToken.None).ConfigureAwait(false);
                return;
            }
            m_logger.ReverseConnectServiceReloadFailedRestored(openError);
        }

        /// <summary>
        /// Recreates hosts from previously running descriptors, reusing the
        /// persistent manual endpoint hosts by object identity where possible
        /// (recreating any whose listener was destroyed by a failed close) and
        /// creating configured hosts from the snapshot application config. The
        /// reconstruction is transactional: freshly created configured/owned
        /// hosts are collected incrementally and disposed on a later
        /// construction failure before the exception is rethrown, so a partial
        /// reconstruction never leaks an unopened listener.
        /// </summary>
        private async Task<Dictionary<Uri, ReverseConnectInfo>> RecreateHostsAsync(
            List<(Uri Url, bool ConfigEntry)> descriptors,
            ApplicationConfiguration? appConfig)
        {
            var dict = new Dictionary<Uri, ReverseConnectInfo>();
            var created = new List<ReverseConnectInfo>();
            try
            {
                foreach ((Uri url, bool configEntry) in descriptors)
                {
                    ReverseConnectInfo? manual = null;
                    if (!configEntry)
                    {
                        lock (m_lock)
                        {
                            m_manualEndpoints.TryGetValue(url, out manual);
                        }
                    }
                    if (manual != null)
                    {
                        ReverseConnectInfo reused = await ReuseOrRecreateManualHostAsync(
                            url,
                            manual).ConfigureAwait(false);
                        reused.State = ReverseConnectHostState.Closed;
                        dict[url] = reused;
                    }
                    else
                    {
                        ReverseConnectInfo info = CreateEndpointInfo(url, configEntry, appConfig);
                        created.Add(info);
                        dict[url] = info;
                    }
                }
            }
            catch
            {
                // A later host construction failed after earlier configured
                // hosts were already created. Dispose the freshly created
                // configured/owned hosts collected so far so a partial
                // reconstruction never leaks an unopened listener; reused manual
                // endpoint hosts are persistent and are never disposed here.
                await CloseAndDisposeOwnedHostsAsync(created, CancellationToken.None)
                    .ConfigureAwait(false);
                throw;
            }
            return dict;
        }

        /// <summary>
        /// Returns a usable persistent manual endpoint host for reuse. A manual
        /// host whose listener was destroyed by a failed destructive close (see
        /// <see cref="ReverseConnectHost.CloseAsync"/>) is unusable - a
        /// subsequent <see cref="ReverseConnectHost.OpenAsync"/> would reject it
        /// so the endpoint could never reopen - so it is recreated from its
        /// endpoint and the application configuration originally supplied to
        /// <see cref="AddEndpoint(Uri, ApplicationConfiguration?)"/> (its TLS
        /// context), NOT the later start configuration. This keeps a manual WSS
        /// listener bound to its original certificate registry and validator
        /// even when the manager is (re)started with a bare
        /// <see cref="ReverseConnectClientConfiguration"/> or a different
        /// application configuration. The replacement is published back into
        /// <see cref="m_manualEndpoints"/> as the persistent manual host, and
        /// the unusable host is disposed.
        /// </summary>
        private async ValueTask<ReverseConnectInfo> ReuseOrRecreateManualHostAsync(
            Uri url,
            ReverseConnectInfo existing)
        {
            if (existing.ReverseConnectHost.HasListener)
            {
                return existing;
            }

            // Recreate the manual host from the ApplicationConfiguration that
            // was originally supplied to AddEndpoint (persisted on the manual
            // ReverseConnectInfo), so a WSS listener keeps its original TLS
            // context regardless of the activation's application configuration.
            ReverseConnectInfo replacement = CreateEndpointInfo(
                url,
                false,
                existing.ManualConfiguration);
            bool published;
            lock (m_lock)
            {
                if (m_manualEndpoints.TryGetValue(url, out ReverseConnectInfo? current) &&
                    ReferenceEquals(current, existing))
                {
                    m_manualEndpoints[url] = replacement;
                    published = true;
                }
                else
                {
                    published = false;
                }
            }
            if (!published)
            {
                // The manual endpoint was swapped or removed concurrently; the
                // freshly created replacement is not needed, so dispose it and
                // reuse whatever the map now holds (or the original as a
                // fallback) rather than leaking the replacement.
                await DisposeHostAsync(replacement).ConfigureAwait(false);
                lock (m_lock)
                {
                    if (m_manualEndpoints.TryGetValue(url, out ReverseConnectInfo? current))
                    {
                        return current;
                    }
                }
                return existing;
            }
            await DisposeHostAsync(existing).ConfigureAwait(false);
            return replacement;
        }

        /// <summary>
        /// Cleans up a not-yet-committed candidate host set without
        /// cancellation. Only hosts this operation owns (by object identity)
        /// are torn down; reused manual endpoint hosts are never closed here.
        /// </summary>
        private async ValueTask CleanupCandidateAsync(PreparedConfiguration prepared)
        {
            var toClose = new List<ReverseConnectInfo>();
            foreach (ReverseConnectInfo host in prepared.OwnedHosts)
            {
                if (host.State == ReverseConnectHostState.New)
                {
                    host.State = ReverseConnectHostState.Errored;
                }
                toClose.Add(host);
            }
            // Every owned host is a freshly created configured candidate that
            // never committed, so dispose it outright.
            await CloseAndDisposeOwnedHostsAsync(toClose, CancellationToken.None)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Open host ports for the supplied hosts.
        /// </summary>
        private async ValueTask OpenHostsAsync(
            List<ReverseConnectInfo> snapshot,
            CancellationToken ct)
        {
            var failures = new List<(Uri? Url, Exception Error)>();
            foreach (ReverseConnectInfo value in snapshot)
            {
                if (value.State == ReverseConnectHostState.Errored)
                {
                    failures.Add((
                        value.EndpointUrl,
                        value.Error ?? new ServiceResultException(StatusCodes.BadNoCommunication)));
                    continue;
                }

                try
                {
                    if (value.State < ReverseConnectHostState.Open)
                    {
                        await value.ReverseConnectHost.OpenAsync(ct).ConfigureAwait(false);
                        value.State = ReverseConnectHostState.Open;
                        value.Error = null;
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    // The host whose OpenAsync was cancelled may have partially
                    // bound its listener but is still in a New/Closed state, so
                    // the non-cancellable CloseHostsAsync below (which only
                    // closes Open/Errored hosts) would skip it and leave it
                    // partially initialized. Mark it Errored so it is closed and
                    // reset to a clean, retryable state. This is essential for
                    // reused manual endpoint hosts, which are attempted here but
                    // are not candidate-owned, so no owned-host cleanup would
                    // otherwise reclaim them.
                    if (value.State < ReverseConnectHostState.Open)
                    {
                        value.State = ReverseConnectHostState.Errored;
                        value.Error ??= new OperationCanceledException(ct);
                    }
                    await CloseHostsAsync(snapshot, CancellationToken.None).ConfigureAwait(false);
                    throw;
                }
                catch (Exception e)
                {
                    m_logger.FailedOpenUri(
                        e,
                        value.EndpointUrl);
                    value.State = ReverseConnectHostState.Errored;
                    value.Error = e;
                    failures.Add((value.EndpointUrl, e));
                }
            }

            if (failures.Count == 0)
            {
                return;
            }

            await CloseHostsAsync(snapshot, CancellationToken.None).ConfigureAwait(false);

            string failedUrls = string.Join(
                ", ",
                failures.Select(failure => failure.Url?.ToString() ?? "<unknown>"));
            Exception error = failures.Count == 1
                ? failures[0].Error
                : new AggregateException(failures.Select(failure => failure.Error));
            throw ServiceResultException.Create(
                StatusCodes.BadNoCommunication,
                error,
                "Failed to open reverse connect listener(s): {0}.",
                failedUrls);
        }

        /// <summary>
        /// Close the provided reverse-connect hosts without acquiring the manager lock.
        /// </summary>
        private async ValueTask CloseHostsAsync(
            List<ReverseConnectInfo> snapshot,
            CancellationToken ct)
        {
            foreach (ReverseConnectInfo value in snapshot)
            {
                try
                {
                    if (value.State is ReverseConnectHostState.Open or ReverseConnectHostState.Errored)
                    {
                        await value.ReverseConnectHost.CloseAsync(ct).ConfigureAwait(false);
                        value.State = ReverseConnectHostState.Closed;
                        value.Error = null;
                    }
                }
                catch (Exception e)
                {
                    m_logger.FailedCloseUri(
                        e,
                        value.EndpointUrl);
                    value.State = ReverseConnectHostState.Errored;
                    value.Error = e;
                }
            }
        }

        /// <summary>
        /// Closes the provided hosts and additionally disposes the underlying
        /// transport listener of every host this manager owns outright
        /// (configured/candidate hosts, <see cref="ReverseConnectInfo.ConfigEntry"/>
        /// is <c>true</c>). A configured host that is discarded - a failed or
        /// superseded candidate, a replaced snapshot host, or a committed-stop
        /// listener - is torn down for good so it can never be reused. Reused
        /// manual endpoint hosts (<see cref="ReverseConnectInfo.ConfigEntry"/>
        /// is <c>false</c>) are only closed so a later reload/restart can reopen
        /// them; they are disposed only when the manager itself is disposed.
        /// </summary>
        private async ValueTask CloseAndDisposeOwnedHostsAsync(
            List<ReverseConnectInfo> snapshot,
            CancellationToken ct)
        {
            await CloseHostsAsync(snapshot, ct).ConfigureAwait(false);
            foreach (ReverseConnectInfo value in snapshot)
            {
                if (value.ConfigEntry)
                {
                    await DisposeHostAsync(value).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Disposes the underlying transport listener of every supplied host,
        /// regardless of ownership. Used when the manager is torn down so both
        /// configured and reused manual endpoint hosts release their listener.
        /// </summary>
        private async ValueTask DisposeAllHostsAsync(List<ReverseConnectInfo> snapshot)
        {
            foreach (ReverseConnectInfo value in snapshot)
            {
                await DisposeHostAsync(value).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Disposes a single host's underlying transport listener idempotently,
        /// swallowing (and logging) any teardown failure.
        /// </summary>
        private async ValueTask DisposeHostAsync(ReverseConnectInfo value)
        {
            try
            {
                await value.ReverseConnectHost.DisposeAsync().ConfigureAwait(false);
                value.State = ReverseConnectHostState.Closed;
                value.Error = null;
            }
            catch (Exception e)
            {
                m_logger.FailedCloseUri(
                    e,
                    value.EndpointUrl);
                value.State = ReverseConnectHostState.Errored;
                value.Error = e;
            }
        }

        /// <summary>
        /// Closes the provided reverse-connect hosts honoring the supplied
        /// cancellation token. A cancelled close surfaces
        /// <see cref="OperationCanceledException"/> so the caller can restore a
        /// coherent state; the host whose close was cancelled is left
        /// <see cref="ReverseConnectHostState.Open"/> (its close did not
        /// complete). Non-cancellation close failures are swallowed per host
        /// exactly like <see cref="CloseHostsAsync"/>.
        /// </summary>
        private async ValueTask CloseHostsHonoringCancellationAsync(
            List<ReverseConnectInfo> snapshot,
            CancellationToken ct)
        {
            foreach (ReverseConnectInfo value in snapshot)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    if (value.State is ReverseConnectHostState.Open or ReverseConnectHostState.Errored)
                    {
                        await value.ReverseConnectHost.CloseAsync(ct).ConfigureAwait(false);
                        value.State = ReverseConnectHostState.Closed;
                        value.Error = null;
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception e)
                {
                    m_logger.FailedCloseUri(
                        e,
                        value.EndpointUrl);
                    value.State = ReverseConnectHostState.Errored;
                    value.Error = e;
                }
            }
        }

        /// <summary>
        /// Restores a coherent, retryable <see cref="ReverseConnectManagerState.Started"/>
        /// state after a cancellable stop whose callback drain was cancelled at
        /// the caller's deadline BEFORE any listener was closed. Unlike
        /// <see cref="RestoreStartedAfterCanceledStopAsync"/> the running
        /// listeners were never touched, so this neither reopens nor closes any
        /// host and leaves the preserved waiting-connection registrations intact;
        /// it only re-publishes Started (unless a concurrent
        /// <see cref="DisposeAsync"/> or another shutdown has taken ownership of
        /// the lifecycle while this cancelled stop held the gate) and re-queues
        /// any reload candidate the stop's lifecycle-version bump would otherwise
        /// strand. The caller's finally ends the callback drain and renews the
        /// registration token so held callbacks can hold again.
        /// </summary>
        private void RestoreStartedAfterCanceledDrain()
        {
            (ApplicationConfiguration Config, int Generation)? requeueReload;
            lock (m_lock)
            {
                // A dispose or a concurrent non-cancellable stop that took
                // ownership of the lifecycle while this cancelled stop held the
                // gate must win: leave the pending shutdown to finalize the
                // lifecycle rather than resurrecting a Started manager.
                if (ShutdownOwnsLifecycleLocked())
                {
                    return;
                }
                m_state = ReverseConnectManagerState.Started;
                m_lifecycleVersion++;
                // The stop bumped the lifecycle version, so an in-flight or
                // pending watcher reload racing this rolled-back stop would
                // observe a superseded version and be dropped even though the
                // manager is back to Started. Capture that candidate so it is
                // re-queued below against the restored version.
                requeueReload = CaptureReloadCandidateLocked();
            }
            if (requeueReload != null)
            {
                _ = ReloadConfigurationAsync(
                    requeueReload.Value.Config,
                    requeueReload.Value.Generation);
            }
        }

        /// <summary>
        /// Restores a coherent, retryable <see cref="ReverseConnectManagerState.Started"/>
        /// state after a stop whose listener close was cancelled. Reopens the
        /// still-running listener set non-cancellably so a subsequent stop can
        /// complete cleanly; if the reopen fails the manager is faulted with no
        /// listeners. If a concurrent <see cref="DisposeAsync"/> (or another
        /// shutdown) has taken ownership of the lifecycle while this cancelled
        /// stop held the gate, the reopen and every state write are skipped so
        /// the disposal is left to finalize the lifecycle (Started is never
        /// written after disposal begins).
        /// </summary>
        private async Task RestoreStartedAfterCanceledStopAsync(
            List<ReverseConnectInfo> live)
        {
            // A dispose or a concurrent non-cancellable stop sets the shutdown
            // latch and (for a dispose) marks the manager Disposing synchronously
            // (without waiting on the lifecycle gate this stop still holds), then
            // queues its teardown behind the gate. Detect that ownership transfer
            // before reopening: reopening and writing Started here would resurrect
            // a manager that is being shut down and strand it as running. Leave
            // the pending shutdown to finish.
            lock (m_lock)
            {
                if (ShutdownOwnsLifecycleLocked())
                {
                    return;
                }
            }

            // Publish a shutdown-only transaction so a concurrent Stop/Dispose
            // aborts the reopen (cancelling the open) and the rechecks below
            // observe it. The reopen runs under this token so a shutdown that
            // begins while the listeners are being reopened reliably unblocks and
            // aborts the rollback rather than resurrecting a shutting-down manager.
            var transaction = new ActiveTransaction(
                CancellationTokenSource.CreateLinkedTokenSource(CancellationToken.None));
            try
            {
                lock (m_lock)
                {
                    // Atomically recheck the stop/dispose shutdown latch and
                    // publish the rollback transaction so a shutdown that latched
                    // after the initial check but before this publication is not
                    // missed: leave the transaction unpublished and let the queued
                    // shutdown finalize the lifecycle.
                    if (ShutdownOwnsLifecycleLocked() || transaction.AbortedByShutdown)
                    {
                        return;
                    }
                    m_activeTransaction = transaction;
                }

                // Deterministic test seam: let a test start a Stop/Dispose after
                // the initial shutdown check has passed and the transaction is
                // published, but BEFORE the pre-open recheck, proving the recheck
                // aborts the rollback without reopening any listener.
                Func<Task>? reopenHook = RestoreReopenHookForTest;
                if (reopenHook != null)
                {
                    await reopenHook().ConfigureAwait(false);
                }

                // Recheck the shutdown latch/version/dispose immediately before
                // reopening: a Stop/Dispose that latched after the initial check
                // (its teardown queued behind the gate this stop still holds) must
                // abort the rollback without reopening any listener.
                lock (m_lock)
                {
                    if (ShutdownOwnsLifecycleLocked() || transaction.AbortedByShutdown)
                    {
                        return;
                    }
                }

                await OpenHostsAsync(live, transaction.Token).ConfigureAwait(false);
            }
            catch (Exception reopenError)
            {
                bool abortedByShutdown;
                lock (m_lock)
                {
                    abortedByShutdown = ShutdownOwnsLifecycleLocked() ||
                        transaction.AbortedByShutdown;
                }
                if (abortedByShutdown)
                {
                    // A Dispose aborted the reopen mid-open: the live hosts remain
                    // referenced by m_endpointUrls and are closed by OpenHostsAsync
                    // on cancellation, so the disposal teardown disposes them. Do
                    // not fault; leave the disposal to finalize the lifecycle.
                    return;
                }
                // The reopen failed, so the manager faults with no listeners.
                // Dispose the discarded configured hosts for good BEFORE clearing
                // the references so no recreated listener leaks; reused manual
                // endpoint hosts are only closed here so their persistent
                // ownership survives for a later restart (a manual host whose
                // listener was destroyed by this failed reopen is recreated on
                // the next start via ReuseOrRecreateManualHostAsync).
                await CloseAndDisposeOwnedHostsAsync(live, CancellationToken.None)
                    .ConfigureAwait(false);
                lock (m_lock)
                {
                    // Re-check ownership: a dispose may have taken over while the
                    // reopen was failing. Never overwrite the disposal state.
                    if (ShutdownOwnsLifecycleLocked())
                    {
                        return;
                    }
                    m_endpointUrls = [];
                    m_state = ReverseConnectManagerState.Faulted;
                    m_lifecycleVersion++;
                }
                m_logger.ReverseConnectServiceRestoreFailed(reopenError);
                return;
            }
            finally
            {
                lock (m_lock)
                {
                    if (ReferenceEquals(m_activeTransaction, transaction))
                    {
                        m_activeTransaction = null;
                    }
                }
                transaction.Dispose();
            }
            (ApplicationConfiguration Config, int Generation)? requeueReload;
            lock (m_lock)
            {
                // Final ownership re-check before committing Started: a dispose
                // that latched while the reopen ran must win.
                if (ShutdownOwnsLifecycleLocked() || transaction.AbortedByShutdown)
                {
                    return;
                }
                m_state = ReverseConnectManagerState.Started;
                m_lifecycleVersion++;
                // The stop bumped the lifecycle version (twice, counting this
                // restore) so an in-flight or pending watcher reload racing this
                // rolled-back stop would observe a superseded version in
                // ActivateAsync and be dropped even though the manager is back
                // to Started. Capture that reload candidate so it can be
                // re-queued below against the restored version and the file
                // change is eventually applied.
                requeueReload = CaptureReloadCandidateLocked();
            }
            if (requeueReload != null)
            {
                // Re-queue outside the state lock. RunReloadLoopAsync coalesces
                // this against the still-in-flight reload (latest-wins), so the
                // dropped candidate is re-driven against the restored Started
                // state rather than lost.
                _ = ReloadConfigurationAsync(
                    requeueReload.Value.Config,
                    requeueReload.Value.Generation);
            }
        }

        /// <summary>
        /// Swap the active configuration watcher after a successful activation.
        /// </summary>
        private void SwapWatcher(ConfigurationWatcher? newWatcher)
        {
            ConfigurationWatcher? old;
            lock (m_lock)
            {
                old = m_configurationWatcher;
                m_configurationWatcher = newWatcher;
                m_watcherGeneration++;
            }
            if (old != null)
            {
                old.Changed -= OnConfigurationChangedAsync;
                old.Dispose();
            }
            if (newWatcher != null)
            {
                newWatcher.Changed += OnConfigurationChangedAsync;
            }
        }

        /// <summary>
        /// Stop monitoring the application configuration.
        /// </summary>
        private void DisposeConfigurationWatcher()
        {
            ConfigurationWatcher? watcher;
            lock (m_lock)
            {
                watcher = m_configurationWatcher;
                m_configurationWatcher = null;
                m_watcherGeneration++;
            }
            if (watcher == null)
            {
                return;
            }

            watcher.Changed -= OnConfigurationChangedAsync;
            watcher.Dispose();
        }

        /// <summary>
        /// Returns the shared disposal task, starting the teardown exactly
        /// once. Every caller (sync or async, first or subsequent) awaits the
        /// same task and therefore the complete teardown.
        /// </summary>
        /// <param name="reentrantFromStartup">
        /// Set to <c>true</c> when the caller is running inside this manager's
        /// own in-flight startup flow (a configuration provider/factory or
        /// legacy update hook). The caller must NOT await the returned task in
        /// that case: the teardown drains the current start task, so awaiting
        /// it from the start's own flow would deadlock. The dispose is still
        /// requested and scheduled here; it completes once the start unwinds.
        /// </param>
        private Task GetOrStartDisposeTask(out bool reentrantFromStartup)
        {
            // Capture whether this call originates from within an in-flight
            // startup flow BEFORE any state work so the caller can fail fast
            // instead of awaiting the shared teardown (which would deadlock on
            // the current start task this dispose is about to drain). Only an
            // active scope counts as re-entry; a deferred child task whose
            // originating startup flow already completed is not treated as one.
            reentrantFromStartup = m_activeStartupOwner.Value?.Active == true;
            TaskCompletionSource<bool>? owner = null;
            CancellationTokenSource? pendingStart = null;
            Task? pendingStartTask = null;
            CancellationTokenSource? explicitStart = null;
            ActiveTransaction? activeTransaction = null;
            ReloadRequest? supersededReload = null;
            CancellationTokenSource? supersededReloadCts = null;
            Task task;
            lock (m_lock)
            {
                if (m_disposeSignal == null)
                {
                    m_disposeSignal = new TaskCompletionSource<bool>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    m_disposed = 1;
                    m_state = ReverseConnectManagerState.Disposing;
                    m_lifecycleVersion++;
                    // A dispose supersedes every queued reload: advance the
                    // teardown epoch so the drain rejects any reload queued
                    // before this dispose, and detach the newest pending request
                    // so it is completed deterministically below instead of
                    // reopening a listener during/after teardown.
                    m_teardownEpoch++;
                    supersededReload = m_pendingReload;
                    m_pendingReload = null;
                    // Detach the in-flight reload drain loop from the prior
                    // epoch and cancel its manager-owned source (below) so a
                    // cooperative reload provider/activation exits; a
                    // noncooperative stale reload's completion is discarded by
                    // the loop's epoch guard and never restarts the drain.
                    supersededReloadCts = DetachActiveReloadLocked();
                    // Latch the shutdown permanently BEFORE the transaction
                    // lookup below so an activation that already holds the gate
                    // (its ActiveTransaction not yet published) aborts without
                    // opening when it checks the latch after gate acquisition.
                    // Disposal never clears the latch.
                    m_shutdownLatchOwner = s_disposeLatch;
                    owner = m_disposeSignal;
                    // Invalidate and cancel a pending lazy start so it can never
                    // bind, even if it has not yet reached provider preparation.
                    // Keep its task so teardown can drain it (and defer disposing
                    // its token source) before completing.
                    pendingStart = m_startCts;
                    pendingStartTask = m_startTask;
                    m_startCts = null;
                    m_startTask = null;
                    // Capture an in-flight explicit StartServiceAsync so its
                    // token is cancelled below (a cooperative provider then
                    // observes cancellation and exits, and the start unwinds).
                    // The explicit start owns and disposes its own token source,
                    // so it is only cancelled - never disposed - here.
                    explicitStart = m_activeStartCts;
                    m_currentStartTask = null;
                    // Abort an in-flight activation blocked inside a listener
                    // OpenAsync so DisposeTeardownAsync can acquire the gate and
                    // the superseded start can never commit afterward. The abort
                    // marks the supersession as a shutdown so the activation does
                    // not restore/reopen the previous listeners.
                    activeTransaction = m_activeTransaction;
                }
                task = m_disposeSignal.Task;
            }

            if (owner != null)
            {
                try
                {
                    pendingStart?.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }
                try
                {
                    explicitStart?.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }
                activeTransaction?.AbortForShutdown();
                CancelDetachedReload(supersededReloadCts);
                if (supersededReload != null)
                {
                    RejectSupersededReload(supersededReload);
                }
                _ = RunDisposeAsync(owner, pendingStart, pendingStartTask);
            }
            return task;
        }

        /// <summary>
        /// Runs the one-shot teardown and completes the shared signal. Drains
        /// a superseded lazy start (which may still be using the manager-owned
        /// token) and disposes its token source before the complete teardown,
        /// so no provider/activation ever registers on a disposed token. A
        /// superseded explicit start is cancelled (in
        /// <see cref="GetOrStartDisposeTask"/>) and owns/disposes its own token
        /// source, so it is not drained here.
        /// </summary>
        private async Task RunDisposeAsync(
            TaskCompletionSource<bool> owner,
            CancellationTokenSource? pendingStart,
            Task? pendingStartTask)
        {
            try
            {
                // The pending start's token was cancelled above; a cooperative
                // start completes promptly. Awaiting it here never deadlocks on
                // the lifecycle gate because a cancelled start throws before (or
                // at) the gate wait without holding it.
                if (pendingStartTask != null)
                {
                    try
                    {
                        await pendingStartTask.ConfigureAwait(false);
                    }
                    catch
                    {
                        // The start's failure is surfaced to its own awaiters.
                    }
                }
                pendingStart?.Dispose();

                await DisposeTeardownAsync().ConfigureAwait(false);
                owner.TrySetResult(true);
            }
            catch (Exception e)
            {
                owner.TrySetException(e);
            }
        }

        /// <summary>
        /// The actual dispose implementation, serialized with the lifecycle
        /// gate. Runs once; derived cleanup participates via
        /// <see cref="DisposeAsyncCore"/>.
        /// </summary>
        private async Task DisposeTeardownAsync()
        {
            await m_gate.WaitAsync().ConfigureAwait(false);
            try
            {
                lock (m_lock)
                {
                    m_state = ReverseConnectManagerState.Disposed;
                    m_lifecycleVersion++;
                }

                DisposeConfigurationWatcher();

                List<ReverseConnectInfo> live;
                List<ReverseConnectInfo> manual;
                lock (m_lock)
                {
                    live = [.. m_endpointUrls.Values];
                    manual = [.. m_manualEndpoints.Values];
                    m_endpointUrls = [];
                    m_manualEndpoints = [];
                }

                // Cancel any held OnConnectionWaitingAsync callbacks and wait for
                // every in-flight callback to finish before disposing the
                // listeners, so no callback is still holding a transport when the
                // terminal DisposeAllHostsAsync tears the channels down. The drain
                // is one-way here (the manager is terminal), so the reject-new
                // signal is left set: any late callback rejects, which is correct.
                await DrainConnectionCallbacksAsync().ConfigureAwait(false);

                // Final ownership teardown: dispose every listener, both the
                // configured (live) hosts and the reused manual endpoint hosts
                // that survived earlier reloads/stops. DisposeAsync closes the
                // listener before disposing it (so no separate close is needed)
                // and is idempotent, so a host present in both sets - or already
                // closed - is disposed exactly once.
                await DisposeAllHostsAsync(live).ConfigureAwait(false);
                await DisposeAllHostsAsync(manual).ConfigureAwait(false);

                // Tear down the registration cancellation source under the
                // registrations lock so it is serialized with
                // RegisterWaitingConnectionCore/CancelAndRenewTokenSource. The
                // disposed flag is already set, so no registration can install
                // a new source after this point. Fault any pending
                // WaitForConnectionAsync so disposal wakes its waiters promptly
                // instead of leaving them to time out.
                lock (m_registrationsLock)
                {
                    m_registrations.Clear();
                    AbortActiveWaitsLocked(new ServiceResultException(
                        StatusCodes.BadInvalidState,
                        "The reverse connect manager was disposed."));
                    m_cts.Dispose();
                }
            }
            finally
            {
                m_gate.Release();
            }

            // Let derived types participate after all listeners are closed.
            await DisposeAsyncCore().ConfigureAwait(false);

            // Invoke the legacy protected Dispose(bool) override exactly once
            // for subclasses that predate DisposeAsyncCore and follow the
            // classic IDisposable pattern. The guard ensures it can never run
            // twice (the shared teardown itself runs once) and the empty base
            // implementation guarantees no re-entry into the obsolete Dispose()
            // wrapper or a second manager teardown.
            if (Interlocked.Exchange(ref m_legacyDisposeInvoked, 1) == 0)
            {
                Dispose(true);
            }

            // The lifecycle gate is intentionally not disposed: queued callers
            // may still acquire/release it before observing the disposed state.
        }

        /// <summary>
        /// Create an endpoint entry and its transport listener using the
        /// supplied operation-local application configuration for TLS state.
        /// </summary>
        [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
            Justification = "Ownership of the created ReverseConnectHost transfers to the returned " +
            "ReverseConnectInfo; the async lifecycle disposes it (candidate cleanup, snapshot " +
            "replacement, committed stop or manager disposal). On a creation failure the host holds " +
            "no open listener yet, so there is nothing to dispose.")]
        private ReverseConnectInfo CreateEndpointInfo(
            Uri endpointUrl,
            bool configEntry,
            ApplicationConfiguration? appConfig)
        {
            ValidateEndpointUrl(endpointUrl);

            var reverseConnectHost = new ReverseConnectHost(m_telemetry, TransportBindings);
            var info = new ReverseConnectInfo(
                endpointUrl,
                reverseConnectHost,
                configEntry,
                configEntry ? null : appConfig);
            try
            {
                // Listener bindings that terminate TLS (WSS) need a server
                // TLS certificate + validator at Open time. Pull them from
                // the snapshot ApplicationConfiguration's CertificateManager
                // when available so the user does not have to plumb them
                // manually for the common case.
                ICertificateRegistry? serverCertificates = null;
                ICertificateValidatorEx? certificateValidator = null;
                if (Utils.IsUriWssScheme(endpointUrl.AbsoluteUri) && appConfig != null)
                {
                    serverCertificates = appConfig.CertificateManager;
                    certificateValidator = appConfig.CertificateManager;
                }
                reverseConnectHost.CreateListener(
                    endpointUrl,
                    new ConnectionWaitingHandlerAsync(OnConnectionWaitingAsync),
                    new EventHandler<ConnectionStatusEventArgs>(OnConnectionStatusChanged),
                    serverCertificates,
                    certificateValidator);
            }
            catch (ServiceResultException e)
            {
                throw ServiceResultException.Create(
                    e.StatusCode,
                    e,
                    "Could not create reverse connect listener for endpoint {0}.",
                    endpointUrl);
            }
            catch (ArgumentException ae)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadTcpEndpointUrlInvalid,
                    ae,
                    "Invalid reverse connect listener endpoint URL: {0}.",
                    endpointUrl);
            }
            return info;
        }

        /// <summary>
        /// Validate a reverse-connect listener endpoint URL.
        /// </summary>
        private static void ValidateEndpointUrl(Uri endpointUrl)
        {
            if (!endpointUrl.IsAbsoluteUri || string.IsNullOrWhiteSpace(endpointUrl.Host))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadTcpEndpointUrlInvalid,
                    "Invalid reverse connect listener endpoint URL: {0}.",
                    endpointUrl);
            }
        }

        /// <summary>
        /// Whether endpoint mutation is allowed in the current state.
        /// </summary>
        private static bool CanMutateEndpoints(ReverseConnectManagerState state)
        {
            return state is ReverseConnectManagerState.New
                or ReverseConnectManagerState.Stopped
                or ReverseConnectManagerState.Faulted;
        }

        /// <summary>
        /// Raised when a reverse connection is waiting,
        /// finds and calls a waiting connection.
        /// </summary>
        private async Task OnConnectionWaitingAsync(object sender, ConnectionWaitingEventArgs e)
        {
            // Register this callback so a concurrent Stop/Dispose waits for it to
            // finish (releasing or handing off the transport) before the terminal
            // listener close/dispose tears the channels down. If a shutdown is
            // already draining callbacks, do not hold the transport: return with
            // the connection unaccepted so the listener reclaims/closes it.
            if (!TryEnterConnectionCallback())
            {
                return;
            }
            // Publish the per-flow callback marker so a Stop/Dispose re-entered
            // directly by the user callback below fails fast rather than
            // deadlocking on its own drain. Cleared in the finally, which also
            // deactivates the scope so a child task deferred past this callback
            // (e.g. a user-callback-spawned Task.Run) that captured the scope
            // reference is not falsely rejected as a re-entry.
            OperationScope? previousCallbackOwner = m_activeCallbackOwner.Value;
            var callbackScope = new OperationScope(
                Interlocked.Increment(ref m_operationScopeGeneration));
            m_activeCallbackOwner.Value = callbackScope;
            try
            {
                long startTimestamp = m_timeProvider.GetTimestamp();
                var holdTime = TimeSpan.FromMilliseconds(m_configuration?.HoldTime ?? 15000);

                bool matched = MatchRegistration(sender, e);
                while (!matched)
                {
                    m_logger.HoldingReverseConnectionServerUriEndpointUrl(
                        e.ServerUri,
                        e.EndpointUrl);
                    CancellationToken ct;
                    lock (m_registrationsLock)
                    {
                        ct = m_cts.Token;
                    }
                    TimeSpan delay = holdTime - m_timeProvider.GetElapsedTime(startTimestamp);
                    if (delay > TimeSpan.Zero)
                    {
                        await m_timeProvider.Delay(delay, ct)
                            .ContinueWith(tsk =>
                            {
                                if (tsk.IsCanceled)
                                {
                                    matched = MatchRegistration(sender, e);
                                    if (matched && m_logger.IsEnabled(LogLevel.Information))
                                    {
                                        m_logger.MatchedReverseConnectionServerUriEndpointUrlAfter(
                                            e.ServerUri,
                                            e.EndpointUrl,
                                            (long)m_timeProvider.GetElapsedTime(startTimestamp).TotalMilliseconds);
                                    }
                                }
                            },
                            default,
                            TaskContinuationOptions.None,
                            TaskScheduler.Default)
                            .ConfigureAwait(false);
                    }
                    break;
                }

                if (m_logger.IsEnabled(LogLevel.Information))
                {
                    m_logger.ActionReverseConnectionServerUriEndpointUrlAfter(
                        e.Accepted ? "Accepted" : "Rejected",
                        e.ServerUri,
                        e.EndpointUrl,
                        (long)m_timeProvider.GetElapsedTime(startTimestamp).TotalMilliseconds);
                }
            }
            finally
            {
                callbackScope.Active = false;
                m_activeCallbackOwner.Value = previousCallbackOwner;
                ExitConnectionCallback();
            }
        }

        /// <summary>
        /// Match the waiting connection with a registration, callback registration,
        /// return if connection is accepted in event.
        /// </summary>
        /// <returns>true if a match was found.</returns>
        private bool MatchRegistration(object sender, ConnectionWaitingEventArgs e)
        {
            Registration? callbackRegistration = null;
            bool found = false;
            lock (m_registrationsLock)
            {
                // Build the ordered candidate list: exact/server-uri single
                // matches first, then Any-strategy matches. Materialized so a
                // claimed or dead registration can be removed without mutating a
                // live enumeration.
                var candidates = new List<Registration>();
                foreach (Registration registration in m_registrations)
                {
                    if ((registration.ReverseConnectStrategy & ReverseConnectStrategy.Any) == 0 &&
                        MatchesSingle(registration, e))
                    {
                        candidates.Add(registration);
                    }
                }
                foreach (Registration registration in m_registrations)
                {
                    if ((registration.ReverseConnectStrategy & ReverseConnectStrategy.Any) != 0 &&
                        registration.EndpointUrl.Scheme
                            .Equals(e.EndpointUrl.Scheme, StringComparison.Ordinal))
                    {
                        candidates.Add(registration);
                    }
                }

                foreach (Registration registration in candidates)
                {
                    bool isAny =
                        (registration.ReverseConnectStrategy & ReverseConnectStrategy.Any) != 0;
                    if (registration.Wait != null)
                    {
                        // Tracked WaitForConnectionAsync: claim the transport by
                        // completing the waiter UNDER the lock. Only accept the
                        // event and keep it if the claim wins the race against a
                        // committed Stop/Dispose that may have faulted the same
                        // source; otherwise leave e.Accepted false so the
                        // listener reclaims/closes the transport. Either way the
                        // now-terminal source and its registration are removed
                        // from tracking.
                        bool claimed = registration.Wait.TrySetResult(e);
                        m_registrations.Remove(registration);
                        m_activeWaits.Remove(registration.Wait);
                        if (!claimed)
                        {
                            // The waiter was already claimed by a shutdown; keep
                            // searching for another live registration.
                            continue;
                        }
                        e.Accepted = true;
                        found = true;
                        LogAccepted(isAny, e);
                        break;
                    }

                    // External callback registration: accept under the lock and
                    // invoke the user callback outside it (unchanged semantics).
                    callbackRegistration = registration;
                    e.Accepted = true;
                    found = true;
                    LogAccepted(isAny, e);
                    if ((registration.ReverseConnectStrategy & ReverseConnectStrategy.Once) != 0)
                    {
                        m_registrations.Remove(registration);
                    }
                    break;
                }
            }

            callbackRegistration?.OnConnectionWaiting?.Invoke(sender, e);

            return found;

            static bool MatchesSingle(Registration registration, ConnectionWaitingEventArgs args)
            {
                return registration.EndpointUrl.Scheme
                        .Equals(args.EndpointUrl.Scheme, StringComparison.Ordinal) &&
                    (registration.ServerUri?
                        .Equals(args.ServerUri, StringComparison.Ordinal) == true ||
                        registration.EndpointUrl.Authority.Equals(args.EndpointUrl.Authority,
                            StringComparison.OrdinalIgnoreCase));
            }

            void LogAccepted(bool isAny, ConnectionWaitingEventArgs args)
            {
                if (isAny)
                {
                    m_logger.AcceptAnyReverseConnectionApprovalServerUri(
                        args.ServerUri,
                        args.EndpointUrl);
                }
                else
                {
                    m_logger.AcceptedReverseConnectionServerUriEndpointUrl(
                        args.ServerUri,
                        args.EndpointUrl);
                }
            }
        }

        /// <summary>
        /// Raised when a connection status changes.
        /// </summary>
        private void OnConnectionStatusChanged(object? sender, ConnectionStatusEventArgs e)
        {
            m_logger.ChannelStatusEndpointUrlChannelStatusClosed(
                e.EndpointUrl,
                e.ChannelStatus,
                e.Closed);
        }

        /// <summary>
        /// Registers entry into an <see cref="OnConnectionWaitingAsync"/>
        /// callback so a concurrent Stop/Dispose can wait for every in-flight
        /// callback that may still be holding a transport to complete before the
        /// terminal listener close/dispose tears the channels down. Returns
        /// <c>false</c> when a shutdown drain is already in progress: the caller
        /// must then not hold the transport and should return immediately,
        /// leaving the connection unaccepted so the listener reclaims/closes it.
        /// </summary>
        private bool TryEnterConnectionCallback()
        {
            lock (m_callbackLock)
            {
                if (m_callbacksDrainedSignal != null)
                {
                    return false;
                }
                m_activeCallbacks++;
                return true;
            }
        }

        /// <summary>
        /// Registers exit from an <see cref="OnConnectionWaitingAsync"/> callback
        /// and completes the shutdown drain signal once the last in-flight
        /// callback has finished.
        /// </summary>
        private void ExitConnectionCallback()
        {
            TaskCompletionSource<bool>? drained = null;
            lock (m_callbackLock)
            {
                if (--m_activeCallbacks == 0)
                {
                    drained = m_callbacksDrainedSignal;
                }
            }
            drained?.TrySetResult(true);
        }

        /// <summary>
        /// Cancels the registration token so held <see cref="OnConnectionWaitingAsync"/>
        /// callbacks unblock promptly instead of waiting the full hold time, and
        /// returns a task that completes once every in-flight callback has
        /// finished. Called by a committed Stop/Dispose BEFORE the terminal
        /// listener close so no callback is still holding a transport when the
        /// channels are torn down. While the returned signal is pending, new
        /// callbacks reject immediately (<see cref="TryEnterConnectionCallback"/>
        /// returns <c>false</c>).
        /// </summary>
        /// <param name="ct">
        /// A cancellation token honored while awaiting the drain. A cancellable
        /// Stop that carries a caller/host deadline passes its token so a blocked
        /// external callback that ignores the cancelled hold token cannot pin the
        /// stop past its deadline: the await throws
        /// <see cref="OperationCanceledException"/> and the caller restores a
        /// coherent Started state. A dispose or a transactional activation drain
        /// passes <see cref="CancellationToken.None"/> (the default) and always
        /// waits the drain to completion so no callback outlives the terminal
        /// listener close.
        /// </param>
        private Task<bool> DrainConnectionCallbacksAsync(CancellationToken ct = default)
        {
            TaskCompletionSource<bool> signal;
            lock (m_callbackLock)
            {
                signal = m_callbacksDrainedSignal ??= new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                if (m_activeCallbacks == 0)
                {
                    signal.TrySetResult(true);
                }
            }
            // Cancel the held-transport delays outside the callback lock so a
            // callback that (re)takes the registrations lock never contends with
            // this. Do not renew here: a committed stop renews via
            // ClearWaitingConnections, a rolled-back stop and a restart renew via
            // EndCallbackDrain, and disposal disposes the source in teardown.
            lock (m_registrationsLock)
            {
                try
                {
                    m_cts.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }
            }
            // A non-cancellable drain (dispose or a transactional activation)
            // waits unconditionally so the terminal close never races an
            // in-flight callback. A cancellable drain honors the caller token so
            // a still-held external callback cannot block the stop past its
            // deadline.
            return ct.CanBeCanceled
                ? signal.Task.WaitAsync(ct)
                : signal.Task;
        }

        /// <summary>
        /// Ends a shutdown drain started by <see cref="DrainConnectionCallbacksAsync"/>
        /// so a rolled-back or restarted manager accepts new callbacks again, and
        /// renews the drain-cancelled registration token so held callbacks can
        /// hold once more. A committed stop already renews the token via
        /// <see cref="ClearWaitingConnections"/>; renewing an uncancelled source
        /// is skipped. Disposal disposes the source in teardown, so a disposed
        /// manager never renews here.
        /// </summary>
        private void EndCallbackDrain()
        {
            lock (m_callbackLock)
            {
                m_callbacksDrainedSignal = null;
            }
            lock (m_registrationsLock)
            {
                if (Volatile.Read(ref m_disposed) == 0 && m_cts.IsCancellationRequested)
                {
                    CancellationTokenSource cts = m_cts;
                    m_cts = new CancellationTokenSource();
                    cts.Dispose();
                }
            }
        }

        /// <summary>
        /// Renew the cancellation token after use. Must be called while
        /// holding <see cref="m_registrationsLock"/>.
        /// </summary>
        private void CancelAndRenewTokenSource()
        {
            // Do not install a new source once disposal has begun: the teardown
            // disposes the current source under m_registrationsLock, so renewing
            // here would either race that disposal or leak an undisposed source.
            if (Volatile.Read(ref m_disposed) != 0)
            {
                return;
            }
            CancellationTokenSource cts = m_cts;
            m_cts = new CancellationTokenSource();
            cts.Cancel();
            cts.Dispose();
        }

        /// <summary>
        /// Reserves the lifecycle for a start operation. Must be called while
        /// holding <see cref="m_lock"/>. Rejects immediately when the manager
        /// is already started or another start/reload is in progress, marks the
        /// manager <see cref="ReverseConnectManagerState.Preparing"/>, and
        /// returns the reserved lifecycle version.
        /// </summary>
        private long ReserveStartLocked()
        {
            ThrowIfDisposedLocked();
            // Only a genuinely idle lifecycle may reserve a start. New, Stopped
            // and Faulted are the only reservable states; every in-flight
            // operation state (Preparing/Starting/Reloading/Stopping) and the
            // already-running Started state must reject so a lazy Wait can never
            // supersede an in-flight start, reload or stop (in particular the
            // Stopping window a reload passes through while closing the previous
            // listeners). Disposing/Disposed are rejected by
            // ThrowIfDisposedLocked above.
            if (m_state is not (ReverseConnectManagerState.New
                or ReverseConnectManagerState.Stopped
                or ReverseConnectManagerState.Faulted))
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidState,
                    "The reverse connect manager is already started or starting.");
            }
            m_state = ReverseConnectManagerState.Preparing;
            return ++m_lifecycleVersion;
        }

        /// <summary>
        /// Attempts to acquire the shutdown/supersession latch on behalf of a
        /// non-cancellable stop. Returns <c>true</c> only when this stop set the
        /// latch (so it, and only it, must clear it on completion). Returns
        /// <c>false</c> when the latch is already held by a concurrent stop or
        /// by a disposal (which owns it permanently).
        /// </summary>
        private bool TryAcquireShutdownLatch()
        {
            lock (m_lock)
            {
                if (m_shutdownLatchOwner != null)
                {
                    return false;
                }
                m_shutdownLatchOwner = s_stopLatch;
                return true;
            }
        }

        /// <summary>
        /// Clears the shutdown latch acquired by a non-cancellable stop, unless
        /// a concurrent disposal has taken it over (in which case it stays
        /// latched permanently).
        /// </summary>
        private void ReleaseShutdownLatch()
        {
            lock (m_lock)
            {
                if (ReferenceEquals(m_shutdownLatchOwner, s_stopLatch))
                {
                    m_shutdownLatchOwner = null;
                }
            }
        }

        /// <summary>
        /// Whether a <see cref="DisposeAsync"/> or a non-cancellable
        /// <see cref="StopServiceAsync"/> (or another shutdown that owns the
        /// lifecycle) has begun while an activation rollback or a cancellable
        /// stop still holds the lifecycle gate. Must be called while holding
        /// <see cref="m_lock"/>. A rolled-back activation/stop consults this
        /// before reopening listeners or writing
        /// <see cref="ReverseConnectManagerState.Started"/> so it never
        /// resurrects a manager that is being disposed, nor reopens listeners a
        /// queued non-cancellable stop (which set the shutdown latch before its
        /// gate wait) is about to tear down. Both the stop latch
        /// (<see cref="s_stopLatch"/>) and the dispose latch
        /// (<see cref="s_disposeLatch"/>) are recognized, so any owned shutdown
        /// supersedes the rollback.
        /// </summary>
        private bool ShutdownOwnsLifecycleLocked()
        {
            return Volatile.Read(ref m_disposed) != 0 ||
                m_state is ReverseConnectManagerState.Disposing
                    or ReverseConnectManagerState.Disposed ||
                m_shutdownLatchOwner != null;
        }

        /// <summary>
        /// Captures the newest in-flight or pending watcher reload candidate so
        /// a rolled-back stop can re-queue it against the restored lifecycle
        /// version. Must be called while holding <see cref="m_lock"/>. Returns
        /// <c>null</c> when no reload is in flight or pending.
        /// </summary>
        private (ApplicationConfiguration Config, int Generation)? CaptureReloadCandidateLocked()
        {
            ReloadRequest? candidate = m_pendingReload ?? m_inFlightReload;
            if (candidate == null)
            {
                return null;
            }
            return (candidate.Configuration, candidate.WatcherGeneration);
        }

        /// <summary>
        /// Whether an in-flight activation has been superseded by a shutdown
        /// (<c>StopServiceAsync</c>/<c>DisposeAsync</c>) rather than by a
        /// caller/provider cancellation. Must be called while holding
        /// <see cref="m_lock"/>. A shutdown supersession bumps the lifecycle
        /// version, marks the manager disposing/disposed, sets the shutdown
        /// latch, or aborts the active transaction directly.
        /// </summary>
        private bool IsShutdownSupersededLocked(ActiveTransaction transaction, long myVersion)
        {
            return m_lifecycleVersion != myVersion ||
                m_state is ReverseConnectManagerState.Disposing
                    or ReverseConnectManagerState.Disposed ||
                Volatile.Read(ref m_disposed) != 0 ||
                m_shutdownLatchOwner != null ||
                transaction.AbortedByShutdown;
        }

        /// <summary>
        /// Transition to <see cref="ReverseConnectManagerState.Faulted"/> if
        /// this operation still owns the lifecycle (not superseded/disposed).
        /// </summary>
        private void FaultIfOwned(long myVersion)
        {
            lock (m_lock)
            {
                if (m_state is not ReverseConnectManagerState.Disposed
                    and not ReverseConnectManagerState.Disposing &&
                    m_lifecycleVersion == myVersion)
                {
                    m_state = ReverseConnectManagerState.Faulted;
                }
            }
        }

        /// <summary>
        /// Awaits the failed-start finalization test seam if one is installed.
        /// A no-op in production (the hook is always <c>null</c>); tests use it
        /// to hold a start between <see cref="FaultIfOwned"/> and the mapped
        /// throw so the tracked task stays incomplete while the state is
        /// already Faulted.
        /// </summary>
        private Task InvokeStartFailureFinalizationHookForTestAsync()
        {
            Func<Task>? hook = StartFailureFinalizationHookForTest;
            return hook == null ? Task.CompletedTask : hook();
        }

        /// <summary>
        /// Maps a lifecycle failure to the historical
        /// <see cref="ServiceResultException"/> shape.
        /// </summary>
        private Exception MapStartException(Exception e)
        {
            if (e is ServiceResultException or OperationCanceledException or ObjectDisposedException)
            {
                return e;
            }
            m_logger.UnexpectedErrorStartingReverseConnectManager(e);
            var error = ServiceResult.Create(
                e,
                StatusCodes.BadInternalError,
                "Unexpected error starting reverse connect manager");
            return new ServiceResultException(error);
        }

        /// <summary>
        /// Throws if the manager has been disposed.
        /// </summary>
        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref m_disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(ReverseConnectManager));
            }
        }

        /// <summary>
        /// Fails fast when a stop or dispose is re-entered directly by this
        /// manager's own in-flight connection-waiting callback (or a
        /// synchronous wait on such a call). The teardown drains in-flight
        /// callbacks, so awaiting it from within the very callback it is waiting
        /// for would deadlock. A normal external Stop/Dispose issued from a
        /// different async flow never observes the per-flow marker and still
        /// drains callbacks. A child task deferred past the callback whose
        /// originating callback flow already completed observes an inactive
        /// scope and is not rejected. The manager is left fully coherent so it
        /// can be stopped or disposed externally afterwards.
        /// </summary>
        /// <param name="disposing">
        /// <c>true</c> for a Dispose/DisposeAsync call (surfaces
        /// <see cref="InvalidOperationException"/>); <c>false</c> for a
        /// StopServiceAsync call (surfaces <see cref="ServiceResultException"/>
        /// with <see cref="StatusCodes.BadInvalidState"/>).
        /// </param>
        private void ThrowIfInvokedFromConnectionCallback(bool disposing)
        {
            if (m_activeCallbackOwner.Value?.Active != true)
            {
                return;
            }
            if (disposing)
            {
                throw new InvalidOperationException(
                    "The reverse connect manager cannot be disposed from within " +
                    "its own connection-waiting callback.");
            }
            throw new ServiceResultException(
                StatusCodes.BadInvalidState,
                "The reverse connect manager cannot be stopped from within its " +
                "own connection-waiting callback.");
        }

        /// <summary>
        /// Throws if disposed while the state lock is held.
        /// </summary>
        private void ThrowIfDisposedLocked()
        {
            if (m_state is ReverseConnectManagerState.Disposed
                or ReverseConnectManagerState.Disposing ||
                Volatile.Read(ref m_disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(ReverseConnectManager));
            }
        }

        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        private Type? m_configType;

        private readonly Lock m_lock = new();
        // The lifecycle gate is intentionally never disposed: queued callers
        // may still acquire/release it before observing the disposed state, so
        // disposing it here would risk ObjectDisposedException on the gate.
        [SuppressMessage("Reliability", "CA2213:Disposable fields should be disposed",
            Justification = "Not disposed by design; queued callers may still touch the gate after DisposeAsync.")]
        private readonly SemaphoreSlim m_gate = new(1, 1);
        private readonly TimeProvider m_timeProvider;
        private readonly ILogger m_logger;
        private readonly ITelemetryContext m_telemetry;
        private ConfigurationWatcher? m_configurationWatcher;
        private int m_watcherGeneration;
        private ApplicationType m_applicationType;
        private ApplicationConfiguration? m_appConfig;
        private ReverseConnectClientConfiguration? m_configuration;
        private Dictionary<Uri, ReverseConnectInfo> m_endpointUrls;
        private Dictionary<Uri, ReverseConnectInfo> m_manualEndpoints;
        private ReverseConnectManagerState m_state;
        private long m_lifecycleVersion;
        // Monotonic counter advanced only by a committed Stop or a Dispose
        // (never by a reload commit, which only advances m_lifecycleVersion).
        // Each queued ReloadRequest captures it at enqueue time; the reload
        // drain rejects a request whose captured epoch no longer matches so a
        // reload queued before a shutdown never reopens a listener, while an
        // overlapping reload queued in the same epoch is still applied
        // (latest-wins). Guarded by m_lock.
        private long m_teardownEpoch;
        private readonly AsyncLocal<LegacyCaptureContext?> m_legacyCapture = new();
        // Operation-owner marker for this manager's in-flight startup pipeline.
        // Set (per async flow) around preparation/provider callbacks so a
        // re-entrant EnsureStartedAsync/RegisterWaitingConnectionAsync issued by
        // a provider or legacy hook fails fast instead of awaiting this start's
        // own shared task. Per-instance, so nested unrelated manager instances
        // (whose marker is unset) still start normally.
        private readonly AsyncLocal<OperationScope?> m_activeStartupOwner = new();
        private Task? m_startTask;
        private CancellationTokenSource? m_startCts;
        // Manager-owned cancellation for an in-flight explicit StartServiceAsync,
        // linked to the caller token and published under m_lock so hosted
        // cancellation (CancelPendingStart) can abort an explicit start joined
        // by EnsureStartedAsync - not merely the joining awaiter. Owned and
        // disposed by StartServiceExplicitAsync after its start completes.
        [SuppressMessage("Reliability", "CA2213:Disposable fields should be disposed",
            Justification = "Owned and disposed by StartServiceExplicitAsync, which creates the " +
            "linked source, publishes it here, and disposes it in its finally after the start completes.")]
        private CancellationTokenSource? m_activeStartCts;
        // The in-flight start task shared by explicit StartServiceAsync and the
        // lazy shared start. EnsureStartedAsync awaits it so it only returns
        // once the manager is Started (or the tracked start fails). For a lazy
        // start this is the same task as m_startTask; for an explicit start
        // m_startTask stays null (an explicit start has no manager-owned CTS).
        private Task? m_currentStartTask;
        // The in-flight configuration reload task, published for the whole
        // duration of a ReloadConfigurationAsync so a lazy EnsureStartedAsync
        // that races the reload's transitional window (Reloading/Stopping/
        // Starting) awaits its completion instead of returning premature
        // success. Cleared when the reload completes. Guarded by m_lock.
        private Task? m_activeReloadTask;
        // Manager-owned cancellation for the in-flight reload drain loop.
        // Created together with the loop and cancelled by a committed
        // Stop/Dispose so a cooperative provider/activation running under a
        // reload observes cancellation and exits. Owned (and disposed) by the
        // reload loop that created it; a shutdown only ever cancels it (guarded
        // against ObjectDisposedException). Guarded by m_lock.
        [SuppressMessage("Reliability", "CA2213:Disposable fields should be disposed",
            Justification = "Owned and disposed by the RunReloadLoopAsync instance that created it " +
            "(single owner) in its finally; a committed Stop/Dispose only cancels it.")]
        private CancellationTokenSource? m_activeReloadCts;
        // The teardown epoch under which the current reload drain loop was
        // created. A committed Stop/Dispose advances m_teardownEpoch and
        // detaches the loop, so a stale loop from a prior epoch discards its
        // result and never clobbers a newer loop's state, and a lazy
        // EnsureStartedAsync never awaits a reload task from a superseded
        // epoch. Guarded by m_lock.
        private long m_activeReloadEpoch;
        // Newest queued reload request (latest-wins). A single serialized loop
        // (RunReloadLoopAsync) drains it outside the lifecycle gate so
        // overlapping reloads never run concurrently and an older commit never
        // invalidates/drops a newer queued reload. Guarded by m_lock.
        private ReloadRequest? m_pendingReload;
        // The reload request currently being drained by RunReloadLoopAsync (the
        // most recently dequeued one). Kept so a rolled-back cancelled stop can
        // re-queue the reload candidate it stranded by bumping the lifecycle
        // version. Guarded by m_lock; null while the loop is idle.
        private ReloadRequest? m_inFlightReload;
        // True while the reload drain loop is running. Guarded by m_lock.
        private bool m_reloadLoopActive;
        // True while a StopServiceAsync is actively transitioning the manager
        // down (from the Stopping transition until the stop completes or rolls
        // back). A lazy EnsureStartedAsync observing this rejects deterministically
        // rather than returning success and registering an inert waiter. Guarded
        // by m_lock. A completed stop (state Stopped) clears this and is
        // restartable via a fresh lazy start.
        private bool m_stopInProgress;
        // Generation bookkeeping for a hosted-start cancellation latch. The
        // hosted service brackets each IHostedService.StartAsync in a hosted
        // startup scope (BeginHostedStartup/EndHostedStartup) that publishes a
        // monotonically increasing generation as the active one. A
        // CancelPendingStart that fires before the shared start task (and its
        // m_startCts) is published latches the ACTIVE generation so the pending
        // cancellation is applied atomically only when THAT generation's start
        // is created. Tying the latch to an active hosted startup generation
        // means a late/stale cancellation observed while no hosted startup is in
        // flight (m_activeHostedStartGeneration == 0) can never latch and poison
        // an unrelated later start. Guarded by m_lock.
        private long m_hostedStartGeneration;
        private long m_activeHostedStartGeneration;
        private long m_startCancelLatchGeneration;
        // Shutdown/supersession latch owner. Set by a non-cancellable
        // StopServiceAsync (s_stopLatch) or a DisposeAsync (s_disposeLatch)
        // BEFORE its transaction lookup/gate wait, and checked by ActivateAsync
        // immediately after acquiring the gate so an activation that acquired
        // the gate first (leaving the shutdown queued behind it) aborts without
        // opening a listener. A stop clears its own latch on completion;
        // disposal keeps it latched permanently. Guarded by m_lock.
        private object? m_shutdownLatchOwner;
        // Manager-owned transaction marker for the in-flight activation. Kept
        // visible while ActivateAsync awaits listener close/open so Stop/Dispose
        // can abort a blocked open BEFORE waiting on the lifecycle gate. The
        // abort also records that the supersession was a shutdown so the
        // activation neither restores nor non-cancellably reopens the previous
        // listeners.
        [SuppressMessage("Reliability", "CA2213:Disposable fields should be disposed",
            Justification = "Owned and disposed by ActivateAsync, which creates the linked source, " +
            "assigns it here, and disposes it in its finally before clearing the field.")]
        private ActiveTransaction? m_activeTransaction;
        private TaskCompletionSource<bool>? m_disposeSignal;
        private ApplicationConfiguration? m_initialConfiguration;
        private Func<CancellationToken, Task<ApplicationConfiguration>>?
            m_initialConfigurationFactory;
        // Captured SourceFilePath of a file-backed initial configuration so a
        // lazy restart after a stop re-reads it (see
        // RestartFromInitialSourceFileAsync). Guarded by m_lock.
        private string? m_initialConfigurationSourceFilePath;
        // Optional decorator supplied by the DI activator that reapplies the
        // configured overlay (in particular the ClientReverseConnectOptions
        // reverse-connect endpoints) onto a configuration reloaded from
        // SourceFilePath. Reused so a file-backed lazy restart after a stop
        // preserves the DI overlay instead of losing it to a plain file load.
        // Guarded by m_lock.
        private Func<ApplicationConfiguration, ApplicationConfiguration>?
            m_initialConfigurationDecorator;
        private bool m_initialStartRequested;
        private bool m_initialConfigurationMissing;
        private int m_disposed;
        private int m_legacyDisposeInvoked;
        private readonly List<Registration> m_registrations;
        private readonly Lock m_registrationsLock = new();
        // Pending WaitForConnectionAsync completion sources, tracked under
        // m_registrationsLock so a committed StopServiceAsync/DisposeAsync can
        // fault every in-flight wait promptly (BadInvalidState) instead of
        // leaving it to time out. Inserted atomically with the waiter's
        // registration and removed when the wait completes.
        private readonly List<TaskCompletionSource<ITransportWaitingConnection>> m_activeWaits =
            [];
        private CancellationTokenSource m_cts;

        // Tracks in-flight OnConnectionWaitingAsync callbacks so a Stop/Dispose
        // can cancel their held transport delay and wait for every callback that
        // may still be holding a transport to finish, reject or reinsert before
        // the terminal listener close/dispose tears the channels down. Guarded by
        // m_callbackLock, a distinct lock from m_registrationsLock so a callback
        // that takes the registrations lock (MatchRegistration) never runs it
        // while holding this one, and a shutdown draining callbacks never blocks
        // registration matching. m_callbacksDrainedSignal is non-null only while
        // a shutdown drain is in progress; while set, new callbacks reject
        // immediately instead of holding a transport that is about to be torn
        // down.
        private readonly Lock m_callbackLock = new();
        private int m_activeCallbacks;
        private TaskCompletionSource<bool>? m_callbacksDrainedSignal;
        // Per-manager marker set (per async flow) around the invocation of a
        // user connection-waiting callback so a Stop/Dispose/StopServiceAsync
        // re-entered directly by that callback (or a synchronous wait on such a
        // call) fails fast instead of letting the teardown wait for the very
        // callback that triggered it (which would deadlock: the drain never
        // reaches zero while the callback is blocked on the teardown). Being a
        // per-instance AsyncLocal, an external Stop/Dispose from a different
        // async flow never observes the marker and still drains callbacks
        // normally.
        private readonly AsyncLocal<OperationScope?> m_activeCallbackOwner = new();

        // Distinct sentinels identifying the shutdown-latch owner so a stop only
        // clears a latch it set and a disposal latch is never cleared.
        private static readonly object s_stopLatch = new();
        private static readonly object s_disposeLatch = new();
        // Monotonic generation source for OperationScope instances, so nested
        // startup/callback scopes carry a unique identifier for diagnostics.
        private long m_operationScopeGeneration;

        /// <summary>
        /// Deterministic test seam invoked by <see cref="ActivateAsync"/>
        /// immediately after the lifecycle gate is acquired and before any
        /// <see cref="ActiveTransaction"/> is published. Lets a test pause an
        /// activation inside the gate so it can drive a concurrent Stop/Dispose
        /// and prove the shutdown latch aborts the activation without opening a
        /// listener. Always <c>null</c> in production.
        /// </summary>
        internal Func<Task>? GateAcquiredForTest { get; set; }

        /// <summary>
        /// Deterministic test seam invoked by the failed-start finalization
        /// path (both the shared initial factory catch in
        /// <see cref="RunInitialStartAsync"/> and the prepare/activate catch in
        /// <see cref="StartServiceCoreAsync"/>) AFTER
        /// <see cref="FaultIfOwned"/> has run but BEFORE the mapped exception is
        /// thrown (i.e. before the tracked start task actually completes). Lets
        /// a test hold a start in a state where the lifecycle is already
        /// Faulted yet the failing task has not yet published its exception, so
        /// it can prove a concurrent <see cref="EnsureStartedAsync"/> awaits the
        /// SAME failing task (no premature return, no overlapping retry) and
        /// only reserves a fresh start once the task completes. Always
        /// <c>null</c> in production.
        /// </summary>
        internal Func<Task>? StartFailureFinalizationHookForTest { get; set; }

        /// <summary>
        /// Deterministic test seam invoked by <see cref="RestoreAfterFailureAsync"/>
        /// and <see cref="RestoreStartedAfterCanceledStopAsync"/> after the
        /// shutdown-only restore transaction is published and BEFORE the pre-open
        /// shutdown recheck. Lets a test start a concurrent Dispose in that exact
        /// window and prove the rollback aborts without reopening a listener (the
        /// disposal-starts-between-rollback-check-and-open scenario). Always
        /// <c>null</c> in production.
        /// </summary>
        internal Func<Task>? RestoreReopenHookForTest { get; set; }

        /// <summary>
        /// Deterministic test seam invoked by
        /// the synchronous or asynchronous registration path after any lazy
        /// <see cref="EnsureStartedAsync"/> call has returned but before the
        /// registration is inserted. Lets a test drive a concurrent Stop/Dispose
        /// that commits inside that exact window and prove the registration is
        /// rejected without leaving an inert waiter behind. Always <c>null</c>
        /// in production.
        /// </summary>
        internal Func<Task>? BeforeRegisterWaitForTest { get; set; }

        /// <summary>
        /// Deterministic test seam replacing the file load performed by
        /// <see cref="RestartFromInitialSourceFileAsync"/> for a file-backed
        /// lazy restart and by <see cref="OnConfigurationChangedAsync"/> for a
        /// watcher-triggered reload. Receives the captured source file path,
        /// application type and configuration type and returns the reloaded
        /// configuration, so a test can exercise the restart/watcher reload
        /// behavior without a real configuration file. Always <c>null</c> in
        /// production, where
        /// <see cref="ApplicationConfiguration.LoadAsync(FileInfo, ApplicationType, Type, ITelemetryContext, CancellationToken)"/>
        /// is used.
        /// </summary>
        internal Func<string, ApplicationType, Type?, CancellationToken,
            Task<ApplicationConfiguration>>? ConfigurationFileLoaderForTest
        { get; set; }
    }

    /// <summary>
    /// Source-generated log messages for <see cref="ReverseConnectManager"/>.
    /// </summary>
    internal static partial class ReverseConnectManagerLog
    {
        [LoggerMessage(EventId = ClientEventIds.ReverseConnectManager + 0, Level = LogLevel.Error,
            Message = "Could not load updated configuration file from: {FilePath}")]
        public static partial void CouldNotLoadUpdatedConfigurationFile(
            this ILogger logger,
            Exception? exception,
            string filePath);

        [LoggerMessage(EventId = ClientEventIds.ReverseConnectManager + 1, Level = LogLevel.Error,
            Message = "Failed to Open {Uri}.")]
        public static partial void FailedOpenUri(this ILogger logger, Exception? exception, Uri? uri);

        [LoggerMessage(EventId = ClientEventIds.ReverseConnectManager + 2, Level = LogLevel.Error,
            Message = "Failed to Close {Uri}.")]
        public static partial void FailedCloseUri(this ILogger logger, Exception? exception, Uri? uri);

        [LoggerMessage(EventId = ClientEventIds.ReverseConnectManager + 3, Level = LogLevel.Error,
            Message = "Unexpected error starting reverse connect manager.")]
        public static partial void UnexpectedErrorStartingReverseConnectManager(
            this ILogger logger,
            Exception? exception);

        [LoggerMessage(EventId = ClientEventIds.ReverseConnectManager + 4, Level = LogLevel.Error,
            Message = "No listener was found for endpoint {EndpointUrl}.")]
        public static partial void NoListenerFoundEndpointEndpointUrl(
            this ILogger logger,
            Exception? exception,
            Uri endpointUrl);

        [LoggerMessage(EventId = ClientEventIds.ReverseConnectManager + 5, Level = LogLevel.Information,
            Message = "Holding reverse connection: {ServerUri} {EndpointUrl}")]
        public static partial void HoldingReverseConnectionServerUriEndpointUrl(
            this ILogger logger,
            string serverUri,
            Uri endpointUrl);

        [LoggerMessage(EventId = ClientEventIds.ReverseConnectManager + 6, Level = LogLevel.Information,
            Message = "Matched reverse connection {ServerUri} {EndpointUrl} after {Duration}ms")]
        public static partial void MatchedReverseConnectionServerUriEndpointUrlAfter(
            this ILogger logger,
            string serverUri,
            Uri endpointUrl,
            long duration);

        [LoggerMessage(EventId = ClientEventIds.ReverseConnectManager + 7, Level = LogLevel.Information,
            Message = "{Action} reverse connection: {ServerUri} {EndpointUrl} after {Duration}ms")]
        public static partial void ActionReverseConnectionServerUriEndpointUrlAfter(
            this ILogger logger,
            string action,
            string serverUri,
            Uri endpointUrl,
            long duration);

        [LoggerMessage(EventId = ClientEventIds.ReverseConnectManager + 8, Level = LogLevel.Information,
            Message = "Accepted reverse connection: {ServerUri} {EndpointUrl}")]
        public static partial void AcceptedReverseConnectionServerUriEndpointUrl(
            this ILogger logger,
            string serverUri,
            Uri endpointUrl);

        [LoggerMessage(EventId = ClientEventIds.ReverseConnectManager + 9, Level = LogLevel.Information,
            Message = "Accept any reverse connection for approval: {ServerUri} {EndpointUrl}")]
        public static partial void AcceptAnyReverseConnectionApprovalServerUri(
            this ILogger logger,
            string serverUri,
            Uri endpointUrl);

        [LoggerMessage(EventId = ClientEventIds.ReverseConnectManager + 10, Level = LogLevel.Information,
            Message = "Channel status: {EndpointUrl} {ChannelStatus} {Closed}")]
        public static partial void ChannelStatusEndpointUrlChannelStatusClosed(
            this ILogger logger,
            Uri endpointUrl,
            ServiceResult channelStatus,
            bool closed);

        [LoggerMessage(EventId = ClientEventIds.ReverseConnectManager + 11, Level = LogLevel.Warning,
            Message = "Reverse connect reload failed; restored the previously running listeners.")]
        public static partial void ReverseConnectServiceReloadFailedRestored(
            this ILogger logger,
            Exception? exception);

        [LoggerMessage(EventId = ClientEventIds.ReverseConnectManager + 12, Level = LogLevel.Error,
            Message = "Reverse connect reload failed and the previous listeners could not be restored.")]
        public static partial void ReverseConnectServiceRestoreFailed(
            this ILogger logger,
            Exception? exception);

        [LoggerMessage(EventId = ClientEventIds.ReverseConnectManager + 13, Level = LogLevel.Error,
            Message = "Failed to reload reverse connect configuration from: {FilePath}")]
        public static partial void ReverseConnectConfigurationReloadFailed(
            this ILogger logger,
            Exception? exception,
            string filePath);

        [LoggerMessage(EventId = ClientEventIds.ReverseConnectManager + 14, Level = LogLevel.Information,
            Message = "Reverse connect configuration reload (watcher generation {WatcherGeneration}, " +
            "lifecycle version {LifecycleVersion}) superseded by a shutdown; not applied.")]
        public static partial void ReverseConnectConfigurationReloadSuperseded(
            this ILogger logger,
            int watcherGeneration,
            long lifecycleVersion);
    }
}
