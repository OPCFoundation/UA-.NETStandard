// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client.AliasNames.Refresh;

namespace Opc.Ua.Client.AliasNames.PubSub
{
    /// <summary>
    /// Bridges an <see cref="AliasNamePubSubReader"/> into the
    /// <see cref="IAliasNameRefreshStrategy"/> extension point. When a
    /// Part 17 Annex D <c>AliasUpdateDataType</c> message arrives for
    /// the resolver's category, the strategy invokes
    /// <c>onInvalidate</c> — so the resolver refetches via
    /// <c>FindAlias</c> on the next access.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The bridge matches by the source server's <c>NamespaceUri</c>
    /// (which the reader's
    /// <see cref="AliasNamePubSubReaderOptions.ExpectedApplicationUri"/>
    /// may already restrict). For each received message, every
    /// <see cref="AliasCategoryUpdateDataType"/> entry whose
    /// <see cref="PortableNodeId.NamespaceUri"/> matches the resolver's
    /// category namespace AND whose
    /// <see cref="PortableNodeId.Identifier"/> matches the resolver's
    /// category identifier triggers cache invalidation.
    /// </para>
    /// <para>
    /// LastChange comparison is wrap-safe (<c>VersionTime</c> is
    /// <c>uint</c>) — any difference fires; equal values are ignored.
    /// </para>
    /// </remarks>
    public sealed class AliasNamePubSubRefreshStrategy : IAliasNameRefreshStrategy
    {
        /// <summary>
        /// Initializes a new bridge over the supplied reader.
        /// </summary>
        /// <param name="reader">The reader that surfaces incoming
        /// Part 17 Annex D messages.</param>
        public AliasNamePubSubRefreshStrategy(AliasNamePubSubReader reader)
        {
            m_reader = reader ?? throw new ArgumentNullException(nameof(reader));
        }

        /// <inheritdoc/>
        public ValueTask StartAsync(
            AliasNameClient client,
            Action onInvalidate,
            CancellationToken ct)
        {
            if (client == null)
            {
                throw new ArgumentNullException(nameof(client));
            }

            m_categoryId = client.CategoryId;
            string? namespaceUri = client.Session.NamespaceUris
                .GetString(m_categoryId.NamespaceIndex);
            m_categoryNamespaceUri = namespaceUri ?? string.Empty;
            m_onInvalidate = onInvalidate ?? throw new ArgumentNullException(nameof(onInvalidate));
            m_reader.AliasUpdateReceived += OnAliasUpdateReceived;
            return default;
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            m_reader.AliasUpdateReceived -= OnAliasUpdateReceived;
            m_onInvalidate = null;
            return default;
        }

        private void OnAliasUpdateReceived(
            object? sender,
            AliasUpdateReceivedEventArgs e)
        {
            Action? invalidate = m_onInvalidate;
            if (invalidate == null)
            {
                return;
            }
            foreach (AliasCategoryUpdateDataType entry in e.Update.Categories)
            {
                if (!MatchesCategory(entry.Category))
                {
                    continue;
                }
                uint? last = m_lastSeen;
                uint current = entry.LastChange;
                if (last != current)
                {
                    m_lastSeen = current;
                    invalidate();
                    return;
                }
            }
        }

        private bool MatchesCategory(PortableNodeId portable)
        {
            if (portable == null)
            {
                return false;
            }
            if (!string.Equals(portable.NamespaceUri,
                    m_categoryNamespaceUri, StringComparison.Ordinal))
            {
                return false;
            }
            // Compare on identifier value only — the local namespace
            // index is irrelevant because the publisher stripped it.
            return CompareIdentifiers(portable.Identifier, m_categoryId);
        }

        private static bool CompareIdentifiers(NodeId publisherId, NodeId localId)
        {
            // PortableNodeId strips the namespace index but preserves
            // the underlying identifier type and value.
            return publisherId.WithNamespaceIndex(0) ==
                localId.WithNamespaceIndex(0);
        }

        private readonly AliasNamePubSubReader m_reader;
        private NodeId m_categoryId = NodeId.Null;
        private string m_categoryNamespaceUri = string.Empty;
        private Action? m_onInvalidate;
        private uint? m_lastSeen;
    }
}
