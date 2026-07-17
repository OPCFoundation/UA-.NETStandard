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
using Opc.Ua.Client;
using Opc.Ua.Client.Redundancy;

namespace Opc.Ua.Redundancy.Client
{
    /// <summary>
    /// Transparent redundant-client session facade that exposes one stable <see cref="ISession"/> handle.
    /// </summary>
    public sealed class RedundantClientSession : ISession
    {
        /// <summary>
        /// Initializes a new redundant client session facade.
        /// </summary>
        /// <param name="coordinator">The replica coordinator that supplies the active leader session.</param>
        public RedundantClientSession(ClientReplicaCoordinator coordinator)
            : this(coordinator, () => coordinator.CurrentSession)
        {
        }

        /// <summary>
        /// Initializes a new redundant client session facade with an explicit active-session accessor.
        /// </summary>
        /// <param name="coordinator">The replica coordinator that manages leadership for this facade.</param>
        /// <param name="currentSessionAccessor">The accessor that returns the current leader session.</param>
        internal RedundantClientSession(ClientReplicaCoordinator coordinator, Func<ISession?> currentSessionAccessor)
        {
            m_coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
            m_currentSessionAccessor =
                currentSessionAccessor ?? throw new ArgumentNullException(nameof(currentSessionAccessor));
            m_activeSession = CreateSessionCompletionSource();
            m_coordinator.RoleChanged += OnRoleChanged;
            UpdateActiveSession();
        }

        /// <summary>
        /// Gets whether this replica is currently the elected leader.
        /// </summary>
        public bool IsLeader => m_coordinator.IsLeader;

        /// <summary>
        /// Gets the current leader session for this replica, if one is active.
        /// </summary>
        public ISession? Current => GetCurrentSession();

        /// <inheritdoc/>
        public ISessionFactory SessionFactory => RequireCurrentSession().SessionFactory;

        /// <inheritdoc/>
        public ConfiguredEndpoint ConfiguredEndpoint => RequireCurrentSession().ConfiguredEndpoint;

        /// <inheritdoc/>
        public string SessionName => RequireCurrentSession().SessionName;

        /// <inheritdoc/>
        public double SessionTimeout => RequireCurrentSession().SessionTimeout;

        /// <inheritdoc/>
        public object? Handle
        {
            get => RequireCurrentSession().Handle;
            set
            {
                lock (m_syncRoot)
                {
                    m_handle = value;
                    m_hasHandle = true;
                    ApplyHandle(RequireCurrentSession(), value);
                }
            }
        }

        /// <inheritdoc/>
        public IUserIdentity Identity => RequireCurrentSession().Identity;

        /// <inheritdoc/>
        public IEnumerable<IUserIdentity> IdentityHistory => RequireCurrentSession().IdentityHistory;

        /// <inheritdoc/>
        public NamespaceTable NamespaceUris => RequireCurrentSession().NamespaceUris;

        /// <inheritdoc/>
        public StringTable ServerUris => RequireCurrentSession().ServerUris;

        /// <inheritdoc/>
        public ISystemContext SystemContext => RequireCurrentSession().SystemContext;

        /// <inheritdoc/>
        public IEncodeableFactory Factory => RequireCurrentSession().Factory;

        /// <inheritdoc/>
        public ITypeTable TypeTree => RequireCurrentSession().TypeTree;

        /// <inheritdoc/>
        public INodeCache NodeCache => RequireCurrentSession().NodeCache;

        /// <inheritdoc/>
        public IFilterContext FilterContext => RequireCurrentSession().FilterContext;

        /// <inheritdoc/>
        public ArrayOf<string> PreferredLocales => RequireCurrentSession().PreferredLocales;

        /// <inheritdoc/>
        public IEnumerable<Subscription> Subscriptions => RequireCurrentSession().Subscriptions;

        /// <inheritdoc/>
        public int SubscriptionCount => RequireCurrentSession().SubscriptionCount;

