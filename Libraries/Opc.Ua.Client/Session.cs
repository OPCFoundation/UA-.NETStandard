/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Manages a session with a server.
    /// </summary>
    public partial class Session : SessionClientBatched, ISession, IDisposable
    {
        #region Constructors
        /// <summary>
        /// Constructs a new instance of the <see cref="Session"/> class.
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
        /// Constructs a new instance of the <see cref="ISession"/> class.
        /// </summary>
        /// <param name="channel">The channel used to communicate with the server.</param>
        /// <param name="configuration">The configuration for the client application.</param>
        /// <param name="endpoint">The endpoint used to initialize the channel.</param>
        /// <param name="clientCertificate">The certificate to use for the client.</param>
        /// <param name="availableEndpoints">The list of available endpoints returned by server in GetEndpoints() response.</param>
        /// <param name="discoveryProfileUris">The value of profileUris used in GetEndpoints() request.</param>
        /// <remarks>
        /// The application configuration is used to look up the certificate if none is provided.
        /// The clientCertificate must have the private key. This will require that the certificate
        /// be loaded from a certicate store. Converting a DER encoded blob to a X509Certificate2
        /// will not include a private key.
        /// The <i>availableEndpoints</i> and <i>discoveryProfileUris</i> parameters are used to validate
        /// that the list of EndpointDescriptions returned at GetEndpoints matches the list returned at CreateSession.
        /// </remarks>
        public Session(
            ITransportChannel channel,
            ApplicationConfiguration configuration,
            ConfiguredEndpoint endpoint,
            X509Certificate2 clientCertificate,
            EndpointDescriptionCollection availableEndpoints = null,
            StringCollection discoveryProfileUris = null)
            :
                base(channel)
        {
            Initialize(channel, configuration, endpoint, clientCertificate);
            m_discoveryServerEndpoints = availableEndpoints;
            m_discoveryProfileUris = discoveryProfileUris;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ISession"/> class.
        /// </summary>
        /// <param name="channel">The channel.</param>
        /// <param name="template">The template session.</param>
        /// <param name="copyEventHandlers">if set to <c>true</c> the event handlers are copied.</param>
        public Session(ITransportChannel channel, Session template, bool copyEventHandlers)
        :
            base(channel)
        {
            Initialize(channel, template.m_configuration, template.ConfiguredEndpoint, template.m_instanceCertificate);

            m_sessionFactory = template.m_sessionFactory;
            m_defaultSubscription = template.m_defaultSubscription;
            m_deleteSubscriptionsOnClose = template.m_deleteSubscriptionsOnClose;
            m_transferSubscriptionsOnReconnect = template.m_transferSubscriptionsOnReconnect;
            m_sessionTimeout = template.m_sessionTimeout;
            m_maxRequestMessageSize = template.m_maxRequestMessageSize;
            m_preferredLocales = template.PreferredLocales;
            m_sessionName = template.SessionName;
            m_handle = template.Handle;
            m_identity = template.Identity;
            m_keepAliveInterval = template.KeepAliveInterval;
            m_checkDomain = template.m_checkDomain;
            if (template.OperationTimeout > 0)
            {
                OperationTimeout = template.OperationTimeout;
            }

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
                AddSubscription(new Subscription(subscription, copyEventHandlers));
            }
        }
        #endregion

        #region Private Methods
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

            ValidateClientConfiguration(configuration);

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

                    m_instanceCertificate = m_configuration.SecurityConfiguration.ApplicationCertificate.Find(true).Result;
                }

                // check for valid certificate.
                if (m_instanceCertificate == null)
                {
                    var cert = m_configuration.SecurityConfiguration.ApplicationCertificate;
                    throw ServiceResultException.Create(
                        StatusCodes.BadConfigurationError,
                        "Cannot find the application instance certificate. Store={0}, SubjectName={1}, Thumbprint={2}.",
                        cert.StorePath, cert.SubjectName, cert.Thumbprint);
                }

                // check for private key.
                if (!m_instanceCertificate.HasPrivateKey)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadConfigurationError,
                        "No private key for the application instance certificate. Subject={0}, Thumbprint={1}.",
                        m_instanceCertificate.Subject,
                        m_instanceCertificate.Thumbprint);
                }

                // load certificate chain.
                m_instanceCertificateChain = new X509Certificate2Collection(m_instanceCertificate);
                List<CertificateIdentifier> issuers = new List<CertificateIdentifier>();
                configuration.CertificateValidator.GetIssuers(m_instanceCertificate, issuers).Wait();

                for (int i = 0; i < issuers.Count; i++)
                {
                    m_instanceCertificateChain.Add(issuers[i].Certificate);
                }
            }

            // initialize the message context.
            IServiceMessageContext messageContext = channel.MessageContext;

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
                m_factory = new EncodeableFactory(EncodeableFactory.GlobalFactory);
            }

            // initialize the NodeCache late, it needs references to the namespaceUris
            m_nodeCache = new NodeCache(this);

            // set the default preferred locales.
            m_preferredLocales = new string[] { CultureInfo.CurrentCulture.Name };

            // create a context to use.
            m_systemContext = new SystemContext {
                SystemHandle = this,
                EncodeableFactory = m_factory,
                NamespaceUris = m_namespaceUris,
                ServerUris = m_serverUris,
                TypeTable = TypeTree,
                PreferredLocales = null,
                SessionId = null,
                UserIdentity = null
            };
        }

        /// <summary>
        /// Sets the object members to default values.
        /// </summary>
        private void Initialize()
        {
            m_sessionFactory = new DefaultSessionFactory();
            m_sessionTimeout = 0;
            m_namespaceUris = new NamespaceTable();
            m_serverUris = new StringTable();
            m_factory = EncodeableFactory.GlobalFactory;
            m_configuration = null;
            m_instanceCertificate = null;
            m_endpoint = null;
            m_subscriptions = new List<Subscription>();
            m_dictionaries = new Dictionary<NodeId, DataDictionary>();
            m_acknowledgementsToSend = new SubscriptionAcknowledgementCollection();
            m_latestAcknowledgementsSent = new Dictionary<uint, uint>();
            m_identityHistory = new List<IUserIdentity>();
            m_outstandingRequests = new LinkedList<AsyncRequestState>();
            m_keepAliveInterval = 5000;
            m_tooManyPublishRequests = 0;
            m_sessionName = "";
            m_deleteSubscriptionsOnClose = true;
            m_transferSubscriptionsOnReconnect = false;

            m_defaultSubscription = new Subscription {
                DisplayName = "Subscription",
                PublishingInterval = 1000,
                KeepAliveCount = 10,
                LifetimeCount = 1000,
                Priority = 255,
                PublishingEnabled = true
            };
        }

        /// <summary>
        /// Check if all required configuration fields are populated.
        /// </summary>
        private void ValidateClientConfiguration(ApplicationConfiguration configuration)
        {
            String configurationField;
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }
            if (configuration.ClientConfiguration == null)
            {
                configurationField = "ClientConfiguration";
            }
            else if (configuration.SecurityConfiguration == null)
            {
                configurationField = "SecurityConfiguration";
            }
            else if (configuration.CertificateValidator == null)
            {
                configurationField = "CertificateValidator";
            }
            else
            {
                return;
            }

            throw new ServiceResultException(
                StatusCodes.BadConfigurationError,
                $"The client configuration does not specify the {configurationField}.");
        }

        /// <summary>
        /// Validates the server nonce and security parameters of user identity.
        /// </summary>
        private void ValidateServerNonce(
            IUserIdentity identity,
            byte[] serverNonce,
            string securityPolicyUri,
            byte[] previousServerNonce,
            MessageSecurityMode channelSecurityMode = MessageSecurityMode.None)
        {
            // skip validation if server nonce is not used for encryption.
            if (String.IsNullOrEmpty(securityPolicyUri) || securityPolicyUri == SecurityPolicies.None)
            {
                return;
            }

            if (identity != null && identity.TokenType != UserTokenType.Anonymous)
            {
                // the server nonce should be validated if the token includes a secret.
                if (!Utils.Nonce.ValidateNonce(serverNonce, MessageSecurityMode.SignAndEncrypt, (uint)m_configuration.SecurityConfiguration.NonceLength))
                {
                    if (channelSecurityMode == MessageSecurityMode.SignAndEncrypt ||
                        m_configuration.SecurityConfiguration.SuppressNonceValidationErrors)
                    {
                        Utils.LogWarning(Utils.TraceMasks.Security, "Warning: The server nonce has not the correct length or is not random enough. The error is suppressed by user setting or because the channel is encrypted.");
                    }
                    else
                    {
                        throw ServiceResultException.Create(StatusCodes.BadNonceInvalid, "The server nonce has not the correct length or is not random enough.");
                    }
                }

                // check that new nonce is different from the previously returned server nonce.
                if (previousServerNonce != null && Utils.CompareNonce(serverNonce, previousServerNonce))
                {
                    if (channelSecurityMode == MessageSecurityMode.SignAndEncrypt ||
                        m_configuration.SecurityConfiguration.SuppressNonceValidationErrors)
                    {
                        Utils.LogWarning(Utils.TraceMasks.Security, "Warning: The Server nonce is equal with previously returned nonce. The error is suppressed by user setting or because the channel is encrypted.");
                    }
                    else
                    {
                        throw ServiceResultException.Create(StatusCodes.BadNonceInvalid, "Server nonce is equal with previously returned nonce.");
                    }
                }
            }
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
        /// A session factory that was used to create the session.
        /// </summary>
        public ISessionFactory SessionFactory
        {
            get => m_sessionFactory;
            set => m_sessionFactory = value;
        }

        /// <summary>
        /// Gets the endpoint used to connect to the server.
        /// </summary>
        public ConfiguredEndpoint ConfiguredEndpoint => m_endpoint;

        /// <summary>
        /// Gets the name assigned to the session.
        /// </summary>
        public string SessionName => m_sessionName;

        /// <summary>
        /// Gets the period for wich the server will maintain the session if there is no communication from the client.
        /// </summary>
        public double SessionTimeout => m_sessionTimeout;

        /// <summary>
        /// Gets the local handle assigned to the session.
        /// </summary>
        public object Handle
        {
            get { return m_handle; }
            set { m_handle = value; }
        }

        /// <summary>
        /// Gets the user identity currently used for the session.
        /// </summary>
        public IUserIdentity Identity => m_identity;

        /// <summary>
        /// Gets a list of user identities that can be used to connect to the server.
        /// </summary>
        public IEnumerable<IUserIdentity> IdentityHistory => m_identityHistory;

        /// <summary>
        /// Gets the table of namespace uris known to the server.
        /// </summary>
        public NamespaceTable NamespaceUris => m_namespaceUris;

        /// <summary>
        /// Gets the table of remote server uris known to the server.
        /// </summary>
        public StringTable ServerUris => m_serverUris;

        /// <summary>
        /// Gets the system context for use with the session.
        /// </summary>
        public ISystemContext SystemContext => m_systemContext;

        /// <summary>
        /// Gets the factory used to create encodeable objects that the server understands.
        /// </summary>
        public IEncodeableFactory Factory => m_factory;

        /// <summary>
        /// Gets the cache of the server's type tree.
        /// </summary>
        public ITypeTable TypeTree => m_nodeCache.TypeTree;

        /// <summary>
        /// Gets the cache of nodes fetched from the server.
        /// </summary>
        public INodeCache NodeCache => m_nodeCache;

        /// <summary>
        /// Gets the context to use for filter operations.
        /// </summary>
        public FilterContext FilterContext => new FilterContext(m_namespaceUris, m_nodeCache.TypeTree, m_preferredLocales);

        /// <summary>
        /// Gets the locales that the server should use when returning localized text.
        /// </summary>
        public StringCollection PreferredLocales => m_preferredLocales;

        /// <summary>
        /// Gets the data type system dictionaries in use.
        /// </summary>
        public IReadOnlyDictionary<NodeId, DataDictionary> DataTypeSystem => m_dictionaries;

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
        /// If the subscriptions are deleted when a session is closed. 
        /// </summary>
        /// <remarks>
        /// Default <c>true</c>, set to <c>false</c> if subscriptions need to
        /// be transferred or for durable subscriptions.
        /// </remarks>   
        public bool DeleteSubscriptionsOnClose
        {
            get { return m_deleteSubscriptionsOnClose; }
            set { m_deleteSubscriptionsOnClose = value; }
        }

        /// <summary>
        /// If the subscriptions are transferred when a session is reconnected. 
        /// </summary>
        /// <remarks>
        /// Default <c>false</c>, set to <c>true</c> if subscriptions should
        /// be transferred after reconnect. Service must be supported by server.
        /// </remarks>   
        public bool TransferSubscriptionsOnReconnect
        {
            get { return m_transferSubscriptionsOnReconnect; }
            set { m_transferSubscriptionsOnReconnect = value; }
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
        public DateTime LastKeepAliveTime => m_lastKeepAliveTime;

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
        /// Gets the number of outstanding publish or keep alive requests which appear to be missing.
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

        #region Public Static Methods
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
        public static Task<Session> Create(
            ApplicationConfiguration configuration,
            ConfiguredEndpoint endpoint,
            bool updateBeforeConnect,
            string sessionName,
            uint sessionTimeout,
            IUserIdentity identity,
            IList<string> preferredLocales)
        {
            return Create(configuration, endpoint, updateBeforeConnect, false, sessionName, sessionTimeout, identity, preferredLocales);
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
        public static Task<Session> Create(
            ApplicationConfiguration configuration,
            ConfiguredEndpoint endpoint,
            bool updateBeforeConnect,
            bool checkDomain,
            string sessionName,
            uint sessionTimeout,
            IUserIdentity identity,
            IList<string> preferredLocales)
        {
            return Create(configuration, null, endpoint, updateBeforeConnect, checkDomain, sessionName, sessionTimeout, identity, preferredLocales);
        }

        /// <summary>
        /// Creates a new session with a server using the specified channel by invoking the CreateSession service
        /// </summary>
        /// <param name="configuration">The configuration for the client application.</param>
        /// <param name="channel">The channel for the server.</param>
        /// <param name="endpoint">The endpoint for the server.</param>
        /// <param name="clientCertificate">The certificate to use for the client.</param>
        /// <param name="availableEndpoints">The list of available endpoints returned by server in GetEndpoints() response.</param>
        /// <param name="discoveryProfileUris">The value of profileUris used in GetEndpoints() request.</param>
        /// <returns></returns>
        public static Session Create(
           ApplicationConfiguration configuration,
           ITransportChannel channel,
           ConfiguredEndpoint endpoint,
           X509Certificate2 clientCertificate,
           EndpointDescriptionCollection availableEndpoints = null,
           StringCollection discoveryProfileUris = null)
        {
            return new Session(channel, configuration, endpoint, clientCertificate, availableEndpoints, discoveryProfileUris);
        }

        /// <summary>
        /// Creates a secure channel to the specified endpoint.
        /// </summary>
        /// <param name="configuration">The application configuration.</param>
        /// <param name="connection">The client endpoint for the reverse connect.</param>
        /// <param name="endpoint">A configured endpoint to connect to.</param> 
        /// <param name="updateBeforeConnect">Update configuration based on server prior connect.</param>
        /// <param name="checkDomain">Check that the certificate specifies a valid domain (computer) name.</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
        public static async Task<ITransportChannel> CreateChannelAsync(
            ApplicationConfiguration configuration,
            ITransportWaitingConnection connection,
            ConfiguredEndpoint endpoint,
            bool updateBeforeConnect,
            bool checkDomain)
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
            IServiceMessageContext messageContext = configuration.CreateMessageContext(true);

            // update endpoint description using the discovery endpoint.
            if (endpoint.UpdateBeforeConnect && connection == null)
            {
                endpoint.UpdateFromServer();
                endpointDescription = endpoint.Description;
                endpointConfiguration = endpoint.Configuration;
            }

            // checks the domains in the certificate.
            if (checkDomain &&
                endpoint.Description.ServerCertificate != null &&
                endpoint.Description.ServerCertificate.Length > 0)
            {
                configuration.CertificateValidator?.ValidateDomains(
                    new X509Certificate2(endpoint.Description.ServerCertificate),
                    endpoint);
                checkDomain = false;
            }

            X509Certificate2 clientCertificate = null;
            X509Certificate2Collection clientCertificateChain = null;
            if (endpointDescription.SecurityPolicyUri != SecurityPolicies.None)
            {
                clientCertificate = await LoadCertificate(configuration).ConfigureAwait(false);
                clientCertificateChain = await LoadCertificateChain(configuration, clientCertificate).ConfigureAwait(false);
            }

            // initialize the channel which will be created with the server.
            ITransportChannel channel;
            if (connection != null)
            {
                channel = SessionChannel.CreateUaBinaryChannel(
                    configuration,
                    connection,
                    endpointDescription,
                    endpointConfiguration,
                    clientCertificate,
                    clientCertificateChain,
                    messageContext);
            }
            else
            {
                channel = SessionChannel.Create(
                     configuration,
                     endpointDescription,
                     endpointConfiguration,
                     clientCertificate,
                     clientCertificateChain,
                     messageContext);
            }

            return channel;
        }

        /// <summary>
        /// Creates a new communication session with a server using a reverse connection.
        /// </summary>
        /// <param name="configuration">The configuration for the client application.</param>
        /// <param name="connection">The client endpoint for the reverse connect.</param>
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
            ITransportWaitingConnection connection,
            ConfiguredEndpoint endpoint,
            bool updateBeforeConnect,
            bool checkDomain,
            string sessionName,
            uint sessionTimeout,
            IUserIdentity identity,
            IList<string> preferredLocales)
        {
            // initialize the channel which will be created with the server.
            ITransportChannel channel = await Session.CreateChannelAsync(configuration, connection, endpoint, updateBeforeConnect, checkDomain).ConfigureAwait(false);

            // create the session object.
            Session session = new Session(channel, configuration, endpoint, null);

            // create the session.
            try
            {
                session.Open(sessionName, sessionTimeout, identity, preferredLocales, checkDomain);
            }
            catch (Exception)
            {
                session.Dispose();
                throw;
            }

            return session;
        }

        /// <summary>
        /// Creates a new communication session with a server using a reverse connect manager.
        /// </summary>
        /// <param name="configuration">The configuration for the client application.</param>
        /// <param name="reverseConnectManager">The reverse connect manager for the client connection.</param>
        /// <param name="endpoint">The endpoint for the server.</param>
        /// <param name="updateBeforeConnect">If set to <c>true</c> the discovery endpoint is used to update the endpoint description before connecting.</param>
        /// <param name="checkDomain">If set to <c>true</c> then the domain in the certificate must match the endpoint used.</param>
        /// <param name="sessionName">The name to assign to the session.</param>
        /// <param name="sessionTimeout">The timeout period for the session.</param>
        /// <param name="userIdentity">The user identity to associate with the session.</param>
        /// <param name="preferredLocales">The preferred locales.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The new session object.</returns>
        public static async Task<Session> Create(
            ApplicationConfiguration configuration,
            ReverseConnectManager reverseConnectManager,
            ConfiguredEndpoint endpoint,
            bool updateBeforeConnect,
            bool checkDomain,
            string sessionName,
            uint sessionTimeout,
            IUserIdentity userIdentity,
            IList<string> preferredLocales,
            CancellationToken ct = default(CancellationToken)
            )
        {
            if (reverseConnectManager == null)
            {
                return await Create(configuration, endpoint, updateBeforeConnect,
                    checkDomain, sessionName, sessionTimeout, userIdentity, preferredLocales).ConfigureAwait(false);
            }

            ITransportWaitingConnection connection = null;
            do
            {
                connection = await reverseConnectManager.WaitForConnection(
                    endpoint.EndpointUrl,
                    endpoint.ReverseConnect?.ServerUri,
                    ct).ConfigureAwait(false);

                if (updateBeforeConnect)
                {
                    await endpoint.UpdateFromServerAsync(
                        endpoint.EndpointUrl, connection,
                        endpoint.Description.SecurityMode,
                        endpoint.Description.SecurityPolicyUri).ConfigureAwait(false);
                    updateBeforeConnect = false;
                    connection = null;
                }
            } while (connection == null);

            return await Create(
                configuration,
                connection,
                endpoint,
                false,
                checkDomain,
                sessionName,
                sessionTimeout,
                userIdentity,
                preferredLocales).ConfigureAwait(false);
        }

        /// <summary>
        /// Recreates a session based on a specified template.
        /// </summary>
        /// <param name="template">The Session object to use as template</param>
        /// <returns>The new session object.</returns>
        public static Session Recreate(Session template)
        {
            var messageContext = template.m_configuration.CreateMessageContext();
            messageContext.Factory = template.Factory;

            // create the channel object used to connect to the server.
            ITransportChannel channel = SessionChannel.Create(
                template.m_configuration,
                template.ConfiguredEndpoint.Description,
                template.ConfiguredEndpoint.Configuration,
                template.m_instanceCertificate,
                template.m_configuration.SecurityConfiguration.SendCertificateChain ?
                    template.m_instanceCertificateChain : null,
                messageContext);

            // create the session object.
            Session session = new Session(channel, template, true);

            try
            {
                // open the session.
                session.Open(
                    template.SessionName,
                    (uint)template.SessionTimeout,
                    template.Identity,
                    template.PreferredLocales,
                    template.m_checkDomain);

                session.RecreateSubscriptions(template.Subscriptions);
            }
            catch (Exception e)
            {
                session.Dispose();
                throw ServiceResultException.Create(StatusCodes.BadCommunicationError, e, "Could not recreate session. {0}", template.SessionName);
            }

            return session;
        }

        /// <summary>
        /// Recreates a session based on a specified template.
        /// </summary>
        /// <param name="template">The Session object to use as template</param>
        /// <param name="connection">The waiting reverse connection.</param>
        /// <returns>The new session object.</returns>
        public static Session Recreate(Session template, ITransportWaitingConnection connection)
        {
            var messageContext = template.m_configuration.CreateMessageContext();
            messageContext.Factory = template.Factory;

            // create the channel object used to connect to the server.
            ITransportChannel channel = SessionChannel.Create(
                template.m_configuration,
                connection,
                template.m_endpoint.Description,
                template.m_endpoint.Configuration,
                template.m_instanceCertificate,
                template.m_configuration.SecurityConfiguration.SendCertificateChain ?
                    template.m_instanceCertificateChain : null,
                messageContext);

            // create the session object.
            Session session = new Session(channel, template, true);

            try
            {
                // open the session.
                session.Open(
                    template.m_sessionName,
                    (uint)template.m_sessionTimeout,
                    template.m_identity,
                    template.m_preferredLocales,
                    template.m_checkDomain);

                session.RecreateSubscriptions(template.Subscriptions);
            }
            catch (Exception e)
            {
                session.Dispose();
                throw ServiceResultException.Create(StatusCodes.BadCommunicationError, e, "Could not recreate session. {0}", template.m_sessionName);
            }

            return session;
        }

        /// <summary>
        /// Recreates a session based on a specified template using the provided channel.
        /// </summary>
        /// <param name="template">The Session object to use as template</param>
        /// <param name="transportChannel">The waiting reverse connection.</param>
        /// <returns>The new session object.</returns>
        public static Session Recreate(Session template, ITransportChannel transportChannel)
        {
            var messageContext = template.m_configuration.CreateMessageContext();
            messageContext.Factory = template.Factory;

            // create the session object.
            Session session = new Session(transportChannel, template, true);

            try
            {
                // open the session.
                session.Open(
                    template.m_sessionName,
                    (uint)template.m_sessionTimeout,
                    template.m_identity,
                    template.m_preferredLocales,
                    template.m_checkDomain);

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
        #endregion

        #region Events
        /// <summary>
        /// Raised before a reconnect operation completes.
        /// </summary>
        public event RenewUserIdentityEventHandler RenewUserIdentity
        {
            add { m_RenewUserIdentity += value; }
            remove { m_RenewUserIdentity -= value; }
        }

        private event RenewUserIdentityEventHandler m_RenewUserIdentity;
        #endregion

        #region Public Methods
        /// <summary>
        /// Reconnects to the server after a network failure.
        /// </summary>
        public void Reconnect()
        {
            Reconnect(null, null);
        }

        /// <summary>
        /// Reconnects to the server after a network failure using a waiting connection.
        /// </summary>
        public void Reconnect(ITransportWaitingConnection connection)
            => Reconnect(connection, null);

        /// <summary>
        /// Reconnects to the server after a network failure using a waiting connection.
        /// </summary>
        public void Reconnect(ITransportWaitingConnection connection, ITransportChannel transportChannel = null)
        {
            try
            {
                lock (SyncRoot)
                {
                    // check if already connecting.
                    if (m_reconnecting)
                    {
                        Utils.LogWarning("Session is already attempting to reconnect.");

                        throw ServiceResultException.Create(
                            StatusCodes.BadInvalidState,
                            "Session is already attempting to reconnect.");
                    }

                    Utils.LogInfo("Session RECONNECT starting.");
                    m_reconnecting = true;

                    // stop keep alives.
                    Utils.SilentDispose(m_keepAliveTimer);
                    m_keepAliveTimer = null;
                }

                // create the client signature.
                byte[] dataToSign = Utils.Append(m_serverCertificate != null ? m_serverCertificate.RawData : null, m_serverNonce);
                EndpointDescription endpoint = m_endpoint.Description;
                SignatureData clientSignature = SecurityPolicies.Sign(m_instanceCertificate, endpoint.SecurityPolicyUri, dataToSign);

                // check that the user identity is supported by the endpoint.
                UserTokenPolicy identityPolicy = endpoint.FindUserTokenPolicy(m_identity.TokenType, m_identity.IssuedTokenType);

                if (identityPolicy == null)
                {
                    Utils.LogError("Reconnect: Endpoint does not support the user identity type provided.");

                    throw ServiceResultException.Create(
                        StatusCodes.BadUserAccessDenied,
                        "Endpoint does not support the user identity type provided.");
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

                // validate server nonce and security parameters for user identity.
                ValidateServerNonce(
                    m_identity,
                    m_serverNonce,
                    securityPolicyUri,
                    m_previousServerNonce,
                    m_endpoint.Description.SecurityMode);

                // sign data with user token.
                UserIdentityToken identityToken = m_identity.GetIdentityToken();
                identityToken.PolicyId = identityPolicy.PolicyId;
                SignatureData userTokenSignature = identityToken.Sign(dataToSign, securityPolicyUri);

                // encrypt token.
                identityToken.Encrypt(m_serverCertificate, m_serverNonce, securityPolicyUri);

                // send the software certificates assigned to the client.
                SignedSoftwareCertificateCollection clientSoftwareCertificates = GetSoftwareCertificates();

                Utils.LogInfo("Session REPLACING channel.");

                if (connection != null)
                {
                    // check if the channel supports reconnect.
                    if ((TransportChannel.SupportedFeatures & TransportChannelFeatures.Reconnect) != 0)
                    {
                        TransportChannel.Reconnect(connection);
                    }
                    else
                    {
                        // initialize the channel which will be created with the server.
                        ITransportChannel channel = SessionChannel.Create(
                            m_configuration,
                            connection,
                            m_endpoint.Description,
                            m_endpoint.Configuration,
                            m_instanceCertificate,
                            m_configuration.SecurityConfiguration.SendCertificateChain ? m_instanceCertificateChain : null,
                            MessageContext);

                        // disposes the existing channel.
                        TransportChannel = channel;
                    }
                }
                else if (transportChannel != null)
                {
                    TransportChannel = transportChannel;
                }
                else
                {
                    // check if the channel supports reconnect.
                    if (TransportChannel != null && (TransportChannel.SupportedFeatures & TransportChannelFeatures.Reconnect) != 0)
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
                            m_configuration.SecurityConfiguration.SendCertificateChain ? m_instanceCertificateChain : null,
                            MessageContext);

                        // disposes the existing channel.
                        TransportChannel = channel;
                    }
                }

                // reactivate session.
                byte[] serverNonce = null;
                StatusCodeCollection certificateResults = null;
                DiagnosticInfoCollection certificateDiagnosticInfos = null;

                Utils.LogInfo("Session RE-ACTIVATING session.");

                RequestHeader header = new RequestHeader() { TimeoutHint = kReconnectTimeout };
                IAsyncResult result = BeginActivateSession(
                    header,
                    clientSignature,
                    null,
                    m_preferredLocales,
                    new ExtensionObject(identityToken),
                    userTokenSignature,
                    null,
                    null);

                if (!result.AsyncWaitHandle.WaitOne(kReconnectTimeout / 2))
                {
                    Utils.LogWarning("WARNING: ACTIVATE SESSION timed out. {0}/{1}", GoodPublishRequestCount, OutstandingRequestCount);
                }

                EndActivateSession(
                    result,
                    out serverNonce,
                    out certificateResults,
                    out certificateDiagnosticInfos);

                int publishCount = 0;

                lock (SyncRoot)
                {
                    Utils.LogInfo("Session RECONNECT completed successfully.");
                    m_previousServerNonce = m_serverNonce;
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
        /// Saves a set of subscriptions to a stream.
        /// </summary>
        public void Save(Stream stream, IEnumerable<Subscription> subscriptions)
        {
            SubscriptionCollection subscriptionList = new SubscriptionCollection(subscriptions);
            XmlWriterSettings settings = Utils.DefaultXmlWriterSettings();

            using (XmlWriter writer = XmlWriter.Create(stream, settings))
            {
                DataContractSerializer serializer = new DataContractSerializer(typeof(SubscriptionCollection));
                serializer.WriteObject(writer, subscriptionList);
            }
        }

        /// <summary>
        /// Saves a set of subscriptions to a file.
        /// </summary>
        public void Save(string filePath, IEnumerable<Subscription> subscriptions)
        {
            using (FileStream stream = new FileStream(filePath, FileMode.Create))
            {
                Save(stream, subscriptions);
            }
        }

        /// <summary>
        /// Load the list of subscriptions saved in a stream.
        /// </summary>
        /// <param name="stream">The stream.</param>
        /// <param name="transferSubscriptions">Load the subscriptions for transfer after load.</param>
        /// <returns>The list of loaded subscriptions</returns>
        public IEnumerable<Subscription> Load(Stream stream, bool transferSubscriptions = false)
        {
            // secure settings
            XmlReaderSettings settings = Utils.DefaultXmlReaderSettings();
            settings.CloseInput = true;

            using (XmlReader reader = XmlReader.Create(stream, settings))
            {
                DataContractSerializer serializer = new DataContractSerializer(typeof(SubscriptionCollection));
                SubscriptionCollection subscriptions = (SubscriptionCollection)serializer.ReadObject(reader);
                foreach (Subscription subscription in subscriptions)
                {
                    if (!transferSubscriptions)
                    {
                        // ServerId must be reset if the saved list of subscriptions
                        // is not used to transfer a subscription
                        foreach (var monitoredItem in subscription.MonitoredItems)
                        {
                            monitoredItem.ServerId = 0;
                        }
                    }
                    AddSubscription(subscription);
                }
                return subscriptions;
            }
        }

        /// <summary>
        /// Load the list of subscriptions saved in a file.
        /// </summary>
        /// <param name="filePath">The file path.</param>
        /// <param name="transferSubscriptions">Load the subscriptions for transfer after load.</param>
        /// <returns>The list of loaded subscriptions</returns>
        public IEnumerable<Subscription> Load(string filePath, bool transferSubscriptions = false)
        {
            using (FileStream stream = File.OpenRead(filePath))
            {
                return Load(stream, transferSubscriptions);
            }
        }

        /// <summary>
        /// Updates the local copy of the server's namespace uri and server uri tables.
        /// </summary>
        public void FetchNamespaceTables()
        {
            ReadValueIdCollection nodesToRead = new ReadValueIdCollection();

            // request namespace array.
            ReadValueId valueId = new ReadValueId {
                NodeId = Variables.Server_NamespaceArray,
                AttributeId = Attributes.Value
            };

            nodesToRead.Add(valueId);

            // request server array.
            valueId = new ReadValueId {
                NodeId = Variables.Server_ServerArray,
                AttributeId = Attributes.Value
            };

            nodesToRead.Add(valueId);

            // read from server.
            ResponseHeader responseHeader = this.Read(
                null,
                0,
                TimestampsToReturn.Both,
                nodesToRead,
                out DataValueCollection values,
                out DiagnosticInfoCollection diagnosticInfos);

            ValidateResponse(values, nodesToRead);
            ValidateDiagnosticInfos(diagnosticInfos, nodesToRead);

            // validate namespace array.
            ServiceResult result = ValidateDataValue(values[0], typeof(string[]), 0, diagnosticInfos, responseHeader);

            if (ServiceResult.IsBad(result))
            {
                Utils.LogError("FetchNamespaceTables: Cannot read NamespaceArray node: {0}", result.StatusCode);
            }
            else
            {
                m_namespaceUris.Update((string[])values[0].Value);
            }

            // validate server array.
            result = ValidateDataValue(values[1], typeof(string[]), 1, diagnosticInfos, responseHeader);

            if (ServiceResult.IsBad(result))
            {
                Utils.LogError("FetchNamespaceTables: Cannot read ServerArray node: {0} ", result.StatusCode);
            }
            else
            {
                m_serverUris.Update((string[])values[1].Value);
            }
        }

        /// <summary>
        /// Fetch the operation limits of the server.
        /// </summary>
        public void FetchOperationLimits()
        {
            try
            {
                var operationLimitsProperties = typeof(OperationLimits)
                    .GetProperties().Select(p => p.Name).ToList();

                var nodeIds = new NodeIdCollection(
                    operationLimitsProperties.Select(name => (NodeId)typeof(VariableIds)
                    .GetField("Server_ServerCapabilities_OperationLimits_" + name, BindingFlags.Public | BindingFlags.Static)
                    .GetValue(null))
                    );

                ReadValues(nodeIds, Enumerable.Repeat(typeof(uint), nodeIds.Count).ToList(), out var values, out var errors);

                var configOperationLimits = m_configuration?.ClientConfiguration?.OperationLimits ?? new OperationLimits();
                var operationLimits = new OperationLimits();

                for (int ii = 0; ii < nodeIds.Count; ii++)
                {
                    var property = typeof(OperationLimits).GetProperty(operationLimitsProperties[ii]);
                    uint value = (uint)property.GetValue(configOperationLimits);
                    if (values[ii] != null &&
                        ServiceResult.IsNotBad(errors[ii]))
                    {
                        uint serverValue = (uint)values[ii];
                        if (serverValue > 0 &&
                           (value == 0 || serverValue < value))
                        {
                            value = serverValue;
                        }
                    }
                    property.SetValue(operationLimits, value);
                }

                OperationLimits = operationLimits;
            }
            catch (Exception ex)
            {
                Utils.LogError(ex, "Failed to read operation limits from server. Using configuration defaults.");
                var operationLimits = m_configuration?.ClientConfiguration?.OperationLimits;
                if (operationLimits != null)
                {
                    OperationLimits = operationLimits;
                }
            }
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
                var subTypes = new ExpandedNodeIdCollection();
                foreach (IReference reference in node.Find(ReferenceTypeIds.HasSubtype, false))
                {
                    subTypes.Add(reference.TargetId);
                }
                if (subTypes.Count > 0)
                {
                    FetchTypeTree(subTypes);
                }
            }
        }

        /// <summary>
        /// Updates the cache with the types and its subtypes.
        /// </summary>
        /// <remarks>
        /// This method can be used to ensure the TypeTree is populated.
        /// </remarks>
        public void FetchTypeTree(ExpandedNodeIdCollection typeIds)
        {
            var referenceTypeIds = new NodeIdCollection() { ReferenceTypeIds.HasSubtype };
            IList<INode> nodes = NodeCache.FindReferences(typeIds, referenceTypeIds, false, false);
            var subTypes = new ExpandedNodeIdCollection();
            foreach (INode inode in nodes)
            {
                if (inode is Node node)
                {
                    foreach (IReference reference in node.Find(ReferenceTypeIds.HasSubtype, false))
                    {
                        if (!typeIds.Contains(reference.TargetId))
                        {
                            subTypes.Add(reference.TargetId);
                        }
                    }
                }
            }
            if (subTypes.Count > 0)
            {
                FetchTypeTree(subTypes);
            }
        }

        /// <summary>
        /// Returns the available encodings for a node
        /// </summary>
        /// <param name="variableId">The variable node.</param>
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
        ///  Returns the data dictionary that contains the description.
        /// </summary>
        /// <param name="descriptionId">The description id.</param>
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

            IList<INode> references = this.NodeCache.FindReferences(descriptionId, ReferenceTypeIds.HasComponent, true, false);
            if (references.Count == 0)
            {
                throw ServiceResultException.Create(StatusCodes.BadNodeIdInvalid, "Description does not refer to a valid data dictionary.");
            }

            // load the dictionary.
            NodeId dictionaryId = ExpandedNodeId.ToNodeId(references[0].NodeId, m_namespaceUris);

            DataDictionary dictionaryToLoad = new DataDictionary(this);

            await dictionaryToLoad.Load(references[0]).ConfigureAwait(false);

            m_dictionaries[dictionaryId] = dictionaryToLoad;

            return dictionaryToLoad;
        }

        /// <summary>
        ///  Returns the data dictionary that contains the description.
        /// </summary>
        /// <param name="dictionaryNode">The dictionary id.</param>
        /// <param name="forceReload"></param>
        /// <returns>The dictionary.</returns>
        public async Task<DataDictionary> LoadDataDictionary(ReferenceDescription dictionaryNode, bool forceReload = false)
        {
            // check if the dictionary has already been loaded.
            DataDictionary dictionary;
            NodeId dictionaryId = ExpandedNodeId.ToNodeId(dictionaryNode.NodeId, m_namespaceUris);
            if (!forceReload &&
                m_dictionaries.TryGetValue(dictionaryId, out dictionary))
            {
                return dictionary;
            }

            // load the dictionary.
            DataDictionary dictionaryToLoad = new DataDictionary(this);
            await dictionaryToLoad.Load(dictionaryId, dictionaryNode.ToString()).ConfigureAwait(false);
            m_dictionaries[dictionaryId] = dictionaryToLoad;
            return dictionaryToLoad;
        }

        /// <summary>
        /// Loads all dictionaries of the OPC binary or Xml schema type system.
        /// </summary>
        /// <param name="dataTypeSystem">The type system.</param>
        public async Task<Dictionary<NodeId, DataDictionary>> LoadDataTypeSystem(NodeId dataTypeSystem = null)
        {
            if (dataTypeSystem == null)
            {
                dataTypeSystem = ObjectIds.OPCBinarySchema_TypeSystem;
            }
            else
            if (!Utils.IsEqual(dataTypeSystem, ObjectIds.OPCBinarySchema_TypeSystem) &&
                !Utils.IsEqual(dataTypeSystem, ObjectIds.XmlSchema_TypeSystem))
            {
                throw ServiceResultException.Create(StatusCodes.BadNodeIdInvalid, $"{nameof(dataTypeSystem)} does not refer to a valid data dictionary.");
            }

            // find the dictionary for the description.
            IList<INode> references = this.NodeCache.FindReferences(dataTypeSystem, ReferenceTypeIds.HasComponent, false, false);

            if (references.Count == 0)
            {
                throw ServiceResultException.Create(StatusCodes.BadNodeIdInvalid, "Type system does not contain a valid data dictionary.");
            }

            // batch read all encodings and namespaces
            var referenceNodeIds = references.Select(r => r.NodeId).ToList();

            // find namespace properties
            var namespaceNodes = this.NodeCache.FindReferences(referenceNodeIds, new NodeIdCollection { ReferenceTypeIds.HasProperty }, false, false)
                .Where(n => n.BrowseName == BrowseNames.NamespaceUri).ToList();
            var namespaceNodeIds = namespaceNodes.Select(n => ExpandedNodeId.ToNodeId(n.NodeId, this.NamespaceUris)).ToList();

            // read all schema definitions
            var referenceExpandedNodeIds = references
                .Select(r => ExpandedNodeId.ToNodeId(r.NodeId, this.NamespaceUris))
                .Where(n => n.NamespaceIndex != 0).ToList();
            IDictionary<NodeId, byte[]> schemas = await DataDictionary.ReadDictionaries(this, referenceExpandedNodeIds).ConfigureAwait(false);

            // read namespace property values
            var namespaces = new Dictionary<NodeId, string>();
            ReadValues(namespaceNodeIds, Enumerable.Repeat(typeof(string), namespaceNodeIds.Count).ToList(), out var nameSpaceValues, out var errors);

            // build the namespace dictionary
            for (int ii = 0; ii < nameSpaceValues.Count; ii++)
            {
                if (StatusCode.IsNotBad(errors[ii].StatusCode))
                {
                    namespaces[((NodeId)referenceNodeIds[ii])] = (string)nameSpaceValues[ii];
                }
            }

            // build the namespace/schema import dictionary
            var imports = new Dictionary<string, byte[]>();
            foreach (var r in references)
            {
                NodeId nodeId = ExpandedNodeId.ToNodeId(r.NodeId, NamespaceUris);
                if (schemas.TryGetValue(nodeId, out var schema))
                {
                    imports[namespaces[nodeId]] = schema;
                }
            }

            // read all type dictionaries in the type system
            foreach (var r in references)
            {
                DataDictionary dictionaryToLoad = null;
                NodeId dictionaryId = ExpandedNodeId.ToNodeId(r.NodeId, m_namespaceUris);
                if (dictionaryId.NamespaceIndex != 0 &&
                    !m_dictionaries.TryGetValue(dictionaryId, out dictionaryToLoad))
                {
                    try
                    {
                        dictionaryToLoad = new DataDictionary(this);
                        if (schemas.TryGetValue(dictionaryId, out var schema))
                        {
                            await dictionaryToLoad.Load(dictionaryId, dictionaryId.ToString(), schema, imports).ConfigureAwait(false);
                        }
                        else
                        {
                            await dictionaryToLoad.Load(dictionaryId, dictionaryId.ToString()).ConfigureAwait(false);
                        }
                        m_dictionaries[dictionaryId] = dictionaryToLoad;
                    }
                    catch (Exception ex)
                    {
                        Utils.LogError("Dictionary load error for Dictionary {0} : {1}", r.NodeId, ex.Message);
                    }
                }
            }

            return m_dictionaries;
        }

        /// <summary>
        /// Reads the values for the node attributes and returns a node object collection.
        /// </summary>
        /// <remarks>
        /// If the nodeclass for the nodes in nodeIdCollection is already known,
        /// and passed as nodeClass, reads only values of required attributes.
        /// Otherwise NodeClass.Unspecified should be used.
        /// </remarks>
        /// <param name="nodeIds">The nodeId collection to read.</param>
        /// <param name="nodeClass">The nodeClass of all nodes in the collection. Set to <c>NodeClass.Unspecified</c> if the nodeclass is unknown.</param>
        /// <param name="nodeCollection">The node collection that is created from attributes read from the server.</param>
        /// <param name="errors">The errors that occured reading the nodes.</param>
        /// <param name="optionalAttributes">Set to <c>true</c> if optional attributes should not be omitted.</param>
        public void ReadNodes(
            IList<NodeId> nodeIds,
            NodeClass nodeClass,
            out IList<Node> nodeCollection,
            out IList<ServiceResult> errors,
            bool optionalAttributes = false)
        {
            if (nodeIds.Count == 0)
            {
                nodeCollection = new NodeCollection();
                errors = new List<ServiceResult>();
                return;
            }

            if (nodeClass == NodeClass.Unspecified)
            {
                ReadNodes(nodeIds, out nodeCollection, out errors, optionalAttributes);
                return;
            }

            // determine attributes to read for nodeclass
            var attributesPerNodeId = new List<IDictionary<uint, DataValue>>(nodeIds.Count);
            var attributesToRead = new ReadValueIdCollection();
            nodeCollection = new NodeCollection(nodeIds.Count);

            CreateNodeClassAttributesReadNodesRequest(
                nodeIds, nodeClass,
                attributesToRead, attributesPerNodeId,
                nodeCollection, optionalAttributes);

            ResponseHeader responseHeader = Read(
                null,
                0,
                TimestampsToReturn.Neither,
                attributesToRead,
                out DataValueCollection values,
                out DiagnosticInfoCollection diagnosticInfos);

            ClientBase.ValidateResponse(values, attributesToRead);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, attributesToRead);

            errors = new ServiceResult[nodeIds.Count].ToList();
            ProcessAttributesReadNodesResponse(
                responseHeader,
                attributesToRead, attributesPerNodeId,
                values, diagnosticInfos,
                nodeCollection, errors);
        }

        /// <summary>
        /// Reads the values for the node attributes and returns a node object.
        /// Reads the nodeclass of the nodeIds, then reads
        /// the values for the node attributes and returns a node object collection.
        /// </summary>
        /// <param name="nodeIds">The nodeId collection.</param>
        /// <param name="nodeCollection">The node collection read from the server.</param>
        /// <param name="errors">The errors occured reading the nodes.</param>
        /// <param name="optionalAttributes">Set to <c>true</c> if optional attributes should not be omitted.</param>
        public void ReadNodes(
            IList<NodeId> nodeIds,
            out IList<Node> nodeCollection,
            out IList<ServiceResult> errors,
            bool optionalAttributes = false)
        {
            int count = nodeIds.Count;
            nodeCollection = new NodeCollection(count);
            errors = new List<ServiceResult>(count);

            if (count == 0)
            {
                return;
            }

            // first read only nodeclasses for nodes from server.
            var itemsToRead = new ReadValueIdCollection(
                nodeIds.Select(nodeId =>
                    new ReadValueId {
                        NodeId = nodeId,
                        AttributeId = Attributes.NodeClass
                    }));

            DataValueCollection nodeClassValues = null;
            DiagnosticInfoCollection diagnosticInfos = null;
            ResponseHeader responseHeader = null;

            if (count > 1)
            {
                responseHeader = Read(
                    null,
                    0,
                    TimestampsToReturn.Neither,
                    itemsToRead,
                    out nodeClassValues,
                    out diagnosticInfos);

                ClientBase.ValidateResponse(nodeClassValues, itemsToRead);
                ClientBase.ValidateDiagnosticInfos(diagnosticInfos, itemsToRead);
            }
            else
            {
                // for a single node read all attributes to skip the first service call
                nodeClassValues = new DataValueCollection() {
                    new DataValue(new Variant((int)NodeClass.Unspecified),
                    statusCode: StatusCodes.Good)
                    };
            }

            // second determine attributes to read per nodeclass
            var attributesPerNodeId = new List<IDictionary<uint, DataValue>>(count);
            var attributesToRead = new ReadValueIdCollection();

            CreateAttributesReadNodesRequest(
                responseHeader,
                itemsToRead, nodeClassValues, diagnosticInfos,
                attributesToRead, attributesPerNodeId,
                nodeCollection, errors,
                optionalAttributes);

            if (attributesToRead.Count > 0)
            {
                responseHeader = Read(
                    null,
                    0,
                    TimestampsToReturn.Neither,
                    attributesToRead,
                    out DataValueCollection values,
                    out diagnosticInfos);

                ClientBase.ValidateResponse(values, attributesToRead);
                ClientBase.ValidateDiagnosticInfos(diagnosticInfos, attributesToRead);

                ProcessAttributesReadNodesResponse(
                    responseHeader,
                    attributesToRead, attributesPerNodeId,
                    values, diagnosticInfos,
                    nodeCollection, errors);
            }
        }

        /// <summary>
        /// Reads the values for the node attributes and returns a node object.
        /// </summary>
        /// <param name="nodeId">The nodeId.</param>
        public Node ReadNode(NodeId nodeId)
        {
            return ReadNode(nodeId, NodeClass.Unspecified, true);
        }

        /// <summary>
        /// Reads the values for the node attributes and returns a node object.
        /// </summary>
        /// <remarks>
        /// If the nodeclass is known, only the supported attribute values are read.
        /// </remarks>
        /// <param name="nodeId">The nodeId.</param>
        /// <param name="nodeClass">The nodeclass of the node to read.</param>
        /// <param name="optionalAttributes">Read optional attributes.</param>
        public Node ReadNode(
            NodeId nodeId,
            NodeClass nodeClass,
            bool optionalAttributes = true)
        {
            // build list of attributes.
            var attributes = CreateAttributes(nodeClass, optionalAttributes);

            // build list of values to read.
            ReadValueIdCollection itemsToRead = new ReadValueIdCollection();
            foreach (uint attributeId in attributes.Keys)
            {
                ReadValueId itemToRead = new ReadValueId {
                    NodeId = nodeId,
                    AttributeId = attributeId
                };
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

            return ProcessReadResponse(responseHeader, attributes, itemsToRead, values, diagnosticInfos);
        }

        /// <summary>
        /// Reads the value for a node.
        /// </summary>
        /// <param name="nodeId">The node Id.</param>
        public DataValue ReadValue(NodeId nodeId)
        {
            ReadValueId itemToRead = new ReadValueId {
                NodeId = nodeId,
                AttributeId = Attributes.Value
            };

            ReadValueIdCollection itemsToRead = new ReadValueIdCollection {
                itemToRead
            };

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
        /// Reads the values for a node collection. Returns diagnostic errors.
        /// </summary>
        /// <param name="nodeIds">The node Id.</param>
        /// <param name="values">The data values read from the server.</param>
        /// <param name="errors">The errors reported by the server.</param>
        public void ReadValues(
            IList<NodeId> nodeIds,
            out DataValueCollection values,
            out IList<ServiceResult> errors)
        {
            if (nodeIds.Count == 0)
            {
                values = new DataValueCollection();
                errors = new List<ServiceResult>();
                return;
            }

            // read all values from server.
            var itemsToRead = new ReadValueIdCollection(
                nodeIds.Select(nodeId =>
                    new ReadValueId {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value
                    }));

            // read from server.
            errors = new List<ServiceResult>(itemsToRead.Count);

            ResponseHeader responseHeader = Read(
                null,
                0,
                TimestampsToReturn.Both,
                itemsToRead,
                out values,
                out DiagnosticInfoCollection diagnosticInfos);

            ClientBase.ValidateResponse(values, itemsToRead);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, itemsToRead);

            int ii = 0;
            foreach (var value in values)
            {
                ServiceResult result = ServiceResult.Good;
                if (StatusCode.IsNotGood(value.StatusCode))
                {
                    result = ClientBase.GetResult(value.StatusCode, ii, diagnosticInfos, responseHeader);
                }
                errors.Add(result);
                ii++;
            }
        }

        /// <summary>
        /// Reads the value for a node an checks that it is the specified type.
        /// </summary>
        /// <param name="nodeId">The node id.</param>
        /// <param name="expectedType">The expected type.</param>
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
        /// Fetches all references for the specified nodes.
        /// </summary>
        /// <param name="nodeIds">The node id collection.</param>
        /// <param name="referenceDescriptions">A list of reference collections.</param>
        /// <param name="errors">The errors reported by the server.</param>
        public void FetchReferences(
            IList<NodeId> nodeIds,
            out IList<ReferenceDescriptionCollection> referenceDescriptions,
            out IList<ServiceResult> errors)
        {
            var result = new List<ReferenceDescriptionCollection>();

            // browse for all references.
            Browse(
                null,
                null,
                nodeIds,
                0,
                BrowseDirection.Both,
                null,
                true,
                0,
                out ByteStringCollection continuationPoints,
                out IList<ReferenceDescriptionCollection> descriptions,
                out errors);

            result.AddRange(descriptions);

            // process any continuation point.
            var previousResult = result;
            var previousErrors = errors;
            while (HasAnyContinuationPoint(continuationPoints))
            {
                var nextContinuationPoints = new ByteStringCollection();
                var nextResult = new List<ReferenceDescriptionCollection>();
                var nextErrors = new List<ServiceResult>();

                for (int ii = 0; ii < continuationPoints.Count; ii++)
                {
                    var cp = continuationPoints[ii];
                    if (cp != null)
                    {
                        nextContinuationPoints.Add(cp);
                        nextResult.Add(previousResult[ii]);
                        nextErrors.Add(previousErrors[ii]);
                    }
                }

                BrowseNext(
                    null,
                    false,
                    nextContinuationPoints,
                    out ByteStringCollection revisedContinuationPoints,
                    out descriptions,
                    out IList<ServiceResult> browseNextErrors);

                continuationPoints = revisedContinuationPoints;
                previousResult = nextResult;
                previousErrors = nextErrors;

                for (int ii = 0; ii < descriptions.Count; ii++)
                {
                    nextResult[ii].AddRange(descriptions[ii]);
                    if (StatusCode.IsBad(browseNextErrors[ii].StatusCode))
                    {
                        nextErrors[ii] = browseNextErrors[ii];
                    }
                }
            }

            referenceDescriptions = result;
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
        public void Open(
            string sessionName,
            uint sessionTimeout,
            IUserIdentity identity,
            IList<string> preferredLocales)
        {
            Open(sessionName, sessionTimeout, identity, preferredLocales, true);
        }

        /// <summary>
        /// Establishes a session with the server.
        /// </summary>
        /// <param name="sessionName">The name to assign to the session.</param>
        /// <param name="sessionTimeout">The session timeout.</param>
        /// <param name="identity">The user identity.</param>
        /// <param name="preferredLocales">The list of preferred locales.</param>
        /// <param name="checkDomain">If set to <c>true</c> then the domain in the certificate must match the endpoint used.</param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA1506:AvoidExcessiveClassCoupling")]
        public void Open(
            string sessionName,
            uint sessionTimeout,
            IUserIdentity identity,
            IList<string> preferredLocales,
            bool checkDomain)
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

            // catch security policies which are not supported by core
            if (SecurityPolicies.GetDisplayName(securityPolicyUri) == null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadSecurityChecksFailed,
                    "The chosen security policy is not supported by the client to connect to the server.");
            }

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
                        "Endpoint does not support the user identity type provided.");
                }

                identityToken.PolicyId = identityPolicy.PolicyId;
            }

            bool requireEncryption = securityPolicyUri != SecurityPolicies.None;

            if (!requireEncryption)
            {
                requireEncryption = identityPolicy.SecurityPolicyUri != SecurityPolicies.None &&
                    !String.IsNullOrEmpty(identityPolicy.SecurityPolicyUri);
            }

            // validate the server certificate /certificate chain.
            X509Certificate2 serverCertificate = null;
            byte[] certificateData = m_endpoint.Description.ServerCertificate;

            if (certificateData != null && certificateData.Length > 0)
            {
                X509Certificate2Collection serverCertificateChain = Utils.ParseCertificateChainBlob(certificateData);

                if (serverCertificateChain.Count > 0)
                {
                    serverCertificate = serverCertificateChain[0];
                }

                if (requireEncryption)
                {
                    if (checkDomain)
                    {
                        m_configuration.CertificateValidator.Validate(serverCertificateChain, m_endpoint);
                    }
                    else
                    {
                        m_configuration.CertificateValidator.Validate(serverCertificateChain);
                    }
                    // save for reconnect
                    m_checkDomain = checkDomain;
                }
            }

            // create a nonce.
            uint length = (uint)m_configuration.SecurityConfiguration.NonceLength;
            byte[] clientNonce = Utils.Nonce.CreateNonce(length);
            NodeId sessionId = null;
            NodeId sessionCookie = null;
            byte[] serverNonce = Array.Empty<byte>();
            byte[] serverCertificateData = Array.Empty<byte>();
            SignatureData serverSignature = null;
            EndpointDescriptionCollection serverEndpoints = null;
            SignedSoftwareCertificateCollection serverSoftwareCertificates = null;

            // send the application instance certificate for the client.
            byte[] clientCertificateData = m_instanceCertificate != null ? m_instanceCertificate.RawData : null;
            byte[] clientCertificateChainData = null;

            if (m_instanceCertificateChain != null && m_instanceCertificateChain.Count > 0 && m_configuration.SecurityConfiguration.SendCertificateChain)
            {
                List<byte> clientCertificateChain = new List<byte>();

                for (int i = 0; i < m_instanceCertificateChain.Count; i++)
                {
                    clientCertificateChain.AddRange(m_instanceCertificateChain[i].RawData);
                }

                clientCertificateChainData = clientCertificateChain.ToArray();
            }

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
                    base.CreateSession(
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
                    Utils.LogInfo("Create session failed with client certificate NULL. " + ex.Message);
                    successCreateSession = false;
                }
            }

            if (!successCreateSession)
            {
                base.CreateSession(
                        null,
                        clientDescription,
                        m_endpoint.Description.Server.ApplicationUri,
                        m_endpoint.EndpointUrl.ToString(),
                        sessionName,
                        clientNonce,
                        clientCertificateChainData != null ? clientCertificateChainData : clientCertificateData,
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

            Utils.LogInfo("Revised session timeout value: {0}. ", m_sessionTimeout);
            Utils.LogInfo("Max response message size value: {0}. Max request message size: {1} ",
                MessageContext.MaxMessageSize, m_maxRequestMessageSize);

            //we need to call CloseSession if CreateSession was successful but some other exception is thrown
            try
            {
                // verify that the server returned the same instance certificate.
                if (serverCertificateData != null &&
                    m_endpoint.Description.ServerCertificate != null &&
                    !Utils.IsEqual(serverCertificateData, m_endpoint.Description.ServerCertificate))
                {
                    try
                    {
                        // verify for certificate chain in endpoint.
                        X509Certificate2Collection serverCertificateChain = Utils.ParseCertificateChainBlob(m_endpoint.Description.ServerCertificate);

                        if (serverCertificateChain.Count > 0 && !Utils.IsEqual(serverCertificateData, serverCertificateChain[0].RawData))
                        {
                            throw ServiceResultException.Create(
                                        StatusCodes.BadCertificateInvalid,
                                        "Server did not return the certificate used to create the secure channel.");
                        }
                    }
                    catch (Exception)
                    {
                        throw ServiceResultException.Create(
                                StatusCodes.BadCertificateInvalid,
                                "Server did not return the certificate used to create the secure channel.");
                    }
                }

                if (serverSignature == null || serverSignature.Signature == null)
                {
                    Utils.LogInfo("Server signature is null or empty.");

                    //throw ServiceResultException.Create(
                    //    StatusCodes.BadSecurityChecksFailed,
                    //    "Server signature is null or empty.");
                }

                if (m_discoveryServerEndpoints != null && m_discoveryServerEndpoints.Count > 0)
                {
                    // Compare EndpointDescriptions returned at GetEndpoints with values returned at CreateSession
                    EndpointDescriptionCollection expectedServerEndpoints = null;

                    if (serverEndpoints != null &&
                        m_discoveryProfileUris != null && m_discoveryProfileUris.Count > 0)
                    {
                        // Select EndpointDescriptions with a transportProfileUri that matches the
                        // profileUris specified in the original GetEndpoints() request.
                        expectedServerEndpoints = new EndpointDescriptionCollection();

                        foreach (EndpointDescription serverEndpoint in serverEndpoints)
                        {
                            if (m_discoveryProfileUris.Contains(serverEndpoint.TransportProfileUri))
                            {
                                expectedServerEndpoints.Add(serverEndpoint);
                            }
                        }
                    }
                    else
                    {
                        expectedServerEndpoints = serverEndpoints;
                    }

                    if (expectedServerEndpoints == null ||
                        m_discoveryServerEndpoints.Count != expectedServerEndpoints.Count)
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadSecurityChecksFailed,
                            "Server did not return a number of ServerEndpoints that matches the one from GetEndpoints.");
                    }

                    for (int ii = 0; ii < expectedServerEndpoints.Count; ii++)
                    {
                        EndpointDescription serverEndpoint = expectedServerEndpoints[ii];
                        EndpointDescription expectedServerEndpoint = m_discoveryServerEndpoints[ii];

                        if (serverEndpoint.SecurityMode != expectedServerEndpoint.SecurityMode ||
                            serverEndpoint.SecurityPolicyUri != expectedServerEndpoint.SecurityPolicyUri ||
                            serverEndpoint.TransportProfileUri != expectedServerEndpoint.TransportProfileUri ||
                            serverEndpoint.SecurityLevel != expectedServerEndpoint.SecurityLevel)
                        {
                            throw ServiceResultException.Create(
                                StatusCodes.BadSecurityChecksFailed,
                                "The list of ServerEndpoints returned at CreateSession does not match the list from GetEndpoints.");
                        }

                        if (serverEndpoint.UserIdentityTokens.Count != expectedServerEndpoint.UserIdentityTokens.Count)
                        {
                            throw ServiceResultException.Create(
                                StatusCodes.BadSecurityChecksFailed,
                                "The list of ServerEndpoints returned at CreateSession does not match the one from GetEndpoints.");
                        }

                        for (int jj = 0; jj < serverEndpoint.UserIdentityTokens.Count; jj++)
                        {
                            if (!serverEndpoint.UserIdentityTokens[jj].IsEqual(expectedServerEndpoint.UserIdentityTokens[jj]))
                            {
                                throw ServiceResultException.Create(
                                StatusCodes.BadSecurityChecksFailed,
                                "The list of ServerEndpoints returned at CreateSession does not match the one from GetEndpoints.");
                            }
                        }
                    }
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
                    // validate the signature with complete chain if the check with leaf certificate failed.
                    if (clientCertificateChainData != null)
                    {
                        dataToSign = Utils.Append(clientCertificateChainData, clientNonce);

                        if (!SecurityPolicies.Verify(serverCertificate, m_endpoint.Description.SecurityPolicyUri, dataToSign, serverSignature))
                        {
                            throw ServiceResultException.Create(
                                StatusCodes.BadApplicationSignatureInvalid,
                                "Server did not provide a correct signature for the nonce data provided by the client.");
                        }
                    }
                    else
                    {
                        throw ServiceResultException.Create(
                           StatusCodes.BadApplicationSignatureInvalid,
                           "Server did not provide a correct signature for the nonce data provided by the client.");
                    }
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
                dataToSign = Utils.Append(serverCertificate != null ? serverCertificate.RawData : null, serverNonce);
                SignatureData clientSignature = SecurityPolicies.Sign(m_instanceCertificate, securityPolicyUri, dataToSign);

                // select the security policy for the user token.
                securityPolicyUri = identityPolicy.SecurityPolicyUri;

                if (String.IsNullOrEmpty(securityPolicyUri))
                {
                    securityPolicyUri = m_endpoint.Description.SecurityPolicyUri;
                }

                byte[] previousServerNonce = null;

                if (TransportChannel.CurrentToken != null)
                {
                    previousServerNonce = TransportChannel.CurrentToken.ServerNonce;
                }

                // validate server nonce and security parameters for user identity.
                ValidateServerNonce(
                    identity,
                    serverNonce,
                    securityPolicyUri,
                    previousServerNonce,
                    m_endpoint.Description.SecurityMode);

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

                if (certificateResults != null)
                {
                    for (int i = 0; i < certificateResults.Count; i++)
                    {
                        Utils.LogInfo("ActivateSession result[{0}] = {1}", i, certificateResults[i]);
                    }
                }

                if (certificateResults == null || certificateResults.Count == 0)
                {
                    Utils.LogInfo("Empty results were received for the ActivateSession call.");
                }

                // fetch namespaces.
                FetchNamespaceTables();

                lock (SyncRoot)
                {
                    // save nonces.
                    m_sessionName = sessionName;
                    m_identity = identity;
                    m_previousServerNonce = previousServerNonce;
                    m_serverNonce = serverNonce;
                    m_serverCertificate = serverCertificate;

                    // update system context.
                    m_systemContext.PreferredLocales = m_preferredLocales;
                    m_systemContext.SessionId = this.SessionId;
                    m_systemContext.UserIdentity = identity;
                }

                // fetch operation limits
                FetchOperationLimits();

                // start keep alive thread.
                StartKeepAliveTimer();
            }
            catch (Exception)
            {
                try
                {
                    CloseSession(null, false);
                    CloseChannel();
                }
                catch (Exception e)
                {
                    Utils.LogError("Cleanup: CloseSession() or CloseChannel() raised exception. " + e.Message);
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
            byte[] dataToSign = Utils.Append(m_serverCertificate != null ? m_serverCertificate.RawData : null, serverNonce);
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
                    "Endpoint does not support the user identity type provided.");
            }

            // select the security policy for the user token.
            securityPolicyUri = identityPolicy.SecurityPolicyUri;

            if (String.IsNullOrEmpty(securityPolicyUri))
            {
                securityPolicyUri = m_endpoint.Description.SecurityPolicyUri;
            }

            bool requireEncryption = securityPolicyUri != SecurityPolicies.None;

            // validate the server certificate before encrypting tokens.
            if (m_serverCertificate != null && requireEncryption && identity.TokenType != UserTokenType.Anonymous)
            {
                m_configuration.CertificateValidator.Validate(m_serverCertificate);
            }

            // validate server nonce and security parameters for user identity.
            ValidateServerNonce(
                identity,
                serverNonce,
                securityPolicyUri,
                m_previousServerNonce,
                m_endpoint.Description.SecurityMode);

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

                m_previousServerNonce = m_serverNonce;
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
                0,
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
                    if (StatusCode.IsBad(results[ii].StatusCode))
                    {
                        continue;
                    }
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
            out IList<string> displayNames,
            out IList<ServiceResult> errors)
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
                TimestampsToReturn.Neither,
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
        #endregion

        #region Close Methods
        /// <summary>
        /// Disconnects from the server and frees any network resources.
        /// </summary>
        public override StatusCode Close()
        {
            return Close(m_keepAliveInterval, true);
        }

        /// <summary>
        /// Close the session with the server and optionally closes the channel.
        /// </summary>
        /// <param name="closeChannel"></param>
        /// <returns></returns>
        public StatusCode Close(bool closeChannel)
        {
            return Close(m_keepAliveInterval, closeChannel);
        }

        /// <summary>
        /// Disconnects from the server and frees any network resources with the specified timeout.
        /// </summary>
        public StatusCode Close(int timeout)
            => Close(timeout, true);

        /// <summary>
        /// Disconnects from the server and frees any network resources with the specified timeout.
        /// </summary>
        public virtual StatusCode Close(int timeout, bool closeChannel)
        {
            // check if already called.
            if (Disposed)
            {
                return StatusCodes.Good;
            }

            StatusCode result = StatusCodes.Good;

            // stop the keep alive timer.
            Utils.SilentDispose(m_keepAliveTimer);
            m_keepAliveTimer = null;

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
                        Utils.LogError(e, "Session: Unexpected eror raising SessionClosing event.");
                    }
                }
            }

            // close the session with the server.
            if (connected && !KeepAliveStopped)
            {
                int existingTimeout = this.OperationTimeout;

                try
                {
                    // close the session and delete all subscriptions if specified.
                    this.OperationTimeout = timeout;
                    CloseSession(null, m_deleteSubscriptionsOnClose);
                    this.OperationTimeout = existingTimeout;

                    if (closeChannel)
                    {
                        CloseChannel();
                    }

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

                    Utils.LogError("Session close error: " + result);
                }
            }

            // clean up.
            if (closeChannel)
            {
                Dispose();
            }

            return result;
        }
        #endregion

        #region Subscription Methods
        /// <summary>
        /// Adds a subscription to the session.
        /// </summary>
        /// <param name="subscription">The subscription to add.</param>
        public bool AddSubscription(Subscription subscription)
        {
            if (subscription == null) throw new ArgumentNullException(nameof(subscription));

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
        public bool RemoveSubscription(Subscription subscription)
        {
            if (subscription == null) throw new ArgumentNullException(nameof(subscription));

            if (subscription.Created)
            {
                subscription.Delete(false);
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
        /// Removes a list of subscriptions from the session.
        /// </summary>
        /// <param name="subscriptions">The list of subscriptions to remove.</param>
        public bool RemoveSubscriptions(IEnumerable<Subscription> subscriptions)
        {
            if (subscriptions == null) throw new ArgumentNullException(nameof(subscriptions));

            List<Subscription> subscriptionsToDelete = new List<Subscription>();
            bool removed = PrepareSubscriptionsToDelete(subscriptions, subscriptionsToDelete);

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

            return removed;
        }

        /// <summary>
        /// Removes a transferred subscription from the session.
        /// Called by the session to which the subscription
        /// is transferred to obtain ownership. Internal.
        /// </summary>
        /// <param name="subscription">The subscription to remove.</param>
        public bool RemoveTransferredSubscription(Subscription subscription)
        {
            if (subscription == null) throw new ArgumentNullException(nameof(subscription));

            if (subscription.Session != this)
            {
                return false;
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
        /// Transfers a list of Subscriptions from another session.
        /// </summary>
        public bool TransferSubscriptions(
            SubscriptionCollection subscriptions,
            bool sendInitialValues)
        {
            var subscriptionIds = new UInt32Collection();
            foreach (var subscription in subscriptions)
            {
                if (subscription.Created && SessionId.Equals(subscription.Session.SessionId))
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidState, Utils.Format("The subscriptionId {0} is already created.", subscription.Id));
                }
                if (subscription.TransferId == 0)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidState, Utils.Format("A subscription can not be transferred due to missing transfer Id."));
                }
                subscriptionIds.Add(subscription.TransferId);
            }

            lock (SyncRoot)
            {
                if (subscriptionIds.Count > 0)
                {
                    ResponseHeader responseHeader = TransferSubscriptions(null, subscriptionIds, sendInitialValues, out var results, out var diagnosticInfos);
                    if (!StatusCode.IsGood(responseHeader.ServiceResult))
                    {
                        Utils.LogError("TransferSubscription failed: {0}", responseHeader.ServiceResult);
                        return false;
                    }
                    ClientBase.ValidateResponse(results, subscriptionIds);
                    ClientBase.ValidateDiagnosticInfos(diagnosticInfos, subscriptionIds);
                    var failedSubscriptionIds = new UInt32Collection();

                    for (int ii = 0; ii < subscriptions.Count; ii++)
                    {
                        if (StatusCode.IsGood(results[ii].StatusCode))
                        {
                            if (subscriptions[ii].Transfer(this, subscriptionIds[ii], results[ii].AvailableSequenceNumbers))
                            {   // create ack for available sequence numbers
                                foreach (var sequenceNumber in results[ii].AvailableSequenceNumbers)
                                {
                                    var ack = new SubscriptionAcknowledgement() {
                                        SubscriptionId = subscriptionIds[ii],
                                        SequenceNumber = sequenceNumber
                                    };
                                    m_acknowledgementsToSend.Add(ack);
                                }
                            }
                        }
                        else
                        {
                            Utils.LogError("SubscriptionId {0} failed to transfer, StatusCode={1}", subscriptionIds[ii], results[ii].StatusCode);
                            failedSubscriptionIds.Add(subscriptions[ii].TransferId);
                        }
                    }
                    if (failedSubscriptionIds.Count > 0)
                    {
                        return false;
                    }
                }
                else
                {
                    Utils.LogInfo("No subscriptions. Transfersubscription skipped.");
                }
            }

            return true;
        }
        #endregion

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
        /// Invokes the Browse service. Handles multiple nodes for browse request.
        /// </summary>
        /// <param name="requestHeader">The request header.</param>
        /// <param name="view">The view to browse.</param>
        /// <param name="nodesToBrowse">The nodes to browse.</param>
        /// <param name="maxResultsToReturn">The maximum number of returned values.</param>
        /// <param name="browseDirection">The browse direction.</param>
        /// <param name="referenceTypeId">The reference type id.</param>
        /// <param name="includeSubtypes">If set to <c>true</c> the subtypes of the ReferenceType will be included in the browse.</param>
        /// <param name="nodeClassMask">The node class mask.</param>
        /// <param name="continuationPoints">The continuation points per browse.</param>
        /// <param name="referencesList">The list of node references collections.</param>
        /// <param name="errors"></param>
        public virtual ResponseHeader Browse(
            RequestHeader requestHeader,
            ViewDescription view,
            IList<NodeId> nodesToBrowse,
            uint maxResultsToReturn,
            BrowseDirection browseDirection,
            NodeId referenceTypeId,
            bool includeSubtypes,
            uint nodeClassMask,
            out ByteStringCollection continuationPoints,
            out IList<ReferenceDescriptionCollection> referencesList,
            out IList<ServiceResult> errors)
        {

            BrowseDescriptionCollection browseDescription = new BrowseDescriptionCollection();
            foreach (var nodeToBrowse in nodesToBrowse)
            {
                BrowseDescription description = new BrowseDescription {
                    NodeId = nodeToBrowse,
                    BrowseDirection = browseDirection,
                    ReferenceTypeId = referenceTypeId,
                    IncludeSubtypes = includeSubtypes,
                    NodeClassMask = nodeClassMask,
                    ResultMask = (uint)BrowseResultMask.All
                };

                browseDescription.Add(description);
            }

            ResponseHeader responseHeader = Browse(
                requestHeader,
                view,
                maxResultsToReturn,
                browseDescription,
                out BrowseResultCollection results,
                out DiagnosticInfoCollection diagnosticInfos);

            ClientBase.ValidateResponse(results, browseDescription);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, browseDescription);

            int ii = 0;
            errors = new List<ServiceResult>();
            continuationPoints = new ByteStringCollection();
            referencesList = new List<ReferenceDescriptionCollection>();
            foreach (var result in results)
            {
                if (StatusCode.IsBad(result.StatusCode))
                {
                    errors.Add(new ServiceResult(result.StatusCode, ii, diagnosticInfos, responseHeader.StringTable));
                }
                else
                {
                    errors.Add(ServiceResult.Good);
                }
                continuationPoints.Add(result.ContinuationPoint);
                referencesList.Add(result.References);
                ii++;
            }

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
        /// Invokes the BrowseNext service. Handles multiple continuation points.
        /// </summary>
        public virtual ResponseHeader BrowseNext(
            RequestHeader requestHeader,
            bool releaseContinuationPoint,
            ByteStringCollection continuationPoints,
            out ByteStringCollection revisedContinuationPoints,
            out IList<ReferenceDescriptionCollection> referencesList,
            out IList<ServiceResult> errors)
        {
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

            int ii = 0;
            errors = new List<ServiceResult>();
            revisedContinuationPoints = new ByteStringCollection();
            referencesList = new List<ReferenceDescriptionCollection>();
            foreach (var result in results)
            {
                if (StatusCode.IsBad(result.StatusCode))
                {
                    errors.Add(new ServiceResult(result.StatusCode, ii, diagnosticInfos, responseHeader.StringTable));
                }
                else
                {
                    errors.Add(ServiceResult.Good);
                }
                revisedContinuationPoints.Add(result.ContinuationPoint);
                referencesList.Add(result.References);
                ii++;
            }

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

        #region Call Methods
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

            var nodesToRead = new ReadValueIdCollection() {
                // read the server state.
                new ReadValueId {
                    NodeId = Variables.Server_ServerStatus_State,
                    AttributeId = Attributes.Value,
                    DataEncoding = null,
                    IndexRange = null
                }
            };

            // restart the publish timer.
            lock (SyncRoot)
            {
                Utils.SilentDispose(m_keepAliveTimer);
                m_keepAliveTimer = null;

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
                Utils.LogError("Could not send keep alive request: {0} {1}", e.GetType().FullName, e.Message);
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
                Utils.LogError("Unexpected keep alive error occurred: {0}", e.Message);
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
                    Utils.LogError(e, "Session: Unexpected error invoking KeepAliveCallback.");
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

            Utils.LogInfo(
                "KEEP ALIVE LATE: {0}s, EndpointUrl={1}, RequestCount={2}/{3}",
                ((double)delta) / TimeSpan.TicksPerSecond,
                this.Endpoint.EndpointUrl,
                this.GoodPublishRequestCount,
                this.OutstandingRequestCount);

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
                    Utils.LogError(e, "Session: Unexpected error invoking KeepAliveCallback.");
                }
            }

            return true;
        }

        /// <summary>
        /// Prepare a list of subscriptions to delete.
        /// </summary>
        private bool PrepareSubscriptionsToDelete(IEnumerable<Subscription> subscriptions, IList<Subscription> subscriptionsToDelete)
        {
            bool removed = false;
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
            return removed;
        }

        /// <summary>
        /// Creates a read request with attributes determined by the NodeClass.
        /// </summary>
        private void CreateNodeClassAttributesReadNodesRequest(
            IList<NodeId> nodeIdCollection,
            NodeClass nodeClass,
            ReadValueIdCollection attributesToRead,
            IList<IDictionary<uint, DataValue>> attributesPerNodeId,
            IList<Node> nodeCollection,
            bool optionalAttributes)
        {
            for (int ii = 0; ii < nodeIdCollection.Count; ii++)
            {
                var node = new Node();
                node.NodeId = nodeIdCollection[ii];
                node.NodeClass = nodeClass;

                var attributes = CreateAttributes(node.NodeClass, optionalAttributes);
                foreach (uint attributeId in attributes.Keys)
                {
                    ReadValueId itemToRead = new ReadValueId {
                        NodeId = node.NodeId,
                        AttributeId = attributeId
                    };
                    attributesToRead.Add(itemToRead);
                }

                nodeCollection.Add(node);
                attributesPerNodeId.Add(attributes);
            }
        }

        /// <summary>
        /// Creates a read request with attributes determined by the NodeClass.
        /// </summary>
        private void CreateAttributesReadNodesRequest(
            ResponseHeader responseHeader,
            ReadValueIdCollection itemsToRead,
            DataValueCollection nodeClassValues,
            DiagnosticInfoCollection diagnosticInfos,
            ReadValueIdCollection attributesToRead,
            IList<IDictionary<uint, DataValue>> attributesPerNodeId,
            IList<Node> nodeCollection,
            IList<ServiceResult> errors,
            bool optionalAttributes
            )
        {
            int? nodeClass;
            for (int ii = 0; ii < itemsToRead.Count; ii++)
            {
                var node = new Node();
                node.NodeId = itemsToRead[ii].NodeId;
                if (!DataValue.IsGood(nodeClassValues[ii]))
                {
                    nodeCollection.Add(node);
                    errors.Add(new ServiceResult(nodeClassValues[ii].StatusCode, ii, diagnosticInfos, responseHeader.StringTable));
                    attributesPerNodeId.Add(null);
                    continue;
                }

                // check for valid node class.
                nodeClass = nodeClassValues[ii].Value as int?;

                if (nodeClass == null)
                {
                    nodeCollection.Add(node);
                    errors.Add(ServiceResult.Create(StatusCodes.BadUnexpectedError,
                        "Node does not have a valid value for NodeClass: {0}.", nodeClassValues[ii].Value));
                    attributesPerNodeId.Add(null);
                    continue;
                }

                node.NodeClass = (NodeClass)nodeClass;

                var attributes = CreateAttributes(node.NodeClass, optionalAttributes);
                foreach (uint attributeId in attributes.Keys)
                {
                    ReadValueId itemToRead = new ReadValueId {
                        NodeId = node.NodeId,
                        AttributeId = attributeId
                    };
                    attributesToRead.Add(itemToRead);
                }

                nodeCollection.Add(node);
                errors.Add(ServiceResult.Good);
                attributesPerNodeId.Add(attributes);
            }
        }

        /// <summary>
        /// Builds the node collection results based on the attribute values of the read response. 
        /// </summary>
        /// <param name="attributesToRead">The collection of all attributes to read passed in the read request.</param>
        /// <param name="attributesPerNodeId">The attributes requested per NodeId</param>
        /// <param name="values">The attribute values returned by the read request.</param>
        /// <param name="diagnosticInfos">The diagnostic info returned by the read request.</param>
        /// <param name="responseHeader">The response header of the read request.</param>
        /// <param name="nodeCollection">The node collection which holds the results.</param>
        /// <param name="errors">The service results for each node.</param>
        private void ProcessAttributesReadNodesResponse(
            ResponseHeader responseHeader,
            ReadValueIdCollection attributesToRead,
            IList<IDictionary<uint, DataValue>> attributesPerNodeId,
            DataValueCollection values,
            DiagnosticInfoCollection diagnosticInfos,
            IList<Node> nodeCollection,
            IList<ServiceResult> errors)
        {
            int readIndex = 0;
            for (int ii = 0; ii < nodeCollection.Count; ii++)
            {
                var attributes = attributesPerNodeId[ii];
                if (attributes == null)
                {
                    continue;
                }

                int readCount = attributes.Count;
                ReadValueIdCollection subRangeAttributes = new ReadValueIdCollection(attributesToRead.GetRange(readIndex, readCount));
                DataValueCollection subRangeValues = new DataValueCollection(values.GetRange(readIndex, readCount));
                DiagnosticInfoCollection subRangeDiagnostics = diagnosticInfos.Count > 0 ? new DiagnosticInfoCollection(diagnosticInfos.GetRange(readIndex, readCount)) : diagnosticInfos;
                try
                {
                    nodeCollection[ii] = ProcessReadResponse(responseHeader, attributes,
                        subRangeAttributes, subRangeValues, subRangeDiagnostics);
                    errors[ii] = ServiceResult.Good;
                }
                catch (ServiceResultException sre)
                {
                    errors[ii] = sre.Result;
                }
                readIndex += readCount;
            }
        }

        /// <summary>
        /// Creates a Node based on the read response.
        /// </summary>
        private Node ProcessReadResponse(
            ResponseHeader responseHeader,
            IDictionary<uint, DataValue> attributes,
            ReadValueIdCollection itemsToRead,
            DataValueCollection values,
            DiagnosticInfoCollection diagnosticInfos)
        {
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

                        // ignore errors on optional attributes
                        if (StatusCode.IsBad(values[ii].StatusCode))
                        {
                            if (attributeId == Attributes.AccessRestrictions ||
                                attributeId == Attributes.Description ||
                                attributeId == Attributes.RolePermissions ||
                                attributeId == Attributes.UserRolePermissions ||
                                attributeId == Attributes.UserWriteMask ||
                                attributeId == Attributes.WriteMask)
                            {
                                continue;
                            }
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

            Node node;
            DataValue value;
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

                    objectNode.EventNotifier = (byte)value.GetValue(typeof(byte));
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

                    objectTypeNode.IsAbstract = (bool)value.GetValue(typeof(bool));
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

                    variableNode.DataType = (NodeId)value.GetValue(typeof(NodeId));

                    // ValueRank Attribute
                    value = attributes[Attributes.ValueRank];

                    if (value == null)
                    {
                        throw ServiceResultException.Create(StatusCodes.BadUnexpectedError, "Variable does not support the ValueRank attribute.");
                    }

                    variableNode.ValueRank = (int)value.GetValue(typeof(int));

                    // ArrayDimensions Attribute
                    value = attributes[Attributes.ArrayDimensions];

                    if (value != null)
                    {
                        if (value.Value == null)
                        {
                            variableNode.ArrayDimensions = Array.Empty<uint>();
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

                    variableNode.AccessLevel = (byte)value.GetValue(typeof(byte));

                    // UserAccessLevel Attribute
                    value = attributes[Attributes.UserAccessLevel];

                    if (value == null)
                    {
                        throw ServiceResultException.Create(StatusCodes.BadUnexpectedError, "Variable does not support the UserAccessLevel attribute.");
                    }

                    variableNode.UserAccessLevel = (byte)value.GetValue(typeof(byte));

                    // Historizing Attribute
                    value = attributes[Attributes.Historizing];

                    if (value == null)
                    {
                        throw ServiceResultException.Create(StatusCodes.BadUnexpectedError, "Variable does not support the Historizing attribute.");
                    }

                    variableNode.Historizing = (bool)value.GetValue(typeof(bool));

                    // MinimumSamplingInterval Attribute
                    value = attributes[Attributes.MinimumSamplingInterval];

                    if (value != null)
                    {
                        variableNode.MinimumSamplingInterval = Convert.ToDouble(attributes[Attributes.MinimumSamplingInterval].Value);
                    }

                    // AccessLevelEx Attribute
                    value = attributes[Attributes.AccessLevelEx];

                    if (value != null)
                    {
                        variableNode.AccessLevelEx = (uint)value.GetValue(typeof(uint));
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

                    variableTypeNode.IsAbstract = (bool)value.GetValue(typeof(bool));

                    // DataType Attribute
                    value = attributes[Attributes.DataType];

                    if (value == null)
                    {
                        throw ServiceResultException.Create(StatusCodes.BadUnexpectedError, "VariableType does not support the DataType attribute.");
                    }

                    variableTypeNode.DataType = (NodeId)value.GetValue(typeof(NodeId));

                    // ValueRank Attribute
                    value = attributes[Attributes.ValueRank];

                    if (value == null)
                    {
                        throw ServiceResultException.Create(StatusCodes.BadUnexpectedError, "VariableType does not support the ValueRank attribute.");
                    }

                    variableTypeNode.ValueRank = (int)value.GetValue(typeof(int));

                    // ArrayDimensions Attribute
                    value = attributes[Attributes.ArrayDimensions];

                    if (value != null && value.Value != null)
                    {
                        variableTypeNode.ArrayDimensions = (uint[])value.GetValue(typeof(uint[]));
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

                    methodNode.Executable = (bool)value.GetValue(typeof(bool));

                    // UserExecutable Attribute
                    value = attributes[Attributes.UserExecutable];

                    if (value == null)
                    {
                        throw ServiceResultException.Create(StatusCodes.BadUnexpectedError, "Method does not support the UserExecutable attribute.");
                    }

                    methodNode.UserExecutable = (bool)value.GetValue(typeof(bool));

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

                    dataTypeNode.IsAbstract = (bool)value.GetValue(typeof(bool));

                    // DataTypeDefinition Attribute
                    value = attributes[Attributes.DataTypeDefinition];

                    if (value != null)
                    {
                        dataTypeNode.DataTypeDefinition = value.Value as ExtensionObject;
                    }

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

                    referenceTypeNode.IsAbstract = (bool)value.GetValue(typeof(bool));

                    // Symmetric Attribute
                    value = attributes[Attributes.Symmetric];

                    if (value == null)
                    {
                        throw ServiceResultException.Create(StatusCodes.BadUnexpectedError, "ReferenceType does not support the Symmetric attribute.");
                    }

                    referenceTypeNode.Symmetric = (bool)value.GetValue(typeof(bool));

                    // InverseName Attribute
                    value = attributes[Attributes.InverseName];

                    if (value != null && value.Value != null)
                    {
                        referenceTypeNode.InverseName = (LocalizedText)value.GetValue(typeof(LocalizedText));
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

                    viewNode.EventNotifier = (byte)value.GetValue(typeof(byte));

                    // ContainsNoLoops Attribute
                    value = attributes[Attributes.ContainsNoLoops];

                    if (value == null)
                    {
                        throw ServiceResultException.Create(StatusCodes.BadUnexpectedError, "View does not support the ContainsNoLoops attribute.");
                    }

                    viewNode.ContainsNoLoops = (bool)value.GetValue(typeof(bool));

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

            node.NodeId = (NodeId)value.GetValue(typeof(NodeId));
            node.NodeClass = (NodeClass)nodeClass.Value;

            // BrowseName Attribute
            value = attributes[Attributes.BrowseName];

            if (value == null)
            {
                throw ServiceResultException.Create(StatusCodes.BadUnexpectedError, "Node does not support the BrowseName attribute.");
            }

            node.BrowseName = (QualifiedName)value.GetValue(typeof(QualifiedName));

            // DisplayName Attribute
            value = attributes[Attributes.DisplayName];

            if (value == null)
            {
                throw ServiceResultException.Create(StatusCodes.BadUnexpectedError, "Node does not support the DisplayName attribute.");
            }

            node.DisplayName = (LocalizedText)value.GetValue(typeof(LocalizedText));

            // all optional attributes follow

            // Description Attribute
            if (attributes.TryGetValue(Attributes.Description, out value) &&
                value != null && value.Value != null)
            {
                node.Description = (LocalizedText)value.GetValue(typeof(LocalizedText));
            }

            // WriteMask Attribute
            if (attributes.TryGetValue(Attributes.WriteMask, out value) &&
                value != null)
            {
                node.WriteMask = (uint)value.GetValue(typeof(uint));
            }

            // UserWriteMask Attribute
            if (attributes.TryGetValue(Attributes.UserWriteMask, out value) &&
                value != null)
            {
                node.UserWriteMask = (uint)value.GetValue(typeof(uint));
            }

            // RolePermissions Attribute
            if (attributes.TryGetValue(Attributes.RolePermissions, out value) &&
                value != null)
            {
                ExtensionObject[] rolePermissions = value.Value as ExtensionObject[];

                if (rolePermissions != null)
                {
                    node.RolePermissions = new RolePermissionTypeCollection();

                    foreach (ExtensionObject rolePermission in rolePermissions)
                    {
                        node.RolePermissions.Add(rolePermission.Body as RolePermissionType);
                    }
                }
            }

            // UserRolePermissions Attribute
            if (attributes.TryGetValue(Attributes.UserRolePermissions, out value) &&
                value != null)
            {
                ExtensionObject[] userRolePermissions = value.Value as ExtensionObject[];

                if (userRolePermissions != null)
                {
                    node.UserRolePermissions = new RolePermissionTypeCollection();

                    foreach (ExtensionObject rolePermission in userRolePermissions)
                    {
                        node.UserRolePermissions.Add(rolePermission.Body as RolePermissionType);
                    }
                }
            }

            // AccessRestrictions Attribute
            if (attributes.TryGetValue(Attributes.AccessRestrictions, out value) &&
                value != null)
            {
                node.AccessRestrictions = (ushort)value.GetValue(typeof(ushort));
            }

            return node;
        }

        /// <summary>
        /// Create a dictionary of attributes to read for a nodeclass.
        /// </summary>
        private IDictionary<uint, DataValue> CreateAttributes(NodeClass nodeclass = NodeClass.Unspecified, bool optionalAttributes = true)
        {
            // Attributes to read for all types of nodes
            var attributes = new SortedDictionary<uint, DataValue>() {
                { Attributes.NodeId, null },
                { Attributes.NodeClass, null },
                { Attributes.BrowseName, null },
                { Attributes.DisplayName, null },
            };

            switch (nodeclass)
            {
                case NodeClass.Object:
                    attributes.Add(Attributes.EventNotifier, null);
                    break;

                case NodeClass.Variable:
                    attributes.Add(Attributes.DataType, null);
                    attributes.Add(Attributes.ValueRank, null);
                    attributes.Add(Attributes.ArrayDimensions, null);
                    attributes.Add(Attributes.AccessLevel, null);
                    attributes.Add(Attributes.UserAccessLevel, null);
                    attributes.Add(Attributes.Historizing, null);
                    attributes.Add(Attributes.MinimumSamplingInterval, null);
                    attributes.Add(Attributes.AccessLevelEx, null);
                    break;

                case NodeClass.Method:
                    attributes.Add(Attributes.Executable, null);
                    attributes.Add(Attributes.UserExecutable, null);
                    break;

                case NodeClass.ObjectType:
                    attributes.Add(Attributes.IsAbstract, null);
                    break;

                case NodeClass.VariableType:
                    attributes.Add(Attributes.IsAbstract, null);
                    attributes.Add(Attributes.DataType, null);
                    attributes.Add(Attributes.ValueRank, null);
                    attributes.Add(Attributes.ArrayDimensions, null);
                    break;

                case NodeClass.ReferenceType:
                    attributes.Add(Attributes.IsAbstract, null);
                    attributes.Add(Attributes.Symmetric, null);
                    attributes.Add(Attributes.InverseName, null);
                    break;

                case NodeClass.DataType:
                    attributes.Add(Attributes.IsAbstract, null);
                    attributes.Add(Attributes.DataTypeDefinition, null);
                    break;

                case NodeClass.View:
                    attributes.Add(Attributes.EventNotifier, null);
                    attributes.Add(Attributes.ContainsNoLoops, null);
                    break;

                default:
                    // build complete list of attributes.
                    attributes = new SortedDictionary<uint, DataValue> {
                        { Attributes.NodeId, null },
                        { Attributes.NodeClass, null },
                        { Attributes.BrowseName, null },
                        { Attributes.DisplayName, null },
                        //{ Attributes.Description, null },
                        //{ Attributes.WriteMask, null },
                        //{ Attributes.UserWriteMask, null },
                        { Attributes.DataType, null },
                        { Attributes.ValueRank, null },
                        { Attributes.ArrayDimensions, null },
                        { Attributes.AccessLevel, null },
                        { Attributes.UserAccessLevel, null },
                        { Attributes.MinimumSamplingInterval, null },
                        { Attributes.Historizing, null },
                        { Attributes.EventNotifier, null },
                        { Attributes.Executable, null },
                        { Attributes.UserExecutable, null },
                        { Attributes.IsAbstract, null },
                        { Attributes.InverseName, null },
                        { Attributes.Symmetric, null },
                        { Attributes.ContainsNoLoops, null },
                        { Attributes.DataTypeDefinition, null },
                        //{ Attributes.RolePermissions, null },
                        //{ Attributes.UserRolePermissions, null },
                        //{ Attributes.AccessRestrictions, null },
                        { Attributes.AccessLevelEx, null }
                    };
                    break;
            }

            if (optionalAttributes)
            {
                attributes.Add(Attributes.Description, null);
                attributes.Add(Attributes.WriteMask, null);
                attributes.Add(Attributes.UserWriteMask, null);
                attributes.Add(Attributes.RolePermissions, null);
                attributes.Add(Attributes.UserRolePermissions, null);
                attributes.Add(Attributes.AccessRestrictions, null);
            }

            return attributes;
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
                Utils.LogWarning("Publish skipped due to reconnect");
                return null;
            }

            SubscriptionAcknowledgementCollection acknowledgementsToSend = null;

            // collect the current set if acknowledgements.
            lock (SyncRoot)
            {
                acknowledgementsToSend = m_acknowledgementsToSend;
                m_acknowledgementsToSend = new SubscriptionAcknowledgementCollection();
                foreach (var toSend in acknowledgementsToSend)
                {
                    m_latestAcknowledgementsSent[toSend.SubscriptionId] = toSend.SequenceNumber;
                }
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

            CoreClientUtils.EventLog.PublishStart((int)requestHeader.RequestHandle);

            try
            {

                IAsyncResult result = BeginPublish(
                    requestHeader,
                    acknowledgementsToSend,
                    OnPublishComplete,
                    new object[] { SessionId, acknowledgementsToSend, requestHeader });

                AsyncRequestStarted(result, requestHeader.RequestHandle, DataTypes.PublishRequest);

                return result;
            }
            catch (Exception e)
            {
                Utils.LogError(e, "Unexpected error sending publish request.");
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

            CoreClientUtils.EventLog.PublishStop((int)requestHeader.RequestHandle);

            try
            {
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

                foreach (StatusCode code in acknowledgeResults)
                {
                    if (StatusCode.IsBad(code))
                    {
                        Utils.LogError("Error - Publish call finished. ResultCode={0}; SubscriptionId={1};", code.ToString(), subscriptionId);
                    }
                }

                // nothing more to do if session changed.
                if (sessionId != SessionId)
                {
                    Utils.LogWarning("Publish response discarded because session id changed: Old {0} != New {1}", sessionId, SessionId);
                    return;
                }

                CoreClientUtils.EventLog.NotificationReceived((int)subscriptionId, (int)notificationMessage.SequenceNumber);

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
                    Utils.LogWarning("No new publish sent because of reconnect in progress.");
                    return;
                }
            }
            catch (Exception e)
            {
                if (m_subscriptions.Count == 0)
                {
                    // Publish responses with error should occur after deleting the last subscription.
                    Utils.LogError("Publish #{0}, Subscription count = 0, Error: {1}", requestHeader.RequestHandle, e.Message);
                }
                else
                {
                    Utils.LogError("Publish #{0}, Reconnecting={1}, Error: {2}", requestHeader.RequestHandle, m_reconnecting, e.Message);
                }

                moreNotifications = false;

                // ignore errors if reconnecting.
                if (m_reconnecting)
                {
                    Utils.LogWarning("Publish abandoned after error due to reconnect: {0}", e.Message);
                    return;
                }

                // nothing more to do if session changed.
                if (sessionId != SessionId)
                {
                    Utils.LogError("Publish abandoned after error because session id changed: Old {0} != New {1}", sessionId, SessionId);
                    return;
                }

                // try to acknowledge the notifications again in the next publish.
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
                            Utils.LogError(e2, "Session: Unexpected error invoking PublishErrorCallback.");
                        }
                    }
                }

                // don't send another publish for these errors.
                switch (error.Code)
                {
                    case StatusCodes.BadTooManyPublishRequests:
                        int tooManyPublishRequests = GoodPublishRequestCount;
                        if (BelowPublishRequestLimit(tooManyPublishRequests))
                        {
                            m_tooManyPublishRequests = tooManyPublishRequests;
                            Utils.LogInfo("PUBLISH - Too many requests, set limit to GoodPublishRequestCount={0}.", m_tooManyPublishRequests);
                        }
                        return;
                    case StatusCodes.BadNoSubscription:
                    case StatusCodes.BadSessionClosed:
                    case StatusCodes.BadSessionIdInvalid:
                    case StatusCodes.BadSecureChannelIdInvalid:
                    case StatusCodes.BadSecureChannelClosed:
                    case StatusCodes.BadServerHalted:
                        return;
                }

                Utils.LogError(e, "PUBLISH #{0} - Unhandled error {1} during Publish.", requestHeader.RequestHandle, error.StatusCode);
            }

            int requestCount = GoodPublishRequestCount;
            var subscriptionsCount = m_subscriptions.Count;
            if (requestCount < subscriptionsCount)
            {
                BeginPublish(OperationTimeout);
            }
            else
            {
                Utils.LogInfo("PUBLISH - Did not send another publish request. GoodPublishRequestCount={0}, Subscriptions={1}", requestCount, subscriptionsCount);
            }
        }

        /// <summary>
        /// Sends a republish request.
        /// </summary>
        public bool Republish(uint subscriptionId, uint sequenceNumber)
        {
            // send publish request.
            RequestHeader requestHeader = new RequestHeader {
                TimeoutHint = (uint)OperationTimeout,
                ReturnDiagnostics = (uint)(int)ReturnDiagnostics,
                RequestHandle = Utils.IncrementIdentifier(ref m_publishCounter)
            };

            try
            {
                Utils.LogInfo("Requesting Republish for {0}-{1}", subscriptionId, sequenceNumber);

                // request republish.
                NotificationMessage notificationMessage = null;

                ResponseHeader responseHeader = Republish(
                    requestHeader,
                    subscriptionId,
                    sequenceNumber,
                    out notificationMessage);

                Utils.LogInfo("Received Republish for {0}-{1}-{2}", subscriptionId, sequenceNumber, responseHeader.ServiceResult);

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

                bool result = true;
                switch (error.StatusCode.Code)
                {
                    case StatusCodes.BadMessageNotAvailable:
                        Utils.LogWarning("Message {0}-{1} no longer available.", subscriptionId, sequenceNumber);
                        break;
                    // if encoding limits are exceeded, the issue is logged and
                    // the published data is acknoledged to prevent the endless republish loop.
                    case StatusCodes.BadEncodingLimitsExceeded:
                        Utils.LogError(e, "Message {0}-{1} exceeded size limits, ignored.", subscriptionId, sequenceNumber);
                        var ack = new SubscriptionAcknowledgement {
                            SubscriptionId = subscriptionId,
                            SequenceNumber = sequenceNumber
                        };
                        lock (SyncRoot)
                        {
                            m_acknowledgementsToSend.Add(ack);
                        }
                        break;
                    default:
                        result = false;
                        Utils.LogError(e, "Unexpected error sending republish request.");
                        break;
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
                        Utils.LogError(e2, "Session: Unexpected error invoking PublishErrorCallback.");
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

#if DEBUG_SEQUENTIALPUBLISHING
                // Checks for debug info only. 
                // Once more than a single publish request is queued, the checks are invalid
                // because a publish response may not include the latest ack information yet.

                uint lastSentSequenceNumber = 0;
                if (availableSequenceNumbers != null)
                {
                    foreach (uint availableSequenceNumber in availableSequenceNumbers)
                    {
                        if (m_latestAcknowledgementsSent.ContainsKey(subscriptionId))
                        {
                            lastSentSequenceNumber = m_latestAcknowledgementsSent[subscriptionId];
                            // If the last sent sequence number is uint.Max do not display the warning; the counter rolled over
                            // If the last sent sequence number is greater or equal to the available sequence number (returned by the publish),
                            // a warning must be logged.
                            if (((lastSentSequenceNumber >= availableSequenceNumber) && (lastSentSequenceNumber != uint.MaxValue)) ||
                                (lastSentSequenceNumber == availableSequenceNumber) && (lastSentSequenceNumber == uint.MaxValue))
                            {
                                Utils.LogWarning("Received sequence number which was already acknowledged={0}", availableSequenceNumber);
                            }
                        }
                    }
                }

                if (m_latestAcknowledgementsSent.ContainsKey(subscriptionId))
                {
                    lastSentSequenceNumber = m_latestAcknowledgementsSent[subscriptionId];

                    // If the last sent sequence number is uint.Max do not display the warning; the counter rolled over
                    // If the last sent sequence number is greater or equal to the notificationMessage's sequence number (returned by the publish),
                    // a warning must be logged.
                    if (((lastSentSequenceNumber >= notificationMessage.SequenceNumber) && (lastSentSequenceNumber != uint.MaxValue)) || (lastSentSequenceNumber == notificationMessage.SequenceNumber) && (lastSentSequenceNumber == uint.MaxValue))
                    {
                        Utils.LogWarning("Received sequence number which was already acknowledged={0}", notificationMessage.SequenceNumber);
                    }
                }
#endif

                if (availableSequenceNumbers != null)
                {
                    foreach (var acknowledgement in acknowledgementsToSend)
                    {
                        if (acknowledgement.SubscriptionId == subscriptionId && !availableSequenceNumbers.Contains(acknowledgement.SequenceNumber))
                        {
                            Utils.LogWarning("Sequence number={0} was not received in the available sequence numbers.", acknowledgement.SequenceNumber);
                        }
                    }
                }

                m_acknowledgementsToSend = acknowledgementsToSend;

                if (notificationMessage.IsEmpty)
                {
                    Utils.LogTrace("Empty notification message received for SessionId {0} with PublishTime {1}", SessionId, notificationMessage.PublishTime.ToLocalTime());
                }

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
                // Validate publish time and reject old values.
                if (notificationMessage.PublishTime.AddMilliseconds(subscription.CurrentPublishingInterval * subscription.CurrentLifetimeCount) < DateTime.UtcNow)
                {
                    Utils.LogWarning("PublishTime {0} in publish response is too old for SubscriptionId {1}.", notificationMessage.PublishTime.ToLocalTime(), subscription.Id);
                }

                // Validate publish time and reject old values.
                if (notificationMessage.PublishTime > DateTime.UtcNow.AddMilliseconds(subscription.CurrentPublishingInterval * subscription.CurrentLifetimeCount))
                {
                    Utils.LogWarning("PublishTime {0} in publish response is newer than actual time for SubscriptionId {1}.", notificationMessage.PublishTime.ToLocalTime(), subscription.Id);
                }

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
                        Task.Run(() => {
                            OnRaisePublishNotification(args);
                        });
                    }
                }
            }
            else
            {
                if (m_deleteSubscriptionsOnClose)
                {
                    // Delete abandoned subscription from server.
                    Utils.LogWarning("Received Publish Response for Unknown SubscriptionId={0}. Deleting abandoned subscription from server.", subscriptionId);

                    Task.Run(() => {
                        DeleteSubscription(subscriptionId);
                    });
                }
                else
                {
                    // Do not delete publish requests of stale subscriptions
                    Utils.LogWarning("Received Publish Response for Unknown SubscriptionId={0}. Ignored.", subscriptionId);
                }
            }
        }

        /// <summary>
        /// Recreate the subscriptions in a reconnected session.
        /// Uses Transfer service if <see cref="TransferSubscriptionsOnReconnect"/> is set to <c>true</c>.
        /// </summary>
        /// <param name="subscriptionsTemplate">The template for the subscriptions.</param>
        private void RecreateSubscriptions(IEnumerable<Subscription> subscriptionsTemplate)
        {
            bool transferred = false;
            if (TransferSubscriptionsOnReconnect)
            {
                try
                {
                    transferred = TransferSubscriptions(new SubscriptionCollection(subscriptionsTemplate), false);
                }
                catch (ServiceResultException sre)
                {
                    Utils.LogError(sre, "Transfer subscriptions failed.");
                }
                catch (Exception ex)
                {
                    Utils.LogError(ex, "Unexpected Transfer subscriptions error.");
                }
            }

            if (!transferred)
            {
                // Create the subscriptions which were not transferred.
                foreach (Subscription subscription in Subscriptions)
                {
                    if (!subscription.Created)
                    {
                        subscription.Create();
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
                Utils.LogError(e, "Session: Unexpected error while raising Notification event.");
            }
        }

        /// <summary>
        /// Invokes a DeleteSubscriptions call for the specified subscriptionId.
        /// </summary>
        private void DeleteSubscription(uint subscriptionId)
        {
            try
            {
                Utils.LogInfo("Deleting server subscription for SubscriptionId={0}", subscriptionId);

                // delete the subscription.
                UInt32Collection subscriptionIds = new uint[] { subscriptionId };

                StatusCodeCollection results;
                DiagnosticInfoCollection diagnosticInfos;

                ResponseHeader responseHeader = DeleteSubscriptions(
                    null,
                    subscriptionIds,
                    out results,
                    out diagnosticInfos);

                // validate response.
                ClientBase.ValidateResponse(results, subscriptionIds);
                ClientBase.ValidateDiagnosticInfos(diagnosticInfos, subscriptionIds);

                if (StatusCode.IsBad(results[0]))
                {
                    throw new ServiceResultException(ClientBase.GetResult(results[0], 0, diagnosticInfos, responseHeader));
                }
            }
            catch (Exception e)
            {
                Utils.LogError(e, "Session: Unexpected error while deleting subscription for SubscriptionId={0}.", subscriptionId);
            }
        }

        /// <summary>
        /// Load certificate for connection.
        /// </summary>
        private static async Task<X509Certificate2> LoadCertificate(ApplicationConfiguration configuration)
        {
            X509Certificate2 clientCertificate;
            if (configuration.SecurityConfiguration.ApplicationCertificate == null)
            {
                throw ServiceResultException.Create(StatusCodes.BadConfigurationError, "ApplicationCertificate must be specified.");
            }

            clientCertificate = await configuration.SecurityConfiguration.ApplicationCertificate.Find(true).ConfigureAwait(false);

            if (clientCertificate == null)
            {
                throw ServiceResultException.Create(StatusCodes.BadConfigurationError, "ApplicationCertificate cannot be found.");
            }
            return clientCertificate;
        }

        /// <summary>
        /// Load certificate chain for connection.
        /// </summary>
        private static async Task<X509Certificate2Collection> LoadCertificateChain(ApplicationConfiguration configuration, X509Certificate2 clientCertificate)
        {
            X509Certificate2Collection clientCertificateChain = null;
            // load certificate chain.
            if (configuration.SecurityConfiguration.SendCertificateChain)
            {
                clientCertificateChain = new X509Certificate2Collection(clientCertificate);
                List<CertificateIdentifier> issuers = new List<CertificateIdentifier>();
                await configuration.CertificateValidator.GetIssuers(clientCertificate, issuers).ConfigureAwait(false);

                for (int i = 0; i < issuers.Count; i++)
                {
                    clientCertificateChain.Add(issuers[i].Certificate);
                }
            }
            return clientCertificateChain;
        }

        /// <summary>
        /// Helper to determine if a continuation point needs to be processed.
        /// </summary>
        private bool HasAnyContinuationPoint(ByteStringCollection continuationPoints)
        {
            foreach (byte[] cp in continuationPoints)
            {
                if (cp != null)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Returns true if the Bad_TooManyPublishRequests limit
        /// has not been reached.
        /// </summary>
        /// <param name="requestCount">The actual number of publish requests.</param>
        /// <returns>If the publish request limit was reached.</returns>
        private bool BelowPublishRequestLimit(int requestCount)
        {
            return (m_tooManyPublishRequests == 0) ||
                (requestCount < m_tooManyPublishRequests);
        }
        #endregion

        #region Protected Fields
        /// <summary>
        /// The period for which the server will maintain the session if there is no communication from the client.
        /// </summary>
        protected double m_sessionTimeout;

        /// <summary>
        /// The locales that the server should use when returning localized text.
        /// </summary>
        protected StringCollection m_preferredLocales;

        /// <summary>
        /// The Application Configuration.
        /// </summary>
        protected ApplicationConfiguration m_configuration;

        /// <summary>
        /// The endpoint used to connect to the server.
        /// </summary>
        protected ConfiguredEndpoint m_endpoint;

        /// <summary>
        /// The Instance Certificate.
        /// </summary>
        protected X509Certificate2 m_instanceCertificate;

        /// <summary>
        /// The Instance Certificate Chain.
        /// </summary>
        protected X509Certificate2Collection m_instanceCertificateChain;

        /// <summary>
        /// If set to<c>true</c> then the domain in the certificate must match the endpoint used.
        /// </summary>
        protected bool m_checkDomain;

        /// <summary>
        /// The name assigned to the session.
        /// </summary>
        protected string m_sessionName;

        /// <summary>
        /// The user identity currently used for the session.
        /// </summary>
        protected IUserIdentity m_identity;
        #endregion

        #region Private Fields
        private ISessionFactory m_sessionFactory;
        private SubscriptionAcknowledgementCollection m_acknowledgementsToSend;
        private Dictionary<uint, uint> m_latestAcknowledgementsSent;
        private List<Subscription> m_subscriptions;
        private Dictionary<NodeId, DataDictionary> m_dictionaries;
        private Subscription m_defaultSubscription;
        private bool m_deleteSubscriptionsOnClose;
        private bool m_transferSubscriptionsOnReconnect;
        private uint m_maxRequestMessageSize;
        private NamespaceTable m_namespaceUris;
        private StringTable m_serverUris;
        private IEncodeableFactory m_factory;
        private SystemContext m_systemContext;
        private NodeCache m_nodeCache;
        private List<IUserIdentity> m_identityHistory;
        private object m_handle;
        private byte[] m_serverNonce;
        private byte[] m_previousServerNonce;
        private X509Certificate2 m_serverCertificate;
        private long m_publishCounter;
        private int m_tooManyPublishRequests;
        private DateTime m_lastKeepAliveTime;
        private ServerState m_serverState;
        private int m_keepAliveInterval;
        private Timer m_keepAliveTimer;
        private long m_keepAliveCounter;
        private bool m_reconnecting;
        private const int kReconnectTimeout = 15000;
        private LinkedList<AsyncRequestState> m_outstandingRequests;
        private readonly EndpointDescriptionCollection m_discoveryServerEndpoints;
        private readonly StringCollection m_discoveryProfileUris;

        private class AsyncRequestState
        {
            public uint RequestTypeId;
            public uint RequestId;
            public DateTime Timestamp;
            public IAsyncResult Result;
            public bool Defunct;
        }

        private readonly object m_eventLock = new object();
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
        public ServiceResult Status => m_status;

        /// <summary>
        /// Gets the current server state.
        /// </summary>
        public ServerState CurrentState => m_currentState;

        /// <summary>
        /// Gets the current server time.
        /// </summary>
        public DateTime CurrentTime => m_currentTime;

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
        private readonly ServiceResult m_status;
        private readonly ServerState m_currentState;
        private readonly DateTime m_currentTime;
        private bool m_cancelKeepAlive;
        #endregion
    }
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
        public Subscription Subscription => m_subscription;

        /// <summary>
        /// Gets the notification message.
        /// </summary>
        public NotificationMessage NotificationMessage => m_notificationMessage;

        /// <summary>
        /// Gets the string table returned with the notification message.
        /// </summary>
        public IList<string> StringTable => m_stringTable;
        #endregion

        #region Private Fields
        private readonly Subscription m_subscription;
        private readonly NotificationMessage m_notificationMessage;
        private readonly IList<string> m_stringTable;
        #endregion
    }
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
        public ServiceResult Status => m_status;

        /// <summary>
        /// Gets the subscription with the message that could not be republished.
        /// </summary>
        public uint SubscriptionId => m_subscriptionId;

        /// <summary>
        /// Gets the sequence number for the message that could not be republished.
        /// </summary>
        public uint SequenceNumber => m_sequenceNumber;
        #endregion

        #region Private Fields
        private readonly uint m_subscriptionId;
        private readonly uint m_sequenceNumber;
        private readonly ServiceResult m_status;
        #endregion
    }
    #endregion
}
