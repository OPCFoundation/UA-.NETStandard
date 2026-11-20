/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using Opc.Ua.Machinery.Server;

namespace Opc.Ua.Recycling
{
    /// <summary>
    /// Stable instance identities from the qualified containment path, not namespace-table positions,
    /// creation order, a process id, a random run id or a projection clock.
    /// </summary>
    internal sealed class RecyclingInstanceNodeIds(ushort namespaceIndex) : IMachineryNodeIdFactory
    {
        /// <inheritdoc/>
        public NodeId New(ISystemContext context, NodeState node)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(node);
            if (node.NodeId.NamespaceIndex == namespaceIndex && node.NodeId.TryGetValue(out string _))
            {
                return node.NodeId;
            }
            string parent = node is BaseInstanceState { Parent: { } owner } &&
                owner.NodeId.NamespaceIndex == namespaceIndex &&
                owner.NodeId.TryGetValue(out string identifier)
                ? identifier
                : "assets";
            string uri = context.NamespaceUris.GetString(node.BrowseName.NamespaceIndex) ?? string.Empty;
            return new NodeId(
                parent + "/" + Uri.EscapeDataString(uri) + ":" +
                    Uri.EscapeDataString(node.BrowseName.Name ?? string.Empty),
                namespaceIndex);
        }
    }
}
