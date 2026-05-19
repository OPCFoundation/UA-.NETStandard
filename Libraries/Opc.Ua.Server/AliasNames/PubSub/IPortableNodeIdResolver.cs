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
