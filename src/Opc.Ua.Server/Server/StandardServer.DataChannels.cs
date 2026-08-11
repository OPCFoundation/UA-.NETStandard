/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Bindings;

#pragma warning disable CS1591
            #pragma warning disable CA2000

namespace Opc.Ua.Server
{
    /// <summary>
    /// Optional transport-side binding for the DataChannel Service Set.
    /// </summary>
    public interface IServerDataChannelTransport
    {
        bool TryGetManager(
            SecureChannelContext secureChannelContext,
            DataChannelServerCapabilities capabilities,
            ITelemetryContext telemetry,
            out DataChannelManager manager,
            out uint maxFrameSize,
            out bool isReliable);

        ValueTask<ulong> AllocateServerStreamAsync(
            SecureChannelContext secureChannelContext,
            uint channelId,
            DataChannelDirection direction,
            CancellationToken ct);

        ValueTask BindClientStreamAsync(
            SecureChannelContext secureChannelContext,
            uint channelId,
            ulong streamId,
            DataChannelDirection direction,
            CancellationToken ct);

        void AbortSecureChannel(SecureChannelContext secureChannelContext, StatusCode reason);
    }

    public partial class StandardServer
    {
        public DataChannelSourceRegistry DataChannelSources { get; } = new();

        public DataChannelServerCapabilities DataChannelCapabilities { get; set; } = new()
        {
            MaxDataChannels = 16,
            MaxFrameSize = 64 * 1024,
            MaxCreditPerChannel = 1024 * 1024,
            SupportedDeliveryModes = [DataChannelDeliveryMode.ReliableOrdered],
            SupportedTransportProfileUris = [Profiles.UaTcpTransport]
        };

        public IDataChannelAuthorizer? DataChannelAuthorizer { get; set; }

        public IDataChannelAuditor? DataChannelAuditor { get; set; }

        public IServerDataChannelTransport? DataChannelTransport { get; set; }

        public TimeSpan DataChannelAuthorizationRecheckInterval { get; set; } =
            TimeSpan.FromMilliseconds(DataChannelConstants.DefaultAuthorizationRecheckInterval);

        /// <inheritdoc/>
        public override async ValueTask<OpenDataChannelResponse> OpenDataChannelAsync(
            SecureChannelContext secureChannelContext,
            RequestHeader? requestHeader,
            NodeId sourceNodeId,
            uint offerId,
            ulong transportChannelId,
            DataChannelParametersDataType? requestedParameters,
            RequestLifetime requestLifetime)
        {
            OperationContext context = await ValidateRequestAsync(
                secureChannelContext,
                requestHeader,
                RequestType.OpenDataChannel,
                requestLifetime).ConfigureAwait(false);

            try
            {
                DataChannelServiceHandler handler = GetDataChannelHandler(secureChannelContext);
                OpenDataChannelResponse response = await handler.OpenDataChannelAsync(
                    CreateDataChannelRequestContext(context, secureChannelContext, transportChannelId),
                    sourceNodeId,
                    offerId,
                    requestedParameters,
                    requestLifetime.CancellationToken).ConfigureAwait(false);

                response.ResponseHeader = CreateResponse(requestHeader, context.StringTable);
                handler.OnResponseSent(response.ChannelId);
                return response;
            }
            catch (ServiceResultException e)
            {
                throw TranslateException(context, e);
            }
            finally
            {
                OnRequestComplete(context);
            }
        }

                    #pragma warning restore CA2000
        #pragma warning restore CS1591

