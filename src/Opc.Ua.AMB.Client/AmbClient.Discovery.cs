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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.Streaming;
using MonitoringOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;

namespace Opc.Ua.AMB.Client
{
    /// <summary>
    /// The alias categories through which OPC 10000-110 §8.2 lets clients
    /// discover assets.
    /// </summary>
    public enum AssetAliasCategory
    {
        /// <summary><c>Assets</c>, which covers both subcategories.</summary>
        Assets,

        /// <summary><c>AssetsByProductInstanceUri</c>.</summary>
        ByProductInstanceUri,

        /// <summary><c>AssetsByAssetId</c>.</summary>
        ByAssetId
    }

    /// <summary>
    /// An alias of one or more assets.
    /// </summary>
    /// <param name="Category">The category the alias was found in.</param>
    /// <param name="Name">The alias name: a ProductInstanceUri, an AssetId or <c>NoAssetIdAssigned</c>.</param>
    /// <param name="ReferencedNodes">
    /// The assets; an asset of another server carries its server URI.
    /// </param>
    public sealed record AssetAlias(AssetAliasCategory Category, string Name, ArrayOf<ExpandedNodeId> ReferencedNodes);

    /// <summary>
    /// A change of the assets listed in an alias category, announced by a
    /// <c>GeneralModelChangeEvent</c>.
    /// </summary>
    /// <param name="Category">The category whose aliases changed.</param>
    /// <param name="Verb">What changed, as <c>ModelChangeStructureVerbMask</c> bits.</param>
    public sealed record AssetSetChange(AssetAliasCategory Category, byte Verb);

    public sealed partial class AmbClient
    {
        /// <summary>
        /// Gets the NodeId of an alias category.
        /// </summary>
        /// <param name="category">The category.</param>
        /// <returns>The NodeId, or <see cref="NodeId.Null"/> when the server has no AMB.</returns>
        public NodeId GetCategoryId(AssetAliasCategory category)
        {
            return ToNodeId(category switch
            {
                AssetAliasCategory.ByProductInstanceUri => ObjectIds.AssetsByProductInstanceUri,
                AssetAliasCategory.ByAssetId => ObjectIds.AssetsByAssetId,
                _ => ObjectIds.Assets
            });
        }

        /// <summary>
        /// Finds assets by alias name with the <c>FindAlias</c> method of a
        /// category (OPC 10000-17).
        /// </summary>
        /// <param name="pattern">
        /// The pattern, with the wildcards of OPC 10000-17, for example
        /// <c>urn:acme:%</c>.
        /// </param>
        /// <param name="category">The category to search; <c>Assets</c> searches both.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The matching aliases.</returns>
        /// <exception cref="ServiceResultException">The server refused the call.</exception>
        public async ValueTask<ArrayOf<AssetAlias>> FindAssetsAsync(
            string pattern,
            AssetAliasCategory category = AssetAliasCategory.Assets,
            CancellationToken cancellationToken = default)
        {
            ExpandedNodeId method = category switch
            {
                AssetAliasCategory.ByProductInstanceUri => MethodIds.AssetsByProductInstanceUri_FindAlias,
                AssetAliasCategory.ByAssetId => MethodIds.AssetsByAssetId_FindAlias,
                _ => MethodIds.Assets_FindAlias
            };
            CallResponse response = await Session.CallAsync(
                null,
                [
                    new CallMethodRequest
                    {
                        ObjectId = GetCategoryId(category),
                        MethodId = ToNodeId(method),
                        InputArguments = [Variant.From(pattern ?? string.Empty), Variant.From(NodeId.Null)]
                    }
                ],
                cancellationToken).ConfigureAwait(false);
            CallMethodResult result = response.Results[0];
            if (StatusCode.IsBad(result.StatusCode))
            {
                throw new ServiceResultException(result.StatusCode);
            }

            var aliases = new List<AssetAlias>();
            if (result.OutputArguments.Count > 0 &&
                result.OutputArguments[0].TryGetValue(out ArrayOf<AliasNameDataType> found, Session.MessageContext))
            {
                foreach (AliasNameDataType alias in found)
                {
                    aliases.Add(new AssetAlias(category, alias.AliasName.Name ?? string.Empty, alias.ReferencedNodes));
                }
            }
            return aliases.ToArrayOf();
        }

