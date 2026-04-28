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
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
using System.Runtime.CompilerServices;
#endif
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;
using Opc.Ua.Client.Sessions;

namespace Opc.Ua.Client
{
    /// <summary>
    /// A managed session that wraps a V1 <see cref="Session"/> and
    /// automatically handles connection lifecycle, reconnection with
    /// configurable policy, and server redundancy failover.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Service calls are gated during reconnect — callers transparently
    /// wait until the session is reconnected. The gating uses a
    /// <see cref="AsyncReaderWriterLock"/>: connected service calls
    /// take a reader lock (cheap, concurrent), while reconnect holds
    /// the writer lock exclusively.
    /// </para>
    /// <para>
    /// This class uses composition, not inheritance, to wrap the V1
    /// <see cref="Session"/>. All <see cref="ISession"/> members are
    /// delegated to the inner session.
    /// </para>
    /// </remarks>
    public partial class ManagedSession : ISession, IAsyncDisposable
    {

        private volatile Session? m_session;
        private readonly ConnectionStateMachine m_stateMachine;
        private readonly AsyncReaderWriterLock m_serviceLock = new();
        private readonly ApplicationConfiguration m_configuration;
        private readonly ConfiguredEndpoint m_endpoint;
        private readonly IReconnectPolicy m_reconnectPolicy;
        private readonly IServerRedundancyHandler? m_redundancyHandler;
        private readonly ISessionFactory m_sessionFactory;
        private readonly ILogger m_logger;
        private IUserIdentity? m_identity;
        private ArrayOf<string> m_preferredLocales;
        private string m_sessionName;
        private uint m_sessionTimeout;
        private bool m_checkDomain;
        private ServerRedundancyInfo? m_redundancyInfo;
        private int m_disposed;

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
            ArrayOf<string> preferredLocales,
            string sessionName,
            uint sessionTimeout,
            bool checkDomain)
        {
            m_configuration = configuration
                ?? throw new ArgumentNullException(nameof(configuration));
            m_endpoint = endpoint
                ?? throw new ArgumentNullException(nameof(endpoint));
            m_sessionFactory = sessionFactory
                ?? throw new ArgumentNullException(nameof(sessionFactory));
            m_reconnectPolicy = reconnectPolicy
                ?? throw new ArgumentNullException(nameof(reconnectPolicy));
            m_redundancyHandler = redundancyHandler;
            m_logger = logger
                ?? throw new ArgumentNullException(nameof(logger));
            m_identity = identity;
            m_preferredLocales = preferredLocales;
            m_sessionName = sessionName;
            m_sessionTimeout = sessionTimeout;
            m_checkDomain = checkDomain;

            m_stateMachine = new ConnectionStateMachine(
                reconnectPolicy, logger);

            WireStateMachineCallbacks();
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
            CancellationToken ct = default)
        {
            telemetry ??= sessionFactory.Telemetry;
            var logger = telemetry.CreateLogger<ManagedSession>();

            var managed = new ManagedSession(
                configuration,
                endpoint,
                sessionFactory,
                reconnectPolicy ?? new ReconnectPolicy(),
                redundancyHandler,
                logger,
                identity,
                preferredLocales,
                sessionName,
                sessionTimeout,
                checkDomain);

            managed.m_stateMachine.Start();
            managed.m_stateMachine.RequestConnect();

            await managed.m_stateMachine
                .WaitForConnectedAsync(ct)
                .ConfigureAwait(false);

            return managed;
        }

        /// <summary>
        /// Gets the inner V1 session. Throws if no session is available.
        /// </summary>
        internal Session InnerSession
        {
            get
            {
                var session = m_session;
                if (session == null)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadNotConnected,
                        "The managed session is not connected.");
                }