        /// <inheritdoc/>
        public override async ValueTask<ModifyDataChannelResponse> ModifyDataChannelAsync(
            SecureChannelContext secureChannelContext,
            RequestHeader? requestHeader,
            uint channelId,
            DataChannelParametersDataType? requestedParameters,
            RequestLifetime requestLifetime)
        {
            OperationContext context = await ValidateRequestAsync(
                secureChannelContext,
                requestHeader,
                RequestType.ModifyDataChannel,
                requestLifetime).ConfigureAwait(false);

            try
            {
                ModifyDataChannelResponse response = await GetDataChannelHandler(secureChannelContext)
                    .ModifyDataChannelAsync(
                        CreateDataChannelRequestContext(context, secureChannelContext, 0),
                        channelId,
                        requestedParameters,
                        requestLifetime.CancellationToken).ConfigureAwait(false);

                response.ResponseHeader = CreateResponse(requestHeader, context.StringTable);
                return response;
            }
            catch (ServiceResultException e)
            {
                throw TranslateException(context, e);
            }
            finally
            {
                OnRequestComplete(context);
            }
        }

        /// <inheritdoc/>
        public override async ValueTask<CloseDataChannelResponse> CloseDataChannelAsync(
            SecureChannelContext secureChannelContext,
            RequestHeader? requestHeader,
            uint channelId,
            StatusCode reason,
            bool deleteQueued,
            RequestLifetime requestLifetime)
        {
            OperationContext context = await ValidateRequestAsync(
                secureChannelContext,
                requestHeader,
                RequestType.CloseDataChannel,
                requestLifetime).ConfigureAwait(false);

            try
            {
                CloseDataChannelResponse response = await GetDataChannelHandler(secureChannelContext)
                    .CloseDataChannelAsync(
                        CreateDataChannelRequestContext(context, secureChannelContext, 0),
                        channelId,
                        reason,
                        deleteQueued,
                        requestLifetime.CancellationToken).ConfigureAwait(false);

                response.ResponseHeader = CreateResponse(requestHeader, context.StringTable);
                return response;
            }
            catch (ServiceResultException e)
            {
                throw TranslateException(context, e);
            }
            finally
            {
                OnRequestComplete(context);
            }
        }

        /// <summary>
        /// Aborts data channels authorized by a Session as it closes.
        /// </summary>
        protected virtual void OnDataChannelSessionClosing(ISession session, SessionEventReason reason)
        {
            AbortDataChannelsOfSession(session.Id, StatusCodes.BadSessionClosed);
            ReapDataChannelStates();
        }

        /// <summary>
        /// Releases the per-SecureChannel data channel state once no Session
        /// remains on that SecureChannel.
        /// </summary>
        /// <remarks>
        /// The state holds a <see cref="DataChannelManager"/> with a running
        /// scheduler, so retaining one per SecureChannel ever seen would let
        /// any peer that can open and close a SecureChannel accumulate them
        /// without bound — and a data channel cannot be authorized on a
        /// SecureChannel with no Session anyway, so nothing is lost by
        /// releasing it.
        /// </remarks>
        private void ReapDataChannelStates()
        {
            if (m_dataChannelStates.IsEmpty)
            {
                return;
            }

            var live = new HashSet<string>(StringComparer.Ordinal);

            foreach (ISession candidate in ServerInternal.SessionManager.GetSessions())
            {
                string? id = candidate?.SecureChannelId;

                if (!string.IsNullOrEmpty(id))
                {
                    live.Add(id!);
                }
            }

            foreach (string secureChannelId in m_dataChannelStates.Keys)
            {
                if (live.Contains(secureChannelId))
                {
                    continue;
                }

                if (m_dataChannelStates.TryRemove(
                    secureChannelId,
                    out DataChannelSecureChannelState? state) && state != null)
                {
                    state.Manager.ChannelStateChanged -= OnDataChannelStateChanged;
                    state.Manager.AbortAll(StatusCodes.BadSecureChannelClosed);
                    _ = state.Manager.DisposeAsync().AsTask();
                }
            }
        }

        /// <summary>
        /// Aborts data channels when a Session changes identity or SecureChannel.
        /// </summary>
        protected virtual void OnDataChannelSessionActivated(ISession session, SessionEventReason reason)
        {
            AbortDataChannelsOfSession(session.Id, StatusCodes.BadUserAccessDenied);
            _ = RecheckDataChannelAuthorizationAsync(CancellationToken.None).AsTask();
        }

