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
    /// Describes an OPC UA Part 17 alias name category — a node of type
    /// <c>AliasNameCategoryType</c> (Part 17 §6.3.1) — together with the
    /// optional Part 17 children it exposes and any sub-categories.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Returned by <c>IAliasNameStore.RootCategories</c> to describe
    /// the address-space tree a store contributes. Used by
    /// <c>AliasNameNodeManager</c> to construct
    /// <c>AliasNameCategoryState</c> instances and wire their method
    /// handlers, and by the standard-node late binder in
    /// <c>DiagnosticsNodeManager</c> to identify which standard categories
    /// (<c>Aliases</c>, <c>TagVariables</c>, <c>Topics</c> — Part 17 §9) a
    /// store serves.
    /// </para>
    /// <para>
    /// <see cref="NodeId"/> uniquely identifies the category and is the
    /// <c>categoryId</c> passed to <c>IAliasNameStore</c> when dispatching
    /// <c>FindAlias</c>, <c>FindAliasVerbose</c>, <c>AddAliasesToCategory</c>
    /// and <c>DeleteAliasesFromCategory</c> calls. To serve a standard
    /// well-known category, use the corresponding identifier:
    /// <c>ObjectIds.Aliases</c>, <c>ObjectIds.TagVariables</c> or
    /// <c>ObjectIds.Topics</c>.
    /// </para>
    /// </remarks>
    public sealed record AliasNameCategoryDescriptor
    {
        /// <summary>
        /// Initializes a new category descriptor.
        /// </summary>
        /// <param name="nodeId">The category's <see cref="NodeId"/>. For a
        /// standard well-known category use one of the
        /// <c>ObjectIds.Aliases</c>/<c>TagVariables</c>/<c>Topics</c>
        /// values.</param>
        /// <param name="browseName">The category's <see cref="QualifiedName"/>.
        /// When serving a standard well-known category, callers typically
        /// supply the standard name (e.g. <c>"Aliases"</c>); for app-defined
        /// categories any meaningful name in the manager's namespace.</param>
        /// <param name="capabilities">The optional Part 17 children this
        /// category exposes. The mandatory <c>FindAlias</c> method is always
        /// present and does not need to be flagged.</param>
        /// <param name="subCategories">Optional list of sub-categories;
        /// <c>FindAlias</c> on the parent recursively searches these as
        /// well, per Part 17 §6.3.2.</param>
        public AliasNameCategoryDescriptor(
            NodeId nodeId,
            QualifiedName browseName,
            AliasNameCapabilities capabilities = AliasNameCapabilities.None,
            IReadOnlyList<AliasNameCategoryDescriptor>? subCategories = null)
        {
            if (nodeId.IsNull)
            {
                throw new ArgumentException(
                    "Category NodeId must not be null.",
                    nameof(nodeId));
            }
            if (browseName.IsNull)
            {
                throw new ArgumentException(
                    "Category BrowseName must not be null.",
                    nameof(browseName));
            }

            NodeId = nodeId;
            BrowseName = browseName;
            Capabilities = capabilities;
            SubCategories = subCategories ?? [];
        }

        /// <summary>
        /// The category's <see cref="NodeId"/>; also used as the dispatch
        /// discriminator when routing <c>IAliasNameStore</c> calls.
        /// </summary>
        public NodeId NodeId { get; }

        /// <summary>
        /// The category's <see cref="QualifiedName"/>.
        /// </summary>
        public QualifiedName BrowseName { get; }

        /// <summary>
        /// Which optional Part 17 children this category exposes.
        /// </summary>
        public AliasNameCapabilities Capabilities { get; }

        /// <summary>
        /// The (possibly empty) sub-categories nested under this category
        /// via <c>Organizes</c> references.
        /// </summary>
        public IReadOnlyList<AliasNameCategoryDescriptor> SubCategories { get; }
    }
}
