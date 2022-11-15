/* ========================================================================
 * Copyright (c) 2005-2021 The OPC Foundation, Inc. All rights reserved.
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
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

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
        };

        /// <summary>
        /// Internal state of the reverse connect host.
        /// </summary>
        private enum ReverseConnectHostState
        {
            New = 0,
            Closed = 1,
            Open = 2,
            Errored = 3
        };

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
                string serverUri,
                Uri endpointUrl,
                EventHandler<ConnectionWaitingEventArgs> onConnectionWaiting) :
                this(endpointUrl, onConnectionWaiting)
            {
                ServerUri = Utils.ReplaceLocalhost(serverUri);
            }

            /// <summary>
            /// Register with the server certificate.
            /// </summary>
            /// <param name="serverCertificate"></param>
            /// <param name="endpointUrl"></param>
            /// <param name="onConnectionWaiting"></param>
            public Registration(
                X509Certificate2 serverCertificate,
                Uri endpointUrl,
                EventHandler<ConnectionWaitingEventArgs> onConnectionWaiting) :
                this(endpointUrl, onConnectionWaiting)
            {
                ServerUri = X509Utils.GetApplicationUriFromCertificate(serverCertificate);
            }

            private Registration(
                Uri endpointUrl,
                EventHandler<ConnectionWaitingEventArgs> onConnectionWaiting)
            {
                EndpointUrl = new Uri(Utils.ReplaceLocalhost(endpointUrl.ToString()));
                OnConnectionWaiting = onConnectionWaiting;
                ReverseConnectStrategy = ReverseConnectStrategy.Once;
            }

            public readonly string ServerUri;
            public readonly Uri EndpointUrl;
            public readonly EventHandler<ConnectionWaitingEventArgs> OnConnectionWaiting;
            public ReverseConnectStrategy ReverseConnectStrategy;
        }

        #region Constructors
        /// <summary>
        /// Initializes the object with default values.
        /// </summary>
        public ReverseConnectManager()
        {
            m_state = ReverseConnectManagerState.New;
            m_registrations = new List<Registration>();
            m_registrationsLock = new object();
            m_endpointUrls = new Dictionary<Uri, ReverseConnectInfo>();
            m_cts = new CancellationTokenSource();
        }
        #endregion

        #region IDisposable Members
        /// <summary>
        /// Dispose implementation.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
        }

        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        /// <param name="disposing"><c>true</c> to release both managed and unmanaged resources; <c>false</c> to release only unmanaged resources.</param>
        public virtual void Dispose(bool disposing)
        {
            // close the watcher.
            if (m_configurationWatcher != null)
            {
                Utils.SilentDispose(m_configurationWatcher);
                m_configurationWatcher = null;
            }
            DisposeHosts();
        }
        #endregion

        #region Protected Members
        /// <summary>
        /// Raised when the configuration changes.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="args">The <see cref="Opc.Ua.ConfigurationWatcherEventArgs"/> instance containing the event data.</param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Security", "CA2109:ReviewVisibleEventHandlers")]
        protected virtual async void OnConfigurationChanged(object sender, ConfigurationWatcherEventArgs args)
        {
            try
            {
                ApplicationConfiguration configuration = await ApplicationConfiguration.Load(
                    new FileInfo(args.FilePath),
                    m_applicationType,
                    m_configType).ConfigureAwait(false);

                OnUpdateConfiguration(configuration);
            }
            catch (Exception e)
            {
                Utils.LogError(e, "Could not load updated configuration file from: {0}", args);
            }
        }

        /// <summary>
        /// Called when the configuration is changed on disk.
        /// </summary>
        /// <param name="configuration">The configuration.</param>
        protected virtual void OnUpdateConfiguration(ApplicationConfiguration configuration)
        {
            // save types for config watcher
            m_applicationType = configuration.ApplicationType;
            m_configType = configuration.GetType();

            OnUpdateConfiguration(configuration.ClientConfiguration.ReverseConnect);
        }

        /// <summary>
        /// Called when the reverse connect configuration is changed.
        /// </summary>
        /// <remarks>
        ///  An empty configuration or null stops service on all configured endpoints.
        /// </remarks>
        /// <param name="configuration">The client endpoint configuration.</param>
        protected virtual void OnUpdateConfiguration(ReverseConnectClientConfiguration configuration)
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
                    foreach (var endpoint in configuration.ClientEndpoints)
                    {
                        var uri = Utils.ParseUri(endpoint.EndpointUrl);
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
        private void OpenHosts()
        {
            lock (m_lock)
            {
                foreach (var host in m_endpointUrls)
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
                        Utils.LogError(e, "Failed to Open {0}.", host.Key);
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
            lock (m_lock)
            {
                foreach (var host in m_endpointUrls)
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
                        Utils.LogError(e, "Failed to Close {0}.", host.Key);
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
            lock (m_lock)
            {
                CloseHosts();
                m_endpointUrls = null;
            }
        }

        /// <summary>
        /// Add endpoint for reverse connection.
        /// </summary>
        /// <param name="endpointUrl"></param>
        public void AddEndpoint(Uri endpointUrl)
        {
            if (endpointUrl == null) throw new ArgumentNullException(nameof(endpointUrl));
            lock (m_lock)
            {
                if (m_state == ReverseConnectManagerState.Started) throw new ServiceResultException(StatusCodes.BadInvalidState);
                AddEndpointInternal(endpointUrl, false);
            }
        }

        /// <summary>
        /// Starts the server application.
        /// </summary>
        /// <param name="configuration">The configuration.</param>
        public void StartService(ApplicationConfiguration configuration)
        {
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));
            lock (m_lock)
            {
                if (m_state == ReverseConnectManagerState.Started) throw new ServiceResultException(StatusCodes.BadInvalidState);
                try
                {
                    OnUpdateConfiguration(configuration);
                    StartService();

                    // monitor the configuration file.
                    if (!String.IsNullOrEmpty(configuration.SourceFilePath))
                    {
                        m_configurationWatcher = new ConfigurationWatcher(configuration);
                        m_configurationWatcher.Changed += OnConfigurationChanged;
                    }
                }
                catch (Exception e)
                {
                    Utils.LogError(e, "Unexpected error starting reverse connect manager.");
                    m_state = ReverseConnectManagerState.Errored;
                    ServiceResult error = ServiceResult.Create(e, StatusCodes.BadInternalError, "Unexpected error starting application");
                    throw new ServiceResultException(error);
                }
            }
        }

        /// <summary>
        /// Starts the server application.
        /// </summary>
        /// <param name="configuration">The configuration.</param>
        public void StartService(ReverseConnectClientConfiguration configuration)
        {
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));
            lock (m_lock)
            {
                if (m_state == ReverseConnectManagerState.Started) throw new ServiceResultException(StatusCodes.BadInvalidState);
                try
                {
                    m_configurationWatcher = null;
                    OnUpdateConfiguration(configuration);
                    OpenHosts();
                    m_state = ReverseConnectManagerState.Started;
                }
                catch (Exception e)
                {
                    Utils.LogError(e, "Unexpected error starting reverse connect manager.");
                    m_state = ReverseConnectManagerState.Errored;
                    ServiceResult error = ServiceResult.Create(e, StatusCodes.BadInternalError, "Unexpected error starting reverse connect manager");
                    throw new ServiceResultException(error);
                }
            }
        }

        /// <summary>
        /// Clears all waiting reverse connectino handlers.
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
        /// <param name="endpointUrl"></param>
        /// <param name="serverUri"></param>
        /// <param name="ct"></param>
        public async Task<ITransportWaitingConnection> WaitForConnection(
            Uri endpointUrl,
            string serverUri,
            CancellationToken ct = default(CancellationToken))
        {
            var tcs = new TaskCompletionSource<ITransportWaitingConnection>();
            int hashCode = RegisterWaitingConnection(endpointUrl, serverUri,
                (object sender, ConnectionWaitingEventArgs e) => tcs.TrySetResult(e),
                ReverseConnectStrategy.Once);

            Func<Task> listenForCancelTaskFnc = async () => {
                if (ct == default(CancellationToken))
                {
                    var waitTimeout = m_configuration.WaitTimeout > 0 ? m_configuration.WaitTimeout : DefaultWaitTimeout;
                    await Task.Delay(waitTimeout).ConfigureAwait(false);
                }
                else
                {
                    await Task.Delay(-1, ct).ContinueWith(tsk => { }).ConfigureAwait(false);
                }
                tcs.TrySetCanceled();
            };

            await Task.WhenAny(new Task[] {
                tcs.Task,
                listenForCancelTaskFnc()
            }).ConfigureAwait(false);

            if (!tcs.Task.IsCompleted || tcs.Task.IsCanceled)
            {
                UnregisterWaitingConnection(hashCode);
                throw new ServiceResultException(StatusCodes.BadTimeout, "Waiting for the reverse connection timed out.");
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
        public int RegisterWaitingConnection(
            Uri endpointUrl,
            string serverUri,
            EventHandler<ConnectionWaitingEventArgs> onConnectionWaiting,
            ReverseConnectStrategy reverseConnectStrategy
            )
        {
            if (endpointUrl == null) throw new ArgumentNullException(nameof(endpointUrl));
            var registration = new Registration(serverUri, endpointUrl, onConnectionWaiting) {
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
                Registration toRemove = null;
                foreach (var registration in m_registrations)
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
            lock (m_lock)
            {
                CloseHosts();
                m_state = ReverseConnectManagerState.Stopped;
            }
        }

        /// <summary>
        /// Called to start hosting the reverse connect ports.
        /// </summary>
        private void StartService()
        {
            lock (m_lock)
            {
                OpenHosts();
                m_state = ReverseConnectManagerState.Started;
            }
        }

        /// <summary>
        /// Remove configuration endpoints from list.
        /// </summary>
        private void ClearEndpoints(bool configEntry)
        {
            var newEndpointUrls = new Dictionary<Uri, ReverseConnectInfo>();
            foreach (var endpoint in m_endpointUrls)
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
            var reverseConnectHost = new ReverseConnectHost();
            var info = new ReverseConnectInfo(reverseConnectHost, configEntry);
            try
            {
                m_endpointUrls[endpointUrl] = info;
                reverseConnectHost.CreateListener(
                    endpointUrl,
                    new ConnectionWaitingHandlerAsync(OnConnectionWaiting),
                    new EventHandler<ConnectionStatusEventArgs>(OnConnectionStatusChanged));
            }
            catch (ArgumentException ae)
            {
                Utils.LogError(ae, "No listener was found for endpoint {0}.", endpointUrl);
                info.State = ReverseConnectHostState.Errored;
            }
        }

        /// <summary>
        /// Raised when a reverse connection is waiting,
        /// finds and calls a waiting connection.
        /// </summary>
        private async Task OnConnectionWaiting(object sender, ConnectionWaitingEventArgs e)
        {
            DateTime startTime = DateTime.UtcNow;
            DateTime endTime = startTime + TimeSpan.FromMilliseconds(m_configuration.HoldTime);
            bool matched = MatchRegistration(sender, e);
            while (!matched)
            {
                Utils.LogInfo("Holding reverse connection: {0} {1}", e.ServerUri, e.EndpointUrl);
                CancellationToken ct;
                lock (m_registrationsLock)
                {
                    ct = m_cts.Token;
                }
                TimeSpan delay = endTime - DateTime.UtcNow;
                if (delay.TotalMilliseconds > 0)
                {
                    await Task.Delay(delay, ct).ContinueWith(tsk => {
                        if (tsk.IsCanceled)
                        {
                            matched = MatchRegistration(sender, e);
                            if (matched)
                            {
                                Utils.LogInfo("Matched reverse connection {0} {1} after {2}ms",
                                     e.ServerUri, e.EndpointUrl,
                                    (int)(DateTime.UtcNow - startTime).TotalMilliseconds);
                            }
                        }
                    }
                    ).ConfigureAwait(false);
                }
                break;
            }

            Utils.LogInfo("{0} reverse connection: {1} {2} after {3}ms",
                e.Accepted ? "Accepted" : "Rejected",
                e.ServerUri, e.EndpointUrl, (int)DateTime.UtcNow.Subtract(startTime).TotalMilliseconds);
        }

        /// <summary>
        /// Match the waiting connection with a registration, callback registration,
        /// return if connection is accepted in event.
        /// </summary>
        /// <returns>true if a match was found.</returns>
        private bool MatchRegistration(object sender, ConnectionWaitingEventArgs e)
        {
            Registration callbackRegistration = null;
            bool found = false;
            lock (m_registrationsLock)
            {
                // first try to match single registrations
                foreach (var registration in m_registrations.Where(r => (r.ReverseConnectStrategy & ReverseConnectStrategy.Any) == 0))
                {
                    if (registration.EndpointUrl.Scheme.Equals(e.EndpointUrl.Scheme, StringComparison.InvariantCulture) &&
                       (registration.ServerUri == e.ServerUri ||
                        registration.EndpointUrl.Authority.Equals(e.EndpointUrl.Authority, StringComparison.InvariantCulture)))
                    {
                        callbackRegistration = registration;
                        e.Accepted = true;
                        found = true;
                        Utils.LogInfo("Accepted reverse connection: {0} {1}", e.ServerUri, e.EndpointUrl);
                        break;
                    }
                }

                // now try any registrations.
                if (callbackRegistration == null)
                {
                    foreach (var registration in m_registrations.Where(r => (r.ReverseConnectStrategy & ReverseConnectStrategy.Any) != 0))
                    {
                        if (registration.EndpointUrl.Scheme.Equals(e.EndpointUrl.Scheme, StringComparison.InvariantCulture))
                        {
                            callbackRegistration = registration;
                            e.Accepted = true;
                            found = true;
                            Utils.LogInfo("Accept any reverse connection for approval: {0} {1}", e.ServerUri, e.EndpointUrl);
                            break;
                        }
                    }
                }

                if (callbackRegistration != null)
                {
                    if ((callbackRegistration.ReverseConnectStrategy & ReverseConnectStrategy.Once) != 0)
                    {
                        m_registrations.Remove(callbackRegistration);
                    }
                }
            }

            callbackRegistration?.OnConnectionWaiting?.Invoke(sender, e);

            return found;
        }

        /// <summary>
        /// Raised when a connection status changes.
        /// </summary>
        private void OnConnectionStatusChanged(object sender, ConnectionStatusEventArgs e)
        {
            Utils.LogInfo("Channel status: {0} {1} {2}", e.EndpointUrl, e.ChannelStatus, e.Closed);
        }

        /// <summary>
        /// Renew the cancellation token after use.
        /// </summary>
        private void CancelAndRenewTokenSource()
        {
            CancellationTokenSource cts = m_cts;
            m_cts = new CancellationTokenSource();
            cts.Cancel();
        }
        #endregion

        #region Private Fields
        private readonly object m_lock = new object();
        private ConfigurationWatcher m_configurationWatcher;
        private ApplicationType m_applicationType;
        private Type m_configType;
        private ReverseConnectClientConfiguration m_configuration;
        private Dictionary<Uri, ReverseConnectInfo> m_endpointUrls;
        private ReverseConnectManagerState m_state;
        private List<Registration> m_registrations;
        private readonly object m_registrationsLock = new object();
        private CancellationTokenSource m_cts;
        #endregion
    }
}