        /// <summary>
        /// Rechecks open data-channel authorization after Role configuration changes.
        /// </summary>
        protected virtual void OnDataChannelRoleConfigurationChanged(
            object? sender,
            RoleConfigurationChangedEventArgs e)
        {
            _ = RecheckDataChannelAuthorizationAsync(CancellationToken.None).AsTask();
        }

        /// <summary>
        /// Re-evaluates authorization for every open data channel.
        /// </summary>
        protected virtual async ValueTask<int> RecheckDataChannelAuthorizationAsync(
            CancellationToken ct)
        {
            int aborted = 0;
            foreach (DataChannelSecureChannelState state in m_dataChannelStates.Values)
            {
                aborted += await state.Handler.RecheckAuthorizationAsync(
                    sessionId => CreateRecheckContext(state, sessionId),
                    ct).ConfigureAwait(false);
            }

            return aborted;
        }

        private void InitializeDataChannelServices()
        {
            ServerInternal.SessionManager.SessionClosing += OnDataChannelSessionClosing;
            ServerInternal.SessionManager.SessionActivated += OnDataChannelSessionActivated;
            ServerInternal.RoleManager.RoleConfigurationChanged += OnDataChannelRoleConfigurationChanged;
            m_dataChannelAuthorizationTimer = TimeProvider.CreateTimer(
                _ => _ = RecheckDataChannelAuthorizationAsync(CancellationToken.None).AsTask(),
                null,
                DataChannelAuthorizationRecheckInterval,
                DataChannelAuthorizationRecheckInterval);
        }

        private void ShutdownDataChannelServices()
        {
            m_dataChannelAuthorizationTimer?.Dispose();
            m_dataChannelAuthorizationTimer = null;

            try
            {
                ServerInternal.SessionManager.SessionClosing -= OnDataChannelSessionClosing;
                ServerInternal.SessionManager.SessionActivated -= OnDataChannelSessionActivated;
                ServerInternal.RoleManager.RoleConfigurationChanged -= OnDataChannelRoleConfigurationChanged;
            }
            catch (ServiceResultException)
            {
            }

            foreach (DataChannelSecureChannelState state in m_dataChannelStates.Values)
            {
                state.Manager.AbortAll(StatusCodes.BadServerHalted);
            }

            m_dataChannelStates.Clear();
        }

        private DataChannelServiceHandler GetDataChannelHandler(
            SecureChannelContext secureChannelContext)
        {
            return m_dataChannelStates.GetOrAdd(
                secureChannelContext.SecureChannelId,
                _ => CreateDataChannelState(secureChannelContext)).Handler;
        }

        private DataChannelSecureChannelState CreateDataChannelState(
            SecureChannelContext secureChannelContext)
        {
            DataChannelManager manager;
            uint maxFrameSize;
            bool isReliable;

            // CA2000 cannot follow manager ownership through the transport out parameter
            // and the state record that owns the manager.
            // TODO: Replace the out-parameter contract with an ownership-bearing result type.
#pragma warning disable CA2000
            if (DataChannelTransport?.TryGetManager(
                secureChannelContext,
                DataChannelCapabilities,
                ServerInternal.Telemetry,
                out manager!,
                out maxFrameSize,
                out isReliable) != true)
            {
                var transport = new ServiceOnlyDataChannelTransport(
                    DataChannelCapabilities.MaxFrameSize,
                    ServerInternal.Telemetry,
                    TimeProvider);
                manager = new DataChannelManager(
                    transport,
                    isServer: true,
                    ServerInternal.Telemetry,
                    DataChannelCapabilities.MaxDataChannels,
                    DataChannelCapabilities.MaxCreditPerChannel);
                maxFrameSize = DataChannelCapabilities.MaxFrameSize;
                isReliable = true;
            }

            var state = new DataChannelSecureChannelState(
                secureChannelContext,
                manager,
                new DataChannelServiceHandler(
                    manager,
                    DataChannelSources,
                    DataChannelCapabilities,
                    DataChannelAuthorizer ?? new ReadEquivalentDataChannelAuthorizer(ServerInternal),
                    DataChannelAuditor ?? new ServerDataChannelAuditor(ServerInternal),
                    new ServerDataChannelStreamAllocator(this, secureChannelContext),
                    TimeProvider),
                maxFrameSize,
                isReliable);
#pragma warning restore CA2000

            manager.ChannelStateChanged += OnDataChannelStateChanged;
            return state;
        }

