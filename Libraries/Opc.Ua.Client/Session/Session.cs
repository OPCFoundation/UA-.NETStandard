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
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.Extensions.Logging;
using Opc.Ua.Bindings;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Manages a session with a server.
    /// </summary>
    public class Session : SessionClientBatched, ISession
    {
        private const int kReconnectTimeout = 15000;
        private const int kMinPublishRequestCountMax = 100;
        private const int kMaxPublishRequestCountMax = ushort.MaxValue;
        private const int kDefaultPublishRequestCount = 1;
        private const int kPublishRequestSequenceNumberOutOfOrderThreshold = 10;
        private const int kPublishRequestSequenceNumberOutdatedThreshold = 100;

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
            : this(
                  channel as ITransportChannel,
                  configuration,
                  endpoint,
                  clientCertificate: null)
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
            : this(
                  channel,
                  configuration,
                  endpoint,
                  channel.MessageContext ?? configuration.CreateMessageContext(true))
        {
            LoadInstanceCertificateAsync(clientCertificate).GetAwaiter().GetResult();
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
            : this(
                  channel,
                  template.m_configuration,
                  template.ConfiguredEndpoint,
                  channel.MessageContext ?? template.m_configuration.CreateMessageContext(true))
        {
            LoadInstanceCertificateAsync(template.m_instanceCertificate).GetAwaiter().GetResult();
            SessionFactory = template.SessionFactory;
            m_defaultSubscription = template.m_defaultSubscription;
            DeleteSubscriptionsOnClose = template.DeleteSubscriptionsOnClose;
            TransferSubscriptionsOnReconnect = template.TransferSubscriptionsOnReconnect;
            m_sessionTimeout = template.m_sessionTimeout;
            m_maxRequestMessageSize = template.m_maxRequestMessageSize;
            m_minPublishRequestCount = template.m_minPublishRequestCount;
            m_maxPublishRequestCount = template.m_maxPublishRequestCount;
            m_preferredLocales = template.PreferredLocales;
            m_sessionName = template.SessionName;
            Handle = template.Handle;
            m_identity = template.Identity;
            m_keepAliveInterval = template.KeepAliveInterval;

            // Create timer for keep alive event triggering but in off state
            m_keepAliveTimer = new Timer(_ => m_keepAliveEvent.Set(), this, Timeout.Infinite, Timeout.Infinite);

            m_checkDomain = template.m_checkDomain;
            ContinuationPointPolicy = template.ContinuationPointPolicy;
            ReturnDiagnostics = template.ReturnDiagnostics;
            if (template.OperationTimeout > 0)
            {
                OperationTimeout = template.OperationTimeout;
            }

            if (copyEventHandlers)
            {
                m_KeepAlive = template.m_KeepAlive;
                m_Publish = template.m_Publish;
                m_PublishError = template.m_PublishError;
                m_PublishSequenceNumbersToAcknowledge = template
                    .m_PublishSequenceNumbersToAcknowledge;
                m_SubscriptionsChanged = template.m_SubscriptionsChanged;
                m_SessionClosing = template.m_SessionClosing;
                m_SessionConfigurationChanged = template.m_SessionConfigurationChanged;
                m_RenewUserIdentity = template.m_RenewUserIdentity;
            }

            foreach (Subscription subscription in template.Subscriptions)
            {
                AddSubscription(subscription.CloneSubscription(copyEventHandlers));
            }
        }

        /// <summary>
        /// Initializes the session.
        /// </summary>
        private Session(
            ITransportChannel channel,
            ApplicationConfiguration configuration,
            ConfiguredEndpoint endpoint,
            IServiceMessageContext messageContext)
            : base(channel, messageContext.Telemetry)
        {
            if (messageContext == null)
            {
                throw new ArgumentNullException(nameof(messageContext));
            }

            m_telemetry = messageContext.Telemetry;
            m_logger = m_telemetry.CreateLogger<Session>();

            Initialize();

            ValidateClientConfiguration(configuration);

            // save configuration information.
            m_configuration = configuration;
            m_endpoint = endpoint;

            // update the default subscription.
            DefaultSubscription.MinLifetimeInterval = (uint)m_configuration.ClientConfiguration
                .MinSubscriptionLifetime;

            NamespaceUris = messageContext.NamespaceUris;
            ServerUris = messageContext.ServerUris;
            Factory = messageContext.Factory;

            // initialize the NodeCache late, it needs references to the namespaceUris
            m_nodeCache = new NodeCache(this, m_telemetry);

            // Create timer for keep alive event triggering but in off state
            m_keepAliveTimer = new Timer(_ => m_keepAliveEvent.Set(), this, Timeout.Infinite, Timeout.Infinite);

            // set the default preferred locales.
            m_preferredLocales = new string[] { CultureInfo.CurrentCulture.Name };

            // create a context to use.
            m_systemContext = new SystemContext(m_telemetry)
            {
                SystemHandle = this,
                EncodeableFactory = Factory,
                NamespaceUris = NamespaceUris,
                ServerUris = ServerUris,
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
            SessionFactory ??= new DefaultSessionFactory(m_telemetry)
            {
                ReturnDiagnostics = ReturnDiagnostics
            };
            m_sessionTimeout = 0;
            NamespaceUris = new NamespaceTable();
            ServerUris = new StringTable();
            Factory = EncodeableFactory.Create();
            m_configuration = null;
            m_instanceCertificate = null;
            m_endpoint = null;
            m_subscriptions = [];
            m_acknowledgementsToSend = [];
            m_acknowledgementsToSendLock = new object();
#if DEBUG_SEQUENTIALPUBLISHING
            m_latestAcknowledgementsSent = new Dictionary<uint, uint>();
#endif
            m_identityHistory = [];
            m_outstandingRequests = new LinkedList<AsyncRequestState>();
            m_keepAliveInterval = 5000;
            m_tooManyPublishRequests = 0;
            m_minPublishRequestCount = kDefaultPublishRequestCount;
            m_maxPublishRequestCount = kMaxPublishRequestCountMax;
            m_sessionName = string.Empty;
            DeleteSubscriptionsOnClose = true;
            TransferSubscriptionsOnReconnect = false;
            Reconnecting = false;
            m_reconnectLock = new SemaphoreSlim(1, 1);
            ServerMaxContinuationPointsPerBrowse = 0;
        }

        /// <summary>
        /// Check if all required configuration fields are populated.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <c>null</c>.</exception>
        /// <exception cref="ServiceResultException"></exception>
        private static void ValidateClientConfiguration(ApplicationConfiguration configuration)
        {
            string configurationField;
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
        /// <exception cref="ServiceResultException"></exception>
        private void ValidateServerNonce(
            IUserIdentity identity,
            byte[] serverNonce,
            string securityPolicyUri,
            byte[] previousServerNonce,
            MessageSecurityMode channelSecurityMode = MessageSecurityMode.None)
        {
            // skip validation if server nonce is not used for encryption.
            if (string.IsNullOrEmpty(securityPolicyUri) ||
                securityPolicyUri == SecurityPolicies.None)
            {
                return;
            }

            if (identity != null && identity.TokenType != UserTokenType.Anonymous)
            {
                // the server nonce should be validated if the token includes a secret.
                if (!Nonce.ValidateNonce(
                    serverNonce,
                    MessageSecurityMode.SignAndEncrypt,
                    (uint)m_configuration.SecurityConfiguration.NonceLength))
                {
                    if (channelSecurityMode == MessageSecurityMode.SignAndEncrypt ||
                        m_configuration.SecurityConfiguration.SuppressNonceValidationErrors)
                    {
                        m_logger.LogWarning(
                            Utils.TraceMasks.Security,
                            "Warning: The server nonce has not the correct length or is not random enough. " +
                            "The error is suppressed by user setting or because the channel is encrypted.");
                    }
                    else
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadNonceInvalid,
                            "The server nonce has not the correct length or is not random enough.");
                    }
                }

                // check that new nonce is different from the previously returned server nonce.
                if (previousServerNonce != null &&
                    Nonce.CompareNonce(serverNonce, previousServerNonce))
                {
                    if (channelSecurityMode == MessageSecurityMode.SignAndEncrypt ||
                        m_configuration.SecurityConfiguration.SuppressNonceValidationErrors)
                    {
                        m_logger.LogWarning(
                            Utils.TraceMasks.Security,
                            "Warning: The Server nonce is equal with previously returned nonce. " +
                            "The error is suppressed by user setting or because the channel is encrypted.");
                    }
                    else
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadNonceInvalid,
                            "Server nonce is equal with previously returned nonce.");
                    }
                }
            }
        }

        /// <summary>
        /// Closes the session and the underlying channel.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                StopKeepAliveTimerAsync().AsTask().GetAwaiter().GetResult();

                Utils.SilentDispose(m_defaultSubscription);
                m_defaultSubscription = null;

                Utils.SilentDispose(m_nodeCache);
                m_nodeCache = null;

                List<Subscription> subscriptions = null;
                lock (SyncRoot)
                {
                    subscriptions = [.. m_subscriptions];
                    m_subscriptions.Clear();
                }

                foreach (Subscription subscription in subscriptions)
                {
                    Utils.SilentDispose(subscription);
                }
                subscriptions.Clear();
            }

            base.Dispose(disposing);

            if (disposing)
            {
                m_keepAliveTimer.Dispose();

                // suppress spurious events
                m_KeepAlive = null;
                m_Publish = null;
                m_PublishError = null;
                m_PublishSequenceNumbersToAcknowledge = null;
                m_SubscriptionsChanged = null;
                m_SessionClosing = null;
                m_SessionConfigurationChanged = null;
            }
        }

        /// <summary>
        /// Raised when a keep alive arrives from the server or an error is detected.
        /// </summary>
        /// <remarks>
        /// Once a session is created a timer will periodically read the server state and current time.
        /// If this read operation succeeds this event will be raised each time the keep alive period elapses.
        /// If an error is detected (KeepAliveStopped == true) then this event will be raised as well.
        /// </remarks>
        public event KeepAliveEventHandler KeepAlive
        {
            add => m_KeepAlive += value;
            remove => m_KeepAlive -= value;
        }

        /// <summary>
        /// Raised when a notification message arrives in a publish response.
        /// </summary>
        /// <remarks>
        /// All publish requests are managed by the Session object. When a response arrives it is
        /// validated and passed to the appropriate Subscription object and this event is raised.
        /// </remarks>
        public event NotificationEventHandler Notification
        {
            add => m_Publish += value;
            remove => m_Publish -= value;
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
        public event PublishErrorEventHandler PublishError
        {
            add => m_PublishError += value;
            remove => m_PublishError -= value;
        }

        /// <inheritdoc/>
        public event PublishSequenceNumbersToAcknowledgeEventHandler PublishSequenceNumbersToAcknowledge
        {
            add => m_PublishSequenceNumbersToAcknowledge += value;
            remove => m_PublishSequenceNumbersToAcknowledge -= value;
        }

        /// <summary>
        /// Raised when a subscription is added or removed
        /// </summary>
        public event EventHandler SubscriptionsChanged
        {
            add => m_SubscriptionsChanged += value;
            remove => m_SubscriptionsChanged -= value;
        }

        /// <summary>
        /// Raised to indicate the session is closing.
        /// </summary>
        public event EventHandler SessionClosing
        {
            add => m_SessionClosing += value;
            remove => m_SessionClosing -= value;
        }

        /// <inheritdoc/>
        public event EventHandler SessionConfigurationChanged
        {
            add => m_SessionConfigurationChanged += value;
            remove => m_SessionConfigurationChanged -= value;
        }

        /// <summary>
        /// A session factory that was used to create the session.
        /// </summary>
        public ISessionFactory SessionFactory { get; set; }

        /// <summary>
        /// Gets the endpoint used to connect to the server.
        /// </summary>
        public ConfiguredEndpoint ConfiguredEndpoint => m_endpoint;

        /// <summary>
        /// Gets the name assigned to the session.
        /// </summary>
        public string SessionName => m_sessionName;

        /// <summary>
        /// Whether the session is reconnecting
        /// </summary>
        public bool Reconnecting { get; private set; }

        /// <summary>
        /// Gets the period for wich the server will maintain the session if
        /// there is no communication from the client.
        /// </summary>
        public double SessionTimeout => m_sessionTimeout;

        /// <summary>
        /// Gets the local handle assigned to the session.
        /// </summary>
        public object Handle { get; set; }

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
        public NamespaceTable NamespaceUris { get; private set; }

        /// <summary>
        /// Gets the table of remote server uris known to the server.
        /// </summary>
        public StringTable ServerUris { get; private set; }

        /// <summary>
        /// Gets the system context for use with the session.
        /// </summary>
        public ISystemContext SystemContext => m_systemContext;

        /// <summary>
        /// Gets the factory used to create encodeable objects that the server understands.
        /// </summary>
        public IEncodeableFactory Factory { get; private set; }

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
        public FilterContext FilterContext
            => new(NamespaceUris, m_nodeCache.TypeTree, m_preferredLocales, m_telemetry);

        /// <summary>
        /// Gets the locales that the server should use when returning localized text.
        /// </summary>
        public StringCollection PreferredLocales => m_preferredLocales;

        /// <summary>
        /// Gets the subscriptions owned by the session.
        /// </summary>
        public IEnumerable<Subscription> Subscriptions
        {
            get
            {
                lock (SyncRoot)
                {
                    return [.. m_subscriptions];
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
        public bool DeleteSubscriptionsOnClose { get; set; }

        /// <summary>
        /// If the subscriptions are transferred when a session is reconnected.
        /// </summary>
        /// <remarks>
        /// Default <c>false</c>, set to <c>true</c> if subscriptions should
        /// be transferred after reconnect. Service must be supported by server.
        /// </remarks>
        public bool TransferSubscriptionsOnReconnect { get; set; }

        /// <summary>
        /// Whether the endpoint Url domain is checked in the certificate.
        /// </summary>
        public bool CheckDomain => m_checkDomain;

        /// <summary>
        /// Gets or Sets the default subscription for the session.
        /// </summary>
        public Subscription DefaultSubscription
        {
            get => m_defaultSubscription ??= new Subscription(m_telemetry)
            {
                DisplayName = "Subscription",
                PublishingInterval = 1000,
                KeepAliveCount = 10,
                LifetimeCount = 1000,
                Priority = 255,
                PublishingEnabled = true,
                MinLifetimeInterval = (uint)m_configuration.ClientConfiguration
                  .MinSubscriptionLifetime
            };
            set
            {
                Utils.SilentDispose(m_defaultSubscription);
                m_defaultSubscription = value;
            }
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
            get => m_keepAliveInterval;
            set
            {
                m_keepAliveInterval = value;
                ResetKeepAliveTimer();
            }
        }

        /// <summary>
        /// Returns true if the session is not receiving keep alives.
        /// </summary>
        /// <remarks>
        /// Set to true if the server does not respond for the
        /// KeepAliveInterval * 1 (KeepAliveIntervalFactor) + 1 Second (KeepAliveGuardBand) *
        /// To change the sensitivity of the keep alive check, set the
        /// <see cref="m_keepAliveIntervalFactor"/> / <see cref="m_keepAliveGuardBand"/> fields.
        /// or if another error was reported.
        /// Set to false is communication is ok or recovered.
        /// </remarks>
        public bool KeepAliveStopped
        {
            get
            {
                StatusCode lastKeepAliveErrorStatusCode = m_lastKeepAliveErrorStatusCode;
                if (StatusCode.IsGood(lastKeepAliveErrorStatusCode) ||
                    lastKeepAliveErrorStatusCode == StatusCodes.BadNoCommunication)
                {
                    int delta = HiResClock.TickCount - LastKeepAliveTickCount;

                    // add a guard band to allow for network lag.
                    return ((m_keepAliveInterval * m_keepAliveIntervalFactor) +
                        m_keepAliveGuardBand) <= delta;
                }

                // another error was reported which caused keep alive to stop.
                return true;
            }
        }

        /// <summary>
        /// Gets the time of the last keep alive.
        /// </summary>
        public DateTime LastKeepAliveTime
        {
            get
            {
                long ticks = Interlocked.Read(ref m_lastKeepAliveTime);
                return new DateTime(ticks, DateTimeKind.Utc);
            }
        }

        /// <summary>
        /// Gets the TickCount in ms of the last keep alive based on <see cref="HiResClock.TickCount"/>.
        /// Independent of system time changes.
        /// </summary>
        public int LastKeepAliveTickCount { get; private set; }

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

                    for (LinkedListNode<AsyncRequestState> ii = m_outstandingRequests.First;
                        ii != null;
                        ii = ii.Next)
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

                    for (LinkedListNode<AsyncRequestState> ii = m_outstandingRequests.First;
                        ii != null;
                        ii = ii.Next)
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

        /// <summary>
        /// Gets and sets the minimum number of publish requests to be used in the session.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public int MinPublishRequestCount
        {
            get => m_minPublishRequestCount;
            set
            {
                lock (SyncRoot)
                {
                    if (value is >= kDefaultPublishRequestCount and <= kMinPublishRequestCountMax)
                    {
                        m_minPublishRequestCount = value;
                    }
                    else
                    {
                        throw new ArgumentOutOfRangeException(
                            nameof(MinPublishRequestCount),
                            $"Minimum publish request count must be between {kDefaultPublishRequestCount} and {kMinPublishRequestCountMax}.");
                    }
                }
            }
        }

        /// <summary>
        /// Gets and sets the maximum number of publish requests to be used in the session.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public int MaxPublishRequestCount
        {
            get => Math.Max(m_minPublishRequestCount, m_maxPublishRequestCount);
            set
            {
                lock (SyncRoot)
                {
                    if (value is >= kDefaultPublishRequestCount and <= kMaxPublishRequestCountMax)
                    {
                        m_maxPublishRequestCount = value;
                    }
                    else
                    {
                        throw new ArgumentOutOfRangeException(
                            nameof(MaxPublishRequestCount),
                            $"Maximum publish request count must be between {kDefaultPublishRequestCount} and {kMaxPublishRequestCountMax}.");
                    }
                }
            }
        }

        /// <summary>
        /// Read from the Server capability MaxContinuationPointsPerBrowse when the Operation Limits are fetched
        /// </summary>
        public uint ServerMaxContinuationPointsPerBrowse { get; set; }

        /// <summary>
        /// Read from the Server capability MaxByteStringLength when the Operation Limits are fetched
        /// </summary>
        public uint ServerMaxByteStringLength { get; set; }

        /// <inheritdoc/>
        public ContinuationPointPolicy ContinuationPointPolicy { get; set; }
            = ContinuationPointPolicy.Default;

        /// <summary>
        /// Creates a new communication session with a server by invoking the CreateSession service
        /// </summary>
        /// <param name="configuration">The configuration for the client application.</param>
        /// <param name="endpoint">The endpoint for the server.</param>
        /// <param name="updateBeforeConnect">If set to <c>true</c> the discovery endpoint is
        /// used to update the endpoint description before connecting.</param>
        /// <param name="sessionName">The name to assign to the session.</param>
        /// <param name="sessionTimeout">The timeout period for the session.</param>
        /// <param name="identity">The identity.</param>
        /// <param name="preferredLocales">The user identity to associate with the session.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The new session object</returns>
        [Obsolete("Use ISessionFactory.CreateAsync")]
        public static Task<Session> Create(
            ApplicationConfiguration configuration,
            ConfiguredEndpoint endpoint,
            bool updateBeforeConnect,
            string sessionName,
            uint sessionTimeout,
            IUserIdentity identity,
            IList<string> preferredLocales,
            CancellationToken ct = default)
        {
            return Create(
                configuration,
                endpoint,
                updateBeforeConnect,
                false,
                sessionName,
                sessionTimeout,
                identity,
                preferredLocales,
                ct);
        }

        /// <summary>
        /// Creates a new communication session with a server by invoking the CreateSession service
        /// </summary>
        /// <param name="configuration">The configuration for the client application.</param>
        /// <param name="endpoint">The endpoint for the server.</param>
        /// <param name="updateBeforeConnect">If set to <c>true</c> the discovery endpoint is
        /// used to update the endpoint description before connecting.</param>
        /// <param name="checkDomain">If set to <c>true</c> then the domain in the certificate
        /// must match the endpoint used.</param>
        /// <param name="sessionName">The name to assign to the session.</param>
        /// <param name="sessionTimeout">The timeout period for the session.</param>
        /// <param name="identity">The user identity to associate with the session.</param>
        /// <param name="preferredLocales">The preferred locales.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The new session object.</returns>
        [Obsolete("Use ISessionFactory.CreateAsync")]
        public static Task<Session> Create(
            ApplicationConfiguration configuration,
            ConfiguredEndpoint endpoint,
            bool updateBeforeConnect,
            bool checkDomain,
            string sessionName,
            uint sessionTimeout,
            IUserIdentity identity,
            IList<string> preferredLocales,
            CancellationToken ct = default)
        {
            return Create(
                configuration,
                (ITransportWaitingConnection)null,
                endpoint,
                updateBeforeConnect,
                checkDomain,
                sessionName,
                sessionTimeout,
                identity,
                preferredLocales,
                ct);
        }

        /// <summary>
        /// Creates a new session with a server using the specified channel by invoking
        /// the CreateSession service
        /// </summary>
        /// <param name="configuration">The configuration for the client application.</param>
        /// <param name="channel">The channel for the server.</param>
        /// <param name="endpoint">The endpoint for the server.</param>
        /// <param name="clientCertificate">The certificate to use for the client.</param>
        /// <param name="availableEndpoints">The list of available endpoints returned by server
        /// in GetEndpoints() response.</param>
        /// <param name="discoveryProfileUris">The value of profileUris used in GetEndpoints()
        /// request.</param>
        [Obsolete("Use ISessionFactory.CreateAsync")]
        public static Session Create(
            ApplicationConfiguration configuration,
            ITransportChannel channel,
            ConfiguredEndpoint endpoint,
            X509Certificate2 clientCertificate,
            EndpointDescriptionCollection availableEndpoints = null,
            StringCollection discoveryProfileUris = null)
        {
            return Create(
                DefaultSessionFactory.Instance,
                configuration,
                channel,
                endpoint,
                clientCertificate,
                availableEndpoints,
                discoveryProfileUris);
        }

        /// <summary>
        /// Recreates a session based on a specified template.
        /// </summary>
        /// <param name="template">The Session object to use as template</param>
        /// <returns>The new session object.</returns>
        [Obsolete("Use ISessionFactory.RecreateAsync")]
        public static Session Recreate(Session template)
        {
            return RecreateAsync(template).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Recreates a session based on a specified template.
        /// </summary>
        /// <param name="template">The Session object to use as template</param>
        /// <param name="connection">The waiting reverse connection.</param>
        /// <returns>The new session object.</returns>
        [Obsolete("Use ISessionFactory.RecreateAsync")]
        public static Session Recreate(Session template, ITransportWaitingConnection connection)
        {
            return RecreateAsync(template, connection).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Recreates a session based on a specified template using the provided channel.
        /// </summary>
        /// <param name="template">The Session object to use as template</param>
        /// <param name="transportChannel">The waiting reverse connection.</param>
        /// <returns>The new session object.</returns>
        [Obsolete("Use ISessionFactory.RecreateAsync")]
        public static Session Recreate(Session template, ITransportChannel transportChannel)
        {
            return RecreateAsync(template, transportChannel).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Creates a new communication session with a server using a reverse connection.
        /// </summary>
        /// <param name="configuration">The configuration for the client application.</param>
        /// <param name="connection">The client endpoint for the reverse connect.</param>
        /// <param name="endpoint">The endpoint for the server.</param>
        /// <param name="updateBeforeConnect">If set to <c>true</c> the discovery endpoint is
        /// used to update the endpoint description before connecting.</param>
        /// <param name="checkDomain">If set to <c>true</c> then the domain in the certificate
        /// must match the endpoint used.</param>
        /// <param name="sessionName">The name to assign to the session.</param>
        /// <param name="sessionTimeout">The timeout period for the session.</param>
        /// <param name="identity">The user identity to associate with the session.</param>
        /// <param name="preferredLocales">The preferred locales.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The new session object.</returns>
        [Obsolete("Use ISessionFactory.CreateAsync")]
        public static Task<Session> Create(
            ApplicationConfiguration configuration,
            ITransportWaitingConnection connection,
            ConfiguredEndpoint endpoint,
            bool updateBeforeConnect,
            bool checkDomain,
            string sessionName,
            uint sessionTimeout,
            IUserIdentity identity,
            IList<string> preferredLocales,
            CancellationToken ct = default)
        {
            return CreateAsync(
                DefaultSessionFactory.Instance,
                configuration,
                connection,
                endpoint,
                updateBeforeConnect,
                checkDomain,
                sessionName,
                sessionTimeout,
                identity,
                preferredLocales,
                DiagnosticsMasks.None,
                ct);
        }

        /// <summary>
        /// Create a session
        /// </summary>
        [Obsolete("Use ISessionFactory.CreateAsync")]
        public static Task<Session> Create(
            ISessionInstantiator sessionInstantiator,
            ApplicationConfiguration configuration,
            ITransportWaitingConnection connection,
            ConfiguredEndpoint endpoint,
            bool updateBeforeConnect,
            bool checkDomain,
            string sessionName,
            uint sessionTimeout,
            IUserIdentity identity,
            IList<string> preferredLocales,
            CancellationToken ct = default)
        {
            return CreateAsync(
                sessionInstantiator,
                configuration,
                connection,
                endpoint,
                updateBeforeConnect,
                checkDomain,
                sessionName,
                sessionTimeout,
                identity,
                preferredLocales,
                DiagnosticsMasks.None,
                ct);
        }

        /// <summary>
        /// Create a session
        /// </summary>
        [Obsolete("Use ISessionFactory.CreateAsync")]
        public static Task<Session> Create(
            ISessionInstantiator sessionInstantiator,
            ApplicationConfiguration configuration,
            ReverseConnectManager reverseConnectManager,
            ConfiguredEndpoint endpoint,
            bool updateBeforeConnect,
            bool checkDomain,
            string sessionName,
            uint sessionTimeout,
            IUserIdentity userIdentity,
            IList<string> preferredLocales,
            CancellationToken ct = default)
        {
            return CreateAsync(
                sessionInstantiator,
                configuration,
                reverseConnectManager,
                endpoint,
                updateBeforeConnect,
                checkDomain,
                sessionName,
                sessionTimeout,
                userIdentity,
                preferredLocales,
                DiagnosticsMasks.None,
                ct);
        }

        /// <summary>
        /// Create a session
        /// </summary>
        [Obsolete("Use ISessionFactory.CreateAsync")]
        public static Task<Session> Create(
            ApplicationConfiguration configuration,
            ReverseConnectManager reverseConnectManager,
            ConfiguredEndpoint endpoint,
            bool updateBeforeConnect,
            bool checkDomain,
            string sessionName,
            uint sessionTimeout,
            IUserIdentity userIdentity,
            IList<string> preferredLocales,
            CancellationToken ct = default)
        {
            return CreateAsync(
                configuration,
                reverseConnectManager,
                endpoint,
                updateBeforeConnect,
                checkDomain,
                sessionName,
                sessionTimeout,
                userIdentity,
                preferredLocales,
                ct);
        }

        /// <summary>
        /// Creates a new session with a server using the specified channel by invoking the
        /// CreateSession service. With the sessionInstantiator subclasses of Sessions can
        /// be created.
        /// </summary>
        /// <param name="sessionInstantiator">The Session constructor to use to create the session.</param>
        /// <param name="configuration">The configuration for the client application.</param>
        /// <param name="channel">The channel for the server.</param>
        /// <param name="endpoint">The endpoint for the server.</param>
        /// <param name="clientCertificate">The certificate to use for the client.</param>
        /// <param name="availableEndpoints">The list of available endpoints returned by
        /// server in GetEndpoints() response.</param>
        /// <param name="discoveryProfileUris">The value of profileUris used in GetEndpoints()
        /// request.</param>
        public static Session Create(
            ISessionInstantiator sessionInstantiator,
            ApplicationConfiguration configuration,
            ITransportChannel channel,
            ConfiguredEndpoint endpoint,
            X509Certificate2 clientCertificate,
            EndpointDescriptionCollection availableEndpoints = null,
            StringCollection discoveryProfileUris = null)
        {
            return sessionInstantiator.Create(
                channel,
                configuration,
                endpoint,
                clientCertificate,
                availableEndpoints,
                discoveryProfileUris);
        }

        /// <summary>
        /// Creates a secure channel to the specified endpoint.
        /// </summary>
        /// <param name="configuration">The application configuration.</param>
        /// <param name="connection">The client endpoint for the reverse connect.</param>
        /// <param name="endpoint">A configured endpoint to connect to.</param>
        /// <param name="updateBeforeConnect">Update configuration based on server prior connect.</param>
        /// <param name="checkDomain">Check that the certificate specifies a valid domain (computer) name.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
        public static async Task<ITransportChannel> CreateChannelAsync(
            ApplicationConfiguration configuration,
            ITransportWaitingConnection connection,
            ConfiguredEndpoint endpoint,
            bool updateBeforeConnect,
            bool checkDomain,
            CancellationToken ct = default)
        {
            endpoint.UpdateBeforeConnect = updateBeforeConnect;

            EndpointDescription endpointDescription = endpoint.Description;

            // create the endpoint configuration (use the application configuration to provide default values).
            EndpointConfiguration endpointConfiguration = endpoint.Configuration;

            if (endpointConfiguration == null)
            {
                endpoint.Configuration = endpointConfiguration = EndpointConfiguration.Create(
                    configuration);
            }

            // create message context.
            ServiceMessageContext messageContext = configuration.CreateMessageContext(true);

            // update endpoint description using the discovery endpoint.
            if (endpoint.UpdateBeforeConnect && connection == null)
            {
                await endpoint.UpdateFromServerAsync(messageContext.Telemetry, ct).ConfigureAwait(false);
                endpointDescription = endpoint.Description;
                endpointConfiguration = endpoint.Configuration;
            }

            // checks the domains in the certificate.
            if (checkDomain &&
                endpoint.Description.ServerCertificate != null &&
                endpoint.Description.ServerCertificate.Length > 0)
            {
                configuration.CertificateValidator?.ValidateDomains(
                    CertificateFactory.Create(endpoint.Description.ServerCertificate),
                    endpoint);
            }

            X509Certificate2 clientCertificate = null;
            X509Certificate2Collection clientCertificateChain = null;
            if (endpointDescription.SecurityPolicyUri != SecurityPolicies.None)
            {
                clientCertificate = await LoadCertificateAsync(
                    configuration,
                    endpointDescription.SecurityPolicyUri,
                    messageContext.Telemetry,
                    ct)
                    .ConfigureAwait(false);
                clientCertificateChain = await LoadCertificateChainAsync(
                    configuration,
                    clientCertificate,
                    ct)
                    .ConfigureAwait(false);
            }

            // initialize the channel which will be created with the server.
            if (connection != null)
            {
                return UaChannelBase.CreateUaBinaryChannel(
                    configuration,
                    connection,
                    endpointDescription,
                    endpointConfiguration,
                    clientCertificate,
                    clientCertificateChain,
                    messageContext);
            }

            return SessionChannel.Create(
                configuration,
                endpointDescription,
                endpointConfiguration,
                clientCertificate,
                clientCertificateChain,
                messageContext);
        }

        /// <summary>
        /// Creates a new communication session with a server using a reverse connection.
        /// </summary>
        /// <param name="sessionInstantiator">The Session constructor to use to create the session.</param>
        /// <param name="configuration">The configuration for the client application.</param>
        /// <param name="connection">The client endpoint for the reverse connect.</param>
        /// <param name="endpoint">The endpoint for the server.</param>
        /// <param name="updateBeforeConnect">If set to <c>true</c> the discovery endpoint is used to
        /// update the endpoint description before connecting.</param>
        /// <param name="checkDomain">If set to <c>true</c> then the domain in the certificate must match
        /// the endpoint used.</param>
        /// <param name="sessionName">The name to assign to the session.</param>
        /// <param name="sessionTimeout">The timeout period for the session.</param>
        /// <param name="identity">The user identity to associate with the session.</param>
        /// <param name="preferredLocales">The preferred locales.</param>
        /// <param name="returnDiagnostics">The return diagnostics to use on this session</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The new session object.</returns>
        public static async Task<Session> CreateAsync(
            ISessionInstantiator sessionInstantiator,
            ApplicationConfiguration configuration,
            ITransportWaitingConnection connection,
            ConfiguredEndpoint endpoint,
            bool updateBeforeConnect,
            bool checkDomain,
            string sessionName,
            uint sessionTimeout,
            IUserIdentity identity,
            IList<string> preferredLocales,
            DiagnosticsMasks returnDiagnostics,
            CancellationToken ct = default)
        {
            // initialize the channel which will be created with the server.
            ITransportChannel channel = await CreateChannelAsync(
                    configuration,
                    connection,
                    endpoint,
                    updateBeforeConnect,
                    checkDomain,
                    ct)
                .ConfigureAwait(false);

            // create the session object.
            Session session = sessionInstantiator.Create(channel, configuration, endpoint, null);
            session.ReturnDiagnostics = returnDiagnostics;

            // create the session.
            try
            {
                await session
                    .OpenAsync(
                        sessionName,
                        sessionTimeout,
                        identity,
                        preferredLocales,
                        checkDomain,
                        ct)
                    .ConfigureAwait(false);
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
        /// <param name="updateBeforeConnect">If set to <c>true</c> the discovery endpoint is used to
        /// update the endpoint description before connecting.</param>
        /// <param name="checkDomain">If set to <c>true</c> then the domain in the certificate must match
        /// the endpoint used.</param>
        /// <param name="sessionName">The name to assign to the session.</param>
        /// <param name="sessionTimeout">The timeout period for the session.</param>
        /// <param name="userIdentity">The user identity to associate with the session.</param>
        /// <param name="preferredLocales">The preferred locales.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The new session object.</returns>
        [Obsolete("Use ISessionFactory.CreateAsync")]
        public static Task<Session> CreateAsync(
            ApplicationConfiguration configuration,
            ReverseConnectManager reverseConnectManager,
            ConfiguredEndpoint endpoint,
            bool updateBeforeConnect,
            bool checkDomain,
            string sessionName,
            uint sessionTimeout,
            IUserIdentity userIdentity,
            IList<string> preferredLocales,
            CancellationToken ct = default)
        {
            return CreateAsync(
                DefaultSessionFactory.Instance,
                configuration,
                reverseConnectManager,
                endpoint,
                updateBeforeConnect,
                checkDomain,
                sessionName,
                sessionTimeout,
                userIdentity,
                preferredLocales,
                DiagnosticsMasks.None,
                ct);
        }

        /// <summary>
        /// Creates a new communication session with a server using a reverse connect manager.
        /// </summary>
        /// <param name="sessionInstantiator">The Session constructor to use to create the session.</param>
        /// <param name="configuration">The configuration for the client application.</param>
        /// <param name="reverseConnectManager">The reverse connect manager for the client connection.</param>
        /// <param name="endpoint">The endpoint for the server.</param>
        /// <param name="updateBeforeConnect">If set to <c>true</c> the discovery endpoint is used to
        /// update the endpoint description before connecting.</param>
        /// <param name="checkDomain">If set to <c>true</c> then the domain in the certificate must
        /// match the endpoint used.</param>
        /// <param name="sessionName">The name to assign to the session.</param>
        /// <param name="sessionTimeout">The timeout period for the session.</param>
        /// <param name="userIdentity">The user identity to associate with the session.</param>
        /// <param name="preferredLocales">The preferred locales.</param>
        /// <param name="returnDiagnostics">Diagnostics mask to use in the sesion</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The new session object.</returns>
        public static async Task<Session> CreateAsync(
            ISessionInstantiator sessionInstantiator,
            ApplicationConfiguration configuration,
            ReverseConnectManager reverseConnectManager,
            ConfiguredEndpoint endpoint,
            bool updateBeforeConnect,
            bool checkDomain,
            string sessionName,
            uint sessionTimeout,
            IUserIdentity userIdentity,
            IList<string> preferredLocales,
            DiagnosticsMasks returnDiagnostics,
            CancellationToken ct = default)
        {
            if (reverseConnectManager == null)
            {
                return await CreateAsync(
                    sessionInstantiator,
                    configuration,
                    (ITransportWaitingConnection)null,
                    endpoint,
                    updateBeforeConnect,
                    checkDomain,
                    sessionName,
                    sessionTimeout,
                    userIdentity,
                    preferredLocales,
                    returnDiagnostics,
                    ct)
                .ConfigureAwait(false);
            }

            ITransportWaitingConnection connection;
            do
            {
                connection = await reverseConnectManager
                    .WaitForConnectionAsync(
                        endpoint.EndpointUrl,
                        endpoint.ReverseConnect?.ServerUri,
                        ct)
                    .ConfigureAwait(false);

                if (updateBeforeConnect)
                {
                    await endpoint
                        .UpdateFromServerAsync(
                            endpoint.EndpointUrl,
                            connection,
                            endpoint.Description.SecurityMode,
                            endpoint.Description.SecurityPolicyUri,
                            sessionInstantiator.Telemetry,
                            ct)
                        .ConfigureAwait(false);
                    updateBeforeConnect = false;
                    connection = null;
                }
            } while (connection == null);

            return await CreateAsync(
                sessionInstantiator,
                configuration,
                connection,
                endpoint,
                false,
                checkDomain,
                sessionName,
                sessionTimeout,
                userIdentity,
                preferredLocales,
                returnDiagnostics,
                ct)
            .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public event RenewUserIdentityEventHandler RenewUserIdentity
        {
            add => m_RenewUserIdentity += value;
            remove => m_RenewUserIdentity -= value;
        }

        private event RenewUserIdentityEventHandler m_RenewUserIdentity;

        /// <inheritdoc/>
        public bool ApplySessionConfiguration(SessionConfiguration sessionConfiguration)
        {
            if (sessionConfiguration == null)
            {
                throw new ArgumentNullException(nameof(sessionConfiguration));
            }

            byte[] serverCertificate = m_endpoint.Description?.ServerCertificate;
            m_sessionName = sessionConfiguration.SessionName;
            m_serverCertificate =
                serverCertificate != null
                    ? CertificateFactory.Create(serverCertificate)
                    : null;
            m_identity = sessionConfiguration.Identity;
            m_checkDomain = sessionConfiguration.CheckDomain;
            m_serverNonce = sessionConfiguration.ServerNonce.Data;
            m_userTokenSecurityPolicyUri = sessionConfiguration.UserIdentityTokenPolicy;
            m_eccServerEphemeralKey = sessionConfiguration.ServerEccEphemeralKey;
            SessionCreated(
                sessionConfiguration.SessionId,
                sessionConfiguration.AuthenticationToken);

            return true;
        }

        /// <inheritdoc/>
        public SessionConfiguration SaveSessionConfiguration(Stream stream = null)
        {
            var serverNonce = Nonce.CreateNonce(
                m_endpoint.Description?.SecurityPolicyUri,
                m_serverNonce);

            var sessionConfiguration = new SessionConfiguration(
                this,
                serverNonce,
                m_userTokenSecurityPolicyUri,
                m_eccServerEphemeralKey,
                AuthenticationToken);

            if (stream != null)
            {
                XmlWriterSettings settings = Utils.DefaultXmlWriterSettings();
                using var writer = XmlWriter.Create(stream, settings);
                var serializer = new DataContractSerializer(typeof(SessionConfiguration));
                using IDisposable scope = AmbientMessageContext.SetScopedContext(MessageContext);
                serializer.WriteObject(writer, sessionConfiguration);
            }
            return sessionConfiguration;
        }

        /// <inheritdoc/>
        public void Save(string filePath, IEnumerable<Type> knownTypes = null)
        {
            Save(filePath, Subscriptions, knownTypes);
        }

        /// <inheritdoc/>
        public void Save(
            Stream stream,
            IEnumerable<Subscription> subscriptions,
            IEnumerable<Type> knownTypes = null)
        {
            var subscriptionList = new SubscriptionCollection(subscriptions);
            XmlWriterSettings settings = Utils.DefaultXmlWriterSettings();

            using var writer = XmlWriter.Create(stream, settings);
            var serializer = new DataContractSerializer(typeof(SubscriptionCollection), knownTypes);
            using IDisposable scope = AmbientMessageContext.SetScopedContext(MessageContext);
            serializer.WriteObject(writer, subscriptionList);
        }

        /// <inheritdoc/>
        public void Save(
            string filePath,
            IEnumerable<Subscription> subscriptions,
            IEnumerable<Type> knownTypes = null)
        {
            using var stream = new FileStream(filePath, FileMode.Create);
            Save(stream, subscriptions, knownTypes);
        }

        /// <inheritdoc/>
        public IEnumerable<Subscription> Load(
            Stream stream,
            bool transferSubscriptions = false,
            IEnumerable<Type> knownTypes = null)
        {
            // secure settings
            XmlReaderSettings settings = Utils.DefaultXmlReaderSettings();
            settings.CloseInput = true;

            using var reader = XmlReader.Create(stream, settings);
            var serializer = new DataContractSerializer(typeof(SubscriptionCollection), knownTypes);
            using IDisposable scope = AmbientMessageContext.SetScopedContext(MessageContext);
            var subscriptions = (SubscriptionCollection)serializer.ReadObject(reader);
            foreach (Subscription subscription in subscriptions)
            {
                if (!transferSubscriptions)
                {
                    // ServerId must be reset if the saved list of subscriptions
                    // is not used to transfer a subscription
                    foreach (MonitoredItem monitoredItem in subscription.MonitoredItems)
                    {
                        monitoredItem.ServerId = 0;
                    }
                }
                AddSubscription(subscription);
            }
            return subscriptions;
        }

        /// <inheritdoc/>
        public IEnumerable<Subscription> Load(
            string filePath,
            bool transferSubscriptions = false,
            IEnumerable<Type> knownTypes = null)
        {
            using FileStream stream = File.OpenRead(filePath);
            return Load(stream, transferSubscriptions, knownTypes);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(this, obj))
            {
                return true;
            }

            if (obj is ISession session)
            {
                if (!m_endpoint.Equals(session.Endpoint))
                {
                    return false;
                }

                if (!m_sessionName.Equals(session.SessionName, StringComparison.Ordinal))
                {
                    return false;
                }

                if (!SessionId.Equals(session.SessionId))
                {
                    return false;
                }

                return true;
            }

            return false;
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCode.Combine(m_endpoint, m_sessionName, SessionId);
        }

        /// <summary>
        /// An overrideable version of a session clone which is used
        /// internally to create new subclassed clones from a Session class.
        /// </summary>
        public virtual Session CloneSession(ITransportChannel channel, bool copyEventHandlers)
        {
            return new Session(channel, this, copyEventHandlers);
        }

        /// <inheritdoc/>
        public Task OpenAsync(string sessionName, IUserIdentity identity, CancellationToken ct)
        {
            return OpenAsync(sessionName, 0, identity, null, ct);
        }

        /// <inheritdoc/>
        public Task OpenAsync(
            string sessionName,
            uint sessionTimeout,
            IUserIdentity identity,
            IList<string> preferredLocales,
            CancellationToken ct)
        {
            return OpenAsync(sessionName, sessionTimeout, identity, preferredLocales, true, ct);
        }

        /// <inheritdoc/>
        public Task OpenAsync(
            string sessionName,
            uint sessionTimeout,
            IUserIdentity identity,
            IList<string> preferredLocales,
            bool checkDomain,
            CancellationToken ct)
        {
            return OpenAsync(
                sessionName,
                sessionTimeout,
                identity,
                preferredLocales,
                checkDomain,
                true,
                ct);
        }

        /// <inheritdoc/>
        public async Task OpenAsync(
            string sessionName,
            uint sessionTimeout,
            IUserIdentity identity,
            IList<string> preferredLocales,
            bool checkDomain,
            bool closeChannel,
            CancellationToken ct)
        {
            OpenValidateIdentity(
                ref identity,
                out UserIdentityToken identityToken,
                out UserTokenPolicy identityPolicy,
                out string securityPolicyUri,
                out bool requireEncryption);

            // validate the server certificate /certificate chain.
            X509Certificate2 serverCertificate = null;
            byte[] certificateData = m_endpoint.Description.ServerCertificate;

            if (certificateData != null && certificateData.Length > 0)
            {
                X509Certificate2Collection serverCertificateChain = Utils.ParseCertificateChainBlob(
                    certificateData,
                    m_telemetry);

                if (serverCertificateChain.Count > 0)
                {
                    serverCertificate = serverCertificateChain[0];
                }

                if (requireEncryption)
                {
                    // validation skipped until IOP isses are resolved.
                    // ValidateServerCertificateApplicationUri(serverCertificate);
                    if (checkDomain)
                    {
                        await m_configuration
                            .CertificateValidator.ValidateAsync(
                                serverCertificateChain,
                                m_endpoint,
                                ct)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        await m_configuration
                            .CertificateValidator.ValidateAsync(serverCertificateChain, ct)
                            .ConfigureAwait(false);
                    }
                    // save for reconnect
                    m_checkDomain = checkDomain;
                }
            }

            // create a nonce.
            uint length = (uint)m_configuration.SecurityConfiguration.NonceLength;
            byte[] clientNonce = Nonce.CreateRandomNonceData(length);

            // send the application instance certificate for the client.
            BuildCertificateData(
                out byte[] clientCertificateData,
                out byte[] clientCertificateChainData);

            var clientDescription = new ApplicationDescription
            {
                ApplicationUri = m_configuration.ApplicationUri,
                ApplicationName = m_configuration.ApplicationName,
                ApplicationType = ApplicationType.Client,
                ProductUri = m_configuration.ProductUri
            };

            if (sessionTimeout == 0)
            {
                sessionTimeout = (uint)m_configuration.ClientConfiguration.DefaultSessionTimeout;
            }

            // select the security policy for the user token.
            RequestHeader requestHeader = CreateRequestHeaderPerUserTokenPolicy(
                identityPolicy.SecurityPolicyUri,
                m_endpoint.Description.SecurityPolicyUri);

            bool successCreateSession = false;
            CreateSessionResponse response = null;

            //if security none, first try to connect without certificate
            if (m_endpoint.Description.SecurityPolicyUri == SecurityPolicies.None)
            {
                //first try to connect with client certificate NULL
                try
                {
                    response = await base.CreateSessionAsync(
                            null,
                            clientDescription,
                            m_endpoint.Description.Server.ApplicationUri,
                            m_endpoint.EndpointUrl.ToString(),
                            sessionName,
                            clientNonce,
                            null,
                            sessionTimeout,
                            (uint)MessageContext.MaxMessageSize,
                            ct)
                        .ConfigureAwait(false);

                    successCreateSession = true;
                }
                catch (Exception ex)
                {
                    m_logger.LogWarning(ex, "Create session failed with client certificate NULL.");
                    successCreateSession = false;
                }
            }

            if (!successCreateSession)
            {
                response = await base.CreateSessionAsync(
                        requestHeader,
                        clientDescription,
                        m_endpoint.Description.Server.ApplicationUri,
                        m_endpoint.EndpointUrl.ToString(),
                        sessionName,
                        clientNonce,
                        clientCertificateChainData ?? clientCertificateData,
                        sessionTimeout,
                        (uint)MessageContext.MaxMessageSize,
                        ct)
                    .ConfigureAwait(false);
            }

            NodeId sessionId = response.SessionId;
            NodeId sessionCookie = response.AuthenticationToken;
            byte[] serverNonce = response.ServerNonce;
            byte[] serverCertificateData = response.ServerCertificate;
            SignatureData serverSignature = response.ServerSignature;
            EndpointDescriptionCollection serverEndpoints = response.ServerEndpoints;
            SignedSoftwareCertificateCollection serverSoftwareCertificates = response
                .ServerSoftwareCertificates;

            m_sessionTimeout = response.RevisedSessionTimeout;
            m_maxRequestMessageSize = response.MaxRequestMessageSize;

            // save session id.
            lock (SyncRoot)
            {
                // save session id and cookie in base
                base.SessionCreated(sessionId, sessionCookie);
            }

            m_logger.LogInformation("Revised session timeout value: {SessionTimeout}.", m_sessionTimeout);
            m_logger.LogInformation(
                "Max response message size value: {MaxMessageSize}. Max request message size: {MaxRequestSize}",
                MessageContext.MaxMessageSize,
                m_maxRequestMessageSize);

            //we need to call CloseSession if CreateSession was successful but some other exception is thrown
            try
            {
                // verify that the server returned the same instance certificate.
                ValidateServerCertificateData(serverCertificateData);

                ValidateServerEndpoints(serverEndpoints);

                ValidateServerSignature(
                    serverCertificate,
                    serverSignature,
                    clientCertificateData,
                    clientCertificateChainData,
                    clientNonce);

                HandleSignedSoftwareCertificates(serverSoftwareCertificates);

                //  process additional header
                ProcessResponseAdditionalHeader(response.ResponseHeader, serverCertificate);

                // create the client signature.
                byte[] dataToSign = Utils.Append(serverCertificate?.RawData, serverNonce);
                SignatureData clientSignature = SecurityPolicies.Sign(
                    m_instanceCertificate,
                    securityPolicyUri,
                    dataToSign);

                // select the security policy for the user token.
                string tokenSecurityPolicyUri = identityPolicy.SecurityPolicyUri;

                if (string.IsNullOrEmpty(tokenSecurityPolicyUri))
                {
                    tokenSecurityPolicyUri = m_endpoint.Description.SecurityPolicyUri;
                }

                // save previous nonce
                byte[] previousServerNonce = GetCurrentTokenServerNonce();

                // validate server nonce and security parameters for user identity.
                ValidateServerNonce(
                    identity,
                    serverNonce,
                    tokenSecurityPolicyUri,
                    previousServerNonce,
                    m_endpoint.Description.SecurityMode);

                // sign data with user token.
                SignatureData userTokenSignature = identityToken.Sign(
                    dataToSign,
                    tokenSecurityPolicyUri,
                    m_telemetry);

                // encrypt token.
                identityToken.Encrypt(
                    serverCertificate,
                    serverNonce,
                    m_userTokenSecurityPolicyUri,
                    MessageContext,
                    m_eccServerEphemeralKey,
                    m_instanceCertificate,
                    m_instanceCertificateChain,
                    m_endpoint.Description.SecurityMode != MessageSecurityMode.None);

                // send the software certificates assigned to the client.
                SignedSoftwareCertificateCollection clientSoftwareCertificates
                    = GetSoftwareCertificates();

                // copy the preferred locales if provided.
                if (preferredLocales != null && preferredLocales.Count > 0)
                {
                    m_preferredLocales = [.. preferredLocales];
                }

                // activate session.
                ActivateSessionResponse activateResponse = await ActivateSessionAsync(
                        null,
                        clientSignature,
                        clientSoftwareCertificates,
                        m_preferredLocales,
                        new ExtensionObject(identityToken),
                        userTokenSignature,
                        ct)
                    .ConfigureAwait(false);

                //  process additional header
                ProcessResponseAdditionalHeader(activateResponse.ResponseHeader, serverCertificate);

                serverNonce = activateResponse.ServerNonce;
                StatusCodeCollection certificateResults = activateResponse.Results;
                DiagnosticInfoCollection certificateDiagnosticInfos = activateResponse
                    .DiagnosticInfos;

                if (certificateResults != null)
                {
                    for (int i = 0; i < certificateResults.Count; i++)
                    {
                        m_logger.LogInformation(
                            "ActivateSession result[{Index}] = {Result}",
                            i,
                            certificateResults[i]);
                    }
                }

                if (clientSoftwareCertificates?.Count > 0 &&
                    (certificateResults == null || certificateResults.Count == 0))
                {
                    m_logger.LogInformation("Empty results were received for the ActivateSession call.");
                }

                // fetch namespaces.
                await FetchNamespaceTablesAsync(ct).ConfigureAwait(false);

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
                    m_systemContext.SessionId = SessionId;
                    m_systemContext.UserIdentity = identity;
                }

                // fetch operation limits
                await FetchOperationLimitsAsync(ct).ConfigureAwait(false);

                // start keep alive thread.
                await StartKeepAliveTimerAsync().ConfigureAwait(false);

                // raise event that session configuration changed.
                IndicateSessionConfigurationChanged();

                // call session created callback, which was already set in base class only.
                SessionCreated(sessionId, sessionCookie);
            }
            catch (Exception ex)
            {
                m_logger.LogError(ex, "Failed to activate session - closing.");

                try
                {
                    await base.CloseSessionAsync(null, false, CancellationToken.None)
                        .ConfigureAwait(false);
                    if (closeChannel)
                    {
                        await CloseChannelAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                }
                catch (Exception e)
                {
                    m_logger.LogError(
                        e,
                        "Cleanup: CloseSessionAsync() or CloseChannelAsync() raised exception.");
                }
                finally
                {
                    SessionCreated(null, null);
                }

                throw;
            }
        }

        /// <inheritdoc/>
        public Task ChangePreferredLocalesAsync(
            StringCollection preferredLocales,
            CancellationToken ct)
        {
            return UpdateSessionAsync(Identity, preferredLocales, ct);
        }

        /// <inheritdoc/>
        public async Task UpdateSessionAsync(
            IUserIdentity identity,
            StringCollection preferredLocales,
            CancellationToken ct = default)
        {
            byte[] serverNonce = null;

            lock (SyncRoot)
            {
                // check connection state.
                if (!Connected)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidState,
                        "Not connected to server.");
                }

                // get current nonce.
                serverNonce = m_serverNonce;

                preferredLocales ??= m_preferredLocales;
            }

            // get the identity token.
            string securityPolicyUri = m_endpoint.Description.SecurityPolicyUri;

            // create the client signature.
            byte[] dataToSign = Utils.Append(m_serverCertificate?.RawData, serverNonce);
            SignatureData clientSignature = SecurityPolicies.Sign(
                m_instanceCertificate,
                securityPolicyUri,
                dataToSign);

            // choose a default token.
            identity ??= new UserIdentity();

            // check that the user identity is supported by the endpoint.
            UserTokenPolicy identityPolicy =
                m_endpoint.Description.FindUserTokenPolicy(
                    identity.TokenType,
                    identity.IssuedTokenType,
                    securityPolicyUri)
                ?? throw ServiceResultException.Create(
                    StatusCodes.BadUserAccessDenied,
                    "Endpoint does not support the user identity type provided.");

            // select the security policy for the user token.
            string tokenSecurityPolicyUri = identityPolicy.SecurityPolicyUri;

            if (string.IsNullOrEmpty(tokenSecurityPolicyUri))
            {
                tokenSecurityPolicyUri = m_endpoint.Description.SecurityPolicyUri;
            }

            bool requireEncryption = tokenSecurityPolicyUri != SecurityPolicies.None;

            // validate the server certificate before encrypting tokens.
            if (m_serverCertificate != null &&
                requireEncryption &&
                identity.TokenType != UserTokenType.Anonymous)
            {
                m_configuration.CertificateValidator.Validate(m_serverCertificate);
            }

            // validate server nonce and security parameters for user identity.
            ValidateServerNonce(
                identity,
                serverNonce,
                tokenSecurityPolicyUri,
                m_previousServerNonce,
                m_endpoint.Description.SecurityMode);

            // sign data with user token.
            UserIdentityToken identityToken = identity.GetIdentityToken();
            identityToken.PolicyId = identityPolicy.PolicyId;
            SignatureData userTokenSignature = identityToken.Sign(
                dataToSign,
                tokenSecurityPolicyUri,
                m_telemetry);

            m_userTokenSecurityPolicyUri = tokenSecurityPolicyUri;

            // encrypt token.
            identityToken.Encrypt(
                m_serverCertificate,
                serverNonce,
                m_userTokenSecurityPolicyUri,
                MessageContext,
                m_eccServerEphemeralKey,
                m_instanceCertificate,
                m_instanceCertificateChain,
                m_endpoint.Description.SecurityMode != MessageSecurityMode.None);

            // send the software certificates assigned to the client.
            SignedSoftwareCertificateCollection clientSoftwareCertificates
                = GetSoftwareCertificates();

            ActivateSessionResponse response = await ActivateSessionAsync(
                null,
                clientSignature,
                clientSoftwareCertificates,
                preferredLocales,
                new ExtensionObject(identityToken),
                userTokenSignature,
                ct).ConfigureAwait(false);

            serverNonce = response.ServerNonce;

            ProcessResponseAdditionalHeader(response.ResponseHeader, m_serverCertificate);

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
                m_systemContext.SessionId = SessionId;
                m_systemContext.UserIdentity = identity;
            }

            IndicateSessionConfigurationChanged();
        }

        /// <inheritdoc/>
        public async Task<bool> RemoveSubscriptionAsync(
            Subscription subscription,
            CancellationToken ct = default)
        {
            if (subscription == null)
            {
                throw new ArgumentNullException(nameof(subscription));
            }

            if (subscription.Created)
            {
                await subscription.DeleteAsync(false, ct).ConfigureAwait(false);
            }

            lock (SyncRoot)
            {
                if (!m_subscriptions.Remove(subscription))
                {
                    return false;
                }

                subscription.Session = null;
            }

            m_SubscriptionsChanged?.Invoke(this, null);

            return true;
        }

        /// <inheritdoc/>
        public async Task<bool> RemoveSubscriptionsAsync(
            IEnumerable<Subscription> subscriptions,
            CancellationToken ct = default)
        {
            if (subscriptions == null)
            {
                throw new ArgumentNullException(nameof(subscriptions));
            }

            var subscriptionsToDelete = new List<Subscription>();

            bool removed = PrepareSubscriptionsToDelete(subscriptions, subscriptionsToDelete);

            foreach (Subscription subscription in subscriptionsToDelete)
            {
                await subscription.DeleteAsync(true, ct).ConfigureAwait(false);
            }

            if (removed)
            {
                m_SubscriptionsChanged?.Invoke(this, null);
            }

            return removed;
        }

        /// <inheritdoc/>
        public async Task<bool> ReactivateSubscriptionsAsync(
            SubscriptionCollection subscriptions,
            bool sendInitialValues,
            CancellationToken ct = default)
        {
            UInt32Collection subscriptionIds = CreateSubscriptionIdsForTransfer(subscriptions);
            int failedSubscriptions = 0;

            if (subscriptionIds.Count > 0)
            {
                bool reconnecting = false;
                await m_reconnectLock.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    reconnecting = Reconnecting;
                    Reconnecting = true;

                    for (int ii = 0; ii < subscriptions.Count; ii++)
                    {
                        if (!await subscriptions[ii]
                                .TransferAsync(this, subscriptionIds[ii], [], ct)
                                .ConfigureAwait(false))
                        {
                            m_logger.LogError(
                                "SubscriptionId {SubscriptionId} failed to reactivate.",
                                subscriptionIds[ii]);
                            failedSubscriptions++;
                        }
                    }

                    if (sendInitialValues)
                    {
                        (bool success, IList<ServiceResult> resendResults) = await ResendDataAsync(
                            subscriptions,
                            ct)
                            .ConfigureAwait(false);
                        if (!success)
                        {
                            m_logger.LogError("Failed to call resend data for subscriptions.");
                        }
                        else if (resendResults != null)
                        {
                            for (int ii = 0; ii < resendResults.Count; ii++)
                            {
                                // no need to try for subscriptions which do not exist
                                if (StatusCode.IsNotGood(resendResults[ii].StatusCode))
                                {
                                    m_logger.LogError(
                                        "SubscriptionId {SubscriptionId} failed to resend data.",
                                        subscriptionIds[ii]);
                                }
                            }
                        }
                    }

                    m_logger.LogInformation(
                        "Session REACTIVATE of {Count} subscriptions completed. {FailCount} failed.",
                        subscriptions.Count,
                        failedSubscriptions);
                }
                finally
                {
                    Reconnecting = reconnecting;
                    m_reconnectLock.Release();
                }

                StartPublishing(OperationTimeout, true);
            }
            else
            {
                m_logger.LogInformation("No subscriptions. TransferSubscription skipped.");
            }

            return failedSubscriptions == 0;
        }

        /// <inheritdoc/>
        public async Task<(bool, IList<ServiceResult>)> ResendDataAsync(
            IEnumerable<Subscription> subscriptions,
            CancellationToken ct)
        {
            CallMethodRequestCollection requests = CreateCallRequestsForResendData(subscriptions);

            var errors = new List<ServiceResult>(requests.Count);
            try
            {
                CallResponse response = await CallAsync(null, requests, ct).ConfigureAwait(false);
                CallMethodResultCollection results = response.Results;
                DiagnosticInfoCollection diagnosticInfos = response.DiagnosticInfos;
                ResponseHeader responseHeader = response.ResponseHeader;
                ValidateResponse(results, requests);
                ValidateDiagnosticInfos(diagnosticInfos, requests);

                int ii = 0;
                foreach (CallMethodResult value in results)
                {
                    ServiceResult result = ServiceResult.Good;
                    if (StatusCode.IsNotGood(value.StatusCode))
                    {
                        result = GetResult(value.StatusCode, ii, diagnosticInfos, responseHeader);
                    }
                    errors.Add(result);
                    ii++;
                }

                return (true, errors);
            }
            catch (ServiceResultException sre)
            {
                m_logger.LogError(sre, "Failed to call ResendData on server.");
            }

            return (false, errors);
        }

        /// <inheritdoc/>
        public async Task<bool> TransferSubscriptionsAsync(
            SubscriptionCollection subscriptions,
            bool sendInitialValues,
            CancellationToken ct)
        {
            UInt32Collection subscriptionIds = CreateSubscriptionIdsForTransfer(subscriptions);
            int failedSubscriptions = 0;

            if (subscriptionIds.Count > 0)
            {
                bool reconnecting = false;
                await m_reconnectLock.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    reconnecting = Reconnecting;
                    Reconnecting = true;

                    TransferSubscriptionsResponse response = await base.TransferSubscriptionsAsync(
                            null,
                            subscriptionIds,
                            sendInitialValues,
                            ct)
                        .ConfigureAwait(false);
                    TransferResultCollection results = response.Results;
                    DiagnosticInfoCollection diagnosticInfos = response.DiagnosticInfos;
                    ResponseHeader responseHeader = response.ResponseHeader;

                    if (!StatusCode.IsGood(responseHeader.ServiceResult))
                    {
                        m_logger.LogError(
                            "TransferSubscription failed: {ServiceResult}",
                            responseHeader.ServiceResult);
                        return false;
                    }

                    ValidateResponse(results, subscriptionIds);
                    ValidateDiagnosticInfos(diagnosticInfos, subscriptionIds);

                    for (int ii = 0; ii < subscriptions.Count; ii++)
                    {
                        if (StatusCode.IsGood(results[ii].StatusCode))
                        {
                            if (await subscriptions[ii].TransferAsync(
                                    this,
                                    subscriptionIds[ii],
                                    results[ii].AvailableSequenceNumbers,
                                    ct)
                                .ConfigureAwait(false))
                            {
                                lock (m_acknowledgementsToSendLock)
                                {
                                    // create ack for available sequence numbers
                                    foreach (uint sequenceNumber in results[ii]
                                        .AvailableSequenceNumbers)
                                    {
                                        AddAcknowledgementToSend(
                                            m_acknowledgementsToSend,
                                            subscriptionIds[ii],
                                            sequenceNumber);
                                    }
                                }
                            }
                            else
                            {
                                m_logger.LogInformation(
                                    "SubscriptionId {SubscriptionId} could not be moved to session.",
                                    subscriptionIds[ii]);
                                failedSubscriptions++;
                            }
                        }
                        else if (results[ii].StatusCode == StatusCodes.BadNothingToDo)
                        {
                            m_logger.LogInformation(
                                "SubscriptionId {SubscriptionId} is already member of the session.",
                                subscriptionIds[ii]);
                            failedSubscriptions++;
                        }
                        else
                        {
                            m_logger.LogError(
                                "SubscriptionId {SubscriptionId} failed to transfer, StatusCode={StatusCode}",
                                subscriptionIds[ii],
                                results[ii].StatusCode);
                            failedSubscriptions++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    m_logger.LogError(ex,
                        "Session TRANSFER ASYNC of {Count} subscriptions Failed due to unexpected Exception",
                        subscriptions.Count);
                    failedSubscriptions++;
                }
                finally
                {
                    Reconnecting = reconnecting;
                    m_reconnectLock.Release();
                }

                StartPublishing(OperationTimeout, false);
            }
            else
            {
                m_logger.LogInformation("No subscriptions. TransferSubscription skipped.");
            }

            return failedSubscriptions == 0;
        }

        /// <inheritdoc/>
        public async Task FetchNamespaceTablesAsync(CancellationToken ct = default)
        {
            ReadValueIdCollection nodesToRead = PrepareNamespaceTableNodesToRead();

            // read from server.
            ReadResponse response = await ReadAsync(
                null,
                0,
                TimestampsToReturn.Neither,
                nodesToRead,
                ct)
                .ConfigureAwait(false);

            DataValueCollection values = response.Results;
            DiagnosticInfoCollection diagnosticInfos = response.DiagnosticInfos;
            ResponseHeader responseHeader = response.ResponseHeader;

            ValidateResponse(values, nodesToRead);
            ValidateDiagnosticInfos(diagnosticInfos, nodesToRead);

            UpdateNamespaceTable(values, diagnosticInfos, responseHeader);
        }

        /// <inheritdoc/>
        public async Task FetchTypeTreeAsync(ExpandedNodeId typeId, CancellationToken ct = default)
        {
            if (await NodeCache.FindAsync(typeId, ct).ConfigureAwait(false) is Node node)
            {
                var subTypes = new ExpandedNodeIdCollection();
                foreach (IReference reference in node.Find(ReferenceTypeIds.HasSubtype, false))
                {
                    subTypes.Add(reference.TargetId);
                }
                if (subTypes.Count > 0)
                {
                    await FetchTypeTreeAsync(subTypes, ct).ConfigureAwait(false);
                }
            }
        }

        /// <inheritdoc/>
        public async Task FetchTypeTreeAsync(
            ExpandedNodeIdCollection typeIds,
            CancellationToken ct = default)
        {
            var referenceTypeIds = new NodeIdCollection { ReferenceTypeIds.HasSubtype };
            IList<INode> nodes = await NodeCache
                .FindReferencesAsync(typeIds, referenceTypeIds, false, false, ct)
                .ConfigureAwait(false);
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
                await FetchTypeTreeAsync(subTypes, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Fetch the operation limits of the server.
        /// </summary>
        public async Task FetchOperationLimitsAsync(CancellationToken ct = default)
        {
            try
            {
                var operationLimitsProperties = typeof(OperationLimits).GetProperties()
                    .Select(p => p.Name)
                    .ToList();

                var nodeIds = new NodeIdCollection(
                    operationLimitsProperties.Select(name =>
                        (NodeId)
                            typeof(VariableIds)
                                .GetField(
                                    "Server_ServerCapabilities_OperationLimits_" + name,
                                    BindingFlags.Public | BindingFlags.Static)
                                .GetValue(null)))
                {
                    // add the server capability MaxContinuationPointPerBrowse and MaxByteStringLength
                    VariableIds.Server_ServerCapabilities_MaxBrowseContinuationPoints
                };
                int maxBrowseContinuationPointIndex = nodeIds.Count - 1;

                nodeIds.Add(VariableIds.Server_ServerCapabilities_MaxByteStringLength);
                int maxByteStringLengthIndex = nodeIds.Count - 1;

                (DataValueCollection values, IList<ServiceResult> errors) = await ReadValuesAsync(
                    nodeIds,
                    ct)
                    .ConfigureAwait(false);

                OperationLimits configOperationLimits =
                    m_configuration?.ClientConfiguration?.OperationLimits ?? new OperationLimits();
                var operationLimits = new OperationLimits();

                for (int ii = 0; ii < operationLimitsProperties.Count; ii++)
                {
                    PropertyInfo property = typeof(OperationLimits).GetProperty(
                        operationLimitsProperties[ii]);
                    uint value = (uint)property.GetValue(configOperationLimits);
                    if (values[ii] != null &&
                        ServiceResult.IsNotBad(errors[ii]) &&
                        values[ii].Value is uint serverValue &&
                        serverValue > 0 &&
                        (value == 0 || serverValue < value))
                    {
                        value = serverValue;
                    }
                    property.SetValue(operationLimits, value);
                }
                OperationLimits = operationLimits;

                if (values[maxBrowseContinuationPointIndex]
                        .Value is ushort serverMaxContinuationPointsPerBrowse &&
                    ServiceResult.IsNotBad(errors[maxBrowseContinuationPointIndex]))
                {
                    ServerMaxContinuationPointsPerBrowse = serverMaxContinuationPointsPerBrowse;
                }

                if (values[maxByteStringLengthIndex].Value is uint serverMaxByteStringLength &&
                    ServiceResult.IsNotBad(errors[maxByteStringLengthIndex]))
                {
                    ServerMaxByteStringLength = serverMaxByteStringLength;
                }
            }
            catch (Exception ex)
            {
                m_logger.LogError(
                    ex,
                    "Failed to read operation limits from server. Using configuration defaults.");
                OperationLimits operationLimits = m_configuration?.ClientConfiguration?
                    .OperationLimits;
                if (operationLimits != null)
                {
                    OperationLimits = operationLimits;
                }
            }
        }

        /// <inheritdoc/>
        public async Task<(IList<Node>, IList<ServiceResult>)> ReadNodesAsync(
            IList<NodeId> nodeIds,
            NodeClass nodeClass,
            bool optionalAttributes = false,
            CancellationToken ct = default)
        {
            if (nodeIds.Count == 0)
            {
                return (new List<Node>(), new List<ServiceResult>());
            }

            if (nodeClass == NodeClass.Unspecified)
            {
                return await ReadNodesAsync(nodeIds, optionalAttributes, ct).ConfigureAwait(false);
            }

            var nodeCollection = new NodeCollection(nodeIds.Count);

            // determine attributes to read for nodeclass
            var attributesPerNodeId = new List<IDictionary<uint, DataValue>>(nodeIds.Count);
            var attributesToRead = new ReadValueIdCollection();

            CreateNodeClassAttributesReadNodesRequest(
                nodeIds,
                nodeClass,
                attributesToRead,
                attributesPerNodeId,
                nodeCollection,
                optionalAttributes);

            ReadResponse readResponse = await ReadAsync(
                null,
                0,
                TimestampsToReturn.Neither,
                attributesToRead,
                ct)
                .ConfigureAwait(false);

            DataValueCollection values = readResponse.Results;
            DiagnosticInfoCollection diagnosticInfos = readResponse.DiagnosticInfos;

            ValidateResponse(values, attributesToRead);
            ValidateDiagnosticInfos(diagnosticInfos, attributesToRead);

            List<ServiceResult> serviceResults = new ServiceResult[nodeIds.Count].ToList();
            ProcessAttributesReadNodesResponse(
                readResponse.ResponseHeader,
                attributesToRead,
                attributesPerNodeId,
                values,
                diagnosticInfos,
                nodeCollection,
                serviceResults);

            return (nodeCollection, serviceResults);
        }

        /// <inheritdoc/>
        public async Task<(IList<Node>, IList<ServiceResult>)> ReadNodesAsync(
            IList<NodeId> nodeIds,
            bool optionalAttributes = false,
            CancellationToken ct = default)
        {
            if (nodeIds.Count == 0)
            {
                return (new List<Node>(), new List<ServiceResult>());
            }

            var nodeCollection = new NodeCollection(nodeIds.Count);
            var itemsToRead = new ReadValueIdCollection(nodeIds.Count);

            // first read only nodeclasses for nodes from server.
            itemsToRead =
            [
                .. nodeIds.Select(nodeId => new ReadValueId {
                    NodeId = nodeId,
                    AttributeId = Attributes.NodeClass })
            ];

            ReadResponse readResponse = await ReadAsync(
                null,
                0,
                TimestampsToReturn.Neither,
                itemsToRead,
                ct)
                .ConfigureAwait(false);

            DataValueCollection nodeClassValues = readResponse.Results;
            DiagnosticInfoCollection diagnosticInfos = readResponse.DiagnosticInfos;

            ValidateResponse(nodeClassValues, itemsToRead);
            ValidateDiagnosticInfos(diagnosticInfos, itemsToRead);

            // second determine attributes to read per nodeclass
            var attributesPerNodeId = new List<IDictionary<uint, DataValue>>(nodeIds.Count);
            var serviceResults = new List<ServiceResult>(nodeIds.Count);
            var attributesToRead = new ReadValueIdCollection();

            CreateAttributesReadNodesRequest(
                readResponse.ResponseHeader,
                itemsToRead,
                nodeClassValues,
                diagnosticInfos,
                attributesToRead,
                attributesPerNodeId,
                nodeCollection,
                serviceResults,
                optionalAttributes);

            if (attributesToRead.Count > 0)
            {
                readResponse = await ReadAsync(
                    null,
                    0,
                    TimestampsToReturn.Neither,
                    attributesToRead,
                    ct)
                    .ConfigureAwait(false);

                DataValueCollection values = readResponse.Results;
                diagnosticInfos = readResponse.DiagnosticInfos;

                ValidateResponse(values, attributesToRead);
                ValidateDiagnosticInfos(diagnosticInfos, attributesToRead);

                ProcessAttributesReadNodesResponse(
                    readResponse.ResponseHeader,
                    attributesToRead,
                    attributesPerNodeId,
                    values,
                    diagnosticInfos,
                    nodeCollection,
                    serviceResults);
            }

            return (nodeCollection, serviceResults);
        }

        /// <inheritdoc/>
        public Task<Node> ReadNodeAsync(NodeId nodeId, CancellationToken ct = default)
        {
            return ReadNodeAsync(nodeId, NodeClass.Unspecified, true, ct);
        }

        /// <inheritdoc/>
        public async Task<Node> ReadNodeAsync(
            NodeId nodeId,
            NodeClass nodeClass,
            bool optionalAttributes = true,
            CancellationToken ct = default)
        {
            // build list of attributes.
            IDictionary<uint, DataValue> attributes = CreateAttributes(
                nodeClass,
                optionalAttributes);

            // build list of values to read.
            var itemsToRead = new ReadValueIdCollection();
            foreach (uint attributeId in attributes.Keys)
            {
                var itemToRead = new ReadValueId { NodeId = nodeId, AttributeId = attributeId };
                itemsToRead.Add(itemToRead);
            }

            // read from server.
            ReadResponse readResponse = await ReadAsync(
                null,
                0,
                TimestampsToReturn.Neither,
                itemsToRead,
                ct)
                .ConfigureAwait(false);

            DataValueCollection values = readResponse.Results;
            DiagnosticInfoCollection diagnosticInfos = readResponse.DiagnosticInfos;

            ValidateResponse(values, itemsToRead);
            ValidateDiagnosticInfos(diagnosticInfos, itemsToRead);

            return ProcessReadResponse(
                readResponse.ResponseHeader,
                attributes,
                itemsToRead,
                values,
                diagnosticInfos);
        }

        /// <inheritdoc/>
        public async Task<(IList<string>, IList<ServiceResult>)> ReadDisplayNameAsync(
            IList<NodeId> nodeIds,
            CancellationToken ct = default)
        {
            var displayNames = new List<string>();
            var errors = new List<ServiceResult>();

            // build list of values to read.
            var valuesToRead = new ReadValueIdCollection();

            for (int ii = 0; ii < nodeIds.Count; ii++)
            {
                var valueToRead = new ReadValueId
                {
                    NodeId = nodeIds[ii],
                    AttributeId = Attributes.DisplayName,
                    IndexRange = null,
                    DataEncoding = null
                };

                valuesToRead.Add(valueToRead);
            }

            // read the values.

            ReadResponse response = await ReadAsync(
                null,
                int.MaxValue,
                TimestampsToReturn.Neither,
                valuesToRead,
                ct).ConfigureAwait(false);

            DataValueCollection results = response.Results;
            DiagnosticInfoCollection diagnosticInfos = response.DiagnosticInfos;
            ResponseHeader responseHeader = response.ResponseHeader;

            // verify that the server returned the correct number of results.
            ValidateResponse(results, valuesToRead);
            ValidateDiagnosticInfos(diagnosticInfos, valuesToRead);

            for (int ii = 0; ii < nodeIds.Count; ii++)
            {
                displayNames.Add(string.Empty);
                errors.Add(ServiceResult.Good);

                // process any diagnostics associated with bad or uncertain data.
                if (StatusCode.IsNotGood(results[ii].StatusCode))
                {
                    errors[ii] = new ServiceResult(
                        results[ii].StatusCode,
                        ii,
                        diagnosticInfos,
                        responseHeader.StringTable);
                    continue;
                }

                // extract the name.
                LocalizedText displayName = results[ii].GetValue<LocalizedText>(null);

                if (!LocalizedText.IsNullOrEmpty(displayName))
                {
                    displayNames[ii] = displayName.Text;
                }
            }

            return (displayNames, errors);
        }

        /// <inheritdoc/>
        public async Task<ReferenceDescriptionCollection> ReadAvailableEncodingsAsync(
            NodeId variableId,
            CancellationToken ct = default)
        {
            if (await NodeCache.FindAsync(variableId, ct).ConfigureAwait(false)
                is not VariableNode variable)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNodeIdInvalid,
                    "NodeId does not refer to a valid variable node.");
            }

            // no encodings available if there was a problem reading the
            // data type for the node.
            if (NodeId.IsNull(variable.DataType))
            {
                return [];
            }

            // no encodings for non-structures.
            if (!await NodeCache.IsTypeOfAsync(
                variable.DataType,
                DataTypes.Structure,
                ct).ConfigureAwait(false))
            {
                return [];
            }

            // look for cached values.
            IList<INode> encodings = await NodeCache.FindAsync(
                variableId,
                ReferenceTypeIds.HasEncoding,
                false,
                true,
                ct).ConfigureAwait(false);

            if (encodings.Count > 0)
            {
                var references = new ReferenceDescriptionCollection();

                foreach (INode encoding in encodings)
                {
                    var reference = new ReferenceDescription
                    {
                        ReferenceTypeId = ReferenceTypeIds.HasEncoding,
                        IsForward = true,
                        NodeId = encoding.NodeId,
                        NodeClass = encoding.NodeClass,
                        BrowseName = encoding.BrowseName,
                        DisplayName = encoding.DisplayName,
                        TypeDefinition = encoding.TypeDefinitionId
                    };

                    references.Add(reference);
                }

                return references;
            }

            var browser = new Browser(this, m_telemetry)
            {
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = ReferenceTypeIds.HasEncoding,
                IncludeSubtypes = false,
                NodeClassMask = 0
            };

            return await browser.BrowseAsync(variable.DataType, ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<ReferenceDescription> FindDataDescriptionAsync(NodeId encodingId,
            CancellationToken ct = default)
        {
            var browser = new Browser(this, m_telemetry)
            {
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = ReferenceTypeIds.HasDescription,
                IncludeSubtypes = false,
                NodeClassMask = 0
            };

            ReferenceDescriptionCollection references =
                await browser.BrowseAsync(encodingId, ct).ConfigureAwait(false);

            if (references.Count == 0)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNodeIdInvalid,
                    "Encoding does not refer to a valid data description.");
            }

            return references[0];
        }

        /// <inheritdoc/>
        public async Task<(NodeIdCollection, IList<ServiceResult>)> FindComponentIdsAsync(
            NodeId instanceId,
            IList<string> componentPaths,
            CancellationToken ct = default)
        {
            var componentIds = new NodeIdCollection();
            var errors = new List<ServiceResult>();

            // build list of paths to translate.
            var pathsToTranslate = new BrowsePathCollection();

            for (int ii = 0; ii < componentPaths.Count; ii++)
            {
                var pathToTranslate = new BrowsePath
                {
                    StartingNode = instanceId,
                    RelativePath = RelativePath.Parse(componentPaths[ii], TypeTree)
                };

                pathsToTranslate.Add(pathToTranslate);
            }

            // translate the paths.

            TranslateBrowsePathsToNodeIdsResponse response = await TranslateBrowsePathsToNodeIdsAsync(
                null,
                pathsToTranslate,
                ct).ConfigureAwait(false);

            BrowsePathResultCollection results = response.Results;
            DiagnosticInfoCollection diagnosticInfos = response.DiagnosticInfos;
            ResponseHeader responseHeader = response.ResponseHeader;

            // verify that the server returned the correct number of results.
            ValidateResponse(results, pathsToTranslate);
            ValidateDiagnosticInfos(diagnosticInfos, pathsToTranslate);

            for (int ii = 0; ii < componentPaths.Count; ii++)
            {
                componentIds.Add(NodeId.Null);
                errors.Add(ServiceResult.Good);

                // process any diagnostics associated with any error.
                if (StatusCode.IsBad(results[ii].StatusCode))
                {
                    errors[ii] = new ServiceResult(
                        results[ii].StatusCode,
                        ii,
                        diagnosticInfos,
                        responseHeader.StringTable);
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

                if (results[ii].Targets[0].RemainingPathIndex != uint.MaxValue)
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
                componentIds[ii] = ExpandedNodeId.ToNodeId(
                    results[ii].Targets[0].TargetId,
                    NamespaceUris);
            }
            return (componentIds, errors);
        }

        /// <inheritdoc/>
        public async Task<T> ReadValueAsync<T>(NodeId nodeId, CancellationToken ct = default)
        {
            DataValue dataValue = await ReadValueAsync(nodeId, ct).ConfigureAwait(false);
            object value = dataValue.Value;

            if (value is ExtensionObject extension)
            {
                value = extension.Body;
            }

            if (!typeof(T).IsInstanceOfType(value))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadTypeMismatch,
                    "Server returned value unexpected type: {0}",
                    value != null ? value.GetType().Name : "(null)");
            }
            return (T)value;
        }

        /// <inheritdoc/>
        public async Task<DataValue> ReadValueAsync(NodeId nodeId, CancellationToken ct = default)
        {
            var itemToRead = new ReadValueId
            {
                NodeId = nodeId,
                AttributeId = Attributes.Value
            };
            var itemsToRead = new ReadValueIdCollection { itemToRead };

            // read from server.
            ReadResponse readResponse = await ReadAsync(
                null,
                0,
                TimestampsToReturn.Both,
                itemsToRead,
                ct)
                .ConfigureAwait(false);

            DataValueCollection values = readResponse.Results;
            DiagnosticInfoCollection diagnosticInfos = readResponse.DiagnosticInfos;

            ValidateResponse(values, itemsToRead);
            ValidateDiagnosticInfos(diagnosticInfos, itemsToRead);

            if (StatusCode.IsBad(values[0].StatusCode))
            {
                ServiceResult result = GetResult(
                    values[0].StatusCode,
                    0,
                    diagnosticInfos,
                    readResponse.ResponseHeader);
                throw new ServiceResultException(result);
            }

            return values[0];
        }

        /// <inheritdoc/>
        public async Task<(DataValueCollection, IList<ServiceResult>)> ReadValuesAsync(
            IList<NodeId> nodeIds,
            CancellationToken ct = default)
        {
            if (nodeIds.Count == 0)
            {
                return (new DataValueCollection(), new List<ServiceResult>());
            }

            // read all values from server.
            var itemsToRead = new ReadValueIdCollection(
                nodeIds.Select(
                    nodeId => new ReadValueId { NodeId = nodeId, AttributeId = Attributes.Value }));

            // read from server.
            var errors = new List<ServiceResult>(itemsToRead.Count);

            ReadResponse readResponse = await ReadAsync(
                null,
                0,
                TimestampsToReturn.Both,
                itemsToRead,
                ct)
                .ConfigureAwait(false);

            DataValueCollection values = readResponse.Results;
            DiagnosticInfoCollection diagnosticInfos = readResponse.DiagnosticInfos;

            ValidateResponse(values, itemsToRead);
            ValidateDiagnosticInfos(diagnosticInfos, itemsToRead);

            foreach (DataValue value in values)
            {
                ServiceResult result = ServiceResult.Good;
                if (StatusCode.IsBad(value.StatusCode))
                {
                    result = GetResult(
                        values[0].StatusCode,
                        0,
                        diagnosticInfos,
                        readResponse.ResponseHeader);
                }
                errors.Add(result);
            }

            return (values, errors);
        }

        /// <inheritdoc/>
        public async Task<byte[]> ReadByteStringInChunksAsync(NodeId nodeId, CancellationToken ct)
        {
            int count = (int)ServerMaxByteStringLength;

            int maxByteStringLength = m_configuration.TransportQuotas.MaxByteStringLength;
            if (maxByteStringLength > 0)
            {
                count =
                    ServerMaxByteStringLength > maxByteStringLength
                        ? maxByteStringLength
                        : (int)ServerMaxByteStringLength;
            }

            if (count <= 1)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadIndexRangeNoData,
                    "The MaxByteStringLength is not known or too small for reading data in chunks.");
            }

            int offset = 0;
            using var bytes = new MemoryStream();
            while (true)
            {
                var valueToRead = new ReadValueId
                {
                    NodeId = nodeId,
                    AttributeId = Attributes.Value,
                    IndexRange = new NumericRange(offset, offset + count - 1).ToString(),
                    DataEncoding = null
                };
                var readValueIds = new ReadValueIdCollection { valueToRead };

                ReadResponse result = await ReadAsync(
                    null,
                    0,
                    TimestampsToReturn.Neither,
                    readValueIds,
                    ct)
                    .ConfigureAwait(false);

                ResponseHeader responseHeader = result.ResponseHeader;
                DataValueCollection results = result.Results;
                DiagnosticInfoCollection diagnosticInfos = result.DiagnosticInfos;
                ValidateResponse(results, readValueIds);
                ValidateDiagnosticInfos(diagnosticInfos, readValueIds);

                if (offset == 0)
                {
                    Variant wrappedValue = results[0].WrappedValue;
                    if (wrappedValue.TypeInfo.BuiltInType != BuiltInType.ByteString ||
                        wrappedValue.TypeInfo.ValueRank != ValueRanks.Scalar)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadTypeMismatch,
                            "Value is not a ByteString scalar.");
                    }
                }

                if (StatusCode.IsBad(results[0].StatusCode))
                {
                    if (results[0].StatusCode == StatusCodes.BadIndexRangeNoData)
                    {
                        // this happens when the previous read has fetched all remaining data
                        break;
                    }
                    ServiceResult serviceResult = GetResult(
                        results[0].StatusCode,
                        0,
                        diagnosticInfos,
                        responseHeader);
                    throw new ServiceResultException(serviceResult);
                }

                if (results[0].Value is not byte[] chunk || chunk.Length == 0)
                {
                    break;
                }

                bytes.Write(chunk, 0, chunk.Length);
                if (chunk.Length < count)
                {
                    break;
                }
                offset += count;
            }
            return bytes.ToArray();
        }

        /// <inheritdoc/>
        public async Task<(
            ResponseHeader responseHeader,
            ByteStringCollection continuationPoints,
            IList<ReferenceDescriptionCollection> referencesList,
            IList<ServiceResult> errors
        )> BrowseAsync(
            RequestHeader requestHeader,
            ViewDescription view,
            IList<NodeId> nodesToBrowse,
            uint maxResultsToReturn,
            BrowseDirection browseDirection,
            NodeId referenceTypeId,
            bool includeSubtypes,
            uint nodeClassMask,
            CancellationToken ct = default)
        {
            var browseDescriptions = new BrowseDescriptionCollection();
            foreach (NodeId nodeToBrowse in nodesToBrowse)
            {
                var description = new BrowseDescription
                {
                    NodeId = nodeToBrowse,
                    BrowseDirection = browseDirection,
                    ReferenceTypeId = referenceTypeId,
                    IncludeSubtypes = includeSubtypes,
                    NodeClassMask = nodeClassMask,
                    ResultMask = (uint)BrowseResultMask.All
                };

                browseDescriptions.Add(description);
            }

            BrowseResponse browseResponse = await BrowseAsync(
                    requestHeader,
                    view,
                    maxResultsToReturn,
                    browseDescriptions,
                    ct)
                .ConfigureAwait(false);

            ValidateResponse(browseResponse.ResponseHeader);
            BrowseResultCollection results = browseResponse.Results;
            DiagnosticInfoCollection diagnosticInfos = browseResponse.DiagnosticInfos;

            ValidateResponse(results, browseDescriptions);
            ValidateDiagnosticInfos(diagnosticInfos, browseDescriptions);

            int ii = 0;
            var errors = new List<ServiceResult>();
            var continuationPoints = new ByteStringCollection();
            var referencesList = new List<ReferenceDescriptionCollection>();
            foreach (BrowseResult result in results)
            {
                if (StatusCode.IsBad(result.StatusCode))
                {
                    errors.Add(
                        new ServiceResult(
                            result.StatusCode,
                            ii,
                            diagnosticInfos,
                            browseResponse.ResponseHeader.StringTable));
                }
                else
                {
                    errors.Add(ServiceResult.Good);
                }
                continuationPoints.Add(result.ContinuationPoint);
                referencesList.Add(result.References);
                ii++;
            }

            return (browseResponse.ResponseHeader, continuationPoints, referencesList, errors);
        }

        /// <inheritdoc/>
        public async Task<(
            ResponseHeader responseHeader,
            ByteStringCollection revisedContinuationPoints,
            IList<ReferenceDescriptionCollection> referencesList,
            IList<ServiceResult> errors
        )> BrowseNextAsync(
            RequestHeader requestHeader,
            ByteStringCollection continuationPoints,
            bool releaseContinuationPoint,
            CancellationToken ct = default)
        {
            BrowseNextResponse response = await base.BrowseNextAsync(
                    requestHeader,
                    releaseContinuationPoint,
                    continuationPoints,
                    ct)
                .ConfigureAwait(false);

            ValidateResponse(response.ResponseHeader);

            BrowseResultCollection results = response.Results;
            DiagnosticInfoCollection diagnosticInfos = response.DiagnosticInfos;

            ValidateResponse(results, continuationPoints);
            ValidateDiagnosticInfos(diagnosticInfos, continuationPoints);

            int ii = 0;
            var errors = new List<ServiceResult>();
            var revisedContinuationPoints = new ByteStringCollection();
            var referencesList = new List<ReferenceDescriptionCollection>();
            foreach (BrowseResult result in results)
            {
                if (StatusCode.IsBad(result.StatusCode))
                {
                    errors.Add(
                        new ServiceResult(
                            result.StatusCode,
                            ii,
                            diagnosticInfos,
                            response.ResponseHeader.StringTable));
                }
                else
                {
                    errors.Add(ServiceResult.Good);
                }
                revisedContinuationPoints.Add(result.ContinuationPoint);
                referencesList.Add(result.References);
                ii++;
            }

            return (response.ResponseHeader, revisedContinuationPoints, referencesList, errors);
        }

        /// <inheritdoc/>
        public async Task<(IList<ReferenceDescriptionCollection>, IList<ServiceResult>)> ManagedBrowseAsync(
            RequestHeader requestHeader,
            ViewDescription view,
            IList<NodeId> nodesToBrowse,
            uint maxResultsToReturn,
            BrowseDirection browseDirection,
            NodeId referenceTypeId,
            bool includeSubtypes,
            uint nodeClassMask,
            CancellationToken ct = default)
        {
            int count = nodesToBrowse.Count;
            var result = new List<ReferenceDescriptionCollection>(count);
            var errors = new List<ServiceResult>(count);

            // first attempt for implementation: create the references for the output in advance.
            // optimize later, when everything works fine.
            for (int i = 0; i < nodesToBrowse.Count; i++)
            {
                result.Add([]);
                errors.Add(new ServiceResult(StatusCodes.Good));
            }

            try
            {
                // in the first pass, we browse all nodes from the input.
                // Some nodes may need to be browsed again, these are then fed into the next pass.
                var nodesToBrowseForPass = new List<NodeId>(count);
                nodesToBrowseForPass.AddRange(nodesToBrowse);

                var resultForPass = new List<ReferenceDescriptionCollection>(count);
                resultForPass.AddRange(result);

                var errorsForPass = new List<ServiceResult>(count);
                errorsForPass.AddRange(errors);

                int passCount = 0;

                do
                {
                    int badNoCPErrorsPerPass = 0;
                    int badCPInvalidErrorsPerPass = 0;
                    int otherErrorsPerPass = 0;
                    uint maxNodesPerBrowse = OperationLimits.MaxNodesPerBrowse;

                    if (ContinuationPointPolicy == ContinuationPointPolicy.Balanced &&
                        ServerMaxContinuationPointsPerBrowse > 0)
                    {
                        maxNodesPerBrowse =
                            ServerMaxContinuationPointsPerBrowse < maxNodesPerBrowse
                                ? ServerMaxContinuationPointsPerBrowse
                                : maxNodesPerBrowse;
                    }

                    // split input into batches
                    int batchOffset = 0;

                    var nodesToBrowseForNextPass = new List<NodeId>();
                    var referenceDescriptionsForNextPass
                        = new List<ReferenceDescriptionCollection>();
                    var errorsForNextPass = new List<ServiceResult>();

                    // loop over the batches
                    foreach (
                        List<NodeId> nodesToBrowseBatch in nodesToBrowseForPass
                            .Batch<NodeId, List<NodeId>>(
                                maxNodesPerBrowse))
                    {
                        int nodesToBrowseBatchCount = nodesToBrowseBatch.Count;

                        (IList<ReferenceDescriptionCollection> resultForBatch, IList<ServiceResult> errorsForBatch) =
                            await BrowseWithBrowseNextAsync(
                                    requestHeader,
                                    view,
                                    nodesToBrowseBatch,
                                    maxResultsToReturn,
                                    browseDirection,
                                    referenceTypeId,
                                    includeSubtypes,
                                    nodeClassMask,
                                    ct)
                                .ConfigureAwait(false);

                        int resultOffset = batchOffset;
                        for (int ii = 0; ii < nodesToBrowseBatchCount; ii++)
                        {
                            StatusCode statusCode = errorsForBatch[ii].StatusCode;
                            if (StatusCode.IsBad(statusCode))
                            {
                                bool addToNextPass = false;
                                if (statusCode == StatusCodes.BadNoContinuationPoints)
                                {
                                    addToNextPass = true;
                                    badNoCPErrorsPerPass++;
                                }
                                else if (statusCode == StatusCodes.BadContinuationPointInvalid)
                                {
                                    addToNextPass = true;
                                    badCPInvalidErrorsPerPass++;
                                }
                                else
                                {
                                    otherErrorsPerPass++;
                                }

                                if (addToNextPass)
                                {
                                    nodesToBrowseForNextPass.Add(
                                        nodesToBrowseForPass[resultOffset]);
                                    referenceDescriptionsForNextPass.Add(
                                        resultForPass[resultOffset]);
                                    errorsForNextPass.Add(errorsForPass[resultOffset]);
                                }
                            }

                            resultForPass[resultOffset].Clear();
                            resultForPass[resultOffset].AddRange(resultForBatch[ii]);
                            errorsForPass[resultOffset] = errorsForBatch[ii];
                            resultOffset++;
                        }

                        batchOffset += nodesToBrowseBatchCount;
                    }

                    resultForPass = referenceDescriptionsForNextPass;
                    referenceDescriptionsForNextPass = [];

                    errorsForPass = errorsForNextPass;
                    errorsForNextPass = [];

                    nodesToBrowseForPass = nodesToBrowseForNextPass;
                    nodesToBrowseForNextPass = [];

                    if (badCPInvalidErrorsPerPass > 0)
                    {
                        m_logger.LogDebug(
                            "ManagedBrowse: in pass {Pass}, {Count} error(s) occured with a status code {StatusCode}.",
                            passCount,
                            badCPInvalidErrorsPerPass,
                            nameof(StatusCodes.BadContinuationPointInvalid));
                    }
                    if (badNoCPErrorsPerPass > 0)
                    {
                        m_logger.LogDebug(
                            "ManagedBrowse: in pass {Pass}, {Count} error(s) occured with a status code {StatusCode}.",
                            passCount,
                            badNoCPErrorsPerPass,
                            nameof(StatusCodes.BadNoContinuationPoints));
                    }
                    if (otherErrorsPerPass > 0)
                    {
                        m_logger.LogDebug(
                            "ManagedBrowse: in pass {Pass}, {Count} error(s) occured with a status code {StatusCode}.",
                            passCount,
                            otherErrorsPerPass,
                            $"different from {nameof(StatusCodes.BadNoContinuationPoints)} or {nameof(StatusCodes.BadContinuationPointInvalid)}");
                    }
                    if (otherErrorsPerPass == 0 &&
                        badCPInvalidErrorsPerPass == 0 &&
                        badNoCPErrorsPerPass == 0)
                    {
                        m_logger.LogTrace("ManagedBrowse completed with no errors.");
                    }

                    passCount++;
                } while (nodesToBrowseForPass.Count > 0);
            }
            catch (Exception ex)
            {
                m_logger.LogError(ex, "ManagedBrowse failed");
            }

            return (result, errors);
        }

        /// <summary>
        /// Used to pass on references to the Service results in the loop in ManagedBrowseAsync.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        private class ReferenceWrapper<T>
        {
            public T Reference { get; set; }
        }

        /// <summary>
        /// Call the browse service asynchronously and call browse next,
        /// if applicable, immediately afterwards. Observe proper treatment
        /// of specific service results, specifically
        /// BadNoContinuationPoint and BadContinuationPointInvalid
        /// </summary>
        private async Task<(IList<ReferenceDescriptionCollection>, IList<ServiceResult>)> BrowseWithBrowseNextAsync(
            RequestHeader requestHeader,
            ViewDescription view,
            List<NodeId> nodeIds,
            uint maxResultsToReturn,
            BrowseDirection browseDirection,
            NodeId referenceTypeId,
            bool includeSubtypes,
            uint nodeClassMask,
            CancellationToken ct = default)
        {
            if (requestHeader != null)
            {
                requestHeader.RequestHandle = 0;
            }

            var result = new List<ReferenceDescriptionCollection>(nodeIds.Count);

            (
                _,
                ByteStringCollection continuationPoints,
                IList<ReferenceDescriptionCollection> referenceDescriptions,
                IList<ServiceResult> errors
            ) = await BrowseAsync(
                    requestHeader,
                    view,
                    nodeIds,
                    maxResultsToReturn,
                    browseDirection,
                    referenceTypeId,
                    includeSubtypes,
                    nodeClassMask,
                    ct)
                .ConfigureAwait(false);

            result.AddRange(referenceDescriptions);

            // process any continuation point.
            List<ReferenceDescriptionCollection> previousResults = result;
            var errorAnchors = new List<ReferenceWrapper<ServiceResult>>();
            var previousErrors = new List<ReferenceWrapper<ServiceResult>>();
            foreach (ServiceResult error in errors)
            {
                previousErrors.Add(new ReferenceWrapper<ServiceResult> { Reference = error });
                errorAnchors.Add(previousErrors[^1]);
            }

            var nextContinuationPoints = new ByteStringCollection();
            var nextResults = new List<ReferenceDescriptionCollection>();
            var nextErrors = new List<ReferenceWrapper<ServiceResult>>();

            for (int ii = 0; ii < nodeIds.Count; ii++)
            {
                if (continuationPoints[ii] != null &&
                    !StatusCode.IsBad(previousErrors[ii].Reference.StatusCode))
                {
                    nextContinuationPoints.Add(continuationPoints[ii]);
                    nextResults.Add(previousResults[ii]);
                    nextErrors.Add(previousErrors[ii]);
                }
            }
            while (nextContinuationPoints.Count > 0)
            {
                if (requestHeader != null)
                {
                    requestHeader.RequestHandle = 0;
                }

                (
                    _,
                    ByteStringCollection revisedContinuationPoints,
                    IList<ReferenceDescriptionCollection> browseNextResults,
                    IList<ServiceResult> browseNextErrors
                ) = await BrowseNextAsync(requestHeader, nextContinuationPoints, false, ct)
                    .ConfigureAwait(false);

                for (int ii = 0; ii < browseNextResults.Count; ii++)
                {
                    nextResults[ii].AddRange(browseNextResults[ii]);
                    nextErrors[ii].Reference = browseNextErrors[ii];
                }

                previousResults = nextResults;
                previousErrors = nextErrors;

                nextResults = [];
                nextErrors = [];
                nextContinuationPoints = [];

                for (int ii = 0; ii < revisedContinuationPoints.Count; ii++)
                {
                    if (revisedContinuationPoints[ii] != null &&
                        !StatusCode.IsBad(browseNextErrors[ii].StatusCode))
                    {
                        nextContinuationPoints.Add(revisedContinuationPoints[ii]);
                        nextResults.Add(previousResults[ii]);
                        nextErrors.Add(previousErrors[ii]);
                    }
                }
            }
            var finalErrors = new List<ServiceResult>(errorAnchors.Count);
            foreach (ReferenceWrapper<ServiceResult> errorReference in errorAnchors)
            {
                finalErrors.Add(errorReference.Reference);
            }

            return (result, finalErrors);
        }

        /// <inheritdoc/>
        public async Task<IList<object>> CallAsync(
            NodeId objectId,
            NodeId methodId,
            CancellationToken ct = default,
            params object[] args)
        {
            var inputArguments = new VariantCollection();

            if (args != null)
            {
                for (int ii = 0; ii < args.Length; ii++)
                {
                    inputArguments.Add(new Variant(args[ii]));
                }
            }

            var request = new CallMethodRequest
            {
                ObjectId = objectId,
                MethodId = methodId,
                InputArguments = inputArguments
            };

            var requests = new CallMethodRequestCollection { request };

            CallMethodResultCollection results;
            DiagnosticInfoCollection diagnosticInfos;

            CallResponse response = await base.CallAsync(null, requests, ct).ConfigureAwait(false);

            results = response.Results;
            diagnosticInfos = response.DiagnosticInfos;

            ValidateResponse(results, requests);
            ValidateDiagnosticInfos(diagnosticInfos, requests);

            if (StatusCode.IsBad(results[0].StatusCode))
            {
                throw ServiceResultException.Create(
                    results[0].StatusCode,
                    0,
                    diagnosticInfos,
                    response.ResponseHeader.StringTable);
            }

            var outputArguments = new List<object>();

            foreach (Variant arg in results[0].OutputArguments)
            {
                outputArguments.Add(arg.Value);
            }

            return outputArguments;
        }

        /// <inheritdoc/>
        public async Task<ReferenceDescriptionCollection> FetchReferencesAsync(
            NodeId nodeId,
            CancellationToken ct = default)
        {
            (IList<ReferenceDescriptionCollection> descriptions, _) = await ManagedBrowseAsync(
                    null,
                    null,
                    [nodeId],
                    0,
                    BrowseDirection.Both,
                    null,
                    true,
                    0,
                    ct)
                .ConfigureAwait(false);
            return descriptions[0];
        }

        /// <inheritdoc/>
        public Task<(IList<ReferenceDescriptionCollection>, IList<ServiceResult>)> FetchReferencesAsync(
            IList<NodeId> nodeIds,
            CancellationToken ct = default)
        {
            return ManagedBrowseAsync(
                null,
                null,
                nodeIds,
                0,
                BrowseDirection.Both,
                null,
                true,
                0,
                ct);
        }

        /// <summary>
        /// Recreates a session based on a specified template.
        /// </summary>
        /// <param name="sessionTemplate">The Session object to use as template</param>
        /// <param name="ct">Cancellation Token to cancel operation with</param>
        /// <returns>The new session object.</returns>
        public static async Task<Session> RecreateAsync(
            Session sessionTemplate,
            CancellationToken ct = default)
        {
            ServiceMessageContext messageContext = sessionTemplate.m_configuration
                .CreateMessageContext();
            messageContext.Factory = sessionTemplate.Factory;

            // create the channel object used to connect to the server.
            ITransportChannel channel = SessionChannel.Create(
                sessionTemplate.m_configuration,
                sessionTemplate.ConfiguredEndpoint.Description,
                sessionTemplate.ConfiguredEndpoint.Configuration,
                sessionTemplate.m_instanceCertificate,
                sessionTemplate.m_configuration.SecurityConfiguration.SendCertificateChain
                    ? sessionTemplate.m_instanceCertificateChain
                    : null,
                messageContext);

            // create the session object.
            Session session = sessionTemplate.CloneSession(channel, true);

            try
            {
                session.RecreateRenewUserIdentity();
                // open the session.
                await session
                    .OpenAsync(
                        sessionTemplate.SessionName,
                        (uint)sessionTemplate.SessionTimeout,
                        session.Identity,
                        sessionTemplate.PreferredLocales,
                        sessionTemplate.m_checkDomain,
                        ct)
                    .ConfigureAwait(false);

                await session.RecreateSubscriptionsAsync(sessionTemplate.Subscriptions, ct)
                    .ConfigureAwait(false);
            }
            catch (Exception e)
            {
                session.Dispose();
                ThrowCouldNotRecreateSessionException(e, sessionTemplate.m_sessionName);
            }

            return session;
        }

        /// <summary>
        /// Recreates a session based on a specified template.
        /// </summary>
        /// <param name="sessionTemplate">The Session object to use as template</param>
        /// <param name="connection">The waiting reverse connection.</param>
        /// <param name="ct">Cancelation token to cancel operation with</param>
        /// <returns>The new session object.</returns>
        public static async Task<Session> RecreateAsync(
            Session sessionTemplate,
            ITransportWaitingConnection connection,
            CancellationToken ct = default)
        {
            ServiceMessageContext messageContext = sessionTemplate.m_configuration
                .CreateMessageContext();
            messageContext.Factory = sessionTemplate.Factory;

            // create the channel object used to connect to the server.
            ITransportChannel channel = SessionChannel.Create(
                sessionTemplate.m_configuration,
                connection,
                sessionTemplate.m_endpoint.Description,
                sessionTemplate.m_endpoint.Configuration,
                sessionTemplate.m_instanceCertificate,
                sessionTemplate.m_configuration.SecurityConfiguration.SendCertificateChain
                    ? sessionTemplate.m_instanceCertificateChain
                    : null,
                messageContext);

            // create the session object.
            Session session = sessionTemplate.CloneSession(channel, true);

            try
            {
                session.RecreateRenewUserIdentity();
                // open the session.
                await session
                    .OpenAsync(
                        sessionTemplate.m_sessionName,
                        (uint)sessionTemplate.m_sessionTimeout,
                        session.Identity,
                        sessionTemplate.m_preferredLocales,
                        sessionTemplate.m_checkDomain,
                        ct)
                    .ConfigureAwait(false);

                await session.RecreateSubscriptionsAsync(sessionTemplate.Subscriptions, ct)
                    .ConfigureAwait(false);
            }
            catch (Exception e)
            {
                session.Dispose();
                ThrowCouldNotRecreateSessionException(e, sessionTemplate.m_sessionName);
            }

            return session;
        }

        /// <summary>
        /// Recreates a session based on a specified template using the provided channel.
        /// </summary>
        /// <param name="sessionTemplate">The Session object to use as template</param>
        /// <param name="transportChannel">The waiting reverse connection.</param>
        /// <param name="ct">Cancellation token to cancel the operation with</param>
        /// <returns>The new session object.</returns>
        public static async Task<Session> RecreateAsync(
            Session sessionTemplate,
            ITransportChannel transportChannel,
            CancellationToken ct = default)
        {
            if (transportChannel == null)
            {
                return await RecreateAsync(sessionTemplate, ct).ConfigureAwait(false);
            }

            ServiceMessageContext messageContext = sessionTemplate.m_configuration
                .CreateMessageContext();
            messageContext.Factory = sessionTemplate.Factory;

            // create the session object.
            Session session = sessionTemplate.CloneSession(transportChannel, true);

            try
            {
                session.RecreateRenewUserIdentity();
                // open the session.
                await session
                    .OpenAsync(
                        sessionTemplate.m_sessionName,
                        (uint)sessionTemplate.m_sessionTimeout,
                        session.Identity,
                        sessionTemplate.m_preferredLocales,
                        sessionTemplate.m_checkDomain,
                        false,
                        ct)
                    .ConfigureAwait(false);

                // create the subscriptions.
                foreach (Subscription subscription in session.Subscriptions)
                {
                    await subscription.CreateAsync(ct).ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                session.Dispose();
                ThrowCouldNotRecreateSessionException(e, sessionTemplate.m_sessionName);
            }

            return session;
        }

        /// <inheritdoc/>
        public override Task<StatusCode> CloseAsync(CancellationToken ct = default)
        {
            return CloseAsync(m_keepAliveInterval, true, ct);
        }

        /// <inheritdoc/>
        public Task<StatusCode> CloseAsync(bool closeChannel, CancellationToken ct = default)
        {
            return CloseAsync(m_keepAliveInterval, closeChannel, ct);
        }

        /// <inheritdoc/>
        public Task<StatusCode> CloseAsync(int timeout, CancellationToken ct = default)
        {
            return CloseAsync(timeout, true, ct);
        }

        /// <inheritdoc/>
        public virtual async Task<StatusCode> CloseAsync(
            int timeout,
            bool closeChannel,
            CancellationToken ct = default)
        {
            // check if already called.
            if (Disposed)
            {
                return StatusCodes.Good;
            }

            StatusCode result = StatusCodes.Good;

            // stop the keep alive timer.
            await StopKeepAliveTimerAsync().ConfigureAwait(false);

            // check if correctly connected.
            bool connected = Connected;

            // halt all background threads.
            if (connected && m_SessionClosing != null)
            {
                try
                {
                    m_SessionClosing(this, null);
                }
                catch (Exception e)
                {
                    m_logger.LogError(e, "Session: Unexpected error raising SessionClosing event.");
                }
            }

            // close the session with the server.
            if (connected && !KeepAliveStopped)
            {
                try
                {
                    // close the session and delete all subscriptions if specified.
                    var requestHeader = new RequestHeader
                    {
                        TimeoutHint = timeout > 0
                            ? (uint)timeout
                            : (uint)(OperationTimeout > 0 ? OperationTimeout : 0)
                    };
                    CloseSessionResponse response = await base.CloseSessionAsync(
                            requestHeader,
                            DeleteSubscriptionsOnClose,
                            ct)
                        .ConfigureAwait(false);

                    if (closeChannel)
                    {
                        await CloseChannelAsync(ct).ConfigureAwait(false);
                    }

                    // raised notification indicating the session is closed.
                    SessionCreated(null, null);
                }
                // don't throw errors on disconnect, but return them
                // so the caller can log the error.
                catch (ServiceResultException sre)
                {
                    result = sre.StatusCode;
                }
                catch (Exception)
                {
                    result = StatusCodes.Bad;
                }
            }

            // clean up.
            if (closeChannel)
            {
                Dispose();
            }

            return result;
        }

        /// <inheritdoc/>
        public Task ReconnectAsync(CancellationToken ct)
        {
            return ReconnectAsync(null, null, ct);
        }

        /// <inheritdoc/>
        public Task ReconnectAsync(ITransportWaitingConnection connection, CancellationToken ct)
        {
            return ReconnectAsync(connection, null, ct);
        }

        /// <inheritdoc/>
        public Task ReconnectAsync(ITransportChannel channel, CancellationToken ct)
        {
            return ReconnectAsync(null, channel, ct);
        }

        /// <inheritdoc/>
        public async Task ReloadInstanceCertificateAsync(CancellationToken ct = default)
        {
            await m_reconnectLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await LoadInstanceCertificateAsync(clientCertificate: null, ct).ConfigureAwait(false);
            }
            finally
            {
                m_reconnectLock.Release();
            }
        }

        /// <summary>
        /// Reconnects to the server after a network failure using a waiting connection.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private async Task ReconnectAsync(
            ITransportWaitingConnection connection,
            ITransportChannel transportChannel,
            CancellationToken ct)
        {
            bool resetReconnect = false;
            await m_reconnectLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                bool reconnecting = Reconnecting;
                Reconnecting = true;
                resetReconnect = true;
                m_reconnectLock.Release();

                // check if already connecting.
                if (reconnecting)
                {
                    m_logger.LogWarning("Session is already attempting to reconnect.");

                    throw ServiceResultException.Create(
                        StatusCodes.BadInvalidState,
                        "Session is already attempting to reconnect.");
                }

                m_logger.LogInformation("Session RECONNECT {SessionId} starting...", SessionId);

                await StopKeepAliveTimerAsync().ConfigureAwait(false);

                // create the client signature.
                byte[] dataToSign = Utils.Append(m_serverCertificate?.RawData, m_serverNonce);
                EndpointDescription endpoint = m_endpoint.Description;
                SignatureData clientSignature = SecurityPolicies.Sign(
                    m_instanceCertificate,
                    endpoint.SecurityPolicyUri,
                    dataToSign);

                // check that the user identity is supported by the endpoint.
                UserTokenPolicy identityPolicy = endpoint.FindUserTokenPolicy(
                    m_identity.TokenType,
                    m_identity.IssuedTokenType,
                    endpoint.SecurityPolicyUri);

                if (identityPolicy == null)
                {
                    m_logger.LogError(
                        "Reconnect: Endpoint does not support the user identity type provided.");

                    throw ServiceResultException.Create(
                        StatusCodes.BadUserAccessDenied,
                        "Endpoint does not support the user identity type provided.");
                }

                // select the security policy for the user token.
                string tokenSecurityPolicyUri = identityPolicy.SecurityPolicyUri;

                if (string.IsNullOrEmpty(tokenSecurityPolicyUri))
                {
                    tokenSecurityPolicyUri = endpoint.SecurityPolicyUri;
                }
                m_userTokenSecurityPolicyUri = tokenSecurityPolicyUri;

                // need to refresh the identity (reprompt for password, refresh token).
                if (m_RenewUserIdentity != null)
                {
                    m_identity = m_RenewUserIdentity(this, m_identity);
                }

                // validate server nonce and security parameters for user identity.
                ValidateServerNonce(
                    m_identity,
                    m_serverNonce,
                    tokenSecurityPolicyUri,
                    m_previousServerNonce,
                    m_endpoint.Description.SecurityMode);

                // sign data with user token.
                UserIdentityToken identityToken = m_identity.GetIdentityToken();
                identityToken.PolicyId = identityPolicy.PolicyId;
                SignatureData userTokenSignature = identityToken.Sign(
                    dataToSign,
                    tokenSecurityPolicyUri,
                    m_telemetry);

                // encrypt token.
                identityToken.Encrypt(
                    m_serverCertificate,
                    m_serverNonce,
                    m_userTokenSecurityPolicyUri,
                    MessageContext,
                    m_eccServerEphemeralKey,
                    m_instanceCertificate,
                    m_instanceCertificateChain,
                    m_endpoint.Description.SecurityMode != MessageSecurityMode.None);

                // send the software certificates assigned to the client.
                SignedSoftwareCertificateCollection clientSoftwareCertificates
                    = GetSoftwareCertificates();

                m_logger.LogInformation("Session REPLACING channel for {SessionId}.", SessionId);

                if (connection != null)
                {
                    ITransportChannel channel = NullableTransportChannel;

                    // check if the channel supports reconnect.
                    if (channel != null &&
                        (channel.SupportedFeatures & TransportChannelFeatures.Reconnect) != 0)
                    {
                        channel.Reconnect(connection);
                    }
                    else
                    {
                        // initialize the channel which will be created with the server.
                        channel = SessionChannel.Create(
                            m_configuration,
                            connection,
                            m_endpoint.Description,
                            m_endpoint.Configuration,
                            m_instanceCertificate,
                            m_configuration.SecurityConfiguration.SendCertificateChain
                                ? m_instanceCertificateChain
                                : null,
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
                    ITransportChannel channel = NullableTransportChannel;

                    // check if the channel supports reconnect.
                    if (channel != null &&
                        (channel.SupportedFeatures & TransportChannelFeatures.Reconnect) != 0)
                    {
                        channel.Reconnect();
                    }
                    else
                    {
                        // initialize the channel which will be created with the server.
                        channel = SessionChannel.Create(
                            m_configuration,
                            m_endpoint.Description,
                            m_endpoint.Configuration,
                            m_instanceCertificate,
                            m_configuration.SecurityConfiguration.SendCertificateChain
                                ? m_instanceCertificateChain
                                : null,
                            MessageContext);

                        // disposes the existing channel.
                        TransportChannel = channel;
                    }
                }

                m_logger.LogInformation("Session RE-ACTIVATING {SessionId}.", SessionId);

                var header = new RequestHeader { TimeoutHint = kReconnectTimeout };

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromMilliseconds(kReconnectTimeout / 2));
                try
                {
                    // reactivate session.
                    ActivateSessionResponse activateResult = await ActivateSessionAsync(
                        header,
                        clientSignature,
                        null,
                        m_preferredLocales,
                        new ExtensionObject(identityToken),
                        userTokenSignature,
                        timeout.Token).ConfigureAwait(false);

                    byte[] serverNonce = activateResult.ServerNonce;
                    StatusCodeCollection certificateResults = activateResult.Results;
                    DiagnosticInfoCollection certificateDiagnosticInfos = activateResult.DiagnosticInfos;

                    m_logger.LogInformation("Session RECONNECT {SessionId} completed successfully.", SessionId);

                    lock (SyncRoot)
                    {
                        m_previousServerNonce = m_serverNonce;
                        m_serverNonce = serverNonce;
                    }

                    await m_reconnectLock.WaitAsync(ct).ConfigureAwait(false);
                    Reconnecting = false;
                    resetReconnect = false;
                    m_reconnectLock.Release();

                    StartPublishing(OperationTimeout, true);

                    await StartKeepAliveTimerAsync().ConfigureAwait(false);

                    IndicateSessionConfigurationChanged();
                }
                catch (OperationCanceledException)
                    when (timeout.IsCancellationRequested && !ct.IsCancellationRequested)
                {
                    var error = ServiceResult.Create(
                        StatusCodes.BadRequestTimeout,
                        "ACTIVATE SESSION timed out. {0}/{1}",
                        GoodPublishRequestCount,
                        OutstandingRequestCount);

                    m_logger.LogWarning(
                        "ACTIVATE SESSION ASYNC timed out. {GoodRequestCount}/{OutstandingRequestCount}",
                        GoodPublishRequestCount,
                        OutstandingRequestCount);
                    throw new ServiceResultException(error);
                }
            }
            finally
            {
                if (resetReconnect)
                {
                    await m_reconnectLock.WaitAsync(ct).ConfigureAwait(false);
                    Reconnecting = false;
                    m_reconnectLock.Release();
                }
            }
        }

        /// <inheritdoc/>
        public async Task<(bool, ServiceResult)> RepublishAsync(
            uint subscriptionId,
            uint sequenceNumber,
            CancellationToken ct)
        {
            // send republish request.
            var requestHeader = new RequestHeader
            {
                TimeoutHint = (uint)OperationTimeout,
                ReturnDiagnostics = (uint)(int)ReturnDiagnostics,
                RequestHandle = Utils.IncrementIdentifier(ref m_publishCounter)
            };

            try
            {
                m_logger.LogInformation(
                    "Requesting RepublishAsync for {SubscriptionId}-{SequenceNumber}",
                    subscriptionId,
                    sequenceNumber);

                // request republish.
                RepublishResponse response = await RepublishAsync(
                    requestHeader,
                    subscriptionId,
                    sequenceNumber,
                    ct)
                    .ConfigureAwait(false);
                ResponseHeader responseHeader = response.ResponseHeader;
                NotificationMessage notificationMessage = response.NotificationMessage;

                m_logger.LogInformation(
                    "Received RepublishAsync for {SubscriptionId}-{SequenceNumber}-{ServiceResult}",
                    subscriptionId,
                    sequenceNumber,
                    responseHeader.ServiceResult);

                // process response.
                ProcessPublishResponse(
                    responseHeader,
                    subscriptionId,
                    null,
                    false,
                    notificationMessage);

                return (true, ServiceResult.Good);
            }
            catch (Exception e)
            {
                return ProcessRepublishResponseError(e, subscriptionId, sequenceNumber);
            }
        }

        /// <summary>
        /// Recreate the subscriptions in a reconnected session.
        /// Uses Transfer service if <see cref="TransferSubscriptionsOnReconnect"/> is set to <c>true</c>.
        /// </summary>
        /// <param name="subscriptionsTemplate">The template for the subscriptions.</param>
        /// <param name="ct">Cancelation token to cancel operation with</param>
        private async Task RecreateSubscriptionsAsync(
            IEnumerable<Subscription> subscriptionsTemplate,
            CancellationToken ct)
        {
            bool transferred = false;
            if (TransferSubscriptionsOnReconnect)
            {
                try
                {
                    transferred = await TransferSubscriptionsAsync(
                        [.. subscriptionsTemplate],
                        false,
                        ct)
                        .ConfigureAwait(false);
                }
                catch (ServiceResultException sre)
                {
                    if (sre.StatusCode == StatusCodes.BadServiceUnsupported)
                    {
                        TransferSubscriptionsOnReconnect = false;
                        m_logger.LogWarning(
                            "Transfer subscription unsupported, TransferSubscriptionsOnReconnect set to false.");
                    }
                    else
                    {
                        m_logger.LogError(sre, "Transfer subscriptions failed.");
                    }
                }
                catch (Exception ex)
                {
                    m_logger.LogError(ex, "Unexpected Transfer subscriptions error.");
                }
            }

            if (!transferred)
            {
                // Create the subscriptions which were not transferred.
                foreach (Subscription subscription in Subscriptions)
                {
                    if (!subscription.Created)
                    {
                        await subscription.CreateAsync(ct).ConfigureAwait(false);
                    }
                }
            }
        }

        /// <inheritdoc/>
        public bool AddSubscription(Subscription subscription)
        {
            if (subscription == null)
            {
                throw new ArgumentNullException(nameof(subscription));
            }

            lock (SyncRoot)
            {
                if (m_subscriptions.Contains(subscription))
                {
                    return false;
                }

                subscription.Session = this;
                subscription.Telemetry = m_telemetry;
                m_subscriptions.Add(subscription);
            }

            m_SubscriptionsChanged?.Invoke(this, null);

            return true;
        }

        /// <inheritdoc/>
        public bool RemoveTransferredSubscription(Subscription subscription)
        {
            if (subscription == null)
            {
                throw new ArgumentNullException(nameof(subscription));
            }

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

            m_SubscriptionsChanged?.Invoke(this, null);

            return true;
        }

        /// <summary>
        /// Returns the software certificates assigned to the application.
        /// </summary>
        protected virtual SignedSoftwareCertificateCollection GetSoftwareCertificates()
        {
            return [];
        }

        /// <summary>
        /// Handles an error when validating the application instance certificate provided by the server.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        protected virtual void OnApplicationCertificateError(
            byte[] serverCertificate,
            ServiceResult result)
        {
            throw new ServiceResultException(result);
        }

        /// <summary>
        /// Handles an error when validating software certificates provided by the server.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        protected virtual void OnSoftwareCertificateError(
            SignedSoftwareCertificate signedCertificate,
            ServiceResult result)
        {
            throw new ServiceResultException(result);
        }

        /// <summary>
        /// Inspects the software certificates provided by the server.
        /// </summary>
        protected virtual void ValidateSoftwareCertificates(
            List<SoftwareCertificate> softwareCertificates)
        {
            // always accept valid certificates.
        }

        /// <summary>
        /// Starts a timer to check that the connection to the server is still available.
        /// </summary>
        private async ValueTask StartKeepAliveTimerAsync()
        {
            int keepAliveInterval = m_keepAliveInterval;

            m_lastKeepAliveErrorStatusCode = StatusCodes.Good;
            Interlocked.Exchange(ref m_lastKeepAliveTime, DateTime.UtcNow.Ticks);
            LastKeepAliveTickCount = HiResClock.TickCount;

            m_serverState = ServerState.Unknown;

            var nodesToRead = new ReadValueIdCollection
            {
                // read the server state.
                new ReadValueId
                {
                    NodeId = Variables.Server_ServerStatus_State,
                    AttributeId = Attributes.Value,
                    DataEncoding = null,
                    IndexRange = null
                }
            };

            // restart the publish timer.

            await StopKeepAliveTimerAsync().ConfigureAwait(false);

            lock (SyncRoot)
            {
                if (m_keepAliveWorker == null)
                {
                    m_keepAliveCancellation = new CancellationTokenSource();

                    // start timer
                    m_keepAliveWorker = Task
                        .Factory.StartNew(
                            () => OnSendKeepAliveAsync(
                                nodesToRead,
                                m_keepAliveCancellation.Token),
                            m_keepAliveCancellation.Token,
                            TaskCreationOptions.LongRunning,
                            TaskScheduler.Default);
                }

                // send initial keep alive.
                m_keepAliveTimer.Change(0, m_keepAliveInterval);
            }
        }

        /// <summary>
        /// Reset the timer used to send keep alive messages.
        /// </summary>
        private void ResetKeepAliveTimer()
        {
            lock (SyncRoot)
            {
                if (m_keepAliveWorker != null)
                {
                    m_keepAliveTimer.Change(m_keepAliveInterval, m_keepAliveInterval);
                }
            }
        }

        /// <summary>
        /// Stops the keep alive timer.
        /// </summary>
        private async ValueTask StopKeepAliveTimerAsync()
        {
            Task keepAliveWorker;
            CancellationTokenSource keepAliveCancellation;

            lock (SyncRoot)
            {
                keepAliveWorker = m_keepAliveWorker;
                keepAliveCancellation = m_keepAliveCancellation;

                m_keepAliveWorker = null;
                m_keepAliveCancellation = null;

                m_keepAliveTimer.Change(Timeout.Infinite, Timeout.Infinite);
            }

            if (keepAliveWorker == null)
            {
                Debug.Assert(keepAliveCancellation == null);
                return;
            }
            try
            {
                keepAliveCancellation.Cancel();
                await keepAliveWorker.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                m_logger.LogDebug(ex, "Keep alive task did not stop cleanly.");
            }
            finally
            {
                keepAliveCancellation.Dispose();
            }
        }

        /// <summary>
        /// Removes a completed async request.
        /// </summary>
        private AsyncRequestState RemoveRequest(object result, uint requestId, uint typeId)
        {
            lock (m_outstandingRequests)
            {
                for (LinkedListNode<AsyncRequestState> ii = m_outstandingRequests.First;
                    ii != null;
                    ii = ii.Next)
                {
                    if (ReferenceEquals(result, ii.Value.Result) ||
                        (requestId == ii.Value.RequestId && typeId == ii.Value.RequestTypeId))
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
        private void AsyncRequestStarted(object result, uint requestId, uint typeId)
        {
            lock (m_outstandingRequests)
            {
                // check if the request completed asynchronously.
                AsyncRequestState state = RemoveRequest(result, requestId, typeId);

                // add a new request.
                if (state == null)
                {
                    state = new AsyncRequestState
                    {
                        Defunct = false,
                        RequestId = requestId,
                        RequestTypeId = typeId,
                        Result = result,
                        TickCount = HiResClock.TickCount
                    };

                    m_outstandingRequests.AddLast(state);
                }
            }
        }

        /// <summary>
        /// Removes a completed async request.
        /// </summary>
        private void AsyncRequestCompleted(object result, uint requestId, uint typeId)
        {
            lock (m_outstandingRequests)
            {
                // remove the request.
                AsyncRequestState state = RemoveRequest(result, requestId, typeId);

                if (state != null)
                {
                    // mark any old requests as default (i.e. the should have returned before this request).
                    const int maxAge = 1000;

                    for (LinkedListNode<AsyncRequestState> ii = m_outstandingRequests.First;
                        ii != null;
                        ii = ii.Next)
                    {
                        if (ii.Value.RequestTypeId == typeId &&
                            (state.TickCount - ii.Value.TickCount) > maxAge)
                        {
                            ii.Value.Defunct = true;
                        }
                    }
                }

                // add a dummy placeholder since the begin request has not completed yet.
                if (state == null)
                {
                    state = new AsyncRequestState
                    {
                        Defunct = true,
                        RequestId = requestId,
                        RequestTypeId = typeId,
                        Result = result,
                        TickCount = HiResClock.TickCount
                    };

                    m_outstandingRequests.AddLast(state);
                }
            }
        }

        /// <summary>
        /// Sends a keep alive by reading from the server.
        /// </summary>
        private async Task OnSendKeepAliveAsync(
            ReadValueIdCollection nodesToRead,
            CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                await m_keepAliveEvent.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    // check if session has been closed.
                    if (!Connected)
                    {
                        continue;
                    }

                    // check if session has been closed.
                    if (Reconnecting)
                    {
                        m_logger.LogWarning(
                            "Session {SessionId}: KeepAlive ignored while reconnecting.",
                            SessionId);
                        continue;
                    }

                    // raise error if keep alives are not coming back.
                    if (KeepAliveStopped &&
                        !OnKeepAliveError(
                            ServiceResult.Create(
                                StatusCodes.BadNoCommunication,
                                "Server not responding to keep alive requests.")
                        ))
                    {
                        continue;
                    }

                    var requestHeader = new RequestHeader
                    {
                        RequestHandle = Utils.IncrementIdentifier(ref m_keepAliveCounter),
                        TimeoutHint = (uint)(KeepAliveInterval * 2),
                        ReturnDiagnostics = 0
                    };

                    ReadResponse result = await ReadAsync(
                        requestHeader,
                        0,
                        TimestampsToReturn.Neither,
                        nodesToRead,
                        ct).ConfigureAwait(false);

                    // read the server status.
                    DataValueCollection values = result.Results;
                    DiagnosticInfoCollection diagnosticInfos = result.DiagnosticInfos;
                    ResponseHeader responseHeader = result.ResponseHeader;

                    ValidateResponse(values, nodesToRead);
                    ValidateDiagnosticInfos(diagnosticInfos, nodesToRead);

                    // validate value returned.
                    ServiceResult error = ValidateDataValue(
                        values[0],
                        typeof(int),
                        0,
                        diagnosticInfos,
                        responseHeader);

                    if (ServiceResult.IsBad(error))
                    {
                        m_logger.LogError("Keep alive read failed: {ServiceResult}, EndpointUrl={EndpointUrl}, RequestCount={Good}/{Outstanding}",
                            error,
                            Endpoint?.EndpointUrl,
                            GoodPublishRequestCount,
                            OutstandingRequestCount);
                        throw new ServiceResultException(error);
                    }

                    // send notification that keep alive completed.
                    OnKeepAlive((ServerState)(int)values[0].Value, responseHeader.Timestamp);
                }
                catch (ServiceResultException sre)
                {
                    // recover from error condition when secure channel is still alive
                    OnKeepAliveError(sre.Result);
                }
                catch (Exception e)
                {
                    m_logger.LogError(
                        "Could not send keep alive request: {RequestType} {Message}",
                        e.GetType().FullName,
                        e.Message);
                }
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
                if (Reconnecting)
                {
                    return;
                }

                m_lastKeepAliveErrorStatusCode = StatusCodes.Good;
                Interlocked.Exchange(ref m_lastKeepAliveTime, DateTime.UtcNow.Ticks);
                LastKeepAliveTickCount = HiResClock.TickCount;

                lock (m_outstandingRequests)
                {
                    for (LinkedListNode<AsyncRequestState> ii = m_outstandingRequests.First;
                        ii != null;
                        ii = ii.Next)
                    {
                        if (ii.Value.RequestTypeId == DataTypes.PublishRequest)
                        {
                            ii.Value.Defunct = true;
                        }
                    }
                }

                StartPublishing(OperationTimeout, false);
            }
            else
            {
                m_lastKeepAliveErrorStatusCode = StatusCodes.Good;
                Interlocked.Exchange(ref m_lastKeepAliveTime, DateTime.UtcNow.Ticks);
                LastKeepAliveTickCount = HiResClock.TickCount;
            }

            // save server state.
            m_serverState = currentState;

            KeepAliveEventHandler callback = m_KeepAlive;

            if (callback != null)
            {
                try
                {
                    callback(this, new KeepAliveEventArgs(null, currentState, currentTime));
                }
                catch (Exception e)
                {
                    m_logger.LogError(e, "Session: Unexpected error invoking KeepAliveCallback.");
                }
            }
        }

        /// <summary>
        /// Called when a error occurs during a keep alive.
        /// </summary>
        protected virtual bool OnKeepAliveError(ServiceResult result)
        {
            m_lastKeepAliveErrorStatusCode = result.StatusCode;
            if (result.StatusCode == StatusCodes.BadNoCommunication)
            {
                //keep alive read timed out
                int delta = HiResClock.TickCount - LastKeepAliveTickCount;
                m_logger.LogInformation(
                    "KEEP ALIVE LATE: {Duration}ms, EndpointUrl={EndpointUrl}, RequestCount={Good}/{Outstanding}",
                    delta,
                    Endpoint?.EndpointUrl,
                    GoodPublishRequestCount,
                    OutstandingRequestCount);
            }

            KeepAliveEventHandler callback = m_KeepAlive;

            if (callback != null)
            {
                try
                {
                    var args = new KeepAliveEventArgs(result, ServerState.Unknown, DateTime.UtcNow);
                    callback(this, args);
                    return !args.CancelKeepAlive;
                }
                catch (Exception e)
                {
                    m_logger.LogError(e, "Session: Unexpected error invoking KeepAliveCallback.");
                }
            }

            return true;
        }

        /// <summary>
        /// Prepare a list of subscriptions to delete.
        /// </summary>
        private bool PrepareSubscriptionsToDelete(
            IEnumerable<Subscription> subscriptions,
            List<Subscription> subscriptionsToDelete)
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
        private static void CreateNodeClassAttributesReadNodesRequest(
            IList<NodeId> nodeIdCollection,
            NodeClass nodeClass,
            ReadValueIdCollection attributesToRead,
            List<IDictionary<uint, DataValue>> attributesPerNodeId,
            NodeCollection nodeCollection,
            bool optionalAttributes)
        {
            for (int ii = 0; ii < nodeIdCollection.Count; ii++)
            {
                var node = new Node { NodeId = nodeIdCollection[ii], NodeClass = nodeClass };

                Dictionary<uint, DataValue> attributes = CreateAttributes(
                    node.NodeClass,
                    optionalAttributes);
                foreach (uint attributeId in attributes.Keys)
                {
                    var itemToRead = new ReadValueId
                    {
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
        /// Prepares the list of node ids to read to fetch the namespace table.
        /// </summary>
        private static ReadValueIdCollection PrepareNamespaceTableNodesToRead()
        {
            var nodesToRead = new ReadValueIdCollection();

            // request namespace array.
            var valueId = new ReadValueId
            {
                NodeId = Variables.Server_NamespaceArray,
                AttributeId = Attributes.Value
            };

            nodesToRead.Add(valueId);

            // request server array.
            valueId = new ReadValueId
            {
                NodeId = Variables.Server_ServerArray,
                AttributeId = Attributes.Value
            };

            nodesToRead.Add(valueId);

            return nodesToRead;
        }

        /// <summary>
        /// Updates the NamespaceTable with the result of the
        /// <see cref="PrepareNamespaceTableNodesToRead"/> read operation.
        /// </summary>
        private void UpdateNamespaceTable(
            DataValueCollection values,
            DiagnosticInfoCollection diagnosticInfos,
            ResponseHeader responseHeader)
        {
            // validate namespace array.
            ServiceResult result = ValidateDataValue(
                values[0],
                typeof(string[]),
                0,
                diagnosticInfos,
                responseHeader);

            if (ServiceResult.IsBad(result))
            {
                m_logger.LogError(
                    "FetchNamespaceTables: Cannot read NamespaceArray node: {StatusCOde}",
                    result.StatusCode);
            }
            else
            {
                NamespaceUris.Update((string[])values[0].Value);
            }

            // validate server array.
            result = ValidateDataValue(
                values[1],
                typeof(string[]),
                1,
                diagnosticInfos,
                responseHeader);

            if (ServiceResult.IsBad(result))
            {
                m_logger.LogError(
                    "FetchNamespaceTables: Cannot read ServerArray node: {StatusCode} ",
                    result.StatusCode);
            }
            else
            {
                ServerUris.Update((string[])values[1].Value);
            }
        }

        /// <summary>
        /// Creates a read request with attributes determined by the NodeClass.
        /// </summary>
        private static void CreateAttributesReadNodesRequest(
            ResponseHeader responseHeader,
            ReadValueIdCollection itemsToRead,
            DataValueCollection nodeClassValues,
            DiagnosticInfoCollection diagnosticInfos,
            ReadValueIdCollection attributesToRead,
            List<IDictionary<uint, DataValue>> attributesPerNodeId,
            NodeCollection nodeCollection,
            List<ServiceResult> errors,
            bool optionalAttributes)
        {
            int? nodeClass;
            for (int ii = 0; ii < itemsToRead.Count; ii++)
            {
                var node = new Node { NodeId = itemsToRead[ii].NodeId };
                if (!DataValue.IsGood(nodeClassValues[ii]))
                {
                    nodeCollection.Add(node);
                    errors.Add(
                        new ServiceResult(
                            nodeClassValues[ii].StatusCode,
                            ii,
                            diagnosticInfos,
                            responseHeader.StringTable));
                    attributesPerNodeId.Add(null);
                    continue;
                }

                // check for valid node class.
                nodeClass = nodeClassValues[ii].Value as int?;

                if (nodeClass == null)
                {
                    nodeCollection.Add(node);
                    errors.Add(
                        ServiceResult.Create(
                            StatusCodes.BadUnexpectedError,
                            "Node does not have a valid value for NodeClass: {0}.",
                            nodeClassValues[ii].Value));
                    attributesPerNodeId.Add(null);
                    continue;
                }

                node.NodeClass = (NodeClass)nodeClass;

                Dictionary<uint, DataValue> attributes = CreateAttributes(
                    node.NodeClass,
                    optionalAttributes);
                foreach (uint attributeId in attributes.Keys)
                {
                    var itemToRead = new ReadValueId
                    {
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
        /// <param name="responseHeader">The response header of the read request.</param>
        /// <param name="attributesToRead">The collection of all attributes to read passed in the read request.</param>
        /// <param name="attributesPerNodeId">The attributes requested per NodeId</param>
        /// <param name="values">The attribute values returned by the read request.</param>
        /// <param name="diagnosticInfos">The diagnostic info returned by the read request.</param>
        /// <param name="nodeCollection">The node collection which holds the results.</param>
        /// <param name="errors">The service results for each node.</param>
        private static void ProcessAttributesReadNodesResponse(
            ResponseHeader responseHeader,
            ReadValueIdCollection attributesToRead,
            List<IDictionary<uint, DataValue>> attributesPerNodeId,
            DataValueCollection values,
            DiagnosticInfoCollection diagnosticInfos,
            NodeCollection nodeCollection,
            List<ServiceResult> errors)
        {
            int readIndex = 0;
            for (int ii = 0; ii < nodeCollection.Count; ii++)
            {
                IDictionary<uint, DataValue> attributes = attributesPerNodeId[ii];
                if (attributes == null)
                {
                    continue;
                }

                int readCount = attributes.Count;
                var subRangeAttributes = new ReadValueIdCollection(
                    attributesToRead.GetRange(readIndex, readCount));
                var subRangeValues = new DataValueCollection(values.GetRange(readIndex, readCount));
                DiagnosticInfoCollection subRangeDiagnostics =
                    diagnosticInfos.Count > 0
                        ? [.. diagnosticInfos.GetRange(readIndex, readCount)]
                        : diagnosticInfos;
                try
                {
                    nodeCollection[ii] = ProcessReadResponse(
                        responseHeader,
                        attributes,
                        subRangeAttributes,
                        subRangeValues,
                        subRangeDiagnostics);
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
        /// <exception cref="ServiceResultException"></exception>
        private static Node ProcessReadResponse(
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
                        throw ServiceResultException.Create(
                            values[ii].StatusCode,
                            ii,
                            diagnosticInfos,
                            responseHeader.StringTable);
                    }

                    // check for valid node class.
                    nodeClass = values[ii].Value as int?;

                    if (nodeClass == null)
                    {
                        throw ServiceResultException.Unexpected(
                            "Node does not have a valid value for NodeClass: {0}.",
                            values[ii].Value);
                    }
                }
                else if (!DataValue.IsGood(values[ii]))
                {
                    // check for unsupported attributes.
                    if (values[ii].StatusCode == StatusCodes.BadAttributeIdInvalid)
                    {
                        continue;
                    }

                    // ignore errors on optional attributes
                    if (StatusCode.IsBad(values[ii].StatusCode) &&
                        attributeId
                            is Attributes.AccessRestrictions
                                or Attributes.Description
                                or Attributes.RolePermissions
                                or Attributes.UserRolePermissions
                                or Attributes.UserWriteMask
                                or Attributes.WriteMask
                                or Attributes.AccessLevelEx
                                or Attributes.ArrayDimensions
                                or Attributes.DataTypeDefinition
                                or Attributes.InverseName
                                or Attributes.MinimumSamplingInterval)
                    {
                        continue;
                    }

                    // all supported attributes must be readable.
                    if (attributeId != Attributes.Value)
                    {
                        throw ServiceResultException.Create(
                            values[ii].StatusCode,
                            ii,
                            diagnosticInfos,
                            responseHeader.StringTable);
                    }
                }

                attributes[attributeId] = values[ii];
            }

            Node node;
            DataValue value;
            switch ((NodeClass)nodeClass.Value)
            {
                case NodeClass.Object:
                    var objectNode = new ObjectNode();

                    value = attributes[Attributes.EventNotifier];

                    if (value == null)
                    {
                        throw ServiceResultException.Unexpected(
                            "Object does not support the EventNotifier attribute.");
                    }

                    objectNode.EventNotifier = value.GetValueOrDefault<byte>();
                    node = objectNode;
                    break;
                case NodeClass.ObjectType:
                    var objectTypeNode = new ObjectTypeNode();

                    value = attributes[Attributes.IsAbstract];

                    if (value == null)
                    {
                        throw ServiceResultException.Unexpected(
                            "ObjectType does not support the IsAbstract attribute.");
                    }

                    objectTypeNode.IsAbstract = value.GetValueOrDefault<bool>();
                    node = objectTypeNode;
                    break;
                case NodeClass.Variable:
                    var variableNode = new VariableNode();

                    // DataType Attribute
                    value = attributes[Attributes.DataType];

                    if (value == null)
                    {
                        throw ServiceResultException.Unexpected(
                            "Variable does not support the DataType attribute.");
                    }

                    variableNode.DataType = (NodeId)value.GetValue(typeof(NodeId));

                    // ValueRank Attribute
                    value = attributes[Attributes.ValueRank];

                    if (value == null)
                    {
                        throw ServiceResultException.Unexpected(
                            "Variable does not support the ValueRank attribute.");
                    }

                    variableNode.ValueRank = value.GetValueOrDefault<int>();

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
                        throw ServiceResultException.Unexpected(
                            "Variable does not support the AccessLevel attribute.");
                    }

                    variableNode.AccessLevel = value.GetValueOrDefault<byte>();

                    // UserAccessLevel Attribute
                    value = attributes[Attributes.UserAccessLevel];

                    if (value == null)
                    {
                        throw ServiceResultException.Unexpected(
                            "Variable does not support the UserAccessLevel attribute.");
                    }

                    variableNode.UserAccessLevel = value.GetValueOrDefault<byte>();

                    // Historizing Attribute
                    value = attributes[Attributes.Historizing];

                    if (value == null)
                    {
                        throw ServiceResultException.Unexpected(
                            "Variable does not support the Historizing attribute.");
                    }

                    variableNode.Historizing = value.GetValueOrDefault<bool>();

                    // MinimumSamplingInterval Attribute
                    value = attributes[Attributes.MinimumSamplingInterval];

                    if (value != null)
                    {
                        variableNode.MinimumSamplingInterval = Convert.ToDouble(
                            attributes[Attributes.MinimumSamplingInterval].Value,
                            CultureInfo.InvariantCulture);
                    }

                    // AccessLevelEx Attribute
                    value = attributes[Attributes.AccessLevelEx];

                    if (value != null)
                    {
                        variableNode.AccessLevelEx = value.GetValueOrDefault<uint>();
                    }

                    node = variableNode;
                    break;
                case NodeClass.VariableType:
                    var variableTypeNode = new VariableTypeNode();

                    // IsAbstract Attribute
                    value = attributes[Attributes.IsAbstract];

                    if (value == null)
                    {
                        throw ServiceResultException.Unexpected(
                            "VariableType does not support the IsAbstract attribute.");
                    }

                    variableTypeNode.IsAbstract = value.GetValueOrDefault<bool>();

                    // DataType Attribute
                    value = attributes[Attributes.DataType];

                    if (value == null)
                    {
                        throw ServiceResultException.Unexpected(
                            "VariableType does not support the DataType attribute.");
                    }

                    variableTypeNode.DataType = (NodeId)value.GetValue(typeof(NodeId));

                    // ValueRank Attribute
                    value = attributes[Attributes.ValueRank];

                    if (value == null)
                    {
                        throw ServiceResultException.Unexpected(
                            "VariableType does not support the ValueRank attribute.");
                    }

                    variableTypeNode.ValueRank = value.GetValueOrDefault<int>();

                    // ArrayDimensions Attribute
                    value = attributes[Attributes.ArrayDimensions];

                    if (value != null && value.Value != null)
                    {
                        variableTypeNode.ArrayDimensions = (uint[])value.GetValue(typeof(uint[]));
                    }

                    node = variableTypeNode;
                    break;
                case NodeClass.Method:
                    var methodNode = new MethodNode();

                    // Executable Attribute
                    value = attributes[Attributes.Executable];

                    if (value == null)
                    {
                        throw ServiceResultException.Unexpected(
                            "Method does not support the Executable attribute.");
                    }

                    methodNode.Executable = value.GetValueOrDefault<bool>();

                    // UserExecutable Attribute
                    value = attributes[Attributes.UserExecutable];

                    if (value == null)
                    {
                        throw ServiceResultException.Unexpected(
                            "Method does not support the UserExecutable attribute.");
                    }

                    methodNode.UserExecutable = value.GetValueOrDefault<bool>();

                    node = methodNode;
                    break;
                case NodeClass.DataType:
                    var dataTypeNode = new DataTypeNode();

                    // IsAbstract Attribute
                    value = attributes[Attributes.IsAbstract];

                    if (value == null)
                    {
                        throw ServiceResultException.Unexpected(
                            "DataType does not support the IsAbstract attribute.");
                    }

                    dataTypeNode.IsAbstract = value.GetValueOrDefault<bool>();

                    // DataTypeDefinition Attribute
                    value = attributes[Attributes.DataTypeDefinition];

                    if (value != null)
                    {
                        dataTypeNode.DataTypeDefinition = value.Value as ExtensionObject;
                    }

                    node = dataTypeNode;
                    break;
                case NodeClass.ReferenceType:
                    var referenceTypeNode = new ReferenceTypeNode();

                    // IsAbstract Attribute
                    value = attributes[Attributes.IsAbstract];

                    if (value == null)
                    {
                        throw ServiceResultException.Unexpected(
                            "ReferenceType does not support the IsAbstract attribute.");
                    }

                    referenceTypeNode.IsAbstract = value.GetValueOrDefault<bool>();

                    // Symmetric Attribute
                    value = attributes[Attributes.Symmetric];

                    if (value == null)
                    {
                        throw ServiceResultException.Unexpected(
                            "ReferenceType does not support the Symmetric attribute.");
                    }

                    referenceTypeNode.Symmetric = value.GetValueOrDefault<bool>();

                    // InverseName Attribute
                    value = attributes[Attributes.InverseName];

                    if (value != null && value.Value != null)
                    {
                        referenceTypeNode.InverseName = (LocalizedText)value.GetValue(
                            typeof(LocalizedText));
                    }

                    node = referenceTypeNode;
                    break;
                case NodeClass.View:
                    var viewNode = new ViewNode();

                    // EventNotifier Attribute
                    value = attributes[Attributes.EventNotifier];

                    if (value == null)
                    {
                        throw ServiceResultException.Unexpected(
                            "View does not support the EventNotifier attribute.");
                    }

                    viewNode.EventNotifier = value.GetValueOrDefault<byte>();

                    // ContainsNoLoops Attribute
                    value = attributes[Attributes.ContainsNoLoops];

                    if (value == null)
                    {
                        throw ServiceResultException.Unexpected(
                            "View does not support the ContainsNoLoops attribute.");
                    }

                    viewNode.ContainsNoLoops = value.GetValueOrDefault<bool>();

                    node = viewNode;
                    break;
                case NodeClass.Unspecified:
                    throw ServiceResultException.Unexpected(
                        "Node does not have a valid value for NodeClass: {0}.",
                        nodeClass.Value);
                default:
                    throw ServiceResultException.Unexpected(
                        $"Unexpected NodeClass: {nodeClass.Value}.");
            }

            // NodeId Attribute
            value = attributes[Attributes.NodeId];

            if (value == null)
            {
                throw ServiceResultException.Unexpected(
                    "Node does not support the NodeId attribute.");
            }

            node.NodeId = (NodeId)value.GetValue(typeof(NodeId));
            node.NodeClass = (NodeClass)nodeClass.Value;

            // BrowseName Attribute
            value = attributes[Attributes.BrowseName];

            if (value == null)
            {
                throw ServiceResultException.Unexpected(
                    "Node does not support the BrowseName attribute.");
            }

            node.BrowseName = (QualifiedName)value.GetValue(typeof(QualifiedName));

            // DisplayName Attribute
            value = attributes[Attributes.DisplayName];

            if (value == null)
            {
                throw ServiceResultException.Unexpected(
                    "Node does not support the DisplayName attribute.");
            }

            node.DisplayName = (LocalizedText)value.GetValue(typeof(LocalizedText));

            // all optional attributes follow

            // Description Attribute
            if (attributes.TryGetValue(Attributes.Description, out value) &&
                value != null &&
                value.Value != null)
            {
                node.Description = (LocalizedText)value.GetValue(typeof(LocalizedText));
            }

            // WriteMask Attribute
            if (attributes.TryGetValue(Attributes.WriteMask, out value) && value != null)
            {
                node.WriteMask = value.GetValueOrDefault<uint>();
            }

            // UserWriteMask Attribute
            if (attributes.TryGetValue(Attributes.UserWriteMask, out value) && value != null)
            {
                node.UserWriteMask = value.GetValueOrDefault<uint>();
            }

            // RolePermissions Attribute
            if (attributes.TryGetValue(Attributes.RolePermissions, out value) && value != null)
            {
                if (value.Value is ExtensionObject[] rolePermissions)
                {
                    node.RolePermissions = [];

                    foreach (ExtensionObject rolePermission in rolePermissions)
                    {
                        node.RolePermissions.Add(rolePermission.Body as RolePermissionType);
                    }
                }
            }

            // UserRolePermissions Attribute
            if (attributes.TryGetValue(Attributes.UserRolePermissions, out value) && value != null)
            {
                if (value.Value is ExtensionObject[] userRolePermissions)
                {
                    node.UserRolePermissions = [];

                    foreach (ExtensionObject rolePermission in userRolePermissions)
                    {
                        node.UserRolePermissions.Add(rolePermission.Body as RolePermissionType);
                    }
                }
            }

            // AccessRestrictions Attribute
            if (attributes.TryGetValue(Attributes.AccessRestrictions, out value) && value != null)
            {
                node.AccessRestrictions = value.GetValueOrDefault<ushort>();
            }

            return node;
        }

        /// <summary>
        /// Create a dictionary of attributes to read for a nodeclass.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private static Dictionary<uint, DataValue> CreateAttributes(
            NodeClass nodeClass = NodeClass.Unspecified,
            bool optionalAttributes = true)
        {
            // Attributes to read for all types of nodes
            var attributes = new Dictionary<uint, DataValue>(Attributes.MaxAttributes)
            {
                { Attributes.NodeId, null },
                { Attributes.NodeClass, null },
                { Attributes.BrowseName, null },
                { Attributes.DisplayName, null }
            };

            switch (nodeClass)
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
                case NodeClass.Unspecified:
                    // build complete list of attributes.
                    attributes = new Dictionary<uint, DataValue>(Attributes.MaxAttributes)
                    {
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
                default:
                    throw ServiceResultException.Unexpected(
                        $"Unexpected NodeClass: {nodeClass}.");
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

        /// <summary>
        /// Sends an additional publish request.
        /// </summary>
        public object BeginPublish(int timeout)
        {
            // do not publish if reconnecting or the session is in closed state.
            if (!Connected)
            {
                m_logger.LogWarning("Publish skipped due to session not connected");
                return null;
            }

            if (Reconnecting)
            {
                m_logger.LogWarning("Publish skipped due to session reconnect");
                return null;
            }

            // get event handler to modify ack list
            PublishSequenceNumbersToAcknowledgeEventHandler callback
                = m_PublishSequenceNumbersToAcknowledge;

            // collect the current set if acknowledgements.
            SubscriptionAcknowledgementCollection acknowledgementsToSend = null;
            lock (m_acknowledgementsToSendLock)
            {
                if (callback != null)
                {
                    try
                    {
                        var deferredAcknowledgementsToSend
                            = new SubscriptionAcknowledgementCollection();
                        callback(
                            this,
                            new PublishSequenceNumbersToAcknowledgeEventArgs(
                                m_acknowledgementsToSend,
                                deferredAcknowledgementsToSend));
                        acknowledgementsToSend = m_acknowledgementsToSend;
                        m_acknowledgementsToSend = deferredAcknowledgementsToSend;
                    }
                    catch (Exception e2)
                    {
                        m_logger.LogError(
                            e2,
                            "Session: Unexpected error invoking PublishSequenceNumbersToAcknowledgeEventArgs.");
                    }
                }

                if (acknowledgementsToSend == null)
                {
                    // send all ack values, clear list
                    acknowledgementsToSend = m_acknowledgementsToSend;
                    m_acknowledgementsToSend = [];
                }
#if DEBUG_SEQUENTIALPUBLISHING
                foreach (var toSend in acknowledgementsToSend)
                {
                    m_latestAcknowledgementsSent[toSend.SubscriptionId] = toSend.SequenceNumber;
                }
#endif
            }

            uint timeoutHint = timeout > 0 ? (uint)timeout : uint.MaxValue;
            timeoutHint = Math.Min((uint)(OperationTimeout / 2), timeoutHint);

            // send publish request.
            var requestHeader = new RequestHeader
            {
                // ensure the publish request is discarded before the timeout occurs to ensure the channel is dropped.
                TimeoutHint = timeoutHint,
                ReturnDiagnostics = (uint)(int)ReturnDiagnostics,
                RequestHandle = Utils.IncrementIdentifier(ref m_publishCounter)
            };

            var state = new AsyncRequestState
            {
                RequestTypeId = DataTypes.PublishRequest,
                RequestId = requestHeader.RequestHandle,
                TickCount = HiResClock.TickCount
            };

            m_logger.LogTrace("PUBLISH #{RequestHandle} SENT", requestHeader.RequestHandle);
            CoreClientUtils.EventLog.PublishStart((int)requestHeader.RequestHandle);

            try
            {
                Task<PublishResponse> task = PublishAsync(requestHeader, acknowledgementsToSend, default);
                AsyncRequestStarted(task, requestHeader.RequestHandle, DataTypes.PublishRequest);
                task.ConfigureAwait(false)
                    .GetAwaiter()
                    .OnCompleted(() => OnPublishComplete(task, SessionId, acknowledgementsToSend, requestHeader));
                return task;
            }
            catch (Exception e)
            {
                m_logger.LogError(e, "Unexpected error sending publish request.");
                return null;
            }
        }

        /// <summary>
        /// Create the publish requests for the active subscriptions.
        /// </summary>
        public void StartPublishing(int timeout, bool fullQueue)
        {
            int publishCount = GetDesiredPublishRequestCount(true);

            // refill pipeline. Send at least one publish request if subscriptions are active.
            if (publishCount > 0 && BeginPublish(timeout) != null)
            {
                int startCount = fullQueue ? 1 : GoodPublishRequestCount + 1;
                for (int ii = startCount; ii < publishCount; ii++)
                {
                    if (BeginPublish(timeout) == null)
                    {
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Completes an asynchronous publish operation.
        /// </summary>
        private void OnPublishComplete(
            Task<PublishResponse> task,
            NodeId sessionId,
            SubscriptionAcknowledgementCollection acknowledgementsToSend,
            RequestHeader requestHeader)
        {
            // extract state information.
            uint subscriptionId = 0;

            AsyncRequestCompleted(task, requestHeader.RequestHandle, DataTypes.PublishRequest);

            m_logger.LogTrace("PUBLISH #{RequestHandle} RECEIVED", requestHeader.RequestHandle);
            CoreClientUtils.EventLog.PublishStop((int)requestHeader.RequestHandle);

            try
            {
                // gate entry if transfer/reactivate is busy
                m_reconnectLock.Wait();
                bool reconnecting = Reconnecting;
                m_reconnectLock.Release();

                // complete publish.
                PublishResponse response = task.Result;
                ResponseHeader responseHeader = response.ResponseHeader;
                subscriptionId = response.SubscriptionId;
                UInt32Collection availableSequenceNumbers = response.AvailableSequenceNumbers;
                bool moreNotifications = response.MoreNotifications;
                NotificationMessage notificationMessage = response.NotificationMessage;
                StatusCodeCollection acknowledgeResults = response.Results;
                DiagnosticInfoCollection acknowledgeDiagnosticInfos = response.DiagnosticInfos;

                LogLevel logLevel = LogLevel.Warning;
                foreach (StatusCode code in acknowledgeResults)
                {
                    if (StatusCode.IsBad(code) && code != StatusCodes.BadSequenceNumberUnknown)
                    {
                        m_logger.Log(
                            logLevel,
                            "Publish Ack Response. ResultCode={StatusCode}; SubscriptionId={SubscriptionId}",
                            code,
                            subscriptionId);
                        // only show the first error as warning
                        logLevel = LogLevel.Trace;
                    }
                }

                // nothing more to do if we were never connected
                if (NodeId.IsNull(sessionId))
                {
                    return;
                }

                // nothing more to do if session changed.
                if (sessionId != SessionId)
                {
                    m_logger.LogWarning(
                        "Publish response discarded because session id changed: Old {PreviousSessionId} != New {SessionId}",
                        sessionId,
                        SessionId);
                    return;
                }

                m_logger.LogTrace(
                    "NOTIFICATION RECEIVED: SubId={SubscriptionId}, SeqNo={SequenceNumber}",
                    subscriptionId,
                    notificationMessage.SequenceNumber);
                CoreClientUtils.EventLog.NotificationReceived(
                    (int)subscriptionId,
                    (int)notificationMessage.SequenceNumber);

                // process response.
                ProcessPublishResponse(
                    responseHeader,
                    subscriptionId,
                    availableSequenceNumbers,
                    moreNotifications,
                    notificationMessage);

                // nothing more to do if reconnecting.
                if (reconnecting)
                {
                    m_logger.LogWarning("No new publish sent because of reconnect in progress.");
                    return;
                }
            }
            catch (Exception e)
            {
                if (m_subscriptions.Count == 0)
                {
                    // Publish responses with error should occur after deleting the last subscription.
                    m_logger.LogError(
                        "Publish #{RequestHandle}, Subscription count = 0, Error: {Message}",
                        requestHeader.RequestHandle,
                        e.Message);
                }
                else
                {
                    m_logger.LogError(
                        "Publish #{RequestHandle}, Reconnecting={Reconnecting}, Error: {Message}",
                        requestHeader.RequestHandle,
                        Reconnecting,
                        e.Message);
                }

                // raise an error event.
                var error = new ServiceResult(e);

                if (error.Code != StatusCodes.BadNoSubscription)
                {
                    PublishErrorEventHandler callback = m_PublishError;

                    if (callback != null)
                    {
                        try
                        {
                            callback(this, new PublishErrorEventArgs(error, subscriptionId, 0));
                        }
                        catch (Exception e2)
                        {
                            m_logger.LogError(
                                e2,
                                "Session: Unexpected error invoking PublishErrorCallback.");
                        }
                    }
                }

                // ignore errors if reconnecting
                if (Reconnecting)
                {
                    m_logger.LogInformation(
                        "Publish abandoned after error {Message} due to session {SessionId} reconnecting",
                        e.Message,
                        sessionId);
                    return;
                }

                // nothing more to do if session changed.
                if (sessionId != SessionId)
                {
                    if (Connected)
                    {
                        m_logger.LogError(
                            "Publish abandoned after error {Message} because session id changed: Old {PreviousSessionId} != New {SessionId}",
                            e.Message,
                            sessionId,
                            SessionId);
                    }
                    else
                    {
                        m_logger.LogInformation(
                            "Publish abandoned after error {Message} because session {SessionId} was closed.",
                            e.Message,
                            sessionId);
                    }
                    return;
                }

                // try to acknowledge the notifications again in the next publish.
                if (acknowledgementsToSend != null)
                {
                    lock (m_acknowledgementsToSendLock)
                    {
                        m_acknowledgementsToSend.AddRange(acknowledgementsToSend);
                    }
                }

                // don't send another publish for these errors,
                // or throttle to avoid server overload.
                switch (error.Code)
                {
                    case StatusCodes.BadTooManyPublishRequests:
                        int tooManyPublishRequests = GoodPublishRequestCount;
                        if (BelowPublishRequestLimit(tooManyPublishRequests))
                        {
                            m_tooManyPublishRequests = tooManyPublishRequests;
                            m_logger.LogInformation(
                                "PUBLISH - Too many requests, set limit to GoodPublishRequestCount={GoodRequestCount}.",
                                m_tooManyPublishRequests);
                        }
                        return;
                    case StatusCodes.BadNoSubscription:
                    case StatusCodes.BadSessionClosed:
                    case StatusCodes.BadSecurityChecksFailed:
                    case StatusCodes.BadCertificateInvalid:
                    case StatusCodes.BadServerHalted:
                        return;
                    // may require a reconnect or activate to recover
                    case StatusCodes.BadSessionIdInvalid:
                    case StatusCodes.BadSecureChannelIdInvalid:
                    case StatusCodes.BadSecureChannelClosed:
                        OnKeepAliveError(error);
                        return;
                    // Servers may return this error when overloaded
                    case StatusCodes.BadTooManyOperations:
                    case StatusCodes.BadTcpServerTooBusy:
                    case StatusCodes.BadServerTooBusy:
                        // throttle the next publish to reduce server load
                        _ = Task.Run(async () =>
                        {
                            await Task.Delay(100).ConfigureAwait(false);
                            QueueBeginPublish();
                        });
                        return;
                    case StatusCodes.BadTimeout:
                        break;
                    default:
                        m_logger.LogError(
                            e,
                            "PUBLISH #{RequestHandle} - Unhandled error {StatusCode} during Publish.",
                            requestHeader.RequestHandle,
                            error.StatusCode);
                        goto case StatusCodes.BadServerTooBusy;
                }
            }

            QueueBeginPublish();
        }

        /// <summary>
        /// Helper to refresh the identity (reprompt for password, refresh token) in case of a Recreate of the Session.
        /// </summary>
        public virtual void RecreateRenewUserIdentity()
        {
            if (m_RenewUserIdentity != null)
            {
                m_identity = m_RenewUserIdentity(this, m_identity);
            }
        }

        /// <summary>
        /// Helper to throw a recreate session exception.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private static void ThrowCouldNotRecreateSessionException(Exception e, string sessionName)
        {
            throw ServiceResultException.Create(
                StatusCodes.BadCommunicationError,
                e,
                "Could not recreate session {0}:{1}",
                sessionName,
                e.Message);
        }

        /// <summary>
        /// Queues a publish request if there are not enough outstanding requests.
        /// </summary>
        private void QueueBeginPublish()
        {
            int requestCount = GoodPublishRequestCount;

            int minPublishRequestCount = GetDesiredPublishRequestCount(false);

            if (requestCount < minPublishRequestCount)
            {
                BeginPublish(OperationTimeout);
            }
            else
            {
                m_logger.LogDebug(
                    "PUBLISH - Did not send another publish request. " +
                    "GoodPublishRequestCount={GoodRequestCount}, MinPublishRequestCount={MinRequestCount}",
                    requestCount,
                    minPublishRequestCount);
            }
        }

        /// <summary>
        /// Validates  the identity for an open call.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private void OpenValidateIdentity(
            ref IUserIdentity identity,
            out UserIdentityToken identityToken,
            out UserTokenPolicy identityPolicy,
            out string securityPolicyUri,
            out bool requireEncryption)
        {
            // check connection state.
            lock (SyncRoot)
            {
                if (Connected)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidState,
                        "Already connected to server.");
                }
            }

            securityPolicyUri = m_endpoint.Description.SecurityPolicyUri;

            // catch security policies which are not supported by core
            if (SecurityPolicies.GetDisplayName(securityPolicyUri) == null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadSecurityChecksFailed,
                    "The chosen security policy is not supported by the client to connect to the server.");
            }

            // get the identity token.
            identity ??= new UserIdentity();

            // get identity token.
            identityToken = identity.GetIdentityToken();

            // check that the user identity is supported by the endpoint.
            identityPolicy = m_endpoint.Description
                .FindUserTokenPolicy(identityToken.PolicyId, securityPolicyUri);

            if (identityPolicy == null)
            {
                // try looking up by TokenType if the policy id was not found.
                identityPolicy = m_endpoint.Description.FindUserTokenPolicy(
                    identity.TokenType,
                    identity.IssuedTokenType,
                    securityPolicyUri);

                if (identityPolicy == null)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadIdentityTokenRejected,
                        "Endpoint does not support the user identity type provided.");
                }

                identityToken.PolicyId = identityPolicy.PolicyId;
            }

            requireEncryption = securityPolicyUri != SecurityPolicies.None;

            if (!requireEncryption)
            {
                requireEncryption =
                    identityPolicy.SecurityPolicyUri != SecurityPolicies.None &&
                    !string.IsNullOrEmpty(identityPolicy.SecurityPolicyUri);
            }
        }

#if UNUSED
        /// <summary>
        /// Validates the ServerCertificate ApplicationUri to match the ApplicationUri of the Endpoint
        /// for an open call (Spec Part 4 5.4.1)
        /// </summary>
        private void ValidateServerCertificateApplicationUri(X509Certificate2 serverCertificate)
        {
            string applicationUri = m_endpoint?.Description?.Server?.ApplicationUri;
            //check is only neccessary if the ApplicatioUri is specified for the Endpoint
            if (string.IsNullOrEmpty(applicationUri))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadSecurityChecksFailed,
                    "No ApplicationUri is specified for the server in the EndpointDescription.");
            }
            string certificateApplicationUri = X509Utils.GetApplicationUriFromCertificate(serverCertificate);
            if (!string.Equals(certificateApplicationUri, applicationUri, StringComparison.Ordinal))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadSecurityChecksFailed,
                    "Server did not return a Certificate matching the ApplicationUri specified in the EndpointDescription.");
            }
        }
#endif

        private void BuildCertificateData(
            out byte[] clientCertificateData,
            out byte[] clientCertificateChainData)
        {
            // send the application instance certificate for the client.
            clientCertificateData = (m_instanceCertificate?.RawData);
            clientCertificateChainData = null;

            if (m_instanceCertificateChain != null &&
                m_instanceCertificateChain.Count > 0 &&
                m_configuration.SecurityConfiguration.SendCertificateChain)
            {
                var clientCertificateChain = new List<byte>();

                for (int i = 0; i < m_instanceCertificateChain.Count; i++)
                {
                    clientCertificateChain.AddRange(m_instanceCertificateChain[i].RawData);
                }

                clientCertificateChainData = [.. clientCertificateChain];
            }
        }

        /// <summary>
        /// Validates the server certificate returned.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private void ValidateServerCertificateData(byte[] serverCertificateData)
        {
            if (serverCertificateData != null &&
                m_endpoint.Description.ServerCertificate != null &&
                !Utils.IsEqual(serverCertificateData, m_endpoint.Description.ServerCertificate))
            {
                try
                {
                    // verify for certificate chain in endpoint.
                    X509Certificate2Collection serverCertificateChain =
                        Utils.ParseCertificateChainBlob(
                            m_endpoint.Description.ServerCertificate,
                            m_telemetry);

                    if (serverCertificateChain.Count > 0 &&
                        !Utils.IsEqual(serverCertificateData, serverCertificateChain[0].RawData))
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
        }

        /// <summary>
        /// Validates the server signature created with the client nonce.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private void ValidateServerSignature(
            X509Certificate2 serverCertificate,
            SignatureData serverSignature,
            byte[] clientCertificateData,
            byte[] clientCertificateChainData,
            byte[] clientNonce)
        {
            if (serverSignature == null || serverSignature.Signature == null)
            {
                m_logger.LogInformation("Server signature is null or empty.");

                //throw ServiceResultException.Create(
                //    StatusCodes.BadSecurityChecksFailed,
                //    "Server signature is null or empty.");
            }

            // validate the server's signature.
            byte[] dataToSign = Utils.Append(clientCertificateData, clientNonce);

            if (!SecurityPolicies.Verify(
                    serverCertificate,
                    m_endpoint.Description.SecurityPolicyUri,
                    dataToSign,
                    serverSignature))
            {
                // validate the signature with complete chain if the check with leaf certificate failed.
                if (clientCertificateChainData != null)
                {
                    dataToSign = Utils.Append(clientCertificateChainData, clientNonce);

                    if (!SecurityPolicies.Verify(
                        serverCertificate,
                        m_endpoint.Description.SecurityPolicyUri,
                        dataToSign,
                        serverSignature))
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
        }

        /// <summary>
        /// Validates the server endpoints returned.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private void ValidateServerEndpoints(EndpointDescriptionCollection serverEndpoints)
        {
            if (m_discoveryServerEndpoints != null && m_discoveryServerEndpoints.Count > 0)
            {
                // Compare EndpointDescriptions returned at GetEndpoints with values returned at CreateSession
                EndpointDescriptionCollection expectedServerEndpoints;
                if (serverEndpoints != null &&
                    m_discoveryProfileUris != null &&
                    m_discoveryProfileUris.Count > 0)
                {
                    // Select EndpointDescriptions with a transportProfileUri that matches the
                    // profileUris specified in the original GetEndpoints() request.
                    expectedServerEndpoints = [];

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
                        serverEndpoint.SecurityPolicyUri != expectedServerEndpoint
                            .SecurityPolicyUri ||
                        serverEndpoint.TransportProfileUri != expectedServerEndpoint
                            .TransportProfileUri ||
                        serverEndpoint.SecurityLevel != expectedServerEndpoint.SecurityLevel)
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadSecurityChecksFailed,
                            "The list of ServerEndpoints returned at CreateSession does not match the list from GetEndpoints.");
                    }

                    if (serverEndpoint.UserIdentityTokens.Count != expectedServerEndpoint
                        .UserIdentityTokens
                        .Count)
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadSecurityChecksFailed,
                            "The list of ServerEndpoints returned at CreateSession does not match the one from GetEndpoints.");
                    }

                    for (int jj = 0; jj < serverEndpoint.UserIdentityTokens.Count; jj++)
                    {
                        if (!serverEndpoint
                                .UserIdentityTokens[jj]
                                .IsEqual(expectedServerEndpoint.UserIdentityTokens[jj]))
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

            EndpointDescription foundDescription = FindMatchingDescription(
                serverEndpoints,
                m_endpoint.Description,
                true);
            if (foundDescription != null)
            {
                found = true;
                // ensure endpoint has up to date information.
                UpdateDescription(m_endpoint.Description, foundDescription);
            }
            else
            {
                foundDescription = FindMatchingDescription(
                    serverEndpoints,
                    m_endpoint.Description,
                    false);
                if (foundDescription != null)
                {
                    found = true;
                    // ensure endpoint has up to date information.
                    UpdateDescription(m_endpoint.Description, foundDescription);
                }
            }

            // could be a security risk.
            if (!found)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadSecurityChecksFailed,
                    "Server did not return an EndpointDescription that matched the one used to create the secure channel.");
            }
        }

        /// <summary>
        /// Find and return matching application description
        /// </summary>
        /// <param name="endpointDescriptions">The descriptions to search through</param>
        /// <param name="match">The description to match</param>
        /// <param name="matchPort">Match criteria includes port</param>
        /// <returns>Matching description or null if no description is matching</returns>
        private EndpointDescription FindMatchingDescription(
            EndpointDescriptionCollection endpointDescriptions,
            EndpointDescription match,
            bool matchPort)
        {
            Uri expectedUrl = Utils.ParseUri(match.EndpointUrl);
            for (int ii = 0; ii < endpointDescriptions.Count; ii++)
            {
                EndpointDescription serverEndpoint = endpointDescriptions[ii];
                Uri actualUrl = Utils.ParseUri(serverEndpoint.EndpointUrl);

                if (actualUrl != null &&
                    actualUrl.Scheme == expectedUrl.Scheme &&
                    (!matchPort || actualUrl.Port == expectedUrl.Port) &&
                    serverEndpoint.SecurityPolicyUri == m_endpoint.Description.SecurityPolicyUri &&
                    serverEndpoint.SecurityMode == m_endpoint.Description.SecurityMode)
                {
                    return serverEndpoint;
                }
            }

            return null;
        }

        /// <summary>
        /// Update the target description from the source description
        /// </summary>
        private static void UpdateDescription(
            EndpointDescription target,
            EndpointDescription source)
        {
            target.Server.ApplicationName = source.Server.ApplicationName;
            target.Server.ApplicationUri = source.Server.ApplicationUri;
            target.Server.ApplicationType = source.Server.ApplicationType;
            target.Server.ProductUri = source.Server.ProductUri;
            target.TransportProfileUri = source.TransportProfileUri;
            target.UserIdentityTokens = source.UserIdentityTokens;
        }

        /// <summary>
        /// Process Republish error response.
        /// </summary>
        /// <param name="e">The exception that occurred during the republish operation.</param>
        /// <param name="subscriptionId">The subscription Id for which the republish was requested. </param>
        /// <param name="sequenceNumber">The sequencenumber for which the republish was requested.</param>
        private (bool, ServiceResult) ProcessRepublishResponseError(
            Exception e,
            uint subscriptionId,
            uint sequenceNumber)
        {
            var error = new ServiceResult(e);

            bool result = true;
            switch (error.StatusCode.Code)
            {
                case StatusCodes.BadSubscriptionIdInvalid:
                case StatusCodes.BadMessageNotAvailable:
                    m_logger.LogWarning(
                        "Message {SubscriptionId}-{SequenceNumber} no longer available.",
                        subscriptionId,
                        sequenceNumber);
                    break;
                // if encoding limits are exceeded, the issue is logged and
                // the published data is acknowledged to prevent the endless republish loop.
                case StatusCodes.BadEncodingLimitsExceeded:
                    m_logger.LogError(
                        e,
                        "Message {SubscriptionId}-{SequenceNumber} exceeded size limits, ignored.",
                        subscriptionId,
                        sequenceNumber);
                    lock (m_acknowledgementsToSendLock)
                    {
                        AddAcknowledgementToSend(
                            m_acknowledgementsToSend,
                            subscriptionId,
                            sequenceNumber);
                    }
                    break;
                default:
                    result = false;
                    m_logger.LogError(e, "Unexpected error sending republish request.");
                    break;
            }

            PublishErrorEventHandler callback = m_PublishError;

            // raise an error event.
            if (callback != null)
            {
                try
                {
                    var args = new PublishErrorEventArgs(error, subscriptionId, sequenceNumber);

                    callback(this, args);
                }
                catch (Exception e2)
                {
                    m_logger.LogError(e2, "Session: Unexpected error invoking PublishErrorCallback.");
                }
            }

            return (result, error);
        }

        /// <summary>
        /// If available, returns the current nonce or null.
        /// </summary>
        private byte[] GetCurrentTokenServerNonce()
        {
            ChannelToken currentToken = NullableTransportChannel?.CurrentToken;
            return currentToken?.ServerNonce;
        }

        /// <summary>
        /// Handles the validation of server software certificates and application callback.
        /// </summary>
        private void HandleSignedSoftwareCertificates(
            SignedSoftwareCertificateCollection serverSoftwareCertificates)
        {
            // get a validator to check certificates provided by server.
            CertificateValidator validator = m_configuration.CertificateValidator;

            // validate software certificates.
            var softwareCertificates = new List<SoftwareCertificate>();

            foreach (SignedSoftwareCertificate signedCertificate in serverSoftwareCertificates)
            {
                ServiceResult result = SoftwareCertificate.Validate(
                    validator,
                    signedCertificate.CertificateData,
                    m_telemetry,
                    out SoftwareCertificate softwareCertificate);

                if (ServiceResult.IsBad(result))
                {
                    OnSoftwareCertificateError(signedCertificate, result);
                }

                softwareCertificates.Add(softwareCertificate);
            }

            // check if software certificates meet application requirements.
            ValidateSoftwareCertificates(softwareCertificates);
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

            // collect the current set of acknowledgements.
            lock (m_acknowledgementsToSendLock)
            {
                // clear out acknowledgements for messages that the server does not have any more.
                var acknowledgementsToSend = new SubscriptionAcknowledgementCollection();

                uint latestSequenceNumberToSend = 0;

                // create an acknowledgement to be sent back to the server.
                if (notificationMessage.NotificationData.Count > 0)
                {
                    AddAcknowledgementToSend(
                        acknowledgementsToSend,
                        subscriptionId,
                        notificationMessage.SequenceNumber);
                    UpdateLatestSequenceNumberToSend(
                        ref latestSequenceNumberToSend,
                        notificationMessage.SequenceNumber);
                    _ = availableSequenceNumbers?.Remove(notificationMessage.SequenceNumber);
                }

                // match an acknowledgement to be sent back to the server.
                for (int ii = 0; ii < m_acknowledgementsToSend.Count; ii++)
                {
                    SubscriptionAcknowledgement acknowledgement = m_acknowledgementsToSend[ii];

                    if (acknowledgement.SubscriptionId != subscriptionId)
                    {
                        acknowledgementsToSend.Add(acknowledgement);
                    }
                    else if (availableSequenceNumbers == null ||
                        availableSequenceNumbers.Remove(acknowledgement.SequenceNumber))
                    {
                        acknowledgementsToSend.Add(acknowledgement);
                        UpdateLatestSequenceNumberToSend(
                            ref latestSequenceNumberToSend,
                            acknowledgement.SequenceNumber);
                    }
                    // a publish response may by processed out of order,
                    // allow for a tolerance until the sequence number is removed.
                    else if (Math.Abs(
                            (int)(acknowledgement.SequenceNumber - latestSequenceNumberToSend)) <
                        kPublishRequestSequenceNumberOutOfOrderThreshold)
                    {
                        acknowledgementsToSend.Add(acknowledgement);
                    }
                    else
                    {
                        m_logger.LogWarning(
                            "SessionId {SessionId}, SubscriptionId {SubscriptionId}, Sequence number={SequenceNumber} was not received in the available sequence numbers.",
                            SessionId,
                            subscriptionId,
                            acknowledgement.SequenceNumber);
                    }
                }

                // Check for outdated sequence numbers. May have been not acked due to a network glitch.
                if (latestSequenceNumberToSend != 0 && availableSequenceNumbers?.Count > 0)
                {
                    foreach (uint sequenceNumber in availableSequenceNumbers)
                    {
                        if ((int)(latestSequenceNumberToSend - sequenceNumber) >
                            kPublishRequestSequenceNumberOutdatedThreshold)
                        {
                            AddAcknowledgementToSend(
                                acknowledgementsToSend,
                                subscriptionId,
                                sequenceNumber);
                            m_logger.LogWarning(
                                "SessionId {SessionId}, SubscriptionId {SubscriptionId}, Sequence number={SequenceNumber} was outdated, acknowledged.",
                                SessionId,
                                subscriptionId,
                                sequenceNumber);
                        }
                    }
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
                            // If the last sent sequence number is uint.Max do not display the warning;
                            // the counter rolled over
                            // If the last sent sequence number is greater or equal to the available
                            // sequence number (returned by the publish), a warning must be logged.
                            if ((
                                    (lastSentSequenceNumber >= availableSequenceNumber)
                                    && (lastSentSequenceNumber != uint.MaxValue))
                                || (lastSentSequenceNumber == availableSequenceNumber)
                                    && (lastSentSequenceNumber == uint.MaxValue))
                            {
                                m_logger.LogWarning(
                                    "Received sequence number which was already acknowledged={0}",
                                    availableSequenceNumber);
                            }
                        }
                    }
                }

                if (m_latestAcknowledgementsSent.ContainsKey(subscriptionId))
                {
                    lastSentSequenceNumber = m_latestAcknowledgementsSent[subscriptionId];

                    // If the last sent sequence number is uint.Max do not display the warning;
                    // the counter rolled over
                    // If the last sent sequence number is greater or equal to the notificationMessage's
                    // sequence number (returned by the publish) a warning must be logged.
                    if ((
                            (lastSentSequenceNumber >= notificationMessage.SequenceNumber)
                            && (lastSentSequenceNumber != uint.MaxValue))
                        || (lastSentSequenceNumber == notificationMessage.SequenceNumber)
                            && (lastSentSequenceNumber == uint.MaxValue))
                    {
                        m_logger.LogWarning(
                            "Received sequence number which was already acknowledged={0}",
                            notificationMessage.SequenceNumber);
                    }
                }
#endif

                m_acknowledgementsToSend = acknowledgementsToSend;

                if (notificationMessage.IsEmpty)
                {
                    m_logger.LogTrace(
                        "Empty notification message received for SessionId {SessionId} with PublishTime {PublishTime}",
                        SessionId,
                        notificationMessage.PublishTime.ToLocalTime());
                }
            }

            bool subscriptionCreationInProgress = false;

            lock (SyncRoot)
            {
                // find the subscription.
                foreach (Subscription current in m_subscriptions)
                {
                    if (current.Id == subscriptionId)
                    {
                        subscription = current;
                        break;
                    }
                    if (current.Id == default)
                    {
                        // Subscription is being created, disable cleanup mechanism
                        subscriptionCreationInProgress = true;
                    }
                }
            }

            // ignore messages with a subscription that has been deleted.
            if (subscription != null)
            {
#if DEBUG
                // Validate publish time and reject old values.
                if (notificationMessage.PublishTime.AddMilliseconds(
                        subscription.CurrentPublishingInterval * subscription.CurrentLifetimeCount
                    ) < DateTime.UtcNow)
                {
                    m_logger.LogTrace(
                        "PublishTime {PublishTime} in publish response is too old for SubscriptionId {SubscriptionId}.",
                        notificationMessage.PublishTime.ToLocalTime(),
                        subscription.Id);
                }

                // Validate publish time and reject old values.
                if (notificationMessage.PublishTime >
                    DateTime.UtcNow.AddMilliseconds(
                        subscription.CurrentPublishingInterval * subscription.CurrentLifetimeCount))
                {
                    m_logger.LogTrace(
                        "PublishTime {PublishTime} in publish response is newer than actual time for SubscriptionId {SubscriptionId}.",
                        notificationMessage.PublishTime.ToLocalTime(),
                        subscription.Id);
                }
#endif
                // save the information that more notifications are expected
                notificationMessage.MoreNotifications = moreNotifications;

                // save the string table that came with the notification.
                notificationMessage.StringTable = responseHeader.StringTable;

                // update subscription cache.
                subscription.SaveMessageInCache(availableSequenceNumbers, notificationMessage);

                // raise the notification.
                NotificationEventHandler publishEventHandler = m_Publish;
                if (publishEventHandler != null)
                {
                    var args = new NotificationEventArgs(
                        subscription,
                        notificationMessage,
                        responseHeader.StringTable);

                    Task.Run(() => OnRaisePublishNotification(publishEventHandler, args));
                }
            }
            else if (DeleteSubscriptionsOnClose && !Reconnecting && !subscriptionCreationInProgress)
            {
                // Delete abandoned subscription from server.
                m_logger.LogWarning(
                    "Received Publish Response for Unknown SubscriptionId={SubscriptionId}. Deleting abandoned subscription from server.",
                    subscriptionId);

                Task.Run(() => DeleteSubscriptionAsync(subscriptionId));
            }
            else
            {
                // Do not delete publish requests of stale subscriptions
                m_logger.LogWarning(
                    "Received Publish Response for Unknown SubscriptionId={SubscriptionId}. Ignored.",
                    subscriptionId);
            }
        }

        /// <summary>
        /// Raises an event indicating that publish has returned a notification.
        /// </summary>
        private void OnRaisePublishNotification(
            NotificationEventHandler callback,
            NotificationEventArgs args)
        {
            try
            {
                if (callback != null && args.Subscription.Id != 0)
                {
                    callback(this, args);
                }
            }
            catch (Exception e)
            {
                m_logger.LogError(e, "Session: Unexpected error while raising Notification event.");
            }
        }

        /// <summary>
        /// Invokes a DeleteSubscriptions call for the specified subscriptionId.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private async ValueTask DeleteSubscriptionAsync(
            uint subscriptionId,
            CancellationToken ct = default)
        {
            try
            {
                m_logger.LogInformation(
                    "Deleting server subscription for SubscriptionId={SubscriptionId}",
                    subscriptionId);

                // delete the subscription.
                UInt32Collection subscriptionIds = new uint[] { subscriptionId };

                DeleteSubscriptionsResponse response = await DeleteSubscriptionsAsync(
                    null,
                    subscriptionIds,
                    ct).ConfigureAwait(false);

                ResponseHeader responseHeader = response.ResponseHeader;
                StatusCodeCollection results = response.Results;
                DiagnosticInfoCollection diagnosticInfos = response.DiagnosticInfos;

                // validate response.
                ValidateResponse(results, subscriptionIds);
                ValidateDiagnosticInfos(diagnosticInfos, subscriptionIds);

                if (StatusCode.IsBad(results[0]))
                {
                    throw new ServiceResultException(
                        GetResult(results[0], 0, diagnosticInfos, responseHeader));
                }
            }
            catch (Exception e)
            {
                m_logger.LogError(
                    e,
                    "Session: Unexpected error while deleting subscription for SubscriptionId={SubscriptionId}.",
                    subscriptionId);
            }
        }

        /// <summary>
        /// Asynchronously load instance certificate
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private async Task LoadInstanceCertificateAsync(
            X509Certificate2 clientCertificate,
            CancellationToken ct = default)
        {
            if (m_endpoint.Description.SecurityPolicyUri != SecurityPolicies.None)
            {
                if (clientCertificate == null)
                {
                    m_instanceCertificate = await LoadCertificateAsync(
                            m_configuration,
                            m_endpoint.Description.SecurityPolicyUri,
                            m_telemetry,
                            ct)
                        .ConfigureAwait(false);
                    if (m_instanceCertificate == null)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadConfigurationError,
                            "The client configuration does not specify an application instance certificate.");
                    }
                }
                else
                {
                    // update client certificate.
                    m_instanceCertificate = clientCertificate;
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
                m_instanceCertificateChain = await LoadCertificateChainAsync(
                    m_configuration,
                    m_instanceCertificate,
                    ct)
                    .ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Load certificate for connection.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private static async Task<X509Certificate2> LoadCertificateAsync(
            ApplicationConfiguration configuration,
            string securityProfile,
            ITelemetryContext telemetry,
            CancellationToken ct = default)
        {
            return await configuration.SecurityConfiguration.FindApplicationCertificateAsync(
                securityProfile,
                privateKey: true,
                telemetry,
                ct).ConfigureAwait(false)
                ?? throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "ApplicationCertificate for the security profile {0} cannot be found.",
                    securityProfile);
        }

        /// <summary>
        /// Load certificate chain for connection.
        /// </summary>
        private static async Task<X509Certificate2Collection> LoadCertificateChainAsync(
            ApplicationConfiguration configuration,
            X509Certificate2 clientCertificate,
            CancellationToken ct = default)
        {
            X509Certificate2Collection clientCertificateChain = null;
            // load certificate chain.
            if (configuration.SecurityConfiguration.SendCertificateChain)
            {
                clientCertificateChain = new X509Certificate2Collection(clientCertificate);
                List<CertificateIdentifier> issuers = [];
                await configuration
                    .CertificateValidator.GetIssuersAsync(clientCertificate, issuers, ct)
                    .ConfigureAwait(false);

                for (int i = 0; i < issuers.Count; i++)
                {
                    clientCertificateChain.Add(issuers[i].Certificate);
                }
            }
            return clientCertificateChain;
        }

        private void AddAcknowledgementToSend(
            SubscriptionAcknowledgementCollection acknowledgementsToSend,
            uint subscriptionId,
            uint sequenceNumber)
        {
            if (acknowledgementsToSend == null)
            {
                throw new ArgumentNullException(nameof(acknowledgementsToSend));
            }

            Debug.Assert(Monitor.IsEntered(m_acknowledgementsToSendLock));

            var acknowledgement = new SubscriptionAcknowledgement
            {
                SubscriptionId = subscriptionId,
                SequenceNumber = sequenceNumber
            };

            acknowledgementsToSend.Add(acknowledgement);
        }

        /// <summary>
        /// Returns true if the Bad_TooManyPublishRequests limit
        /// has not been reached.
        /// </summary>
        /// <param name="requestCount">The actual number of publish requests.</param>
        /// <returns>If the publish request limit was reached.</returns>
        private bool BelowPublishRequestLimit(int requestCount)
        {
            return (m_tooManyPublishRequests == 0) || (requestCount < m_tooManyPublishRequests);
        }

        /// <summary>
        /// Returns the desired number of active publish request that should be used.
        /// </summary>
        /// <remarks>
        /// Returns 0 if there are no subscriptions.
        /// </remarks>
        /// <param name="createdOnly">False if call when re-queuing.</param>
        /// <returns>The number of desired publish requests for the session.</returns>
        protected virtual int GetDesiredPublishRequestCount(bool createdOnly)
        {
            lock (SyncRoot)
            {
                if (m_subscriptions.Count == 0)
                {
                    return 0;
                }

                int publishCount;

                if (createdOnly)
                {
                    int count = 0;
                    foreach (Subscription subscription in m_subscriptions)
                    {
                        if (subscription.Created)
                        {
                            count++;
                        }
                    }

                    if (count == 0)
                    {
                        return 0;
                    }
                    publishCount = count;
                }
                else
                {
                    publishCount = m_subscriptions.Count;
                }

                //
                // If a dynamic limit was set because of badTooManyPublishRequest error.
                // limit the number of publish requests to this value.
                //
                if (m_tooManyPublishRequests > 0 && publishCount > m_tooManyPublishRequests)
                {
                    publishCount = m_tooManyPublishRequests;
                }

                //
                // Limit resulting to a number between min and max request count.
                // If max is below min, we honor the min publish request count.
                // See return from MinPublishRequestCount property which the max of both.
                //
                if (publishCount > m_maxPublishRequestCount)
                {
                    publishCount = m_maxPublishRequestCount;
                }
                if (publishCount < m_minPublishRequestCount)
                {
                    publishCount = m_minPublishRequestCount;
                }
                return publishCount;
            }
        }

        /// <summary>
        /// Creates resend data call requests for the subscriptions.
        /// </summary>
        /// <param name="subscriptions">The subscriptions to call resend data.</param>
        private static CallMethodRequestCollection CreateCallRequestsForResendData(
            IEnumerable<Subscription> subscriptions)
        {
            var requests = new CallMethodRequestCollection();

            foreach (Subscription subscription in subscriptions)
            {
                var inputArguments = new VariantCollection { new Variant(subscription.Id) };

                var request = new CallMethodRequest
                {
                    ObjectId = ObjectIds.Server,
                    MethodId = MethodIds.Server_ResendData,
                    InputArguments = inputArguments
                };

                requests.Add(request);
            }
            return requests;
        }

        /// <summary>
        /// Creates and validates the subscription ids for a transfer.
        /// </summary>
        /// <param name="subscriptions">The subscriptions to transfer.</param>
        /// <returns>The subscription ids for the transfer.</returns>
        /// <exception cref="ServiceResultException">Thrown if a subscription is in invalid state.</exception>
        private UInt32Collection CreateSubscriptionIdsForTransfer(
            SubscriptionCollection subscriptions)
        {
            var subscriptionIds = new UInt32Collection();
            lock (SyncRoot)
            {
                foreach (Subscription subscription in subscriptions)
                {
                    if (subscription.Created && SessionId.Equals(subscription.Session.SessionId))
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadInvalidState,
                            Utils.Format(
                                "The subscriptionId {0} is already created.",
                                subscription.Id));
                    }
                    if (subscription.TransferId == 0)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadInvalidState,
                            Utils.Format(
                                "A subscription can not be transferred due to missing transfer Id."));
                    }
                    subscriptionIds.Add(subscription.TransferId);
                }
            }
            return subscriptionIds;
        }

        /// <summary>
        /// Indicates that the session configuration has changed.
        /// </summary>
        private void IndicateSessionConfigurationChanged()
        {
            try
            {
                m_SessionConfigurationChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception e)
            {
                m_logger.LogError(
                    e,
                    "Unexpected error calling SessionConfigurationChanged event handler.");
            }
        }

        /// <summary>
        /// Helper to update the latest sequence number to send.
        /// Handles wrap around of sequence numbers.
        /// </summary>
        private static void UpdateLatestSequenceNumberToSend(
            ref uint latestSequenceNumberToSend,
            uint sequenceNumber)
        {
            // Handle wrap around with subtraction and test result is int.
            // Assume sequence numbers to ack do not differ by more than uint.Max / 2
            if (latestSequenceNumberToSend == 0 ||
                ((int)(sequenceNumber - latestSequenceNumberToSend)) > 0)
            {
                latestSequenceNumberToSend = sequenceNumber;
            }
        }

        /// <summary>
        /// Creates a request header with additional parameters
        /// for the ecc user token security policy, if needed.
        /// </summary>
        private RequestHeader CreateRequestHeaderPerUserTokenPolicy(
            string identityTokenSecurityPolicyUri,
            string endpointSecurityPolicyUri)
        {
            var requestHeader = new RequestHeader();
            string userTokenSecurityPolicyUri = identityTokenSecurityPolicyUri;
            if (string.IsNullOrEmpty(userTokenSecurityPolicyUri))
            {
                userTokenSecurityPolicyUri = m_endpoint.Description.SecurityPolicyUri;
            }
            m_userTokenSecurityPolicyUri = userTokenSecurityPolicyUri;

            if (EccUtils.IsEccPolicy(userTokenSecurityPolicyUri))
            {
                var parameters = new AdditionalParametersType();
                parameters.Parameters.Add(
                    new KeyValuePair { Key = "ECDHPolicyUri", Value = userTokenSecurityPolicyUri });
                requestHeader.AdditionalHeader = new ExtensionObject(parameters);
            }

            return requestHeader;
        }

        /// <summary>
        /// Process the AdditionalHeader field of a ResponseHeader
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        protected virtual void ProcessResponseAdditionalHeader(
            ResponseHeader responseHeader,
            X509Certificate2 serverCertificate)
        {
            if (ExtensionObject.ToEncodeable(
                responseHeader?.AdditionalHeader) is AdditionalParametersType parameters)
            {
                foreach (KeyValuePair ii in parameters.Parameters)
                {
#if ECC_SUPPORT
                    if (ii.Key == "ECDHKey")
                    {
                        if (ii.Value.TypeInfo == TypeInfo.Scalars.StatusCode)
                        {
                            throw new ServiceResultException(
                                (uint)(StatusCode)ii.Value.Value,
                                "Server could not provide an ECDHKey. User authentication not possible.");
                        }

                        if (ExtensionObject.ToEncodeable(
                            ii.Value.Value as ExtensionObject) is not EphemeralKeyType key)
                        {
                            throw new ServiceResultException(
                                StatusCodes.BadDecodingError,
                                "Server did not provide a valid ECDHKey. User authentication not possible.");
                        }

                        if (!EccUtils.Verify(
                                new ArraySegment<byte>(key.PublicKey),
                                key.Signature,
                                serverCertificate,
                                m_userTokenSecurityPolicyUri))
                        {
                            throw new ServiceResultException(
                                StatusCodes.BadDecodingError,
                                "Could not verify signature on ECDHKey. User authentication not possible.");
                        }

                        m_eccServerEphemeralKey = Nonce.CreateNonce(
                            m_userTokenSecurityPolicyUri,
                            key.PublicKey);
                    }
#endif
                }
            }
        }

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
        /// The session telemetry context
        /// </summary>
        protected ITelemetryContext m_telemetry;

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

        /// <summary>
        /// Factor applied to the <see cref="m_keepAliveInterval"/> before <see cref="KeepAliveStopped"/> is set to true
        /// </summary>
        protected int m_keepAliveIntervalFactor = 1;

        /// <summary>
        /// Time in milliseconds added to <see cref="m_keepAliveInterval"/> before <see cref="KeepAliveStopped"/> is set to true
        /// </summary>
        protected int m_keepAliveGuardBand = 1000;
        private SubscriptionAcknowledgementCollection m_acknowledgementsToSend;
        private object m_acknowledgementsToSendLock;
