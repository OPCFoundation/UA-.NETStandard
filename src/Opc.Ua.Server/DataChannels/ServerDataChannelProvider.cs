/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.DataChannels;

namespace Opc.Ua.Server.DataChannels
{
    /// <summary>
    /// Application-scoped source authorization and accepted-flow routing. Configured flows work
    /// independently of the optional Server model and Session-control attachment.
    /// </summary>
    public sealed partial class ServerDataChannelProvider :
        IDataChannelContextFactory,
        IDataChannelAcceptedFlowSource,
        IDataChannelAuthorizer,
        IAsyncDisposable
    {
        /// <summary>
        /// Creates a provider over shared application resource accounting and an injected clock.
        /// </summary>
        public ServerDataChannelProvider(
            DataChannelSourceRegistry sources,
            DataChannelOptions options,
            DataChannelApplicationBudget budget,
            TimeProvider timeProvider,
            ITelemetryContext telemetry)
        {
            Sources = sources ?? throw new ArgumentNullException(nameof(sources));
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            m_budget = budget ?? throw new ArgumentNullException(nameof(budget));
            m_clock = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            m_logger = telemetry.CreateLogger<ServerDataChannelProvider>();
            m_grants = new DataChannelGrantStore(budget, timeProvider, options);
        }

        /// <summary>
        /// Gets the exact resource registry.
        /// </summary>
        public DataChannelSourceRegistry Sources { get; }

        /// <summary>
        /// Gets whether the application explicitly enables integrity-only UASC.
        /// Each resource must independently permit it.
        /// </summary>
        public bool AllowIntegrity => m_options.AllowIntegrity;

        /// <summary>
        /// Gets the effective finite context options.
        /// </summary>
        public DataChannelOptions Options => m_options;

        /// <summary>
        /// Raised from actual runtime transitions, never synthesized readiness.
        /// </summary>
        public event Action<DataChannelStateChangedEventArgs>? StateChanged;

        /// <summary>
        /// Raised for accepted and refused control or configured admission decisions.
        /// No grant, challenge or key material is included.
        /// </summary>
        public event Action<DataChannelControlAudit>? Audited;

        /// <summary>
        /// Raised only for a validated recipient-specific source hint.
        /// </summary>
        public event Action<DataChannelOffer>? Offered;

        /// <inheritdoc/>
        public DataChannelContext Create(IDataChannelCarrier carrier)
        {
            if (carrier == null)
            {
                throw new ArgumentNullException(nameof(carrier));
            }
            lock (m_gate)
            {
                ThrowIfDisposed();
                var context = new DataChannelContext(
                    carrier, this, m_options, m_budget, m_clock, m_telemetry);
                var registration = new ContextRegistration(context, carrier);
                registration.OnStateChanged = OnStateChanged;
                registration.OnClosed = result => OnContextClosed(registration, result);
                context.StateChanged += registration.OnStateChanged;
                context.Closed += registration.OnClosed;
                m_contexts.Add(context, registration);
                registration.Router = RouteAcceptedAsync(registration);
                PulseContexts();
                return context;
            }
        }

        /// <summary>
        /// Waits for one confirmed association with an exact peer application and carrier.
        /// This does not start HELLO, take the acceptance queue or guess between multiple live contexts.
        /// The returned context is borrowed from its transport.
        /// </summary>
        public async ValueTask<DataChannelContext> WaitForContextAsync(
            string peerApplicationUri,
            DataChannelCarrierKind carrierKind,
            CancellationToken cancellationToken = default)
        {
            if (!Uri.TryCreate(peerApplicationUri, UriKind.Absolute, out _))
            {
                throw new ArgumentException(
                    "An absolute peer application URI is required.", nameof(peerApplicationUri));
            }
            if (carrierKind is not (DataChannelCarrierKind.DedicatedUasc or DataChannelCarrierKind.SharedUasc or
                DataChannelCarrierKind.Quic or DataChannelCarrierKind.WebTransport))
            {
                throw new ArgumentOutOfRangeException(nameof(carrierKind));
            }
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ContextRegistration? selected = null;
                Task changed;
                lock (m_gate)
                {
                    ThrowIfDisposed();
                    foreach (ContextRegistration candidate in m_contexts.Values)
                    {
                        if (candidate.Closed || candidate.Carrier.Kind != carrierKind ||
                            candidate.Carrier.Identity.PeerApplicationUri != peerApplicationUri)
                        {
                            continue;
                        }
                        if (selected != null)
                        {
                            throw new DataChannelException(DataChannelResult.InvalidState);
                        }
                        selected = candidate;
                    }
                    changed = m_contextChanged.Task;
                }
                if (selected == null)
                {
                    await changed.WaitAsync(cancellationToken).ConfigureAwait(false);
                    continue;
                }
                await selected.Context.WaitForReadyAsync(cancellationToken).ConfigureAwait(false);
                lock (m_gate)
                {
                    ThrowIfDisposed();
                    if (selected.Closed)
                    {
                        throw new DataChannelException(DataChannelResult.Closed);
                    }
                    foreach (ContextRegistration candidate in m_contexts.Values)
                    {
                        if (!ReferenceEquals(candidate, selected) && !candidate.Closed &&
                            candidate.Carrier.Kind == carrierKind &&
                            candidate.Carrier.Identity.PeerApplicationUri == peerApplicationUri)
                        {
                            throw new DataChannelException(DataChannelResult.InvalidState);
                        }
                    }
                    return selected.Context;
                }
            }
        }

