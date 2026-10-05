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
using Microsoft.Extensions.Logging;
using Opc.Ua.AMB.Server.Assets;
using Opc.Ua.AMB.Server.Discovery;
using Opc.Ua.Server;
using Opc.Ua.Server.NodeManager;

namespace Opc.Ua.AMB.Server
{
    /// <summary>
    /// Asset discovery through the AMB alias categories (OPC 10000-110 §8.2).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every asset with a <c>ProductInstanceUri</c> is listed in
    /// <c>AssetsByProductInstanceUri</c>; every asset that has a
    /// <c>2:AssetId</c> is listed in <c>AssetsByAssetId</c>, under its value
    /// or, while it is empty, as <c>NoAssetIdAssigned</c> (§8.2.3). An asset
    /// without an <c>AssetId</c> is not listed there.
    /// </para>
    /// <para>
    /// One <c>AliasNameType</c> object stands for an alias name in a
    /// category: assets that share a name - two nodes with one
    /// <c>ProductInstanceUri</c>, or several assets without an assigned
    /// <c>AssetId</c> - are the targets of its <c>AliasFor</c> references
    /// (§8.1).
    /// </para>
    /// <para>
    /// The aliases follow the values: writing a configurable
    /// <c>AssetId</c> moves the asset at once, and a change of the value of
    /// the <c>ProductInstanceUri</c> or of an <c>AssetId</c> outside the
    /// binding moves it once the change is reported through
    /// <c>ClearChangeMasks</c>.
    /// </para>
    /// </remarks>
    public sealed partial class AmbNodeManager
    {
        /// <summary>
        /// Wires the alias categories and lists the assets registered so far.
        /// </summary>
        private async ValueTask InitializeDiscoveryAsync(CancellationToken cancellationToken)
        {
            AliasNameCategoryState assets = Category(ObjectIds.Assets);
            m_byProductInstanceUri = Category(ObjectIds.AssetsByProductInstanceUri);
            m_byAssetId = Category(ObjectIds.AssetsByAssetId);
            foreach (AliasNameCategoryState category in new[] { m_byProductInstanceUri, m_byAssetId })
            {
                PropertyState<string>? nodeVersion = category.GetNodeVersionProperty();
                if (nodeVersion != null && nodeVersion.Value == null)
                {
                    nodeVersion.Value = "0";
                }
            }

            m_aliasIndex = new AssetAliasIndex(assets, m_byProductInstanceUri, m_byAssetId);
            m_aliasIndex.Wire(assets, Server.TypeTree);
            m_aliasIndex.Wire(m_byProductInstanceUri, Server.TypeTree);
            m_aliasIndex.Wire(m_byAssetId, Server.TypeTree);

            // Assets registered before this address space existed - by a
            // Device Integration manager created earlier - are listed now.
            m_discoveryReady = true;
            ArrayOf<AssetHandle> registered = m_assetManagement.Snapshot();
            for (int ii = 0; ii < registered.Count; ii++)
            {
                await PublishAliasesAsync(registered[ii], cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Lists a newly registered asset in the alias categories.
        /// </summary>
        internal async ValueTask OnAssetRegisteredAsync(AssetHandle handle, CancellationToken cancellationToken)
        {
            if (m_discoveryReady)
            {
                await PublishAliasesAsync(handle, cancellationToken).ConfigureAwait(false);
            }
            await OnAssetsChangedAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Removes an unregistered asset from the alias categories.
        /// </summary>
        internal async ValueTask OnAssetUnregisteredAsync(AssetHandle handle, CancellationToken cancellationToken)
        {
            await m_aliasLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await WithdrawAsync(handle, handle.ProductInstanceUriAlias, cancellationToken).ConfigureAwait(false);
                handle.ProductInstanceUriAlias = null;
                await WithdrawAsync(handle, handle.AssetIdAlias, cancellationToken).ConfigureAwait(false);
                handle.AssetIdAlias = null;
            }
            finally
            {
                m_aliasLock.Release();
            }
            await ReleaseLocationsAsync(handle, cancellationToken).ConfigureAwait(false);
            await OnAssetsChangedAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Moves an asset to its new name in <c>AssetsByAssetId</c> after a
        /// client wrote its configurable <c>AssetId</c>, before the write is
        /// acknowledged.
        /// </summary>
        /// <param name="handle">The asset.</param>
        /// <param name="assetId">The value written; the property still has the old one.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        internal async ValueTask OnAssetIdChangedAsync(
            AssetHandle handle,
            string? assetId,
            CancellationToken cancellationToken)
        {
            if (!m_discoveryReady || !handle.IsRegistered)
            {
                return;
            }

            bool changed;
            await m_aliasLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                changed = await ReconcileAliasesAsync(handle, new AssetIdValue(assetId), cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                m_aliasLock.Release();
            }
            if (changed)
            {
                m_ambLogger.AssetIdAliasMoved(handle.BrowseName, handle.AssetIdAlias?.Name ?? string.Empty);
            }
        }

        /// <summary>
        /// Brings the aliases of an asset in line with the values of its
        /// identification once a change of them was reported. The work runs
        /// on the thread pool: the change is reported from inside
        /// <c>ClearChangeMasks</c> of whoever wrote the value.
        /// </summary>
        internal void OnIdentificationChanged(AssetHandle handle)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await FollowIdentificationAsync(handle, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    m_ambLogger.IdentificationNotFollowed(ex, handle.BrowseName);
                }
            });
        }

