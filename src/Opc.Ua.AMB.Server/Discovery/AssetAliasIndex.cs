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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server.AliasNames;

namespace Opc.Ua.AMB.Server.Discovery
{
    /// <summary>
    /// Answers <c>FindAlias</c> on the three AMB alias categories from the
    /// registered assets (OPC 10000-110 §8.2).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The aliases live in an <see cref="InMemoryAliasNameStore"/> whose
    /// descriptor tree is <c>Assets</c> with the two subcategories, so
    /// <c>FindAlias</c> on <c>Assets</c> searches both, and every alias name
    /// carries the browse-name namespace of its category - the AMB
    /// namespace, as §8.2.2 requires.
    /// </para>
    /// <para>
    /// The store is reached through a registry of its own rather than the
    /// server-wide one: the server would otherwise try to materialize the
    /// AMB categories in its diagnostics node manager, which does not own
    /// them. The flip side is that <c>0:Aliases/FindAlias</c> does not search
    /// the AMB categories; clients call the methods of the categories.
    /// </para>
    /// <para>
    /// Several assets without an <c>AssetId</c> share the alias name
    /// <c>NoAssetIdAssigned</c>; <c>FindAlias</c> returns them as one entry
    /// with several referenced nodes, which §8.1 allows.
    /// </para>
    /// </remarks>
    internal sealed class AssetAliasIndex : IDisposable
    {
        /// <summary>
        /// Creates the index for the three categories.
        /// </summary>
        public AssetAliasIndex(
            AliasNameCategoryState assets,
            AliasNameCategoryState byProductInstanceUri,
            AliasNameCategoryState byAssetId)
        {
            const AliasNameCapabilities mutable =
                AliasNameCapabilities.AddAliasesToCategory | AliasNameCapabilities.DeleteAliasesFromCategory;
            var root = new AliasNameCategoryDescriptor(
                assets.NodeId,
                assets.BrowseName,
                AliasNameCapabilities.None,
                [
                    new AliasNameCategoryDescriptor(
                        byProductInstanceUri.NodeId,
                        byProductInstanceUri.BrowseName,
                        mutable),
                    new AliasNameCategoryDescriptor(byAssetId.NodeId, byAssetId.BrowseName, mutable)
                ]);
            m_store = new InMemoryAliasNameStore([root]);
            m_registry = new AliasNameStoreRegistry();
            m_registry.Register(m_store);
        }

        /// <summary>
        /// Points the <c>FindAlias</c> method of a category at the index.
        /// </summary>
        /// <param name="category">The category.</param>
        /// <param name="typeTree">The type tree reference type filters are resolved in.</param>
        public void Wire(AliasNameCategoryState category, ITypeTable typeTree)
        {
            NodeId categoryId = category.NodeId;
            if (category.FindAlias != null)
            {
                category.FindAlias.OnCallAsync = (context, method, objectId, pattern, referenceTypeFilter, ct) =>
                    FindAsync(categoryId, pattern, referenceTypeFilter, typeTree, ct);
            }
        }

        /// <summary>
        /// Runs <c>FindAlias</c> on a category (OPC 10000-17 §6.3.2).
        /// </summary>
        public async ValueTask<FindAliasMethodStateResult> FindAsync(
            NodeId categoryId,
            string pattern,
            NodeId referenceTypeFilter,
            ITypeTable typeTree,
            CancellationToken cancellationToken)
        {
            if (!AliasNameWildcardMatcher.IsValidPattern(pattern))
            {
                return Failed("AliasNameSearchPattern is not a valid search string.");
            }
            if (!referenceTypeFilter.IsNull &&
                !referenceTypeFilter.Equals(Ua.ReferenceTypeIds.References) &&
                !referenceTypeFilter.Equals(Ua.ReferenceTypeIds.AliasFor) &&
                !typeTree.IsTypeOf(referenceTypeFilter, Ua.ReferenceTypeIds.AliasFor))
            {
                return Failed("ReferenceTypeFilter must be AliasFor or one of its subtypes.");
            }

            (ServiceResult result, IReadOnlyList<AliasNameDataType> aliases) = await m_registry
                .DispatchFindAliasAsync(categoryId, pattern, referenceTypeFilter, typeTree, cancellationToken)
                .ConfigureAwait(false);

            var found = new AliasNameDataType[aliases.Count];
            for (int ii = 0; ii < aliases.Count; ii++)
            {
                found[ii] = aliases[ii];
            }
            return new FindAliasMethodStateResult
            {
                ServiceResult = result,
                AliasNodeList = found.ToArrayOf()
            };
        }

        /// <summary>
        /// Adds an asset under an alias name of a category.
        /// </summary>
        /// <exception cref="ServiceResultException">The store refuses the alias.</exception>
        public async ValueTask AddAsync(
            NodeId categoryId,
            string name,
            NodeId asset,
            CancellationToken cancellationToken)
        {
            StatusCode[] results = await m_store.AddAliasesAsync(
                categoryId,
                [new AliasAddRequest(name, asset, null, Ua.ReferenceTypeIds.AliasFor)],
                cancellationToken).ConfigureAwait(false);
            if (StatusCode.IsBad(results[0]))
            {
                throw ServiceResultException.Create(
                    results[0],
                    "Asset {0} cannot be listed as '{1}' in {2}.",
                    asset,
                    name,
                    categoryId);
            }
        }

        /// <summary>
        /// Removes an asset from an alias name of a category.
        /// </summary>
        public async ValueTask RemoveAsync(
            NodeId categoryId,
            string name,
            NodeId asset,
            CancellationToken cancellationToken)
        {
            await m_store.DeleteAliasesAsync(
                categoryId,
                [new AliasDeleteRequest(name, asset)],
                cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            m_registry.Dispose();
            m_store.Dispose();
        }

        private static FindAliasMethodStateResult Failed(string message)
        {
            return new FindAliasMethodStateResult
            {
                ServiceResult = ServiceResult.Create(StatusCodes.BadInvalidArgument, message),
                AliasNodeList = []
            };
        }

        private readonly InMemoryAliasNameStore m_store;
        private readonly AliasNameStoreRegistry m_registry;
    }
}
