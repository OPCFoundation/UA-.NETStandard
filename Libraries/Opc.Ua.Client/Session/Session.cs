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
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
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
    public partial class Session : SessionClientBatched, ISession,
        ISnapshotRestore<SessionState>, ISnapshotRestore<SessionConfiguration>
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
        [Obsolete("Use constructor with ITransportChannel instead of ISessionChannel.")]
        public Session(
            ISessionChannel channel,
            ApplicationConfiguration configuration,
            ConfiguredEndpoint endpoint)
            : this(
                  channel is ITransportChannel transportChannel ?
                    transportChannel :
                    throw new ArgumentException("not a transport channel"),
                  configuration,
                  endpoint)
        {
        }

        /// <summary>
        /// Constructs a new instance of the <see cref="ISession"/> class.
        /// </summary>
        /// <param name="channel">The channel used to communicate with the server.</param>
        /// <param name="configuration">The configuration for the client application.</param>
        /// <param name="endpoint">The endpoint used to initialize the channel.</param>
        /// <param name="clientCertificate">The certificate to use for the client.</param>
        /// <param name="clientCertificateChain">The certificate chain of the client
        /// certificate.</param>
        /// <param name="availableEndpoints">The list of available endpoints returned
        /// by server in GetEndpoints() response.</param>
        /// <param name="discoveryProfileUris">The value of profileUris used in
        /// GetEndpoints() request.</param>
        /// <remarks>
        /// The application configuration is used to look up the certificate if none
        /// is provided. The clientCertificate must have the private key. This will
        /// require that the certificate be loaded from a certicate store. Converting
        /// a DER encoded blob to a X509Certificate2 will not include a private key.
        /// The <i>availableEndpoints</i> and <i>discoveryProfileUris</i> parameters are
        /// used to validate that the list of EndpointDescriptions returned at GetEndpoints
        /// matches the list returned at CreateSession.
        /// </remarks>
        public Session(
            ITransportChannel channel,
            ApplicationConfiguration configuration,
            ConfiguredEndpoint endpoint,
            X509Certificate2? clientCertificate = null,
            X509Certificate2Collection? clientCertificateChain = null,
            EndpointDescriptionCollection? availableEndpoints = null,
            StringCollection? discoveryProfileUris = null)
            : this(
                  channel,
                  configuration,
                  endpoint,
                  channel.MessageContext ?? configuration.CreateMessageContext())
        {
            m_instanceCertificate = clientCertificate;
            m_instanceCertificateChain = clientCertificateChain;
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
                  channel.MessageContext ?? template.m_configuration.CreateMessageContext())
        {
            m_instanceCertificate = template.m_instanceCertificate;
            m_instanceCertificateChain = template.m_instanceCertificateChain;
            m_effectiveEndpoint = template.m_effectiveEndpoint;
            SessionFactory = template.SessionFactory;
            m_defaultSubscription = template.m_defaultSubscription;
            DeleteSubscriptionsOnClose = template.DeleteSubscriptionsOnClose;
            TransferSubscriptionsOnReconnect = template.TransferSubscriptionsOnReconnect;
            PublishRequestCancelDelayOnCloseSession = template.PublishRequestCancelDelayOnCloseSession;
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

            SessionFactory ??= new DefaultSessionFactory(m_telemetry)
            {
                ReturnDiagnostics = ReturnDiagnostics
            };

            NamespaceUris = new NamespaceTable();
            ServerUris = new StringTable();
            Factory = EncodeableFactory.Create();
            m_keepAliveInterval = 5000;
            m_minPublishRequestCount = kDefaultPublishRequestCount;
            m_maxPublishRequestCount = kMaxPublishRequestCountMax;
            m_sessionName = string.Empty;
            DeleteSubscriptionsOnClose = true;
            PublishRequestCancelDelayOnCloseSession = 5000; // 5 seconds default

            ValidateClientConfiguration(configuration);

            // save configuration information.
            m_configuration = configuration;
            m_effectiveEndpoint = m_endpoint = endpoint;
            m_identity = new UserIdentity();

            // update the default subscription.
            DefaultSubscription.MinLifetimeInterval = (uint)m_configuration.ClientConfiguration
                .MinSubscriptionLifetime;

            NamespaceUris = messageContext.NamespaceUris;
            ServerUris = messageContext.ServerUris;
            Factory = messageContext.Factory;

            // initialize the NodeCache late, it needs references to the namespaceUris
            m_nodeCache = new NodeCache(new NodeCacheContext(this), m_telemetry);

            // Create timer for keep alive event triggering but in off state
            m_keepAliveTimer = new Timer(_ => m_keepAliveEvent.Set(), this, Timeout.Infinite, Timeout.Infinite);

            // set the default preferred locales.
            m_preferredLocales = new string[] { CultureInfo.CurrentCulture.Name };

            // create a context to use.
            m_systemContext = new SessionSystemContext(m_telemetry)
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
            byte[]? serverNonce,
            string securityPolicyUri,
            byte[]? previousServerNonce,
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
                    m_configuration.SecurityConfiguration.NonceLength))
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
            if (Disposed && disposing)
            {
                return;
            }

            if (disposing)
            {
                StopKeepAliveTimerAsync().AsTask().GetAwaiter().GetResult();

                Utils.SilentDispose(m_defaultSubscription);
                Utils.SilentDispose(m_nodeCache);

                List<Subscription>? subscriptions;
                lock (m_lock)
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

                Debug.Assert(Disposed);
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
        /// Whether the session is closing
        /// </summary>
        public bool Closing { get; private set; }

        /// <summary>
        /// Gets the period for wich the server will maintain the session if
        /// there is no communication from the client.
        /// </summary>
        public double SessionTimeout => m_sessionTimeout;

        /// <summary>
        /// Gets the local handle assigned to the session.
        /// </summary>
        public object? Handle { get; set; }

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
        public IFilterContext FilterContext
            => new FilterContext(NamespaceUris, m_nodeCache.TypeTree, m_preferredLocales, m_telemetry);

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
                lock (m_lock)
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
                lock (m_lock)
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

        /// <inheritdoc/>
        public int PublishRequestCancelDelayOnCloseSession { get; set; }

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
            get => m_defaultSubscription ??= CreateSubscription(new SubscriptionOptions
            {
                DisplayName = "Subscription",
                PublishingInterval = 1000,
                KeepAliveCount = 10,
                LifetimeCount = 1000,
                Priority = 255,
                PublishingEnabled = true,
                MinLifetimeInterval = (uint)m_configuration.ClientConfiguration
                  .MinSubscriptionLifetime
            });
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

                    for (LinkedListNode<AsyncRequestState>? ii = m_outstandingRequests.First;
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

                    for (LinkedListNode<AsyncRequestState>? ii = m_outstandingRequests.First;
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
                lock (m_lock)
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
                lock (m_lock)
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

        /// <inheritdoc/>
        public ContinuationPointPolicy ContinuationPointPolicy { get; set; }
            = ContinuationPointPolicy.Default;

        /// <inheritdoc/>
        public event RenewUserIdentityEventHandler RenewUserIdentity
        {
            add => m_RenewUserIdentity += value;
            remove => m_RenewUserIdentity -= value;
        }

        private event RenewUserIdentityEventHandler? m_RenewUserIdentity;

        /// <inheritdoc/>
        public virtual void Snapshot(out SessionState state)
        {
            using Activity? activity = m_telemetry.StartActivity();
            Snapshot(out SessionConfiguration configuration);

            // Snapshot subscription state
            var subscriptionStateCollection = new SubscriptionStateCollection(SubscriptionCount);
            foreach (Subscription subscription in Subscriptions)
            {
                subscription.Snapshot(out SubscriptionState subscriptionState);
                subscriptionStateCollection.Add(subscriptionState);
            }
            state = new SessionState(configuration)
            {
                Subscriptions = subscriptionStateCollection
            };
        }

        /// <inheritdoc/>
        public virtual void Restore(SessionState state)
        {
            using Activity? activity = m_telemetry.StartActivity();
            ThrowIfDisposed();
            Restore((SessionConfiguration)state);
            if (state.Subscriptions == null)
            {
                return;
            }
            foreach (SubscriptionState subscriptionState in state.Subscriptions)
            {
                // Restore subscription from state
                Subscription subscription = CreateSubscription(subscriptionState);
                subscription.Restore(subscriptionState);
                AddSubscription(subscription);
            }
        }

        /// <inheritdoc/>
        public void Snapshot(out SessionConfiguration sessionConfiguration)
        {
            var serverNonce = Nonce.CreateNonce(
                SecurityPolicies.GetInfo(m_endpoint.Description?.SecurityPolicyUri),
                m_serverNonce);
            sessionConfiguration = new SessionConfiguration
            {
                SessionName = SessionName,
                SessionId = SessionId,
                AuthenticationToken = AuthenticationToken,
                Identity = Identity,
                ConfiguredEndpoint = ConfiguredEndpoint,
                CheckDomain = CheckDomain,
                ServerNonce = serverNonce,
                ServerEccEphemeralKey = m_eccServerEphemeralKey,
                UserIdentityTokenPolicy = m_userTokenSecurityPolicyUri
            };
        }

        /// <inheritdoc/>
        public void Restore(SessionConfiguration sessionConfiguration)
        {
            ThrowIfDisposed();
            byte[]? serverCertificate = m_endpoint.Description?.ServerCertificate;
            m_sessionName = sessionConfiguration.SessionName ?? "SessionName";
            m_serverCertificate =
                serverCertificate != null
                    ? CertificateFactory.Create(serverCertificate)
                    : null;
            m_identity = sessionConfiguration.Identity ?? new UserIdentity();
            m_checkDomain = sessionConfiguration.CheckDomain;
            m_serverNonce = sessionConfiguration.ServerNonce?.Data;
            m_userTokenSecurityPolicyUri = sessionConfiguration.UserIdentityTokenPolicy;
            m_eccServerEphemeralKey = sessionConfiguration.ServerEccEphemeralKey;

            lock (m_lock)
            {
                SessionCreated(
                    sessionConfiguration.SessionId,
                    sessionConfiguration.AuthenticationToken);
            }
        }

        /// <inheritdoc/>
        public bool ApplySessionConfiguration(SessionConfiguration sessionConfiguration)
        {
            if (sessionConfiguration == null)
            {
                throw new ArgumentNullException(nameof(sessionConfiguration));
            }

            Restore(sessionConfiguration);
            return true;
        }

        /// <inheritdoc/>
        public SessionConfiguration SaveSessionConfiguration(Stream? stream = null)
        {
            Snapshot(out SessionConfiguration sessionConfiguration);
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
        public virtual void Save(
            Stream stream,
            IEnumerable<Subscription> subscriptions,
            IEnumerable<Type>? knownTypes = null)
        {
            using Activity? activity = m_telemetry.StartActivity();
            // Snapshot subscription state
            var subscriptionStateCollection = new SubscriptionStateCollection(SubscriptionCount);
            foreach (Subscription subscription in Subscriptions)
            {
                subscription.Snapshot(out SubscriptionState state);
                subscriptionStateCollection.Add(state);
            }
            XmlWriterSettings settings = Utils.DefaultXmlWriterSettings();

            using var writer = XmlWriter.Create(stream, settings);
            var serializer = new DataContractSerializer(typeof(SubscriptionStateCollection), knownTypes);
            using IDisposable scope = AmbientMessageContext.SetScopedContext(MessageContext);
            serializer.WriteObject(writer, subscriptionStateCollection);
        }

        /// <inheritdoc/>
        public virtual IEnumerable<Subscription> Load(
            Stream stream,
            bool transferSubscriptions = false,
            IEnumerable<Type>? knownTypes = null)
        {
            using Activity? activity = m_telemetry.StartActivity();
            // secure settings
            XmlReaderSettings settings = Utils.DefaultXmlReaderSettings();
            settings.CloseInput = true;

            using var reader = XmlReader.Create(stream, settings);
            var serializer = new DataContractSerializer(typeof(SubscriptionStateCollection), knownTypes);
            using IDisposable scope = AmbientMessageContext.SetScopedContext(MessageContext);
            var stateCollection = (SubscriptionStateCollection?)serializer.ReadObject(reader);
            if (stateCollection == null)
            {
                return [];
            }
            var subscriptions = new SubscriptionCollection(stateCollection.Count);
            foreach (SubscriptionState state in stateCollection)
            {
                // Restore subscription from state
                Subscription subscription = CreateSubscription(state);
                subscription.Restore(state);
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
                subscriptions.Add(subscription);
            }
            return subscriptions;
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
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
        public async Task OpenAsync(
            string sessionName,
            uint sessionTimeout,
            IUserIdentity identity,
            IList<string>? preferredLocales,
            bool checkDomain,
            bool closeChannel,
            CancellationToken ct)
        {
            ThrowIfDisposed();
            using Activity? activity = m_telemetry.StartActivity();

            uint maxMessageSize = (uint?)MessageContext?.MaxMessageSize ??
                throw ServiceResultException.Unexpected(
                    "Transport channel is null or does not have a message context");

            // Load certificate and chain if not already loaded.
            await LoadInstanceCertificateAsync(false, ct).ConfigureAwait(false);

            OpenValidateIdentity(
                ref identity,
                out UserIdentityToken identityToken,
                out UserTokenPolicy identityPolicy,
                out string securityPolicyUri,
                out bool requireEncryption);

            // validate the server certificate /certificate chain.
            X509Certificate2? serverCertificate = null;
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
            int length = m_configuration.SecurityConfiguration.NonceLength;
            m_clientNonce = Nonce.CreateRandomNonceData(length);

            // send the application instance certificate for the client.
            BuildCertificateData(
                out byte[]? clientCertificateData,
                out byte[]? clientCertificateChainData);

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
            CreateSessionResponse? response = null;

            // if security none, first try to connect without certificate
            if (m_endpoint.Description.SecurityPolicyUri == SecurityPolicies.None)
            {
                // first try to connect with client certificate NULL
                try
                {
                    response = await base.CreateSessionAsync(
                        null,
                        clientDescription,
                        m_endpoint.Description.Server.ApplicationUri,
                        m_endpoint.EndpointUrl.ToString(),
                        sessionName,
                        m_clientNonce,
                        null,
                        sessionTimeout,
                        maxMessageSize,
                        ct).ConfigureAwait(false);

                    successCreateSession = true;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
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
                    m_clientNonce,
                    clientCertificateChainData ?? clientCertificateData,
                    sessionTimeout,
                    maxMessageSize,
                    ct).ConfigureAwait(false);
            }
            if (NodeId.IsNull(response?.SessionId))
            {
                throw ServiceResultException.Unexpected(
                    "Create response returned null session id");
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
            lock (m_lock)
            {
                // save session id and cookie in base
                base.SessionCreated(sessionId, sessionCookie);
            }

            m_logger.LogInformation("Revised session timeout value: {SessionTimeout}.", m_sessionTimeout);
            m_logger.LogInformation(
                "Max response message size value: {MaxMessageSize}. Max request message size: {MaxRequestSize}",
                maxMessageSize,
                m_maxRequestMessageSize);

            // we need to call CloseSession if CreateSession was successful but some other exception is thrown
            try
            {
                // verify that the server returned the same instance certificate.
                ValidateServerCertificateData(serverCertificateData);

                ValidateServerEndpoints(serverEndpoints);

                ValidateServerCertificateApplicationUri(serverCertificate, m_endpoint);

                ValidateServerSignature(
                    serverCertificate,
                    serverSignature,
                    clientCertificateData,
                    clientCertificateChainData,
                    m_clientNonce,
                    serverNonce);

                HandleSignedSoftwareCertificates(serverSoftwareCertificates);

                //  process additional header
                ProcessResponseAdditionalHeader(response.ResponseHeader, serverCertificate);

                // create the client signature.
                SecurityPolicyInfo securityPolicy = SecurityPolicies.GetInfo(securityPolicyUri);

                // create the client signature.
                byte[] dataToSign = securityPolicy.GetClientSignatureData(
                    TransportChannel.SecureChannelHash,
                    serverNonce,
                    serverCertificate?.RawData,
                    TransportChannel.ServerChannelCertificate,
                    TransportChannel.ClientChannelCertificate,
                    m_clientNonce ?? []);

                SignatureData clientSignature = SecurityPolicies.CreateSignatureData(
                    securityPolicyUri,
                    m_instanceCertificate,
                    dataToSign);

                // select the security policy for the user token.
                string tokenSecurityPolicyUri = identityPolicy.SecurityPolicyUri;

                if (string.IsNullOrEmpty(tokenSecurityPolicyUri))
                {
                    tokenSecurityPolicyUri = m_endpoint.Description.SecurityPolicyUri;
                }

                // validate server nonce and security parameters for user identity.
                ValidateServerNonce(
                    identity,
                    serverNonce,
                    tokenSecurityPolicyUri,
                    m_previousServerNonce,
                    m_endpoint.Description.SecurityMode);

                // sign data with user token.
                dataToSign = securityPolicy.GetUserTokenSignatureData(
                    TransportChannel.SecureChannelHash,
                    serverNonce,
                    serverCertificate?.RawData,
                    TransportChannel.ServerChannelCertificate,
                    m_instanceCertificate?.RawData,
                    TransportChannel.ClientChannelCertificate,
                    m_clientNonce ?? []);

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

                lock (m_lock)
                {
                    // save nonces.
                    m_sessionName = sessionName;
                    m_identity = identity;
                    m_previousServerNonce = m_serverNonce;
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
            }
            catch (Exception ex)
            {
                m_logger.LogError(ex, "Failed to activate session - closing.");

                try
                {
                    await base.CloseSessionAsync(null, false, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    m_logger.LogError(
                        e,
                        "Cleanup: CloseSessionAsync() or CloseChannelAsync() raised exception.");
                }
                finally
                {
                    lock (m_lock)
                    {
                        SessionCreated(null, null);
                    }
                }
                if (closeChannel)
                {
                    await CloseChannelAsync(CancellationToken.None).ConfigureAwait(false);
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
            IUserIdentity? identity,
            StringCollection preferredLocales,
            CancellationToken ct = default)
        {
            ThrowIfDisposed();
            using Activity? activity = m_telemetry.StartActivity();
            byte[]? serverNonce = null;

            lock (m_lock)
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
            SecurityPolicyInfo securityPolicy = SecurityPolicies.GetInfo(securityPolicyUri);

            // create the client signature.
            byte[] dataToSign = securityPolicy.GetClientSignatureData(
                TransportChannel.SecureChannelHash,
                serverNonce,
                m_serverCertificate?.RawData,
                TransportChannel.ServerChannelCertificate,
                TransportChannel.ClientChannelCertificate,
                m_clientNonce ?? []);

            SignatureData clientSignature = SecurityPolicies.CreateSignatureData(
                securityPolicyUri,
                m_instanceCertificate,
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
                    StatusCodes.BadIdentityTokenRejected,
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
                await m_configuration.CertificateValidator.ValidateAsync(m_serverCertificate, ct).ConfigureAwait(false);
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

            dataToSign = securityPolicy.GetUserTokenSignatureData(
                TransportChannel.SecureChannelHash,
                serverNonce,
                m_serverCertificate?.RawData,
                TransportChannel.ServerChannelCertificate,
                m_instanceCertificate?.RawData,
                TransportChannel.ClientChannelCertificate,
                m_clientNonce ?? []);

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
            lock (m_lock)
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
            ThrowIfDisposed();
            using Activity? activity = m_telemetry.StartActivity();
            if (subscription == null)
            {
                throw new ArgumentNullException(nameof(subscription));
            }

            if (subscription.Created)
            {
                await subscription.DeleteAsync(false, ct).ConfigureAwait(false);
            }

            lock (m_lock)
            {
                if (!m_subscriptions.Remove(subscription))
                {
                    return false;
                }

                subscription.Session = null;
            }

            m_SubscriptionsChanged?.Invoke(this, EventArgs.Empty);

            return true;
        }

        /// <inheritdoc/>
        public async Task<bool> RemoveSubscriptionsAsync(
            IEnumerable<Subscription> subscriptions,
            CancellationToken ct = default)
        {
            ThrowIfDisposed();
            using Activity? activity = m_telemetry.StartActivity();
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
                m_SubscriptionsChanged?.Invoke(this, EventArgs.Empty);
            }

            return removed;
        }

        /// <inheritdoc/>
        public async Task<bool> ReactivateSubscriptionsAsync(
            SubscriptionCollection subscriptions,
            bool sendInitialValues,
            CancellationToken ct = default)
        {
            ThrowIfDisposed();
            using Activity? activity = m_telemetry.StartActivity();
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
                        try
                        {
                            IReadOnlyList<ServiceResult> resendResults = await this.ResendDataAsync(
                                subscriptions.Select(s => s.Id),
                                ct).ConfigureAwait(false);
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
                        catch (ServiceResultException sre)
                        {
                            m_logger.LogError(sre, "Failed to call resend data for subscriptions.");
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
        public async Task<bool> TransferSubscriptionsAsync(
            SubscriptionCollection subscriptions,
            bool sendInitialValues,
            CancellationToken ct)
        {
            using Activity? activity = m_telemetry.StartActivity();
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
            using Activity? activity = m_telemetry.StartActivity();
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
            using Activity? activity = m_telemetry.StartActivity();
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
            using Activity? activity = m_telemetry.StartActivity();
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

        /// <inheritdoc/>
        public async Task FetchOperationLimitsAsync(CancellationToken ct)
        {
            using Activity? activity = m_telemetry.StartActivity();
            // First we read the node read max to optimize the second read.
            var nodeIds = new List<NodeId>
            {
        VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerRead
            };
            (DataValueCollection values, IList<ServiceResult> errors) =
                await this.ReadValuesAsync(nodeIds, ct).ConfigureAwait(false);
            int index = 0;
            OperationLimits.MaxNodesPerRead = Get<uint>(ref index, values, errors);

            nodeIds =
            [
        VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerHistoryReadData,
        VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerHistoryReadEvents,
        VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerWrite,
        VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerRead,
        VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerHistoryUpdateData,
        VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerHistoryUpdateEvents,
        VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerMethodCall,
        VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerBrowse,
        VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerRegisterNodes,
        VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerNodeManagement,
        VariableIds.Server_ServerCapabilities_OperationLimits_MaxMonitoredItemsPerCall,
        VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerTranslateBrowsePathsToNodeIds,
        VariableIds.Server_ServerCapabilities_MaxBrowseContinuationPoints,
        VariableIds.Server_ServerCapabilities_MaxHistoryContinuationPoints,
        VariableIds.Server_ServerCapabilities_MaxQueryContinuationPoints,
        VariableIds.Server_ServerCapabilities_MaxStringLength,
        VariableIds.Server_ServerCapabilities_MaxArrayLength,
        VariableIds.Server_ServerCapabilities_MaxByteStringLength,
        VariableIds.Server_ServerCapabilities_MinSupportedSampleRate,
        VariableIds.Server_ServerCapabilities_MaxSessions,
        VariableIds.Server_ServerCapabilities_MaxSubscriptions,
        VariableIds.Server_ServerCapabilities_MaxMonitoredItems,
        VariableIds.Server_ServerCapabilities_MaxMonitoredItemsPerSubscription,
        VariableIds.Server_ServerCapabilities_MaxMonitoredItemsQueueSize,
        VariableIds.Server_ServerCapabilities_MaxSubscriptionsPerSession,
        VariableIds.Server_ServerCapabilities_MaxWhereClauseParameters,
        VariableIds.Server_ServerCapabilities_MaxSelectClauseParameters
            ];

            (values, errors) = await this.ReadValuesAsync(nodeIds, ct).ConfigureAwait(false);
            index = 0;
            OperationLimits.MaxNodesPerHistoryReadData = Get<uint>(ref index, values, errors);
            OperationLimits.MaxNodesPerHistoryReadEvents = Get<uint>(ref index, values, errors);
            OperationLimits.MaxNodesPerWrite = Get<uint>(ref index, values, errors);
            OperationLimits.MaxNodesPerRead = Get<uint>(ref index, values, errors);
            OperationLimits.MaxNodesPerHistoryUpdateData = Get<uint>(ref index, values, errors);
            OperationLimits.MaxNodesPerHistoryUpdateEvents = Get<uint>(ref index, values, errors);
            OperationLimits.MaxNodesPerMethodCall = Get<uint>(ref index, values, errors);
            OperationLimits.MaxNodesPerBrowse = Get<uint>(ref index, values, errors);
            OperationLimits.MaxNodesPerRegisterNodes = Get<uint>(ref index, values, errors);
            OperationLimits.MaxNodesPerNodeManagement = Get<uint>(ref index, values, errors);
            OperationLimits.MaxMonitoredItemsPerCall = Get<uint>(ref index, values, errors);
            OperationLimits.MaxNodesPerTranslateBrowsePathsToNodeIds = Get<uint>(ref index, values, errors);
            ServerCapabilities.MaxBrowseContinuationPoints = Get<ushort>(ref index, values, errors);
            ServerCapabilities.MaxHistoryContinuationPoints = Get<ushort>(ref index, values, errors);
            ServerCapabilities.MaxQueryContinuationPoints = Get<ushort>(ref index, values, errors);
            ServerCapabilities.MaxStringLength = Get<uint>(ref index, values, errors);
            ServerCapabilities.MaxArrayLength = Get<uint>(ref index, values, errors);
            ServerCapabilities.MaxByteStringLength = Get<uint>(ref index, values, errors);
            ServerCapabilities.MinSupportedSampleRate = Get<double>(ref index, values, errors);
            ServerCapabilities.MaxSessions = Get<uint>(ref index, values, errors);
            ServerCapabilities.MaxSubscriptions = Get<uint>(ref index, values, errors);
            ServerCapabilities.MaxMonitoredItems = Get<uint>(ref index, values, errors);
            ServerCapabilities.MaxMonitoredItemsPerSubscription = Get<uint>(ref index, values, errors);
            ServerCapabilities.MaxMonitoredItemsQueueSize = Get<uint>(ref index, values, errors);
            ServerCapabilities.MaxSubscriptionsPerSession = Get<uint>(ref index, values, errors);
            ServerCapabilities.MaxWhereClauseParameters = Get<uint>(ref index, values, errors);
            ServerCapabilities.MaxSelectClauseParameters = Get<uint>(ref index, values, errors);

            // Helper extraction
            static T Get<T>(ref int index, IList<DataValue> values, IList<ServiceResult> errors)
                where T : struct
            {
                DataValue value = values[index];
                ServiceResult error = errors.Count > 0 ? errors[index] : ServiceResult.Good;
                index++;
                if (ServiceResult.IsNotBad(error) && value.Value is T retVal)
                {
                    return retVal;
                }
                return default;
            }

            uint maxByteStringLength = (uint?)m_configuration.TransportQuotas?.MaxByteStringLength ?? 0u;
            if (maxByteStringLength != 0 &&
                (ServerCapabilities.MaxByteStringLength == 0 ||
                    ServerCapabilities.MaxByteStringLength > maxByteStringLength))
            {
                ServerCapabilities.MaxByteStringLength = maxByteStringLength;
            }
        }

        /// <summary>
        /// Recreates a session based on a specified template.
        /// </summary>
        /// <param name="ct">Cancellation Token to cancel operation with</param>
        /// <returns>The new session object.</returns>
        protected internal async Task<Session> RecreateAsync(
            CancellationToken ct = default)
        {
            ServiceMessageContext messageContext = m_configuration
                .CreateMessageContext();
            messageContext.Factory = Factory;

            // create the channel object used to connect to the server.
            ITransportChannel channel = await UaChannelBase.CreateUaBinaryChannelAsync(
                m_configuration,
                ConfiguredEndpoint.Description,
                ConfiguredEndpoint.Configuration,
                m_instanceCertificate,
                m_configuration.SecurityConfiguration.SendCertificateChain
                    ? m_instanceCertificateChain
                    : null,
                messageContext,
                ct).ConfigureAwait(false);

            // create the session object.
            Session session = CloneSession(channel, true);

            try
            {
                session.RecreateRenewUserIdentity();
                // open the session.
                await session
                    .OpenAsync(
                        SessionName,
                        (uint)SessionTimeout,
                        session.Identity ?? new UserIdentity(),
                        PreferredLocales,
                        m_checkDomain,
                        ct)
                    .ConfigureAwait(false);

                await session.RecreateSubscriptionsAsync(
                    TransferSubscriptionsOnReconnect,
                    Subscriptions,
                    ct).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                session.Dispose();
                ThrowCouldNotRecreateSessionException(e, SessionName);
            }
            return session;
        }

        /// <summary>
        /// Recreates a session based on a specified template.
        /// </summary>
        /// <param name="connection">The waiting reverse connection.</param>
        /// <param name="ct">Cancellation token to cancel operation with</param>
        /// <returns>The new session object.</returns>
        protected internal async Task<Session> RecreateAsync(
            ITransportWaitingConnection connection,
            CancellationToken ct = default)
        {
            ServiceMessageContext messageContext = m_configuration
                .CreateMessageContext();
            messageContext.Factory = Factory;

            // create the channel object used to connect to the server.
            ITransportChannel channel = await UaChannelBase.CreateUaBinaryChannelAsync(
                m_configuration,
                connection,
                ConfiguredEndpoint.Description,
                ConfiguredEndpoint.Configuration,
                m_instanceCertificate,
                m_configuration.SecurityConfiguration.SendCertificateChain
                    ? m_instanceCertificateChain
                    : null,
                messageContext,
                ct).ConfigureAwait(false);

            // create the session object.
            Session session = CloneSession(channel, true);

            try
            {
                session.RecreateRenewUserIdentity();
                // open the session.
                await session
                    .OpenAsync(
                        SessionName,
                        (uint)SessionTimeout,
                        session.Identity ?? new UserIdentity(),
                        PreferredLocales,
                        CheckDomain,
                        ct)
                    .ConfigureAwait(false);

                await session.RecreateSubscriptionsAsync(
                    TransferSubscriptionsOnReconnect,
                    Subscriptions,
                    ct).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                session.Dispose();
                ThrowCouldNotRecreateSessionException(e, SessionName);
            }

            return session;
        }

        /// <summary>
        /// Recreates a session based on a specified template using the provided channel.
        /// </summary>
        /// <param name="transportChannel">The waiting reverse connection.</param>
        /// <param name="ct">Cancellation token to cancel the operation with</param>
        /// <returns>The new session object.</returns>
        protected internal async Task<Session> RecreateAsync(
            ITransportChannel transportChannel,
            CancellationToken ct = default)
        {
            if (transportChannel == null)
            {
                return await RecreateAsync(ct).ConfigureAwait(false);
            }

            // create the session object.
            Session session = CloneSession(transportChannel, true);

            try
            {
                session.RecreateRenewUserIdentity();
                // open the session.
                await session
                    .OpenAsync(
                        SessionName,
                        (uint)SessionTimeout,
                        session.Identity ?? new UserIdentity(),
                        PreferredLocales,
                        CheckDomain,
                        false,
                        ct)
                    .ConfigureAwait(false);

                // create the subscriptions.
                await session.RecreateSubscriptionsAsync(
                    false,
                    Subscriptions,
                    ct).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                session.Dispose();
                ThrowCouldNotRecreateSessionException(e, SessionName);
            }

            return session;
        }

        /// <inheritdoc/>
        public override Task<StatusCode> CloseAsync(CancellationToken ct = default)
        {
            return CloseAsync(m_keepAliveInterval, true, ct);
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

            Closing = true;

            using Activity? activity = m_telemetry.StartActivity();
            try
            {
                // stop the keep alive timer.
                await StopKeepAliveTimerAsync().ConfigureAwait(false);

                // check if correctly connected.
                bool connected = Connected;

                // halt all background threads.
                if (connected && m_SessionClosing != null)
                {
                    try
                    {
                        m_SessionClosing(this, EventArgs.Empty);
                    }
                    catch (Exception e)
                    {
                        m_logger.LogError(e, "Session: Unexpected error raising SessionClosing event.");
                    }
                }

                // close the session with the server.
                if (connected)
                {
                    try
                    {
                        // Wait for or cancel outstanding publish requests before closing session.
                        await WaitForOrCancelOutstandingPublishRequestsAsync(ct).ConfigureAwait(false);

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
                            ct).ConfigureAwait(false);
                    }
                    // don't throw errors on disconnect, but return them
                    // so the caller can log the error.
                    catch (ServiceResultException sre)
                    {
                        m_logger.LogDebug(sre, "Error closing session during Close.");
                        result = sre.StatusCode;
                    }
                    catch (Exception e1)
                    {
                        m_logger.LogDebug(e1, "Error closing session during Close.");
                        result = StatusCodes.Bad;
                    }
                    finally
                    {
                        if (closeChannel)
                        {
                            try
                            {
                                await CloseChannelAsync(ct).ConfigureAwait(false);
                            }
                            catch (Exception e2)
                            {
                                m_logger.LogDebug(e2, "Error closing channel during Close");
                            }
                        }

                        // raised notification indicating the session is closed.
                        lock (m_lock)
                        {
                            SessionCreated(null, null);
                        }
                    }
                }

                // clean up.
                if (closeChannel)
                {
                    Dispose();
                }

                return result;
            }
            finally
            {
                Closing = false;
            }
        }

        /// <inheritdoc/>
        public async Task ReloadInstanceCertificateAsync(CancellationToken ct = default)
        {
            ThrowIfDisposed();
            using Activity? activity = m_telemetry.StartActivity();
            await m_reconnectLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                // Force reload
                m_instanceCertificate = null;
                await LoadInstanceCertificateAsync(false, ct).ConfigureAwait(false);
            }
            finally
            {
                m_reconnectLock.Release();
            }
        }

        /// <inheritdoc/>
        public async Task ReconnectAsync(
            ITransportWaitingConnection? connection,
            ITransportChannel? channel,
            CancellationToken ct)
        {
            ThrowIfDisposed();
            using Activity? activity = m_telemetry.StartActivity();
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

                // need to refresh the identity (reprompt for password, refresh token).
                RecreateRenewUserIdentity();

                //
                // It is possible the session was created and a previous configuration was
                // applied, then reconnect can be called even though we are not connected
                // But while valid we also want to check that otherwise the endpoint was not
                // changed.
                //
                await LoadInstanceCertificateAsync(true, ct).ConfigureAwait(false);

                string securityPolicyUri = m_endpoint.Description.SecurityPolicyUri;
                SecurityPolicyInfo securityPolicy = SecurityPolicies.GetInfo(securityPolicyUri);

                // create the client signature.
                byte[] dataToSign = securityPolicy.GetClientSignatureData(
                    TransportChannel.SecureChannelHash,
                    m_serverNonce,
                    m_serverCertificate?.RawData,
                    TransportChannel.ServerChannelCertificate,
                    TransportChannel.ClientChannelCertificate,
                    m_clientNonce ?? []);

                EndpointDescription endpoint = m_endpoint.Description;

                SignatureData clientSignature = SecurityPolicies.CreateSignatureData(
                    endpoint.SecurityPolicyUri,
                    m_instanceCertificate,
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
                        StatusCodes.BadIdentityTokenRejected,
                        "Endpoint does not support the user identity type provided.");
                }

                // select the security policy for the user token.
                string tokenSecurityPolicyUri = identityPolicy.SecurityPolicyUri;

                if (string.IsNullOrEmpty(tokenSecurityPolicyUri))
                {
                    tokenSecurityPolicyUri = endpoint.SecurityPolicyUri;
                }
                m_userTokenSecurityPolicyUri = tokenSecurityPolicyUri;

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

                dataToSign = securityPolicy.GetUserTokenSignatureData(
                    TransportChannel.SecureChannelHash,
                    m_serverNonce,
                    m_serverCertificate?.RawData,
                    TransportChannel.ServerChannelCertificate,
                    m_instanceCertificate?.RawData,
                    TransportChannel.ClientChannelCertificate,
                    m_clientNonce ?? []);

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
                    ITransportChannel? transportChannel = NullableTransportChannel;

                    // check if the channel supports reconnect.
                    if (transportChannel != null &&
                        (transportChannel.SupportedFeatures & TransportChannelFeatures.Reconnect) != 0)
                    {
                        await transportChannel.ReconnectAsync(connection, ct).ConfigureAwait(false);
                    }
                    else
                    {
                        // initialize the channel which will be created with the server.
                        transportChannel = await UaChannelBase.CreateUaBinaryChannelAsync(
                            m_configuration,
                            connection,
                            m_endpoint.Description,
                            m_endpoint.Configuration,
                            m_instanceCertificate,
                            m_configuration.SecurityConfiguration.SendCertificateChain
                                ? m_instanceCertificateChain
                                : null,
                            MessageContext,
                            ct).ConfigureAwait(false);

                        // disposes the existing channel.
                        TransportChannel = transportChannel;
                    }
                }
                else if (channel != null)
                {
                    TransportChannel = channel;
                }
                else
                {
                    ITransportChannel? transportChannel = NullableTransportChannel;

                    // check if the channel supports reconnect.
                    if (transportChannel != null &&
                        (transportChannel.SupportedFeatures & TransportChannelFeatures.Reconnect) != 0)
                    {
                        await transportChannel.ReconnectAsync(ct: ct).ConfigureAwait(false);
                    }
                    else
                    {
                        // initialize the channel which will be created with the server.
                        transportChannel = await UaChannelBase.CreateUaBinaryChannelAsync(
                            m_configuration,
                            m_endpoint.Description,
                            m_endpoint.Configuration,
                            m_instanceCertificate,
                            m_configuration.SecurityConfiguration.SendCertificateChain
                                ? m_instanceCertificateChain
                                : null,
                            MessageContext,
                            ct).ConfigureAwait(false);

                        // disposes the existing channel.
                        TransportChannel = transportChannel;
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

                    lock (m_lock)
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
            using Activity? activity = m_telemetry.StartActivity();
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
        /// Recreate the subscriptions in a recreated session.
        /// </summary>
        /// <param name="transferSbscriptionTemplates">Uses Transfer service
        /// if set to <c>true</c>.</param>
        /// <param name="subscriptionsTemplate">The template for the subscriptions.</param>
        /// <param name="ct">Cancellation token to cancel operation with</param>
        private async Task RecreateSubscriptionsAsync(
            bool transferSbscriptionTemplates,
            IEnumerable<Subscription> subscriptionsTemplate,
            CancellationToken ct)
        {
            using Activity? activity = m_telemetry.StartActivity();
            bool transferred = false;
            if (transferSbscriptionTemplates)
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
            ThrowIfDisposed();
            if (subscription == null)
            {
                throw new ArgumentNullException(nameof(subscription));
            }

            lock (m_lock)
            {
                if (m_subscriptions.Contains(subscription))
                {
                    return false;
                }

                subscription.Session = this;
                subscription.Telemetry = m_telemetry;
                m_subscriptions.Add(subscription);
            }

            m_SubscriptionsChanged?.Invoke(this, EventArgs.Empty);

            return true;
        }

        /// <inheritdoc/>
        public bool RemoveTransferredSubscription(Subscription subscription)
        {
            ThrowIfDisposed();
            if (subscription == null)
            {
                throw new ArgumentNullException(nameof(subscription));
            }

            if (subscription.Session != this)
            {
                return false;
            }

            lock (m_lock)
            {
                if (!m_subscriptions.Remove(subscription))
                {
                    return false;
                }

                subscription.Session = null;
            }

            m_SubscriptionsChanged?.Invoke(this, EventArgs.Empty);

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

            await StopKeepAliveTimerAsync().ConfigureAwait(false);

            lock (m_lock)
            {
                ThrowIfDisposed();

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
            lock (m_lock)
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
            Task? keepAliveWorker;
            CancellationTokenSource? keepAliveCancellation;

            lock (m_lock)
            {
                ThrowIfDisposed();

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
            Debug.Assert(keepAliveCancellation != null);
            try
            {
                keepAliveCancellation!.Cancel();
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
                keepAliveCancellation!.Dispose();
            }
        }

        /// <summary>
        /// Waits for outstanding publish requests to complete or cancels them.
        /// </summary>
        private async Task WaitForOrCancelOutstandingPublishRequestsAsync(CancellationToken ct)
        {
            // Get outstanding publish requests
            List<uint> publishRequestHandles = [];
            lock (m_outstandingRequests)
            {
                foreach (AsyncRequestState state in m_outstandingRequests)
                {
                    if (state.RequestTypeId == DataTypes.PublishRequest && !state.Defunct)
                    {
                        publishRequestHandles.Add(state.RequestId);
                    }
                }
            }

            if (publishRequestHandles.Count == 0)
            {
                m_logger.LogDebug("No outstanding publish requests to cancel.");
                return;
            }

            m_logger.LogInformation(
                "Waiting for {Count} outstanding publish requests to complete before closing session.",
                publishRequestHandles.Count);

            // Wait for outstanding requests with timeout
            if (PublishRequestCancelDelayOnCloseSession != 0)
            {
                int waitTimeout = PublishRequestCancelDelayOnCloseSession < 0
                    ? int.MaxValue
                    : PublishRequestCancelDelayOnCloseSession;

                int startTime = HiResClock.TickCount;
                while (true)
                {
                    // Check if all publish requests completed
                    int remainingCount = 0;
                    lock (m_outstandingRequests)
                    {
                        foreach (AsyncRequestState state in m_outstandingRequests)
                        {
                            if (state.RequestTypeId == DataTypes.PublishRequest && !state.Defunct)
                            {
                                remainingCount++;
                            }
                        }
                    }

                    if (remainingCount == 0)
                    {
                        m_logger.LogDebug("All outstanding publish requests completed.");
                        return;
                    }

                    // Check timeout
                    int elapsed = HiResClock.TickCount - startTime;
                    if (elapsed >= waitTimeout)
                    {
                        m_logger.LogWarning(
                            "Timeout waiting for {Count} publish requests to complete. Cancelling them.",
                            remainingCount);
                        break;
                    }

                    // Check cancellation
                    if (ct.IsCancellationRequested)
                    {
                        m_logger.LogWarning("Cancellation requested while waiting for publish requests.");
                        break;
                    }

                    // Wait a bit before checking again
                    try
                    {
                        await Task.Delay(100, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        m_logger.LogWarning("Cancellation requested while waiting for publish requests.");
                        break;
                    }
                }
            }

            // Cancel remaining outstanding publish requests
            List<uint> requestsToCancel = [];
            lock (m_outstandingRequests)
            {
                foreach (AsyncRequestState state in m_outstandingRequests)
                {
                    if (state.RequestTypeId == DataTypes.PublishRequest && !state.Defunct)
                    {
                        requestsToCancel.Add(state.RequestId);
                    }
                }
            }

            if (requestsToCancel.Count > 0)
            {
                m_logger.LogInformation(
                    "Cancelling {Count} outstanding publish requests.",
                    requestsToCancel.Count);

                // Cancel each outstanding publish request
                foreach (uint requestHandle in requestsToCancel)
                {
                    try
                    {
                        var requestHeader = new RequestHeader
                        {
                            TimeoutHint = (uint)OperationTimeout
                        };

                        await CancelAsync(requestHeader, requestHandle, ct).ConfigureAwait(false);

                        m_logger.LogDebug("Cancelled publish request with handle {Handle}.", requestHandle);
                    }
                    catch (Exception ex)
                    {
                        // Log but don't throw - we're closing anyway
                        m_logger.LogWarning(
                            ex,
                            "Error cancelling publish request with handle {Handle}.",
                            requestHandle);
                    }
                }
            }
        }

        /// <summary>
        /// Removes a completed async request.
        /// </summary>
        private AsyncRequestState? RemoveRequest(Task result, uint requestId, uint typeId)
        {
            lock (m_outstandingRequests)
            {
                for (LinkedListNode<AsyncRequestState>? ii = m_outstandingRequests.First;
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
        private void AsyncRequestStarted(Task result, Activity? activity, uint requestId, uint typeId)
        {
            lock (m_outstandingRequests)
            {
                // check if the request completed asynchronously.
                AsyncRequestState? state = RemoveRequest(result, requestId, typeId);

                // add a new request.
                if (state == null)
                {
                    state = new AsyncRequestState
                    {
                        Activity = activity,
                        Defunct = false,
                        RequestId = requestId,
                        RequestTypeId = typeId,
                        Result = result,
                        TickCount = HiResClock.TickCount
                    };

                    m_outstandingRequests.AddLast(state);
                }
                else
                {
                    state.Dispose();
                }
            }
        }

        /// <summary>
        /// Removes a completed async request.
        /// </summary>
        private void AsyncRequestCompleted(Task result, uint requestId, uint typeId)
        {
            lock (m_outstandingRequests)
            {
                // remove the request.
                AsyncRequestState? state = RemoveRequest(result, requestId, typeId);

                if (state != null)
                {
                    // mark any old requests as defunct (i.e. the should have returned before this request).
                    const int maxAge = 1000;

                    for (LinkedListNode<AsyncRequestState>? ii = m_outstandingRequests.First;
                        ii != null;
                        ii = ii.Next)
                    {
                        if (ii.Value.RequestTypeId == typeId &&
                            (state.TickCount - ii.Value.TickCount) > maxAge)
                        {
                            ii.Value.Defunct = true;
                        }
                    }

                    state.Dispose();
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
                        TickCount = HiResClock.TickCount,
                        Activity = null
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
            while (!ct.IsCancellationRequested && !Disposed)
            {
                await m_keepAliveEvent.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    // check if session has been closed.
                    if (!Connected || Disposed)
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
                catch (ObjectDisposedException) when (Disposed)
                {
                    // This should not happen, but we fail gracefully anyway
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
                    for (LinkedListNode<AsyncRequestState>? ii = m_outstandingRequests.First;
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

            KeepAliveEventHandler? callback = m_KeepAlive;

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

            KeepAliveEventHandler? callback = m_KeepAlive;

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
            lock (m_lock)
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
        /// Throws in case of types not matching or empty namespace
        /// array returned.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
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
                throw ServiceResultException.Create(result.StatusCode.Code,
                    "Cannot read NamespaceArray node. Validation of returned value failed.");
            }

            string[] namespaceArray = (string[])values[0].Value;
            if (namespaceArray.Length == 0)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadUnexpectedError,
                    "Retrieved namespace list contain no entries.");
            }
            if (namespaceArray[0] != Namespaces.OpcUa)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadUnexpectedError,
                    "Retrieved namespaces are missing OPC UA namespace at index 0.");
            }

            NamespaceUris.Update(namespaceArray);

            if (StatusCode.IsBad(values[1].StatusCode))
            {
                // Gracefully handle not loading server array.
                m_logger.LogError("Cannot read ServerArray node: {StatusCode} - skipping.",
                    values[1].StatusCode);
                return;
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
                throw ServiceResultException.Create(result.StatusCode.Code,
                    "Cannot read ServerArray node. Validation of returned value failed.");
            }

            string[] serverArray = (string[])values[1].Value;
            ServerUris.Update(serverArray);
        }

        /// <summary>
        /// Sends an additional publish request.
        /// </summary>
        public bool BeginPublish(int timeout)
        {
            // do not publish if reconnecting or the session is in closed state.
            if (!Connected)
            {
                m_logger.LogWarning("Publish skipped due to session not connected");
                return false;
            }

            if (Reconnecting)
            {
                m_logger.LogWarning("Publish skipped due to session reconnect");
                return false;
            }

            if (Closing)
            {
                m_logger.LogWarning("Publish cancelled due to session closed");
                return false;
            }

            // get event handler to modify ack list
            PublishSequenceNumbersToAcknowledgeEventHandler? callback
                = m_PublishSequenceNumbersToAcknowledge;

            // collect the current set if acknowledgements.
            SubscriptionAcknowledgementCollection? acknowledgementsToSend = null;
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

            m_logger.LogTrace("PUBLISH #{RequestHandle} SENT", requestHeader.RequestHandle);
            CoreClientUtils.EventLog.PublishStart((int)requestHeader.RequestHandle);

            try
            {
                Activity? activity = m_telemetry.StartActivity();
                Task<PublishResponse> task = PublishAsync(
                    requestHeader,
                    acknowledgementsToSend,
                    default); // TODO: Need a session scoped cancellation token.
                AsyncRequestStarted(task, activity, requestHeader.RequestHandle, DataTypes.PublishRequest);
                task.ConfigureAwait(false)
                    .GetAwaiter()
                    .OnCompleted(() => OnPublishComplete(
                        task,
                        SessionId,
                        acknowledgementsToSend,
                        requestHeader));
                return true;
            }
            catch (Exception e)
            {
                m_logger.LogError(e, "Unexpected error sending publish request.");
                return false;
            }
        }

        /// <summary>
        /// Create the publish requests for the active subscriptions.
        /// </summary>
        public void StartPublishing(int timeout, bool fullQueue)
        {
            int publishCount = GetDesiredPublishRequestCount(true);

            // refill pipeline. Send at least one publish request if subscriptions are active.
            if (publishCount > 0 && BeginPublish(timeout))
            {
                int startCount = fullQueue ? 1 : GoodPublishRequestCount + 1;
                for (int ii = startCount; ii < publishCount; ii++)
                {
                    if (!BeginPublish(timeout))
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
                    PublishErrorEventHandler? callback = m_PublishError;

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
                m_identity = m_RenewUserIdentity(this, m_identity) ?? new UserIdentity();
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
            lock (m_lock)
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

        private void BuildCertificateData(
            out byte[]? clientCertificateData,
            out byte[]? clientCertificateChainData)
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
            X509Certificate2? serverCertificate,
            SignatureData serverSignature,
            byte[]? clientCertificateData,
            byte[]? clientCertificateChainData,
            byte[] clientNonce,
            byte[] serverNonce)
        {
            if (serverSignature == null || serverSignature.Signature == null)
            {
                m_logger.LogInformation("Server signature is null or empty.");
                return;
            }

            // validate the server's signature.
            SecurityPolicyInfo securityPolicy = SecurityPolicies.GetInfo(m_endpoint.Description.SecurityPolicyUri);

            byte[] dataToSign = securityPolicy.GetServerSignatureData(
                TransportChannel.SecureChannelHash,
                clientNonce,
                TransportChannel.ServerChannelCertificate,
                clientCertificateData,
                TransportChannel.ClientChannelCertificate,
                serverNonce);

            if (!SecurityPolicies.VerifySignatureData(
                    serverSignature,
                    m_endpoint.Description.SecurityPolicyUri,
                    serverCertificate,
                    dataToSign))
            {
                // validate the signature with complete chain if the check with leaf certificate failed.
                if (clientCertificateChainData != null)
                {
                    dataToSign = securityPolicy.GetServerSignatureData(
                        TransportChannel.SecureChannelHash,
                        clientNonce,
                        TransportChannel.ServerChannelCertificate,
                        clientCertificateChainData,
                        TransportChannel.ClientChannelCertificate,
                        serverNonce);

                    if (!SecurityPolicies.VerifySignatureData(
                            serverSignature,
                            m_endpoint.Description.SecurityPolicyUri,
                            serverCertificate,
                            dataToSign))
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
        /// Validates the ServerCertificate ApplicationUri to match the ApplicationUri
        /// of the Endpoint (Spec Part 4 5.4.1) returned by the CreateSessionResponse.
        /// Ensure the endpoint was matched in <see cref="ValidateServerEndpoints"/>
        /// with the applicationUri of the server description before the validation.
        /// </summary>
        private void ValidateServerCertificateApplicationUri(
            X509Certificate2? serverCertificate,
            ConfiguredEndpoint endpoint)
        {
            if (serverCertificate != null)
            {
                m_configuration.CertificateValidator.ValidateApplicationUri(serverCertificate, endpoint);
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
                EndpointDescriptionCollection? expectedServerEndpoints;
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

            EndpointDescription? foundDescription = FindMatchingDescription(
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
        private EndpointDescription? FindMatchingDescription(
            EndpointDescriptionCollection? endpointDescriptions,
            EndpointDescription match,
            bool matchPort)
        {
            if (endpointDescriptions == null)
            {
                return null;
            }
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

            PublishErrorEventHandler? callback = m_PublishError;

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
            UInt32Collection? availableSequenceNumbers,
            bool moreNotifications,
            NotificationMessage notificationMessage)
        {
            Subscription? subscription = null;

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

            lock (m_lock)
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
                NotificationEventHandler? publishEventHandler = m_Publish;
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
            bool throwIfConfigurationChangedFromLastLoad,
            CancellationToken ct = default)
        {
            if (m_endpoint.Description.SecurityPolicyUri == SecurityPolicies.None)
            {
                // No need to load instance certificates
                return;
            }

            if (m_instanceCertificate != null &&
                m_instanceCertificate.HasPrivateKey &&
                !m_endpoint.Equals(m_effectiveEndpoint))
            {
                if (throwIfConfigurationChangedFromLastLoad)
                {
                    // Updating a live session must be prevented unless the session was
                    // closed. Therefore we need to throw here to catch this case during any
                    // reconnect or other activation operation
                    throw ServiceResultException.Create(StatusCodes.BadConfigurationError,
                        "Configuration was changed for an active session.");
                }
                // If the configured endpoint was updated while we are closed we reload.
                m_instanceCertificate = null;
            }

            if (m_instanceCertificate == null || !m_instanceCertificate.HasPrivateKey)
            {
                m_instanceCertificate = await LoadInstanceCertificateAsync(
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
                m_effectiveEndpoint = m_endpoint;
                m_instanceCertificateChain = null; // Reload the chain too
            }

            // check for private key.
            if (!m_instanceCertificate.HasPrivateKey)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "Client certificate configured for security policy {0} is missing a private key.",
                    m_endpoint.Description.SecurityPolicyUri);
            }

            // load certificate chain.
            m_instanceCertificateChain ??= await LoadCertificateChainAsync(
                m_configuration,
                m_instanceCertificate,
                ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Load certificate for connection.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal static async Task<X509Certificate2> LoadInstanceCertificateAsync(
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
        internal static async Task<X509Certificate2Collection?> LoadCertificateChainAsync(
            ApplicationConfiguration configuration,
            X509Certificate2 clientCertificate,
            CancellationToken ct = default)
        {
            X509Certificate2Collection? clientCertificateChain = null;
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
            lock (m_lock)
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
        /// Creates and validates the subscription ids for a transfer.
        /// </summary>
        /// <param name="subscriptions">The subscriptions to transfer.</param>
        /// <returns>The subscription ids for the transfer.</returns>
        /// <exception cref="ServiceResultException">Thrown if a subscription is in invalid state.</exception>
        private UInt32Collection CreateSubscriptionIdsForTransfer(
            SubscriptionCollection subscriptions)
        {
            var subscriptionIds = new UInt32Collection();
            lock (m_lock)
            {
                foreach (Subscription subscription in subscriptions)
                {
                    if (subscription.Created && SessionId.Equals(subscription.Session?.SessionId))
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

            if (CryptoUtils.IsEccPolicy(userTokenSecurityPolicyUri))
            {
                var parameters = new AdditionalParametersType();
                parameters.Parameters.Add(
                    new KeyValuePair { Key = AdditionalParameterNames.ECDHPolicyUri, Value = userTokenSecurityPolicyUri });
                requestHeader.AdditionalHeader = new ExtensionObject(parameters);
            }

            return requestHeader;
        }

        /// <summary>
        /// Create a subscription the provided item options
        /// </summary>
        protected virtual Subscription CreateSubscription(SubscriptionOptions? options = null)
        {
            return new Subscription(m_telemetry, options);
        }

        /// <summary>
        /// Process the AdditionalHeader field of a ResponseHeader
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        protected virtual void ProcessResponseAdditionalHeader(
            ResponseHeader responseHeader,
            X509Certificate2? serverCertificate)
        {
            if (ExtensionObject.ToEncodeable(
                responseHeader?.AdditionalHeader) is AdditionalParametersType parameters)
            {
                foreach (KeyValuePair ii in parameters.Parameters)
                {
                    if (ii.Key == AdditionalParameterNames.Padding)
                    {
                        if (ii.Value.TypeInfo != TypeInfo.Scalars.ByteString || ii.Value.Value is not byte[])
                        {
                            m_logger.LogWarning(
                                "Server returned invalid message padding. Ignored.");
                        }

                        if (ii.Value.Value is byte[] padding && padding.Length > 4096)
                        {
                            m_logger.LogWarning(
                                "Server returned a {Size}byte message padding that is too long. Ignored.",
                                padding.Length);
                        }

                        continue;
                    }

                    if (ii.Key == AdditionalParameterNames.ECDHKey)
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

                        if (!CryptoUtils.Verify(
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
                            SecurityPolicies.GetInfo(m_userTokenSecurityPolicyUri),
                            key.PublicKey);
                    }
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
        /// The endpoint configured for the session.
        /// </summary>
        protected ConfiguredEndpoint m_endpoint;

        /// <summary>
        /// The endpoint used while connected to the server.
        /// </summary>
        protected ConfiguredEndpoint m_effectiveEndpoint;

        /// <summary>
        /// The Instance Certificate.
        /// </summary>
        protected X509Certificate2? m_instanceCertificate;

        /// <summary>
        /// The Instance Certificate Chain.
        /// </summary>
        protected X509Certificate2Collection? m_instanceCertificateChain;

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

        /// <summary>m
        /// Time in milliseconds added to <see cref="m_keepAliveInterval"/> before <see cref="KeepAliveStopped"/> is set to true
        /// </summary>
        protected int m_keepAliveGuardBand = 1000;
        private SubscriptionAcknowledgementCollection m_acknowledgementsToSend = [];
        private readonly object m_acknowledgementsToSendLock = new();
#if DEBUG_SEQUENTIALPUBLISHING
        private Dictionary<uint, uint> m_latestAcknowledgementsSent = [];
#endif
        private readonly Lock m_lock = new();
        private readonly List<Subscription> m_subscriptions = [];
        private uint m_maxRequestMessageSize;
        private readonly SessionSystemContext m_systemContext;
        private readonly NodeCache m_nodeCache;
        private readonly List<IUserIdentity> m_identityHistory = [];
        private byte[]? m_serverNonce;
        private byte[]? m_clientNonce;
        private byte[]? m_previousServerNonce;
        private X509Certificate2? m_serverCertificate;
        private uint m_publishCounter;
        private int m_tooManyPublishRequests;
        private long m_lastKeepAliveTime;
        private StatusCode m_lastKeepAliveErrorStatusCode;
        private ServerState m_serverState;
        private int m_keepAliveInterval;
        private readonly Timer m_keepAliveTimer;
        private readonly AsyncAutoResetEvent m_keepAliveEvent = new();
        private uint m_keepAliveCounter;
        private Task? m_keepAliveWorker;
        private CancellationTokenSource? m_keepAliveCancellation;
        private readonly SemaphoreSlim m_reconnectLock = new(1, 1);
        private int m_minPublishRequestCount;
        private int m_maxPublishRequestCount;
        private readonly LinkedList<AsyncRequestState> m_outstandingRequests = [];
        private string? m_userTokenSecurityPolicyUri;
        private Nonce? m_eccServerEphemeralKey;
        private Subscription? m_defaultSubscription;
        private readonly EndpointDescriptionCollection? m_discoveryServerEndpoints;
        private readonly StringCollection? m_discoveryProfileUris;

        private sealed class AsyncRequestState : IDisposable
        {
            public uint RequestTypeId { get; init; }
            public uint RequestId { get; init; }
            public int TickCount { get; init; }
            public required Task Result { get; set; }
            public bool Defunct { get; set; }
            public required Activity? Activity { get; init; }

            public void Dispose()
            {
                Activity?.Dispose();
                Debug.Assert(Result.IsCompleted);
            }
        }

        private event KeepAliveEventHandler? m_KeepAlive;
        private event NotificationEventHandler? m_Publish;
        private event PublishErrorEventHandler? m_PublishError;
        private event PublishSequenceNumbersToAcknowledgeEventHandler? m_PublishSequenceNumbersToAcknowledge;
        private event EventHandler? m_SubscriptionsChanged;
        private event EventHandler? m_SessionClosing;
        private event EventHandler? m_SessionConfigurationChanged;
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
            ServiceResult? status,
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
        public ServiceResult? Status { get; }

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
