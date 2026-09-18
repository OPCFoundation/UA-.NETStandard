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

namespace Opc.Ua
{
    public partial class SessionClient
    {
        /// <inheritdoc/>
        public virtual ValueTask<ISessionClient> CreateBindingAsync(CancellationToken ct = default)
        {
            IServiceMessageContext context = MessageContext;
            return CreateBindingCoreAsync(
                context.NamespaceUris, context.ServerUris, context.Telemetry, static () => true, ct);
        }

        /// <summary>
        /// Captures service dispatch with the owning session's current maps.
        /// </summary>
        /// <exception cref="ServiceResultException">
        /// The session changed, is closed, or cannot provide a generation-bound transport and maps.
        /// </exception>
        protected async ValueTask<ISessionClient> CreateBindingCoreAsync(
            NamespaceTable namespaces,
            StringTable servers,
            ITelemetryContext telemetry,
            Func<bool> ownerCurrent,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (namespaces.GetType() != typeof(NamespaceTable) || servers.GetType() != typeof(StringTable))
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported, "Custom mapping tables must provide their own binding capability.");
            }
            ITransportChannel channel = CaptureChannel(out long channelGeneration);
            NodeId sessionId;
            NodeId authenticationToken;
            long incarnation;
            lock (m_sessionBindingGate)
            {
                sessionId = SessionId;
                authenticationToken = AuthenticationToken;
                incarnation = m_sessionIncarnation;
            }
            if (sessionId.IsNull || authenticationToken.IsNull)
            {
                throw new ServiceResultException(StatusCodes.BadSessionClosed);
            }
            if (channel is not ITransportChannelBindingProvider provider)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported, "The transport cannot capture a generation-bound session.");
            }
            if (channel.MessageContext is not ServiceMessageContext sourceContext ||
                sourceContext.GetType() != typeof(ServiceMessageContext))
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported, "Custom message contexts must provide their own binding capability.");
            }
            long sourceMappingVersion = sourceContext.MappingVersion;
            if (!ReferenceEquals(sourceContext.NamespaceUris, namespaces) ||
                !ReferenceEquals(sourceContext.ServerUris, servers))
            {
                throw TransportChannelBinding.InvalidBinding();
            }
            var namespaceCopy = new NamespaceTable(
                namespaces.GetSnapshot(out long namespaceVersion).ToArray() ??
                throw TransportChannelBinding.InvalidBinding());
            var serverCopy = new StringTable(
                servers.GetSnapshot(out long serverVersion).ToArray() ??
                throw TransportChannelBinding.InvalidBinding());
            long copiedNamespaceVersion = namespaceCopy.Version;
            long copiedServerVersion = serverCopy.Version;
            TransportChannelBinding captured = await provider.CreateTransportBindingAsync(ct).ConfigureAwait(false);
            ServiceMessageContext? context = null;
            long copiedMappingVersion = 0;
            bool IsCurrent()
            {
                bool current;
                lock (m_sessionBindingGate)
                {
                    current = m_sessionIncarnation == incarnation &&
                        SessionId == sessionId &&
                        AuthenticationToken == authenticationToken;
                }
                return current &&
                    IsChannelCurrent(channel, channelGeneration) &&
                    captured.HasSourceContext(sourceContext) &&
                    ReferenceEquals(channel.MessageContext, sourceContext) &&
                    ReferenceEquals(sourceContext.NamespaceUris, namespaces) &&
                    ReferenceEquals(sourceContext.ServerUris, servers) &&
                    sourceContext.MappingVersion == sourceMappingVersion &&
                    namespaces.Version == namespaceVersion &&
                    servers.Version == serverVersion &&
                    context is not null &&
                    ReferenceEquals(context.NamespaceUris, namespaceCopy) &&
                    ReferenceEquals(context.ServerUris, serverCopy) &&
                    context.MappingVersion == copiedMappingVersion &&
                    namespaceCopy.Version == copiedNamespaceVersion &&
                    serverCopy.Version == copiedServerVersion &&
                    ownerCurrent();
            }
            try
            {
                ct.ThrowIfCancellationRequested();
                context = TransportChannelBinding.CopyContext(captured.MessageContext, namespaceCopy, serverCopy);
                copiedMappingVersion = context.MappingVersion;
                captured.AddValidation(IsCurrent, context);
                captured.ThrowIfInvalid();
                return new BoundSessionClient(captured, sessionId, authenticationToken, telemetry)
                {
                    ReturnDiagnostics = ReturnDiagnostics,
                    DefaultTimeoutHint = DefaultTimeoutHint,
                    ActivityTraceFlags = ActivityTraceFlags
                };
            }
            catch
            {
                captured.Dispose();
                throw;
            }
        }

        private readonly Lock m_sessionBindingGate = new();
        private long m_sessionIncarnation;

        private sealed class BoundSessionClient : SessionClient
        {
            internal BoundSessionClient(
                TransportChannelBinding binding,
                NodeId sessionId,
                NodeId authenticationToken,
                ITelemetryContext telemetry)
                : base(binding, telemetry)
            {
                m_binding = binding;
                base.SessionCreated(sessionId, authenticationToken);
            }

            public override void SessionCreated(NodeId sessionId, NodeId sessionCookie)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported, "A captured client cannot change its session incarnation.");
            }

            [Obsolete("A captured client cannot replace its channel.")]
            public override void AttachChannel(ITransportChannel channel)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported, "A captured client cannot replace its channel.");
            }

            [Obsolete("Close or dispose the captured client instead.")]
            public override void DetachChannel()
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported, "A captured client cannot detach its channel.");
            }

            protected override void UpdateRequestHeader(IServiceRequest request, bool useDefaults)
            {
                m_binding.ThrowIfInvalid();
                if (request.RequestHeader is { } header &&
                    !header.AuthenticationToken.IsNull &&
                    header.AuthenticationToken != AuthenticationToken)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadSecurityChecksFailed,
                        "A captured client cannot dispatch with a different session authentication token.");
                }
                base.UpdateRequestHeader(request, useDefaults);
            }

            protected override void Dispose(bool disposing)
            {
                if (!Disposed)
                {
                    m_binding.Dispose();
                    base.SessionCreated(default, default);
                    ReleaseChannel();
                    DisposeClientResources();
                }
                base.Dispose(disposing);
            }

            private readonly TransportChannelBinding m_binding;
        }
    }
}
