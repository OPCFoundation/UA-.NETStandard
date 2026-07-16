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
    /// Server-wide registry of <see cref="IAliasNameStore"/> instances and
    /// a live dispatcher used by both the standalone
    /// <c>AliasNameNodeManager</c> and the standard-node binder in
    /// <c>DiagnosticsNodeManager</c> to route Part 17 method calls to the
    /// store that owns the affected category.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each category <see cref="NodeId"/> is owned by exactly one
    /// registered store. <see cref="Register"/> rejects a store whose
    /// root or sub-category set overlaps with any already-registered
    /// store's coverage.
    /// </para>
    /// <para>
    /// Dispatch resolves the owning store at call time, not at registration
    /// time — this allows custom node managers to register their stores
    /// after <c>DiagnosticsNodeManager</c> has already activated its
    /// predefined Part 17 nodes. When no store owns the supplied
    /// <c>categoryId</c>, every dispatch method returns
    /// <c>BadNotImplemented</c> (Part 17 §6.3.2 — empty result is also
    /// allowed, but <c>BadNotImplemented</c> is the more honest answer for
    /// the well-known nodes).
    /// </para>
    /// </remarks>
    public interface IAliasNameStoreRegistry
    {
        /// <summary>
        /// The currently registered stores in registration order.
        /// </summary>
        IReadOnlyList<IAliasNameStore> Stores { get; }

        /// <summary>
        /// Registers <paramref name="store"/>. Throws when any of its
        /// category <see cref="NodeId"/>s (root or nested) is already
        /// owned by another registered store.
        /// </summary>
        void Register(IAliasNameStore store);

        /// <summary>
        /// Unregisters <paramref name="store"/>. No-op if not registered.
        /// </summary>
        void Unregister(IAliasNameStore store);

        /// <summary>
        /// Resolves the store that owns <paramref name="categoryId"/>, or
        /// <c>null</c> if no registered store does.
        /// </summary>
        IAliasNameStore? GetStoreForCategory(NodeId categoryId);

        /// <summary>
        /// Raised after any registered store reports a change (alias added
        /// or removed) so binders can refresh address-space values such as
        /// the standard <c>Aliases.LastChange</c> property.
        /// </summary>
        event EventHandler<AliasStoreChangedEventArgs>? Changed;

        /// <summary>
        /// Dispatches a <c>FindAlias</c> call. Returns
        /// <c>BadNotImplemented</c> on the service-result when no store
        /// owns the category.
        /// </summary>
        ValueTask<(ServiceResult Result, IReadOnlyList<AliasNameDataType> Aliases)>
            DispatchFindAliasAsync(
                NodeId categoryId,
                string? aliasNameSearchPattern,
                NodeId referenceTypeFilter,
                ITypeTable typeTree,
                CancellationToken ct = default);

        /// <summary>
        /// Dispatches a <c>FindAliasVerbose</c> call. Returns
        /// <c>BadNotImplemented</c> on the service-result when no store
        /// owns the category.
        /// </summary>
        ValueTask<(ServiceResult Result, IReadOnlyList<AliasNameVerboseDataType> Aliases)>
            DispatchFindAliasVerboseAsync(
                NodeId categoryId,
                string? aliasNameSearchPattern,
                NodeId referenceTypeFilter,
                ITypeTable typeTree,
                CancellationToken ct = default);

        /// <summary>
        /// Dispatches an <c>AddAliasesToCategory</c> call. Returns
        /// <c>BadNotImplemented</c> on the service-result when no store
        /// owns the category; otherwise returns the per-entry status codes
        /// from the owning store.
        /// </summary>
        ValueTask<(ServiceResult Result, StatusCode[] ErrorCodes)>
            DispatchAddAliasesAsync(
                NodeId categoryId,
                IReadOnlyList<AliasAddRequest> requests,
                CancellationToken ct = default);

        /// <summary>
        /// Dispatches a <c>DeleteAliasesFromCategory</c> call. Returns
        /// <c>BadNotImplemented</c> on the service-result when no store
        /// owns the category; otherwise returns the per-entry status codes
        /// from the owning store.
        /// </summary>
        ValueTask<(ServiceResult Result, StatusCode[] ErrorCodes)>
            DispatchDeleteAliasesAsync(
                NodeId categoryId,
                IReadOnlyList<AliasDeleteRequest> requests,
                CancellationToken ct = default);
    }

    /// <summary>
    /// Optional opt-in interface that hosts a server's
    /// <see cref="IAliasNameStoreRegistry"/>. <c>ServerInternalData</c>
    /// implements this so node managers can resolve the registry without
    /// any <c>IServerInternal</c> surface change; external/mocked
    /// <c>IServerInternal</c> implementations remain unaffected.
    /// </summary>
    public interface IAliasNameStoreRegistryProvider
    {
        /// <summary>
        /// Gets the server's alias-name store registry; never <c>null</c>.
        /// </summary>
        IAliasNameStoreRegistry AliasNameStoreRegistry { get; }
    }
}