                return session;
            }
        }

        /// <summary>
        /// Gets the connection state machine.
        /// </summary>
        internal ConnectionStateMachine StateMachine => m_stateMachine;

        /// <inheritdoc/>
        public ISessionFactory SessionFactory => m_sessionFactory;

        /// <inheritdoc/>
        public ConfiguredEndpoint ConfiguredEndpoint => m_endpoint;

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
            set
            {
                if (m_session != null)
                {
                    m_session.Handle = value;
                }
            }
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

        /// <inheritdoc/>
        public int SubscriptionCount
            => m_session?.SubscriptionCount ?? 0;

        /// <inheritdoc/>
        public bool DeleteSubscriptionsOnClose
        {
            get => m_session?.DeleteSubscriptionsOnClose ?? true;
            set
            {
                if (m_session != null)
                {
                    m_session.DeleteSubscriptionsOnClose = value;
                }
            }
        }

        /// <inheritdoc/>
        public int PublishRequestCancelDelayOnCloseSession
        {
            get => m_session?.PublishRequestCancelDelayOnCloseSession
                ?? 5000;
            set
            {
                if (m_session != null)
                {
                    m_session.PublishRequestCancelDelayOnCloseSession
                        = value;
                }
            }
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
            set
            {
                if (m_session != null)
                {
                    m_session.KeepAliveInterval = value;
                }
            }
        }

        /// <inheritdoc/>
        public bool KeepAliveStopped
            => m_session?.KeepAliveStopped ?? true;

        /// <inheritdoc/>
        public DateTime LastKeepAliveTime
            => m_session?.LastKeepAliveTime ?? DateTime.MinValue;

        /// <inheritdoc/>
        public int LastKeepAliveTickCount
            => m_session?.LastKeepAliveTickCount ?? 0;

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
            set
            {
                if (m_session != null)
                {
                    m_session.MinPublishRequestCount = value;
                }
            }
        }

        /// <inheritdoc/>
        public int MaxPublishRequestCount
        {
            get => m_session?.MaxPublishRequestCount ?? 20;
            set
            {
                if (m_session != null)
                {
                    m_session.MaxPublishRequestCount = value;
                }
            }
        }

        /// <inheritdoc/>
        public bool Reconnecting
            => m_stateMachine.State is ConnectionState.Reconnecting
                or ConnectionState.Failover;

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
            set
            {
                if (m_session != null)
                {
                    m_session.TransferSubscriptionsOnReconnect = value;
                }
            }
        }

        /// <inheritdoc/>
        public bool CheckDomain
            => m_session?.CheckDomain ?? m_checkDomain;

        /// <inheritdoc/>
        public ContinuationPointPolicy ContinuationPointPolicy
        {
            get => m_session?.ContinuationPointPolicy
                ?? ContinuationPointPolicy.Default;
            set
            {
                if (m_session != null)
                {
                    m_session.ContinuationPointPolicy = value;
                }
            }
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
            get => m_session?.ActivityTraceFlags
                ?? ClientTraceFlags.None;
            set
            {
                if (m_session != null)
                {
                    m_session.ActivityTraceFlags = value;
                }
            }
        }

        /// <inheritdoc/>
        public EndpointDescription Endpoint
            => m_session?.Endpoint
                ?? m_endpoint.Description;

        /// <inheritdoc/>
        public EndpointConfiguration EndpointConfiguration
            => m_session?.EndpointConfiguration
                ?? m_endpoint.Configuration;

        /// <inheritdoc/>
        public IServiceMessageContext MessageContext
            => InnerSession.MessageContext!;

        /// <inheritdoc/>
        public ITransportChannel NullableTransportChannel
            => InnerSession.NullableTransportChannel!;

        /// <inheritdoc/>
        public ITransportChannel TransportChannel
            => InnerSession.TransportChannel!;

        /// <inheritdoc/>
        public DiagnosticsMasks ReturnDiagnostics
        {
            get => m_session?.ReturnDiagnostics
                ?? DiagnosticsMasks.None;
            set
            {
                if (m_session != null)
                {
                    m_session.ReturnDiagnostics = value;
                }
            }
        }

        /// <inheritdoc/>
        public int OperationTimeout
        {
            get => m_session?.OperationTimeout ?? 0;
            set
            {
                if (m_session != null)
                {
                    m_session.OperationTimeout = value;
                }
            }
        }

        /// <inheritdoc/>
        public int DefaultTimeoutHint
        {
            get => m_session?.DefaultTimeoutHint ?? 0;
            set
            {
                if (m_session != null)
                {
                    m_session.DefaultTimeoutHint = value;
                }
            }
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

        private KeepAliveEventHandler? m_keepAlive;
        private NotificationEventHandler? m_notification;
        private PublishErrorEventHandler? m_publishError;
        private PublishSequenceNumbersToAcknowledgeEventHandler?
            m_publishSequenceNumbersToAcknowledge;
        private EventHandler? m_subscriptionsChanged;
        private EventHandler? m_sessionClosing;
        private EventHandler? m_sessionConfigurationChanged;
        private RenewUserIdentityEventHandler? m_renewUserIdentity;

        /// <summary>
        /// Raised when the connection state changes.
        /// </summary>
        public event EventHandler<ConnectionStateChangedEventArgs>?
            ConnectionStateChanged;

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
            => InnerSession.Save(stream, subscriptions, knownTypes);

        /// <inheritdoc/>
        public IEnumerable<Subscription> Load(
            Stream stream,
            bool transferSubscriptions = false,
            IEnumerable<Type>? knownTypes = null)
            => InnerSession.Load(
                stream, transferSubscriptions, knownTypes);

        /// <inheritdoc/>
        public SessionConfiguration SaveSessionConfiguration(
            Stream? stream = null)
            => InnerSession.SaveSessionConfiguration(stream);

        /// <inheritdoc/>
        public bool ApplySessionConfiguration(
            SessionConfiguration sessionConfiguration)
            => InnerSession
                .ApplySessionConfiguration(sessionConfiguration);

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
            m_stateMachine.RequestClose();
            var session = m_session;
            if (session != null)
            {
                return await session.CloseAsync(
                    timeout, closeChannel, ct)
                    .ConfigureAwait(false);
            }

            return StatusCodes.Good;
        }

        /// <inheritdoc/>
        public bool AddSubscription(Subscription subscription)
            => InnerSession.AddSubscription(subscription);

        /// <inheritdoc/>
        public bool RemoveTransferredSubscription(
            Subscription subscription)
            => InnerSession
                .RemoveTransferredSubscription(subscription);

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
            => InnerSession.BeginPublish(timeout);

        /// <inheritdoc/>
        public void StartPublishing(int timeout, bool fullQueue)
            => InnerSession.StartPublishing(timeout, fullQueue);

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

