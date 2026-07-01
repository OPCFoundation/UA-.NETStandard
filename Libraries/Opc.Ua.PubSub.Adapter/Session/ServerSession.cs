/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.MonitoredItems;
using MonitoredItemOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;
using SubscriptionOptions = Opc.Ua.Client.Subscriptions.SubscriptionOptions;

namespace Opc.Ua.PubSub.Adapter.Session
{
    /// <summary>
    /// Default <see cref="IServerSession"/> implementation that wraps a
    /// modern <see cref="ManagedSession"/> built from
    /// <see cref="ServerConnectionOptions"/> and an
    /// <see cref="ITelemetryContext"/>. Read/Write/Call services delegate to
    /// the managed session; data-change subscriptions use the session's V2
    /// subscription manager (see
    /// <see cref="ISession.TryGetSubscriptionManager"/>). Reconnect and
    /// keep-alive are owned by the managed session. The session is held via
    /// the <see cref="ISession"/> abstraction to avoid coupling to the
    /// concrete <see cref="ManagedSession"/>.
    /// </summary>
    public sealed class ServerSession : IServerSession
    {
        private static readonly TimeSpan s_applyPollInterval = TimeSpan.FromMilliseconds(25);
        private static readonly long s_modelChangeCoalesceTicks =
            (long)(TimeSpan.FromMilliseconds(250).TotalSeconds * Stopwatch.Frequency);

        private readonly ServerConnectionOptions m_options;
        private readonly ITelemetryContext m_telemetry;
        private readonly ILogger m_logger;
        private readonly SemaphoreSlim m_connectLock = new(1, 1);
        private readonly System.Threading.Lock m_disposeGate = new();
        private readonly ConcurrentDictionary<string, NodeId> m_resolvedPaths = new(StringComparer.Ordinal);
        private ISession? m_session;
        private ISubscription? m_modelChangeSubscription;
        private long m_lastModelChangeTicks;
        private int m_modelChangeMonitoringStarted;
        private bool m_disposed;

        /// <summary>
        /// Creates a new external server session for the supplied connection
        /// options and telemetry context. The managed session is created lazily
        /// on the first <see cref="ConnectAsync"/> or service call.
        /// </summary>
        public ServerSession(
            ServerConnectionOptions options,
            ITelemetryContext telemetry)
        {
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            if (string.IsNullOrWhiteSpace(m_options.EndpointUrl))
            {
                throw new ArgumentException(
                    "EndpointUrl must be specified.", nameof(options));
            }
            m_logger = telemetry.CreateLogger<ServerSession>();
        }

        /// <inheritdoc/>
        public bool IsConnected => m_session?.Connected ?? false;

        /// <inheritdoc/>
        public event EventHandler? ModelChanged;

        /// <inheritdoc/>
        public async ValueTask ConnectAsync(CancellationToken ct = default)
        {
            ThrowIfDisposed();

            await m_connectLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                // Idempotent: only create the managed session once. A concurrent
                // caller may have established it while this call awaited the lock.
                if (m_session == null)
                {
                    m_session = await CreateSessionAsync(ct).ConfigureAwait(false);
                }
            }
            finally
            {
                m_connectLock.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask<ArrayOf<DataValue>> ReadAsync(
            ArrayOf<ReadValueId> nodesToRead,
            CancellationToken ct = default)
        {
            ISession session = await EnsureConnectedAsync(ct).ConfigureAwait(false);
            ReadResponse response = await session.ReadAsync(
                null,
                0.0,
                TimestampsToReturn.Both,
                nodesToRead,
                ct).ConfigureAwait(false);
            return response.Results;
        }

        /// <inheritdoc/>
        public async ValueTask<ArrayOf<StatusCode>> WriteAsync(
            ArrayOf<WriteValue> nodesToWrite,
            CancellationToken ct = default)
        {
            ISession session = await EnsureConnectedAsync(ct).ConfigureAwait(false);
            WriteResponse response = await session.WriteAsync(
                null,
                nodesToWrite,
                ct).ConfigureAwait(false);
            return response.Results;
        }

        /// <inheritdoc/>
        public async ValueTask<RemoteCallResult> CallAsync(
            NodeId objectId,
            NodeId methodId,
            ArrayOf<Variant> inputArguments,
            CancellationToken ct = default)
        {
            ISession session = await EnsureConnectedAsync(ct).ConfigureAwait(false);

            var request = new CallMethodRequest
            {
                ObjectId = objectId,
                MethodId = methodId,
                InputArguments = inputArguments
            };
            ArrayOf<CallMethodRequest> requests = [request];

            CallResponse response = await session.CallAsync(
                null,
                requests,
                ct).ConfigureAwait(false);

            ArrayOf<CallMethodResult> results = response.Results;
            ClientBase.ValidateResponse(results, requests);
            ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, requests);

            CallMethodResult result = results[0];
            return new RemoteCallResult(result.StatusCode, result.OutputArguments);
        }