        private DataChannelRequestContext CreateDataChannelRequestContext(
            OperationContext context,
            SecureChannelContext secureChannelContext,
            ulong transportChannelId)
        {
            DataChannelSecureChannelState state = m_dataChannelStates.GetOrAdd(
                secureChannelContext.SecureChannelId,
                _ => CreateDataChannelState(secureChannelContext));

            return new DataChannelRequestContext
            {
                SessionId = context.Session.Id,
                IsSessionActivated = context.Session.Activated,
                SecurityMode = secureChannelContext.EndpointDescription?.SecurityMode ??
                    MessageSecurityMode.Invalid,
                TransportProfileUri = secureChannelContext.EndpointDescription?.TransportProfileUri ??
                    Profiles.UaTcpTransport,
                TransportChannelId = transportChannelId,
                TransportIsReliable = state.TransportIsReliable,
                TransportMaxFrameSize = state.TransportMaxFrameSize,
                ClientAuditEntryId = context.AuditEntryId,
                ClientUserId = context.UserIdentity?.DisplayName
            };
        }

        private DataChannelRequestContext? CreateRecheckContext(
            DataChannelSecureChannelState state,
            NodeId sessionId)
        {
            foreach (ISession session in ServerInternal.SessionManager.GetSessions())
            {
                if (session.Id == sessionId)
                {
                    return new DataChannelRequestContext
                    {
                        SessionId = session.Id,
                        IsSessionActivated = session.Activated,
                        SecurityMode = state.SecureChannel.EndpointDescription?.SecurityMode ??
                            MessageSecurityMode.Invalid,
                        TransportProfileUri =
                            state.SecureChannel.EndpointDescription?.TransportProfileUri ??
                            Profiles.UaTcpTransport,
                        TransportIsReliable = state.TransportIsReliable,
                        TransportMaxFrameSize = state.TransportMaxFrameSize,
                        ClientUserId = session.EffectiveIdentity?.DisplayName
                    };
                }
            }

            return null;
        }

        private void AbortDataChannelsOfSession(NodeId sessionId, StatusCode reason)
        {
            foreach (DataChannelSecureChannelState state in m_dataChannelStates.Values)
            {
                state.Handler.AbortChannelsOfSession(sessionId, reason);
            }
        }

        private void OnDataChannelStateChanged(
            object? sender,
            DataChannelStateChangedEventArgs e)
        {
            var values = DataChannelModel.BuildStateChangeEvent(e);
            var ev = new DataChannelStateChangeEventState(null);
            ev.SetChildValue(ServerInternal.DefaultSystemContext, BrowseNames.ChannelId, values.ChannelId, false);
            ev.SetChildValue(ServerInternal.DefaultSystemContext, BrowseNames.State, (int)values.State, false);
            ev.SetChildValue(ServerInternal.DefaultSystemContext, BrowseNames.Status, values.Status, false);
            ServerInternal.ReportEvent(ServerInternal.DefaultSystemContext, ev);
        }

        private sealed record DataChannelSecureChannelState(
            SecureChannelContext SecureChannel,
            DataChannelManager Manager,
            DataChannelServiceHandler Handler,
            uint TransportMaxFrameSize,
            bool TransportIsReliable);

