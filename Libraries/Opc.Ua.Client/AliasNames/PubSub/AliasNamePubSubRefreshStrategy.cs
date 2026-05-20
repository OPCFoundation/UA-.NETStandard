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
            if (onInvalidate == null)
            {
                throw new ArgumentNullException(nameof(onInvalidate));
            }

            m_categoryId = client.CategoryId;
            string? namespaceUri = client.Session.NamespaceUris
                .GetString(m_categoryId.NamespaceIndex);
            m_categoryNamespaceUri = namespaceUri ?? string.Empty;
            m_onInvalidate = onInvalidate;
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
            // Both NodeIds carry the same identifier-shape (numeric /
            // string / guid / opaque). Comparing identifier text after
            // dropping the namespace prefix is the simplest robust path.
            string a = StripNamespacePrefix(publisherId.ToString());
            string b = StripNamespacePrefix(localId.ToString());
            return string.Equals(a, b, StringComparison.Ordinal);
        }

        private static string StripNamespacePrefix(string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }
            string value = text!;
            int idx = value.IndexOf(';', StringComparison.Ordinal);
            return idx < 0 ? value : value.Substring(idx + 1);
        }

        private readonly AliasNamePubSubReader m_reader;
        private NodeId m_categoryId = NodeId.Null;
        private string m_categoryNamespaceUri = string.Empty;
        private Action? m_onInvalidate;
        private uint? m_lastSeen;
    }
}
