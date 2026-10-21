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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Borrows one real server-session generation for requests that retain remote handles or policy decisions.
    /// </summary>
    /// <remarks>
    /// Use <see cref="Session"/> for every classification, request and cleanup in the operation.
    /// Each wire dispatch checks the originating stack session, its generation and namespace mappings,
    /// then sends once using the captured authentication token and physical transport. Recovery invalidates
    /// the context even when it might ultimately reactivate the same server session. Already dispatched
    /// requests may complete; their results are not replaced by a later cancellation or invalidation.
    /// The context owns only local client resources, never the owner's session, subscriptions or channel.
    /// Server-side model changes within a session still require the caller's model-invalidation policy.
    /// </remarks>
    public sealed class SessionRequestContext : IAsyncDisposable
    {
        /// <summary>
        /// Captures a session under identity synchronization and, when present, its managed service gate.
        /// </summary>
        internal SessionRequestContext(
            ManagedSession? owner, Session session, long generation, bool requireConfidentiality = false)
        {
            m_owner = owner;
            m_originalSession = session;
            m_generation = generation;
            m_requireConfidentiality = requireConfidentiality;
            SessionConfiguration configuration = session.SaveSessionConfiguration();
            SessionId = configuration.SessionId;
            m_authenticationToken = configuration.AuthenticationToken;
            if (SessionId.IsNull || m_authenticationToken.IsNull)
            {
                throw InvalidSession();
            }
            m_originalChannel = session.TransportChannel;
            m_lease = m_originalChannel as ManagedTransportChannelLease;
            if (m_originalChannel is IManagedTransportChannel && m_lease == null)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported,
                    "The custom managed transport does not provide a fixed dispatch transport.");
            }
            m_entry = m_lease?.Entry;
            m_channelGeneration = m_entry?.ReconnectGeneration ?? 0;
            m_transport = m_entry?.Underlying ?? m_originalChannel;
            if (m_entry != null && (m_entry.State != ChannelState.Ready || !m_lease!.IsActive))
            {
                throw InvalidSession();
            }
            ValidateConfidentiality();
            m_namespaceUris = session.NamespaceUris.ToArrayOf();
            m_serverUris = session.ServerUris.ToArrayOf();
            m_messageContext = new ServiceMessageContext(session.MessageContext, session.MessageContext.Telemetry);
            m_endpoint = CoreUtils.Clone(session.ConfiguredEndpoint.Description)!;
            m_endpointConfiguration = CoreUtils.Clone(session.ConfiguredEndpoint.Configuration)
                ?? EndpointConfiguration.Create();
            m_requestTransport = new RequestContextTransport(this);
            try
            {
                m_session = session.CreateRequestContextSession(m_requestTransport, configuration);
            }
            catch
            {
                m_requestTransport.Dispose();
                throw;
            }
            session.RequestContextChanged += Invalidate;
            if (owner != null)
            {
                owner.ConnectionStateChanged += OnConnectionStateChanged;
                owner.ChannelStateChanged += OnChannelStateChanged;
            }
        }

        /// <summary>
        /// Gets a local, generation-bound session for existing typed clients, node caches and service methods.
        /// Closing or disposing it never closes the original server session.
        /// </summary>
        public ISession Session => m_session;

        /// <summary>
        /// Gets the original server-assigned session identifier, including after invalidation or disposal.
        /// </summary>
        public NodeId SessionId { get; }

        /// <summary>
        /// Completes when the generation changes, recovery or close begins, or this context is disposed.
        /// Namespace remapping discovered at dispatch also completes this task. It is not a model-change monitor.
        /// </summary>
        public Task Invalidated => m_invalidated.Task;

        /// <summary>
        /// Captures a stack-managed session without acquiring ownership of its connection.
        /// </summary>
        /// <remarks>
        /// Ordinary or custom <see cref="ISession"/> implementations are rejected: their lifecycle cannot be
        /// synchronized by this API. Custom transports must not be independently retargeted outside their owner.
        /// Reuse the returned <see cref="Session"/> across later cursor or handle operations; do not capture again.
        /// </remarks>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ServiceResultException"></exception>
        public static ValueTask<SessionRequestContext> CaptureAsync(
            ISession session, CancellationToken ct = default)
        {
            if (session is null)
            {
                throw new ArgumentNullException(nameof(session));
            }
            if (session is not ManagedSession managed)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported,
                    "Generation-bound requests require a stack ManagedSession.");
            }
            return managed.CaptureRequestContextAsync(requireConfidentiality: false, ct);
        }

        /// <summary>
        /// Captures an encrypted dispatch channel for an operation carrying passwords or private keys.
        /// </summary>
        /// <remarks>
        /// Supports stack <see cref="ManagedSession"/> and <see cref="Client.Session"/> instances.
        /// Use only the returned <see cref="Session"/> for classification and dispatch, and dispose this
        /// context after the operation. Every send checks the original session, authentication token,
        /// namespace tables and physical channel generation, then requires that channel's actual endpoint
        /// to use SignAndEncrypt with a non-None security policy. The operation never follows a replacement
        /// session or retries on a replacement channel. Unsupported custom sessions fail closed.
        /// Custom transports must not be independently retargeted outside their stack owner.
        /// </remarks>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ServiceResultException"></exception>
        public static async ValueTask<SessionRequestContext> CaptureConfidentialAsync(
            ISession session, CancellationToken ct = default)
        {
            if (session is null)
            {
                throw new ArgumentNullException(nameof(session));
            }
            ct.ThrowIfCancellationRequested();
            switch (session)
            {
                case ManagedSession managed:
                    return await managed.CaptureRequestContextAsync(requireConfidentiality: true, ct)
                        .ConfigureAwait(false);
                case Session fixedSession:
                    if (fixedSession.NullableTransportChannel is RequestContextTransport borrowed)
                    {
                        return await borrowed.CaptureConfidentialAsync(ct).ConfigureAwait(false);
                    }
                    return fixedSession.CreateRequestContext(owner: null, requireConfidentiality: true);
                default:
                    throw new ServiceResultException(
                        StatusCodes.BadNotSupported,
                        "Confidential requests require a stack Session or ManagedSession with fixed dispatch.");
            }
        }

        /// <summary>
        /// Invokes an operation with the borrowed session; each service request is independently guarded.
        /// </summary>
        /// <remarks>
        /// There is deliberately no callback-entry or callback-exit cancellation or generation check.
        /// Local cleanup must still run after invalidation, and a known completed mutation must retain its result.
        /// This method does not hold a service gate across local work or awaits.
        /// </remarks>
        /// <typeparam name="TResult">The operation's result type.</typeparam>
        /// <exception cref="ArgumentNullException"></exception>
        public ValueTask<TResult> ExecuteAsync<TResult>(
            Func<ISession, CancellationToken, ValueTask<TResult>> operation, CancellationToken ct = default)
        {
            if (operation is null)
            {
                throw new ArgumentNullException(nameof(operation));
            }
            return operation(m_session, ct);
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref m_disposed, 1) != 0)
            {
                return;
            }
            Invalidate();
            m_originalSession.RequestContextChanged -= Invalidate;
            if (m_owner != null)
            {
                m_owner.ConnectionStateChanged -= OnConnectionStateChanged;
                m_owner.ChannelStateChanged -= OnChannelStateChanged;
            }
            try
            {
                await m_session.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                m_requestTransport.Dispose();
            }
        }

        /// <summary>
        /// Validates and begins a single send while the owner's service gate and session identity guard are held.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal ValueTask<IServiceResponse> SendAsync(
            Session current, long generation, NodeId authenticationToken,
            IServiceRequest request, CancellationToken ct)
        {
            if (m_invalidated.Task.IsCompleted ||
                !ReferenceEquals(current, m_originalSession) ||
                generation != m_generation ||
                current.SessionId != SessionId ||
                authenticationToken != m_authenticationToken ||
                !ReferenceEquals(current.NullableTransportChannel, m_originalChannel) ||
                (m_lease != null &&
                    (!m_lease.IsActive ||
                        !ReferenceEquals(m_lease.Entry, m_entry) ||
                        m_entry!.ReconnectGeneration != m_channelGeneration ||
                        m_entry.State != ChannelState.Ready ||
                        !ReferenceEquals(m_entry.Underlying, m_transport))) ||
                !Matches(current.NamespaceUris, m_namespaceUris) ||
                !Matches(current.ServerUris, m_serverUris))
            {
                Invalidate();
                throw InvalidSession();
            }
            if (request is CreateSessionRequest or ActivateSessionRequest or CloseSessionRequest)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported,
                    "A borrowed request context cannot change or close the owning server session.");
            }
            request.RequestHeader ??= new RequestHeader();
            if (!request.RequestHeader.AuthenticationToken.IsNull &&
                request.RequestHeader.AuthenticationToken != m_authenticationToken)
            {
                throw InvalidSession();
            }
            ct.ThrowIfCancellationRequested();
            ValidateConfidentiality();
            request.RequestHeader.AuthenticationToken = m_authenticationToken;

            // A managed lease may wait, swap transports, retry, or reject an already completed
            // response on cancellation. Send once on the physical transport captured above instead.
            return m_transport.SendRequestAsync(request, ct);
        }

        /// <summary>
        /// Promotes a borrowed session without trusting its advertised metadata or adopting a newer owner generation.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private async ValueTask<SessionRequestContext> CaptureConfidentialCopyAsync(CancellationToken ct)
        {
            if (m_invalidated.Task.IsCompleted)
            {
                throw InvalidSession();
            }
            SessionRequestContext captured = await CaptureConfidentialAsync(
                (ISession?)m_owner ?? m_originalSession, ct).ConfigureAwait(false);
            if (m_invalidated.Task.IsCompleted ||
                !ReferenceEquals(captured.m_originalSession, m_originalSession) ||
                captured.m_generation != m_generation ||
                captured.SessionId != SessionId ||
                captured.m_authenticationToken != m_authenticationToken ||
                !ReferenceEquals(captured.m_originalChannel, m_originalChannel) ||
                !ReferenceEquals(captured.m_transport, m_transport) ||
                captured.m_channelGeneration != m_channelGeneration ||
                !captured.m_namespaceUris.Equals(m_namespaceUris) ||
                !captured.m_serverUris.Equals(m_serverUris))
            {
                Invalidate();
                await captured.DisposeAsync().ConfigureAwait(false);
                throw InvalidSession();
            }
            return captured;
        }

        /// <summary>
        /// Checks the physical channel, not mutable advertised session metadata, before any confidential send.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private void ValidateConfidentiality()
        {
            if (!m_requireConfidentiality)
            {
                return;
            }
            EndpointDescription endpoint = m_transport.EndpointDescription;
            if (endpoint == null ||
                endpoint.SecurityMode != MessageSecurityMode.SignAndEncrypt ||
                string.IsNullOrEmpty(endpoint.SecurityPolicyUri) ||
                endpoint.SecurityPolicyUri == SecurityPolicies.None)
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityModeInsufficient,
                    "Confidential method arguments require an encrypted channel.");
            }
        }

        /// <summary>
        /// Tests table identity without allocating another copy for every dispatch.
        /// </summary>
        private static bool Matches(StringTable table, ArrayOf<string> captured)
        {
            if (table.Count != captured.Count)
            {
                return false;
            }
            for (int i = 0; i < captured.Count; i++)
            {
                if (!string.Equals(table.GetString((uint)i), captured[i], StringComparison.Ordinal))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Creates a failure that never includes authentication material.
        /// </summary>
        private static ServiceResultException InvalidSession()
        {
            return new ServiceResultException(
                StatusCodes.BadSessionIdInvalid,
                "The captured session generation or namespace mapping is no longer current.");
        }

        /// <summary>
        /// Signals invalidation without running a consumer's continuation inside recovery.
        /// </summary>
        private void Invalidate()
        {
            m_invalidated.TrySetResult(true);
        }

        /// <summary>
        /// Invalidates conservatively rather than following reconnection or failover.
        /// </summary>
        private void OnConnectionStateChanged(object? sender, ConnectionStateChangedEventArgs args)
        {
            if (args.NewState != ConnectionState.Connected)
            {
                Invalidate();
            }
        }

        /// <summary>
        /// Detects recovery initiated by the shared channel manager.
        /// </summary>
        private void OnChannelStateChanged(ManagedSession sender, ChannelStateChange change)
        {
            if (change.NewState != ChannelState.Ready)
            {
                Invalidate();
            }
        }

        /// <summary>
        /// A non-owning transport for the existing generated Session implementation.
        /// </summary>
        private sealed class RequestContextTransport(SessionRequestContext context) : ITransportChannel
        {
            /// <inheritdoc/>
            public TransportChannelFeatures SupportedFeatures => TransportChannelFeatures.None;

            /// <inheritdoc/>
            public EndpointDescription EndpointDescription => context.m_endpoint;

            /// <inheritdoc/>
            public EndpointConfiguration EndpointConfiguration => context.m_endpointConfiguration;

            /// <inheritdoc/>
            public byte[] ChannelThumbprint => context.m_transport.ChannelThumbprint;

            /// <inheritdoc/>
            public byte[] ClientChannelCertificate => context.m_transport.ClientChannelCertificate;

            /// <inheritdoc/>
            public byte[] ServerChannelCertificate => context.m_transport.ServerChannelCertificate;

            /// <inheritdoc/>
            public IServiceMessageContext MessageContext => context.m_messageContext;

            /// <inheritdoc/>
            public int OperationTimeout { get; set; } = context.m_originalSession.OperationTimeout;

            /// <inheritdoc/>
            public ValueTask ReconnectAsync(
                ITransportWaitingConnection? connection = null, CancellationToken ct = default)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported, "A borrowed request context cannot reconnect its owner.");
            }

            /// <inheritdoc/>
            public ValueTask<IServiceResponse> SendRequestAsync(IServiceRequest request, CancellationToken ct = default)
            {
                if (context.m_invalidated.Task.IsCompleted)
                {
                    throw InvalidSession();
                }
                return context.m_owner != null
                    ? context.m_owner.SendRequestContextAsync(context, request, ct)
                    : context.m_originalSession.SendRequestContextAsync(context, request, ct);
            }

            /// <inheritdoc/>
            public ValueTask CloseAsync(CancellationToken ct = default)
            {
                Dispose();
                return default;
            }

            /// <inheritdoc/>
            public void Dispose()
            {
                context.Invalidate();
            }

            /// <summary>
            /// Captures confidentiality at the physical owner, never at this metadata-only adapter.
            /// </summary>
            internal ValueTask<SessionRequestContext> CaptureConfidentialAsync(CancellationToken ct)
            {
                return context.CaptureConfidentialCopyAsync(ct);
            }
        }

        private readonly ManagedSession? m_owner;
        private readonly Session m_originalSession;
        private readonly Session m_session;
        private readonly RequestContextTransport m_requestTransport;
        private readonly long m_generation;
        private readonly NodeId m_authenticationToken;
        private readonly ITransportChannel m_originalChannel;
        private readonly ITransportChannel m_transport;
        private readonly ManagedTransportChannelLease? m_lease;
        private readonly ChannelEntry? m_entry;
        private readonly long m_channelGeneration;
        private readonly ArrayOf<string> m_namespaceUris;
        private readonly ArrayOf<string> m_serverUris;
        private readonly ServiceMessageContext m_messageContext;
        private readonly EndpointDescription m_endpoint;
        private readonly EndpointConfiguration m_endpointConfiguration;

        /// <summary>Whether every physical dispatch requires an encrypted channel.</summary>
        private readonly bool m_requireConfidentiality;

        private readonly TaskCompletionSource<bool> m_invalidated =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private int m_disposed;
    }
}
