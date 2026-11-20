/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.Server.AliasNames
{
    /// <summary>
    /// A single entry to add to an alias-name category — corresponds to one
    /// row across the four parallel input arrays of the Part 17 §6.3.4
    /// <c>AddAliasesToCategory</c> method (<c>AliasNames[i]</c>,
    /// <c>TargetNodes[i]</c>, <c>TargetServers[i]</c>) plus the scalar
    /// <c>TargetReferenceType</c>.
    /// </summary>
    /// <param name="Name">The alias name to add.</param>
    /// <param name="TargetNode">The node the alias resolves to.</param>
    /// <param name="TargetServer">The server URI hosting the target; empty
    /// or <c>null</c> means the local server.</param>
    /// <param name="TargetReferenceType">The reference type between the
    /// alias and the target node; typically
    /// <c>ReferenceTypeIds.AliasFor</c>.</param>
    public sealed record AliasAddRequest(
        string Name,
        ExpandedNodeId TargetNode,
        string? TargetServer,
        NodeId TargetReferenceType);

    /// <summary>
    /// A single entry to remove from an alias-name category — corresponds
    /// to one row across the two parallel input arrays of the Part 17
    /// §6.3.5 <c>DeleteAliasesFromCategory</c> method
    /// (<c>AliasNames[i]</c>, <c>TargetNodes[i]</c>).
    /// </summary>
    /// <param name="Name">The alias name to remove.</param>
    /// <param name="TargetNode">The specific target node to detach. To
    /// remove every target of the named alias pass each target on its own
    /// row.</param>
    public sealed record AliasDeleteRequest(
        string Name,
        ExpandedNodeId TargetNode);
}
