/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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

namespace Opc.Ua.Export
{
    /// <summary>
    /// Creates the empty state which carries one imported node.
    /// </summary>
    /// <remarks>
    /// The returned state must be empty: <see cref="UANodeSet"/> applies every
    /// attribute, value, reference and child of the document to it. Return
    /// <c>null</c> to let the import allocate the generic state for the node
    /// class.
    /// </remarks>
    /// <param name="nodeClass">The node class being imported.</param>
    /// <param name="nodeId">The imported node's own NodeId.</param>
    /// <param name="discriminatorId">
    /// The TypeDefinition of an Object or Variable, the MethodDeclarationId of
    /// a Method, and the node's own NodeId for a declaration.
    /// </param>
    /// <returns>The empty state to import into, or <c>null</c>.</returns>
    public delegate NodeState? NodeSetImportStateFactory(
        NodeClass nodeClass,
        NodeId nodeId,
        NodeId discriminatorId);

    /// <summary>
    /// Options for linking a batch of imported NodeSet documents once.
    /// </summary>
    /// <seealso cref="UANodeSet.LinkParentChildRelationships(ISystemContext, NodeStateCollection, NodeSetImportLinkOptions)"/>
    public sealed class NodeSetImportLinkOptions
    {
        /// <summary>
        /// Gets or sets the parent NodeId each imported instance declared.
        /// </summary>
        /// <remarks>
        /// A single-document import carries the declared parent in
        /// <see cref="NodeState.Handle"/> and clears it while linking. A batch
        /// importer which drains those handles into its own table supplies them
        /// here instead, so an application-owned <c>Handle</c> value survives.
        /// </remarks>
        public IReadOnlyDictionary<BaseInstanceState, NodeId>? ParentNodeIds { get; set; }

        /// <summary>
        /// Gets or sets nodes outside the batch which an imported parent
        /// identifier may resolve to.
        /// </summary>
        /// <remarks>
        /// This is how a NodeSet overlay attaches to nodes which already exist,
        /// for example the nodes of a generated model in the same node manager.
        /// </remarks>
        public IReadOnlyDictionary<NodeId, NodeState>? AvailableNodes { get; set; }

        /// <summary>
        /// Gets or sets the predicate deciding whether an imported child should
        /// take over the parent's explicitly defined child slot instead of being
        /// added as an ordinary child.
        /// </summary>
        /// <remarks>
        /// A typed parent declares its children as generated properties. An
        /// imported child which belongs in such a slot has to replace the
        /// generated placeholder, or the typed accessor would keep returning
        /// the placeholder while the address space exposes the imported node.
        /// </remarks>
        public Func<NodeState, BaseInstanceState, bool>? UseTypedReplacement { get; set; }

        /// <summary>
        /// Gets or sets the callback which receives every generated placeholder
        /// displaced by an imported child, together with its replacement.
        /// </summary>
        public Action<BaseInstanceState, BaseInstanceState>? OnTypedReplacement { get; set; }
    }
}
