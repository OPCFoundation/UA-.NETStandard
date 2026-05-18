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
using System.Collections.Generic;

namespace Opc.Ua.Server.AliasNames
{
    /// <summary>
    /// Configuration describing a single OPC UA Part 17 alias. An alias maps
    /// a human-readable name (within its containing
    /// <see cref="AliasNameCategoryDescriptor"/>) to one or more target nodes
    /// reached via a specific reference type.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The wire-level representation is <c>AliasNameDataType</c> (Part 17
    /// §7.2) — a <see cref="QualifiedName"/> and a list of
    /// <see cref="ExpandedNodeId"/> targets. The <see cref="ReferenceTypeId"/>
    /// is the reference type used between the <c>AliasName</c> object and
    /// each target node in the server's address space; it also drives the
    /// <c>ReferenceTypeFilter</c> argument of the <c>FindAlias</c> methods
    /// (Part 17 §6.3.2).
    /// </para>
    /// <para>
    /// <see cref="ServerUris"/> is the optional list of server URIs used by
    /// the verbose form <c>AliasNameVerboseDataType</c> (Part 17 §7.3). When
    /// present, <c>ServerUris[i]</c> identifies the server hosting
    /// <c>ReferencedNodes[i]</c>; an empty entry, or a list shorter than
    /// <c>ReferencedNodes</c>, means "the current server".
    /// </para>
    /// </remarks>
    public sealed record AliasDefinition
    {
        /// <summary>
        /// Initializes a new alias definition.
        /// </summary>
        /// <param name="name">The alias name as a <see cref="QualifiedName"/>;
        /// the <c>NamespaceIndex</c> should belong to the node manager's
        /// namespace.</param>
        /// <param name="referencedNodes">The (one or more)
        /// <see cref="ExpandedNodeId"/>s the alias resolves to.</param>
        /// <param name="referenceTypeId">The reference type that connects the
        /// alias node to each referenced node; typically
        /// <c>ReferenceTypeIds.AliasFor</c> but may be any non-hierarchical
        /// reference (per Part 17 §8.2).</param>
        /// <param name="serverUris">Optional per-target server URIs for the
        /// verbose form. <c>null</c> means "all targets live on the current
        /// server".</param>
        public AliasDefinition(
            QualifiedName name,
            IReadOnlyList<ExpandedNodeId> referencedNodes,
            NodeId referenceTypeId,
            IReadOnlyList<string?>? serverUris = null)
        {
            if (referencedNodes == null)
            {
                throw new ArgumentNullException(nameof(referencedNodes));
            }
            if (name.IsNull)
            {
                throw new ArgumentException(
                    "AliasName must not be null.",
                    nameof(name));
            }
            if (referencedNodes.Count == 0)
            {
                throw new ArgumentException(
                    "At least one referenced node is required.",
                    nameof(referencedNodes));
            }
            if (referenceTypeId.IsNull)
            {
                throw new ArgumentException(
                    "ReferenceTypeId must not be null.",
                    nameof(referenceTypeId));
            }
            if (serverUris != null && serverUris.Count > referencedNodes.Count)
            {
                throw new ArgumentException(
                    "ServerUris must not be longer than ReferencedNodes.",
                    nameof(serverUris));
            }

            Name = name;
            ReferencedNodes = referencedNodes;
            ReferenceTypeId = referenceTypeId;
            ServerUris = serverUris;
        }

        /// <summary>
        /// The alias name. Equality across aliases inside a category is
        /// determined by the case-sensitive name (per Part 17 wildcard
        /// semantics) and namespace index.
        /// </summary>
        public QualifiedName Name { get; }

        /// <summary>
        /// One or more target nodes the alias resolves to.
        /// </summary>
        public IReadOnlyList<ExpandedNodeId> ReferencedNodes { get; }

        /// <summary>
        /// The reference type connecting the alias to each target. Used both
        /// to construct the address-space references and to filter result
        /// sets in <c>FindAlias</c>/<c>FindAliasVerbose</c>.
        /// </summary>
        public NodeId ReferenceTypeId { get; }

        /// <summary>
        /// Optional per-target server URIs used by the verbose form. May be
        /// <c>null</c> (all targets on the local server) or shorter than
        /// <see cref="ReferencedNodes"/> (trailing entries default to the
        /// local server).
        /// </summary>
        public IReadOnlyList<string?>? ServerUris { get; }
    }
}
