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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Identity;

namespace Opc.Ua.Client
{
    /// <summary>
    /// A managed session that wraps a unmanaged <see cref="Session"/>
    /// and automatically handles connection lifecycle, reconnection
    /// with configurable policy, and server redundancy failover.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Service calls are gated during reconnect — callers transparently
    /// wait until the session is reconnected. The gating uses an
    /// <see cref="AsyncReaderWriterLock"/>: connected
    /// service calls take a reader lock (cheap, concurrent), while
    /// reconnect / failover holds the writer lock exclusively.
    /// </para>
    /// <para>
    /// This class uses composition, not inheritance, to wrap the unmanaged
    /// <see cref="Session"/>. All <see cref="ISession"/> members are
    /// delegated to the inner session.
    /// </para>
    /// </remarks>
    public partial class ManagedSession : ISession
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ManagedSession"/>
        /// class. Use <see cref="CreateAsync"/> to create a connected
        /// instance.
        /// </summary>
        private ManagedSession(
            ApplicationConfiguration configuration,
            ConfiguredEndpoint endpoint,
            ISessionFactory sessionFactory,
            IReconnectPolicy reconnectPolicy,
            IServerRedundancyHandler? redundancyHandler,
            ILogger logger,
            IUserIdentity? identity,
            IClientIdentityProvider? identityProvider,
            TimeProvider? timeProvider,
            ArrayOf<string> preferredLocales,
            string sessionName,
            uint sessionTimeout,
            bool checkDomain,
            bool transferSubscriptionsOnRecreate,
            bool poolNotifications,
            IClientChannelManager? channelManager)
        {
            m_configuration = configuration
                ?? throw new ArgumentNullException(nameof(configuration));
            ConfiguredEndpoint = endpoint
                ?? throw new ArgumentNullException(nameof(endpoint));
            SessionFactory = sessionFactory
                ?? throw new ArgumentNullException(nameof(sessionFactory));
            m_reconnectPolicy = reconnectPolicy
                ?? throw new ArgumentNullException(nameof(reconnectPolicy));
            m_redundancyHandler = redundancyHandler;
            m_logger = logger
                ?? throw new ArgumentNullException(nameof(logger));
            m_identity = identity;
            m_identityProvider = identityProvider;
            m_timeProvider = timeProvider ?? TimeProvider.System;
            m_maxTotalReconnectTime = reconnectPolicy is ReconnectPolicy policy
                ? policy.MaxTotalReconnectTime
                : ReconnectPolicy.DefaultMaxTotalReconnectTime;
            m_preferredLocales = preferredLocales;
            m_sessionName = sessionName;
            m_sessionTimeout = sessionTimeout;
            m_checkDomain = checkDomain;
            m_transferSubscriptionsOnRecreate = transferSubscriptionsOnRecreate;
            m_poolNotifications = poolNotifications;
            m_channelManager = channelManager;

            StateMachine = new ConnectionStateMachine(
                reconnectPolicy,
                logger,
                m_maxTotalReconnectTime,
                m_timeProvider);

            WireStateMachineCallbacks();
            SubscribeCertificateChanges();
        }

        /// <summary>
        /// Creates a new <see cref="ManagedSession"/> that is connected
        /// to the specified endpoint.
        /// </summary>
        /// <param name="configuration">The application configuration.
        /// </param>
        /// <param name="endpoint">The configured endpoint to connect to.
        /// </param>
        /// <param name="sessionFactory">The session factory to use for
        /// creating sessions.</param>
        /// <param name="identity">Optional user identity.</param>
        /// <param name="reconnectPolicy">Optional reconnect policy.
        /// Defaults to <see cref="ReconnectPolicy"/>.</param>
        /// <param name="redundancyHandler">Optional redundancy handler.
        /// </param>
        /// <param name="telemetry">Optional telemetry context.</param>
        /// <param name="sessionName">The session name.</param>
        /// <param name="sessionTimeout">Session timeout in ms.</param>
        /// <param name="preferredLocales">Preferred locales.</param>
        /// <param name="checkDomain">Whether to check the domain in
        /// the server certificate.</param>
        /// <param name="engineFactory">Optional subscription engine
        /// factory. Defaults to <see cref="DefaultSubscriptionEngineFactory"/>
        /// (V2 engine) so that <see cref="SubscriptionManager"/> is
        /// available. Pass <see cref="ClassicSubscriptionEngineFactory"/>
        /// for legacy classic-engine behavior.</param>
        /// <param name="transferSubscriptionsOnRecreate">When
        /// <c>true</c>, opt the V2 subscription engine into
        /// transfer-on-recreate. After a session re-create (e.g. a
        /// failover via <c>Session.RecreateInPlaceAsync</c>)
        /// the V2 manager attempts to transfer existing server-side
        /// subscriptions before falling back to per-subscription
        /// recreate. Default <c>false</c> — recreate is the universal
        /// fallback; transfer requires server support.</param>
        /// <param name="poolNotifications">When <c>true</c>, the V2
        /// subscription manager calls <see cref="IPooledEncodeable.Reuse"/>
        /// on notification payload instances after handler dispatch to
        /// release them back to their activator pools. Default
        /// <c>false</c>. See <c>ManagedSessionOptions.PoolNotifications</c>
        /// for the retain-by-copy contract.</param>
        /// <param name="identityProvider">Optional lazy identity provider.</param>
        /// <param name="timeProvider">Optional time provider for proactive refresh.</param>
        /// <param name="channelManager">Optional central
        /// <see cref="IClientChannelManager"/>. When supplied, the
        /// inner Session shares its transport channel with any other
        /// session/discovery client targeting the same endpoint, and
        /// channel reconnect is coordinated centrally.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>A connected <see cref="ManagedSession"/>.</returns>
        public static async Task<ManagedSession> CreateAsync(
            ApplicationConfiguration configuration,
            ConfiguredEndpoint endpoint,
            ISessionFactory sessionFactory,
            IUserIdentity? identity = null,
            IReconnectPolicy? reconnectPolicy = null,
            IServerRedundancyHandler? redundancyHandler = null,
            ITelemetryContext? telemetry = null,
            string sessionName = "ManagedSession",
            uint sessionTimeout = 60000,
            ArrayOf<string> preferredLocales = default,
            bool checkDomain = false,
            ISubscriptionEngineFactory? engineFactory = null,
            bool transferSubscriptionsOnRecreate = false,
            bool poolNotifications = false,
            IClientIdentityProvider? identityProvider = null,
            TimeProvider? timeProvider = null,
            IClientChannelManager? channelManager = null,
            CancellationToken ct = default)
        {
            telemetry ??= sessionFactory.Telemetry;
            ILogger<ManagedSession> logger = telemetry.CreateLogger<ManagedSession>();

            // Default the engine factory to V2 so callers get the new
            // ISubscriptionManager API by default. If the inner session
            // factory is a DefaultSessionFactory and no engine factory is
            // already configured on it, propagate this choice so the inner
            // Session is constructed with the V2 engine.
            engineFactory ??= timeProvider == null
                ? DefaultSubscriptionEngineFactory.Instance
                : new DefaultSubscriptionEngineFactory(timeProvider);
            if (sessionFactory is DefaultSessionFactory dsf &&
                dsf.SubscriptionEngineFactory is null)
            {
                sessionFactory = new DefaultSessionFactory(dsf.Telemetry)
                {
                    ReturnDiagnostics = dsf.ReturnDiagnostics,
                    SubscriptionEngineFactory = engineFactory,
                    TimeProvider = timeProvider ?? dsf.TimeProvider
                };
            }

            var managed = new ManagedSession(
                configuration,
                endpoint,
                sessionFactory,
                reconnectPolicy ?? new ReconnectPolicy(),
                redundancyHandler,
                logger,
                identity,
                identityProvider,
                timeProvider,
                preferredLocales,
                sessionName,
                sessionTimeout,
                checkDomain,
                transferSubscriptionsOnRecreate,
                poolNotifications,
                channelManager)
            {
                m_engineFactory = engineFactory
            };

            managed.StateMachine.Start();
            managed.StateMachine.RequestConnect();

            await managed.StateMachine
                .WaitForConnectedAsync(ct)
                .ConfigureAwait(false);

            return managed;
        }

