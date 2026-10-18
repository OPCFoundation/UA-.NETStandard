/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.DataChannels;

namespace Opc.Ua.Client.DataChannels
{
    /// <summary>
    /// Independently authorizes the opener's resource policy and the grant returned by its participating
    /// control Server. A grant never replaces application authorization on this peer.
    /// </summary>
    public sealed class SessionDataChannelAuthorizer : IDataChannelAuthorizer, IAsyncDisposable
    {
        /// <summary>
        /// Creates a Session-bound grant consumer around a caller-owned local resource policy.
        /// Configured requests still use only that policy.
        /// </summary>
        public SessionDataChannelAuthorizer(
            ISession session,
            IDataChannelAuthorizer applicationPolicy,
            TimeProvider timeProvider,
            ITelemetryContext telemetry,
            int maximumFlows = 128)
            : this(applicationPolicy, timeProvider, telemetry, maximumFlows)
        {
            AttachSession(session);
        }

        /// <summary>
        /// Creates an unbound authorizer for a shared data transport whose ordinary Session is opened later.
        /// Session authorization fails closed until attachment; configured resource policy remains independent.
        /// </summary>
        public SessionDataChannelAuthorizer(
            IDataChannelAuthorizer applicationPolicy,
            TimeProvider timeProvider,
            ITelemetryContext telemetry,
            int maximumFlows = 128)
        {
            m_policy = applicationPolicy ?? throw new ArgumentNullException(nameof(applicationPolicy));
            m_clock = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
            if (telemetry == null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            if (maximumFlows is <= 0 or > 1024)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumFlows));
            }
            m_logger = telemetry.CreateLogger<SessionDataChannelAuthorizer>();
            m_maximumFlows = maximumFlows;
            m_lastContact = timeProvider.GetTimestamp();
        }

        /// <summary>
        /// Attaches one authenticated Session after shared HELLO and ordinary Session activation.
        /// Reattaching the same Session is harmless; transferring grants to a different Session is refused.
        /// </summary>
        public void AttachSession(ISession session)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }
            lock (m_gate)
            {
                ThrowIfDisposed();
                if (m_session != null)
                {
                    if (ReferenceEquals(m_session, session) && m_sessionId == session.SessionId)
                    {
                        return;
                    }
                    throw new InvalidOperationException("The data authorizer is already bound to another Session.");
                }
                if (session.SessionId.IsNull || !session.Connected)
                {
                    throw new ArgumentException("An activated Session is required.", nameof(session));
                }
                m_session = session;
                m_sessionId = session.SessionId;
                m_identity = session.Identity;
                m_lastContact = m_clock.GetTimestamp();
                session.SessionClosing += OnSessionClosing;
                session.SessionConfigurationChanged += OnSessionChanged;
                session.KeepAlive += OnKeepAlive;
                if (session.SessionId != m_sessionId || !session.Connected || !m_identity.Equals(session.Identity))
                {
                    m_revoked = true;
                    throw new InvalidOperationException("The Session changed while authorization was attached.");
                }
            }
        }

        /// <inheritdoc/>
        public async ValueTask<DataChannelAuthorization> AuthorizeAsync(
            DataChannelAuthorizationRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            DataChannelAuthorization application = await m_policy.AuthorizeAsync(request, cancellationToken)
                .ConfigureAwait(false);
            if (request.Mode == DataChannelAuthorizationMode.Configured || application.Result != DataChannelResult.Good)
            {
                return application;
            }
            lock (m_gate)
            {
                var key = (request.Context.ContextId, request.Context.Generation, request.ChannelId);
                if (request.Mode != DataChannelAuthorizationMode.Session || !request.IsLocalOpener ||
                    !IsSessionValid() || !m_grants.TryGetValue(key, out AcceptedGrant? accepted) ||
                    !MatchesContext(accepted.Context.Identity, request.Context))
                {
                    return new DataChannelAuthorization(DataChannelResult.NotAuthorized);
                }
                if (request.Purpose == DataChannelAuthorizationPurpose.Open)
                {
                    if (accepted.Redeemed || !request.GrantId.HasValue ||
                        request.GrantId.Value != (Guid)accepted.Grant.GrantId ||
                        m_clock.GetElapsedTime(accepted.RequestStarted) >= TimeSpan.FromSeconds(10) ||
                        !FlowModel.SameParameters(request.Parameters, accepted.Grant.Parameters))
                    {
                        return new DataChannelAuthorization(DataChannelResult.NotAuthorized);
                    }
                    accepted.Redeemed = true;
                }
                else if (!accepted.Redeemed)
                {
                    return new DataChannelAuthorization(DataChannelResult.NotAuthorized);
                }
                if (request.Purpose == DataChannelAuthorizationPurpose.Update &&
                    (accepted.OperationId != request.OperationId || accepted.Revision != request.Revision ||
                    accepted.Proposal == null || !FlowModel.SameParameters(accepted.Proposal, request.Parameters)))
                {
                    return new DataChannelAuthorization(DataChannelResult.NotAuthorized);
                }
                TimeSpan lifetime = TimeSpan.FromMilliseconds(m_session!.SessionTimeout) -
                    m_clock.GetElapsedTime(m_lastContact);
                if (application.Lifetime is { } applicationLifetime && applicationLifetime < lifetime)
                {
                    lifetime = applicationLifetime;
                }
                if (lifetime <= TimeSpan.Zero)
                {
                    return new DataChannelAuthorization(DataChannelResult.NotAuthorized);
                }
                return new DataChannelAuthorization(
                    DataChannelResult.Good, application.Parameters, accepted.Grant.SourceNodeId, m_sessionId, lifetime,
                    accepted.LinkRevocation(application.RevocationToken));
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            DataChannelContext[] contexts;
            AcceptedGrant[] grants;
            lock (m_gate)
            {
                if (m_disposed)
                {
                    return;
                }
                m_disposed = true;
                m_revoked = true;
                contexts = [.. m_contexts.Keys];
                foreach (KeyValuePair<DataChannelContext, Action<DataChannelResult>> item in m_contexts)
                {
                    item.Key.Closed -= item.Value;
                }
                m_contexts.Clear();
                grants = [.. m_grants.Values];
                m_grants.Clear();
            }
            m_revocation.Cancel();
            foreach (AcceptedGrant grant in grants)
            {
                grant.Dispose();
            }
            if (m_session != null)
            {
                m_session.SessionClosing -= OnSessionClosing;
                m_session.SessionConfigurationChanged -= OnSessionChanged;
                m_session.KeepAlive -= OnKeepAlive;
            }
            foreach (DataChannelContext context in contexts)
            {
                await context.RecheckAuthorizationAsync().ConfigureAwait(false);
            }
            m_revocation.Dispose();
        }

        /// <summary>
        /// Records only a grant verified by the generated-proxy control helper.
        /// </summary>
        internal void AcceptGrant(
            DataChannelContext context,
            DataChannelGrantDataType grant,
            long requestStarted)
        {
            lock (m_gate)
            {
                ThrowIfDisposed();
                if (!IsSessionValid() || m_grants.Count >= m_maximumFlows ||
                    grant.SessionId != m_sessionId || grant.GrantId == Guid.Empty ||
                    grant.ContextId != context.Identity.ContextId || grant.Generation != context.Identity.Generation)
                {
                    throw new DataChannelException(DataChannelResult.NotAuthorized);
                }
                var key = (context.Identity.ContextId, context.Identity.Generation, grant.ChannelId);
                if (m_grants.ContainsKey(key))
                {
                    throw new DataChannelException(DataChannelResult.InvalidState);
                }
                m_grants.Add(key, new AcceptedGrant(
                    context, (DataChannelGrantDataType)grant.Clone(), requestStarted, m_revocation.Token));
                m_lastContact = m_clock.GetTimestamp();
                if (!m_contexts.ContainsKey(context))
                {
                    void Closed(DataChannelResult result) => RemoveContext(context);
                    context.Closed += Closed;
                    m_contexts.Add(context, Closed);
                }
            }
        }

        /// <summary>
        /// Records the exact proposal admitted by Call, without claiming its data revision committed.
        /// </summary>
        internal void AcceptUpdate(
            DataChannel channel,
            uint operationId,
            uint revision,
            DataChannelParametersDataType parameters)
        {
            DataChannelStatusDataType status = channel.GetStatus();
            lock (m_gate)
            {
                if (!IsSessionValid() || !m_grants.TryGetValue(
                    (status.ContextId, status.Generation, status.ChannelId), out AcceptedGrant? grant))
                {
                    throw new DataChannelException(DataChannelResult.NotAuthorized);
                }
                grant.OperationId = operationId;
                grant.Revision = revision;
                grant.Proposal = FlowModel.Copy(parameters);
                m_lastContact = m_clock.GetTimestamp();
            }
        }

        /// <summary>
        /// Removes an abandoned grant after failed establishment.
        /// </summary>
        internal void Forget(DataChannelContext context, uint channelId)
        {
            lock (m_gate)
            {
                if (context.IsReady)
                {
                    var key = (context.Identity.ContextId, context.Identity.Generation, channelId);
                    if (m_grants.TryGetValue(key, out AcceptedGrant? grant))
                    {
                        grant.Dispose();
                        m_grants.Remove(key);
                    }
                }
            }
        }

        /// <summary>
        /// Requires the same Session and identity; a disconnected control transport is not revocation.
        /// </summary>
        private bool IsSessionValid()
        {
            return !m_revoked && !m_disposed && m_session is { } session && m_identity != null &&
                session.SessionId == m_sessionId && m_identity.Equals(session.Identity) && session.SessionTimeout > 0 &&
                m_clock.GetElapsedTime(m_lastContact).TotalMilliseconds < session.SessionTimeout;
        }

        /// <summary>
        /// Rejects use after disposal on every supported framework.
        /// </summary>
        private void ThrowIfDisposed()
        {
            if (m_disposed)
            {
                throw new ObjectDisposedException(nameof(SessionDataChannelAuthorizer));
            }
        }

        /// <summary>
        /// Checks both peers, both challenges and the exact carrier generation.
        /// </summary>
        private static bool MatchesContext(DataChannelContextIdentity expected, DataChannelContextIdentity actual)
        {
            return expected.ContextId == actual.ContextId && expected.Generation == actual.Generation &&
                expected.Carrier == actual.Carrier && expected.LocalChallenge == actual.LocalChallenge &&
                expected.PeerChallenge == actual.PeerChallenge &&
                expected.Peer.LocalApplicationUri == actual.Peer.LocalApplicationUri &&
                expected.Peer.PeerApplicationUri == actual.Peer.PeerApplicationUri &&
                expected.Peer.LocalKeyThumbprint == actual.Peer.LocalKeyThumbprint &&
                expected.Peer.PeerKeyThumbprint == actual.Peer.PeerKeyThumbprint &&
                expected.Peer.Protection == actual.Peer.Protection;
        }

        /// <summary>
        /// Tracks only successful control keep-alives. Failure alone does not close a valid Session's flow.
        /// </summary>
        private void OnKeepAlive(ISession session, KeepAliveEventArgs args)
        {
            if (ServiceResult.IsGood(args.Status))
            {
                lock (m_gate)
                {
                    m_lastContact = m_clock.GetTimestamp();
                }
            }
        }

        /// <summary>
        /// Revokes when the actual Session closes.
        /// </summary>
        private void OnSessionClosing(object? sender, EventArgs args)
        {
            Revoke();
        }

        /// <summary>
        /// Preserves same-identity reactivation while rejecting changed users or Sessions.
        /// </summary>
        private void OnSessionChanged(object? sender, EventArgs args)
        {
            if (m_session == null || m_session.SessionId != m_sessionId ||
                m_identity?.Equals(m_session.Identity) != true)
            {
                Revoke();
            }
        }

        /// <summary>
        /// Marks authorization invalid before scheduling context rechecks.
        /// </summary>
        private void Revoke()
        {
            DataChannelContext[] contexts;
            lock (m_gate)
            {
                if (m_disposed)
                {
                    return;
                }
                m_revoked = true;
                contexts = [.. m_contexts.Keys];
            }
            try
            {
                m_revocation.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // A captured Session callback can race disposal after it has been detached.
            }
            foreach (DataChannelContext context in contexts)
            {
                _ = RecheckAsync(context);
            }
        }

        /// <summary>
        /// Rechecks outside the Session event publisher's synchronization.
        /// </summary>
        private async Task RecheckAsync(DataChannelContext context)
        {
            await Task.Yield();
            try
            {
                await context.RecheckAuthorizationAsync().ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                m_logger.RecheckFailed(exception);
            }
        }

        /// <summary>
        /// Releases only records associated with a closed authenticated data context.
        /// </summary>
        private void RemoveContext(DataChannelContext context)
        {
            lock (m_gate)
            {
                List<(Guid, ulong, uint)> keys = [];
                foreach (KeyValuePair<(Guid, ulong, uint), AcceptedGrant> item in m_grants)
                {
                    if (ReferenceEquals(item.Value.Context, context))
                    {
                        keys.Add(item.Key);
                    }
                }
                foreach ((Guid, ulong, uint) key in keys)
                {
                    m_grants[key].Dispose();
                    m_grants.Remove(key);
                }
                if (m_contexts.TryGetValue(context, out Action<DataChannelResult>? handler))
                {
                    context.Closed -= handler;
                    m_contexts.Remove(context);
                }
            }
        }

        /// <summary>
        /// A grant obtained from the authenticated participating application's ordinary Call response.
        /// </summary>
        private sealed class AcceptedGrant(
            DataChannelContext context,
            DataChannelGrantDataType grant,
            long requestStarted,
            CancellationToken sessionRevocation) : IDisposable
        {
            public DataChannelContext Context { get; } = context;
            public DataChannelGrantDataType Grant { get; } = grant;
            public long RequestStarted { get; } = requestStarted;
            public bool Redeemed { get; set; }
            public uint OperationId { get; set; }
            public uint Revision { get; set; }
            public DataChannelParametersDataType? Proposal { get; set; }

            /// <summary>
            /// Keeps old policy registrations alive until flow retirement so replacement cannot miss revocation.
            /// </summary>
            internal CancellationToken LinkRevocation(CancellationToken policy)
            {
                if (!policy.CanBeCanceled)
                {
                    return m_lifetime.Token;
                }
                if (!m_policyTokens.TryGetValue(policy, out CancellationTokenSource? linked))
                {
                    if (m_policyTokens.Count >= 64)
                    {
                        throw new DataChannelException(DataChannelResult.NotAuthorized);
                    }
                    linked = CancellationTokenSource.CreateLinkedTokenSource(m_lifetime.Token, policy);
                    m_policyTokens.Add(policy, linked);
                }
                return linked.Token;
            }

            /// <inheritdoc/>
            public void Dispose()
            {
                m_lifetime.Cancel();
                foreach (CancellationTokenSource linked in m_policyTokens.Values)
                {
                    linked.Dispose();
                }
                m_policyTokens.Clear();
                m_lifetime.Dispose();
            }

            private readonly CancellationTokenSource m_lifetime =
                CancellationTokenSource.CreateLinkedTokenSource(sessionRevocation);
            private readonly Dictionary<CancellationToken, CancellationTokenSource> m_policyTokens = [];
        }

        private readonly Lock m_gate = new();
        private ISession? m_session;
        private NodeId m_sessionId;
        private IUserIdentity? m_identity;
        private readonly IDataChannelAuthorizer m_policy;
        private readonly TimeProvider m_clock;
        private readonly ILogger m_logger;
        private readonly int m_maximumFlows;
        private readonly CancellationTokenSource m_revocation = new();
        private readonly Dictionary<(Guid, ulong, uint), AcceptedGrant> m_grants = [];
        private readonly Dictionary<DataChannelContext, Action<DataChannelResult>> m_contexts = [];
        private long m_lastContact;
        private bool m_revoked;
        private bool m_disposed;
    }

    /// <summary>
    /// Source-generated client authorization diagnostics without grant or key material.
    /// </summary>
    internal static partial class SessionDataChannelAuthorizerLog
    {
        /// <summary>
        /// Records a failed asynchronous revocation notification.
        /// </summary>
        [LoggerMessage(EventId = ClientEventIds.DataChannelControl, Level = LogLevel.Warning,
            Message = "Data channel Session authorization recheck failed.")]
        internal static partial void RecheckFailed(this ILogger logger, Exception exception);
    }
}
