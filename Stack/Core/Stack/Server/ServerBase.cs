/* ========================================================================
 * Copyright (c) 2005-2013 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Reciprocal Community License ("RCL") Version 1.00
 * 
 * Unless explicitly acquired and licensed from Licensor under another 
 * license, the contents of this file are subject to the Reciprocal 
 * Community License ("RCL") Version 1.00, or subsequent versions 
 * as allowed by the RCL, and You may not copy or use this file in either 
 * source code or executable form, except in compliance with the terms and 
 * conditions of the RCL.
 * 
 * All software distributed under the RCL is provided strictly on an 
 * "AS IS" basis, WITHOUT WARRANTY OF ANY KIND, EITHER EXPRESS OR IMPLIED, 
 * AND LICENSOR HEREBY DISCLAIMS ALL SUCH WARRANTIES, INCLUDING WITHOUT 
 * LIMITATION, ANY WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR 
 * PURPOSE, QUIET ENJOYMENT, OR NON-INFRINGEMENT. See the RCL for specific 
 * language governing rights and limitations under the RCL.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/RCL/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using Windows.ApplicationModel.Background;
using System.Net;
using System.Threading.Tasks;

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
            m_hosts = new List<IBackgroundTask>();
            m_listeners = new List<ITransportListener>();
            m_endpoints = null;
            m_requestQueue = new RequestQueue(this, 10, 100, 1000);
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
        public ServiceMessageContext MessageContext 
        { 
            get 
            { 
                return (ServiceMessageContext)m_messageContext; 
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
        #endregion

        #region Public Methods
        /// <summary>
        /// Starts the server (called from a IIS host process).
        /// </summary>
        /// <param name="configuration">The object that stores the configurable configuration information 
        /// for a UA application</param>
        /// <param name="baseAddresses">The array of Uri elements which contains base addresses.</param>
        /// <returns>Returns a host for a UA service.</returns>
        public IBackgroundTask Start(ApplicationConfiguration configuration, params Uri[] baseAddresses)
        {
            if (configuration == null) throw new ArgumentNullException("configuration");

            // do any pre-startup processing
            OnServerStarting(configuration);

            // intialize the request queue from the configuration.
            InitializeRequestQueue(configuration);

            // create the binding factory.
            BindingFactory bindingFactory = BindingFactory.Create(configuration, MessageContext);

            // initialize the base addresses.
            InitializeBaseAddresses(configuration);

            // initialize the hosts.
            ApplicationDescription serverDescription = null;
            EndpointDescriptionCollection endpoints = null;

            IList<IBackgroundTask> hosts = InitializeServiceHosts(
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
            if (configuration == null) throw new ArgumentNullException("configuration");

            // do any pre-startup processing
            OnServerStarting(configuration);

            // intialize the request queue from the configuration.
            InitializeRequestQueue(configuration);

            // create the binding factory.
            BindingFactory bindingFactory = BindingFactory.Create(configuration, MessageContext);

            // initialize the base addresses.
            InitializeBaseAddresses(configuration);

            // initialize the hosts.
            ApplicationDescription serverDescription = null;
            EndpointDescriptionCollection endpoints = null;

            IList<IBackgroundTask> hosts = InitializeServiceHosts(
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
                foreach (IBackgroundTask serviceHost in hosts)
                {
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
                    case Utils.UriSchemeHttp:
                    case Utils.UriSchemeNetTcp:
                    case Utils.UriSchemeNetPipe:
                    {
                        address.ProfileUri = Profiles.WsHttpXmlOrBinaryTransport;
                        address.DiscoveryUrl = new Uri(address.Url.ToString() + "/discovery");
                        break;
                    }

                    case Utils.UriSchemeHttps:
                    {
                        address.ProfileUri = Profiles.HttpsXmlOrBinaryTransport;
                        address.DiscoveryUrl = address.Url;
                        break;
                    }

                    case Utils.UriSchemeNoSecurityHttp:
                    {
                        UriBuilder builder = new UriBuilder(address.Url);
                        builder.Scheme = Utils.UriSchemeHttp;
                        address.Url = builder.Uri;

                        if (address.AlternateUrls != null)
                        {
                            for (int ii = 0; ii < address.AlternateUrls.Count; ii++)
                            {
                                builder = new UriBuilder(address.AlternateUrls[ii]);
                                builder.Scheme = Utils.UriSchemeHttp;
                                address.AlternateUrls[ii] = builder.Uri;
                            }
                        }

                        address.ProfileUri = Profiles.HttpsXmlOrBinaryTransport;
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

                        switch (baseAddress.ProfileUri)
                        {
                            case Profiles.WsHttpXmlOrBinaryTransport:
                            case Profiles.WsHttpXmlTransport:
                            {
                                builder.Path += "/discovery";
                                break;
                            }
                        }

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
                        Utils.Trace(e, "Unexpected error closing a listener. {0}", listeners[ii].GetType().FullName);
                    }
                }

                listeners.Clear();
            }

            // close the hosts.
            lock (m_hosts)
            {
                m_hosts.Clear();
            }
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
        protected ReadOnlyList<EndpointDescription> Endpoints
        {
            get { return m_endpoints; }
        }

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
        /// Gets the list of WCF service hosts used by the server instance.
        /// </summary>
        /// <value>The WCF service hosts.</value>
        protected List<IBackgroundTask> ServiceHosts
        {
            get { return m_hosts; }
        }

        /// <summary>
        /// Gets the list of transport listeners used by the server instance.
        /// </summary>
        /// <value>The transport listeners.</value>
        protected List<ITransportListener> TransportListeners
        {
            get { return m_listeners; }
        }
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
        /// Create a new service host for protocols that support only one policy per host.
        /// </summary>
        /// <param name="hosts">The hosts.</param>
        /// <param name="configuration">The configuration.</param>
        /// <param name="bindingFactory">The binding factory.</param>
        /// <param name="baseAddresses">The base addresses.</param>
        /// <param name="serverDescription">The server description.</param>
        /// <param name="securityMode">The security mode.</param>
        /// <param name="securityPolicyUri">The security policy URI.</param>
        /// <param name="basePath">The base path to use when constructing the hosts.</param>
        /// <returns>Returns list of descriptions for the EndpointDescription DataType, return type is list of <seealso cref="EndpointDescription"/>.</returns>
        protected List<EndpointDescription> CreateSinglePolicyServiceHost(
            IDictionary<string, IBackgroundTask> hosts,
            ApplicationConfiguration configuration,
            BindingFactory bindingFactory,
            IList<string> baseAddresses,
            ApplicationDescription serverDescription,
            MessageSecurityMode securityMode,
            string securityPolicyUri,
            string basePath)
        {
            // generate a unique host name.
            string hostName = basePath;

            if (hosts.ContainsKey(hostName))
            {
                hostName += Utils.Format("/{0}", SecurityPolicies.GetDisplayName(securityPolicyUri));
            }

            if (hosts.ContainsKey(hostName))
            {
                hostName += Utils.Format("/{0}", securityMode);
            }

            if (hosts.ContainsKey(hostName))
            {
                hostName += Utils.Format("/{0}", hosts.Count);
            }

            // build list of uris.
            List<Uri> uris = new List<Uri>();
            List<EndpointDescription> endpoints = new List<EndpointDescription>();
            string computerName = Utils.GetHostName();

            for (int ii = 0; ii < baseAddresses.Count; ii++)
            {
                // UA TCP and HTTPS endpoints have their own host.
                if (baseAddresses[ii].StartsWith(Utils.UriSchemeOpcTcp, StringComparison.Ordinal) ||
                    baseAddresses[ii].StartsWith(Utils.UriSchemeHttps, StringComparison.Ordinal)  ||
                    baseAddresses[ii].StartsWith(Utils.UriSchemeNoSecurityHttp, StringComparison.Ordinal))
                {
                    continue;
                }

                UriBuilder uri = new UriBuilder(baseAddresses[ii]);

                if (String.Compare(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase) == 0)
                {
                    uri.Host = computerName;
                }

                uri.Path += hostName;
                uris.Add(uri.Uri);

                // create the endpoint description.
                EndpointDescription description = new EndpointDescription();

                description.EndpointUrl = uri.ToString();
                description.Server = serverDescription;
                
                description.SecurityMode = securityMode;
                description.SecurityPolicyUri = securityPolicyUri;
                description.TransportProfileUri = Profiles.WsHttpXmlTransport; 
                description.UserIdentityTokens = GetUserTokenPolicies(configuration, description);

                bool requireEncryption = RequireEncryption(description);

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

                if (requireEncryption)
                {
                    if (InstanceCertificate == null)
                    {
                        throw new ServiceResultException( StatusCodes.BadConfigurationError,
                            "Server does not have an instance certificate assigned." );
                    }

                    description.ServerCertificate = InstanceCertificate.RawData;
                }

                endpoints.Add(description);
            }

            // check if nothing to do.
            if (uris.Count == 0)
            {
                return endpoints;
            }

            // create the endpoint configuration to use.
            EndpointConfiguration endpointConfiguration = EndpointConfiguration.Create(configuration);

            return endpoints;
        }

        /// <summary>
        /// Specifies if the server requires encryption; if so the server needs to send its certificate to the clients and validate the client certificates
        /// </summary>
        /// <param name="description">The description.</param>
        /// <returns></returns>
        public static bool RequireEncryption(EndpointDescription description)
        {
            bool requireEncryption = description.SecurityPolicyUri != SecurityPolicies.None;

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
            return requireEncryption;
        }

        /// <summary>
        /// Create a new service host for UA TCP.
        /// </summary>
        protected List<EndpointDescription> CreateUaTcpServiceHost(
            IDictionary<string, IBackgroundTask> hosts,
            ApplicationConfiguration configuration,
            BindingFactory bindingFactory,
            IList<string> baseAddresses,
            ApplicationDescription serverDescription,
            List<ServerSecurityPolicy> securityPolicies)
        {
            // generate a unique host name.
            string hostName = String.Empty;

            if (hosts.ContainsKey(hostName))
            {
                hostName = "/Tcp";
            }

            if (hosts.ContainsKey(hostName))
            {
                hostName += Utils.Format("/{0}", hosts.Count);
            }

            // check if the server if configured to use the ANSI C stack.
            bool useAnsiCStack = configuration.UseNativeStack;

            // build list of uris.
            List<Uri> uris = new List<Uri>();
            EndpointDescriptionCollection endpoints = new EndpointDescriptionCollection();

            // create the endpoint configuration to use.
            EndpointConfiguration endpointConfiguration = EndpointConfiguration.Create(configuration);
            string computerName = Utils.GetHostName();

            for (int ii = 0; ii < baseAddresses.Count; ii++)
            {
                // UA TCP and HTTPS endpoints support multiple policies.
                if (!baseAddresses[ii].StartsWith(Utils.UriSchemeOpcTcp, StringComparison.Ordinal))
                {
                    continue;
                }

                UriBuilder uri = new UriBuilder(baseAddresses[ii]);

                if (String.Compare(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase) == 0)
                {
                    uri.Host = computerName;
                }

                uris.Add(uri.Uri);

                foreach (ServerSecurityPolicy policy in securityPolicies)
                {
                    // create the endpoint description.
                    EndpointDescription description = new EndpointDescription();

                    description.EndpointUrl = uri.ToString();
                    description.Server = serverDescription;

                    description.SecurityMode = policy.SecurityMode;
                    description.SecurityPolicyUri = policy.SecurityPolicyUri;
                    description.SecurityLevel = policy.SecurityLevel;
                    description.UserIdentityTokens = GetUserTokenPolicies( configuration, description );
                    description.TransportProfileUri = Profiles.UaTcpTransport;

                    bool requireEncryption = RequireEncryption(description);

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

                    if (requireEncryption)
                    {
                        description.ServerCertificate = InstanceCertificate.RawData;
                    }

                    endpoints.Add( description );
                }

                // create the UA-TCP stack listener.
                try
                {
                    TransportListenerSettings settings = new TransportListenerSettings();

                    settings.Descriptions = endpoints;
                    settings.Configuration = endpointConfiguration;
                    settings.ServerCertificate = this.InstanceCertificate;
                    settings.CertificateValidator = configuration.CertificateValidator.GetChannelValidator();
                    settings.NamespaceUris = this.MessageContext.NamespaceUris;
                    settings.Factory = this.MessageContext.Factory;

                    ITransportListener listener = null;

                    Type type = null;

                    if (useAnsiCStack)
                    {
                        type = Type.GetType("Opc.Ua.NativeStack.NativeStackListener,Opc.Ua.NativeStackWrapper");
                    }

                    if (useAnsiCStack && type != null)
                    {
                        listener = (ITransportListener)Activator.CreateInstance(type);
                    }
                    else
                    {
                        listener = new Opc.Ua.Bindings.UaTcpChannelListener();
                    }

                    listener.Open(
                       uri.Uri,
                       settings,
                       GetEndpointInstance(this));

                    TransportListeners.Add(listener);
                }
                catch (Exception e)
                {
                    Utils.Trace(e, "Could not load UA-TCP Stack Listener.");
					throw;
                }
            }

            return endpoints;
        }

        /// <summary>
        /// Create a new service host for UA HTTPS.
        /// </summary>
        protected List<EndpointDescription> CreateHttpsServiceHost(
            IDictionary<string, IBackgroundTask> hosts,
            ApplicationConfiguration configuration,
            BindingFactory bindingFactory,
            IList<string> baseAddresses,
            ApplicationDescription serverDescription,
            List<ServerSecurityPolicy> securityPolicies)
        {
            // generate a unique host name.
            string hostName = String.Empty;

            if (hosts.ContainsKey(hostName))
            {
                hostName = "/Https";
            }

            if (hosts.ContainsKey(hostName))
            {
                hostName += Utils.Format("/{0}", hosts.Count);
            }

            // build list of uris.
            List<Uri> uris = new List<Uri>();
            EndpointDescriptionCollection endpoints = new EndpointDescriptionCollection();

            // create the endpoint configuration to use.
            EndpointConfiguration endpointConfiguration = EndpointConfiguration.Create(configuration);
            string computerName = Utils.GetHostName();

            for (int ii = 0; ii < baseAddresses.Count; ii++)
            {
                if (!baseAddresses[ii].StartsWith(Utils.UriSchemeHttps, StringComparison.Ordinal) &&
                    !baseAddresses[ii].StartsWith(Utils.UriSchemeNoSecurityHttp, StringComparison.Ordinal))
                {
                    continue;
                }

                UriBuilder uri = new UriBuilder(baseAddresses[ii]);

                if (uri.Scheme == Utils.UriSchemeNoSecurityHttp)
                {
                    uri.Scheme = Utils.UriSchemeHttp;
                }

                if (uri.Path[uri.Path.Length-1] != '/')
                {
                    uri.Path += "/";
                }

                if (String.Compare(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase) == 0)
                {
                    uri.Host = computerName;
                }

                uris.Add(uri.Uri);

                if (uri.Scheme == Utils.UriSchemeHttps)
                {
                    // can only support one policy with HTTPS so pick the best one.
                    ServerSecurityPolicy bestPolicy = null;

                    foreach (ServerSecurityPolicy policy in securityPolicies)
                    {
                        if (bestPolicy == null)
                        {
                            bestPolicy = policy;
                            continue;
                        }

                        if (bestPolicy.SecurityLevel > policy.SecurityLevel)
                        {
                            bestPolicy = policy;
                            continue;
                        }
                    }
                
                    EndpointDescription description = new EndpointDescription();

                    description.EndpointUrl = uri.ToString();
                    description.Server = serverDescription;

                    if (InstanceCertificate != null)
                    {
                        description.ServerCertificate = InstanceCertificate.RawData;
                    }

                    description.SecurityMode = bestPolicy.SecurityMode;
                    description.SecurityPolicyUri = bestPolicy.SecurityPolicyUri;
                    description.SecurityLevel = bestPolicy.SecurityLevel;
                    description.UserIdentityTokens = GetUserTokenPolicies(configuration, description);
                    description.TransportProfileUri = Profiles.HttpsBinaryTransport;

                    endpoints.Add(description);

                    // create the endpoint description.
                    description = new EndpointDescription();

                    description.EndpointUrl = uri.ToString();
                    description.Server = serverDescription;

                    if (InstanceCertificate != null)
                    {
                        description.ServerCertificate = InstanceCertificate.RawData;
                    }

                    description.SecurityMode = MessageSecurityMode.None;
                    description.SecurityPolicyUri = SecurityPolicies.None;
                    description.SecurityLevel = 0;
                    description.UserIdentityTokens = GetUserTokenPolicies(configuration, description);
                    description.TransportProfileUri = Profiles.HttpsXmlTransport;

                    endpoints.Add(description);
                }

                // create the stack listener.
                try
                {
                    TransportListenerSettings settings = new TransportListenerSettings();

                    settings.Descriptions = endpoints;
                    settings.Configuration = endpointConfiguration;
                    settings.ServerCertificate = this.InstanceCertificate;
                    settings.CertificateValidator = configuration.CertificateValidator.GetChannelValidator();
                    settings.NamespaceUris = this.MessageContext.NamespaceUris;
                    settings.Factory = this.MessageContext.Factory;

                    ITransportListener listener = new Opc.Ua.Bindings.UaTcpChannelListener();

                    listener.Open(
                       uri.Uri,
                       settings,
                       GetEndpointInstance(this));

                    TransportListeners.Add(listener);
                }
                catch (Exception e)
                {
                    Utils.Trace(e, "Could not load HTTPS Stack Listener.");
					throw;
                }
            }

            return endpoints;
        }

        /// <summary>
        /// Returns the UserTokenPolicies supported by the server.
        /// </summary>
        /// <param name="configuration">The configuration.</param>
        /// <param name="description">The description.</param>
        /// <returns>Returns a collection of UserTokenPolicy objects, the return type is <seealso cref="UserTokenPolicyCollection"/> . </returns>
        protected virtual UserTokenPolicyCollection GetUserTokenPolicies(ApplicationConfiguration configuration, EndpointDescription description)
        {
            UserTokenPolicyCollection policies = new UserTokenPolicyCollection();

            if (configuration.ServerConfiguration == null || configuration.ServerConfiguration.UserTokenPolicies == null)
            {
                return policies;
            }

            foreach (UserTokenPolicy policy in configuration.ServerConfiguration.UserTokenPolicies)
            {
                // ensure a security policy is specified for user tokens.
                if (description.SecurityMode == MessageSecurityMode.None)
                {
                    if (String.IsNullOrEmpty(policy.SecurityPolicyUri))
                    {
                        UserTokenPolicy clone = (UserTokenPolicy)policy.MemberwiseClone();
                        clone.SecurityPolicyUri = SecurityPolicies.Basic256;
                        policies.Add(clone);
                        continue;
                    }
                }

                policies.Add(policy);
            }

            // ensure each policy has a unique id.
            for (int ii = 0; ii < policies.Count; ii++)
            {
                if (String.IsNullOrEmpty(policies[ii].PolicyId))
                {
                    policies[ii].PolicyId = Utils.Format("{0}", ii);
                }
            }

            return policies;
        }

        /// <summary>
        /// Checks for IP address or well known hostnames that map to the computer.
        /// </summary>
        /// <param name="hostname">The hostname.</param>
        /// <returns>The hostname to use for URL filtering.</returns>
        protected async Task<string> NormalizeHostname(string hostname)
        {
            string computerName = Utils.GetHostName();

            // substitute the computer name for localhost if localhost used by client.
            if (Utils.AreDomainsEqual(hostname, "localhost"))
            {
                return computerName.ToUpper();
            }


            // check if client is using an ip address.
            IPAddress address = null;

            if (System.Net.IPAddress.TryParse(hostname, out address))
            {
                if (System.Net.IPAddress.IsLoopback(address))
                {
                    return computerName.ToUpper();
                }

                // substitute the computer name for any local IP if an IP is used by client.
                IPAddress[] addresses = await Utils.GetHostAddresses(Utils.GetHostName());

                for (int ii = 0; ii < addresses.Length; ii++)
                {
                    if (addresses[ii].Equals(address))
                    {
                        return computerName.ToUpper();
                    }
                }

                // not a localhost IP address.
                return hostname.ToUpper();
            }

            // return normalized hostname.
            return hostname.ToUpper();
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
            // client gets all of the endpoints if it using a known variant of the hostname.
            if (NormalizeHostname(endpointUrl.DnsSafeHost) == NormalizeHostname("localhost"))
            {
                return baseAddresses;
            }

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
                            accessibleAddresses.Add(baseAddress);
                            break;
                        }
                    }
                }
            }

            // no match on client DNS name. client gets only addresses that match the scheme.
            if (accessibleAddresses.Count == 0)
            {
                foreach (BaseAddress baseAddress in baseAddresses)
                {
                    if (baseAddress.Url.Scheme == endpointUrl.Scheme)
                    {
                        accessibleAddresses.Add(baseAddress);
                        continue;
                    }
                }
            }

            return accessibleAddresses;
        }

        /// <summary>
        /// Returns the best discovery URL for the base address based on the URL used by the client.
        /// </summary>
        private string GetBestDiscoveryUrl(Uri clientUrl, BaseAddress baseAddress)
        {
            string url = baseAddress.Url.ToString();

            if (baseAddress.ProfileUri == Profiles.WsHttpXmlOrBinaryTransport || baseAddress.ProfileUri == Profiles.WsHttpXmlTransport)
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
					if ((endpoint.TransportProfileUri == Profiles.HttpsBinaryTransport && baseAddress.ProfileUri == Profiles.HttpsXmlOrBinaryTransport)
						|| endpoint.TransportProfileUri == Profiles.HttpsBinaryTransport && baseAddress.ProfileUri == Profiles.HttpsBinaryTransport)
					{
						translateHttpsEndpoint = true;
					}
					if ((endpoint.TransportProfileUri == Profiles.HttpsXmlTransport && baseAddress.ProfileUri == Profiles.HttpsXmlOrBinaryTransport)
						|| endpoint.TransportProfileUri == Profiles.HttpsXmlTransport && baseAddress.ProfileUri == Profiles.HttpsXmlTransport)
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
                            
                    EndpointDescription translation = new EndpointDescription();

                    translation.EndpointUrl = baseAddress.Url.ToString();

                    if (endpointUrl.Path.StartsWith(baseAddress.Url.PathAndQuery) && endpointUrl.Path.Length > baseAddress.Url.PathAndQuery.Length)
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

            responseHeader.Timestamp     = DateTime.UtcNow;
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

            responseHeader.Timestamp     = DateTime.UtcNow;
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

            responseHeader.Timestamp     = DateTime.UtcNow;
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
                Task t = Task.Run(async () =>
                {
                    InstanceCertificate = await configuration.SecurityConfiguration.ApplicationCertificate.Find(true);
                });
                t.Wait();
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

            // use the message context from the configuration to ensure the channels are using the same one.
            MessageContext = configuration.CreateMessageContext();

            // assign a unique identifier if none specified.
            if (String.IsNullOrEmpty(configuration.ApplicationUri))
            {
                configuration.ApplicationUri = Utils.GetApplicationUriFromCertficate(InstanceCertificate);

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
            MessageContext.NamespaceUris = new NamespaceTable();
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
        /// <param name="bindingFactory">The object of a class that manages a mapping between a URL scheme and a binding.</param>
        /// <param name="serverDescription">The object of the class that contains a description for the ApplicationDescription DataType.</param>
        /// <param name="endpoints">The collection of <see cref="EndpointDescription"/> objects.</param>
        /// <returns>Returns list of hosts for a UA service.</returns>
        protected virtual IList<IBackgroundTask> InitializeServiceHosts(
            ApplicationConfiguration          configuration, 
            BindingFactory                    bindingFactory,
            out ApplicationDescription        serverDescription,
            out EndpointDescriptionCollection endpoints)            
        {
            serverDescription = null;
            endpoints = null;
            return new List<IBackgroundTask>();
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
        protected virtual void ProcessRequest(IEndpointIncomingRequest request)
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
                    m_stopped = true;
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
                if (m_stopped)
                {
                    request.OperationCompleted(null, StatusCodes.BadTooManyOperations);
                }
                else
                {
                    Task.Run(() =>
                    {
                        m_server.ProcessRequest(request);
                    });
                }
            }
#endregion

#region Private Fields
            private ServerBase m_server;
            private bool m_stopped;
#endregion

        }

#endregion

#region Private Fields
        private object m_messageContext;
        private object m_serverError;
        private object m_certificateValidator;
        private object m_instanceCertificate;
        private object m_serverProperties;
        private object m_configuration;
        private object m_serverDescription;
        private List<IBackgroundTask> m_hosts;
        private List<ITransportListener> m_listeners;
        private ReadOnlyList<EndpointDescription> m_endpoints;
        private RequestQueue m_requestQueue;
#endregion
    }
}
