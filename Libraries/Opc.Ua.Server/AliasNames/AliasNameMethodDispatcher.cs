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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server.AliasNames
{
    /// <summary>
    /// Translates Part 17 method-state typed call handlers into
    /// <see cref="IAliasNameStoreRegistry"/> dispatch calls. Provides the
    /// glue used by both <c>AliasNameNodeManager</c> (for its own category
    /// instances) and the standard-node late binder in
    /// <c>DiagnosticsNodeManager</c> (for the well-known
    /// <c>Aliases</c>/<c>TagVariables</c>/<c>Topics</c> nodes).
    /// </summary>
    /// <remarks>
    /// The dispatcher centralises:
    /// <list type="bullet">
    ///   <item><description>method-level argument validation (array length
    ///   mismatches → <c>BadInvalidArgument</c>; see Part 17 §6.3.4 / §6.3.5
    ///   parallel-array semantics);</description></item>
    ///   <item><description>conversion between the auto-generated
    ///   <see cref="ArrayOf{T}"/> input shapes and the
    ///   <see cref="AliasAddRequest"/>/<see cref="AliasDeleteRequest"/>
    ///   record shapes consumed by stores;</description></item>
    ///   <item><description>conversion of registry results back into the
    ///   generated <c>*MethodStateResult</c> types.</description></item>
    /// </list>
    /// </remarks>
    internal static class AliasNameMethodDispatcher
    {
        /// <summary>
        /// Dispatches a <c>FindAlias</c> method call (Part 17 §6.3.2).
        /// </summary>
        public static async ValueTask<FindAliasMethodStateResult> FindAliasAsync(
            IAliasNameStoreRegistry registry,
            ITypeTable typeTree,
            NodeId categoryId,
            string aliasNameSearchPattern,
            NodeId referenceTypeFilter,
            CancellationToken ct)
        {
            (ServiceResult result, IReadOnlyList<AliasNameDataType> aliases) = await registry
                .DispatchFindAliasAsync(
                    categoryId,
                    aliasNameSearchPattern,
                    referenceTypeFilter,
                    typeTree,
                    ct)
                .ConfigureAwait(false);

            return new FindAliasMethodStateResult
            {
                ServiceResult = result,
                AliasNodeList = ToArrayOf(aliases)
            };
        }

        /// <summary>
        /// Dispatches a <c>FindAliasVerbose</c> method call (Part 17 §6.3.3).
        /// </summary>
        public static async ValueTask<FindAliasVerboseMethodStateResult> FindAliasVerboseAsync(
            IAliasNameStoreRegistry registry,
            ITypeTable typeTree,
            NodeId categoryId,
            string aliasNameSearchPattern,
            NodeId referenceTypeFilter,
            CancellationToken ct)
        {
            (ServiceResult result, IReadOnlyList<AliasNameVerboseDataType> aliases) = await registry
                .DispatchFindAliasVerboseAsync(
                    categoryId,
                    aliasNameSearchPattern,
                    referenceTypeFilter,
                    typeTree,
                    ct)
                .ConfigureAwait(false);

            return new FindAliasVerboseMethodStateResult
            {
                ServiceResult = result,
                AliasNodeList = ToArrayOf(aliases)
            };
        }

        /// <summary>
        /// Dispatches an <c>AddAliasesToCategory</c> method call (Part 17
        /// §6.3.4). Validates the parallel-array shape before invoking the
        /// store.
        /// </summary>
        public static async ValueTask<AddAliasesToCategoryMethodStateResult> AddAliasesAsync(
            IAliasNameStoreRegistry registry,
            NodeId categoryId,
            ArrayOf<string> aliasNames,
            ArrayOf<ExpandedNodeId> targetNodes,
            ArrayOf<string> targetServers,
            NodeId targetReferenceType,
            CancellationToken ct)
        {
            if (aliasNames.Count != targetNodes.Count ||
                aliasNames.Count != targetServers.Count)
            {
                return new AddAliasesToCategoryMethodStateResult
                {
                    ServiceResult = ServiceResult.Create(
                        StatusCodes.BadInvalidArgument,
                        "AliasNames, TargetNodes and TargetServers must have equal length."),
                    ErrorCodes = default
                };
            }

            var requests = new List<AliasAddRequest>(aliasNames.Count);
            for (int i = 0; i < aliasNames.Count; i++)
            {
                requests.Add(new AliasAddRequest(
                    aliasNames[i] ?? string.Empty,
                    targetNodes[i],
                    targetServers[i],
                    targetReferenceType));
            }

            (ServiceResult result, StatusCode[] codes) = await registry
                .DispatchAddAliasesAsync(categoryId, requests, ct)
                .ConfigureAwait(false);

            return new AddAliasesToCategoryMethodStateResult
            {
                ServiceResult = result,
                ErrorCodes = codes.ToArrayOf()
            };
        }

        /// <summary>
        /// Dispatches a <c>DeleteAliasesFromCategory</c> method call
        /// (Part 17 §6.3.5). Validates the parallel-array shape before
        /// invoking the store.
        /// </summary>
        public static async ValueTask<DeleteAliasesFromCategoryMethodStateResult> DeleteAliasesAsync(
            IAliasNameStoreRegistry registry,
            NodeId categoryId,
            ArrayOf<string> aliasNames,
            ArrayOf<ExpandedNodeId> targetNodes,
            CancellationToken ct)
        {
            if (aliasNames.Count != targetNodes.Count)
            {
                return new DeleteAliasesFromCategoryMethodStateResult
                {
                    ServiceResult = ServiceResult.Create(
                        StatusCodes.BadInvalidArgument,
                        "AliasNames and TargetNodes must have equal length."),
                    ErrorCodes = default
                };
            }

            var requests = new List<AliasDeleteRequest>(aliasNames.Count);
            for (int i = 0; i < aliasNames.Count; i++)
            {
                requests.Add(new AliasDeleteRequest(
                    aliasNames[i] ?? string.Empty,
                    targetNodes[i]));
            }

            (ServiceResult result, StatusCode[] codes) = await registry
                .DispatchDeleteAliasesAsync(categoryId, requests, ct)
                .ConfigureAwait(false);

            return new DeleteAliasesFromCategoryMethodStateResult
            {
                ServiceResult = result,
                ErrorCodes = codes.ToArrayOf()
            };
        }

        private static ArrayOf<T> ToArrayOf<T>(IReadOnlyList<T> items)
        {
            if (items == null || items.Count == 0)
            {
                return ArrayOf.Empty<T>();
            }
            var array = new T[items.Count];
            for (int i = 0; i < items.Count; i++)
            {
                array[i] = items[i];
            }
            return array.ToArrayOf();
        }
    }
}
