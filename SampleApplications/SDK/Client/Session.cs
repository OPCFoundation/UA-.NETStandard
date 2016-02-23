/* ========================================================================
 * Copyright (c) 2005-2013 The OPC Foundation, Inc. All rights reserved.
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
using System.Text;
using System.Threading;
using System.ServiceModel;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Globalization;
using System.IO;
using System.Xml;
using System.Threading.Tasks;
using Windows.Security.Cryptography;
using Windows.Storage.Streams;
using System.Reflection;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Manages a session with a server.
    /// </summary>
    public class Session : SessionClient, IDisposable
    {
        #region Constructors
        /// <summary>
        /// Constructs a new instance of the session.
        /// </summary>
        /// <param name="channel">The channel used to communicate with the server.</param>
        /// <param name="configuration">The configuration for the client application.</param>
        /// <param name="endpoint">The endpoint use to initialize the channel.</param>
        public Session(
            ISessionChannel channel,
            ApplicationConfiguration configuration,
            ConfiguredEndpoint endpoint)
        :
            this(channel as ITransportChannel, configuration, endpoint, null)
        {
        }

        /// <summary>
        /// Constructs a new instance of the session.
        /// </summary>
        /// <param name="channel">The channel used to communicate with the server.</param>
        /// <param name="configuration">The configuration for the client application.</param>
        /// <param name="endpoint">The endpoint use to initialize the channel.</param>
        /// <param name="clientCertificate">The certificate to use for the client.</param>
        /// <remarks>
        /// The application configuration is used to look up the certificate if none is provided.
        /// The clientCertificate must have the private key. This will require that the certificate
        /// be loaded from a certicate store. Converting a DER encoded blob to a X509Certificate2
        /// will not include a private key.
        /// </remarks>
        public Session(
            ITransportChannel channel,
            ApplicationConfiguration configuration,
            ConfiguredEndpoint endpoint,
            X509Certificate2 clientCertificate)
        :
            base(channel)
        {
            Initialize(channel, configuration, endpoint, clientCertificate);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Session"/> class.
        /// </summary>
        /// <param name="channel">The channel.</param>
        /// <param name="template">The template session.</param>
        /// <param name="copyEventHandlers">if set to <c>true</c> the event handlers are copied.</param>
        public Session(ITransportChannel channel, Session template, bool copyEventHandlers)
        :
            base(channel)
        {
            Initialize(channel, template.m_configuration, template.m_endpoint, template.m_instanceCertificate);

            m_defaultSubscription = template.m_defaultSubscription;
            m_sessionTimeout = template.m_sessionTimeout;
            m_maxRequestMessageSize = template.m_maxRequestMessageSize;
            m_preferredLocales = template.m_preferredLocales;
            m_sessionName = template.m_sessionName;
            m_handle = template.m_handle;
            m_identity = template.m_identity;
            m_keepAliveInterval = template.m_keepAliveInterval;

            if (copyEventHandlers)
            {
                m_KeepAlive = template.m_KeepAlive;
                m_Publish = template.m_Publish;
                m_PublishError = template.m_PublishError;
                m_SubscriptionsChanged = template.m_SubscriptionsChanged;
                m_SessionClosing = template.m_SessionClosing;
            }

            foreach (Subscription subscription in template.Subscriptions)
            {
                this.AddSubscription(new Subscription(subscription, copyEventHandlers));
            }
        }

        /// <summary>
        /// Initializes the channel.
        /// </summary>
        private void Initialize(
            ITransportChannel channel,
            ApplicationConfiguration configuration,
            ConfiguredEndpoint endpoint,
            X509Certificate2 clientCertificate)
        {
            Initialize();

            // save configuration information.
            m_configuration = configuration;
            m_endpoint = endpoint;

            // update the default subscription. 
            m_defaultSubscription.MinLifetimeInterval = (uint)configuration.ClientConfiguration.MinSubscriptionLifetime;

            if (m_endpoint.Description.SecurityPolicyUri != SecurityPolicies.None)
            {
                // update client certificate.
                m_instanceCertificate = clientCertificate;

                if (clientCertificate == null)
                {
                    // load the application instance certificate.
                    if (m_configuration.SecurityConfiguration.ApplicationCertificate == null)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadConfigurationError,
                            "The client configuration does not specify an application instance certificate.");
                    }

                    Task t = Task.Run( async () =>
                    {
                        m_instanceCertificate = await m_configuration.SecurityConfiguration.ApplicationCertificate.Find(true);
                    });
                    t.Wait();
                }

                // check for valid certificate.
                if (m_instanceCertificate == null)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadConfigurationError,
                        "Cannot find the application instance certificate. Store={0}, SubjectName={1}, Thumbprint={2}.",
                        m_configuration.SecurityConfiguration.ApplicationCertificate.StorePath,
                        m_configuration.SecurityConfiguration.ApplicationCertificate.SubjectName,
                        m_configuration.SecurityConfiguration.ApplicationCertificate.Thumbprint);
                }

                // check for private key.
                if (!m_instanceCertificate.HasPrivateKey)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadConfigurationError,
                        "Do not have a privat key for the application instance certificate. Subject={0}, Thumbprint={1}.",
                        m_instanceCertificate.Subject,
                        m_instanceCertificate.Thumbprint);
                }

                //load certificate chain
                /*m_instanceCertificateChain = new X509Certificate2Collection(m_instanceCertificate);
                List<CertificateIdentifier> issuers = new List<CertificateIdentifier>();
                configuration.CertificateValidator.GetIssuers(m_instanceCertificate, issuers);
                for (int i = 0; i < issuers.Count; i++)
                {
                    m_instanceCertificateChain.Add(issuers[i].Certificate);
                }*/
            }

            // initialize the message context.
            ServiceMessageContext messageContext = channel.MessageContext;

            if (messageContext != null)
            {
                m_namespaceUris = messageContext.NamespaceUris;
                m_serverUris = messageContext.ServerUris;
                m_factory = messageContext.Factory;
            }
            else
            {
                m_namespaceUris = new NamespaceTable();
                m_serverUris = new StringTable();
                m_factory = ServiceMessageContext.GlobalContext.Factory;
            }

            // set the default preferred locales.
            m_preferredLocales = new string[] { CultureInfo.CurrentCulture.Name };

            // create a context to use.
            m_systemContext = new SystemContext();

            m_systemContext.SystemHandle = this;
            m_systemContext.EncodeableFactory = m_factory;
            m_systemContext.NamespaceUris = m_namespaceUris;
            m_systemContext.ServerUris = m_serverUris;
            m_systemContext.TypeTable = this.TypeTree;
            m_systemContext.PreferredLocales = null;
            m_systemContext.SessionId = null;
            m_systemContext.UserIdentity = null;
        }

        /// <summary>
        /// Sets the object members to default values.
        /// </summary>
        private void Initialize()
        {
            m_sessionTimeout = 0;
            m_namespaceUris = new NamespaceTable();
            m_serverUris = new StringTable();
            m_factory = EncodeableFactory.GlobalFactory;
            m_nodeCache = new NodeCache(this);
            m_configuration = null;
            m_instanceCertificate = null;
            m_endpoint = null;
            m_subscriptions = new List<Subscription>();
            m_dictionaries = new Dictionary<NodeId, DataDictionary>();
            m_acknowledgementsToSend = new SubscriptionAcknowledgementCollection();
            m_identityHistory = new List<IUserIdentity>();
            m_outstandingRequests = new LinkedList<AsyncRequestState>();
            m_keepAliveInterval = 5000;
            m_sessionName = "";

            m_defaultSubscription = new Subscription();

            m_defaultSubscription.DisplayName = "Subscription";
            m_defaultSubscription.PublishingInterval = 1000;
            m_defaultSubscription.KeepAliveCount = 10;
            m_defaultSubscription.LifetimeCount = 1000;
            m_defaultSubscription.Priority = 255;
            m_defaultSubscription.PublishingEnabled = true;
        }
        #endregion

        #region IDisposable Members
        /// <summary>
        /// Closes the session and the underlying channel.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Utils.SilentDispose(m_keepAliveTimer);
                m_keepAliveTimer = null;

                Utils.SilentDispose(m_defaultSubscription);
                m_defaultSubscription = null;

                foreach (Subscription subscription in m_subscriptions)
                {
                    Utils.SilentDispose(subscription);
                }

                m_subscriptions.Clear();
            }

            base.Dispose(disposing);
        }
        #endregion

        #region Events
        /// <summary>
        /// Raised when a keep alive arrives from the server or an error is detected.
        /// </summary>
        /// <remarks>
        /// Once a session is created a timer will periodically read the server state and current time.
        /// If this read operation succeeds this event will be raised each time the keep alive period elapses.
        /// If an error is detected (KeepAliveStopped == true) then this event will be raised as well.
        /// </remarks>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1009:DeclareEventHandlersCorrectly")]
        public event KeepAliveEventHandler KeepAlive
        {
            add
            {
                lock (m_eventLock)
                {
                    m_KeepAlive += value;
                }
            }

            remove
            {
                lock (m_eventLock)
                {
                    m_KeepAlive -= value;
                }
            }
        }

        /// <summary>
        /// Raised when a notification message arrives in a publish response.
        /// </summary>
        /// <remarks>
        /// All publish requests are managed by the Session object. When a response arrives it is
        /// validated and passed to the appropriate Subscription object and this event is raised.
        /// </remarks>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1009:DeclareEventHandlersCorrectly")]
        public event NotificationEventHandler Notification
        {
            add
            {
                lock (m_eventLock)
                {
                    m_Publish += value;
                }
            }

            remove
            {
                lock (m_eventLock)
                {
                    m_Publish -= value;
                }
            }
        }

        /// <summary>
        /// Raised when an exception occurs while processing a publish response.
        /// </summary>
        /// <remarks>
        /// Exceptions in a publish response are not necessarily fatal and the Session will 
        /// attempt to recover by issuing Republish requests if missing messages are detected.
        /// That said, timeout errors may be a symptom of a OperationTimeout that is too short
        /// when compared to the shortest PublishingInterval/KeepAliveCount amount the current
        /// Subscriptions. The OperationTimeout should be twice the minimum value for
        /// PublishingInterval*KeepAliveCount.
        /// </remarks>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1009:DeclareEventHandlersCorrectly")]
        public event PublishErrorEventHandler PublishError
        {
            add
            {
                lock (m_eventLock)
                {
                    m_PublishError += value;
                }
            }

            remove
            {
                lock (m_eventLock)
                {
                    m_PublishError -= value;
                }
            }
        }

        /// <summary>
        /// Raised when a subscription is added or removed
        /// </summary>
        public event EventHandler SubscriptionsChanged
        {
            add
            {
                m_SubscriptionsChanged += value;
            }

            remove
            {
                m_SubscriptionsChanged -= value;
            }
        }

        /// <summary>
        /// Raised to indicate the session is closing.
        /// </summary>
        public event EventHandler SessionClosing
        {
            add
            {
                m_SessionClosing += value;
            }

            remove
            {
                m_SessionClosing -= value;
            }
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// Gets the endpoint used to connect to the server.
        /// </summary>
        public ConfiguredEndpoint ConfiguredEndpoint
        {
            get
            {
                return m_endpoint;
            }
        }

        /// <summary>
        /// Gets the name assigned to the session.
        /// </summary>
        public string SessionName
        {
            get
            {
                return m_sessionName;
            }
        }

        /// <summary>
        /// Gets the period for wich the server will maintain the session if there is no communication from the client.
        /// </summary>
        public double SessionTimeout
        {
            get
            {
                return m_sessionTimeout;
            }
        }

        /// <summary>
        /// Gets the local handle assigned to the session
        /// </summary>
        public object Handle
        {
            get { return m_handle; }
            set { m_handle = value; }
        }

        /// <summary>
        /// Gets the user identity currently used for the session.
        /// </summary>
        public IUserIdentity Identity
        {
            get
            {
                return m_identity;
            }
        }

        /// <summary>
        /// Gets a list of user identities that can be used to connect to the server.
        /// </summary>
        public IEnumerable<IUserIdentity> IdentityHistory
        {
            get { return m_identityHistory; }
        }

        /// <summary>
        /// Gets the table of namespace uris known to the server.
        /// </summary>
        public NamespaceTable NamespaceUris
        {
            get { return m_namespaceUris; }
        }

        /// <summary>
        /// Gest the table of remote server uris known to the server.
        /// </summary>
        public StringTable ServerUris
        {
            get { return m_serverUris; }
        }

        /// <summary>
        /// Gets the system context for use with the session.
        /// </summary>
        public ISystemContext SystemContext
        {
            get { return m_systemContext; }
        }

        /// <summary>
        /// Gets the factory used to create encodeable objects that the server understands.
        /// </summary>
        public EncodeableFactory Factory
        {
            get { return m_factory; }
        }

        /// <summary>
        /// Gets the cache of the server's type tree.
        /// </summary>
        public ITypeTable TypeTree
        {
            get { return m_nodeCache.TypeTree; }
        }

        /// <summary>
        /// Gets the cache of nodes fetched from the server.
        /// </summary>
        public NodeCache NodeCache
        {
            get { return m_nodeCache; }
        }

        /// <summary>
        /// Gets the context to use for filter operations.
        /// </summary>
        public FilterContext FilterContext
        {
            get { return new FilterContext(m_namespaceUris, m_nodeCache.TypeTree, m_preferredLocales); }
        }

        /// <summary>
        /// Gets the locales that the server should use when returning localized text.
        /// </summary>
        public StringCollection PreferredLocales
        {
            get { return m_preferredLocales; }
        }

        /// <summary>
        /// Gets the subscriptions owned by the session.
        /// </summary>
        public IEnumerable<Subscription> Subscriptions
        {
            get
            {
                lock (SyncRoot)
                {
                    return new ReadOnlyList<Subscription>(m_subscriptions);
                }
            }
        }

        /// <summary>
        /// Gets the number of subscriptions owned by the session.
        /// </summary>
        public int SubscriptionCount
        {
            get
            {
                lock (SyncRoot)
                {
                    return m_subscriptions.Count;
                }
            }
        }

        /// <summary>
        /// Gets or Sets the default subscription for the session.
        /// </summary>
        public Subscription DefaultSubscription
        {
            get { return m_defaultSubscription; }
            set { m_defaultSubscription = value; }
        }

        /// <summary>
        /// Gets or Sets how frequently the server is pinged to see if communication is still working.
        /// </summary>
        /// <remarks>
        /// This interval controls how much time elaspes before a communication error is detected.
        /// If everything is ok the KeepAlive event will be raised each time this period elapses.
        /// </remarks>
        public int KeepAliveInterval
        {
            get
            {
                return m_keepAliveInterval;
            }

            set
            {
                m_keepAliveInterval = value;
                StartKeepAliveTimer();
            }
        }

        /// <summary>
        /// Returns true if the session is not receiving keep alives.
        /// </summary>
        /// <remarks>
        /// Set to true if the server does not respond for 2 times the KeepAliveInterval.
        /// Set to false is communication recovers.
        /// </remarks>
        public bool KeepAliveStopped
        {
            get
            {
                lock (m_eventLock)
                {
                    long delta = DateTime.UtcNow.Ticks - m_lastKeepAliveTime.Ticks;

                    // add a 1000ms guard band to allow for network lag.
                    return (m_keepAliveInterval * 2) * TimeSpan.TicksPerMillisecond <= delta;
                }
            }
        }

        /// <summary>
        /// Gets the time of the last keep alive.
        /// </summary>
        public DateTime LastKeepAliveTime
        {
            get { return m_lastKeepAliveTime; }
        }

        /// <summary>
        /// Gets the number of outstanding publish or keep alive requests.
        /// </summary>
        public int OutstandingRequestCount
        {
            get
            {
                lock (m_outstandingRequests)
                {
                    return m_outstandingRequests.Count;
                }
            }
        }

        /// <summary>
        /// Gets the number of outstanding publish or keep alive requests which appear to hung.
        /// </summary>
        public int DefunctRequestCount
        {
            get
            {
                lock (m_outstandingRequests)
                {
                    int count = 0;

                    for (LinkedListNode<AsyncRequestState> ii = m_outstandingRequests.First; ii != null; ii = ii.Next)
                    {
                        if (ii.Value.Defunct)
                        {
                            count++;
                        }
                    }

                    return count;
                }
            }
        }

        /// <summary>
        /// Gets the number of good outstanding publish requests.
        /// </summary>
        public int GoodPublishRequestCount
        {
            get
            {
                lock (m_outstandingRequests)
                {
                    int count = 0;

                    for (LinkedListNode<AsyncRequestState> ii = m_outstandingRequests.First; ii != null; ii = ii.Next)
                    {
                        if (!ii.Value.Defunct && ii.Value.RequestTypeId == DataTypes.PublishRequest)
                        {
                            count++;
                        }
                    }

                    return count;
                }
            }
        }
        #endregion

        #region Public Methods

        /// <summary>
        /// Creates a new communication session with a server by invoking the CreateSession service
        /// </summary>
        /// <param name="configuration">The configuration for the client application.</param>
        /// <param name="endpoint">The endpoint for the server.</param>
        /// <param name="updateBeforeConnect">If set to <c>true</c> the discovery endpoint is used to update the endpoint description before connecting.</param>
        /// <param name="sessionName">The name to assign to the session.</param>
        /// <param name="sessionTimeout">The timeout period for the session.</param>
        /// <param name="identity">The identity.</param>
        /// <param name="preferredLocales">The user identity to associate with the session.</param>
        /// <returns>The new session object</returns>
        public static async Task<Session> Create(
            ApplicationConfiguration configuration,
            ConfiguredEndpoint endpoint,
            bool updateBeforeConnect,
            string sessionName,
            uint sessionTimeout,
            IUserIdentity identity,
            IList<string> preferredLocales)
        {
            return await Create(configuration, endpoint, updateBeforeConnect, false, sessionName, sessionTimeout, identity, preferredLocales);
        }

        /// <summary>
        /// Creates a new communication session with a server by invoking the CreateSession service
        /// </summary>
        /// <param name="configuration">The configuration for the client application.</param>
        /// <param name="endpoint">The endpoint for the server.</param>
        /// <param name="updateBeforeConnect">If set to <c>true</c> the discovery endpoint is used to update the endpoint description before connecting.</param>
        /// <param name="checkDomain">If set to <c>true</c> then the domain in the certificate must match the endpoint used.</param>
        /// <param name="sessionName">The name to assign to the session.</param>
        /// <param name="sessionTimeout">The timeout period for the session.</param>
        /// <param name="identity">The user identity to associate with the session.</param>
        /// <param name="preferredLocales">The preferred locales.</param>
        /// <returns>The new session object.</returns>
        public static async Task<Session> Create(
            ApplicationConfiguration configuration,
            ConfiguredEndpoint endpoint,
            bool updateBeforeConnect,
            bool checkDomain,
            string sessionName,
            uint sessionTimeout,
            IUserIdentity identity,
            IList<string> preferredLocales)
        {
            endpoint.UpdateBeforeConnect = updateBeforeConnect;

            EndpointDescription endpointDescription = endpoint.Description;

            // create the endpoint configuration (use the application configuration to provide default values).
            EndpointConfiguration endpointConfiguration = endpoint.Configuration;

            if (endpointConfiguration == null)
            {
                endpoint.Configuration = endpointConfiguration = EndpointConfiguration.Create(configuration);
            }

            // create message context.
            ServiceMessageContext messageContext = configuration.CreateMessageContext();

            // update endpoint description using the discovery endpoint.
            if (endpoint.UpdateBeforeConnect)
            {
                BindingFactory bindingFactory = BindingFactory.Create(configuration, messageContext);
                endpoint.UpdateFromServer(bindingFactory);

                endpointDescription = endpoint.Description;
                endpointConfiguration = endpoint.Configuration;
            }

            // checks the domains in the certificate.
            if (checkDomain && endpoint.Description.ServerCertificate != null && endpoint.Description.ServerCertificate.Length > 0)
            {
                bool domainFound = false;

                X509Certificate2 serverCertificate = new X509Certificate2(endpoint.Description.ServerCertificate);

                // check the certificate domains.
                IList<string> domains = Utils.GetDomainsFromCertficate(serverCertificate);

                if (domains != null)
                {
                    string hostname = endpoint.EndpointUrl.DnsSafeHost;

                    if (hostname == "localhost" || hostname == "127.0.0.1")
                    {
                        hostname = Utils.GetHostName();
                    }

                    for (int ii = 0; ii < domains.Count; ii++)
                    {
                        if (String.Compare(hostname, domains[ii], StringComparison.CurrentCultureIgnoreCase) == 0)
                        {
                            domainFound = true;
                            break;
                        }
                    }
                }

                if (!domainFound)
                {
                    throw new ServiceResultException(StatusCodes.BadCertificateHostNameInvalid);
                }
            }

            X509Certificate2 clientCertificate = null;

            if (endpointDescription.SecurityPolicyUri != SecurityPolicies.None)
            {
                if (configuration.SecurityConfiguration.ApplicationCertificate == null)
                {
                    throw ServiceResultException.Create(StatusCodes.BadConfigurationError, "ApplicationCertificate must be specified.");
                }

                clientCertificate = await configuration.SecurityConfiguration.ApplicationCertificate.Find(true);

                if (clientCertificate == null)
                {
                    throw ServiceResultException.Create(StatusCodes.BadConfigurationError, "ApplicationCertificate cannot be found.");
                }
            }

            // initialize the channel which will be created with the server.
            ITransportChannel channel = SessionChannel.Create(
                 configuration,
                 endpointDescription,
                 endpointConfiguration,
                 //clientCertificateChain,
                 clientCertificate,
                 messageContext);

            // create the session object.
            Session session = new Session(channel, configuration, endpoint, null);

            // create the session.
            try
            {
                session.Open(sessionName, sessionTimeout, identity, preferredLocales);
            }
            catch
            {
                session.Dispose();
                throw;
            }

            return session;
        }


        /// <summary>
        /// Recreates a session based on a specified template.
        /// </summary>
        /// <param name="template">The Session object to use as template</param>
        /// <returns>The new session object.</returns>
        public static Session Recreate(Session template)
        {
            // create the channel object used to connect to the server.
            ITransportChannel channel = SessionChannel.Create(
                template.m_configuration,
                template.m_endpoint.Description,
                template.m_endpoint.Configuration,
                template.m_instanceCertificate,
                template.m_configuration.CreateMessageContext());

            // create the session object.
            Session session = new Session(channel, template, true);

            try
            {
                // open the session.
                session.Open(
                    template.m_sessionName,
                    (uint)template.m_sessionTimeout,
                    template.m_identity,
                    template.m_preferredLocales);

                // create the subscriptions.
                foreach (Subscription subscription in session.Subscriptions)
                {
                    subscription.Create();
                }
            }
            catch (Exception e)
            {
                session.Dispose();
                throw ServiceResultException.Create(StatusCodes.BadCommunicationError, e, "Could not recreate session. {0}", template.m_sessionName);
            }

            return session;
        }

        /// <summary>
        /// Used to handle renews of user identity tokens before reconnect.
        /// </summary>
        public delegate IUserIdentity RenewUserIdentityEventHandler(Session session, IUserIdentity identity);

        /// <summary>
        /// Raised before a reconnect operation completes.
        /// </summary>
        public event RenewUserIdentityEventHandler RenewUserIdentity
        {
            add { m_RenewUserIdentity += value; }
            remove { m_RenewUserIdentity -= value; }
        }

        private event RenewUserIdentityEventHandler m_RenewUserIdentity;

        /// <summary>
        /// Reconnects to the server after a network failure.
        /// </summary>
        public void Reconnect()
        {
            try
            {
                lock (SyncRoot)
                {
                    // check if already connecting.
                    if (m_reconnecting)
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadInvalidState,
                            "Session is already attempting to reconnect.");
                    }

                    Utils.Trace("Session RECONNECT starting.");
                    m_reconnecting = true;

                    // stop keep alives.
                    if (m_keepAliveTimer != null)
                    {
                        m_keepAliveTimer.Dispose();
                        m_keepAliveTimer = null;
                    }
                }

                EndpointDescription endpoint = m_endpoint.Description;

                // create the client signature.
                byte[] dataToSign = Utils.Append(endpoint.ServerCertificate, m_serverNonce);
                SignatureData clientSignature = SecurityPolicies.Sign(m_instanceCertificate, endpoint.SecurityPolicyUri, dataToSign);

                // check that the user identity is supported by the endpoint.
                UserTokenPolicy identityPolicy = endpoint.FindUserTokenPolicy(m_identity.TokenType, m_identity.IssuedTokenType);

                if (identityPolicy == null)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadUserAccessDenied,
                        "Endpoint does not supported the user identity type provided.");
                }

                // select the security policy for the user token.
                string securityPolicyUri = identityPolicy.SecurityPolicyUri;

                if (String.IsNullOrEmpty(securityPolicyUri))
                {
                    securityPolicyUri = endpoint.SecurityPolicyUri;
                }

                // need to refresh the identity (reprompt for password, refresh token).
                if (m_RenewUserIdentity != null)
                {
                    m_identity = m_RenewUserIdentity(this, m_identity);
                }

                // sign data with user token.
                UserIdentityToken identityToken = m_identity.GetIdentityToken();
                identityToken.PolicyId = identityPolicy.PolicyId;
                SignatureData userTokenSignature = identityToken.Sign(dataToSign, securityPolicyUri);

                // encrypt token.
                identityToken.Encrypt(m_serverCertificate, m_serverNonce, securityPolicyUri);

                // send the software certificates assigned to the client.
                SignedSoftwareCertificateCollection clientSoftwareCertificates = GetSoftwareCertificates();

                Utils.Trace("Session REPLACING channel.");

                // check if the channel supports reconnect.
                if ((TransportChannel.SupportedFeatures & TransportChannelFeatures.Reconnect) != 0)
                {
                    TransportChannel.Reconnect();
                }
                else
                {
                    // initialize the channel which will be created with the server.
                    ITransportChannel channel = SessionChannel.Create(
                        m_configuration,
                        m_endpoint.Description,
                        m_endpoint.Configuration,
                        m_instanceCertificate,
                        MessageContext);

                    // disposes the existing channel.
                    TransportChannel = channel;
                }

                // reactivate session.
                byte[] serverNonce = null;
                StatusCodeCollection certificateResults = null;
                DiagnosticInfoCollection certificateDiagnosticInfos = null;

                Utils.Trace("Session RE-ACTIVATING session.");

                IAsyncResult result = BeginActivateSession(
                    null,
                    clientSignature,
                    null,
                    m_preferredLocales,
                    new ExtensionObject(identityToken),
                    userTokenSignature,
                    null,
                    null);

                if (!result.AsyncWaitHandle.WaitOne(5000))
                {
                    Utils.Trace("WARNING: ACTIVATE SESSION timed out. {1}/{0}", OutstandingRequestCount, GoodPublishRequestCount);
                }

                EndActivateSession(
                    result,
                    out serverNonce,
                    out certificateResults,
                    out certificateDiagnosticInfos);

                int publishCount = 0;

                lock (SyncRoot)
                {
                    Utils.Trace("Session RECONNECT completed successfully.");
                    m_serverNonce = serverNonce;
                    m_reconnecting = false;
                    publishCount = m_subscriptions.Count;
                }

                // refill pipeline.
                for (int ii = 0; ii < publishCount; ii++)
                {
                    BeginPublish(OperationTimeout);
                }

                StartKeepAliveTimer();
            }
            finally
            {
                m_reconnecting = false;
            }
        }


        /// <summary>
        /// Saves all the subscriptions of the session.
        /// </summary>
        /// <param name="filePath">The file path.</param>
        public void Save(string filePath)
        {
            Save(filePath, Subscriptions);
        }

        /// <summary>
        /// Saves a set of subscriptions.
        /// </summary>
        public void Save(string filePath, IEnumerable<Subscription> subscriptions)
        {
            XmlWriterSettings settings = new XmlWriterSettings();

            settings.Indent = true;
            settings.OmitXmlDeclaration = false;
            settings.Encoding = Encoding.UTF8;

            XmlWriter writer = XmlWriter.Create(new StringBuilder(filePath), settings);

            SubscriptionCollection subscriptionList = new SubscriptionCollection(subscriptions);

            try
            {
                DataContractSerializer serializer = new DataContractSerializer(typeof(SubscriptionCollection));
                serializer.WriteObject(writer, subscriptionList);
            }
            finally
            {
                writer.Flush();
                writer.Dispose();
            }
        }


        /// <summary>
        /// Load the list of subscriptions saved in a file.
        /// </summary>
        /// <param name="filePath">The file path.</param>
        /// <returns>The list of loaded subscritons</returns>
        public IEnumerable<Subscription> Load(string filePath)
        {
            XmlReaderSettings settings = new XmlReaderSettings();

            settings.ConformanceLevel = ConformanceLevel.Document;
            settings.CloseInput = true;

            XmlReader reader = XmlReader.Create(filePath, settings);

            try
            {
                DataContractSerializer serializer = new DataContractSerializer(typeof(SubscriptionCollection));

                SubscriptionCollection subscriptions = (SubscriptionCollection)serializer.ReadObject(reader);

                foreach (Subscription subscription in subscriptions)
                {
                    AddSubscription(subscription);
                }

                return subscriptions;
            }
            finally
            {
                reader.Dispose();
            }
        }

        /// <summary>
        /// Updates the local copy of the server's namespace uri and server uri tables.
        /// </summary>
        public void FetchNamespaceTables()
        {
            ReadValueIdCollection nodesToRead = new ReadValueIdCollection();

            // request namespace array.
            ReadValueId valueId = new ReadValueId();

            valueId.NodeId = Variables.Server_NamespaceArray;
            valueId.AttributeId = Attributes.Value;

            nodesToRead.Add(valueId);

            // request server array.
            valueId = new ReadValueId();

            valueId.NodeId = Variables.Server_ServerArray;
            valueId.AttributeId = Attributes.Value;

            nodesToRead.Add(valueId);

            // read from server.
            DataValueCollection values = null;
            DiagnosticInfoCollection diagnosticInfos = null;

            ResponseHeader responseHeader = this.Read(
                null,
                0,
                TimestampsToReturn.Both,
                nodesToRead,
                out values,
                out diagnosticInfos);

            ValidateResponse(values, nodesToRead);
            ValidateDiagnosticInfos(diagnosticInfos, nodesToRead);

            // validate namespace array.
            ServiceResult result = ValidateDataValue(values[0], typeof(string[]), 0, diagnosticInfos, responseHeader);

            if (ServiceResult.IsBad(result))
            {
                throw new ServiceResultException(result);
            }

            m_namespaceUris.Update((string[])values[0].Value);

            // validate server array.
            result = ValidateDataValue(values[1], typeof(string[]), 1, diagnosticInfos, responseHeader);

            if (ServiceResult.IsBad(result))
            {
                throw new ServiceResultException(result);
            }

            m_serverUris.Update((string[])values[1].Value);
        }

        /// <summary>
        /// Updates the cache with the type and its subtypes.
        /// </summary>
        /// <remarks>
        /// This method can be used to ensure the TypeTree is populated.
        /// </remarks>
        public void FetchTypeTree(ExpandedNodeId typeId)
        {
            Node node = NodeCache.Find(typeId) as Node;

            if (node != null)
            {
                foreach (IReference reference in node.Find(ReferenceTypeIds.HasSubtype, false))
                {
                    FetchTypeTree(reference.TargetId);
                }
            }
        }

        /// <summary>
        /// Returns the available encodings for a node
        /// </summary>
        /// <param name="variableId">The variable node.</param>
        /// <returns></returns>
        public ReferenceDescriptionCollection ReadAvailableEncodings(NodeId variableId)
        {
            VariableNode variable = NodeCache.Find(variableId) as VariableNode;

            if (variable == null)
            {
                throw ServiceResultException.Create(StatusCodes.BadNodeIdInvalid, "NodeId does not refer to a valid variable node.");
            }

            // no encodings available if there was a problem reading the data type for the node.
            if (NodeId.IsNull(variable.DataType))
            {
                return new ReferenceDescriptionCollection();
            }

            // no encodings for non-structures.
            if (!TypeTree.IsTypeOf(variable.DataType, DataTypes.Structure))
            {
                return new ReferenceDescriptionCollection();
            }

            // look for cached values.
            IList<INode> encodings = NodeCache.Find(variableId, ReferenceTypeIds.HasEncoding, false, true);

            if (encodings.Count > 0)
            {
                ReferenceDescriptionCollection references = new ReferenceDescriptionCollection();

                foreach (INode encoding in encodings)
                {
                    ReferenceDescription reference = new ReferenceDescription();

                    reference.ReferenceTypeId = ReferenceTypeIds.HasEncoding;
                    reference.IsForward = true;
                    reference.NodeId = encoding.NodeId;
                    reference.NodeClass = encoding.NodeClass;
                    reference.BrowseName = encoding.BrowseName;
                    reference.DisplayName = encoding.DisplayName;
                    reference.TypeDefinition = encoding.TypeDefinitionId;

                    references.Add(reference);
                }

                return references;
            }

            Browser browser = new Browser(this);

            browser.BrowseDirection = BrowseDirection.Forward;
            browser.ReferenceTypeId = ReferenceTypeIds.HasEncoding;
            browser.IncludeSubtypes = false;
            browser.NodeClassMask = 0;

            return browser.Browse(variable.DataType);
        }


        /// <summary>
        /// Returns the data description for the encoding.
        /// </summary>
        /// <param name="encodingId">The encoding Id.</param>
        /// <returns></returns>
        public ReferenceDescription FindDataDescription(NodeId encodingId)
        {
            Browser browser = new Browser(this);

            browser.BrowseDirection = BrowseDirection.Forward;
            browser.ReferenceTypeId = ReferenceTypeIds.HasDescription;
            browser.IncludeSubtypes = false;
            browser.NodeClassMask = 0;

            ReferenceDescriptionCollection references = browser.Browse(encodingId);

            if (references.Count == 0)
            {
                throw ServiceResultException.Create(StatusCodes.BadNodeIdInvalid, "Encoding does not refer to a valid data description.");
            }

            return references[0];
        }


        /// <summary>
        ///  Returns the data dictionary that constains the description.
        /// </summary>
        /// <param name="descriptionId">The description id.</param>
        /// <returns></returns>
        public async Task<DataDictionary> FindDataDictionary(NodeId descriptionId)
        {
            // check if the dictionary has already been loaded.
            foreach (DataDictionary dictionary in m_dictionaries.Values)
            {
                if (dictionary.Contains(descriptionId))
                {
                    return dictionary;
                }
            }

            // find the dictionary for the description.
            Browser browser = new Browser(this);

            browser.BrowseDirection = BrowseDirection.Inverse;
            browser.ReferenceTypeId = ReferenceTypeIds.HasComponent;
            browser.IncludeSubtypes = false;
            browser.NodeClassMask = 0;

            ReferenceDescriptionCollection references = browser.Browse(descriptionId);

            if (references.Count == 0)
            {
                throw ServiceResultException.Create(StatusCodes.BadNodeIdInvalid, "Description does not refer to a valid data dictionary.");
            }

            // load the dictionary.
            NodeId dictionaryId = ExpandedNodeId.ToNodeId(references[0].NodeId, m_namespaceUris);

            DataDictionary dictionaryToLoad = new DataDictionary(this);

            await dictionaryToLoad.Load(references[0]);

            m_dictionaries[dictionaryId] = dictionaryToLoad;

            return dictionaryToLoad;
        }

        /// <summary>
        /// Reads the values for the node attributes and returns a node object.
        /// </summary>
        /// <param name="nodeId">The nodeId.</param>
        /// <returns></returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA1505:AvoidUnmaintainableCode"), System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA1502:AvoidExcessiveComplexity"), System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA1506:AvoidExcessiveClassCoupling")]
        public Node ReadNode(NodeId nodeId)
        {
            // build list of attributes.
            SortedDictionary<uint, DataValue> attributes = new SortedDictionary<uint, DataValue>();

            attributes.Add(Attributes.NodeId, null);
            attributes.Add(Attributes.NodeClass, null);
            attributes.Add(Attributes.BrowseName, null);
            attributes.Add(Attributes.DisplayName, null);
            attributes.Add(Attributes.Description, null);
            attributes.Add(Attributes.WriteMask, null);
            attributes.Add(Attributes.UserWriteMask, null);
            attributes.Add(Attributes.DataType, null);
            attributes.Add(Attributes.ValueRank, null);
            attributes.Add(Attributes.ArrayDimensions, null);
            attributes.Add(Attributes.AccessLevel, null);
            attributes.Add(Attributes.UserAccessLevel, null);
            attributes.Add(Attributes.Historizing, null);
            attributes.Add(Attributes.MinimumSamplingInterval, null);
            attributes.Add(Attributes.EventNotifier, null);
            attributes.Add(Attributes.Executable, null);
            attributes.Add(Attributes.UserExecutable, null);
            attributes.Add(Attributes.IsAbstract, null);
            attributes.Add(Attributes.InverseName, null);
            attributes.Add(Attributes.Symmetric, null);
            attributes.Add(Attributes.ContainsNoLoops, null);

            // build list of values to read.
            ReadValueIdCollection itemsToRead = new ReadValueIdCollection();

            foreach (uint attributeId in attributes.Keys)
            {
                ReadValueId itemToRead = new ReadValueId();

                itemToRead.NodeId = nodeId;
                itemToRead.AttributeId = attributeId;

                itemsToRead.Add(itemToRead);
            }

            // read from server.
            DataValueCollection values = null;
            DiagnosticInfoCollection diagnosticInfos = null;

            ResponseHeader responseHeader = Read(
                null,
                0,
                TimestampsToReturn.Neither,
                itemsToRead,
                out values,
                out diagnosticInfos);

            ClientBase.ValidateResponse(values, itemsToRead);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, itemsToRead);

            // process results.
            int? nodeClass = null;

            for (int ii = 0; ii < itemsToRead.Count; ii++)
            {
                uint attributeId = itemsToRead[ii].AttributeId;

                // the node probably does not exist if the node class is not found.
                if (attributeId == Attributes.NodeClass)
                {
                    if (!DataValue.IsGood(values[ii]))
                    {
                        throw ServiceResultException.Create(values[ii].StatusCode, ii, diagnosticInfos, responseHeader.StringTable);
                    }

                    // check for valid node class.
                    nodeClass = values[ii].Value as int?;

                    if (nodeClass == null)
                    {
                        throw ServiceResultException.Create(StatusCodes.BadUnexpectedError, "Node does not have a valid value for NodeClass: {0}.", values[ii].Value);
                    }
                }
                else
                {
                    if (!DataValue.IsGood(values[ii]))
                    {
                        // check for unsupported attributes.
                        if (values[ii].StatusCode == StatusCodes.BadAttributeIdInvalid)
                        {
                            continue;
                        }

                        // all supported attributes must be readable.
                        if (attributeId != Attributes.Value)
                        {
                            throw ServiceResultException.Create(values[ii].StatusCode, ii, diagnosticInfos, responseHeader.StringTable);
                        }
                    }
                }

                attributes[attributeId] = values[ii];
            }

            Node node = null;
            DataValue value = null;

            switch ((NodeClass)nodeClass.Value)
            {
                default:
                    {
                        throw ServiceResultException.Create(StatusCodes.BadUnexpectedError, "Node does not have a valid value for NodeClass: {0}.", nodeClass.Value);
                    }

                case NodeClass.Object:
                    {
                        ObjectNode objectNode = new ObjectNode();

                        value = attributes[Attributes.EventNotifier];

                        if (value == null)
                        {
                            throw ServiceResultException.Create(StatusCodes.BadUnexpectedError, "Object does not support the EventNotifier attribute.");
                        }

                        objectNode.EventNotifier = (byte)attributes[Attributes.EventNotifier].GetValue(typeof(byte));
                        node = objectNode;
                        break;
                    }

                case NodeClass.ObjectType:
                    {
                        ObjectTypeNode objectTypeNode = new ObjectTypeNode();

                        value = attributes[Attributes.IsAbstract];

                        if (value == null)
                        {
                            throw ServiceResultException.Create(StatusCodes.BadUnexpectedError, "ObjectType does not support the IsAbstract attribute.");
                        }

                        objectTypeNode.IsAbstract = (bool)attributes[Attributes.IsAbstract].GetValue(typeof(bool));
                        node = objectTypeNode;
                        break;
                    }

                case NodeClass.Variable:
                    {
                        VariableNode variableNode = new VariableNode();

                        // DataType Attribute
                        value = attributes[Attributes.DataType];

                        if (value == null)
                        {
                            throw ServiceResultException.Create(StatusCodes.BadUnexpectedError, "Variable does not support the DataType attribute.");
                        }

                        variableNode.DataType = (NodeId)attributes[Attributes.DataType].GetValue(typeof(NodeId));

                        // ValueRank Attribute
                        value = attributes[Attributes.ValueRank];

                        if (value == null)
                        {
                            throw ServiceResultException.Create(StatusCodes.BadUnexpectedError, "Variable does not support the ValueRank attribute.");
                        }

                        variableNode.ValueRank = (int)attributes[Attributes.ValueRank].GetValue(typeof(int));

                        // ArrayDimensions Attribute
                        value = attributes[Attributes.ArrayDimensions];

                        if (value != null)
                        {
                            if (value.Value == null)
                            {
                                variableNode.ArrayDimensions = new uint[0];
                            }
                            else
                            {
                                variableNode.ArrayDimensions = (uint[])value.GetValue(typeof(uint[]));
                            }
                        }

                        // AccessLevel Attribute
                        value = attributes[Attributes.AccessLevel];

                        if (value == null)
                        {
                            throw ServiceResultException.Create(StatusCodes.BadUnexpectedError, "Variable does not support the AccessLevel attribute.");
                        }

                        variableNode.AccessLevel = (byte)attributes[Attributes.AccessLevel].GetValue(typeof(byte));

                        // UserAccessLevel Attribute
                        value = attributes[Attributes.UserAccessLevel];

                        if (value == null)
                        {
                            throw ServiceResultException.Create(StatusCodes.BadUnexpectedError, "Variable does not support the UserAccessLevel attribute.");
                        }

                        variableNode.UserAccessLevel = (byte)attributes[Attributes.UserAccessLevel].GetValue(typeof(byte));

                        // Historizing Attribute
                        value = attributes[Attributes.Historizing];

                        if (value == null)
                        {
                            throw ServiceResultException.Create(StatusCodes.BadUnexpectedError, "Variable does not support the Historizing attribute.");
                        }

                        variableNode.Historizing = (bool)attributes[Attributes.Historizing].GetValue(typeof(bool));

                        // MinimumSamplingInterval Attribute
                        value = attributes[Attributes.MinimumSamplingInterval];

                        if (value != null)
                        {
                            variableNode.MinimumSamplingInterval = Convert.ToDouble(attributes[Attributes.MinimumSamplingInterval].Value);
                        }

                        node = variableNode;
                        break;
                    }

                case NodeClass.VariableType:
                    {
                        VariableTypeNode variableTypeNode = new VariableTypeNode();

                        // IsAbstract Attribute
                        value = attributes[Attributes.IsAbstract];

                        if (value == null)
                        {
                            throw ServiceResultException.Create(StatusCodes.BadUnexpectedError, "VariableType does not support the IsAbstract attribute.");
                        }

                        variableTypeNode.IsAbstract = (bool)attributes[Attributes.IsAbstract].GetValue(typeof(bool));

                        // DataType Attribute
                        value = attributes[Attributes.DataType];

                        if (value == null)
                        {
                            throw ServiceResultException.Create(StatusCodes.BadUnexpectedError, "VariableType does not support the DataType attribute.");
                        }

                        variableTypeNode.DataType = (NodeId)attributes[Attributes.DataType].GetValue(typeof(NodeId));

                        // ValueRank Attribute
                        value = attributes[Attributes.ValueRank];

                        if (value == null)
                        {
                            throw ServiceResultException.Create(StatusCodes.BadUnexpectedError, "VariableType does not support the ValueRank attribute.");
                        }

                        variableTypeNode.ValueRank = (int)attributes[Attributes.ValueRank].GetValue(typeof(int));

                        // ArrayDimensions Attribute
                        value = attributes[Attributes.ArrayDimensions];

                        if (value != null && value.Value != null)
                        {
                            variableTypeNode.ArrayDimensions = (uint[])attributes[Attributes.ArrayDimensions].GetValue(typeof(uint[]));
                        }

                        node = variableTypeNode;
                        break;
                    }

                case NodeClass.Method:
                    {
                        MethodNode methodNode = new MethodNode();

                        // Executable Attribute
                        value = attributes[Attributes.Executable];

                        if (value == null)
                        {
                            throw ServiceResultException.Create(StatusCodes.BadUnexpectedError, "Method does not support the Executable attribute.");
                        }

                        methodNode.Executable = (bool)attributes[Attributes.Executable].GetValue(typeof(bool));

                        // UserExecutable Attribute
                        value = attributes[Attributes.UserExecutable];

                        if (value == null)
                        {
                            throw ServiceResultException.Create(StatusCodes.BadUnexpectedError, "Method does not support the UserExecutable attribute.");
                        }

                        methodNode.UserExecutable = (bool)attributes[Attributes.UserExecutable].GetValue(typeof(bool));

                        node = methodNode;
                        break;
                    }

                case NodeClass.DataType:
                    {
                        DataTypeNode dataTypeNode = new DataTypeNode();

                        // IsAbstract Attribute
                        value = attributes[Attributes.IsAbstract];

                        if (value == null)
                        {
                            throw ServiceResultException.Create(StatusCodes.BadUnexpectedError, "DataType does not support the IsAbstract attribute.");
                        }

                        dataTypeNode.IsAbstract = (bool)attributes[Attributes.IsAbstract].GetValue(typeof(bool));

                        node = dataTypeNode;
                        break;
                    }

                case NodeClass.ReferenceType:
                    {
                        ReferenceTypeNode referenceTypeNode = new ReferenceTypeNode();

                        // IsAbstract Attribute
                        value = attributes[Attributes.IsAbstract];

                        if (value == null)
                        {
                            throw ServiceResultException.Create(StatusCodes.BadUnexpectedError, "ReferenceType does not support the IsAbstract attribute.");
                        }

                        referenceTypeNode.IsAbstract = (bool)attributes[Attributes.IsAbstract].GetValue(typeof(bool));

                        // Symmetric Attribute
                        value = attributes[Attributes.Symmetric];

                        if (value == null)
                        {
                            throw ServiceResultException.Create(StatusCodes.BadUnexpectedError, "ReferenceType does not support the Symmetric attribute.");
                        }

                        referenceTypeNode.Symmetric = (bool)attributes[Attributes.IsAbstract].GetValue(typeof(bool));

                        // InverseName Attribute
                        value = attributes[Attributes.InverseName];

                        if (value != null && value.Value != null)
                        {
                            referenceTypeNode.InverseName = (LocalizedText)attributes[Attributes.InverseName].GetValue(typeof(LocalizedText));
                        }

                        node = referenceTypeNode;
                        break;
                    }

                case NodeClass.View:
                    {
                        ViewNode viewNode = new ViewNode();

                        // EventNotifier Attribute
                        value = attributes[Attributes.EventNotifier];

                        if (value == null)
                        {
                            throw ServiceResultException.Create(StatusCodes.BadUnexpectedError, "View does not support the EventNotifier attribute.");
                        }

                        viewNode.EventNotifier = (byte)attributes[Attributes.EventNotifier].GetValue(typeof(byte));

                        // ContainsNoLoops Attribute
                        value = attributes[Attributes.ContainsNoLoops];

                        if (value == null)
                        {
                            throw ServiceResultException.Create(StatusCodes.BadUnexpectedError, "View does not support the ContainsNoLoops attribute.");
                        }

                        viewNode.ContainsNoLoops = (bool)attributes[Attributes.ContainsNoLoops].GetValue(typeof(bool));

                        node = viewNode;
                        break;
                    }
            }

            // NodeId Attribute
            value = attributes[Attributes.NodeId];

            if (value == null)
            {
                throw ServiceResultException.Create(StatusCodes.BadUnexpectedError, "Node does not support the NodeId attribute.");
            }

            node.NodeId = (NodeId)attributes[Attributes.NodeId].GetValue(typeof(NodeId));
            node.NodeClass = (NodeClass)nodeClass.Value;

            // BrowseName Attribute
            value = attributes[Attributes.BrowseName];

            if (value == null)
            {
                throw ServiceResultException.Create(StatusCodes.BadUnexpectedError, "Node does not support the BrowseName attribute.");
            }

            node.BrowseName = (QualifiedName)attributes[Attributes.BrowseName].GetValue(typeof(QualifiedName));

            // DisplayName Attribute
            value = attributes[Attributes.DisplayName];

            if (value == null)
            {
                throw ServiceResultException.Create(StatusCodes.BadUnexpectedError, "Node does not support the DisplayName attribute.");
            }

            node.DisplayName = (LocalizedText)attributes[Attributes.DisplayName].GetValue(typeof(LocalizedText));

            // Description Attribute
            value = attributes[Attributes.Description];

            if (value != null && value.Value != null)
            {
                node.Description = (LocalizedText)attributes[Attributes.Description].GetValue(typeof(LocalizedText));
            }

            // WriteMask Attribute
            value = attributes[Attributes.WriteMask];

            if (value != null)
            {
                node.WriteMask = (uint)attributes[Attributes.WriteMask].GetValue(typeof(uint));
            }

            // UserWriteMask Attribute
            value = attributes[Attributes.UserWriteMask];

            if (value != null)
            {
                node.WriteMask = (uint)attributes[Attributes.UserWriteMask].GetValue(typeof(uint));
            }

            return node;
        }

        /// <summary>
        /// Reads the value for a node.
        /// </summary>
        /// <param name="nodeId">The node Id.</param>
        /// <returns></returns>
        public DataValue ReadValue(NodeId nodeId)
        {
            ReadValueId itemToRead = new ReadValueId();

            itemToRead.NodeId = nodeId;
            itemToRead.AttributeId = Attributes.Value;

            ReadValueIdCollection itemsToRead = new ReadValueIdCollection();
            itemsToRead.Add(itemToRead);

            // read from server.
            DataValueCollection values = null;
            DiagnosticInfoCollection diagnosticInfos = null;

            ResponseHeader responseHeader = Read(
                null,
                0,
                TimestampsToReturn.Both,
                itemsToRead,
                out values,
                out diagnosticInfos);

            ClientBase.ValidateResponse(values, itemsToRead);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, itemsToRead);

            if (StatusCode.IsBad(values[0].StatusCode))
            {
                ServiceResult result = ClientBase.GetResult(values[0].StatusCode, 0, diagnosticInfos, responseHeader);
                throw new ServiceResultException(result);
            }

            return values[0];
        }

        /// <summary>
        /// Reads the value for a node an checks that it is the specified type.
        /// </summary>
        /// <param name="nodeId">The node id.</param>
        /// <param name="expectedType">The expected type.</param>
        /// <returns></returns>
        public object ReadValue(NodeId nodeId, Type expectedType)
        {
            DataValue dataValue = ReadValue(nodeId);

            object value = dataValue.Value;

            if (expectedType != null)
            {
                ExtensionObject extension = value as ExtensionObject;

                if (extension != null)
                {
                    value = extension.Body;
                }

                if (!expectedType.IsInstanceOfType(value))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadTypeMismatch,
                        "Server returned value unexpected type: {0}",
                        (value != null) ? value.GetType().Name : "(null)");
                }
            }

            return value;
        }


        /// <summary>
        /// Fetches all references for the specified node.
        /// </summary>
        /// <param name="nodeId">The node id.</param>
        /// <returns></returns>
        public ReferenceDescriptionCollection FetchReferences(NodeId nodeId)
        {
            // browse for all references.
            byte[] continuationPoint;
            ReferenceDescriptionCollection descriptions;

            Browse(
                null,
                null,
                nodeId,
                0,
                BrowseDirection.Both,
                null,
                true,
                0,
                out continuationPoint,
                out descriptions);

            // process any continuation point.
            while (continuationPoint != null)
            {
                byte[] revisedContinuationPoint;
                ReferenceDescriptionCollection additionalDescriptions;

                BrowseNext(
                    null,
                    false,
                    continuationPoint,
                    out revisedContinuationPoint,
                    out additionalDescriptions);

                continuationPoint = revisedContinuationPoint;

                descriptions.AddRange(additionalDescriptions);
            }

            return descriptions;
        }

        /// <summary>
        /// Establishes a session with the server.
        /// </summary>
        /// <param name="sessionName">The name to assign to the session.</param>
        /// <param name="identity">The user identity.</param>
        public void Open(
            string sessionName,
            IUserIdentity identity)
        {
            Open(sessionName, 0, identity, null);
        }

        /// <summary>
        /// Establishes a session with the server.
        /// </summary>
        /// <param name="sessionName">The name to assign to the session.</param>
        /// <param name="sessionTimeout">The session timeout.</param>
        /// <param name="identity">The user identity.</param>
        /// <param name="preferredLocales">The list of preferred locales.</param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA1506:AvoidExcessiveClassCoupling")]
        public void Open(
            string sessionName,
            uint sessionTimeout,
            IUserIdentity identity,
            IList<string> preferredLocales)
        {
            // check connection state.
            lock (SyncRoot)
            {
                if (Connected)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidState, "Already connected to server.");
                }
            }

            string securityPolicyUri = m_endpoint.Description.SecurityPolicyUri;

            // get the identity token.            
            if (identity == null)
            {
                identity = new UserIdentity();
            }

            // get identity token.
            UserIdentityToken identityToken = identity.GetIdentityToken();

            // check that the user identity is supported by the endpoint.
            UserTokenPolicy identityPolicy = m_endpoint.Description.FindUserTokenPolicy(identityToken.PolicyId);

            if (identityPolicy == null)
            {
                // try looking up by TokenType if the policy id was not found.
                identityPolicy = m_endpoint.Description.FindUserTokenPolicy(identity.TokenType, identity.IssuedTokenType);

                if (identityPolicy == null)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadUserAccessDenied,
                        "Endpoint does not supported the user identity type provided.");
                }

                identityToken.PolicyId = identityPolicy.PolicyId;
            }

            bool requireEncryption = securityPolicyUri != SecurityPolicies.None;
            if (!requireEncryption)
            {
                requireEncryption = identityPolicy.SecurityPolicyUri != SecurityPolicies.None;
            }

            // validate the server certificate.
            X509Certificate2 serverCertificate = null;
            byte[] certificateData = m_endpoint.Description.ServerCertificate;

            if (certificateData != null && certificateData.Length > 0 && requireEncryption)
            {
                serverCertificate = Utils.ParseCertificateBlob(certificateData);
                m_configuration.CertificateValidator.Validate(serverCertificate);
            }

            // create a nonce.
            uint length = (uint)m_configuration.SecurityConfiguration.NonceLength;
            byte[] clientNonce = new byte[length];
            IBuffer buffer = CryptographicBuffer.GenerateRandom(length);
            CryptographicBuffer.CopyToByteArray(buffer, out clientNonce);

            NodeId sessionId = null;
            NodeId sessionCookie = null;
            byte[] serverNonce = new byte[0];
            byte[] serverCertificateData = new byte[0];
            SignatureData serverSignature = null;
            EndpointDescriptionCollection serverEndpoints = null;
            SignedSoftwareCertificateCollection serverSoftwareCertificates = null;

            // send the application instance certificate for the client.
            byte[] clientCertificateData = m_instanceCertificate != null ? m_instanceCertificate.RawData : null;

            ApplicationDescription clientDescription = new ApplicationDescription();

            clientDescription.ApplicationUri = m_configuration.ApplicationUri;
            clientDescription.ApplicationName = m_configuration.ApplicationName;
            clientDescription.ApplicationType = ApplicationType.Client;
            clientDescription.ProductUri = m_configuration.ProductUri;

            if (sessionTimeout == 0)
            {
                sessionTimeout = (uint)m_configuration.ClientConfiguration.DefaultSessionTimeout;
            }

            bool successCreateSession = false;
            //if security none, first try to connect without certificate
            if (m_endpoint.Description.SecurityPolicyUri == SecurityPolicies.None)
            {
                //first try to connect with client certificate NULL
                try
                {
                    CreateSession(
                        null,
                        clientDescription,
                        m_endpoint.Description.Server.ApplicationUri,
                        m_endpoint.EndpointUrl.ToString(),
                        sessionName,
                        clientNonce,
                        null,
                        sessionTimeout,
                        (uint)MessageContext.MaxMessageSize,
                        out sessionId,
                        out sessionCookie,
                        out m_sessionTimeout,
                        out serverNonce,
                        out serverCertificateData,
                        out serverEndpoints,
                        out serverSoftwareCertificates,
                        out serverSignature,
                        out m_maxRequestMessageSize);

                    successCreateSession = true;
                }
                catch (Exception ex)
                {
                    Utils.Trace("Create session failed with client certificate NULL. " + ex.Message);
                    successCreateSession = false;
                }
            }

            if (!successCreateSession)
            {
                CreateSession(
                        null,
                        clientDescription,
                        m_endpoint.Description.Server.ApplicationUri,
                        m_endpoint.EndpointUrl.ToString(),
                        sessionName,
                        clientNonce,
                        clientCertificateData,
                        sessionTimeout,
                        (uint)MessageContext.MaxMessageSize,
                        out sessionId,
                        out sessionCookie,
                        out m_sessionTimeout,
                        out serverNonce,
                        out serverCertificateData,
                        out serverEndpoints,
                        out serverSoftwareCertificates,
                        out serverSignature,
                        out m_maxRequestMessageSize);

            }
            // save session id.
            lock (SyncRoot)
            {
                base.SessionCreated(sessionId, sessionCookie);
            }

            //we need to call CloseSession if CreateSession was successful but some other exception is thrown
            try
            {

                // verify that the server returned the same instance certificate.
                if (serverCertificateData != null && !Utils.IsEqual(serverCertificateData, m_endpoint.Description.ServerCertificate))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadCertificateInvalid,
                        "Server did not return the certificate used to create the secure channel.");
                }

                // find the matching description (TBD - check domains against certificate).
                bool found = false;
                Uri expectedUrl = Utils.ParseUri(m_endpoint.Description.EndpointUrl);

                if (expectedUrl != null)
                {
                    for (int ii = 0; ii < serverEndpoints.Count; ii++)
                    {
                        EndpointDescription serverEndpoint = serverEndpoints[ii];
                        Uri actualUrl = Utils.ParseUri(serverEndpoint.EndpointUrl);

                        if (actualUrl != null && actualUrl.Scheme == expectedUrl.Scheme)
                        {
                            if (serverEndpoint.SecurityPolicyUri == m_endpoint.Description.SecurityPolicyUri)
                            {
                                if (serverEndpoint.SecurityMode == m_endpoint.Description.SecurityMode)
                                {
                                    // ensure endpoint has up to date information.
                                    m_endpoint.Description.Server.ApplicationName = serverEndpoint.Server.ApplicationName;
                                    m_endpoint.Description.Server.ApplicationUri = serverEndpoint.Server.ApplicationUri;
                                    m_endpoint.Description.Server.ApplicationType = serverEndpoint.Server.ApplicationType;
                                    m_endpoint.Description.Server.ProductUri = serverEndpoint.Server.ProductUri;
                                    m_endpoint.Description.TransportProfileUri = serverEndpoint.TransportProfileUri;
                                    m_endpoint.Description.UserIdentityTokens = serverEndpoint.UserIdentityTokens;

                                    found = true;
                                    break;
                                }
                            }
                        }
                    }
                }

                // could be a security risk.
                if (!found)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadSecurityChecksFailed,
                        "Server did not return an EndpointDescription that matched the one used to create the secure channel.");
                }

                // validate the server's signature.
                byte[] dataToSign = Utils.Append(clientCertificateData, clientNonce);

                if (!SecurityPolicies.Verify(serverCertificate, m_endpoint.Description.SecurityPolicyUri, dataToSign, serverSignature))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadApplicationSignatureInvalid,
                        "Server did not provide a correct signature for the nonce data provided by the client.");
                }

                // get a validator to check certificates provided by server.
                CertificateValidator validator = m_configuration.CertificateValidator;

                // validate software certificates.
                List<SoftwareCertificate> softwareCertificates = new List<SoftwareCertificate>();

                foreach (SignedSoftwareCertificate signedCertificate in serverSoftwareCertificates)
                {
                    SoftwareCertificate softwareCertificate = null;

                    ServiceResult result = SoftwareCertificate.Validate(
                        validator,
                        signedCertificate.CertificateData,
                        out softwareCertificate);

                    if (ServiceResult.IsBad(result))
                    {
                        OnSoftwareCertificateError(signedCertificate, result);
                    }

                    softwareCertificates.Add(softwareCertificate);
                }

                // check if software certificates meet application requirements.
                ValidateSoftwareCertificates(softwareCertificates);

                // create the client signature.
                dataToSign = Utils.Append(serverCertificateData, serverNonce);
                SignatureData clientSignature = SecurityPolicies.Sign(m_instanceCertificate, securityPolicyUri, dataToSign);

                // select the security policy for the user token.
                securityPolicyUri = identityPolicy.SecurityPolicyUri;

                if (String.IsNullOrEmpty(securityPolicyUri))
                {
                    securityPolicyUri = m_endpoint.Description.SecurityPolicyUri;
                }

                // sign data with user token.
                SignatureData userTokenSignature = identityToken.Sign(dataToSign, securityPolicyUri);

                // encrypt token.
                identityToken.Encrypt(serverCertificate, serverNonce, securityPolicyUri);

                // send the software certificates assigned to the client.
                SignedSoftwareCertificateCollection clientSoftwareCertificates = GetSoftwareCertificates();

                // copy the preferred locales if provided.
                if (preferredLocales != null && preferredLocales.Count > 0)
                {
                    m_preferredLocales = new StringCollection(preferredLocales);
                }

                StatusCodeCollection certificateResults = null;
                DiagnosticInfoCollection certificateDiagnosticInfos = null;

                // activate session.
                ActivateSession(
                    null,
                    clientSignature,
                    clientSoftwareCertificates,
                    m_preferredLocales,
                    new ExtensionObject(identityToken),
                    userTokenSignature,
                    out serverNonce,
                    out certificateResults,
                    out certificateDiagnosticInfos);

                // fetch namespaces.
                FetchNamespaceTables();

                lock (SyncRoot)
                {
                    // save nonces.
                    m_sessionName = sessionName;
                    m_identity = identity;
                    m_serverNonce = serverNonce;
                    m_serverCertificate = serverCertificate;

                    // update system context.
                    m_systemContext.PreferredLocales = m_preferredLocales;
                    m_systemContext.SessionId = this.SessionId;
                    m_systemContext.UserIdentity = identity;
                }

                // start keep alive thread.
                StartKeepAliveTimer();
            }
            catch
            {
                try
                {
                    CloseSession(null, false);
                    CloseChannel();
                }
                catch (Exception e)
                {
                    Utils.Trace("Cleanup: CloseSession() or CloseChannel() raised exception. " + e.Message);
                }
                finally
                {
                    SessionCreated(null, null);
                }

                throw;
            }
        }

        /// <summary>
        /// Updates the preferred locales used for the session.
        /// </summary>
        /// <param name="preferredLocales">The preferred locales.</param>
        public void ChangePreferredLocales(StringCollection preferredLocales)
        {
            UpdateSession(Identity, preferredLocales);
        }

        /// <summary>
        /// Updates the user identity and/or locales used for the session.
        /// </summary>
        /// <param name="identity">The user identity.</param>
        /// <param name="preferredLocales">The preferred locales.</param>
        public void UpdateSession(IUserIdentity identity, StringCollection preferredLocales)
        {
            byte[] serverNonce = null;

            lock (SyncRoot)
            {
                // check connection state.
                if (!Connected)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidState, "Not connected to server.");
                }

                // get current nonce.
                serverNonce = m_serverNonce;

                if (preferredLocales == null)
                {
                    preferredLocales = m_preferredLocales;
                }
            }

            // get the identity token.
            UserIdentityToken identityToken = null;
            SignatureData userTokenSignature = null;

            string securityPolicyUri = m_endpoint.Description.SecurityPolicyUri;

            // create the client signature.
            byte[] serverCertificateData = null;
            if (m_serverCertificate != null)
            {
                serverCertificateData = m_serverCertificate.RawData;
            }
            // create the client signature.
            byte[] dataToSign = Utils.Append(serverCertificateData, serverNonce);
            SignatureData clientSignature = SecurityPolicies.Sign(m_instanceCertificate, securityPolicyUri, dataToSign);

            // choose a default token.            
            if (identity == null)
            {
                identity = new UserIdentity();
            }

            // check that the user identity is supported by the endpoint.
            UserTokenPolicy identityPolicy = m_endpoint.Description.FindUserTokenPolicy(identity.TokenType, identity.IssuedTokenType);

            if (identityPolicy == null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadUserAccessDenied,
                    "Endpoint does not supported the user identity type provided.");
            }

            // select the security policy for the user token.
            securityPolicyUri = identityPolicy.SecurityPolicyUri;

            if (String.IsNullOrEmpty(securityPolicyUri))
            {
                securityPolicyUri = m_endpoint.Description.SecurityPolicyUri;
            }

            // sign data with user token.
            identityToken = identity.GetIdentityToken();
            identityToken.PolicyId = identityPolicy.PolicyId;
            userTokenSignature = identityToken.Sign(dataToSign, securityPolicyUri);

            // encrypt token.
            identityToken.Encrypt(m_serverCertificate, serverNonce, securityPolicyUri);

            // send the software certificates assigned to the client.
            SignedSoftwareCertificateCollection clientSoftwareCertificates = GetSoftwareCertificates();

            StatusCodeCollection certificateResults = null;
            DiagnosticInfoCollection certificateDiagnosticInfos = null;

            // activate session.
            ActivateSession(
                null,
                clientSignature,
                clientSoftwareCertificates,
                preferredLocales,
                new ExtensionObject(identityToken),
                userTokenSignature,
                out serverNonce,
                out certificateResults,
                out certificateDiagnosticInfos);

            // save nonce and new values.
            lock (SyncRoot)
            {
                if (identity != null)
                {
                    m_identity = identity;
                }

                m_serverNonce = serverNonce;
                m_preferredLocales = preferredLocales;

                // update system context.
                m_systemContext.PreferredLocales = m_preferredLocales;
                m_systemContext.SessionId = this.SessionId;
                m_systemContext.UserIdentity = identity;
            }
        }

        /// <summary>
        /// Finds the NodeIds for the components for an instance.
        /// </summary>
        public void FindComponentIds(
            NodeId instanceId,
            IList<string> componentPaths,
            out NodeIdCollection componentIds,
            out List<ServiceResult> errors)
        {
            componentIds = new NodeIdCollection();
            errors = new List<ServiceResult>();

            // build list of paths to translate.
            BrowsePathCollection pathsToTranslate = new BrowsePathCollection();

            for (int ii = 0; ii < componentPaths.Count; ii++)
            {
                BrowsePath pathToTranslate = new BrowsePath();

                pathToTranslate.StartingNode = instanceId;
                pathToTranslate.RelativePath = RelativePath.Parse(componentPaths[ii], TypeTree);

                pathsToTranslate.Add(pathToTranslate);
            }

            // translate the paths.
            BrowsePathResultCollection results = null;
            DiagnosticInfoCollection diagnosticInfos = null;

            ResponseHeader responseHeader = TranslateBrowsePathsToNodeIds(
                null,
                pathsToTranslate,
                out results,
                out diagnosticInfos);

            // verify that the server returned the correct number of results.
            ClientBase.ValidateResponse(results, pathsToTranslate);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, pathsToTranslate);

            for (int ii = 0; ii < componentPaths.Count; ii++)
            {
                componentIds.Add(NodeId.Null);
                errors.Add(ServiceResult.Good);

                // process any diagnostics associated with any error.
                if (StatusCode.IsBad(results[ii].StatusCode))
                {
                    errors[ii] = new ServiceResult(results[ii].StatusCode, ii, diagnosticInfos, responseHeader.StringTable);
                    continue;
                }

                // Expecting exact one NodeId for a local node.
                // Report an error if the server returns anything other than that.

                if (results[ii].Targets.Count == 0)
                {
                    errors[ii] = ServiceResult.Create(
                        StatusCodes.BadTargetNodeIdInvalid,
                        "Could not find target for path: {0}.",
                        componentPaths[ii]);

                    continue;
                }

                if (results[ii].Targets.Count != 1)
                {
                    errors[ii] = ServiceResult.Create(
                        StatusCodes.BadTooManyMatches,
                        "Too many matches found for path: {0}.",
                        componentPaths[ii]);

                    continue;
                }

                if (results[ii].Targets[0].RemainingPathIndex != UInt32.MaxValue)
                {
                    errors[ii] = ServiceResult.Create(
                        StatusCodes.BadTargetNodeIdInvalid,
                        "Cannot follow path to external server: {0}.",
                        componentPaths[ii]);

                    continue;
                }

                if (NodeId.IsNull(results[ii].Targets[0].TargetId))
                {
                    errors[ii] = ServiceResult.Create(
                        StatusCodes.BadUnexpectedError,
                        "Server returned a null NodeId for path: {0}.",
                        componentPaths[ii]);

                    continue;
                }

                if (results[ii].Targets[0].TargetId.IsAbsolute)
                {
                    errors[ii] = ServiceResult.Create(
                        StatusCodes.BadUnexpectedError,
                        "Server returned a remote node for path: {0}.",
                        componentPaths[ii]);

                    continue;
                }

                // suitable target found.
                componentIds[ii] = ExpandedNodeId.ToNodeId(results[ii].Targets[0].TargetId, m_namespaceUris);
            }
        }


        /// <summary>
        /// Reads the values for a set of variables.
        /// </summary>
        /// <param name="variableIds">The variable ids.</param>
        /// <param name="expectedTypes">The expected types.</param>
        /// <param name="values">The list of returned values.</param>
        /// <param name="errors">The list of returned errors.</param>
        public void ReadValues(
            IList<NodeId> variableIds,
            IList<Type> expectedTypes,
            out List<object> values,
            out List<ServiceResult> errors)
        {
            values = new List<object>();
            errors = new List<ServiceResult>();

            // build list of values to read.
            ReadValueIdCollection valuesToRead = new ReadValueIdCollection();

            for (int ii = 0; ii < variableIds.Count; ii++)
            {
                ReadValueId valueToRead = new ReadValueId();

                valueToRead.NodeId = variableIds[ii];
                valueToRead.AttributeId = Attributes.Value;
                valueToRead.IndexRange = null;
                valueToRead.DataEncoding = null;

                valuesToRead.Add(valueToRead);
            }

            // read the values.
            DataValueCollection results = null;
            DiagnosticInfoCollection diagnosticInfos = null;

            ResponseHeader responseHeader = Read(
                null,
                Int32.MaxValue,
                TimestampsToReturn.Both,
                valuesToRead,
                out results,
                out diagnosticInfos);

            // verify that the server returned the correct number of results.
            ClientBase.ValidateResponse(results, valuesToRead);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, valuesToRead);

            for (int ii = 0; ii < variableIds.Count; ii++)
            {
                values.Add(null);
                errors.Add(ServiceResult.Good);

                // process any diagnostics associated with bad or uncertain data.
                if (StatusCode.IsNotGood(results[ii].StatusCode))
                {
                    errors[ii] = new ServiceResult(results[ii].StatusCode, ii, diagnosticInfos, responseHeader.StringTable);
                    continue;
                }

                object value = results[ii].Value;

                // extract the body from extension objects.
                ExtensionObject extension = value as ExtensionObject;

                if (extension != null && extension.Body is IEncodeable)
                {
                    value = extension.Body;
                }

                // check expected type.
                if (expectedTypes[ii] != null && !expectedTypes[ii].IsInstanceOfType(value))
                {
                    errors[ii] = ServiceResult.Create(
                        StatusCodes.BadTypeMismatch,
                        "Value {0} does not have expected type: {1}.",
                        value,
                        expectedTypes[ii].Name);

                    continue;
                }

                // suitable value found.
                values[ii] = value;
            }
        }


        /// <summary>
        /// Reads the display name for a set of Nodes.
        /// </summary>
        public void ReadDisplayName(
            IList<NodeId> nodeIds,
            out List<string> displayNames,
            out List<ServiceResult> errors)
        {
            displayNames = new List<string>();
            errors = new List<ServiceResult>();

            // build list of values to read.
            ReadValueIdCollection valuesToRead = new ReadValueIdCollection();

            for (int ii = 0; ii < nodeIds.Count; ii++)
            {
                ReadValueId valueToRead = new ReadValueId();

                valueToRead.NodeId = nodeIds[ii];
                valueToRead.AttributeId = Attributes.DisplayName;
                valueToRead.IndexRange = null;
                valueToRead.DataEncoding = null;

                valuesToRead.Add(valueToRead);
            }

            // read the values.
            DataValueCollection results = null;
            DiagnosticInfoCollection diagnosticInfos = null;

            ResponseHeader responseHeader = Read(
                null,
                Int32.MaxValue,
                TimestampsToReturn.Both,
                valuesToRead,
                out results,
                out diagnosticInfos);

            // verify that the server returned the correct number of results.
            ClientBase.ValidateResponse(results, valuesToRead);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, valuesToRead);

            for (int ii = 0; ii < nodeIds.Count; ii++)
            {
                displayNames.Add(String.Empty);
                errors.Add(ServiceResult.Good);

                // process any diagnostics associated with bad or uncertain data.
                if (StatusCode.IsNotGood(results[ii].StatusCode))
                {
                    errors[ii] = new ServiceResult(results[ii].StatusCode, ii, diagnosticInfos, responseHeader.StringTable);
                    continue;
                }

                // extract the name.
                LocalizedText displayName = results[ii].GetValue<LocalizedText>(null);

                if (!LocalizedText.IsNullOrEmpty(displayName))
                {
                    displayNames[ii] = displayName.Text;
                }
            }
        }

        /// <summary>
        /// Disconnects from the server and frees any network resources.
        /// </summary>
        public override StatusCode Close()
        {
            return Close(m_keepAliveInterval);
        }

        /// <summary>
        /// Disconnects from the server and frees any network resources with the specified timeout.
        /// </summary>
        public virtual StatusCode Close(int timeout)
        {
            // check if already called.
            if (Disposed)
            {
                return StatusCodes.Good;
            }

            StatusCode result = StatusCodes.Good;

            // stop the keep alive timer.
            if (m_keepAliveTimer != null)
            {
                m_keepAliveTimer.Dispose();
                m_keepAliveTimer = null;
            }

            // check if currectly connected.
            bool connected = Connected;

            // halt all background threads.
            if (connected)
            {
                if (m_SessionClosing != null)
                {
                    try
                    {
                        m_SessionClosing(this, null);
                    }
                    catch (Exception e)
                    {
                        Utils.Trace(e, "Session: Unexpected eror raising SessionClosing event.");
                    }
                }
            }

            // close the session with the server.
            if (connected && !KeepAliveStopped)
            {
                int existingTimeout = this.OperationTimeout;

                try
                {
                    // close the session and delete all subscriptions.
                    this.OperationTimeout = timeout;
                    CloseSession(null, true);
                    this.OperationTimeout = existingTimeout;

                    CloseChannel();

                    // raised notification indicating the session is closed.
                    SessionCreated(null, null);
                }
                catch (Exception e)
                {
                    // dont throw errors on disconnect, but return them
                    // so the caller can log the error.
                    if (e is ServiceResultException)
                    {
                        result = ((ServiceResultException)e).StatusCode;
                    }
                    else
                    {
                        result = StatusCodes.Bad;
                    }
                }
            }

            // clean up.
            Dispose();
            return result;
        }

        /// <summary>
        /// Adds a subscription to the session.
        /// </summary>
        /// <param name="subscription">The subscription to add.</param>
        /// <returns></returns>
        public bool AddSubscription(Subscription subscription)
        {
            if (subscription == null) throw new ArgumentNullException("subscription");

            lock (SyncRoot)
            {
                if (m_subscriptions.Contains(subscription))
                {
                    return false;
                }

                subscription.Session = this;
                m_subscriptions.Add(subscription);
            }

            if (m_SubscriptionsChanged != null)
            {
                m_SubscriptionsChanged(this, null);
            }

            return true;
        }

        /// <summary>
        /// Removes a subscription from the session.
        /// </summary>
        /// <param name="subscription">The subscription to remove.</param>
        /// <returns></returns>
        public bool RemoveSubscription(Subscription subscription)
        {
            if (subscription == null) throw new ArgumentNullException("subscription");

            if (subscription.Created)
            {
                subscription.Delete(true);
            }

            lock (SyncRoot)
            {
                if (!m_subscriptions.Remove(subscription))
                {
                    return false;
                }

                subscription.Session = null;
            }

            if (m_SubscriptionsChanged != null)
            {
                m_SubscriptionsChanged(this, null);
            }

            return true;
        }

        /// <summary>
        /// Removes a list of subscriptions from the sessiont.
        /// </summary>
        /// <param name="subscriptions">The list of subscriptions to remove.</param>
        /// <returns></returns>
        public bool RemoveSubscriptions(IEnumerable<Subscription> subscriptions)
        {
            if (subscriptions == null) throw new ArgumentNullException("subscriptions");

            bool removed = false;
            List<Subscription> subscriptionsToDelete = new List<Subscription>();

            lock (SyncRoot)
            {
                foreach (Subscription subscription in subscriptions)
                {
                    if (m_subscriptions.Remove(subscription))
                    {
                        if (subscription.Created)
                        {
                            subscriptionsToDelete.Add(subscription);
                        }

                        removed = true;
                    }
                }
            }

            foreach (Subscription subscription in subscriptionsToDelete)
            {
                subscription.Delete(true);
            }

            if (removed)
            {
                if (m_SubscriptionsChanged != null)
                {
                    m_SubscriptionsChanged(this, null);
                }
            }

            return true;
        }

        #region Browse Methods
        /// <summary>
        /// Invokes the Browse service.
        /// </summary>
        /// <param name="requestHeader">The request header.</param>
        /// <param name="view">The view to browse.</param>
        /// <param name="nodeToBrowse">The node to browse.</param>
        /// <param name="maxResultsToReturn">The maximum number of returned values.</param>
        /// <param name="browseDirection">The browse direction.</param>
        /// <param name="referenceTypeId">The reference type id.</param>
        /// <param name="includeSubtypes">If set to <c>true</c> the subtypes of the ReferenceType will be included in the browse.</param>
        /// <param name="nodeClassMask">The node class mask.</param>
        /// <param name="continuationPoint">The continuation point.</param>
        /// <param name="references">The list of node references.</param>
        /// <returns></returns>
        public virtual ResponseHeader Browse(
            RequestHeader requestHeader,
            ViewDescription view,
            NodeId nodeToBrowse,
            uint maxResultsToReturn,
            BrowseDirection browseDirection,
            NodeId referenceTypeId,
            bool includeSubtypes,
            uint nodeClassMask,
            out byte[] continuationPoint,
            out ReferenceDescriptionCollection references)
        {
            BrowseDescription description = new BrowseDescription();

            description.NodeId = nodeToBrowse;
            description.BrowseDirection = browseDirection;
            description.ReferenceTypeId = referenceTypeId;
            description.IncludeSubtypes = includeSubtypes;
            description.NodeClassMask = nodeClassMask;
            description.ResultMask = (uint)BrowseResultMask.All;

            BrowseDescriptionCollection nodesToBrowse = new BrowseDescriptionCollection();
            nodesToBrowse.Add(description);

            BrowseResultCollection results;
            DiagnosticInfoCollection diagnosticInfos;

            ResponseHeader responseHeader = Browse(
                requestHeader,
                view,
                maxResultsToReturn,
                nodesToBrowse,
                out results,
                out diagnosticInfos);

            ClientBase.ValidateResponse(results, nodesToBrowse);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, nodesToBrowse);

            if (StatusCode.IsBad(results[0].StatusCode))
            {
                throw new ServiceResultException(new ServiceResult(results[0].StatusCode, 0, diagnosticInfos, responseHeader.StringTable));
            }

            continuationPoint = results[0].ContinuationPoint;
            references = results[0].References;

            return responseHeader;
        }

        /// <summary>
        /// Begins an asynchronous invocation of the Browse service.
        /// </summary>
        /// <param name="requestHeader">The request header.</param>
        /// <param name="view">The view to browse.</param>
        /// <param name="nodeToBrowse">The node to browse.</param>
        /// <param name="maxResultsToReturn">The maximum number of returned values..</param>
        /// <param name="browseDirection">The browse direction.</param>
        /// <param name="referenceTypeId">The reference type id.</param>
        /// <param name="includeSubtypes">If set to <c>true</c> the subtypes of the ReferenceType will be included in the browse.</param>
        /// <param name="nodeClassMask">The node class mask.</param>
        /// <param name="callback">The callback.</param>
        /// <param name="asyncState"></param>
        /// <returns></returns>
        public IAsyncResult BeginBrowse(
            RequestHeader requestHeader,
            ViewDescription view,
            NodeId nodeToBrowse,
            uint maxResultsToReturn,
            BrowseDirection browseDirection,
            NodeId referenceTypeId,
            bool includeSubtypes,
            uint nodeClassMask,
            AsyncCallback callback,
            object asyncState)
        {
            BrowseDescription description = new BrowseDescription();

            description.NodeId = nodeToBrowse;
            description.BrowseDirection = browseDirection;
            description.ReferenceTypeId = referenceTypeId;
            description.IncludeSubtypes = includeSubtypes;
            description.NodeClassMask = nodeClassMask;
            description.ResultMask = (uint)BrowseResultMask.All;

            BrowseDescriptionCollection nodesToBrowse = new BrowseDescriptionCollection();
            nodesToBrowse.Add(description);

            return BeginBrowse(
                requestHeader,
                view,
                maxResultsToReturn,
                nodesToBrowse,
                callback,
                asyncState);
        }

        /// <summary>
        /// Finishes an asynchronous invocation of the Browse service.
        /// </summary>
        /// <param name="result">The result.</param>
        /// <param name="continuationPoint">The continuation point.</param>
        /// <param name="references">The list of node references.</param>
        /// <returns></returns>
        public ResponseHeader EndBrowse(
            IAsyncResult result,
            out byte[] continuationPoint,
            out ReferenceDescriptionCollection references)
        {
            BrowseResultCollection results;
            DiagnosticInfoCollection diagnosticInfos;

            ResponseHeader responseHeader = EndBrowse(
                result,
                out results,
                out diagnosticInfos);

            if (results == null || results.Count != 1)
            {
                throw new ServiceResultException(StatusCodes.BadUnknownResponse);
            }

            if (StatusCode.IsBad(results[0].StatusCode))
            {
                throw new ServiceResultException(new ServiceResult(results[0].StatusCode, 0, diagnosticInfos, responseHeader.StringTable));
            }

            continuationPoint = results[0].ContinuationPoint;
            references = results[0].References;

            return responseHeader;
        }
        #endregion

        #region BrowseNext Methods
        /// <summary>
        /// Invokes the BrowseNext service.
        /// </summary>
        public virtual ResponseHeader BrowseNext(
            RequestHeader requestHeader,
            bool releaseContinuationPoint,
            byte[] continuationPoint,
            out byte[] revisedContinuationPoint,
            out ReferenceDescriptionCollection references)
        {
            ByteStringCollection continuationPoints = new ByteStringCollection();
            continuationPoints.Add(continuationPoint);

            BrowseResultCollection results;
            DiagnosticInfoCollection diagnosticInfos;

            ResponseHeader responseHeader = BrowseNext(
                requestHeader,
                releaseContinuationPoint,
                continuationPoints,
                out results,
                out diagnosticInfos);

            ClientBase.ValidateResponse(results, continuationPoints);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, continuationPoints);

            if (StatusCode.IsBad(results[0].StatusCode))
            {
                throw new ServiceResultException(new ServiceResult(results[0].StatusCode, 0, diagnosticInfos, responseHeader.StringTable));
            }

            revisedContinuationPoint = results[0].ContinuationPoint;
            references = results[0].References;

            return responseHeader;
        }

        /// <summary>
        /// Begins an asynchronous invocation of the BrowseNext service.
        /// </summary>
        public IAsyncResult BeginBrowseNext(
            RequestHeader requestHeader,
            bool releaseContinuationPoint,
            byte[] continuationPoint,
            AsyncCallback callback,
            object asyncState)
        {
            ByteStringCollection continuationPoints = new ByteStringCollection();
            continuationPoints.Add(continuationPoint);

            return BeginBrowseNext(
                requestHeader,
                releaseContinuationPoint,
                continuationPoints,
                callback,
                asyncState);
        }

        /// <summary>
        /// Finishes an asynchronous invocation of the BrowseNext service.
        /// </summary>
        public ResponseHeader EndBrowseNext(
            IAsyncResult result,
            out byte[] revisedContinuationPoint,
            out ReferenceDescriptionCollection references)
        {
            BrowseResultCollection results;
            DiagnosticInfoCollection diagnosticInfos;

            ResponseHeader responseHeader = EndBrowseNext(
                result,
                out results,
                out diagnosticInfos);

            if (results == null || results.Count != 1)
            {
                throw new ServiceResultException(StatusCodes.BadUnknownResponse);
            }

            if (StatusCode.IsBad(results[0].StatusCode))
            {
                throw new ServiceResultException(new ServiceResult(results[0].StatusCode, 0, diagnosticInfos, responseHeader.StringTable));
            }

            revisedContinuationPoint = results[0].ContinuationPoint;
            references = results[0].References;

            return responseHeader;
        }
        #endregion

        /// <summary>
        /// Calls the specified method and returns the output arguments.
        /// </summary>
        /// <param name="objectId">The NodeId of the object that provides the method.</param>
        /// <param name="methodId">The NodeId of the method to call.</param>
        /// <param name="args">The input arguments.</param>
        /// <returns>The list of output argument values.</returns>
        public IList<object> Call(NodeId objectId, NodeId methodId, params object[] args)
        {
            VariantCollection inputArguments = new VariantCollection();

            if (args != null)
            {
                for (int ii = 0; ii < args.Length; ii++)
                {
                    inputArguments.Add(new Variant(args[ii]));
                }
            }

            CallMethodRequest request = new CallMethodRequest();

            request.ObjectId = objectId;
            request.MethodId = methodId;
            request.InputArguments = inputArguments;

            CallMethodRequestCollection requests = new CallMethodRequestCollection();
            requests.Add(request);

            CallMethodResultCollection results;
            DiagnosticInfoCollection diagnosticInfos;

            ResponseHeader responseHeader = Call(
                null,
                requests,
                out results,
                out diagnosticInfos);

            ClientBase.ValidateResponse(results, requests);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, requests);

            if (StatusCode.IsBad(results[0].StatusCode))
            {
                throw ServiceResultException.Create(results[0].StatusCode, 0, diagnosticInfos, responseHeader.StringTable);
            }

            List<object> outputArguments = new List<object>();

            foreach (Variant arg in results[0].OutputArguments)
            {
                outputArguments.Add(arg.Value);
            }

            return outputArguments;
        }
        #endregion

        #region Protected Methods
        /// <summary>
        /// Returns the software certificates assigned to the application.
        /// </summary>
        protected virtual SignedSoftwareCertificateCollection GetSoftwareCertificates()
        {
            return new SignedSoftwareCertificateCollection();
        }

        /// <summary>
        /// Handles an error when validating the application instance certificate provided by the server.
        /// </summary>
        protected virtual void OnApplicationCertificateError(byte[] serverCertificate, ServiceResult result)
        {
            throw new ServiceResultException(result);
        }

        /// <summary>
        /// Handles an error when validating software certificates provided by the server.
        /// </summary>
        protected virtual void OnSoftwareCertificateError(SignedSoftwareCertificate signedCertificate, ServiceResult result)
        {
            throw new ServiceResultException(result);
        }

        /// <summary>
        /// Inspects the software certificates provided by the server. 
        /// </summary>
        protected virtual void ValidateSoftwareCertificates(List<SoftwareCertificate> softwareCertificates)
        {
            // always accept valid certificates.
        }

        /// <summary>
        /// Starts a timer to check that the connection to the server is still available.
        /// </summary>
        private void StartKeepAliveTimer()
        {
            int keepAliveInterval = m_keepAliveInterval;

            lock (m_eventLock)
            {
                m_serverState = ServerState.Unknown;
                m_lastKeepAliveTime = DateTime.UtcNow;
            }

            ReadValueIdCollection nodesToRead = new ReadValueIdCollection();

            // read the server state.
            ReadValueId serverState = new ReadValueId();

            serverState.NodeId = Variables.Server_ServerStatus_State;
            serverState.AttributeId = Attributes.Value;
            serverState.DataEncoding = null;
            serverState.IndexRange = null;

            nodesToRead.Add(serverState);

            // restart the publish timer.
            lock (SyncRoot)
            {
                if (m_keepAliveTimer != null)
                {
                    m_keepAliveTimer.Dispose();
                    m_keepAliveTimer = null;
                }

                // start timer.
                m_keepAliveTimer = new Timer(OnKeepAlive, nodesToRead, keepAliveInterval, keepAliveInterval);
            }

            // send initial keep alive.
            OnKeepAlive(nodesToRead);
        }

        /// <summary>
        /// Removes a completed async request.
        /// </summary>
        private AsyncRequestState RemoveRequest(IAsyncResult result, uint requestId, uint typeId)
        {
            lock (m_outstandingRequests)
            {
                for (LinkedListNode<AsyncRequestState> ii = m_outstandingRequests.First; ii != null; ii = ii.Next)
                {
                    if (Object.ReferenceEquals(result, ii.Value.Result) || (requestId == ii.Value.RequestId && typeId == ii.Value.RequestTypeId))
                    {
                        AsyncRequestState state = ii.Value;
                        m_outstandingRequests.Remove(ii);
                        return state;
                    }
                }

                return null;
            }
        }

        /// <summary>
        /// Adds a new async request.
        /// </summary>
        private void AsyncRequestStarted(IAsyncResult result, uint requestId, uint typeId)
        {
            lock (m_outstandingRequests)
            {
                // check if the request completed asynchronously.
                AsyncRequestState state = RemoveRequest(result, requestId, typeId);

                // add a new request.
                if (state == null)
                {
                    state = new AsyncRequestState();

                    state.Defunct = false;
                    state.RequestId = requestId;
                    state.RequestTypeId = typeId;
                    state.Result = result;
                    state.Timestamp = DateTime.UtcNow;

                    m_outstandingRequests.AddLast(state);
                }
            }
        }

        /// <summary>
        /// Removes a completed async request.
        /// </summary>
        private void AsyncRequestCompleted(IAsyncResult result, uint requestId, uint typeId)
        {
            lock (m_outstandingRequests)
            {
                // remove the request.
                AsyncRequestState state = RemoveRequest(result, requestId, typeId);

                if (state != null)
                {
                    // mark any old requests as default (i.e. the should have returned before this request).
                    DateTime maxAge = state.Timestamp.AddSeconds(-1);

                    for (LinkedListNode<AsyncRequestState> ii = m_outstandingRequests.First; ii != null; ii = ii.Next)
                    {
                        if (ii.Value.RequestTypeId == typeId && ii.Value.Timestamp < maxAge)
                        {
                            ii.Value.Defunct = true;
                        }
                    }
                }

                // add a dummy placeholder since the begin request has not completed yet.
                if (state == null)
                {
                    state = new AsyncRequestState();

                    state.Defunct = true;
                    state.RequestId = requestId;
                    state.RequestTypeId = typeId;
                    state.Result = result;
                    state.Timestamp = DateTime.UtcNow;

                    m_outstandingRequests.AddLast(state);
                }
            }
        }

        /// <summary>
        /// Sends a keep alive by reading from the server.
        /// </summary>
        private void OnKeepAlive(object state)
        {
            ReadValueIdCollection nodesToRead = (ReadValueIdCollection)state;

            try
            {
                // check if session has been closed.
                if (!Connected || m_keepAliveTimer == null)
                {
                    return;
                }

                // raise error if keep alives are not coming back.
                if (KeepAliveStopped)
                {
                    if (!OnKeepAliveError(ServiceResult.Create(StatusCodes.BadNoCommunication, "Server not responding to keep alive requests.")))
                    {
                        return;
                    }
                }

                // limit the number of keep alives sent.
                if (OutstandingRequestCount > SubscriptionCount + 10)
                {
                    return;
                }

                RequestHeader requestHeader = new RequestHeader();

                requestHeader.RequestHandle = Utils.IncrementIdentifier(ref m_keepAliveCounter);
                requestHeader.TimeoutHint = (uint)(KeepAliveInterval * 2);
                requestHeader.ReturnDiagnostics = 0;

                IAsyncResult result = BeginRead(
                    requestHeader,
                    0,
                    TimestampsToReturn.Neither,
                    nodesToRead,
                    OnKeepAliveComplete,
                    nodesToRead);

                AsyncRequestStarted(result, requestHeader.RequestHandle, DataTypes.ReadRequest);
            }
            catch (Exception e)
            {
                Utils.Trace("Could not send keep alive request: {1} {0}", e.Message, e.GetType().FullName);
            }
        }

        /// <summary>
        /// Checks if a notification has arrived. Sends a publish if it has not.
        /// </summary>
        private void OnKeepAliveComplete(IAsyncResult result)
        {
            ReadValueIdCollection nodesToRead = (ReadValueIdCollection)result.AsyncState;

            AsyncRequestCompleted(result, 0, DataTypes.ReadRequest);

            try
            {
                // read the server status.
                DataValueCollection values = new DataValueCollection();
                DiagnosticInfoCollection diagnosticInfos = new DiagnosticInfoCollection();

                ResponseHeader responseHeader = EndRead(
                    result,
                    out values,
                    out diagnosticInfos);

                ValidateResponse(values, nodesToRead);
                ValidateDiagnosticInfos(diagnosticInfos, nodesToRead);

                // validate value returned.
                ServiceResult error = ValidateDataValue(values[0], typeof(int), 0, diagnosticInfos, responseHeader);

                if (ServiceResult.IsBad(error))
                {
                    throw new ServiceResultException(error);
                }

                // send notification that keep alive completed.
                OnKeepAlive((ServerState)(int)values[0].Value, responseHeader.Timestamp);
            }
            catch (Exception e)
            {
                Utils.Trace("Unexpected keep alive error occurred: {0}", e.Message);
            }
        }

        /// <summary>
        /// Called when the server returns a keep alive response.
        /// </summary>
        protected virtual void OnKeepAlive(ServerState currentState, DateTime currentTime)
        {
            // restart publishing if keep alives recovered.
            if (KeepAliveStopped)
            {
                // ignore if already reconnecting.
                if (m_reconnecting)
                {
                    return;
                }

                int count = 0;

                lock (m_outstandingRequests)
                {
                    for (LinkedListNode<AsyncRequestState> ii = m_outstandingRequests.First; ii != null; ii = ii.Next)
                    {
                        if (ii.Value.RequestTypeId == DataTypes.PublishRequest)
                        {
                            ii.Value.Defunct = true;
                        }
                    }
                }

                lock (SyncRoot)
                {
                    count = m_subscriptions.Count;
                }

                while (count-- > 0)
                {
                    BeginPublish(OperationTimeout);
                }
            }

            KeepAliveEventHandler callback = null;

            lock (m_eventLock)
            {
                callback = m_KeepAlive;

                // save server state.
                m_serverState = currentState;
                m_lastKeepAliveTime = DateTime.UtcNow;
            }

            if (callback != null)
            {
                try
                {
                    callback(this, new KeepAliveEventArgs(null, currentState, currentTime));
                }
                catch (Exception e)
                {
                    Utils.Trace(e, "Session: Unexpected error invoking KeepAliveCallback.");
                }
            }
        }

        /// <summary>
        /// Called when a error occurs during a keep alive.
        /// </summary>
        protected virtual bool OnKeepAliveError(ServiceResult result)
        {
            long delta = 0;

            lock (m_eventLock)
            {
                delta = DateTime.UtcNow.Ticks - m_lastKeepAliveTime.Ticks;
            }

            Utils.Trace(
                "KEEP ALIVE LATE: {0}s, EndpointUrl={1}, RequestCount={3}/{2}",
                ((double)delta) / TimeSpan.TicksPerSecond,
                this.Endpoint.EndpointUrl,
                this.OutstandingRequestCount,
                this.GoodPublishRequestCount);

            KeepAliveEventHandler callback = null;

            lock (m_eventLock)
            {
                callback = m_KeepAlive;
            }

            if (callback != null)
            {
                try
                {
                    KeepAliveEventArgs args = new KeepAliveEventArgs(result, ServerState.Unknown, DateTime.UtcNow);
                    callback(this, args);
                    return !args.CancelKeepAlive;
                }
                catch (Exception e)
                {
                    Utils.Trace(e, "Session: Unexpected error invoking KeepAliveCallback.");
                }
            }

            return true;
        }
        #endregion

        #region Publish Methods
        /// <summary>
        /// Sends an additional publish request.
        /// </summary>
        public IAsyncResult BeginPublish(int timeout)
        {
            // do not publish if reconnecting.
            if (m_reconnecting)
            {
                Utils.Trace("Published skipped due to reconnect");
                return null;
            }

            SubscriptionAcknowledgementCollection acknowledgementsToSend = null;

            // collect the current set if acknowledgements.
            lock (SyncRoot)
            {
                acknowledgementsToSend = m_acknowledgementsToSend;
                m_acknowledgementsToSend = new SubscriptionAcknowledgementCollection();
            }

            // send publish request.
            RequestHeader requestHeader = new RequestHeader();

            // ensure the publish request is discarded before the timeout occurs to ensure the channel is dropped.
            requestHeader.TimeoutHint = (uint)OperationTimeout / 2;
            requestHeader.ReturnDiagnostics = (uint)(int)ReturnDiagnostics;
            requestHeader.RequestHandle = Utils.IncrementIdentifier(ref m_publishCounter);

            AsyncRequestState state = new AsyncRequestState();

            state.RequestTypeId = DataTypes.PublishRequest;
            state.RequestId = requestHeader.RequestHandle;
            state.Timestamp = DateTime.UtcNow;

            try
            {
                IAsyncResult result = BeginPublish(
                    requestHeader,
                    acknowledgementsToSend,
                    OnPublishComplete,
                    new object[] { SessionId, acknowledgementsToSend, requestHeader });

                AsyncRequestStarted(result, requestHeader.RequestHandle, DataTypes.PublishRequest);

                Utils.Trace("PUBLISH #{0} SENT", requestHeader.RequestHandle);

                return result;
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error sending publish request.");
                return null;
            }
        }

        /// <summary>
        /// Completes an asynchronous publish operation.
        /// </summary>
        private void OnPublishComplete(IAsyncResult result)
        {
            // extract state information.
            object[] state = (object[])result.AsyncState;
            NodeId sessionId = (NodeId)state[0];
            SubscriptionAcknowledgementCollection acknowledgementsToSend = (SubscriptionAcknowledgementCollection)state[1];
            RequestHeader requestHeader = (RequestHeader)state[2];
            bool moreNotifications;

            AsyncRequestCompleted(result, requestHeader.RequestHandle, DataTypes.PublishRequest);

            try
            {
                Utils.Trace("PUBLISH #{0} RECEIVED", requestHeader.RequestHandle);

                // complete publish.
                uint subscriptionId;
                UInt32Collection availableSequenceNumbers;
                NotificationMessage notificationMessage;
                StatusCodeCollection acknowledgeResults;
                DiagnosticInfoCollection acknowledgeDiagnosticInfos;

                ResponseHeader responseHeader = EndPublish(
                    result,
                    out subscriptionId,
                    out availableSequenceNumbers,
                    out moreNotifications,
                    out notificationMessage,
                    out acknowledgeResults,
                    out acknowledgeDiagnosticInfos);

                // nothing more to do if session changed.
                if (sessionId != SessionId)
                {
                    Utils.Trace("Publish response discarded because session id changed: Old {0} != New {1}", sessionId, SessionId);
                    return;
                }

                Utils.Trace("NOTIFICATION RECEIVED: SubId={0}, SeqNo={1}", subscriptionId, notificationMessage.SequenceNumber);

                // process response.
                ProcessPublishResponse(
                    responseHeader,
                    subscriptionId,
                    availableSequenceNumbers,
                    moreNotifications,
                    notificationMessage);

                // nothing more to do if reconnecting.
                if (m_reconnecting)
                {
                    Utils.Trace("No new publish sent because of reconnect in progress.");
                    return;
                }
            }
            catch (Exception e)
            {
                Utils.Trace("Publish #{0}, Reconnecting={2}, Error: {1}", requestHeader.RequestHandle, e.Message, m_reconnecting);

                moreNotifications = false;

                // ignore errors if reconnecting.
                if (m_reconnecting)
                {
                    Utils.Trace("Publish abandoned after error due to reconnect: {0}", e.Message);
                    return;
                }

                // nothing more to do if session changed.
                if (sessionId != SessionId)
                {
                    Utils.Trace("Publish abandoned after error because session id changed: Old {0} != New {1}", sessionId, SessionId);
                    return;
                }

                // try to acknowlege the notifications again in the next publish.
                if (acknowledgementsToSend != null)
                {
                    lock (SyncRoot)
                    {
                        m_acknowledgementsToSend.AddRange(acknowledgementsToSend);
                    }
                }

                // raise an error event.     
                ServiceResult error = new ServiceResult(e);

                if (error.Code != StatusCodes.BadNoSubscription)
                {
                    PublishErrorEventHandler callback = null;

                    lock (m_eventLock)
                    {
                        callback = m_PublishError;
                    }

                    if (callback != null)
                    {
                        try
                        {
                            callback(this, new PublishErrorEventArgs(error));
                        }
                        catch (Exception e2)
                        {
                            Utils.Trace(e2, "Session: Unexpected error invoking PublishErrorCallback.");
                        }
                    }
                }

                // don't send another publish for these errors.
                switch (error.Code)
                {
                    case StatusCodes.BadNoSubscription:
                    case StatusCodes.BadSessionClosed:
                    case StatusCodes.BadTooManyPublishRequests:
                    case StatusCodes.BadServerHalted:
                        {
                            return;
                        }
                }

                Utils.Trace(e, "PUBLISH #{0} - Unhandled error during Publish.", requestHeader.RequestHandle);
            }

            int requestCount = GoodPublishRequestCount;

            if (requestCount < m_subscriptions.Count)
            {
                BeginPublish(OperationTimeout);
            }
            else
            {
                Utils.Trace("PUBLISH - Did not send another publish request. GoodPublishRequestCount={0}, Subscriptions={1}", requestCount, m_subscriptions.Count);
            }
        }

        /// <summary>
        /// Sends a republish request.
        /// </summary>
        public bool Republish(uint subscriptionId, uint sequenceNumber)
        {
            // send publish request.
            RequestHeader requestHeader = new RequestHeader();

            requestHeader.TimeoutHint = (uint)OperationTimeout;
            requestHeader.ReturnDiagnostics = (uint)(int)ReturnDiagnostics;
            requestHeader.RequestHandle = Utils.IncrementIdentifier(ref m_publishCounter);

            try
            {
                Utils.Trace("Requesting Republish for {0}-{1}", subscriptionId, sequenceNumber);

                // request republish.
                NotificationMessage notificationMessage = null;

                ResponseHeader responseHeader = Republish(
                    requestHeader,
                    subscriptionId,
                    sequenceNumber,
                    out notificationMessage);

                Utils.Trace("Received Republish for {0}-{1}", subscriptionId, sequenceNumber);

                // process response.
                ProcessPublishResponse(
                    responseHeader,
                    subscriptionId,
                    null,
                    false,
                    notificationMessage);

                return true;
            }
            catch (Exception e)
            {
                ServiceResult error = new ServiceResult(e);

                bool result = (error.StatusCode == StatusCodes.BadMessageNotAvailable);

                if (result)
                {
                    Utils.Trace("Message {0}-{1} no longer available.", subscriptionId, sequenceNumber);
                }
                else
                {
                    Utils.Trace(e, "Unexpected error sending republish request.");
                }

                PublishErrorEventHandler callback = null;

                lock (m_eventLock)
                {
                    callback = m_PublishError;
                }

                // raise an error event.
                if (callback != null)
                {
                    try
                    {
                        PublishErrorEventArgs args = new PublishErrorEventArgs(
                            error,
                            subscriptionId,
                            sequenceNumber);

                        callback(this, args);
                    }
                    catch (Exception e2)
                    {
                        Utils.Trace(e2, "Session: Unexpected error invoking PublishErrorCallback.");
                    }
                }

                return result;
            }
        }

        /// <summary>
        /// Processes the response from a publish request.
        /// </summary>
        private void ProcessPublishResponse(
            ResponseHeader responseHeader,
            uint subscriptionId,
            UInt32Collection availableSequenceNumbers,
            bool moreNotifications,
            NotificationMessage notificationMessage)
        {
            Subscription subscription = null;

            // send notification that the server is alive.
            OnKeepAlive(m_serverState, responseHeader.Timestamp);

            // collect the current set if acknowledgements.
            lock (SyncRoot)
            {
                // clear out acknowledgements for messages that the server does not have any more.
                SubscriptionAcknowledgementCollection acknowledgementsToSend = new SubscriptionAcknowledgementCollection();

                for (int ii = 0; ii < m_acknowledgementsToSend.Count; ii++)
                {
                    SubscriptionAcknowledgement acknowledgement = m_acknowledgementsToSend[ii];

                    if (acknowledgement.SubscriptionId != subscriptionId)
                    {
                        acknowledgementsToSend.Add(acknowledgement);
                    }
                    else
                    {
                        if (availableSequenceNumbers == null || availableSequenceNumbers.Contains(acknowledgement.SequenceNumber))
                        {
                            acknowledgementsToSend.Add(acknowledgement);
                        }
                    }
                }

                // create an acknowledgement to be sent back to the server.
                if (notificationMessage.NotificationData.Count > 0)
                {
                    SubscriptionAcknowledgement acknowledgement = new SubscriptionAcknowledgement();

                    acknowledgement.SubscriptionId = subscriptionId;
                    acknowledgement.SequenceNumber = notificationMessage.SequenceNumber;

                    acknowledgementsToSend.Add(acknowledgement);
                }

                m_acknowledgementsToSend = acknowledgementsToSend;

                // find the subscription.
                foreach (Subscription current in m_subscriptions)
                {
                    if (current.Id == subscriptionId)
                    {
                        subscription = current;
                        break;
                    }
                }
            }

            // ignore messages with a subscription that has been deleted.
            if (subscription != null)
            {
                // update subscription cache.                                 
                subscription.SaveMessageInCache(
                    availableSequenceNumbers,
                    notificationMessage,
                    responseHeader.StringTable);

                // raise the notification.
                lock (m_eventLock)
                {
                    NotificationEventArgs args = new NotificationEventArgs(subscription, notificationMessage, responseHeader.StringTable);

                    if (m_Publish != null)
                    {
                        Task.Run(() =>
                        {
                            OnRaisePublishNotification(args);
                        });
                    }
                }
            }
        }

        /// <summary>
        /// Raises an event indicating that publish has returned a notification.
        /// </summary>
        private void OnRaisePublishNotification(object state)
        {
            try
            {
                NotificationEventArgs args = (NotificationEventArgs)state;
                NotificationEventHandler callback = m_Publish;

                if (callback != null && args.Subscription.Id != 0)
                {
                    callback(this, args);
                }
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Session: Unexpected rrror while raising Notification event.");
            }
        }
        #endregion

        #region Private Fields
        private SubscriptionAcknowledgementCollection m_acknowledgementsToSend;
        private List<Subscription> m_subscriptions;
        private Dictionary<NodeId, DataDictionary> m_dictionaries;
        private Subscription m_defaultSubscription;
        private double m_sessionTimeout;
        private uint m_maxRequestMessageSize;
        private StringCollection m_preferredLocales;
        private NamespaceTable m_namespaceUris;
        private StringTable m_serverUris;
        private EncodeableFactory m_factory;
        private SystemContext m_systemContext;
        private NodeCache m_nodeCache;
        private ApplicationConfiguration m_configuration;
        private ConfiguredEndpoint m_endpoint;
        private X509Certificate2 m_instanceCertificate;
        //private X509Certificate2Collection m_instanceCertificateChain;
        private List<IUserIdentity> m_identityHistory;

        private string m_sessionName;
        private object m_handle;
        private IUserIdentity m_identity;
        private byte[] m_serverNonce;
        private X509Certificate2 m_serverCertificate;
        private long m_publishCounter;
        private DateTime m_lastKeepAliveTime;
        private ServerState m_serverState;
        private int m_keepAliveInterval;
        private Timer m_keepAliveTimer;
        private long m_keepAliveCounter;
        private bool m_reconnecting;
        private LinkedList<AsyncRequestState> m_outstandingRequests;

        private class AsyncRequestState
        {
            public uint RequestTypeId;
            public uint RequestId;
            public DateTime Timestamp;
            public IAsyncResult Result;
            public bool Defunct;
        }

        private object m_eventLock = new object();
        private event KeepAliveEventHandler m_KeepAlive;
        private event NotificationEventHandler m_Publish;
        private event PublishErrorEventHandler m_PublishError;
        private event EventHandler m_SubscriptionsChanged;
        private event EventHandler m_SessionClosing;
        #endregion
    }

    #region KeepAliveEventArgs Class
    /// <summary>
    /// The event arguments provided when a keep alive response arrives.
    /// </summary>
    public class KeepAliveEventArgs : EventArgs
    {
        #region Constructors
        /// <summary>
        /// Creates a new instance.
        /// </summary>
        internal KeepAliveEventArgs(
            ServiceResult status,
            ServerState currentState,
            DateTime currentTime)
        {
            m_status = status;
            m_currentState = currentState;
            m_currentTime = currentTime;
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// Gets the status associated with the keep alive operation.
        /// </summary>
        public ServiceResult Status
        {
            get { return m_status; }
        }

        /// <summary>
        /// Gets the current server state.
        /// </summary>
        public ServerState CurrentState
        {
            get { return m_currentState; }
        }

        /// <summary>
        /// Gets the current server time.
        /// </summary>
        public DateTime CurrentTime
        {
            get { return m_currentTime; }
        }

        /// <summary>
        /// Gets or sets a flag indicating whether the session should send another keep alive.
        /// </summary>
        public bool CancelKeepAlive
        {
            get { return m_cancelKeepAlive; }
            set { m_cancelKeepAlive = value; }
        }
        #endregion

        #region Private Fields
        private ServiceResult m_status;
        private ServerState m_currentState;
        private DateTime m_currentTime;
        private bool m_cancelKeepAlive;
        #endregion
    }

    /// <summary>
    /// The delegate used to receive keep alive notifications.
    /// </summary>
    public delegate void KeepAliveEventHandler(Session session, KeepAliveEventArgs e);
    #endregion

    #region NotificationEventArgs Class
    /// <summary>
    /// Represents the event arguments provided when a new notification message arrives.
    /// </summary>
    public class NotificationEventArgs : EventArgs
    {
        #region Constructors
        /// <summary>
        /// Creates a new instance.
        /// </summary>
        internal NotificationEventArgs(
            Subscription subscription,
            NotificationMessage notificationMessage,
            IList<string> stringTable)
        {
            m_subscription = subscription;
            m_notificationMessage = notificationMessage;
            m_stringTable = stringTable;
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// Gets the subscription that the notification applies to.
        /// </summary>
        public Subscription Subscription
        {
            get { return m_subscription; }
        }

        /// <summary>
        /// Gets the notification message.
        /// </summary>
        public NotificationMessage NotificationMessage
        {
            get { return m_notificationMessage; }
        }

        /// <summary>
        /// Gets the string table returned with the notification message.
        /// </summary>
        public IList<string> StringTable
        {
            get { return m_stringTable; }
        }
        #endregion

        #region Private Fields
        private Subscription m_subscription;
        private NotificationMessage m_notificationMessage;
        private IList<string> m_stringTable;
        #endregion
    }

    /// <summary>
    /// The delegate used to receive publish notifications.
    /// </summary>
    public delegate void NotificationEventHandler(Session session, NotificationEventArgs e);
    #endregion

    #region PublishErrorEventArgs Class
    /// <summary>
    /// Represents the event arguments provided when a publish error occurs.
    /// </summary>
    public class PublishErrorEventArgs : EventArgs
    {
        #region Constructors
        /// <summary>
        /// Creates a new instance.
        /// </summary>
        internal PublishErrorEventArgs(ServiceResult status)
        {
            m_status = status;
        }

        /// <summary>
        /// Creates a new instance.
        /// </summary>
        internal PublishErrorEventArgs(ServiceResult status, uint subscriptionId, uint sequenceNumber)
        {
            m_status = status;
            m_subscriptionId = subscriptionId;
            m_sequenceNumber = sequenceNumber;
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// Gets the status associated with the keep alive operation.
        /// </summary>
        public ServiceResult Status
        {
            get { return m_status; }
        }

        /// <summary>
        /// Gets the subscription with the message that could not be republished.
        /// </summary>
        public uint SubscriptionId
        {
            get { return m_subscriptionId; }
        }

        /// <summary>
        /// Gets the sequence number for the message that could not be republished.
        /// </summary>
        public uint SequenceNumber
        {
            get { return m_sequenceNumber; }
        }
        #endregion

        #region Private Fields
        private uint m_subscriptionId;
        private uint m_sequenceNumber;
        private ServiceResult m_status;
        #endregion
    }

    /// <summary>
    /// The delegate used to receive pubish error notifications.
    /// </summary>
    public delegate void PublishErrorEventHandler(Session session, PublishErrorEventArgs e);
    #endregion
}
