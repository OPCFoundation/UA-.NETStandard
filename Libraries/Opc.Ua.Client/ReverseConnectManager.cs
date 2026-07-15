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
    /// </remarks>
    public class ReverseConnectManager : IDisposable
    {
        /// <summary>
        /// A default value for reverse hello configurations, if undefined.
        /// </summary>
        /// <remarks>
        /// This value is used as wait timeout if the value is undefined by a caller.
        /// </remarks>
        public const int DefaultWaitTimeout = 20000;

        /// <summary>
        /// Internal state of the reverse connect manager.
        /// </summary>
        private enum ReverseConnectManagerState
        {
            New = 0,
            Stopped = 1,
            Started = 2,
            Errored = 3
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
        private class ReverseConnectInfo
        {
            public ReverseConnectInfo(ReverseConnectHost reverseConnectHost, bool configEntry)
            {
                ReverseConnectHost = reverseConnectHost;
                State = ReverseConnectHostState.New;
                ConfigEntry = configEntry;
            }

            public ReverseConnectHost ReverseConnectHost;
            public ReverseConnectHostState State;
            public bool ConfigEntry;
        }

        /// <summary>
        /// Record to store information on a client
        /// registration for a reverse connect event.
        /// </summary>
        private class Registration
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
            m_cts = new CancellationTokenSource();
        }

        /// <summary>
        /// Dispose implementation.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        /// <param name="disposing"><c>true</c> to release both managed and unmanaged resources; <c>false</c> to release only unmanaged resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            // close the watcher.
            m_configurationWatcher?.Dispose();
            m_configurationWatcher = null;
            m_cts?.Dispose();
            DisposeHosts();
        }

        /// <summary>
        /// Raised when the configuration changes.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="args">The <see cref="ConfigurationWatcherEventArgs"/> instance containing the event data.</param>
        protected virtual async void OnConfigurationChangedAsync(
            object? sender,
            ConfigurationWatcherEventArgs args)
        {
            try
            {
                ApplicationConfiguration configuration = await ApplicationConfiguration
                    .LoadAsync(
                        new FileInfo(args.FilePath),
                        m_applicationType,
                        m_configType,
                        m_telemetry)
                    .ConfigureAwait(false);

                OnUpdateConfiguration(configuration);
            }
            catch (Exception e)
            {
                m_logger.CouldNotLoadUpdatedConfigurationFile(
                    e,
                    args.FilePath);
            }
        }

        /// <summary>
        /// Called when the configuration is changed on disk.
        /// </summary>
        /// <param name="configuration">The configuration.</param>
        [UnconditionalSuppressMessage("Trimming", "IL2074",
            Justification = "The configuration type was loaded with PublicParameterlessConstructor, so GetType() is safe to store.")]
        protected virtual void OnUpdateConfiguration(ApplicationConfiguration configuration)
        {
            // save types for config watcher
            m_applicationType = configuration.ApplicationType;
            m_configType = configuration.GetType();
            // capture the application configuration so AddEndpointInternal
            // can plumb the CertificateManager into ReverseConnectHost.CreateListener
            // for transports that terminate TLS (e.g. opc.wss).
            m_appConfig = configuration;

            // ClientConfiguration and ReverseConnect are nullable on ApplicationConfiguration,
            // but the file watcher is only enabled for client configurations that include a
            // populated ReverseConnect section, so both are guaranteed non-null here.
            OnUpdateConfiguration(configuration.ClientConfiguration!.ReverseConnect!);
        }

        /// <summary>
        /// Called when the reverse connect configuration is changed.
        /// </summary>
        /// <remarks>
        ///  An empty configuration or null stops service on all configured endpoints.
        /// </remarks>
        /// <param name="configuration">The client endpoint configuration.</param>
        protected virtual void OnUpdateConfiguration(
            ReverseConnectClientConfiguration configuration)
        {
            bool restartService = false;

            lock (m_lock)
            {
                if (m_configuration != null)
                {
                    StopService();
                    m_configuration = null;
                    restartService = true;
                }

                m_configuration = configuration ?? new ReverseConnectClientConfiguration();

                // clear configured endpoints
                ClearEndpoints(true);

                if (configuration?.ClientEndpoints != null)
                {
                    foreach (ReverseConnectClientEndpoint endpoint in configuration.ClientEndpoints)
                    {
                        Uri? uri = Utils.ParseUri(endpoint.EndpointUrl);
                        if (uri != null)
                        {
                            AddEndpointInternal(uri, true);
                        }
                    }
                }

                if (restartService)
                {
                    StartService();
                }
            }
        }

        /// <summary>
        /// Open host ports.
        /// </summary>
        private async ValueTask OpenHostsAsync(CancellationToken ct = default)
        {
            List<ReverseConnectInfo> snapshot;
            lock (m_lock)
            {
                snapshot = [.. m_endpointUrls.Values];
            }

            foreach (ReverseConnectInfo value in snapshot)
            {
                try
                {
                    if (value.State < ReverseConnectHostState.Open)
                    {
                        await value.ReverseConnectHost.OpenAsync(ct).ConfigureAwait(false);
                        value.State = ReverseConnectHostState.Open;
                    }
                }
                catch (Exception e)
                {
                    m_logger.FailedOpenUri(
                        e,
                        value.ReverseConnectHost.Url);
                    value.State = ReverseConnectHostState.Errored;
                }
            }
        }

        /// <summary>
        /// Close host ports.
        /// </summary>
        private async ValueTask CloseHostsAsync(CancellationToken ct = default)
        {
            List<ReverseConnectInfo> snapshot;
            lock (m_lock)
            {
                snapshot = [.. m_endpointUrls.Values];
            }

            foreach (ReverseConnectInfo value in snapshot)
            {
                try
                {
                    if (value.State == ReverseConnectHostState.Open)
                    {
                        await value.ReverseConnectHost.CloseAsync(ct).ConfigureAwait(false);
                        value.State = ReverseConnectHostState.Closed;
                    }
                }
                catch (Exception e)
                {
                    m_logger.FailedCloseUri(
                        e,
                        value.ReverseConnectHost.Url);
                    value.State = ReverseConnectHostState.Errored;
                }
            }
        }

        /// <summary>
        /// Dispose the hosts;
        /// </summary>
        private void DisposeHosts()
        {
            // Snapshot under lock, await close outside the lock (CloseHostsAsync
            // does this internally). Sync bridge at the IDisposable boundary
            // keeps existing 'using var manager = ...' callers working.
            CloseHostsAsync().AsTask().GetAwaiter().GetResult();
            lock (m_lock)
            {
                m_endpointUrls.Clear();
            }
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
        /// <see cref="StartService(ApplicationConfiguration)"/> runs - too
        /// late for WSS listeners that need TLS state at bind time.
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

            lock (m_lock)
            {
                if (m_state == ReverseConnectManagerState.Started)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidState);
                }

                // capture the appConfig early so AddEndpointInternal
                // can plumb the CertificateManager into the listener
                // when the endpoint URL needs TLS termination (WSS).
                if (configuration != null && m_appConfig == null)
                {
                    m_appConfig = configuration;
                }

                AddEndpointInternal(endpointUrl, false);
            }
        }

        /// <summary>
        /// Starts the server application.
        /// </summary>
        /// <param name="configuration">The configuration.</param>
        /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <c>null</c>.</exception>
        /// <exception cref="ServiceResultException"></exception>
        public void StartService(ApplicationConfiguration configuration)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            lock (m_lock)
            {
                if (m_state == ReverseConnectManagerState.Started)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidState);
                }

                try
                {
                    OnUpdateConfiguration(configuration);
                    StartService();

                    // monitor the configuration file.
                    if (!string.IsNullOrEmpty(configuration.SourceFilePath))
                    {
                        m_configurationWatcher = new ConfigurationWatcher(configuration, m_telemetry);
                        m_configurationWatcher.Changed += OnConfigurationChangedAsync;
                    }
                }
                catch (Exception e)
                {
                    m_logger.UnexpectedErrorStartingReverseConnectManager(e);
                    m_state = ReverseConnectManagerState.Errored;
                    var error = ServiceResult.Create(
                        e,
                        StatusCodes.BadInternalError,
                        "Unexpected error starting application");
                    throw new ServiceResultException(error);
                }
            }
        }

        /// <summary>
        /// Starts the server application.
        /// </summary>
        /// <param name="configuration">The configuration.</param>
        /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <c>null</c>.</exception>
        /// <exception cref="ServiceResultException"></exception>
        public void StartService(ReverseConnectClientConfiguration configuration)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            lock (m_lock)
            {
                if (m_state == ReverseConnectManagerState.Started)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidState);
                }

                try
                {
                    m_configurationWatcher = null;
                    OnUpdateConfiguration(configuration);
                    // Sync bridge: OpenHostsAsync snapshots under m_lock and
                    // awaits OpenAsync() outside, so calling it from inside
                    // this lock does not deadlock — the inner lock acquisition
                    // happens reentrantly on the same thread.
                    OpenHostsAsync().AsTask().GetAwaiter().GetResult();
                    m_state = ReverseConnectManagerState.Started;
                }
                catch (Exception e)
                {
                    m_logger.UnexpectedErrorStartingReverseConnectManager(e);
                    m_state = ReverseConnectManagerState.Errored;
                    var error = ServiceResult.Create(
                        e,
                        StatusCodes.BadInternalError,
                        "Unexpected error starting reverse connect manager");
                    throw new ServiceResultException(error);
                }
            }
        }

        /// <summary>
        /// Clears all waiting reverse connection handlers.
        /// </summary>
        public void ClearWaitingConnections()
        {
            lock (m_registrationsLock)
            {
                m_registrations.Clear();
                CancelAndRenewTokenSource();
            }
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
        /// <exception cref="ServiceResultException"></exception>
        public async Task<ITransportWaitingConnection> WaitForConnectionAsync(
            Uri endpointUrl,
            string? serverUri,
            CancellationToken ct = default)
        {
            var tcs = new TaskCompletionSource<ITransportWaitingConnection>();
            int hashCode = RegisterWaitingConnection(
                endpointUrl,
                serverUri,
                (sender, e) => tcs.TrySetResult(e),
                ReverseConnectStrategy.Once);

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

            await Task.WhenAny([tcs.Task, ListenForCancelAsync(ct)]).ConfigureAwait(false);

            if (!tcs.Task.IsCompleted || tcs.Task.IsCanceled)
            {
                UnregisterWaitingConnection(hashCode);
                throw new ServiceResultException(
                    StatusCodes.BadTimeout,
                    "Waiting for the reverse connection timed out.");
            }

            return await tcs.Task.ConfigureAwait(false);
        }

        /// <summary>
        /// Register for a waiting reverse connection.
        /// </summary>
        /// <param name="endpointUrl">The endpoint Url of the reverse connection.</param>
        /// <param name="serverUri">Optional. The server application Uri of the reverse connection.</param>
        /// <param name="onConnectionWaiting">The callback</param>
        /// <param name="reverseConnectStrategy">The reverse connect callback strategy.</param>
        /// <exception cref="ArgumentNullException"><paramref name="endpointUrl"/> is <c>null</c>.</exception>
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

            var registration = new Registration(serverUri, endpointUrl, onConnectionWaiting)
            {
                ReverseConnectStrategy = reverseConnectStrategy
            };
            lock (m_registrationsLock)
            {
                m_registrations.Add(registration);
                CancelAndRenewTokenSource();
            }
            return registration.GetHashCode();
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
        /// Called before the server stops
        /// </summary>
        private void StopService()
        {
            ClearWaitingConnections();
            // CloseHostsAsync snapshots the host list under the registry
            // lock and then awaits CloseAsync() on each listener outside
            // the lock — safe to bridge to sync here because the public
            // StopService boundary stays synchronous for backward
            // compatibility. The listener-layer work itself is fully
            // async (issue #3923).
            CloseHostsAsync().AsTask().GetAwaiter().GetResult();
            lock (m_lock)
            {
                m_state = ReverseConnectManagerState.Stopped;
            }
        }

        /// <summary>
        /// Called to start hosting the reverse connect ports.
        /// </summary>
        private void StartService()
        {
            // OpenHostsAsync snapshots under lock then awaits OpenAsync()
            // outside — sync bridge at the existing public StartService
            // boundary keeps callers (samples, builder, tests) source-
            // compatible.
            OpenHostsAsync().AsTask().GetAwaiter().GetResult();
            lock (m_lock)
            {
                m_state = ReverseConnectManagerState.Started;
            }
        }

        /// <summary>
        /// Remove configuration endpoints from list.
        /// </summary>
        private void ClearEndpoints(bool configEntry)
        {
            var newEndpointUrls = new Dictionary<Uri, ReverseConnectInfo>();
            foreach (KeyValuePair<Uri, ReverseConnectInfo> endpoint in m_endpointUrls)
            {
                if (endpoint.Value.ConfigEntry != configEntry)
                {
                    newEndpointUrls[endpoint.Key] = endpoint.Value;
                }
            }
            m_endpointUrls = newEndpointUrls;
        }

        /// <summary>
        /// Add endpoint for reverse connection.
        /// </summary>
        /// <param name="endpointUrl">The endpoint Url of the reverse connect client endpoint.</param>
        /// <param name="configEntry">Tf this is an entry in the application configuration.</param>
        private void AddEndpointInternal(Uri endpointUrl, bool configEntry)
        {
            var reverseConnectHost = new ReverseConnectHost(m_telemetry, TransportBindings);
            var info = new ReverseConnectInfo(reverseConnectHost, configEntry);
            try
            {
                m_endpointUrls[endpointUrl] = info;
                // Listener bindings that terminate TLS (WSS) need a server
                // TLS certificate + validator at Open time. Pull them from
                // the captured ApplicationConfiguration's CertificateManager
                // when available so the user does not have to plumb them
                // manually for the common case.
                ICertificateRegistry? serverCertificates = null;
                ICertificateValidatorEx? certificateValidator = null;
                if (Utils.IsUriWssScheme(endpointUrl.AbsoluteUri) && m_appConfig != null)
                {
                    serverCertificates = m_appConfig.CertificateManager;
                    certificateValidator = m_appConfig.CertificateManager;
                }
                reverseConnectHost.CreateListener(
                    endpointUrl,
                    new ConnectionWaitingHandlerAsync(OnConnectionWaitingAsync),
                    new EventHandler<ConnectionStatusEventArgs>(OnConnectionStatusChanged),
                    serverCertificates,
                    certificateValidator);
            }
            catch (ArgumentException ae)
            {
                m_logger.NoListenerFoundEndpointEndpointUrl(
                    ae,
                    endpointUrl);
                info.State = ReverseConnectHostState.Errored;
            }
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
        /// Renew the cancellation token after use.
        /// </summary>
        private void CancelAndRenewTokenSource()
        {
            CancellationTokenSource cts = m_cts;
            m_cts = new CancellationTokenSource();
            cts.Cancel();
            cts.Dispose();
        }

        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        private Type? m_configType;

        private readonly Lock m_lock = new();
        private readonly TimeProvider m_timeProvider;
        private readonly ILogger m_logger;
        private readonly ITelemetryContext m_telemetry;
        private ConfigurationWatcher? m_configurationWatcher;
        private ApplicationType m_applicationType;
        private ApplicationConfiguration? m_appConfig;
        private ReverseConnectClientConfiguration? m_configuration;
        private Dictionary<Uri, ReverseConnectInfo> m_endpointUrls;
        private ReverseConnectManagerState m_state;
        private readonly List<Registration> m_registrations;
        private readonly Lock m_registrationsLock = new();
        private CancellationTokenSource m_cts;
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
    }

}