        /// <summary>
        /// Gets the assets of this server listed in the alias categories,
        /// each once.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The NodeIds of the assets.</returns>
        public async ValueTask<ArrayOf<NodeId>> DiscoverAssetsAsync(CancellationToken cancellationToken = default)
        {
            ArrayOf<AssetAlias> aliases = await FindAssetsAsync("%", AssetAliasCategory.Assets, cancellationToken)
                .ConfigureAwait(false);
            var assets = new List<NodeId>();
            var seen = new HashSet<NodeId>();
            foreach (AssetAlias alias in aliases)
            {
                foreach (ExpandedNodeId target in alias.ReferencedNodes)
                {
                    if (target.ServerIndex != 0)
                    {
                        continue;
                    }
                    NodeId local = ToNodeId(target);
                    if (!local.IsNull && seen.Add(local))
                    {
                        assets.Add(local);
                    }
                }
            }
            return assets.ToArrayOf();
        }

        /// <summary>
        /// Lists the aliases of a category by browsing its alias objects and
        /// their <c>AliasFor</c> references, without calling a method.
        /// </summary>
        /// <param name="category">
        /// The category; <c>Assets</c> lists both subcategories.
        /// </param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The aliases.</returns>
        public async IAsyncEnumerable<AssetAlias> EnumerateAssetsAsync(
            AssetAliasCategory category = AssetAliasCategory.ByProductInstanceUri,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            AssetAliasCategory[] categories = category == AssetAliasCategory.Assets
                ? [AssetAliasCategory.ByProductInstanceUri, AssetAliasCategory.ByAssetId]
                : [category];
            foreach (AssetAliasCategory current in categories)
            {
                List<ReferenceDescription> aliases = await BrowseAsync(
                    GetCategoryId(current),
                    Ua.ReferenceTypeIds.Organizes,
                    BrowseDirection.Forward,
                    cancellationToken,
                    NodeClass.Object).ConfigureAwait(false);
                foreach (ReferenceDescription alias in aliases)
                {
                    List<ReferenceDescription> targets = await BrowseAsync(
                        ToNodeId(alias.NodeId),
                        Ua.ReferenceTypeIds.AliasFor,
                        BrowseDirection.Forward,
                        cancellationToken).ConfigureAwait(false);
                    var referenced = new ExpandedNodeId[targets.Count];
                    for (int ii = 0; ii < referenced.Length; ii++)
                    {
                        referenced[ii] = targets[ii].NodeId;
                    }
                    yield return new AssetAlias(
                        current,
                        alias.BrowseName.Name ?? string.Empty,
                        referenced.ToArrayOf());
                }
            }
        }

        /// <summary>
        /// Reads the <c>NodeVersion</c> of an alias category, which changes
        /// whenever an asset is listed, moved or withdrawn (§8.2).
        /// </summary>
        /// <param name="category">The category.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The version, or <see langword="null"/> when the category has none.</returns>
        public async ValueTask<string?> ReadNodeVersionAsync(
            AssetAliasCategory category,
            CancellationToken cancellationToken = default)
        {
            Variant[] values = await ReadChildValuesAsync(
                GetCategoryId(category),
                [[new QualifiedName(Ua.BrowseNames.NodeVersion)]],
                cancellationToken).ConfigureAwait(false);
            return StringOf(values[0]);
        }

        /// <summary>
        /// Follows the changes of the alias categories through the
        /// <c>GeneralModelChangeEvent</c>s of the server.
        /// </summary>
        /// <param name="streaming">
        /// The streaming subscription; the default one of a
        /// <see cref="ManagedSession"/> when null.
        /// </param>
        /// <param name="options">The monitored item options.</param>
        /// <param name="cancellationToken">Ends the observation.</param>
        /// <returns>One change per affected category and event.</returns>
        public async IAsyncEnumerable<AssetSetChange> ObserveAssetSetChangesAsync(
            IStreamingSubscription? streaming = null,
            MonitoringOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var categories = new Dictionary<NodeId, AssetAliasCategory>
            {
                [GetCategoryId(AssetAliasCategory.Assets)] = AssetAliasCategory.Assets,
                [GetCategoryId(AssetAliasCategory.ByProductInstanceUri)] = AssetAliasCategory.ByProductInstanceUri,
                [GetCategoryId(AssetAliasCategory.ByAssetId)] = AssetAliasCategory.ByAssetId
            };
            EventFilter filter = AmbEventFilters.ModelChanges();
            await foreach (EventNotification notification in StreamingOf(streaming)
                .SubscribeEventsAsync(Ua.ObjectIds.Server, filter, options, cancellationToken)
                .ConfigureAwait(false))
            {
                ArrayOf<Variant> fields = notification.Fields;
                if (fields.Count < 2 ||
                    !fields[1].TryGetValue(out ArrayOf<ModelChangeStructureDataType> changes, Session.MessageContext))
                {
                    continue;
                }
                for (int ii = 0; ii < changes.Count; ii++)
                {
                    ModelChangeStructureDataType change = changes[ii];
                    if (categories.TryGetValue(change.Affected, out AssetAliasCategory category))
                    {
                        yield return new AssetSetChange(category, change.Verb);
                    }
                }
            }
        }
    }
}
