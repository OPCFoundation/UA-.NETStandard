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

// use a thread scheduler with dedicated worker threads
#define THREAD_SCHEDULER

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Logging;
using Opc.Ua.Bindings;

namespace Opc.Ua
{
    /// <summary>
    /// A base class for a UA server implementation.
    /// </summary>
    public partial class ServerBase : IServerBase, IDisposable
    {
        #region Constructors
        /// <summary>
        /// Initializes object with default values.
        /// </summary>
        public ServerBase()
        {
            m_messageContext = new ServiceMessageContext();
            m_serverError = new ServiceResult(StatusCodes.BadServerHalted);
            m_hosts = new List<ServiceHost>();
            m_listeners = new List<ITransportListener>();
            m_endpoints = null;
            m_requestQueue = new RequestQueue(this, 10, 100, 1000);
            m_userTokenPolicyId = 0;
        }
        #endregion

        #region IDisposable Members
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
                // dispose any listeners.
                if (m_listeners != null)
                {
                    for (int ii = 0; ii < m_listeners.Count; ii++)
                    {
                        Utils.SilentDispose(m_listeners[ii]);
                    }

                    m_listeners.Clear();
                }

                // dispose any hosts.
                if (m_hosts != null)
                {
                    for (int ii = 0; ii < m_hosts.Count; ii++)
                    {
                        Utils.SilentDispose(m_hosts[ii]);
                    }

                    m_hosts.Clear();
                }