        /// <summary>
        /// Brings the aliases of an asset in line with the values of its
        /// identification.
        /// </summary>
        internal async ValueTask FollowIdentificationAsync(AssetHandle handle, CancellationToken cancellationToken)
        {
            if (!m_discoveryReady || !handle.IsRegistered)
            {
                return;
            }

            bool changed;
            await m_aliasLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                changed = await ReconcileAliasesAsync(handle, null, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                m_aliasLock.Release();
            }
            if (changed)
            {
                await OnAssetsChangedAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Lists an asset in the alias categories once.
        /// </summary>
        private async ValueTask PublishAliasesAsync(AssetHandle handle, CancellationToken cancellationToken)
        {
            await m_aliasLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (handle.IsRegistered)
                {
                    await ReconcileAliasesAsync(handle, null, cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                m_aliasLock.Release();
            }
        }

        /// <summary>
        /// Lists, moves or withdraws the aliases of an asset so they match its
        /// identification; the caller holds the alias lock.
        /// </summary>
        /// <param name="handle">The asset.</param>
        /// <param name="assetId">
        /// The <c>AssetId</c> to use instead of the value of the property, while
        /// a write of it is being acknowledged.
        /// </param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>Whether an alias changed.</returns>
        private async ValueTask<bool> ReconcileAliasesAsync(
            AssetHandle handle,
            AssetIdValue? assetId,
            CancellationToken cancellationToken)
        {
            AssetDiscovery discovery = m_assetManagement.Options.Discovery;
            bool changed = false;

            if ((discovery & AssetDiscovery.ProductInstanceUri) != 0)
            {
                string productInstanceUri = handle.ProductInstanceUri;
                string? name = productInstanceUri.Length > 0 ? productInstanceUri : null;
                (handle.ProductInstanceUriAlias, bool moved) = await MoveAsync(
                    handle,
                    m_byProductInstanceUri!,
                    handle.ProductInstanceUriAlias,
                    name,
                    cancellationToken).ConfigureAwait(false);
                changed |= moved;
            }

            if ((discovery & AssetDiscovery.AssetId) != 0)
            {
                // §8.2.3: only an asset that has an AssetId is listed, as
                // NoAssetIdAssigned while it is empty.
                string? name = assetId is AssetIdValue written
                    ? AssetIdAliasName(written.Value)
                    : handle.HasAssetIdProperty ? AssetIdAliasName(handle.AssetId) : null;
                (handle.AssetIdAlias, bool moved) = await MoveAsync(
                    handle,
                    m_byAssetId!,
                    handle.AssetIdAlias,
                    name,
                    cancellationToken).ConfigureAwait(false);
                changed |= moved;
            }
            return changed;
        }

        /// <summary>
        /// Moves an asset to an alias name of a category, or out of it.
        /// </summary>
        private async ValueTask<(AssetAlias? Alias, bool Changed)> MoveAsync(
            AssetHandle handle,
            AliasNameCategoryState category,
            AssetAlias? current,
            string? name,
            CancellationToken cancellationToken)
        {
            if (current != null && string.Equals(current.Name, name, StringComparison.Ordinal))
            {
                return (current, false);
            }
            if (current == null && name == null)
            {
                return (null, false);
            }
            await WithdrawAsync(handle, current, cancellationToken).ConfigureAwait(false);
            AssetAlias? alias = name == null
                ? null
                : await PublishAsync(category, name, handle, cancellationToken).ConfigureAwait(false);
            return (alias, true);
        }

        /// <summary>
        /// Lists an asset under an alias name of a category: in the index, and
        /// as a target of the <c>AliasNameType</c> object of the name (OPC
        /// 10000-17 §6.2), which is organized by the category and created with
        /// its first asset. The asset gets the inverse <c>HasAlias</c>
        /// reference.
        /// </summary>
        private async ValueTask<AssetAlias> PublishAsync(
            AliasNameCategoryState category,
            string name,
            AssetHandle handle,
            CancellationToken cancellationToken)
        {
            await m_aliasIndex!.AddAsync(category.NodeId, name, handle.NodeId, cancellationToken)
                .ConfigureAwait(false);

            if (!m_aliasNodes.TryGetValue((category.NodeId, name), out SharedAlias? shared))
            {
                var node = new AliasNameState(category);
                node.Create(
                    SystemContext,
                    NodeId.Null,
                    new QualifiedName(name, AmbNamespaceIndex),
                    new LocalizedText(name),
                    true);
                node.ReferenceTypeId = Ua.ReferenceTypeIds.Organizes;
                node.AddReference(Ua.ReferenceTypeIds.AliasFor, false, handle.NodeId);
                category.AddChild(node);
                await AddPredefinedNodeAsync(SystemContext, node, cancellationToken).ConfigureAwait(false);
                shared = new SharedAlias(node);
                m_aliasNodes.Add((category.NodeId, name), shared);
                ModelChangeAggregator.RecordNodeAdded(node.NodeId, node.TypeDefinitionId);
            }
            else
            {
                shared.Node.AddReference(Ua.ReferenceTypeIds.AliasFor, false, handle.NodeId);
            }
            shared.Assets.Add(handle.NodeId);
            handle.Asset.AddReference(Ua.ReferenceTypeIds.AliasFor, true, shared.Node.NodeId);

            ModelChangeAggregator.RecordReferenceAdded(category.NodeId);
            EmitModelChange(SystemContext);
            return new AssetAlias(category, name, shared.Node);
        }

        /// <summary>
        /// Removes an asset from an alias name of a category; the
        /// <c>AliasNameType</c> object goes with its last asset.
        /// </summary>
        private async ValueTask WithdrawAsync(
            AssetHandle handle,
            AssetAlias? alias,
            CancellationToken cancellationToken)
        {
            if (alias == null)
            {
                return;
            }
            await m_aliasIndex!.RemoveAsync(alias.Category.NodeId, alias.Name, handle.NodeId, cancellationToken)
                .ConfigureAwait(false);
            handle.Asset.RemoveReference(Ua.ReferenceTypeIds.AliasFor, true, alias.Node.NodeId);
            alias.Node.RemoveReference(Ua.ReferenceTypeIds.AliasFor, false, handle.NodeId);
            if (m_aliasNodes.TryGetValue((alias.Category.NodeId, alias.Name), out SharedAlias? shared))
            {
                shared.Assets.Remove(handle.NodeId);
                if (shared.Assets.Count == 0)
                {
                    m_aliasNodes.Remove((alias.Category.NodeId, alias.Name));
                    await DeleteNodeAsync(SystemContext, alias.Node.NodeId, cancellationToken).ConfigureAwait(false);
                }
            }
            ModelChangeAggregator.RecordReferenceDeleted(alias.Category.NodeId);
            EmitModelChange(SystemContext);
        }

        private AliasNameCategoryState Category(ExpandedNodeId nodeId)
        {
            return FindPredefinedNode<AliasNameCategoryState>(ExpandedNodeId.ToNodeId(nodeId, Server.NamespaceUris)) ??
                throw new InvalidOperationException($"The AMB model does not provide the alias category {nodeId}.");
        }

        private static string AssetIdAliasName(string? assetId)
        {
            return string.IsNullOrEmpty(assetId) ? AmbBrowseNames.NoAssetIdAssigned : assetId!;
        }

        /// <summary>
        /// The <c>AliasNameType</c> object of an alias name and the assets it
        /// stands for; guarded by the alias lock.
        /// </summary>
        private sealed class SharedAlias
        {
            public SharedAlias(AliasNameState node)
            {
                Node = node;
            }

            public AliasNameState Node { get; }

            public HashSet<NodeId> Assets { get; } = [];
        }

        /// <summary>
        /// An <c>AssetId</c> being written.
        /// </summary>
        /// <param name="Value">The value; null or empty when cleared.</param>
        private readonly record struct AssetIdValue(string? Value);

        private readonly SemaphoreSlim m_aliasLock = new(1, 1);
        private readonly Dictionary<(NodeId Category, string Name), SharedAlias> m_aliasNodes = [];
        private AssetAliasIndex? m_aliasIndex;
        private AliasNameCategoryState? m_byProductInstanceUri;
        private AliasNameCategoryState? m_byAssetId;
        private volatile bool m_discoveryReady;
    }

    /// <summary>
    /// The alias of an asset in one category.
    /// </summary>
    /// <param name="Category">The category.</param>
    /// <param name="Name">The alias name.</param>
    /// <param name="Node">The <c>AliasNameType</c> object of the name.</param>
    internal sealed record AssetAlias(AliasNameCategoryState Category, string Name, AliasNameState Node);
}