#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
        /// <inheritdoc/>
        public async IAsyncEnumerable<BrowseResult> BrowseStreamAsync(
            RequestHeader? requestHeader,
            ViewDescription? view,
            ArrayOf<BrowseDescription> nodesToBrowse,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            using (await m_serviceLock.ReaderLockAsync(ct)
                .ConfigureAwait(false))
            {
                await foreach (var result in InnerSession
                    .BrowseStreamAsync(
                        requestHeader, view, nodesToBrowse, ct)
                    .ConfigureAwait(false))
                {
                    yield return result;
                }
            }
        }
#endif

        /// <inheritdoc/>
        public void AttachChannel(ITransportChannel channel)
            => InnerSession.AttachChannel(channel);

        /// <inheritdoc/>
        public void DetachChannel()
            => InnerSession.DetachChannel();

        /// <inheritdoc/>
        public async Task<StatusCode> CloseAsync(
            CancellationToken ct = default)
        {
            m_stateMachine.RequestClose();
            var session = m_session;
            if (session != null)
            {
                return await session.CloseAsync(ct)
                    .ConfigureAwait(false);
            }

            return StatusCodes.Good;
        }

        /// <inheritdoc/>
        public uint NewRequestHandle()
            => InnerSession.NewRequestHandle();

        private void WireStateMachineCallbacks()
        {
            m_stateMachine.ConnectAsync = HandleConnectAsync;
            m_stateMachine.ReconnectAsync = HandleReconnectAsync;
            m_stateMachine.FailoverAsync = HandleFailoverAsync;
            m_stateMachine.CloseSessionAsync = HandleCloseSessionAsync;
            m_stateMachine.StateChanged += OnStateChanged;
        }

        private async Task<ServiceResult> HandleConnectAsync(
            CancellationToken ct)
        {
            try
            {
                m_logger.LogInformation(
                    "ManagedSession: Connecting to {Endpoint}.",
                    m_endpoint.EndpointUrl);

                var session = (Session)await m_sessionFactory.CreateAsync(
                    m_configuration,
                    m_endpoint,
                    updateBeforeConnect: true,
                    m_checkDomain,
                    m_sessionName,
                    m_sessionTimeout,
                    m_identity,
                    m_preferredLocales,
                    ct).ConfigureAwait(false);

                WireSessionEvents(session);
                m_session = session;

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
            CancellationToken ct)
        {
            try
            {
                m_logger.LogInformation(
                    "ManagedSession: Reconnecting.");

                using (await m_serviceLock.WriterLockAsync(ct)
                    .ConfigureAwait(false))
                {
                    var session = m_session;
                    if (session != null)
                    {
                        await session.ReconnectAsync(
                            connection: null,
                            channel: null,
                            ct).ConfigureAwait(false);
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
            CancellationToken ct)
        {
            if (m_redundancyHandler == null || m_redundancyInfo == null)
            {
                return new ServiceResult(
                    StatusCodes.BadNotSupported);
            }

            try
            {
                var failoverEndpoint =
                    m_redundancyHandler.SelectFailoverTarget(
                        m_redundancyInfo, m_endpoint);

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
                    var oldSession = m_session;

                    var newSession =
                        (Session)await m_sessionFactory.CreateAsync(
                            m_configuration,
                            failoverEndpoint,
                            updateBeforeConnect: true,
                            m_checkDomain,
                            m_sessionName,
                            m_sessionTimeout,
                            m_identity,
                            m_preferredLocales,
                            ct).ConfigureAwait(false);

                    WireSessionEvents(newSession);

                    if (oldSession != null)
                    {
                        UnwireSessionEvents(oldSession);
                        try
                        {
                            await oldSession
                                .CloseAsync(ct)
                                .ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            m_logger.LogDebug(
                                ex,
                                "ManagedSession: " +
                                "Old session close failed " +
                                "during failover.");
                        }

                        oldSession.Dispose();
                    }

                    m_session = newSession;
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

        private async Task HandleCloseSessionAsync(
            CancellationToken ct)
        {
            var session = m_session;
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
        }

        private void OnInnerKeepAlive(
            ISession session, KeepAliveEventArgs e)
        {
            if (e.Status != null &&
                ServiceResult.IsBad(e.Status))
            {
                m_stateMachine.TriggerReconnect();
            }

            m_keepAlive?.Invoke(this, e);
        }

        private void OnInnerNotification(
            ISession session, NotificationEventArgs e)
            => m_notification?.Invoke(this, e);

        private void OnInnerPublishError(
            ISession session, PublishErrorEventArgs e)
            => m_publishError?.Invoke(this, e);

        private void OnInnerPublishSequenceNumbers(
            ISession session,
            PublishSequenceNumbersToAcknowledgeEventArgs e)
            => m_publishSequenceNumbersToAcknowledge?.Invoke(this, e);

        private void OnInnerSubscriptionsChanged(
            object? sender, EventArgs e)
            => m_subscriptionsChanged?.Invoke(this, e);

        private void OnInnerSessionClosing(
            object? sender, EventArgs e)
            => m_sessionClosing?.Invoke(this, e);

        private void OnInnerSessionConfigurationChanged(
            object? sender, EventArgs e)
            => m_sessionConfigurationChanged?.Invoke(this, e);

        private IUserIdentity OnInnerRenewUserIdentity(
            ISession session, IUserIdentity identity)
            => m_renewUserIdentity?.Invoke(this, identity)
                ?? identity;

        private void OnStateChanged(
            object? sender, ConnectionStateChangedEventArgs e)
        {
            ConnectionStateChanged?.Invoke(this, e);
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
                m_stateMachine.RequestClose();

                var session = m_session;
                m_session = null;

                if (session != null)
                {
                    UnwireSessionEvents(session);
                    session.Dispose();
                }
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref m_disposed, 1) != 0)
            {
                return;
            }

            await m_stateMachine.DisposeAsync()
                .ConfigureAwait(false);

            var session = m_session;
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
    }
}
