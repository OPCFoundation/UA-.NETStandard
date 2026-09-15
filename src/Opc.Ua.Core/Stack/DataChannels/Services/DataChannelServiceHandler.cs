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
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// The Session and SecureChannel a DataChannel Service request
    /// arrived on.
    /// </summary>
    public sealed record DataChannelRequestContext
    {
        /// <summary>
        /// The Session that authorized the request. A channel is owned by
        /// the SecureChannel and authorized by the Session, and
        /// separating the two is what makes the lifecycle rules
        /// unambiguous.
        /// </summary>
        public NodeId SessionId { get; init; }

        /// <summary>
        /// True once ActivateSession has completed for that Session.
        /// </summary>
        public bool IsSessionActivated { get; init; }

        /// <summary>
        /// The SecurityMode of the SecureChannel carrying the request.
        /// </summary>
        public MessageSecurityMode SecurityMode { get; init; }
            = MessageSecurityMode.SignAndEncrypt;

        /// <summary>
        /// The TransportProfileUri of the endpoint carrying the request.
        /// </summary>
        public string TransportProfileUri { get; init; } = Profiles.UaTcpTransport;

        /// <summary>
        /// The transport stream identifier the client opened for a client
        /// initiated direction, or zero.
        /// </summary>
        public ulong TransportChannelId { get; init; }

        /// <summary>
        /// True when the transport itself provides reliability, which
        /// makes MaxRetransmits and FrameDeadline ineffective.
        /// </summary>
        public bool TransportIsReliable { get; init; } = true;

        /// <summary>
        /// The bound the negotiated buffer size imposes on a frame
        /// payload.
        /// </summary>
        public uint TransportMaxFrameSize { get; init; }

        /// <summary>
        /// The audit entry id supplied with the Service request.
        /// </summary>
        public string? ClientAuditEntryId { get; init; }

        /// <summary>
        /// The effective user id for audit events.
        /// </summary>
        public string? ClientUserId { get; init; }
    }

    /// <summary>
    /// Decides whether a Session's user identity may open, modify or
    /// close a data channel on a source Node.
    /// </summary>
    /// <remarks>
    /// Authorization is re evaluated rather than granted once: a channel
    /// is long lived and moves content out of the server continuously and
    /// outside the Service path, so a permission checked only at open is
    /// a permission that cannot be revoked.
    /// </remarks>
    public interface IDataChannelAuthorizer
    {
        /// <summary>
        /// Applies the same rules a Read of the same content would, and a
        /// Write as well when the channel carries payload towards the source.
        /// </summary>
        /// <remarks>
        /// The direction is part of the decision, not a detail of it. Part 4
        /// errata §7.2: a user permitted to watch a drive but not to command
        /// it could otherwise open the <c>SinkToSource</c> channel that drive
        /// advertises and send it firmware, setpoints or console input.
        /// </remarks>
        /// <param name="context">The Session and SecureChannel.</param>
        /// <param name="sourceNodeId">The data channel source.</param>
        /// <param name="direction">The direction the channel carries payload
        /// in, which selects the permissions that have to hold.</param>
        /// <param name="ct">Cancellation token.</param>
        ValueTask<bool> IsAuthorizedAsync(
            DataChannelRequestContext context,
            NodeId sourceNodeId,
            DataChannelDirection direction,
            CancellationToken ct);
    }

    /// <summary>
    /// Records an OpenDataChannel attempt, successful or refused.
    /// </summary>
    /// <remarks>
    /// This is deliberately stricter than the treatment of a Read: a data
    /// channel moves content out of the server continuously and outside
    /// the Service path, so the moment it was authorized is the only
    /// point at which an audit trail can capture it.
    /// </remarks>
    public interface IDataChannelAuditor
    {
        /// <summary>
        /// Records an attempt.
        /// </summary>
        /// <param name="context">The Session and SecureChannel.</param>
        /// <param name="sourceNodeId">The endpoint requested.</param>
        /// <param name="parameters">The parameters as revised, or as
        /// requested when the request was refused.</param>
        /// <param name="channelId">The assigned ChannelId, or null when
        /// the request was refused.</param>
        /// <param name="status">The Service result.</param>
        void OnOpenDataChannel(
            DataChannelRequestContext context,
            NodeId sourceNodeId,
            DataChannelParametersDataType parameters,
            uint? channelId,
            StatusCode status);
    }

    /// <summary>
    /// Allocates transport streams needed by mappings that do not carry
    /// data-channel frames inline with Services.
    /// </summary>
    public interface IDataChannelTransportStreamAllocator
    {
        /// <summary>
        /// Allocates the server-initiated transport stream for a channel.
        /// </summary>
        ValueTask<ulong> AllocateServerStreamAsync(
            DataChannelRequestContext context,
            uint channelId,
            DataChannelDirection direction,
            CancellationToken ct);

        /// <summary>
        /// Binds the client-initiated transport stream for a channel.
        /// </summary>
        ValueTask BindClientStreamAsync(
            DataChannelRequestContext context,
            uint channelId,
            ulong streamId,
            DataChannelDirection direction,
            CancellationToken ct);
    }

    /// <summary>
    /// The server side of the DataChannel Service Set on one
    /// SecureChannel.
    /// </summary>
    /// <remarks>
    /// Every Service in this set is scoped to both the SecureChannel and
    /// the authorizing Session. OPC 10000-4 permits several Sessions on
    /// one SecureChannel and they share one ChannelId space; since
    /// ChannelIds are allocated monotonically from one and are therefore
    /// trivially guessable, scoping only to the SecureChannel would let
    /// one user enumerate and seize another's channels.
    /// </remarks>
    public sealed class DataChannelServiceHandler
    {
        /// <summary>
        /// Creates a handler.
        /// </summary>
        /// <param name="manager">The channels on this SecureChannel.</param>
        /// <param name="sources">The endpoints this server hosts.</param>
        /// <param name="capabilities">The server wide limits.</param>
        /// <param name="authorizer">The authorization policy.</param>
        /// <param name="auditor">The audit sink, or null.</param>
        /// <param name="streamAllocator">The transport stream allocator, or null.</param>
        /// <param name="timeProvider">The clock.</param>
        public DataChannelServiceHandler(
            DataChannelManager manager,
            DataChannelSourceRegistry sources,
            DataChannelServerCapabilities capabilities,
            IDataChannelAuthorizer authorizer,
            IDataChannelAuditor? auditor = null,
            IDataChannelTransportStreamAllocator? streamAllocator = null,
            TimeProvider? timeProvider = null)
        {
            m_manager = manager ?? throw new ArgumentNullException(nameof(manager));
            m_sources = sources ?? throw new ArgumentNullException(nameof(sources));
            m_capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
            m_authorizer = authorizer ?? throw new ArgumentNullException(nameof(authorizer));
            m_auditor = auditor;
            m_streamAllocator = streamAllocator;
            Offers = new DataChannelOfferRegistry(timeProvider);
        }

        /// <summary>
        /// The offers outstanding on this SecureChannel.
        /// </summary>
        public DataChannelOfferRegistry Offers { get; }

        /// <summary>
        /// Opens a data channel on a data channel source, or accepts a
        /// server offer.
        /// </summary>
        /// <param name="context">The Session and SecureChannel.</param>
        /// <param name="sourceNodeId">The endpoint.</param>
        /// <param name="offerId">Zero for a client initiated open,
        /// otherwise the offer being accepted.</param>
        /// <param name="requestedParameters">What the client asked
        /// for.</param>
        /// <param name="ct">Cancellation token.</param>
        public async ValueTask<OpenDataChannelResponse> OpenDataChannelAsync(
            DataChannelRequestContext context,
            NodeId sourceNodeId,
            uint offerId,
            DataChannelParametersDataType? requestedParameters,
            CancellationToken ct)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            DataChannelParametersDataType attempted =
                requestedParameters ?? new DataChannelParametersDataType();

            if (!context.IsSessionActivated)
            {
                return Refuse(context, sourceNodeId, attempted, StatusCodes.BadSessionNotActivated);
            }

            // A server enforces the minimum SecureChannel security itself.
            // A rule only the attacker is asked to obey is not a rule.
            if (context.SecurityMode is MessageSecurityMode.None or MessageSecurityMode.Invalid &&
                !m_capabilities.AllowInsecureDataChannels)
            {
                return Refuse(
                    context,
                    sourceNodeId,
                    attempted,
                    StatusCodes.BadSecurityModeInsufficient);
            }

            if (!IsTransportSupported(context.TransportProfileUri))
            {
                return Refuse(
                    context,
                    sourceNodeId,
                    attempted,
                    StatusCodes.BadDataChannelTransportUnsupported);
            }

            if (offerId != 0 &&
                !Offers.TryRedeem(offerId, sourceNodeId, out DataChannelOfferDataType? _))
            {
                return Refuse(
                    context,
                    sourceNodeId,
                    attempted,
                    StatusCodes.BadDataChannelOfferInvalid);
            }

            if (!m_sources.TryGet(sourceNodeId, out IDataChannelSource? source) || source == null)
            {
                return Refuse(
                    context,
                    sourceNodeId,
                    attempted,
                    StatusCodes.BadDataChannelNotSupported);
            }

            if (source.Capabilities.MaxChannels != 0 &&
                source.ActiveChannelCount >= source.Capabilities.MaxChannels)
            {
                return Refuse(
                    context,
                    sourceNodeId,
                    attempted,
                    StatusCodes.BadTooManyDataChannels);
            }

            if (!DataChannelNegotiator.TryRevise(
                requestedParameters,
                source.Capabilities,
                m_capabilities,
                context.TransportMaxFrameSize,
                context.TransportIsReliable,
                out DataChannelParametersDataType revised,
                out StatusCode negotiation))
            {
                return Refuse(context, sourceNodeId, attempted, negotiation);
            }

            // Authorized against the direction the channel will actually
            // carry, which negotiation has only now settled, and before
            // anything is allocated: a ChannelId and, over an outer-protocol
            // transport, a stream are created below.
            if (!await m_authorizer
                .IsAuthorizedAsync(context, sourceNodeId, revised.Direction, ct)
                .ConfigureAwait(false))
            {
                return Refuse(context, sourceNodeId, attempted, StatusCodes.BadUserAccessDenied);
            }

            if (!m_manager.TryAllocateChannelId(out uint channelId))
            {
                return Refuse(
                    context,
                    sourceNodeId,
                    attempted,
                    StatusCodes.BadTooManyDataChannels);
            }

            ulong revisedTransportChannelId = await ReviseTransportChannelIdAsync(
                context,
                sourceNodeId,
                attempted,
                channelId,
                revised,
                ct).ConfigureAwait(false);

            DataChannel channel = m_manager.Register(
                channelId,
                sourceNodeId,
                DataChannelSettings.FromParameters(revised),
                isSource: true,
                revisedTransportChannelId);

            m_authorizingSessions[channelId] = context.SessionId;
            m_auditor?.OnOpenDataChannel(
                context,
                sourceNodeId,
                revised,
                channelId,
                StatusCodes.Good);

            source.OnChannelOpened(channel);
            TrackSourceLifetime(channel, source);

            return new OpenDataChannelResponse
            {
                ChannelId = channelId,
                RevisedParameters = revised,
                RevisedTransportChannelId = revisedTransportChannelId
            };
        }

        /// <summary>
        /// Reports that the OpenDataChannel response has been handed to
        /// the transport, which is what makes the channel eligible to
        /// carry frames.
        /// </summary>
        /// <param name="channelId">The identifier.</param>
        public void OnResponseSent(uint channelId)
        {
            m_manager.MarkOpen(channelId);
        }

        /// <summary>
        /// Changes the mutable parameters of an open channel without
        /// interrupting it.
        /// </summary>
        /// <param name="context">The Session and SecureChannel.</param>
        /// <param name="channelId">The channel.</param>
        /// <param name="requestedParameters">The new parameters.</param>
        /// <param name="ct">Cancellation token.</param>
        public async ValueTask<ModifyDataChannelResponse> ModifyDataChannelAsync(
            DataChannelRequestContext context,
            uint channelId,
            DataChannelParametersDataType? requestedParameters,
            CancellationToken ct)
        {
            (DataChannel? channel, StatusCode scope) = await ResolveAsync(context, channelId, ct)
                .ConfigureAwait(false);

            if (channel == null)
            {
                throw new ServiceResultException(scope);
            }

            DataChannelParametersDataType inForce = channel.Settings.ToParameters();

            if (DataChannelNegotiator.IsMutation(inForce, requestedParameters))
            {
                throw new ServiceResultException(StatusCodes.BadDataChannelLimitsExceeded);
            }

            if (!m_sources.TryGet(channel.SourceNodeId, out IDataChannelSource? source) ||
                source == null)
            {
                throw new ServiceResultException(StatusCodes.BadDataChannelNotSupported);
            }

            // Direction and DeliveryMode are immutable, so they are carried
            // from what is in force rather than taken from the request. A
            // request that omits them - including a null request, which means
            // "change nothing" - would otherwise be revised against their
            // default and silently re-point a Bidirectional channel, after
            // which the peer's own DATA starts being refused.
            DataChannelParametersDataType effective = CloneForModify(requestedParameters, inForce);

            if (!DataChannelNegotiator.TryRevise(
                effective,
                source.Capabilities,
                m_capabilities,
                context.TransportMaxFrameSize,
                context.TransportIsReliable,
                out DataChannelParametersDataType revised,
                out StatusCode negotiation))
            {
                throw new ServiceResultException(negotiation);
            }

            channel.ApplyRevisedSettings(DataChannelSettings.FromParameters(revised));

            return new ModifyDataChannelResponse { RevisedParameters = revised };
        }

        /// <summary>
        /// Closes a data channel in an orderly fashion.
        /// </summary>
        /// <param name="context">The Session and SecureChannel.</param>
        /// <param name="channelId">The channel.</param>
        /// <param name="reason">Good for a normal close.</param>
        /// <param name="deleteQueued">True discards frames still queued
        /// and closes immediately; false drains them first, bounded by
        /// DrainTimeout.</param>
        /// <param name="ct">Cancellation token.</param>
        public async ValueTask<CloseDataChannelResponse> CloseDataChannelAsync(            DataChannelRequestContext context,
            uint channelId,
            StatusCode reason,
            bool deleteQueued,
            CancellationToken ct)
        {
            (DataChannel? channel, StatusCode scope) = await ResolveAsync(context, channelId, ct)
                .ConfigureAwait(false);

            if (channel == null)
            {
                throw new ServiceResultException(scope);
            }

            if (channel.State is DataChannelState.Closed or DataChannelState.Faulted)
            {
                throw new ServiceResultException(StatusCodes.BadDataChannelClosed);
            }

            if (deleteQueued)
            {
                // Realized as a RESET carrying the reason, so a Good
                // reason takes both peers to Closed and a Bad one takes
                // both to Faulted. The StatusCode is the only wire signal
                // that distinguishes them.
                channel.Reset(reason);
            }
            else
            {
                channel.Close();
            }

            return new CloseDataChannelResponse();
        }

        /// <summary>
        /// Aborts every channel a Session authorized, used when the
        /// Session closes, its user identity changes, or it is
        /// transferred to a different SecureChannel.
        /// </summary>
        /// <param name="sessionId">The Session.</param>
        /// <param name="reason">Why.</param>
        public void AbortChannelsOfSession(NodeId sessionId, StatusCode reason)
        {
            foreach (System.Collections.Generic.KeyValuePair<uint, NodeId> entry
                in m_authorizingSessions)
            {
                if (entry.Value != sessionId)
                {
                    continue;
                }

                if (m_manager.TryGetChannel(entry.Key, out DataChannel? channel) && channel != null)
                {
                    channel.Abort(reason);
                }

                m_authorizingSessions.TryRemove(entry.Key, out _);
            }
        }

        /// <summary>
        /// Re evaluates the authorization of every open channel and
        /// aborts those that no longer pass.
        /// </summary>
        /// <param name="contextFor">Builds the request context for a
        /// Session, so the same rules a Read would apply are applied
        /// again.</param>
        /// <param name="ct">Cancellation token.</param>
        public async ValueTask<int> RecheckAuthorizationAsync(
            Func<NodeId, DataChannelRequestContext?> contextFor,
            CancellationToken ct)
        {
            if (contextFor == null)
            {
                throw new ArgumentNullException(nameof(contextFor));
            }

            int aborted = 0;

            foreach (System.Collections.Generic.KeyValuePair<uint, NodeId> entry
                in m_authorizingSessions)
            {
                if (!m_manager.TryGetChannel(entry.Key, out DataChannel? channel) ||
                    channel == null)
                {
                    m_authorizingSessions.TryRemove(entry.Key, out _);
                    continue;
                }

                DataChannelRequestContext? context = contextFor(entry.Value);

                if (context == null ||
                    !await m_authorizer
                        .IsAuthorizedAsync(
                            context,
                            channel.SourceNodeId,
                            channel.Settings.Direction,
                            ct)
                        .ConfigureAwait(false))
                {
                    channel.Abort(StatusCodes.BadUserAccessDenied);
                    m_authorizingSessions.TryRemove(entry.Key, out _);
                    aborted++;
                }
            }

            return aborted;
        }

        private async ValueTask<(DataChannel?, StatusCode)> ResolveAsync(
            DataChannelRequestContext context,
            uint channelId,
            CancellationToken ct)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (!context.IsSessionActivated)
            {
                return (null, StatusCodes.BadSessionNotActivated);
            }

            // A server shall not disclose, through a StatusCode or
            // otherwise, the existence of a channel authorized by another
            // Session, so an identifier owned by someone else is
            // indistinguishable from an unassigned one.
            if (!m_authorizingSessions.TryGetValue(channelId, out NodeId owner) ||
                owner != context.SessionId ||
                !m_manager.TryGetChannel(channelId, out DataChannel? channel) ||
                channel == null)
            {
                // A channel that has already ended is released from the
                // manager but its identifier is never reissued, so an
                // identifier this Session owned that is no longer present
                // names a closed channel rather than an unknown one. Part 4
                // distinguishes the two and a Client acts on the difference:
                // Bad_DataChannelClosed is idempotent success for a Close,
                // Bad_DataChannelIdInvalid is a programming error.
                if (owner == context.SessionId && m_manager.WasEverAllocated(channelId))
                {
                    return (null, StatusCodes.BadDataChannelClosed);
                }

                return (null, StatusCodes.BadDataChannelIdInvalid);
            }

            // The permissions are re checked on every call, not only at
            // open.
            if (!await m_authorizer
                .IsAuthorizedAsync(
                    context,
                    channel.SourceNodeId,
                    channel.Settings.Direction,
                    ct)
                .ConfigureAwait(false))
            {
                return (null, StatusCodes.BadUserAccessDenied);
            }

            return (channel, StatusCodes.Good);
        }

        private bool IsTransportSupported(string transportProfileUri)
        {
            for (int ii = 0; ii < m_capabilities.SupportedTransportProfileUris.Count; ii++)
            {
                if (string.Equals(
                    m_capabilities.SupportedTransportProfileUris[ii],
                    transportProfileUri,
                    StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private async ValueTask<ulong> ReviseTransportChannelIdAsync(
            DataChannelRequestContext context,
            NodeId sourceNodeId,
            DataChannelParametersDataType attempted,
            uint channelId,
            DataChannelParametersDataType revised,
            CancellationToken ct)
        {
            if (!string.Equals(
                context.TransportProfileUri,
                Profiles.UaQuicTransport,
                StringComparison.Ordinal))
            {
                return 0;
            }

            if (IsClientInitiatedDirection(revised.Direction))
            {
                if (context.TransportChannelId == 0)
                {
                    Refuse(
                        context,
                        sourceNodeId,
                        attempted,
                        StatusCodes.BadDataChannelLimitsExceeded);
                }

                if (m_streamAllocator == null)
                {
                    Refuse(
                        context,
                        sourceNodeId,
                        attempted,
                        StatusCodes.BadDataChannelTransportUnsupported);
                }

                await m_streamAllocator!
                    .BindClientStreamAsync(
                        context,
                        channelId,
                        context.TransportChannelId,
                        revised.Direction,
                        ct)
                    .ConfigureAwait(false);

                return context.TransportChannelId;
            }

            if (m_streamAllocator == null)
            {
                Refuse(
                    context,
                    sourceNodeId,
                    attempted,
                    StatusCodes.BadDataChannelTransportUnsupported);
            }

            return await m_streamAllocator!
                .AllocateServerStreamAsync(context, channelId, revised.Direction, ct)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Tells the source when its channel reaches a terminal state.
        /// </summary>
        /// <remarks>
        /// The source counts its own live channels against
        /// <see cref="DataChannelSourceCapabilities.MaxChannels"/>, and the
        /// only place it can decrement is here. Without this the count only
        /// ever rises and the source refuses every channel with
        /// <c>Bad_TooManyDataChannels</c> once it has opened its limit even if
        /// none is still open.
        /// </remarks>
        /// <param name="channel">The channel.</param>
        /// <param name="source">The source that produced it.</param>
        private void TrackSourceLifetime(DataChannel channel, IDataChannelSource source)
        {
            int notified = 0;

            void Close(StatusCode reason)
            {
                if (Interlocked.Exchange(ref notified, 1) != 0)
                {
                    return;
                }

                channel.StateChanged -= OnStateChanged;

                // The authorizing Session is deliberately still recorded: it
                // is what lets a later Close on this identifier answer
                // Bad_DataChannelClosed rather than Bad_DataChannelIdInvalid,
                // which Part 4 distinguishes and a Client acts on.
                source.OnChannelClosed(channel, reason);
            }

            void OnStateChanged(object? sender, DataChannelStateChangedEventArgs e)
            {
                if (e.State is DataChannelState.Closed or DataChannelState.Faulted)
                {
                    Close(e.Status);
                }
            }

            channel.StateChanged += OnStateChanged;

            // A channel can reach a terminal state between being registered
            // and being subscribed to - an abort during open does exactly
            // that - so the current state is checked once after subscribing.
            if (channel.State is DataChannelState.Closed or DataChannelState.Faulted)
            {
                Close(channel.Status);
            }
        }

        /// <summary>
        /// Builds the parameters a ModifyDataChannel is revised against,
        /// pinning the immutable ones to what is already in force.
        /// </summary>
        /// <param name="requested">What the Client asked for, or <c>null</c>
        /// to change nothing.</param>
        /// <param name="inForce">The parameters currently in force.</param>
        private static DataChannelParametersDataType CloneForModify(
            DataChannelParametersDataType? requested,
            DataChannelParametersDataType inForce)
        {
            if (requested == null)
            {
                return inForce;
            }

            return new DataChannelParametersDataType
            {
                Direction = inForce.Direction,
                DeliveryMode = inForce.DeliveryMode,
                ContentType = requested.ContentType,
                ContentParameters = requested.ContentParameters,
                MaxFrameSize = requested.MaxFrameSize,
                InitialCredit = requested.InitialCredit,
                Priority = requested.Priority,
                MaxBitrate = requested.MaxBitrate,
                MaxRetransmits = requested.MaxRetransmits,
                FrameDeadline = requested.FrameDeadline
            };
        }

        private static bool IsClientInitiatedDirection(DataChannelDirection direction)
        {
            return direction is DataChannelDirection.SinkToSource or DataChannelDirection.Bidirectional;
        }

        private OpenDataChannelResponse Refuse(
            DataChannelRequestContext context,
            NodeId sourceNodeId,
            DataChannelParametersDataType attempted,
            StatusCode status)
        {
            // An audit event is generated on failure too: a refused
            // attempt to start a media stream is exactly as interesting
            // to an auditor as a successful one.
            m_auditor?.OnOpenDataChannel(context, sourceNodeId, attempted, null, status);
            throw new ServiceResultException(status);
        }

        private readonly ConcurrentDictionary<uint, NodeId> m_authorizingSessions = new();
        private readonly DataChannelManager m_manager;
        private readonly DataChannelSourceRegistry m_sources;
        private readonly DataChannelServerCapabilities m_capabilities;
        private readonly IDataChannelAuthorizer m_authorizer;
        private readonly IDataChannelAuditor? m_auditor;
        private readonly IDataChannelTransportStreamAllocator? m_streamAllocator;
    }
}
