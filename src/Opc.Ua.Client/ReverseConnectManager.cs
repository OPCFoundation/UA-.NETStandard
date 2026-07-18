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
                bool configEntry)
            {
                EndpointUrl = endpointUrl;
                ReverseConnectHost = reverseConnectHost;
                State = ReverseConnectHostState.New;
                ConfigEntry = configEntry;
            }

            public readonly Uri EndpointUrl;
            public ReverseConnectHost ReverseConnectHost;
            public ReverseConnectHostState State;
            public bool ConfigEntry;
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
            // Isolated sync-over-async bridge for the obsolete IDisposable
            // boundary. Task.Run moves the shared disposal off any captured
            // synchronization context so a provider/derived-cleanup await that
            // posts back to the caller's context cannot deadlock. The shared
            // teardown invokes the protected Dispose(bool) override exactly
            // once at the end, so this wrapper must never call it directly.
            Task.Run(GetOrStartDisposeTask).GetAwaiter().GetResult();
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
            await GetOrStartDisposeTask().ConfigureAwait(false);
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
        /// configuration is loaded.
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
            lock (m_lock)
            {
                if (!ReferenceEquals(sender, m_configurationWatcher))
                {
                    return;
                }
                generation = m_watcherGeneration;
                applicationType = m_applicationType;
                configType = m_configType;
            }

            try
            {
                ApplicationConfiguration configuration = await ApplicationConfiguration
                    .LoadAsync(
                        new FileInfo(args.FilePath),
                        applicationType,
                        configType,
                        m_telemetry)
                    .ConfigureAwait(false);

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
                    if (m_startCancelRequested)
                    {
                        m_startCancelRequested = false;
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
            // operations on the same flow are unaffected.
            object? previousStartupOwner = m_activeStartupOwner.Value;
            m_activeStartupOwner.Value = new object();
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
                throw MapStartException(e);
            }
            finally
            {
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
            // own shared m_startTask, which would deadlock.
            if (m_activeStartupOwner.Value != null)
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

                if (m_initialConfigurationMissing)
                {
                    throw new InvalidOperationException(
                        "OpcUaClientOptions.Configuration must be set before starting " +
                        "the reverse connect manager.");
                }

                ApplicationConfiguration? initial = m_initialConfiguration;
                if (!m_initialStartRequested || initial == null)
                {
                    // Nothing to auto-start (direct construction or manual start).
                    return;
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
                        if (m_startCancelRequested)
                        {
                            m_startCancelRequested = false;
                            startCts.Cancel();
                        }
                        m_startTask = RunInitialStartAsync(
                            initial,
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
            return task == null || task.IsCompleted ? null : task;
        }

        /// <summary>
        /// Returns the in-flight start task (explicit or shared lazy) while a
        /// start is genuinely preparing/starting, or <c>null</c> otherwise.
        /// Must be called while holding <see cref="m_lock"/>. A completed
        /// tracked task is treated as absent so a stale (failed/superseded/
        /// finished) start is never awaited.
        /// </summary>
        private Task? InFlightStartTaskLocked()
        {
            Task? task = m_currentStartTask;
            if (task == null || task.IsCompleted)
            {
                return null;
            }
            if (m_state is ReverseConnectManagerState.Preparing
                or ReverseConnectManagerState.Starting)
            {
                return task;
            }
            return null;
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
                    m_state is not ReverseConnectManagerState.Started
                    and not ReverseConnectManagerState.Disposed
                    and not ReverseConnectManagerState.Disposing
                    and not ReverseConnectManagerState.Stopped)
                {
                    // Neither a shared lazy start nor an explicit start has
                    // published its token yet. Latch the cancellation so the
                    // shared lazy start applies it atomically when it creates
                    // the manager-owned start token (EnsureStartedAsync) or an
                    // explicit start consumes it at reservation
                    // (StartServiceExplicitAsync). An in-flight explicit start
                    // whose token is already visible is cancelled directly
                    // below, so the latch is NOT set - a later restart is never
                    // spuriously cancelled.
                    m_startCancelRequested = true;
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
            ApplicationConfiguration configuration,
            long reservedVersion,
            CancellationToken ct)
        {
            await Task.Yield();
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
            }

            activeTransaction?.AbortForShutdown();
            try
            {
                pendingStart?.Cancel();
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
                CancellationTokenSource? pendingStart;
                Task? pendingStartTask;
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

                List<ReverseConnectInfo> live;
                lock (m_lock)
                {
                    live = [.. m_endpointUrls.Values];
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
                    m_state = ReverseConnectManagerState.Stopped;
                    m_lifecycleVersion++;
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
                ReverseConnectStrategy = ReverseConnectStrategy.Once
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
            if (RequiresLazyInitialStart())
            {
                Task.Run(() => EnsureStartedAsync()).GetAwaiter().GetResult();
            }

            return RegisterWaitingConnectionCore(
                endpointUrl,
                serverUri,
                onConnectionWaiting,
                reverseConnectStrategy);
        }

        /// <summary>
        /// Adds a waiting reverse connection registration without starting the
        /// manager. Shared by the public registration entry points.
        /// </summary>
        private int RegisterWaitingConnectionCore(
            Uri endpointUrl,
            string? serverUri,
            EventHandler<ConnectionWaitingEventArgs> onConnectionWaiting,
            ReverseConnectStrategy reverseConnectStrategy)
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
        private bool RequiresLazyInitialStart()
        {
            if (!m_initialStartRequested ||
                m_initialConfigurationMissing ||
                m_initialConfiguration == null)
            {
                return false;
            }
            lock (m_lock)
            {
                return m_state != ReverseConnectManagerState.Started;
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
            return RegisterWaitingConnectionCore(
                endpointUrl,
                serverUri,
                onConnectionWaiting,
                reverseConnectStrategy);
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
            m_initialConfiguration = configuration ?? throw new ArgumentNullException(nameof(configuration));
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

            // Publish the reload task for its whole duration so a lazy
            // EnsureStartedAsync that races the reload's transitional window
            // (Reloading/Stopping/Starting) awaits its completion rather than
            // returning premature success. The core yields first so its
            // synchronous prefix never runs while the publishing lock is held.
            Task reloadTask;
            lock (m_lock)
            {
                reloadTask = ReloadConfigurationCoreAsync(configuration, watcherGeneration, ct);
                m_activeReloadTask = reloadTask;
            }
            return AwaitAndClearReloadAsync(reloadTask);
        }

        /// <summary>
        /// Awaits a published reload task and clears the shared active-reload
        /// field when it completes.
        /// </summary>
        private async Task AwaitAndClearReloadAsync(Task reloadTask)
        {
            try
            {
                await reloadTask.ConfigureAwait(false);
            }
            finally
            {
                lock (m_lock)
                {
                    if (ReferenceEquals(m_activeReloadTask, reloadTask))
                    {
                        m_activeReloadTask = null;
                    }
                }
            }
        }

        /// <summary>
        /// Runs the reload prepare/activate pipeline. Yields first so the
        /// synchronous prefix never runs while the caller of
        /// <see cref="ReloadConfigurationAsync"/> still holds the state lock.
        /// Failures are logged and swallowed.
        /// </summary>
        private async Task ReloadConfigurationCoreAsync(
            ApplicationConfiguration configuration,
            int watcherGeneration,
            CancellationToken ct)
        {
            await Task.Yield();

            if (Volatile.Read(ref m_disposed) != 0)
            {
                return;
            }

            long myVersion;
            lock (m_lock)
            {
                if (watcherGeneration != m_watcherGeneration)
                {
                    return;
                }
                myVersion = m_lifecycleVersion;
            }

            PreparedConfiguration? prepared = null;
            try
            {
                prepared = await PrepareAsync(configuration, null, ct).ConfigureAwait(false);

                lock (m_lock)
                {
                    if (watcherGeneration != m_watcherGeneration)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadInvalidState,
                            "Reverse connect configuration reload superseded.");
                    }
                }

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

            var candidate = new Dictionary<Uri, ReverseConnectInfo>();
            lock (m_lock)
            {
                foreach (KeyValuePair<Uri, ReverseConnectInfo> manual in m_manualEndpoints)
                {
                    candidate[manual.Key] = manual.Value;
                }
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

                // Check the shutdown/supersession latch immediately after
                // acquiring the gate and BEFORE publishing or awaiting any
                // ActiveTransaction. A non-cancellable Stop or a Dispose sets the
                // latch before its own transaction lookup/gate wait, so an
                // activation that acquired the gate first (leaving the shutdown
                // queued behind it, its ActiveTransaction not yet visible) still
                // observes the pending shutdown here and aborts WITHOUT opening
                // any listener. The queued shutdown then acquires the gate and
                // finalizes the lifecycle.
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
                }

                ApplicationConfiguration? previousAppConfig;
                bool previousWasStarted;
                lock (m_lock)
                {
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
                }

                List<ReverseConnectInfo> previousLive;
                List<(Uri Url, bool ConfigEntry)> previousDescriptors;
                lock (m_lock)
                {
                    previousLive = [.. m_endpointUrls.Values];
                    previousDescriptors =
                        [.. previousLive.Select(info => (info.EndpointUrl, info.ConfigEntry))];
                }

                // Publish the manager-owned active transaction BEFORE any
                // awaited close/open so a Stop/Dispose can observe and abort it
                // BEFORE waiting on the lifecycle gate: aborting cancels the
                // operation token (unblocking a cooperative listener open so the
                // gate can be acquired) and records that a shutdown, not a
                // caller/provider cancellation, superseded this activation.
                transaction = new ActiveTransaction(
                    CancellationTokenSource.CreateLinkedTokenSource(ct));
                CancellationToken operationToken = transaction.Token;
                lock (m_lock)
                {
                    m_activeTransaction = transaction;
                }

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
                            // file-backed reload semantics are preserved.
                            if (m_initialStartRequested && !m_initialConfigurationMissing &&
                                m_initialConfiguration != null)
                            {
                                m_initialConfiguration = prepared.ApplicationConfiguration;
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

            Dictionary<Uri, ReverseConnectInfo>? restored = null;
            try
            {
                restored = RecreateHosts(previousDescriptors, previousAppConfig);
                await OpenHostsAsync([.. restored.Values], CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception restoreError)
            {
                if (restored != null)
                {
                    foreach (ReverseConnectInfo info in restored.Values)
                    {
                        info.State = ReverseConnectHostState.Errored;
                    }
                    // Discard the failed restore set: dispose the configured
                    // (ConfigEntry) hosts outright so no recreated listener
                    // leaks, while reused manual endpoint hosts are only closed
                    // so their persistent ownership survives for a later restart.
                    await CloseAndDisposeOwnedHostsAsync(
                        [.. restored.Values],
                        CancellationToken.None).ConfigureAwait(false);
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

            lock (m_lock)
            {
                m_endpointUrls = restored;
                m_state = ReverseConnectManagerState.Started;
                m_lifecycleVersion++;
            }
            m_logger.ReverseConnectServiceReloadFailedRestored(openError);
        }

        /// <summary>
        /// Recreates hosts from previously running descriptors, reusing the
        /// persistent manual endpoint hosts by object identity where possible
        /// and creating configured hosts from the snapshot application config.
        /// </summary>
        private Dictionary<Uri, ReverseConnectInfo> RecreateHosts(
            List<(Uri Url, bool ConfigEntry)> descriptors,
            ApplicationConfiguration? appConfig)
        {
            var dict = new Dictionary<Uri, ReverseConnectInfo>();
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
                    manual.State = ReverseConnectHostState.Closed;
                    dict[url] = manual;
                }
                else
                {
                    dict[url] = CreateEndpointInfo(url, configEntry, appConfig);
                }
            }
            return dict;
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
        /// state after a stop whose listener close was cancelled. Reopens the
        /// still-running listener set non-cancellably so a subsequent stop can
        /// complete cleanly; if the reopen fails the manager is faulted with no
        /// listeners.
        /// </summary>
        private async Task RestoreStartedAfterCanceledStopAsync(
            List<ReverseConnectInfo> live)
        {
            try
            {
                await OpenHostsAsync(live, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception reopenError)
            {
                lock (m_lock)
                {
                    m_endpointUrls = [];
                    m_state = ReverseConnectManagerState.Faulted;
                    m_lifecycleVersion++;
                }
                m_logger.ReverseConnectServiceRestoreFailed(reopenError);
                return;
            }
            lock (m_lock)
            {
                m_state = ReverseConnectManagerState.Started;
                m_lifecycleVersion++;
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
        private Task GetOrStartDisposeTask()
        {
            TaskCompletionSource<bool>? owner = null;
            CancellationTokenSource? pendingStart = null;
            Task? pendingStartTask = null;
            ActiveTransaction? activeTransaction = null;
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
                activeTransaction?.AbortForShutdown();
                _ = RunDisposeAsync(owner, pendingStart, pendingStartTask);
            }
            return task;
        }

        /// <summary>
        /// Runs the one-shot teardown and completes the shared signal. Drains
        /// a superseded lazy start (which may still be using the manager-owned
        /// token) and disposes its token source before the complete teardown,
        /// so no provider/activation ever registers on a disposed token.
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
            var info = new ReverseConnectInfo(endpointUrl, reverseConnectHost, configEntry);
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
                // first try to match single registrations
                foreach (Registration registration in m_registrations
                    .Where(r => (r.ReverseConnectStrategy & ReverseConnectStrategy.Any) == 0))
                {
                    if (registration.EndpointUrl.Scheme
                        .Equals(e.EndpointUrl.Scheme, StringComparison.Ordinal) &&
                        (registration.ServerUri?
                            .Equals(e.ServerUri, StringComparison.Ordinal) == true ||
                            registration.EndpointUrl.Authority.Equals(e.EndpointUrl.Authority,
                                StringComparison.OrdinalIgnoreCase)))
                    {
                        callbackRegistration = registration;
                        e.Accepted = true;
                        found = true;
                        m_logger.AcceptedReverseConnectionServerUriEndpointUrl(
                            e.ServerUri,
                            e.EndpointUrl);
                        break;
                    }
                }

                // now try any registrations.
                if (callbackRegistration == null)
                {
                    foreach (Registration registration in m_registrations.Where(r =>
                            (r.ReverseConnectStrategy & ReverseConnectStrategy.Any) != 0))
                    {
                        if (registration.EndpointUrl.Scheme
                            .Equals(e.EndpointUrl.Scheme, StringComparison.Ordinal))
                        {
                            callbackRegistration = registration;
                            e.Accepted = true;
                            found = true;
                            m_logger.AcceptAnyReverseConnectionApprovalServerUri(
                                e.ServerUri,
                                e.EndpointUrl);
                            break;
                        }
                    }
                }

                if (callbackRegistration != null &&
                    (callbackRegistration.ReverseConnectStrategy &
                        ReverseConnectStrategy.Once) != 0)
                {
                    m_registrations.Remove(callbackRegistration);
                }
            }

            callbackRegistration?.OnConnectionWaiting?.Invoke(sender, e);

            return found;
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
        private readonly AsyncLocal<LegacyCaptureContext?> m_legacyCapture = new();
        // Operation-owner marker for this manager's in-flight startup pipeline.
        // Set (per async flow) around preparation/provider callbacks so a
        // re-entrant EnsureStartedAsync/RegisterWaitingConnectionAsync issued by
        // a provider or legacy hook fails fast instead of awaiting this start's
        // own shared task. Per-instance, so nested unrelated manager instances
        // (whose marker is unset) still start normally.
        private readonly AsyncLocal<object?> m_activeStartupOwner = new();
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
        // True while a StopServiceAsync is actively transitioning the manager
        // down (from the Stopping transition until the stop completes or rolls
        // back). A lazy EnsureStartedAsync observing this rejects deterministically
        // rather than returning success and registering an inert waiter. Guarded
        // by m_lock. A completed stop (state Stopped) clears this and is
        // restartable via a fresh lazy start.
        private bool m_stopInProgress;
        // Latches a hosted-start cancellation that fires before the shared
        // start task (and its m_startCts) has been published, so the pending
        // cancellation is applied atomically when the start is created.
        private bool m_startCancelRequested;
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

        // Distinct sentinels identifying the shutdown-latch owner so a stop only
        // clears a latch it set and a disposal latch is never cleared.
        private static readonly object s_stopLatch = new();
        private static readonly object s_disposeLatch = new();

        /// <summary>
        /// Deterministic test seam invoked by <see cref="ActivateAsync"/>
        /// immediately after the lifecycle gate is acquired and before any
        /// <see cref="ActiveTransaction"/> is published. Lets a test pause an
        /// activation inside the gate so it can drive a concurrent Stop/Dispose
        /// and prove the shutdown latch aborts the activation without opening a
        /// listener. Always <c>null</c> in production.
        /// </summary>
        internal Func<Task>? GateAcquiredForTest { get; set; }
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
    }
}
