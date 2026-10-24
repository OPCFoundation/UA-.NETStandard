/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.DataChannels;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Client.DataChannels
{
    /// <summary>
    /// Generated-proxy Session control for a separately authenticated flow context.
    /// All control operations use ordinary Call and preserve the data-path readiness/revision protocol.
    /// </summary>
    [global::System.Diagnostics.CodeAnalysis.Experimental(DataChannelFeature.ExperimentalDiagnosticId,
        UrlFormat = DataChannelFeature.ExperimentalUrlFormat)]
    public sealed class DataChannelSessionControl
    {
        /// <summary>
        /// Binds the well-known generated manager proxy to the Session and its local grant authorizer.
        /// </summary>
        public DataChannelSessionControl(
            ISession session,
            SessionDataChannelAuthorizer authorizer,
            TimeProvider timeProvider)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
            m_authorizer = authorizer ?? throw new ArgumentNullException(nameof(authorizer));
            m_clock = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
            authorizer.AttachSession(session);
            Proxy = new DataChannelManagerTypeClient(
                session, ObjectIds.DataChannelManager, session.MessageContext.Telemetry);
        }

        /// <summary>
        /// Gets the ordinary control Session, which may use a separate connection.
        /// </summary>
        public ISession Session { get; }

        /// <summary>
        /// Gets the generated proxy for advanced status reads and Method access.
        /// </summary>
        public DataChannelManagerTypeClient Proxy { get; }

        /// <summary>
        /// Obtains and validates a single-use grant for an already reserved ChannelId.
        /// The deadline is expressed in the issuer's UTC domain, not the client's clock domain.
        /// </summary>
        public async ValueTask<DataChannelGrantDataType> GetGrantAsync(
            DataChannelContext context,
            uint channelId,
            NodeId sourceNodeId,
            DataChannelParametersDataType parameters,
            DateTimeUtc latestIssuerExpiry = default,
            CancellationToken cancellationToken = default)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (parameters == null)
            {
                throw new ArgumentNullException(nameof(parameters));
            }
            ValidateParticipatingServer(context);
            DataChannelContextIdentity identity = context.Identity;
            long started = m_clock.GetTimestamp();
            var binding = new DataChannelBindingDataType
            {
                ContextId = identity.ContextId,
                Generation = identity.Generation,
                PeerApplicationUri = identity.Peer.LocalApplicationUri,
                PeerKeyThumbprint = identity.Peer.LocalKeyThumbprint,
                Challenge = identity.PeerChallenge,
                ExpiresAt = latestIssuerExpiry.IsNull ? DateTime.MaxValue : latestIssuerExpiry
            };
            DataChannelGrantDataType grant = await Proxy.OpenDataChannelAsync(
                channelId, sourceNodeId, binding, parameters, cancellationToken).ConfigureAwait(false);
            ValidateParticipatingServer(context);
            if (grant.ChannelId != channelId || grant.ContextId != identity.ContextId ||
                grant.Generation != identity.Generation || grant.SessionId != Session.SessionId ||
                ExpandedNodeId.ToNodeId(grant.SourceNodeId, Session.NamespaceUris) != sourceNodeId ||
                !FlowModel.SameParameters(parameters, grant.Parameters))
            {
                throw new DataChannelException(DataChannelResult.SecurityFailure);
            }
            m_authorizer.AcceptGrant(context, grant, started);
            return grant;
        }

        /// <summary>
        /// Reserves an id, obtains a grant and completes OPEN/ACCEPT/READY before returning the flow.
        /// </summary>
        public async ValueTask<DataChannel> OpenAsync(
            DataChannelContext context,
            NodeId sourceNodeId,
            DataChannelParametersDataType parameters,
            CancellationToken cancellationToken = default)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            uint channelId = context.ReserveChannelId();
            try
            {
                DataChannelGrantDataType grant = await GetGrantAsync(
                    context, channelId, sourceNodeId, parameters, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                return await context.OpenAsync(
                    parameters, DataChannelAuthorizationMode.Session, grant.GrantId, channelId, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
                m_authorizer.Forget(context, channelId);
                context.ReleaseChannelId(channelId);
                throw;
            }
        }

        /// <summary>
        /// Stages a Session-authorized proposal, then waits for the actual revision transaction outcome.
        /// A lost Call or ACK never justifies rolling back a committed revision.
        /// </summary>
        public async ValueTask<DataChannelStatusDataType> ModifyAsync(
            DataChannel channel,
            uint operationId,
            DataChannelParametersDataType parameters,
            CancellationToken cancellationToken = default)
        {
            if (channel == null)
            {
                throw new ArgumentNullException(nameof(channel));
            }
            if (parameters == null)
            {
                throw new ArgumentNullException(nameof(parameters));
            }
            if (!channel.IsLocalOpener || operationId == 0 || channel.Revision == uint.MaxValue)
            {
                throw new DataChannelException(DataChannelResult.InvalidArgument);
            }
            OperationHistory history = m_operations.GetValue(channel, static _ => new OperationHistory());
            uint revision = history.GetRevision(operationId, channel.Revision, parameters);
            DataChannelStatusDataType staged = await Proxy.ModifyDataChannelAsync(
                channel.ChannelId, operationId, revision, parameters, cancellationToken).ConfigureAwait(false);
            if (staged.OperationId != operationId ||
                (staged.PendingRevision != revision && staged.Revision != revision))
            {
                throw new DataChannelException(DataChannelResult.SecurityFailure);
            }
            m_authorizer.AcceptUpdate(channel, operationId, revision, parameters);
            await channel.UpdateAsync(parameters, operationId, cancellationToken).ConfigureAwait(false);
            return channel.GetOperationStatus(operationId);
        }

        /// <summary>
        /// Requests the remote authorized close and closes the local payload direction separately.
        /// </summary>
        public async ValueTask<DataChannelStatusDataType> CloseAsync(
            DataChannel channel,
            bool deleteQueued = false,
            CancellationToken cancellationToken = default)
        {
            if (channel == null)
            {
                throw new ArgumentNullException(nameof(channel));
            }
            await Proxy.CloseDataChannelAsync(channel.ChannelId, deleteQueued, cancellationToken).ConfigureAwait(false);
            if (deleteQueued)
            {
                await channel.ResetAsync(DataChannelResult.Good, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await channel.EndAsync(cancellationToken).ConfigureAwait(false);
            }
            return channel.GetStatus();
        }

        /// <summary>
        /// Confirms the control Server is the intended data peer, not a forwarding third party.
        /// Session establishment already validated the control certificate; discovery alone is not trust.
        /// </summary>
        private void ValidateParticipatingServer(DataChannelContext context)
        {
            if (!context.IsReady || Session.SessionId.IsNull)
            {
                throw new DataChannelException(DataChannelResult.InvalidState);
            }
            EndpointDescription endpoint = Session.ConfiguredEndpoint.Description;
            if (endpoint.Server?.ApplicationUri != context.Identity.Peer.PeerApplicationUri ||
                endpoint.ServerCertificate.IsEmpty)
            {
                throw new DataChannelException(DataChannelResult.SecurityFailure);
            }
            using CertificateCollection chain = Utils.ParseCertificateChainBlob(
                endpoint.ServerCertificate, Session.MessageContext.Telemetry);
            if (chain.Count == 0 ||
                DataChannelCertificateIdentity.GetKeyThumbprint(chain[0]) != context.Identity.Peer.PeerKeyThumbprint)
            {
                throw new DataChannelException(DataChannelResult.SecurityFailure);
            }
        }

        private readonly SessionDataChannelAuthorizer m_authorizer;
        private readonly TimeProvider m_clock;
        private readonly ConditionalWeakTable<DataChannel, OperationHistory> m_operations = new();

        /// <summary>
        /// Retains bounded retry identity even when a Call response is lost.
        /// </summary>
        private sealed class OperationHistory
        {
            /// <summary>
            /// Reserves or resolves one bounded idempotent revision without exposing synchronization.
            /// </summary>
            internal uint GetRevision(uint operationId, uint currentRevision, DataChannelParametersDataType parameters)
            {
                lock (m_gate)
                {
                    if (m_operations.TryGetValue(operationId, out Proposal? existing))
                    {
                        if (!FlowModel.SameParameters(existing.Parameters, parameters))
                        {
                            throw new DataChannelException(DataChannelResult.InvalidArgument);
                        }
                        return existing.Revision;
                    }
                    if (operationId <= m_highWater)
                    {
                        throw new DataChannelException(DataChannelResult.InvalidArgument);
                    }
                    uint revision = currentRevision + 1;
                    m_highWater = operationId;
                    m_operations.Add(operationId, new Proposal(revision, FlowModel.Copy(parameters)));
                    while (m_operations.Count > 64)
                    {
                        using IEnumerator<uint> oldest = m_operations.Keys.GetEnumerator();
                        if (oldest.MoveNext())
                        {
                            m_operations.Remove(oldest.Current);
                        }
                    }
                    return revision;
                }
            }

            private readonly Lock m_gate = new();
            private readonly SortedDictionary<uint, Proposal> m_operations = [];
            private uint m_highWater;
        }

        /// <summary>
        /// Associates an idempotency identifier with exactly one complete proposal and revision.
        /// </summary>
        private sealed record Proposal(uint Revision, DataChannelParametersDataType Parameters);
    }
}
