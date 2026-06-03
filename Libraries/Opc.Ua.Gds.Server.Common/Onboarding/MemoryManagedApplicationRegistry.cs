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

namespace Opc.Ua.Gds.Server.Onboarding
{
    /// <summary>
    /// In-memory <see cref="IManagedApplicationRegistry"/> backed by
    /// a <see cref="ConcurrentDictionary{TKey,TValue}"/>. NodeIds are
    /// generated deterministically from the product-instance URI so
    /// re-registrations preserve the original NodeId.
    /// </summary>
    public sealed class MemoryManagedApplicationRegistry : IManagedApplicationRegistry
    {
        /// <summary>
        /// Creates a new registry. NodeIds are assigned in the
        /// supplied namespace index; defaults to <c>1</c> (one above
        /// the OPC UA built-in namespace).
        /// </summary>
        public MemoryManagedApplicationRegistry(ushort applicationNamespaceIndex = 1)
        {
            m_namespaceIndex = applicationNamespaceIndex;
        }

        /// <inheritdoc/>
        public ValueTask<NodeId> RegisterAsync(
            string productInstanceUri,
            byte[] certificate,
            TicketRecord ticket,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(productInstanceUri))
            {
                throw new ArgumentException(
                    "ProductInstanceUri must be non-empty.",
                    nameof(productInstanceUri));
            }
            if (certificate == null)
            {
                throw new ArgumentNullException(nameof(certificate));
            }
            if (ticket == null)
            {
                throw new ArgumentNullException(nameof(ticket));
            }
            ManagedApplication record = m_apps.AddOrUpdate(
                productInstanceUri,
                key => new ManagedApplication(
                    NodeId: new NodeId(key, m_namespaceIndex),
                    ProductInstanceUri: key,
                    Certificate: (byte[])certificate.Clone(),
                    Ticket: ticket),
                (key, existing) => existing with
                {
                    Certificate = (byte[])certificate.Clone(),
                    Ticket = ticket
                });

            return new ValueTask<NodeId>(record.NodeId);
        }

        /// <inheritdoc/>
        public ValueTask<bool> UnregisterAsync(
            NodeId applicationNodeId,
            CancellationToken cancellationToken = default)
        {
            if (applicationNodeId.IsNull)
            {
                throw new ArgumentNullException(nameof(applicationNodeId));
            }
            foreach (KeyValuePair<string, ManagedApplication> kvp in m_apps)
            {
                if (kvp.Value.NodeId == applicationNodeId)
                {
                    return new ValueTask<bool>(m_apps.TryRemove(kvp.Key, out _));
                }
            }
            return new ValueTask<bool>(false);
        }

        /// <inheritdoc/>
        public ValueTask<NodeId?> FindAsync(
            string productInstanceUri,
            CancellationToken cancellationToken = default)
        {
            if (productInstanceUri == null)
            {
                throw new ArgumentNullException(nameof(productInstanceUri));
            }
            return new ValueTask<NodeId?>(
                m_apps.TryGetValue(productInstanceUri, out ManagedApplication? app)
                    ? app.NodeId
                    : null);
        }

        /// <inheritdoc/>
        public async IAsyncEnumerable<ManagedApplication> ListAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            foreach (KeyValuePair<string, ManagedApplication> kvp in m_apps)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return kvp.Value;
            }
        }

        private readonly ushort m_namespaceIndex;
        private readonly ConcurrentDictionary<string, ManagedApplication> m_apps
            = new(StringComparer.Ordinal);
    }
}
