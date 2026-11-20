// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