        /// <summary>
        /// Waits for a ready remotely opened flow for this exact resource and payload protocol.
        /// This provider is the sole consumer of each registered context's acceptance queue.
        /// </summary>
        public async ValueTask<DataChannel?> AcceptAsync(
            string resourceUri,
            string protocol,
            DataChannelCarrierKind carrierKind,
            CancellationToken cancellationToken = default)
        {
            if (!Sources.TryGet(resourceUri, out DataChannelSource? source) ||
                !DataChannelSource.Contains(source.SupportedProtocols, protocol))
            {
                throw new ArgumentException("The resource and payload protocol must be registered.");
            }
            Channel<DataChannel> route = GetRoute(source, protocol, carrierKind);
            while (await route.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (route.Reader.TryRead(out DataChannel? channel))
                {
                    if (channel.State is not (DataChannelState.Closed or DataChannelState.Faulted))
                    {
                        return channel;
                    }
                    await channel.DisposeAsync().ConfigureAwait(false);
                }
            }
            return null;
        }

        /// <summary>
        /// Rechecks live policy without renewing Session activity or closing a control transport.
        /// </summary>
        public async ValueTask RecheckAuthorizationAsync(CancellationToken cancellationToken = default)
        {
            ExpireProvisionalState();
            foreach (ContextRegistration registration in GetContexts())
            {
                await registration.Context.RecheckAuthorizationAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Invalidates contexts whose validated application key was revoked or replaced.
        /// It does not match Session control-connection identifiers.
        /// </summary>
        public async ValueTask InvalidatePeerAsync(
            string applicationUri,
            ByteString keyThumbprint,
            CancellationToken cancellationToken = default)
        {
            if (applicationUri == null)
            {
                throw new ArgumentNullException(nameof(applicationUri));
            }
            foreach (ContextRegistration registration in GetContexts())
            {
                DataChannelPeerIdentity peer = registration.Carrier.Identity;
                if (peer.PeerApplicationUri == applicationUri && peer.PeerKeyThumbprint == keyThumbprint)
                {
                    await registration.Context.InvalidateAsync(
                        DataChannelResult.SecurityFailure, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Invalidates all authenticated contexts after trust evidence can no longer be relied on.
        /// New connections must pass the carrier's current certificate validation again.
        /// </summary>
        public async ValueTask InvalidateContextsAsync(CancellationToken cancellationToken = default)
        {
            foreach (ContextRegistration registration in GetContexts())
            {
                await registration.Context.InvalidateAsync(
                    DataChannelResult.SecurityFailure, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            ContextRegistration[] contexts;
            lock (m_gate)
            {
                if (m_disposed)
                {
                    return;
                }
                m_disposed = true;
                PulseContexts();
                contexts = [.. m_contexts.Values];
                foreach (Channel<DataChannel> route in m_routes.Values)
                {
                    route.Writer.TryComplete();
                }
            }
            m_shutdown.Cancel();
            foreach (ContextRegistration registration in contexts)
            {
                await registration.Context.DisposeAsync().ConfigureAwait(false);
                await registration.Router.ConfigureAwait(false);
                registration.Context.StateChanged -= registration.OnStateChanged;
                registration.Context.Closed -= registration.OnClosed;
            }
            lock (m_gate)
            {
                foreach (SessionFlow flow in m_sessionFlows.Values)
                {
                    flow.Dispose();
                }
                m_sessionFlows.Clear();
            }
            m_grants.Dispose();
            m_shutdown.Dispose();
            m_controlGate.Dispose();
        }

        /// <summary>
        /// Attaches optional Session and model services without altering configured authorization.
        /// </summary>
        internal void AttachServer(IServerInternal server)
        {
            if (server == null)
            {
                throw new ArgumentNullException(nameof(server));
            }
            lock (m_gate)
            {
                if (m_server != null && !ReferenceEquals(m_server, server))
                {
                    throw new InvalidOperationException("The provider is already attached to a server.");
                }
                m_server = server;
            }
        }

        /// <summary>
        /// Removes only optional Session state when its hosting server stops.
        /// </summary>
        internal void DetachServer(IServerInternal? server = null)
        {
            NodeId[] sessions;
            lock (m_gate)
            {
                if (server != null && !ReferenceEquals(m_server, server))
                {
                    return;
                }
                var ids = new HashSet<NodeId>();
                foreach (SessionFlow flow in m_sessionFlows.Values)
                {
                    ids.Add(flow.Session.Id);
                }
                sessions = [.. ids];
                m_server = null;
            }
            foreach (NodeId sessionId in sessions)
            {
                RevokeSession(sessionId);
            }
        }

        /// <summary>
        /// Revokes grants synchronously before scheduling reset of dependent flows.
        /// </summary>
        internal void RevokeSession(NodeId sessionId)
        {
            m_grants.RevokeSession(sessionId);
            List<DataChannel> channels = [];
            List<SessionFlow> revoked = [];
            lock (m_gate)
            {
                foreach (KeyValuePair<FlowKey, SessionFlow> item in m_sessionFlows)
                {
                    SessionFlow flow = item.Value;
                    if (flow.Session.Id == sessionId)
                    {
                        flow.Revoked = true;
                        revoked.Add(flow);
                        flow.PendingOperation = null;
                        if (flow.Channel != null)
                        {
                            channels.Add(flow.Channel);
                        }
                        else
                        {
                            m_admitted.Remove(item.Key);
                        }
                    }
                }
            }
            foreach (SessionFlow flow in revoked)
            {
                flow.Revoke();
            }
            foreach (DataChannel channel in channels)
            {
                _ = ResetRevokedAsync(channel);
            }
        }

        /// <summary>
        /// Rechecks identity continuity on reactivation without depending on the control channel id.
        /// </summary>
        internal void SessionActivated(ISession session)
        {
            bool changed = false;
            lock (m_gate)
            {
                foreach (SessionFlow flow in m_sessionFlows.Values)
                {
                    if (flow.Session.Id == session.Id && !HasSameUser(flow))
                    {
                        changed = true;
                        break;
                    }
                }
            }
            if (changed)
            {
                RevokeSession(session.Id);
            }
            else
            {
                _ = ObserveRecheckAsync();
            }
        }

        /// <summary>
        /// Safely schedules an explicit policy recheck from synchronous manager notifications.
        /// </summary>
        internal async Task ObserveRecheckAsync()
        {
            await Task.Yield();
            try
            {
                await RecheckAuthorizationAsync(m_shutdown.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (m_shutdown.IsCancellationRequested)
            {
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                m_logger.BackgroundOperationFailed(exception);
            }
        }

        /// <summary>
        /// Routes only ready flows; a full route refuses that flow rather than blocking other resources.
        /// </summary>
        private async Task RouteAcceptedAsync(ContextRegistration registration)
        {
            try
            {
                while (await registration.Context.AcceptAsync(m_shutdown.Token).ConfigureAwait(false)
                    is { } channel)
                {
                    DataChannelParametersDataType parameters = channel.Parameters;
                    if (!Sources.TryGet(parameters.ResourceUri ?? string.Empty, out DataChannelSource? source) ||
                        !GetRoute(source, parameters.Protocol ?? string.Empty, registration.Carrier.Kind)
                            .Writer.TryWrite(channel))
                    {
                        await channel.ResetAsync(DataChannelResult.WouldBlock, m_shutdown.Token)
                            .ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException) when (m_shutdown.IsCancellationRequested)
            {
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                m_logger.BackgroundOperationFailed(exception);
            }
        }

        /// <summary>
        /// Gets a bounded independent acceptance queue for a registered resource/protocol pair.
        /// </summary>
        private Channel<DataChannel> GetRoute(
            DataChannelSource source,
            string protocol,
            DataChannelCarrierKind carrier)
        {
            lock (m_gate)
            {
                var key = (source.ResourceUri, protocol, carrier);
                if (!m_routes.TryGetValue(key, out Channel<DataChannel>? route))
                {
                    ThrowIfDisposed();
                    route = Channel.CreateBounded<DataChannel>(new BoundedChannelOptions(source.MaxFlows)
                    {
                        SingleReader = false,
                        SingleWriter = false,
                        FullMode = BoundedChannelFullMode.Wait,
                        AllowSynchronousContinuations = false
                    });
                    m_routes.Add(key, route);
                }
                return route;
            }
        }

        /// <summary>
        /// Captures contexts without calling external code under the provider lock.
        /// </summary>
        private ContextRegistration[] GetContexts()
        {
            lock (m_gate)
            {
                return [.. m_contexts.Values];
            }
        }

        /// <summary>
        /// Records actual runtime state and forwards a detached snapshot to observers.
        /// </summary>
        private void OnStateChanged(DataChannelStateChangedEventArgs change)
        {
            DataChannelStatusDataType status = change.Status;
            var key = new FlowKey(status.ContextId, status.Generation, status.ChannelId);
            lock (m_gate)
            {
                if (m_sessionFlows.TryGetValue(key, out SessionFlow? flow))
                {
                    flow.Channel = change.Channel;
                }
                if (status.State is DataChannelState.Closed or DataChannelState.Faulted)
                {
                    m_admitted.Remove(key);
                }
            }
            try
            {
                StateChanged?.Invoke(change);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                m_logger.BackgroundOperationFailed(exception);
            }
        }

        /// <summary>
        /// Invalidates all grants of a lost data association, even when no flow was opened.
        /// </summary>
        private void OnContextClosed(ContextRegistration registration, DataChannelResult result)
        {
            lock (m_gate)
            {
                m_contexts.Remove(registration.Context);
                PulseContexts();
                registration.Closed = true;
                if (registration.Identity is { } identity)
                {
                    m_grants.RevokeContext(identity.ContextId);
                    List<FlowKey> sessionKeys = [];
                    foreach (KeyValuePair<FlowKey, SessionFlow> item in m_sessionFlows)
                    {
                        if (item.Key.ContextId == identity.ContextId && item.Key.Generation == identity.Generation)
                        {
                            item.Value.Revoked = true;
                            item.Value.PendingOperation = null;
                            sessionKeys.Add(item.Key);
                        }
                    }
                    foreach (FlowKey key in sessionKeys)
                    {
                        m_sessionFlows[key].Dispose();
                        m_sessionFlows.Remove(key);
                    }
                    List<FlowKey> keys = [];
                    foreach (FlowKey key in m_admitted.Keys)
                    {
                        if (key.ContextId == identity.ContextId && key.Generation == identity.Generation)
                        {
                            keys.Add(key);
                        }
                    }
                    foreach (FlowKey key in keys)
                    {
                        m_admitted.Remove(key);
                    }
                }
            }
            registration.Context.StateChanged -= registration.OnStateChanged;
            registration.Context.Closed -= registration.OnClosed;
        }

        /// <summary>
        /// Stops one revoked Session flow without closing a configured neighbour or the context.
        /// </summary>
        private async Task ResetRevokedAsync(DataChannel channel)
        {
            try
            {
                await channel.ResetAsync(DataChannelResult.NotAuthorized).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                m_logger.BackgroundOperationFailed(exception);
            }
        }

        /// <summary>
        /// Emits security-attributed decisions without revealing authorization material.
        /// </summary>
        private void Audit(DataChannelControlAudit audit)
        {
            try
            {
                Audited?.Invoke(audit);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                m_logger.BackgroundOperationFailed(exception);
            }
        }

        /// <summary>
        /// Wakes association waiters without running application continuations under synchronization.
        /// </summary>
        private void PulseContexts()
        {
            TaskCompletionSource<bool> previous = m_contextChanged;
            m_contextChanged = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            previous.TrySetResult(true);
        }

        /// <summary>
        /// Rejects use after disposal on every supported framework.
        /// </summary>
        private void ThrowIfDisposed()
        {
            if (m_disposed)
            {
                throw new ObjectDisposedException(nameof(ServerDataChannelProvider));
            }
        }

        /// <summary>
        /// Identifies a channel only within its authenticated context and generation.
        /// </summary>
        private readonly record struct FlowKey(Guid ContextId, ulong Generation, uint ChannelId);

        /// <summary>
        /// Retains the original publishers so teardown never consults replacement server managers.
        /// </summary>
        private sealed class ContextRegistration(DataChannelContext context, IDataChannelCarrier carrier)
        {
            public DataChannelContext Context { get; } = context;
            public IDataChannelCarrier Carrier { get; } = carrier;
            public DataChannelContextIdentity? Identity { get; set; }
            public HashSet<uint> IssuedChannelIds { get; } = [];
            public Action<DataChannelStateChangedEventArgs> OnStateChanged { get; set; } = null!;
            public Action<DataChannelResult> OnClosed { get; set; } = null!;
            public Task Router { get; set; } = Task.CompletedTask;
            public bool Closed { get; set; }
        }

        private readonly Lock m_gate = new();
        private readonly SemaphoreSlim m_controlGate = new(1, 1);
        private readonly DataChannelOptions m_options;
        private readonly DataChannelApplicationBudget m_budget;
        private readonly TimeProvider m_clock;
        private readonly ITelemetryContext m_telemetry;
        private readonly ILogger m_logger;
        private readonly DataChannelGrantStore m_grants;
        private readonly CancellationTokenSource m_shutdown = new();
        private readonly Dictionary<DataChannelContext, ContextRegistration> m_contexts = [];
        private readonly Dictionary<(string Resource, string Protocol, DataChannelCarrierKind Carrier),
            Channel<DataChannel>> m_routes = [];
        private readonly Dictionary<FlowKey, SessionFlow> m_sessionFlows = [];
        private readonly Dictionary<FlowKey, string> m_admitted = [];
        private IServerInternal? m_server;
        private bool m_disposed;
        private TaskCompletionSource<bool> m_contextChanged =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>
    /// A minimal audit record containing attribution and outcomes, never credential material.
    /// </summary>
    public sealed record DataChannelControlAudit(
        string Operation,
        ExpandedNodeId SourceNodeId,
        string ResourceUri,
        string PeerApplicationUri,
        Guid ContextId,
        ulong Generation,
        uint ChannelId,
        NodeId SessionId,
        uint OperationId,
        uint Revision,
        DataChannelResult Result);

    /// <summary>
    /// A recipient-scoped hint which neither allocates nor authorizes a channel.
    /// </summary>
    public sealed class DataChannelOffer
    {
        /// <summary>
        /// Creates a source hint after current recipient permissions have been checked.
        /// </summary>
        internal DataChannelOffer(
            NodeId sessionId,
            ExpandedNodeId sourceNodeId,
            DataChannelParametersDataType parameters,
            DateTimeUtc expiresAt)
        {
            RecipientSessionId = sessionId;
            SourceNodeId = sourceNodeId;
            m_parameters = DataChannelControlModel.Copy(parameters);
            ExpiresAt = expiresAt;
        }

        /// <summary>
        /// Gets the sole permitted recipient.
        /// </summary>
        public NodeId RecipientSessionId { get; }

        /// <summary>
        /// Gets the source Object, not a Session credential.
        /// </summary>
        public ExpandedNodeId SourceNodeId { get; }

        /// <summary>
        /// Gets the independent resource identity.
        /// </summary>
        public string ResourceUri => m_parameters.ResourceUri ?? string.Empty;

        /// <summary>
        /// Gets the proposed, still unauthorized descriptor.
        /// </summary>
        public DataChannelParametersDataType Parameters => DataChannelControlModel.Copy(m_parameters);

        /// <summary>
        /// Gets the hint's issuer-clock expiry.
        /// </summary>
        public DateTimeUtc ExpiresAt { get; }

        private readonly DataChannelParametersDataType m_parameters;
    }

    /// <summary>
    /// Source-generated provider logging deliberately excludes grants, challenges and keys.
    /// </summary>
    internal static partial class ServerDataChannelProviderLog
    {
        /// <summary>
        /// Records an isolated background observer failure.
        /// </summary>
        [LoggerMessage(EventId = ServerEventIds.DataChannels, Level = LogLevel.Warning,
            Message = "Data channel background observer failed.")]
        internal static partial void BackgroundOperationFailed(this ILogger logger, Exception exception);
    }
}
