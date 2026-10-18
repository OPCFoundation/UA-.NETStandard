/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.DataChannels;

namespace Opc.Ua.Server.DataChannels
{
    public sealed partial class ServerDataChannelProvider
    {
        /// <inheritdoc/>
        public async ValueTask<DataChannelAuthorization> AuthorizeAsync(
            DataChannelAuthorizationRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (request.Purpose == DataChannelAuthorizationPurpose.Open)
            {
                ExpireProvisionalState();
            }
            DataChannelParametersDataType parameters = request.Parameters;
            DataChannelResult result = DataChannelResult.NotAuthorized;
            SessionFlow? flow = null;
            DataChannelSource? source = null;
            try
            {
                ContextRegistration? registration = FindContext(request.Context.ContextId, request.Context.Generation);
                if (registration == null || !Sources.TryGet(parameters.ResourceUri ?? string.Empty, out source))
                {
                    return new DataChannelAuthorization(result);
                }
                result = ValidateCarrierAndSource(registration, source, parameters);
                if (result != DataChannelResult.Good)
                {
                    return new DataChannelAuthorization(result);
                }
                var key = new FlowKey(request.Context.ContextId, request.Context.Generation, request.ChannelId);
                if (request.Mode == DataChannelAuthorizationMode.Session)
                {
                    lock (m_gate)
                    {
                        m_sessionFlows.TryGetValue(key, out flow);
                    }
                    if (flow == null || flow.Revoked || !HasSameUser(flow) ||
                        !IsLiveSession(flow.Session) ||
                        !MatchesSessionPeer(flow.Session, request.Context))
                    {
                        result = DataChannelResult.NotAuthorized;
                        return new DataChannelAuthorization(result);
                    }
                    if (request.Purpose == DataChannelAuthorizationPurpose.Open &&
                        (!request.GrantId.HasValue || flow.GrantId != request.GrantId.Value ||
                        !m_grants.TryGetGrant(request.GrantId.Value, out _)))
                    {
                        result = DataChannelResult.NotAuthorized;
                        return new DataChannelAuthorization(result);
                    }
                    if (request.Purpose == DataChannelAuthorizationPurpose.Update)
                    {
                        lock (m_gate)
                        {
                            StagedOperation? operation = flow.PendingOperation;
                            if (operation == null || operation.Id != request.OperationId ||
                                operation.Revision != request.Revision ||
                                !DataChannelControlModel.SameParameters(operation.Parameters, parameters) ||
                                (!operation.Prepared && HasTimedOut(operation)))
                            {
                                result = DataChannelResult.NotAuthorized;
                                return new DataChannelAuthorization(result);
                            }
                        }
                    }
                    if (!await CheckNodePermissionsAsync(
                        flow.Session, source, request.IsLocalOpener, parameters, cancellationToken)
                        .ConfigureAwait(false))
                    {
                        result = DataChannelResult.NotAuthorized;
                        return new DataChannelAuthorization(result);
                    }
                }
                else if (request.Mode != DataChannelAuthorizationMode.Configured)
                {
                    result = DataChannelResult.InvalidArgument;
                    return new DataChannelAuthorization(result);
                }

                result = await source.Policy.AuthorizeAsync(
                    new DataChannelSourceAccess(
                        source, request.Context, flow?.Session, request.IsLocalOpener, request.Purpose, parameters),
                    cancellationToken).ConfigureAwait(false);
                if (result != DataChannelResult.Good)
                {
                    return new DataChannelAuthorization(result);
                }
                lock (m_gate)
                {
                    if (registration.Closed || (flow != null &&
                        (flow.Revoked || !HasSameUser(flow) || !IsLiveSession(flow.Session))))
                    {
                        result = DataChannelResult.NotAuthorized;
                        return new DataChannelAuthorization(result);
                    }
                    if (request.Purpose == DataChannelAuthorizationPurpose.Open)
                    {
                        if (flow != null)
                        {
                            DataChannelAuthorization redemption = m_grants.Redeem(request);
                            result = redemption.Result;
                            if (result != DataChannelResult.Good)
                            {
                                return redemption;
                            }
                            flow.Redeemed = true;
                        }
                        else if (!TryAdmit(key, source))
                        {
                            result = DataChannelResult.WouldBlock;
                            return new DataChannelAuthorization(result);
                        }
                    }
                    if (request.Purpose == DataChannelAuthorizationPurpose.Update && flow?.PendingOperation != null)
                    {
                        flow.PendingOperation.Prepared = true;
                    }
                }
                return new DataChannelAuthorization(
                    result,
                    parameters,
                    source.SourceNodeId,
                    flow?.Session.Id ?? NodeId.Null,
                    flow == null ? null : GetSessionLifetime(flow.Session),
                    flow?.RevocationToken ?? default);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                result = DataChannelResult.NotAuthorized;
                m_logger.BackgroundOperationFailed(exception);
                return new DataChannelAuthorization(result);
            }
            finally
            {
                if (request.Purpose != DataChannelAuthorizationPurpose.Recheck ||
                    result != DataChannelResult.Good)
                {
                    Audit(new DataChannelControlAudit(
                        request.Purpose.ToString(),
                        source?.SourceNodeId ?? ExpandedNodeId.Null,
                        source?.ResourceUri ?? string.Empty,
                        request.Context.Peer.PeerApplicationUri,
                        request.Context.ContextId,
                        request.Context.Generation,
                        request.ChannelId,
                        flow?.Session.Id ?? NodeId.Null,
                        request.OperationId,
                        request.Revision,
                        result));
                }
            }
        }

