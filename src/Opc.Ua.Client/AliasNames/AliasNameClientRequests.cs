// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.Client.AliasNames
{
    /// <summary>
    /// Client-side request record for <c>AddAliasesToCategory</c> (Part 17
    /// §6.3.4) — one row across the four parallel input arrays
    /// (<c>AliasNames[i]</c>, <c>TargetNodes[i]</c>,
    /// <c>TargetServers[i]</c>) plus the scalar
    /// <c>TargetReferenceType</c>. Use with
    /// <see cref="AliasNameClient.AddAliasesToCategoryAsync"/>.
    /// </summary>
    /// <param name="Name">The alias name to add.</param>
    /// <param name="TargetNode">The node the alias resolves to.</param>
    /// <param name="TargetServer">The server URI hosting the target;
    /// empty or <c>null</c> means the local server.</param>
    /// <param name="TargetReferenceType">The reference type between the
    /// alias and the target node; typically
    /// <c>ReferenceTypeIds.AliasFor</c>.</param>
    public sealed record AliasNameAddRequest(
        string Name,
        ExpandedNodeId TargetNode,
        string? TargetServer,
        NodeId TargetReferenceType);

    /// <summary>
    /// Client-side request record for <c>DeleteAliasesFromCategory</c>
    /// (Part 17 §6.3.5) — one row across the two parallel input arrays
    /// (<c>AliasNames[i]</c>, <c>TargetNodes[i]</c>). Use with
    /// <see cref="AliasNameClient.DeleteAliasesFromCategoryAsync"/>.
    /// </summary>
    /// <param name="Name">The alias name to remove.</param>
    /// <param name="TargetNode">The specific target node to detach.</param>
    public sealed record AliasNameDeleteRequest(
        string Name,
        ExpandedNodeId TargetNode);

    /// <summary>
    /// Describes a sub-category discovered under a parent
    /// <c>AliasNameCategoryType</c> instance.
    /// </summary>
    /// <param name="NodeId">The sub-category's NodeId.</param>
    /// <param name="BrowseName">The sub-category's BrowseName.</param>
    /// <param name="DisplayName">The sub-category's DisplayName (may be
    /// <c>null</c>).</param>
    public sealed record AliasNameSubCategoryInfo(
        NodeId NodeId,
        QualifiedName BrowseName,
        LocalizedText? DisplayName);
}
