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
using Opc.Ua.Client;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Keeps generic mutation classification and dispatch on the same named, non-owning session view.
    /// </summary>
    internal sealed class McpMutationSession : IAsyncDisposable
    {
        /// <summary>
        /// Retains the original registration and either a guarded SDK context or an explicit fixed-host contract.
        /// </summary>
        private McpMutationSession(
            OpcUaSessionManager manager, OpcUaSessionManager.SessionInfo registration,
            SessionRequestContext? context, IMcpFixedSessionContract? fixedContract)
        {
            m_manager = manager;
            m_registration = registration;
            m_context = context;
            m_fixedContract = fixedContract;
            Access = new OpcUaSessionManager.SessionInfo
            {
                Name = registration.Name,
                Session = context?.Session ?? registration.Session,
                Endpoint = CoreUtils.Clone(registration.Endpoint)!,
                AuthType = registration.AuthType,
                ConnectedAt = registration.ConnectedAt,
                OwnsSession = false
            };
            if (context == null)
            {
                ISession session = registration.Session;
                m_sessionId = session.SessionId;
                m_messageContext = session.MessageContext;
                m_namespaces = session.NamespaceUris.ToArrayOf();
                m_serverUris = m_messageContext.ServerUris.ToArrayOf();
                m_nodeCache = session.NodeCache;
                m_endpoint = CoreUtils.Clone(session.ConfiguredEndpoint.Description)!;
            }
        }

        /// <summary>
        /// Gets the captured service view with the host's original registered name and metadata.
        /// </summary>
        internal OpcUaSessionManager.SessionInfo Access { get; }

        /// <summary>
        /// Captures an SDK generation before any policy metadata is read.
        /// Unknown custom session implementations fail closed unless their host explicitly supplies a fixed contract.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ServiceResultException"></exception>
        internal static async ValueTask<McpMutationSession> CaptureAsync(
            OpcUaSessionManager manager, OpcUaSessionManager.SessionInfo registration, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(manager);
            ArgumentNullException.ThrowIfNull(registration);
            ct.ThrowIfCancellationRequested();
            if (!ReferenceEquals(manager.GetSessionInfo(registration.Name), registration))
            {
                throw ChangedSession();
            }
            if (registration.Session is ManagedSession)
            {
                SessionRequestContext context = await SessionRequestContext.CaptureAsync(registration.Session, ct)
                    .ConfigureAwait(false);
                try
                {
                    return new McpMutationSession(manager, registration, context, null);
                }
                catch
                {
                    await context.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
            }
            IMcpFixedSessionContract? contract = manager.GetFixedSessionContract();
            if (contract == null || !contract.IsFixed(registration))
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported,
                    "Generic mutations on a custom session require an explicit IMcpFixedSessionContract. " +
                    "Use a stack ManagedSession for reconnectable sessions.");
            }
            return new McpMutationSession(manager, registration, null, contract);
        }

        /// <summary>
        /// Rechecks registration ownership and the observable fixed-host metadata before dispatch, never afterwards.
        /// SDK contexts additionally enforce their generation atomically at every actual service dispatch.
        /// </summary>
        /// <remarks>
        /// The custom-session checks detect known contract violations, not an invented generation gate.
        /// A host opting in must guarantee no unobservable or subsequent identity/model changes during this operation.
        /// </remarks>
        /// <exception cref="ServiceResultException"></exception>
        internal void VerifyBeforeDispatch(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (!ReferenceEquals(m_manager.GetSessionInfo(m_registration.Name), m_registration))
            {
                throw ChangedSession();
            }
            if (m_context != null)
            {
                if (m_context.Invalidated.IsCompleted)
                {
                    throw ChangedSession();
                }
                return;
            }

            ISession session = Access.Session;
            if (m_fixedContract == null ||
                m_endpoint == null ||
                !m_fixedContract.IsFixed(m_registration) ||
                !session.Connected ||
                session.SessionId != m_sessionId ||
                !ReferenceEquals(session.MessageContext, m_messageContext) ||
                !ReferenceEquals(session.NodeCache, m_nodeCache) ||
                !Matches(session.NamespaceUris, m_namespaces) ||
                !Matches(session.MessageContext.ServerUris, m_serverUris) ||
                !Matches(session.ConfiguredEndpoint.Description, m_endpoint))
            {
                throw ChangedSession();
            }
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            return m_context?.DisposeAsync() ?? default;
        }

        /// <summary>
        /// Detects namespace or server-index remapping without retaining a live table as the snapshot.
        /// </summary>
        private static bool Matches(StringTable current, ArrayOf<string> captured)
        {
            if (current.Count != captured.Count)
            {
                return false;
            }
            for (int index = 0; index < captured.Count; index++)
            {
                if (!string.Equals(current.GetString((uint)index), captured[index], StringComparison.Ordinal))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Detects endpoint or security metadata changes in a custom fixed-session contract.
        /// </summary>
        private static bool Matches(EndpointDescription current, EndpointDescription captured)
        {
            return current.SecurityMode == captured.SecurityMode &&
                string.Equals(current.EndpointUrl, captured.EndpointUrl, StringComparison.Ordinal) &&
                string.Equals(current.SecurityPolicyUri, captured.SecurityPolicyUri, StringComparison.Ordinal) &&
                string.Equals(current.TransportProfileUri, captured.TransportProfileUri, StringComparison.Ordinal) &&
                string.Equals(
                    current.Server?.ApplicationUri, captured.Server?.ApplicationUri, StringComparison.Ordinal);
        }

        /// <summary>
        /// Reports an invalidated preflight without leaking authentication or model payloads.
        /// </summary>
        private static ServiceResultException ChangedSession()
        {
            return new ServiceResultException(StatusCodes.BadSessionIdInvalid,
                "The classified session registration, generation or model metadata is no longer current.");
        }

        private readonly OpcUaSessionManager m_manager;
        private readonly OpcUaSessionManager.SessionInfo m_registration;
        private readonly SessionRequestContext? m_context;
        private readonly IMcpFixedSessionContract? m_fixedContract;
        private readonly NodeId m_sessionId;
        private readonly IServiceMessageContext? m_messageContext;
        private readonly ArrayOf<string> m_namespaces;
        private readonly ArrayOf<string> m_serverUris;
        private readonly INodeCache? m_nodeCache;
        private readonly EndpointDescription? m_endpoint;
    }
}
