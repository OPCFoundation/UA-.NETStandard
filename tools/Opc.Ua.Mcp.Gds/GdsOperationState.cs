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
using System.Collections.Generic;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Bounded caller/session-generation ownership for token requests and push staging.
    /// It neither replaces the UA Session nor invents a remote transaction identifier.
    /// </summary>
    public sealed class GdsOperationState
    {
        /// <summary>
        /// Creates host-scoped bookkeeping using the same options and clock as the tools.
        /// </summary>
        public GdsOperationState(GdsMcpOptions options, TimeProvider? timeProvider = null)
        {
            ArgumentNullException.ThrowIfNull(options);
            options.Validate();
            m_limit = options.MaxTrackedOperations;
            m_requestLifetime = options.RequestLifetime;
            m_time = timeProvider ?? TimeProvider.System;
        }

        /// <summary>
        /// Reserves request capacity before a remote StartRequestToken can commit.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal RequestReservation ReserveRequest(GdsEndpoint endpoint, McpCallerIdentity owner, string resource)
        {
            GdsAccess.Text(resource);
            lock (m_state)
            {
                var expired = new List<RequestKey>();
                foreach (KeyValuePair<RequestKey, Request> pair in m_requests)
                {
                    if (pair.Value.ExpiresAt <= m_time.GetUtcNow() ||
                        !pair.Key.Session.IsConnected ||
                        (ReferenceEquals(pair.Key.Session, endpoint.Info) && pair.Key.SessionId != endpoint.SessionId))
                    {
                        expired.Add(pair.Key);
                    }
                }
                foreach (RequestKey key in expired)
                {
                    m_requests.Remove(key);
                }
                if (m_requests.Count + m_reservations >= m_limit)
                {
                    throw new ServiceResultException(StatusCodes.BadTooManyOperations);
                }
                m_reservations++;
                return new RequestReservation(this, endpoint, owner, resource);
            }
        }

        /// <summary>
        /// Requires the original caller, root and Session generation for a token finish.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal string RequireRequest(GdsEndpoint endpoint, McpCallerIdentity owner, string requestId)
        {
            lock (m_state)
            {
                var key = new RequestKey(endpoint.Info, endpoint.SessionId, endpoint.Proxy.ObjectId, requestId);
                if (!m_requests.TryGetValue(key, out Request? request) ||
                    request.Owner != owner ||
                    request.ExpiresAt <= m_time.GetUtcNow())
                {
                    throw new ServiceResultException(StatusCodes.BadUserAccessDenied,
                        "The request is unavailable to this caller, root and session generation.");
                }
                return request.Resource;
            }
        }

        /// <summary>
        /// Consumes a successfully completed request even if local secret persistence later fails.
        /// </summary>
        internal void CompleteRequest(GdsEndpoint endpoint, string requestId)
        {
            lock (m_state)
            {
                m_requests.Remove(new RequestKey(
                    endpoint.Info, endpoint.SessionId, endpoint.Proxy.ObjectId, requestId));
            }
        }

        /// <summary>
        /// Serializes push effects and prevents callers sharing a session from applying another caller's changes.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal PushOperation EnterPush(
            GdsEndpoint endpoint, McpCallerIdentity owner, bool requireExisting = false)
        {
            lock (m_state)
            {
                var key = new PushKey(endpoint.Info, endpoint.SessionId, endpoint.Proxy.ObjectId);
                bool existing = m_push.TryGetValue(key, out PushOwner? entry);
                if (!existing)
                {
                    if (requireExisting)
                    {
                        throw new ServiceResultException(StatusCodes.BadInvalidState,
                            "No MCP-owned push transaction exists for this session generation and root.");
                    }
                    var disconnected = new List<PushKey>();
                    foreach (PushKey candidate in m_push.Keys)
                    {
                        if (!candidate.Session.IsConnected ||
                            (ReferenceEquals(candidate.Session, endpoint.Info) &&
                                candidate.SessionId != endpoint.SessionId))
                        {
                            disconnected.Add(candidate);
                        }
                    }
                    foreach (PushKey candidate in disconnected)
                    {
                        m_push.Remove(candidate);
                    }
                    if (m_push.Count >= m_limit)
                    {
                        throw new ServiceResultException(StatusCodes.BadTooManyOperations);
                    }
                    entry = new PushOwner(owner);
                    m_push.Add(key, entry);
                }
                if (entry!.Owner != owner)
                {
                    throw new ServiceResultException(StatusCodes.BadUserAccessDenied,
                        "The pending push changes belong to another MCP caller.");
                }
                if (entry.Busy)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidState,
                        "Another push operation is still in flight for this root and session.");
                }
                entry.Busy = true;
                return new PushOperation(this, key, entry, existing);
            }
        }

        /// <summary>
        /// Converts admitted request capacity into a real server-assigned id.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        private void Commit(RequestReservation reservation, string requestId)
        {
            lock (m_state)
            {
                if (reservation.Released)
                {
                    throw new InvalidOperationException("The request reservation has already closed.");
                }
                m_requests.Add(new RequestKey(reservation.Endpoint.Info, reservation.Endpoint.SessionId,
                    reservation.Endpoint.Proxy.ObjectId, requestId),
                    new Request(reservation.Owner, reservation.Resource, m_time.GetUtcNow() + m_requestLifetime));
                m_reservations--;
                reservation.Released = true;
            }
        }

        /// <summary>
        /// A finite admission reservation; disposal performs no remote cancellation.
        /// </summary>
        internal sealed class RequestReservation(
            GdsOperationState state, GdsEndpoint endpoint, McpCallerIdentity owner, string resource) : IDisposable
        {
            /// <summary>
            /// Gets the captured endpoint.
            /// </summary>
            internal GdsEndpoint Endpoint { get; } = endpoint;

            /// <summary>
            /// Gets the authorized owner.
            /// </summary>
            internal McpCallerIdentity Owner { get; } = owner;

            /// <summary>
            /// Gets the original requested resource.
            /// </summary>
            internal string Resource { get; } = resource;

            /// <summary>
            /// Gets or sets the admitted slot's completion state.
            /// </summary>
            internal bool Released { get; set; }

            /// <summary>
            /// Retains the actual server-assigned request id.
            /// </summary>
            internal void Commit(string requestId)
            {
                state.Commit(this, requestId);
            }

            /// <inheritdoc/>
            public void Dispose()
            {
                lock (state.m_state)
                {
                    if (!Released)
                    {
                        state.m_reservations--;
                        Released = true;
                    }
                }
            }
        }

        /// <summary>
        /// A single admitted push effect. Unknown outcomes conservatively retain ownership for recovery.
        /// </summary>
        internal sealed class PushOperation(
            GdsOperationState state, PushKey key, PushOwner entry, bool existing) : IDisposable
        {
            /// <summary>
            /// Marks a known completed apply/cancel or a refused first staging operation.
            /// </summary>
            internal void Complete(bool release)
            {
                m_release = release;
            }

            /// <summary>
            /// Marks a known refusal without abandoning an older pending transaction.
            /// </summary>
            internal void Refused()
            {
                m_release = !existing;
            }

            /// <inheritdoc/>
            public void Dispose()
            {
                lock (state.m_state)
                {
                    entry.Busy = false;
                    if (m_release)
                    {
                        state.m_push.Remove(key);
                    }
                }
            }

            private bool m_release;
        }

        /// <summary>
        /// A request's real session generation, object and returned identifier.
        /// </summary>
        private sealed record RequestKey(
            OpcUaSessionManager.SessionInfo Session, NodeId SessionId, NodeId ObjectId, string Id);

        /// <summary>
        /// Non-sensitive pending token-request metadata.
        /// </summary>
        private sealed record Request(McpCallerIdentity Owner, string Resource, DateTimeOffset ExpiresAt);

        /// <summary>
        /// Server push transactions belong to one root on one Session generation.
        /// </summary>
        internal sealed record PushKey(OpcUaSessionManager.SessionInfo Session, NodeId SessionId, NodeId ObjectId);

        /// <summary>
        /// Cross-call ownership and single-operation admission without a public lock.
        /// </summary>
        internal sealed class PushOwner(McpCallerIdentity owner)
        {
            /// <summary>
            /// Gets the exact authorized caller.
            /// </summary>
            internal McpCallerIdentity Owner { get; } = owner;

            /// <summary>
            /// Gets or sets whether one explicit remote effect is in flight.
            /// </summary>
            internal bool Busy { get; set; }
        }

        private readonly int m_limit;
        private readonly TimeSpan m_requestLifetime;
        private readonly TimeProvider m_time;
        private readonly Dictionary<RequestKey, Request> m_requests = [];
        private readonly Dictionary<PushKey, PushOwner> m_push = [];
        private readonly System.Threading.Lock m_state = new();
        private int m_reservations;
    }
}
