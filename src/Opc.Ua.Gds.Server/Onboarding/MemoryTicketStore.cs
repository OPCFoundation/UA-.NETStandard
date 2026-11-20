/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Gds.Server.Onboarding
{
    /// <summary>
    /// In-memory <see cref="ITicketStore"/> implementation suitable
    /// for unit tests and small fixtures. Holds tickets in a
    /// <see cref="ConcurrentDictionary{TKey,TValue}"/> keyed by the
    /// application-assigned ticket id.
    /// </summary>
    public sealed class MemoryTicketStore : ITicketStore
    {
        /// <summary>
        /// Creates a new in-memory store.
        /// </summary>
        /// <param name="timeProvider">
        /// Optional time source for testability.
        /// </param>
        public MemoryTicketStore(TimeProvider? timeProvider = null)
        {
            m_time = timeProvider ?? TimeProvider.System;
        }

        /// <inheritdoc/>
        public ValueTask AddAsync(
            string ticketId,
            byte[] encodedTicket,
            TicketMetadata metadata,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(ticketId))
            {
                throw new ArgumentException(
                    "Ticket id must be non-empty.", nameof(ticketId));
            }
            if (encodedTicket == null)
            {
                throw new ArgumentNullException(nameof(encodedTicket));
            }
            if (metadata == null)
            {
                throw new ArgumentNullException(nameof(metadata));
            }
            cancellationToken.ThrowIfCancellationRequested();
            m_tickets[ticketId] = new TicketRecord(
                ticketId,
                (byte[])encodedTicket.Clone(),
                metadata,
                m_time.GetUtcNow());
            return default;
        }

        /// <inheritdoc/>
        public ValueTask<bool> RemoveAsync(
            string ticketId,
            CancellationToken cancellationToken = default)
        {
            if (ticketId == null)
            {
                throw new ArgumentNullException(nameof(ticketId));
            }
            cancellationToken.ThrowIfCancellationRequested();
            bool removed = m_tickets.TryRemove(ticketId, out _);
            return new ValueTask<bool>(removed);
        }

        /// <inheritdoc/>
        public ValueTask<TicketRecord?> GetAsync(
            string ticketId,
            CancellationToken cancellationToken = default)
        {
            if (ticketId == null)
            {
                throw new ArgumentNullException(nameof(ticketId));
            }
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<TicketRecord?>(
                m_tickets.TryGetValue(ticketId, out TicketRecord? r) ? Copy(r) : null);
        }

        /// <inheritdoc/>
        public async IAsyncEnumerable<TicketRecord> ListAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            foreach (KeyValuePair<string, TicketRecord> kvp in m_tickets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return Copy(kvp.Value);
            }
        }

        /// <inheritdoc/>
        public ValueTask<TicketRecord?> FindByProductInstanceUriAsync(
            string productInstanceUri,
            CancellationToken cancellationToken = default)
        {
            if (productInstanceUri == null)
            {
                throw new ArgumentNullException(nameof(productInstanceUri));
            }
            cancellationToken.ThrowIfCancellationRequested();
            foreach (KeyValuePair<string, TicketRecord> kvp in m_tickets)
            {
                if (string.Equals(
                    kvp.Value.Metadata.ProductInstanceUri,
                    productInstanceUri,
                    StringComparison.Ordinal))
                {
                    return new ValueTask<TicketRecord?>(Copy(kvp.Value));
                }
            }
            return new ValueTask<TicketRecord?>((TicketRecord?)null);
        }

        /// <summary>
        /// Keeps the store's accepted payload independent of caller-owned buffers.
        /// </summary>
        private static TicketRecord Copy(TicketRecord record)
        {
            return record with { EncodedTicket = (byte[])record.EncodedTicket.Clone() };
        }

        private readonly TimeProvider m_time;
        private readonly ConcurrentDictionary<string, TicketRecord> m_tickets = new();
    }
}