        /// <inheritdoc/>
        public bool TryGetSubscriptionManager(
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)]
            out Ua.Client.Subscriptions.ISubscriptionManager? manager)
        {
            ISession? session = GetCurrentSession();
            if (session != null)
            {
                return session.TryGetSubscriptionManager(out manager);
            }

            manager = null;
            return false;
        }

        /// <inheritdoc/>
        public bool DeleteSubscriptionsOnClose
        {
            get => RequireCurrentSession().DeleteSubscriptionsOnClose;
            set =>
                SetAndRemember(
                    value,
                    static (s, v) => s.DeleteSubscriptionsOnClose = v,
                    ref m_deleteSubscriptionsOnClose
                );
        }

        /// <inheritdoc/>
        public int PublishRequestCancelDelayOnCloseSession
        {
            get => RequireCurrentSession().PublishRequestCancelDelayOnCloseSession;
            set =>
                SetAndRemember(
                    value,
                    static (s, v) => s.PublishRequestCancelDelayOnCloseSession = v,
                    ref m_publishRequestCancelDelayOnCloseSession
                );
        }

        /// <inheritdoc/>
        public Subscription DefaultSubscription
        {
            get => RequireCurrentSession().DefaultSubscription;
            set => RequireCurrentSession().DefaultSubscription = value;
        }

        /// <inheritdoc/>
        public int KeepAliveInterval
        {
            get => RequireCurrentSession().KeepAliveInterval;
            set => SetAndRemember(value, static (s, v) => s.KeepAliveInterval = v, ref m_keepAliveInterval);
        }

        /// <inheritdoc/>
        public bool KeepAliveStopped => RequireCurrentSession().KeepAliveStopped;

        /// <inheritdoc/>
        public DateTime LastKeepAliveTime => RequireCurrentSession().LastKeepAliveTime;

        /// <inheritdoc/>
        public long LastKeepAliveTimestamp => RequireCurrentSession().LastKeepAliveTimestamp;

        /// <inheritdoc/>
        public int OutstandingRequestCount => RequireCurrentSession().OutstandingRequestCount;

        /// <inheritdoc/>
        public int DefunctRequestCount => RequireCurrentSession().DefunctRequestCount;

        /// <inheritdoc/>
        public int GoodPublishRequestCount => RequireCurrentSession().GoodPublishRequestCount;

        /// <inheritdoc/>
        public int MinPublishRequestCount
        {
            get => RequireCurrentSession().MinPublishRequestCount;
            set => SetAndRemember(value, static (s, v) => s.MinPublishRequestCount = v, ref m_minPublishRequestCount);
        }

        /// <inheritdoc/>
        public int MaxPublishRequestCount
        {
            get => RequireCurrentSession().MaxPublishRequestCount;
            set => SetAndRemember(value, static (s, v) => s.MaxPublishRequestCount = v, ref m_maxPublishRequestCount);
        }

        /// <inheritdoc/>
        public bool Reconnecting => RequireCurrentSession().Reconnecting;

        /// <inheritdoc/>
        public OperationLimits OperationLimits => RequireCurrentSession().OperationLimits;

        /// <inheritdoc/>
        public ServerCapabilities ServerCapabilities => RequireCurrentSession().ServerCapabilities;

        /// <inheritdoc/>
        public bool TransferSubscriptionsOnReconnect
        {
            get => RequireCurrentSession().TransferSubscriptionsOnReconnect;
            set =>
                SetAndRemember(
                    value,
                    static (s, v) => s.TransferSubscriptionsOnReconnect = v,
                    ref m_transferSubscriptionsOnReconnect
                );
        }

        /// <inheritdoc/>
        public bool CheckDomain => RequireCurrentSession().CheckDomain;

        /// <inheritdoc/>
        public ContinuationPointPolicy ContinuationPointPolicy
        {
            get => RequireCurrentSession().ContinuationPointPolicy;
            set => SetAndRemember(value, static (s, v) => s.ContinuationPointPolicy = v, ref m_continuationPointPolicy);
        }

        /// <inheritdoc/>
        public NodeId SessionId => RequireCurrentSession().SessionId;

        /// <inheritdoc/>
        public bool Connected => RequireCurrentSession().Connected;

        /// <inheritdoc/>
        public ClientTraceFlags ActivityTraceFlags
        {
            get => RequireCurrentSession().ActivityTraceFlags;
            set => RequireCurrentSession().ActivityTraceFlags = value;
        }

        /// <inheritdoc/>
        public EndpointDescription Endpoint => RequireCurrentSession().Endpoint;

        /// <inheritdoc/>
        public EndpointConfiguration EndpointConfiguration => RequireCurrentSession().EndpointConfiguration;

        /// <inheritdoc/>
        public IServiceMessageContext MessageContext => RequireCurrentSession().MessageContext;

        /// <inheritdoc/>
        public ITransportChannel NullableTransportChannel => RequireCurrentSession().NullableTransportChannel;

        /// <inheritdoc/>
        public ITransportChannel TransportChannel => RequireCurrentSession().TransportChannel;

        /// <inheritdoc/>
        public DiagnosticsMasks ReturnDiagnostics
        {
            get => RequireCurrentSession().ReturnDiagnostics;
            set => RequireCurrentSession().ReturnDiagnostics = value;
        }

        /// <inheritdoc/>
        public int OperationTimeout
        {
            get => RequireCurrentSession().OperationTimeout;
            set => RequireCurrentSession().OperationTimeout = value;
        }

        /// <inheritdoc/>
        public int DefaultTimeoutHint
        {
            get => RequireCurrentSession().DefaultTimeoutHint;
            set => RequireCurrentSession().DefaultTimeoutHint = value;
        }

        /// <inheritdoc/>
        public bool Disposed => m_disposed;

        /// <summary>
        /// Raised after this facade's replica role changes.
        /// </summary>
        public event Action<bool>? RoleChanged;

        /// <inheritdoc/>
        public event KeepAliveEventHandler KeepAlive
        {
            add
            {
                lock (m_syncRoot)
                {
                    m_keepAlive += value;
                }
            }
            remove
            {
                lock (m_syncRoot)
                {
                    m_keepAlive -= value;
                }
            }
        }

        /// <inheritdoc/>
        public event NotificationEventHandler Notification
        {
            add
            {
                lock (m_syncRoot)
                {
                    m_notification += value;
                }
            }
            remove
            {
                lock (m_syncRoot)
                {
                    m_notification -= value;
                }
            }
        }

        /// <inheritdoc/>
        public event PublishErrorEventHandler PublishError
        {
            add
            {
                lock (m_syncRoot)
                {
                    m_publishError += value;
                }
            }
            remove
            {
                lock (m_syncRoot)
                {
                    m_publishError -= value;
                }
            }
        }

        /// <inheritdoc/>
        public event PublishSequenceNumbersToAcknowledgeEventHandler PublishSequenceNumbersToAcknowledge
        {
            add
            {
                lock (m_syncRoot)
                {
                    m_publishSequenceNumbersToAcknowledge += value;
                }
            }
            remove
            {
                lock (m_syncRoot)
                {
                    m_publishSequenceNumbersToAcknowledge -= value;
                }
            }
        }

        /// <inheritdoc/>
        public event EventHandler SubscriptionsChanged
        {
            add
            {
                lock (m_syncRoot)
                {
                    m_subscriptionsChanged += value;
                }
            }
            remove
            {
                lock (m_syncRoot)
                {
                    m_subscriptionsChanged -= value;
                }
            }
        }

        /// <inheritdoc/>
        public event EventHandler SessionClosing
        {
            add
            {
                lock (m_syncRoot)
                {
                    m_sessionClosing += value;
                }
            }
            remove
            {
                lock (m_syncRoot)
                {
                    m_sessionClosing -= value;
                }
            }
        }

        /// <inheritdoc/>
        public event EventHandler SessionConfigurationChanged
        {
            add
            {
                lock (m_syncRoot)
                {
                    m_sessionConfigurationChanged += value;
                }
            }
            remove
            {
                lock (m_syncRoot)
                {
                    m_sessionConfigurationChanged -= value;
                }
            }
        }

        /// <inheritdoc/>
        public event RenewUserIdentityEventHandler RenewUserIdentity
        {
            add
            {
                lock (m_syncRoot)
                {
                    m_renewUserIdentity += value;
                }
            }
            remove
            {
                lock (m_syncRoot)
                {
                    m_renewUserIdentity -= value;
                }
            }
        }

        /// <summary>
        /// Starts the underlying replica coordinator and wires the initial leader session state.
        /// </summary>
        /// <param name="ct">The token that cancels the startup operation.</param>
        /// <returns>A task that completes when the coordinator has started.</returns>
        public async ValueTask StartAsync(CancellationToken ct = default)
        {
            ThrowIfDisposed();
            await m_coordinator.StartAsync(ct).ConfigureAwait(false);
            UpdateActiveSession();
        }

        /// <summary>
        /// Waits until this replica is leader and has a live session.
        /// </summary>
        /// <param name="ct">The token that cancels the wait.</param>
        /// <returns>A task that completes when leadership and a live session are available.</returns>
        public async Task WaitForLeadershipAsync(CancellationToken ct = default)
        {
            _ = await GetActiveAsync(ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            TaskCompletionSource<ISession>? completion = BeginDispose();
            if (completion == null)
            {
                return;
            }
            completion.TrySetException(new ObjectDisposedException(nameof(RedundantClientSession)));
            m_coordinator.RoleChanged -= OnRoleChanged;
            await m_coordinator.DisposeAsync().ConfigureAwait(false);
            GC.SuppressFinalize(this);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            // ISession is IAsyncDisposable; DisposeAsync is preferred. This synchronous path
            // (required by IClientBase : IDisposable, e.g. a synchronous container teardown)
            // performs the same best-effort cleanup and lets the coordinator drain
            // asynchronously rather than blocking on an async dispose.
            TaskCompletionSource<ISession>? completion = BeginDispose();
            if (completion == null)
            {
                return;
            }
            completion.TrySetException(new ObjectDisposedException(nameof(RedundantClientSession)));
            m_coordinator.RoleChanged -= OnRoleChanged;
            _ = m_coordinator.DisposeAsync().AsTask();
            GC.SuppressFinalize(this);
        }

        private TaskCompletionSource<ISession>? BeginDispose()
        {
            lock (m_syncRoot)
            {
                if (m_disposed)
                {
                    return null;
                }
                m_disposed = true;
                TaskCompletionSource<ISession> completion = m_activeSession;
                DetachEvents(m_attachedSession);
                m_attachedSession = null;
                m_currentSession = null;
                m_activeSession = CreateSessionCompletionSource();
                return completion;
            }
        }

        /// <inheritdoc/>
        public async Task ReconnectAsync(
            ITransportWaitingConnection? connection,
            ITransportChannel? channel,
            CancellationToken ct = default
        )
        {
            ISession s = await GetActiveAsync(ct).ConfigureAwait(false);
            await s.ReconnectAsync(connection, channel, ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task ReloadInstanceCertificateAsync(CancellationToken ct = default)
        {
            ISession s = await GetActiveAsync(ct).ConfigureAwait(false);
            await s.ReloadInstanceCertificateAsync(ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public void Save(
            Stream stream,
            IEnumerable<Subscription> subscriptions,
            IEnumerable<Type>? knownTypes = null
        )
        {
            RequireCurrentSession().Save(stream, subscriptions, knownTypes);
        }

        /// <inheritdoc/>
        public IEnumerable<Subscription> Load(
            Stream stream,
            bool transferSubscriptions = false,
            IEnumerable<Type>? knownTypes = null
        )
        {
            return RequireCurrentSession().Load(stream, transferSubscriptions, knownTypes);
        }

        /// <inheritdoc/>
        public SessionConfiguration SaveSessionConfiguration(Stream? stream = null)
        {
            return RequireCurrentSession().SaveSessionConfiguration(stream);
        }

        /// <inheritdoc/>
        public bool ApplySessionConfiguration(SessionConfiguration sessionConfiguration)
        {
            return RequireCurrentSession().ApplySessionConfiguration(sessionConfiguration);
        }

        /// <inheritdoc/>
        public async Task FetchNamespaceTablesAsync(CancellationToken ct = default)
        {
            ISession s = await GetActiveAsync(ct).ConfigureAwait(false);
            await s.FetchNamespaceTablesAsync(ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task FetchTypeTreeAsync(ExpandedNodeId typeId, CancellationToken ct = default)
        {
            ISession s = await GetActiveAsync(ct).ConfigureAwait(false);
            await s.FetchTypeTreeAsync(typeId, ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task FetchTypeTreeAsync(ArrayOf<ExpandedNodeId> typeIds, CancellationToken ct = default)
        {
            ISession s = await GetActiveAsync(ct).ConfigureAwait(false);
            await s.FetchTypeTreeAsync(typeIds, ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task OpenAsync(
            string sessionName,
            uint sessionTimeout,
            IUserIdentity identity,
            ArrayOf<string> preferredLocales,
            bool checkDomain,
            bool closeChannel,
            CancellationToken ct = default
        )
        {
            ISession s = await GetActiveAsync(ct).ConfigureAwait(false);
            await s.OpenAsync(sessionName, sessionTimeout, identity, preferredLocales, checkDomain, closeChannel, ct)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task UpdateSessionAsync(
            IUserIdentity identity,
            ArrayOf<string> preferredLocales,
            CancellationToken ct = default
        )
        {
            ISession s = await GetActiveAsync(ct).ConfigureAwait(false);
            await s.UpdateSessionAsync(identity, preferredLocales, ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task ChangePreferredLocalesAsync(ArrayOf<string> preferredLocales, CancellationToken ct = default)
        {
            ISession s = await GetActiveAsync(ct).ConfigureAwait(false);
            await s.ChangePreferredLocalesAsync(preferredLocales, ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<StatusCode> CloseAsync(int timeout, bool closeChannel, CancellationToken ct = default)
        {
            ISession s = await GetActiveAsync(ct).ConfigureAwait(false);
            return await s.CloseAsync(timeout, closeChannel, ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public bool AddSubscription(Subscription subscription)
        {
            return RequireCurrentSession().AddSubscription(subscription);
        }

        /// <inheritdoc/>
        public bool RemoveTransferredSubscription(Subscription subscription)
        {
            return RequireCurrentSession().RemoveTransferredSubscription(subscription);
        }

        /// <inheritdoc/>
        public async Task<bool> RemoveSubscriptionAsync(Subscription subscription, CancellationToken ct = default)
        {
            ISession s = await GetActiveAsync(ct).ConfigureAwait(false);
            return await s.RemoveSubscriptionAsync(subscription, ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<bool> RemoveSubscriptionsAsync(
            IEnumerable<Subscription> subscriptions,
            CancellationToken ct = default
        )
        {
            ISession s = await GetActiveAsync(ct).ConfigureAwait(false);
            return await s.RemoveSubscriptionsAsync(subscriptions, ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<bool> ReactivateSubscriptionsAsync(
            SubscriptionCollection subscriptions,
            bool sendInitialValues,
            CancellationToken ct = default
        )
        {
            ISession s = await GetActiveAsync(ct).ConfigureAwait(false);
            return await s.ReactivateSubscriptionsAsync(subscriptions, sendInitialValues, ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<bool> TransferSubscriptionsAsync(
            SubscriptionCollection subscriptions,
            bool sendInitialValues,
            CancellationToken ct = default
        )
        {
            ISession s = await GetActiveAsync(ct).ConfigureAwait(false);
            return await s.TransferSubscriptionsAsync(subscriptions, sendInitialValues, ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public bool BeginPublish(int timeout)
        {
            return RequireCurrentSession().BeginPublish(timeout);
        }

        /// <inheritdoc/>
        public void StartPublishing(int timeout, bool fullQueue)
        {
            RequireCurrentSession().StartPublishing(timeout, fullQueue);
        }

        /// <inheritdoc/>
        public async Task<(bool, ServiceResult)> RepublishAsync(
            uint subscriptionId,
            uint sequenceNumber,
            CancellationToken ct = default
        )
        {
            ISession s = await GetActiveAsync(ct).ConfigureAwait(false);
            return await s.RepublishAsync(subscriptionId, sequenceNumber, ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        [Obsolete(
            "Channels are now managed centrally via IClientChannelManager. Use Session.CreateAsync(IClientChannelManager, ...) instead of manual AttachChannel/DetachChannel. This method remains functional for back-compat."
        )]
        public void AttachChannel(ITransportChannel channel)
        {
            RequireCurrentSession().AttachChannel(channel);
        }

        /// <inheritdoc/>
        [Obsolete(
            "Channels are now managed centrally via IClientChannelManager. Use Session.CreateAsync(IClientChannelManager, ...) instead of manual AttachChannel/DetachChannel. This method remains functional for back-compat."
        )]
        public void DetachChannel()
        {
            RequireCurrentSession().DetachChannel();
        }

        /// <inheritdoc/>
        public async Task<StatusCode> CloseAsync(CancellationToken ct = default)
        {
            ISession s = await GetActiveAsync(ct).ConfigureAwait(false);
            return await s.CloseAsync(ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public uint NewRequestHandle()
        {
            return RequireCurrentSession().NewRequestHandle();
        }

        /// <inheritdoc/>
        public async ValueTask<ReadResponse> ReadAsync(
            RequestHeader? requestHeader,
            double maxAge,
            TimestampsToReturn timestampsToReturn,
            ArrayOf<ReadValueId> nodesToRead,
            CancellationToken ct
        )
        {
            ISession session = await GetActiveAsync(ct).ConfigureAwait(false);
            return await session
                .ReadAsync(requestHeader, maxAge, timestampsToReturn, nodesToRead, ct)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask<HistoryReadResponse> HistoryReadAsync(
            RequestHeader? requestHeader,
            ExtensionObject historyReadDetails,
            TimestampsToReturn timestampsToReturn,
            bool releaseContinuationPoints,
            ArrayOf<HistoryReadValueId> nodesToRead,
            CancellationToken ct
        )
        {
            ISession session = await GetActiveAsync(ct).ConfigureAwait(false);
            return await session
                .HistoryReadAsync(
                    requestHeader,
                    historyReadDetails,
                    timestampsToReturn,
                    releaseContinuationPoints,
                    nodesToRead,
                    ct
                )
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask<WriteResponse> WriteAsync(
            RequestHeader? requestHeader,
            ArrayOf<WriteValue> nodesToWrite,
            CancellationToken ct
        )
        {
            ISession session = await GetActiveAsync(ct).ConfigureAwait(false);
            return await session.WriteAsync(requestHeader, nodesToWrite, ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask<HistoryUpdateResponse> HistoryUpdateAsync(
            RequestHeader? requestHeader,
            ArrayOf<ExtensionObject> historyUpdateDetails,
            CancellationToken ct
        )
        {
            ISession session = await GetActiveAsync(ct).ConfigureAwait(false);
            return await session.HistoryUpdateAsync(requestHeader, historyUpdateDetails, ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask<BrowseResponse> BrowseAsync(
            RequestHeader? requestHeader,
            ViewDescription? view,
            uint requestedMaxReferencesPerNode,
            ArrayOf<BrowseDescription> nodesToBrowse,
            CancellationToken ct
        )
        {
            ISession session = await GetActiveAsync(ct).ConfigureAwait(false);
            return await session
                .BrowseAsync(requestHeader, view, requestedMaxReferencesPerNode, nodesToBrowse, ct)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask<BrowseNextResponse> BrowseNextAsync(
            RequestHeader? requestHeader,
            bool releaseContinuationPoints,
            ArrayOf<ByteString> continuationPoints,
            CancellationToken ct
        )
        {
            ISession session = await GetActiveAsync(ct).ConfigureAwait(false);
            return await session
                .BrowseNextAsync(requestHeader, releaseContinuationPoints, continuationPoints, ct)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask<TranslateBrowsePathsToNodeIdsResponse> TranslateBrowsePathsToNodeIdsAsync(
            RequestHeader? requestHeader,
            ArrayOf<BrowsePath> browsePaths,
            CancellationToken ct
        )
        {
            ISession session = await GetActiveAsync(ct).ConfigureAwait(false);
            return await session
                .TranslateBrowsePathsToNodeIdsAsync(requestHeader, browsePaths, ct)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask<RegisterNodesResponse> RegisterNodesAsync(
            RequestHeader? requestHeader,
            ArrayOf<NodeId> nodesToRegister,
            CancellationToken ct
        )
        {
            ISession session = await GetActiveAsync(ct).ConfigureAwait(false);
            return await session.RegisterNodesAsync(requestHeader, nodesToRegister, ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask<UnregisterNodesResponse> UnregisterNodesAsync(
            RequestHeader? requestHeader,
            ArrayOf<NodeId> nodesToUnregister,
            CancellationToken ct
        )
        {
            ISession session = await GetActiveAsync(ct).ConfigureAwait(false);
            return await session.UnregisterNodesAsync(requestHeader, nodesToUnregister, ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask<CallResponse> CallAsync(
            RequestHeader? requestHeader,
            ArrayOf<CallMethodRequest> methodsToCall,
            CancellationToken ct
        )
        {
            ISession session = await GetActiveAsync(ct).ConfigureAwait(false);
            return await session.CallAsync(requestHeader, methodsToCall, ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask<CreateMonitoredItemsResponse> CreateMonitoredItemsAsync(
            RequestHeader? requestHeader,
            uint subscriptionId,
            TimestampsToReturn timestampsToReturn,
            ArrayOf<MonitoredItemCreateRequest> itemsToCreate,
            CancellationToken ct
        )
        {
            ISession session = await GetActiveAsync(ct).ConfigureAwait(false);
            return await session
                .CreateMonitoredItemsAsync(requestHeader, subscriptionId, timestampsToReturn, itemsToCreate, ct)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask<ModifyMonitoredItemsResponse> ModifyMonitoredItemsAsync(
            RequestHeader? requestHeader,
            uint subscriptionId,
            TimestampsToReturn timestampsToReturn,
            ArrayOf<MonitoredItemModifyRequest> itemsToModify,
            CancellationToken ct
        )
        {
            ISession session = await GetActiveAsync(ct).ConfigureAwait(false);
            return await session
                .ModifyMonitoredItemsAsync(requestHeader, subscriptionId, timestampsToReturn, itemsToModify, ct)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask<SetMonitoringModeResponse> SetMonitoringModeAsync(
            RequestHeader? requestHeader,
            uint subscriptionId,
            MonitoringMode monitoringMode,
            ArrayOf<uint> monitoredItemIds,
            CancellationToken ct
        )
        {
            ISession session = await GetActiveAsync(ct).ConfigureAwait(false);
            return await session
                .SetMonitoringModeAsync(requestHeader, subscriptionId, monitoringMode, monitoredItemIds, ct)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask<SetTriggeringResponse> SetTriggeringAsync(
            RequestHeader? requestHeader,
            uint subscriptionId,
            uint triggeringItemId,
            ArrayOf<uint> linksToAdd,
            ArrayOf<uint> linksToRemove,
            CancellationToken ct
        )
        {
            ISession session = await GetActiveAsync(ct).ConfigureAwait(false);
            return await session
                .SetTriggeringAsync(requestHeader, subscriptionId, triggeringItemId, linksToAdd, linksToRemove, ct)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask<DeleteMonitoredItemsResponse> DeleteMonitoredItemsAsync(
            RequestHeader? requestHeader,
            uint subscriptionId,
            ArrayOf<uint> monitoredItemIds,
            CancellationToken ct
        )
        {
            ISession session = await GetActiveAsync(ct).ConfigureAwait(false);
            return await session
                .DeleteMonitoredItemsAsync(requestHeader, subscriptionId, monitoredItemIds, ct)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask<CreateSubscriptionResponse> CreateSubscriptionAsync(
            RequestHeader? requestHeader,
            double requestedPublishingInterval,
            uint requestedLifetimeCount,
            uint requestedMaxKeepAliveCount,
            uint maxNotificationsPerPublish,
            bool publishingEnabled,
            byte priority,
            CancellationToken ct
        )
        {
            ISession session = await GetActiveAsync(ct).ConfigureAwait(false);
            return await session
                .CreateSubscriptionAsync(
                    requestHeader,
                    requestedPublishingInterval,
                    requestedLifetimeCount,
                    requestedMaxKeepAliveCount,
                    maxNotificationsPerPublish,
                    publishingEnabled,
                    priority,
                    ct
                )
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask<ModifySubscriptionResponse> ModifySubscriptionAsync(
            RequestHeader? requestHeader,
            uint subscriptionId,
            double requestedPublishingInterval,
            uint requestedLifetimeCount,
            uint requestedMaxKeepAliveCount,
            uint maxNotificationsPerPublish,
            byte priority,
            CancellationToken ct
        )
        {
            ISession session = await GetActiveAsync(ct).ConfigureAwait(false);
            return await session
                .ModifySubscriptionAsync(
                    requestHeader,
                    subscriptionId,
                    requestedPublishingInterval,
                    requestedLifetimeCount,
                    requestedMaxKeepAliveCount,
                    maxNotificationsPerPublish,
                    priority,
                    ct
                )
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask<SetPublishingModeResponse> SetPublishingModeAsync(
            RequestHeader? requestHeader,
            bool publishingEnabled,
            ArrayOf<uint> subscriptionIds,
            CancellationToken ct
        )
        {
            ISession session = await GetActiveAsync(ct).ConfigureAwait(false);
            return await session
                .SetPublishingModeAsync(requestHeader, publishingEnabled, subscriptionIds, ct)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask<PublishResponse> PublishAsync(
            RequestHeader? requestHeader,
            ArrayOf<SubscriptionAcknowledgement> subscriptionAcknowledgements,
            CancellationToken ct
        )
        {
            ISession session = await GetActiveAsync(ct).ConfigureAwait(false);
            return await session.PublishAsync(requestHeader, subscriptionAcknowledgements, ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask<RepublishResponse> RepublishAsync(
            RequestHeader? requestHeader,
            uint subscriptionId,
            uint retransmitSequenceNumber,
            CancellationToken ct
        )
        {
            ISession session = await GetActiveAsync(ct).ConfigureAwait(false);
            return await session
                .RepublishAsync(requestHeader, subscriptionId, retransmitSequenceNumber, ct)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask<TransferSubscriptionsResponse> TransferSubscriptionsAsync(
            RequestHeader? requestHeader,
            ArrayOf<uint> subscriptionIds,
            bool sendInitialValues,
            CancellationToken ct
        )
        {
            ISession session = await GetActiveAsync(ct).ConfigureAwait(false);
            return await session
                .TransferSubscriptionsAsync(requestHeader, subscriptionIds, sendInitialValues, ct)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask<DeleteSubscriptionsResponse> DeleteSubscriptionsAsync(
            RequestHeader? requestHeader,
            ArrayOf<uint> subscriptionIds,
            CancellationToken ct
        )
        {
            ISession session = await GetActiveAsync(ct).ConfigureAwait(false);
            return await session.DeleteSubscriptionsAsync(requestHeader, subscriptionIds, ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask<AddNodesResponse> AddNodesAsync(
            RequestHeader? requestHeader,
            ArrayOf<AddNodesItem> nodesToAdd,
            CancellationToken ct
        )
        {
            ISession session = await GetActiveAsync(ct).ConfigureAwait(false);
            return await session.AddNodesAsync(requestHeader, nodesToAdd, ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask<AddReferencesResponse> AddReferencesAsync(
            RequestHeader? requestHeader,
            ArrayOf<AddReferencesItem> referencesToAdd,
            CancellationToken ct
        )
        {
            ISession session = await GetActiveAsync(ct).ConfigureAwait(false);
            return await session.AddReferencesAsync(requestHeader, referencesToAdd, ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask<DeleteNodesResponse> DeleteNodesAsync(
            RequestHeader? requestHeader,
            ArrayOf<DeleteNodesItem> nodesToDelete,
            CancellationToken ct
        )
        {
            ISession session = await GetActiveAsync(ct).ConfigureAwait(false);
            return await session.DeleteNodesAsync(requestHeader, nodesToDelete, ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask<DeleteReferencesResponse> DeleteReferencesAsync(
            RequestHeader? requestHeader,
            ArrayOf<DeleteReferencesItem> referencesToDelete,
            CancellationToken ct
        )
        {
            ISession session = await GetActiveAsync(ct).ConfigureAwait(false);
            return await session.DeleteReferencesAsync(requestHeader, referencesToDelete, ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask<QueryFirstResponse> QueryFirstAsync(
            RequestHeader? requestHeader,
            ViewDescription? view,
            ArrayOf<NodeTypeDescription> nodeTypes,
            ContentFilter? filter,
            uint maxDataSetsToReturn,
            uint maxReferencesToReturn,
            CancellationToken ct
        )
        {
            ISession session = await GetActiveAsync(ct).ConfigureAwait(false);
            return await session
                .QueryFirstAsync(requestHeader, view, nodeTypes, filter, maxDataSetsToReturn, maxReferencesToReturn, ct)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask<QueryNextResponse> QueryNextAsync(
            RequestHeader? requestHeader,
            bool releaseContinuationPoint,
            ByteString continuationPoint,
            CancellationToken ct
        )
        {
            ISession session = await GetActiveAsync(ct).ConfigureAwait(false);
            return await session
                .QueryNextAsync(requestHeader, releaseContinuationPoint, continuationPoint, ct)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask<CreateSessionResponse> CreateSessionAsync(
            RequestHeader? requestHeader,
            ApplicationDescription? clientDescription,
            string? serverUri,
            string? endpointUrl,
            string? sessionName,
            ByteString clientNonce,
            ByteString clientCertificate,
            double requestedSessionTimeout,
            uint maxResponseMessageSize,
            CancellationToken ct
        )
        {
            ISession session = await GetActiveAsync(ct).ConfigureAwait(false);
            return await session
                .CreateSessionAsync(
                    requestHeader,
                    clientDescription,
                    serverUri,
                    endpointUrl,
                    sessionName,
                    clientNonce,
                    clientCertificate,
                    requestedSessionTimeout,
                    maxResponseMessageSize,
                    ct
                )
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask<ActivateSessionResponse> ActivateSessionAsync(
            RequestHeader? requestHeader,
            SignatureData? clientSignature,
            ArrayOf<SignedSoftwareCertificate> clientSoftwareCertificates,
            ArrayOf<string> localeIds,
            ExtensionObject userIdentityToken,
            SignatureData? userTokenSignature,
            CancellationToken ct
        )
        {
            ISession session = await GetActiveAsync(ct).ConfigureAwait(false);
            return await session
                .ActivateSessionAsync(
                    requestHeader,
                    clientSignature,
                    clientSoftwareCertificates,
                    localeIds,
                    userIdentityToken,
                    userTokenSignature,
                    ct
                )
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask<CloseSessionResponse> CloseSessionAsync(
            RequestHeader? requestHeader,
            bool deleteSubscriptions,
            CancellationToken ct
        )
        {
            ISession session = await GetActiveAsync(ct).ConfigureAwait(false);
            return await session.CloseSessionAsync(requestHeader, deleteSubscriptions, ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask<CancelResponse> CancelAsync(
            RequestHeader? requestHeader,
            uint requestHandle,
            CancellationToken ct
        )
        {
            ISession session = await GetActiveAsync(ct).ConfigureAwait(false);
            return await session.CancelAsync(requestHeader, requestHandle, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Refreshes the cached active session from the coordinator for unit tests.
        /// </summary>
        internal void RefreshActiveSessionForTesting()
        {
            UpdateActiveSession();
        }

        private async ValueTask<ISession> GetActiveAsync(CancellationToken ct)
        {
            while (true)
            {
                ThrowIfDisposed();
                ct.ThrowIfCancellationRequested();
                TaskCompletionSource<ISession> tcs;
                lock (m_syncRoot)
                {
                    tcs = m_activeSession;
                }
                ISession s = await tcs.Task.WaitAsync(ct).ConfigureAwait(false);
                ThrowIfDisposed();
                lock (m_syncRoot)
                {
                    if (ReferenceEquals(tcs, m_activeSession) && ReferenceEquals(s, m_currentSession))
                    {
                        return s;
                    }
                }
            }
        }

        private ISession RequireCurrentSession()
        {
            ThrowIfDisposed();
            return GetCurrentSession() ??
                throw new ServiceResultException(
                    StatusCodes.BadInvalidState,
                    "The redundant client session is not the leader or has no live session."
                    );
        }

        private ISession? GetCurrentSession()
        {
            lock (m_syncRoot)
            {
                return m_currentSession;
            }
        }

        private void SetAndRemember<T>(T value, Action<ISession, T> apply, ref RememberedValue<T> remembered)
        {
            lock (m_syncRoot)
            {
                remembered = new RememberedValue<T>(value, true);
                apply(RequireCurrentSession(), value);
            }
        }

        private void UpdateActiveSession()
        {
            ISession? s = m_coordinator.IsLeader ? m_currentSessionAccessor() : null;
            TaskCompletionSource<ISession>? release;
            lock (m_syncRoot)
            {
                if (m_disposed)
                {
                    return;
                }
                if (!ReferenceEquals(s, m_attachedSession))
                {
                    DetachEvents(m_attachedSession);
                    m_attachedSession = s;
                    AttachEvents(m_attachedSession);
                }
                ISession? previousSession = m_currentSession;
                m_currentSession = s;
                if (s == null)
                {
                    if (previousSession != null || m_activeSession.Task.IsCompleted)
                    {
                        m_activeSession = CreateSessionCompletionSource();
                    }
                    return;
                }
                if (previousSession != null && !ReferenceEquals(previousSession, s))
                {
                    m_activeSession = CreateSessionCompletionSource();
                }
                ApplyRememberedValues(s);
                release = m_activeSession;
            }
            release.TrySetResult(s);
        }

        private static TaskCompletionSource<ISession> CreateSessionCompletionSource()
        {
            return new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private void ApplyRememberedValues(ISession s)
        {
            if (m_deleteSubscriptionsOnClose.HasValue)
            {
                s.DeleteSubscriptionsOnClose = m_deleteSubscriptionsOnClose.Value;
            }
            if (m_keepAliveInterval.HasValue)
            {
                s.KeepAliveInterval = m_keepAliveInterval.Value;
            }
            if (m_minPublishRequestCount.HasValue)
            {
                s.MinPublishRequestCount = m_minPublishRequestCount.Value;
            }
            if (m_maxPublishRequestCount.HasValue)
            {
                s.MaxPublishRequestCount = m_maxPublishRequestCount.Value;
            }
            if (m_transferSubscriptionsOnReconnect.HasValue)
            {
                s.TransferSubscriptionsOnReconnect = m_transferSubscriptionsOnReconnect.Value;
            }
            if (m_continuationPointPolicy.HasValue)
            {
                s.ContinuationPointPolicy = m_continuationPointPolicy.Value;
            }
            if (m_publishRequestCancelDelayOnCloseSession.HasValue)
            {
                s.PublishRequestCancelDelayOnCloseSession = m_publishRequestCancelDelayOnCloseSession.Value;
            }
            if (m_hasHandle)
            {
                ApplyHandle(s, m_handle);
            }
        }

        private static void ApplyHandle(ISession session, object? value)
        {
            if (session is ManagedSession managedSession)
            {
                managedSession.Handle = value;
            }
        }

        private void AttachEvents(ISession? s)
        {
            if (s == null)
            {
                return;
            }
            s.KeepAlive += OnKeepAlive;
            s.Notification += OnNotification;
            s.PublishError += OnPublishError;
            s.PublishSequenceNumbersToAcknowledge += OnPublishSequenceNumbersToAcknowledge;
            s.SubscriptionsChanged += OnSubscriptionsChanged;
            s.SessionClosing += OnSessionClosing;
            s.SessionConfigurationChanged += OnSessionConfigurationChanged;
            s.RenewUserIdentity += OnRenewUserIdentity;
        }

        private void DetachEvents(ISession? s)
        {
            if (s == null)
            {
                return;
            }
            s.KeepAlive -= OnKeepAlive;
            s.Notification -= OnNotification;
            s.PublishError -= OnPublishError;
            s.PublishSequenceNumbersToAcknowledge -= OnPublishSequenceNumbersToAcknowledge;
            s.SubscriptionsChanged -= OnSubscriptionsChanged;
            s.SessionClosing -= OnSessionClosing;
            s.SessionConfigurationChanged -= OnSessionConfigurationChanged;
            s.RenewUserIdentity -= OnRenewUserIdentity;
        }

        private void OnRoleChanged(bool isLeader)
        {
            UpdateActiveSession();
            RoleChanged?.Invoke(isLeader);
        }

        private void OnKeepAlive(ISession session, KeepAliveEventArgs e)
        {
            m_keepAlive?.Invoke(this, e);
        }

        private void OnNotification(ISession session, NotificationEventArgs e)
        {
            m_notification?.Invoke(this, e);
        }

        private void OnPublishError(ISession session, PublishErrorEventArgs e)
        {
            m_publishError?.Invoke(this, e);
        }

        private void OnPublishSequenceNumbersToAcknowledge(
            ISession session,
            PublishSequenceNumbersToAcknowledgeEventArgs e
        )
        {
            m_publishSequenceNumbersToAcknowledge?.Invoke(this, e);
        }

        private void OnSubscriptionsChanged(object? sender, EventArgs e)
        {
            m_subscriptionsChanged?.Invoke(this, e);
        }

        private void OnSessionClosing(object? sender, EventArgs e)
        {
            m_sessionClosing?.Invoke(this, e);
        }

        private void OnSessionConfigurationChanged(object? sender, EventArgs e)
        {
            m_sessionConfigurationChanged?.Invoke(this, e);
        }

        private IUserIdentity OnRenewUserIdentity(ISession session, IUserIdentity identity)
        {
            RenewUserIdentityEventHandler? h = m_renewUserIdentity;
            return h == null ? identity : h(this, identity);
        }

        private void ThrowIfDisposed()
        {
            if (m_disposed)
            {
                throw new ObjectDisposedException(nameof(RedundantClientSession));
            }
        }

        /// <summary>
        /// Immutable holder for a value read through a redundant session together with a flag indicating whether a
        /// value has actually been captured.
        /// </summary>
        /// <typeparam name="T">The type of the remembered value.</typeparam>
        private readonly struct RememberedValue<T>
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="RememberedValue{T}"/> struct.
            /// </summary>
            /// <param name="value">The captured value.</param>
            /// <param name="hasValue"><c>true</c> when <paramref name="value"/> holds a captured value.</param>
            public RememberedValue(T value, bool hasValue)
            {
                Value = value;
                HasValue = hasValue;
            }

            /// <summary>
            /// Gets the captured value.
            /// </summary>
            public T Value { get; }

            /// <summary>
            /// Gets a value indicating whether a value has been captured.
            /// </summary>
            public bool HasValue { get; }
        }

        private readonly ClientReplicaCoordinator m_coordinator;
        private readonly Func<ISession?> m_currentSessionAccessor;
        private readonly Lock m_syncRoot = new();
        private TaskCompletionSource<ISession> m_activeSession;
        private ISession? m_currentSession;
        private ISession? m_attachedSession;
        private bool m_disposed;
        private bool m_hasHandle;
        private object? m_handle;
        private RememberedValue<bool> m_deleteSubscriptionsOnClose;
        private RememberedValue<int> m_keepAliveInterval;
        private RememberedValue<int> m_minPublishRequestCount;
        private RememberedValue<int> m_maxPublishRequestCount;
        private RememberedValue<bool> m_transferSubscriptionsOnReconnect;
        private RememberedValue<ContinuationPointPolicy> m_continuationPointPolicy;
        private RememberedValue<int> m_publishRequestCancelDelayOnCloseSession;
        private KeepAliveEventHandler? m_keepAlive;
        private NotificationEventHandler? m_notification;
        private PublishErrorEventHandler? m_publishError;
        private PublishSequenceNumbersToAcknowledgeEventHandler? m_publishSequenceNumbersToAcknowledge;
        private EventHandler? m_subscriptionsChanged;
        private EventHandler? m_sessionClosing;
        private EventHandler? m_sessionConfigurationChanged;
        private RenewUserIdentityEventHandler? m_renewUserIdentity;
    }
}
