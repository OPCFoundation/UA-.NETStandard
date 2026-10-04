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

namespace Opc.Ua.Gds.Server.AliasNames
{
    /// <summary>
    /// The AliasNames one registered AliasName Server exposes, as read by
    /// an <see cref="IAliasNameSourceReader"/>: its
    /// <c>AliasNameCategoryType</c> hierarchy below the well-known
    /// <c>Aliases</c> object and every <c>AliasNameType</c> instance with
    /// its <c>AliasFor</c> targets (OPC 10000-17 §6, Annex C.2).
    /// </summary>
    /// <remarks>
    /// Categories are identified by their path of BrowseName names below
    /// <c>Aliases</c> (i=23470): <c>[]</c> is <c>Aliases</c> itself,
    /// <c>["TagVariables"]</c> and <c>["Topics"]</c> are the well-known
    /// categories of Part 17 §9, anything else is a server-defined category.
    /// Part 17 Annex C.1 merges categories by the Name part of their
    /// BrowseName, so the path is the identity the GDS aggregates on.
    /// </remarks>
    public sealed class AliasNameSourceSnapshot
    {
        /// <summary>
        /// Creates a snapshot.
        /// </summary>
        /// <param name="serverUri">The ApplicationUri of the source.</param>
        /// <param name="categories">Every category of the source, including
        /// the well-known ones that hold aliases.</param>
        /// <param name="aliases">One entry per alias and target.</param>
        public AliasNameSourceSnapshot(
            string serverUri,
            IReadOnlyList<AliasNameSourceCategory> categories,
            IReadOnlyList<AliasNameSourceAlias> aliases)
        {
            if (string.IsNullOrEmpty(serverUri))
            {
                throw new ArgumentException("The server URI must be set.", nameof(serverUri));
            }
            ServerUri = serverUri;
            Categories = categories ?? throw new ArgumentNullException(nameof(categories));
            Aliases = aliases ?? throw new ArgumentNullException(nameof(aliases));
        }

        /// <summary>
        /// The ApplicationUri of the source, which the GDS adds to its
        /// <c>ServerArray</c> (Part 17 Annex C.2).
        /// </summary>
        public string ServerUri { get; }

        /// <summary>
        /// The categories of the source.
        /// </summary>
        public IReadOnlyList<AliasNameSourceCategory> Categories { get; }

        /// <summary>
        /// The aliases of the source, one entry per target.
        /// </summary>
        public IReadOnlyList<AliasNameSourceAlias> Aliases { get; }
    }

    /// <summary>
    /// One <c>AliasNameCategoryType</c> instance of a source.
    /// </summary>
    /// <param name="Path">The BrowseName names from <c>Aliases</c> down to
    /// the category (see <see cref="AliasNameSourceSnapshot"/>).</param>
    public sealed record AliasNameSourceCategory(IReadOnlyList<string> Path);

    /// <summary>
    /// One target of one <c>AliasNameType</c> instance of a source.
    /// </summary>
    /// <param name="CategoryPath">The path of the category that organizes
    /// the alias.</param>
    /// <param name="Name">The Name part of the alias BrowseName.</param>
    /// <param name="ReferenceTypeId">The reference type from the alias to the
    /// target: <c>AliasFor</c> or a subtype.</param>
    /// <param name="Target">The target in absolute form: the namespace is a
    /// URI, the ServerIndex is not used.</param>
    /// <param name="ServerUri">The ApplicationUri of the Server that hosts
    /// the target, normally the source itself.</param>
    public sealed record AliasNameSourceAlias(
        IReadOnlyList<string> CategoryPath,
        string Name,
        NodeId ReferenceTypeId,
        ExpandedNodeId Target,
        string ServerUri);
}