#if DEBUG_SEQUENTIALPUBLISHING
        private Dictionary<uint, uint> m_latestAcknowledgementsSent;
#endif
        private List<Subscription> m_subscriptions;
        private uint m_maxRequestMessageSize;
        private readonly SystemContext m_systemContext;
        private NodeCache m_nodeCache;
        private List<IUserIdentity> m_identityHistory;
        private byte[] m_serverNonce;
        private byte[] m_previousServerNonce;
        private X509Certificate2 m_serverCertificate;
        private uint m_publishCounter;
        private int m_tooManyPublishRequests;
        private long m_lastKeepAliveTime;
        private StatusCode m_lastKeepAliveErrorStatusCode;
        private ServerState m_serverState;
        private int m_keepAliveInterval;
        private readonly Timer m_keepAliveTimer;
        private readonly AsyncAutoResetEvent m_keepAliveEvent = new();
        private uint m_keepAliveCounter;
        private Task m_keepAliveWorker;
        private CancellationTokenSource m_keepAliveCancellation;
        private SemaphoreSlim m_reconnectLock;
        private int m_minPublishRequestCount;
        private int m_maxPublishRequestCount;
        private LinkedList<AsyncRequestState> m_outstandingRequests;
        private string m_userTokenSecurityPolicyUri;
        private Nonce m_eccServerEphemeralKey;
        private Subscription m_defaultSubscription;
        private readonly EndpointDescriptionCollection m_discoveryServerEndpoints;
        private readonly StringCollection m_discoveryProfileUris;

        private class AsyncRequestState
        {
            public uint RequestTypeId;
            public uint RequestId;
            public int TickCount;
            public object Result;
            public bool Defunct;
        }

        private event KeepAliveEventHandler m_KeepAlive;
        private event NotificationEventHandler m_Publish;
        private event PublishErrorEventHandler m_PublishError;
        private event PublishSequenceNumbersToAcknowledgeEventHandler m_PublishSequenceNumbersToAcknowledge;
        private event EventHandler m_SubscriptionsChanged;
        private event EventHandler m_SessionClosing;
        private event EventHandler m_SessionConfigurationChanged;
    }

    /// <summary>
    /// The event arguments provided when a keep alive response arrives.
    /// </summary>
    public class KeepAliveEventArgs : EventArgs
    {
        /// <summary>
        /// Creates a new instance.
        /// </summary>
        public KeepAliveEventArgs(
            ServiceResult status,
            ServerState currentState,
            DateTime currentTime)
        {
            Status = status;
            CurrentState = currentState;
            CurrentTime = currentTime;
        }

        /// <summary>
        /// Gets the status associated with the keep alive operation.
        /// </summary>
        public ServiceResult Status { get; }

        /// <summary>
        /// Gets the current server state.
        /// </summary>
        public ServerState CurrentState { get; }

        /// <summary>
        /// Gets the current server time.
        /// </summary>
        public DateTime CurrentTime { get; }

        /// <summary>
        /// Gets or sets a flag indicating whether the session should send another keep alive.
        /// </summary>
        public bool CancelKeepAlive { get; set; }
    }

    /// <summary>
    /// Represents the event arguments provided when a new notification message arrives.
    /// </summary>
    public class NotificationEventArgs : EventArgs
    {
        /// <summary>
        /// Creates a new instance.
        /// </summary>
        public NotificationEventArgs(
            Subscription subscription,
            NotificationMessage notificationMessage,
            IList<string> stringTable)
        {
            Subscription = subscription;
            NotificationMessage = notificationMessage;
            StringTable = stringTable;
        }

        /// <summary>
        /// Gets the subscription that the notification applies to.
        /// </summary>
        public Subscription Subscription { get; }

        /// <summary>
        /// Gets the notification message.
        /// </summary>
        public NotificationMessage NotificationMessage { get; }

        /// <summary>
        /// Gets the string table returned with the notification message.
        /// </summary>
        public IList<string> StringTable { get; }
    }

    /// <summary>
    /// Represents the event arguments provided when a publish error occurs.
    /// </summary>
    public class PublishErrorEventArgs : EventArgs
    {
        /// <summary>
        /// Creates a new instance.
        /// </summary>
        public PublishErrorEventArgs(ServiceResult status)
        {
            Status = status;
        }

        /// <summary>
        /// Creates a new instance.
        /// </summary>
        internal PublishErrorEventArgs(
            ServiceResult status,
            uint subscriptionId,
            uint sequenceNumber)
        {
            Status = status;
            SubscriptionId = subscriptionId;
            SequenceNumber = sequenceNumber;
        }

        /// <summary>
        /// Gets the status associated with the keep alive operation.
        /// </summary>
        public ServiceResult Status { get; }

        /// <summary>
        /// Gets the subscription with the message that could not be republished.
        /// </summary>
        public uint SubscriptionId { get; }

        /// <summary>
        /// Gets the sequence number for the message that could not be republished.
        /// </summary>
        public uint SequenceNumber { get; }
    }

    /// <summary>
    /// Represents the event arguments provided when publish response
    /// sequence numbers are about to be ackknowledged with a publish request.
    /// </summary>
    /// <remarks>
    /// A callee can defer an acknowledge to the next publish request by
    /// moving the <see cref="SubscriptionAcknowledgement"/> to the deferred list.
    /// The callee can modify the list of acknowledgements to send, it is the
    /// responsibility of the caller to protect the lists for modifications.
    /// </remarks>
    public class PublishSequenceNumbersToAcknowledgeEventArgs : EventArgs
    {
        /// <summary>
        /// Creates a new instance.
        /// </summary>
        public PublishSequenceNumbersToAcknowledgeEventArgs(
            SubscriptionAcknowledgementCollection acknowledgementsToSend,
            SubscriptionAcknowledgementCollection deferredAcknowledgementsToSend)
        {
            AcknowledgementsToSend = acknowledgementsToSend;
            DeferredAcknowledgementsToSend = deferredAcknowledgementsToSend;
        }

        /// <summary>
        /// The acknowledgements which are sent with the next publish request.
        /// </summary>
        /// <remarks>
        /// A client may also choose to remove an acknowledgement from this list to add it back
        /// to the list in a subsequent callback when the request is fully processed.
        /// </remarks>
        public SubscriptionAcknowledgementCollection AcknowledgementsToSend { get; }

        /// <summary>
        /// The deferred list of acknowledgements.
        /// </summary>
        /// <remarks>
        /// The callee can transfer an outstanding <see cref="SubscriptionAcknowledgement"/>
        /// to this list to defer the acknowledge of a sequence number to the next publish request.
        /// </remarks>
        public SubscriptionAcknowledgementCollection DeferredAcknowledgementsToSend { get; }
    }
}
