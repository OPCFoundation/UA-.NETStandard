// ------------------------------------------------------------
//  Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client
{
    using Microsoft.Extensions.Logging;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

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
    internal sealed class ReverseConnectManager : IDisposable
    {
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
        /// Initializes the object with default values.
        /// </summary>
        /// <param name="loggerFactory"></param>
        public ReverseConnectManager(ILoggerFactory loggerFactory) // TODO: Make internal
        {
            _logger = loggerFactory.CreateLogger<ReverseConnectManager>();
            _state = ReverseConnectManagerState.New;
            _registrations = [];
            _endpointUrls = [];
            _configuration = new ReverseConnectClientConfiguration();
            _cts = new CancellationTokenSource();
        }

        /// <summary>
        /// Dispose implementation.
        /// </summary>
        public void Dispose()
        {
            _cts.Dispose();
            DisposeHosts();
        }

        /// <summary>
        /// Starts the server application.
        /// </summary>
        /// <param name="configuration">The configuration.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="configuration"/> is <c>null</c>.</exception>
        /// <exception cref="ServiceResultException"></exception>
        public void StartService(ReverseConnectClientConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            lock (_lock)
            {
                if (_state == ReverseConnectManagerState.Started)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidState);
                }
                try
                {
                    _configuration = configuration;

                    // clear configured endpoints
                    ClearEndpoints(true);
                    if (_configuration.ClientEndpoints != null)
                    {
                        foreach (var endpoint in _configuration.ClientEndpoints)
                        {
                            var uri = Utils.ParseUri(endpoint.EndpointUrl);
                            if (uri != null)
                            {
                                AddEndpointInternal(uri, true);
                            }
                        }
                    }
                    OpenHosts();
                    _state = ReverseConnectManagerState.Started;
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Unexpected error starting reverse connect manager.");
                    _state = ReverseConnectManagerState.Errored;
                    var error = ServiceResult.Create(e, StatusCodes.BadInternalError,
                        "Unexpected error starting reverse connect manager");
                    throw new ServiceResultException(error);
                }
            }
        }

        /// <summary>
        /// Helper to wait for a reverse connection.
        /// </summary>
        /// <param name="endpointUrl"></param>
        /// <param name="serverUri"></param>
        /// <param name="ct"></param>
        /// <exception cref="ServiceResultException"></exception>
        public async Task<ITransportWaitingConnection> WaitForConnectionAsync(
            Uri endpointUrl, string? serverUri, CancellationToken ct = default)
        {
            var tcs = new TaskCompletionSource<ITransportWaitingConnection>();
            var hashCode = RegisterWaitingConnection(endpointUrl, serverUri,
                (_, e) => tcs.TrySetResult(e), ReverseConnectStrategy.Once);

            async Task listenForCancelTaskFnc()
            {
                if (ct == default)
                {
                    var waitTimeout = _configuration.WaitTimeout > 0 ?
                        _configuration.WaitTimeout : DefaultWaitTimeout;
                    await Task.Delay(waitTimeout, ct).ConfigureAwait(false);
                }
                else
                {
                    await Task.Delay(-1, ct).ContinueWith(tsk => { },
                        scheduler: TaskScheduler.Current).ConfigureAwait(false);
                }
                tcs.TrySetCanceled(ct);
            }

            await Task.WhenAny([
                tcs.Task,
                listenForCancelTaskFnc()
            ]).ConfigureAwait(false);

            if (!tcs.Task.IsCompleted || tcs.Task.IsCanceled)
            {
                UnregisterWaitingConnection(hashCode);
                throw new ServiceResultException(StatusCodes.BadTimeout,
                    "Waiting for the reverse connection timed out.");
            }

            return await tcs.Task.ConfigureAwait(false);
        }

        /// <summary>
        /// Register for a waiting reverse connection.
        /// </summary>
        /// <param name="endpointUrl">The endpoint Url of the reverse
        /// connection.</param>
        /// <param name="serverUri">Optional. The server application Uri
        /// of the reverse connection.</param>
        /// <param name="onConnectionWaiting">The callback</param>
        /// <param name="reverseConnectStrategy">The reverse connect
        /// callback strategy.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="endpointUrl"/> is <c>null</c>.</exception>
        public int RegisterWaitingConnection(Uri endpointUrl, string? serverUri,
            EventHandler<ConnectionWaitingEventArgs> onConnectionWaiting,
            ReverseConnectStrategy reverseConnectStrategy)
        {
            ArgumentNullException.ThrowIfNull(endpointUrl);
            var registration = new Registration(serverUri, endpointUrl,
                onConnectionWaiting, reverseConnectStrategy);
            lock (_registrationsLock)
            {
                _registrations.Add(registration);
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
            lock (_registrationsLock)
            {
                Registration? toRemove = null;
                foreach (var registration in _registrations)
                {
                    if (registration.GetHashCode() == hashCode)
                    {
                        toRemove = registration;
                        break;
                    }
                }
                if (toRemove != null)
                {
                    _registrations.Remove(toRemove);
                    CancelAndRenewTokenSource();
                }
            }
        }

        /// <summary>
        /// Open host ports.
        /// </summary>
        private void OpenHosts()
        {
            lock (_lock)
            {
                foreach (var host in _endpointUrls)
                {
                    var value = host.Value;
                    try
                    {
                        if (host.Value.State < ReverseConnectHostState.Open)
                        {
                            value.ReverseConnectHost.Open();
                            value.State = ReverseConnectHostState.Open;
                        }
                    }
                    catch (Exception e)
                    {
                        _logger.LogError(e, "Failed to Open {Host}.", host.Key);
                        value.State = ReverseConnectHostState.Errored;
                    }
                }
            }
        }

        /// <summary>
        /// Close host ports.
        /// </summary>
        private void CloseHosts()
        {
            lock (_lock)
            {
                foreach (var host in _endpointUrls)
                {
                    var value = host.Value;
                    try
                    {
                        if (value.State == ReverseConnectHostState.Open)
                        {
                            value.ReverseConnectHost.Close();
                            value.State = ReverseConnectHostState.Closed;
                        }
                    }
                    catch (Exception e)
                    {
                        _logger.LogError(e, "Failed to Close {Host}.", host.Key);
                        value.State = ReverseConnectHostState.Errored;
                    }
                }
            }
        }

        /// <summary>
        /// Dispose the hosts;
        /// </summary>
        private void DisposeHosts()
        {
            lock (_lock)
            {
                CloseHosts();
                _endpointUrls.Clear();
            }
        }

        /// <summary>
        /// Remove configuration endpoints from list.
        /// </summary>
        /// <param name="configEntry"></param>
        private void ClearEndpoints(bool configEntry)
        {
            var newEndpointUrls = new Dictionary<Uri, ReverseConnectInfo>();
            foreach (var endpoint in _endpointUrls)
            {
                if (endpoint.Value.ConfigEntry != configEntry)
                {
                    newEndpointUrls[endpoint.Key] = endpoint.Value;
                }
            }
            _endpointUrls = newEndpointUrls;
        }

        /// <summary>
        /// Add endpoint for reverse connection.
        /// </summary>
        /// <param name="endpointUrl">The endpoint Url of the reverse
        /// connect client endpoint.</param>
        /// <param name="configEntry">Tf this is an entry in the application
        /// configuration.</param>
        private void AddEndpointInternal(Uri endpointUrl, bool configEntry)
        {
            var reverseConnectHost = new ReverseConnectHost();
            var info = new ReverseConnectInfo(reverseConnectHost, configEntry);
            try
            {
                _endpointUrls[endpointUrl] = info;
                reverseConnectHost.CreateListener(endpointUrl, OnConnectionWaitingAsync,
                    OnConnectionStatusChanged);
            }
            catch (ArgumentException ae)
            {
                _logger.LogError(ae, "No listener was found for endpoint {Endpoint}.",
                    endpointUrl);
                info.State = ReverseConnectHostState.Errored;
            }
        }

        /// <summary>
        /// Raised when a reverse connection is waiting,
        /// finds and calls a waiting connection.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async Task OnConnectionWaitingAsync(object sender, ConnectionWaitingEventArgs e)
        {
            var startTime = HiResClock.TickCount;
            var endTime = startTime + _configuration.HoldTime;

            var matched = MatchRegistration(sender, e);
            while (!matched)
            {
                _logger.LogInformation("Holding reverse connection: {Server} {Endpoint}",
                    e.ServerUri, e.EndpointUrl);
                CancellationToken ct;
                lock (_registrationsLock)
                {
                    ct = _cts.Token;
                }
                var delay = endTime - HiResClock.TickCount;
                if (delay > 0)
                {
                    await Task.Delay(delay, ct).ContinueWith(tsk =>
                    {
                        if (tsk.IsCanceled)
                        {
                            matched = MatchRegistration(sender, e);
                            if (matched)
                            {
                                _logger.LogInformation(
                                    "Matched reverse connection {Server} {Endpoint} after {Duration}ms",
                                     e.ServerUri, e.EndpointUrl,
                                     HiResClock.TickCount - startTime);
                            }
                        }
                    }, scheduler: TaskScheduler.Current).ConfigureAwait(false);
                }
                break;
            }

            _logger.LogInformation(
                "{Action} reverse connection: {Server} {Endpoint} after {Duration}ms",
                e.Accepted ? "Accepted" : "Rejected",
                e.ServerUri, e.EndpointUrl, HiResClock.TickCount - startTime);
        }

        /// <summary>
        /// Match the waiting connection with a registration, callback registration,
        /// return if connection is accepted in event.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        /// <returns>true if a match was found.</returns>
        private bool MatchRegistration(object sender, ConnectionWaitingEventArgs e)
        {
            Registration? callbackRegistration = null;
            var found = false;
            lock (_registrationsLock)
            {
                // first try to match single registrations
                foreach (var registration in _registrations
                    .Where(r => (r.Strategy & ReverseConnectStrategy.Any) == 0))
                {
                    if (registration.EndpointUrl.Scheme.Equals(e.EndpointUrl.Scheme,
                            StringComparison.Ordinal) &&
                       (registration.ServerUri == e.ServerUri ||
                        registration.EndpointUrl.Authority.Equals(e.EndpointUrl.Authority,
                            StringComparison.Ordinal)))
                    {
                        callbackRegistration = registration;
                        e.Accepted = true;
                        found = true;
                        _logger.LogInformation(
                            "Accepted reverse connection: {Server} {Endpoint}",
                            e.ServerUri, e.EndpointUrl);
                        break;
                    }
                }

                // now try any registrations.
                if (callbackRegistration == null)
                {
                    foreach (var registration in _registrations
                        .Where(r => (r.Strategy & ReverseConnectStrategy.Any) != 0))
                    {
                        if (registration.EndpointUrl.Scheme.Equals(
                            e.EndpointUrl.Scheme, StringComparison.Ordinal))
                        {
                            callbackRegistration = registration;
                            e.Accepted = true;
                            found = true;
                            _logger.LogInformation(
                                "Accept any reverse connection for approval: {Server} {Endpoint}",
                                e.ServerUri, e.EndpointUrl);
                            break;
                        }
                    }
                }

                if (callbackRegistration != null &&
                    (callbackRegistration.Strategy & ReverseConnectStrategy.Once) != 0)
                {
                    _registrations.Remove(callbackRegistration);
                }
            }
            callbackRegistration?.OnConnectionWaiting?.Invoke(sender, e);
            return found;
        }

        /// <summary>
        /// Raised when a connection status changes.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnConnectionStatusChanged(object? sender, ConnectionStatusEventArgs e)
        {
            _logger.LogInformation("{Endpoint}: Channel={ChannelStatus} Closed={Closed}",
                e.EndpointUrl, e.ChannelStatus, e.Closed);
        }

        /// <summary>
        /// Renew the cancellation token after use.
        /// </summary>
        private void CancelAndRenewTokenSource()
        {
            var cts = _cts;
            _cts = new CancellationTokenSource();
            cts.Cancel();
            cts.Dispose();
        }
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
        /// Entry for a client reverse connect registration.
        /// </summary>
        /// <param name="reverseConnectHost"></param>
        /// <param name="configEntry"></param>
        private sealed class ReverseConnectInfo(ReverseConnectHost reverseConnectHost,
            bool configEntry)
        {
            public ReverseConnectHostState State { get; set; } = ReverseConnectHostState.New;
            public ReverseConnectHost ReverseConnectHost { get; } = reverseConnectHost;
            public bool ConfigEntry { get; } = configEntry;
        }

        /// <summary>
        /// Record to store information on a client
        /// registration for a reverse connect event.
        /// </summary>
        /// <param name="serverUri"></param>
        /// <param name="endpointUrl"></param>
        /// <param name="onConnectionWaiting"></param>
        /// <param name="strategy"></param>
        private sealed class Registration(string? serverUri, Uri endpointUrl,
            EventHandler<ConnectionWaitingEventArgs> onConnectionWaiting,
            ReverseConnectManager.ReverseConnectStrategy strategy
                = ReverseConnectManager.ReverseConnectStrategy.Once)
        {
            public string? ServerUri { get; } = Utils.ReplaceLocalhost(serverUri);
            public Uri EndpointUrl { get; } = new Uri(Utils.ReplaceLocalhost(endpointUrl.ToString()));
            public EventHandler<ConnectionWaitingEventArgs> OnConnectionWaiting { get; } = onConnectionWaiting;
            public ReverseConnectStrategy Strategy { get; } = strategy;
        }

        private readonly Lock _lock = new();
        private ReverseConnectClientConfiguration _configuration;
        private readonly ILogger _logger;
        private Dictionary<Uri, ReverseConnectInfo> _endpointUrls;
        private ReverseConnectManagerState _state;
        private CancellationTokenSource _cts;
        private readonly Lock _registrationsLock = new();
        private readonly List<Registration> _registrations;
    }
}
