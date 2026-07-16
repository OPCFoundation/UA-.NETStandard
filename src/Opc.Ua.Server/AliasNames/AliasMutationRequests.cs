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