        private sealed class ServerDataChannelStreamAllocator(
            StandardServer server,
            SecureChannelContext secureChannelContext) : IDataChannelTransportStreamAllocator
        {
            public ValueTask<ulong> AllocateServerStreamAsync(
                DataChannelRequestContext context,
                uint channelId,
                DataChannelDirection direction,
                CancellationToken ct)
            {
                IServerDataChannelTransport? transport = server.DataChannelTransport;
                if (transport == null)
                {
                    throw new ServiceResultException(StatusCodes.BadDataChannelTransportUnsupported);
                }

                return transport.AllocateServerStreamAsync(
                    secureChannelContext,
                    channelId,
                    direction,
                    ct);
            }

            public ValueTask BindClientStreamAsync(
                DataChannelRequestContext context,
                uint channelId,
                ulong streamId,
                DataChannelDirection direction,
                CancellationToken ct)
            {
                IServerDataChannelTransport? transport = server.DataChannelTransport;
                if (transport == null)
                {
                    throw new ServiceResultException(StatusCodes.BadDataChannelTransportUnsupported);
                }

                return transport.BindClientStreamAsync(
                    secureChannelContext,
                    channelId,
                    streamId,
                    direction,
                    ct);
            }
        }

        /// <summary>
        /// Authorizes a data channel exactly as a Read of the same content
        /// would be authorized.
        /// </summary>
        /// <remarks>
        /// Part 4 errata §7.2 requires that a Server "shall not grant a data
        /// channel where it would refuse a Read of the same content", and
        /// that the decision be re-evaluated rather than granted once. This
        /// resolves the Session, builds an OperationContext that carries the
        /// request SecureChannel security properties, and applies the same
        /// permission metadata composition as <see cref="MasterNodeManager"/>:
        /// RolePermissions/UserRolePermissions first, then AccessRestrictions.
        /// It fails closed: a Session that cannot be resolved, a source with
        /// no owning NodeManager, or any error during validation denies the
        /// request. Registry-only sources therefore require an explicit
        /// <see cref="DataChannelAuthorizer"/> configured by the application.
        /// </remarks>
        private sealed class ReadEquivalentDataChannelAuthorizer(IServerInternal server)
            : IDataChannelAuthorizer
        {
            public async ValueTask<bool> IsAuthorizedAsync(
                DataChannelRequestContext context,
                NodeId sourceNodeId,
                CancellationToken ct)
            {
                if (context == null || sourceNodeId.IsNull)
                {
                    return false;
                }

                try
                {
                    ISession? session = null;

                    foreach (ISession candidate in server.SessionManager.GetSessions())
                    {
                        if (candidate != null && candidate.Id == context.SessionId)
                        {
                            session = candidate;
                            break;
                        }
                    }

                    if (session == null)
                    {
                        return false;
                    }

                    (object? nodeHandle, IAsyncNodeManager? nodeManager) = await server.NodeManager
                        .GetManagerHandleAsync(sourceNodeId, ct)
                        .ConfigureAwait(false);

                    if (nodeManager == null || nodeHandle == null)
                    {
                        return false;
                    }

                    using var operationContext = CreateReadOperationContext(context, session);
                    NodeMetadata? nodeMetadata = await nodeManager.GetPermissionMetadataAsync(
                            operationContext,
                            nodeHandle,
                            BrowseResultMask.NodeClass,
                            [],
                            permissionsOnly: true,
                            ct)
                        .ConfigureAwait(false);

                    nodeMetadata ??= await nodeManager.GetNodeMetadataAsync(
                            operationContext,
                            nodeHandle,
                            BrowseResultMask.NodeClass,
                            ct)
                        .ConfigureAwait(false);

                    if (nodeMetadata == null)
                    {
                        // Without metadata there is nothing to evaluate, and
                        // an unevaluated source is not an authorized one.
                        // Granting here would reopen the fail-open path that
                        // treating an unknown Node as permitted created.
                        return false;
                    }

                    ServiceResult result = MasterNodeManager.ValidateRolePermissions(
                        operationContext,
                        nodeMetadata,
                        PermissionType.Read);

                    if (ServiceResult.IsGood(result))
                    {
                        result = MasterNodeManager.ValidateAccessRestrictions(
                            operationContext,
                            nodeMetadata);
                    }

                    return ServiceResult.IsGood(result);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    // An authorization decision that threw is a denial, not a
                    // grant. Anything else would make an unrelated fault in
                    // the NodeManager open the channel.
                    return false;
                }
            }

            private static OperationContext CreateReadOperationContext(
                DataChannelRequestContext context,
                ISession session)
            {
                string transportProfileUri = string.IsNullOrEmpty(context.TransportProfileUri)
                    ? Profiles.UaTcpTransport
                    : context.TransportProfileUri;

                var endpoint = new EndpointDescription
                {
                    SecurityMode = context.SecurityMode,
                    TransportProfileUri = transportProfileUri
                };

                var secureChannel = new SecureChannelContext(
                    session.SecureChannelId ?? string.Empty,
                    endpoint,
                    RequestEncoding.Binary);

                return new OperationContext(
                    new RequestHeader(),
                    secureChannel,
                    RequestType.Read,
                    RequestLifetime.None,
                    session);
            }
        }

