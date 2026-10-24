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
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace Opc.Ua.DataChannels
{
    /// <summary>
    /// Bounded, single-use monotonic grant storage for an injected Session authorizer.
    /// Session permission checks and staged-update authorization remain with that provider.
    /// </summary>
    [global::System.Diagnostics.CodeAnalysis.Experimental(DataChannelFeature.ExperimentalDiagnosticId,
        UrlFormat = DataChannelFeature.ExperimentalUrlFormat)]
    public sealed class DataChannelGrantStore : IDisposable
    {
        /// <summary>
        /// Creates an application-scoped grant store.
        /// </summary>
        public DataChannelGrantStore(
            DataChannelApplicationBudget budget,
            TimeProvider timeProvider,
            DataChannelOptions? options = null)
        {
            m_budget = budget ?? throw new ArgumentNullException(nameof(budget));
            m_clock = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
            DataChannelOptions settings = options ?? new DataChannelOptions();
            settings.Validate();
            m_lifetime = settings.GrantLifetime;
            m_timer = m_clock.CreateTimer(
                _ => Expire(),
                null,
                m_lifetime,
                m_lifetime);
        }

        /// <summary>
        /// Issues a context/key/challenge-bound grant after the caller's Session policy check.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="context"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        /// <exception cref="DataChannelException"></exception>
        public DataChannelGrantDataType Issue(
            DataChannelContextIdentity context,
            uint channelId,
            DataChannelParametersDataType parameters,
            ExpandedNodeId sourceNodeId,
            NodeId sessionId,
            TimeSpan? lifetime = null)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (channelId == 0 || sessionId.IsNull)
            {
                throw new ArgumentException("A flow and authorizing Session are required.");
            }
            DataChannelParametersDataType snapshot = FlowModel.Copy(parameters);
            TimeSpan admittedLifetime = lifetime ?? m_lifetime;
            if (admittedLifetime <= TimeSpan.Zero || admittedLifetime > m_lifetime)
            {
                throw new ArgumentOutOfRangeException(nameof(lifetime));
            }
            lock (m_gate)
            {
                ThrowIfDisposed();
                ExpireCore();
                if (!m_budget.TryReserve(FlowResource.Grant, 1))
                {
                    throw new DataChannelException(DataChannelResult.WouldBlock);
                }
                var id = Guid.NewGuid();
                var entry = new GrantEntry(
                    context,
                    channelId,
                    snapshot,
                    sourceNodeId,
                    sessionId,
                    m_clock.GetTimestamp(),
                    admittedLifetime,
                    m_clock.GetUtcNow().UtcDateTime + admittedLifetime);
                m_grants.Add(id, entry);
                ArmTimer();
                return new DataChannelGrantDataType
                {
                    GrantId = id,
                    ChannelId = channelId,
                    ContextId = context.ContextId,
                    Generation = context.Generation,
                    SourceNodeId = sourceNodeId,
                    SessionId = sessionId,
                    Parameters = FlowModel.Copy(snapshot),
                    ExpiresAt = entry.ExpiresAt
                };
            }
        }

        /// <summary>
        /// Looks up a live grant without redeeming it, so the Session owner can check current
        /// permissions first. The returned generated value is a defensive snapshot.
        /// </summary>
        public bool TryGetGrant(Guid grantId, [NotNullWhen(true)] out DataChannelGrantDataType? grant)
        {
            lock (m_gate)
            {
                ThrowIfDisposed();
                ExpireCore();
                if (!m_grants.TryGetValue(grantId, out GrantEntry? entry))
                {
                    grant = null;
                    return false;
                }
                grant = new DataChannelGrantDataType
                {
                    GrantId = grantId,
                    ChannelId = entry.ChannelId,
                    ContextId = entry.Context.ContextId,
                    Generation = entry.Context.Generation,
                    SourceNodeId = entry.SourceNodeId,
                    SessionId = entry.SessionId,
                    Parameters = FlowModel.Copy(entry.Parameters),
                    ExpiresAt = entry.ExpiresAt
                };
                return true;
            }
        }

        /// <summary>
        /// Redeems once after matching every binding field and the exact granted descriptor.
        /// Invalid attempts do not consume another Session's grant.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="request"/> is <c>null</c>.</exception>
        public DataChannelAuthorization Redeem(DataChannelAuthorizationRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            lock (m_gate)
            {
                ThrowIfDisposed();
                ExpireCore();
                if (request.Mode != DataChannelAuthorizationMode.Session ||
                    request.Purpose != DataChannelAuthorizationPurpose.Open ||
                    !request.GrantId.HasValue ||
                    !m_grants.TryGetValue(request.GrantId.Value, out GrantEntry? entry) ||
                    entry.ChannelId != request.ChannelId ||
                    entry.Context.ContextId != request.Context.ContextId ||
                    entry.Context.Generation != request.Context.Generation ||
                    entry.Context.Carrier != request.Context.Carrier ||
                    entry.Context.Peer.LocalApplicationUri != request.Context.Peer.LocalApplicationUri ||
                    entry.Context.Peer.PeerApplicationUri != request.Context.Peer.PeerApplicationUri ||
                    entry.Context.Peer.LocalKeyThumbprint != request.Context.Peer.LocalKeyThumbprint ||
                    entry.Context.Peer.PeerKeyThumbprint != request.Context.Peer.PeerKeyThumbprint ||
                    entry.Context.Peer.Protection != request.Context.Peer.Protection ||
                    entry.Context.LocalChallenge != request.Context.LocalChallenge ||
                    entry.Context.PeerChallenge != request.Context.PeerChallenge ||
                    !FlowModel.SameParameters(entry.Parameters, request.Parameters))
                {
                    return new DataChannelAuthorization(DataChannelResult.NotAuthorized);
                }
                m_grants.Remove(request.GrantId.Value);
                m_budget.Release(FlowResource.Grant, 1);
                return new DataChannelAuthorization(
                    DataChannelResult.Good,
                    entry.Parameters,
                    entry.SourceNodeId,
                    entry.SessionId);
            }
        }

        /// <summary>
        /// Invalidates unredeemed grants of a closed or revoked Session.
        /// Active flow authorization is rechecked through the context separately.
        /// </summary>
        public void RevokeSession(NodeId sessionId)
        {
            lock (m_gate)
            {
                foreach (Guid id in Find(entry => entry.SessionId == sessionId))
                {
                    m_grants.Remove(id);
                    m_budget.Release(FlowResource.Grant, 1);
                }
            }
        }

        /// <summary>
        /// Revokes one unredeemed grant without affecting another flow or authorizing Session.
        /// </summary>
        public bool RevokeGrant(Guid grantId)
        {
            lock (m_gate)
            {
                if (!m_grants.Remove(grantId))
                {
                    return false;
                }
                m_budget.Release(FlowResource.Grant, 1);
                return true;
            }
        }

        /// <summary>
        /// Invalidates grants when a data security context closes or changes.
        /// </summary>
        public void RevokeContext(Guid contextId)
        {
            lock (m_gate)
            {
                foreach (Guid id in Find(entry => entry.Context.ContextId == contextId))
                {
                    m_grants.Remove(id);
                    m_budget.Release(FlowResource.Grant, 1);
                }
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            m_timer.Dispose();
            lock (m_gate)
            {
                if (m_disposed)
                {
                    return;
                }
                m_disposed = true;
                m_budget.Release(FlowResource.Grant, m_grants.Count);
                m_grants.Clear();
            }
        }

        /// <summary>
        /// Expires unredeemed grants under the injected monotonic clock.
        /// </summary>
        private void Expire()
        {
            lock (m_gate)
            {
                if (!m_disposed)
                {
                    ExpireCore();
                    ArmTimer();
                }
            }
        }

        /// <summary>
        /// Rejects use after store shutdown on every supported target framework.
        /// </summary>
        /// <exception cref="ObjectDisposedException"></exception>
        private void ThrowIfDisposed()
        {
            if (m_disposed)
            {
                throw new ObjectDisposedException(nameof(DataChannelGrantStore));
            }
        }

        /// <summary>
        /// Removes grants at or after their exact monotonic deadline.
        /// </summary>
        private void ExpireCore()
        {
            foreach (Guid id in Find(entry => m_clock.GetElapsedTime(entry.Created) >= entry.Lifetime))
            {
                m_grants.Remove(id);
                m_budget.Release(FlowResource.Grant, 1);
            }
        }

        /// <summary>
        /// Arms the exact earliest grant deadline, including a shorter Session/source lifetime.
        /// </summary>
        private void ArmTimer()
        {
            TimeSpan due = m_lifetime;
            foreach (GrantEntry entry in m_grants.Values)
            {
                TimeSpan remaining = entry.Lifetime - m_clock.GetElapsedTime(entry.Created);
                if (remaining < due)
                {
                    due = remaining;
                }
            }
            m_timer.Change(due < TimeSpan.Zero ? TimeSpan.Zero : due, Timeout.InfiniteTimeSpan);
        }

        /// <summary>
        /// Captures bounded removal keys before changing the store.
        /// </summary>
        private List<Guid> Find(Func<GrantEntry, bool> predicate)
        {
            List<Guid> keys = [];
            foreach (KeyValuePair<Guid, GrantEntry> item in m_grants)
            {
                if (predicate(item.Value))
                {
                    keys.Add(item.Key);
                }
            }
            return keys;
        }

        /// <summary>
        /// Immutable grant material retained only until redemption or expiration.
        /// </summary>
        private sealed record GrantEntry(
            DataChannelContextIdentity Context,
            uint ChannelId,
            DataChannelParametersDataType Parameters,
            ExpandedNodeId SourceNodeId,
            NodeId SessionId,
            long Created,
            TimeSpan Lifetime,
            DateTime ExpiresAt);

        private readonly Lock m_gate = new();
        private readonly DataChannelApplicationBudget m_budget;
        private readonly TimeProvider m_clock;
        private readonly TimeSpan m_lifetime;
        private readonly ITimer m_timer;
        private readonly Dictionary<Guid, GrantEntry> m_grants = [];
        private bool m_disposed;
    }
}
