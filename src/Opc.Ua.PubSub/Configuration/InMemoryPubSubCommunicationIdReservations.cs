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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.PubSub.Configuration
{
    /// <summary>
    /// Process-local Part 14 reservation store. Supply a shared implementation for HA deployments.
    /// A default PublisherId policy is deliberately required to enable reservation of a transport scope.
    /// </summary>
    public sealed class InMemoryPubSubCommunicationIdReservations :
        IPubSubCommunicationIdReservations, IPubSubCommunicationIdAllocation, IDisposable
    {
        /// <summary>
        /// Creates a reservation store using an explicit server PublisherId policy.
        /// Without a policy, reservation requests fail with BadNotSupported; mutation protection remains active.
        /// </summary>
        public InMemoryPubSubCommunicationIdReservations(IPubSubDefaultPublisherIdPolicy? policy = null)
        {
            m_policy = policy;
        }

        /// <inheritdoc/>
        public async ValueTask<PubSubCommunicationIdReservation> ReserveAsync(
            NodeId sessionId,
            string transportProfileUri,
            ushort writerGroupCount,
            ushort dataSetWriterCount,
            PubSubConfigurationDataType configuration,
            CancellationToken cancellationToken = default)
        {
            if (configuration is null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }
            await m_operations.WaitAsync(cancellationToken).ConfigureAwait(false);
            BeginOperation();
            try
            {
                CheckSession(sessionId);
                if (m_policy is null || string.IsNullOrEmpty(transportProfileUri))
                {
                    throw new ServiceResultException(StatusCodes.BadNotSupported);
                }
                Variant publisherId = await m_policy.ResolveAsync(transportProfileUri, cancellationToken)
                    .ConfigureAwait(false);
                if (!IsValidPublisherId(publisherId))
                {
                    throw new ServiceResultException(StatusCodes.BadConfigurationError,
                        "The default PublisherId must be a nonzero unsigned integer or nonempty string.");
                }
                HashSet<CommunicationId> unavailable = GetIds(configuration);
                lock (m_gate)
                {
                    unavailable.UnionWith(m_configured);
                    unavailable.UnionWith(m_reservations.Keys);
                    unavailable.UnionWith(m_provisional.Keys);
                }
                ArrayOf<ushort> groups = SelectIds(transportProfileUri, publisherId, false,
                    writerGroupCount, unavailable);
                ArrayOf<ushort> writers = SelectIds(transportProfileUri, publisherId, true,
                    dataSetWriterCount, unavailable);
                cancellationToken.ThrowIfCancellationRequested();
                lock (m_gate)
                {
                    CheckSession(sessionId);
                    foreach (ushort id in groups)
                    {
                        m_reservations.Add(new CommunicationId(transportProfileUri, publisherId, false, id), sessionId);
                    }
                    foreach (ushort id in writers)
                    {
                        m_reservations.Add(new CommunicationId(transportProfileUri, publisherId, true, id), sessionId);
                    }
                }
                return new PubSubCommunicationIdReservation(publisherId, groups, writers);
            }
            finally
            {
                EndOperation();
                m_operations.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask PersistAsync(
            PubSubConfigurationDataType configuration,
            Func<CancellationToken, ValueTask> persist,
            CancellationToken cancellationToken = default)
        {
            if (configuration is null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }
            if (persist is null)
            {
                throw new ArgumentNullException(nameof(persist));
            }
            await m_operations.WaitAsync(cancellationToken).ConfigureAwait(false);
            BeginOperation();
            try
            {
                NodeId sessionId = PubSubConfigurationSession.Current;
                HashSet<CommunicationId> next = GetIds(configuration);
                lock (m_gate)
                {
                    if (!sessionId.IsNull)
                    {
                        CheckSession(sessionId);
                    }
                    foreach (CommunicationId id in next)
                    {
                        if (m_reservations.TryGetValue(id, out NodeId owner) &&
                            (sessionId.IsNull || owner != sessionId))
                        {
                            throw new ServiceResultException(StatusCodes.BadUserAccessDenied,
                                "Only the reserving Session may consume a communication ID.");
                        }
                        if (m_provisional.TryGetValue(id, out NodeId provisionalOwner) &&
                            provisionalOwner != sessionId)
                        {
                            throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
                        }
                    }
                }
                await persist(cancellationToken).ConfigureAwait(false);
                lock (m_gate)
                {
                    // Persistence is the commit point, even if runtime rebuild or version persistence later fails.
                    m_configured = next;
                    foreach (CommunicationId id in next)
                    {
                        m_reservations.Remove(id);
                        m_provisional.Remove(id);
                    }
                }
            }
            finally
            {
                EndOperation();
                m_operations.Release();
            }
        }

        /// <inheritdoc/>
        public void CloseSession(NodeId sessionId)
        {
            if (sessionId.IsNull)
            {
                return;
            }
            lock (m_gate)
            {
                m_closed.Add(sessionId);
                if (!m_busy)
                {
                    ReleaseClosedSessions();
                }
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            m_operations.Dispose();
        }

        /// <inheritdoc/>
        public async ValueTask<Variant> ResolvePublisherIdAsync(
            string transportProfileUri, CancellationToken cancellationToken)
        {
            if (m_policy is null || string.IsNullOrEmpty(transportProfileUri))
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported);
            }
            Variant result = await m_policy.ResolveAsync(transportProfileUri, cancellationToken).ConfigureAwait(false);
            if (!IsValidPublisherId(result))
            {
                throw new ServiceResultException(StatusCodes.BadConfigurationError);
            }
            return result;
        }

        /// <inheritdoc/>
        public async ValueTask<IPubSubCommunicationIdAllocationLease> AllocateAsync(
            string transportProfileUri,
            Variant publisherId,
            bool dataSetWriter,
            PubSubConfigurationDataType configuration,
            CancellationToken cancellationToken)
        {
            if (configuration is null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }
            await m_operations.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                NodeId session = PubSubConfigurationSession.Current;
                if (!session.IsNull)
                {
                    CheckSession(session);
                }
                HashSet<CommunicationId> unavailable = GetIds(configuration);
                lock (m_gate)
                {
                    unavailable.UnionWith(m_configured);
                    unavailable.UnionWith(m_reservations.Keys);
                    unavailable.UnionWith(m_provisional.Keys);
                    ushort id = SelectIds(transportProfileUri, publisherId, dataSetWriter, 1, unavailable)[0];
                    var key = new CommunicationId(transportProfileUri, publisherId, dataSetWriter, id);
                    m_provisional.Add(key, session);
                    return new AllocationLease(this, key);
                }
            }
            finally
            {
                m_operations.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask ValidateOwnershipAsync(
            PubSubConfigurationDataType configuration, CancellationToken cancellationToken)
        {
            await m_operations.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                NodeId session = PubSubConfigurationSession.Current;
                if (!session.IsNull)
                {
                    CheckSession(session);
                }
                lock (m_gate)
                {
                    foreach (CommunicationId id in GetIds(configuration))
                    {
                        if (m_reservations.TryGetValue(id, out NodeId owner) &&
                            (session.IsNull || owner != session))
                        {
                            throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
                        }
                    }
                }
            }
            finally
            {
                m_operations.Release();
            }
        }

        /// <summary>
        /// Releases a provisional identifier when its import exits, including failed atomic imports.
        /// </summary>
        private sealed class AllocationLease(
            InMemoryPubSubCommunicationIdReservations owner, CommunicationId key)
            : IPubSubCommunicationIdAllocationLease
        {
            /// <inheritdoc/>
            public ushort Id => key.Id;

            /// <inheritdoc/>
            public void Dispose()
            {
                lock (owner.m_gate)
                {
                    owner.m_provisional.Remove(key);
                }
            }
        }

        /// <summary>
        /// Validates the Part 14 PublisherId value without numeric coercion.
        /// </summary>
        internal static bool IsValidPublisherId(Variant value)
        {
            return (value.TryGetValue(out byte b) && b != 0) ||
                (value.TryGetValue(out ushort u16) && u16 != 0) ||
                (value.TryGetValue(out uint u32) && u32 != 0) ||
                (value.TryGetValue(out ulong u64) && u64 != 0) ||
                (value.TryGetValue(out string? text) && !string.IsNullOrEmpty(text));
        }

        /// <summary>
        /// Selects a whole category without mutating the reservation state.
        /// </summary>
        private static ArrayOf<ushort> SelectIds(
            string scope,
            Variant publisher,
            bool writer,
            ushort count,
            HashSet<CommunicationId> unavailable)
        {
            var selected = new List<ushort>(count);
            for (int value = 0x8000; value <= ushort.MaxValue && selected.Count < count; value++)
            {
                ushort id = (ushort)value;
                if (!unavailable.Contains(new CommunicationId(scope, publisher, writer, id)))
                {
                    selected.Add(id);
                }
            }
            if (selected.Count != count)
            {
                throw new ServiceResultException(StatusCodes.BadResourceUnavailable);
            }
            return new ArrayOf<ushort>(selected.ToArray());
        }

        /// <summary>
        /// Includes disabled components and preserves the exact typed PublisherId.
        /// </summary>
        private static HashSet<CommunicationId> GetIds(PubSubConfigurationDataType configuration)
        {
            var ids = new HashSet<CommunicationId>();
            foreach (PubSubConnectionDataType connection in configuration.Connections)
            {
                foreach (WriterGroupDataType group in connection.WriterGroups)
                {
                    ids.Add(new CommunicationId(
                        connection.TransportProfileUri ?? string.Empty, connection.PublisherId,
                        false, group.WriterGroupId));
                    foreach (DataSetWriterDataType writer in group.DataSetWriters)
                    {
                        ids.Add(new CommunicationId(connection.TransportProfileUri ?? string.Empty, connection.PublisherId,
                            true, writer.DataSetWriterId));
                    }
                }
            }
            return ids;
        }

        /// <summary>
        /// Rejects missing and closed Session identifiers without inventing a fallback identity.
        /// </summary>
        private void CheckSession(NodeId sessionId)
        {
            lock (m_gate)
            {
                if (sessionId.IsNull || m_closed.Contains(sessionId))
                {
                    throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
                }
            }
        }

        /// <summary>
        /// Defers close-event cleanup until the serialized operation has finished.
        /// </summary>
        private void BeginOperation()
        {
            lock (m_gate)
            {
                m_busy = true;
            }
        }

        /// <summary>
        /// Releases unused IDs belonging to closed Sessions after any pending persistence finishes.
        /// </summary>
        private void EndOperation()
        {
            lock (m_gate)
            {
                ReleaseClosedSessions();
                m_busy = false;
            }
        }

        /// <summary>
        /// Removes only outstanding reservations; configured IDs remain unavailable.
        /// </summary>
        private void ReleaseClosedSessions()
        {
            var released = new List<CommunicationId>();
            foreach (KeyValuePair<CommunicationId, NodeId> entry in m_reservations)
            {
                if (m_closed.Contains(entry.Value))
                {
                    released.Add(entry.Key);
                }
            }
            foreach (CommunicationId id in released)
            {
                m_reservations.Remove(id);
            }
        }

        /// <summary>
        /// Exact transport, typed publisher and communication-ID category key.
        /// </summary>
        private readonly record struct CommunicationId(string Scope, Variant Publisher, bool Writer, ushort Id);

        /// <summary>Explicit host default PublisherId policy.</summary>
        private readonly IPubSubDefaultPublisherIdPolicy? m_policy;

        /// <summary>Serializes asynchronous provider operations.</summary>
        private readonly SemaphoreSlim m_operations = new(1, 1);

        /// <summary>Protects close-event coordination and state transitions.</summary>
        private readonly Lock m_gate = new();

        /// <summary>Outstanding reservations keyed by their complete identifier domain.</summary>
        private readonly Dictionary<CommunicationId, NodeId> m_reservations = [];

        /// <summary>Provisional automatic identifiers released at the end of their owning import.</summary>
        private readonly Dictionary<CommunicationId, NodeId> m_provisional = [];

        /// <summary>Closed Session tombstones retained for the provider lifetime.</summary>
        private readonly HashSet<NodeId> m_closed = [];

        /// <summary>Last successfully persisted identifiers, including pre-runtime-rebuild commits.</summary>
        private HashSet<CommunicationId> m_configured = [];

        /// <summary>Whether an operation may still persist outstanding reservations.</summary>
        private bool m_busy;
    }
}