        private sealed class ServerDataChannelAuditor(IServerInternal server) : IDataChannelAuditor
        {
            public void OnOpenDataChannel(
                DataChannelRequestContext context,
                NodeId sourceNodeId,
                DataChannelParametersDataType parameters,
                uint? channelId,
                StatusCode status)
            {
                if (!server.Auditing)
                {
                    return;
                }

                ISystemContext systemContext = server.DefaultAuditContext;
                var ev = new AuditOpenDataChannelEventState(null);
                ev.Initialize(
                    systemContext,
                    null,
                    EventSeverity.Min,
                    new LocalizedText("AuditOpenDataChannelEvent"),
                    StatusCode.IsGood(status),
                    DateTime.UtcNow);
                ev.SetChildValue(systemContext, BrowseNames.SourceNode, ObjectIds.Server, false);
                ev.SetChildValue(systemContext, BrowseNames.SourceName, "Session/OpenDataChannel", false);
                ev.SetChildValue(systemContext, BrowseNames.ClientAuditEntryId, context.ClientAuditEntryId!, false);
                ev.SetChildValue(systemContext, BrowseNames.ClientUserId, context.ClientUserId!, false);
                ev.SetChildValue(systemContext, BrowseNames.DataChannelSourceNodeId, sourceNodeId, false);
                ev.SetChildValue(systemContext, BrowseNames.Parameters, parameters, false);
                ev.SetChildValue(systemContext, BrowseNames.ChannelId, channelId.GetValueOrDefault(), false);
                server.ReportAuditEvent(systemContext, ev);
            }
        }

        private sealed class ServiceOnlyDataChannelTransport : IDataChannelTransport
        {
            public ServiceOnlyDataChannelTransport(
                uint maxFrameSize,
                ITelemetryContext telemetry,
                TimeProvider timeProvider)
            {
                MaxFrameBodySize = (int)Math.Min(maxFrameSize, int.MaxValue);
                BufferManager = new BufferManager(
                    "server-data-channels-service-only",
                    MaxFrameBodySize,
                    telemetry);
                TimeProvider = timeProvider;
            }

            public DataChannelFramingMode FramingMode => DataChannelFramingMode.Inline;

            public int MaxFrameBodySize { get; }

            public bool HasTransportFlowControl => false;

            public BufferManager BufferManager { get; }

            public TimeProvider TimeProvider { get; }

            public ValueTask SendFrameAsync(DataChannelFrame frame, CancellationToken ct)
            {
                return default;
            }

            public void OnProtocolFault(DataChannelFrameError error)
            {
            }
        }

        private readonly ConcurrentDictionary<string, DataChannelSecureChannelState> m_dataChannelStates = new();
        private ITimer? m_dataChannelAuthorizationTimer;
    }
}
