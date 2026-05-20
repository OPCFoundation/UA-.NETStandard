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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server.AliasNames
{
    /// <summary>
    /// Pluggable backend for an OPC UA Part 17 alias-name provider. One
    /// store may serve multiple categories — each declared via
    /// <see cref="RootCategories"/> together with their sub-categories — and
    /// is the authoritative source for both queries
    /// (<c>FindAlias</c>/<c>FindAliasVerbose</c>) and mutations
    /// (<c>AddAliasesToCategory</c>/<c>DeleteAliasesFromCategory</c>) on
    /// those categories.
    /// </summary>
    /// <remarks>
    /// <para>
    /// All operations are async and accept a <see cref="CancellationToken"/>.
    /// Implementations MUST be thread-safe.
    /// </para>
    /// <para>
    /// <c>categoryId</c> arguments identify any category exposed by this
    /// store — root or nested. When an operation is invoked on a parent
    /// category, <c>FindAlias</c>/<c>FindAliasVerbose</c> implementations
    /// MUST also include results from descendant categories per Part 17
    /// §6.3.2; <c>AddAliasesAsync</c>/<c>DeleteAliasesAsync</c> however
    /// apply only to the named category itself, never its descendants.
    /// </para>
    /// </remarks>
    public interface IAliasNameStore
    {
        /// <summary>
        /// The (immutable) root categories this store owns. Used by
        /// <c>AliasNameNodeManager</c> to build the address space and
        /// by <c>IAliasNameStoreRegistry</c> to route well-known
        /// standard-node dispatches. Standard well-known categories should
        /// use <c>ObjectIds.Aliases</c>, <c>ObjectIds.TagVariables</c> and
        /// <c>ObjectIds.Topics</c> as their identifiers.
        /// </summary>
        IReadOnlyList<AliasNameCategoryDescriptor> RootCategories { get; }

        /// <summary>
        /// Raised after a successful <see cref="AddAliasesAsync"/> or
        /// <see cref="DeleteAliasesAsync"/> batch — carries the new
        /// <c>LastChange</c> value (VersionTime) for the affected
        /// category.
        /// </summary>
        event EventHandler<AliasStoreChangedEventArgs>? Changed;

        /// <summary>
        /// Returns the current <c>LastChange</c> value (Part 17 §6.3.1) for
        /// the named category. Returns <c>null</c> when the category is
        /// unknown to this store or does not expose the
        /// <see cref="AliasNameCapabilities.LastChange"/> capability.
        /// </summary>
        /// <param name="categoryId">The category's <see cref="NodeId"/>.</param>
        uint? GetLastChange(NodeId categoryId);

        /// <summary>
        /// Returns <c>true</c> if <paramref name="categoryId"/> identifies
        /// any category (root or nested) owned by this store.
        /// </summary>
        bool OwnsCategory(NodeId categoryId);

        /// <summary>
        /// Implements <c>FindAlias</c> (Part 17 §6.3.2): returns all aliases
        /// under <paramref name="categoryId"/> (recursively, including
        /// sub-categories) whose name matches the wildcard
        /// <paramref name="aliasNameSearchPattern"/> and whose
        /// <c>ReferenceTypeId</c> is, or is a subtype of,
        /// <paramref name="referenceTypeFilter"/>.
        /// </summary>
        /// <param name="categoryId">The category to search.</param>
        /// <param name="aliasNameSearchPattern">An OPC UA Like-operator
        /// pattern (Part 4 §7.40). An empty pattern returns no
        /// matches.</param>
        /// <param name="referenceTypeFilter">Reference type to filter by;
        /// <c>NodeId.Null</c> or <c>ReferenceTypeIds.References</c> matches
        /// any reference type.</param>
        /// <param name="typeTree">The server type tree used to evaluate
        /// subtype membership.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The matching aliases as Part 17 §7.2
        /// <c>AliasNameDataType</c> records. Returns an empty list when the
        /// category is unknown or there are no matches.</returns>
        ValueTask<IReadOnlyList<AliasNameDataType>> FindAliasAsync(
            NodeId categoryId,
            string? aliasNameSearchPattern,
            NodeId referenceTypeFilter,
            ITypeTable typeTree,
            CancellationToken ct = default);

        /// <summary>
        /// Implements <c>FindAliasVerbose</c> (Part 17 §6.3.3) — same
        /// filtering as <see cref="FindAliasAsync"/> but returns
        /// <c>AliasNameVerboseDataType</c> records (Part 17 §7.3) including
        /// per-target <c>ServerUris</c> and the <c>AliasNameCategoryId</c>
        /// of the alias's home category.
        /// </summary>
        ValueTask<IReadOnlyList<AliasNameVerboseDataType>> FindAliasVerboseAsync(
            NodeId categoryId,
            string? aliasNameSearchPattern,
            NodeId referenceTypeFilter,
            ITypeTable typeTree,
            CancellationToken ct = default);

        /// <summary>
        /// Implements <c>AddAliasesToCategory</c> (Part 17 §6.3.4) — adds
        /// each request as a single alias-to-target mapping in the named
        /// category. Returns one <see cref="StatusCode"/> per request entry
        /// in input order; typical per-entry failures are
        /// <c>BadBrowseNameDuplicated</c> when the mapping already exists,
        /// <c>BadNodeIdInvalid</c>/<c>BadReferenceTypeIdInvalid</c> for
        /// malformed entries, or <c>BadOutOfRange</c> when the store is at
        /// capacity. <c>LastChange</c> for the category MUST advance once
        /// per non-empty successful batch.
        /// </summary>
        /// <exception cref="ServiceResultException">Thrown for method-level
        /// failures: unknown category (<c>BadNodeIdUnknown</c>), category
        /// does not support adds (<c>BadNotSupported</c>), or unauthorized
        /// (<c>BadUserAccessDenied</c>).</exception>
        ValueTask<StatusCode[]> AddAliasesAsync(
            NodeId categoryId,
            IReadOnlyList<AliasAddRequest> requests,
            CancellationToken ct = default);

        /// <summary>
        /// Implements <c>DeleteAliasesFromCategory</c> (Part 17 §6.3.5) —
        /// removes each request as a single alias-to-target mapping from
        /// the named category. Returns one <see cref="StatusCode"/> per
        /// request; typical per-entry failures are <c>BadNotFound</c> when
        /// the mapping is absent. <c>LastChange</c> for the category MUST
        /// advance once per non-empty successful batch.
        /// </summary>
        /// <exception cref="ServiceResultException">Thrown for method-level
        /// failures: unknown category (<c>BadNodeIdUnknown</c>), category
        /// does not support deletes (<c>BadNotSupported</c>), or
        /// unauthorized (<c>BadUserAccessDenied</c>).</exception>
        ValueTask<StatusCode[]> DeleteAliasesAsync(
            NodeId categoryId,
            IReadOnlyList<AliasDeleteRequest> requests,
            CancellationToken ct = default);
    }
}