        /// <summary>
        /// Checks source discovery against at least one actually permitted complete direction/combination.
        /// This does not grant any data-path operation.
        /// </summary>
        internal async ValueTask<bool> CanInspectSourceAsync(
            ISession session,
            DataChannelSource source,
            CancellationToken cancellationToken)
        {
            if (!Sources.TryGet(source.ResourceUri, out DataChannelSource? registered) ||
                !ReferenceEquals(registered, source))
            {
                return false;
            }
            foreach (DataChannelDirection direction in source.SupportedDirections.ToList())
            {
                foreach (string protocol in source.SupportedProtocols.ToList())
                {
                    foreach (DataChannelDeliveryMode delivery in source.SupportedDeliveryModes.ToList())
                    {
                        foreach (DataChannelProtectionMode protection in source.SupportedProtectionModes.ToList())
                        {
                            var parameters = new DataChannelParametersDataType
                            {
                                ResourceUri = source.ResourceUri,
                                Protocol = protocol,
                                ContentType = source.ContentType.Length == 0 &&
                                    protocol == DataChannelProfiles.OpaquePayload
                                    ? "application/octet-stream"
                                    : source.ContentType,
                                Direction = direction,
                                Delivery = delivery,
                                Protection = protection,
                                MaxMessageSize = source.MaxMessageSize,
                                MaxReceiveBytes = source.MaxReceiveBytes,
                                MaxRate = source.MaxRate > 0 ? source.MaxRate :
                                    delivery == DataChannelDeliveryMode.Datagram ? 1 : 0
                            };
                            if (await CheckNodePermissionsAsync(
                                session, source, false, parameters, cancellationToken).ConfigureAwait(false) &&
                                await source.Policy.AuthorizeAsync(new DataChannelSourceAccess(
                                    source, null, session, false, DataChannelAuthorizationPurpose.Recheck, parameters),
                                    cancellationToken).ConfigureAwait(false) == DataChannelResult.Good)
                            {
                                return true;
                            }
                        }
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Finds only a confirmed live local context, never caller-provided evidence alone.
        /// </summary>
        private ContextRegistration? FindContext(Guid contextId, ulong generation)
        {
            foreach (ContextRegistration candidate in GetContexts())
            {
                if (!candidate.Closed && candidate.Context.IsReady)
                {
                    DataChannelContextIdentity identity = candidate.Context.Identity;
                    if (identity.ContextId == contextId && identity.Generation == generation)
                    {
                        candidate.Identity = identity;
                        return candidate;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Applies source selections and the actual selected carrier's encoded-message bounds.
        /// </summary>
        private DataChannelResult ValidateCarrierAndSource(
            ContextRegistration registration,
            DataChannelSource source,
            DataChannelParametersDataType parameters)
        {
            DataChannelResult result = source.Validate(parameters);
            if (result != DataChannelResult.Good)
            {
                return result;
            }
            if (parameters.Protection != registration.Carrier.Identity.Protection ||
                (parameters.Protection == DataChannelProtectionMode.Integrity && !AllowIntegrity))
            {
                return DataChannelResult.SecurityFailure;
            }
            uint overhead = registration.Carrier.Kind == DataChannelCarrierKind.SharedUasc ||
                parameters.Delivery == DataChannelDeliveryMode.Datagram ? 17u : 13u;
            if ((ulong)parameters.MaxMessageSize + overhead > parameters.MaxReceiveBytes)
            {
                return DataChannelResult.InvalidArgument;
            }
            if (parameters.MaxMessageSize > m_options.MaxMessageSize ||
                parameters.MaxReceiveBytes > m_options.MaxReceiveBytes ||
                (ulong)parameters.MaxMessageSize + 13 > (ulong)registration.Carrier.MaxRecordSize)
            {
                return DataChannelResult.TooLarge;
            }
            if (parameters.Delivery == DataChannelDeliveryMode.Datagram)
            {
                if (registration.Carrier.MaxDatagramSize <= 0)
                {
                    return DataChannelResult.Unsupported;
                }
                if ((ulong)parameters.MaxMessageSize + 17 > (ulong)registration.Carrier.MaxDatagramSize)
                {
                    return DataChannelResult.TooLarge;
                }
            }
            return DataChannelResult.Good;
        }

        /// <summary>
        /// Reserves the resource's flow quota across configured, granted and ready flows.
        /// The caller holds the provider gate.
        /// </summary>
        private bool TryAdmit(FlowKey key, DataChannelSource source)
        {
            if (m_admitted.ContainsKey(key))
            {
                return true;
            }
            int count = 0;
            foreach (string resource in m_admitted.Values)
            {
                if (resource == source.ResourceUri)
                {
                    count++;
                }
            }
            if (count >= source.MaxFlows)
            {
                return false;
            }
            m_admitted.Add(key, source.ResourceUri);
            return true;
        }

        /// <summary>
        /// Checks current Session validity without treating data traffic as Session activity.
        /// </summary>
        private bool IsLiveSession(ISession session)
        {
            IServerInternal? server = m_server;
            if (server == null || !session.Activated || session.IsClosing || session.HasExpired)
            {
                return false;
            }
            foreach (ISession candidate in server.SessionManager.GetSessions())
            {
                if (ReferenceEquals(candidate, session))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Matches the Session's validated client application and public key to the data association.
        /// </summary>
        private bool MatchesSessionPeer(ISession session, DataChannelContextIdentity identity)
        {
            return ClientCertificateProvenance.IsValidated(session) &&
                session.ClientCertificate != null &&
                session.ClientCertificate.NotAfter > m_clock.GetUtcNow().UtcDateTime &&
                session.ClientCertificate.NotBefore <= m_clock.GetUtcNow().UtcDateTime &&
                session.ClientApplicationUri == identity.Peer.PeerApplicationUri &&
                m_server?.ServerUris.GetString(0) == identity.Peer.LocalApplicationUri &&
                DataChannelCertificateIdentity.GetKeyThumbprint(session.ClientCertificate) ==
                    identity.Peer.PeerKeyThumbprint;
        }

        /// <summary>
        /// Uses the existing token-specific identity continuity semantics, not display names.
        /// </summary>
        private static bool HasSameUser(SessionFlow flow)
        {
            return HasSameUser(flow.Session, flow.IdentityKey, flow.IdentityType);
        }

        /// <summary>
        /// Compares a captured subject/type before an asynchronous authorization decision is committed.
        /// </summary>
        private static bool HasSameUser(ISession session, string? identityKey, UserTokenType identityType)
        {
            return ClientUserIdResolver.TryResolveContinuityKey(
                session.IdentityToken, session.Identity, out string? current) &&
                identityType == session.Identity.TokenType &&
                string.Equals(identityKey, current, StringComparison.Ordinal);
        }

        /// <summary>
        /// Computes remaining Session validity in the issuer's monotonic clock domain.
        /// </summary>
        private TimeSpan GetSessionLifetime(ISession session)
        {
            double timeout = session.ReadDiagnostics(static diagnostics => diagnostics.ActualSessionTimeout);
            double remaining = timeout - (m_clock.GetTimestampMilliseconds() - session.LastContactTickCount);
            if (double.IsNaN(remaining) || double.IsInfinity(remaining) || remaining <= 0)
            {
                return TimeSpan.Zero;
            }
            return TimeSpan.FromMilliseconds(remaining);
        }

        /// <summary>
        /// Checks each required permission separately on the source and every represented content Node.
        /// </summary>
        private async ValueTask<bool> CheckNodePermissionsAsync(
            ISession session,
            DataChannelSource source,
            bool isLocalOpener,
            DataChannelParametersDataType parameters,
            CancellationToken cancellationToken)
        {
            IServerInternal? server = m_server;
            if (server == null || source.SourceNodeId.IsNull || !IsLiveSession(session))
            {
                return false;
            }
            if (server.SessionManager is SessionManager manager)
            {
                manager.RefreshIdentityForAuthorization(session);
            }
            if (session.IsIdentityStale)
            {
                return false;
            }
            using var operation = new OperationContext(
                new RequestHeader(),
                new SecureChannelContext(
                    session.SecureChannelId ?? string.Empty, session.EndpointDescription, RequestEncoding.Binary),
                RequestType.Call,
                RequestLifetime.None,
                session);
            bool sends = parameters.Direction == DataChannelDirection.Duplex ||
                (isLocalOpener
                    ? parameters.Direction == DataChannelDirection.OpenerToAcceptor
                    : parameters.Direction == DataChannelDirection.AcceptorToOpener);
            bool receives = parameters.Direction == DataChannelDirection.Duplex || !sends;
            List<ExpandedNodeId> nodes = [source.SourceNodeId, .. source.ContentNodeIds];
            foreach (ExpandedNodeId expanded in nodes)
            {
                NodeId id = ExpandedNodeId.ToNodeId(expanded, server.NamespaceUris);
                (object? handle, IAsyncNodeManager? owner) = await server.NodeManager
                    .GetManagerHandleAsync(id, cancellationToken).ConfigureAwait(false);
                if (id.IsNull || owner == null || handle == null)
                {
                    return false;
                }
                NodeMetadata? metadata = expanded == source.SourceNodeId
                    ? await owner.GetNodeMetadataAsync(
                        operation, handle, BrowseResultMask.NodeClass, cancellationToken).ConfigureAwait(false)
                    : await owner.GetPermissionMetadataAsync(
                        operation, handle, BrowseResultMask.NodeClass, [], true, cancellationToken)
                        .ConfigureAwait(false);
                metadata ??= await owner.GetNodeMetadataAsync(
                    operation, handle, BrowseResultMask.NodeClass, cancellationToken).ConfigureAwait(false);
                if (metadata == null ||
                    (expanded == source.SourceNodeId && metadata.NodeClass != NodeClass.Object) ||
                    ServiceResult.IsBad(MasterNodeManager.ValidateAccessRestrictions(operation, metadata)) ||
                    ServiceResult.IsBad(MasterNodeManager.ValidateRolePermissions(
                        operation, metadata, PermissionType.Browse)) ||
                    (sends && ServiceResult.IsBad(MasterNodeManager.ValidateRolePermissions(
                        operation, metadata, PermissionType.Read))) ||
                    (receives && ServiceResult.IsBad(MasterNodeManager.ValidateRolePermissions(
                        operation, metadata, PermissionType.Write))))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Releases provisional quotas exactly when the grant expires and expires unprepared updates.
        /// </summary>
        private void ExpireProvisionalState()
        {
            lock (m_gate)
            {
                foreach (KeyValuePair<FlowKey, SessionFlow> item in m_sessionFlows)
                {
                    SessionFlow flow = item.Value;
                    if (!flow.Redeemed && !flow.Revoked &&
                        !m_grants.TryGetGrant(flow.GrantId, out _))
                    {
                        flow.Revoked = true;
                        m_admitted.Remove(item.Key);
                    }
                    if (flow.PendingOperation is { Prepared: false } operation && HasTimedOut(operation))
                    {
                        operation.Expired = true;
                        flow.PendingOperation = null;
                    }
                }
            }
        }

        /// <summary>
        /// Applies the monotonic update deadline to a staged operation.
        /// </summary>
        private bool HasTimedOut(StagedOperation operation)
        {
            return m_clock.GetElapsedTime(operation.Created) >= m_options.UpdateTimeout;
        }
    }
}
