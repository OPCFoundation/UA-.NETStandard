/* Copyright (c) 1996-2022 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation Corporate Members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Bindings;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua
{
    /// <summary>
    /// A base class for a UA server implementation.
    /// </summary>
    public class ServerBase : IServerBase
    {
        /// <summary>
        /// Initializes object with default values.
        /// </summary>
        public ServerBase()
        {
            ServerError = new ServiceResult(StatusCodes.BadServerHalted);
            m_requestQueue = new RequestQueue(this, 10, 100, 1000);
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
            if (disposing && !m_disposed)
            {
                m_disposed = true;
                // dispose any listeners.
                if (TransportListeners != null)
                {
                    for (int ii = 0; ii < TransportListeners.Count; ii++)
                    {
                        Utils.SilentDispose(TransportListeners[ii]);
                    }

                    TransportListeners.Clear();
                }

                // dispose any hosts.
                if (ServiceHosts != null)
                {
                    for (int ii = 0; ii < ServiceHosts.Count; ii++)
                    {
                        Utils.SilentDispose(ServiceHosts[ii]);
                    }

                    ServiceHosts.Clear();
                }

                Utils.SilentDispose(m_requestQueue);
            }
        }

        /// <summary>
        /// The message context to use with the service.
        /// </summary>
        /// <value>The message context that stores context information associated with a UA
        /// server that is used during message processing.
        /// </value>
        /// <exception cref="ServiceResultException">if server was not started</exception>
        public IServiceMessageContext MessageContext
        {
            get => m_messageContext ?? throw new ServiceResultException(StatusCodes.BadServerHalted);
            private set
            {
                m_messageContext = value;
                if (m_telemetry != value.Telemetry)
                {
                    m_telemetry = value.Telemetry;
                    m_logger = m_telemetry.CreateLogger(this);
                }
            }
        }

        /// <summary>
        /// An error condition that describes why the server if not running (null if no error exists).
        /// </summary>
        /// <value>The object that combines the status code and diagnostic info structures.</value>
        public ServiceResult ServerError { get; protected set; }

        /// <summary>
        /// Returns the endpoints supported by the server.
        /// </summary>
        /// <returns>Returns a collection of EndpointDescription.</returns>
        public virtual EndpointDescriptionCollection GetEndpoints()
        {
            ReadOnlyList<EndpointDescription> endpoints = Endpoints;

            if (endpoints != null)
            {
                return [.. endpoints];
            }

            return [];
        }

        /// <summary>
        /// Schedules an incoming request.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <param name="cancellationToken">The cancellationToken</param>
        public virtual void ScheduleIncomingRequest(
            IEndpointIncomingRequest request,
            CancellationToken cancellationToken = default)
        {
            m_requestQueue.ScheduleIncomingRequest(request);
        }

        /// <summary>
        /// Trys to get the secure channel id for an AuthenticationToken.
        /// The ChannelId is known to the sessions of the Server.
        /// Each session has an AuthenticationToken which can be used to identify the session.
        /// </summary>
        /// <param name="authenticationToken">The AuthenticationToken from the RequestHeader</param>
        /// <param name="channelId">The Channel id</param>
        /// <returns>returns true if a channelId was found for the provided AuthenticationToken</returns>
        public virtual bool TryGetSecureChannelIdForAuthenticationToken(
            NodeId authenticationToken,
            out uint channelId)
        {
            channelId = 0;
            return false;
        }

        /// <inheritdoc/>
        public virtual void ReportAuditOpenSecureChannelEvent(
            string globalChannelId,
            EndpointDescription endpointDescription,
            OpenSecureChannelRequest request,
            X509Certificate2 clientCertificate,
            Exception exception)
        {
            // raise an audit open secure channel event.
        }

        /// <inheritdoc/>
        public virtual void ReportAuditCloseSecureChannelEvent(
            string globalChannelId,
            Exception exception)
        {
            // raise an audit close secure channel event.
        }

        /// <inheritdoc/>
        public virtual void ReportAuditCertificateEvent(
            X509Certificate2 clientCertificate,
            Exception exception)
        {
            // raise the audit certificate
        }

        /// <summary>
        /// Raised when the status of a monitored connection changes.
        /// </summary>
        public event EventHandler<ConnectionStatusEventArgs> ConnectionStatusChanged;

        /// <summary>
        /// Raised when a connection arrives and is waiting for a callback.
        /// </summary>
        protected virtual void OnConnectionStatusChanged(object sender, ConnectionStatusEventArgs e)
        {
            ConnectionStatusChanged?.Invoke(sender, e);
        }

        /// <summary>
        /// Creates a new connection with a client.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        public void CreateConnection(Uri url, int timeout)
        {
            ITransportListener listener = null;

            m_logger.LogInformation("Create Reverse Connection to Client at {Url}.", url);

            if (TransportListeners != null)
            {
                foreach (ITransportListener ii in TransportListeners)
                {
                    if (ii.UriScheme == url.Scheme)
                    {
                        listener = ii;
                        listener.CreateReverseConnection(url, timeout);
                    }
                }
            }

            if (listener == null)
            {
                throw new ArgumentException("No suitable listener found.", nameof(url));
            }
        }

        /// <summary>
        /// Starts the server.
        /// </summary>
        /// <param name="configuration">The object that stores the configurable configuration information
        /// for a UA application</param>
        /// <param name="cancellationToken">The cancellation token</param>
        /// <param name="baseAddresses">The array of Uri elements which contains base addresses.</param>
        /// <returns>Returns a host for a UA service.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <c>null</c>.</exception>
        /// <exception cref="ServiceResultException"></exception>
        public async ValueTask<ServiceHost> StartAsync(
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default,
            params Uri[] baseAddresses)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            // do any pre-startup processing
            OnServerStarting(configuration);

            // initialize the request queue from the configuration.
            InitializeRequestQueue(configuration);

            // create the binding factory.
            ITransportListenerBindings bindingFactory = TransportBindings.Listeners;

            // initialize the server capabilities
            ServerCapabilities = configuration.ServerConfiguration.ServerCapabilities;

            // initialize the base addresses.
            InitializeBaseAddresses(configuration);

            // initialize the hosts.

            IList<ServiceHost> hosts = InitializeServiceHosts(
                configuration,
                bindingFactory,
                out ApplicationDescription serverDescription,
                out EndpointDescriptionCollection endpoints);

            // save discovery information.
            ServerDescription = serverDescription;
            Endpoints = new ReadOnlyList<EndpointDescription>(endpoints);

            // start the application.
            await StartApplicationAsync(configuration, cancellationToken)
                .ConfigureAwait(false);

            // the configuration file may specify multiple security policies or non-HTTP protocols
            // which will require multiple service hosts. the default host will be opened by WCF when
            // it is returned from this function. The others must be opened here.

            if (hosts == null || hosts.Count == 0)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "The UA server does not have a default host.");
            }

            lock (ServiceHosts)
            {
                for (int ii = 1; ii < hosts.Count; ii++)
                {
                    hosts[ii].Open();
                    ServiceHosts.Add(hosts[ii]);
                }
            }

            return hosts[0];
        }

        /// <summary>
        /// Starts the server (called from a dedicated host process).
        /// </summary>
        /// <param name="configuration">The object that stores the configurable configuration
        /// information for a UA application.</param>
        /// <param name="cancellationToken">Thee cancellation token</param>
        /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <c>null</c>.</exception>
        public async ValueTask StartAsync(ApplicationConfiguration configuration, CancellationToken cancellationToken = default)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            // do any pre-startup processing
            OnServerStarting(configuration);

            // initialize the request queue from the configuration.
            InitializeRequestQueue(configuration);

            // create the listener factory.
            ITransportListenerBindings bindingFactory = TransportBindings.Listeners;

            // initialize the server capabilities
            ServerCapabilities = configuration.ServerConfiguration.ServerCapabilities;

            // initialize the base addresses.
            InitializeBaseAddresses(configuration);

            // initialize the hosts.

            IList<ServiceHost> hosts = InitializeServiceHosts(
                configuration,
                bindingFactory,
                out ApplicationDescription serverDescription,
                out EndpointDescriptionCollection endpoints);

            // save discovery information.
            ServerDescription = serverDescription;
            Endpoints = new ReadOnlyList<EndpointDescription>(endpoints);

            // start the application.
            await StartApplicationAsync(configuration, cancellationToken)
                .ConfigureAwait(false);

            // open the hosts.
            lock (ServiceHosts)
            {
                foreach (ServiceHost serviceHost in hosts)
                {
                    serviceHost.Open();
                    ServiceHosts.Add(serviceHost);
                }
            }
        }

        /// <summary>
        /// Initializes the list of base addresses.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        protected void InitializeBaseAddresses(ApplicationConfiguration configuration)
        {
            BaseAddresses = [];

            StringCollection sourceBaseAddresses = null;
            StringCollection sourceAlternateAddresses = null;

            if (configuration.ServerConfiguration != null)
            {
                sourceBaseAddresses = configuration.ServerConfiguration.BaseAddresses;
                sourceAlternateAddresses = configuration.ServerConfiguration.AlternateBaseAddresses;
            }

            if (configuration.DiscoveryServerConfiguration != null)
            {
                sourceBaseAddresses = configuration.DiscoveryServerConfiguration.BaseAddresses;
                sourceAlternateAddresses = configuration.DiscoveryServerConfiguration
                    .AlternateBaseAddresses;
            }

            if (sourceBaseAddresses == null)
            {
                return;
            }

            foreach (string baseAddress in sourceBaseAddresses)
            {
                var address = new BaseAddress { Url = new Uri(baseAddress) };

                if (sourceAlternateAddresses != null)
                {
                    foreach (string alternateAddress in sourceAlternateAddresses)
                    {
                        var alternateUrl = new Uri(alternateAddress);

                        if (alternateUrl.Scheme == address.Url.Scheme)
                        {
                            (address.AlternateUrls ??= []).Add(alternateUrl);
                        }
                    }
                }

                switch (address.Url.Scheme)
                {
                    case Utils.UriSchemeHttps:
                    case Utils.UriSchemeOpcHttps:
                        address.ProfileUri = Profiles.HttpsBinaryTransport;
                        address.DiscoveryUrl = address.Url;
                        break;
                    case Utils.UriSchemeOpcTcp:
                        address.ProfileUri = Profiles.UaTcpTransport;
                        address.DiscoveryUrl = address.Url;
                        break;
                    case Utils.UriSchemeOpcWss:
                        address.ProfileUri = Profiles.UaWssTransport;
                        address.DiscoveryUrl = address.Url;
                        break;
                    default:
                        throw new ServiceResultException(StatusCodes.BadConfigurationError,
                            $"Unsupported scheme for base address: {address.Url}");
                }

                BaseAddresses.Add(address);
            }
        }

        /// <summary>
        /// Returns the discovery URLs for the server.
        /// </summary>
        protected StringCollection GetDiscoveryUrls()
        {
            // build list of discovery uris.
            var discoveryUrls = new StringCollection();
            string computerName = Utils.GetHostName();

            foreach (BaseAddress baseAddress in BaseAddresses)
            {
                var builder = new UriBuilder(baseAddress.DiscoveryUrl);

                int index = builder.Host.IndexOf("localhost", StringComparison.OrdinalIgnoreCase);

                if (index == -1)
                {
                    index = builder.Host.IndexOf("{0}", StringComparison.OrdinalIgnoreCase);
                }

                if (index != -1)
                {
                    builder.Host = computerName;
                }

                discoveryUrls.Add(builder.ToString());

                if (baseAddress.AlternateUrls != null)
                {
                    foreach (Uri alternateUrl in baseAddress.AlternateUrls)
                    {
                        builder = new UriBuilder(alternateUrl);
                        discoveryUrls.Add(builder.ToString());
                    }
                }
            }

            return discoveryUrls;
        }

        /// <summary>
        /// Initializes the request queue.
        /// </summary>
        /// <param name="configuration">The configuration.</param>
        protected void InitializeRequestQueue(ApplicationConfiguration configuration)
        {
            // set suitable defaults.
            int minRequestThreadCount = 10;
            int maxRequestThreadCount = 1000;
            int maxQueuedRequestCount = 2000;

            if (configuration.ServerConfiguration != null)
            {
                minRequestThreadCount = configuration.ServerConfiguration.MinRequestThreadCount;
                maxRequestThreadCount = configuration.ServerConfiguration.MaxRequestThreadCount;
                maxQueuedRequestCount = configuration.ServerConfiguration.MaxQueuedRequestCount;
            }
            else if (configuration.DiscoveryServerConfiguration != null)
            {
                minRequestThreadCount = configuration.DiscoveryServerConfiguration
                    .MinRequestThreadCount;
                maxRequestThreadCount = configuration.DiscoveryServerConfiguration
                    .MaxRequestThreadCount;
                maxQueuedRequestCount = configuration.DiscoveryServerConfiguration
                    .MaxQueuedRequestCount;
            }

            // ensure configuration errors don't render the server inoperable.
            if (minRequestThreadCount < 1)
            {
                minRequestThreadCount = 1;
            }

            if (maxRequestThreadCount < minRequestThreadCount)
            {
                maxRequestThreadCount = minRequestThreadCount;
            }

            if (maxRequestThreadCount < 100)
            {
                maxRequestThreadCount = 100;
            }

            if (maxQueuedRequestCount < 100)
            {
                maxQueuedRequestCount = 100;
            }

            Utils.SilentDispose(m_requestQueue);
            m_requestQueue = new RequestQueue(
                this,
                minRequestThreadCount,
                maxRequestThreadCount,
                maxQueuedRequestCount);
        }

        /// <summary>
        /// Stops the server and releases all resources.
        /// </summary>
        public virtual async ValueTask StopAsync(CancellationToken cancellationToken = default)
        {
            // do any pre-stop processing.
            try
            {
                await OnServerStoppingAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                ServerError = new ServiceResult(e);
            }

            // close any listeners.
            List<ITransportListener> listeners = TransportListeners;

            if (listeners != null)
            {
                for (int ii = 0; ii < listeners.Count; ii++)
                {
                    try
                    {
                        listeners[ii].Close();
                    }
                    catch (Exception e)
                    {
                        m_logger.LogError(
                            e,
                            "Unexpected error closing a listener {Name}.",
                            listeners[ii].GetType().FullName);
                    }
                }

                listeners.Clear();
            }

            // close the hosts.
            lock (ServiceHosts)
            {
                foreach (ServiceHost host in ServiceHosts)
                {
                    if (host.State == ServiceHostState.Opened)
                    {
                        host.Abort();
                    }
                    host.Close();
                }
            }

            m_messageContext = null;
        }

        /// <summary>
        /// Creates an instance of the service host.
        /// </summary>
        public virtual ServiceHost CreateServiceHost(ServerBase server, params Uri[] addresses)
        {
            return null;
        }

        /// <summary>
        /// Specifies if the server requires encryption; if so the server needs to send its certificate to the clients and validate the client certificates
        /// </summary>
        /// <param name="description">The description.</param>
        public static bool RequireEncryption(EndpointDescription description)
        {
            bool requireEncryption = false;

            if (description != null)
            {
                requireEncryption = description.SecurityPolicyUri != SecurityPolicies.None;

                if (!requireEncryption)
                {
                    foreach (UserTokenPolicy userTokenPolicy in description.UserIdentityTokens)
                    {
                        if (userTokenPolicy.SecurityPolicyUri != SecurityPolicies.None)
                        {
                            requireEncryption = true;
                            break;
                        }
                    }
                }
            }
            return requireEncryption;
        }

        /// <summary>
        /// Sets the Server Certificate in an Endpoint description if the description requires encryption.
        /// </summary>
        /// <param name="description">the endpoint Description to set the server certificate</param>
        /// <param name="certificateTypesProvider">The provider to get the server certificate per certificate type.</param>
        /// <param name="checkRequireEncryption">only set certificate if the endpoint does require Encryption</param>
        public static void SetServerCertificateInEndpointDescription(
            EndpointDescription description,
            CertificateTypesProvider certificateTypesProvider,
            bool checkRequireEncryption = true)
        {
            if (!checkRequireEncryption || RequireEncryption(description))
            {
                X509Certificate2 serverCertificate = certificateTypesProvider
                    .GetInstanceCertificate(
                        description.SecurityPolicyUri);
                // check if complete chain should be sent.
                if (certificateTypesProvider.SendCertificateChain)
                {
                    description.ServerCertificate = certificateTypesProvider
                        .LoadCertificateChainRaw(serverCertificate);
                }
                else
                {
                    description.ServerCertificate = serverCertificate.RawData;
                }
            }
        }

        /// <summary>
        /// Stores information about a base address.
        /// </summary>
        protected class BaseAddress
        {
            /// <summary>
            /// The URL for the base address.
            /// </summary>
            public Uri Url { get; set; }

            /// <summary>
            /// Alternate URLs for the base address.
            /// </summary>
            public List<Uri> AlternateUrls { get; set; }

            /// <summary>
            /// The profile URL for the address.
            /// </summary>
            public string ProfileUri { get; set; }

            /// <summary>
            /// The discovery URL for the address.
            /// </summary>
            public Uri DiscoveryUrl { get; set; }
        }

        /// <summary>
        /// Gets the list of base addresses supported by the server.
        /// </summary>
        protected IList<BaseAddress> BaseAddresses { get; set; }

        /// <summary>
        /// Gets the list of endpoints supported by the server.
        /// </summary>
        protected ReadOnlyList<EndpointDescription> Endpoints { get; private set; }

        /// <summary>
        /// The object used to verify client certificates
        /// </summary>
        /// <value>The identifier for an X509 certificate.</value>
        public CertificateValidator CertificateValidator { get; private set; }

        /// <summary>
        /// The server's application instance certificate types provider.
        /// </summary>
        /// <value>The provider for the X.509 certificates.</value>
        public CertificateTypesProvider InstanceCertificateTypesProvider { get; private set; }

        /// <summary>
        /// The non-configurable properties for the server.
        /// </summary>
        /// <value>The properties of the current server instance.</value>
        protected ServerProperties ServerProperties { get; private set; }

        /// <summary>
        /// The configuration for the server.
        /// </summary>
        /// <value>Object that stores the configurable configuration information for a UA application</value>
        protected ApplicationConfiguration Configuration { get; private set; }

        /// <summary>
        /// The application description for the server.
        /// </summary>
        /// <value>Object that contains a description for the ApplicationDescription DataType.</value>
        protected ApplicationDescription ServerDescription { get; private set; }

        /// <summary>
        /// Gets the list of service hosts used by the server instance.
        /// </summary>
        /// <value>The service hosts.</value>
        protected List<ServiceHost> ServiceHosts { get; } = [];

        /// <summary>
        /// Gets or set the capabilities for the server.
        /// </summary>
        protected StringCollection ServerCapabilities { get; set; }

        /// <summary>
        /// Gets the list of transport listeners used by the server instance.
        /// </summary>
        /// <value>The transport listeners.</value>
        protected List<ITransportListener> TransportListeners { get; } = [];

        /// <summary>
        /// Returns the service contract to use.
        /// </summary>
        protected virtual Type GetServiceContract()
        {
            return null;
        }

        /// <summary>
        /// Returns an instance of the endpoint to use.
        /// </summary>
        protected virtual EndpointBase GetEndpointInstance(ServerBase server)
        {
            return null;
        }

        /// <summary>
        /// Called after the application certificate update.
        /// </summary>
        protected virtual async void OnCertificateUpdateAsync(object sender, CertificateUpdateEventArgs e)
        {
            try
            {
                InstanceCertificateTypesProvider.Update(e.SecurityConfiguration);

                foreach (
                    CertificateIdentifier certificateIdentifier in Configuration
                        .SecurityConfiguration
                        .ApplicationCertificates)
                {
                    // preload chain
                    X509Certificate2 certificate = await certificateIdentifier.FindAsync(false)
                        .ConfigureAwait(false);
                    await InstanceCertificateTypesProvider.LoadCertificateChainAsync(certificate)
                        .ConfigureAwait(false);
                }

                //update certificate in the endpoint descriptions
                foreach (EndpointDescription endpointDescription in Endpoints)
                {
                    SetServerCertificateInEndpointDescription(
                        endpointDescription,
                        InstanceCertificateTypesProvider);
                }

                foreach (ITransportListener listener in TransportListeners)
                {
                    listener.CertificateUpdate(
                        e.CertificateValidator,
                        InstanceCertificateTypesProvider);
                }
            }
            catch (Exception ex)
            {
                m_logger.LogError(ex, "Failed to update Instance Certificates: {0}", e);
            }
        }

        /// <summary>
        /// Create the transport listener for the service host endpoint.
        /// </summary>
        /// <param name="endpointUri">The endpoint Uri.</param>
        /// <param name="endpoints">The description of the endpoints.</param>
        /// <param name="endpointConfiguration">The configuration of the endpoints.</param>
        /// <param name="listener">The transport listener.</param>
        /// <param name="certificateValidator">The certificate validator for the transport.</param>
        public virtual void CreateServiceHostEndpoint(
            Uri endpointUri,
            EndpointDescriptionCollection endpoints,
            EndpointConfiguration endpointConfiguration,
            ITransportListener listener,
            ICertificateValidator certificateValidator)
        {
            // create the stack listener.
            try
            {
                var settings = new TransportListenerSettings
                {
                    Descriptions = endpoints,
                    Configuration = endpointConfiguration,
                    ServerCertificateTypesProvider = InstanceCertificateTypesProvider,
                    CertificateValidator = certificateValidator,
                    NamespaceUris = MessageContext.NamespaceUris,
                    Factory = MessageContext.Factory,
                    MaxChannelCount = 0
                };

                settings.MaxChannelCount = Configuration.ServerConfiguration.MaxChannelCount;
                if (Utils.IsUriHttpsScheme(endpointUri.AbsoluteUri))
                {
                    settings.HttpsMutualTls = Configuration.ServerConfiguration
                        .HttpsMutualTls;
                }

                listener.Open(endpointUri, settings, GetEndpointInstance(this));

                TransportListeners.Add(listener);

                listener.ConnectionStatusChanged += OnConnectionStatusChanged;
            }
            catch (Exception e)
            {
                m_logger.LogError(
                    e,
                    "Could not load {Scheme} Stack Listener.",
                    endpointUri.Scheme);
                throw;
            }
        }

        /// <summary>
        /// Returns the UserTokenPolicies supported by the server.
        /// </summary>
        /// <param name="configuration">The configuration.</param>
        /// <param name="description">The description.</param>
        /// <returns>
        /// Returns a collection of UserTokenPolicy objects,
        /// the return type is <seealso cref="UserTokenPolicyCollection"/> .
        /// </returns>
        public virtual UserTokenPolicyCollection GetUserTokenPolicies(
            ApplicationConfiguration configuration,
            EndpointDescription description)
        {
            var policies = new UserTokenPolicyCollection();

            if (configuration.ServerConfiguration == null ||
                configuration.ServerConfiguration.UserTokenPolicies == null)
            {
                return policies;
            }

            foreach (UserTokenPolicy policy in configuration.ServerConfiguration.UserTokenPolicies)
            {
                var clone = (UserTokenPolicy)policy.Clone();

                if (string.IsNullOrEmpty(policy.SecurityPolicyUri) &&
                    description.SecurityMode == MessageSecurityMode.None)
                {
                    if (clone.TokenType == UserTokenType.Anonymous)
                    {
                        // no need for security with anonymous token
                        clone.SecurityPolicyUri = SecurityPolicies.None;
                    }
                    else
                    {
                        // ensure a security policy is specified for user tokens.
                        clone.SecurityPolicyUri = SecurityPolicies.Basic256Sha256;
                    }
                }

                // ensure each policy has a unique id within the context of the Server
                clone.PolicyId = Utils.Format("{0}", ++m_userTokenPolicyId);

                policies.Add(clone);
            }

            return policies;
        }

        /// <summary>
        /// Checks for IP address or well known hostnames that map to the computer.
        /// </summary>
        /// <param name="hostname">The hostname.</param>
        /// <returns>The hostname to use for URL filtering.</returns>
        protected string NormalizeHostname(string hostname)
        {
            string computerName = Utils.GetHostName();

            // substitute the computer name for localhost if localhost used by client.
            if (Utils.AreDomainsEqual(hostname, "localhost"))
            {
                return computerName.ToUpper(CultureInfo.InvariantCulture);
            }

            // check if client is using an ip address.
            if (IPAddress.TryParse(hostname, out IPAddress address))
            {
                if (IPAddress.IsLoopback(address))
                {
                    return computerName.ToUpper(CultureInfo.InvariantCulture);
                }

                // substitute the computer name for any local IP if an IP is used by client.
                IPAddress[] addresses = [];
                try
                {
                    addresses = Utils.GetHostAddresses(computerName);
                }
                catch (SocketException e)
                {
                    m_logger.LogWarning(e, "Unable to get host addresses for hostname {Name}.", hostname);
                }

                if (addresses.Length == 0)
                {
                    string fullName = Dns.GetHostName();
                    try
                    {
                        addresses = Utils.GetHostAddresses(fullName);
                    }
                    catch (SocketException e)
                    {
                        m_logger.LogError(
                            e,
                            "Unable to get host addresses for DNS hostname {Name}.",
                            fullName);
                    }
                }

                for (int ii = 0; ii < addresses.Length; ii++)
                {
                    if (addresses[ii].Equals(address))
                    {
                        return computerName.ToUpper(CultureInfo.InvariantCulture);
                    }
                }

                // not a localhost IP address.
                return hostname.ToUpper(CultureInfo.InvariantCulture);
            }

            // check for aliases.
            IPHostEntry entry = null;

            try
            {
                entry = Dns.GetHostEntry(computerName);
            }
            catch (SocketException e)
            {
                m_logger.LogError(e, "Unable to check aliases for hostname {Name}.", computerName);
            }

            if (entry != null)
            {
                for (int ii = 0; ii < entry.Aliases.Length; ii++)
                {
                    if (Utils.AreDomainsEqual(hostname, entry.Aliases[ii]))
                    {
                        return computerName.ToUpper(CultureInfo.InvariantCulture);
                    }
                }
            }

            // return normalized hostname.
            return hostname.ToUpper(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Filters the list of addresses by profile.
        /// </summary>
        protected IList<BaseAddress> FilterByProfile(
            StringCollection profileUris,
            IList<BaseAddress> baseAddresses)
        {
            if (profileUris == null || profileUris.Count == 0)
            {
                return baseAddresses;
            }

            var filteredAddresses = new List<BaseAddress>();

            foreach (BaseAddress baseAddress in baseAddresses)
            {
                foreach (string profileUri in profileUris)
                {
                    if (baseAddress.ProfileUri == Profiles.NormalizeUri(profileUri))
                    {
                        filteredAddresses.Add(baseAddress);
                        break;
                    }
                }
            }

            return filteredAddresses;
        }

        /// <summary>
        /// Filters the list of addresses by the URL that the client provided.
        /// </summary>
        protected IList<BaseAddress> FilterByEndpointUrl(
            Uri endpointUrl,
            IList<BaseAddress> baseAddresses)
        {
            // client only gets alternate addresses that match the DNS name that it used.
            var accessibleAddresses = new List<BaseAddress>();
            foreach (BaseAddress baseAddress in baseAddresses)
            {
                if (baseAddress.Url.IdnHost == endpointUrl.IdnHost)
                {
                    accessibleAddresses.Add(baseAddress);
                    continue;
                }

                if (baseAddress.AlternateUrls != null)
                {
                    foreach (Uri alternateUrl in baseAddress.AlternateUrls)
                    {
                        if (alternateUrl.IdnHost == endpointUrl.IdnHost)
                        {
                            if (!accessibleAddresses.Any(item => item.Url == alternateUrl))
                            {
                                accessibleAddresses.Add(
                                    new BaseAddress
                                    {
                                        Url = alternateUrl,
                                        ProfileUri = baseAddress.ProfileUri,
                                        DiscoveryUrl = alternateUrl
                                    });
                            }
                            break;
                        }
                    }
                }
            }

            if (accessibleAddresses.Count != 0)
            {
                return accessibleAddresses;
            }

            // client gets all of the endpoints if it using a known variant of the hostname
            if (NormalizeHostname(endpointUrl.IdnHost) == NormalizeHostname("localhost"))
            {
                return baseAddresses;
            }

            // no match on client DNS name. client gets only addresses that match the scheme.
            foreach (BaseAddress baseAddress in baseAddresses)
            {
                if (baseAddress.Url.Scheme == endpointUrl.Scheme)
                {
                    accessibleAddresses.Add(baseAddress);
                }
            }

            if (accessibleAddresses.Count != 0)
            {
                return accessibleAddresses;
            }

            return baseAddresses;
        }

        /// <summary>
        /// Returns the best discovery URL for the base address based on the URL used by the client.
        /// </summary>
        private static string GetBestDiscoveryUrl(BaseAddress baseAddress)
        {
            string url = baseAddress.Url.ToString();

            if ((baseAddress.ProfileUri == Profiles.HttpsBinaryTransport) &&
                Utils.IsUriHttpRelatedScheme(url) &&
                (!url.EndsWith(
                    ConfiguredEndpoint.DiscoverySuffix,
                    StringComparison.OrdinalIgnoreCase)))
            {
                url += ConfiguredEndpoint.DiscoverySuffix;
            }

            return url;
        }

        /// <summary>
        /// Translates the discovery URLs based on the client url and returns an updated ApplicationDescription.
        /// </summary>
        /// <param name="clientUrl">The client URL.</param>
        /// <param name="description">The application description.</param>
        /// <param name="baseAddresses">The base addresses.</param>
        /// <param name="applicationName">The localized application name.</param>
        /// <returns>A copy of the application description</returns>
        protected ApplicationDescription TranslateApplicationDescription(
            Uri clientUrl,
            ApplicationDescription description,
            IList<BaseAddress> baseAddresses,
            LocalizedText applicationName)
        {
            // get the discovery urls.
            var discoveryUrls = new StringCollection();

            foreach (BaseAddress baseAddress in baseAddresses)
            {
                discoveryUrls.Add(GetBestDiscoveryUrl(baseAddress));
            }

            // copy the description.
            var copy = new ApplicationDescription
            {
                ApplicationName = description.ApplicationName,
                ApplicationUri = description.ApplicationUri,
                ApplicationType = description.ApplicationType,
                ProductUri = description.ProductUri,
                GatewayServerUri = description.DiscoveryProfileUri,
                DiscoveryUrls = discoveryUrls
            };

            if (!LocalizedText.IsNullOrEmpty(applicationName))
            {
                copy.ApplicationName = applicationName;
            }

            // return the copy.
            return copy;
        }

        /// <summary>
        /// Translates the endpoint descriptions based on the client url and profiles provided.
        /// </summary>
        /// <param name="clientUrl">The client URL.</param>
        /// <param name="baseAddresses">The base addresses.</param>
        /// <param name="endpoints">The endpoints.</param>
        /// <param name="application">The application to use with the endpoints.</param>
        /// <returns>The translated list of endpoints.</returns>
        protected EndpointDescriptionCollection TranslateEndpointDescriptions(
            Uri clientUrl,
            IList<BaseAddress> baseAddresses,
            IList<EndpointDescription> endpoints,
            ApplicationDescription application)
        {
            var translations = new EndpointDescriptionCollection();

            bool matchPort = false;
            do
            {
                // first round with port match
                matchPort = !matchPort;

                // process endpoints
                foreach (EndpointDescription endpoint in endpoints)
                {
                    var endpointUrl = new UriBuilder(endpoint.EndpointUrl);

                    // find matching base address.
                    foreach (BaseAddress baseAddress in baseAddresses)
                    {
                        bool translateHttpsEndpoint = false;
                        if (endpoint.TransportProfileUri == Profiles.HttpsBinaryTransport &&
                            baseAddress.ProfileUri == Profiles.HttpsBinaryTransport)
                        {
                            translateHttpsEndpoint = true;
                        }

                        if (endpoint.TransportProfileUri != baseAddress.ProfileUri &&
                            !translateHttpsEndpoint)
                        {
                            continue;
                        }

                        if (endpointUrl.Scheme != baseAddress.Url.Scheme)
                        {
                            continue;
                        }

                        // try to match port in the first round, skip in the second round
                        if (matchPort && endpointUrl.Port != baseAddress.Url.Port)
                        {
                            continue;
                        }

                        var translation = new EndpointDescription
                        {
                            EndpointUrl = baseAddress.Url.ToString()
                        };

                        if (endpointUrl.Path.StartsWith(
                                baseAddress.Url.PathAndQuery,
                                StringComparison.Ordinal) &&
                            endpointUrl.Path.Length > baseAddress.Url.PathAndQuery.Length)
                        {
                            string suffix = endpointUrl.Path[baseAddress.Url.PathAndQuery.Length..];
                            translation.EndpointUrl += suffix;
                        }

                        translation.ProxyUrl = endpoint.ProxyUrl;
                        translation.SecurityLevel = endpoint.SecurityLevel;
                        translation.SecurityMode = endpoint.SecurityMode;
                        translation.SecurityPolicyUri = endpoint.SecurityPolicyUri;
                        translation.ServerCertificate = endpoint.ServerCertificate;
                        translation.TransportProfileUri = endpoint.TransportProfileUri;
                        translation.UserIdentityTokens = endpoint.UserIdentityTokens;
                        translation.Server = application;

                        if (!translations.Exists(match =>
                                match.EndpointUrl
                                    .Equals(translation.EndpointUrl, StringComparison.Ordinal) &&
                                match.SecurityMode == translation.SecurityMode &&
                                match.SecurityPolicyUri.Equals(
                                    translation.SecurityPolicyUri,
                                    StringComparison.Ordinal)))
                        {
                            translations.Add(translation);
                        }
                    }
                }
            } while (matchPort && translations.Count == 0);

            translations.Sort(
                (ep1, ep2) => string.CompareOrdinal(ep1.EndpointUrl, ep2.EndpointUrl));

            return translations;
        }

        /// <summary>
        /// Verifies that the request header is valid.
        /// </summary>
        /// <param name="requestHeader">The object that contains description for the RequestHeader DataType.</param>
        /// <exception cref="ServiceResultException"></exception>
        protected virtual void ValidateRequest(RequestHeader requestHeader)
        {
            if (requestHeader == null)
            {
                throw new ServiceResultException(StatusCodes.BadRequestHeaderInvalid);
            }

            // mask valid diagnostic masks
            requestHeader.ReturnDiagnostics &= (uint)DiagnosticsMasks.All;
        }

        /// <summary>
        /// Creates the response header.
        /// </summary>
        /// <param name="requestHeader">The object that contains description for the RequestHeader DataType.</param>
        /// <param name="statusCode">The status code.</param>
        /// <exception cref="ServiceResultException">If statusCode is bad.</exception>
        /// <returns>Returns a description for the ResponseHeader DataType. </returns>
        protected virtual ResponseHeader CreateResponse(
            RequestHeader requestHeader,
            uint statusCode)
        {
            if (StatusCode.IsBad(statusCode))
            {
                throw new ServiceResultException(statusCode);
            }

            return new ResponseHeader
            {
                Timestamp = DateTime.UtcNow,
                RequestHandle = requestHeader.RequestHandle
            };
        }

        /// <summary>
        /// Creates the response header.
        /// </summary>
        /// <param name="requestHeader">The object that contains description for the RequestHeader DataType.</param>
        /// <param name="exception">The exception used to create DiagnosticInfo assigned to the ServiceDiagnostics.</param>
        /// <returns>Returns a description for the ResponseHeader DataType. </returns>
        protected virtual ResponseHeader CreateResponse(
            RequestHeader requestHeader,
            Exception exception)
        {
            var responseHeader = new ResponseHeader
            {
                Timestamp = DateTime.UtcNow,
                RequestHandle = requestHeader.RequestHandle
            };

            var stringTable = new StringTable();
            responseHeader.ServiceDiagnostics = new DiagnosticInfo(
                exception,
                (DiagnosticsMasks)requestHeader.ReturnDiagnostics,
                true,
                stringTable,
                m_logger);
            responseHeader.StringTable = stringTable.ToArray();

            return responseHeader;
        }

        /// <summary>
        /// Creates the response header.
        /// </summary>
        /// <param name="requestHeader">The object that contains description for the RequestHeader DataType.</param>
        /// <param name="stringTable">The thread safe table of string constants.</param>
        /// <returns>Returns a description for the ResponseHeader DataType. </returns>
        protected virtual ResponseHeader CreateResponse(
            RequestHeader requestHeader,
            StringTable stringTable)
        {
            var responseHeader = new ResponseHeader
            {
                Timestamp = DateTime.UtcNow,
                RequestHandle = requestHeader.RequestHandle
            };

            responseHeader.StringTable.AddRange(stringTable.ToArray());

            return responseHeader;
        }

        /// <summary>
        /// Called when the server configuration is changed on disk.
        /// </summary>
        /// <param name="configuration">The object that stores the configurable configuration information for a UA application.</param>
        /// <param name="cancellationToken">The cancellationToken</param>
        /// <remarks>
        /// Servers are free to ignore changes if it is difficult/impossible to apply them without a restart.
        /// </remarks>
        protected virtual ValueTask OnUpdateConfigurationAsync(ApplicationConfiguration configuration, CancellationToken cancellationToken = default)
        {
#pragma warning disable CS0618 // Type or member is obsolete
            OnUpdateConfiguration(configuration);
#pragma warning restore CS0618 // Type or member is obsolete
            return default;
        }

        /// <summary>
        /// Called when the server configuration is changed on disk.
        /// </summary>
        /// <param name="configuration">The object that stores the configurable configuration information for a UA application.</param>
        /// <remarks>
        /// Servers are free to ignore changes if it is difficult/impossible to apply them without a restart.
        /// </remarks>
        [Obsolete("User OnUpdateConfigurationAsync")]
        protected virtual void OnUpdateConfiguration(ApplicationConfiguration configuration)
        {
        }

        /// <summary>
        /// Called before the server starts.
        /// </summary>
        /// <param name="configuration">The object that stores the configurable configuration information for a UA application.</param>
        /// <exception cref="ServiceResultException"></exception>
        protected virtual void OnServerStarting(ApplicationConfiguration configuration)
        {
            // use the message context from the configuration to ensure the channels are
            // using the same one. This also sets the telemetry context for the server
            // from configuration.
            ServiceMessageContext messageContext = configuration.CreateMessageContext(true);
            messageContext.NamespaceUris = new NamespaceTable();
            MessageContext = messageContext;

            // fetch properties and configuration.
            Configuration = configuration;
            ServerProperties = LoadServerProperties();

            // ensure at least one security policy exists.
            if (configuration.ServerConfiguration != null)
            {
                if (configuration.ServerConfiguration.SecurityPolicies.Count == 0)
                {
                    configuration.ServerConfiguration.SecurityPolicies
                        .Add(new ServerSecurityPolicy());
                }

                // ensure at least one user token policy exists.
                if (configuration.ServerConfiguration.UserTokenPolicies.Count == 0)
                {
                    var userTokenPolicy = new UserTokenPolicy
                    {
                        TokenType = UserTokenType.Anonymous
                    };
                    userTokenPolicy.PolicyId = userTokenPolicy.TokenType.ToString();

                    configuration.ServerConfiguration.UserTokenPolicies.Add(userTokenPolicy);
                }
            }

            // load the instance certificate.
            X509Certificate2 defaultInstanceCertificate = null;
            InstanceCertificateTypesProvider = new CertificateTypesProvider(
                configuration,
                MessageContext.Telemetry);
            InstanceCertificateTypesProvider.InitializeAsync().GetAwaiter().GetResult();

            foreach (ServerSecurityPolicy securityPolicy in configuration.ServerConfiguration
                .SecurityPolicies)
            {
                if (securityPolicy.SecurityMode == MessageSecurityMode.None)
                {
                    continue;
                }

                X509Certificate2 instanceCertificate =
                    InstanceCertificateTypesProvider.GetInstanceCertificate(
                        securityPolicy.SecurityPolicyUri)
                    ?? throw new ServiceResultException(
                        StatusCodes.BadConfigurationError,
                        "Server does not have an instance certificate assigned.");

                if (!instanceCertificate.HasPrivateKey)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadConfigurationError,
                        "Server does not have access to the private key for the instance certificate.");
                }

                defaultInstanceCertificate ??= instanceCertificate;

                // preload chain
                InstanceCertificateTypesProvider
                    .LoadCertificateChainAsync(instanceCertificate)
                    .GetAwaiter()
                    .GetResult();
            }

            // assign a unique identifier if none specified.
            if (string.IsNullOrEmpty(configuration.ApplicationUri))
            {
                X509Certificate2 instanceCertificate = InstanceCertificateTypesProvider
                    .GetInstanceCertificate(
                        configuration.ServerConfiguration.SecurityPolicies[0].SecurityPolicyUri);

                IReadOnlyList<string> applicationUris = X509Utils.GetApplicationUrisFromCertificate(
                    instanceCertificate);
                // it is ok to pick the first here since it is only a fallback value
                configuration.ApplicationUri = applicationUris.Count > 0 ? applicationUris[0] : null;

                if (string.IsNullOrEmpty(configuration.ApplicationUri))
                {
                    configuration.ApplicationUri = Utils.Format(
                        "http://{0}/{1}/{2}",
                        Utils.GetHostName(),
                        configuration.ApplicationName,
                        Guid.NewGuid());
                }
            }

            // initialize namespace table.
            MessageContext.NamespaceUris.Append(configuration.ApplicationUri);

            // assign an instance name.
            if (string.IsNullOrEmpty(configuration.ApplicationName) &&
                defaultInstanceCertificate != null)
            {
                configuration.ApplicationName = defaultInstanceCertificate.GetNameInfo(
                    X509NameType.DnsName,
                    false);
            }

            // save the certificate validator.
            CertificateValidator = configuration.CertificateValidator;
        }

        /// <summary>
        /// Creates the endpoints and creates the hosts.
        /// </summary>
        /// <param name="configuration">The object that stores the configurable configuration information for a UA application.</param>
        /// <param name="bindingFactory">The object of a class that manages a mapping between a URL scheme and a listener.</param>
        /// <param name="serverDescription">The object of the class that contains a description for the ApplicationDescription DataType.</param>
        /// <param name="endpoints">The collection of <see cref="EndpointDescription"/> objects.</param>
        /// <returns>Returns list of hosts for a UA service.</returns>
        protected virtual IList<ServiceHost> InitializeServiceHosts(
            ApplicationConfiguration configuration,
            ITransportListenerBindings bindingFactory,
            out ApplicationDescription serverDescription,
            out EndpointDescriptionCollection endpoints)
        {
            serverDescription = null;
            endpoints = null;
            return [];
        }

        /// <summary>
        /// Starts the server application.
        /// </summary>
        /// <param name="configuration">The object that stores the configurable configuration information for a UA application.</param>
        /// <param name="cancellationToken">The cancellation token</param>
        protected virtual ValueTask StartApplicationAsync(ApplicationConfiguration configuration, CancellationToken cancellationToken = default)
        {
            // must be defined by the subclass.
            return default;
        }

        /// <summary>
        /// Starts the server application.
        /// </summary>
        /// <param name="configuration">The object that stores the configurable configuration information for a UA application.</param>
        [Obsolete("Use StartApplicationAsync")]
        protected virtual void StartApplication(ApplicationConfiguration configuration)
        {
            // must be defined by the subclass.
        }

        /// <summary>
        /// Called before the server stops
        /// </summary>
        protected virtual ValueTask OnServerStoppingAsync(CancellationToken cancellationToken = default)
        {
            // may be overridden by the subclass.
            return default;
        }

        /// <summary>
        /// Called before the server stops
        /// </summary>
        [Obsolete("Use OnServerStoppingAsync")]
        protected virtual void OnServerStopping()
        {
            // may be overridden by the subclass.
        }

        /// <summary>
        /// Returns the properties for associated with the server instance.
        /// </summary>
        /// <returns>Returns the properties of the current server instance.</returns>
        protected virtual ServerProperties LoadServerProperties()
        {
            return new ServerProperties();
        }

        /// <summary>
        /// Processes the request.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        protected virtual async Task ProcessRequestAsync(
            IEndpointIncomingRequest request,
            CancellationToken cancellationToken = default)
        {
            await request.CallAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Asynchronously manages a queue of requests.
        /// </summary>
        protected class RequestQueue : IDisposable
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="RequestQueue"/> class.
            /// </summary>
            /// <param name="server">The server.</param>
            /// <param name="minThreadCount">The minimum number of threads in the pool.</param>
            /// <param name="maxThreadCount">The maximum number of threads  in the pool.</param>
            /// <param name="maxRequestCount">The maximum number of requests that will placed in the queue.</param>
            public RequestQueue(
                ServerBase server,
                int minThreadCount,
                int maxThreadCount,
                int maxRequestCount)
            {
                m_server = server;
                m_minThreadCount = minThreadCount;
                m_maxThreadCount = maxThreadCount;
                m_maxRequestCount = maxRequestCount;
                m_queue = new ConcurrentQueue<IEndpointIncomingRequest>();
                m_queueSignal = new SemaphoreSlim(0);
                m_workers = [];
                m_cts = new CancellationTokenSource();
                m_activeThreadCount = 0;
                m_totalThreadCount = 0;
                m_queuedRequestsCount = 0;
                m_stopped = false;

                ThreadPool.GetMinThreads(out minThreadCount, out int minCompletionPortThreads);

                ThreadPool.SetMinThreads(
                    Math.Max(minThreadCount, m_minThreadCount),
                    Math.Max(minCompletionPortThreads, m_minThreadCount)
                );

                ThreadPool.GetMaxThreads(out maxThreadCount, out int maxCompletionPortThreads);

                ThreadPool.SetMaxThreads(
                    Math.Max(maxThreadCount, m_maxThreadCount),
                    Math.Max(maxCompletionPortThreads, m_maxThreadCount)
                );

                // Start worker tasks
                for (int i = 0; i < m_minThreadCount; i++)
                {
                    m_workers.Add(Task.Run(() => WorkerLoopAsync(m_cts.Token)));
                }
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
                    m_stopped = true;
                    m_cts.Cancel();

                    if (m_totalThreadCount > 0)
                    {
                        m_queueSignal.Release(m_totalThreadCount); // Unblock all workers
                    }
                    Utils.SilentDispose(m_queueSignal);

                    foreach (IEndpointIncomingRequest request in m_queue.ToList())
                    {
                        Utils.SilentDispose(request);
                    }
#if NETSTANDARD2_1_OR_GREATER
                    m_queue.Clear();
#endif
                    Utils.SilentDispose(m_cts);
                }
            }

            /// <summary>
            /// Schedules an incoming request.
            /// </summary>
            /// <param name="request">The request.</param>
            public void ScheduleIncomingRequest(IEndpointIncomingRequest request)
            {
                // check if server is stopped
                if (m_stopped)
                {
                    request.OperationCompleted(null, StatusCodes.BadServerHalted);
                    return;
                }

                // check if we can accept more requests
                if (m_queuedRequestsCount >= m_maxRequestCount)
                {
                    request.OperationCompleted(null, StatusCodes.BadServerTooBusy);
                    // TODO: make a metric
                    m_server.m_logger.LogDebug("Too many operations. Active threads: {Count}", m_activeThreadCount);
                    return;
                }
                // Optionally scale up workers if needed
                if (m_totalThreadCount < m_maxThreadCount &&
                    m_activeThreadCount >= m_totalThreadCount)
                {
                    lock (m_workers)
                    {
                        m_workers.Add(Task.Run(() => WorkerLoopAsync(m_cts.Token)));
                    }
                }
                // Enqueue requests
                m_queue.Enqueue(request);
                Interlocked.Increment(ref m_queuedRequestsCount);
                m_queueSignal.Release();
            }

            /// <summary>
            /// Ran by the worker threads to process requests.
            /// </summary>
            /// <returns></returns>
            private async Task WorkerLoopAsync(CancellationToken ct)
            {
                Interlocked.Increment(ref m_totalThreadCount);
                try
                {
                    while (!ct.IsCancellationRequested)
                    {
                        // wait for a request
                        if ((!await m_queueSignal.WaitAsync(15_000, ct).ConfigureAwait(false)) &&
                            m_totalThreadCount > m_minThreadCount)
                        {
                            //end loop if no requests and we have enough threads
                            return;
                        }

                        //process request from queue
                        if (m_queue.TryDequeue(out IEndpointIncomingRequest request))
                        {
                            try
                            {
                                Interlocked.Decrement(ref m_queuedRequestsCount);
                                Interlocked.Increment(ref m_activeThreadCount);
                                await m_server.ProcessRequestAsync(request, ct)
                                    .ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            {
                                m_server.m_logger.LogError(ex, "Unexpected error processing incoming request.");
                                request.OperationCompleted(null, StatusCodes.BadInternalError);
                            }
                            finally
                            {
                                Interlocked.Decrement(ref m_activeThreadCount);
                            }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // Graceful shutdown
                }
                finally
                {
                    Interlocked.Decrement(ref m_totalThreadCount);
                }
            }

            private readonly ServerBase m_server;
            private readonly int m_minThreadCount;
            private readonly int m_maxThreadCount;
            private readonly int m_maxRequestCount;
            private readonly ConcurrentQueue<IEndpointIncomingRequest> m_queue;
            private readonly SemaphoreSlim m_queueSignal;
            private readonly List<Task> m_workers;
            private readonly CancellationTokenSource m_cts;
            private int m_activeThreadCount;
            private int m_totalThreadCount;
            private int m_queuedRequestsCount;
            private bool m_stopped;
        }

        /// <summary>
        /// Logger instance for the server which is set when setting the
        /// Telemetry member. Shall only be used by concrete implementations
        /// deriving from this class.
        /// </summary>
#pragma warning disable IDE1006 // Naming Styles
        protected ILogger m_logger { get; private set; } = LoggerUtils.Null.Logger;
#pragma warning restore IDE1006 // Naming Styles

        private IServiceMessageContext m_messageContext;
        private RequestQueue m_requestQueue;
        private ITelemetryContext m_telemetry;

        /// <summary>
        /// identifier for the UserTokenPolicy should be unique within the context of a single Server
        /// </summary>
        private int m_userTokenPolicyId;
        private bool m_disposed;
    }
}