        /// <inheritdoc/>
        public async ValueTask<IDataChangeSubscription> CreateDataChangeSubscriptionAsync(
            double publishingIntervalMs,
            CancellationToken ct = default)
        {
            ISession session = await EnsureConnectedAsync(ct).ConfigureAwait(false);
            if (!session.TryGetSubscriptionManager(out ISubscriptionManager? subscriptionManager))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNotSupported,
                    "The external server session for endpoint '{0}' does not expose a V2 " +
                    "subscription manager. Data-change subscriptions require the V2 subscription " +
                    "engine (the ManagedSession default); recreate the session with the V2 " +
                    "engine (DefaultSubscriptionEngineFactory) rather than the classic engine.",
                    m_options.EndpointUrl);
            }
            return new DataChangeSubscription(
                subscriptionManager,
                publishingIntervalMs,
                m_telemetry);
        }

        /// <inheritdoc/>
        public async ValueTask StartModelChangeMonitoringAsync(CancellationToken ct = default)
        {
            ThrowIfDisposed();
            if (Interlocked.CompareExchange(ref m_modelChangeMonitoringStarted, 1, 0) != 0)
            {
                return;
            }

            try
            {
                ISession session = await EnsureConnectedAsync(ct).ConfigureAwait(false);
                if (!session.TryGetSubscriptionManager(out ISubscriptionManager? subscriptionManager))
                {
                    // Model-change monitoring is an optional, best-effort enhancement
                    // (unlike data-change subscriptions, which are required and throw):
                    // the whole method already swallows failures and continues, so a
                    // classic-engine session simply skips it rather than faulting.
                    m_logger.LogInformation(
                        "ServerSession: model-change event monitoring is not available for " +
                        "endpoint {EndpointUrl} because the session does not expose a V2 " +
                        "subscription manager (requires the V2 subscription engine / " +
                        "DefaultSubscriptionEngineFactory). Monitoring is optional; continuing " +
                        "without it.",
                        m_options.EndpointUrl);
                    return;
                }

                var subscriptionOptions = new SubscriptionOptions
                {
                    PublishingInterval = TimeSpan.FromMilliseconds(1000),
                    PublishingEnabled = true
                };
                ISubscription subscription = subscriptionManager.Add(
                    new ModelChangeNotifier(this),
                    new SingletonOptionsMonitor<SubscriptionOptions>(subscriptionOptions));

                var itemOptions = new MonitoredItemOptions
                {
                    StartNodeId = ObjectIds.Server,
                    AttributeId = Attributes.EventNotifier,
                    SamplingInterval = TimeSpan.FromMilliseconds(-1),
                    QueueSize = 10,
                    Filter = BuildModelChangeFilter()
                };

                if (!subscription.MonitoredItems.TryAdd(
                        "ext_model_change_server",
                        new SingletonOptionsMonitor<MonitoredItemOptions>(itemOptions),
                        out IMonitoredItem? item) ||
                    item == null)
                {
                    await DisposeModelChangeSubscriptionAsync(subscription).ConfigureAwait(false);
                    m_logger.LogInformation(
                        "ServerSession: model-change event monitoring is not available.");
                    return;
                }

                await WaitForModelChangeItemAsync(subscription, item, ct).ConfigureAwait(false);
                if (!item.Created && StatusCode.IsBad(item.Error.StatusCode))
                {
                    await DisposeModelChangeSubscriptionAsync(subscription).ConfigureAwait(false);
                    m_logger.LogInformation(
                        "ServerSession: model-change event monitoring is not available ({StatusCode}).",
                        item.Error.StatusCode);
                    return;
                }
                bool disposeSubscription;
                lock (m_disposeGate)
                {
                    if (m_disposed)
                    {
                        disposeSubscription = true;
                    }
                    else
                    {
                        m_modelChangeSubscription = subscription;
                        disposeSubscription = false;
                    }
                }
                if (disposeSubscription)
                {
                    await DisposeModelChangeSubscriptionAsync(subscription).ConfigureAwait(false);
                    return;
                }
                m_logger.LogDebug(
                    "ServerSession: model-change event monitoring started on the Server object.");
            }
            catch (OperationCanceledException)
            {
                Volatile.Write(ref m_modelChangeMonitoringStarted, 0);
                throw;
            }
            catch (Exception ex)
            {
                m_logger.LogInformation(
                    ex,
                    "ServerSession: model-change event monitoring is not available.");
            }
        }

        /// <inheritdoc/>
        public async ValueTask<NodeId> ResolveNodeIdAsync(
            NodeId nodeId,
            CancellationToken ct = default)
        {
            if (!NodeBrowsePath.IsBrowsePath(nodeId))
            {
                return nodeId;
            }

            string path = nodeId.IdentifierAsString;
            if (m_resolvedPaths.TryGetValue(path, out NodeId cached))
            {
                return cached;
            }

            ISession session = await EnsureConnectedAsync(ct).ConfigureAwait(false);

            var request = new Opc.Ua.BrowsePath
            {
                StartingNode = ObjectIds.ObjectsFolder,
                RelativePath = NodeBrowsePath.ToRelativePath(nodeId)
            };
            ArrayOf<Opc.Ua.BrowsePath> requests = [request];

            TranslateBrowsePathsToNodeIdsResponse response = await session
                .TranslateBrowsePathsToNodeIdsAsync(null, requests, ct)
                .ConfigureAwait(false);

            ArrayOf<BrowsePathResult> results = response.Results;
            ClientBase.ValidateResponse(results, requests);

            BrowsePathResult result = results[0];
            if (StatusCode.IsBad(result.StatusCode) || result.Targets.IsNull || result.Targets.Count == 0)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNoMatch,
                    "Browse path '{0}' did not resolve to a node ({1}).",
                    path,
                    result.StatusCode);
            }

            NodeId resolved = ExpandedNodeId.ToNodeId(
                result.Targets[0].TargetId,
                session.MessageContext.NamespaceUris);
            m_resolvedPaths[path] = resolved;
            m_logger.LogDebug(
                "Resolved browse path '{Path}' to node {NodeId}.", path, resolved);
            return resolved;
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            ISubscription? modelChangeSubscription;
            lock (m_disposeGate)
            {
                if (m_disposed)
                {
                    return;
                }
                m_disposed = true;
                ModelChanged = null;
                modelChangeSubscription = m_modelChangeSubscription;
                m_modelChangeSubscription = null;
            }

            if (modelChangeSubscription != null)
            {
                await DisposeModelChangeSubscriptionAsync(modelChangeSubscription).ConfigureAwait(false);
            }

            ISession? session = m_session;
            m_session = null;
            if (session != null)
            {
                try
                {
                    await session.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    m_logger.LogDebug(ex,
                        "ServerSession: managed session dispose failed.");
                }
            }

            m_connectLock.Dispose();
        }

        private static EventFilter BuildModelChangeFilter()
        {
            var filter = new EventFilter();
            filter.AddSelectClause(
                ObjectTypeIds.BaseEventType,
                QualifiedName.From(BrowseNames.EventType));
            filter.WhereClause.Push(
                FilterOperator.OfType,
                Variant.From(ObjectTypeIds.GeneralModelChangeEventType));
            return filter;
        }

        private static async ValueTask DisposeModelChangeSubscriptionAsync(ISubscription subscription)
        {
            try
            {
                await subscription.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                // Best-effort cleanup; callers log the operation context.
            }
        }

        private async ValueTask WaitForModelChangeItemAsync(
            ISubscription subscription,
            IMonitoredItem item,
            CancellationToken ct)
        {
            var watch = Stopwatch.StartNew();
            TimeSpan budget = TimeSpan.FromMilliseconds(5000);

            while (!subscription.Created ||
                (!item.Created && StatusCode.IsGood(item.Error.StatusCode)))
            {
                ct.ThrowIfCancellationRequested();
                if (watch.Elapsed >= budget)
                {
                    m_logger.LogDebug(
                        "ServerSession: model-change monitored item creation is still pending.");
                    return;
                }

                await Task.Delay(s_applyPollInterval, ct).ConfigureAwait(false);
            }
        }

        private void DispatchModelChange()
        {
            long now = Stopwatch.GetTimestamp();
            long previous = Interlocked.Read(ref m_lastModelChangeTicks);
            if (previous != 0 &&
                now >= previous &&
                now - previous < s_modelChangeCoalesceTicks)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref m_lastModelChangeTicks, now, previous) != previous)
            {
                return;
            }

            ModelChanged?.Invoke(this, EventArgs.Empty);
        }

        private async ValueTask<ISession> EnsureConnectedAsync(CancellationToken ct)
        {
            ISession? session = m_session;
            if (session != null)
            {
                return session;
            }
            await ConnectAsync(ct).ConfigureAwait(false);
            return m_session ?? throw ServiceResultException.Create(
                StatusCodes.BadNotConnected,
                "External server session is not connected.");
        }

        private void ThrowIfDisposed()
        {
            lock (m_disposeGate)
            {
                if (m_disposed)
                {
                    throw new ObjectDisposedException(nameof(ServerSession));
                }
            }
        }

        private async Task<ISession> CreateSessionAsync(CancellationToken ct)
        {
            ApplicationConfiguration configuration = m_options.ApplicationConfiguration
                ?? await BuildApplicationConfigurationAsync(ct).ConfigureAwait(false);

            EndpointDescription selectedEndpoint =
                await SelectEndpointAsync(configuration, ct).ConfigureAwait(false);

            var endpoint = new ConfiguredEndpoint(
                null,
                selectedEndpoint,
                EndpointConfiguration.Create(configuration));

            IUserIdentity? identity = ResolveUserIdentity();

            ManagedSessionBuilder builder = new ManagedSessionBuilder(configuration, m_telemetry)
                .UseEndpoint(endpoint)
                .WithSessionName(m_options.SessionName)
                .WithSessionTimeout(TimeSpan.FromMilliseconds(m_options.SessionTimeout));
            if (identity != null)
            {
                builder = builder.WithUserIdentity(identity);
            }

            m_logger.LogInformation(
                "Connecting external server session to {EndpointUrl} ({SecurityMode}).",
                selectedEndpoint.EndpointUrl,
                selectedEndpoint.SecurityMode);

            return await builder.ConnectAsync(ct).ConfigureAwait(false);
        }

        private IUserIdentity? ResolveUserIdentity()
        {
            if (m_options.UserIdentity != null)
            {
                return m_options.UserIdentity;
            }
            if (!string.IsNullOrEmpty(m_options.UserName))
            {
                return new UserIdentity(
                    m_options.UserName!,
                    System.Text.Encoding.UTF8.GetBytes(m_options.Password ?? string.Empty));
            }
            return null;
        }

        private async ValueTask<EndpointDescription> SelectEndpointAsync(
            ApplicationConfiguration configuration,
            CancellationToken ct)
        {
            var requestUri = new Uri(m_options.EndpointUrl);
            var endpointConfiguration = EndpointConfiguration.Create(configuration);

            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                configuration,
                requestUri,
                endpointConfiguration,
                ct: ct).ConfigureAwait(false);

            ArrayOf<EndpointDescription> endpoints =
                await client.GetEndpointsAsync(default, ct).ConfigureAwait(false);

            EndpointDescription? selected = null;
            foreach (EndpointDescription endpoint in endpoints)
            {
                if (endpoint.EndpointUrl == null ||
                    !endpoint.EndpointUrl.StartsWith(
                        requestUri.Scheme, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (endpoint.SecurityMode != m_options.SecurityMode)
                {
                    continue;
                }
                if (m_options.SecurityPolicyUri != null &&
                    !string.Equals(
                        endpoint.SecurityPolicyUri,
                        m_options.SecurityPolicyUri,
                        StringComparison.Ordinal))
                {
                    continue;
                }
                if (selected == null || endpoint.SecurityLevel > selected.SecurityLevel)
                {
                    selected = endpoint;
                }
            }

            if (selected == null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNotFound,
                    "No endpoint at {0} matches security mode {1} / policy {2}.",
                    m_options.EndpointUrl,
                    m_options.SecurityMode,
                    m_options.SecurityPolicyUri ?? "(auto)");
            }

            // Preserve the requested host/port: discovery may advertise an
            // endpoint URL with a different host than the one the caller used.
            Uri? selectedUrl = Utils.ParseUri(selected.EndpointUrl);
            if (selectedUrl != null && selectedUrl.Scheme == requestUri.Scheme)
            {
                selected.EndpointUrl = new UriBuilder(selectedUrl)
                {
                    Host = requestUri.IdnHost,
                    Port = requestUri.Port
                }.ToString();
            }

            return selected;
        }

        private async ValueTask<ApplicationConfiguration> BuildApplicationConfigurationAsync(
            CancellationToken ct)
        {
            string pkiRoot = Path.Combine(
                AppContext.BaseDirectory, "pki", "Opc.Ua.PubSub.Adapter");

            var configuration = new ApplicationConfiguration(m_telemetry)
            {
                ApplicationName = m_options.ApplicationName,
                ApplicationType = ApplicationType.Client,
                SecurityConfiguration = new SecurityConfiguration
                {
                    ApplicationCertificate = new CertificateIdentifier
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = Path.Combine(pkiRoot, "own"),
                        SubjectName = string.Format(
                            CultureInfo.InvariantCulture,
                            "CN={0}, O=OPC Foundation",
                            m_options.ApplicationName)
                    },
                    TrustedIssuerCertificates = new CertificateTrustList
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = Path.Combine(pkiRoot, "issuer")
                    },
                    TrustedPeerCertificates = new CertificateTrustList
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = Path.Combine(pkiRoot, "trusted")
                    },
                    RejectedCertificateStore = new CertificateTrustList
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = Path.Combine(pkiRoot, "rejected")
                    },
                    AutoAcceptUntrustedCertificates = true
                },
                TransportQuotas = new TransportQuotas
                {
                    MaxMessageSize = 4 * 1024 * 1024
                },
                ClientConfiguration = new ClientConfiguration()
            };

            await configuration.ValidateAsync(ApplicationType.Client, ct).ConfigureAwait(false);
            return configuration;
        }

        private sealed class ModelChangeNotifier : ISubscriptionNotificationHandler
        {
            private readonly ServerSession m_parent;

            public ModelChangeNotifier(ServerSession parent)
            {
                m_parent = parent;
            }

            public ValueTask OnDataChangeNotificationAsync(
                ISubscription subscription,
                uint sequenceNumber,
                DateTime publishTime,
                ReadOnlyMemory<DataValueChange> notification,
                PublishState publishStateMask,
                System.Collections.Generic.IReadOnlyList<string> stringTable)
            {
                return default;
            }

            public ValueTask OnEventDataNotificationAsync(
                ISubscription subscription,
                uint sequenceNumber,
                DateTime publishTime,
                ReadOnlyMemory<EventNotification> notification,
                PublishState publishStateMask,
                System.Collections.Generic.IReadOnlyList<string> stringTable)
            {
                if (!notification.IsEmpty)
                {
                    m_parent.DispatchModelChange();
                }

                return default;
            }

            public ValueTask OnKeepAliveNotificationAsync(
                ISubscription subscription,
                uint sequenceNumber,
                DateTime publishTime,
                PublishState publishStateMask)
            {
                return default;
            }

            public ValueTask OnSubscriptionStateChangedAsync(
                ISubscription subscription,
                Opc.Ua.Client.Subscriptions.SubscriptionState state,
                PublishState publishStateMask,
                CancellationToken ct = default)
            {
                return default;
            }
        }

        private sealed class SingletonOptionsMonitor<[DynamicallyAccessedMembers(
            DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] T>
            : IOptionsMonitor<T>
        {
            public SingletonOptionsMonitor(T value)
            {
                CurrentValue = value;
            }

            public T CurrentValue { get; }

            public T Get(string? name)
            {
                return CurrentValue;
            }

            public IDisposable? OnChange(Action<T, string?> listener)
            {
                return null;
            }
        }
    }
}