                Utils.SilentDispose(m_requestQueue);
            }
        }
        #endregion

        #region IServerBase Members
        /// <summary>
        /// The message context to use with the service.
        /// </summary>
        /// <value>The message context that stores context information associated with a UA 
        /// server that is used during message processing.
        /// </value>
        public IServiceMessageContext MessageContext
        {
            get
            {
                return (IServiceMessageContext)m_messageContext;
            }

            set
            {
                Interlocked.Exchange(ref m_messageContext, value);
            }
        }

        /// <summary>
        /// An error condition that describes why the server if not running (null if no error exists).
        /// </summary>
        /// <value>The object that combines the status code and diagnostic info structures.</value>
        public ServiceResult ServerError
        {
            get
            {
                return (ServiceResult)m_serverError;
            }

            set
            {
                Interlocked.Exchange(ref m_serverError, value);
            }
        }

        /// <summary>
        /// Returns the endpoints supported by the server.
        /// </summary>
        /// <returns>Returns a collection of EndpointDescription.</returns>
        public virtual EndpointDescriptionCollection GetEndpoints()
        {
            ReadOnlyList<EndpointDescription> endpoints = m_endpoints;

            if (endpoints != null)
            {
                return new EndpointDescriptionCollection(endpoints);
            }

            return new EndpointDescriptionCollection();
        }

        /// <summary>
        /// Schedules an incoming request.
        /// </summary>
        /// <param name="request">The request.</param>
        public virtual void ScheduleIncomingRequest(IEndpointIncomingRequest request)
        {
            m_requestQueue.ScheduleIncomingRequest(request);
        }

        #region IAuditEventCallback Members
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
        #endregion

        #endregion

        #region Public Methods
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
        public void CreateConnection(Uri url, int timeout)
        {
            ITransportListener listener = null;

            Utils.LogInfo("Create Reverse Connection to Client at {0}.", url);

            if (TransportListeners != null)
            {
                foreach (var ii in TransportListeners)
                {
                    if (ii.UriScheme == url.Scheme)
                    {
                        listener = ii;
                        break;
                    }
                }
            }

            if (listener == null)
            {
                throw new ArgumentException("No suitable listener found.", nameof(url));
            }

            listener.CreateReverseConnection(url, timeout);
        }

        /// <summary>
        /// Starts the server.
        /// </summary>
        /// <param name="configuration">The object that stores the configurable configuration information 
        /// for a UA application</param>
        /// <param name="baseAddresses">The array of Uri elements which contains base addresses.</param>
        /// <returns>Returns a host for a UA service.</returns>
        public ServiceHost Start(ApplicationConfiguration configuration, params Uri[] baseAddresses)
        {
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));

            // do any pre-startup processing
            OnServerStarting(configuration);

            // initialize the request queue from the configuration.
            InitializeRequestQueue(configuration);

            // create the binding factory.
            TransportListenerBindings bindingFactory = TransportBindings.Listeners;

            // initialize the server capabilities
            ServerCapabilities = configuration.ServerConfiguration.ServerCapabilities;

            // initialize the base addresses.
            InitializeBaseAddresses(configuration);

            // initialize the hosts.
            ApplicationDescription serverDescription = null;
            EndpointDescriptionCollection endpoints = null;

            IList<ServiceHost> hosts = InitializeServiceHosts(
                configuration,
                bindingFactory,
                out serverDescription,
                out endpoints);

            // save discovery information.
            ServerDescription = serverDescription;
            m_endpoints = new ReadOnlyList<EndpointDescription>(endpoints);

            // start the application.
            StartApplication(configuration);

            // the configuration file may specify multiple security policies or non-HTTP protocols
            // which will require multiple service hosts. the default host will be opened by WCF when
            // it is returned from this function. The others must be opened here.

            if (hosts == null || hosts.Count == 0)
            {
                throw ServiceResultException.Create(StatusCodes.BadConfigurationError, "The UA server does not have a default host.");
            }

            lock (m_hosts)
            {
                for (int ii = 1; ii < hosts.Count; ii++)
                {
                    hosts[ii].Open();
                    m_hosts.Add(hosts[ii]);
                }
            }

            return hosts[0];
        }

        /// <summary>
        /// Starts the server (called from a dedicated host process).
        /// </summary>
        /// <param name="configuration">The object that stores the configurable configuration 
        /// information for a UA application. 
        /// </param>
        public void Start(ApplicationConfiguration configuration)
        {
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));

            // do any pre-startup processing
            OnServerStarting(configuration);

            // initialize the request queue from the configuration.
            InitializeRequestQueue(configuration);

            // create the listener factory.
            TransportListenerBindings bindingFactory = TransportBindings.Listeners;

            // initialize the server capabilities
            ServerCapabilities = configuration.ServerConfiguration.ServerCapabilities;

            // initialize the base addresses.
            InitializeBaseAddresses(configuration);

            // initialize the hosts.
            ApplicationDescription serverDescription = null;
            EndpointDescriptionCollection endpoints = null;

            IList<ServiceHost> hosts = InitializeServiceHosts(
                configuration,
                bindingFactory,
                out serverDescription,
                out endpoints);

            // save discovery information.
            ServerDescription = serverDescription;
            m_endpoints = new ReadOnlyList<EndpointDescription>(endpoints);

            // start the application.
            StartApplication(configuration);

            // open the hosts.
            lock (m_hosts)
            {
                foreach (ServiceHost serviceHost in hosts)
                {
                    serviceHost.Open();
                    m_hosts.Add(serviceHost);
                }
            }
        }

        /// <summary>
        /// Initializes the list of base addresses.
        /// </summary>
        private void InitializeBaseAddresses(ApplicationConfiguration configuration)
        {
            BaseAddresses = new List<BaseAddress>();

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
                sourceAlternateAddresses = configuration.DiscoveryServerConfiguration.AlternateBaseAddresses;
            }

            if (sourceBaseAddresses == null)
            {
                return;
            }

            foreach (string baseAddress in sourceBaseAddresses)
            {
                BaseAddress address = new BaseAddress() { Url = new Uri(baseAddress) };

                if (sourceAlternateAddresses != null)
                {
                    foreach (string alternateAddress in sourceAlternateAddresses)
                    {
                        Uri alternateUrl = new Uri(alternateAddress);

                        if (alternateUrl.Scheme == address.Url.Scheme)
                        {
                            if (address.AlternateUrls == null)
                            {
                                address.AlternateUrls = new List<Uri>();
                            }

                            address.AlternateUrls.Add(alternateUrl);
                        }
                    }
                }

                switch (address.Url.Scheme)
                {
                    case Utils.UriSchemeHttps:
                    {
                        address.ProfileUri = Profiles.HttpsBinaryTransport;
                        address.DiscoveryUrl = address.Url;
                        break;
                    }

                    case Utils.UriSchemeOpcTcp:
                    {
                        address.ProfileUri = Profiles.UaTcpTransport;
                        address.DiscoveryUrl = address.Url;
                        break;
                    }
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
            StringCollection discoveryUrls = new StringCollection();
            string computerName = Utils.GetHostName();

            foreach (BaseAddress baseAddress in BaseAddresses)
            {
                UriBuilder builder = new UriBuilder(baseAddress.DiscoveryUrl);

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
                minRequestThreadCount = configuration.DiscoveryServerConfiguration.MinRequestThreadCount;
                maxRequestThreadCount = configuration.DiscoveryServerConfiguration.MaxRequestThreadCount;
                maxQueuedRequestCount = configuration.DiscoveryServerConfiguration.MaxQueuedRequestCount;
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

            if (m_requestQueue != null)
            {
                m_requestQueue.Dispose();
            }

            m_requestQueue = new RequestQueue(this, minRequestThreadCount, maxRequestThreadCount, maxQueuedRequestCount);
        }

        /// <summary>
        /// Stops the server and releases all resources.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1716:IdentifiersShouldNotMatchKeywords", MessageId = "Stop")]
        public virtual void Stop()
        {
            // do any pre-stop processing.
            try
            {
                OnServerStopping();
            }
            catch (Exception e)
            {
                m_serverError = new ServiceResult(e);
            }

            // close any listeners.
            List<ITransportListener> listeners = m_listeners;

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
                        Utils.LogError(e, "Unexpected error closing a listener. {0}", listeners[ii].GetType().FullName);
                    }
                }

                listeners.Clear();
            }

            // close the hosts.
            lock (m_hosts)
            {
                foreach (ServiceHost host in m_hosts)
                {
                    if (host.State == ServiceHostState.Opened)
                    {
                        host.Abort();
                    }
                    host.Close();
                }
            }
        }

        /// <summary>
        /// Creates an instance of the service host.
        /// </summary>
        public virtual ServiceHost CreateServiceHost(ServerBase server, params Uri[] addresses)
        {
            return null;
        }
        #endregion

        #region BaseAddress Class
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
        #endregion

        #region Protected Properties
        /// <summary>
        /// Gets the list of base addresses supported by the server.
        /// </summary>
        protected IList<BaseAddress> BaseAddresses { get; set; }

        /// <summary>
        /// Gets the list of endpoints supported by the server.
        /// </summary>
        protected ReadOnlyList<EndpointDescription> Endpoints => m_endpoints;

        /// <summary>
        /// The object used to verify client certificates
        /// </summary>
        /// <value>The identifier for an X509 certificate.</value>
        public CertificateValidator CertificateValidator
        {
            get
            {
                return (CertificateValidator)m_certificateValidator;
            }

            private set
            {
                m_certificateValidator = value;
            }
        }

        /// <summary>
        /// The server's application instance certificate.
        /// </summary>
        /// <value>The instance X.509 certificate.</value>
        protected X509Certificate2 InstanceCertificate
        {
            get
            {
                return (X509Certificate2)m_instanceCertificate;
            }

            private set
            {
                m_instanceCertificate = value;
            }
        }

        /// <summary>
        /// Gets the instance certificate chain.
        /// </summary>
        protected X509Certificate2Collection InstanceCertificateChain
        {
            get
            {
                return m_instanceCertificateChain;
            }

            private set
            {
                m_instanceCertificateChain = value;
            }
        }

        /// <summary>
        /// The non-configurable properties for the server.
        /// </summary>
        /// <value>The properties of the current server instance.</value>
        protected ServerProperties ServerProperties
        {
            get
            {
                return (ServerProperties)m_serverProperties;
            }

            private set
            {
                m_serverProperties = value;
            }
        }

        /// <summary>
        /// The configuration for the server.
        /// </summary>
        /// <value>Object that stores the configurable configuration information for a UA application</value>
        protected ApplicationConfiguration Configuration
        {
            get
            {
                return (ApplicationConfiguration)m_configuration;
            }

            private set
            {
                m_configuration = value;
            }
        }

        /// <summary>
        /// The application description for the server.
        /// </summary>
        /// <value>Object that contains a description for the ApplicationDescription DataType.</value>
        protected ApplicationDescription ServerDescription
        {
            get
            {
                return (ApplicationDescription)m_serverDescription;
            }

            private set
            {
                m_serverDescription = value;
            }
        }

        /// <summary>
        /// Gets the list of service hosts used by the server instance.
        /// </summary>
        /// <value>The service hosts.</value>
        protected List<ServiceHost> ServiceHosts
        {
            get { return m_hosts; }
        }

        /// <summary>
        /// Gets or set the capabilities for the server.
        /// </summary>
        protected StringCollection ServerCapabilities { get; set; }

        /// <summary>
        /// Gets the list of transport listeners used by the server instance.
        /// </summary>
        /// <value>The transport listeners.</value>
        protected List<ITransportListener> TransportListeners => m_listeners;
        #endregion

        #region Protected Methods
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
        /// Called after the application certificate update.
        /// </summary>
        protected virtual void OnCertificateUpdate(object sender, CertificateUpdateEventArgs e)
        {
            // disconnect all sessions
            InstanceCertificateChain = null;
            InstanceCertificate = e.SecurityConfiguration.ApplicationCertificate.Certificate;
            if (Configuration.SecurityConfiguration.SendCertificateChain)
            {
                InstanceCertificateChain = new X509Certificate2Collection(InstanceCertificate);
                var issuers = new List<CertificateIdentifier>();
                var validationErrors = new Dictionary<X509Certificate2, ServiceResultException>();
                Configuration.CertificateValidator.GetIssuersNoExceptionsOnGetIssuer(InstanceCertificateChain, issuers, validationErrors).GetAwaiter().GetResult();
                foreach (var error in validationErrors)
                {
                    if (error.Value != null)
                    {
                        Utils.LogCertificate("OnCertificateUpdate: GetIssuers Validation Error: {0}", error.Key, error.Value.Result);
                    }
                }
                for (int i = 0; i < issuers.Count; i++)
                {
                    InstanceCertificateChain.Add(issuers[i].Certificate);
                }
            }

            foreach (var listener in TransportListeners)
            {
                listener.CertificateUpdate(e.CertificateValidator, InstanceCertificate, InstanceCertificateChain);
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
            ICertificateValidator certificateValidator
            )
        {
            // create the stack listener.
            try
            {
                TransportListenerSettings settings = new TransportListenerSettings();

                settings.Descriptions = endpoints;
                settings.Configuration = endpointConfiguration;
                settings.ServerCertificate = InstanceCertificate;
                settings.CertificateValidator = certificateValidator;
                settings.NamespaceUris = MessageContext.NamespaceUris;
                settings.Factory = MessageContext.Factory;

                listener.Open(
                   endpointUri,
                   settings,
                   GetEndpointInstance(this));

                TransportListeners.Add(listener);

                listener.ConnectionStatusChanged += OnConnectionStatusChanged;
            }
            catch (Exception e)
            {
                StringBuilder message = new StringBuilder();
                message.Append("Could not load ").Append(endpointUri.Scheme).Append(" Stack Listener.");
                if (e.InnerException != null)
                {
                    message.Append(' ')
                        .Append(e.InnerException.Message);
                }
                Utils.LogError(e, message.ToString());
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
        public virtual UserTokenPolicyCollection GetUserTokenPolicies(ApplicationConfiguration configuration, EndpointDescription description)
        {
            UserTokenPolicyCollection policies = new UserTokenPolicyCollection();

            if (configuration.ServerConfiguration == null || configuration.ServerConfiguration.UserTokenPolicies == null)
            {
                return policies;
            }

            foreach (UserTokenPolicy policy in configuration.ServerConfiguration.UserTokenPolicies)
            {
                UserTokenPolicy clone = (UserTokenPolicy)policy.MemberwiseClone();

                if (String.IsNullOrEmpty(policy.SecurityPolicyUri))
                {
                    if (description.SecurityMode == MessageSecurityMode.None)
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
            IPAddress address = null;

            if (IPAddress.TryParse(hostname, out address))
            {
                if (IPAddress.IsLoopback(address))
                {
                    return computerName.ToUpper(CultureInfo.InvariantCulture);
                }

                // substitute the computer name for any local IP if an IP is used by client.
                IPAddress[] addresses = Utils.GetHostAddresses(Utils.GetHostName());

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
            catch (System.Net.Sockets.SocketException e)
            {
                Utils.LogError(e, "Unable to check aliases for hostname {0}.", computerName);
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
        protected IList<BaseAddress> FilterByProfile(StringCollection profileUris, IList<BaseAddress> baseAddresses)
        {
            if (profileUris == null || profileUris.Count == 0)
            {
                return baseAddresses;
            }

            List<BaseAddress> filteredAddresses = new List<BaseAddress>();

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
        protected IList<BaseAddress> FilterByEndpointUrl(Uri endpointUrl, IList<BaseAddress> baseAddresses)
        {
            // client only gets alternate addresses that match the DNS name that it used.
            List<BaseAddress> accessibleAddresses = new List<BaseAddress>();
            foreach (BaseAddress baseAddress in baseAddresses)
            {
                if (baseAddress.Url.DnsSafeHost == endpointUrl.DnsSafeHost)
                {
                    accessibleAddresses.Add(baseAddress);
                    continue;
                }

                if (baseAddress.AlternateUrls != null)
                {
                    foreach (Uri alternateUrl in baseAddress.AlternateUrls)
                    {
                        if (alternateUrl.DnsSafeHost == endpointUrl.DnsSafeHost)
                        {
                            accessibleAddresses.Add(new BaseAddress() { Url = alternateUrl, ProfileUri = baseAddress.ProfileUri, DiscoveryUrl = alternateUrl });
                            break;
                        }
                    }
                }
            }

            if (accessibleAddresses.Count != 0)
            {
                return accessibleAddresses;
            }

            // client gets all of the endpoints if it using a known variant of the hostname.
            if (NormalizeHostname(endpointUrl.DnsSafeHost) == NormalizeHostname("localhost"))
            {
                return baseAddresses;
            }

            // no match on client DNS name. client gets only addresses that match the scheme.
            foreach (BaseAddress baseAddress in baseAddresses)
            {
                if (baseAddress.Url.Scheme == endpointUrl.Scheme)
                {
                    accessibleAddresses.Add(baseAddress);
                    continue;
                }
            }

            return accessibleAddresses;
        }

        /// <summary>
        /// Returns the best discovery URL for the base address based on the URL used by the client.
        /// </summary>
        private static string GetBestDiscoveryUrl(Uri clientUrl, BaseAddress baseAddress)
        {
            string url = baseAddress.Url.ToString();

            if ((baseAddress.ProfileUri == Profiles.HttpsBinaryTransport) && (!(url.EndsWith("discovery"))))
            {
                url += "/discovery";
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
            StringCollection discoveryUrls = new StringCollection();

            foreach (BaseAddress baseAddress in baseAddresses)
            {
                discoveryUrls.Add(GetBestDiscoveryUrl(clientUrl, baseAddress));
            }

            // copy the description.
            ApplicationDescription copy = new ApplicationDescription();

            copy.ApplicationName = description.ApplicationName;
            copy.ApplicationUri = description.ApplicationUri;
            copy.ApplicationType = description.ApplicationType;
            copy.ProductUri = description.ProductUri;
            copy.GatewayServerUri = description.DiscoveryProfileUri;
            copy.DiscoveryUrls = discoveryUrls;

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
            EndpointDescriptionCollection translations = new EndpointDescriptionCollection();

            // process endpoints
            foreach (EndpointDescription endpoint in endpoints)
            {
                UriBuilder endpointUrl = new UriBuilder(endpoint.EndpointUrl);

                // find matching base address.
                foreach (BaseAddress baseAddress in baseAddresses)
                {
                    bool translateHttpsEndpoint = false;
                    if (endpoint.TransportProfileUri == Profiles.HttpsBinaryTransport && baseAddress.ProfileUri == Profiles.HttpsBinaryTransport)
                    {
                        translateHttpsEndpoint = true;
                    }

                    if (endpoint.TransportProfileUri != baseAddress.ProfileUri && !translateHttpsEndpoint)
                    {
                        continue;
                    }

                    if (endpointUrl.Scheme != baseAddress.Url.Scheme)
                    {
                        continue;
                    }

                    if (endpointUrl.Port != baseAddress.Url.Port)
                    {
                        continue;
                    }

                    EndpointDescription translation = new EndpointDescription();

                    translation.EndpointUrl = baseAddress.Url.ToString();

                    if (endpointUrl.Path.StartsWith(baseAddress.Url.PathAndQuery, StringComparison.Ordinal) &&
                        endpointUrl.Path.Length > baseAddress.Url.PathAndQuery.Length)
                    {
                        string suffix = endpointUrl.Path.Substring(baseAddress.Url.PathAndQuery.Length);
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

                    translations.Add(translation);
                }
            }

            return translations;
        }

        /// <summary>
        /// Verifies that the request header is valid.
        /// </summary>
        /// <param name="requestHeader">The object that contains description for the RequestHeader DataType.</param>
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
        protected virtual ResponseHeader CreateResponse(RequestHeader requestHeader, uint statusCode)
        {
            if (StatusCode.IsBad(statusCode))
            {
                throw new ServiceResultException(statusCode);
            }

            ResponseHeader responseHeader = new ResponseHeader();

            responseHeader.Timestamp = DateTime.UtcNow;
            responseHeader.RequestHandle = requestHeader.RequestHandle;

            return responseHeader;
        }

        /// <summary>
        /// Creates the response header.
        /// </summary>
        /// <param name="requestHeader">The object that contains description for the RequestHeader DataType.</param>
        /// <param name="exception">The exception used to create DiagnosticInfo assigned to the ServiceDiagnostics.</param>
        /// <returns>Returns a description for the ResponseHeader DataType. </returns>
        protected virtual ResponseHeader CreateResponse(RequestHeader requestHeader, Exception exception)
        {
            ResponseHeader responseHeader = new ResponseHeader();

            responseHeader.Timestamp = DateTime.UtcNow;
            responseHeader.RequestHandle = requestHeader.RequestHandle;

            StringTable stringTable = new StringTable();
            responseHeader.ServiceDiagnostics = new DiagnosticInfo(exception, (DiagnosticsMasks)requestHeader.ReturnDiagnostics, true, stringTable);
            responseHeader.StringTable = stringTable.ToArray();

            return responseHeader;
        }

        /// <summary>
        /// Creates the response header.
        /// </summary>
        /// <param name="requestHeader">The object that contains description for the RequestHeader DataType.</param>
        /// <param name="stringTable">The thread safe table of string constants.</param>
        /// <returns>Returns a description for the ResponseHeader DataType. </returns>
        protected virtual ResponseHeader CreateResponse(RequestHeader requestHeader, StringTable stringTable)
        {
            ResponseHeader responseHeader = new ResponseHeader();

            responseHeader.Timestamp = DateTime.UtcNow;
            responseHeader.RequestHandle = requestHeader.RequestHandle;

            responseHeader.StringTable.AddRange(stringTable.ToArray());

            return responseHeader;
        }

        /// <summary>
        /// Called when the server configuration is changed on disk.
        /// </summary>
        /// <param name="configuration">The object that stores the configurable configuration information for a UA application.</param>
        /// <remarks>
        /// Servers are free to ignore changes if it is difficult/impossible to apply them without a restart.
        /// </remarks>
        protected virtual void OnUpdateConfiguration(ApplicationConfiguration configuration)
        {
        }

        /// <summary>
        /// Called before the server starts.
        /// </summary>
        /// <param name="configuration">The object that stores the configurable configuration information for a UA application.</param>
        protected virtual void OnServerStarting(ApplicationConfiguration configuration)
        {
            // fetch properties and configuration.
            Configuration = configuration;
            ServerProperties = LoadServerProperties();

            // ensure at least one security policy exists.
            if (configuration.ServerConfiguration != null)
            {
                if (configuration.ServerConfiguration.SecurityPolicies.Count == 0)
                {
                    configuration.ServerConfiguration.SecurityPolicies.Add(new ServerSecurityPolicy());
                }

                // ensure at least one user token policy exists.
                if (configuration.ServerConfiguration.UserTokenPolicies.Count == 0)
                {
                    UserTokenPolicy userTokenPolicy = new UserTokenPolicy();

                    userTokenPolicy.TokenType = UserTokenType.Anonymous;
                    userTokenPolicy.PolicyId = userTokenPolicy.TokenType.ToString();

                    configuration.ServerConfiguration.UserTokenPolicies.Add(userTokenPolicy);
                }
            }

            // load the instance certificate.
            if (configuration.SecurityConfiguration.ApplicationCertificate != null)
            {
                InstanceCertificate = configuration.SecurityConfiguration.ApplicationCertificate.Find(true).GetAwaiter().GetResult();
            }

            if (InstanceCertificate == null)
            {
                throw new ServiceResultException(
                    StatusCodes.BadConfigurationError,
                    "Server does not have an instance certificate assigned.");
            }

            if (!InstanceCertificate.HasPrivateKey)
            {
                throw new ServiceResultException(
                    StatusCodes.BadConfigurationError,
                    "Server does not have access to the private key for the instance certificate.");
            }

            // load certificate chain.
            InstanceCertificateChain = new X509Certificate2Collection(InstanceCertificate);
            var issuers = new List<CertificateIdentifier>();
            var validationErrors = new Dictionary<X509Certificate2, ServiceResultException>();
            configuration.CertificateValidator.GetIssuersNoExceptionsOnGetIssuer(InstanceCertificateChain, issuers, validationErrors).Wait();

            if (validationErrors.Count > 0)
            {
                Utils.LogWarning("Issuer validation errors ignored on startup:");
                // only list warning for errors to avoid that the server can not start
                foreach (var error in validationErrors)
                {
                    if (error.Value != null)
                    {
                        Utils.LogCertificate(LogLevel.Warning, "- " + error.Value.Message, error.Key);
                    }
                }
            }

            for (int i = 0; i < issuers.Count; i++)
            {
                InstanceCertificateChain.Add(issuers[i].Certificate);
            }

            // use the message context from the configuration to ensure the channels are using the same one.
            ServiceMessageContext messageContext = configuration.CreateMessageContext(true);
            messageContext.NamespaceUris = new NamespaceTable();
            MessageContext = messageContext;

            // assign a unique identifier if none specified.
            if (String.IsNullOrEmpty(configuration.ApplicationUri))
            {
                configuration.ApplicationUri = X509Utils.GetApplicationUriFromCertificate(InstanceCertificate);

                if (String.IsNullOrEmpty(configuration.ApplicationUri))
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
            if (String.IsNullOrEmpty(configuration.ApplicationName) && InstanceCertificate != null)
            {
                configuration.ApplicationName = InstanceCertificate.GetNameInfo(X509NameType.DnsName, false);
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
            TransportListenerBindings bindingFactory,
            out ApplicationDescription serverDescription,
            out EndpointDescriptionCollection endpoints)
        {
            serverDescription = null;
            endpoints = null;
            return new List<ServiceHost>();
        }

        /// <summary>
        /// Starts the server application.
        /// </summary>
        /// <param name="configuration">The object that stores the configurable configuration information for a UA application.</param>
        protected virtual void StartApplication(ApplicationConfiguration configuration)
        {
            // must be defined by the subclass.
        }

        /// <summary>
        /// Called before the server stops
        /// </summary>
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
        /// <param name="calldata">The calldata passed with the request.</param>
        protected virtual void ProcessRequest(IEndpointIncomingRequest request, object calldata)
        {
            request.CallSynchronously();
        }
        #endregion

        #region RequestQueue Class
        /// <summary>
        /// Manages a queue of requests.
        /// </summary>
        protected class RequestQueue : IDisposable
        {
            #region Constructors
            /// <summary>
            /// Initializes a new instance of the <see cref="RequestQueue"/> class.
            /// </summary>
            /// <param name="server">The server.</param>
            /// <param name="minThreadCount">The minimum number of threads in the pool.</param>
            /// <param name="maxThreadCount">The maximum number of threads  in the pool.</param>
            /// <param name="maxRequestCount">The maximum number of requests that will placed in the queue.</param>
            public RequestQueue(ServerBase server, int minThreadCount, int maxThreadCount, int maxRequestCount)
            {
                m_server = server;
                m_stopped = false;
                m_minThreadCount = minThreadCount;
                m_maxThreadCount = maxThreadCount;
                m_maxRequestCount = maxRequestCount;
                m_activeThreadCount = 0;

#if THREAD_SCHEDULER
                m_queue = new Queue<IEndpointIncomingRequest>(maxRequestCount);
                m_totalThreadCount = 0;
#endif

                // adjust ThreadPool, only increase values if necessary
                int minCompletionPortThreads;
                ThreadPool.GetMinThreads(out minThreadCount, out minCompletionPortThreads);
                ThreadPool.SetMinThreads(
                    Math.Max(minThreadCount, m_minThreadCount),
                    Math.Max(minCompletionPortThreads, m_minThreadCount));
                int maxCompletionPortThreads;
                ThreadPool.GetMaxThreads(out maxThreadCount, out maxCompletionPortThreads);
                ThreadPool.SetMaxThreads(
                    Math.Max(maxThreadCount, m_maxThreadCount),
                    Math.Max(maxCompletionPortThreads, m_maxThreadCount));
            }
            #endregion

            #region IDisposable Members
            /// <summary>
            /// Frees any unmanaged resources.
            /// </summary>
            public void Dispose()
            {
                Dispose(true);
            }

            /// <summary>
            /// An overrideable version of the Dispose.
            /// </summary>
            protected virtual void Dispose(bool disposing)
            {
                if (disposing)
                {
#if THREAD_SCHEDULER
                    lock (m_lock)
                    {
                        m_stopped = true;

                        Monitor.PulseAll(m_lock);

                        m_queue.Clear();
                    }
#else
                    m_stopped = true;
#endif
                }
            }
            #endregion

            #region Public Members
            /// <summary>
            /// Schedules an incoming request.
            /// </summary>
            /// <param name="request">The request.</param>
            public void ScheduleIncomingRequest(IEndpointIncomingRequest request)
            {
#if THREAD_SCHEDULER
                int totalThreadCount;
                int activeThreadCount;

                // Queue the request. Call logger only outside lock.
                Monitor.Enter(m_lock);
                bool monitorExit = true;
                try
                {
                    // check able to schedule requests.
                    if (m_stopped || m_queue.Count >= m_maxRequestCount)
                    {
                        // too many operations
                        totalThreadCount = m_totalThreadCount;
                        activeThreadCount = m_activeThreadCount;
                        monitorExit = false;
                        Monitor.Exit(m_lock);

                        request.OperationCompleted(null, StatusCodes.BadTooManyOperations);

                        Utils.LogTrace("Too many operations. Total: {0} Active: {1}",
                            totalThreadCount, activeThreadCount);
                    }
                    else
                    {
                        m_queue.Enqueue(request);

                        // wake up an idle thread to handle the request if there is one
                        if (m_activeThreadCount < m_totalThreadCount)
                        {
                            Monitor.Pulse(m_lock);
                        }
                        // start a new thread to handle the request if none are idle and the pool is not full.
                        else if (m_totalThreadCount < m_maxThreadCount)
                        {
                            totalThreadCount = ++m_totalThreadCount;
                            activeThreadCount = ++m_activeThreadCount;
                            monitorExit = false;
                            Monitor.Exit(m_lock);

                            // new threads start in an active state
                            Thread thread = new Thread(OnProcessRequestQueue);
                            thread.IsBackground = true;
                            thread.Start(null);

                            Utils.LogTrace("Thread created: {0:X8}. Total: {1} Active: {2}",
                                thread.ManagedThreadId, totalThreadCount, activeThreadCount);

                            return;
                        }
                    }
                }
                finally
                {
                    if (monitorExit)
                    {
                        Monitor.Exit(m_lock);
                    }
                }
#else
                if (m_stopped)
                {
                    request.OperationCompleted(null, StatusCodes.BadTooManyOperations);
                    return;
                }

                int activeThreadCount = Interlocked.Increment(ref m_activeThreadCount);
                if (activeThreadCount >= m_maxRequestCount)
                {
                    Interlocked.Decrement(ref m_activeThreadCount);
                    request.OperationCompleted(null, StatusCodes.BadTooManyOperations);
                    Utils.LogWarning("Too many operations. Active thread count: {0}", m_activeThreadCount);
                    return;
                }

                Task.Run(() => {
                    try
                    {
                        m_server.ProcessRequest(request, null);
                    }
                    finally
                    {
                        Interlocked.Decrement(ref m_activeThreadCount);
                    }
                });
#endif
            }
            #endregion

            #region Private Methods
#if THREAD_SCHEDULER
            /// <summary>
            /// Processes the requests in the request queue.
            /// </summary>
            private void OnProcessRequestQueue(object state)
            {
                lock (m_lock)   // i.e. Monitor.Enter(m_lock)
                {
                    while (true)
                    {
                        // check if the queue is empty.
                        while (m_queue.Count == 0)
                        {
                            m_activeThreadCount--;

                            // wait for a request. end the thread if no activity.
                            if (m_stopped || (!Monitor.Wait(m_lock, 15_000) && m_totalThreadCount > m_minThreadCount))
                            {
                                m_totalThreadCount--;
                                Utils.LogTrace("Thread ended: {0:X8}. Total: {1} Active: {2}",
                                    Environment.CurrentManagedThreadId, m_totalThreadCount, m_activeThreadCount);
                                return;
                            }

                            m_activeThreadCount++;
                        }

                        IEndpointIncomingRequest request = m_queue.Dequeue();

                        Monitor.Exit(m_lock);

                        try
                        {
                            // process the request.
                            m_server.ProcessRequest(request, state);
                        }
                        catch (Exception e)
                        {
                            Utils.LogError(e, "Unexpected error processing incoming request.");
                        }
                        finally
                        {
                            Monitor.Enter(m_lock);
                        }
                    }
                }
            }
#endif
            #endregion

            #region Private Fields
            private ServerBase m_server;
            private bool m_stopped;
            private int m_activeThreadCount;
            private int m_maxThreadCount;
            private int m_minThreadCount;
            private int m_maxRequestCount;
#if THREAD_SCHEDULER
            private object m_lock = new object();
            private Queue<IEndpointIncomingRequest> m_queue;
            private int m_totalThreadCount;
#endif
            #endregion

        }
        #endregion

        #region Private Fields
        private object m_messageContext;
        private object m_serverError;
        private object m_certificateValidator;
        private object m_instanceCertificate;
        private X509Certificate2Collection m_instanceCertificateChain;
        private object m_serverProperties;
        private object m_configuration;
        private object m_serverDescription;
        private List<ServiceHost> m_hosts;
        private List<ITransportListener> m_listeners;
        private ReadOnlyList<EndpointDescription> m_endpoints;
        private RequestQueue m_requestQueue;
        // identifier for the UserTokenPolicy should be unique within the context of a single Server
        private int m_userTokenPolicyId = 0;
        #endregion
    }
}
