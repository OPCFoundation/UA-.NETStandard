/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;

namespace Opc.Ua.Server.AliasNames.PubSub
{
    /// <summary>
    /// Default <see cref="IPortableNodeIdResolver"/> backed by an
    /// <see cref="IServerInternal"/>'s
    /// <see cref="IServerInternal.NamespaceUris"/> and
    /// <see cref="IServerInternal.ServerUris"/> tables. The current
    /// server's URI (index 0 of <c>ServerUris</c>) is stamped into
    /// every <see cref="PortableNodeId"/>; subscribers use this to
    /// filter messages by originating publisher.
    /// </summary>
    public sealed class ServerPortableNodeIdResolver : IPortableNodeIdResolver
    {
        /// <summary>
        /// Initializes a new resolver bound to the supplied server.
        /// </summary>
        /// <param name="server">The server whose namespace + server URI
        /// tables the resolver should use.</param>
        public ServerPortableNodeIdResolver(IServerInternal server)
        {
            m_server = server ?? throw new ArgumentNullException(nameof(server));
        }

        /// <inheritdoc/>
        public PortableNodeId? ToPortable(NodeId nodeId)
        {
            if (nodeId.IsNull)
            {
                return null;
            }

            string? namespaceUri = m_server.NamespaceUris.GetString(nodeId.NamespaceIndex);
            if (namespaceUri == null)
            {
                return null;
            }

            // The PortableNodeId Identifier field carries the local
            // NodeId stripped of its namespace index — the URI conveys
            // the namespace explicitly so the index is meaningless to a
            // subscriber.
            NodeId stripped = NodeId.Null;
            if (nodeId.TryGetValue(out uint numeric))
            {
                stripped = new NodeId(numeric);
            }
            else if (nodeId.TryGetValue(out string strValue) && strValue != null)
            {
                stripped = NodeId.Parse("s=" + strValue);
            }
            else if (nodeId.TryGetValue(out Guid guidValue))
            {
                stripped = new NodeId(guidValue);
            }
            else if (nodeId.TryGetValue(out ByteString opaque) && !opaque.IsNull)
            {
                stripped = new NodeId(opaque);
            }

            return new PortableNodeId
            {
                NamespaceUri = namespaceUri,
                Identifier = stripped
            };
        }

        private readonly IServerInternal m_server;
    }
}