        /// <summary>
        /// Gets the inner unmanaged session.
        /// Throws if no session is available.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal Session InnerSession => m_session ??
            throw new ServiceResultException(
                StatusCodes.BadNotConnected,
                "The managed session is not connected.");

        /// <summary>
        /// Gets the connection state machine.
        /// </summary>
        internal ConnectionStateMachine StateMachine { get; }

        /// <summary>
        /// Waits until a proactive identity refresh attempt has completed.
        /// </summary>
        internal async Task EnsureRefreshAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            Task? task;
            long version;
            lock (m_identityRefreshLock)
            {
                if (m_identityRefreshCompletedVersion > m_identityRefreshObservedVersion)
                {
                    m_identityRefreshObservedVersion = m_identityRefreshCompletedVersion;
                    return;
                }

                task = m_identityRefreshAttemptCompletion?.Task;
                version = m_identityRefreshAttemptVersion;
            }

            if (task == null)
            {
                return;
            }

            await AwaitWithCancellationAsync(task, ct).ConfigureAwait(false);
            MarkIdentityRefreshObserved(version);
        }

        /// <inheritdoc/>
        public ISessionFactory SessionFactory { get; }

        /// <inheritdoc/>
        public ConfiguredEndpoint ConfiguredEndpoint { get; }

        /// <inheritdoc/>
        public string SessionName
            => m_session?.SessionName ?? m_sessionName;

        /// <inheritdoc/>
        public double SessionTimeout
            => m_session?.SessionTimeout ?? m_sessionTimeout;

        /// <inheritdoc/>
        public object? Handle
        {
            get => m_session?.Handle;
            set => m_session?.Handle = value;
        }

        /// <inheritdoc/>
        public IUserIdentity Identity
            => m_session?.Identity ?? m_identity!;

        /// <inheritdoc/>
        public IEnumerable<IUserIdentity> IdentityHistory
            => m_session?.IdentityHistory ?? [];

        /// <inheritdoc/>
        public NamespaceTable NamespaceUris
            => InnerSession.NamespaceUris;

        /// <inheritdoc/>
        public StringTable ServerUris
            => InnerSession.ServerUris;

        /// <inheritdoc/>
        public ISystemContext SystemContext
            => InnerSession.SystemContext;

        /// <inheritdoc/>
        public IEncodeableFactory Factory
            => InnerSession.Factory;

        /// <inheritdoc/>
        public ITypeTable TypeTree
            => InnerSession.TypeTree;

        /// <inheritdoc/>
        public INodeCache NodeCache
            => InnerSession.NodeCache;

        /// <inheritdoc/>
        public IFilterContext FilterContext
            => InnerSession.FilterContext;

        /// <inheritdoc/>
        public ArrayOf<string> PreferredLocales
            => m_session?.PreferredLocales ?? m_preferredLocales;

        /// <inheritdoc/>
        public IEnumerable<Subscription> Subscriptions
            => m_session?.Subscriptions ?? [];

        /// <summary>
        /// The new options-based <see cref="Subscriptions.ISubscriptionManager"/>.
        /// Available when the underlying session was created with the V2
        /// subscription engine (the default for <see cref="ManagedSession"/>).
        /// </summary>
        /// <exception cref="InvalidOperationException">when the session
        /// is using the classic engine.</exception>
        public Subscriptions.ISubscriptionManager SubscriptionManager
        {
            get
            {
                if (InnerSession.SubscriptionEngine is DefaultSubscriptionEngine v2)
                {
                    return v2.SubscriptionManager;
                }
                throw new InvalidOperationException(
                    "ManagedSession.SubscriptionManager requires the V2 subscription engine. " +
                    "The session is using the classic engine; use Subscriptions/AddSubscription " +
                    "for the legacy API or recreate the ManagedSession with the V2 engine factory.");
            }
        }

        /// <inheritdoc/>
        public int SubscriptionCount
            => m_session?.SubscriptionCount ?? 0;

        /// <inheritdoc/>
        public bool DeleteSubscriptionsOnClose
        {
            get => m_session?.DeleteSubscriptionsOnClose ?? true;
            set => m_session?.DeleteSubscriptionsOnClose = value;
        }

        /// <inheritdoc/>
        public int PublishRequestCancelDelayOnCloseSession
        {
            get => m_session?.PublishRequestCancelDelayOnCloseSession ?? 5000;
            set => m_session?.PublishRequestCancelDelayOnCloseSession = value;
        }

        /// <inheritdoc/>
        public Subscription DefaultSubscription
        {
            get => InnerSession.DefaultSubscription;
            set => InnerSession.DefaultSubscription = value;
        }

        /// <inheritdoc/>
        public int KeepAliveInterval
        {
            get => m_session?.KeepAliveInterval ?? 5000;
            set => m_session?.KeepAliveInterval = value;
        }

        /// <inheritdoc/>
        public bool KeepAliveStopped
            => m_session?.KeepAliveStopped ?? true;

        /// <inheritdoc/>
        public DateTime LastKeepAliveTime
            => m_session?.LastKeepAliveTime ?? DateTime.MinValue;

        /// <inheritdoc/>
        public long LastKeepAliveTimestamp
            => m_session?.LastKeepAliveTimestamp ?? 0L;

        /// <inheritdoc/>
        public int OutstandingRequestCount
            => m_session?.OutstandingRequestCount ?? 0;

        /// <inheritdoc/>
        public int DefunctRequestCount
            => m_session?.DefunctRequestCount ?? 0;

        /// <inheritdoc/>
        public int GoodPublishRequestCount
            => m_session?.GoodPublishRequestCount ?? 0;

        /// <inheritdoc/>
        public int MinPublishRequestCount
        {
            get => m_session?.MinPublishRequestCount ?? 1;
            set => m_session?.MinPublishRequestCount = value;
        }

        /// <inheritdoc/>
        public int MaxPublishRequestCount
        {
            get => m_session?.MaxPublishRequestCount ?? 20;
            set => m_session?.MaxPublishRequestCount = value;
        }

        /// <inheritdoc/>
        public bool Reconnecting
            => StateMachine.State is ConnectionState.Reconnecting or ConnectionState.Failover;

        /// <inheritdoc/>
        public OperationLimits OperationLimits
            => InnerSession.OperationLimits;

        /// <inheritdoc/>
        public ServerCapabilities ServerCapabilities
            => InnerSession.ServerCapabilities;

        /// <inheritdoc/>
        public bool TransferSubscriptionsOnReconnect
        {
            get => m_session?.TransferSubscriptionsOnReconnect ?? false;
            set => m_session?.TransferSubscriptionsOnReconnect = value;
        }

        /// <inheritdoc/>
        public bool CheckDomain
            => m_session?.CheckDomain ?? m_checkDomain;

        /// <inheritdoc/>
        public ContinuationPointPolicy ContinuationPointPolicy
        {
            get => m_session?.ContinuationPointPolicy ?? ContinuationPointPolicy.Default;
            set => m_session?.ContinuationPointPolicy = value;
        }

        /// <inheritdoc/>
        public NodeId SessionId
            => m_session?.SessionId ?? NodeId.Null;

        /// <inheritdoc/>
        public bool Connected
            => m_session?.Connected ?? false;

        /// <inheritdoc/>
        public ClientTraceFlags ActivityTraceFlags
        {
            get => m_session?.ActivityTraceFlags ?? ClientTraceFlags.None;
            set => m_session?.ActivityTraceFlags = value;
        }

        /// <inheritdoc/>
        public EndpointDescription Endpoint
            => m_session?.Endpoint
                ?? ConfiguredEndpoint.Description;

        /// <inheritdoc/>
        public EndpointConfiguration EndpointConfiguration
            => m_session?.EndpointConfiguration
                ?? ConfiguredEndpoint.Configuration!;

        /// <inheritdoc/>
        public IServiceMessageContext MessageContext
            => InnerSession.MessageContext!;

        /// <inheritdoc/>
        public ITransportChannel NullableTransportChannel
            => InnerSession.NullableTransportChannel!;

        /// <inheritdoc/>
        public ITransportChannel TransportChannel
            => InnerSession.TransportChannel;

        /// <inheritdoc/>
        public DiagnosticsMasks ReturnDiagnostics
        {
            get => m_session?.ReturnDiagnostics ?? DiagnosticsMasks.None;
            set => m_session?.ReturnDiagnostics = value;
        }

        /// <inheritdoc/>
        public int OperationTimeout
        {
            get => m_session?.OperationTimeout ?? 0;
            set => m_session?.OperationTimeout = value;
        }

        /// <inheritdoc/>
        public int DefaultTimeoutHint
        {
            get => m_session?.DefaultTimeoutHint ?? 0;
            set => m_session?.DefaultTimeoutHint = value;
        }

        /// <inheritdoc/>
        public bool Disposed => m_disposed != 0;

        /// <inheritdoc/>
        public event KeepAliveEventHandler KeepAlive
        {
            add => m_keepAlive += value;
            remove => m_keepAlive -= value;
        }

        /// <inheritdoc/>
        public event NotificationEventHandler Notification
        {
            add => m_notification += value;
            remove => m_notification -= value;
        }

        /// <inheritdoc/>
        public event PublishErrorEventHandler PublishError
        {
            add => m_publishError += value;
            remove => m_publishError -= value;
        }

        /// <inheritdoc/>
        public event PublishSequenceNumbersToAcknowledgeEventHandler
            PublishSequenceNumbersToAcknowledge
        {
            add => m_publishSequenceNumbersToAcknowledge += value;
            remove => m_publishSequenceNumbersToAcknowledge -= value;
        }

        /// <inheritdoc/>
        public event EventHandler SubscriptionsChanged
        {
            add => m_subscriptionsChanged += value;
            remove => m_subscriptionsChanged -= value;
        }

        /// <inheritdoc/>
        public event EventHandler SessionClosing
        {
            add => m_sessionClosing += value;
            remove => m_sessionClosing -= value;
        }

        /// <inheritdoc/>
        public event EventHandler SessionConfigurationChanged
        {
            add => m_sessionConfigurationChanged += value;
            remove => m_sessionConfigurationChanged -= value;
        }

        /// <inheritdoc/>
        public event RenewUserIdentityEventHandler RenewUserIdentity
        {
            add => m_renewUserIdentity += value;
            remove => m_renewUserIdentity -= value;
        }

        /// <summary>
        /// Raised when the outer connection state changes. Subscribe to
        /// <see cref="ChannelStateChanged"/> for underlying channel-manager
        /// state transitions that do not change the outer state.
        /// </summary>
        public event EventHandler<ConnectionStateChangedEventArgs>?
            ConnectionStateChanged;

        /// <summary>
        /// Raised when the underlying managed transport channel state changes.
        /// This event is only raised when the inner session uses an
        /// <see cref="IManagedTransportChannel"/> supplied by a channel manager.
        /// </summary>
        public event Action<ManagedSession, ChannelStateChange>? ChannelStateChanged;

        /// <inheritdoc/>
        public async Task ReconnectAsync(
            ITransportWaitingConnection? connection,
            ITransportChannel? channel,
            CancellationToken ct = default)
        {
            using (await m_serviceLock.ReaderLockAsync(ct)
                .ConfigureAwait(false))
            {
                await InnerSession.ReconnectAsync(
                    connection, channel, ct).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async Task ReloadInstanceCertificateAsync(
            CancellationToken ct = default)
        {
            using (await m_serviceLock.ReaderLockAsync(ct)
                .ConfigureAwait(false))
            {
                await InnerSession.ReloadInstanceCertificateAsync(ct)
                    .ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public void Save(
            Stream stream,
            IEnumerable<Subscription> subscriptions,
            IEnumerable<Type>? knownTypes = null)
        {
            InnerSession.Save(stream, subscriptions, knownTypes);
        }

        /// <inheritdoc/>
        public IEnumerable<Subscription> Load(
            Stream stream,
            bool transferSubscriptions = false,
            IEnumerable<Type>? knownTypes = null)
        {
            return InnerSession.Load(stream, transferSubscriptions, knownTypes);
        }

        /// <inheritdoc/>
        public SessionConfiguration SaveSessionConfiguration(
            Stream? stream = null)
        {
            return InnerSession.SaveSessionConfiguration(stream);
        }

        /// <inheritdoc/>
        public bool ApplySessionConfiguration(
            SessionConfiguration sessionConfiguration)
        {
            return InnerSession.ApplySessionConfiguration(sessionConfiguration);
        }

        /// <inheritdoc/>
        public async Task FetchNamespaceTablesAsync(
            CancellationToken ct = default)
        {
            using (await m_serviceLock.ReaderLockAsync(ct)
                .ConfigureAwait(false))
            {
                await InnerSession.FetchNamespaceTablesAsync(ct)
                    .ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async Task FetchTypeTreeAsync(
            ExpandedNodeId typeId,
            CancellationToken ct = default)
        {
            using (await m_serviceLock.ReaderLockAsync(ct)
                .ConfigureAwait(false))
            {
                await InnerSession.FetchTypeTreeAsync(typeId, ct)
                    .ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async Task FetchTypeTreeAsync(
            ArrayOf<ExpandedNodeId> typeIds,
            CancellationToken ct = default)
        {
            using (await m_serviceLock.ReaderLockAsync(ct)
                .ConfigureAwait(false))
            {
                await InnerSession.FetchTypeTreeAsync(typeIds, ct)
                    .ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async Task OpenAsync(
            string sessionName,
            uint sessionTimeout,
            IUserIdentity identity,
            ArrayOf<string> preferredLocales,
            bool checkDomain,
            bool closeChannel,
            CancellationToken ct = default)
        {
            using (await m_serviceLock.ReaderLockAsync(ct)
                .ConfigureAwait(false))
            {
                await InnerSession.OpenAsync(
                    sessionName, sessionTimeout, identity,
                    preferredLocales, checkDomain, closeChannel, ct)
                    .ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async Task UpdateSessionAsync(
            IUserIdentity identity,
            ArrayOf<string> preferredLocales,
            CancellationToken ct = default)
        {
            using (await m_serviceLock.ReaderLockAsync(ct)
                .ConfigureAwait(false))
            {
                await InnerSession.UpdateSessionAsync(
                    identity, preferredLocales, ct)
                    .ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async Task ChangePreferredLocalesAsync(
            ArrayOf<string> preferredLocales,
            CancellationToken ct = default)
        {
            using (await m_serviceLock.ReaderLockAsync(ct)
                .ConfigureAwait(false))
            {
                await InnerSession.ChangePreferredLocalesAsync(
                    preferredLocales, ct).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async Task<StatusCode> CloseAsync(
            int timeout,
            bool closeChannel,
            CancellationToken ct = default)
        {
            StateMachine.RequestClose();

            await StateMachine.WaitForClosedAsync(ct).ConfigureAwait(false);

            return StatusCodes.Good;
        }

        /// <inheritdoc/>
        public bool AddSubscription(Subscription subscription)
        {
            return InnerSession.AddSubscription(subscription);
        }

        /// <inheritdoc/>
        public bool RemoveTransferredSubscription(
            Subscription subscription)
        {
            return InnerSession
                        .RemoveTransferredSubscription(subscription);
        }

        /// <inheritdoc/>
        public async Task<bool> RemoveSubscriptionAsync(
            Subscription subscription,
            CancellationToken ct = default)
        {
            using (await m_serviceLock.ReaderLockAsync(ct)
                .ConfigureAwait(false))
            {
                return await InnerSession.RemoveSubscriptionAsync(
                    subscription, ct).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async Task<bool> RemoveSubscriptionsAsync(
            IEnumerable<Subscription> subscriptions,
            CancellationToken ct = default)
        {
            using (await m_serviceLock.ReaderLockAsync(ct)
                .ConfigureAwait(false))
            {
                return await InnerSession.RemoveSubscriptionsAsync(
                    subscriptions, ct).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async Task<bool> ReactivateSubscriptionsAsync(
            SubscriptionCollection subscriptions,
            bool sendInitialValues,
            CancellationToken ct = default)
        {
            using (await m_serviceLock.ReaderLockAsync(ct)
                .ConfigureAwait(false))
            {
                return await InnerSession
                    .ReactivateSubscriptionsAsync(
                        subscriptions, sendInitialValues, ct)
                    .ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async Task<bool> TransferSubscriptionsAsync(
            SubscriptionCollection subscriptions,
            bool sendInitialValues,
            CancellationToken ct = default)
        {
            using (await m_serviceLock.ReaderLockAsync(ct)
                .ConfigureAwait(false))
            {
                return await InnerSession
                    .TransferSubscriptionsAsync(
                        subscriptions, sendInitialValues, ct)
                    .ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public bool BeginPublish(int timeout)
        {
            return InnerSession.BeginPublish(timeout);
        }

        /// <inheritdoc/>
        public void StartPublishing(int timeout, bool fullQueue)
        {
            InnerSession.StartPublishing(timeout, fullQueue);
        }

        /// <inheritdoc/>
        public async Task<(bool, ServiceResult)> RepublishAsync(
            uint subscriptionId,
            uint sequenceNumber,
            CancellationToken ct = default)
        {
            using (await m_serviceLock.ReaderLockAsync(ct)
                .ConfigureAwait(false))
            {
                return await InnerSession.RepublishAsync(
                    subscriptionId, sequenceNumber, ct)
                    .ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        [Obsolete("Channels are now managed centrally via IClientChannelManager. " +
            "Use ManagedSessionBuilder.WithChannelManager(...) or " +
            "Session.CreateAsync(IClientChannelManager, ...) instead of manual " +
            "AttachChannel/DetachChannel. This method remains functional for back-compat.")]
        public void AttachChannel(ITransportChannel channel)
        {
            InnerSession.AttachChannel(channel);
        }

        /// <inheritdoc/>
        [Obsolete("Channels are now managed centrally via IClientChannelManager. " +
            "Use ManagedSessionBuilder.WithChannelManager(...) or " +
            "Session.CreateAsync(IClientChannelManager, ...) instead of manual " +
            "AttachChannel/DetachChannel. This method remains functional for back-compat.")]
        public void DetachChannel()
        {
            InnerSession.DetachChannel();
        }

        /// <inheritdoc/>
        public async Task<StatusCode> CloseAsync(
            CancellationToken ct = default)
        {
            StateMachine.RequestClose();
            Session? session = m_session;
            if (session != null)
            {
                return await session.CloseAsync(ct)
                    .ConfigureAwait(false);
            }

            return StatusCodes.Good;
        }

        /// <inheritdoc/>
        public uint NewRequestHandle()
        {
            return InnerSession.NewRequestHandle();
        }

        /// <summary>
        /// Refreshes the user identity on the connected inner session.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="provider"/> is <c>null</c>.</exception>
        public async ValueTask UpdateIdentityAsync(
            IClientIdentityProvider provider,
            CancellationToken ct = default)
        {
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }

            using (await m_serviceLock.WriterLockAsync(ct)
                .ConfigureAwait(false))
            {
                await InnerSession
                    .UpdateIdentityAsync(provider, ct)
                    .ConfigureAwait(false);
            }
        }

        private void WireStateMachineCallbacks()
        {
            StateMachine.ConnectAsync = HandleConnectAsync;
            StateMachine.ReconnectWithBudgetAsync = HandleReconnectAsync;
            StateMachine.FailoverWithBudgetAsync = HandleFailoverAsync;
            StateMachine.CloseSessionAsync = HandleCloseSessionAsync;
            StateMachine.StateChanged += OnStateChanged;
        }

        private async Task<ServiceResult> HandleConnectAsync(
            CancellationToken ct)
        {
            try
            {
                m_logger.LogInformation(
                    "ManagedSession: Connecting to {Endpoint}.",
                    ConfiguredEndpoint.EndpointUrl);

                Session session;
                if (m_channelManager != null)
                {
                    // Channel-manager-aware path: acquire a shared
                    // managed channel and let the manager drive any
                    // future reconnect transparently. Other sessions
                    // sharing this endpoint join the same channel.
                    session = await Session.CreateAsync(
                        m_channelManager,
                        m_configuration,
                        ConfiguredEndpoint,
                        updateBeforeConnect: true,
                        m_checkDomain,
                        m_sessionName,
                        m_sessionTimeout,
                        m_identityProvider == null ? m_identity : null,
                        m_preferredLocales,
                        m_engineFactory,
                        m_timeProvider,
                        ct).ConfigureAwait(false);
                }
                else
                {
                    session = (Session)await SessionFactory.CreateAsync(
                        m_configuration,
                        ConfiguredEndpoint,
                        updateBeforeConnect: true,
                        m_checkDomain,
                        m_sessionName,
                        m_sessionTimeout,
                        m_identityProvider == null ? m_identity : null,
                        m_preferredLocales,
                        ct).ConfigureAwait(false);
                }

                WireSessionEvents(session);
                m_session = session;

                if (m_identityProvider != null)
                {
                    using (await m_serviceLock.WriterLockAsync(ct)
                        .ConfigureAwait(false))
                    {
                        await session
                            .UpdateIdentityAsync(m_identityProvider, ct)
                            .ConfigureAwait(false);
                    }
                    StartIdentityRefreshLoop();
                }

                // Apply opt-in V2 transfer-on-recreate. The V2 engine
                // and SubscriptionManager survive in-place re-creates
                // (failover via Session.RecreateInPlaceAsync), so this
                // setting persists for the entire session lifetime
                // once applied here. No-op when the classic engine is
                // in use.
                if ((m_transferSubscriptionsOnRecreate || m_poolNotifications) &&
                    session.SubscriptionEngine
                        is DefaultSubscriptionEngine v2 &&
                    v2.SubscriptionManager
                        is Subscriptions.SubscriptionManager v2Manager)
                {
                    if (m_transferSubscriptionsOnRecreate)
                    {
                        v2Manager.TransferSubscriptionsOnRecreate = true;
                    }
                    if (m_poolNotifications)
                    {
                        v2Manager.PoolNotifications = true;
                    }
                }

                if (m_redundancyHandler != null)
                {
                    m_redundancyInfo = await m_redundancyHandler
                        .FetchRedundancyInfoAsync(this, ct)
                        .ConfigureAwait(false);
                }

                m_logger.LogInformation(
                    "ManagedSession: Connected, SessionId={SessionId}.",
                    session.SessionId);

                return ServiceResult.Good;
            }
            catch (Exception ex)
            {
                m_logger.LogError(
                    ex,
                    "ManagedSession: Connect failed.");
                return new ServiceResult(ex);
            }
        }

        private async Task<ServiceResult> HandleReconnectAsync(
            IRetryBudget budget,
            CancellationToken ct)
        {
            try
            {
                m_logger.LogInformation(
                    "ManagedSession: Reconnecting.");

                using (await m_serviceLock.WriterLockAsync(ct)
                    .ConfigureAwait(false))
                {
                    Session? session = m_session;
                    if (session != null)
                    {
                        try
                        {
                            await session.ReconnectAsync(
                                    budget,
                                    ct)
                                .ConfigureAwait(false);
                        }
                        catch (ServiceResultException sre) when (
                            sre.StatusCode == StatusCodes.BadSecureChannelClosed &&
                            session.ManagedChannel?.State is ChannelState.Closed or ChannelState.Faulted)
                        {
                            m_logger.LogInformation(
                                sre,
                                "ManagedSession: managed channel is faulted; recreating session in place.");
                            await session.RecreateInPlaceAsync(
                                    endpoint: null,
                                    budget: budget,
                                    ct: ct)
                                .ConfigureAwait(false);
                        }
                    }
                }

                m_reconnectPolicy.Reset();

                m_logger.LogInformation(
                    "ManagedSession: Reconnected.");

                return ServiceResult.Good;
            }
            catch (Exception ex)
            {
                m_logger.LogWarning(
                    ex,
                    "ManagedSession: Reconnect attempt failed.");
                return new ServiceResult(ex);
            }
        }

        private async Task<ServiceResult> HandleFailoverAsync(
            IRetryBudget budget,
            CancellationToken ct)
        {
            if (m_redundancyHandler == null || m_redundancyInfo == null)
            {
                return new ServiceResult(
                    StatusCodes.BadNotSupported);
            }

            try
            {
                ConfiguredEndpoint? failoverEndpoint =
                    m_redundancyHandler.SelectFailoverTarget(
                        m_redundancyInfo, ConfiguredEndpoint);

                if (failoverEndpoint == null)
                {
                    return new ServiceResult(
                        StatusCodes.BadNothingToDo);
                }

                m_logger.LogInformation(
                    "ManagedSession: Failing over to {Endpoint}.",
                    failoverEndpoint.EndpointUrl);

                using (await m_serviceLock.WriterLockAsync(ct)
                    .ConfigureAwait(false))
                {
                    Session? session = m_session;
                    if (session == null)
                    {
                        return new ServiceResult(
                            StatusCodes.BadInvalidState);
                    }

                    // Recreate in place: the inner Session reference is
                    // preserved across failover so that the V2
                    // SubscriptionEngine, SubscriptionManager, and any
                    // external holders of InnerSession all continue to
                    // operate on the same Session object. Session
                    // internals re-run CreateSession+ActivateSession
                    // against the new endpoint and drive subscription
                    // recreate/transfer for both unamanged templates and
                    // the new engine.
                    await session
                        .RecreateInPlaceAsync(
                            failoverEndpoint,
                            budget,
                            ct)
                        .ConfigureAwait(false);
                }

                m_reconnectPolicy.Reset();

                m_logger.LogInformation(
                    "ManagedSession: Failover complete.");

                return ServiceResult.Good;
            }
            catch (Exception ex)
            {
                m_logger.LogError(
                    ex,
                    "ManagedSession: Failover failed.");
                return new ServiceResult(ex);
            }
        }

        private async Task HandleCloseSessionAsync(CancellationToken ct)
        {
            await StopIdentityRefreshLoopAsync().ConfigureAwait(false);

            Session? session = m_session;
            if (session != null)
            {
                m_session = null;
                UnwireSessionEvents(session);

                try
                {
                    await session
                        .CloseAsync(ct)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    m_logger.LogDebug(
                        ex,
                        "ManagedSession: Session close failed.");
                }

                session.Dispose();
            }
        }

        private void WireSessionEvents(Session session)
        {
            session.KeepAlive += OnInnerKeepAlive;
            session.Notification += OnInnerNotification;
            session.PublishError += OnInnerPublishError;
            session.PublishSequenceNumbersToAcknowledge +=
                OnInnerPublishSequenceNumbers;
            session.SubscriptionsChanged +=
                OnInnerSubscriptionsChanged;
            session.SessionClosing += OnInnerSessionClosing;
            session.SessionConfigurationChanged +=
                OnInnerSessionConfigurationChanged;
            session.RenewUserIdentity +=
                OnInnerRenewUserIdentity;
            session.ManagedChannel?.StateChanged
                    += OnManagedChannelStateChanged;
        }

        private void UnwireSessionEvents(Session session)
        {
            session.KeepAlive -= OnInnerKeepAlive;
            session.Notification -= OnInnerNotification;
            session.PublishError -= OnInnerPublishError;
            session.PublishSequenceNumbersToAcknowledge -=
                OnInnerPublishSequenceNumbers;
            session.SubscriptionsChanged -=
                OnInnerSubscriptionsChanged;
            session.SessionClosing -= OnInnerSessionClosing;
            session.SessionConfigurationChanged -=
                OnInnerSessionConfigurationChanged;
            session.RenewUserIdentity -=
                OnInnerRenewUserIdentity;
            session.ManagedChannel?.StateChanged
                    -= OnManagedChannelStateChanged;
        }

        private void OnInnerKeepAlive(ISession session, KeepAliveEventArgs e)
        {
            if (e.Status != null &&
                ServiceResult.IsBad(e.Status))
            {
                // When the channel manager is wired AND it already
                // reports the channel as not Ready (TransportReconnecting
                // or TransportConnectedSessionReactivating), it is
                // already handling the reconnect. Suppress the outer
                // state-machine churn — the manager will drive the
                // inner Session reactivation transparently via
                // OnReconnectAsync. The outer state machine only takes
                // over when the channel manager terminally faults the
                // channel.
                if (Volatile.Read(ref m_channelReconnectInProgress) > 0)
                {
                    m_logger.LogDebug(
                        "ManagedSession: keep-alive failure suppressed " +
                        "while channel manager reconnect is in progress.");
                }
                else
                {
                    StateMachine.TriggerReconnect();
                }
            }

            m_keepAlive?.Invoke(this, e);
        }

        private void OnManagedChannelStateChanged(
            IManagedTransportChannel channel,
            ChannelStateChange change)
        {
            RaiseChannelStateChanged(change);

            // Track whether the manager is in the middle of a
            // reconnect cycle. When entering reconnect, we want to
            // suppress OnInnerKeepAlive-triggered outer reconnects so
            // both layers don't race. When the manager terminally
            // faults the channel, surface that to the outer state
            // machine so the higher-level retry policy can take over.
            switch (change.NewState)
            {
                case ChannelState.TransportReconnecting:
                case ChannelState.TransportConnectedSessionReactivating:
                    Interlocked.Increment(ref m_channelReconnectInProgress);
                    break;
                case ChannelState.Ready:
                    Interlocked.Exchange(ref m_channelReconnectInProgress, 0);
                    break;
                case ChannelState.Faulted:
                    Interlocked.Exchange(ref m_channelReconnectInProgress, 0);
                    // Channel-mgr gave up. Surface to outer state
                    // machine so the IReconnectPolicy / failover path
                    // can run.
                    StateMachine.TriggerReconnect(change);
                    break;
                case ChannelState.Closed:
                    Interlocked.Exchange(ref m_channelReconnectInProgress, 0);
                    // Channel-mgr closed. Surface to outer state
                    // machine so the IReconnectPolicy / failover path
                    // can run.
                    StateMachine.TriggerReconnect();
                    break;
                default:
                    break;
            }
        }

        private void RaiseChannelStateChanged(ChannelStateChange change)
        {
            try
            {
                ChannelStateChanged?.Invoke(this, change);
            }
            catch (Exception ex)
            {
                m_logger.LogError(
                    ex,
                    "ManagedSession: ChannelStateChanged handler threw an exception.");
            }
        }

        private void OnInnerNotification(ISession session, NotificationEventArgs e)
        {
            m_notification?.Invoke(this, e);
        }

        private void OnInnerPublishError(ISession session, PublishErrorEventArgs e)
        {
            m_publishError?.Invoke(this, e);
        }

        private void OnInnerPublishSequenceNumbers(
            ISession session,
            PublishSequenceNumbersToAcknowledgeEventArgs e)
        {
            m_publishSequenceNumbersToAcknowledge?.Invoke(this, e);
        }

        private void OnInnerSubscriptionsChanged(object? sender, EventArgs e)
        {
            m_subscriptionsChanged?.Invoke(this, e);
        }

        private void OnInnerSessionClosing(object? sender, EventArgs e)
        {
            m_sessionClosing?.Invoke(this, e);
        }

        private void OnInnerSessionConfigurationChanged(object? sender, EventArgs e)
        {
            m_sessionConfigurationChanged?.Invoke(this, e);
        }

        private IUserIdentity OnInnerRenewUserIdentity(
            ISession session,
            IUserIdentity identity)
        {
            return m_renewUserIdentity?.Invoke(this, identity) ?? identity;
        }

        private void OnStateChanged(object? sender, ConnectionStateChangedEventArgs e)
        {
            ConnectionStateChanged?.Invoke(this, e);
        }

        private void StartIdentityRefreshLoop()
        {
            if (m_identityProvider == null)
            {
                return;
            }

            var cts = new CancellationTokenSource();
            Task task = RunIdentityRefreshLoopAsync(m_identityProvider, cts.Token);

            CancellationTokenSource? previousCts;
            lock (m_identityRefreshLock)
            {
                previousCts = m_identityRefreshCancellation;
                m_identityRefreshCancellation = cts;
                m_identityRefreshTask = task;
            }
            previousCts?.Cancel();
        }

        private async Task RunIdentityRefreshLoopAsync(
            IClientIdentityProvider provider,
            CancellationToken ct)
        {
            int retryAttempt = 0;
            TaskCompletionSource<object?>? attemptCompletion = null;
            long attemptVersion = 0;
            while (!ct.IsCancellationRequested)
            {
                attemptCompletion ??= BeginIdentityRefreshAttempt(out attemptVersion);

                TaskCompletionSource<object?> currentCompletion = attemptCompletion;
                long currentVersion = attemptVersion;
                TimeSpan delay = GetIdentityRefreshDelay(provider.ExpiresAt, retryAttempt == 0);
                try
                {
                    await DelayAsync(delay, ct).ConfigureAwait(false);
                    await RefreshIdentityOnceAsync(provider, ct).ConfigureAwait(false);
                    retryAttempt = 0;
                    CompleteIdentityRefreshAttempt(currentCompletion, currentVersion);
                    attemptCompletion = null;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    currentCompletion.TrySetCanceled(ct);
                    break;
                }
                catch (Exception ex)
                {
                    retryAttempt++;
                    m_logger.LogWarning(
                        ex,
                        "ManagedSession: proactive identity refresh failed; retrying.");
                    TimeSpan backoff = GetIdentityRefreshBackoff(retryAttempt);
                    Task backoffTask = DelayAsync(backoff, ct);
                    attemptCompletion = BeginIdentityRefreshAttempt(out attemptVersion);
                    CompleteIdentityRefreshAttempt(currentCompletion, currentVersion);
                    try
                    {
                        await backoffTask.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        attemptCompletion.TrySetCanceled(ct);
                        break;
                    }
                }
            }
        }

        private TaskCompletionSource<object?> BeginIdentityRefreshAttempt(out long version)
        {
            var completion = new TaskCompletionSource<object?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            lock (m_identityRefreshLock)
            {
                version = ++m_identityRefreshAttemptVersion;
                m_identityRefreshAttemptCompletion = completion;
            }
            return completion;
        }

        private void CompleteIdentityRefreshAttempt(
            TaskCompletionSource<object?> completion,
            long version)
        {
            lock (m_identityRefreshLock)
            {
                if (m_identityRefreshCompletedVersion < version)
                {
                    m_identityRefreshCompletedVersion = version;
                }
            }
            completion.TrySetResult(null);
        }

        private void MarkIdentityRefreshObserved(long version)
        {
            lock (m_identityRefreshLock)
            {
                if (m_identityRefreshObservedVersion < version)
                {
                    m_identityRefreshObservedVersion = version;
                }
            }
        }

        private static async Task AwaitWithCancellationAsync(Task task, CancellationToken ct)
        {
            if (task.IsCompleted || !ct.CanBeCanceled)
            {
                await task.ConfigureAwait(false);
                return;
            }

            var cancellation = new TaskCompletionSource<object?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            using (ct.Register(
                static state => ((TaskCompletionSource<object?>)state!).TrySetCanceled(),
                cancellation))
            {
                Task completed = await Task.WhenAny(task, cancellation.Task).ConfigureAwait(false);
                await completed.ConfigureAwait(false);
            }
        }

        private async Task DelayAsync(TimeSpan delay, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (delay == TimeSpan.Zero)
            {
                return;
            }

            var delayState = new DelayState();
            delayState.Timer = m_timeProvider.CreateTimer(
                static state => ((DelayState)state!).Complete(),
                delayState,
                delay,
                Timeout.InfiniteTimeSpan);
            delayState.Cancellation = ct.Register(
                static state => ((DelayState)state!).Cancel(),
                delayState);
            try
            {
                await delayState.Task.ConfigureAwait(false);
            }
            finally
            {
                delayState.Dispose();
            }
        }

        private TimeSpan GetIdentityRefreshDelay(DateTime expiresAt, bool allowInfinite)
        {
            if (expiresAt == DateTime.MaxValue)
            {
                return allowInfinite ? Timeout.InfiniteTimeSpan : TimeSpan.FromMinutes(5);
            }

            DateTimeOffset now = m_timeProvider.GetUtcNow();
            DateTimeOffset expires = expiresAt.Kind == DateTimeKind.Local
                ? new DateTimeOffset(expiresAt).ToUniversalTime()
                : new DateTimeOffset(DateTime.SpecifyKind(expiresAt, DateTimeKind.Utc));
            TimeSpan delay = expires - now - IdentityRefreshSafetyMargin;
            if (delay <= TimeSpan.Zero)
            {
                return allowInfinite ? TimeSpan.FromSeconds(1) : TimeSpan.Zero;
            }
            return delay;
        }

        private static TimeSpan GetIdentityRefreshBackoff(int retryAttempt)
        {
            int cappedAttempt = Math.Min(retryAttempt, 5);
            double seconds = Math.Min(5 * Math.Pow(2, cappedAttempt - 1), 60);
            return TimeSpan.FromSeconds(seconds);
        }

        private async Task RefreshIdentityOnceAsync(
            IClientIdentityProvider provider,
            CancellationToken ct)
        {
            using (await m_serviceLock.WriterLockAsync(ct)
                .ConfigureAwait(false))
            {
                Session? session = m_session;
                if (session == null)
                {
                    return;
                }

                await session.UpdateIdentityAsync(provider, ct).ConfigureAwait(false);
            }
        }

        private async Task StopIdentityRefreshLoopAsync()
        {
            CancellationTokenSource? cts;
            Task? task;
            lock (m_identityRefreshLock)
            {
                cts = m_identityRefreshCancellation;
                task = m_identityRefreshTask;
                m_identityRefreshCancellation = null;
                m_identityRefreshTask = null;
            }

            if (cts == null)
            {
                return;
            }

            try
            {
                cts.Cancel();
                if (task != null)
                {
                    await task.ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                cts.Dispose();
            }
        }

        private void CancelIdentityRefreshLoop()
        {
            CancellationTokenSource? cts;
            lock (m_identityRefreshLock)
            {
                cts = m_identityRefreshCancellation;
                m_identityRefreshCancellation = null;
                m_identityRefreshTask = null;
            }
            cts?.Cancel();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Releases managed and unmanaged resources.
        /// </summary>
        /// <param name="disposing">True if called from
        /// <see cref="Dispose()"/>.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (Interlocked.Exchange(ref m_disposed, 1) != 0)
            {
                return;
            }

            if (disposing)
            {
                CancelIdentityRefreshLoop();
                UnsubscribeCertificateChanges();
                StateMachine.RequestClose();

                Session? session = m_session;
                m_session = null;

                if (session != null)
                {
                    UnwireSessionEvents(session);
                    session.Dispose();
                }

                m_serviceLock.Dispose();
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref m_disposed, 1) != 0)
            {
                return;
            }

            await StopIdentityRefreshLoopAsync().ConfigureAwait(false);
            UnsubscribeCertificateChanges();
            await StopRevalidationLoopAsync().ConfigureAwait(false);

            // Tear down streaming subscription and model change tracker
            // before closing the session so any in-flight publish work
            // completes against a still-valid session.
            await DisposeStreamingAsync().ConfigureAwait(false);

            await StateMachine.DisposeAsync()
                .ConfigureAwait(false);

            Session? session = m_session;
            m_session = null;

            if (session != null)
            {
                UnwireSessionEvents(session);
                try
                {
                    await session.CloseAsync(default)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    m_logger.LogDebug(
                        ex,
                        "ManagedSession: Dispose close failed.");
                }

                session.Dispose();
            }

            GC.SuppressFinalize(this);
        }

        private sealed class DelayState : IDisposable
        {
            public Task Task => m_completion.Task;

            public ITimer? Timer { get; set; }

            public CancellationTokenRegistration Cancellation { get; set; }

            public void Complete()
            {
                m_completion.TrySetResult(null);
            }

            public void Cancel()
            {
                m_completion.TrySetCanceled();
            }

            public void Dispose()
            {
                Cancellation.Dispose();
                Timer?.Dispose();
            }

            private readonly TaskCompletionSource<object?> m_completion = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private KeepAliveEventHandler? m_keepAlive;
        private NotificationEventHandler? m_notification;
        private PublishErrorEventHandler? m_publishError;
        private PublishSequenceNumbersToAcknowledgeEventHandler? m_publishSequenceNumbersToAcknowledge;
        private EventHandler? m_subscriptionsChanged;
        private EventHandler? m_sessionClosing;
        private EventHandler? m_sessionConfigurationChanged;
        private RenewUserIdentityEventHandler? m_renewUserIdentity;
        private volatile Session? m_session;
        private readonly AsyncReaderWriterLock m_serviceLock = new();
        private readonly ApplicationConfiguration m_configuration;
        private readonly IReconnectPolicy m_reconnectPolicy;
        private readonly IServerRedundancyHandler? m_redundancyHandler;
        private static readonly TimeSpan IdentityRefreshSafetyMargin = TimeSpan.FromSeconds(60);

        private readonly ILogger m_logger;
        private readonly IUserIdentity? m_identity;
        private readonly IClientIdentityProvider? m_identityProvider;
        private readonly TimeProvider m_timeProvider;
        private readonly TimeSpan m_maxTotalReconnectTime;
        private readonly ArrayOf<string> m_preferredLocales;
        private readonly string m_sessionName;
        private readonly uint m_sessionTimeout;
        private readonly bool m_checkDomain;
        private readonly bool m_transferSubscriptionsOnRecreate;
        private readonly bool m_poolNotifications;
        private readonly IClientChannelManager? m_channelManager;
        private ISubscriptionEngineFactory? m_engineFactory;
        private int m_channelReconnectInProgress;
        private ServerRedundancyInfo? m_redundancyInfo;
        private readonly Lock m_identityRefreshLock = new();
#pragma warning disable CA2213
        // Disposed by StopIdentityRefreshLoopAsync; sync Dispose cancels because it cannot await.
        // TODO: move ManagedSession to async-only disposal.
        private CancellationTokenSource? m_identityRefreshCancellation;
#pragma warning restore CA2213
        private Task? m_identityRefreshTask;
        private TaskCompletionSource<object?>? m_identityRefreshAttemptCompletion;
        private long m_identityRefreshAttemptVersion;
        private long m_identityRefreshCompletedVersion;
        private long m_identityRefreshObservedVersion;
        private int m_disposed;
    }
}
