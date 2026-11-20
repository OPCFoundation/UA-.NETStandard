// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.Server.AliasNames.PubSub
{
    /// <summary>
    /// Translates a local <see cref="NodeId"/> (whose
    /// <see cref="NodeId.NamespaceIndex"/> refers to the publisher's
    /// local namespace table) into a server-independent
    /// <see cref="PortableNodeId"/> (namespace URI + server URI +
    /// identifier) suitable for Part 17 Annex D PubSub messages.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The Part 17 <c>AliasCategoryUpdateDataType.Category</c> field is
    /// typed as <c>PortableNodeId</c> because cross-server PubSub
    /// subscribers may have completely different local namespace tables
    /// — they cannot meaningfully consume raw <c>NodeId</c> indices.
    /// </para>
    /// <para>
    /// Implementations typically read from
    /// <see cref="IServerInternal.NamespaceUris"/> and
    /// <see cref="IServerInternal.ServerUris"/>; the default
    /// <see cref="ServerPortableNodeIdResolver"/> does exactly that.
    /// </para>
    /// </remarks>
    public interface IPortableNodeIdResolver
    {
        /// <summary>
        /// Builds a <see cref="PortableNodeId"/> for the supplied
        /// <see cref="NodeId"/>. Returns <c>null</c> when the namespace
        /// index does not have a corresponding URI in the resolver's
        /// table.
        /// </summary>
        PortableNodeId? ToPortable(NodeId nodeId);
    }
}
