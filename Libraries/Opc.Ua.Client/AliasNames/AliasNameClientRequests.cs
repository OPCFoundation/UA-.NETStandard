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
