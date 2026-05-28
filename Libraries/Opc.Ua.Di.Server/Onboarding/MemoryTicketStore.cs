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
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Di.Server.Onboarding
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
            if (encodedTicket == null) { throw new ArgumentNullException(nameof(encodedTicket)); }
            if (metadata == null) { throw new ArgumentNullException(nameof(metadata)); }

            var record = new TicketRecord(
                ticketId,
                (byte[])encodedTicket.Clone(),
                metadata,
                m_time.GetUtcNow());

            m_tickets[ticketId] = record;
            return default;
        }

        /// <inheritdoc/>
        public ValueTask<bool> RemoveAsync(
            string ticketId,
            CancellationToken cancellationToken = default)
        {
            if (ticketId == null) { throw new ArgumentNullException(nameof(ticketId)); }
            bool removed = m_tickets.TryRemove(ticketId, out _);
            return new ValueTask<bool>(removed);
        }

        /// <inheritdoc/>
        public ValueTask<TicketRecord?> GetAsync(
            string ticketId,
            CancellationToken cancellationToken = default)
        {
            if (ticketId == null) { throw new ArgumentNullException(nameof(ticketId)); }
            return new ValueTask<TicketRecord?>(
                m_tickets.TryGetValue(ticketId, out TicketRecord? r) ? r : null);
        }

        /// <inheritdoc/>
        public async IAsyncEnumerable<TicketRecord> ListAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            foreach (KeyValuePair<string, TicketRecord> kvp in m_tickets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return kvp.Value;
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

            foreach (KeyValuePair<string, TicketRecord> kvp in m_tickets)
            {
                if (string.Equals(
                    kvp.Value.Metadata.ProductInstanceUri,
                    productInstanceUri,
                    StringComparison.Ordinal))
                {
                    return new ValueTask<TicketRecord?>(kvp.Value);
                }
            }
            return new ValueTask<TicketRecord?>((TicketRecord?)null);
        }

        private readonly TimeProvider m_time;
        private readonly ConcurrentDictionary<string, TicketRecord> m_tickets = new();
    }
}
